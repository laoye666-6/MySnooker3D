// =====================================================================================
// UIManager.cs —— 全部游戏界面：HUD 记分板 / 主菜单 / 设置面板 / 结算 / 力度滑条 / 按钮
//
// 渲染策略：
//   - 图形元素（面板、按钮、滑条）用 uGUI；文字统一用 IMGUI（OnGUI）绘制——
//     uGUI legacy Text 依赖动态字体图集，部分安卓机会整片空白，IMGUI 实测稳定。
//   - 文字坐标用 CRect()：屏幕中心 + (设计坐标-设计中心)×k，与 uGUI 中心锚定精确对齐。
//
// 动画系统（v0.31）：
//   - 入场：GameCamera 播 3.4s 俯冲飞行；菜单在 3.0s 起淡入（0.8s），与相机衔接
//   - 开始游戏/结算面板：CanvasGroup alpha 渐入渐出
//   - 设置面板：从右侧滑入 + 淡入（easeOutCubic 0.35s），关闭反向滑出
//
// 设置面板：帧率上限（60/90/120/144）/ 渲染分辨率（50%/75%/100%）/ 画面阴影（开/关），
//           改动即通过 GameSettings.Apply() 生效并持久化（PlayerPrefs）。
//
// 美化（v0.31）：所有 IMGUI 文字带投影；uGUI 按钮加深色描边底 + 顶部高光条；
//               菜单副标题下加金色装饰条；记分板加双方阵营色块。
// =====================================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UIManager : MonoBehaviour
{
    private Canvas canvas;
    private Font font;
    private Slider power;
    private GameObject menu, over, settingsPanel;
    private CanvasGroup menuCG, overCG, settingsCG;      // 三个面板的渐变组
    private Image menuImg;                               // 菜单遮罩（控制底色透明度）
    private Color menuBaseCol;
    private RectTransform settingsPanelRT;               // 设置滑入位移用
    private CueController cc;
    private Material uiMat;

    private bool menuActiveFlag;                         // ShowMenu 设置的"菜单应显示"
    private bool overActiveFlag;
    private bool settingsOpen;                           // 设置面板开/关
    private float settingsAnimT;                         // 设置滑入进度 0..1
    private float menuAlpha, overAlpha;                  // 当前渐变值（Update 平滑）
    private float settingsOffset;                        // 设置面板当前右移量（设计 px，IMGUI 同步用）
    private float settingsAlpha;                         // 设置面板当前整体透明度（IMGUI 同步用）

    // ---- IMGUI 文字内容 ----
    private string p1Text = "", p2Text = "";              // 玩家名（单杆分单独绘制，金色大字）
    private string p1Break = "0", p2Break = "0";          // 双方当前单杆分
    private string centerText = "", msgText = "", overTextStr = "";
    private string aimHudText = "", aimMenuText = "", powerText = "";
    private float msgTimer;

    // ---- 147 满分提示横幅（从底部弹出）----
    private RectTransform popupRT;
    private CanvasGroup popupCG;
    private float popupTimer;                             // >0 时横幅显示中（含弹入/停留/收回）
    private float popupAlpha, popupYoff = 760f;           // 当前透明度与底部偏移（IMGUI 同步用）
    private string popupPairsText = "";                   // 例如 "5/15"

    // =================================================================================
    // Build()：由 Bootstrapper 调用一次，搭出全部 UI。
    // =================================================================================
    public void Build()
    {
        cc = GameManager.I.cueCtl;
        font = LoadFont();
        var uiShader = Shader.Find("UI/Default");
        uiMat = uiShader != null ? new Material(uiShader) : null;

        // ---- HUD 主画布 ----
        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.transform.SetParent(transform, false);

        if (Object.FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // 顶部记分板底条 + 双方阵营色块（美化：蓝=玩家1，红=玩家2）
        Img("TopPanel", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 485), new Vector2(1920, 96), new Color(0f, 0f, 0f, 0.55f));
        Img("ChipP1", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-916, 492), new Vector2(28, 28), new Color(0.23f, 0.44f, 0.85f));
        Img("ChipP2", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(916, 492), new Vector2(28, 28), new Color(0.85f, 0.27f, 0.27f));

        // ---- 力度滑条 ----
        power = MakeSlider(cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(630, -448), new Vector2(300, 56));
        power.value = cc.power;
        power.onValueChanged.AddListener(v =>
        {
            cc.power = v;
            powerText = "力度 " + Mathf.RoundToInt(v * 100) + "%";
        });
        powerText = "力度 " + Mathf.RoundToInt(cc.power * 100) + "%";

        // ---- HUD 按钮（美化版：自动加深色描边底 + 顶部高光条）----
        Btn("ShootBtn", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(835, -452), new Vector2(180, 150),
            new Color(0.16f, 0.55f, 0.25f), cc.BeginStrike);
        Btn("AimHudBtn", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-820, -448), new Vector2(240, 62),
            new Color(0.16f, 0.30f, 0.55f), ToggleAim);
        Btn("NudgeL", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-668, -448), new Vector2(70, 62),
            new Color(0.20f, 0.23f, 0.32f), () => cc.Rotate(-0.0035f));
        Btn("NudgeR", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-594, -448), new Vector2(70, 62),
            new Color(0.20f, 0.23f, 0.32f), () => cc.Rotate(+0.0035f));
        Btn("RestartBtn", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-820, -364), new Vector2(240, 58),
            new Color(0.42f, 0.22f, 0.16f), () => UnityEngine.SceneManagement.SceneManager.LoadScene(0));
        Btn("SettingsHudBtn", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(-540, -364), new Vector2(240, 58),
            new Color(0.20f, 0.23f, 0.32f), ToggleSettings);

        // ---- 菜单/结算/设置 共用上层画布 ----
        var mgo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var mcan = mgo.GetComponent<Canvas>();
        mcan.renderMode = RenderMode.ScreenSpaceOverlay;
        mcan.sortingOrder = 200;
        var mscaler = mgo.GetComponent<CanvasScaler>();
        mscaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        mscaler.referenceResolution = new Vector2(1920, 1080);
        mscaler.matchWidthOrHeight = 0.5f;
        mgo.transform.SetParent(transform, false);

        // ---- 主菜单（带 CanvasGroup 做淡入淡出）----
        menu = StretchImg("Menu", mgo.transform, new Color(0.01f, 0.05f, 0.03f, 0.80f));
        menuBaseCol = menu.GetComponent<Image>().color;
        menuImg = menu.GetComponent<Image>();
        menuCG = menu.AddComponent<CanvasGroup>();
        menuCG.alpha = 0f;                                   // 入场动画期间不可见
        menuCG.blocksRaycasts = false;
        menuCG.interactable = false;

        // 金色装饰条（副标题下方左右各一条）
        Img("BarL", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(-280, -18), new Vector2(150, 6), new Color(1f, 0.84f, 0.35f, 0.9f));
        Img("BarR", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(280, -18), new Vector2(150, 6), new Color(1f, 0.84f, 0.35f, 0.9f));

        Btn("AimMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(400, 84),
            new Color(0.16f, 0.30f, 0.55f), ToggleAim);
        Btn("SettingsMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -290), new Vector2(460, 96),
            new Color(0.20f, 0.23f, 0.32f), ToggleSettings);
        Btn("StartBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -180), new Vector2(460, 116),
            new Color(0.16f, 0.55f, 0.25f), () => GameManager.I.StartGame());

        // ---- 结算面板 ----
        over = StretchImg("Over", mgo.transform, new Color(0f, 0f, 0f, 0.80f));
        overCG = over.AddComponent<CanvasGroup>();
        overCG.alpha = 0f;
        overCG.blocksRaycasts = false;
        overCG.interactable = false;
        Btn("AgainBtn", over.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(460, 116),
            new Color(0.16f, 0.55f, 0.25f), () => UnityEngine.SceneManagement.SceneManager.LoadScene(0));

        // ---- 设置面板（全屏暗化底 + 中央面板，滑入动画）----
        var settingsRoot = StretchImg("SettingsDim", mgo.transform, new Color(0f, 0f, 0f, 0.5f));
        settingsCG = settingsRoot.AddComponent<CanvasGroup>();
        settingsCG.alpha = 0f;
        settingsCG.blocksRaycasts = false;
        settingsCG.interactable = false;

        settingsPanel = Img("SettingsPanel", settingsRoot.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(900, 760), new Color(0.09f, 0.12f, 0.15f, 0.97f));
        settingsPanelRT = settingsPanel.GetComponent<RectTransform>();
        // 注意 y 符号：pos.y = 540 - 设计y（设计坐标从顶部往下，uGUI 中心锚定 y 向上）
        // 版面（自上而下）：标题条 205 → 帧率 380 → 分辨率 530 → 阴影 680 → 完成 835
        Img("SettingsHeader", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 335), new Vector2(900, 90),
            new Color(0.13f, 0.42f, 0.34f));
        Btn("FpsLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 160), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(0, -1));
        Btn("FpsRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 160), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(0, +1));
        Btn("ResLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 10), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(1, -1));
        Btn("ResRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 10), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(1, +1));
        Btn("ShadowLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, -140), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(2, -1));
        Btn("ShadowRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, -140), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(2, +1));
        Btn("SettingsDoneBtn", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -295), new Vector2(360, 100),
            new Color(0.16f, 0.55f, 0.25f), ToggleSettings);

        // ---- 147 满分提示横幅（金色描边 + 深色底，平时藏在屏幕外）----
        var popup = Img("Popup147", mgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270), new Vector2(1150, 150),
            new Color(1f, 0.84f, 0.35f));
        Img("PopupIn", popup.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1130, 134),
            new Color(0.07f, 0.09f, 0.11f, 0.97f));
        popupRT = popup.GetComponent<RectTransform>();
        popupCG = popup.AddComponent<CanvasGroup>();
        popupCG.alpha = 0f;
        popupCG.blocksRaycasts = false;

        foreach (var g in mgo.GetComponentsInChildren<Graphic>())
            if (uiMat != null) g.material = uiMat;

        UpdateAimLabels();

        Debug.Log("[SNOOKER] UI diag screen=" + Screen.width + "x" + Screen.height +
                  " font=" + (font != null ? font.name : "NULL"));
        Debug.Log("[SNOOKER] UI built");
    }

    // ---------------------------------------------------------------------------------
    // 每帧动画推进：菜单淡入(衔接入场相机) / 结算淡入 / 设置面板滑入滑出 / 消息倒计时
    // ---------------------------------------------------------------------------------
    void Update()
    {
        var gm = GameManager.I;

        // ---- 菜单透明度：入场 3.0s 起淡入 0.8s（与相机俯冲衔接）；开始游戏后快速淡出 ----
        float menuTarget;
        if (menuActiveFlag && gm != null && gm.state == GameManager.State.Menu)
            menuTarget = Mathf.Clamp01((Time.timeSinceLevelLoad - 3.0f) / 0.8f);
        else menuTarget = 0f;
        menuAlpha = Mathf.MoveTowards(menuAlpha, menuTarget, Time.deltaTime / 0.45f);
        if (menuCG != null)
        {
            menuCG.alpha = menuAlpha;
            menuCG.blocksRaycasts = menuAlpha > 0.6f;
            menuCG.interactable = menuAlpha > 0.6f;
        }

        // ---- 结算面板淡入淡出 ----
        float overTarget = overActiveFlag ? 1f : 0f;
        overAlpha = Mathf.MoveTowards(overAlpha, overTarget, Time.deltaTime / 0.3f);
        if (overCG != null)
        {
            overCG.alpha = overAlpha;
            overCG.blocksRaycasts = overAlpha > 0.6f;
            overCG.interactable = overAlpha > 0.6f;
        }

        // ---- 设置面板滑入滑出（easeOutCubic）----
        settingsAnimT = Mathf.MoveTowards(settingsAnimT, settingsOpen ? 1f : 0f,
            Time.deltaTime / (settingsOpen ? 0.35f : 0.25f));
        float e = 1f - Mathf.Pow(1f - settingsAnimT, 3f);            // easeOutCubic
        settingsOffset = (1f - e) * 1300f;                           // 从右侧 1300 设计像素滑入
        settingsAlpha = settingsAnimT;
        if (settingsCG != null)
        {
            settingsCG.alpha = e;
            settingsCG.blocksRaycasts = settingsAnimT > 0.5f;
            settingsCG.interactable = settingsAnimT > 0.5f;
            if (settingsPanelRT != null)
                settingsPanelRT.anchoredPosition = new Vector2(settingsOffset * K(), 0f);
        }

        if (msgTimer > 0f) msgTimer -= Time.deltaTime;

        // ---- 147 横幅动画：0.45s easeOutCubic 弹入 → 停留 → 0.35s 收回 ----
        if (popupTimer > 0f)
        {
            popupTimer -= Time.deltaTime;
            float elapsed = 3.6f - popupTimer;
            float eIn = Mathf.Clamp01(elapsed / 0.45f);
            eIn = 1f - Mathf.Pow(1f - eIn, 3f);              // easeOutCubic 弹入
            float cOut = Mathf.Clamp01(popupTimer / 0.35f);  // 收回进度
            popupAlpha = Mathf.Min(eIn, cOut);
            popupYoff = (1f - eIn) * 760f;                   // 从屏幕底之外(760)滑到目标位
        }
        else { popupAlpha = 0f; popupYoff = 760f; }
        if (popupCG != null)
        {
            popupCG.alpha = popupAlpha;
            if (popupRT != null)
                popupRT.anchoredPosition = new Vector2(0f, (-270f - popupYoff) * K());
        }
    }

    /// 设计→屏幕统一缩放系数（与 CanvasScaler match 0.5 一致）。
    private float K()
    {
        return Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
    }

    // ---------------------------------------------------------------------------------
    // 设置面板开关与选项循环
    // ---------------------------------------------------------------------------------
    void ToggleSettings() { settingsOpen = !settingsOpen; settingsAnimT = Mathf.Clamp01(settingsAnimT); }

    /// kind: 0=帧率 1=分辨率 2=阴影；dir=+1/-1 循环方向。改完立即生效并持久化。
    void CycleSetting(int kind, int dir)
    {
        if (kind == 0)
        {
            GameSettings.FpsIndex = (GameSettings.FpsIndex + dir + GameSettings.FpsOptions.Length) % GameSettings.FpsOptions.Length;
        }
        else if (kind == 1)
        {
            GameSettings.ResIndex = (GameSettings.ResIndex + dir + GameSettings.ResOptions.Length) % GameSettings.ResOptions.Length;
        }
        else
        {
            GameSettings.Shadows = !GameSettings.Shadows;
        }
        GameSettings.Apply();
        GameSettings.Save();
    }

    void ToggleAim()
    {
        cc.aimLineOn = !cc.aimLineOn;
        UpdateAimLabels();
    }

    void UpdateAimLabels()
    {
        string s = "辅助线：" + (cc.aimLineOn ? "开" : "关");
        aimHudText = s;
        aimMenuText = s;
    }

    // ---------------------------------------------------------------------------------
    // 字体加载：优先内嵌字体（DroidSansFallback Apache-2.0），HasCharacter 验证中文字形。
    // ---------------------------------------------------------------------------------
    Font LoadFont()
    {
        string[] candidates = { "Fonts/DroidSansFallback", "Fonts/NotoSansSC", "Fonts/NotoSansCJK-Regular" };
        foreach (string c in candidates)
        {
            Font f = Resources.Load<Font>(c);
            if (f != null && f.HasCharacter('斯'))
            {
                Debug.Log("[SNOOKER] font picked " + c);
                return f;
            }
        }
        string[] names = { "Microsoft YaHei", "Noto Sans CJK SC", "DroidSansFallback", "SimHei" };
        foreach (string n in names)
        {
            Font os = null;
            try { os = Font.CreateDynamicFontFromOSFont(n, 30); } catch { }
            if (os != null && os.HasCharacter('斯'))
            {
                Debug.Log("[SNOOKER] font picked OS " + n);
                return os;
            }
        }
        Debug.Log("[SNOOKER] font fallback LegacyRuntime");
        Font lr = null;
        try { lr = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        return lr;
    }

    // ---------------------------------------------------------------------------------
    // uGUI 构建帮助方法
    // ---------------------------------------------------------------------------------
    private GameObject Img(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = col;
        return go;
    }

    /// 四角拉伸铺满父容器的 Image（全屏遮罩用）。
    private GameObject StretchImg(string name, Transform parent, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = col;
        return go;
    }

    /// 美化版按钮：深色描边底（外扩 10px）+ 主体 + 顶部高光条，三层结构。
    private void Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        Color edge = new Color(bg.r * 0.45f, bg.g * 0.45f, bg.b * 0.45f, 1f);   // 描边=主体色加深
        Img(name + "Edge", parent, anchor, pos, size + new Vector2(10, 10), edge);
        var go = Img(name, parent, anchor, pos, size, bg);
        // 顶部高光条：宽 = 主体宽 - 28，贴主体内侧上沿，营造轻微立体感
        Img(name + "Hi", go.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0, size.y / 2f - 7), new Vector2(size.x - 28, 6), new Color(1f, 1f, 1f, 0.16f));
        var b = go.AddComponent<Button>();
        var colors = b.colors;
        colors.pressedColor = new Color(0.75f, 0.9f, 1f);       // 按下高亮
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(onClick);
    }

    /// 力度滑条（手动搭结构：底框/轨道/橙色填充/白色滑块）。
    private Slider MakeSlider(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Power", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        var bgGo = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.SetParent(rt, false);
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(4, 14); bgRt.offsetMax = new Vector2(-4, -14);
        bgGo.GetComponent<Image>().color = new Color(0.30f, 0.33f, 0.38f, 1f);

        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        var faRt = (RectTransform)fillArea.transform;
        faRt.SetParent(rt, false);
        faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
        faRt.offsetMin = new Vector2(6, 16); faRt.offsetMax = new Vector2(-6, -16);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fRt = (RectTransform)fill.transform;
        fRt.SetParent(faRt, false);
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.sizeDelta = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.95f, 0.65f, 0.1f, 1f);

        var handleArea = new GameObject("HandleArea", typeof(RectTransform));
        var haRt = (RectTransform)handleArea.transform;
        haRt.SetParent(rt, false);
        haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(10, 0); haRt.offsetMax = new Vector2(-10, 0);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var hRt = (RectTransform)handle.transform;
        hRt.SetParent(haRt, false);
        hRt.anchorMin = new Vector2(0.5f, 0f); hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.sizeDelta = new Vector2(26, 0);
        handle.GetComponent<Image>().color = new Color(1f, 0.9f, 0.7f, 1f);

        var s = go.GetComponent<Slider>();
        s.fillRect = fRt;
        s.handleRect = hRt;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f; s.maxValue = 1f;
        return s;
    }

    // =================================================================================
    // 运行时数据更新接口（GameManager 调用）
    // =================================================================================
    /// 刷新记分板：v0.32 顶部两侧改为"单杆分"（金色大字），总分挪到中央信息行。
    public void SetHud(int s0, int s1, int b0, int b1, int cur, string target, int reds)
    {
        var gm = GameManager.I;
        p1Text = gm.names[0];
        p2Text = gm.names[1];
        p1Break = b0.ToString();
        p2Break = b1.ToString();
        centerText = "轮到 " + gm.names[cur] + "   目标: " + target + "   剩余红球: " + reds + "   总分 " + s0 + ":" + s1;
    }

    /// 弹出"147 满分进行中"横幅（pairs = 已完成红黑套数，满分需 15 套）。
    public void Show147(int pairs)
    {
        popupPairsText = pairs + "/15";
        popupTimer = 3.6f;
    }

    public void ShowMsg(string text, float dur)
    {
        msgText = text;
        msgTimer = dur;
    }

    public void SetMsg(string text, float dur) { ShowMsg(text, dur); }

    /// 显隐主菜单（实际淡入淡出由 Update 驱动）。
    public void ShowMenu(bool on) { menuActiveFlag = on; }

    /// 显隐结算面板并写入胜者与比分（淡入由 Update 驱动）。
    public void ShowGameOver(bool on, int winner = 0, int s0 = 0, int s1 = 0)
    {
        if (on)
            overTextStr = GameManager.I.names[winner] + " 获胜！  " + s0 + " : " + s1;
        overActiveFlag = on;
    }

    // =================================================================================
    // IMGUI 文字层
    // =================================================================================
    private Rect CRect(float cx, float cy, float w, float h)
    {
        float k = K();
        return new Rect(Screen.width * 0.5f + (cx - 960) * k - w * k * 0.5f,
                        Screen.height * 0.5f + (cy - 540) * k - h * k * 0.5f,
                        w * k, h * k);
    }

    /// 画一条带投影的文字（先画右下偏移的黑色半透明影子，再画主体）。
    private void DrawLabel(Rect r, string text, int size, Color col, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text)) return;
        float k = K();
        var style = new GUIStyle();
        style.font = font;
        style.fontSize = Mathf.RoundToInt(size * k);
        style.alignment = anchor;
        style.wordWrap = false;

        style.normal.textColor = new Color(0f, 0f, 0f, 0.55f * col.a);   // 投影
        Rect sr = new Rect(r.x + 2, r.y + 2, r.width, r.height);
        GUI.Label(sr, text, style);

        style.normal.textColor = col;                                     // 主体
        GUI.Label(r, text, style);
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (font == null) return;

        // ---- HUD 记分板（v0.32：两侧 = 玩家名 + 金色大字单杆分；总分在中央行）----
        // 注意：CRect 的 cx 是矩形【中心】，左对齐文字的 cx = 左边缘 + w/2，右对齐则 - w/2
        Color breakGold = new Color(1f, 0.84f, 0.30f);
        Color dimGray = new Color(0.72f, 0.75f, 0.78f);
        DrawLabel(CRect(161, 48, 190, 76), p1Text, 30, Color.white, TextAnchor.MiddleLeft);       // 名字：左边缘 66
        DrawLabel(CRect(303, 48, 90, 76), "单杆", 22, dimGray, TextAnchor.MiddleLeft);            // 左边缘 258
        DrawLabel(CRect(447, 48, 190, 76), p1Break, 46, breakGold, TextAnchor.MiddleLeft);        // 左边缘 352
        DrawLabel(CRect(1749, 48, 190, 76), p2Text, 30, Color.white, TextAnchor.MiddleRight);     // 名字：右边缘 1844
        DrawLabel(CRect(1585, 48, 90, 76), "单杆", 22, dimGray, TextAnchor.MiddleRight);          // 右边缘 1630
        DrawLabel(CRect(1435, 48, 190, 76), p2Break, 46, breakGold, TextAnchor.MiddleRight);      // 右边缘 1530
        DrawLabel(CRect(960, 48, 900, 76), centerText, 28, new Color(1f, 0.92f, 0.6f), TextAnchor.MiddleCenter);
        DrawLabel(CRect(960, 160, 1400, 64), msgText, 36, new Color(1f, 0.85f, 0.25f), TextAnchor.MiddleCenter);
        DrawLabel(CRect(1590, 1080 - 158, 300, 44), powerText, 28, Color.white, TextAnchor.MiddleCenter);

        // ---- HUD 按钮文字 ----
        DrawLabel(CRect(1920 - 125, 1080 - 88, 180, 150), "击球", 46, Color.white, TextAnchor.MiddleCenter);
        DrawLabel(CRect(140, 1080 - 92, 240, 62), aimHudText, 28, Color.white, TextAnchor.MiddleCenter);
        DrawLabel(CRect(292, 1080 - 92, 70, 62), "◀", 30, Color.white, TextAnchor.MiddleCenter);
        DrawLabel(CRect(366, 1080 - 92, 70, 62), "▶", 30, Color.white, TextAnchor.MiddleCenter);
        DrawLabel(CRect(140, 1080 - 176, 240, 58), "重新开局", 28, Color.white, TextAnchor.MiddleCenter);
        DrawLabel(CRect(420, 1080 - 176, 240, 58), "设 置", 28, Color.white, TextAnchor.MiddleCenter);

        // ---- 主菜单（文字随菜单整体淡入；设置面板打开时再淡出避免与面板重叠）----
        if (menuAlpha > 0.01f)
        {
            float ma = menuAlpha * (1f - settingsAlpha * 0.95f);   // 设置打开时菜单文字让位
            Color w = Color.white; w.a *= ma;
            Color sub = new Color(0.85f, 0.9f, 1f); sub.a *= ma;
            DrawLabel(CRect(960, 540 - 170, 1200, 120), "双人斯诺克 3D", 76, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 - 62, 1200, 50), "标准斯诺克规则 · 真实物理 · 轮流击球", 26, sub, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 40, 400, 84), aimMenuText, 32, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 180, 460, 116), "开始游戏", 46, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 290, 460, 96), "设 置", 32, w, TextAnchor.MiddleCenter);
        }

        // ---- 结算面板 ----
        if (overAlpha > 0.01f)
        {
            Color w = Color.white; w.a *= overAlpha;
            DrawLabel(CRect(960, 540 - 90, 1400, 220), overTextStr, 56, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 140, 460, 116), "再来一局", 44, w, TextAnchor.MiddleCenter);
        }

        // ---- 设置面板（文字随面板滑入位移 + 淡入）----
        if (settingsAlpha > 0.01f)
        {
            float sox = settingsOffset;                      // 与 uGUI 面板同量位移
            Color w = Color.white; w.a *= settingsAlpha;
            Color gold = new Color(1f, 0.84f, 0.35f); gold.a *= settingsAlpha;
            Color gray = new Color(0.65f, 0.70f, 0.75f); gray.a *= settingsAlpha;

            DrawLabel(CRect(960 + sox, 205, 400, 70), "设 置", 42, gold, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 380, 260, 56), "帧率上限", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 380, 340, 64), GameSettings.FpsText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 380, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 380, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 530, 260, 56), "渲染分辨率", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 530, 340, 64), GameSettings.ResText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 530, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 530, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 680, 260, 56), "画面阴影", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 680, 340, 64), GameSettings.ShadowText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 680, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 680, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(960 + sox, 835, 360, 100), "完 成", 40, w, TextAnchor.MiddleCenter);
        }

        // ---- 147 满分提示横幅（文字随横幅从底部弹入）----
        if (popupAlpha > 0.01f)
        {
            float poy = 810 - popupYoff;                     // 横幅当前设计 y（负值=还在屏幕外）
            Color gold = new Color(1f, 0.84f, 0.30f); gold.a *= popupAlpha;
            Color goldLight = new Color(1f, 0.92f, 0.55f); goldLight.a *= popupAlpha;
            Color w = Color.white; w.a *= popupAlpha;
            DrawLabel(CRect(705, poy, 280, 110), "147", 64, gold, TextAnchor.MiddleRight);
            DrawLabel(CRect(770, poy - 30, 420, 56), "满分进行中", 34, w, TextAnchor.MiddleLeft);
            DrawLabel(CRect(770, poy + 28, 420, 50), "红黑连击 " + popupPairsText, 26, goldLight, TextAnchor.MiddleLeft);
        }
    }
}
