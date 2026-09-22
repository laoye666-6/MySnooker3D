// =====================================================================================
// GameManager.cs —— 全局单例：游戏状态机 + 斯诺克规则引擎 + 计分 + 球位管理
//
// 一局游戏的完整状态流转：
//   Menu(主菜单) --点击开始--> Aiming(瞄准) --出杆--> Rolling(滚动中)
//        --所有球停--> EvaluateShot(结算) --返回--> Aiming / GameOver(结算画面)
//
// 规则要点（v0.34 起为完整斯诺克规则；判定集中在 SnookerRules.cs，本文件只负责落地）：
//   - 球 on 由三个变量推导（SnookerRules.BallOn）：
//       colorsPhase=false, freeColorPending=false → 打红球（OnRed == true）
//       colorsPhase=false, freeColorPending=true  → 打任意彩球（刚进红球 / 最后一红之后那颗）
//       colorsPhase=true                          → 按黄绿咖啡蓝粉黑升序打指定彩球
//   - Rule 10.3：只要台面还有红球，换手后的接台方永远以红球为球 on（v0.33 漏了这条复位）
//   - 犯规罚分：max(4, 球 on 分值, 涉及球分值)；连续两杆打红 7 分；同杆多犯规取最高
//   - 犯规杆打进的球一律不计分；彩球回点（高分优先）、红球永不回点；白球落袋回开球区
//   - Rule 4：只剩黑球时第一次得分或犯规即终局，仅当比分打平时重置黑球继续
//   - 未实现（后续可加）：指定彩球 nomination、自由球 Free Ball、犯规与未击到 Foul and Miss、让对手重打
//
// 调试日志：所有关键节点都打 [SNOOKER] 前缀日志，可 adb logcat -s Unity 过滤查看。
// =====================================================================================
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    /// 全局单例。Bootstrapper 启动时创建并调用 Init() 绑定；
    /// BallController / CueController / AimLine 等都通过 GameManager.I 访问。
    public static GameManager I;

    /// 游戏状态机的四个状态：
    ///   Menu     主菜单（球已摆好供展示，点击开始才正式开球）
    ///   Aiming   瞄准中：接受触控转方向/调力度，等待击球按钮
    ///   Rolling  球滚动中：等待所有球停下，期间做袋口捕获检测
    ///   GameOver 一局结束（显示胜负面板，"再来一局"通过重载场景复位）
    public enum State { Menu, Aiming, Rolling, GameOver }
    public State state = State.Menu;

    // ---------------------------------------------------------------------------------
    // 引用与队伍状态（由 Bootstrapper 注入 / 运行中维护）
    // ---------------------------------------------------------------------------------
    [HideInInspector] public BallController cue;                        // 白球（出杆对象）
    [HideInInspector] public List<BallController> balls = new List<BallController>(); // 全部 22 颗球
    [HideInInspector] public CueController cueCtl;                      // 瞄准/出杆控制器
    [HideInInspector] public UIManager ui;                              // 界面层

    [HideInInspector] public int cur = 0;                               // 当前击球方下标：0=玩家1，1=玩家2
    [HideInInspector] public int[] scores = new int[2];                 // 两名玩家总分 scores[0]/scores[1]
    [HideInInspector] public int[] breakScore = new int[2];             // 单杆得分（本次连续得分，换手即清零）
    public string[] names = { "玩家 1", "玩家 2" };                      // HUD 上显示的名字

    // ---------------------------------------------------------------------------------
    // 目标球状态（三者组合决定当前"该打什么球"，见文件头说明）
    // ---------------------------------------------------------------------------------
    public bool freeColorPending;                 // "任意彩球"待打（刚进红球 / 最后一红之后那颗仍待打）
    public bool colorsPhase;                      // 是否已进入清彩阶段（台面无红球后）
    public BallKind targetColor = BallKind.Yellow; // 清彩阶段当前要打的彩球（按 ColorOrder 推进）
    public int redsLeft = 15;                     // 台面剩余红球数（每次结算后重算，HUD 显示用）

    // ---- v0.35：指定彩球 / 自由球 / 让对手重打 ----
    [HideInInspector] public bool freeBallActive;   // 本杆为自由球（犯规后被斯诺克，接台方获资格）
    [HideInInspector] public BallKind nominatedColor = BallKind.Black; // 球 on 为彩球时指定的球
    [HideInInspector] public bool nominatedSet;     // 是否已指定（准线指向彩球即为指定）
    [HideInInspector] public bool canReplay;        // 上一杆判 Miss → 接台方可要求犯规方重打
    private int missCount;                          // 本局连续 Miss 次数（仅日志，未实现三次判负）

    // ---- v0.36：球在手（开球前 / 白球落袋后可在 D 区内自由摆放，Rule 3 开球与 Rule 8 犯规） ----
    /// 白球当前"球在手"：玩家可拖动白球在开球区 D 内摆放；出杆后自动失效。
    [HideInInspector] public bool cueInHand;

    // ---------------------------------------------------------------------------------
    // 单杆内部记录（每次 Shoot 清空，结算 EvaluateShot 消费）
    // ---------------------------------------------------------------------------------
    private BallKind? firstHit;          // 白球本杆第一个碰到的球种类；null=没碰到任何球（犯规）
    private bool cushionContact;         // 白球是否碰过库边（区分"啥都没碰"的犯规文案）
    private readonly List<BallController> pottedThisShot = new List<BallController>(); // 本杆落袋的球
    private float rollTimer;             // 本杆已滚动秒数（超 18 秒强制结算，防死等）
    private float shotMaxY;              // 本杆期间所有球心最高高度（诊断用：>0.09 说明球飞起来了）
    private Vector3[] aimSnapshot;       // 进入瞄准时的全部球位快照（防暂停丢位置，见 RestoreSnapshot）
    private bool shotWasSnookered;       // v0.35：出杆瞬间是否被斯诺克（决定该杆是否判 Miss）

    // ---- 红黑连击追踪（147 满分提示用）----
    // pairStreak：本轮连续"红→黑"交替的套数；lastPotKind：本轮上一颗合法落袋的球；
    // max147Shown：本轮是否已弹过 147 提示（每轮最多弹一次）。任何偏离路线的进球、
    // 犯规或空杆都会清零重来。
    private int pairStreak;
    private BallKind? lastPotKind;
    private bool max147Shown;

    /// 当前目标是否为"红球"：不在清彩阶段、且上一杆没打进红球。
    public bool OnRed { get { return !colorsPhase && !freeColorPending; } }

    void Awake() { Init(); }

    /// 单例绑定（显式公开是为了编辑器离线测试：executeMethod 模式下 Awake 不自动执行）。
    public void Init() { I = this; }

    // ---------------------------------------------------------------------------------
    // 首次进入：摆好球展示 + 显示主菜单。正式开局逻辑在 StartGame。
    // ---------------------------------------------------------------------------------
    void Start()
    {
        PlaceAllBalls();
        ui.ShowMenu(true);                               // 主菜单覆盖层
        state = State.Menu;
        UpdateHud();
        LogBalls("INIT");                                // 打印初始球位（调试定位用）
        Debug.Log("[SNOOKER] READY state=Menu");
    }

    // ---------------------------------------------------------------------------------
    // 正式开局（主菜单"开始游戏"按钮触发）：
    // 清空比分与阶段状态 → 重新布球 → 隐藏菜单 → 玩家 1 开球。
    // ---------------------------------------------------------------------------------
    public void StartGame()
    {
        scores[0] = scores[1] = 0;                       // 比分清零
        breakScore[0] = breakScore[1] = 0;               // 单杆分清零
        pairStreak = 0; lastPotKind = null; max147Shown = false;
        cur = 0;                                         // 玩家 1 先手
        freeColorPending = false;
        colorsPhase = false;
        targetColor = BallKind.Yellow;                   // 清彩阶段从黄球开始（先占位，进清彩才用）
        freeBallActive = false;                          // v0.35
        nominatedSet = false;
        canReplay = false;
        missCount = 0;
        PlaceAllBalls();
        cueInHand = true;                                // v0.36：开球前白球"球在手"，可在 D 区内摆放
        ui.ResetSpin();                                  // v0.36：加塞复位到中杆
        pottedThisShot.Clear();
        ui.ShowMenu(false);
        ui.ShowGameOver(false);
        ShowTurnMsg(true);                               // 弹出"玩家 X 开球"
        EnterAim();
        Debug.Log("[SNOOKER] GAME START");
    }

    // ---------------------------------------------------------------------------------
    // 布球：白球放 D 区内 + 15 颗红球摆正三角 + 六颗彩球回置球点。
    //
    // 白球位置特别说明：放在 (BaulkX-0.09, z=+0.09) 而不是棕球置球点！
    //   历史教训：若与棕球完全同点重生，PhysX 对两颗完全重合的球会产生任意方向
    //   的巨大分离冲量，整局物理直接炸膛（球飞出 30 多米）。
    //   偏移 (0.09,0.09) 距棕球约 0.127m > 两球半径和 0.0525，且仍在 D 圆内，合法。
    //
    // 红球三角架：apex(顶颗) 在粉球点后 2r+8mm 处，朝黑球方向排 5 排（1+2+3+4+5=15）。
    //   stepX = 行距 = 2r·cos30° + 1.5mm 间隙；stepZ = 排内间距 = 2r + 1.5mm。
    //   留 1~2mm 间隙是防止刚生成时就相互接触导致 solver 抖动。
    // ---------------------------------------------------------------------------------
    void PlaceAllBalls()
    {
        // 先摆红球与彩球，最后再摆白球：白球的落点要避开其它球（SpotFree 检查），
        // 所以必须等其它球都到位之后再算（v0.36 起用 FreeInHandPos）。
        var reds = balls.Where(b => b.kind == BallKind.Red).ToList();
        float apex = G.PinkSpot.x + 2 * G.BallR + 0.008f;     // 顶颗红球 X：粉球后 6cm
        float stepX = G.BallR * 1.7320508f + 0.002f;          // 排距（2r·cos30° + 2mm 间隙）
        float stepZ = 2 * G.BallR + 0.002f;                   // 排内间距（2r + 2mm）
        int i = 0;
        for (int row = 0; row < 5; row++)                     // 5 排：1,2,3,4,5 颗
            for (int j = 0; j <= row; j++)
                reds[i++].Place(new Vector3(apex + row * stepX, 0, (j - row / 2f) * stepZ));

        var kinds = new[] { BallKind.Yellow, BallKind.Green, BallKind.Brown, BallKind.Blue, BallKind.Pink, BallKind.Black };
        var spots = new[] { G.YellowSpot, G.GreenSpot, G.BrownSpot, G.BlueSpot, G.PinkSpot, G.BlackSpot };
        for (int k = 0; k < 6; k++)
            balls.First(b => b.kind == kinds[k]).Place(spots[k]);

        cue.Place(FreeInHandPos());                           // 白球：D 区内的默认位置（避开棕球！）
        redsLeft = 15;
    }

    /// <summary>
    /// v0.36：D 区内的一个空位，作为"球在手"的默认摆放点。
    /// 首选 (BaulkX-0.09, +0.09) —— 距棕球约 0.127m > 两球半径和，且完全在 D 圆内（历史踩坑 3：
    /// 白球与棕球同点重生会让 PhysX 炸膛），被占则依次试其它候选点。
    /// </summary>
    Vector3 FreeInHandPos()
    {
        Vector3[] cands =
        {
            new Vector3(G.BaulkX - 0.09f, 0f,  0.09f),
            new Vector3(G.BaulkX - 0.09f, 0f, -0.09f),
            new Vector3(G.BaulkX - 0.05f, 0f,  0f),
            new Vector3(G.BaulkX - 0.15f, 0f,  0f),
            new Vector3(G.BaulkX - 0.20f, 0f,  0.12f),
            new Vector3(G.BaulkX - 0.20f, 0f, -0.12f),
            new Vector3(G.BaulkX - 0.25f, 0f,  0f),
        };
        foreach (var c in cands)
            if (SpotFree(c, cue)) return c;
        return G.ClampToD(cands[0]);                          // 极端情况：硬放（下同）
    }

    // ---------------------------------------------------------------------------------
    // 进入瞄准阶段（每次结算完都会调用）：
    // 保存球位快照 → 刷新 HUD → 打印当前目标与全部球位（外部测试脚本据此计算瞄准角）。
    // ---------------------------------------------------------------------------------
    void EnterAim()
    {
        state = State.Aiming;
        SaveSnapshot();
        UpdateHud();
        string tgt = OnRed ? "红" : colorsPhase ? G.CnName(targetColor) : "任意彩";
        LogBalls("AIM p" + (cur + 1) + " target=" + tgt);
    }

    /// 把当前所有球的世界坐标存入快照数组（与 balls 列表下标一一对应）。
    void SaveSnapshot()
    {
        if (aimSnapshot == null || aimSnapshot.Length != balls.Count) aimSnapshot = new Vector3[balls.Count];
        for (int i = 0; i < balls.Count; i++) aimSnapshot[i] = balls[i].transform.position;
    }

    /// <summary>
    /// 快照是否"损坏"：部分安卓设备（模拟器窗口失焦等场景）应用暂停/恢复后，
    /// 刚体的变换会被异常清零或漂移。出杆前若发现任何未落袋的球：
    ///   - 坐标接近原点（平方距离 < 0.0005），或
    ///   - 飘到台面外 0.6m 以外，
    /// 就判定快照损坏，需要在出杆前恢复。
    /// </summary>
    bool SnapshotBroken()
    {
        if (aimSnapshot == null) return false;
        for (int i = 0; i < balls.Count; i++)
        {
            if (balls[i].potted) continue;
            Vector3 p = balls[i].transform.position;
            if (p.sqrMagnitude < 0.0005f) return true;
            if (Mathf.Abs(p.x) > G.HalfL + 0.6f || Mathf.Abs(p.z) > G.HalfW + 0.6f) return true;
        }
        return false;
    }

    /// 把全部未落袋球恢复到进瞄准时的位置，同时清速度并唤醒刚体。
    /// （历史教训：恢复后如果忘记 WakeUp，物理体会保持冻结，出杆后球原地不动。）
    void RestoreSnapshot()
    {
        for (int i = 0; i < balls.Count; i++)
        {
            if (balls[i].potted) continue;
            Vector3 p = aimSnapshot[i];
            balls[i].Rb.position = p;
            balls[i].transform.position = p;
            balls[i].Rb.velocity = Vector3.zero;
            balls[i].Rb.angularVelocity = Vector3.zero;
        }
        Debug.Log("[SNOOKER] SNAPSHOT RESTORED");
    }

    // ---------------------------------------------------------------------------------
    // 出杆（由 CueController 的出杆动画结束时调用）。
    // 参数：
    //   dir     —— 出杆方向（单位向量，XZ 平面），由瞄准角换算而来
    //   power01 —— 力度滑条值 0~1，线性映射到 [MinShotSpeed, MaxShotSpeed] 的初速
    //   spinV   —— v0.36 加塞：+1 高杆 / -1 低杆 / 0 中杆（默认值，编辑器测试可直接省略）
    //   spinH   —— v0.36 加塞：-1 左塞 / +1 右塞 / 0 无塞
    //
    // 流程：必要时恢复快照 → 切 Rolling 状态 → 清空单杆记录 → 白球唤醒并赋初速+自旋 → 记日志。
    // WakeUp 必须调用：白球静置几秒后会休眠，直接赋速度不会让休眠刚体动起来。
    // 三次 Invoke(LogCueVel) 是调试采样（0.2/0.6/1.2 秒后打印白球速度与位置）。
    // ---------------------------------------------------------------------------------
    public void Shoot(Vector3 dir, float power01, float spinV = 0f, float spinH = 0f)
    {
        if (state != State.Aiming) return;
        if (SnapshotBroken()) RestoreSnapshot();          // 暂停导致的球位异常先修复
        // v0.35：记录"出杆瞬间是否被斯诺克"，供结算时判定 Miss（Rule 11(b)）
        shotWasSnookered = IsSnookered();
        cueInHand = false;                                // v0.36：出杆后白球不再"在手"（不能再挪）
        state = State.Rolling;
        rollTimer = 0f;
        firstHit = null;
        cushionContact = false;
        pottedThisShot.Clear();
        shotMaxY = cue.transform.position.y;              // 以白球初始高度作为"飞行高度"基准
        float sp = Mathf.Lerp(G.MinShotSpeed, G.MaxShotSpeed, Mathf.Clamp01(power01));
        cue.Rb.WakeUp();                                  // 关键：唤醒休眠刚体！
        cue.Rb.velocity = dir * sp;
        cue.ApplySpin(dir, sp, spinV, spinH);             // v0.36：加塞 → 白球初始角速度
        Invoke(nameof(LogCueVel), 0.2f);
        Invoke(nameof(LogCueVel), 0.6f);
        Invoke(nameof(LogCueVel), 1.2f);
        Debug.Log(string.Format("[SNOOKER] SHOT p{0} pow={1:F2} speed={2:F2} dir=({3:F4},{4:F4}) spin v={5:F2} h={6:F2}",
            cur + 1, power01, sp, dir.x, dir.z, spinV, spinH));
        ui.SetMsg("", 0f);
        UpdateHud();
    }

    // ---------------------------------------------------------------------------------
    // v0.36："球在手"时拖动白球（CueController 把手指位置换算成台面世界坐标后调这里）。
    //   ① 先夹进 D 区（球心不得越过开球线、不得超出 D 圆）
    //   ② 若与其它球重叠 → 沿"被推开"方向迭代分离，再夹回 D 区
    //   ③ 只有确实是合法落点才 MoveTo
    // 每帧都会被调用（拖动中），所以只做最必要的工作。
    // ---------------------------------------------------------------------------------
    public void DragCueBall(Vector3 target)
    {
        if (!cueInHand || state != State.Aiming || cue == null) return;
        Vector3 p = G.ClampToD(target);
        p = SeparateFromBalls(p);
        p = G.ClampToD(p);
        cue.MoveTo(p);
        SaveSnapshot();                       // 摆放后刷新快照，避免暂停恢复时把白球弹回拖动前的位置
    }

    /// 把点 p 从与其它球的重叠里推出来（最多 8 轮，通常 1 轮就够）。
    /// 两球必须相距 ≥ 2.05r（留 5% 余量，防止贴放时物理抖动）。
    Vector3 SeparateFromBalls(Vector3 p)
    {
        float minD = G.BallR * 2.05f;
        for (int iter = 0; iter < 8; iter++)
        {
            bool hit = false;
            foreach (var b in balls)
            {
                if (b == cue || b.potted) continue;
                Vector3 q = b.transform.position;
                float dx = p.x - q.x, dz = p.z - q.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= minD) continue;
                hit = true;
                if (d < 1e-4f) { dx = 1f; dz = 0f; d = 1f; }   // 完全重合：随便挑个方向推开
                float need = minD - d;
                p.x += dx / d * need;
                p.z += dz / d * need;
            }
            if (!hit) break;
        }
        return p;
    }

    // ---------------------------------------------------------------------------------
    // 碰撞上报入口（BallController.OnCollisionEnter 调用）。
    // self：报告方；other：碰到的碰撞体。
    // 只在 Rolling 状态记录；只关心白球：
    //   - 碰到的是另一颗球 → 若是本杆首次接触则记 firstHit（首触犯规判定依据）
    //   - 碰到的不是球（库边/袋口等） → 记 cushionContact（用于犯规文案）
    // other 通过 GetComponentInParent 找球组件——球的碰撞体就在球根节点上，直接命中。
    // ---------------------------------------------------------------------------------
    public void NotifyContact(BallController self, Collider other)
    {
        if (state != State.Rolling) return;
        var ob = other.GetComponentInParent<BallController>();
        if (self.kind == BallKind.Cue && ob != null && ob != self)
        {
            if (firstHit == null)
            {
                firstHit = ob.kind;
                Debug.Log("[SNOOKER] FIRSTHIT " + ob.kind);
            }
        }
        else if (self.kind == BallKind.Cue && ob == null)
        {
            cushionContact = true;
        }
    }

    // ---------------------------------------------------------------------------------
    // 每帧驱动（只在 Rolling 状态有效）：
    //   1. 袋口捕获检测（球进捕获圈 → 落袋）
    //   2. 停判 + 快速收杆（v0.30，缩减换手等待）：
    //        - 出杆 4 秒后，停判阈值从 StopSpeed(0.09) 放宽到 0.22 m/s——只剩爬行的球
    //          直接按停结算，免去每颗球约 2 秒的慢滚收尾
    //        - 公平保护：慢爬球若按剩余速度还能滚进某个袋口（reach = v²/2a ≥ 距捕获圈
    //          距离），则继续等它滚完，保证不漏判慢滚进球
    //   3. 全停或超时（18 秒兜底）→ 统一刹停（含残余角速度）→ 结算
    // ---------------------------------------------------------------------------------
    void Update()
    {
        // v0.35：瞄准阶段持续更新"指定彩球"（准线指向哪颗彩球就指定哪颗，Rule 3(f)(i)(b)）
        if (state == State.Aiming) UpdateNomination();

        if (state != State.Rolling) return;
        rollTimer += Time.deltaTime;
        CheckPockets();
        float snapV = rollTimer > 4f ? 0.22f : G.StopSpeed;   // 4 秒后放宽停判阈值
        bool moving = false;
        foreach (var b in balls)
        {
            if (b.potted) continue;
            float y = b.transform.position.y;
            if (y > shotMaxY) shotMaxY = y;               // 记录最高球高（SHOTDONE 日志用）
            Vector3 v = b.Rb.velocity; v.y = 0f;          // 只看水平速度
            float sp = v.magnitude;
            if (sp > snapV) { moving = true; continue; }
            if (sp > G.StopSpeed)                         // 慢爬球：还够得着袋口就继续等
            {
                float reach = sp * sp / (2f * G.RollDecel);   // 剩余可滚动距离
                for (int i = 0; i < G.Pockets.Length; i++)
                {
                    float r = i < 4 ? G.CornerCaptureR : G.CenterCaptureR;
                    Vector3 d = b.transform.position - G.Pockets[i];
                    d.y = 0;
                    if (d.magnitude - r < reach + 0.02f) { moving = true; break; }
                }
            }
        }
        if (!moving || rollTimer > 18f)                   // 全停(含快速收杆) 或 18 秒兜底超时
        {
            foreach (var b in balls)                      // 结算前统一刹停（含原地旋转残余）
            {
                if (b.potted) continue;
                b.Rb.velocity = Vector3.zero;
                b.Rb.angularVelocity = Vector3.zero;
                b.ClearSpin();                            // v0.36：自旋字段也要清，否则模型会写回刚体
            }
            EvaluateShot();
        }
    }

    // ---------------------------------------------------------------------------------
    // 落袋检测（v0.41 重写）：**不再做"球心进捕获圈"的脚本判定**。
    //
    // 真实机制：台呢在洞口处没有布料，球心越过洞缘就失去支撑、由重力自然下坠。
    // 现在物理层已经挖出了真的洞口（Bootstrapper.BuildClothBed），所以这里只做
    // "确认球确实掉下去了"的观测：
    //   ① 出界保护：|x| 或 |z| 超出台面 0.3m —— 球穿库飞出（极端穿透），
    //      按落袋处理，保证游戏能继续（否则球消失在外面卡死流程）。
    //   ② 落袋确认：球心低于 -PotDepth(-0.10m) → 已下坠 126mm，不可能再回到台面。
    //      晃袋/挂袋的球达不到这个深度（洞缘下方一点点就会被颚面或内壁弹回），
    //      所以它们不会被误判 —— 这正是"袋口有真实物理"与"进圈即消失"的区别。
    // ---------------------------------------------------------------------------------
    void CheckPockets()
    {
        foreach (var b in balls)
        {
            if (b.potted) continue;
            Vector3 pb = b.transform.position;
            if (Mathf.Abs(pb.x) > G.HalfL + 0.3f || Mathf.Abs(pb.z) > G.HalfW + 0.3f)
            { RegisterPot(b); continue; }                 // 出界球按落袋处理（兜底）
            // 落袋判定（v0.41：多层判据集中在 G.InPocket，说明见那里）
            Vector3 v = b.Rb.velocity;
            if (G.InPocket(pb, v)) { RegisterPot(b); continue; }
        }
    }

    /// 登记落袋：打印落袋位置与速度（调参用）→ 执行落袋 → 记入本杆落袋清单（结算用）。
    void RegisterPot(BallController b)
    {
        Vector3 p = b.transform.position;
        Debug.Log("[SNOOKER] POTTED " + b.kind +
                  " at(" + p.x.ToString("F3") + "," + p.y.ToString("F3") + "," + p.z.ToString("F3") + ")" +
                  " vel=" + b.Rb.velocity.magnitude.ToString("F2"));
        b.Pot();
        pottedThisShot.Add(b);
    }

    /// 调试采样：出杆后 0.2/0.6/1.2 秒打印白球速度与位置（transform 与 rb 双份，
    /// 若两者不一致说明物理同步出了问题——曾用它揪出 SimulationMode 配置错误）。
    void LogCueVel()
    {
        if (state != State.Rolling || cue == null) return;
        Debug.Log("[SNOOKER] CUEVEL v=" + cue.Rb.velocity.ToString("F2") +
                  " pos=" + cue.transform.position.ToString("F3") +
                  " rbpos=" + cue.Rb.position.ToString("F3"));
    }

    // =================================================================================
    // 单杆结算（v0.34：规则判定交给纯引擎 SnookerRules，本方法只负责"落地"）
    //   输入：firstHit / pottedThisShot / cushionContact（出杆期间累积的事实）
    //   过程：构造"出杆时刻台面状态 + 本杆事实" → SnookerRules.Evaluate → 结算结果
    //   落地：加分 → 147 连击 → 彩球回点（高分优先）→ 状态推进 → 终局/换手 → 回到 Aiming
    //
    // 为什么改成这样：旧版把规则判断散在这里，只能在模拟器上真打才能验证，结果"清彩阶段
    // 犯规落袋导致一局永远打不完"这类致命规则错误一直没被发现。现在规则是纯函数，
    // 由 Editor/RuleTest.cs 离线断言（见该文件）。规则依据见 SnookerRules.cs 文件头。
    // =================================================================================
    void EvaluateShot()
    {
        Debug.Log("[SNOOKER] SHOTDONE maxY=" + shotMaxY.ToString("F3") +
                  (shotMaxY > 0.09f ? "  <<< BALLS FLYING" : ""));   // 诊断：球飞太高说明物理异常

        // ---- ① 出杆时刻的台面状态（快照）与本杆事实 ----
        var pre = new TableState();
        pre.colorsPhase = colorsPhase;
        pre.freeColorPending = freeColorPending;
        pre.redsLeft = redsLeft;
        pre.colorsOnTable = ColorsOnTable();
        pre.freeBallActive = freeBallActive;               // v0.35

        var facts = new ShotFacts();
        facts.hitNothing = !firstHit.HasValue;
        facts.cushionContact = cushionContact;
        facts.firstHit = firstHit.HasValue ? firstHit.Value : BallKind.Red;
        facts.potted = pottedThisShot.Select(b => b.kind).ToArray();
        facts.nominated = nominatedColor;                  // v0.35：本杆指定的彩球
        facts.nominatedSet = nominatedSet;
        facts.snookered = shotWasSnookered;                // v0.35：出杆时是否被斯诺克（Miss 判定用）

        ShotOutcome oc = SnookerRules.Evaluate(pre, facts, scores[cur], scores[1 - cur]);

        // ---- ② 计分：犯规让对手、合法归自己（Rule 10 / Rule 11(e)）----
        scores[cur] += oc.scoreDeltaStriker;
        scores[1 - cur] += oc.scoreDeltaOpponent;
        if (oc.foulPts > 0)
        {
            ui.ShowMsg("犯规！" + names[1 - cur] + " +" + oc.foulPts + "（" + oc.reason + "）", 3f);
            Debug.Log("[SNOOKER] FOUL +" + oc.foulPts + " to P" + (2 - cur) + " (" + oc.reason + ")");
        }
        else
        {
            ui.ShowMsg(oc.legalPts > 0 ? names[cur] + " +" + oc.legalPts : "未进球，交换击球权", 2.2f);
            Debug.Log("[SNOOKER] SCORE p" + (cur + 1) + " +" + oc.legalPts);
        }

        // ---- ③ 单杆分与红黑连击（147 提示）----
        // 新规则下"完成一套红黑"是可判定的事实：进红记红杆，球 on 为彩球时进黑即完成一套。
        if (oc.foulPts > 0 || oc.legalPts == 0)
        {
            breakScore[cur] = 0;
            pairStreak = 0; lastPotKind = null; max147Shown = false;
        }
        else
        {
            breakScore[cur] += oc.legalPts;
            if (oc.pottedRed && !oc.pottedColor) lastPotKind = BallKind.Red;      // 打进红球 → 红杆
            else if (oc.pottedColor && oc.pottedColorKind == BallKind.Black && lastPotKind == BallKind.Red)
            { lastPotKind = BallKind.Black; pairStreak++; }                        // 红→黑，完成一套
            else { pairStreak = 0; lastPotKind = null; max147Shown = false; }       // 偏离红黑节奏
            if (pairStreak >= 5 && !max147Shown)
            {
                max147Shown = true;
                ui.Show147(pairStreak);
                Debug.Log("[SNOOKER] 147-HINT pairs=" + pairStreak);
            }
        }

        // ---- ④ 彩球回点（Rule 7(e)：多颗同时回点时高分优先）；自由球红球回点；
        //         白球落袋回开球区 ----
        foreach (var k in oc.respotColors)
        {
            var rb = BallOf(k);
            if (rb != null) RespotColor(rb);
        }
        foreach (var k in oc.respotReds)              // v0.35：自由球情形被打进的红球也要回点（Rule 12）
        {
            var rb = BallOf(k);
            if (rb != null) RespotRed(rb);
        }
        if (cue.potted) RespotCue();

        // ---- ⑤ 状态推进：剩余红球 / 阶段 / 清彩目标 ----
        int recount = balls.Count(b => !b.potted && b.kind == BallKind.Red);
        if (recount != pre.redsLeft - oc.redsPotted)
            Debug.LogWarning("[SNOOKER] redsLeft mismatch recount=" + recount +
                             " rule=" + (pre.redsLeft - oc.redsPotted));
        redsLeft = recount;
        colorsPhase = oc.nextColorsPhase;
        freeColorPending = oc.nextFreeColorPending;
        targetColor = oc.nextTargetColor;

        // ---- ⑤b 自由球资格（Rule 12）：犯规 + 接台方被斯诺克 → 下一位击球方可打自由球 ----
        // 资格归属于"即将接手的那一方"，因此在换手后才生效（见下方换手分支）。
        bool earnedFreeBall = oc.foulPts > 0 && IsSnookered();

        // ---- ⑤c 犯规与未击到（Rule 11(b)）：判 Miss 时接台方可要求犯规方从当前球位重打 ----
        if (oc.isMiss)
        {
            missCount++;
            Debug.Log("[SNOOKER] MISS called (连续 " + missCount + " 次)");
        }
        else if (oc.foulPts > 0 || oc.legalPts > 0) missCount = 0;

        // ---- ⑥ 只剩黑球：第一次得分或犯规即终局；仅当打平时重置黑球继续（Rule 4）----
        if (oc.frameOver) { UpdateHud(); GameOver(); return; }
        if (oc.respotBlackTie)
        {
            var black = BallOf(BallKind.Black);
            if (black != null) RespotColor(black);
            RespotCue();
            ui.ShowMsg("平分！重置黑球决胜", 3f);
            Debug.Log("[SNOOKER] RESPOTTED BLACK (tie)");
        }

        // ---- ⑦ 换手 ----
        freeBallActive = false;                          // v0.35：自由球资格只在下一杆有效
        canReplay = false;
        if (oc.handover)
        {
            cur = 1 - cur;
            breakScore[cur] = 0;                    // 新一轮击球权，单杆分从零起算
            pairStreak = 0; lastPotKind = null; max147Shown = false;
            ui.ResetSpin();                         // v0.36：换手 → 加塞复位（新一杆从中杆开始）
            // 新接台方若满足"犯规 + 被斯诺克"，获得自由球资格（Rule 12）
            if (earnedFreeBall)
            {
                freeBallActive = true;
                ui.ShowMsg(names[cur] + " 获得自由球", 3f);
                Debug.Log("[SNOOKER] FREE BALL granted to P" + (cur + 1));
            }
            // 判 Miss → 接台方可要求犯规方重打（Rule 11(b)）；本作提供按钮由玩家决定
            if (oc.isMiss)
            {
                canReplay = true;
                ui.ShowReplayOption();
                Debug.Log("[SNOOKER] REPLAY option offered");
            }
        }
        else
        {
            missCount = 0;                          // 合法得分则 Miss 计数清零
        }

        UpdateHud();
        EnterAim();
    }

    /// <summary>
    /// v0.35：让对手重打（Rule 11(b) 的 (b) 选项）。由 UI 的"让对手重打"按钮触发。
    /// 规则语义：接台方放弃自己的击球权，要求犯规方从【当前球位】再打一次；
    /// 因此只需要把击球权换回给犯规方，球位保持不动（不重新摆球）。
    /// 注意：单杆分与 147 连击在犯规时已经清零，这里不再处理。
    /// </summary>
    public void RequestReplay()
    {
        if (!canReplay || state != State.Aiming) return;
        canReplay = false;
        cur = 1 - cur;                                  // 把击球权交回犯规方
        Debug.Log("[SNOOKER] REPLAY requested → P" + (cur + 1) + " plays again from current position");
        ui.ShowMsg("要求 " + names[cur] + " 重打", 2.5f);
        UpdateHud();
        EnterAim();
    }

    /// <summary>
    /// v0.35：玩家放弃自由球资格（Rule 12 允许选择"不打自由球"）。
    /// 放弃后按真实球 on 继续，只是不再享有"任意球当球 on"的便利。
    /// </summary>
    public void DeclineFreeBall()
    {
        if (!freeBallActive || state != State.Aiming) return;
        freeBallActive = false;
        Debug.Log("[SNOOKER] FREE BALL declined");
        UpdateHud();
    }

    /// 台面上仍在的彩球（黄..黑）：供规则引擎推导"清彩目标"与"是否只剩黑球"。
    BallKind[] ColorsOnTable()
    {
        var list = new List<BallKind>();
        foreach (var k in G.ColorOrder)
            if (balls.Any(b => b.kind == k && !b.potted)) list.Add(k);
        return list.ToArray();
    }

    // =================================================================================
    // v0.35：斯诺克（snooker）判定 —— 自由球（Rule 12）与犯规与未击到（Rule 11(b)）共用
    //
    // 官方定义：若白球到某颗"球 on"的【左右两侧边缘】都不能被直线击中（被非球 on 挡住），
    // 即对该球被斯诺克。判定实现：
    //   ① 取当前所有合法球 on（红球阶段=所有红球；清彩阶段=目标那一颗；任意彩球=任意彩球）
    //   ② 对每颗球 on，检查"打向该球中心两侧各半个球宽"的两条切线路径是否被其它球挡住
    //      （沿路径做球-球相交测试；库边不算遮挡，因为可以翻袋——按官方规则只用直线判定）
    //   ③ 只要有一颗球 on 存在至少一条通畅路径 → 未被斯诺克
    // =================================================================================
    /// 当前合法的"球 on"集合（用于斯诺克判定）。
    List<BallController> BallsOn()
    {
        var list = new List<BallController>();
        if (colorsPhase)
        {
            var t = BallOf(targetColor);
            if (t != null && !t.potted) list.Add(t);
        }
        else if (freeColorPending)
        {
            foreach (var k in G.ColorOrder)
            {
                var b = BallOf(k);
                if (b != null && !b.potted) list.Add(b);
            }
        }
        else
        {
            foreach (var b in balls)
                if (b.kind == BallKind.Red && !b.potted) list.Add(b);
        }
        return list;
    }

    /// <summary>
    /// 白球到目标球是否存在"直接击打线路"。做法：从白球中心向目标球两侧各偏移
    /// 一个球半径（即擦边球的极限位置）分别做路径检测，任一通畅即算有线路。
    /// 参数 ignorePotting: 判定时忽略的球（自由球判定用自己的逻辑，这里传 null 即可）。
    /// </summary>
    bool HasClearPath(Vector3 from, BallController target)
    {
        Vector3 tp = target.transform.position;
        Vector3 d = tp - from;
        d.y = 0;
        float dist = d.magnitude;
        if (dist < 1e-4f) return true;
        Vector3 dir = d / dist;
        Vector3 perp = new Vector3(-dir.z, 0f, dir.x);          // 水平面内的垂直方向

        // 两侧擦边：沿垂线偏移约一个球直径（2r）处再瞄准目标球中心，
        // 这样能覆盖"薄擦即可击中"的最宽合法路径
        float[] offsets = { -2f * G.BallR, 2f * G.BallR, 0f };
        foreach (float off in offsets)
        {
            Vector3 start = from + perp * off;
            Vector3 dd = tp - start;
            dd.y = 0;
            float ddLen = dd.magnitude;
            if (ddLen < 1e-4f) return true;
            Vector3 ddir = dd / ddLen;
            if (IsPathClear(start, ddir, ddLen, target)) return true;
        }
        return false;
    }

    /// 从 start 沿 dir 走 len 距离，路径上是否没有其它球阻挡（球-球最小间距 ≥ 2r）。
    bool IsPathClear(Vector3 start, Vector3 dir, float len, BallController target)
    {
        foreach (var b in balls)
        {
            if (b == cue || b == target || b.potted) continue;
            Vector3 toB = b.transform.position - start;
            toB.y = 0;
            float along = Vector3.Dot(toB, dir);
            if (along <= 0f || along >= len) continue;           // 不在路径区间内
            float perp2 = toB.sqrMagnitude - along * along;
            float rr = 4f * G.BallR * G.BallR;                   // 两个球心距 < 2r 即相撞
            if (perp2 < rr) return false;                        // 被这颗球挡住
        }
        return true;
    }

    /// 接台方是否对所有球 on 都被斯诺克（决定能否打自由球 / 是否判 Miss）。
    public bool IsSnookered()
    {
        var ons = BallsOn();
        if (ons.Count == 0) return false;                        // 台面无球 on（极端）
        foreach (var t in ons)
        {
            Vector3 tp = t.transform.position;
            // 白球本身与该球重叠/极近时不判斯诺克（否则贴球必判犯规）
            Vector3 d = tp - cue.transform.position; d.y = 0;
            if (d.magnitude <= 2.02f * G.BallR) return false;
            if (HasClearPath(cue.transform.position, t)) return false;   // 有通畅线路 → 未被斯诺克
        }
        return true;
    }

    /// 出杆前的球 on 描述（HUD 文案用）。
    public string BallOnText()
    {
        if (freeBallActive) return "自由球";
        if (OnRed) return "红球";
        if (colorsPhase) return G.CnName(targetColor);
        return nominatedSet ? G.CnName(nominatedColor) : "任意彩球";
    }

    /// 按球种取球对象（回点用）。
    BallController BallOf(BallKind k)
    {
        return balls.FirstOrDefault(b => b.kind == k);
    }

    // ---------------------------------------------------------------------------------
    // 白球回位（v0.36 改）：白球落袋后按规则"球在手"，须从开球区 D 内击打。
    // 这里只把它放到 D 区内的一个合法默认位置，随后交给玩家在 D 区内自由拖动摆放
    // （CueController 拖球 → DragCueBall）。
    // 注意棕球点常被棕球占着（红球阶段棕球一直在台面），所以默认点取 D 区内偏移处。
    // ---------------------------------------------------------------------------------
    void RespotCue()
    {
        cue.Place(FreeInHandPos());
        cueInHand = true;                                      // v0.36：进入"球在手"，玩家可摆放
        Debug.Log("[SNOOKER] CUE IN HAND (D)");
    }

    /// 判断放球点 p 周围是否清空：与任何未落袋球（除 ignore 自己）间距 ≥ 2.05 倍球半径。
    /// 系数 2.05 留 5% 余量，防止贴着放导致物理初始接触抖动。
    bool SpotFree(Vector3 p, BallController ignore)
    {
        foreach (var b in balls)
        {
            if (b == ignore || b.potted) continue;
            Vector3 d = b.transform.position - p;
            d.y = 0;
            if (d.magnitude < G.BallR * 2.05f) return false;
        }
        return true;
    }

    /// <summary>
    /// 彩球回置球点（斯诺克规则）：
    ///   1. 先试自己的置球点；
    ///   2. 被占则按 黑→粉→蓝→棕→绿→黄 的分值从高到低试其他置球点；
    ///   3. 全被占则从自己的点出发向顶库（+X）方向逐 12mm 找空位；
    ///   4. 兜底直接硬放原点（理论到不了这一步）。
    /// </summary>
    void RespotColor(BallController b)
    {
        Vector3 own = OwnSpot(b.kind);
        if (SpotFree(own, b)) { b.Place(own); return; }
        var order = new[] { BallKind.Black, BallKind.Pink, BallKind.Blue, BallKind.Brown, BallKind.Green, BallKind.Yellow };
        foreach (var k in order)
        {
            Vector3 s = OwnSpot(k);
            if (SpotFree(s, b)) { b.Place(s); Debug.Log("[SNOOKER] RESPOT " + b.kind + " -> " + k + " spot"); return; }
        }
        for (float dx = 0.012f; dx < 1.4f; dx += 0.012f)
        {
            Vector3 q = own + Vector3.right * dx;
            if (q.x > G.HalfL - G.BallR - 0.01f) break;        // 到顶库为止
            if (SpotFree(q, b)) { b.Place(q); Debug.Log("[SNOOKER] RESPOT " + b.kind + " nudged +" + dx.ToString("F2")); return; }
        }
        b.Place(own);
    }

    /// 彩球 → 自己的置球点（与 G 里 ColorSpots 一致，写成函数便于 RespotColor 复用）。
    Vector3 OwnSpot(BallKind k)
    {
        switch (k)
        {
            case BallKind.Yellow: return G.YellowSpot;
            case BallKind.Green: return G.GreenSpot;
            case BallKind.Brown: return G.BrownSpot;
            case BallKind.Blue: return G.BlueSpot;
            case BallKind.Pink: return G.PinkSpot;
            case BallKind.Black: return G.BlackSpot;
        }
        return Vector3.zero;
    }

    /// <summary>
    /// v0.35：红球回点（仅自由球规则下需要，Rule 12）。
    /// 红球没有专属置球点，按官方做法放到"粉球点与顶库之间、尽量靠近粉球点"的空位；
    /// 若被占则沿 +X 方向逐 12mm 找最近空位。
    /// </summary>
    void RespotRed(BallController b)
    {
        Vector3 baseP = G.PinkSpot;
        if (SpotFree(baseP, b)) { b.Place(baseP); Debug.Log("[SNOOKER] RESPOT Red -> pink spot area"); return; }
        for (float dx = 0.012f; dx < 0.90f; dx += 0.012f)
        {
            Vector3 q = baseP + Vector3.right * dx;
            if (q.x > G.BlackSpot.x - 2f * G.BallR) break;   // 不超过黑球点
            if (SpotFree(q, b)) { b.Place(q); Debug.Log("[SNOOKER] RESPOT Red nudged +" + dx.ToString("F2")); return; }
        }
        b.Place(baseP);                                      // 兜底硬放
    }

    /// <summary>
    /// v0.35：每帧更新"指定彩球"——球 on 为彩球时，把准线指向的球作为指定对象
    /// （Rule 3(f)(i)(b)：击球方必须指定打哪一颗彩球）。
    /// 用准线指向自动表达意图，玩家无需额外操作；指定结果参与首触犯规判定。
    /// 只有"当前合法的球 on 候选"才能被指定（清彩阶段只能指定目标那一颗）。
    /// </summary>
    void UpdateNomination()
    {
        if (!(freeColorPending || colorsPhase)) { nominatedSet = false; return; }
        var aimed = cueCtl != null ? cueCtl.AimedBall : null;
        if (aimed == null || aimed.potted) { nominatedSet = false; return; }
        if (colorsPhase)
        {
            if (aimed.kind != targetColor) { nominatedSet = false; return; }  // 清彩阶段只能指定目标球
        }
        else if (aimed.kind == BallKind.Red) { nominatedSet = false; return; } // 任意彩球阶段不能指定红球
        nominatedColor = aimed.kind;
        nominatedSet = true;
    }

    /// 一局结束：分高者胜（平分已在结算里用黑球决胜消化，到这里必有胜负）。
    /// 显示结算面板并打印最终比分。重开一局由面板上的"再来一局"重载场景实现。
    void GameOver()
    {
        state = State.GameOver;
        int w = scores[0] >= scores[1] ? 0 : 1;
        ui.ShowGameOver(true, w, scores[0], scores[1]);
        Debug.Log("[SNOOKER] GAMEOVER P" + (w + 1) + " wins " + scores[0] + ":" + scores[1]);
        LogBalls("GAMEOVER");
    }

    /// 回合开始提示文案（开球/击球）。
    void ShowTurnMsg(bool isBreak)
    {
        ui.ShowMsg(names[cur] + (isBreak ? " 开球" : " 击球"), 2.5f);
    }

    /// 刷新顶部记分板（单杆分、当前玩家、目标球、剩余红球、总分）。
    void UpdateHud()
    {
        string tgt = OnRed ? "红球" : colorsPhase ? G.CnName(targetColor) : "任意彩球";
        ui.SetHud(scores[0], scores[1], breakScore[0], breakScore[1], cur, tgt, redsLeft);
    }

    /// 打印全部未落袋球的坐标（外部测试脚本 / 调参时的核心数据源）。
    public void LogBalls(string tag)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in balls)
        {
            if (b.potted) continue;
            sb.AppendFormat("{0}({1:F3},{2:F3}) ", b.kind, b.transform.position.x, b.transform.position.z);
        }
        Debug.Log("[SNOOKER] " + tag + " | " + sb);
    }
}
