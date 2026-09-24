// =====================================================================================
// BuildGame.cs —— 命令行构建入口（Editor 专用，不会打进 APK）
//
// 用法（在 cmd 中执行，构建日志看 -logFile 指向的文件）：
//   "E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
//     -projectPath "E:\Snooker3D" ^
//     -x86only ^                                  ← 可选：只编 x86_64，模拟器调试用（IL2CPP 时间减半）
//     -executeMethod BuildGame.BuildAndroid ^
//     -logFile "E:\Snooker\logs\build.log"
//
// 产物：E:\Snooker3D\Builds\Snooker3D.apk（默认 ARM64 + X86_64 双架构）
//
// 本方法内完成四件事：
//   1. 空场景 + 只挂一个 Bootstrapper（游戏内容全部运行时自建）并保存
//   2. 写入 Android 播放器设置（包名/后端/架构/图形 API 等）
//   3. 切换构建目标（必须先切换，否则后面的 Android 设置会被重置！）
//   4. BuildPipeline.BuildPlayer 打包；成功 Exit(0)，失败 Exit(1)（cmd 可判断）
// =====================================================================================
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildGame
{
    public static void BuildAndroid()
    {
        // 解析命令行：带 -x86only 参数时只编 x86_64（模拟器调试用，IL2CPP 编译时间减半）
        bool x86only = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-x86only") >= 0;
        Debug.Log("[SNOOKER] BuildAndroid start x86only=" + x86only);

        // ---- 1. 构建用场景：空场景 + Bootstrapper（一切内容运行时自建）----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Bootstrapper", typeof(Bootstrapper));
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");

        // ---- 2. 播放器设置 ----
        // 应用图标（Blender 渲染的红球图标，Assets/Icons/icon.png）
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/icon.png");
        if (icon != null)
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Android, new[] { icon },
                IconKind.Application);
        else
            Debug.LogWarning("[SNOOKER] icon.png not found, using default icon");
        PlayerSettings.companyName = "SnookerLab";
        PlayerSettings.productName = "Snooker3D";
        // 版本 0.42（第 42 次迭代）：对外显示的迭代版本号；versionCode 递增供安装覆盖
        PlayerSettings.bundleVersion = "0.42";
        PlayerSettings.Android.bundleVersionCode = 42;

        // ★ 必须先切构建目标再写 Android 设置！
        //   顺序反了的话切换目标会把 Android 专属设置（架构/后端等）重置为默认，
        //   曾经导致 APK 只含 armeabi-v7a、MuMu 拒绝安装。
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.snookerlab.snooker3d");
        // IL2CPP：MuMu 模拟器的 ARM 翻译层只支持 arm64-v8a，Unity 的 Mono 后端又只能出
        // ARMv7 —— 只有 IL2CPP 能同时产出 ARM64 与 x86_64。
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        // 关闭引擎代码裁剪：默认裁剪会把 CreatePrimitive 内部用到的
        // SphereCollider/MeshCollider 剥掉，运行时报 "class doesn't exist"。
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;  // Android 7.0+
        // 目标架构：真机走 ARM64；MuMu 走 X86_64（原生执行，不吃翻译性能损耗）
        PlayerSettings.Android.targetArchitectures = x86only
            ? AndroidArchitecture.X86_64
            : (AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64);
        Debug.Log("[SNOOKER] targetArchitectures=" + PlayerSettings.Android.targetArchitectures +
                  " backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android));

        // 横屏（左右均可），不响应竖屏旋转
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.colorSpace = ColorSpace.Gamma;    // Gamma 色彩空间：兼容性更好、性能略高

        // 图形 API 强制 GLES3：模拟器的 Vulkan 兼容层不稳定，GLES3 最稳
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        // ---- 2b. 播放器设置读回校验（v0.34）----
        // 上面这些设置若静默失败，旧版只会留一条 warning 然后照样 Exit(0)——打出一个装不上
        // 或跑不动的包（历史上就出过"只含 armeabi-v7a 被 MuMu 拒装"）。所以设置完立即读回。
        var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        var icons = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Android, IconKind.Application);
        int iconCount = icons != null ? icons.Length : 0;
        string pkg = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        Debug.Log("[SNOOKER] verify version=" + PlayerSettings.bundleVersion +
                  " versionCode=" + PlayerSettings.Android.bundleVersionCode +
                  " pkg=" + pkg +
                  " arch=" + PlayerSettings.Android.targetArchitectures +
                  " backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) +
                  " gles3=" + (System.Array.IndexOf(apis, GraphicsDeviceType.OpenGLES3) >= 0) +
                  " stripEngineCode=" + PlayerSettings.stripEngineCode +
                  " icons=" + iconCount);

        bool bad = false;
        if (PlayerSettings.bundleVersion != "0.42") { Debug.LogError("[SNOOKER] bundleVersion 未生效"); bad = true; }
        if (PlayerSettings.Android.bundleVersionCode < 42) { Debug.LogError("[SNOOKER] versionCode 未生效"); bad = true; }
        if (pkg != "com.snookerlab.snooker3d") { Debug.LogError("[SNOOKER] 包名未生效: " + pkg); bad = true; }
        if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
        { Debug.LogError("[SNOOKER] 脚本后端不是 IL2CPP（MuMu 会拒装）"); bad = true; }
        if (PlayerSettings.stripEngineCode) { Debug.LogError("[SNOOKER] 引擎裁剪仍开着（会剥掉 Collider）"); bad = true; }
        if (System.Array.IndexOf(apis, GraphicsDeviceType.OpenGLES3) < 0)
        { Debug.LogError("[SNOOKER] 图形 API 未包含 GLES3"); bad = true; }
        if (icon != null && (iconCount == 0 || System.Array.IndexOf(icons, icon) < 0))
            Debug.LogWarning("[SNOOKER] 图标读回未命中预期贴图（count=" + iconCount + "），建议开包核对 res/*.png");
        if (bad) { Debug.Log("[SNOOKER] === ABORTED: 播放器设置未生效，拒绝打出坏包 ==="); EditorApplication.Exit(3); return; }

        // ---- 3. 打包 ----
        string output = @"E:\Snooker3D\Builds\Snooker3D.apk";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, output, BuildTarget.Android, BuildOptions.None);

        // 结果码：成功 Exit(0)，失败 Exit(1)（外层脚本/CI 可判断）
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[SNOOKER] BUILD FAILED: " + report.summary.result + " errors=" + report.summary.totalErrors);
            EditorApplication.Exit(1);
        }
        Debug.Log("[SNOOKER] BUILD OK size=" + report.summary.totalSize + " -> " + output);
        EditorApplication.Exit(0);
    }
}
