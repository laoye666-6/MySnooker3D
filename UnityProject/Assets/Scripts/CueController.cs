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

    private GameObject stick;      // 球杆模型（Blender 导出，杆头在局部 +X）
    private AimLine aimLine;       // 同物体上的瞄准线组件
    private bool striking;         // 正在播出杆动画（期间锁输入、不隐藏球杆）

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

    /// 精确旋转指定弧度（UI 上 ◀ ▶ 按钮每次调 ±0.0035，即 ±0.2°）。
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
        if (aimLineOn) aimLine.Compute(gm.cue.transform.position, Dir);
    }

    // ---------------------------------------------------------------------------------
    // 瞄准输入：触屏优先，无触屏时退回鼠标（编辑器调试用）。
    //   - 累加所有触点的水平位移 deltaPosition.x
    //   - 落在 UI 上的触摸/点击不参与（EventSystem 命中检测），防止拖滑条时转动球杆
    //   - 最后按 Sensitivity 换算成弧度
    // ---------------------------------------------------------------------------------
    void HandleAimInput()
    {
        float dx = 0f;                                                // 本帧水平位移合计（像素）
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
            {
                if (t.phase != TouchPhase.Moved) continue;            // 只统计移动中的触点
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId)) continue;
                dx += t.deltaPosition.x;
            }
        }
        else if (Input.GetMouseButton(0))
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                dx += Input.GetAxis("Mouse X");                       // 鼠标 X 轴帧位移
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
    // 球的实际初速由 Shoot 里的 power 决定。
    // ---------------------------------------------------------------------------------
    IEnumerator StrikeCo()
    {
        striking = true;
        aimLine.Show(false);                                          // 出杆瞬间隐藏辅助线
        if (stick == null)                                            // 无球杆资源时直接出杆
        {
            striking = false;
            GameManager.I.Shoot(Dir, power);
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
        GameManager.I.Shoot(Dir, power);                              // 真正出杆
    }
}
