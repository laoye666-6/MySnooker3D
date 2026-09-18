// =====================================================================================
// GameSettings.cs —— 游戏设置：帧率 / 渲染分辨率 / 阴影画质
//
// 数据流：Bootstrapper 启动时 CaptureNative() 记录设备原生分辨率 → Load() 读 PlayerPrefs
//        → Apply() 应用；UIManager 的设置面板修改后调用 Save()+Apply() 立即生效并持久化。
//
// 各参数说明：
//   FpsOptions  帧率四档（60/90/120/144），默认下标 2 = 120（v0.31 前的固定值）
//   ResOptions  渲染分辨率三档（原生高度的 50%/75%/100%），通过 Screen.SetResolution 生效，
//               低端机可用 50% 换流畅度；默认 100%
//   Shadows     阴影开关（关 = 零阴影，低端机提速明显）；默认开
// =====================================================================================
using UnityEngine;

public static class GameSettings
{
    public static readonly int[] FpsOptions = { 60, 90, 120, 144 };        // 帧率四档
    public static readonly float[] ResOptions = { 0.5f, 0.75f, 1.0f };     // 分辨率三档（×原生）

    public static int FpsIndex = 2;        // 当前帧率档下标，默认 120
    public static int ResIndex = 2;        // 当前分辨率档下标，默认 100%
    public static bool Shadows = true;     // 阴影开关，默认开

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
    }

    /// 写回 PlayerPrefs。
    public static void Save()
    {
        PlayerPrefs.SetInt("snk_fpsIdx", FpsIndex);
        PlayerPrefs.SetInt("snk_resIdx", ResIndex);
        PlayerPrefs.SetInt("snk_shadows", Shadows ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// 应用当前设置到引擎（帧率 / 分辨率 / 阴影）。
    public static void Apply()
    {
        Application.targetFrameRate = FpsOptions[FpsIndex];
        int w = Mathf.Max(320, Mathf.RoundToInt(NativeW * ResOptions[ResIndex]));
        int h = Mathf.Max(180, Mathf.RoundToInt(NativeH * ResOptions[ResIndex]));
        Screen.SetResolution(w, h, true);
        QualitySettings.shadows = Shadows ? ShadowQuality.All : ShadowQuality.Disable;
        Debug.Log("[SNOOKER] settings applied fps=" + FpsOptions[FpsIndex] +
                  " res=" + w + "x" + h + " shadows=" + Shadows);
    }

    // ---- 设置面板展示文案 ----
    public static string FpsText { get { return FpsOptions[FpsIndex] + " FPS"; } }
    public static string ResText { get { return Mathf.RoundToInt(ResOptions[ResIndex] * 100) + "%"; } }
    public static string ShadowText { get { return Shadows ? "开" : "关"; } }
}
