// =====================================================================================
// GlassSceneCamera.cs —— 液态玻璃折射/磨砂的确定性场景源（v0.47）
//
// 挂在主相机上。创建一个跟随主相机的子相机，每帧(隔帧)把场景渲染到【半分辨率】RT：
//   _GlassScene —— 原始半分辨率场景（清晰折射层）
//   _GlassBlur  —— 四分之一分辨率 + 两轮分离高斯（磨砂主体，LiquidGlass 采样）
//
// 为什么用副相机（三次真机翻车的教训，动这里前必读）：
//   1) GrabPass：MuMu GLES3 抓到黑帧；
//   2) 相机命令缓冲 AfterEverything 里 Blit(CameraTarget→RT)：GLES 上拷到清屏色；
//   3) OnRenderImage：同一 APK 不同次启动时好时坏（间歇黑帧）。
//   三者共同点：都依赖"从帧缓冲拷贝"的时机/语义，在 GLES 驱动上不可控。
//   副相机是多相机渲染（小地图/镜面/传送门的标配），走完全常规的渲染路径，
//   结果确定性 100%；代价是场景每帧重绘一遍，用半分辨率 RT 摊薄成本。
//   v0.47 的磨砂链用 RT→RT 的 Graphics.Blit（常规路径，非帧缓冲拷贝），
//   在相机渲染完成后的下一帧做（磨砂源 30Hz + 1 帧延迟，肉眼不可辨）。
//
// 注意：UI 画布是 ScreenSpaceOverlay，不归相机渲染 → RT 里天然没有 UI，不会递归。
// =====================================================================================
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class GlassSceneCamera : MonoBehaviour
{
    private Camera glassCam;
    private RenderTexture rt;                 // 半分辨率原始场景
    private RenderTexture qa, qb;             // 四分辨率模糊交换链
    private Material blurMat;

    private int frameTick;

    void LateUpdate()
    {
        // 隔帧渲染：折射源 30Hz 更新肉眼不可辨，场景重绘成本直接减半
        frameTick++;
        bool render = (frameTick % 2 == 0);
        glassCam.enabled = render;
        // 相机关闭的那一帧，对【上一帧】的场景 RT 做磨砂模糊（RT→RT Blit，确定性路径）
        if (!render && rt != null) BlurChain();
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

        var bs = Resources.Load<Shader>("Shaders/GlassBlur");
        if (bs != null) blurMat = new Material(bs);
        else Debug.LogWarning("[GLASS] GlassBlur shader 缺失，磨砂层退化为半分辨率原图");
        MakeBlurRT();
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

    /// 四分辨率磨砂 RT（兜底：创建失败时 _GlassBlur 指向原始 RT，只是磨砂弱一些）
    private void MakeBlurRT()
    {
        int w = Mathf.Max(2, Screen.width / 4);
        int h = Mathf.Max(2, Screen.height / 4);
        qa = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "GlassBlurA" };
        qb = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "GlassBlurB" };
        PublishBlur(qb);
    }

    /// 对上一帧的半分辨率 RT 做两轮分离高斯（降采样在第一次 Blit 顺带完成）
    private void BlurChain()
    {
        if (blurMat == null || qa == null) { PublishBlur(rt); return; }
        blurMat.SetVector("_Dir", new Vector4(1f, 0f, 0f, 0f));
        Graphics.Blit(rt, qa, blurMat);                // 降采样 + 水平
        blurMat.SetVector("_Dir", new Vector4(0f, 1f, 0f, 0f));
        Graphics.Blit(qa, qb, blurMat);                // 垂直
        blurMat.SetVector("_Dir", new Vector4(1f, 0f, 0f, 0f));
        Graphics.Blit(qb, qa, blurMat);                // 第二轮加宽磨砂半径
        blurMat.SetVector("_Dir", new Vector4(0f, 1f, 0f, 0f));
        Graphics.Blit(qa, qb, blurMat);
        PublishBlur(qb);
    }

    private static void PublishBlur(Texture t)
    {
        Shader.SetGlobalTexture("_GlassBlur", t);
        Shader.SetGlobalVector("_GlassBlur_TexelSize",
            new Vector4(t.width, t.height, 1f / t.width, 1f / t.height));
    }
}
