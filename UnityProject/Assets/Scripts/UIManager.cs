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
    private CanvasGroup hudCG;                           // HUD 整体渐变组（v0.34：菜单/结算时淡出）
    private float hudAlpha;                              // HUD 当前透明度（0=隐藏 1=完全显示）

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

    // ---- v0.35：让对手重打 提示（判 Miss 后弹出）----
    private bool replayPrompt;                            // GameManager 请求显示
    private bool replayShown;                             // 已实际显示（按钮已创建）
    private float replayAlpha;                            // 淡入淡出
    private GameObject replayPanel;
    private CanvasGroup replayCG;

    // ---- v0.36：加塞圆盘 ----
    private SpinPad spinPad;                              // 击球点选择器（拖动小圆点选高/低杆与左右塞）

    // ---- v0.38：原神风 UI 配色（深藏青底 + 金色描边/饰件）----
    // 原神按钮的设计语言：深色半透面板、细金描边、顶部微光、四角小菱饰、主按钮带宝石菱。
    private static readonly Color GoldLight = new Color(0.96f, 0.85f, 0.50f);   // 高光金
    private static readonly Color Gold      = new Color(0.80f, 0.65f, 0.30f);   // 主金
    private static readonly Color GoldDark  = new Color(0.47f, 0.37f, 0.16f);   // 描边金
    private static readonly Color Navy      = new Color(0.09f, 0.11f, 0.16f, 0.96f); // 按钮底
    private static readonly Color NavyPanel = new Color(0.07f, 0.09f, 0.13f, 0.97f); // 面板底

    /// 画一个旋转 45° 的小方块（菱形饰件，原风按钮的角饰/宝石）。
    private GameObject Diamond(Transform parent, Vector2 pos, float size, Color col)
    {
        var go = Img("Diamond", parent, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size), col);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return go;
    }

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
        // HUD 整体渐变组（v0.34）：菜单/结算时把 HUD 淡出并停止接收点击 ——
        // 旧版 HUD 的 uGUI 底图会被菜单遮罩压暗，IMGUI 文字却画在遮罩之上（层级矛盾），
        // 而且菜单里还能点到"击球/力度"。
        hudCG = cgo.AddComponent<CanvasGroup>();

        if (Object.FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // 顶部记分板底条 + 双方阵营色块（美化：蓝=玩家1，红=玩家2）
        // v0.34：HUD 位置一律过 Fit() 夹进"可见设计安全区"。硬贴 1920×1080 边缘的写法
        //        在 20:9 机型上顶部记分板被裁、在 4:3 机型上左右两侧按钮被裁。
        //        （分辨率档位只等比改变像素密度、宽高比不变，所以此处算一次即可。）
        Rect vis = VisibleDesignRect();
        float topW = Mathf.Min(1920f, vis.width);              // 顶条宽度自适应可见宽度
        Img("TopPanel", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(0, 485), new Vector2(topW, 96)), new Vector2(topW, 96), new Color(0f, 0f, 0f, 0.55f));
        Img("ChipP1", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-916, 492), new Vector2(28, 28)), new Vector2(28, 28), new Color(0.23f, 0.44f, 0.85f));
        Img("ChipP2", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(916, 492), new Vector2(28, 28)), new Vector2(28, 28), new Color(0.85f, 0.27f, 0.27f));

        // ---- 力度滑条 ----
        // v0.34：宽 300→240、中心 630→600，右端由 780 收到 720，不再被"击球"按钮
        //        （745..925）盖住 35px —— 旧版滑条右端约 12% 拖不到，点那里还会直接出杆。
        power = MakeSlider(cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(600, -448), new Vector2(240, 56)), new Vector2(240, 56));
        power.value = cc.power;
        power.onValueChanged.AddListener(v =>
        {
            cc.power = v;
            powerText = "力度 " + Mathf.RoundToInt(v * 100) + "%";
        });
        powerText = "力度 " + Mathf.RoundToInt(cc.power * 100) + "%";

        // ---- HUD 按钮（美化版：自动加深色描边底 + 顶部高光条）----
        // 位置同样过 Fit()：4:3 机型上最右"击球"与最左"辅助线"按钮原本会被裁掉。
        Btn("ShootBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(835, -452), new Vector2(180, 150)), new Vector2(180, 150),
            new Color(0.16f, 0.55f, 0.25f), cc.BeginStrike, true);
        Btn("AimHudBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-820, -448), new Vector2(240, 62)), new Vector2(240, 62),
            new Color(0.16f, 0.30f, 0.55f), ToggleAim);
        // v0.42：微调步长改为 CueController.NudgeStep（0.00035 rad ≈ 0.02°），
        // 原来是 0.0035（≈0.2°）——长台上按一次偏 12mm，几乎无法对准。详见该常量注释。
        Btn("NudgeL", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-660, -448), new Vector2(70, 62)), new Vector2(70, 62),
            new Color(0.20f, 0.23f, 0.32f), () => cc.Rotate(-CueController.NudgeStep));
        Btn("NudgeR", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-586, -448), new Vector2(70, 62)), new Vector2(70, 62),
            new Color(0.20f, 0.23f, 0.32f), () => cc.Rotate(+CueController.NudgeStep));
        Btn("RestartBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-820, -364), new Vector2(240, 58)), new Vector2(240, 58),
            new Color(0.42f, 0.22f, 0.16f), () => UnityEngine.SceneManagement.SceneManager.LoadScene(0));
        Btn("SettingsHudBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-540, -364), new Vector2(240, 58)), new Vector2(240, 58),
            new Color(0.20f, 0.23f, 0.32f), ToggleSettings);

        // ---- v0.36：加塞圆盘（击球点选择器）----
        // 位置在屏幕右下角"力度滑条/击球按钮"的正上方（设计中心 y=700，占 545~855），
        // 避开下方的力度百分比文字(y≈922)与击球按钮(y≥917)，也避开左侧的重新开局/设置按钮。
        // 拖动盘内小圆点即可选高杆/低杆/左右塞。
        var padPanel = Img("SpinPanel", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(740, -160), new Vector2(250, 310)), new Vector2(250, 310),
            NavyPanel);
        // v0.40 修复：金描边做成面板的**第一个子物体**（渲染在最底），不要像 v0.38 那样
        // 先在 cgo 下建边框、再 SetParent 重排 —— 真机上 Edge 会盖住整个面板（面板变金色、
        // 圆盘子物体全部不可见），而**编辑器里看不出异常**（这正是它一直没被发现的原因）。
        var padEdge = Img("SpinPanelEdge", padPanel.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(258, 318), GoldDark);
        padEdge.transform.SetAsFirstSibling();          // 沉到最底层，让面板本体压住边框
        padPanel.GetComponent<Image>().raycastTarget = false;
        var padGo = new GameObject("SpinPad", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(SpinPad));
        var padRt = (RectTransform)padGo.transform;
        padRt.SetParent(padPanel.transform, false);
        padRt.anchorMin = padRt.anchorMax = new Vector2(0.5f, 0.5f);
        padRt.anchoredPosition = new Vector2(0f, -8f);
        padRt.sizeDelta = Vector2.one * 160f;
        padGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // 外框透明，球面由 SpinPad 自己画
        spinPad = padGo.GetComponent<SpinPad>();
        spinPad.Build(padRt, cc, 75f);

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

        // 金色装饰条（副标题下方左右各一条，中央一枚菱形宝石 —— v0.38 原神风）
        Img("BarL", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(-280, -18), new Vector2(150, 5), Gold);
        Img("BarR", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(280, -18), new Vector2(150, 5), Gold);
        Diamond(menu.transform, new Vector2(0, -18), 18f, Gold);
        Diamond(menu.transform, new Vector2(0, -18), 8f, GoldLight);
        // v0.39：标题上方的"双翼"细金线 + 副标题两端的渐细点，让标题区更像原神的卷轴题头
        Img("TitleWingL", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(-330, 62), new Vector2(210, 3),
            new Color(Gold.r, Gold.g, Gold.b, 0.55f));
        Img("TitleWingR", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 62), new Vector2(210, 3),
            new Color(Gold.r, Gold.g, Gold.b, 0.55f));
        Diamond(menu.transform, new Vector2(-440, 62), 10f, Gold);
        Diamond(menu.transform, new Vector2(440, 62), 10f, Gold);

        // v0.39：HUD 顶栏下沿一条金线（与按钮描边同一金色，统一视觉语言）
        var topRt = cgo.transform.Find("TopPanel") as RectTransform;
        if (topRt != null)
            Img("TopPanelGold", cgo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0, 437), new Vector2(topW, 3), new Color(Gold.r, Gold.g, Gold.b, 0.75f));

        Btn("AimMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(400, 84),
            new Color(0.16f, 0.30f, 0.55f), ToggleAim);
        Btn("SettingsMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -312), new Vector2(460, 96),
            new Color(0.20f, 0.23f, 0.32f), ToggleSettings);
        Btn("StartBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -172), new Vector2(460, 116),
            new Color(0.16f, 0.55f, 0.25f), () => GameManager.I.StartGame(), true);

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
        // 版面（v0.37 四行，自上而下）：标题 205 → 帧率 340 → 分辨率 465 → 阴影 590 →
        // 物理步长 715 → 完成 845。v0.38：标题条改金描边 + 藏青底。
        Img("SettingsHeaderEdge", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 335), new Vector2(900, 90),
            GoldDark);
        Img("SettingsHeader", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 335), new Vector2(892, 82),
            NavyPanel);
        Diamond(settingsRoot.transform, new Vector2(-390, 335), 14f, Gold);
        Diamond(settingsRoot.transform, new Vector2(390, 335), 14f, Gold);
        Btn("FpsLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 200), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(0, -1));
        Btn("FpsRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 200), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(0, +1));
        Btn("ResLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 75), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(1, -1));
        Btn("ResRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 75), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(1, +1));
        Btn("ShadowLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, -50), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(2, -1));
        Btn("ShadowRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, -50), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(2, +1));
        // v0.37：物理步长三档 0.5/1/2ms（Time.fixedDeltaTime，改了立即生效、持久化）
        Btn("StepLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, -175), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(3, -1));
        Btn("StepRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, -175), new Vector2(110, 72),
            new Color(0.20f, 0.23f, 0.32f), () => CycleSetting(3, +1));
        Btn("SettingsDoneBtn", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -305), new Vector2(360, 100),
            new Color(0.16f, 0.55f, 0.25f), ToggleSettings, true);
        // v0.38：行间金色分隔细线（原神设置面板的排版语言）
        Img("Div1", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 137), new Vector2(780, 2), new Color(Gold.r, Gold.g, Gold.b, 0.35f));
        Img("Div2", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(780, 2), new Color(Gold.r, Gold.g, Gold.b, 0.35f));
        Img("Div3", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -113), new Vector2(780, 2), new Color(Gold.r, Gold.g, Gold.b, 0.35f));
        Img("Div4", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -238), new Vector2(780, 2), new Color(Gold.r, Gold.g, Gold.b, 0.35f));

        // ---- 147 满分提示横幅（金描边 + 藏青底，平时藏在屏幕外）----
        var popup = Img("Popup147", mgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270), new Vector2(1150, 150),
            Gold);
        Img("PopupIn", popup.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1130, 134),
            NavyPanel);
        popupRT = popup.GetComponent<RectTransform>();
        popupCG = popup.AddComponent<CanvasGroup>();
        popupCG.alpha = 0f;
        popupCG.blocksRaycasts = false;

        // ---- v0.35：让对手重打 提示框（判 Miss 后显示，Rule 11(b)）----
        // 位置在屏幕中下方（不遮挡球堆与瞄准区），两个按钮左右并排
        replayPanel = Img("ReplayPanel", mgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(1160, 210),
            Gold);                                                         // 金色描边
        Img("ReplayIn", replayPanel.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1140, 190),
            NavyPanel);
        Btn("ReplayYes", replayPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(-290, -52), new Vector2(520, 84),
            new Color(0.16f, 0.45f, 0.62f), ChooseReplay);
        Btn("ReplayNo", replayPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(290, -52), new Vector2(520, 84),
            new Color(0.22f, 0.26f, 0.34f), DismissReplay);
        replayCG = replayPanel.AddComponent<CanvasGroup>();
        replayCG.alpha = 0f;
        replayCG.blocksRaycasts = false;
        replayCG.interactable = false;

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
                settingsPanelRT.anchoredPosition = new Vector2(settingsOffset, 0f);
        }

        if (msgTimer > 0f) msgTimer -= Time.deltaTime;

        // ---- HUD 显隐（v0.34）：只在 Aiming/Rolling 显示 ----
        // 菜单期与结算期淡出，既消除"文字亮、按钮暗"的层级矛盾，也防止在菜单里点到击球/力度。
        bool hudWant = gm != null && (gm.state == GameManager.State.Aiming || gm.state == GameManager.State.Rolling);
        hudAlpha = Mathf.MoveTowards(hudAlpha, hudWant ? 1f : 0f, Time.deltaTime / 0.25f);
        if (hudCG != null)
        {
            hudCG.alpha = hudAlpha;
            hudCG.blocksRaycasts = hudAlpha > 0.5f;
            hudCG.interactable = hudAlpha > 0.5f;
        }

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

        // ---- v0.35：让对手重打 提示淡入淡出 ----
        float rTarget = replayPrompt ? 1f : 0f;
        replayAlpha = Mathf.MoveTowards(replayAlpha, rTarget, Time.deltaTime / 0.25f);
        if (replayCG != null)
        {
            replayCG.alpha = replayAlpha;
            replayCG.blocksRaycasts = replayAlpha > 0.6f;
            replayCG.interactable = replayAlpha > 0.6f;
        }
    }

    /// 设计→屏幕统一缩放系数（与 CanvasScaler match 0.5 一致）。
    private float K()
    {
        return Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
    }

    /// <summary>
    /// 当前"可见的设计坐标安全区"（uGUI 约定：原点在屏幕中心，x 右为正、y 上为正）。
    /// CanvasScaler(match 0.5) 把设计像素线性放大 K() 倍到屏幕，因此
    ///   可见范围 = 屏幕像素 / K()；再用 Screen.safeArea 扣掉刘海/挖孔，
    /// 并计入安全区中心相对屏幕中心的偏移（横屏刘海机左右不对称时用得上）。
    /// 16:9 → ±960 / ±540；20:9 纵向只剩 ±483、4:3 横向只剩 ±831 ——
    /// HUD 若硬贴 1920×1080 的边缘就必然被裁掉（v0.33 的问题）。
    /// </summary>
    private Rect VisibleDesignRect()
    {
        float k = Mathf.Max(0.0001f, K());
        Rect sa = Screen.safeArea;
        if (sa.width <= 0f || sa.height <= 0f) sa = new Rect(0, 0, Screen.width, Screen.height);
        float halfW = sa.width * 0.5f / k;
        float halfH = sa.height * 0.5f / k;
        float cx = (sa.center.x - Screen.width * 0.5f) / k;
        float cy = (sa.center.y - Screen.height * 0.5f) / k;
        return new Rect(cx - halfW, cy - halfH, halfW * 2f, halfH * 2f);
    }

    /// 把一个【中心锚定】的 HUD 元素位置夹进可见安全区（margin = 设计像素留白）。
    /// 元素比可见区还大时退化为居中，避免 Clamp 出现 min > max。
    private Vector2 Fit(Vector2 pos, Vector2 size, float margin = 12f)
    {
        Rect v = VisibleDesignRect();
        float hx = size.x * 0.5f + margin, hy = size.y * 0.5f + margin;
        float minX = v.xMin + hx, maxX = v.xMax - hx;
        float minY = v.yMin + hy, maxY = v.yMax - hy;
        pos.x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : v.center.x;
        pos.y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : v.center.y;
        return pos;
    }

    /// IMGUI 版 Fit：输入输出都是设计坐标（中心 960/540、y 向下）。
    /// 与 uGUI 的 Fit 走同一套夹取结果，保证"按钮底图"与"IMGUI 文字"永远重合。
    private Rect FittedRect(float cx, float cy, float w, float h, float margin = 12f)
    {
        Vector2 p = Fit(new Vector2(cx - 960f, 540f - cy), new Vector2(w, h), margin);
        return CRect(p.x + 960f, 540f - p.y, w, h);
    }

    // ---------------------------------------------------------------------------------
    // 设置面板开关与选项循环
    // ---------------------------------------------------------------------------------
    void ToggleSettings() { settingsOpen = !settingsOpen; settingsAnimT = Mathf.Clamp01(settingsAnimT); }

    /// kind: 0=帧率 1=分辨率 2=阴影 3=物理步长(v0.37)；dir=+1/-1 循环方向。改完立即生效并持久化。
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
        else if (kind == 3)
        {
            GameSettings.StepIndex = (GameSettings.StepIndex + dir + GameSettings.StepOptions.Length) % GameSettings.StepOptions.Length;
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

    /// 美化版按钮（v0.38 原神风）：金色细描边 + 深藏青渐变底 + 四角菱形饰钉。
    /// primary=true 时左侧再加一枚"宝石菱"（外金内白），用于主操作按钮（开始游戏/击球/完成）。
    private void Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        Btn(name, parent, anchor, pos, size, bg, onClick, false);
    }

    private void Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityEngine.Events.UnityAction onClick, bool primary)
    {
        Img(name + "Edge", parent, anchor, pos, size + new Vector2(8, 8), GoldDark);   // 金描边
        var go = Img(name, parent, anchor, pos, size, Navy);                            // 藏青底
        // 渐变：顶部微光条 + 底部阴影条（模拟原神按钮的上亮下暗）
        Img(name + "Sheen", go.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0, size.y * 0.36f), new Vector2(size.x - 8, size.y * 0.26f), new Color(1f, 1f, 1f, 0.07f));
        Img(name + "Shade", go.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0, -size.y * 0.37f), new Vector2(size.x - 8, size.y * 0.24f), new Color(0f, 0f, 0f, 0.22f));
        // 四角菱形饰钉
        float dx = size.x * 0.5f - 10f, dy = size.y * 0.5f - 10f;
        Diamond(go.transform, new Vector2(-dx,  dy), 9f, Gold);
        Diamond(go.transform, new Vector2( dx,  dy), 9f, Gold);
        Diamond(go.transform, new Vector2(-dx, -dy), 9f, Gold);
        Diamond(go.transform, new Vector2( dx, -dy), 9f, Gold);
        // 主操作按钮：左侧宝石菱（外金内白芯）
        if (primary)
        {
            Diamond(go.transform, new Vector2(-size.x * 0.5f + 24f, 0f), 16f, Gold);
            Diamond(go.transform, new Vector2(-size.x * 0.5f + 24f, 0f), 7f, GoldLight);
        }
        var b = go.AddComponent<Button>();
        var colors = b.colors;
        colors.pressedColor = new Color(1.0f, 0.9f, 0.55f);       // 按下泛金
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(onClick);
    }

    /// 力度滑条（v0.38 原神风：金描边轨道 + 金色填充 + 菱形手柄）。
    private Slider MakeSlider(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Power", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = GoldDark;               // 外框=描边金

        var bgGo = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.SetParent(rt, false);
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(3, 13); bgRt.offsetMax = new Vector2(-3, -13);
        bgGo.GetComponent<Image>().color = Navy;                 // 轨道=藏青

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
        fill.GetComponent<Image>().color = Gold;                 // 填充=主金

        var handleArea = new GameObject("HandleArea", typeof(RectTransform));
        var haRt = (RectTransform)handleArea.transform;
        haRt.SetParent(rt, false);
        haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(10, 0); haRt.offsetMax = new Vector2(-10, 0);

        // 手柄本体透明（Slider 需要一个 handleRect），视觉用菱形宝石：外金内白
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var hRt = (RectTransform)handle.transform;
        hRt.SetParent(haRt, false);
        hRt.anchorMin = new Vector2(0.5f, 0f); hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.sizeDelta = new Vector2(30, 0);
        handle.GetComponent<Image>().color = Color.clear;
        Diamond(handle.transform, Vector2.zero, 34f, Gold);
        Diamond(handle.transform, Vector2.zero, 15f, GoldLight);

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

    /// <summary>
    /// v0.36：把加塞复位到中杆。换手 / 开新局时调用——
    /// 否则上一杆的塞会一直留着，玩家会打出自己没想要的杆法。
    /// </summary>
    public void ResetSpin() { if (spinPad != null) spinPad.ResetSpin(); }

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
    // v0.35：犯规与未击到的"让对手重打"选项（Rule 11(b)）
    //   UI 表现：判 Miss 后在屏幕中下方弹出一个黄框提示 + 两个按钮
    //     左【让对手重打】→ GameManager.RequestReplay()（击球权交回犯规方，球位不动）
    //     右【我自己打】  → 仅关闭提示（接台方按当前球位正常击球）
    // =================================================================================
    public void ShowReplayOption()
    {
        replayPrompt = true;                             // Update 里驱动淡入
    }

    /// 玩家选择"自己打"：只收起提示。
    void DismissReplay()
    {
        replayPrompt = false;
        GameManager.I.DeclineFreeBall();                 // 顺带放弃自由球资格（若同时存在）
        replayShown = false;
    }

    /// 玩家选择"让对手重打"。
    void ChooseReplay()
    {
        replayPrompt = false;
        replayShown = false;
        GameManager.I.RequestReplay();
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

    /// HUD 文字随 HUD 整体淡出（与 uGUI 侧 CanvasGroup 的 alpha 同步）。
    private Color Fade(Color c) { c.a *= hudAlpha; return c; }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (font == null) return;

        // ---- HUD 记分板（v0.32：两侧 = 玩家名 + 金色大字单杆分；总分在中央行）----
        // v0.34：① 仅在 Aiming/Rolling 显示，不再浮在菜单/结算遮罩之上；
        //        ② 坐标用 FittedRect（= CRect + 可见安全区夹取），与 uGUI 按钮同一套算法，
        //           保证 20:9 / 4:3 等非 16:9 机型上文字与底图一起被夹住、不会各自出屏。
        // 注意：cx 是矩形【中心】，左对齐文字的 cx = 左边缘 + w/2，右对齐则 - w/2
        if (hudAlpha > 0.01f)
        {
            Color breakGold = Fade(new Color(1f, 0.84f, 0.30f));
            Color dimGray = Fade(new Color(0.72f, 0.75f, 0.78f));
            DrawLabel(FittedRect(161, 48, 190, 76), p1Text, 30, Fade(Color.white), TextAnchor.MiddleLeft);       // 名字：左边缘 66
            DrawLabel(FittedRect(303, 48, 90, 76), "单杆", 22, dimGray, TextAnchor.MiddleLeft);                    // 左边缘 258
            DrawLabel(FittedRect(447, 48, 190, 76), p1Break, 46, breakGold, TextAnchor.MiddleLeft);                // 左边缘 352
            DrawLabel(FittedRect(1749, 48, 190, 76), p2Text, 30, Fade(Color.white), TextAnchor.MiddleRight);        // 名字：右边缘 1844
            DrawLabel(FittedRect(1585, 48, 90, 76), "单杆", 22, dimGray, TextAnchor.MiddleRight);                  // 右边缘 1630
            DrawLabel(FittedRect(1435, 48, 190, 76), p2Break, 46, breakGold, TextAnchor.MiddleRight);              // 右边缘 1530
            DrawLabel(FittedRect(960, 48, 900, 76), centerText, 28, Fade(new Color(1f, 0.92f, 0.6f)), TextAnchor.MiddleCenter);
            // v0.38：设置面板打开时不再画"XX 击球"提示——IMGUI 永远画在 uGUI 之上，
            // 会穿透设置面板标题条（真机截图确认）。同理下方"球在手"提示也受此保护。
            if (settingsAlpha < 0.4f)
                DrawLabel(FittedRect(960, 160, 1400, 64), msgText, 36, Fade(new Color(1f, 0.85f, 0.25f)), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(1560, 922, 300, 44), powerText, 28, Fade(Color.white), TextAnchor.MiddleCenter);

            // ---- HUD 按钮文字（与上面 Btn/Fit 的位置逐一对齐）----
            DrawLabel(FittedRect(1920 - 125, 1080 - 88, 180, 150), "击球", 46, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(140, 1080 - 92, 240, 62), aimHudText, 28, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(300, 1080 - 92, 70, 62), "◀", 30, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(374, 1080 - 92, 70, 62), "▶", 30, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(140, 1080 - 176, 240, 58), "重新开局", 28, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(420, 1080 - 176, 240, 58), "设 置", 28, Fade(Color.white), TextAnchor.MiddleCenter);

            // ---- v0.36：加塞圆盘文字（与 uGUI 面板逐像素对齐）----
            // 面板中心设计 y=700、尺寸 250×310；圆盘中心设计 y=708、半径 75
            DrawLabel(FittedRect(1700, 595, 240, 40), "击球点", 24, Fade(new Color(0.80f, 0.84f, 0.90f)), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(1700, 805, 240, 40), spinPad != null ? spinPad.Describe() : "中杆", 26, Fade(new Color(1f, 0.88f, 0.5f)), TextAnchor.MiddleCenter);

            // ---- v0.36："球在手"提示（开球前 / 白球落袋后可在 D 区内拖动白球）----
            var gmx = GameManager.I;
            if (gmx != null && gmx.cueInHand && settingsAlpha < 0.4f)
                DrawLabel(FittedRect(960, 300, 1100, 52), "球在手：拖动白球可在开球区 D 内自由摆放", 30,
                    Fade(new Color(0.55f, 0.95f, 0.65f)), TextAnchor.MiddleCenter);
        }

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

            DrawLabel(CRect(700 + sox, 340, 260, 56), "帧率上限", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 340, 340, 64), GameSettings.FpsText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 340, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 340, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 465, 260, 56), "渲染分辨率", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 465, 340, 64), GameSettings.ResText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 465, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 465, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 590, 260, 56), "画面阴影", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 590, 340, 64), GameSettings.ShadowText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 590, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 590, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(700 + sox, 715, 260, 56), "物理步长", 30, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 715, 340, 64), GameSettings.StepText, 40, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 715, 110, 72), "◀", 30, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 715, 110, 72), "▶", 30, w, TextAnchor.MiddleCenter);

            DrawLabel(CRect(960 + sox, 845, 360, 100), "完 成", 40, w, TextAnchor.MiddleCenter);
        }

        // ---- 147 满分提示横幅（文字随横幅从底部弹入）----
        if (popupAlpha > 0.01f)
        {
            float poy = 810 + popupYoff;                     // 横幅当前设计 y（越大越靠屏幕下方，从底部升上来）
            // 注：uGUI 底图在屏幕中心锚定的 y=-270（y 轴向上），换算成 IMGUI 的设计 y（向下）
            //     正是 540+270=810；popupYoff 从 760 归零，所以底图与文字同向从下往上升。
            //     v0.33 这里写成 810-popupYoff，底图往上、文字却往下，动画全程错位数百像素。
            Color gold = new Color(1f, 0.84f, 0.30f); gold.a *= popupAlpha;
            Color goldLight = new Color(1f, 0.92f, 0.55f); goldLight.a *= popupAlpha;
            Color w = Color.white; w.a *= popupAlpha;
            DrawLabel(CRect(705, poy, 280, 110), "147", 64, gold, TextAnchor.MiddleRight);
            DrawLabel(CRect(770, poy - 30, 420, 56), "满分进行中", 34, w, TextAnchor.MiddleLeft);
            DrawLabel(CRect(770, poy + 28, 420, 50), "红黑连击 " + popupPairsText, 26, goldLight, TextAnchor.MiddleLeft);
        }

        // ---- v0.35：让对手重打 提示文字（Rule 11(b) 犯规与未击到）----
        // uGUI 底图中心锚定 y=-300 → IMGUI 设计 y = 540+300 = 840
        if (replayAlpha > 0.01f)
        {
            Color gold = new Color(1f, 0.84f, 0.30f); gold.a *= replayAlpha;
            Color w = Color.white; w.a *= replayAlpha;
            Color sub = new Color(0.80f, 0.85f, 0.90f); sub.a *= replayAlpha;
            DrawLabel(CRect(960, 792, 1100, 56), "对方犯规且未击中球（Miss）", 32, gold, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 836, 1100, 44), "规则允许你要求对方从当前球位重打", 24, sub, TextAnchor.MiddleCenter);
            DrawLabel(CRect(670, 888, 520, 84), "让对手重打", 34, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1250, 888, 520, 84), "我自己打", 34, w, TextAnchor.MiddleCenter);
        }

        // ---- v0.35：自由球 / 指定彩球 状态提示（HUD 中央行下方）----
        // v0.38：设置面板打开时隐藏（IMGUI 在 uGUI 之上，否则会穿透面板）
        var gm = GameManager.I;
        if (gm != null && gm.state == GameManager.State.Aiming && settingsAlpha < 0.4f)
        {
            if (gm.freeBallActive)
                DrawLabel(CRect(960, 240, 900, 52), "自由球：可指定任意一颗球作为球 on",
                    30, new Color(0.45f, 0.95f, 0.60f), TextAnchor.MiddleCenter);
            else if (gm.freeColorPending || gm.colorsPhase)
            {
                string nom = gm.nominatedSet
                    ? "已指定 " + G.CnName(gm.nominatedColor)
                    : "请用准线瞄准要打的彩球以指定";
                DrawLabel(CRect(960, 240, 900, 46), nom, 24,
                    gm.nominatedSet ? new Color(0.95f, 0.88f, 0.55f) : new Color(0.70f, 0.74f, 0.80f),
                    TextAnchor.MiddleCenter);
            }
        }
    }
}
