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
    // 台呢/库边/袋内壁的物理材质（BuildTablePhysics 里创建并缓存，供 AddBox/AddCush/AddJaw 使用）
    private PhysicMaterial clothPM, cushPM, pocketPM;

    void Awake() { Init(); }

    /// 场景搭建主流程（顺序敏感：GameManager/UI 互相引用，先后不能颠倒）。
    public void Init()
    {
        // -----------------------------------------------------------------
        // 一、全局运行参数
        // -----------------------------------------------------------------
        Application.targetFrameRate = 60;          // 占位，Init 尾部按 GameSettings 应用实际档位
        // v0.36：物理步长 4ms → 2ms（500Hz）。加塞的滑动摩擦模型对步长敏感
        // （打滑阶段每步都要修正线速度与角速度，步长大则自旋修正粗糙）。
        // v0.37：这只是默认值——Init 尾部 GameSettings.Apply() 会按用户存档的
        // 步长档位（0.5/1/2ms）覆盖此处。
        Time.fixedDeltaTime = 0.002f;
        Physics.defaultSolverIterations = 14;      // 物理求解器迭代数（默认 6）：球堆挤压更稳定
        Physics.defaultSolverVelocityIterations = 4; // 速度求解迭代（默认 1）：反弹速度更准

        // 接触偏移 0.8mm（默认 10mm）：
        // 默认值会让 1.5mm 间距的红球堆在生成时就互相"预接触"，开球瞬间炸堆。
        Physics.defaultContactOffset = 0.0008f;
        // 去穿透最大速度 0.6 m/s（默认 1e32）：限制重叠解算的弹开速度，防重叠爆炸。
        // v0.41：1.5 → 0.6。口袋内衬是竖直壁面，5m/s 的球每步走 10mm，难免嵌入一点；
        // 若允许 1.5m/s 的去穿透速度，球会被"顶"到 1.5²/2g = 115mm 高（实测确实如此，
        // 看起来像球被弹出桌外）。0.6 对应 18mm 的弹起，肉眼已不可见，
        // 同时仍远高于解决 1mm 级重叠所需的速度。
        Physics.defaultMaxDepenetrationVelocity = 0.6f;
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
        // v0.36 白球专用材质：摩擦取 0 且用 Minimum 合并 —— 白球的"滑→滚"转换与自旋衰减
        // 全部由 BallController.CueRollStep 的模型负责，若再让 PhysX 施加一遍台呢摩擦，
        // 就会双重计算把加塞效果（尤其低杆的反向拉扯）几乎抹平。弹性仍为 0.92 保持撞库手感。
        var cuePM = new PhysicMaterial("cuePM")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0.92f,
            frictionCombine = PhysicMaterialCombine.Minimum,
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
            col.material = kind == BallKind.Cue ? cuePM : ballPM;      // v0.36：白球用零摩擦材质
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.17f;                                          // 真实斯诺克球约 170g
            rb.drag = 0f;                                             // 线性阻尼 0：滚动阻力由脚本给
            // 角阻尼：普通球 0.08（轻微）；白球 0 —— 自旋衰减完全由 CueRollStep 的物理模型
            // 决定（滑移摩擦 + 侧塞阻尼），否则 PhysX 的角阻尼会额外吃掉加塞效果。
            rb.angularDrag = kind == BallKind.Cue ? 0f : 0.08f;
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
        camGo.tag = "MainCamera";       // v0.36：必须打标签，否则 Camera.main 返回 null
                                        // （SpinPad/拖动白球要把屏幕坐标换算成台面坐标，依赖相机）
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
    // 球桌物理：布料板（挖真实洞口）+ 6 段库边 + 12 个斜颚楔块 + 6 个袋内壁。
    // 全部由 G 常量驱动，与 Blender 视觉模型尺寸一一对应。
    //
    // v0.41 袋口重做的动机（旧版的三个不真实处）：
    //   ① 旧版台面是一整块实体盒，球物理上"掉不进洞" —— 落袋只能靠"球心进捕获圈"的
    //      脚本判定，于是球要么已被判落袋、要么被整块台面挡住，撞颚弹回（晃袋）
    //      在物理上不可能发生。
    //   ② 旧版颚部只是一个 45° 斜块的直棱，没有颚尖圆弧。
    //   ③ 旧版袋内无侧墙，判落袋即关碰撞直接下坠。
    //   现在：台呢挖出真的洞（布料板拼装）、颚部按真实斜颚面摆放楔块、
    //   袋内有侧墙兜住下坠球 —— 落袋/晃袋/挂袋全部由重力与碰撞自然产生。
    // ---------------------------------------------------------------------------------
    void BuildTablePhysics()
    {
        // 台呢：摩擦大（球滚动靠脚本减速度，滑动快速转滚动）、几乎不反弹
        clothPM = new PhysicMaterial("clothPM") { dynamicFriction = 0.85f, staticFriction = 0.9f, bounciness = 0.02f };
        // 库边：摩擦小（减少切库时的速度损失）、弹性 0.65（与球 0.92 平均 ≈ 0.78 真实反弹）
        cushPM = new PhysicMaterial("cushPM") { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0.65f };
        // 袋内壁（皮革/绒布衬里）：比库边"软"得多 —— 摩擦大、几乎不弹。
        // bounceCombine 用 Minimum 且值取 0.15：球的材质是 Average(0.92)，两者合并取 0.15，
        // 撞衬里的球很快停住/掉下去，不会被弹飞出台面。
        // （材质合并取优先级最高的那个模式，Minimum 优先于 Average，故球侧设置不会覆盖它。）
        pocketPM = new PhysicMaterial("pocketPM")
        {
            dynamicFriction = 0.45f,
            staticFriction = 0.55f,
            bounciness = 0.15f,
            bounceCombine = PhysicMaterialCombine.Minimum,
            frictionCombine = PhysicMaterialCombine.Average
        };

        var tp = new GameObject("TablePhysics");

        BuildClothBed(tp);

        // 6 段库边主体（true=长库沿 X，false=短库沿 Z）：两端各缩短 JawDx，
        // 让出的楔形空间由下面的斜颚楔块填上（合起来 = 带斜颚的库边）。
        AddCush(tp, true, +1, G.CenGap + G.JawDx, G.HalfL - G.CornGap - G.JawDx);
        AddCush(tp, true, +1, -(G.HalfL - G.CornGap - G.JawDx), -(G.CenGap + G.JawDx));
        AddCush(tp, true, -1, G.CenGap + G.JawDx, G.HalfL - G.CornGap - G.JawDx);
        AddCush(tp, true, -1, -(G.HalfL - G.CornGap - G.JawDx), -(G.CenGap + G.JawDx));
        AddCush(tp, false, +1, -(G.HalfW - G.CornGap - G.JawDx), G.HalfW - G.CornGap - G.JawDx);
        AddCush(tp, false, -1, -(G.HalfW - G.CornGap - G.JawDx), G.HalfW - G.CornGap - G.JawDx);

        // 12 个斜颚楔块：每段库边两端各一个，把"库边端头 → 颚尖"的斜切面补成实体
        for (int s = -1; s <= 1; s += 2)                          // s：库边在哪一侧（+1/-1）
        {
            AddJaw(tp, true, s, G.HalfL - G.CornGap, +1);         // 长库角袋端
            AddJaw(tp, true, s, -(G.HalfL - G.CornGap), -1);
            AddJaw(tp, true, s, G.CenGap, -1);                    // 长库中袋端
            AddJaw(tp, true, s, -G.CenGap, +1);
            AddJaw(tp, false, s, G.HalfW - G.CornGap, +1);        // 短库角袋端
            AddJaw(tp, false, s, -(G.HalfW - G.CornGap), -1);
        }

        BuildPocketTubes(tp);
    }

    /// <summary>
    /// 台呢布料板：用轴对齐矩形拼出台面，并在 6 个袋口处挖出真实的洞。
    ///
    /// 圆形洞口用 N 条水平带逼近（每条带按该高度的圆半宽去掉一整条）——
    /// 逼近误差 = 带的矢高 ≈ 0.4mm（N=8），远小于球半径，手感上等同于圆孔。
    /// 拼装次序：先按所有带的 z 边界把台面切成水平带，每条带内减去该带被挖掉的
    /// x 区间，剩下的即布料块；最后把 x 区间相同且相邻的块纵向合并，减少碰撞体数量。
    /// </summary>
    void BuildClothBed(GameObject tp)
    {
        // v0.41：布料板**就是台面本身**（库边鼻线围出的矩形），不再向四周多留 6cm。
        // 旧版多留一圈"垫在库边下方"的布料，在四个角袋处会变成一圈裸露在袋口区域的
        // 台呢 —— 现实中袋口外是没有台呢的，而且那圈布料会让球在袋口边缘被托住、
        // 或者被库边鼻面与它夹成的直角挤飞（初版实测球会飞出桌外 3 米）。
        // 库边由自己的碰撞盒支撑，不需要布料垫底。
        float bx0 = -G.HalfL, bx1 = G.HalfL;
        float bz0 = -G.HalfW, bz1 = G.HalfW;

        const int N = 16;                                    // 每个洞的逼近带数（×带宽 8mm→4mm）
        const float BIG = 10f;                               // 等效"无穷远"
        var cuts = new System.Collections.Generic.List<Vector4>();   // (zmin, zmax, xmin, xmax)
        var zEdges = new System.Collections.Generic.List<float>();

        for (int i = 0; i < G.Pockets.Length; i++)
        {
            Vector3 p = G.Pockets[i];
            // 洞口半径比视觉孔大 2mm：球不会卡在布料边缘的锯齿上
            float r = (i < 4 ? G.HoleCornerR : G.HoleCenterR) + 0.002f;
            // v0.41：角袋的布料要**朝台面外侧一直敞开**。否则洞口圆与台面边缘之间
            // 会残留一条布料"唇"，正好挡住沿库滚进角袋的球（初版实测：球心降到
            // -0.02 就被这条唇托住，永远到不了落袋深度）。现实中转角处就是没有台呢的。
            bool corner = i < 4;
            // +x / -x 侧的两个角袋：把布料朝该侧台面外一直敞开
            bool outwardX = corner && (i == 0 || i == 1);
            // 角袋在 z 方向也天然是敞开的（洞口圆的 z 范围已经超出布料板边界），无需额外处理

            float zc0 = Mathf.Max(p.z - r, bz0), zc1 = Mathf.Min(p.z + r, bz1);
            if (zc1 <= zc0) continue;
            for (int k = 0; k < N; k++)
            {
                float za = zc0 + (zc1 - zc0) * k / N;
                float zb = zc0 + (zc1 - zc0) * (k + 1) / N;
                float zm = 0.5f * (za + zb);
                float half = Mathf.Sqrt(Mathf.Max(0f, r * r - (zm - p.z) * (zm - p.z)));
                float cx0 = p.x - half, cx1 = p.x + half;
                if (corner)
                {
                    if (outwardX) cx1 = bx1 + BIG; else cx0 = bx0 - BIG;
                }
                cuts.Add(new Vector4(za, zb, cx0, cx1));
                zEdges.Add(za); zEdges.Add(zb);
            }
        }

        zEdges.Sort();
        var zs = new System.Collections.Generic.List<float>();
        foreach (float z in zEdges)
            if (zs.Count == 0 || z - zs[zs.Count - 1] > 1e-5f) zs.Add(z);

        // 逐水平带求补集，得到布料块
        var boxes = new System.Collections.Generic.List<Vector4>();   // (xmin, xmax, zmin, zmax)
        for (int i = 0; i + 1 < zs.Count; i++)
        {
            float za = zs[i], zb = zs[i + 1];
            if (zb - za < 1e-5f) continue;
            float zc = 0.5f * (za + zb);

            var rem = new System.Collections.Generic.List<Vector2>();
            foreach (var c in cuts)
                if (c.x <= zc && c.y >= zc) rem.Add(new Vector2(c.z, c.w));
            rem.Sort((a, b) => a.x.CompareTo(b.x));

            float cur = bx0;
            foreach (var iv in rem)
            {
                if (iv.x > cur) boxes.Add(new Vector4(cur, iv.x, za, zb));
                if (iv.y > cur) cur = iv.y;
            }
            if (cur < bx1) boxes.Add(new Vector4(cur, bx1, za, zb));
        }

        // 纵向合并同 x 区间的相邻块（袋与袋之间的整片台面合并成一块，碰撞体数量大减）
        boxes.Sort((a, b) =>
        {
            int c = a.x.CompareTo(b.x); if (c != 0) return c;
            c = a.y.CompareTo(b.y); if (c != 0) return c;
            return a.z.CompareTo(b.z);
        });
        var merged = new System.Collections.Generic.List<Vector4>();
        foreach (var b in boxes)
        {
            bool done = false;
            for (int i = merged.Count - 1; i >= 0 && i >= merged.Count - 8; i--)
            {
                var m = merged[i];
                if (Mathf.Abs(m.x - b.x) < 1e-5f && Mathf.Abs(m.y - b.y) < 1e-5f &&
                    Mathf.Abs(m.w - b.z) < 1e-5f)
                { m.w = b.w; merged[i] = m; done = true; break; }
            }
            if (!done) merged.Add(b);
        }

        foreach (var b in merged)
            AddBox(tp, new Vector3(0.5f * (b.x + b.y), -0.025f, 0.5f * (b.z + b.w)),
                new Vector3(b.y - b.x, 0.05f, b.w - b.z), clothPM);

        Debug.Log("[SNOOKER] cloth bed pieces=" + merged.Count);
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
    /// 袋口颚部楔块（v0.41）：把"库边端头 → 颚尖"的颚面补成实体，形状与 Blender 视觉模型
    /// **逐一对应**（同一个圆弧 + 同一个斜颚面）。
    ///
    /// 形状（俯视；u = 沿库边方向，v = 离开鼻线的进深，d = endSign·(ue−u) = 离颚尖的进深）：
    ///   · 颚尖圆弧  d ∈ [0, r]：轮廓 v = r − √(r²−d²)。该圆与鼻线 v=0 **在颚尖相切**，
    ///     所以袋口开口宽度（颚尖到颚尖）与旧版完全相同 —— 改形状不改手感。
    ///   · 斜颚面    d ∈ [r, JawDx]：轮廓 v = d·CushD/JawDx（真实球桌的带角度颚面）。
    ///   现实依据：WPBSA 规则书明确库边端头是"被切成曲线"的 ——
    ///   "The curved face of the cushion ... cut into a curve to form the pocket opening."
    ///   （WPBSA Rulebook 2024-25 §2 Rule 4 "Cushion Faces"）；维基百科另证
    ///   "On snooker ... tables, the pocket entries are rounded"。
    ///
    /// 实现：按 u 把颚面切成 N 片**凸梯形棱柱**（每片从鼻线 v=0 到颚面上界 v_top），
    /// 各用一个 convex MeshCollider。两个"为什么不用别的"：
    ///   · 不用旋转的 BoxCollider 逼近：盒子的直角会伸到鼻线**前方**（台面里）形成幽灵墙，
    ///     把贴库滚向袋口的球挡下来（旧版就是这样，只是旧版还是直棱、没有圆弧）。
    ///   · 不用单个非凸网格：凸块拼接不需要三角化与绕序处理，且凸体求交更稳更省。
    /// 高度从 -PocketWallD 一直到 CushTop：向下延伸是为了兜住正在袋口下坠的球。
    ///
    /// 参数：longRail 是否长库；sideSign 库边一侧(±1)；ue 库边端头坐标；endSign 端头方向(±1)。
    /// </summary>
    void AddJaw(GameObject tp, bool longRail, int sideSign, float ue, int endSign)
    {
        // 该端头朝向角袋还是中袋 → 决定圆弧半径（与 Blender 的 JAW_R_* 同源，见 G.cs）。
        // 长库的角袋端在 ±(HalfL−CornGap)≈±1.71，中袋端在 ±CenGap≈±0.056，阈值取两者之间。
        bool corner = !longRail || Mathf.Abs(ue) > 0.5f;
        float r = corner ? G.JawRCorner : G.JawRCenter;
        // 圆弧半径不得超过库边进深，否则轮廓会切到库边背面之外
        r = Mathf.Min(r, G.CushD * 0.9f);

        const int N = 6;                          // 沿进深方向的切片数（越多越贴近圆弧，6 片足够）
        float yB = -G.PocketWallD, yT = G.CushTop;

        var root = new GameObject("jaw");
        root.transform.SetParent(tp.transform, false);

        for (int k = 0; k < N; k++)
        {
            float d0 = G.JawDx * k / N, d1 = G.JawDx * (k + 1) / N;
            float v0 = JawProfileV(d0, r), v1 = JawProfileV(d1, r);
            float u0 = ue - endSign * d0, u1 = ue - endSign * d1;

            // 俯视四点：(u0,0) (u1,0) (u1,v1) (u0,v0) —— 底(vB) 四个 + 顶(vT) 四个 = 凸棱柱
            var verts = new Vector3[8];
            verts[0] = JawPoint(longRail, sideSign, u0, 0f, yB);
            verts[1] = JawPoint(longRail, sideSign, u1, 0f, yB);
            verts[2] = JawPoint(longRail, sideSign, u1, v1, yB);
            verts[3] = JawPoint(longRail, sideSign, u0, v0, yB);
            verts[4] = JawPoint(longRail, sideSign, u0, 0f, yT);
            verts[5] = JawPoint(longRail, sideSign, u1, 0f, yT);
            verts[6] = JawPoint(longRail, sideSign, u1, v1, yT);
            verts[7] = JawPoint(longRail, sideSign, u0, v0, yT);

            var tris = new int[]
            {
                0, 2, 1,  0, 3, 2,        // 底面
                4, 5, 6,  4, 6, 7,        // 顶面
                0, 1, 5,  0, 5, 4,        // 侧：鼻线面 (v=0)
                1, 2, 6,  1, 6, 5,        // 侧：u1 端面
                2, 3, 7,  2, 7, 6,        // 侧：颚面
                3, 0, 4,  3, 4, 7,        // 侧：u0 端面
            };
            var mesh = new Mesh();
            mesh.name = "jawSlice" + k;
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();

            var slice = new GameObject("jaw");
            slice.transform.SetParent(root.transform, false);
            var mc = slice.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;                     // 凸棱柱：用凸包精确求交
            mc.material = cushPM;
        }
    }

    /// <summary>
    /// 颚面轮廓：给定离颚尖的进深 d（≥0，沿库边方向），返回该处的进深 v（离开鼻线）。
    /// = min(斜颚面, 颚尖圆弧)，与 Blender 里"斜切 + 圆柱布尔"的结果一致。
    /// </summary>
    static float JawProfileV(float d, float r)
    {
        float vSlant = d * G.CushD / G.JawDx;                        // 斜颚面
        if (d >= r) return vSlant;                                   // 圆弧段之外只受斜颚面约束
        float vArc = r - Mathf.Sqrt(Mathf.Max(0f, r * r - d * d));   // 颚尖圆弧
        return Mathf.Min(vSlant, vArc);
    }

    /// <summary>把俯视平面坐标 (u, v) + 高度 y 映射到世界坐标（长库/短库两种朝向）。</summary>
    static Vector3 JawPoint(bool longRail, int sideSign, float u, float v, float y)
    {
        return longRail
            ? new Vector3(u, y, sideSign * (G.HalfW + v))
            : new Vector3(sideSign * (G.HalfL + v), y, u);
    }

    /// <summary>
    /// 袋内衬里（v0.41）：台面以下围住袋口的一圈**有厚度的环墙体**，开口侧低、后侧高。
    ///
    /// ① 为什么要有厚度（初版踩坑，见踩坑 33）：第一版用"零厚度曲面"做这圈墙，
    ///    结果高速球（5m/s，每步走 10mm）直接穿进曲面里，PhysX 的去穿透逻辑再以
    ///    `defaultMaxDepenetrationVelocity` 把它顶出来。**薄壁对撞高速球必然出这种事**，
    ///    所以这里给实体厚度（30mm），并把去穿透速度上限从 1.5 降到 0.6 m/s
    ///    （见 Init 里的说明）——两道措施合起来，实测 5m/s 正打不再被弹起。
    ///
    /// ② 半径的硬约束：内径必须 ≥ 洞口半径 + 球半径 + 余量。球在台呢上时球心离袋口
    ///    轴心最近就是洞口半径（55/62mm），加上球半径 26.25mm，球面最远伸到 81/88mm。
    ///    内衬若比这近，球一进袋口就嵌进内衬壁被解崩（实测球飞到台外 3 米、升到 124mm）。
    ///    现取 洞口 + 36mm，留 8mm 余量。
    ///
    /// ③ 顶端高度分两种（用"两端点都在库边鼻线之外"判定）：
    ///    · 后侧（袋的后壁）高墙 y=+0.045：兜住快速冲进袋口的球。这是必需的 ——
    ///      快球离开布料后要下落 41mm 才能被低墙兜住，而这段时间它已横向飞出 ~180mm，
    ///      低墙根本来不及接。高墙离袋口轴心 91mm，快球只走 8mm 就撞上并被拦下来。
    ///    · 开口侧（仍在台面范围内）低墙 y=-0.015：不挡任何在台呢上滚动的球
    ///      （球在台面上球心 y=0.026，球底在 0；低墙顶在台下 15mm，留足下陷余量）。
    ///    只用"两端点都在鼻线之外"的段做高墙（不是"任一端"），保证高墙绝不伸进台面。
    /// </summary>
    void BuildPocketTubes(GameObject tp)
    {
        const int SEG = 24;
        const float LOW_TOP = -0.015f;       // 开口侧：藏在台面下，且留足滚动下陷余量
        const float HIGH_TOP = 0.045f;       // 后侧：高于库边顶(0.034)，兜住快球
        const float Y_BOT = -G.PocketWallD;
        const float MARGIN = 0.012f;         // 判定"在鼻线之外"的余量
        const float RADIUS_EXTRA = 0.036f;   // 内径相对洞口的额外量（硬约束，见上）
        const float WALL_T = 0.030f;         // 壁厚（见 ①）

        for (int i = 0; i < G.Pockets.Length; i++)
        {
            Vector3 p = G.Pockets[i];
            float rIn = (i < 4 ? G.HoleCornerR : G.HoleCenterR) + RADIUS_EXTRA;
            float rOut = rIn + WALL_T;

            // 每段 4 个顶点：内上 / 内下 / 外上 / 外下（共 4*SEG 个）
            var verts = new Vector3[SEG * 4];
            for (int k = 0; k < SEG; k++)
            {
                float a0 = 2f * Mathf.PI * k / SEG;
                float a1 = 2f * Mathf.PI * (k + 1) / SEG;
                // 段内两顶点的外侧判定（高墙只做在"两端都在鼻线之外"的段上，
                // 保证高墙绝不伸进台面 —— 台面内的点必然 |x|≤HalfL 且 |z|≤HalfW）
                bool out0 = IsOutsideNose(p.x + rIn * Mathf.Cos(a0), p.z + rIn * Mathf.Sin(a0), MARGIN);
                bool out1 = IsOutsideNose(p.x + rIn * Mathf.Cos(a1), p.z + rIn * Mathf.Sin(a1), MARGIN);
                float yT = (out0 && out1) ? HIGH_TOP : LOW_TOP;

                float ca = Mathf.Cos(a0), sa = Mathf.Sin(a0);
                verts[k * 4 + 0] = new Vector3(p.x + rIn * ca, yT, p.z + rIn * sa);    // 内上
                verts[k * 4 + 1] = new Vector3(p.x + rIn * ca, Y_BOT, p.z + rIn * sa); // 内下
                verts[k * 4 + 2] = new Vector3(p.x + rOut * ca, yT, p.z + rOut * sa);  // 外上
                verts[k * 4 + 3] = new Vector3(p.x + rOut * ca, Y_BOT, p.z + rOut * sa); // 外下
            }

            // 环墙四面（逐段由 k 与 k+1 组成四边形）：内壁 / 外壁 / 顶面环带 / 底面环带
            var tris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < SEG; k++)
            {
                int n = (k + 1) % SEG;
                int a = k * 4, b = n * 4;
                tris.Add(a + 0); tris.Add(b + 0); tris.Add(b + 1);     // 内壁
                tris.Add(a + 0); tris.Add(b + 1); tris.Add(a + 1);
                tris.Add(a + 2); tris.Add(b + 3); tris.Add(b + 2);     // 外壁
                tris.Add(a + 2); tris.Add(a + 3); tris.Add(b + 3);
                tris.Add(a + 0); tris.Add(b + 2); tris.Add(b + 0);     // 顶面环带
                tris.Add(a + 0); tris.Add(a + 2); tris.Add(b + 2);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b + 3);     // 底面环带
                tris.Add(a + 1); tris.Add(b + 3); tris.Add(a + 3);
            }
            var mesh = new Mesh();
            mesh.name = "pocketRing" + i;
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();

            var go = new GameObject("pocketTube" + i);   // 名字保持 pocketTube*：回归测试按它识别袋内衬
            go.transform.SetParent(tp.transform, false);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false;                    // 环形墙是凹体：静态非凸网格碰撞体（无刚体）
            mc.material = pocketPM;
        }
    }

    /// <summary>某点在库边鼻线围出的台面范围之外吗（用于决定袋内衬该段是高墙还是低墙）。</summary>
    static bool IsOutsideNose(float x, float z, float margin)
    {
        return Mathf.Abs(x) > G.HalfL + margin || Mathf.Abs(z) > G.HalfW + margin;
    }
}
