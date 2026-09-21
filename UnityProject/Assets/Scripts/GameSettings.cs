// =====================================================================================
// GameSettings.cs —— 游戏设置：帧率 / 渲染分辨率 / 阴影画质 / 物理步长
//
// 数据流：Bootstrapper 启动时 CaptureNative() 记录设备原生分辨率 → Load() 读 PlayerPrefs
//        → Apply() 应用；UIManager 的设置面板修改后调用 Save()+Apply() 立即生效并持久化。
//
// 各参数说明：
//   FpsOptions  帧率四档（60/90/120/144），默认下标 2 = 120（v0.31 前的固定值）
//   ResOptions  渲染分辨率三档（原生高度的 50%/75%/100%），通过 Screen.SetResolution 生效，
//               低端机可用 50% 换流畅度；默认 100%
//   Shadows     阴影开关（关 = 零阴影，低端机提速明显）；默认开
//   StepOptions 物理步长三档（v0.37：0.5/1/2 ms），默认 2ms。改 Time.fixedDeltaTime
//               【立即生效、无需重启】—— 步长只影响后续物理积分，运行中切换安全。
//               0.5ms 档每渲染帧最多 80+ 次物理积分，仅供高性能设备 experimentation。
// =====================================================================================
using UnityEngine;

public static class GameSettings
{
    public static readonly int[] FpsOptions = { 60, 90, 120, 144 };        // 帧率四档
    public static readonly float[] ResOptions = { 0.5f, 0.75f, 1.0f };     // 分辨率三档（×原生）
    public static readonly float[] StepOptions = { 0.0005f, 0.001f, 0.002f }; // 物理步长三档(秒)

    public static int FpsIndex = 2;        // 当前帧率档下标，默认 120
    public static int ResIndex = 2;        // 当前分辨率档下标，默认 100%
    public static bool Shadows = true;     // 阴影开关，默认开
    public static int StepIndex = 2;       // 当前物理步长档下标，默认 2ms（v0.36 起的值）

    public static int NativeW = 1920;      // 设备原生宽（CaptureNative 后有效）
    public static int NativeH = 1080;      // 设备原生高

    /// 启动时调用一次：记录未被缩放过的原生分辨率（此后 SetResolution 会改变 Screen）。
    public static void CaptureNative()
    {
        NativeW = Screen.width;
        NativeH = Screen.height;
    }

    /// 从 PlayerPrefs 读取（键名带版本前缀，改档位布局时可整体作废旧存档）。
    public static void Load()
    {
        FpsIndex = Mathf.Clamp(PlayerPrefs.GetInt("snk_fpsIdx", 2), 0, FpsOptions.Length - 1);
        ResIndex = Mathf.Clamp(PlayerPrefs.GetInt("snk_resIdx", 2), 0, ResOptions.Length - 1);
        Shadows = PlayerPrefs.GetInt("snk_shadows", 1) == 1;
        StepIndex = Mathf.Clamp(PlayerPrefs.GetInt("snk_stepIdx", 2), 0, StepOptions.Length - 1);
    }

    /// 写回 PlayerPrefs。
    public static void Save()
    {
        PlayerPrefs.SetInt("snk_fpsIdx", FpsIndex);
        PlayerPrefs.SetInt("snk_resIdx", ResIndex);
        PlayerPrefs.SetInt("snk_shadows", Shadows ? 1 : 0);
        PlayerPrefs.SetInt("snk_stepIdx", StepIndex);
        PlayerPrefs.Save();
    }

    /// 应用当前设置到引擎（帧率 / 分辨率 / 阴影 / 物理步长）。
    public static void Apply()
    {
        // v0.34：必须先关垂直同步。工程默认画质档的 vSyncCount=1 时 targetFrameRate 会被忽略，
        // 四档帧率(60/90/120/144)会全部失效；关掉后 targetFrameRate 才是真正的上限。
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = FpsOptions[FpsIndex];
        int w = Mathf.Max(320, Mathf.RoundToInt(NativeW * ResOptions[ResIndex]));
        int h = Mathf.Max(180, Mathf.RoundToInt(NativeH * ResOptions[ResIndex]));
        Screen.SetResolution(w, h, true);
        QualitySettings.shadows = Shadows ? ShadowQuality.All : ShadowQuality.Disable;
        // v0.37：物理步长。Bootstrapper 先写了默认 0.002，这里按用户存档覆盖；
        // 运行中切换也安全（fixedDeltaTime 只影响后续积分）。
        Time.fixedDeltaTime = StepOptions[StepIndex];
        Debug.Log("[SNOOKER] settings applied fps=" + FpsOptions[FpsIndex] +
                  " res=" + w + "x" + h + " shadows=" + Shadows +
                  " step=" + (StepOptions[StepIndex] * 1000f).ToString("F1") + "ms");
    }

    // ---- 设置面板展示文案 ----
    public static string FpsText { get { return FpsOptions[FpsIndex] + " FPS"; } }
    public static string ResText { get { return Mathf.RoundToInt(ResOptions[ResIndex] * 100) + "%"; } }
    public static string ShadowText { get { return Shadows ? "开" : "关"; } }
    public static string StepText { get { return (StepOptions[StepIndex] * 1000f).ToString("F1").TrimEnd('0', '.') + "ms"; } }
}
