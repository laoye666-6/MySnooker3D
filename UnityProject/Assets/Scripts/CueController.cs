// =====================================================================================
// CueController.cs —— 瞄准 / 力度 / 出杆控制器
//
// 挂在 "CueController" 空物体上（同一物体还挂 AimLine 组件）。
// 职责：
//   1. 维护瞄准角 aimAngle 与力度 power 两个核心状态
//   2. 读取触屏/鼠标拖动 → 旋转瞄准方向（避开 UI 区域）
//   3. 每帧摆放球杆模型（贴着白球、随瞄准方向、随力度后拉）
//   4. "击球"按钮触发：球杆前推的小动画 → 调 GameManager.Shoot 真正出杆
//
// 角度约定：aimAngle 为弧度，0 表示指向 +X（黑球端），逆时针为正（Unity 左手系，
// +Y 向下看时逆时针）。Dir 属性由角度换算成单位方向向量。
// =====================================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CueController : MonoBehaviour
{
    /// 瞄准角（弧度，0~2π）。0 = +X 方向（朝黑球端）。
    /// 旋转灵敏度见 Sensitivity；外部（UI 微调按钮/测试脚本）也可通过 Rotate() 精确加角。
    public float aimAngle = 0f;

    /// 力度滑条值（0~1），出杆初速 = Lerp(MinShotSpeed, MaxShotSpeed, power)。
    /// UIManager 的 Slider 和这里双向同步。
    public float power = 0.5f;

    /// 辅助瞄准线开关（"辅助线：开/关"按钮控制，需求要求可选择性开启）。
    public bool aimLineOn = true;

    /// v0.36：加塞——击球点在白球上的偏移量（-1~1，由加塞圆盘控件给出）。
    ///   spinV：+1 = 高杆（跟杆/上旋）  -1 = 低杆（缩杆/下旋）
    ///   spinH：-1 = 左塞              +1 = 右塞
    /// 出杆时交给 GameManager.Shoot → BallController.ApplySpin 换算成白球初始角速度。
    /// 数值为 0 时（默认）出杆效果与 v0.35 完全一致。
    public float spinV, spinH;

    private GameObject stick;      // 球杆模型（Blender 导出，杆头在局部 +X）
    private AimLine aimLine;       // 同物体上的瞄准线组件
    private bool striking;         // 正在播出杆动画（期间锁输入、不隐藏球杆）
    private Camera cam;            // 主相机缓存（"球在手"拖球要把屏幕坐标换算成台面坐标）

    /// <summary>
    /// v0.35：准线当前指向的球（供 GameManager 做"指定彩球"，Rule 3(f)(i)(b)）。
    /// 即使玩家关掉辅助线（aimLineOn=false）也会计算——指定彩球是规则要求，
    /// 不该因为不显示辅助线就失去指定能力。
    /// </summary>
    public BallController AimedBall
    {
        get { return aimLine != null ? aimLine.AimedBall : null; }
    }

    /// 当前瞄准方向的单位向量（只取水平面，y 恒 0）。
    public Vector3 Dir
    {
        get { return new Vector3(Mathf.Cos(aimAngle), 0f, Mathf.Sin(aimAngle)); }
    }

    void Start()
    {
        aimLine = GetComponent<AimLine>();
    }

    /// 由 Bootstrapper 注入球杆模型（若 Blender 资源缺失则允许无杆打球）。
    public void SetStick(GameObject s) { stick = s; }

    /// 瞄准灵敏度：每像素拖动对应的角度增量（弧度/像素）。
    /// 0.0018 → 全屏宽拖动约旋转 165°；调大转得快但难微调。
    /// 注意：测试脚本通过 adb swipe 计算像素位移时也用这个系数换算。
    public float Sensitivity = 0.0018f;

    /// 微调按钮每次的方向增量（弧度）。v0.42：0.0035 → 0.00035（原来的十分之一）。
    /// 原值 0.0035 rad ≈ 0.2°：长台（约 3.5m）上等于偏 12mm，比球半径（26mm）小不了多少，
    /// 而长台进球的角度容差只有零点几度 —— 一按就越过目标，根本没法微调。
    /// 降到 0.00035（≈0.02°）后，长台上每按一次只偏 1.2mm，可以逐步逼近目标点。
    public const float NudgeStep = 0.00035f;

    /// 精确旋转指定弧度（UI 上 ◀ ▶ 按钮调用，见 NudgeStep）。
    /// 同时把角度规范到 [0, 2π) 区间，防止无限累加后浮点精度下降。
    public void Rotate(float deltaRad)
    {
        aimAngle += deltaRad;
        if (aimAngle > Mathf.PI * 2f) aimAngle -= Mathf.PI * 2f;
        if (aimAngle < 0f) aimAngle += Mathf.PI * 2f;
    }

    // ---------------------------------------------------------------------------------
    // 每帧：根据游戏状态决定显示什么。
    //   非 Aiming（或白球已落袋/正在出杆动画）→ 隐藏球杆与辅助线
    //   Aiming → 处理拖动输入 + 摆球杆 + 画辅助线
    // 球杆摆位公式：
    //   位置 = 白球 - Dir × (BallR + pull)，即贴着白球后方；pull 随力度增大而拉远（蓄力感）
    //   pull = 0.055 + power × 0.28 （米）
    //   旋转 = LookRotation(Dir) × Euler(0, 90, 0)：球杆模型杆头在局部 +X，
    //          先绕 Y 转 90° 把杆头对到局部 +Z，再由 LookRotation 对到世界瞄准方向。
    //          （历史教训：当初用 -90° 导致杆尾朝前，视觉上球杆插进球堆。）
    // ---------------------------------------------------------------------------------
    void Update()
    {
        var gm = GameManager.I;
        if (gm == null || aimLine == null) return;

        bool active = gm.state == GameManager.State.Aiming && !gm.cue.potted && !striking;
        if (!active)
        {
            if (stick != null && !striking) stick.SetActive(false);   // 出杆动画期间保留显示
            aimLine.Show(false);
            if (active || gm.state != GameManager.State.Aiming) return;
            return;
        }
        HandleAimInput();
        if (stick != null)
        {
            stick.SetActive(true);
            float pull = 0.055f + power * 0.28f;                      // 蓄力后拉量（米）
            stick.transform.position = gm.cue.transform.position - Dir * (G.BallR + pull);
            stick.transform.rotation = Quaternion.LookRotation(Dir) * Quaternion.Euler(0f, 90f, 0f);
        }
        aimLine.Show(aimLineOn);
        // 无论辅助线是否显示都要计算：GameManager 依赖 AimedBall 做"指定彩球"（Rule 3(f)(i)(b)）
        aimLine.Compute(gm.cue.transform.position, Dir);
    }

    // 起手就落在 UI 上的手指（fingerId）。只在手指按下那一帧判定一次并记住，
    // 之后这根手指不再参与瞄准——见 HandleAimInput 里的说明。
    private readonly HashSet<int> uiFingers = new HashSet<int>();

    // v0.36：正在"拖白球"的手指（"球在手"时按下点落在白球上）。同样只判定一次。
    private readonly HashSet<int> ballFingers = new HashSet<int>();
    private bool mouseBallDrag;    // 鼠标版（编辑器调试）拖白球

    /// 主相机（Bootstrapper 给它打了 MainCamera 标签，取不到时兜底全局查找）。
    private Camera Cam
    {
        get
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) cam = FindObjectOfType<Camera>();
            }
            return cam;
        }
    }

    /// 屏幕点是否落在白球上（含触摸容差，按屏幕高度自适应——手指比球大得多）。
    private bool OnCueBall(Vector2 screenPos)
    {
        var gm = GameManager.I;
        Camera c = Cam;
        if (c == null || gm == null || gm.cue == null) return false;
        Vector3 sp = c.WorldToScreenPoint(gm.cue.transform.position);
        if (sp.z < 0f) return false;                                  // 球在相机背后
        float tol = Mathf.Max(70f, Screen.height * 0.085f);
        return ((Vector2)sp - screenPos).sqrMagnitude <= tol * tol;
    }

    /// 屏幕点 → 台面平面（球心高度）上的世界坐标。
    private Vector3 TablePoint(Vector2 screenPos)
    {
        var gm = GameManager.I;
        Camera c = Cam;
        if (c == null || gm == null || gm.cue == null) return Vector3.zero;
        Ray ray = c.ScreenPointToRay(screenPos);
        var pl = new Plane(Vector3.up, new Vector3(0f, G.BallR, 0f));
        float dist;
        if (pl.Raycast(ray, out dist)) return ray.GetPoint(dist);
        return gm.cue.transform.position;                             // 相机几乎平视时兜底：不动
    }

    private bool OverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    // ---------------------------------------------------------------------------------
    // 瞄准输入：触屏优先，无触屏时退回鼠标（编辑器调试用）。
    //   - 累加所有触点的水平位移 deltaPosition.x
    //   - 落在 UI 上的触摸/点击不参与（EventSystem 命中检测），防止拖滑条时转动球杆
    //   - 最后按 Sensitivity 换算成弧度
    //
    // v0.36："球在手"（开球前 / 白球落袋后）时，按在白球上的手指改为【拖动白球摆放】，
    //        其余手指照常转球杆 —— 于是"摆球"与"瞄准"不需要任何模式切换按钮。
    // ---------------------------------------------------------------------------------
    void HandleAimInput()
    {
        var gm = GameManager.I;
        float dx = 0f;                                                // 本帧水平位移合计（像素）
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
            {
                // v0.34：UI 命中判定只在【手指按下那一帧】做一次并记住这根手指。
                // 旧版每帧都调 IsPointerOverGameObject：手指从台面拖到底部 UI 区域上方时
                // 瞄准会莫名中断（人还按着屏幕，球杆却不动了）。
                if (t.phase == TouchPhase.Began)
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
                    {
                        uiFingers.Add(t.fingerId);
                        ballFingers.Remove(t.fingerId);
                        continue;
                    }
                    uiFingers.Remove(t.fingerId);
                    // v0.36：按下点落在白球上且白球"在手" → 这根手指拖球，不参与瞄准
                    if (gm.cueInHand && OnCueBall(t.position)) ballFingers.Add(t.fingerId);
                    else ballFingers.Remove(t.fingerId);
                    continue;
                }
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    uiFingers.Remove(t.fingerId);
                    ballFingers.Remove(t.fingerId);
                    continue;
                }
                if (t.phase != TouchPhase.Moved) continue;             // 只统计移动中的触点
                if (uiFingers.Contains(t.fingerId)) continue;           // 起手落在 UI 上的手指不参与瞄准
                if (ballFingers.Contains(t.fingerId))                   // 拖白球摆放（越界会由 GM 夹回 D 区）
                {
                    gm.DragCueBall(TablePoint(t.position));
                    continue;
                }
                dx += t.deltaPosition.x;
            }
        }
        else
        {
            // ---- 鼠标（编辑器内调试用）----
            if (Input.GetMouseButtonDown(0))
                mouseBallDrag = gm.cueInHand && !OverUi() && OnCueBall(Input.mousePosition);
            if (Input.GetMouseButtonUp(0)) mouseBallDrag = false;
            if (Input.GetMouseButton(0))
            {
                if (mouseBallDrag) gm.DragCueBall(TablePoint(Input.mousePosition));
                else if (!OverUi()) dx += Input.GetAxis("Mouse X");    // 鼠标 X 轴帧位移
            }
        }
        if (Mathf.Abs(dx) > 0f) Rotate(dx * Sensitivity);
    }

    /// "击球"按钮入口：Aiming 状态且未在出杆动画中才接受，防止连点重复出杆。
    public void BeginStrike()
    {
        var gm = GameManager.I;
        if (gm.state != GameManager.State.Aiming || striking) return;
        StartCoroutine(StrikeCo());
    }

    // ---------------------------------------------------------------------------------
    // 出杆协程：0.08 秒内把球杆从蓄力位置推近到贴球（Lerp 插值），动画结束瞬间
    // 调 GameManager.Shoot 施加真实速度。视觉与逻辑分离：动画只是演出，
    // 球的实际初速由 Shoot 里的 power 决定，v0.36 起连同加塞（spinV/spinH）一起交给物理。
    // ---------------------------------------------------------------------------------
    IEnumerator StrikeCo()
    {
        striking = true;
        aimLine.Show(false);                                          // 出杆瞬间隐藏辅助线
        if (stick == null)                                            // 无球杆资源时直接出杆
        {
            striking = false;
            GameManager.I.Shoot(Dir, power, spinV, spinH);
            yield break;
        }
        stick.SetActive(true);
        float t = 0f;
        Vector3 start = stick.transform.position;                     // 蓄力位置
        Vector3 end = GameManager.I.cue.transform.position - Dir * (G.BallR + 0.004f);  // 贴球位置
        while (t < 0.08f)
        {
            t += Time.deltaTime;
            stick.transform.position = Vector3.Lerp(start, end, Mathf.Clamp01(t / 0.08f));
            yield return null;
        }
        striking = false;
        GameManager.I.Shoot(Dir, power, spinV, spinH);                // 真正出杆（带上加塞）
    }
}
