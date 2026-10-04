// =====================================================================================
// UIJelly.cs —— 液态玻璃的"Q弹"按压动画（v0.44d 新增；v0.54 按 Apple/Emil 动效原则收敛）
//
// 效果：按下 → 按钮轻微压扁（0.96/0.93，Emil：按压反馈应 subtle 0.95~0.98）；松手 →
// 双轴弹簧带**轻微**过冲弹回（阻尼 18，ζ≈0.52，过冲 ~15% 但行程只有 4% → 观感 ~0.6%），
// 两轴刚度几乎一致（wobble 0.96）→ 收敛干净、不扭动。
// 纯程序弹簧（半隐式欧拉积分），天然满足"从当前值出发、可中断改向"（Apple §3/§4）。
//
// v0.54 调整（对照 animate/emil-design-eng skill）：
//   pressScale (0.90,0.84)→(0.96,0.93)——0.84 压扁过度，按压反馈要 subtle；
//   damping 13→18、wobbleFreq 0.85→0.96——按压缩短、扭动收敛，"简洁流畅"。
//
// 接线：
//   - 按钮（UIManager.Btn）：直接挂在按钮根物体上，自身实现 IPointerDown/Up/Exit，
//     与既有 EventTrigger（触感反馈）并存——uGUI 会把事件派发给物体上所有实现者。
//   - 滑块手柄 / 加塞圆点：物体本身不接收射线（raycastTarget=false），由宿主在
//     拖动开始/结束时调 SetHeld() 驱动（见 MakeSlider / SpinPad）。
//
// 注意：缩放的是 uGUI 根物体，IMGUI 文字层不跟随（0.3s 内的 4%~7% 形变，
// 与居中文字的错位肉眼几乎不可见，换来的是整棵按钮子树一起 Q 弹）。
// =====================================================================================
using UnityEngine;
using UnityEngine.EventSystems;

public class UIJelly : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("按下形态")]
    public Vector2 pressScale = new Vector2(0.96f, 0.93f); // v0.54：轻微压扁（按压反馈 subtle）
    [Header("弹簧参数")]
    public float stiffness = 300f;        // 刚度：越大回弹越快
    public float damping = 18f;           // 阻尼：v0.54 由 13 提到 18（ζ≈0.52，释放轻微过冲）
    public float wobbleFreq = 0.96f;      // y 轴相对 x 轴的刚度比（v0.54：0.85→0.96，几乎不扭动）

    private bool held;
    private Vector2 pos = Vector2.one;    // 当前逐轴缩放
    private Vector2 vel = Vector2.zero;   // 逐轴弹簧速度

    /// 非按钮目标（滑块手柄/加塞圆点）由宿主在拖动开始/结束时驱动。
    public void SetHeld(bool on) { held = on; }

    void OnEnable()
    {
        pos = Vector2.one;
        vel = Vector2.zero;
        Apply();
    }

    public void OnPointerDown(PointerEventData e) { held = true; }
    public void OnPointerUp(PointerEventData e) { held = false; }
    public void OnPointerExit(PointerEventData e) { held = false; }   // 手指滑出 = 取消按压

    void Update()
    {
        Vector2 target = held ? pressScale : Vector2.one;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);   // 掉帧时弹簧不爆炸
        for (int i = 0; i < 2; i++)
        {
            float k = stiffness * (i == 1 ? wobbleFreq : 1f);   // y 轴刚度略低 → 果冻扭动
            vel[i] += (target[i] - pos[i]) * k * dt;
            vel[i] *= Mathf.Exp(-damping * dt);
            pos[i] += vel[i] * dt;
        }
        Apply();
    }

    void OnDisable() { transform.localScale = Vector3.one; }    // 防止隐藏时残留形变

    void Apply()
    {
        transform.localScale = new Vector3(pos.x, pos.y, 1f);
    }
}
