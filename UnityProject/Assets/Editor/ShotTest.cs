// =====================================================================================
// ShotTest.cs —— 编辑器内"自动截图"驱动（Editor 专用，不进 APK）
//
// 用途：v0.38 起，UI/手感迭代全部在编辑器里做可视化验证，不再每轮都装模拟器。
// 做法：进播放模式 → 按时间线驱动游戏（开始游戏/开设置/换物理步长/拖动白球/设加塞/出杆）
//       → 每个关键点用 ScreenCapture 抓一张整屏 PNG（含 overlay UI）→ 退出编辑器。
//
// 运行（注意：不能加 -nographics，需要图形设备；也不能加 -quit ——
//   -quit 会在 isPlaying=true 之后立刻退出，播放模式的协程根本没机会跑）：
//   Unity.exe -batchmode -screen-width 1600 -screen-height 900 ^
//     -projectPath E:\Snooker3D -executeMethod ShotTest.Run ^
//     -logFile E:\Snooker\logs\shot.log
//   退出由 ShotDriver 自己在时间线结束时调 EditorApplication.Exit(0)。
//
// 产物：E:\Snooker\shots\editor\*.png（每轮覆盖）
// 日志：过滤 [SHOT] 看每张图的保存结果与屏幕尺寸。
// =====================================================================================
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class ShotTest
{
    /// 截图输出目录（每轮迭代覆盖同名文件，便于前后对比）。
    public const string OutDir = @"E:\Snooker\shots\editor";

    /// 目标截图分辨率（-screen-* 在批处理下常被忽略，这里强制）。
    public const int Width = 1600;
    public const int Height = 900;

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        // 批处理下 -screen-width 不可靠，进播放模式后由 ShotDriver 用 Screen.SetResolution 兜底
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Bootstrapper", typeof(Bootstrapper));
        new GameObject("ShotDriver", typeof(ShotDriver));
        Debug.Log("[SHOT] entering play mode, screen=" + Screen.width + "x" + Screen.height);
        EditorApplication.isPlaying = true;
    }
}

/// <summary>
/// 时间线驱动：进播放模式后自动执行，按固定节奏抓图。
/// 用反射调用 UIManager 的私有开关（ToggleSettings）以贴近真实点击路径。
/// </summary>
public class ShotDriver : MonoBehaviour
{
    private static UIManager UI { get { return GameManager.I != null ? GameManager.I.ui : null; } }

    void Start()
    {
        // v0.39：编辑器窗口失焦时播放模式可能暂停，协程会挂在等待上。
        // Application.runInBackground 让它在后台继续跑。
        //
        // 重要教训（两次失败换来的）：**不要在编辑器里调 Screen.SetResolution** ——
        // 它会尝试改变 Game 视图的分辨率，导致编辑器播放模式卡死（日志停在
        // "entering play mode"，一张图都抓不到）。截图尺寸就用 Game 视图的实际尺寸，
        // 想看大图请手动在 Game 视图里选 1600x900 分辨率档。
        Application.runInBackground = true;
        done = false;
        StartCoroutine(Heartbeat());
        StartCoroutine(Watchdog());
        StartCoroutine(Seq());
    }

    static bool done;                                    // 时间线完成标记（看门狗用）

    /// 心跳：每 5s 报一次真实时间，用于判断"协程没推进"时玩家循环是否还活着。
    IEnumerator Heartbeat()
    {
        float t0 = Time.unscaledTime;
        while (!done)
        {
            yield return Wait(5f);
            Debug.Log("[SHOT] hb " + (Time.unscaledTime - t0).ToString("F1") + "s");
        }
    }

    /// 看门狗：时间线 150s 未完成（编辑器卡死/协程挂起）则强制退出，不留僵死进程。
    IEnumerator Watchdog()
    {
        yield return Wait(150f);
        if (!done)
        {
            Debug.LogError("[SHOT] watchdog：时间线 150s 未完成，强制退出编辑器");
            EditorApplication.Exit(3);
        }
    }

    /// 等待真实秒数（不依赖帧刷新，见 Start 里的说明）。
    private static IEnumerator Wait(float sec) { yield return new WaitForSecondsRealtime(sec); }

    IEnumerator Seq()
    {
        var gm = GameManager.I;
        if (gm == null) { Debug.LogError("[SHOT] GameManager 未就绪"); EditorApplication.Exit(2); yield break; }

        // v0.44：先把物理步长强制回 2ms 并持久化。时间线 ④ 演示档位会切到 0.5ms，
        // 若一轮没跑完（编辑器卡死被强杀等），PlayerPrefs 留下的 0.5ms 会让下一轮
        // 以 2000Hz 物理启动——22 球 + 127 布料碰撞体在编辑器里首帧慢到像"卡死"
        // （实测连续几轮 ShotTest 间歇性白屏无产出的元凶，见 logs/shot_glass2~6）。
        GameSettings.StepIndex = 2;
        GameSettings.Apply();
        GameSettings.Save();

        // ---- ① 主菜单（等入场运镜 + 菜单淡入完成）----
        // 菜单淡入锚定 Time.timeSinceLevelLoad(3.0~3.8s)，而本协程的 WaitForSecondsRealtime
        // 从播放模式进入就开始走——首次编译/域重载/贴图编译会拖慢场景加载，固定秒数
        // 总会"截在淡入前"（shot_glass8/jelly 两轮复现）。改为直接等 levelLoad 时钟，
        // 与淡入同一时间基准，彻底消除抖动。
        while (Time.timeSinceLevelLoad < 5.2f) yield return null;
        yield return Shot("01_menu");

        // ---- ② 开局后的瞄准 HUD ----
        gm.StartGame();
        yield return Wait(1.4f);
        yield return Shot("02_hud_aim");

        // ---- ③ 设置面板（四行：帧率/分辨率/阴影/物理步长）----
        ToggleSettings();
        yield return Wait(0.8f);
        yield return Shot("03_settings");

        // ---- ④ 物理步长切到 0.5ms（验证档位与文案）----
        GameSettings.StepIndex = 0;
        GameSettings.Apply();
        yield return Wait(0.7f);
        yield return Shot("04_settings_step_0.5ms");

        // ---- ⑤ 切回 2ms 并关闭设置 ----
        GameSettings.StepIndex = 2;
        GameSettings.Apply();
        yield return Wait(0.4f);
        ToggleSettings();
        yield return Wait(0.7f);

        // ---- ⑥ "球在手"：把白球拖到 D 区内偏侧位置 ----
        gm.DragCueBall(new Vector3(-1.20f, 0f, 0.22f));
        yield return Wait(0.7f);
        yield return Shot("05_cue_in_hand");

        // ---- ⑦ 加塞：满低杆（圆盘文字应变「低杆」）----
        var pad = Pad();
        if (pad != null) pad.SetSpin(0f, -1f);
        gm.cueCtl.power = 0.7f;
        yield return Wait(0.7f);
        yield return Shot("06_spin_low");

        // ---- ⑧ 出杆并抓三帧滚动过程（目视验证低杆回拉）----
        gm.Shoot(gm.cueCtl.Dir, 0.7f, -1f, 0f);
        yield return Wait(0.9f);
        yield return Shot("07_shot_t0.9");
        yield return Wait(1.2f);
        yield return Shot("08_shot_t2.1");
        yield return Wait(1.6f);
        yield return Shot("09_shot_t3.7");

        // ---- ⑨ v0.41：袋口特写（验证弧形颚部 + 洞口 + 暗井）----
        // 直接摆放相机（不通过 GameCamera，避免被它的跟球逻辑拉回去）。
        gm.StartGame();                                    // 复位球局，避免上一杆的球散落干扰
        yield return Wait(1.0f);
        var cam = Camera.main;
        if (cam != null)
        {
            // ★ 必须先停掉 GameCamera：它的 Update/LateUpdate 每帧都会把相机拉回
            //   跟球/全景机位，直接摆 transform 会被它立刻覆盖（初版截图因此没切过去）。
            var gc = cam.GetComponent<GameCamera>();
            if (gc != null) gc.enabled = false;
            cam.transform.position = new Vector3(1.62f, 0.22f, 0.70f);
            cam.transform.LookAt(new Vector3(1.79f, 0.005f, 0.895f));
            yield return Wait(0.7f);
            yield return Shot("10_pocket_corner");
            cam.transform.position = new Vector3(-0.02f, 0.20f, 0.72f);
            cam.transform.LookAt(new Vector3(0.0f, 0.005f, 0.90f));
            yield return Wait(0.7f);
            yield return Shot("11_pocket_middle");
        }

        // ---- ⑩ v0.51：胜利结算动画（三帧：卡片 Q 弹中 / 比分跳数+彩纸 / 落定）----
        // 走真实终局路径：公开字段写比分 → 反射调私有 GameOver()（与规则引擎终局同入口），
        // maxBreak 也用反射补上，让结算卡的"最高单杆"行有内容可截。
        var gc2 = Camera.main != null ? Camera.main.GetComponent<GameCamera>() : null;
        if (gc2 != null) gc2.enabled = true;               // 恢复被 ⑨ 停掉的运镜
        gm.StartGame();
        yield return Wait(0.8f);
        gm.scores[0] = 87; gm.scores[1] = 45;
        var mbF = typeof(GameManager).GetField("maxBreak");
        if (mbF != null)
        {
            var arr = (int[])mbF.GetValue(gm);
            if (arr != null) { arr[0] = 62; arr[1] = 38; }
        }
        var goM = typeof(GameManager).GetMethod("GameOver", BindingFlags.NonPublic | BindingFlags.Instance);
        if (goM != null) goM.Invoke(gm, null);
        yield return Wait(0.32f);                          // 卡片弹簧过冲附近
        yield return Shot("12_win_card_pop");
        yield return Wait(0.50f);                          // ~0.82s：跳数进行中、彩纸正落
        yield return Shot("13_win_confetti");
        yield return Wait(1.60f);                          // ~2.42s：彩纸将尽、比分到位、按钮已可点
        yield return Shot("14_win_settled");

        // ---- ⑪ v0.53：双方同意的复位对话框（玩家1 同意 + 玩家2 不同意，待确认态）----
        // 直接驱动 UI（绕开 GameManager.OpenReplaceDialog 的"需已有本杆记录"门禁，
        // 截图只关心版式）；同意状态同时写进 UI 与 GameManager 的镜像字段。
        gm.StartGame();
        yield return Wait(0.6f);
        GameManager.I.ui.ShowReplaceDialog();
        var uiF = typeof(UIManager).GetField("repP1Agree", BindingFlags.NonPublic | BindingFlags.Instance);
        if (uiF != null) uiF.SetValue(GameManager.I.ui, true);
        var uiF2 = typeof(UIManager).GetField("repP2Agree", BindingFlags.NonPublic | BindingFlags.Instance);
        if (uiF2 != null) uiF2.SetValue(GameManager.I.ui, false);
        yield return Wait(0.5f);
        yield return Shot("15_replace_dialog");

        // ---- ⑫ v0.53：Rule 14(b) 三选一选框（判 Miss 后的选择权）----
        GameManager.I.ui.HideReplaceDialog();
        yield return Wait(0.4f);
        GameManager.I.ui.ShowReplayOption();               // 直接弹出选框（真实入口由结算触发）
        yield return Wait(0.6f);
        yield return Shot("16_miss_three_options");

        Debug.Log("[SHOT] DONE");
        done = true;
        yield return Wait(0.3f);
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 抓一张整屏截图（含 overlay UI）。
    /// 实现说明：
    ///   · ScreenCapture.CaptureScreenshot 在 -batchmode 下经常不落盘（实测产物为空），
    ///     故改用 Texture2D.ReadPixels 手动读回帧缓冲，同步写盘、结果确定。
    ///   · 读回前等 0.15s 真实时间（而非 WaitForEndOfFrame）——编辑器窗口失焦时
    ///     帧循环可能停摆，等 EndOfFrame 会永久卡住；等真实时间至少保证不挂死。
    /// </summary>
    private IEnumerator Shot(string name)
    {
        yield return new WaitForSecondsRealtime(0.15f);
        string path = Path.Combine(ShotTest.OutDir, name + ".png");
        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(tex);
        Debug.Log("[SHOT] " + name + " " + Screen.width + "x" + Screen.height +
                  " -> " + (File.Exists(path) ? "OK" : "MISSING"));
    }

    /// <summary>反射调用 UIManager.ToggleSettings（保持"只走公开点击路径"的可信度）。</summary>
    private void ToggleSettings()
    {
        var ui = UI; if (ui == null) return;
        var m = typeof(UIManager).GetMethod("ToggleSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        if (m != null) m.Invoke(ui, null);
    }

    /// <summary>取 UIManager 里的加塞圆盘（私有字段，反射读）。</summary>
    private SpinPad Pad()
    {
        var ui = UI; if (ui == null) return null;
        var f = typeof(UIManager).GetField("spinPad", BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? f.GetValue(ui) as SpinPad : null;
    }
}
