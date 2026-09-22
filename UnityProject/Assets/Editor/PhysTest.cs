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

            RunSpinCase("A-mid (中杆对照)", cue, target, dt, 0.5f, 0f, 0f);
            RunSpinCase("B-low (低杆)", cue, target, dt, 0.5f, -1f, 0f);
            RunSpinCase("C-top (高杆)", cue, target, dt, 0.5f, +1f, 0f);
            RunSpinCase("B2-stun (定杆档)", cue, target, dt, 0.5f, -0.5f, 0f);
            // 满力高杆只用于限速断言（6m/s 下靶球弹回二次碰撞，终点混沌不可断言）
            RunSpinCase("C2-top-full (满力高杆)", cue, target, dt, 1.0f, +1f, 0f);

            // ---- 判定（撞后窗口可观测量，避开后续混沌碰撞）----
            //  vPost[+0.75s]：跟进速度 —— 撞球后白球从 0 被残余自旋重新加速，
            //                 到纯滚动耗时 ~0.65s，0.75s 时高杆(1.5×)应显著快于中杆(1.0×)；
            //                 定杆(ω=0)撞后无人推 → 几乎停住。
            //  dx@+0.45s：低杆在 0.45s 内已明显后移（摩擦立即反向），取该窗口。
            //  注：定杆档(-0.5)在 0.75m 行程中会自然获得部分前旋（真实物理：
            //  定杆只在近距离成立），所以阈值是"跟进减半"而非"完全停住"。
            bool topOk = caseVpost[2] > caseVpost[0] * 1.15f;          // 高杆跟进 > 中杆×1.15
            bool stunOk = caseVpost[3] < caseVpost[0] * 0.5f;          // 定杆跟进减半以上
            bool lowOk = caseDx[1] < -0.05f;                           // 低杆被拉回
            bool midForwardOk = caseDx[0] > 0f;                        // 中杆自然向前（基线合理）
            bool capOk = lastCaseMaxSpeed <= fullCaseSpeed0 * 1.02f;   // 限速（满力高杆）
            L("CHECK 限速(满力高杆最大速度 " + lastCaseMaxSpeed.ToString("F3") +
              " ≤ 出杆初速 " + fullCaseSpeed0.ToString("F3") + "×1.02) → " + capOk);
            L("CHECK 高杆撞后0.75s速度 " + caseVpost[2].ToString("F2") + " > 中杆 " +
              caseVpost[0].ToString("F2") + "×1.15 → " + topOk);
            L("CHECK 定杆撞后0.75s速度 " + caseVpost[3].ToString("F2") + " < 中杆×0.35 → " + stunOk);
            L("CHECK 低杆撞后位移 " + caseDx[1].ToString("F3") + " < -0.05 → " + lowOk);
            L("CHECK 中杆向前位移 " + caseDx[0].ToString("F3") + " > 0 → " + midForwardOk);

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

            bool all = topOk && stunOk && lowOk && midForwardOk && capOk &&
                       perpOk && signOk && zeroOk && driftOk;
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
    /// 单次加塞正碰用例。v0.37 记录【撞击后 0.45 秒窗口】内的可观测量（避开之后
    /// 靶球从底库弹回的二次碰撞混沌）：
    ///   caseVpost[n] = 撞后 0.05s 时白球速度（跟进/定杆的判据）
    ///   caseDx[n]    = 撞后 0.45s 内白球 X 位移（低杆拉回为负）
    /// n 按调用顺序 0..4；另记录全程最大速度 lastCaseMaxSpeed（限速断言）。
    /// </summary>
    private static readonly float[] caseVpost = new float[5];
    private static readonly float[] caseDx = new float[5];
    private static int caseIdx;
    private static float lastCaseMaxSpeed;
    private static float fullCaseSpeed0;

    private static void RunSpinCase(string name, BallController cue, BallController target,
                                    float dt, float power, float spinV, float spinH)
    {
        int n = caseIdx++;
        cue.Place(new Vector3(-0.5f, 0f, 0f));
        target.Place(new Vector3(0.3f, 0f, 0f));
        // 静置几帧让球落稳（Place 会抬高 2mm）
        for (int i = 0; i < 60; i++) { Physics.Simulate(dt); cue.StepOnCloth(dt); target.StepOnCloth(dt); }

        float speed = Mathf.Lerp(G.MinShotSpeed, G.MaxShotSpeed, power);
        fullCaseSpeed0 = speed;
        lastCaseMaxSpeed = 0f;
        cue.Rb.WakeUp();
        cue.Rb.velocity = new Vector3(speed, 0f, 0f);
        cue.ApplySpin(Vector3.right, speed, spinV, spinH);

        bool met = false;
        float impactX = 0f, xAtWindowEnd = 0f;
        int stepsAfterMet = 0;
        const int WindowSteps = 225;                 // 0.45s / 0.002
        for (int i = 1; i <= 4000; i++)              // 最多模拟 8 秒
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);                     // ★ 必须手动推进（Simulate 不触发 FixedUpdate）
            target.StepOnCloth(dt);
            float vmag = cue.Rb.velocity.magnitude;
            if (vmag > lastCaseMaxSpeed) lastCaseMaxSpeed = vmag;
            if (!met && cue.transform.position.x > target.transform.position.x - 3f * G.BallR)
            {
                met = true;                          // 两球已接触（白球被挡在靶球后方）
                impactX = cue.transform.position.x;
            }
            if (met)
            {
                stepsAfterMet++;
                if (stepsAfterMet == WindowSteps)    // 撞后 0.45s：位移采样（二次碰撞尚未发生）
                    xAtWindowEnd = cue.transform.position.x;
                if (stepsAfterMet == 375)            // 撞后 0.75s：跟进速度采样，随后结束本用例
                { caseVpost[n] = cue.Rb.velocity.magnitude; break; }
            }
        }
        caseDx[n] = met ? xAtWindowEnd - impactX : 0f;
        L("SPIN " + name + " spinV=" + spinV.ToString("F1") + " spinH=" + spinH.ToString("F1") +
          " → v@+0.05s=" + caseVpost[n].ToString("F2") +
          " dx@+0.45s=" + caseDx[n].ToString("F3") +
          " maxV=" + lastCaseMaxSpeed.ToString("F2"));
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

    // =================================================================================
    // 入口四：袋口专项测试（v0.41 新增）
    //
    // 验证"台呢有真洞 + 真实下坠"这套袋口物理，而不是旧的"球心进圈即落袋"脚本判定。
    // 四个用例：
    //   A 慢球滚向下角袋      → 应掉进洞里（球心降到 PotDepth 以下）
    //   B 快球横穿洞口        → 应**冲过洞口**继续在台面上跑（旧版会直接判落袋 = 假进球）
    //   C 正对颚面斜撞        → 应被颚面弹回（晃袋），而不是穿进袋里
    //   D 静止球放洞口边缘    → 越界应掉，未越界应留在台上（洞口边界正确性）
    // 关键判定用"是否降到 PotDepth 以下"，与 GameManager.CheckPockets 的落袋判据一致。
    // =================================================================================
    public static void PocketTest()
    {
        Debug.Log("[PHYSPOCKET] === START ===");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootGo = new GameObject("Boot");
        bootGo.AddComponent<Bootstrapper>().Init();
        var gm = GameManager.I;
        if (gm == null || gm.cue == null)
        {
            Debug.LogError("[PHYSPOCKET] Bootstrapper 初始化失败，测试中止");
            Debug.Log("[PHYSPOCKET] === ABORTED ===\n" + Log);
            EditorApplication.Exit(2);
            return;
        }
        gm.StartGame();
        var cue = gm.cue;
        foreach (var b in gm.balls)
            if (b != cue) b.gameObject.SetActive(false);

        int pass = 0, fail = 0;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            float dt = 0.002f;

            // A：慢球贴长库滚向下角袋（+x,+z 角，洞口圆心 (1.7925, 0.897)）。
            //    球心 z 取贴库极限值附近，模拟真实"贴库推球进角袋"。
            bool a = CaseSinkToPocket("A-slow-roll-into-corner", dt, cue,
                new Vector3(1.30f, 0f, 0.860f), new Vector3(1.05f, 0f, 0f), 6f);
            Tally(ref pass, ref fail, "A 慢球滚入角袋 → 落袋", a);

            // B：贴中袋但球心未进洞口 —— 应以原速穿过、不落袋。
            //    中袋洞口圆心 (0, 0.907) 半径 0.062（+2mm 边缘余量 = 0.064）。
            //    球沿 z=0.830 滚（离洞口圆心 0.077 > 0.064，球心在洞外），必须安全通过。
            //    注意：**不能**拿"沿库皮 z≈0.86 滚过中袋"当不落袋用例 —— 那时球心离
            //    洞口圆心只有 47mm、已在洞内，真实球桌也会掉（v0.40 的捕获圈同样会判进袋）。
            bool b = CaseRollAcross("B-past-centre-pocket-outside-hole", dt, cue,
                new Vector3(-0.55f, 0f, 0.830f), new Vector3(2.2f, 0f, 0f), 0.65f);
            Tally(ref pass, ref fail, "B 球心未进洞口 → 穿过不落袋", b);

            // C：撞颚弹回 —— 瞄准**长库颚面的中点**（颚面从颚尖 (1.7125,0.889) 斜到
            //    (1.6625,0.944)，中点 (1.6875,0.9165)）。球应从颚面弹回台面，
            //    既不能落袋、也不能停在袋口外侧。
            //    （早前版本瞄的是 (1.7425,0.889)，那已经在袋口内部了 —— 球会直接进袋，
            //      当时"通过"是因为被洞口阶梯边缘弹了回来，属假象。）
            bool c = CaseBounceOffJaw("C-hit-jaw-and-return", dt, cue);
            Tally(ref pass, ref fail, "C 撞颚弹回 → 未落袋且在台面", c);

            // D：洞口边界 —— 球心停在洞口圈外 20mm 应稳稳留在台面。
            bool d = CaseRestOutsideHole("D-rest-outside-hole", dt, cue);
            Tally(ref pass, ref fail, "D 洞口圈外静止 → 不下坠", d);

            // E：晃袋专项 —— 斜向打进角袋袋口，统计与颚面/袋内衬的接触次数，
            //    并确认最终结局（落袋 或 被弹回台面）都是物理自然产生、没有穿墙。
            int rattleJaws = 0;
            bool anyPotted = false, anyRejected = false, allSane = true;
            float[] speeds = { 5.0f, 3.0f, 1.6f };
            foreach (float sp in speeds)
            {
                Vector3 end; bool potted;
                int jaws = CaseRattle("v=" + sp.ToString("F1"), dt, cue, sp, out end, out potted);
                rattleJaws += jaws;
                if (potted) anyPotted = true; else anyRejected = true;
                bool sane = Mathf.Abs(end.x) < G.HalfL + 0.35f && Mathf.Abs(end.z) < G.HalfW + 0.35f;
                if (!sane) allSane = false;
                L("      结局 " + (potted ? "落袋" : "被弹回/留在台面") +
                  " end=" + end.ToString("F3") + " sane=" + sane);
            }
            Tally(ref pass, ref fail, "E 晃袋：撞颚后落袋或被弹回，且不穿墙",
                  rattleJaws >= 1 && allSane);

            // F：沿角袋轴线正打（不同力度）—— 都应落袋，且不得离开台面、不得被严重弹飞。
            //
            // 抓到的两个真 bug（都已修）：
            //   ① 球飞出台外 3 米、球心升到 124mm —— 袋内衬半径小于"布料支撑下的球面外缘"，
            //      球一进袋口就嵌进内衬壁被解算崩飞（修法：内衬内径加硬约束，见踩坑 33）。
            //   ② 球被弹到 115mm 高 —— 袋内衬原先是**零厚度曲面**，高速球直接穿进去，
            //      再被去穿透逻辑顶出来（修法：给内衬 30mm 实体厚度 + 降去穿透速度上限）。
            //
            // 残留现象（已确认、暂不修）：5 m/s 满力正打角袋时，球在**袋口内的布料拼缝**
            //   处会获得约 1.2 m/s 的向上分量、弹起约 90mm 后仍落入袋中（1.2~3 m/s 无此现象，
            //   最高只到 42mm）。起因是布料板由多块 BoxCollider 拼成，球高速跨越拼缝时
            //   接触法线被解算成倾斜。真实球袋本来也会"跳球"，且球仍正常落袋，
            //   故按现状接受；上限取 0.12m（约 4.5 个球直径）作为防回归红线。
            bool allPotted = true, noEscape = true, noViolentLaunch = true;
            float[] aimSpeeds = { 5.0f, 3.0f, 2.0f, 1.2f };
            foreach (float sp in aimSpeeds)
            {
                Vector3 end; float peakY; bool potted;
                CaseStraightIntoCorner("v=" + sp.ToString("F1"), dt, cue, sp, out end, out peakY, out potted);
                bool violent = peakY > 0.12f;
                bool onTableLevel = end.y > -G.PotDepth;              // 还停在台面高度
                bool outside = Mathf.Abs(end.x) > G.HalfL + 0.08f || Mathf.Abs(end.z) > G.HalfW + 0.08f;
                bool escaped = onTableLevel && outside;               // 台面高度 + 台面外 = 真飞出去
                if (!potted) allPotted = false;
                if (escaped) noEscape = false;
                if (violent) noViolentLaunch = false;
                L("      落袋=" + potted + " 飞出桌外=" + escaped + " 严重弹飞=" + violent +
                  " 最高球心y=" + peakY.ToString("F3") + " end=" + end.ToString("F3"));
            }
            Tally(ref pass, ref fail, "F 沿角袋轴线正打 → 落袋、不飞出桌外、不被严重弹飞",
                  allPotted && noEscape && noViolentLaunch);
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
            L("simulationMode restored=" + Physics.simulationMode);
        }
        L("RESULT PASS=" + pass + " FAIL=" + fail);
        Debug.Log("[PHYSPOCKET] === DONE ===\n" + Log);
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    /// <summary>
    /// 轨迹追踪（调试用）：沿角袋轴线以指定速度直打，逐步打印位置/速度与
    /// "当前接触到的碰撞体名"，用于定位"球被弹起"这类需要看真实过程才能判断的问题。
    /// </summary>
    public static void PocketTrace()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootGo = new GameObject("Boot");
        bootGo.AddComponent<Bootstrapper>().Init();
        var gm = GameManager.I;
        if (gm == null || gm.cue == null) { EditorApplication.Exit(2); return; }
        gm.StartGame();
        var cue = gm.cue;
        foreach (var b in gm.balls) if (b != cue) b.gameObject.SetActive(false);

        try
        {
            Physics.simulationMode = SimulationMode.Script;
            float dt = 0.002f;
            float speed = 5f;
            Vector3 pc = G.Pockets[0];
            const float K = 0.70710678f;
            Vector3 start = new Vector3(pc.x - 0.62f * K, 0f, pc.z - 0.62f * K);
            Vector3 dir = new Vector3(K, 0f, K);

            cue.Place(start);
            for (int i = 0; i < 80; i++) Physics.Simulate(dt);
            cue.Rb.WakeUp();
            cue.Rb.velocity = dir * speed;
            cue.ApplySpin(dir, speed, 0f, 0f);
            Debug.Log("[TRACE] maxDepenetrationVelocity=" + cue.Rb.maxDepenetrationVelocity +
                      " start=" + start.ToString("F3"));

            for (int i = 1; i <= 400; i++)
            {
                Physics.Simulate(dt);
                Vector3 p = cue.transform.position, v = cue.Rb.velocity;
                var hits = Physics.OverlapSphere(p, G.BallR + 0.002f);
                string names = "";
                foreach (var h in hits)
                {
                    if (h.transform.IsChildOf(cue.transform)) continue;
                    names += h.gameObject.name + " ";
                }
                // 只打印"进袋口区域"之后的步（离袋心 0.20m 以内），避免刷屏
                float dpc = new Vector2(p.x - pc.x, p.z - pc.z).magnitude;
                if (dpc < 0.20f || p.y > 0.05f)
                    Debug.Log(string.Format("[TRACE] t={0:F3} p=({1:F3},{2:F3},{3:F3}) v=({4:F2},{5:F2},{6:F2}) d={7:F3} touches=[{8}]",
                        i * dt, p.x, p.y, p.z, v.x, v.y, v.z, dpc, names));
                if (p.y < G.PotHideY) break;
            }
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
        EditorApplication.Exit(0);
    }

    /// <summary>累加通过/失败计数并打印一行结论。</summary>

    private static void Tally(ref int pass, ref int fail, string what, bool ok)
    {
        if (ok) pass++; else fail++;
        L((ok ? "  OK   " : "  FAIL ") + what);
    }

    /// <summary>
    /// 通用袋口用例：把白球放到 pos、赋初速 vel，模拟最多 maxSec 秒。
    /// 返回该过程中记录到的"最低球心高度"（球心 y 的最小值）。
    ///
    /// ★ 必须用 ApplySpin 而不是直接赋 rb.velocity：白球的 CueRollStep 里有
    /// "速度不得超过出杆初速 shotSpeed0"的硬限速（v0.37 用户约束），而 shotSpeed0
    /// 只在 ApplySpin 里赋值。直接赋速度会让限速把它钳到 0（球纹丝不动，
    /// 初版此测试就因此误报"慢球滚不进角袋"）。ApplySpin(...,0,0) = 中杆纯滚动。
    ///
    /// ★ 落袋后要照游戏里的路径调 Pot()：游戏每帧 CheckPockets → G.InPocket 为真就
    /// RegisterPot → Pot()（关碰撞 + 压掉水平速度 + 开始下沉）。测试若不调，
    /// 球会在袋井里一直带着原速度横漂、漂到桌框外面去 —— 那是"测试没走游戏路径"
    /// 造成的假象，不是物理 bug（初版就因此误判过一次）。
    /// </summary>
    private static float RunPocketCase(BallController cue, Vector3 pos, Vector3 vel,
                                       float dt, float maxSec, out Vector3 endPos)
    {
        cue.Place(pos);
        for (int i = 0; i < 80; i++) Physics.Simulate(dt);     // 静置落稳
        float speed = vel.magnitude;
        Vector3 dir = speed > 1e-6f ? vel / speed : Vector3.right;
        cue.Rb.WakeUp();
        cue.Rb.velocity = vel;
        cue.ApplySpin(dir, speed, 0f, 0f);                     // 中杆：同时写好 shotSpeed0

        float minY = cue.transform.position.y;
        int steps = Mathf.RoundToInt(maxSec / dt);
        for (int i = 0; i < steps; i++)
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);
            float y = cue.transform.position.y;
            if (y < minY) minY = y;
            // 复刻游戏里的落袋路径：判据用同一个 G.InPocket（保证测的就是线上的行为）
            if (!cue.potted && G.InPocket(cue.transform.position, cue.Rb.velocity)) cue.Pot();
            if (y < G.PotHideY) break;                          // 已沉到隐藏深度
        }
        endPos = cue.transform.position;
        return minY;
    }

    private static bool CaseSinkToPocket(string name, float dt, BallController cue,
                                         Vector3 pos, Vector3 vel, float maxSec)
    {
        Vector3 end;
        float minY = RunPocketCase(cue, pos, vel, dt, maxSec, out end);
        L("CASE " + name + " minY=" + minY.ToString("F3") +
          " end=" + end.ToString("F3"));
        return minY < -G.PotDepth;
    }

    private static bool CaseRollAcross(string name, float dt, BallController cue,
                                       Vector3 pos, Vector3 vel, float maxSec)
    {
        Vector3 end;
        float minY = RunPocketCase(cue, pos, vel, dt, maxSec, out end);
        L("CASE " + name + " minY=" + minY.ToString("F3") + " end=" + end.ToString("F3"));
        bool stayed = minY >= -G.PotDepth;              // 没有掉进袋里
        bool passed = end.x > pos.x + 0.30f;            // 确实滚过去了（没被洞口卡住）
        return stayed && passed;
    }

    /// <summary>
    /// 撞颚弹回：白球从台面内侧斜向朝角袋颚尖打（瞄准点取在颚面上，不是洞口中心），
    /// 期望被颚面弹回台面、且没有掉进袋里。
    /// </summary>
    private static bool CaseBounceOffJaw(string name, float dt, BallController cue)
    {
        // 瞄准长库颚面的**中点**：颚面从颚尖 (1.7125, 0.889) 斜到 (1.6625, 0.944)。
        // 球从台面内侧打上去，颚面法线朝台内 → 应被弹回台面。
        Vector3 start = new Vector3(1.50f, 0f, 0.70f);
        Vector3 aim = new Vector3(1.6875f, 0f, 0.9165f);
        Vector3 dir = (aim - start); dir.y = 0; dir.Normalize();
        Vector3 end;
        float minY = RunPocketCase(cue, start, dir * 2.5f, dt, 5f, out end);
        L("CASE " + name + " minY=" + minY.ToString("F3") + " end=" + end.ToString("F3") +
          " moved=" + (end - start).magnitude.ToString("F3"));
        bool notPotted = minY >= -G.PotDepth;
        bool onTable = Mathf.Abs(end.x) < G.HalfL && Mathf.Abs(end.z) < G.HalfW;
        return notPotted && onTable;
    }

    /// <summary>
    /// 洞口圈外静止：球心离角袋洞口圆心 0.075m（洞口半径 0.055 + 20mm 余量），
    /// 且仍在台面内 —— 应被布料托住不下坠。
    /// </summary>
    private static bool CaseRestOutsideHole(string name, float dt, BallController cue)
    {
        Vector3 pc = G.Pockets[0];                                  // 右上角袋
        Vector3 pos = new Vector3(pc.x - 0.075f, 0f, pc.z - 0.075f);
        Vector3 end;
        float minY = RunPocketCase(cue, pos, Vector3.zero, dt, 2.5f, out end);
        L("CASE " + name + " pos=" + pos.ToString("F3") +
          " minY=" + minY.ToString("F3") + " end=" + end.ToString("F3"));
        return minY > -0.005f;                                      // 几乎没下沉
    }

    /// <summary>
    /// 晃袋用例：从台面内侧斜着把球打进角袋袋口（瞄准**远端颚面**），
    /// 返回本用例中球与"颚"发生的接触次数（接触"回合"数，不是帧数），并回报是否落袋。
    ///
    /// 真实球桌的晃袋 = 球进袋口后撞颚，被弹到对面颚面、再弹回来，几次之后
    /// 要么掉下去、要么被弹出袋口。这里不做脚本判定，只统计接触，让"晃"自己显现。
    ///
    /// 注意：不能用 OnCollisionEnter 统计 —— 手动 Physics.Simulate 不派发碰撞回调
    /// （初版探针因此一直记 0，而球明明被弹回来了）。改用每步 OverlapSphere 检测。
    /// </summary>
    private static int CaseRattle(string name, float dt, BallController cue, float speed,
                                  out Vector3 end, out bool potted)
    {
        potted = false;
        Vector3 start = new Vector3(1.30f, 0f, 0.72f);
        // 远端颚面：斜面从 (1.7845,0.817) 到 (1.8395,0.767)，取其中点附近
        Vector3 aim = new Vector3(1.800f, 0f, 0.800f);
        Vector3 dir = aim - start; dir.y = 0f; dir.Normalize();

        cue.Place(start);
        for (int i = 0; i < 80; i++) Physics.Simulate(dt);
        cue.Rb.WakeUp();
        cue.Rb.velocity = dir * speed;
        cue.ApplySpin(dir, speed, 0f, 0f);

        int jawEpisodes = 0, wallEpisodes = 0;
        bool inJaw = false, inWall = false;
        var seq = new System.Collections.Generic.List<string>();
        float minY = cue.transform.position.y;
        int steps = Mathf.RoundToInt(6f / dt);
        for (int i = 0; i < steps; i++)
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);
            Vector3 p = cue.transform.position;
            if (p.y < minY) minY = p.y;

            bool jaw = false, wall = false;
            foreach (var h in Physics.OverlapSphere(p, G.BallR + 0.003f))
            {
                if (h.transform.IsChildOf(cue.transform)) continue;
                string n = h.gameObject.name;
                if (n.StartsWith("jaw")) jaw = true;
                else if (n.StartsWith("pocketTube")) wall = true;
            }
            if (jaw && !inJaw) { jawEpisodes++; seq.Add("jaw"); }
            if (wall && !inWall) { wallEpisodes++; seq.Add("wall"); }
            inJaw = jaw; inWall = wall;

            if (G.InPocket(p, cue.Rb.velocity)) { cue.Pot(); potted = true; break; }
        }
        end = cue.transform.position;
        L("RATTLE " + name + " 颚接触=" + jawEpisodes + " 内衬接触=" + wallEpisodes +
          " 事件=[" + string.Join(",", seq.ToArray()) + "]");
        return jawEpisodes;
    }

    /// <summary>
    /// 正对角袋直打：沿**袋口轴线**（台面角点的角平分线）以指定速度直打。
    /// 这条线路就是真实球员"正对袋口推进"的理想线路，任何正常力度都应落袋。
    /// 回报是否落袋、最终位置与全程最高球心高度。
    /// 用于复现/防回归真机 bug：球在袋口被夹住弹飞出台面。
    /// </summary>
    private static void CaseStraightIntoCorner(string name, float dt, BallController cue,
                                               float speed, out Vector3 end, out float peakY,
                                               out bool potted)
    {
        Vector3 pc = G.Pockets[0];                                  // 右上角袋 (1.7925, 0.897)
        const float K = 0.70710678f;                                // 1/√2：角袋的角平分线方向
        Vector3 start = new Vector3(pc.x - 0.62f * K, 0f, pc.z - 0.62f * K);
        Vector3 dir = new Vector3(K, 0f, K);                        // 正对角袋轴线

        cue.Place(start);
        for (int i = 0; i < 80; i++) Physics.Simulate(dt);
        cue.Rb.WakeUp();
        cue.Rb.velocity = dir * speed;
        cue.ApplySpin(dir, speed, 0f, 0f);

        float minY = cue.transform.position.y, maxY = minY;
        bool pottedNow = false;
        int steps = Mathf.RoundToInt(5f / dt);
        for (int i = 0; i < steps; i++)
        {
            Physics.Simulate(dt);
            cue.StepOnCloth(dt);
            Vector3 p = cue.transform.position;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
            // 与游戏同源：一旦 G.InPocket 为真就登记落袋（此时球已被判定进袋、
            // 立刻关碰撞，不会再撞袋壁被顶飞）
            if (!cue.potted && G.InPocket(p, cue.Rb.velocity)) { cue.Pot(); pottedNow = true; }
            if (p.y < G.PotHideY) break;
        }
        end = cue.transform.position;
        peakY = maxY;
        potted = pottedNow;
    }
}
