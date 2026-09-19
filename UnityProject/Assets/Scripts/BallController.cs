// =====================================================================================
// BallController.cs —— 单颗球的运行时行为组件
//
// 挂在每颗球（SphereCollider + Rigidbody）上，负责三件事：
//   1. 落袋动画（Pot / Sink）与"重置回台面"（Place，用于开球、犯规后白球重置、彩球回点）
//   2. 台呢/地面的滚动摩擦（FixedUpdate 中人为施加恒定减速度，PhysX 不自带滚动阻力）
//   3. 把碰撞事件上报给 GameManager（用于"首触犯规"判定）
//
// 注意：PhysX 刚体休眠后直接赋值 rb.velocity 不会让球动起来，
//       所以 Place() 和 GameManager.Shoot() 里都调用了 rb.WakeUp()。
// =====================================================================================
using System.Collections;
using UnityEngine;

public class BallController : MonoBehaviour
{
    /// 这颗球的种类（白/红/六种彩），GameManager 据此做规则判定与计分。
    public BallKind kind;

    /// 是否已落袋。true 时：不再参与移动判定、不再被袋口捕获、被其他球的碰撞检测忽略。
    public bool potted;

    // 本球的刚体引用（Awake/Init 时缓存，避免每次访问 GetComponent）。
    private Rigidbody rb;

    /// 对外只读暴露刚体（GameManager 出杆时 WakeUp + 赋速度要用）。
    public Rigidbody Rb { get { return rb; } }

    // ---------------------------------------------------------------------------------
    // 初始化：缓存刚体引用。
    // Awake 是 Unity 生命周期自动调用；显式暴露 Init() 是为了编辑器离线测试
    // （executeMethod 模式下 AddComponent 不触发 Awake，由 PhysTest 手动调 Init）。
    // ---------------------------------------------------------------------------------
    void Awake() { Init(); }

    public void Init()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// 这颗球是否还在运动（GameManager 用它判断"一杆是否结束"）。
    /// v0.30：只看线速度（> G.StopSpeed = 0.09 m/s 即在动）。
    /// 旧版还检查角速度 >1.1 rad/s——但 angularDrag 仅 0.08，强旋转的球要转几十秒才衰减，
    /// 线速度为零的原地旋转球会拖住整杆结算几十秒，是"换手等待久"的隐藏元凶之一。
    /// </summary>
    public bool IsMoving()
    {
        if (potted) return false;
        Vector3 v = rb.velocity; v.y = 0f;          // 只看水平滚动速度
        return v.sqrMagnitude > G.StopSpeed * G.StopSpeed;
    }

    /// <summary>
    /// 落袋：由 GameManager.CheckPockets 在球心进入袋口捕获半径时调用。
    /// 处理流程：
    ///   1. 关闭碰撞（球要穿过台呢的视觉孔洞往下掉，不能被台面碰撞体接住）
    ///   2. 把速度改成"竖直下坠"：水平速度乘 0.2 保留一点点惯性观感，
    ///      竖直方向至少 -0.5 m/s 保证一定往下掉（Mathf.Min 取更负的那个）
    ///   3. 启动 Sink 协程，掉到桌底以下后整球隐藏
    /// 重复调用是安全的（potted 标志防重入）。
    /// </summary>
    public void Pot()
    {
        if (potted) return;
        potted = true;
        rb.detectCollisions = false;                                     // 不再与任何碰撞体交互
        rb.velocity = new Vector3(rb.velocity.x * 0.2f,                  // 水平惯性衰减到 20%
                                  Mathf.Min(rb.velocity.y, -0.5f),       // 竖直至少 -0.5 m/s 下坠
                                  rb.velocity.z * 0.2f);
        rb.angularVelocity *= 0.2f;                                      // 旋转同样衰减
        StartCoroutine(Sink());
    }

    /// <summary>
    /// 落袋下沉协程：逐帧等待，直到球心 y 低于 -0.5m（台面下方）后隐藏球体。
    /// 隐藏即视为"进袋收纳"，游戏结束时也不再显示。
    /// </summary>
    IEnumerator Sink()
    {
        while (transform.position.y > -0.5f) yield return null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 把球放到指定位置（开球布阵 / 白球犯规重置 / 彩球回置球点都走这里）。
    /// 参数 pos：期望的球心落点（XZ 平面坐标有效，y 会被覆盖为台面高度）。
    /// 内部细节：
    ///   - 实际放置高度 = pos.y + BallR + 0.002，即比台呢面高 2mm，让球自然落下贴台
    ///   - 同时写 transform.position 与 rb.position（两者都写可避免物理不同步）
    ///   - 必须调 rb.WakeUp()：休眠刚体被传送后不会自动醒来，否则球会悬空冻结
    ///   - Place 只清零速度不唤醒会白醒，所以最后统一 WakeUp
    /// </summary>
    public void Place(Vector3 pos)
    {
        // v0.34：先停掉可能仍在运行的 Sink 协程。袋口井底已与台面平齐（v0.30），球不会再掉到
        // -0.5m 以下，Sink 的等待循环会永远空转；一颗球被反复回点就会累积一堆空协程（旧版每次回点漏一个）。
        StopAllCoroutines();
        potted = false;                                  // 复活：清除落袋标志
        rb.detectCollisions = true;                      // 恢复碰撞
        rb.velocity = Vector3.zero;                      // 清空旧速度
        rb.angularVelocity = Vector3.zero;
        transform.position = pos + Vector3.up * (G.BallR + 0.002f);  // 抬高 2mm 放置
        rb.position = transform.position;                // 刚体位置同步（防插值回跳）
        rb.WakeUp();                                     // 唤醒刚体（休眠体不会自动积分）
        gameObject.SetActive(true);                      // 若之前落袋被隐藏则重新显示
    }

    // ---------------------------------------------------------------------------------
    // 碰撞上报：白球每次发生碰撞都通知 GameManager。
    // GameManager.NotifyContact 只关心两件事：
    //   1. 白球本次出杆"第一个碰到的球"是哪种 → 首触犯规判定（必须先碰目标球）
    //   2. 白球是否碰过库边（区分"啥都没碰"与"只碰库没碰球"两种犯规文案）
    // 红球/彩球的碰撞也会上报，但 GameManager 只处理 self 为白球的情况。
    // ---------------------------------------------------------------------------------
    void OnCollisionEnter(Collision c)
    {
        if (GameManager.I != null) GameManager.I.NotifyContact(this, c.collider);
    }

    // ---------------------------------------------------------------------------------
    // 滚动摩擦：PhysX 的库仑摩擦只对"滑动"有效，纯滚动的球理论上永不减速，
    // 所以这里人为施加真实台呢的滚动阻力——每个物理步按恒定减速度 RollDecel(0.11 m/s²)
    // 反着水平速度方向削一刀。
    //
    // 生效条件（满足其一）：
    //   - 球心高度 ≤ BallR + 4mm：球贴着台呢滚（抬高 4mm 容差防止浮点抖动导致摩擦时有时无）
    //   - 球心高度 < -0.5m：球已掉到桌子底下的地板上（防出界球在地板上滑行不止）
    //
    // 实现：只削水平分量（v.y 保留给重力），速度足够小就直接归零，避免无限逼近永不停止。
    // 参数 G.RollDecel 调大 → 台面更"涩"，长球拉不动；调小 → 球滚得久，接近玻璃球手感。
    // ---------------------------------------------------------------------------------
    void FixedUpdate()
    {
        if (!potted && (transform.position.y <= G.BallR + 0.004f || transform.position.y < -0.5f))
        {
            Vector3 v = rb.velocity;
            v.y = 0;                                     // 只考虑水平滚动
            float sp = v.magnitude;
            if (sp > 0.0005f)                            // 有水平速度才需要衰减
            {
                float drop = G.RollDecel * Time.fixedDeltaTime;  // 本步应减掉的速度量
                if (drop >= sp) v = Vector3.zero;        // 本步就能减完 → 直接停
                else v -= v.normalized * drop;           // 否则沿反方向削一刀
                rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
            }
        }
    }
}
