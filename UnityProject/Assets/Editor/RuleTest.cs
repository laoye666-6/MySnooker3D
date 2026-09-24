// =====================================================================================
// RuleTest.cs —— 斯诺克规则离线回归测试（纯逻辑，不走物理、不需要场景）
//
// 运行方式（cmd，去掉 -nographics 也行，本测试不碰物理）：
//   "E:\Program files\2022.3.62f3c1\Editor\Unity.exe" -batchmode -quit -nographics ^
//     -projectPath "E:\Snooker3D" -executeMethod RuleTest.Run -logFile "E:\Snooker\logs\ruletest.log"
//
// 为什么需要它：v0.33 的规则缺陷（换手后没回到红球、清彩阶段犯规落袋导致一局永远打不完、
// 罚分不足 4 分、只剩黑球时终局判定不对）都只能在模拟器上"真打"才可能发现，实际全漏了。
// v0.34 把规则抽成纯函数 SnookerRules.Evaluate 后，就能像下面这样逐条断言。
//
// 每个用例都注明规则条款（WPBSA Section 3），失败会打印期望值与实际值并以退出码 1 结束。
// =====================================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class RuleTest
{
    static int pass, fail;
    static readonly List<string> failures = new List<string>();

    // ---------------- 构造工具 ----------------
    static BallKind[] AllColors()
    {
        return new[] { BallKind.Yellow, BallKind.Green, BallKind.Brown, BallKind.Blue, BallKind.Pink, BallKind.Black };
    }

    /// 台面剩余彩球 = 全彩球去掉 gone 里的
    static BallKind[] ColorsMinus(params BallKind[] gone)
    {
        var list = new List<BallKind>();
        foreach (var k in AllColors())
            if (System.Array.IndexOf(gone, k) < 0) list.Add(k);
        return list.ToArray();
    }

    static TableState St(bool colorsPhase, bool freeColorPending, int redsLeft, BallKind[] colorsOnTable)
    {
        var s = new TableState();
        s.colorsPhase = colorsPhase;
        s.freeColorPending = freeColorPending;
        s.redsLeft = redsLeft;
        s.colorsOnTable = colorsOnTable;
        return s;
    }

    static ShotFacts F(bool hitNothing, BallKind firstHit, params BallKind[] potted)
    {
        var f = new ShotFacts();
        f.hitNothing = hitNothing;
        f.cushionContact = false;
        f.firstHit = firstHit;
        f.potted = potted;
        return f;
    }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Debug.Log("[RULETEST] PASS  " + name); }
        else { fail++; failures.Add(name + "  ->  " + detail); Debug.LogError("[RULETEST] FAIL  " + name + "  ->  " + detail); }
    }

    static bool Has(BallKind[] arr, BallKind k) { return arr != null && System.Array.IndexOf(arr, k) >= 0; }

    // ---------------- v0.35 新增：指定彩球 / 犯规与未击到 / 自由球 ----------------

    /// 带指定信息的事实构造：nomSet=true 时 nomin 为击球方指定的球
    static ShotFacts FN(bool hitNothing, BallKind firstHit, bool nomSet, BallKind nomin, bool snookered, params BallKind[] potted)
    {
        var f = new ShotFacts();
        f.hitNothing = hitNothing;
        f.cushionContact = false;
        f.firstHit = firstHit;
        f.nominated = nomin;
        f.nominatedSet = nomSet;
        f.snookered = snookered;
        f.potted = potted;
        return f;
    }

    static TableState StFB(bool colorsPhase, bool freeColorPending, int redsLeft, BallKind[] colors)
    {
        var s = St(colorsPhase, freeColorPending, redsLeft, colors);
        s.freeBallActive = true;
        return s;
    }

    public static void Run()
    {
        pass = 0; fail = 0; failures.Clear();
        Debug.Log("[RULETEST] ===== 斯诺克规则回归开始 =====");

        // ---- 1. Rule 10.3：只要台面还有红球，换手后接台方永远打红球 ----
        {
            var pre = St(false, true, 10, AllColors());                  // 玩家刚进红球，下一杆本该打彩球
            var oc = SnookerRules.Evaluate(pre, F(true, BallKind.Red), 10, 8);   // 空杆犯规 → 换手
            Check("1.换手后球 on 回到红球(Rule 10.3)",
                  oc.handover && !oc.nextFreeColorPending && !oc.nextColorsPhase,
                  "handover=" + oc.handover + " free=" + oc.nextFreeColorPending + " colors=" + oc.nextColorsPhase);
            Check("1b.该杆空杆罚 4 分", oc.foulPts == 4, "foulPts=" + oc.foulPts);
        }

        // ---- 2. 清彩阶段：目标球与白球同杆落袋（v0.33 死局根因）应回点且目标不变 ----
        {
            var pre = St(true, false, 0, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Yellow, BallKind.Yellow, BallKind.Cue), 20, 20);
            Check("2.清彩犯规落袋的彩球必须回点(否则一局打不完)",
                  Has(oc.respotColors, BallKind.Yellow), "respot=" + string.Join(",", oc.respotColors));
            Check("2b.犯规杆不计分", oc.legalPts == 0, "legalPts=" + oc.legalPts);
            Check("2c.目标球仍是黄球", oc.nextTargetColor == BallKind.Yellow, "target=" + oc.nextTargetColor);
            Check("2d.仍在清彩阶段", oc.nextColorsPhase, "colors=" + oc.nextColorsPhase);
        }

        // ---- 3. 清彩阶段合法进黄：不回点、继续、目标推进到绿 ----
        {
            var pre = St(true, false, 0, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Yellow, BallKind.Yellow), 20, 20);
            Check("3.清彩合法进黄得 2 分且不回点",
                  oc.foulPts == 0 && oc.legalPts == 2 && oc.respotColors.Length == 0,
                  "foul=" + oc.foulPts + " pts=" + oc.legalPts + " respot=" + oc.respotColors.Length);
            Check("3b.目标推进到绿球", oc.nextTargetColor == BallKind.Green, "target=" + oc.nextTargetColor);
            Check("3c.同一玩家继续击球", !oc.handover, "handover=" + oc.handover);
        }

        // ---- 4. 只剩黑球：合法进黑、比分不同 → 终局（Rule 4(a)）----
        {
            var pre = St(true, false, 0, new[] { BallKind.Black });
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Black, BallKind.Black), 40, 30);
            Check("4.进黑球比分不同即终局", oc.frameOver && oc.legalPts == 7 && !oc.respotBlackTie,
                  "frameOver=" + oc.frameOver + " pts=" + oc.legalPts + " tie=" + oc.respotBlackTie);
        }

        // ---- 5. 只剩黑球：犯规（未击中）也应终局，且罚 7 分（Rule 4(a) + Rule 10(a)(vi)）----
        {
            var pre = St(true, false, 0, new[] { BallKind.Black });
            var oc = SnookerRules.Evaluate(pre, F(true, BallKind.Black), 40, 30);
            Check("5.只剩黑球时犯规即终局(Rule 4a)", oc.frameOver, "frameOver=" + oc.frameOver);
            Check("5b.球 on 为黑球时空杆罚 7 分(Rule 10a)", oc.foulPts == 7, "foulPts=" + oc.foulPts);
        }

        // ---- 6. 只剩黑球：犯规导致比分打平 → 重置黑球继续（Rule 4(b)）----
        {
            var pre = St(true, false, 0, new[] { BallKind.Black });
            var oc = SnookerRules.Evaluate(pre, F(true, BallKind.Black), 40, 33);   // 对手 +7 → 40:40
            Check("6.打平时重置黑球继续(Rule 4b)",
                  oc.respotBlackTie && !oc.frameOver, "tie=" + oc.respotBlackTie + " frameOver=" + oc.frameOver);
        }

        // ---- 7. 只剩黑球：合法进黑导致打平 → 重置黑球 ----
        {
            var pre = St(true, false, 0, new[] { BallKind.Black });
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Black, BallKind.Black), 40, 47);
            Check("7.进黑后打平也要重置黑球", oc.respotBlackTie && !oc.frameOver,
                  "tie=" + oc.respotBlackTie + " frameOver=" + oc.frameOver);
        }

        // ---- 8. 只剩黑球：合法安全球（不得分不犯规）不终局 ----
        {
            var pre = St(true, false, 0, new[] { BallKind.Black });
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Black), 40, 30);
            Check("8.安全球不终局，换手继续",
                  !oc.frameOver && oc.handover && oc.nextTargetColor == BallKind.Black,
                  "frameOver=" + oc.frameOver + " handover=" + oc.handover);
        }

        // ---- 9. 误落彩球罚分必须取 max(4, 球on, 涉及球)（Rule 10）----
        {
            var pre = St(false, false, 10, AllColors());                 // 球 on = 红球
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red, BallKind.Red, BallKind.Yellow), 10, 10);
            Check("9.球 on 红时误落黄球应罚 4 分(不是 2)", oc.foulPts == 4, "foulPts=" + oc.foulPts);
            Check("9b.犯规杆不得分", oc.legalPts == 0, "legalPts=" + oc.legalPts);
            Check("9c.误落的黄球回点", Has(oc.respotColors, BallKind.Yellow), "respot=" + string.Join(",", oc.respotColors));
            Check("9d.红球不回点", !Has(oc.respotColors, BallKind.Red), "respot=" + string.Join(",", oc.respotColors));
        }

        // ---- 10. 先碰非球 on：取"球 on 分值 / 涉及球分值"高者 ----
        {
            var pre = St(false, false, 10, AllColors());                 // 球 on = 红球
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Blue), 10, 10);
            Check("10.先碰蓝球罚 5 分(Rule 10b)", oc.foulPts == 5, "foulPts=" + oc.foulPts);
        }

        // ---- 11. 连续两杆打红 = 7 分（Rule 10(d)(iv)）----
        {
            var pre = St(false, true, 10, AllColors());                  // 球 on = 任意彩球
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red), 10, 10);
            Check("11.该打彩球却打红球罚 7 分(Rule 10d)", oc.foulPts == 7, "foulPts=" + oc.foulPts);
        }

        // ---- 12. 同杆多犯规只取最高（Rule 11(g)）----
        {
            var pre = St(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red, BallKind.Cue, BallKind.Blue), 10, 10);
            Check("12.白球落袋(4)+误落蓝球(5) 取 5 分", oc.foulPts == 5, "foulPts=" + oc.foulPts);
        }

        // ---- 13. 红球阶段合法进红 → 继续击球、下一杆任意彩球 ----
        {
            var pre = St(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red, BallKind.Red), 10, 10);
            Check("13.进红得 1 分并继续击球",
                  oc.legalPts == 1 && !oc.handover && oc.nextFreeColorPending,
                  "pts=" + oc.legalPts + " handover=" + oc.handover + " free=" + oc.nextFreeColorPending);
            Check("13b.剩余红球数递减", oc.redsPotted == 1, "redsPotted=" + oc.redsPotted);
        }

        // ---- 14. 一杆打进两颗红球 = 2 分（Rule 3(e)）----
        {
            var pre = St(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red, BallKind.Red, BallKind.Red), 10, 10);
            Check("14.同杆两颗红球得 2 分", oc.legalPts == 2 && oc.redsPotted == 2,
                  "pts=" + oc.legalPts + " reds=" + oc.redsPotted);
        }

        // ---- 15. 红球阶段打进任意彩球 → 得分且回点、下一杆回到红球 ----
        {
            var pre = St(false, true, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Pink, BallKind.Pink), 10, 10);
            Check("15.任意彩球得 6 分、回点、下一杆打红",
                  oc.legalPts == 6 && Has(oc.respotColors, BallKind.Pink) && !oc.nextFreeColorPending,
                  "pts=" + oc.legalPts + " respot=" + string.Join(",", oc.respotColors) + " free=" + oc.nextFreeColorPending);
        }

        // ---- 16. 最后一红之后的"任选彩球 → 升序清彩"切换（v0.42 修正）----
        //
        // 依据 WPBSA 2024-25 Section 3 Rule 3(h)：
        //   (i)   进红（或当红打的自由球）→ 同一球员打一颗任选彩球，进袋计分后【回点】
        //   (ii)  红球全部离台、且"最后一红之后有彩球被【击打过】"之后 → 彩球开始升序成为球 on
        //   (iii) 彩球随即按分值升序成为球 on，进袋后不再回点
        //
        // ★ 3(h)(ii) 的措辞是 "a colour has been played AT" —— 只要求那颗任选彩球
        //   【被击打过】，不要求打进。旧版只在"合法打进"时才切阶段，所以未进/犯规后
        //   接台方仍能任选彩球打（清彩阶段混乱的根因）。以下 16b~16e 锁死正确行为。
        {
            var pre1 = St(false, false, 1, AllColors());
            var oc1 = SnookerRules.Evaluate(pre1, F(false, BallKind.Red, BallKind.Red), 10, 10);
            Check("16.最后一红进袋后仍需打任选彩球",
                  oc1.nextFreeColorPending && !oc1.nextColorsPhase,
                  "free=" + oc1.nextFreeColorPending + " colors=" + oc1.nextColorsPhase);

            // 16b：任选彩球这一杆【空杆】（没碰到任何球）→ 已"击打过彩球"，升序开始 → 黄球
            var pre2 = St(false, true, 0, AllColors());
            var oc2 = SnookerRules.Evaluate(pre2, F(true, BallKind.Red), 10, 10);
            Check("16b.任选彩球空杆后 → 接台方打黄球(而非继续任选)",
                  oc2.nextColorsPhase && !oc2.nextFreeColorPending &&
                  oc2.nextTargetColor == BallKind.Yellow,
                  "free=" + oc2.nextFreeColorPending + " colors=" + oc2.nextColorsPhase +
                  " target=" + oc2.nextTargetColor);

            // 16c：任选彩球这一杆【打进了】（指定蓝球并进袋）→ 蓝球回点、升序从黄球开始
            var oc3 = SnookerRules.Evaluate(pre2, F(false, BallKind.Blue, BallKind.Blue), 10, 10);
            Check("16c.任选彩球打进后 → 该彩球回点、目标为黄球",
                  oc3.nextColorsPhase && !oc3.nextFreeColorPending &&
                  oc3.nextTargetColor == BallKind.Yellow && Has(oc3.respotColors, BallKind.Blue),
                  "colors=" + oc3.nextColorsPhase + " target=" + oc3.nextTargetColor +
                  " respot=" + string.Join(",", oc3.respotColors));

            // 16d：任选彩球这一杆【犯规】（先碰彩球但白球落袋）→ 同样进入升序，目标黄球
            var oc4 = SnookerRules.Evaluate(pre2, F(false, BallKind.Green, BallKind.Cue), 10, 10);
            Check("16d.任选彩球犯规后 → 同样进入升序清彩、目标黄球",
                  oc4.foulPts > 0 && oc4.nextColorsPhase && !oc4.nextFreeColorPending &&
                  oc4.nextTargetColor == BallKind.Yellow,
                  "foul=" + oc4.foulPts + " colors=" + oc4.nextColorsPhase +
                  " target=" + oc4.nextTargetColor);

            // 16e：台面【还有红球】时，任选彩球没打进 → 接台方回红球（Rule 3(g)）
            var pre5 = St(false, true, 5, AllColors());
            var oc5 = SnookerRules.Evaluate(pre5, F(false, BallKind.Brown), 10, 10);
            Check("16e.仍有红球时任选彩球未进 → 接台方打红球",
                  !oc5.nextColorsPhase && !oc5.nextFreeColorPending,
                  "free=" + oc5.nextFreeColorPending + " colors=" + oc5.nextColorsPhase);

            // 16f：仍有红球时任选彩球【打进】→ 接台方回红球（该彩球回点）
            var oc6 = SnookerRules.Evaluate(pre5, F(false, BallKind.Brown, BallKind.Brown), 10, 10);
            Check("16f.仍有红球时任选彩球进袋 → 回点、接台方打红球",
                  !oc6.nextColorsPhase && !oc6.nextFreeColorPending &&
                  Has(oc6.respotColors, BallKind.Brown),
                  "free=" + oc6.nextFreeColorPending + " colors=" + oc6.nextColorsPhase +
                  " respot=" + string.Join(",", oc6.respotColors));
        }

        // ---- 16g~16l：清彩阶段的球 on 推进与犯规（Rule 3(h)(iii) / Rule 10）----
        {
            // 16g：清彩阶段合法进黄 → 目标绿、黄球不回点、同一球员继续
            var p = St(true, false, 0, AllColors());
            var oc = SnookerRules.Evaluate(p, F(false, BallKind.Yellow, BallKind.Yellow), 10, 10);
            Check("16g.清彩进黄 → 得2分、黄球不回点、目标绿、继续击球",
                  oc.legalPts == 2 && oc.respotColors.Length == 0 &&
                  oc.nextTargetColor == BallKind.Green && !oc.handover,
                  "pts=" + oc.legalPts + " respot=" + oc.respotColors.Length +
                  " target=" + oc.nextTargetColor + " handover=" + oc.handover);

            // 16h：清彩阶段【先碰非目标球】→ 犯规、目标不变（接台方仍打同一颗）
            var oc2 = SnookerRules.Evaluate(p, F(false, BallKind.Brown), 10, 10);
            Check("16h.清彩先碰棕球(目标黄) → 犯规4分、目标仍为黄球",
                  oc2.foulPts == 4 && oc2.nextTargetColor == BallKind.Yellow && oc2.handover,
                  "foul=" + oc2.foulPts + " target=" + oc2.nextTargetColor + " handover=" + oc2.handover);

            // 16i：清彩阶段误落高分彩球 → 罚分取高分球值（进黄同时误落黑 → 7 分）
            var oc3 = SnookerRules.Evaluate(p, F(false, BallKind.Yellow, BallKind.Yellow, BallKind.Black), 10, 10);
            Check("16i.清彩进黄同时误落黑 → 罚7分、两颗都回点",
                  oc3.foulPts == 7 && Has(oc3.respotColors, BallKind.Yellow) &&
                  Has(oc3.respotColors, BallKind.Black) && oc3.legalPts == 0,
                  "foul=" + oc3.foulPts + " legal=" + oc3.legalPts +
                  " respot=" + string.Join(",", oc3.respotColors));

            // 16j：只剩黑球时犯规（空杆）→ 罚 7 分（球 on 为黑）
            var pb = St(true, false, 0, new[] { BallKind.Black });
            var oc4 = SnookerRules.Evaluate(pb, F(true, BallKind.Black), 10, 10);
            Check("16j.只剩黑球时空杆 → 罚7分", oc4.foulPts == 7, "foul=" + oc4.foulPts);

            // 16k：清彩阶段白球落袋 → 罚 4 分（球 on 为黄=2，取最低 4）
            var oc5 = SnookerRules.Evaluate(p, F(false, BallKind.Yellow, BallKind.Cue), 10, 10);
            Check("16k.清彩阶段白球落袋 → 罚4分", oc5.foulPts == 4, "foul=" + oc5.foulPts);

            // 16l：清彩阶段目标球与白球同杆落袋 → 目标球回点、目标不变（v0.33 死局）
            var oc6 = SnookerRules.Evaluate(p, F(false, BallKind.Yellow, BallKind.Yellow, BallKind.Cue), 10, 10);
            Check("16l.清彩目标球与白球同杆落袋 → 目标球回点、目标仍为黄球",
                  Has(oc6.respotColors, BallKind.Yellow) && oc6.nextTargetColor == BallKind.Yellow,
                  "respot=" + string.Join(",", oc6.respotColors) + " target=" + oc6.nextTargetColor);
        }

        // ---- 17. 清彩阶段打进目标彩球后才进入下一颗；打进非目标球不改目标 ----
        {
            var pre = St(true, false, 0, ColorsMinus(BallKind.Yellow));   // 黄球已清，当前目标=绿
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Brown, BallKind.Brown), 20, 20);
            Check("17.误落非目标彩球：罚分、回点、目标不变",
                  oc.foulPts == 4 && Has(oc.respotColors, BallKind.Brown) && oc.nextTargetColor == BallKind.Green,
                  "foul=" + oc.foulPts + " respot=" + string.Join(",", oc.respotColors) + " target=" + oc.nextTargetColor);
        }

        // ---- 18. 多颗彩球同时回点时高分优先（Rule 7(e)）----
        {
            var pre = St(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, F(false, BallKind.Red, BallKind.Yellow, BallKind.Blue), 10, 10);
            Check("18.回点顺序高分在前",
                  oc.respotColors.Length == 2 && oc.respotColors[0] == BallKind.Blue,
                  "respot=" + string.Join(",", oc.respotColors));
        }

        // ---- 19. 指定彩球（Rule 3(f)(i)(b)）：指定黑球后白球落袋，罚分按黑球 7 分 ----
        {
            var pre = St(false, true, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Black, true, BallKind.Black, false, BallKind.Cue), 10, 10);
            Check("19.指定黑球后白球落袋罚 7 分(而非 4)",
                  oc.foulPts == 7, "foulPts=" + oc.foulPts + " reason=" + oc.reason);
        }

        // ---- 20. 未指定彩球就犯规：罚分按 4 分下限 ----
        {
            var pre = St(false, true, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(true, BallKind.Red, false, BallKind.Black, false), 10, 10);
            Check("20.未指定彩球时空杆罚 4 分", oc.foulPts == 4, "foulPts=" + oc.foulPts);
        }

        // ---- 21. 指定黑球却先碰蓝球 → 按指定球分值罚 7 分 ----
        {
            var pre = St(false, true, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Blue, true, BallKind.Black, false), 10, 10);
            Check("21.指定黑却先碰蓝罚 7 分(取指定球分值)",
                  oc.foulPts == 7, "foulPts=" + oc.foulPts + " reason=" + oc.reason);
        }

        // ---- 22. 犯规与未击到（Rule 11(b)）：非斯诺克下的空杆判 Miss；被斯诺克则不判 ----
        {
            var pre = St(false, false, 10, AllColors());
            var ocNoSnk = SnookerRules.Evaluate(pre, FN(true, BallKind.Red, false, BallKind.Black, false), 10, 10);
            Check("22.未被斯诺克时空杆判 Miss", ocNoSnk.isMiss, "isMiss=" + ocNoSnk.isMiss);
            var ocSnk = SnookerRules.Evaluate(pre, FN(true, BallKind.Red, false, BallKind.Black, true), 10, 10);
            Check("22b.被斯诺克时空杆不判 Miss", !ocSnk.isMiss, "isMiss=" + ocSnk.isMiss);
            Check("22c.Miss 不影响罚分(仍 4 分)", ocSnk.foulPts == 4, "foulPts=" + ocSnk.foulPts);
        }

        // ---- 23. 自由球（Rule 12）：球 on 为红球，指定黑球当自由球打进 → 只算 1 分且黑球回点 ----
        {
            var pre = StFB(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Black, true, BallKind.Black, false, BallKind.Black), 10, 10);
            Check("23.自由球打进黑球只算 1 分(按真实球 on 红球)",
                  oc.foulPts == 0 && oc.legalPts == 1, "foul=" + oc.foulPts + " pts=" + oc.legalPts);
            Check("23b.自由球(黑球)必须回点", Has(oc.respotColors, BallKind.Black), "respot=" + string.Join(",", oc.respotColors));
            Check("23c.打进自由球后下一杆打任意彩球",
                  oc.nextFreeColorPending && !oc.nextColorsPhase, "free=" + oc.nextFreeColorPending);
            Check("23d.标记为自由球得分(147 连击不计)", oc.viaFreeBall, "viaFreeBall=" + oc.viaFreeBall);
        }

        // ---- 24. 自由球：指定红球当自由球打进 → 1 分且红球也回点、台面红球数不变 ----
        {
            var pre = StFB(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Red, true, BallKind.Red, false, BallKind.Red), 10, 10);
            Check("24.自由球打进红球得 1 分且红球回点(redsPotted=0)",
                  oc.legalPts == 1 && oc.redsPotted == 0 && Has(oc.respotReds, BallKind.Red),
                  "pts=" + oc.legalPts + " reds=" + oc.redsPotted + " respotReds=" + string.Join(",", oc.respotReds));
        }

        // ---- 25. 自由球在清彩阶段：真实球 on 为黄球，自由球打进黑球 → 算 2 分且目标仍是黄球 ----
        {
            var pre = StFB(true, false, 0, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Black, true, BallKind.Black, false, BallKind.Black), 20, 20);
            Check("25.清彩期自由球按真实目标球(黄)计 2 分",
                  oc.legalPts == 2, "pts=" + oc.legalPts);
            Check("25b.黑球回点、目标仍是黄球、仍在清彩",
                  Has(oc.respotColors, BallKind.Black) && oc.nextTargetColor == BallKind.Yellow && oc.nextColorsPhase,
                  "respot=" + string.Join(",", oc.respotColors) + " target=" + oc.nextTargetColor);
        }

        // ---- 26. 自由球杆先碰了非指定球 → 犯规 ----
        {
            var pre = StFB(false, false, 10, AllColors());
            var oc = SnookerRules.Evaluate(pre, FN(false, BallKind.Blue, true, BallKind.Black, false), 10, 10);
            Check("26.自由球未先碰指定球属犯规", oc.foulPts > 0, "foulPts=" + oc.foulPts + " reason=" + oc.reason);
        }

        // ---------------- 汇总 ----------------
        Debug.Log("[RULETEST] ===== 结束：PASS=" + pass + "  FAIL=" + fail + " =====");
        foreach (var s in failures) Debug.LogError("[RULETEST] 失败用例: " + s);
        if (fail == 0) Debug.Log("[RULETEST] ALL RULES OK");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }
}
