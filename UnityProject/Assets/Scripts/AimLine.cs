// =====================================================================================
// AimLine.cs —— 辅助瞄准线（可开关的击球辅助）
//
// 在 Aiming 状态下每帧根据 白球位置 + 瞄准方向 预测并绘制：
//   1. main 白色主准线：白球 → 第一个命中点（球或库边）
//   2. obj  彩色目标线：命中球时 = 目标球被撞后的走向（按目标球颜色着色）；
//                        命中库边时 = 反射方向短线
//   3. defl 灰色分离线：白球碰撞后自身的偏转方向（切线方向）
//   4. ghost 幽灵球：半透明白球，摆在"白球与目标球接触瞬间"的白球中心位置
//
// 预测用解析几何而非物理引擎（SphereCast）：
//   - 球命中：对每颗球解 |P + t·dir − C| = 2r 的一元二次方程取最小正根（幽灵球公式）
//   - 库命中：与 4 条库边平面（x=±(HalfL−r), z=±(HalfW−r)）求交取最近
//   好处是确定性强、不受物理步长影响，且能直接拿到精确的碰撞几何信息。
//
// 线体用 LineRenderer（Sprites/Default 无光照着色器，保证亮度稳定）；
// 幽灵球用半透明 Standard 材质（由 Bootstrapper.GhostMat() 提供）。
// =====================================================================================
using UnityEngine;

public class AimLine : MonoBehaviour
{
    private LineRenderer main, obj, defl;         // 三条线：主准线 / 目标球走向 / 白球分离线
    private GameObject ghost;                     // 幽灵球（半透明）
    private Material mainMat, objMat, deflMat;    // 各线的材质（obj 颜色随目标球实时变化）
    private bool visible;                         // 当前是否允许显示（Show() 设定）

    /// <summary>
    /// 是否允许显示辅助线（含幽灵球）。v0.36 修复：
    /// v0.35 为了让"指定彩球"在关闭辅助线时仍然生效，把 Compute() 改成无条件每帧调用，
    /// 但 Compute 内部命中球时会无条件 `ghost.SetActive(true)` —— 于是玩家关掉辅助线后，
    /// 幽灵球（半透明白球）照样出现在台面上，看起来像"多了一颗白球"。
    /// 现在 Compute 只负责几何与位置，显隐一律服从 visible（由 Show 每帧设定）。
    /// </summary>
    public bool Visible { get { return visible; } }

    /// <summary>
    /// 当前准线指向的那颗球（null = 指向空处/库边）。
    /// v0.35 新增：球 on 为彩球时，规则要求击球方【指定】打哪一颗（Rule 3(f)(i)(b)）——
    /// 用"准线指向的球"作为指定对象，玩家无需额外操作即可表达意图，
    /// 同时保留规则语义（若实际首碰与指定不符即为犯规）。
    /// </summary>
    public BallController AimedBall { get; private set; }

    /// <summary>
    /// 初始化：由 Bootstrapper 调用一次。传入幽灵球材质（半透明白）。
    /// 三条 LineRenderer 各 2 个顶点、宽度 7mm、世界坐标模式、不投影不接收阴影。
    /// </summary>
    public void Setup(Material ghostMat)
    {
        mainMat = LineMat(new Color(1f, 1f, 1f, 0.9f));     // 主准线：白色
        objMat = LineMat(new Color(1f, 1f, 1f, 0.9f));      // 目标线：颜色运行时改
        deflMat = LineMat(new Color(0.8f, 0.8f, 0.8f, 0.55f)); // 分离线：半透明灰
        main = MakeLine("AL_Main", mainMat);
        obj = MakeLine("AL_Obj", objMat);
        defl = MakeLine("AL_Defl", deflMat);

        ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(ghost.GetComponent<Collider>());     // 幽灵球纯视觉，去掉碰撞体
        ghost.name = "GhostBall";
        ghost.transform.localScale = Vector3.one * G.BallR * 2f;   // 与真球同大
        ghost.GetComponent<Renderer>().sharedMaterial = ghostMat;
        Show(false);
    }

    /// 造一个 Sprite/Default 材质（无光照顶点色，LineRenderer 上亮度不随灯光变化）。
    Material LineMat(Color c)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Diffuse");
        var m = new Material(sh);
        m.color = c;
        return m;
    }

    /// 建一条 LineRenderer 并统一外观参数。
    LineRenderer MakeLine(string n, Material m)
    {
        var go = new GameObject(n);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = m;
        lr.startWidth = 0.007f;                     // 线宽 7mm，太细在低分辨率下会闪
        lr.endWidth = 0.007f;
        lr.positionCount = 0;                       // 初始不绘制
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;                      // 线端加两个顶点让端头圆润
        return lr;
    }

    /// 总开关：显示/隐藏三条线与幽灵球；隐藏时清空顶点。
    /// 注意 visible 字段：Compute() 每帧都会跑（"指定彩球"需要），
    /// 它靠这个字段决定要不要显示幽灵球，不能只看 ghost.activeSelf。
    public void Show(bool on)
    {
        visible = on;
        if (main == null) return;
        main.enabled = obj.enabled = defl.enabled = on;
        if (ghost != null) ghost.SetActive(on);
        if (!on)
        {
            main.positionCount = 0;
            obj.positionCount = 0;
            defl.positionCount = 0;
        }
    }

    // ---------------------------------------------------------------------------------
    // 核心计算：由 白球位置 p 与方向 dir 预测本杆线路。每帧在 Aiming 状态下调用。
    // 步骤：
    //   ① 遍历所有球解幽灵球交点（最近者为准）
    //   ② 与四条库边平面求交（只考虑朝向该库的方向分量）
    //   ③ 三者比较取最近：命中球 → 画幽灵球+目标线+分离线；命中库 → 画一次反弹；
    //      都没命中 → 画 3 米直线（指向远方）
    // ---------------------------------------------------------------------------------
    public void Compute(Vector3 p, Vector3 dir)
    {
        if (GameManager.I == null) return;

        // ---- ① 与每颗球的幽灵球求交 ----
        float best = float.MaxValue;                  // 当前最近的命中距离 t
        BallController hit = null;                    // 命中的球
        foreach (var b in GameManager.I.balls)
        {
            if (b == GameManager.I.cue || b.potted) continue;     // 跳过白球自己与已落袋球
            Vector3 d = b.transform.position - p;
            d.y = 0;                                              // 只在水平面计算
            float along = Vector3.Dot(d, dir);                    // 球心在方向上的投影距离
            if (along <= 0f) continue;                            // 在身后，不可能命中
            float perp2 = d.sqrMagnitude - along * along;         // 垂距的平方
            float rr = 4f * G.BallR * G.BallR;                    // 命中条件 (2r)²：白球撞球时球心距 = 2r
            if (perp2 >= rr) continue;                            // 偏得太远，打不到
            float t = along - Mathf.Sqrt(rr - perp2);             // 接触点距离（解直角三角形）
            if (t < best) { best = t; hit = b; }
        }

        // ---- ② 与四条库边求交（库边平面内缩一个球半径，即球心能到达的极限线）----
        float tc = float.MaxValue;                    // 库边命中距离
        Vector3 cn = Vector3.zero;                    // 库边法线（反射用）
        float px = G.HalfL - G.BallR, pz = G.HalfW - G.BallR;
        if (dir.x > 1e-5f) { float t = (px - p.x) / dir.x; if (t > 0f && t < tc) { tc = t; cn = Vector3.left; } }
        if (dir.x < -1e-5f) { float t = (-px - p.x) / dir.x; if (t > 0f && t < tc) { tc = t; cn = Vector3.right; } }
        if (dir.z > 1e-5f) { float t = (pz - p.z) / dir.z; if (t > 0f && t < tc) { tc = t; cn = Vector3.back; } }
        if (dir.z < -1e-5f) { float t = (-pz - p.z) / dir.z; if (t > 0f && t < tc) { tc = t; cn = Vector3.forward; } }

        if (hit != null && best <= tc)
        {
            AimedBall = hit;                                      // v0.35：记录准线指向的球（供"指定彩球"用）
            // ---- 命中球：幽灵球摆接触点，画目标球走向与白球分离方向 ----
            Vector3 gp = p + dir * best;                          // 接触瞬间白球中心（幽灵球位置）
            Set(main, p, gp);                                     // 主准线
            Vector3 n = hit.transform.position - gp;              // 白球→目标球连线（撞击法线）
            n.y = 0;
            n.Normalize();
            objMat.color = G.BallColor(hit.kind);                 // 目标线染成目标球的颜色
            Set(obj, hit.transform.position, hit.transform.position + n * 0.38f);   // 目标球走向 38cm
            Vector3 tang = dir - Vector3.Dot(dir, n) * n;         // 白球分离方向 = 入射方向去掉法线分量
            if (tang.sqrMagnitude > 1e-4f)                        // 正撞（无分离）时不画
            {
                tang.Normalize();
                Set(defl, gp, gp + tang * 0.26f);                 // 分离线 26cm
            }
            else defl.positionCount = 0;
            ghost.transform.position = gp;
            if (ghost.activeSelf != visible) ghost.SetActive(visible);   // v0.36：服从辅助线开关（修复幽灵球残留）
        }
        else if (tc < float.MaxValue)
        {
            AimedBall = null;                                     // v0.35：指向库边 → 未指定任何球
            // ---- 命中库边：画到撞点 + 0.5m 反射方向短线 ----
            Vector3 hp = p + dir * tc;
            Set(main, p, hp);
            Vector3 r = Vector3.Reflect(dir, cn);                 // 镜面反射
            objMat.color = new Color(1f, 1f, 1f, 0.6f);
            Set(obj, hp, hp + r * 0.5f);
            defl.positionCount = 0;
            if (ghost.activeSelf) ghost.SetActive(false);         // 撞库没有幽灵球
        }
        else
        {
            AimedBall = null;                                     // v0.35：指向空处 → 未指定任何球
            // ---- 什么都没命中：画 3 米方向指示线 ----
            Set(main, p, p + dir * 3f);
            obj.positionCount = 0;
            defl.positionCount = 0;
            if (ghost.activeSelf) ghost.SetActive(false);
        }
    }

    /// 设置一条两顶点线段（整体抬高 4mm 浮在台呢面上，避免与地面 z-fighting）。
    void Set(LineRenderer lr, Vector3 a, Vector3 b)
    {
        lr.positionCount = 2;
        lr.SetPosition(0, a + Vector3.up * 0.004f);
        lr.SetPosition(1, b + Vector3.up * 0.004f);
    }
}
