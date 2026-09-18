// =====================================================================================
// PhysTest.cs —— 编辑器内离线物理测试（Editor 专用，不会打进 APK）
//
// 提供两个入口，均为无头批处理运行（不打开界面）：
//
// ① PhysTest.Run —— 开球回归测试
//    复现"白球开球→撞散球堆"全过程，验证整体物理健康度。
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
// 注意：测试结束必须恢复 SimulationMode.FixedUpdate —— 否则该设置会被编辑器
// 持久化进 DynamicsManager.asset 并打进 APK，设备上物理完全不步进（历史事故）。
// =====================================================================================
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
        try { gm.StartGame(); } catch (System.Exception e) { L("StartGame EXC: " + e); }
        try
        {
            Physics.simulationMode = SimulationMode.Script;       // 切手动步进（Script=由代码调 Simulate）
            gm.Shoot(new Vector3(1f, 0f, 0f), 0.5f);              // 力度 0.5 ≈ 2.58 m/s
            L("after Shoot velocity=" + cue.Rb.velocity.ToString("F3"));
        }
        catch (System.Exception e) { L("Shoot EXC: " + e); }

        float dt = 0.004f;
        for (int i = 1; i <= 1125; i++)                           // 手动推进 4.5 秒
        {
            Physics.Simulate(dt);
            if (i % 40 == 0)
                L("t=" + (i * dt).ToString("F2") + " cue=" + cue.transform.position.ToString("F3") +
                  " v=" + cue.Rb.velocity.ToString("F2"));
        }

        gm.LogBalls("AFTER-BREAK");
        Physics.simulationMode = SimulationMode.FixedUpdate;      // 还原工程物理设置
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
        gm.StartGame();                                           // 布球

        var cue = gm.cue;
        // 隐藏其余 21 颗球（禁用其碰撞体），做成单球实验，排除干扰
        foreach (var b in gm.balls)
            if (b != cue) b.gameObject.SetActive(false);

        L("bounceThreshold=" + Physics.bounceThreshold.ToString("F3") +
          "  (球撞库法向速度低于它时不反弹 → 粘库)");
        Physics.simulationMode = SimulationMode.Script;       // 切手动步进
        float dt = 0.005f;

        // 三个用例：位置 / 速度（见文件头说明）
        // B、C 的起点放在距库边 0.53m 处：太远的话低速球会被台呢摩擦减到停、碰不到库
        RunCase("A-fast-normal", new Vector3(1.2f, 0, 0),    new Vector3(1.2f, 0, 0),    dt, cue);
        RunCase("B-slow-normal", new Vector3(1.2f, 0, 0),    new Vector3(0.35f, 0, 0),   dt, cue);
        RunCase("C-grazing",     new Vector3(1.2f, 0, -0.4f), new Vector3(0.35f, 0, -0.2f), dt, cue);

        Physics.simulationMode = SimulationMode.FixedUpdate;      // 还原工程物理设置
        Debug.Log("[PHYSCUSH] === DONE ===\n" + Log);
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 运行单个撞库用例。
    /// 判定 bounced：模拟期间球心最贴近库边的位置记为 maxX，若结束时球已从 maxX
    /// 回退 3cm 以上，说明发生了反弹（低速反弹只有 ~0.12 m/s，3 秒内回退 7~20cm，
    /// 用"回退量"而非"绝对距离"才能同时判定快/慢反弹）。
    /// </summary>
    private static void RunCase(string name, Vector3 pos, Vector3 vel, float dt, BallController cue)
    {
        cue.Place(pos);                       // 传送到起点（内部会清速度 + WakeUp）
        cue.Rb.velocity = vel;                // 赋初速
        L("CASE " + name + " START pos=" + pos.ToString("F3") + " v=" + vel.ToString("F3"));

        int steps = 800;                      // 模拟 4 秒
        float maxX = pos.x;                   // 球心到达过的最靠右位置（短库侧）
        for (int i = 1; i <= steps; i++)
        {
            Physics.Simulate(dt);
            float x = cue.transform.position.x;
            if (x > maxX) maxX = x;
            if (i % 80 == 0)                  // 每 0.4 秒采样一次
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
}
