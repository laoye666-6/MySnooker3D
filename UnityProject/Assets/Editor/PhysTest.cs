// =====================================================================================
// PhysTest.cs —— 编辑器内离线物理测试（Editor 专用，不会打进 APK）
//
// 提供两个入口，均为无头批处理运行（不打开界面）：
//
// ① PhysTest.Run —— 开球回归测试
//    复现"白球开球→撞散球堆"的**纯 PhysX 反弹过程**。
//    ⚠ 边界（别把结论说过头）：编辑器里不执行 MonoBehaviour 的 FixedUpdate/Update，
//      因此 BallController 的滚动摩擦(RollDecel)、袋口捕获、停判逻辑都不会运行——
//      本测试只验证刚体碰撞与库边反弹，手感/停判必须上机实测。
//
// ② PhysTest.CushionTest —— 库边反弹专项测试（复现/验证"低速粘库"bug）
//    三个用例，全部只留一颗白球、隐藏其余球，避免干扰：
//      A 快速正碰  : 1.2 m/s 垂直撞短库   → 任何情况下都应反弹（健康基准）
//      B 低速正碰  : 0.35 m/s 垂直撞短库  → BUG 用例：法向速度低于
//                    Physics.bounceThreshold(0.5) 时 PhysX 不施加弹性，
//                    球会"粘"在库边原地不动，而不是弹回
//      C 掠射斜碰  : 法向 0.35 + 切向 0.9 → BUG 用例：贴着库边滑行不弹开
//    每个用例判定 bounced（3 秒后球是否已离开库边 15cm 以上）。
//
// 用法：
//   blender 式批处理运行：
//   "E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit ^
//     -projectPath "E:\Snooker3D" -executeMethod PhysTest.CushionTest ^
//     -logFile "E:\Snooker\logs\phystest.log"
//   日志里过滤 [PHYSCUSH]。退出码 0 = 全流程正常。
//
// 注意：手动步进**必须包在 try/finally 里**恢复 SimulationMode.FixedUpdate —— 否则任何异常
// 都会让工程停在 Script 模式，退出编辑器时被持久化进 DynamicsManager.asset 并打进 APK，
// 设备上物理完全不步进（README 踩坑 5，历史真实事故）。v0.34 起两个入口都加了 try/finally，
// 且 CushionTest 步长与运行时 Physics.fixedDeltaTime 保持一致（v0.36 起 0.002s）。
//
// ③ PhysTest.SpinTest —— 加塞/杆法专项测试（v0.36 新增）
//    验证白球自旋模型：中杆零回归、低杆拉回、高杆前冲、侧塞撞库横移。
// =====================================================================================
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PhysTest
{
    private static string Log = "";   // 汇总日志（结束时一次性输出）

    /// 双通道打印：写进汇总串 + 即时 Debug.Log。
    private static void L(string s)
    {
        Log += s + "\n";
        Debug.Log("[PHYSCUSH] " + s);
    }

    // =================================================================================
    // 入口一：开球回归测试（整体物理健康度）
    // =================================================================================
    public static void Run()
    {
        Debug.Log("[PHYSTEST] === START ===");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootGo = new GameObject("Boot");
        bootGo.AddComponent<Bootstrapper>().Init();
        var gm = GameManager.I;
        L("gm=" + (gm == null ? "NULL" : "ok"));
        if (gm == null || gm.cue == null)
        {
            // 初始化失败就直接收工：否则后面会解引用 null 抛异常，把"恢复物理设置"那步跳过
            // （这正是必须用 try/finally 的原因）
            Debug.LogError("[PHYSTEST] Bootstrapper 初始化失败，测试中止（物理设置未被改动）");
            Debug.Log("[PHYSTEST] === ABORTED ===\n" + Log);
            EditorApplication.Exit(2);
            return;
        }

        // 倾倒开球线附近的碰撞体（排查幽灵碰撞体/错位碰撞体）
        foreach (var c in Object.FindObjectsOfType<Collider>())
        {
            var b = c.bounds;
            if (b.center.z > -0.4f && b.center.z < 0.4f)
                L("collider " + c.name + " type=" + c.GetType().Name +
                  " center=" + b.center.ToString("F3") + " size=" + b.size.ToString("F3"));
        }

        var cue = gm.cue;
        L("cue start pos=" + cue.transform.position.ToString("F3"));

        // ★ 手动步进整段包在 try/finally 里：中途无论抛什么异常，都必须把 simulationMode
        //   还原为 FixedUpdate，绝不能让它停在 Script 被写进工程设置并打进包。
        try
        {
            try { gm.StartGame(); } catch (System.Exception e) { L("StartGame EXC: " + e); }
            Physics.simulationMode = SimulationMode.Script;       // 切手动步进（Script=由代码调 Simulate）
            try
            {
                gm.Shoot(new Vector3(1f, 0f, 0f), 0.5f);          // 力度 0.5 ≈ 2.58 m/s
                L("after Shoot velocity=" + cue.Rb.velocity.ToString("F3"));
            }
            catch (System.Exception e) { L("Shoot EXC: " + e); }

            float dt = 0.002f;                                    // 与运行时 Physics.fixedDeltaTime 一致（v0.36：4ms→2ms）
            for (int i = 1; i <= 2250; i++)                       // 手动推进 4.5 秒
            {
                Physics.Simulate(dt);
                if (i % 80 == 0)
                    L("t=" + (i * dt).ToString("F2") + " cue=" + cue.transform.position.ToString("F3") +
                      " v=" + cue.Rb.velocity.ToString("F2"));
            }

            gm.LogBalls("AFTER-BREAK");
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;  // 还原工程物理设置（必经路径）
            L("simulationMode restored=" + Physics.simulationMode);
        }
        Debug.Log("[PHYSTEST] === DONE ===\n" + Log);
        EditorApplication.Exit(0);
    }

    // =================================================================================
    // 入口二：库边反弹专项测试（低速粘库 bug 的复现与验证）
    // =================================================================================
    public static void CushionTest()
    {
        Debug.Log("[PHYSCUSH] === START ===");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootGo = new GameObject("Boot");
        bootGo.AddComponent<Bootstrapper>().Init();
        var gm = GameManager.I;
        if (gm == null || gm.cue == null)
        {
            Debug.LogError("[PHYSCUSH] Bootstrapper 初始化失败，测试中止（物理设置未被改动）");
            Debug.Log("[PHYSCUSH] === ABORTED ===\n" + Log);
            EditorApplication.Exit(2);
            return;
        }
        gm.StartGame();                                           // 布球

        var cue = gm.cue;
        // 隐藏其余 21 颗球（禁用其碰撞体），做成单球实验，排除干扰
        foreach (var b in gm.balls)
            if (b != cue) b.gameObject.SetActive(false);

        L("bounceThreshold=" + Physics.bounceThreshold.ToString("F3") +
          "  (球撞库法向速度低于它时不反弹 → 粘库)");

        // ★ 同样包 try/finally：中途异常也必须还原物理设置（见文件头注意事项）
        try
        {
            Physics.simulationMode = SimulationMode.Script;       // 切手动步进
            float dt = 0.002f;                                    // 与运行时 fixedDeltaTime 一致（v0.36：4ms→2ms）

            // 三个用例：位置 / 速度（见文件头说明）
            // B、C 的起点放在距库边 0.53m 处：太远的话低速球会被台呢摩擦减到停、碰不到库
            RunCase("A-fast-normal", new Vector3(1.2f, 0, 0),    new Vector3(1.2f, 0, 0),    dt, cue);
            RunCase("B-slow-normal", new Vector3(1.2f, 0, 0),    new Vector3(0.35f, 0, 0),   dt, cue);
            RunCase("C-grazing",     new Vector3(1.2f, 0, -0.4f), new Vector3(0.35f, 0, -0.2f), dt, cue);
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;      // 还原工程物理设置（必经路径）
            L("simulationMode restored=" + Physics.simulationMode);
        }
        Debug.Log("[PHYSCUSH] === DONE ===\n" + Log);
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 运行单个撞库用例。
    /// 判定 bounced：模拟期间球心最贴近库边的位置记为 maxX，若结束时球已从 maxX
    /// 回退 3cm 以上，说明发生了反弹（低速反弹只有 ~0.12 m/s，4 秒内回退 7~20cm，
    /// 用"回退量"而非"绝对距离"才能同时判定快/慢反弹）。
    /// 注意：模拟时长固定 4 秒（steps 按 dt 换算），v0.36 改步长时曾因写死 800 步
    /// 把时长砍半，导致慢速用例"刚撞库就结束"，误报 bounced=false。
    /// </summary>
    private static void RunCase(string name, Vector3 pos, Vector3 vel, float dt, BallController cue)
    {
        cue.Place(pos);                       // 传送到起点（内部会清速度 + WakeUp）
        cue.Rb.velocity = vel;                // 赋初速
        L("CASE " + name + " START pos=" + pos.ToString("F3") + " v=" + vel.ToString("F3"));

        int steps = Mathf.RoundToInt(4f / dt);   // 恒定模拟 4 秒（步长变化时自动换算，勿写死步数）
        float maxX = pos.x;                   // 球心到达过的最靠右位置（短库侧）
        for (int i = 1; i <= steps; i++)
        {
            Physics.Simulate(dt);
            float x = cue.transform.position.x;
            if (x > maxX) maxX = x;
            if (i % Mathf.RoundToInt(0.4f / dt) == 0)     // 每 0.4 秒采样一次
                L("  t=" + (i * dt).ToString("F2") +
                  " pos=" + cue.transform.position.ToString("F3") +
                  " v=" + cue.Rb.velocity.ToString("F3"));
        }

        float retreat = maxX - cue.transform.position.x;
        bool bounced = retreat > 0.03f;       // 从最贴近库边处回退 ≥3cm = 发生了反弹
        L("CASE " + name + " END pos=" + cue.transform.position.ToString("F3") +
          " v=" + cue.Rb.velocity.ToString("F3") + " retreat=" + retreat.ToString("F3") +
          " bounced=" + bounced);
    }

    // =================================================================================
    // 入口三：加塞/杆法专项测试（v0.36）
    //
    // 验证白球自旋模型（BallController.CueRollStep）是否产生正确的物理效果：
    //   A 中杆对照  : spinV=0 出杆 → 撞球后应停住/微前（与 v0.35 行为一致 = 零回归）
    //   B 低杆      : spinV=-1 出杆 → 撞球后白球应【被拉回来】（X 位移显著小于中杆，甚至倒退）
    //   C 高杆      : spinV=+1 出杆 → 撞球后白球应【继续前冲】（X 位移显著大于中杆）
    //   D 右塞撞库  : spinH=+1 垂直撞库 → 反弹后应出现横向（Z）位移，无塞时 Z 恒 0
    //
    /// 关键：PhysX 的 Physics.Simulate 不会触发 MonoBehaviour.FixedUpdate，
    /// 所以每个物理步都必须手动调 cue.StepOnCloth(dt)，否则自旋模型根本不运行。
    ///
    /// 场景布置：白球在 (-0.5,0,0)，靶球（红球）在 (0.3,0,0)，出杆方向 +X 正碰；
    /// Z 方向留空以便观察侧塞的横向偏移。靶球用一颗红球，测试期间隐藏其余球。
    /// =================================================================================
    public static void SpinTest()
    {
        Debug.Log("[PHYSSPIN] === START ===");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootGo = new GameObject("Boot");
        bootGo.AddComponent<Bootstrapper>().Init();
        var gm = GameManager.I;
        if (gm == null || gm.cue == null)
        {
            Debug.LogError("[PHYSSPIN] Bootstrapper 初始化失败，测试中止（物理设置未被改动）");
            EditorApplication.Exit(2);
            return;
        }
        gm.StartGame();

        var cue = gm.cue;
        var target = gm.balls.First(b => b.kind == BallKind.Red);
        // 只留白球与一颗红球，排除干扰
        foreach (var b in gm.balls)
            if (b != cue && b != target) b.gameObject.SetActive(false);

        try
        {
            Physics.simulationMode = SimulationMode.Script;
            float dt = 0.002f;

            float plainStop = RunSpinCase("A-mid (中杆对照)", cue, target, dt, 0.5f, 0f, 0f);
            float backStop = RunSpinCase("B-low (低杆)", cue, target, dt, 0.5f, -1f, 0f);
            float topStop = RunSpinCase("C-top (高杆)", cue, target, dt, 0.5f, +1f, 0f);
            // 中低杆（圆盘拖到"略低"）也必须产生缩杆效果，不能是"无旋滑行"的死区
            float halfLowStop = RunSpinCase("B2-low-half (中低杆)", cue, target, dt, 0.5f, -0.5f, 0f);

            bool midOk = true;                                   // 中杆是基线，只记录
            bool lowOk = backStop < plainStop - 0.05f;           // 低杆必须比中杆"退得更多"
            bool topOk = topStop > plainStop + 0.05f;            // 高杆必须比中杆"冲得更远"
            bool halfLowOk = halfLowStop < plainStop - 0.02f;    // 中低杆也要有可感知的回缩
            L("CHECK 低杆<中杆: " + backStop.ToString("F3") + " < " + plainStop.ToString("F3") + " → " + lowOk);
            L("CHECK 高杆>中杆: " + topStop.ToString("F3") + " > " + plainStop.ToString("F3") + " → " + topOk);
            L("CHECK 中低杆<中杆(无死区): " + halfLowStop.ToString("F3") + " < " + plainStop.ToString("F3") + " → " + halfLowOk);

            // ---- D：侧塞撞库（纯函数断言）----
            // 说明：撞库回调 OnCollisionEnter 在 -batchmode 下不会触发（没有游戏循环调度
            // MonoBehaviour 消息），所以这里直接验证 CushionKick 的数学：
            //   ① 横向增量必须垂直于入射方向（沿入射方向的分量应为 0）
            //   ② 右塞/左塞的横移方向必须相反
            //   ③ 无侧塞时必须为 0
            // 撞库后的实际走位以 MuMu 真机实测为准（见 README 版本记录）。
            Vector3 kRight = BallController.CushionKick(new Vector3(3f, 0f, 0f), 90f);
            Vector3 kLeft = BallController.CushionKick(new Vector3(3f, 0f, 0f), -90f);
            Vector3 kNone = BallController.CushionKick(new Vector3(3f, 0f, 0f), 0f);
            bool perpOk = Mathf.Abs(kRight.x) < 1e-4f && Mathf.Abs(kRight.z) > 1e-3f;
            bool signOk = Mathf.Sign(kRight.z) != Mathf.Sign(kLeft.z);
            bool zeroOk = kNone.sqrMagnitude < 1e-8f;
            L("CHECK 侧塞踢出垂直于入射: kRight=" + kRight.ToString("F4") + " → " + perpOk);
            L("CHECK 左右塞方向相反: right.z=" + kRight.z.ToString("F4") +
              " left.z=" + kLeft.z.ToString("F4") + " → " + signOk);
            L("CHECK 无塞零横移: " + kNone.ToString("F4") + " → " + zeroOk);

            // ---- E：侧塞不应让白球在台呢上走出弧线（模型正确性）----
            // 侧塞是绕竖直轴的自旋，接触点速度恒为 0（球像陀螺自转），
            // 所以台面上的轨迹必须是直线 —— 若这里出现横移说明模型写错了。
            float sideDrift = RunSideDrift(cue, target, dt);
            bool driftOk = Mathf.Abs(sideDrift) < 0.01f;
            L("CHECK 台面轨迹不受侧塞影响: drift=" + sideDrift.ToString("F4") + " → " + driftOk);

            bool all = midOk && lowOk && topOk && halfLowOk && perpOk && signOk && zeroOk && driftOk;
            L(all ? "ALL SPIN OK" : "SPIN FAILED");
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
            L("simulationMode restored=" + Physics.simulationMode);
        }
        Debug.Log("[PHYSSPIN] === DONE ===\n" + Log);
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 单次加塞正碰用例：白球撞向靶球，返回白球【撞球之后】继续走的净位移（米）。
    /// 低杆应为负（被拉回）、高杆为正且更大、中杆接近 0。
    /// </summary>
    private static float RunSpinCase(string name, BallController cue, BallController target,
                                     float dt, float power, float spinV, float spinH)
    {
        cue.Place(new Vector3(-0.5f, 0f, 0f));
        target.Place(new Vector3(0.3f, 0f, 0f));
        // 静置几帧让球落稳（Place 会抬高 2mm）
        for (int i = 0; i < 60; i++) { Physics.Simulate(dt); cue.StepOnCloth(dt); target.StepOnCloth(dt); }

        float speed = Mathf.Lerp(G.MinShotSpeed, G.MaxShotSpeed, power);
        cue.Rb.WakeUp();
        cue.Rb.velocity = new Vector3(speed, 0f, 0f);
        cue.ApplySpin(Vector3.right, speed, spinV, spinH);

        float restX = 0f;            // 白球停下时的 X
        bool met = false;
        for (int i = 1; i <= 4000; i++)     // 最多模拟 8 秒
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);             // ★ 必须手动推进（Simulate 不触发 FixedUpdate）
            target.StepOnCloth(dt);
            if (!met && cue.transform.position.x > target.transform.position.x - 3f * G.BallR)
                met = true;                  // 两球已接触（白球被挡在靶球后方）
            if (met && cue.Rb.velocity.magnitude < G.StopSpeed &&
                Mathf.Abs(cue.SideSpin) < 0.5f)
            { restX = cue.transform.position.x; break; }
        }
        if (restX == 0f) restX = cue.transform.position.x;
        float offset = restX - 0.3f;         // 相对靶球初始位置：负=被拉回，正=冲过去
        L("SPIN " + name + " spinV=" + spinV.ToString("F1") + " spinH=" + spinH.ToString("F1") +
          " → cueRest=" + restX.ToString("F3") + " offset=" + offset.ToString("F3"));
        return offset;
    }

    /// <summary>
    /// 侧塞在台面上的横向漂移（应为 0 —— 侧塞不改变台呢上的直线轨迹）。
    /// 白球带强侧塞沿 +X 前进，返回停稳后的 Z 偏移。
    /// </summary>
    private static float RunSideDrift(BallController cue, BallController target, float dt)
    {
        target.Place(new Vector3(0.3f, 0f, 0.6f));      // 靶球挪远，不参与
        cue.Place(new Vector3(-1.2f, 0f, 0f));
        for (int i = 0; i < 60; i++) { Physics.Simulate(dt); cue.StepOnCloth(dt); }

        float speed = Mathf.Lerp(G.MinShotSpeed, G.MaxShotSpeed, 0.6f);
        cue.Rb.WakeUp();
        cue.Rb.velocity = new Vector3(speed, 0f, 0f);
        cue.ApplySpin(Vector3.right, speed, 0f, 1f);    // 满侧塞

        for (int i = 1; i <= 4000; i++)
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);
            if (cue.Rb.velocity.magnitude < G.StopSpeed) break;
        }
        L("DRIFT 满侧塞直球 → pos=" + cue.transform.position.ToString("F3"));
        return cue.transform.position.z;
    }
}
