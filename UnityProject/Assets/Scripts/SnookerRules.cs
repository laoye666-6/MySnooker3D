// =====================================================================================
// SnookerRules.cs —— 纯斯诺克规则引擎（不依赖 MonoBehaviour / 物理，可离线单测）
//
// 为什么单独抽出来：v0.33 及以前规则逻辑写在 GameManager.EvaluateShot 里，与物理、UI、
// 球对象耦合，只能在模拟器上真打才能验证——结果"清彩阶段犯规落袋导致一局永远打不完"
// 这类致命缺陷一直没被发现。v0.34 起把"判定"与"执行"分开：
//   本文件：输入"出杆时刻的台面状态 + 本杆事实" → 输出"结算结果 + 新状态"（纯函数）
//   GameManager：把结果落到比分/回点/物理/UI 上
//   Editor/RuleTest.cs：对着一堆构造出来的局面做断言，不走物理也能回归规则
//
// ---------------- 依据规则（WPBSA/USSA 官方规则 Section 3，2024-25 版）-----------------
//   Rule 3(e)(f)      球 on 顺序：红 → 彩（任选并【指定】）→ 红 → 彩 …… 红球清完后仍需
//                     打一颗"任意彩球"，之后才按 黄绿咖啡蓝粉黑 升序清彩（3(f)(ii)）
//   Rule 3(f)(i)(b)   球 on 为彩球时必须指定哪一颗；未指定就击球属犯规，罚分按 4 分下限
//   Rule 10.3         只要台面还有红球，接台方（新一轮击球权的第一杆）永远以红球为球 on
//                     —— v0.33 的核心规则错误（换手后没复位"任意彩球"），v0.34 修正
//   Rule 10           罚分：默认 4 分，或"球 on 分值 / 涉及球分值"取高者；连续两杆打红
//                     （10(d)(iv)）7 分；空杆与白球落袋按球 on 分值取（10(a)）；同杆多犯规取最高（11(g)）
//   Rule 11(e)        犯规杆打进的所有球一律不计分；彩球回点、红球不回点（3(g)）
//   Rule 11(b)(c)     Foul and a Miss（犯规与未击到）：未先击中球 on 且当时【未被斯诺克】
//                     （存在直接击打线路）时判 Miss。判罚之外接台方有权选择：
//                       (a) 从当前球位自己打（默认）
//                       (b) 要求犯规方从当前球位重打（replay）
//                     Miss 不额外加分；本作实现选项 a/b（未实现"连续三次 Miss 判负"）
//   Rule 12           Free Ball（自由球）：犯规后接台方对所有球 on 都被斯诺克时，可指定
//                     任意一颗球作为"球 on"打完这一杆：打进按【真实球 on 的分值】计分，
//                     该球回点（即使是红球也回点），之后按真实球 on 继续
//   Rule 4(a)(b)      只剩黑球时：第一次得分或犯规即终局；仅当比分因此打平时才重置黑球
//                     继续（下一杆的得分或犯规同样立即终局）
//   Rule 7(d)(e)      彩球回点：自己的点被占 → 用分值最高的空点 → 再向顶库方向就近；
//                     多颗彩球同时回点时高分球优先
//
// ---------------- 有意从简的实现（就地标注）-----------------
//   - 自由球的"是否被斯诺克"由 GameManager 用几何射线判定（含"两侧都能打到"的近似），
//     不含裁判裁量；同一杆内打进多颗球的组合情形按通用规则归入犯规
//   - 未实现：Miss 累计三次判负（Rule 11(c)(i)）
// =====================================================================================
using System.Collections.Generic;
using UnityEngine;

/// 球 on 的形态
public enum BallOnKind
{
    Red,            // 打红球
    FreeColor,      // 打进红球后，下一杆打任意彩球（需指定）
    SequenceColor,  // 清彩阶段：按分值升序打指定彩球
    FreeBall        // 自由球：犯规后被斯诺克，可指定任意球当作球 on
}

/// 本杆事实（由 GameManager 在结算时填充）
public struct ShotFacts
{
    public bool hitNothing;         // 白球没碰到任何球（含只碰库边）
    public bool cushionContact;     // 白球碰过库边（仅用于区分犯规文案）
    public BallKind firstHit;       // 白球第一颗碰到的球（hitNothing=true 时无意义）
    public BallKind[] potted;       // 本杆落袋的球（含白球 Cue），顺序无关
    public BallKind nominated;      // 球 on 为彩球/自由球时击球方【指定】的球
    public bool nominatedSet;       // 是否真的做了指定（未指定 → 罚分按 4 分下限）
    public bool snookered;          // 出杆时是否被斯诺克（无法直线击中球 on）→ 决定是否判 Miss
}

/// 出杆时刻的台面状态
public struct TableState
{
    public bool colorsPhase;        // 是否已进入升序清彩阶段
    public bool freeColorPending;   // "任意彩球"是否待打（进红后 / 最后一红之后）
    public int redsLeft;            // 台面剩余红球数
    public BallKind[] colorsOnTable;// 台面剩余彩球（只含黄..黑）
    public bool freeBallActive;     // 本杆是否处于自由球状态
}

/// 结算结果 + 新状态（GameManager 据此落地）
public struct ShotOutcome
{
    public int foulPts;                 // 犯规让分（0 = 无犯规）
    public string reason;               // 犯规原因（只记第一条，供 HUD 文案）
    public int legalPts;                // 本杆合法得分（犯规时恒为 0）
    public int scoreDeltaStriker;       // 应加给击球方的分
    public int scoreDeltaOpponent;      // 应加给对手的分
    public bool pottedRed;              // 本杆合法打进红球
    public bool pottedColor;            // 本杆合法打进彩球
    public BallKind pottedColorKind;    // 合法打进的彩球种类（147 连击追踪用）
    public bool viaFreeBall;            // 本杆得分来自自由球（147 连击不计入）
    public int redsPotted;              // 本杆真正离场（不回点）的红球数
    public BallKind[] respotColors;     // 需要回点的彩球（已按分值从高到低排序）
    public BallKind[] respotReds;       // 需要回点的红球（自由球情形，Rule 12）
    public bool handover;               // 是否交换击球权
    public bool nextColorsPhase;        // 结算后的 colorsPhase
    public bool nextFreeColorPending;   // 结算后的 freeColorPending
    public BallKind nextTargetColor;    // 结算后的清彩目标（由台面剩余彩球推导）
    public bool frameOver;              // 本杆是否终局
    public bool respotBlackTie;         // 只剩黑球且打平 → 重置黑球继续
    // ---- v0.35：犯规与未击到 / 自由球 ----
    public bool isMiss;                 // 是否判"犯规与未击到"（未先击中球 on 且未被斯诺克）
    public bool freeBallEarned;         // 接台方是否获得自由球资格（犯规 + 被斯诺克）
}

public static class SnookerRules
{
    // ---------------------------------------------------------------------------------
    // 当前球 on 的形态
    // ---------------------------------------------------------------------------------
    public static BallOnKind BallOn(TableState s)
    {
        if (s.freeBallActive) return BallOnKind.FreeBall;
        if (s.colorsPhase) return BallOnKind.SequenceColor;
        return s.freeColorPending ? BallOnKind.FreeColor : BallOnKind.Red;
    }

    /// 台面上分值最低的彩球 = 清彩阶段当前该打的那颗（由台面实况推导，
    /// 而不是"维护一个会推偏的下标"——这正是 v0.33 清彩卡死的根因）。
    public static BallKind NextColorOn(BallKind[] colorsOnTable)
    {
        if (colorsOnTable != null)
            foreach (var k in G.ColorOrder)
                if (System.Array.IndexOf(colorsOnTable, k) >= 0) return k;
        return BallKind.Black;                  // 理论兜底：台面无彩球
    }

    /// 是否"只剩黑球"（红球清完、任意彩球已打完、台面仅剩黑球）
    public static bool OnlyBlackLeft(TableState s)
    {
        if (s.redsLeft != 0 || s.freeColorPending) return false;
        return s.colorsOnTable != null && s.colorsOnTable.Length == 1 && s.colorsOnTable[0] == BallKind.Black;
    }

    /// 罚分公式（Rule 10）：最低 4 分；否则取"球 on 分值"与"涉及球分值"的较高者。
    static int Penalty(int onVal, int involved) { return Mathf.Max(4, Mathf.Max(onVal, involved)); }

    /// <summary>
    /// 本杆"球 on 的分值"——Rule 10(a) 类犯规（空杆 / 白球落袋）的罚分基数。
    /// 关键点（Rule 3(f)(i)(b)）：球 on 为彩球时必须按【指定】的那颗计分；
    /// 未指定就犯规 → 按最低 4 分计。
    /// 自由球按"真实球 on"的分值计（Rule 12），而非被指定那颗球的分值。
    /// </summary>
    static int BallOnValue(TableState s, BallOnKind on, ShotFacts f)
    {
        switch (on)
        {
            case BallOnKind.Red: return 1;
            case BallOnKind.SequenceColor: return G.Value(NextColorOn(s.colorsOnTable));
            case BallOnKind.FreeColor: return f.nominatedSet ? G.Value(f.nominated) : 4;
            case BallOnKind.FreeBall:
                if (s.redsLeft > 0) return 1;                                   // 真实球 on = 红球
                if (s.colorsPhase) return G.Value(NextColorOn(s.colorsOnTable)); // 真实球 on = 清彩目标
                return f.nominatedSet ? G.Value(f.nominated) : 4;               // 最后一红后的任意彩
        }
        return 4;
    }

    // ---------------------------------------------------------------------------------
    // 单杆结算
    // ---------------------------------------------------------------------------------
    public static ShotOutcome Evaluate(TableState pre, ShotFacts f, int scoreStriker, int scoreOpponent)
    {
        var r = new ShotOutcome();
        var respotColors = new List<BallKind>();
        var respotReds = new List<BallKind>();

        BallOnKind on = BallOn(pre);
        BallKind seqTarget = NextColorOn(pre.colorsOnTable);       // 清彩阶段的真实目标球
        int onVal = BallOnValue(pre, on, f);

        int foulPts = 0;
        string reason = null;
        int legalPts = 0;
        bool pottedRed = false, pottedColor = false;
        BallKind pottedColorKind = BallKind.Black;
        int redsPotted = 0;
        bool missCalled = false;                                   // 未先击中球 on 且未被斯诺克
        bool freeBallPotted = false;                               // 自由球本身被打进（需回点，Rule 12）

        // "击中球 on"的判定基准：
        //   红球阶段→必须先碰红；任意彩球→必须先碰彩球（且等于指定球，未指定则任彩球）；
        //   清彩阶段→必须先碰当前目标球；自由球→必须先碰被指定为球 on 的那颗球
        bool firstHitLegal;
        switch (on)
        {
            case BallOnKind.Red:
                firstHitLegal = !f.hitNothing && f.firstHit == BallKind.Red; break;
            case BallOnKind.FreeColor:
                firstHitLegal = !f.hitNothing && f.firstHit != BallKind.Red
                                && (!f.nominatedSet || f.firstHit == f.nominated); break;
            case BallOnKind.SequenceColor:
                firstHitLegal = !f.hitNothing && f.firstHit == seqTarget; break;
            default: // FreeBall：必须击中指定为球 on 的那颗
                firstHitLegal = !f.hitNothing && (!f.nominatedSet || f.firstHit == f.nominated); break;
        }

        // ---- ① 首触判定 ----
        if (f.hitNothing)
        {
            foulPts = Penalty(onVal, 0);                           // Rule 10(a)(vi)：未击中任何球
            reason = f.cushionContact ? "先碰库边" : "未击中球";
            missCalled = !f.snookered;                             // Rule 11(b)：非斯诺克下的空杆判 Miss
        }
        else if (!firstHitLegal)
        {
            if (on == BallOnKind.FreeColor && f.firstHit == BallKind.Red)
            {
                foulPts = 7;                                       // Rule 10(d)(iv)：连续两杆打红
                reason = "连续两杆打红球";
            }
            else if (on == BallOnKind.Red)
            {
                foulPts = Penalty(onVal, G.Value(f.firstHit));      // Rule 10(b)(iv)：先碰非球 on
                reason = "未先击中红球";
            }
            else if (on == BallOnKind.SequenceColor)
            {
                foulPts = Penalty(onVal, G.Value(f.firstHit));
                reason = "应先击中" + G.CnName(seqTarget);
            }
            else if (on == BallOnKind.FreeColor)
            {
                // 指定了某彩球却先碰到别的彩球
                foulPts = Penalty(onVal, G.Value(f.firstHit));
                reason = "应先击中指定的" + G.CnName(f.nominated);
            }
            else
            {
                foulPts = Penalty(onVal, G.Value(f.firstHit));      // 自由球：未先碰被指定的球
                reason = "应先击中" + G.CnName(f.nominated);
            }
            missCalled = !f.snookered;                             // 先碰错球同样属"未击中球 on"
        }

        // ---- ② 落袋球逐一判定 ----
        if (f.potted != null)
        {
            foreach (var k in f.potted)
            {
                if (k == BallKind.Cue)
                {
                    foulPts = Mathf.Max(foulPts, Penalty(onVal, 0));  // Rule 10(a)(vii)：白球落袋
                    if (reason == null) reason = "白球落袋";
                    continue;
                }

                // ---- 自由球杆：只有"被指定为球 on 的那颗"是合法进球，其余按误落处理（Rule 12）----
                if (on == BallOnKind.FreeBall)
                {
                    bool isNominee = !f.nominatedSet || k == f.nominated;
                    if (isNominee && !freeBallPotted)              // 打进自由球本身
                    {
                        legalPts += onVal;                         // 按【真实球 on 的分值】计分
                        pottedColor = true; pottedColorKind = k;
                        freeBallPotted = true;
                        if (k == BallKind.Red) respotReds.Add(k);  // 红球当自由球被打进也要回点
                        else respotColors.Add(k);
                    }
                    else
                    {
                        foulPts = Mathf.Max(foulPts, Penalty(onVal, G.Value(k)));
                        if (reason == null) reason = "误落" + G.CnName(k);
                        if (k != BallKind.Red) respotColors.Add(k); // 犯规误落的彩球回点
                        else redsPotted++;                          // 误落的红球真的离场
                    }
                    continue;
                }

                // ---- 红球 ----
                if (k == BallKind.Red)
                {
                    redsPotted++;                                     // 红球无论合法与否都不回点（Rule 3(g)）
                    if (on == BallOnKind.Red) { legalPts += 1; pottedRed = true; }
                    else if (on == BallOnKind.FreeColor)
                    {
                        foulPts = Mathf.Max(foulPts, 7);               // Rule 10(d)(iv)：该打彩球却打红
                        if (reason == null) reason = "连续两杆打红球";
                    }
                    else
                    {
                        foulPts = Mathf.Max(foulPts, Penalty(onVal, 1)); // Rule 10(b)(iii)：误落红球
                        if (reason == null) reason = "误落红球";
                    }
                    continue;
                }

                // ---- 彩球 ----
                switch (on)
                {
                    case BallOnKind.Red:
                        foulPts = Mathf.Max(foulPts, Penalty(onVal, G.Value(k)));   // 误落彩球：取高者且≥4
                        if (reason == null) reason = "误落" + G.CnName(k);
                        break;
                    case BallOnKind.FreeColor:
                        legalPts += G.Value(k);                            // 任意彩球：合法得分
                        pottedColor = true; pottedColorKind = k;
                        break;
                    default: // SequenceColor
                        if (k == seqTarget)
                        { legalPts += G.Value(k); pottedColor = true; pottedColorKind = k; }
                        else
                        {
                            foulPts = Mathf.Max(foulPts, Penalty(onVal, G.Value(k)));
                            if (reason == null) reason = "误落" + G.CnName(k);
                        }
                        break;
                }
            }
        }

        bool foul = foulPts > 0;                                       // Rule 11(g)：同杆多犯规取最高
        if (foul) legalPts = 0;                                        // Rule 11(e)：犯规杆一律不计分
        if (foul) { freeBallPotted = false; respotReds.Clear(); }       // 犯规杆：所有球按常规回点处理

        r.foulPts = foulPts;
        r.reason = reason;
        r.legalPts = legalPts;
        r.pottedRed = pottedRed && !foul;
        r.pottedColor = pottedColor && !foul;
        r.pottedColorKind = pottedColorKind;
        r.viaFreeBall = freeBallPotted;
        r.redsPotted = Mathf.Max(0, redsPotted);
        r.isMiss = foul && missCalled;

        // ---- ③ 需要回点的球（Rule 7 / 11(e) / 12）----
        // 规则：红球阶段打进的彩球一律回点；清彩阶段"合法打进的球 on"留在袋里；
        //       犯规杆打进的彩球全部回点；自由球打进的球一律回点（含红球）。红球常规不回点。
        if (!freeBallPotted && f.potted != null)
        {
            foreach (var k in f.potted)
            {
                if (k == BallKind.Cue || k == BallKind.Red) continue;
                bool staysDown = !foul && on == BallOnKind.SequenceColor && k == seqTarget;
                if (!staysDown) respotColors.Add(k);
            }
        }
        // Rule 7(e)：多颗彩球同时回点时高分球优先
        respotColors.Sort((a, b) => G.Value(b).CompareTo(G.Value(a)));
        r.respotColors = respotColors.ToArray();
        r.respotReds = respotReds.ToArray();

        // ---- ④ 分数增量与终局判定（Rule 4）----
        r.scoreDeltaStriker = foul ? 0 : legalPts;
        r.scoreDeltaOpponent = foul ? foulPts : 0;
        int postS = scoreStriker + r.scoreDeltaStriker;
        int postO = scoreOpponent + r.scoreDeltaOpponent;

        bool scoreOrFoul = foul || legalPts > 0;                       // 本杆是否产生"得分或犯规"
        if (OnlyBlackLeft(pre) && scoreOrFoul)
        {
            if (postS == postO) r.respotBlackTie = true;               // 打平 → 重置黑球继续
            else r.frameOver = true;                                   // Rule 4(a)：第一次得分或犯规即终局
        }

        // ---- ⑤ 新状态：球 on 与阶段推进 ----
        int postReds = Mathf.Max(0, pre.redsLeft - redsPotted);
        bool nextColors = pre.colorsPhase;
        bool nextFree = pre.freeColorPending;
        bool scored = !foul && legalPts > 0;

        if (scored)
        {
            switch (on)
            {
                case BallOnKind.Red:
                    nextFree = true; nextColors = false;               // 进红 → 下一杆任意彩球（Rule 3(f)(i)）
                    break;
                case BallOnKind.FreeColor:
                    nextFree = false;                                  // 任意彩球打完
                    if (postReds == 0 && !r.frameOver) nextColors = true;   // 且红球已清完 → 升序清彩（3(f)(ii)）
                    break;
                case BallOnKind.FreeBall:
                    // Rule 12：自由球打完后按真实球 on 继续——
                    //   真实球 on 是红球 → 下一杆打任意彩球（等同于刚进红）
                    //   真实球 on 是清彩目标 → 继续同一颗目标球
                    if (pre.redsLeft > 0) { nextFree = true; nextColors = false; }
                    else { nextFree = pre.freeColorPending; nextColors = pre.colorsPhase; }
                    break;
                default:
                    nextFree = false; nextColors = true;               // 清彩阶段继续下一颗
                    break;
            }
        }
        else
        {
            // 换手：Rule 10.3 —— 只要台面还有红球，接台方永远打红球（v0.33 在这里漏了复位）
            if (postReds > 0) { nextFree = false; nextColors = false; }
            else if (pre.colorsPhase) { nextFree = false; nextColors = true; }   // 清彩阶段：目标球不变
            else { nextFree = true; nextColors = false; }                        // 最后一红后的任意彩球仍待打
        }

        r.handover = !scored;
        r.nextColorsPhase = nextColors;
        r.nextFreeColorPending = nextFree;

        // 新的清彩目标：从"打完本杆后仍在台面的彩球"里取分值最低者
        var postColors = new List<BallKind>();
        if (pre.colorsOnTable != null)
        {
            foreach (var k in pre.colorsOnTable)
            {
                bool left = freeBallPotted && k == pottedColorKind;      // 自由球打进的彩球回点 → 仍在台面
                if (left) { postColors.Add(k); continue; }
                bool staysDown = !foul && on == BallOnKind.SequenceColor && k == seqTarget && k == pottedColorKind;
                if (!staysDown) postColors.Add(k);
            }
        }
        r.nextTargetColor = NextColorOn(postColors.ToArray());

        // ---- ⑥ 自由球资格（Rule 12）----
        // 犯规 → 接台方是否有资格打自由球，取决于"是否对所有球 on 都被斯诺克"。
        // 该判定需要球位几何，由 GameManager.IsSnookered() 用物理信息算出后回填到本字段。
        r.freeBallEarned = false;   // 由 GameManager 在拿到球位后设置

        return r;
    }
}
