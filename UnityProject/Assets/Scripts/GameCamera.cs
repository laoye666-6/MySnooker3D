// =====================================================================================
// GameCamera.cs —— 游戏主相机（挂在 MainCamera 上，与 Camera/AudioListener 同物体）
//
// 入场动画（v0.33 重做，追求丝滑）：
//   1. 路径：三次贝塞尔弧线——从黑球端高空出发，绕球台 +X 端划一道弧，
//      再沿 -Z 库边滑行落到菜单演示机位（曲线本身 C¹ 连续，无直角折返）
//   2. 缓动：五次 smootherstep（起/收一二阶导数都为 0），加速度连续，绝不顿挫
//   3. 注视点：每帧对注视目标再做一次指数时间平滑，吸收帧率抖动带来的视角微跳
//   4. 收尾：待机漂移的相位从动画结束时刻起算，衔接处零跳变
//
// 机位：
//   Menu/GameOver   演示机位（落定后缓慢漂移）
//   Aiming          俯身击球机位（v0.56）：眼睛贴到台面上方——白球后方 0.42m、
//                   台呢上方仅 0.11m（真实俯身击球时眼睛比球心只高几厘米），
//                   注视白球前方 0.55m 的瞄准线，随瞄准实时旋转。
//                   参考 PCOL（heyzxz.me/pcol）的俯身参数：站立眼高 1.7m →
//                   俯身眼高 1.0m（其台呢 0.88m → 眼比台呢高 0.12m）、身体绕白球
//                   0.38m 圆站位再前倾——换算到本工程（台呢 y=0）即 0.11m 高。
//   Rolling         全景机位：高 2.5m，X 向跟随白球（= "站立环视"，与俯身位拉开反差）
// =====================================================================================
using UnityEngine;

public class GameCamera : MonoBehaviour
{
    private Vector3 lookAt;           // 当前注视点（始终做时间平滑，避免视线突跳）

    /// <summary>
    /// v0.57：瞄准态的"起立观察"开关（HUD 按钮"起立/俯身"切换）。
    /// false = 俯身击球机位（默认）；true = 站起身俯瞰球台。
    /// 模拟真实打球时站起来看球型的动作；出杆结算回到瞄准态后保持当前姿态，
    /// 由玩家自己决定何时俯身（与 PCOL 的 stand/aim 切换行为一致）。
    /// </summary>
    public bool standView;

    // ---- 入场动画参数 ----
    // v0.50：起点/控制点高度压到 2.45~2.55m —— 台球厅有天花板（y=2.68），旧值 4.7m
    // 会让入场弧线从厅顶上方俯冲、穿越天花板（背面剔除导致穿帮闪烁）。
    private float introT;                                   // 入场动画已播秒数
    private const float IntroDur = 3.6f;                    // 稍放慢一点，运镜更从容
    private static readonly Vector3 IntroFrom = new Vector3(2.6f, 2.55f, 2.4f);   // 起点：黑球端高位（厅内）
    private static readonly Vector3 IntroC1 = new Vector3(3.1f, 2.6f, 0.1f);      // 贝塞尔控制点1：绕 +X 端
    private static readonly Vector3 IntroC2 = new Vector3(2.3f, 2.45f, -2.6f);    // 贝塞尔控制点2：转入 -Z 库边
    private static readonly Vector3 IntroLookFrom = new Vector3(0.7f, 0f, 0f);
    private static readonly Vector3 DemoPos = new Vector3(-2.75f, 1.62f, -2.7f);   // 菜单演示机位（v0.57：球台外对角近机位，灯+台+厅同框，PCOL 菜单同构图）
    private static readonly Vector3 DemoLook = new Vector3(0.35f, -0.12f, 0.3f);

    void Start()
    {
        // 开局即处于入场起点（高空俯瞰），动画由 LateUpdate 推进
        transform.position = IntroFrom;
        lookAt = IntroLookFrom;
        transform.LookAt(lookAt);
    }

    /// 三次贝塞尔曲线插值。
    static Vector3 Bezier(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3;
    }

    void LateUpdate()
    {
        var gm = GameManager.I;
        if (gm == null || gm.cue == null) return;      // 场景未就绪时不动

        // v0.34：一旦离开菜单（开始游戏），立即把入场计时器补满。否则玩家在菜单前 3.6s 内点了
        // "开始游戏"，introT 会冻结在中途，之后回到结算画面时又从那里重播入场弧线——
        // 相机会瞬间吸附到弧线中段（硬切），正是"入场运镜"要避免的。
        if (gm.state != GameManager.State.Menu && gm.state != GameManager.State.GameOver)
            introT = IntroDur;

        Vector3 desired, look;
        if (gm.state == GameManager.State.Menu || gm.state == GameManager.State.GameOver)
        {
            if (introT < IntroDur)
            {
                // ---- 入场运镜：贝塞尔弧线 + 五次 smootherstep ----
                introT += Time.deltaTime;
                float raw = Mathf.Clamp01(introT / IntroDur);
                float k = raw * raw * raw * (raw * (raw * 6f - 15f) + 10f);   // C² 连续缓动
                desired = Bezier(IntroFrom, IntroC1, IntroC2, DemoPos, k);
                look = Vector3.Lerp(IntroLookFrom, DemoLook, k);
                // 注视点时间平滑：即使帧率波动，视角也只做指数收敛，绝无硬切
                lookAt = Vector3.Lerp(lookAt, look, 1f - Mathf.Exp(-9f * Time.deltaTime));
                transform.position = desired;
                transform.LookAt(lookAt);
                return;
            }
            // ---- 菜单待机：绕注视点缓慢漂移 ±6°，相位从"入场动画结束"起算 ----
            // v0.34：用自累加的 introT 而非 Time.timeSinceLevelLoad —— 后者含 Bootstrapper
            // 建场景耗时(0.1~0.5s)，入场结束瞬间 idle 已经非 0，衔接处会有一小段跳变。
            float idle = Mathf.Max(0f, introT - IntroDur);
            float ang = Mathf.Sin(idle * 0.15f) * 6f;
            desired = DemoLook + Quaternion.Euler(0f, ang, 0f) * (DemoPos - DemoLook);
            look = DemoLook;
        }
        else if (gm.state == GameManager.State.Aiming)
        {
            Vector3 dir = gm.cueCtl.Dir;               // 当前瞄准方向（拖动时相机跟着转）
            Vector3 c = gm.cue.transform.position;
            if (standView)
            {
                // ---- 起立观察机位（v0.57，HUD 按钮"起立/俯身"切换）----
                // 站起身沿瞄准方向后退半步、眼高提到真实站立眼高（PCOL eyeStandHeight
                // 1.7m - 台呢 0.88 ≈ 0.82，此处取 0.85），视线压向台面中部——
                // 一眼看到整张球台的球型分布（模拟真实打球时站起来看球）。
                desired = c - dir * 1.55f + Vector3.up * 0.85f;
                look = c + dir * 1.15f;
                look.y = 0.02f;
            }
            else
            {
                // ---- 俯身击球机位（v0.56）：眼睛降到真实俯身高度 ----
                // 横向：白球后方 0.42m（= PCOL 身体绕球 0.38m 圆 + 俯身前倾的合成距离）；
                // 高度：台呢上方 0.11m（球心 0.026 → 眼比球心高约 8cm，即"下巴快贴到杆"的视角）。
                // 注视点：瞄准线上白球前方 0.55m、高度略高于球心——画面里白球压在下三分之一，
                // 前方球路与袋口在视线正前方，与真实俯身看到的构图一致。
                desired = c - dir * 0.42f + Vector3.up * 0.11f;
                look = c + dir * 0.55f;
                look.y = 0.045f;
            }
        }
        else
        {
            // ---- Rolling/全景：升高到 2.5m 看全场，水平位置向白球方向偏移（限 ±1.1m）----
            Vector3 c = gm.cue.transform.position;
            desired = new Vector3(Mathf.Clamp(c.x, -1.1f, 1.1f), 2.5f, -1.85f);
            look = new Vector3(c.x * 0.6f, 0f, 0f);
        }

        // 指数衰减插值：帧率无关的平滑跟随（开始游戏时菜单机位→俯身机位的大幅
        // 移动也会被此插值自然演变成一段"俯身到位"的转场）。
        // v0.56：机位降低后跟瞄响应感更明显——瞄准态且已贴近目标位时把时间常数
        // 收紧（τ≈0.08s），拖动转向几乎即时（真实俯身时头是跟着杆走的）；
        // 大距离转场（开始游戏/滚动→俯身）仍用慢常数保持"俯身下去"的过程感。
        float near = (gm.state == GameManager.State.Aiming &&
                      (transform.position - desired).sqrMagnitude < 0.25f) ? 0.08f : 0.2f;
        float kk = 1f - Mathf.Exp(-Time.deltaTime / near);
        transform.position = Vector3.Lerp(transform.position, desired, kk);
        lookAt = Vector3.Lerp(lookAt, look, kk);
        transform.LookAt(lookAt);
    }
}
