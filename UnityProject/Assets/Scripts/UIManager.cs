// =====================================================================================
// UIManager.cs —— 全部游戏界面：HUD 记分板 / 主菜单 / 设置面板 / 结算 / 力度滑条 / 按钮
//
// 渲染策略：
//   - 图形元素（面板、按钮、滑条）用 uGUI；文字统一用 IMGUI（OnGUI）绘制——
//     uGUI legacy Text 依赖动态字体图集，部分安卓机会整片空白，IMGUI 实测稳定。
//   - 文字坐标用 CRect()：屏幕中心 + (设计坐标-设计中心)×k，与 uGUI 中心锚定精确对齐。
//
// 视觉风格（v0.44 重做）：iOS「液态玻璃」——
//   - 高饱和但偏淡的系统色（iOS 蓝/绿/橙），以半透明玻璃为主体；
//   - 全部按钮为胶囊形（UIGlass 顶点网格，零贴图，见该文件说明）；
//   - 玻璃与背景交互：菜单期大面板用 LiquidGlass.shader 抓背景模糊，
//     游戏内 HUD 只用半透明（GrabPass 的全屏拷贝不适合 60~144fps 的对局中）；
//   - 中央提示配玻璃胶囊底（iOS 通知条），文字仍走 IMGUI；
//   - 按钮触感反馈见 Haptics.cs（按下轻点、主按钮确认重点）。
//
// 设置面板：帧率上限（60/90/120/144）/ 渲染分辨率（50%/75%/100%）/ 画面阴影（开/关）/
//           物理步长（0.5/1/2ms）/ 音效开/关（v0.46），
//           改动即通过 GameSettings.Apply() 生效并持久化（PlayerPrefs）。
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
    private string centerText = "", msgText = "";
    private string aimHudText = "", aimMenuText = "", powerText = "";
    private float msgTimer;

    // ---- v0.51：胜利结算动画（终局取代旧版"一行字淡入"）----
    // 时间线（overT 为 ShowGameOver(true) 起的秒数）：
    //   0.00 背景磨砂淡入(overCG 现有 0.3s) → 0.10 胜者卡片 Q 弹入场(弹簧过冲 ~4%)
    //   → 0.40 比分跳数 1.1s(easeOutCubic) → 0.45 彩纸起爆 1.6s → 0.55 "再来一局"淡入
    private float overT;                                  // 结算动画时钟
    private RectTransform overCardRT;                     // 胜者卡片（弹簧缩放）
    private CanvasGroup overCardCG;                       // 卡片淡入
    private CanvasGroup againCG;                          // "再来一局"按钮延迟淡入
    private UIConfetti confetti;                          // 彩纸层（零贴图顶点网格）
    private bool confettiLaunched;
    private int overWinner, overS0, overS1, overMB0, overMB1;  // 跳数动画的终点数据
    private int shownS0, shownS1;                         // 比分跳数当前显示值（IMGUI 同步）
    private float overCardScale = 0.72f, overCardVel;     // 卡片弹簧状态（位移/速度）

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

    // ---- v0.44：中央提示的玻璃胶囊底（iOS 通知条风格；文字仍在 IMGUI 层）----
    private CanvasGroup msgPillCG, cueHandPillCG, ballOnPillCG;
    private UIGlass ballOnPill;                           // 指定彩球/自由球共用，随状态换色

    // ---- v0.44：iOS 液态玻璃配色（高饱和但偏淡的系统色，以透明为主体）----
    // 菜单期大面板：淡色磨砂玻璃——磨砂底不透明度高，模糊后的彩色场景从玻璃里透出
    // 才有"液态玻璃"感（面板 alpha 太低时清晰场景直接穿透，磨砂感会消失）；
    // 游戏内 HUD 与控件：真半透明浅玻璃 + 墨色字；主操作 = 系统绿胶囊。
    private static readonly Color GlassSheet   = new Color(0.74f, 0.79f, 0.88f, 0.58f); // 菜单期淡磨砂底
    private static readonly Color GlassPanel   = new Color(0.72f, 0.78f, 0.88f, 0.60f); // 设置面板磨砂
    private static readonly Color GlassWhite   = new Color(1f, 1f, 1f, 0.24f);          // HUD 顶条
    private static readonly Color GlassNeutral = new Color(1f, 1f, 1f, 0.28f);          // 次级按钮
    private static readonly Color GlassStrong  = new Color(1f, 1f, 1f, 0.36f);          // 提示胶囊/弹层
    private static readonly Color GlassGreen   = new Color(0.22f, 0.72f, 0.42f, 0.44f); // 主操作（iOS 绿）
    private static readonly Color GlassBlue    = new Color(0.28f, 0.58f, 1f, 0.42f);    // 强调操作（iOS 蓝）
    private static readonly Color MintPill     = new Color(0.74f, 0.95f, 0.80f, 0.34f); // "球在手/自由球"薄荷胶囊
    private static readonly Color ChipBlue     = new Color(0.25f, 0.62f, 1f, 0.46f);    // 玩家1 阵营点
    private static readonly Color ChipRed      = new Color(1f, 0.36f, 0.33f, 0.46f);    // 玩家2 阵营点
    private static readonly Color Ink          = new Color(0.08f, 0.09f, 0.11f);        // 主文字（浅玻璃上）
    private static readonly Color Ink2         = new Color(0.34f, 0.36f, 0.42f, 0.85f); // 次级文字
    private static readonly Color AccentOrange = new Color(1f, 0.58f, 0.0f);            // 单杆分/147（iOS 橙）
    private static readonly Color Rim          = new Color(1f, 1f, 1f, 0.55f);          // 玻璃边缘高光环
    private static readonly Color Hairline     = new Color(1f, 1f, 1f, 0.30f);          // 分隔细线
    private static readonly Color TextRed      = new Color(0.86f, 0.27f, 0.24f);        // 重新开局（iOS 红）

    // ---- 字号规范（v0.44）：全部 IMGUI 文字引用下列常量，不再散落字面量。----
    // 起因：此前各处手写 int 字号，同类中文出现了大小不一（如加塞盘"击球点"24 vs"中杆"26、
    // 中央提示 36/30/24 三种并存、记分板"单杆"22 夹在 30 的玩家名中间）。
    // 值为 1920×1080 设计像素，DrawLabel 内统一乘 K() 缩放到实际分辨率。
    private const int FontTitle   = 76;  // 主菜单大标题
    private const int FontBanner  = 64;  // 147 横幅"147"大数字 + 结算卡比分跳数
    private const int FontOver    = 68;  // 结算卡胜者标题（v0.51 从 56 提到 68，配卡片版面）
    private const int FontPrimary = 46;  // 主操作按钮（击球/开始游戏/再来一局）+ 记分板单杆分大数字
    private const int FontHead    = 42;  // 设置面板标题
    private const int FontVal     = 40;  // 设置面板选项值与"完成"按钮
    private const int FontLayer   = 34;  // 弹层主行：147 横幅"满分进行中"、Miss 弹窗按钮
    private const int FontMenuBtn = 32;  // 主菜单按钮文字（按钮比 HUD 大一号，文字等比放大）
    private const int FontMsg     = 30;  // 屏幕中央提示（开球/犯规/球在手/自由球/指定彩球/Miss 标题）与设置行标签
    private const int FontArrow   = 30;  // ◀ ▶ 方向符号（HUD 与设置面板共用）
    private const int FontBtn     = 28;  // HUD 常规文字（玩家名/单杆标签/中央行/力度/各按钮）
    private const int FontSub     = 26;  // 次级行（菜单副标题/加塞盘两行/147"红黑连击"/Miss 副行）

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

        // 顶部记分板底条（iOS 导航栏式玻璃条）+ 双方阵营色点（iOS 蓝 / iOS 红，圆形）
        // v0.34：HUD 位置一律过 Fit() 夹进"可见设计安全区"。硬贴 1920×1080 边缘的写法
        //        在 20:9 机型上顶部记分板被裁、在 4:3 机型上左右两侧按钮被裁。
        //        （分辨率档位只等比改变像素密度、宽高比不变，所以此处算一次即可。）
        Rect vis = VisibleDesignRect();
        float topW = Mathf.Min(1920f, vis.width);              // 顶条宽度自适应可见宽度
        // v0.48：顶条由直角改圆角（28 与加塞盘面板一致），玻璃边缘沿圆角走更贴合 iOS 语言
        var topGlass = UIGlass.Add(cgo.transform, "TopPanel", new Vector2(0.5f, 0.5f),
            Fit(new Vector2(0, 485), new Vector2(topW, 96)), new Vector2(topW, 96), GlassWhite, 28f);
        topGlass.UseRefraction(1.5f, 12f, 1.1f, 0.9f, 0.6f, 0.55f);   // 顶条：实时折射+轻高光
        var chipP1 = UIGlass.Add(cgo.transform, "ChipP1", new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-916, 492), new Vector2(28, 28)), new Vector2(28, 28), ChipBlue, 0f);
        chipP1.shape = UIGlass.Shape.Circle;
        chipP1.UseRefraction(1f, 10f, 1.3f, 1.0f, 0.6f, 0.55f);
        chipP1.raycastTarget = false;
        var chipP2 = UIGlass.Add(cgo.transform, "ChipP2", new Vector2(0.5f, 0.5f),
            Fit(new Vector2(916, 492), new Vector2(28, 28)), new Vector2(28, 28), ChipRed, 0f);
        chipP2.shape = UIGlass.Shape.Circle;
        chipP2.UseRefraction(1f, 10f, 1.3f, 1.0f, 0.6f, 0.55f);
        chipP2.raycastTarget = false;

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
            GlassGreen, cc.BeginStrike, true);
        Btn("AimHudBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-820, -448), new Vector2(240, 62)), new Vector2(240, 62),
            GlassNeutral, ToggleAim);
        // v0.42：微调步长改为 CueController.NudgeStep（0.00035 rad ≈ 0.02°），
        // 原来是 0.0035（≈0.2°）——长台上按一次偏 12mm，几乎无法对准。详见该常量注释。
        Btn("NudgeL", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-660, -448), new Vector2(70, 62)), new Vector2(70, 62),
            GlassNeutral, () => cc.Rotate(-CueController.NudgeStep));
        Btn("NudgeR", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-586, -448), new Vector2(70, 62)), new Vector2(70, 62),
            GlassNeutral, () => cc.Rotate(+CueController.NudgeStep));
        Btn("RestartBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-820, -364), new Vector2(240, 58)), new Vector2(240, 58),
            GlassNeutral, () => UnityEngine.SceneManagement.SceneManager.LoadScene(0));
        Btn("SettingsHudBtn", cgo.transform, new Vector2(0.5f, 0.5f),
            Fit(new Vector2(-540, -364), new Vector2(240, 58)), new Vector2(240, 58),
            GlassNeutral, ToggleSettings);

        // ---- v0.36：加塞圆盘（击球点选择器）----
        // 位置在屏幕右下角"力度滑条/击球按钮"的正上方（设计中心 y=700，占 545~855），
        // 避开下方的力度百分比文字(y≈922)与击球按钮(y≥917)，也避开左侧的重新开局/设置按钮。
        // 拖动盘内小圆点即可选高杆/低杆/左右塞。
        // v0.44：面板=圆角玻璃 + 亮色描边环； SpinPad 自己画的"球面"也改成圆形玻璃。
        var padPanel = UIGlass.Add(cgo.transform, "SpinPanel", new Vector2(0.5f, 0.5f),
            Fit(new Vector2(740, -160), new Vector2(250, 310)), new Vector2(250, 310),
            GlassWhite, 28f);
        var padRim = UIGlass.Add(padPanel.transform, "SpinPanelRim", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(256, 316), Rim, 30.5f);
        padRim.shape = UIGlass.Shape.Ring;
        padRim.rimWidth = 2.2f;
        padRim.raycastTarget = false;
        padPanel.UseRefraction(1.5f, 14f, 1.3f, 0.9f, 0.6f, 0.5f);
        padPanel.raycastTarget = false;
        padPanel.AttachShadow(12f, -6f, 0.20f, 16f);        // v0.47：加塞圆盘面板软投影
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

        // ---- 主菜单（深色磨砂玻璃底 + CanvasGroup 淡入淡出；LiquidGlass 抓背景模糊）----
        menu = UIGlass.Stretch(mgo.transform, "Menu", GlassSheet, true, 16f, 0.12f, 0.10f, 0.15f).gameObject;
        menuCG = menu.AddComponent<CanvasGroup>();
        menuCG.alpha = 0f;                                   // 入场动画期间不可见
        menuCG.blocksRaycasts = false;
        menuCG.interactable = false;

        // v0.44：HUD 顶栏下沿一条发丝分隔线（原为金线；玻璃语言下用半透白）
        var topRt = cgo.transform.Find("TopPanel") as RectTransform;
        if (topRt != null)
            Img("TopPanelHair", cgo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0, 437), new Vector2(topW, 3), Hairline);

        // ---- v0.44：中央提示的玻璃胶囊底（iOS 通知条；文字在 IMGUI 层，胶囊随文字显隐）----
        // 位置与 OnGUI 里的 msgText(设计y=160)/球在手(300)/指定彩球(240) 一一对应：
        // uGUI 中心锚定 y 向上 → pos.y = 540 - 设计y。
        msgPillCG = MakePill("MsgPill", cgo.transform, new Vector2(0, 380), new Vector2(1400, 64), GlassStrong);
        cueHandPillCG = MakePill("CueHandPill", cgo.transform, new Vector2(0, 240), new Vector2(1100, 52), MintPill);
        ballOnPillCG = MakePill("BallOnPill", cgo.transform, new Vector2(0, 300), new Vector2(940, 52), GlassStrong);
        ballOnPill = ballOnPillCG.GetComponent<UIGlass>();
        // 力度百分比文字的胶囊底（常显：随 HUD 整体 CanvasGroup 一起淡出，无需单独驱动）
        var powerPillCG = MakePill("PowerPill", cgo.transform, new Vector2(600, -382), new Vector2(310, 46), GlassStrong);
        powerPillCG.alpha = 1f;
        powerPillCG.GetComponent<UIGlass>().AttachShadow(10f, -5f, 0.18f, 14f);   // v0.47：常显胶囊投影

        Btn("AimMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(400, 84),
            GlassNeutral, ToggleAim);
        Btn("SettingsMenuBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -312), new Vector2(460, 96),
            GlassNeutral, ToggleSettings);
        Btn("StartBtn", menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -172), new Vector2(460, 116),
            GlassGreen, () => GameManager.I.StartGame(), true);

        // ---- 结算面板（深色磨砂玻璃底）----
        over = UIGlass.Stretch(mgo.transform, "Over", GlassSheet, true, 16f, 0.12f, 0.10f, 0.15f).gameObject;
        overCG = over.AddComponent<CanvasGroup>();
        overCG.alpha = 0f;
        overCG.blocksRaycasts = false;
        overCG.interactable = false;

        // v0.51：胜者卡片（结算动画主角）——Q 弹入场 + 软投影 + 高光环，语言与设置面板一致
        // 设计版面（y 从顶往下）：卡片中心 408、980×456（180~636）；标题 296 → 比分 470 →
        // 名字 538 → 最高单杆 596；"再来一局"按钮 720。IMGUI 文字坐标在 OnGUI 一一对应。
        var overCard = UIGlass.Add(over.transform, "OverCard", new Vector2(0.5f, 0.5f),
            new Vector2(0, 540 - 408), new Vector2(980, 456), GlassPanel, 40f);
        overCard.UseBlur(16f, 0.14f, 0.10f, 0.15f);          // 大面板磨砂预设：抓背景模糊
        overCard.AttachShadow(16f, -8f, 0.24f, 20f);         // 大卡片软投影
        overCardRT = overCard.GetComponent<RectTransform>();
        var cardRim = UIGlass.Add(overCard.transform, "OverCardRim", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(988, 464), Rim, 44f);
        cardRim.shape = UIGlass.Shape.Ring;
        cardRim.rimWidth = 2.4f;
        cardRim.raycastTarget = false;
        overCardCG = overCard.gameObject.AddComponent<CanvasGroup>();
        overCardCG.alpha = 0f;

        // v0.51：彩纸层（零贴图顶点网格，踩坑 4 同路径）——盖在卡片上、按钮下；
        // 文字仍在 IMGUI 层永远最上，彩纸不会压住任何字。
        var confGo = new GameObject("OverConfetti", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIConfetti));
        var confRT = (RectTransform)confGo.transform;
        confRT.SetParent(over.transform, false);
        confRT.anchorMin = Vector2.zero;
        confRT.anchorMax = Vector2.one;
        confRT.offsetMin = Vector2.zero;
        confRT.offsetMax = Vector2.zero;
        confetti = confGo.GetComponent<UIConfetti>();
        confetti.raycastTarget = false;

        // "再来一局"：下移 40px 给卡片让位（-140→-180）。按钮与其软投影放进同一个
        // 容器再做延迟淡入——AttachShadow 生成的投影是按钮的【兄弟节点】，若只给按钮
        // 挂 CanvasGroup，按钮淡入前投影会先暴露成一块灰斑（v0.51 编辑器截图实测）。
        var againGroup = new GameObject("AgainGroup", typeof(RectTransform));
        var agRT = (RectTransform)againGroup.transform;
        agRT.SetParent(over.transform, false);
        agRT.anchorMin = agRT.anchorMax = new Vector2(0.5f, 0.5f);
        agRT.anchoredPosition = new Vector2(0, -180);
        agRT.sizeDelta = new Vector2(460, 116);
        Btn("AgainBtn", againGroup.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 116),
            GlassGreen, () => UnityEngine.SceneManagement.SceneManager.LoadScene(0), true);
        againCG = againGroup.AddComponent<CanvasGroup>();
        againCG.alpha = 0f;
        againCG.blocksRaycasts = false;
        againCG.interactable = false;

        // ---- 设置面板（半透暗化底 + 磨砂玻璃大面板，滑入动画；iOS sheet 风格）----
        var settingsRoot = StretchImg("SettingsDim", mgo.transform, new Color(0f, 0f, 0f, 0f));   // 暗化交给模糊层
        settingsCG = settingsRoot.AddComponent<CanvasGroup>();
        settingsCG.alpha = 0f;
        settingsCG.blocksRaycasts = false;
        settingsCG.interactable = false;

        // v0.44g：设置弹出时背景逐渐高斯模糊——全屏磨砂层是 settingsRoot 的子物体，
        // 随 settingsCG 的淡入（= 滑入动画进度）同步出现，alpha 到 1 时背景完全变成
        // 模糊场景（LiquidGlass 的 _Blur 14 = 高斯观感）。置于面板之下、暗化之上。
        var settingsBlurBg = UIGlass.Stretch(settingsRoot.transform, "SettingsBlurBg",
            new Color(0.16f, 0.18f, 0.22f, 0.82f), true, 14f, 0.06f, 0.02f, 0.18f);   // 暗色高斯模糊（iOS sheet 语言：暗底衬亮面板）
        settingsBlurBg.raycastTarget = false;
        settingsBlurBg.transform.SetAsFirstSibling();

        var settingsGlass = UIGlass.Add(settingsRoot.transform, "SettingsPanel", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(900, 760), GlassPanel, 36f);
        settingsGlass.UseBlur(16f, 0.14f, 0.10f, 0.15f);             // 与背景交互：抓取面板后的场景做模糊
        // 玻璃边缘高光环：淡磨砂面板在亮台面上边界模糊，靠这圈高光勾出 sheet 轮廓
        var setRim = UIGlass.Add(settingsRoot.transform, "SettingsPanelRim", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(908, 768), Rim, 40f);
        setRim.shape = UIGlass.Shape.Ring;
        setRim.rimWidth = 2.4f;
        setRim.raycastTarget = false;
        settingsPanel = settingsGlass.gameObject;
        settingsPanelRT = settingsPanel.GetComponent<RectTransform>();
        settingsGlass.AttachShadow(14f, -7f, 0.22f, 18f);   // v0.47：大面板软投影（插在面板与模糊底之间）
        // 注意 y 符号：pos.y = 540 - 设计y（设计坐标从顶部往下，uGUI 中心锚定 y 向上）
        // 版面（v0.46 五行，行距 105）：标题 205 → 帧率 320 → 分辨率 425 → 阴影 530 →
        // 物理步长 635 → 音效 740 → 完成 845。v0.44：标题条/菱饰移除，改 iOS sheet 的纯排版。
        Btn("FpsLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 220), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(0, -1));
        Btn("FpsRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 220), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(0, +1));
        Btn("ResLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 115), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(1, -1));
        Btn("ResRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 115), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(1, +1));
        Btn("ShadowLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, 10), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(2, -1));
        Btn("ShadowRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, 10), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(2, +1));
        // v0.37：物理步长三档 0.5/1/2ms（Time.fixedDeltaTime，改了立即生效、持久化）
        Btn("StepLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, -95), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(3, -1));
        Btn("StepRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, -95), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(3, +1));
        // v0.46：音效开/关（击球与碰撞音效，Sfx 播放前检查 GameSettings.SfxOn）
        Btn("SfxLeft", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(-100, -200), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(4, -1));
        Btn("SfxRight", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(330, -200), new Vector2(110, 72),
            GlassNeutral, () => CycleSetting(4, +1));
        Btn("SettingsDoneBtn", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -305), new Vector2(360, 100),
            GlassGreen, ToggleSettings, true);
        // v0.44：行间发丝分隔线（半透白）；v0.46 行距压缩为 105，补第五条
        Img("Div1", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 167.5f), new Vector2(780, 2), Hairline);
        Img("Div2", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 62.5f), new Vector2(780, 2), Hairline);
        Img("Div3", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -42.5f), new Vector2(780, 2), Hairline);
        Img("Div4", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -147.5f), new Vector2(780, 2), Hairline);
        Img("Div5", settingsRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -252.5f), new Vector2(780, 2), Hairline);

        // ---- 147 满分提示横幅（白色玻璃胶囊，平时藏在屏幕外）----
        var popup = UIGlass.Add(mgo.transform, "Popup147", new Vector2(0.5f, 0.5f), new Vector2(0, -270), new Vector2(1150, 150),
            GlassStrong, 75f, true);
        popup.raycastTarget = false;
        popupRT = popup.GetComponent<RectTransform>();
        popupCG = popup.gameObject.AddComponent<CanvasGroup>();
        popupCG.alpha = 0f;
        popupCG.blocksRaycasts = false;

        // ---- v0.35：让对手重打 提示框（判 Miss 后显示，Rule 11(b)）----
        // 位置在屏幕中下方（不遮挡球堆与瞄准区），两个按钮左右并排
        var replayGlass = UIGlass.Add(mgo.transform, "ReplayPanel", new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(1160, 210),
            GlassStrong, 36f);
        replayGlass.raycastTarget = true;                    // 面板自身挡住下面的按钮
        replayPanel = replayGlass.gameObject;
        replayGlass.AttachShadow(14f, -6f, 0.22f, 18f);     // v0.47：弹层软投影
        // v0.45：全屏透明挡板（选框的子物体，继承其淡入/射线开关）——选框弹出期间
        // 挡住屏幕上一切点击（击球/力度/瞄准），强制先做出选择（Rule 13/14(b)）。
        // 放在两个选项按钮之前创建（渲染在底层），按钮仍可点击。
        StretchImg("ReplayBlocker", replayPanel.transform, new Color(0f, 0f, 0f, 0f));
        Btn("ReplayYes", replayPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(-290, -52), new Vector2(520, 84),
            GlassBlue, ChooseReplay);
        Btn("ReplayNo", replayPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(290, -52), new Vector2(520, 84),
            GlassNeutral, DismissReplay);
        replayCG = replayPanel.AddComponent<CanvasGroup>();
        replayCG.alpha = 0f;
        replayCG.blocksRaycasts = false;
        replayCG.interactable = false;

        // 注意：这里的 uiMat 只赋给传统 Image（发丝线/暗化底等）；
        // UIGlass 有自己的材质（磨砂面板= LiquidGlass，其余=默认 UI 材质），必须跳过，
        // 否则玻璃材质会被覆盖成不透明色块。
        foreach (var g in mgo.GetComponentsInChildren<Graphic>())
        {
            if (g is UIGlass) continue;
            if (uiMat != null) g.material = uiMat;
        }

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

        // ---- v0.51：胜利结算动画时间线（详见字段区注释；卡片是 overCG 子物体，
        //      其 CanvasGroup 与磨砂底淡入自动相乘，这里只驱动各自的进度）----
        if (overActiveFlag)
        {
            overT += Time.deltaTime;

            // 胜者卡片：0.10s 起淡入 0.15s + 欠阻尼弹簧缩放 0.72→1（刚度 180/阻尼 19，
            // ζ≈0.71 → 过冲 ~4%，与 UIJelly 同款半隐式欧拉积分）
            if (overCardCG != null)
                overCardCG.alpha = Mathf.Clamp01((overT - 0.10f) / 0.15f);
            if (overT > 0.10f)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.033f);
                overCardVel += (1f - overCardScale) * 180f * dt;
                overCardVel *= Mathf.Exp(-19f * dt);
                overCardScale += overCardVel * dt;
            }
            if (overCardRT != null)
                overCardRT.localScale = new Vector3(overCardScale, overCardScale, 1f);

            // 比分跳数：0.40s 起 1.1s 内 easeOutCubic 数到最终分（IMGUI 侧取 shownS0/S1 绘制）
            float ct = Mathf.Clamp01((overT - 0.40f) / 1.10f);
            ct = 1f - Mathf.Pow(1f - ct, 3f);
            shownS0 = Mathf.RoundToInt(overS0 * ct);
            shownS1 = Mathf.RoundToInt(overS1 * ct);

            // 彩纸：0.45s 起连爆 1.6s（只起爆一次；重开一局由 ShowGameOver(false) 清场）
            if (!confettiLaunched && overT > 0.45f && confetti != null)
            {
                confettiLaunched = true;
                confetti.Launch(1.6f);
            }

            // "再来一局"：0.55s 起淡入 0.3s，淡入完成才开射线（防止截住半透明按钮的点击）
            if (againCG != null)
            {
                float at = Mathf.Clamp01((overT - 0.55f) / 0.30f);
                againCG.alpha = at;
                againCG.blocksRaycasts = at > 0.9f;
                againCG.interactable = at > 0.9f;
            }
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

        // ---- v0.44：中央提示胶囊底随文字显隐（显示条件与 OnGUI 的文字逐字对应）----
        if (msgPillCG != null)
        {
            float pillTarget = (msgTimer > 0f && settingsAlpha < 0.4f) ? 1f : 0f;
            msgPillCG.alpha = Mathf.MoveTowards(msgPillCG.alpha, pillTarget, Time.deltaTime / 0.15f);
            bool cueVis = gm != null && gm.cueInHand && settingsAlpha < 0.4f;
            cueHandPillCG.alpha = Mathf.MoveTowards(cueHandPillCG.alpha, cueVis ? 1f : 0f, Time.deltaTime / 0.15f);
            bool ballVis = gm != null && gm.state == GameManager.State.Aiming && settingsAlpha < 0.4f &&
                           (gm.freeBallActive || gm.freeColorPending || gm.colorsPhase);
            ballOnPillCG.alpha = Mathf.MoveTowards(ballOnPillCG.alpha, ballVis ? 1f : 0f, Time.deltaTime / 0.15f);
            if (ballOnPill != null)                          // 自由球=薄荷底，指定彩球=白底
                ballOnPill.color = (gm != null && gm.freeBallActive) ? MintPill : GlassStrong;
        }

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

    /// kind: 0=帧率 1=分辨率 2=阴影 3=物理步长(v0.37) 4=音效开/关(v0.46)；dir=+1/-1 循环方向。
    /// 改完立即生效并持久化。
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
        else if (kind == 4)
        {
            GameSettings.SfxOn = !GameSettings.SfxOn;   // 开/关二值：◀ ▶ 任一方向都翻转
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

    /// 玻璃胶囊按钮（v0.44 iOS 液态玻璃）：半透明淡彩胶囊 + 顶部高光条 + 亮色描边环。
    /// primary=true 为主操作按钮（系统绿实色胶囊），点击带确认触感；所有按钮按下轻触感。
    private void Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        Btn(name, parent, anchor, pos, size, bg, onClick, false);
    }

    private void Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityEngine.Events.UnityAction onClick, bool primary)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIGlass), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var body = go.GetComponent<UIGlass>();
        body.shape = UIGlass.Shape.Capsule;                       // iOS 胶囊按钮（近正方形时≈圆形）
        body.color = bg;
        body.UseRefraction(1.0f, 28f, 1.6f, 1.25f, 0.7f, 0.70f);   // 清澈水玻璃：实时折射+反射+光晕
        // 玻璃边缘的镜面高光环（罩在胶囊外沿，随圆角走）
        var rim = UIGlass.Add(go.transform, name + "Rim", new Vector2(0.5f, 0.5f),
            Vector2.zero, size + new Vector2(5, 5), Rim, (size.y + 5f) * 0.5f, true);
        rim.shape = UIGlass.Shape.Ring;
        rim.rimWidth = 2.2f;
        rim.raycastTarget = false;
        // 顶部高光条（玻璃上沿受光）。矮按钮（h<80，如 HUD 的一排小按钮）不放——
        // 文字几乎占满整个按钮，高光条边缘会横穿文字，看起来像"删除线"。
        if (size.y >= 80f)
        {
            var sheen = UIGlass.Add(go.transform, name + "Sheen", new Vector2(0.5f, 0.5f),
                new Vector2(0, size.y * 0.24f), new Vector2(size.x - 16, size.y * 0.5f),
                new Color(1f, 1f, 1f, primary ? 0.16f : 0.30f), 0f, true);
            sheen.raycastTarget = false;
        }
        var b = go.GetComponent<Button>();
        var colors = b.colors;
        colors.pressedColor = primary ? new Color(0.80f, 0.93f, 0.85f) : new Color(0.86f, 0.89f, 0.96f); // 按下变暗一档
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.targetGraphic = body;
        b.onClick.AddListener(() => { if (primary) Haptics.Tap(); onClick(); });  // 主按钮点击=确认触感
        // 所有按钮：按下即轻点一下（iOS light impact）
        var et = go.AddComponent<EventTrigger>();
        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => Haptics.Tick());
        et.triggers.Add(downEntry);
        // Q 弹按压动画：按下压扁、松手带过冲弹回（纯程序弹簧，见 UIJelly）
        go.AddComponent<UIJelly>();
        // v0.47：软投影（参考图药丸质感）——插在按钮正下方，随所在面板 CanvasGroup 一起淡入淡出
        body.AttachShadow(10f, -5f, 0.20f, 16f);
    }

    /// 力度滑条（v0.44 iOS 风：白色玻璃胶囊轨道 + 白色填充 + 圆形白球手柄 + 高光环）。
    private Slider MakeSlider(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Power", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = Color.clear;            // 根节点=透明热区（Slider 命中测试用）

        // 轨道：白色玻璃胶囊（iOS 滑杆的半透轨道）
        var track = UIGlass.Add(rt, "Track", new Vector2(0.5f, 0.5f), Vector2.zero, size, new Color(1f, 1f, 1f, 0.18f), 0f, true);
        track.UseRefraction(1.5f, 12f, 1.3f, 0.9f, 0.6f, 0.55f);
        track.raycastTarget = false;

        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        var faRt = (RectTransform)fillArea.transform;
        faRt.SetParent(rt, false);
        faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
        faRt.offsetMin = new Vector2(6, 16); faRt.offsetMax = new Vector2(-6, -16);

        // 填充：随值伸缩，用胶囊网格自动适配（宽度归零时网格自动退化隐藏）
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIGlass));
        var fRt = (RectTransform)fill.transform;
        fRt.SetParent(faRt, false);
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.sizeDelta = Vector2.zero;
        var fGlass = fill.GetComponent<UIGlass>();
        fGlass.shape = UIGlass.Shape.Capsule;
        fGlass.color = new Color(0.25f, 0.58f, 1f, 0.40f);   // iOS 蓝填充，与白轨道区分开
        fGlass.UseRefraction(1f, 10f, 1.2f, 0.8f, 0.5f, 0.55f);
        fGlass.raycastTarget = false;

        var handleArea = new GameObject("HandleArea", typeof(RectTransform));
        var haRt = (RectTransform)handleArea.transform;
        haRt.SetParent(rt, false);
        haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(10, 0); haRt.offsetMax = new Vector2(-10, 0);

        // 手柄本体透明（Slider 需要一个 handleRect），视觉用白色玻璃圆球 + 高光环
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var hRt = (RectTransform)handle.transform;
        hRt.SetParent(haRt, false);
        hRt.anchorMin = new Vector2(0.5f, 0f); hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.sizeDelta = new Vector2(30, 0);
        handle.GetComponent<Image>().color = Color.clear;
        var knob = UIGlass.Add(hRt, "Knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42, 42),
            new Color(1f, 1f, 1f, 0.46f), 0f);
        knob.shape = UIGlass.Shape.Circle;
        knob.UseRefraction(1f, 14f, 1.6f, 1.2f, 0.65f, 0.65f);
        knob.raycastTarget = false;
        var knobRim = UIGlass.Add(hRt, "KnobRim", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46), Rim, 23f);
        knobRim.shape = UIGlass.Shape.Ring;
        knobRim.raycastTarget = false;

        // 手柄 Q 弹：拖住时放大、松手弹回（手柄自身不接收射线，由滑条根物体驱动）
        var knobJelly = handle.AddComponent<UIJelly>();
        knobJelly.pressScale = new Vector2(1.16f, 1.16f);
        knobJelly.stiffness = 340f;
        var s = go.GetComponent<Slider>();
        s.fillRect = fRt;
        s.handleRect = hRt;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f; s.maxValue = 1f;
        var sEt = go.AddComponent<EventTrigger>();
        var sDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        sDown.callback.AddListener(_ => { knobJelly.SetHeld(true); Haptics.Tick(); });
        var sUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        sUp.callback.AddListener(_ => knobJelly.SetHeld(false));
        sEt.triggers.Add(sDown);
        sEt.triggers.Add(sUp);
        return s;
    }

    /// 中央提示的玻璃胶囊底（v0.44）：文字仍在 IMGUI 层，显隐由 Update 驱动 CanvasGroup。
    private CanvasGroup MakePill(string name, Transform parent, Vector2 uguiPos, Vector2 size, Color col)
    {
        var g = UIGlass.Add(parent, name, new Vector2(0.5f, 0.5f), Fit(uguiPos, size), size, col, 0f, true);
        g.UseRefraction(1.5f, 18f, 1.4f, 1.0f, 0.65f, 0.60f);
        g.raycastTarget = false;
        var cg = g.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        return cg;
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

    /// <summary>
    /// 显隐结算面板（v0.51：胜利结算动画）。on=true 时写入胜者/终分/最高单杆并重置
    /// 动画时间线（卡片 Q 弹 + 比分跳数 + 彩纸 + 延迟按钮，推进在 Update）；
    /// on=false（重开一局）时清掉彩纸与按钮的延迟淡入状态。
    /// </summary>
    public void ShowGameOver(bool on, int winner = 0, int s0 = 0, int s1 = 0, int mb0 = 0, int mb1 = 0)
    {
        if (on)
        {
            overWinner = winner;
            overS0 = s0; overS1 = s1;
            overMB0 = mb0; overMB1 = mb1;
            overT = 0f;                                  // 时间线起表
            shownS0 = shownS1 = 0;                       // 跳数从 0 起数
            overCardScale = 0.72f; overCardVel = 0f;     // 卡片弹簧复位
            confettiLaunched = false;
            if (confetti != null) confetti.ResetState();
            Sfx.Win();                                   // 胜利号角（SfxOn 关闭/测试环境静默）
        }
        else
        {
            if (confetti != null) confetti.ResetState();
            if (againCG != null)
            {
                againCG.alpha = 0f;
                againCG.blocksRaycasts = false;
                againCG.interactable = false;
            }
        }
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

        style.normal.textColor = new Color(0f, 0f, 0f, 0.22f * col.a);   // 投影（玻璃更透后适当加重，保文字对比度）
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
            Color breakGold = Fade(AccentOrange);
            Color dimGray = Fade(Ink2);
            Color ink = Fade(Ink);
            DrawLabel(FittedRect(161, 48, 190, 76), p1Text, FontBtn, ink, TextAnchor.MiddleLeft);       // 名字：左边缘 66
            DrawLabel(FittedRect(303, 48, 90, 76), "单杆", FontBtn, dimGray, TextAnchor.MiddleLeft);                    // 左边缘 258
            DrawLabel(FittedRect(447, 48, 190, 76), p1Break, FontPrimary, breakGold, TextAnchor.MiddleLeft);                // 左边缘 352
            DrawLabel(FittedRect(1749, 48, 190, 76), p2Text, FontBtn, ink, TextAnchor.MiddleRight);        // 名字：右边缘 1844
            DrawLabel(FittedRect(1585, 48, 90, 76), "单杆", FontBtn, dimGray, TextAnchor.MiddleRight);                  // 右边缘 1630
            DrawLabel(FittedRect(1435, 48, 190, 76), p2Break, FontPrimary, breakGold, TextAnchor.MiddleRight);              // 右边缘 1530
            DrawLabel(FittedRect(960, 48, 900, 76), centerText, FontBtn, ink, TextAnchor.MiddleCenter);
            // v0.38：设置面板打开时不再画"XX 击球"提示——IMGUI 永远画在 uGUI 之上，
            // 会穿透设置面板标题条（真机截图确认）。同理下方"球在手"提示也受此保护。
            if (settingsAlpha < 0.4f)
                DrawLabel(FittedRect(960, 160, 1400, 64), msgText, FontMsg, ink, TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(1560, 922, 300, 44), powerText, FontBtn, ink, TextAnchor.MiddleCenter);

            // ---- HUD 按钮文字（与上面 Btn/Fit 的位置逐一对齐）----
            DrawLabel(FittedRect(1920 - 125, 1080 - 88, 180, 150), "击球", FontPrimary, Fade(Color.white), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(140, 1080 - 92, 240, 62), aimHudText, FontBtn, ink, TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(300, 1080 - 92, 70, 62), "◀", FontArrow, ink, TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(374, 1080 - 92, 70, 62), "▶", FontArrow, ink, TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(140, 1080 - 176, 240, 58), "重新开局", FontBtn, Fade(TextRed), TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(420, 1080 - 176, 240, 58), "设 置", FontBtn, ink, TextAnchor.MiddleCenter);

            // ---- v0.36：加塞圆盘文字（与 uGUI 面板逐像素对齐）----
            // 面板中心设计 y=700、尺寸 250×310；圆盘中心设计 y=708、半径 75
            // v0.48：标题「击球点」由次级灰改为主墨色（黑），与下方"中杆"行同色
            DrawLabel(FittedRect(1700, 595, 240, 40), "击球点", FontSub, ink, TextAnchor.MiddleCenter);
            DrawLabel(FittedRect(1700, 805, 240, 40), spinPad != null ? spinPad.Describe() : "中杆", FontSub, ink, TextAnchor.MiddleCenter);

            // ---- v0.36："球在手"提示（开球前 / 白球落袋后可在 D 区内拖动白球）----
            var gmx = GameManager.I;
            if (gmx != null && gmx.cueInHand && settingsAlpha < 0.4f)
                DrawLabel(FittedRect(960, 300, 1100, 52), "球在手：拖动白球可在开球区 D 内自由摆放", FontMsg,
                    ink, TextAnchor.MiddleCenter);
        }

        // ---- 主菜单（文字随菜单整体淡入；设置面板打开时再淡出避免与面板重叠）----
        if (menuAlpha > 0.01f)
        {
            float ma = menuAlpha * (1f - settingsAlpha * 0.95f);   // 设置打开时菜单文字让位
            Color w = Color.white; w.a *= ma;                      // 绿色主按钮上的白字
            Color inkM = Ink; inkM.a *= ma;                        // 淡磨砂底上的墨色标题/按钮字
            Color ink2M = Ink; ink2M.a *= ma * 0.8f;              // 次级副标题（比标题略淡）
            DrawLabel(CRect(960, 540 - 170, 1200, 120), "双人斯诺克 3D", FontTitle, inkM, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 - 62, 1200, 50), "标准斯诺克规则 · 真实物理 · 轮流击球", FontSub, ink2M, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 40, 400, 84), aimMenuText, FontMenuBtn, inkM, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 180, 460, 116), "开始游戏", FontPrimary, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 540 + 290, 460, 96), "设 置", FontMenuBtn, inkM, TextAnchor.MiddleCenter);
        }

        // ---- 结算面板（v0.51：胜利结算动画文字层，与 uGUI 卡片时间线逐项对齐）----
        // 卡片版面（设计 y 从顶往下，卡片 180~636）：标题 296 → 比分 470 → 名字 538 →
        // 最高单杆 596；"再来一局" 720（uGUI 按钮中心锚定 y=-180）。
        if (overAlpha > 0.01f)
        {
            float cardA = overCardCG != null ? overCardCG.alpha : 1f;
            float titleT = Mathf.Clamp01((overT - 0.10f) / 0.45f);
            titleT = 1f - Mathf.Pow(1f - titleT, 3f);    // easeOutCubic：与卡片弹簧同起点，观感连贯
            Color w = Color.white; w.a *= overAlpha * (againCG != null ? againCG.alpha : 0f);

            // 标题：胜者名用阵营色（与 HUD 阵营点同色系），随卡片 Q 弹上滑入场
            Color winCol = overWinner == 0 ? ChipBlue : ChipRed;
            winCol.a *= overAlpha * titleT * cardA;
            float titleY = 296 + (1f - titleT) * 46f;
            DrawLabel(CRect(960, titleY, 1400, 110),
                GameManager.I.names[overWinner] + " 获胜！", FontOver, winCol, TextAnchor.MiddleCenter);

            // 比分跳数（0.40s 起随跳数进度淡入）：胜者数字用胜者色，大字 FontBanner
            float scoreA = Mathf.Clamp01((overT - 0.40f) / 0.30f) * cardA;
            Color c0 = ChipBlue; c0.a *= overAlpha * scoreA;
            Color c1 = ChipRed; c1.a *= overAlpha * scoreA;
            Color colon = Ink; colon.a *= overAlpha * scoreA * 0.6f;
            DrawLabel(CRect(800, 470, 240, 96), shownS0.ToString(), FontBanner,
                overWinner == 0 ? c0 : new Color(c1.r, c1.g, c1.b, c1.a * 0.85f), TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 470, 100, 96), ":", FontBanner, colon, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1120, 470, 240, 96), shownS1.ToString(), FontBanner,
                overWinner == 1 ? c1 : new Color(c0.r, c0.g, c0.b, c0.a * 0.85f), TextAnchor.MiddleCenter);
            // 双方名字（阵营色小字；败者略淡——上面给非胜者数字乘的 0.85 同一逻辑）
            DrawLabel(CRect(800, 538, 300, 40), GameManager.I.names[0], FontSub, c0, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1120, 538, 300, 40), GameManager.I.names[1], FontSub, c1, TextAnchor.MiddleCenter);

            // 最高单杆（0.80s 起淡入，次级灰）
            float mbA = Mathf.Clamp01((overT - 0.80f) / 0.35f) * cardA;
            Color mbCol = Ink2; mbCol.a *= overAlpha * mbA;
            DrawLabel(CRect(960, 596, 800, 40), "最高单杆  " + overMB0 + " : " + overMB1,
                FontSub, mbCol, TextAnchor.MiddleCenter);

            DrawLabel(CRect(960, 720, 460, 116), "再来一局", FontPrimary, w, TextAnchor.MiddleCenter);
        }

        // ---- 设置面板（文字随面板滑入位移 + 淡入）----
        if (settingsAlpha > 0.01f)
        {
            float sox = settingsOffset;                      // 与 uGUI 面板同量位移
            Color w = Color.white; w.a *= settingsAlpha;     // 绿色"完成"按钮上的白字
            Color gray = Ink; gray.a *= settingsAlpha * 0.78f;   // 淡磨砂上的次级行标签（比 Ink 略淡但足够清晰）
            Color inkS = Ink; inkS.a *= settingsAlpha;       // 淡磨砂上的墨色标题/值/箭头

            DrawLabel(CRect(960 + sox, 205, 400, 70), "设 置", FontHead, inkS, TextAnchor.MiddleCenter);

            DrawLabel(CRect(655 + sox, 320, 260, 56), "帧率上限", FontMsg, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 320, 340, 64), GameSettings.FpsText, FontVal, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 320, 110, 72), "◀", FontArrow, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 320, 110, 72), "▶", FontArrow, inkS, TextAnchor.MiddleCenter);

            DrawLabel(CRect(655 + sox, 425, 260, 56), "渲染分辨率", FontMsg, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 425, 340, 64), GameSettings.ResText, FontVal, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 425, 110, 72), "◀", FontArrow, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 425, 110, 72), "▶", FontArrow, inkS, TextAnchor.MiddleCenter);

            DrawLabel(CRect(655 + sox, 530, 260, 56), "画面阴影", FontMsg, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 530, 340, 64), GameSettings.ShadowText, FontVal, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 530, 110, 72), "◀", FontArrow, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 530, 110, 72), "▶", FontArrow, inkS, TextAnchor.MiddleCenter);

            DrawLabel(CRect(655 + sox, 635, 260, 56), "物理步长", FontMsg, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 635, 340, 64), GameSettings.StepText, FontVal, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 635, 110, 72), "◀", FontArrow, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 635, 110, 72), "▶", FontArrow, inkS, TextAnchor.MiddleCenter);

            // v0.46：音效开/关（第五行）
            DrawLabel(CRect(655 + sox, 740, 260, 56), "音效", FontMsg, gray, TextAnchor.MiddleRight);
            DrawLabel(CRect(1075 + sox, 740, 340, 64), GameSettings.SfxText, FontVal, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(860 + sox, 740, 110, 72), "◀", FontArrow, inkS, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1290 + sox, 740, 110, 72), "▶", FontArrow, inkS, TextAnchor.MiddleCenter);

            DrawLabel(CRect(960 + sox, 845, 360, 100), "完 成", FontVal, w, TextAnchor.MiddleCenter);
        }

        // ---- 147 满分提示横幅（文字随横幅从底部弹入）----
        if (popupAlpha > 0.01f)
        {
            float poy = 810 + popupYoff;                     // 横幅当前设计 y（越大越靠屏幕下方，从底部升上来）
            // 注：uGUI 底图在屏幕中心锚定的 y=-270（y 轴向上），换算成 IMGUI 的设计 y（向下）
            //     正是 540+270=810；popupYoff 从 760 归零，所以底图与文字同向从下往上升。
            //     v0.33 这里写成 810-popupYoff，底图往上、文字却往下，动画全程错位数百像素。
            Color gold = AccentOrange; gold.a *= popupAlpha;             // "147" 大数字（iOS 橙）
            Color ink = Ink; ink.a *= popupAlpha;                         // 白胶囊上的墨色主行
            Color ink2 = Ink2; ink2.a *= popupAlpha;                      // 次级行
            DrawLabel(CRect(705, poy, 280, 110), "147", FontBanner, gold, TextAnchor.MiddleRight);
            DrawLabel(CRect(770, poy - 30, 420, 56), "满分进行中", FontLayer, ink, TextAnchor.MiddleLeft);
            DrawLabel(CRect(770, poy + 28, 420, 50), "红黑连击 " + popupPairsText, FontSub, ink2, TextAnchor.MiddleLeft);
        }

        // ---- v0.35：让对手重打 提示文字（Rule 11(b) 犯规与未击到）----
        // uGUI 底图中心锚定 y=-300 → IMGUI 设计 y = 540+300 = 840
        if (replayAlpha > 0.01f)
        {
            Color ink = Ink; ink.a *= replayAlpha;            // 白玻璃弹层上的墨色标题
            Color ink2 = Ink2; ink2.a *= replayAlpha;
            Color w = Color.white; w.a *= replayAlpha;        // 蓝色主按钮上的白字
            DrawLabel(CRect(960, 792, 1100, 56), "对方犯规且未击中球（Miss）", FontMsg, ink, TextAnchor.MiddleCenter);
            DrawLabel(CRect(960, 836, 1100, 44), "请先选择再击球 · 规则允许要求对方从当前球位重打", FontSub, ink2, TextAnchor.MiddleCenter);
            DrawLabel(CRect(670, 888, 520, 84), "让对手重打", FontLayer, w, TextAnchor.MiddleCenter);
            DrawLabel(CRect(1250, 888, 520, 84), "我自己打", FontLayer, ink, TextAnchor.MiddleCenter);
        }

        // ---- v0.35：自由球 / 指定彩球 状态提示（HUD 中央行下方）----
        // v0.38：设置面板打开时隐藏（IMGUI 在 uGUI 之上，否则会穿透面板）
        var gm = GameManager.I;
        if (gm != null && gm.state == GameManager.State.Aiming && settingsAlpha < 0.4f)
        {
            if (gm.freeBallActive)
                DrawLabel(CRect(960, 240, 900, 52), "自由球：可指定任意一颗球作为球 on",
                    FontMsg, Ink, TextAnchor.MiddleCenter);
            else if (gm.freeColorPending || gm.colorsPhase)
            {
                string nom = gm.nominatedSet
                    ? "已指定 " + G.CnName(gm.nominatedColor)
                    : "请用准线瞄准要打的彩球以指定";
                DrawLabel(CRect(960, 240, 900, 46), nom, FontMsg,
                    gm.nominatedSet ? Ink : Ink2,
                    TextAnchor.MiddleCenter);
            }
        }
    }
}
