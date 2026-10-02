// =====================================================================================
// UIGlass.cs —— iOS「液态玻璃」图形组件（v0.44 新增）
//
// 为什么自己写网格而不是用贴图：本项目的真机教训（README 踩坑 6/25）是"运行时创建的
// UI 贴图（Sprite.Create / new Texture2D / 导入 PNG）在真机可能整块不渲染，编辑器却
// 完全正常"。纯色块 Image 从来没出过问题——它走的是"白色纹理 × 顶点色"这条最朴素的
// 路径。本组件用 OnPopulateMesh 直接生成圆角矩形/胶囊/圆形/描边环的顶点网格，
// **零贴图、零纹理采样**，与纯色块完全同一条渲染路径，把真机风险压到最低。
//
// 液态玻璃质感（v0.47 参考稿）= 重磨砂 + 边缘透镜拉丝（LiquidGlass.shader 的 SDF 光学）
//   + 顶部高光条 + 亮色描边环(本组件 Ring 模式) + 软投影(AttachShadow) + 文字(IMGUI)。
// 「与背景交互」全部走 GlassSceneCamera 的双 RT：_GlassScene(半分辨率清晰) / _GlassBlur(四分辨率磨砂)。
//
// 注意：CanvasGroup 的整体淡入淡出（菜单/结算/HUD）通过 CanvasRenderer 继承 alpha
// 实现，对本组件同样生效；Button 的 pressedColor 变色也以本组件为 targetGraphic。
// =====================================================================================
using UnityEngine;
using UnityEngine.UI;

public class UIGlass : MaskableGraphic
{
    public enum Shape { RoundedRect, Capsule, Circle, Ring }

    public Shape shape = Shape.RoundedRect;
    /// 圆角半径（局部单位 ≈ 设计像素）；Capsule/Circle 忽略此值（自动取短边一半）。
    public float cornerRadius = 24f;
    /// Ring 模式的描边宽度。
    public float rimWidth = 2.5f;
    /// 每个圆角的弧段数（4 角共用；40 段轮廓对 UI 完全够用且可合批）。
    public int cornerSegments = 10;
    /// 顶部提亮幅度：玻璃上沿受光更亮、下沿略暗（iOS 玻璃的垂直明暗渐变）。
    [Range(0f, 0.3f)] public float sheen = 0.12f;

    private static Shader blurShader;                    // LiquidGlass.shader 的 Resources 缓存

    /// 创建并配置一个玻璃子物体（用法对齐 UIManager.Img：中心锚定 + 位置/尺寸）。
    public static UIGlass Add(Transform parent, string name, Vector2 anchor, Vector2 pos,
                              Vector2 size, Color col, float radius, bool capsule = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIGlass));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var g = go.GetComponent<UIGlass>();
        g.color = col;
        g.shape = capsule ? Shape.Capsule : Shape.RoundedRect;
        g.cornerRadius = radius;
        return g;
    }

    /// 全屏拉伸的玻璃底板（主菜单/结算的磨砂层），blur=true 时启用背景模糊。
    public static UIGlass Stretch(Transform parent, string name, Color col, bool blur,
                                  float blurRadius = 3.5f, float frost = 0.16f, float lift = 0.12f,
                                  float crisp = 0.22f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIGlass));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var g = go.GetComponent<UIGlass>();
        g.color = col;
        g.raycastTarget = true;
        if (blur) g.UseBlur(blurRadius, frost, lift, crisp);
        return g;
    }

    /// 磨砂玻璃（大面板）：重磨砂 + 长跨度边缘拉丝（v0.47 参考质感；
    /// 参考图的大面板拖影约占高度 15~25%，故 streak/lens 给到大值）。
    public void UseBlur(float blur = 14f, float frost = 0.14f, float lift = 0.10f, float crisp = 0.35f)
    {
        UseLiquid(frost, crisp * 0.3f, 5f, 0.7f, 36f, 90f, 40f, 3f,
                  0.20f, 0.10f, 0.30f, 0.12f, 0.30f, 0.32f, 0.10f);
    }

    /// 清澈水玻璃（按钮/胶囊/圆点）：v0.47 起映射到参考质感的控件预设——
    /// 保留旧签名（SpinPad 等调用点不用改），折射/清晰度收敛、磨砂与边缘拉丝成为主导。
    public void UseRefraction(float blur = 1.5f, float refr = 22f, float bulge = 1.4f,
                              float spec = 1.0f, float edgeAlpha = 0.6f, float crisp = 0.55f,
                              float glow = 0.55f)
    {
        UseLiquid(0.36f, crisp * 0.35f, refr * 0.55f, bulge, 34f, 44f, 26f, 2f,
                  0.28f, 0.12f, 0.32f, 0.10f, spec * 0.4f, edgeAlpha, 0.18f);
    }

    /// <summary>
    /// v0.47 参考质感的完整参数面板（所有 LiquidGlass 元素的底层入口）：
    ///   frost  奶白磨砂（0=清透 1=全白）  crisp 清晰场景混入  refr/bulge 穹顶折射
    ///   lens   边缘法向透镜（px，把界外背景拉进来）  streak 边缘切向拉丝（px）
    ///   edgeW  边缘效果带宽（px）  blurTexels 磨砂源上的小核模糊（blur-RT texel）
    ///   sheen  顶部内侧受光高光  innerSh 底部内侧厚度阴影  rim 最边缘细亮边
    ///   edgeDark 边缘轻收暗（亮背景可读性）  spec 镜面高光  edgeAlpha 边缘 alpha 抬升
    ///   glow   三盏游动光源强度（v0.47 调低作点缀）
    /// </summary>
    public void UseLiquid(float frost, float crisp, float refr, float bulge,
                          float lens, float streak, float edgeW, float blurTexels,
                          float sheen, float innerSh, float rim, float edgeDark,
                          float spec, float edgeAlpha, float glow)
    {
        var m = MakeGlassMaterial();
        if (m == null) return;
        m.SetFloat("_BlurTexels", blurTexels);
        m.SetFloat("_Frost", frost);
        m.SetFloat("_Lift", 0.10f);                     // v0.47：整体提亮固定档（参考稿奶白感）
        m.SetFloat("_Crisp", crisp);
        m.SetFloat("_Refr", refr);
        m.SetFloat("_Bulge", bulge);
        m.SetFloat("_Lens", lens);
        m.SetFloat("_Streak", streak);
        m.SetFloat("_EdgeW", edgeW);
        m.SetFloat("_Sheen", sheen);
        m.SetFloat("_InnerSh", innerSh);
        m.SetFloat("_Rim", rim);
        m.SetFloat("_EdgeDark", edgeDark);
        m.SetFloat("_SpecInt", spec);
        m.SetFloat("_EdgeAlpha", edgeAlpha);
        m.SetFloat("_Glow", glow);
        material = m;
    }

    /// <summary>
    /// v0.47 软边模式（投影专用）：不采场景，颜色=顶点色，
    /// alpha 从中心实体向网格边缘在 fade(px) 内平滑淡出。
    /// </summary>
    public void UseShadow(float fade = 16f)
    {
        var m = MakeGlassMaterial();
        if (m == null) return;
        m.SetFloat("_SoftMode", 1f);
        m.SetFloat("_SoftFade", fade);
        material = m;
    }

    /// <summary>
    /// v0.47：给玻璃元素加软投影（参考图药丸下方的柔和落影）。
    /// 生成一个稍大、下移的玻璃形状插到本元素【正后方】（同父层级），
    /// 用 _SoftMode 软边模式实现中心实、边缘淡出的柔影。
    /// 注意：挂在带 CanvasGroup 的面板下时会一起淡入淡出；自身独立淡入淡出的
    /// 元素（中央提示胶囊等）不要加，否则影子不会跟着消失。
    /// </summary>
    public UIGlass AttachShadow(float expand = 10f, float offsetY = -5f,
                                float alpha = 0.20f, float fade = 16f)
    {
        var rt = transform as RectTransform;
        if (rt == null || transform.parent == null) return null;
        var go = new GameObject(gameObject.name + "Shadow",
                                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIGlass));
        var srt = (RectTransform)go.transform;
        srt.SetParent(transform.parent, false);
        srt.anchorMin = srt.anchorMax = rt.anchorMin;
        srt.anchoredPosition = rt.anchoredPosition + new Vector2(0f, offsetY);
        srt.sizeDelta = rt.sizeDelta + new Vector2(expand, expand);
        srt.SetSiblingIndex(rt.GetSiblingIndex());      // 插到本元素之前 → 渲染在其下方
        var g = go.GetComponent<UIGlass>();
        g.shape = shape == Shape.Ring ? Shape.RoundedRect : shape;
        g.cornerRadius = cornerRadius + expand * 0.5f;
        g.sheen = 0f;                                   // 投影不需要顶点渐变
        g.color = new Color(0.05f, 0.07f, 0.10f, alpha);
        g.raycastTarget = false;
        g.UseShadow(fade);
        return g;
    }

    private static Material MakeGlassMaterial()
    {
        if (blurShader == null)
        {
            blurShader = Resources.Load<Shader>("Shaders/LiquidGlass");
            if (blurShader == null)
                Debug.LogWarning("[GLASS] LiquidGlass shader 缺失（应位于 Assets/Resources/Shaders/）");
        }
        return blurShader != null ? new Material(blurShader) : null;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 1f || r.height < 1f) return;           // 退化矩形（如力值 0 时的滑条填充）不生成网格
        float halfMin = Mathf.Min(r.width, r.height) * 0.5f;
        // v0.47：把几何信息推给 LiquidGlass 的圆角矩形 SDF（材质是每元素独立实例；
        // 滑条填充等每帧变尺寸的元素随重建刷新）。坐标用画布单位，与 _EdgeW 等参数同尺度。
        if (material != null && material.HasProperty("_RectHW"))
        {
            float rad = (shape == Shape.Capsule || shape == Shape.Circle)
                ? halfMin
                : Mathf.Min(cornerRadius, halfMin);
            material.SetVector("_RectHW", new Vector4(r.width * 0.5f, r.height * 0.5f, 0f, 0f));
            material.SetFloat("_CornerR", rad);
        }
        Color cTop = Color.Lerp(color, Color.white, sheen);
        Color cBot = Color.Lerp(color, Color.black, sheen * 0.6f);

        switch (shape)
        {
            case Shape.Circle:
            case Shape.Capsule:
                AddRounded(vh, r, halfMin, cTop, cBot, false, 0f);
                break;
            case Shape.RoundedRect:
                AddRounded(vh, r, Mathf.Min(cornerRadius, halfMin), cTop, cBot, false, 0f);
                break;
            case Shape.Ring:
                float rr = Mathf.Min(cornerRadius, halfMin);
                AddRounded(vh, r, rr, cTop, cBot, true, Mathf.Clamp(rimWidth, 0.5f, rr * 0.5f));
                break;
        }
    }

    /// 圆角矩形轮廓生成：4 个圆角各走 cornerSegments 段圆弧，角与角之间的直边
    /// 由相邻弧端点连成的弦自然形成。fill=false 生成实心（中心扇形三角化）；
    /// fill=true 生成描边环（外轮廓 rad / 内轮廓 rad-rim，两条轮廓连成三角带）。
    void AddRounded(VertexHelper vh, Rect r, float rad, Color cTop, Color cBot, bool ring, float rim)
    {
        int seg = Mathf.Max(2, cornerSegments);
        int perim = seg * 4;                              // 轮廓总点数
        float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
        float hx = r.width * 0.5f - rad, hy = r.height * 0.5f - rad;

        // 4 个圆角的圆心与起始角（逆时针，从 +x 轴起）
        Vector2[] center =
        {
            new Vector2( hx,  hy), new Vector2(-hx,  hy),
            new Vector2(-hx, -hy), new Vector2( hx, -hy)
        };

        void VertAt(int i, float radius, out UIVertex v)
        {
            int k = i / seg;                              // 第几个圆角
            float a = (k * 90 + (i % seg) * 90f / seg) * Mathf.Deg2Rad;
            Vector2 pt = center[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            v = UIVertex.simpleVert;
            v.position = new Vector3(cx + pt.x, cy + pt.y);
            // uv0 = [-1,1] 归一化局部坐标：LiquidGlass.shader 用它构造凸面法线做折射
            v.uv0 = new Vector4(pt.x / (r.width * 0.5f), pt.y / (r.height * 0.5f), 0f, 0f);
            // 垂直渐变：顶部受光(亮) → 底部(暗)
            float t = Mathf.Clamp01(pt.y / (r.height * 0.5f) * 0.5f + 0.5f);
            v.color = Color.Lerp(cBot, cTop, t);
        }

        if (!ring)
        {
            // 实心：中心点 + 轮廓扇形
            var c = UIVertex.simpleVert;
            c.position = new Vector3(cx, cy);
            c.uv0 = Vector4.zero;
            c.color = Color.Lerp(cBot, cTop, 0.5f);
            int ci = vh.currentVertCount;
            vh.AddVert(c);
            for (int i = 0; i < perim; i++) { VertAt(i, rad, out var v); vh.AddVert(v); }
            for (int i = 0; i < perim; i++)
                vh.AddTriangle(ci, ci + 1 + i, ci + 1 + (i + 1) % perim);
        }
        else
        {
            // 描边环：外/内两条轮廓连成三角带（UI shader Cull Off，绕向无关）
            int baseIdx = vh.currentVertCount;
            for (int i = 0; i < perim; i++)
            {
                VertAt(i, rad, out var vo); vh.AddVert(vo);
                VertAt(i, rad - rim, out var vi); vh.AddVert(vi);
            }
            for (int i = 0; i < perim; i++)
            {
                int j = (i + 1) % perim;
                vh.AddTriangle(baseIdx + i * 2,     baseIdx + j * 2,     baseIdx + j * 2 + 1);
                vh.AddTriangle(baseIdx + i * 2,     baseIdx + j * 2 + 1, baseIdx + i * 2 + 1);
            }
        }
    }
}
