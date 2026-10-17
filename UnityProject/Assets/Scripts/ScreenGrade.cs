// =====================================================================================
// ScreenGrade.cs —— ACES 全屏调色链（v0.56/57 光影追平）
//
// 架构（纯多相机，本工程唯一被真机验证的确定性路径）：
//   主相机 targetTexture = _sceneRT（全分辨率 + 24bit 深度 + MSAA2）
//     → 展示相机（PresentCam，depth 更高）只渲染一块贴屏四边形，
//       四边形材质（ColorGrade.shader）采样 _sceneRT 并做 ACES + 对比度 + 暗角。
//   四边形放在 layer 3（Unity 默认未占用的层）：
//     主相机 cullingMask 排除 layer 3（不重复画它），
//     展示相机 cullingMask 只含 layer 3（看不到场景，只看到四边形）。
//
//   与被禁路径的区别（README 踩坑 39/43）：GrabPass / 相机命令缓冲 / OnRenderImage
//   的【帧缓冲拷贝】在 MuMu GLES3 间歇黑帧；本链路 = 相机渲 RT + 相机画四边形，
//   与 GlassSceneCamera 同族，不读写帧缓冲。
//
// 注意事项：
//   · 挂在【主相机】上（Bootstrapper.Camera() 里 AddComponent）。
//   · GlassSceneCamera.Start 的 CopyFrom 复制 targetTexture/cullingMask 后会覆写
//     自己的 RT；它继承主相机的 cullingMask（排除 layer 3）正合适——玻璃磨砂源
//     不包含调色四边形。
//   · UI 是 ScreenSpaceOverlay，在所有相机之后绘制，不受调色影响（白字仍是白字）。
//   · 分辨率变化（GameSettings.Apply 的 Screen.SetResolution）由 Update 轮询重建 RT。
//   · 编辑器 Game 视图必须存在一个渲到屏幕的相机（本组件的展示相机），
//     否则显示 "Display 1 No cameras rendering"（初版 targetTexture 方案的教训）。
// =====================================================================================
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ScreenGrade : MonoBehaviour
{
    private const int QuadLayer = 3;          // Unity 默认未占用的层（0 默认/1 透明FX/2 忽略射线/4 水/5 UI）

    private RenderTexture sceneRT;            // 主相机的渲染目标（未调色）
    private GameObject presentGo;             // 展示相机（只画调色四边形）
    private GameObject gradeQuad;             // 调色四边形（尺寸随屏幕宽高比刷新）
    private Camera presentCam;
    private Material gradeMat;
    private int builtW, builtH;

    void Start()
    {
        var main = GetComponent<Camera>();

        gradeMat = MakeGradeMaterial();

        // 主相机：渲进 RT，且不再画调色四边形所在层
        main.cullingMask &= ~(1 << QuadLayer);

        // 展示相机：只画 layer 3，正交贴满屏幕，渲到屏幕
        presentGo = new GameObject("PresentCamera");
        presentGo.transform.SetParent(main.transform, false);
        presentGo.transform.localPosition = new Vector3(0f, 0f, 1f);
        presentCam = presentGo.AddComponent<Camera>();
        presentCam.CopyFrom(main);
        presentCam.cullingMask = 1 << QuadLayer;
        presentCam.orthographic = true;
        presentCam.orthographicSize = 0.5f;
        presentCam.nearClipPlane = 0.01f;
        presentCam.farClipPlane = 5f;
        presentCam.clearFlags = CameraClearFlags.SolidColor;
        presentCam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
        presentCam.targetTexture = null;                 // 渲到屏幕
        presentCam.depth = main.depth + 10f;             // 最后渲
        presentCam.allowMSAA = false;
        presentCam.useOcclusionCulling = false;

        RebuildRT();
        MakeQuad();
    }

    void OnDestroy()
    {
        var cam = GetComponent<Camera>();
        if (cam != null) cam.targetTexture = null;       // 还原，防编辑器残留
        if (presentGo != null) Destroy(presentGo);
        if (sceneRT != null) { sceneRT.Release(); Destroy(sceneRT); sceneRT = null; }
    }

    void Update()
    {
        // 分辨率档位/窗口尺寸变化 → 重建 RT（四边形材质引用随重建刷新）
        if (Screen.width != builtW || Screen.height != builtH) RebuildRT();
    }

    private void RebuildRT()
    {
        int w = Mathf.Max(320, Screen.width);
        int h = Mathf.Max(180, Screen.height);
        var old = sceneRT;
        sceneRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "GradeScene",
            antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
        };
        sceneRT.Create();
        GetComponent<Camera>().targetTexture = sceneRT;
        if (gradeMat != null) gradeMat.mainTexture = sceneRT;
        builtW = w; builtH = h;
        if (old != null) { old.Release(); Destroy(old); }
        // v0.57 关键：正交相机的视野宽度 = 高度 × 屏幕宽高比——四边形必须按同一
        // 比例拉伸，否则图像在宽屏上被纵向拉长（实测 1146×517 下球变成竖椭圆）。
        if (gradeQuad != null)
        {
            float aspect = (float)w / h;
            gradeQuad.transform.localScale = new Vector3(aspect, 1f, 1f);
        }
        Debug.Log("[GRADE] sceneRT " + w + "x" + h + " msaa=" + sceneRT.antiAliasing);
    }

    /// 调色四边形：贴在展示相机正前方，UV 0..1 铺满，材质完成 ACES 调色。
    /// 放在 layer 3 —— 主相机不画它、展示相机只画它。
    private void MakeQuad()
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "GradeQuad";
        quad.layer = QuadLayer;
        quad.GetComponent<Renderer>().sharedMaterial = gradeMat;
        quad.transform.SetParent(presentGo.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 2f);
        gradeQuad = quad;
        // 注意时序：本方法在 RebuildRT() 之后调用，RebuildRT 里的宽高比赋值
        // 已经过去（当时 gradeQuad 还是 null）——必须在这里就地补尺寸，
        // 否则四边形保持 1×1：宽屏上居中成方块、两侧黑边 + 图像横向压缩（实测）。
        float aspect = (float)builtW / Mathf.Max(1, builtH);
        quad.transform.localScale = new Vector3(aspect, 1f, 1f);
    }

    private static Material MakeGradeMaterial()
    {
        var sh = Resources.Load<Shader>("Shaders/ColorGrade");
        if (sh == null)
        {
            Debug.LogWarning("[GRADE] ColorGrade.shader 缺失（应位于 Assets/Resources/Shaders/），画面不调色");
            return null;
        }
        return new Material(sh);
    }
}
