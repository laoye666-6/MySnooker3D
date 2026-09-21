// =====================================================================================
// SpinPad.cs —— 加塞圆盘（击球点选择器），v0.36 新增
//
// 交互：一个画着白球俯视图的圆盘，玩家拖动里面的小圆点选择"杆头打在白球的哪个位置"：
//   点在圆心   → 中杆（无塞）
//   点在上方   → 高杆（跟杆，白球撞球后继续前冲）
//   点在下方   → 低杆（缩杆，白球撞球后被拉回来）
//   点在左右   → 左塞 / 右塞（撞库后横向偏移，即"吃库改角"）
// 圆盘的"上"对齐屏幕上方（与相机观察方向一致），玩家凭直觉拖动即可。
//
// 与物理的衔接：本组件只维护 spinV/spinH 两个 -1~1 的偏移量，写回 CueController；
// 出杆时经 GameManager.Shoot → BallController.ApplySpin 换算成白球初始角速度。
//
// 实现说明：沿用工程既有策略——图形用 uGUI（Image/RectTransform），
// 文字用 IMGUI（UIManager 里画），避免 uGUI 动态字体在部分机型上空白的问题。
// 拖动用 IDragHandler / IPointerDownHandler 实现（uGUI 事件系统，天然与按钮互不干扰）。
// =====================================================================================
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 加塞圆盘。挂在圆盘根物体上，由 UIManager.Build 创建并传入需要写入的控制器。
/// </summary>
public class SpinPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    /// 目标控制器（加塞值最终写进它的 spinV/spinH）。
    public CueController target;

    /// 圆盘半径（设计像素），由 Build 时设定。
    public float radius = 78f;

    private RectTransform dot;          // 可拖动的小圆点
    private RectTransform ring;         // 圆盘本体（用于把屏幕坐标换算成局部坐标）
    private Image dotImg;       // 可拖动的圆点（纯色块 Image，真机兼容性最好）
    private float dragging;             // >0 表示正在拖动（用于放大圆点反馈）

    /// 归一化击球点：spinH 右为 +，spinV 上为 +（与 CueController 的语义一致）。
    public float SpinH { get; private set; }
    public float SpinV { get; private set; }

    /// <summary>
    /// 创建圆盘结构。parent 为 HUD 画布下的容器物体；
    /// 面板本身（底框/边框）由 UIManager 用既有的 Img() 画好，这里只搭"球面+圆点"。
    /// </summary>
    public void Build(RectTransform self, CueController cc, float r)
    {
        target = cc;
        radius = r;
        ring = self;

        // 白球俯视图（圆盘本体）：浅色方形球面（原神风深色面板之上的浅色区域）。
        // v0.40 结论（真机实测四轮，别再折腾）：**给这个面板加任何 sprite 都会让整块
        // 面板在真机变成金色实心（边框盖住一切）、编辑器里却完全正常**。
        //   试过的圆形方案全部失败：①运行时 Sprite.Create；②运行时 Texture2D+RawImage；
        //   ③导入 PNG 资源 + Android 强制未压缩；④同样导入资源但修了层级（SetAsFirstSibling）。
        //   四条都是"编辑器正常、真机金色板"。而**纯色块方形**在真机稳定显示，
        //   故最终定为方形。若将来要圆，必须在真机上重新验证，不能只看编辑器。
        var face = NewImage("SpinFace", self, Vector2.zero, Vector2.one * (r * 2f), new Color(0.90f, 0.90f, 0.86f, 0.95f));
        face.raycastTarget = true;                     // 自身接收点击（拖动范围 = 整个圆盘）

        // 十字准线：帮助判断"中杆/高杆/低杆"位置（略短于直径，端头不贴圆边）
        NewImage("SpinLineH", self, Vector2.zero, new Vector2(r * 1.7f, 3f), new Color(0.55f, 0.57f, 0.60f, 0.5f));
        NewImage("SpinLineV", self, Vector2.zero, new Vector2(3f, r * 1.7f), new Color(0.55f, 0.57f, 0.60f, 0.5f));

        // v0.39：外圈金环刻度（原神风）——12 颗小金菱绕圆盘边缘，兼作"球面范围"提示
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f;
            var tick = NewImage("SpinTick" + i, self,
                new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r - 7f), Vector2.one * 7f,
                new Color(0.80f, 0.65f, 0.30f, 0.9f));
            tick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 菱形
            tick.raycastTarget = false;
        }

        // 可拖动圆点（杆头位置）：同样是圆（代表杆头打在球面上的位置）
        dotImg = NewImage("SpinDot", self, Vector2.zero, Vector2.one * 30f, new Color(0.85f, 0.20f, 0.16f, 0.98f));
        dotImg.raycastTarget = false;                  // 圆点不拦截射线，交给圆盘统一处理
        dot = dotImg.rectTransform;

        SetSpin(0f, 0f);                               // 初始中杆
    }

    /// 画一个居中的 Image（anchor 固定为 0.5/0.5，位置与尺寸用中心锚定语义）。
    private Image NewImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = col;
        return img;
    }

    // ---------------------------------------------------------------------------------
    // 拖动处理：把点击位置换算到圆盘局部坐标 → 归一化到半径 → 夹进单位圆
    // ---------------------------------------------------------------------------------
    public void OnPointerDown(PointerEventData e) { dragging = 1f; Apply(e); }
    public void OnDrag(PointerEventData e) { dragging = 1f; Apply(e); }
    public void OnPointerUp(PointerEventData e) { dragging = 0f; }

    void Apply(PointerEventData e)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ring, e.position, e.pressEventCamera, out local))
            return;
        // 屏幕 y 向上，与"高杆在上"的直觉一致，故 spinV 直接用 +y
        float nx = local.x / radius;
        float ny = local.y / radius;
        float d = Mathf.Sqrt(nx * nx + ny * ny);
        if (d > 1f) { nx /= d; ny /= d; }              // 超出圆盘 → 投到边缘（最大塞）
        SetSpin(nx, ny);
    }

    /// <summary>写入加塞值并同步圆点位置与目标控制器。</summary>
    public void SetSpin(float h, float v)
    {
        SpinH = Mathf.Clamp(h, -1f, 1f);
        SpinV = Mathf.Clamp(v, -1f, 1f);
        if (target != null) { target.spinH = SpinH; target.spinV = SpinV; }
        if (dot != null)
            dot.anchoredPosition = new Vector2(SpinH, SpinV) * radius * 0.72f;   // 圆点不贴边，留一点视觉余量
        if (dotImg != null)
            dotImg.rectTransform.sizeDelta = Vector2.one * (dragging > 0f ? 34f : 30f);
    }

    /// <summary>把加塞值复位到中杆（换手/新一杆时调用，避免上一杆的塞一直留着）。</summary>
    public void ResetSpin() { dragging = 0f; SetSpin(0f, 0f); }

    /// 当前加塞的文字描述（HUD 显示用）。
    /// v0.37 分档对齐物理映射（SpinLowK=2.0）：spinV≈-0.5 时 ω≈0 = 真实斯诺克的
    /// 【定杆】（打中心偏下，撞球后原地停住），单独命名让玩家理解这一档的手感。
    public string Describe()
    {
        if (Mathf.Abs(SpinH) < 0.08f && Mathf.Abs(SpinV) < 0.08f) return "中杆";
        string v;
        if (Mathf.Abs(SpinV) < 0.08f) v = "";
        else if (SpinV >= 0.6f) v = "高杆";
        else if (SpinV >= 0.08f) v = "略高";
        else if (SpinV <= -0.7f) v = "低杆";
        else if (SpinV <= -0.4f) v = "定杆";
        else v = "略低";
        string h = Mathf.Abs(SpinH) < 0.08f ? "" :
                   SpinH > 0f ? (SpinH > 0.6f ? "右塞" : "略右") : (SpinH < -0.6f ? "左塞" : "略左");
        return v + h;
    }
}
