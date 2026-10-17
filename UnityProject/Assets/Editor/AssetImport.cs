// =====================================================================================
// AssetImport.cs —— 资源导入修正（编辑器一次性工具，v0.56）
//
// 用途：新拷入 Resources 的贴图需要非默认的导入设置——
//   · cloth_normal.png / wood_normal.png：切线空间法线贴图（TextureImporterType.NormalMap，
//     线性采样 + Unity 索里重排）。默认 "Default" 类型按 sRGB 采样会破坏法线数据，
//     Standard 的 _BumpMap 槽显示红色并告警。
//   · ball_shadow.png：RGBA + alphaIsTransparency（径向渐变黑斑，贴地假阴影）。
//
// 运行（命令行一次性）：
//   Unity.exe -batchmode -quit -nographics -projectPath E:\Snooker3D
//     -executeMethod AssetImport.Fix -logFile E:\Snooker\logs\import.log
// 幂等：重复执行无副作用。同步到开源仓库工程后也要在该工程跑一次。
// =====================================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AssetImport
{
    public static void Fix()
    {
        FixTexture("Assets/Resources/Textures/cloth_normal.png", TextureImporterType.NormalMap, false);
        FixTexture("Assets/Resources/Textures/wood_normal.png", TextureImporterType.NormalMap, false);
        FixTexture("Assets/Resources/Textures/ball_shadow.png", TextureImporterType.Default, true);
        UnityEngine.Debug.Log("[ASSET IMPORT] done");
        EditorApplication.Exit(0);
    }

    private static void FixTexture(string path, TextureImporterType type, bool alphaTransparency)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null)
        {
            UnityEngine.Debug.LogWarning("[ASSET IMPORT] missing: " + path);
            return;
        }
        imp.textureType = type;
        imp.sRGBTexture = (type != TextureImporterType.NormalMap);
        imp.alphaIsTransparency = alphaTransparency;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        UnityEngine.Debug.Log("[ASSET IMPORT] " + path + " -> " + type);
    }

    /// 诊断：打印台球厅 FBX 里灯罩相关渲染器的材质名（运行时自发光没生效时先查名字）。
    public static void Diag()
    {
        var hall = UnityEngine.Resources.Load<GameObject>("Models/pool_hall");
        if (hall == null) { UnityEngine.Debug.Log("[DIAG] pool_hall missing"); EditorApplication.Exit(0); return; }
        foreach (var r in hall.GetComponentsInChildren<MeshRenderer>())
        {
            string low = r.name.ToLower();
            if (low.Contains("shade") || low.Contains("lamp") || low.Contains("light") || low.Contains("roof"))
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var m in r.sharedMaterials) names.Add(m != null ? m.name + "/" + m.shader.name : "null");
                UnityEngine.Debug.Log("[DIAG] " + r.name + " -> " + string.Join(" | ", names));
            }
        }
        EditorApplication.Exit(0);
    }

    /// 交互验收入口：打开主场景并保持编辑器运行（不加 -quit / 不 Exit）。
    /// 用法：Unity.exe -projectPath E:\Snooker3D -executeMethod AssetImport.OpenMain
    /// （-openfile 对该工程不生效，README 踩坑 31 的"空场景启动"问题的工作流化解法。）
    public static void OpenMain()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        UnityEngine.Debug.Log("[ASSET IMPORT] Main.unity opened, editor ready for acceptance");
    }
}
