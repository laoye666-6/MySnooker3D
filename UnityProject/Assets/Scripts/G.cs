// =====================================================================================
// G.cs —— 全局常量与公共工具（双人斯诺克 3D）
//
// 本文件集中定义整局游戏用到的所有"物理尺寸 / 调参 / 颜色 / 工具函数"。
// 坐标约定（全工程统一）：
//   - 以米为单位（1 Unity 单位 = 1 米），台呢面高度 y = 0
//   - 球桌长边沿 X 轴：开球区（baulk 端）在 -X，黑球置球点在 +X
//   - 球桌短边沿 Z 轴，上下两条长库分别在 z = ±HalfW
//   - 所有常量都可在 Blender 建模脚本 E:\Snooker\blender\make_table.py 中找到对应值，
//     两边必须保持一致，否则视觉模型与物理碰撞体会错位
// =====================================================================================
using UnityEngine;

// 球的种类（枚举顺序有含义：Bootstrapper 建 22 颗球时用数值区间区分 15 颗红球）
// Cue=0 白球 | Red=1 红球 | Yellow=2 黄球 | Green=3 绿球 | Brown=4 咖啡球
// Blue=5 蓝球 | Pink=6 粉球 | Black=7 黑球
public enum BallKind { Cue, Red, Yellow, Green, Brown, Blue, Pink, Black }

public static class G
{
    // ------------------------------------------------------------------
    // 一、球桌几何尺寸（单位：米，标准比赛桌按 12ft×6ft 折算）
    // ------------------------------------------------------------------

    /// 库边"鼻端"到桌中心的一半长度 = 3.569 / 2。
    /// 即两条短库内表面所在平面为 x = ±1.7845，是球能到达的极限位置。
    public const float HalfL = 1.7845f;

    /// 库边"鼻端"到桌中心的一半宽度 = 1.778 / 2。
    /// 两条长库内表面所在平面为 z = ±0.889。
    public const float HalfW = 0.889f;

    /// 斯诺克球半径 = 52.5mm 直径 / 2。
    /// 决定：球的 localScale（直径）、袋口捕获判定、置球间距等，改动需同步 Blender 脚本。
    public const float BallR = 0.02625f;

    /// 库边碰撞体顶部高度（台呢面以上 34mm）。
    /// 真实库边鼻端接触球心上方，此值必须 > BallR(0.026) 才能压住球不跳飞；
    /// 同时 < 球直径，防止球"骑"上库顶。视觉库边略高于此值（斜面到 0.040）。
    public const float CushTop = 0.034f;

    /// 库边厚度（从鼻端向外的进深，55mm）。
    /// 物理库边碰撞体的截面宽度和视觉库边进深都由此决定。
    public const float CushD = 0.055f;

    /// 角袋开口：库边末端距桌角的让空距离（72mm）。
    /// 值越小袋口越"窄"，进球越难；调大则角袋更宽松。
    public const float CornGap = 0.072f;

    /// 中袋开口的半宽（56mm，全开口 112mm）。
    /// 中袋在长边正中，两侧库边各让开 CenGap。
    public const float CenGap = 0.056f;

    /// 袋口"颚部"斜切深度：库边端面向袋内收的斜切量（50mm），
    /// 用于把贴库球往袋口里导向，同时防止球卡在库边直角上。
    public const float JawDx = 0.050f;

    /// 角袋袋口中心相对台面角点的外偏移（8mm）。
    /// 袋口视觉圆孔与捕获判定圆心的 X/Z 偏移量。
    public const float CornerOff = 0.008f;

    /// 中袋袋口中心相对库边线的外偏移（18mm）。
    public const float CenterOff = 0.018f;

    /// 开球线（baulk line）的 X 坐标 = -(1.7845 - 737mm)。
    /// 距开球端库边 29 英寸（737mm），棕/绿/黄球与开球白球都布置在这条线上。
    public const float BaulkX = -1.0475f;

    /// 开球区"D"的半径（292mm），开球线中点往开球端画半圆。
    public const float DR = 0.292f;

    // ------------------------------------------------------------------
    // 二、物理调参（手感核心，改动后建议用 PhysTest.Run 在编辑器回归）
    // ------------------------------------------------------------------

    /// 台呢滚动摩擦减速度（m/s²）。模拟真实台呢的滚动阻力，
    /// 由 BallController.FixedUpdate 每个物理步对水平速度做恒定衰减。
    /// 调大 → 球停得更快、长台更难打；标准手感约 0.10~0.15。
    public const float RollDecel = 0.11f;

    /// 判定"球已停"的速度阈值（m/s）。v0.30：0.035 → 0.09。
    /// GameManager 每帧检查所有球，速度低于此值即视为静止（0.09 m/s 的爬行肉眼几乎不可见）。
    /// 调大 → 一杆结束得更早（换手等待更短）；调太小会拖长等待时间。
    public const float StopSpeed = 0.09f;

    /// 最小出杆初速（m/s），对应力度滑条 0%。
    /// 保证再轻的球也能推动母球。低于此值手感会"粘滞"。
    public const float MinShotSpeed = 0.55f;

    /// 最大出杆初速（m/s），对应力度滑条 100%。
    /// v0.32：4.6 → 6.0，满力开球更有爆发力（约职业 smash break 水准）。
    public const float MaxShotSpeed = 6.0f;

    /// 角袋捕获半径（米）：球心与角袋捕获圆心距离小于此值即判落袋。
    /// 必须略大于台呢视觉孔洞半径（Blender 里 0.055），否则球会"悬"在袋口。
    /// 调大 → 角袋更容易进；调小则袋口红球可能挂袋不掉。
    public const float CornerCaptureR = 0.070f;

    /// 中袋捕获半径（米），同上，对应中袋视觉孔 0.062。
    public const float CenterCaptureR = 0.066f;

    // ------------------------------------------------------------------
    // 二b、加塞（杆法）物理参数 —— v0.36 新增
    //
    // 真实台球的自旋无法用 PhysX 表达（刚体+库仑摩擦只会把自旋抹平），
    // 因此白球的自旋由 BallController 里的"滑移摩擦"模型自己算：
    //   接触点滑移速度 u = v + ω × (-r·ŷ)，摩擦力沿 -û 作用于球心（改变线速度），
    //   同时以 τ = r_c × F 反过来改变角速度 → 自旋与滚动互相收敛，
    //   于是"跟杆推着球往前、低杆把球拉回来"全部自然涌现。
    // 系数含义与手感调法：
    //   SpinTopK   击球点拉到最高/最低时，附加角速度 = K × (v/r)。
    //              K=1.5 时最大低杆约等于"线速度 0.5 倍的反旋"，职业级拉杆手感。
    //   SpinSideK  最大左右塞的角速度倍数（0.7 ≈ 强塞，再大会让库边角度过于夸张）。
    //   SlipDecel  打滑时的滑动摩擦减速度（m/s²），真实球-呢约 0.2g ≈ 1.96。
    //              它同时决定自旋衰减速度（dω/dt = 2.5·a/r），调大 → 杆法效果消失更快。
    //   SpinSideDecay 侧塞在台呢上的旋转阻尼（1/s，指数衰减）。侧塞不该像高低杆那样
    //              被滑移摩擦吃掉（竖直轴自旋在接触点无滑移），故单独给一个慢衰减。
    //   CushionSpinGrab/Loss 撞库时侧塞"抓"住库边把球横甩出去的强度与每次的侧旋损失。
    // ------------------------------------------------------------------

    /// 高低杆强度：击球点偏移量 |spinV| ≤ 1，附加角速度 = spinV × SpinTopK × (v / BallR)。
    /// 注意高杆与低杆用【不同】系数：中杆(spinV=0)被钉在"自然滚动"(ω = v/r)上，
    /// 若两侧同用一个系数，低杆侧要拖到 spinV < -0.67 才真正出现反旋，
    /// 中间一大段"略低杆"其实是无旋滑行 —— 玩家拖到"低杆"却看不到缩杆效果。
    /// 因此低杆侧用更大的 SpinLowK，使反旋从 spinV ≈ -0.4 就开始出现。
    public const float SpinTopK = 1.5f;

    /// 低杆强度（仅 spinV < 0 时使用，见上面的说明）。
    /// spinV=-1 → ω = -1.6×(v/r)：真机实测（同力度 4.46m/s 直球，撞堆后白球落点）
    ///   中杆 -1.17m / spinV=-0.36 → -0.51m / spinV=-0.85 → 白球被拉回并自己撞进顶袋。
    /// 2.5 时满杆回缩过于剧烈（白球常常自己追进袋），故收到 1.6 —— 保留"拖到一半就有
    /// 明显缩杆"，但满杆不再失控。
    public const float SpinLowK = 1.6f;

    /// 左右塞强度：ω 竖直分量 = -spinH × SpinSideK × (v / BallR)（见 BallController 符号说明）。
    public const float SpinSideK = 0.7f;

    /// 球在台呢上打滑时的滑动摩擦减速度（m/s²）= 0.2g。
    public const float SlipDecel = 1.96f;

    /// 接触点滑移速度低于此值（m/s）即视为纯滚动，改按 RollDecel 处理。
    public const float SlipThreshold = 0.012f;

    /// 侧塞的台呢旋转阻尼（1/s），指数衰减：每秒保留 e^-0.45 ≈ 64%。
    public const float SpinSideDecay = 0.45f;

    /// 撞库时侧塞的切向踢出系数：Δv = Grab × ω_y × r（Grab=1 表示完全抓死）。
    public const float CushionSpinGrab = 0.45f;

    /// 每次撞库损失的侧旋比例（库边摩擦吸收）。
    public const float CushionSpinLoss = 0.35f;

    // ------------------------------------------------------------------
    // 三、袋口捕获圆心（世界坐标，y=0 平面）
    // 前 4 个为角袋（右上/右下/左上/左下），后 2 个为中袋（上/下）。
    // 顺序被 GameManager.CheckPockets 依赖：i<4 用 CornerCaptureR，否则用 CenterCaptureR。
    // ------------------------------------------------------------------
    public static readonly Vector3[] Pockets =
    {
        new Vector3( HalfL + CornerOff, 0,  HalfW + CornerOff),   // 右上角袋
        new Vector3( HalfL + CornerOff, 0, -(HalfW + CornerOff)), // 右下角袋
        new Vector3(-(HalfL + CornerOff), 0,  HalfW + CornerOff), // 左上角袋
        new Vector3(-(HalfL + CornerOff), 0, -(HalfW + CornerOff)),// 左下角袋
        new Vector3(0, 0,  HalfW + CenterOff),                    // 上中袋
        new Vector3(0, 0, -(HalfW + CenterOff)),                  // 下中袋
    };

    // ------------------------------------------------------------------
    // 四、六颗彩球置球点（世界坐标，y=0）
    // 布局符合标准斯诺克：棕球在开球线中点，绿/黄在 D 弧两端，蓝在台心，
    // 粉在两中点之间（台长 1/4 处），黑球距顶库 324mm。
    // ------------------------------------------------------------------
    public static readonly Vector3 BrownSpot = new Vector3(BaulkX, 0, 0);          // 咖啡球点（开球线中点）
    public static readonly Vector3 GreenSpot = new Vector3(BaulkX, 0,  DR);        // 绿球点（开球线 +Z 端）
    public static readonly Vector3 YellowSpot = new Vector3(BaulkX, 0, -DR);       // 黄球点（开球线 -Z 端）
    public static readonly Vector3 BlueSpot = new Vector3(0, 0, 0);                // 蓝球点（台面正中）
    public static readonly Vector3 PinkSpot = new Vector3(HalfL / 2f, 0, 0);       // 粉球点（台长 1/4 处）
    public static readonly Vector3 BlackSpot = new Vector3(HalfL - 0.324f, 0, 0);  // 黑球点（距顶库 324mm）

    /// 清彩阶段的固定顺序（红球打完后必须按分值从低到高清彩）。
    /// GameManager 用 Array.IndexOf 在此数组里推进 targetColor。
    public static readonly BallKind[] ColorOrder =
        { BallKind.Yellow, BallKind.Green, BallKind.Brown, BallKind.Blue, BallKind.Pink, BallKind.Black };

    // ------------------------------------------------------------------
    // 五、主要材质颜色（0~1 的 RGB）
    // ------------------------------------------------------------------
    public static readonly Color ClothCol = new Color(0.10f, 0.47f, 0.22f);     // 台呢：比赛绿
    public static readonly Color CushionCol = new Color(0.08f, 0.42f, 0.20f);   // 库边：比台呢略深的绿
    public static readonly Color WoodCol = new Color(0.45f, 0.26f, 0.13f);      // 桌框/桌腿：红棕木色

    // ------------------------------------------------------------------
    // 六、工具函数
    // ------------------------------------------------------------------

    /// <summary>
    /// 把一个点夹进开球区 D（v0.36）：球心不得越过开球线（x ≤ BaulkX），
    /// 且到棕球点（D 圆心）的距离不超过 D 半径。开球布球与"球在手"摆白球共用。
    /// </summary>
    public static Vector3 ClampToD(Vector3 p)
    {
        float dx = p.x - BaulkX;                     // D 圆心的 x 就是开球线
        float dz = p.z;
        if (dx > 0f) dx = 0f;                        // D 在开球线的开球端一侧，越线即夹回
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d > DR) { float s = DR / d; dx *= s; dz *= s; }   // 圆外 → 投影到圆弧上
        return new Vector3(BaulkX + dx, 0f, dz);
    }

    /// 某个点是否落在 D 区内（球心约束，同上）。
    public static bool InD(Vector3 p)
    {
        float dx = p.x - BaulkX, dz = p.z;
        if (dx > 0f) return false;
        return dx * dx + dz * dz <= DR * DR;
    }

    /// 返回某种球的分值（斯诺克计分规则）。
    /// 白球返回 0（白球落袋走犯规逻辑，不走分值逻辑）。
    public static int Value(BallKind k)
    {
        switch (k)
        {
            case BallKind.Red: return 1;
            case BallKind.Yellow: return 2;
            case BallKind.Green: return 3;
            case BallKind.Brown: return 4;
            case BallKind.Blue: return 5;
            case BallKind.Pink: return 6;
            case BallKind.Black: return 7;
            default: return 0;
        }
    }

    /// 返回球的中文名（用于 HUD 消息，例如"应先击中蓝球""误落粉球"）。
    public static string CnName(BallKind k)
    {
        switch (k)
        {
            case BallKind.Cue: return "白球";
            case BallKind.Red: return "红球";
            case BallKind.Yellow: return "黄球";
            case BallKind.Green: return "绿球";
            case BallKind.Brown: return "咖啡球";
            case BallKind.Blue: return "蓝球";
            case BallKind.Pink: return "粉球";
            case BallKind.Black: return "黑球";
        }
        return "?";
    }

    /// 返回球的模型颜色（Bootstrapper 建球时给 Standard 材质上色用）。
    /// RGBA 的 A 恒为 1（不透明）。
    public static Color BallColor(BallKind k)
    {
        switch (k)
        {
            case BallKind.Cue: return new Color(0.93f, 0.93f, 0.88f);   // 白球：米白
            case BallKind.Red: return new Color(0.72f, 0.06f, 0.05f);   // 红球：深红
            case BallKind.Yellow: return new Color(0.96f, 0.78f, 0.05f);// 黄球
            case BallKind.Green: return new Color(0.05f, 0.52f, 0.15f); // 绿球
            case BallKind.Brown: return new Color(0.45f, 0.22f, 0.07f); // 咖啡球
            case BallKind.Blue: return new Color(0.08f, 0.26f, 0.85f);  // 蓝球
            case BallKind.Pink: return new Color(0.95f, 0.45f, 0.62f);  // 粉球
            case BallKind.Black: return new Color(0.03f, 0.03f, 0.03f); // 黑球
        }
        return Color.white;
    }
}
