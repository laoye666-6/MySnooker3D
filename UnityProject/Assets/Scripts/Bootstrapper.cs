// =====================================================================================
// Bootstrapper.cs —— 场景自举器：游戏启动时用纯代码搭出整个场景
//
// 挂在场景里唯一一个预置物体 "Bootstrapper" 上。场景本身是空的，
// Awake/Init() 会依次创建：物理参数 → 灯光 → 相机 → 球桌模型与碰撞体 → 地板
//   → GameManager 与 22 颗球 → 球杆与瞄准线 → 中文 UI。
// 这样做的好处：场景不需要在编辑器里手工搭建，全部逻辑可版本化、可脚本构建。
//
// 注意：explicit public Init() 是为编辑器离线测试（PhysTest）准备的——
// executeMethod 模式下 AddComponent 不触发 Awake，测试代码需手动调 Init()。
// =====================================================================================
using UnityEngine;
using UnityEngine.Rendering;

public class Bootstrapper : MonoBehaviour
{
    // 台呢/库边的物理材质（BuildTablePhysics 里创建并缓存，供 AddBox/AddCush/AddJaw 使用）
    private PhysicMaterial clothPM, cushPM;

    void Awake() { Init(); }

    /// 场景搭建主流程（顺序敏感：GameManager/UI 互相引用，先后不能颠倒）。
    public void Init()
    {
        // -----------------------------------------------------------------
        // 一、全局运行参数
        // -----------------------------------------------------------------
        Application.targetFrameRate = 60;          // 占位，Init 尾部按 GameSettings 应用实际档位
        Time.fixedDeltaTime = 0.004f;              // 物理步长 4ms=250Hz（v0.29：由 5ms 缩短，高速碰撞更精确）
        Physics.defaultSolverIterations = 14;      // 物理求解器迭代数（默认 6）：球堆挤压更稳定
        Physics.defaultSolverVelocityIterations = 4; // 速度求解迭代（默认 1）：反弹速度更准

        // 接触偏移 0.8mm（默认 10mm）：
        // 默认值会让 1.5mm 间距的红球堆在生成时就互相"预接触"，开球瞬间炸堆。
        Physics.defaultContactOffset = 0.0008f;
        // 去穿透最大速度 1.5 m/s（默认 1e32）：限制重叠解算的弹开速度，防重叠爆炸。
        Physics.defaultMaxDepenetrationVelocity = 1.5f;
        // 反弹速度阈值 0.1 m/s（默认 2，历史值 0.5）：
        // PhysX 规定法向相对速度低于该阈值的接触"不施加弹性"（反弹被吞掉）。
        // 阈值太高时，低速撞库/掠射撞库的球会粘在库边只能沿库滑行，无法弹开。
        // 0.1 让几乎所有可见的撞库都正常反弹，同时仍能保证静止球贴库/球堆不抖动。
        Physics.bounceThreshold = 0.1f;
        Physics.gravity = new Vector3(0f, -9.81f, 0f);   // 标准重力
        Physics.autoSyncTransforms = true;               // transform 改动立即同步给物理（防状态不同步）
        // 物理模拟模式强制为 FixedUpdate：
        // 历史教训——编辑器里跑过 PhysTest(autoSimulation=false) 后该设置被持久化进
        // DynamicsManager.asset 并打进包，设备上物理完全不步进（球冻住）。这里运行时再兜底一次。
        Physics.simulationMode = SimulationMode.FixedUpdate;

        // 画质：开软阴影（球与桌腿投影增强立体感）、阴影距离 12m、2 倍 MSAA 抗锯齿
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowDistance = 12f;
        QualitySettings.antiAliasing = 2;

        Lighting();
        Camera();
        var mats = Materials();

        // -----------------------------------------------------------------
        // 二、球桌视觉模型（Blender 导出的 OBJ，放 Resources/Models 下）
        // -----------------------------------------------------------------
        var tablePrefab = Resources.Load<GameObject>("Models/table");
        if (tablePrefab != null)
        {
            var table = Instantiate(tablePrefab);
            table.name = "TableModel";
            // OBJ 里的开球线/D 区/置球点烘焙时 X 方向与物理坐标镜像，
            // 绕 Y 转 180° 对齐（袋口/库边本身对称，旋转不影响碰撞一致性）。
            table.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            // 遍历所有网格渲染器按材质名重新赋材质：
            // Unity 的 OBJ 导入器会把整桌合并成单个 "default" 物体、带 5 个材质槽
            // (Cloth/Cushion/Wood/Pocket/Mark)，必须给【所有槽】赋值，只赋 sharedMaterial
            // 会导致台面变成白色兜底材质。
            var names = new System.Text.StringBuilder();     // 记录导入结构，打日志核对
            foreach (var r in table.GetComponentsInChildren<MeshRenderer>())
            {
                names.Append(r.gameObject.name).Append("(");
                var imported = r.sharedMaterials;
                for (int k = 0; k < imported.Length; k++)
                {
                    string im = imported[k] != null ? imported[k].name : "none";
                    names.Append(im).Append(k < imported.Length - 1 ? "," : ")");
                    // 优先按导入材质名匹配（稳定）；匹配不上再按物体名前缀兜底
                    Material m =
                        im == "Cloth" ? mats.cloth :         // 台呢 → 绿
                        im == "Cushion" ? mats.cushion :     // 库边 → 深绿
                        im == "Wood" ? mats.wood :           // 桌框/腿 → 木色
                        im == "Pocket" ? mats.dark :         // 袋口 → 黑
                        im == "Mark" ? mats.mark :           // 标线/点 → 白
                        (r.gameObject.name.StartsWith("Cloth") ? mats.cloth :
                         r.gameObject.name.StartsWith("Cushion") ? mats.cushion :
                         (r.gameObject.name.StartsWith("Frame") || r.gameObject.name.StartsWith("Leg")) ? mats.wood :
                         r.gameObject.name.StartsWith("Pocket") ? mats.dark : mats.mark);
                    imported[k] = m;
                }
                r.sharedMaterials = imported;
            }
            Debug.Log("[SNOOKER] table meshes: " + names);
        }
        else Debug.LogError("[SNOOKER] table.obj not found in Resources/Models");

        // -----------------------------------------------------------------
        // 三、地板（桌面底下的深色地毯，承接出界球；出界球会滚落其上）
        // -----------------------------------------------------------------
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(9f, 1f, 7f);      // Plane 原始 10m，缩放后 90×70m
        floor.transform.position = new Vector3(0f, -0.72f, 0f);    // 桌腿底端所在高度
        floor.GetComponent<Renderer>().sharedMaterial = NewMat("Floor", new Color(0.13f, 0.10f, 0.08f), 1f, 0f);

        // -----------------------------------------------------------------
        // 四、球桌物理碰撞体（纯 BoxCollider 组合，与视觉模型分离、由常量驱动）
        // -----------------------------------------------------------------
        BuildTablePhysics();

        // -----------------------------------------------------------------
        // 五、GameManager 与 22 颗球（白×1 + 红×15 + 彩×6）
        // -----------------------------------------------------------------
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.Init();                                  // 编辑模式下 Awake 不触发，手动绑单例

        // 球的物理材质：球-球弹性 0.92（与库边 0.65 平均后 ≈0.78），
        // 摩擦 0.22/0.28 与台呢 0.85 平均后 ≈0.54，让滑动快速过渡成滚动。
        var ballPM = new PhysicMaterial("ballPM")
        {
            dynamicFriction = 0.22f,
            staticFriction = 0.28f,
            bounciness = 0.92f,
            frictionCombine = PhysicMaterialCombine.Average,   // 两材质参数取平均
            bounceCombine = PhysicMaterialCombine.Average
        };

        for (int i = 0; i < 22; i++)
        {
            // i=0 白球；i=1..15 红球；i=16..21 映射到 Yellow(2)~Black(7)
            BallKind kind = i == 0 ? BallKind.Cue : (i <= 15 ? BallKind.Red : (BallKind)(i - 15 + 1));
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball_" + kind + "_" + i;
            go.transform.localScale = Vector3.one * G.BallR * 2f;    // 单位球缩放成 52.5mm
            go.GetComponent<Renderer>().sharedMaterial = NewMat("Ball_" + kind, G.BallColor(kind), 0.65f, 0f);
            // v0.29 球体材质（哑光版）：粗糙度 0.65 + 金属度 0 —— 哑光酚醛树脂观感，
            // 无刺眼高光、漫反射为主；旧参数(0.25, 0.55)金属感过重，中间版(0.12,0)高光过锐。
            var col = go.GetComponent<SphereCollider>();
            col.material = ballPM;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.17f;                                          // 真实斯诺克球约 170g
            rb.drag = 0f;                                             // 线性阻尼 0：滚动阻力由脚本给
            rb.angularDrag = 0.08f;                                   // 轻微角阻尼
            rb.interpolation = RigidbodyInterpolation.Interpolate;    // 插值：渲染平滑不抖
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 连续碰撞防高速穿透
            var bc = go.AddComponent<BallController>();
            bc.Init();                                                // 编辑模式下手动缓存刚体
            bc.kind = kind;
            gm.balls.Add(bc);
            if (kind == BallKind.Cue) gm.cue = bc;                    // 第 0 颗是白球
        }

        // -----------------------------------------------------------------
        // 六、球杆与瞄准线（同一物体挂两个组件）
        // -----------------------------------------------------------------
        var cueGo = new GameObject("CueController", typeof(CueController), typeof(AimLine));
        var cc = cueGo.GetComponent<CueController>();
        var stickPrefab = Resources.Load<GameObject>("Models/cue");
        if (stickPrefab != null)
        {
            var stick = Instantiate(stickPrefab);
            stick.name = "CueStick";
            // 按子物体名上色：杆尾深色、皮头黑、先角白、杆身木色
            foreach (var r in stick.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.gameObject.name;
                Material m =
                    n.StartsWith("CueButt") ? mats.butt :
                    n.StartsWith("Ferrule") ? mats.mark :
                    n.StartsWith("CueTip") ? mats.dark : mats.shaft;
                r.sharedMaterial = m;
            }
            cc.SetStick(stick);
        }
        cueGo.GetComponent<AimLine>().Setup(GhostMat());   // 幽灵球半透明材质

        // -----------------------------------------------------------------
        // 七、UI（中文记分板 + 菜单，详见 UIManager）
        // -----------------------------------------------------------------
        var uiGo = new GameObject("UI", typeof(UIManager));
        var ui = uiGo.GetComponent<UIManager>();
        gm.ui = ui;
        gm.cueCtl = cc;                             // 先互相注入再 Build（Build 里要用到 cc）
        ui.Build();

        // 用户设置最后应用（帧率/分辨率/阴影），覆盖上方的占位与默认画质
        GameSettings.CaptureNative();
        GameSettings.Load();
        GameSettings.Apply();

        Debug.Log("[SNOOKER] Bootstrap done");
    }

    /// 灯光：一盏主平行光（带软阴影）+ 一盏反向补光（提亮背光面），环境光冷灰。
    void Lighting()
    {
        var sunGo = new GameObject("SunLight");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);   // 从右上前方 55° 俯照
        sun.intensity = 1.05f;
        sun.shadows = LightShadows.Soft;

        var fillGo = new GameObject("FillLight");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fillGo.transform.rotation = Quaternion.Euler(35f, 145f, 0f);  // 从反方向补光
        fill.intensity = 0.35f;

        RenderSettings.ambientMode = AmbientMode.Flat;                // 平坦环境光
        RenderSettings.ambientLight = new Color(0.34f, 0.37f, 0.42f); // 冷灰色环境光
        RenderSettings.fog = false;
    }

    /// 主相机参数：近裁剪 0.01（球杆贴球时不穿帮）、远裁剪 60、深灰蓝背景色。
    void Camera()
    {
        var camGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener), typeof(GameCamera));
        var cam = camGo.GetComponent<Camera>();
        cam.fieldOfView = 52f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.055f, 0.07f, 0.09f);
    }

    /// 场景用到的所有 Standard 材质的集合（集中创建，避免重复 new）。
    private struct MatSet
    {
        public Material cloth, cushion, wood, dark, mark, shaft, butt;
    }

    private MatSet Materials()
    {
        // 台呢布纹贴图（Blender 烘焙，1024²=1m×1m 无缝平铺，Resources/Textures 下）。
        // 有贴图时颜色改白色由贴图提供绿色；没有时退回纯色，保证旧资源也能跑。
        var clothTex = Resources.Load<Texture2D>("Textures/cloth_texture");
        var cloth = NewMat("Cloth", clothTex != null ? Color.white : G.ClothCol, 0.95f, 0f);
        if (clothTex != null) cloth.mainTexture = clothTex;      // UV 按米平铺（cube project 1.0）
        // 库边用同一张贴图乘一个略深的冷色调，与台呢区分
        var cushion = NewMat("Cushion", clothTex != null ? new Color(0.82f, 0.92f, 0.86f) : G.CushionCol, 0.9f, 0f);
        if (clothTex != null) cushion.mainTexture = clothTex;

        var woodTex = Resources.Load<Texture2D>("Textures/wood_texture");
        // 桌框/桌腿木纹贴图（v0.31，Blender 程序化木纹烘焙，UV 按米平铺）；无贴图退回纯色
        var wood = NewMat("Wood", woodTex != null ? new Color(1.35f, 1.2f, 1.1f) : G.WoodCol, 0.45f, 0f);
        if (woodTex != null) wood.mainTexture = woodTex;

        return new MatSet
        {
            cloth = cloth,
            cushion = cushion,
            wood = wood,
            dark = NewMat("PocketDark", new Color(0.02f, 0.02f, 0.02f), 0.8f, 0f),
            mark = NewMat("Mark", new Color(0.92f, 0.92f, 0.88f), 0.8f, 0f),
            shaft = NewMat("CueShaft", new Color(0.80f, 0.58f, 0.35f), 0.3f, 0.1f),
            butt = NewMat("CueButt", new Color(0.10f, 0.06f, 0.04f), 0.3f, 0.1f)
        };
    }

    /// 建一个 Standard 材质。rough：粗糙度(0~1，越大越哑光)；metal：金属度(0~1)。
    private Material NewMat(string name, Color c, float rough, float metal)
    {
        var m = new Material(Shader.Find("Standard"));
        m.name = name;
        m.color = c;
        m.SetFloat("_Glossiness", 1f - rough);      // Standard 里 Smoothness = 1 - 粗糙度
        m.SetFloat("_Metallic", metal);
        return m;
    }

    /// 幽灵球材质：半透明白（Alpha 混合、不写深度、渲染队列 3000 = 透明）。
    private Material GhostMat()
    {
        var m = NewMat("Ghost", new Color(1f, 1f, 1f, 0.35f), 0.5f, 0f);
        m.SetFloat("_Mode", 3f);                    // Standard 的 Alpha Blend 模式
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }

    // ---------------------------------------------------------------------------------
    // 球桌物理：1 块台面 + 6 段库边 + 12 个袋口斜块（jaw），全部由 G 常量驱动，
    // 与 Blender 视觉模型尺寸一一对应。
    // ---------------------------------------------------------------------------------
    void BuildTablePhysics()
    {
        // 台呢：摩擦大（球滚动靠脚本减速度，滑动快速转滚动）、几乎不反弹
        clothPM = new PhysicMaterial("clothPM") { dynamicFriction = 0.85f, staticFriction = 0.9f, bounciness = 0.02f };
        // 库边：摩擦小（减少切库时的速度损失）、弹性 0.65（与球 0.92 平均 ≈ 0.78 真实反弹）
        cushPM = new PhysicMaterial("cushPM") { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0.65f };

        var tp = new GameObject("TablePhysics");

        // 台面碰撞盒：顶面在 y=0，比视觉台呢大一圈（每边多 6cm，垫在库边下方）
        AddBox(tp, new Vector3(0f, -0.025f, 0f),
            new Vector3(2f * (G.HalfL + 0.06f), 0.05f, 2f * (G.HalfW + 0.06f)), clothPM);

        // 6 段库边（true=长库沿 X，false=短库沿 Z）：
        //   长库每边 2 段（中袋两侧各一），短库每边 1 段
        AddCush(tp, true, +1, G.CenGap, G.HalfL - G.CornGap);           // +Z 长库右段
        AddCush(tp, true, +1, -(G.HalfL - G.CornGap), -G.CenGap);       // +Z 长库左段
        AddCush(tp, true, -1, G.CenGap, G.HalfL - G.CornGap);           // -Z 长库右段
        AddCush(tp, true, -1, -(G.HalfL - G.CornGap), -G.CenGap);       // -Z 长库左段
        AddCush(tp, false, +1, -(G.HalfW - G.CornGap), G.HalfW - G.CornGap);  // +X 短库
        AddCush(tp, false, -1, -(G.HalfW - G.CornGap), G.HalfW - G.CornGap);  // -X 短库

        // 12 个袋口斜块：每段库边的两端各一个 45° 导向斜面，把贴库球导入袋口
        for (int s = -1; s <= 1; s += 2)                     // s：库边在哪一侧（+1/-1）
        {
            AddJaw(tp, true, s, G.HalfL - G.CornGap, +1);    // 长库角袋端
            AddJaw(tp, true, s, -(G.HalfL - G.CornGap), -1);
            AddJaw(tp, true, s, G.CenGap, -1);               // 长库中袋端
            AddJaw(tp, true, s, -G.CenGap, +1);
            AddJaw(tp, false, s, G.HalfW - G.CornGap, +1);   // 短库角袋端
            AddJaw(tp, false, s, -(G.HalfW - G.CornGap), -1);
        }
    }

    /// 通用碰撞盒添加（台面用）。
    void AddBox(GameObject tp, Vector3 center, Vector3 size, PhysicMaterial pm)
    {
        var c = tp.AddComponent<BoxCollider>();
        c.center = center;
        c.size = size;
        c.material = pm;
    }

    /// 库边碰撞盒：长库沿 X 延伸（z 方向厚度 CushD），短库沿 Z 延伸（x 方向厚度 CushD）。
    /// 高度都是 CushTop（0.034m），顶面略高于球心，与真实库边接触点一致。
    void AddCush(GameObject tp, bool longRail, int s, float u0, float u1)
    {
        float mid = (u0 + u1) / 2f;
        float len = Mathf.Abs(u1 - u0);
        var c = tp.AddComponent<BoxCollider>();
        if (longRail)
        {
            c.center = new Vector3(mid, G.CushTop / 2f, s * (G.HalfW + G.CushD / 2f));
            c.size = new Vector3(len, G.CushTop, G.CushD);
        }
        else
        {
            c.center = new Vector3(s * (G.HalfL + G.CushD / 2f), G.CushTop / 2f, mid);
            c.size = new Vector3(G.CushD, G.CushTop, len);
        }
        c.material = cushPM;
    }

    /// <summary>
    /// 袋口斜块（jaw）：一个细长盒子绕 Y 旋转成 45° 斜面，摆在每个库边端头的袋口处，
    /// 把沿库滚来的球往袋里导向。放在独立子物体上——BoxCollider 的 center 会随
    /// transform 旋转，直接在父物体上旋转会把碰撞盒转跑。
    /// 参数：longRail 是否长库；sideSign 库边一侧(±1)；endU 库边端头坐标；endSign 端头方向(±1)。
    /// </summary>
    void AddJaw(GameObject tp, bool longRail, int sideSign, float endU, int endSign)
    {
        float jx = 0.050f;                            // 斜切深度（与 Blender 的 JawDx 一致）
        float dx, dz;                                 // 斜块的朝向向量（斜面长轴）
        if (longRail)
        {
            dx = -endSign * jx;                       // 长库：斜向 X 收进
            dz = sideSign * G.CushD;                  //        Z 方向为库厚
        }
        else
        {
            dx = sideSign * G.CushD;
            dz = -endSign * jx;
        }
        var go = new GameObject("jaw");
        go.transform.SetParent(tp.transform, false);
        // 位置 = 端头坐标 + 朝向一半（即斜块几何中心）
        go.transform.localPosition = longRail
            ? new Vector3(endU + dx / 2f, G.CushTop / 2f, sideSign * (G.HalfW + G.CushD / 2f))
            : new Vector3(sideSign * (G.HalfL + G.CushD / 2f), G.CushTop / 2f, endU + dz / 2f);
        // 旋转：让盒子的局部 +X 长轴对准 (dx,dz) 方向
        go.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg, 0f);
        var c = go.AddComponent<BoxCollider>();
        c.size = new Vector3(Mathf.Sqrt(dx * dx + dz * dz), G.CushTop, 0.012f);   // 长=斜边长，厚 12mm
        c.material = cushPM;
    }
}
