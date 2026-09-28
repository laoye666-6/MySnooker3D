// =====================================================================================
// UIJelly.cs —— 液态玻璃的"Q弹"按压动画（v0.44d 新增）
//
// 效果：按下 → 按钮压扁（横向略收、纵向更扁）；松手 → 双轴弹簧带过冲地弹回，
// 且两轴刚度略不同 → 释放瞬间产生果冻般的扭动，然后收敛回 1。
// 纯程序弹簧（半隐式欧拉积分，欠阻尼），没有任何动画曲线预烘焙。
//
// 接线：
//   - 按钮（UIManager.Btn）：直接挂在按钮根物体上，自身实现 IPointerDown/Up/Exit，
//     与既有 EventTrigger（触感反馈）并存——uGUI 会把事件派发给物体上所有实现者。
//   - 滑块手柄 / 加塞圆点：物体本身不接收射线（raycastTarget=false），由宿主在
//     拖动开始/结束时调 SetHeld() 驱动（见 MakeSlider / SpinPad）。
//
// 注意：缩放的是 uGUI 根物体，IMGUI 文字层不跟随（0.3s 内的 6%~12% 形变，
// 与居中文字的错位肉眼几乎不可见，换来的是整棵按钮子树一起 Q 弹）。
// =====================================================================================
using UnityEngine;
using UnityEngine.EventSystems;

public class UIJelly : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("按下形态")]
    public Vector2 pressScale = new Vector2(0.90f, 0.84f); // 横向略收、纵向更扁（按压感明显）
    [Header("弹簧参数")]
    public float stiffness = 300f;        // 刚度：越大回弹越快
    public float damping = 13f;           // 阻尼：低于临界阻尼(2√k)才有 Q 弹过冲
    public float wobbleFreq = 0.85f;      // y 轴相对 x 轴的刚度比（≠1 → 释放时扭动）

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
