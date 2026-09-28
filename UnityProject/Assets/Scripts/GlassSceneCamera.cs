// =====================================================================================
// GlassSceneCamera.cs —— 液态玻璃折射/反射的确定性场景源（v0.44f）
//
// 挂在主相机上。创建一个跟随主相机的子相机，每帧把场景渲染到【半分辨率】RT，
// 经 SetGlobalTexture("_GlassScene") 交给 LiquidGlass.shader 做实时折射/反射。
//
// 为什么用副相机（三次真机翻车的教训，动这里前必读）：
//   1) GrabPass：MuMu GLES3 抓到黑帧；
//   2) 相机命令缓冲 AfterEverything 里 Blit(CameraTarget→RT)：GLES 上拷到清屏色；
//   3) OnRenderImage：同一 APK 不同次启动时好时坏（间歇黑帧）。
//   三者共同点：都依赖"从帧缓冲拷贝"的时机/语义，在 GLES 驱动上不可控。
//   副相机是多相机渲染（小地图/镜面/传送门的标配），走完全常规的渲染路径，
//   结果确定性 100%；代价是场景每帧重绘一遍，用半分辨率 RT 摊薄成本。
//
// 注意：UI 画布是 ScreenSpaceOverlay，不归相机渲染 → RT 里天然没有 UI，不会递归。
// =====================================================================================
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class GlassSceneCamera : MonoBehaviour
{
    private Camera glassCam;
    private RenderTexture rt;

    private int frameTick;

    void LateUpdate()
    {
        // 隔帧渲染：折射源 30Hz 更新肉眼不可辨，场景重绘成本直接减半
        frameTick++;
        glassCam.enabled = (frameTick % 2 == 0);
    }

    void Start()
    {
        var main = GetComponent<Camera>();

        var go = new GameObject("GlassSceneCam");
        glassCam = go.AddComponent<Camera>();
        glassCam.CopyFrom(main);                       // 复制 FOV/裁剪面/清屏色/剔除遮罩
        glassCam.transform.SetParent(main.transform, false);   // 跟随主相机位姿
        glassCam.transform.localPosition = Vector3.zero;
        glassCam.transform.localRotation = Quaternion.identity;
        glassCam.targetTexture = MakeRT();
        glassCam.depth = main.depth - 1f;              // 先于主相机渲染
        glassCam.allowMSAA = false;
        glassCam.useOcclusionCulling = false;
        glassCam.enabled = true;
    }

    private RenderTexture MakeRT()
    {
        int w = Mathf.Max(2, Screen.width / 2);        // 半分辨率：折射/光晕足够，成本减半
        int h = Mathf.Max(2, Screen.height / 2);
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "GlassScene" };
        Shader.SetGlobalTexture("_GlassScene", rt);
        Shader.SetGlobalVector("_GlassScene_TexelSize", new Vector4(w, h, 1f / w, 1f / h));
        return rt;
    }
}
