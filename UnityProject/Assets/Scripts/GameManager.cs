// =====================================================================================
// GameManager.cs —— 全局单例：游戏状态机 + 斯诺克规则引擎 + 计分 + 球位管理
//
// 一局游戏的完整状态流转：
//   Menu(主菜单) --点击开始--> Aiming(瞄准) --出杆--> Rolling(滚动中)
//        --所有球停--> EvaluateShot(结算) --返回--> Aiming / GameOver(结算画面)
//
// 规则要点（简化斯诺克）：
//   - 目标球状态由三个变量共同描述：colorsPhase / onColor / targetColor
//       colorsPhase=false, onColor=false → 目标"红球"（OnRed == true）
//       colorsPhase=false, onColor=true  → 目标"任意彩球"（刚进了一颗红球）
//       colorsPhase=true                 → 目标"指定彩球"（按黄绿咖啡蓝粉黑顺序清彩）
//   - 犯规判罚：让对手得 max(4, 涉及球的最高分值)，常见情形都在 EvaluateShot 里
//   - 犯规/未进球 → 换人；合法进球 → 同一人继续打
//   - 白球落袋 → 重置回开球区；彩球误落/红球阶段进彩 → 重置回置球点
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
    public bool onColor;                          // 红球阶段：刚打进红球，下一杆打任意彩球
    public bool colorsPhase;                      // 是否已进入清彩阶段（台面无红球后）
    public BallKind targetColor = BallKind.Yellow; // 清彩阶段当前要打的彩球（按 ColorOrder 推进）
    public int redsLeft = 15;                     // 台面剩余红球数（每次结算后重算，HUD 显示用）

    // ---------------------------------------------------------------------------------
    // 单杆内部记录（每次 Shoot 清空，结算 EvaluateShot 消费）
    // ---------------------------------------------------------------------------------
    private BallKind? firstHit;          // 白球本杆第一个碰到的球种类；null=没碰到任何球（犯规）
    private bool cushionContact;         // 白球是否碰过库边（区分"啥都没碰"的犯规文案）
    private readonly List<BallController> pottedThisShot = new List<BallController>(); // 本杆落袋的球
    private float rollTimer;             // 本杆已滚动秒数（超 18 秒强制结算，防死等）
    private float shotMaxY;              // 本杆期间所有球心最高高度（诊断用：>0.09 说明球飞起来了）
    private Vector3[] aimSnapshot;       // 进入瞄准时的全部球位快照（防暂停丢位置，见 RestoreSnapshot）

    // ---- 红黑连击追踪（147 满分提示用）----
    // pairStreak：本轮连续"红→黑"交替的套数；lastPotKind：本轮上一颗合法落袋的球；
    // max147Shown：本轮是否已弹过 147 提示（每轮最多弹一次）。任何偏离路线的进球、
    // 犯规或空杆都会清零重来。
    private int pairStreak;
    private BallKind? lastPotKind;
    private bool max147Shown;

    /// 当前目标是否为"红球"：不在清彩阶段、且上一杆没打进红球。
    public bool OnRed { get { return !colorsPhase && !onColor; } }

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
        onColor = false;
        colorsPhase = false;
        targetColor = BallKind.Yellow;                   // 清彩阶段从黄球开始（先占位，进清彩才用）
        PlaceAllBalls();
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
        cue.Place(new Vector3(G.BaulkX - 0.09f, 0, 0.09f));   // 白球：D 区内偏右下（避开棕球！）

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
        redsLeft = 15;
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
    //
    // 流程：必要时恢复快照 → 切 Rolling 状态 → 清空单杆记录 → 白球唤醒并赋初速 → 记日志。
    // WakeUp 必须调用：白球静置几秒后会休眠，直接赋速度不会让休眠刚体动起来。
    // 三次 Invoke(LogCueVel) 是调试采样（0.2/0.6/1.2 秒后打印白球速度与位置）。
    // ---------------------------------------------------------------------------------
    public void Shoot(Vector3 dir, float power01)
    {
        if (state != State.Aiming) return;
        if (SnapshotBroken()) RestoreSnapshot();          // 暂停导致的球位异常先修复
        state = State.Rolling;
        rollTimer = 0f;
        firstHit = null;
        cushionContact = false;
        pottedThisShot.Clear();
        shotMaxY = cue.transform.position.y;              // 以白球初始高度作为"飞行高度"基准
        float sp = Mathf.Lerp(G.MinShotSpeed, G.MaxShotSpeed, Mathf.Clamp01(power01));
        cue.Rb.WakeUp();                                  // 关键：唤醒休眠刚体！
        cue.Rb.velocity = dir * sp;
        Invoke(nameof(LogCueVel), 0.2f);
        Invoke(nameof(LogCueVel), 0.6f);
        Invoke(nameof(LogCueVel), 1.2f);
        Debug.Log(string.Format("[SNOOKER] SHOT p{0} pow={1:F2} speed={2:F2} dir=({3:F4},{4:F4})",
            cur + 1, power01, sp, dir.x, dir.z));
        ui.SetMsg("", 0f);
        UpdateHud();
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
            }
            EvaluateShot();
        }
    }

    // ---------------------------------------------------------------------------------
    // 袋口捕获检测：每帧对每颗未落袋的球做三重判定。
    //   ① 出界保护：|x| 或 |z| 超出台面 0.3m —— 球穿库飞出（极端穿透），
    //      直接按落袋处理，保证游戏能继续（否则球消失在外面卡死流程）。
    //   ② 高度过滤：y > 0.12m 的球还在飞/被垫起，不参与袋口判定（防止空中穿过袋口误判）。
    //   ③ 下坠判定：y < -0.15m 已在袋口下方坠落 → 落袋。
    //   ④ 平面距离：球心（只比 XZ）与袋口捕获圆心距离 < 捕获半径 → 落袋。
    //      角袋用 CornerCaptureR(0.070)，中袋用 CenterCaptureR(0.066)，见 G.Pockets 顺序。
    // ---------------------------------------------------------------------------------
    void CheckPockets()
    {
        foreach (var b in balls)
        {
            if (b.potted) continue;
            Vector3 pb = b.transform.position;
            if (Mathf.Abs(pb.x) > G.HalfL + 0.3f || Mathf.Abs(pb.z) > G.HalfW + 0.3f)
            { RegisterPot(b); continue; }                 // 出界球按落袋处理（兜底）
            if (pb.y > 0.12f) continue;                   // 飞得太高，不判袋
            if (pb.y < -0.15f) { RegisterPot(b); continue; }  // 已在袋中下坠
            for (int i = 0; i < G.Pockets.Length; i++)
            {
                float r = i < 4 ? G.CornerCaptureR : G.CenterCaptureR;
                Vector3 d = pb - G.Pockets[i];
                d.y = 0;                                  // 只比水平距离
                if (d.sqrMagnitude < r * r) { RegisterPot(b); break; }
            }
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
    // 单杆结算（本游戏规则引擎的核心）：
    //   输入：firstHit（首触球）、pottedThisShot（本杆落袋清单）、cushionContact
    //   输出：加减分 → 重置落袋球 → 换人/连续 → 推进目标球 → 回到 Aiming
    //
    // 判罚顺序：
    //   ① 首触犯规（没碰到球 / 先碰错球）——注意 all 判定基于"出杆时"的目标快照
    //   ② 逐个检查落袋球：目标球合法得分；非目标球犯规（分值取 max(4, 球分值)）
    //   ③ 白球落袋固定至少 +4
    //   ④ 犯规只让分不加自己分；合法进球才计入自己得分
    // =================================================================================
    void EvaluateShot()
    {
        // 快照"出杆时刻"的目标状态：结算过程中 redsLeft/onColor 会被推进，不能混用
        bool wasOnRed = OnRed;
        bool wasColors = colorsPhase;
        BallKind wasTarget = targetColor;

        Debug.Log("[SNOOKER] SHOTDONE maxY=" + shotMaxY.ToString("F3") +
                  (shotMaxY > 0.09f ? "  <<< BALLS FLYING" : ""));   // 诊断：球飞太高说明物理异常

        int foulPts = 0;        // 本杆犯规让分（0 = 无犯规）
        string reason = null;   // 犯规原因（HUD 文案用，只记第一条）

        // ---- ① 首触判定 ----
        if (firstHit == null) { foulPts = 4; reason = cushionContact ? "先碰库边" : "未击中球"; }
        else if (wasOnRed && firstHit.Value != BallKind.Red) { foulPts = Mathf.Max(4, G.Value(firstHit.Value)); reason = "未先击中红球"; }
        else if (!wasColors && !wasOnRed && firstHit.Value == BallKind.Red) { foulPts = 4; reason = "不应击打红球"; }
        else if (wasColors && firstHit.Value != wasTarget) { foulPts = Mathf.Max(4, G.Value(firstHit.Value)); reason = "应先击中" + G.CnName(wasTarget); }

        // ---- ② 落袋球逐一判定 ----
        int legalPts = 0;                                // 本杆合法得分（犯规时全部作废）
        bool pottedRed = false, pottedColor = false, blackDownLegally = false;
        var respots = new List<BallController>();        // 需要重置回台面的球（白球除外）

        foreach (var b in pottedThisShot)
        {
            if (b.kind == BallKind.Cue)
            {
                foulPts = Mathf.Max(foulPts, 4);         // 白球落袋至少罚 4
                if (reason == null) reason = "白球落袋";
                continue;
            }
            if (wasOnRed)                                // 目标是红球阶段
            {
                if (b.kind == BallKind.Red) { legalPts += 1; pottedRed = true; }   // 红球 +1（可多颗）
                else { foulPts = Mathf.Max(foulPts, G.Value(b.kind)); if (reason == null) reason = "误落" + G.CnName(b.kind); respots.Add(b); }
            }
            else if (!wasColors)                         // 目标是"任意彩球"
            {
                if (b.kind == BallKind.Red) { foulPts = Mathf.Max(foulPts, 4); if (reason == null) reason = "不应击落红球"; }
                else { legalPts += G.Value(b.kind); pottedColor = true; respots.Add(b); }  // 彩球得分并回点
            }
            else                                         // 清彩阶段：只能进 wasTarget 这一颗
            {
                if (b.kind == wasTarget)
                {
                    legalPts += G.Value(b.kind);
                    pottedColor = true;
                    if (b.kind == BallKind.Black) blackDownLegally = true;   // 黑球合法落袋 = 终局
                }
                else
                {
                    foulPts = Mathf.Max(foulPts, G.Value(b.kind));
                    if (reason == null) reason = "误落" + G.CnName(b.kind);
                    respots.Add(b);                      // 清彩阶段误落的彩球也要回点
                }
            }
        }

        redsLeft = balls.Count(b => !b.potted && b.kind == BallKind.Red);   // 重算台面红球数
        bool foul = foulPts > 0;

        // ---- ③ 计分：犯规让对手，合法归自己 ----
        if (foul)
        {
            scores[1 - cur] += foulPts;
            ui.ShowMsg("犯规！" + names[1 - cur] + " +" + foulPts + "（" + reason + "）", 3f);
            Debug.Log("[SNOOKER] FOUL +" + foulPts + " to P" + (2 - cur) + " (" + reason + ")");
        }
        else
        {
            scores[cur] += legalPts;
            ui.ShowMsg(legalPts > 0 ? names[cur] + " +" + legalPts : "未进球，交换击球权", 2.2f);
            Debug.Log("[SNOOKER] SCORE p" + (cur + 1) + " +" + legalPts);
        }

        // ---- 单杆分与红黑连击（147 满分提示）追踪 ----
        // 犯规或空杆：单杆结束、连击清零；合法得分：单杆累加，并按"红→黑"交替节奏
        // 统计连击套数，凑满 5 套且本轮未提示过则弹出 147 满分提示横幅。
        if (foul || legalPts == 0)
        {
            breakScore[cur] = 0;
            pairStreak = 0; lastPotKind = null; max147Shown = false;
        }
        else
        {
            breakScore[cur] += legalPts;
            foreach (var b in pottedThisShot)
            {
                if (b.kind == BallKind.Cue) continue;
                if (b.kind == BallKind.Red && lastPotKind != BallKind.Red)
                    lastPotKind = BallKind.Red;                              // 红杆（延续红黑节奏）
                else if (b.kind == BallKind.Black && lastPotKind == BallKind.Red)
                { lastPotKind = BallKind.Black; pairStreak++; }              // 完成一套红黑
                else
                { pairStreak = 0; max147Shown = false; }                     // 偏离路线（其它彩球/双红等）
            }
            if (pairStreak >= 5 && !max147Shown)
            {
                max147Shown = true;
                ui.Show147(pairStreak);
                Debug.Log("[SNOOKER] 147-HINT pairs=" + pairStreak);
            }
        }

        // ---- ④ 重置球：误落彩球回置球点；白球落袋回开球区 ----
        foreach (var b in respots) RespotColor(b);
        if (cue.potted) RespotCue();

        // ---- ⑤ 黑球终局/平分决胜 ----
        if (wasColors && blackDownLegally)
        {
            if (scores[0] != scores[1]) { UpdateHud(); GameOver(); return; }   // 分出胜负 → 结束
            RespotColor(balls.First(b => b.kind == BallKind.Black));           // 平分 → 黑球回点继续
            RespotCue();
            ui.ShowMsg("平分！重置黑球决胜", 3f);
        }

        // ---- ⑥ 推进目标球与击球权 ----
        if (!foul && (pottedRed || pottedColor))
        {
            if (!colorsPhase)
            {
                if (pottedRed) onColor = true;                 // 进了红球 → 下杆打任意彩
                else
                {
                    onColor = false;                           // 进了彩球 → 回到打红球
                    if (redsLeft == 0) { colorsPhase = true; targetColor = BallKind.Yellow; } // 红球清完 → 清彩阶段
                }
            }
            else if (!blackDownLegally)
            {
                // 清彩阶段合法进球 → 目标推进到下一颗（黑球之后无下一颗，由终局分支处理）
                int idx = System.Array.IndexOf(G.ColorOrder, targetColor);
                targetColor = G.ColorOrder[Mathf.Min(idx + 1, G.ColorOrder.Length - 1)];
            }
            // 合法进球：同一玩家继续击球（cur 不变）
        }
        else
        {
            cur = 1 - cur;                                     // 犯规/未进球 → 换人
            pairStreak = 0; lastPotKind = null; max147Shown = false;   // 新一轮击球权，连击从零开始
            // 换人瞬间若红球恰好清完（无论是否本杆打进）也切清彩阶段
            if (!colorsPhase && redsLeft == 0) { colorsPhase = true; targetColor = BallKind.Yellow; onColor = false; }
        }

        UpdateHud();
        EnterAim();
    }

    // ---------------------------------------------------------------------------------
    // 白球重置：优先放棕球置球点（开球线中点），被占用则沿 D 区上下偏移找空位。
    // 注意棕球点本身常被棕球占着（红球阶段棕球一直在台面），所以几乎总会走偏移分支。
    // ---------------------------------------------------------------------------------
    void RespotCue()
    {
        Vector3 p = G.BrownSpot;
        if (SpotFree(p, cue)) { cue.Place(p); return; }
        float[] zs = { 0.06f, -0.06f, 0.12f, -0.12f, 0.18f, -0.18f, 0.24f, -0.24f };
        foreach (float dz in zs)
        {
            Vector3 q = new Vector3(G.BaulkX, 0, dz);
            if (SpotFree(q, cue)) { cue.Place(q); return; }
        }
        cue.Place(p);                                          // 全被占就硬放（极端情况）
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
