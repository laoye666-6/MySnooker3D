// =====================================================================================
// Haptics.cs —— 按钮触感反馈（v0.44 新增，iOS Taptic 风格的两档强度）
//
// Android 实现：Vibrator.getSystemService + VibrationEffect.createOneShot(时长, 振幅)，
// 短脉冲模拟 iOS 的 light/medium impact feedback（不做整段振动，手感是"嗒"一下）。
//   - VibrationEffect 需要 API 26+（MuMu 是 Android 15 没问题）；
//     低版本（minSdk 24/25）回退到旧版 vibrate(long)（无振幅控制）。
//   - 编辑器 / 非 Android 平台直接跳过（AndroidJavaClass 在编辑器不可用）。
//   - 全程 try/catch：振动失败绝不能影响点击本身的逻辑。
// 权限：AndroidManifest.xml 需要 android.permission.VIBRATE
// （母本在 E:\Snooker\android\，已放进工程 Assets\Plugins\Android\）。
//
// 接线（UIManager.Btn）：所有按钮 PointerDown → Tick()（按下即有反馈）；
// 主操作按钮（开始游戏/击球/完成/再来一局）onClick 再补一次 Tap() 作为确认。
// =====================================================================================
using UnityEngine;

public static class Haptics
{
    /// 轻点：按钮按下时（≈ iOS UIImpactFeedbackGenerator .light）
    public static void Tick() { Pulse(12, 80); }

    /// 确认：主操作按钮点击时（≈ .medium）
    public static void Tap() { Pulse(24, 160); }

    static void Pulse(long ms, int amp)
    {
        if (Application.platform != RuntimePlatform.Android) return;
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var vib = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vib == null) return;
                try
                {
                    using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = ve.CallStatic<AndroidJavaObject>("createOneShot", ms, amp))
                        vib.Call("vibrate", effect);
                }
                catch { vib.Call("vibrate", ms); }            // API < 26：退回旧接口
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[HAPTIC] " + e.Message); }
    }
}
