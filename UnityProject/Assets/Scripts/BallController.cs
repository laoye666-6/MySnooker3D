// =====================================================================================
// BallController.cs —— 单颗球的运行时行为组件
//
// 挂在每颗球（SphereCollider + Rigidbody）上，负责三件事：
//   1. 落袋动画（Pot / Sink）与"重置回台面"（Place，用于开球、犯规后白球重置、彩球回点）
//   2. 台呢/地面的滚动摩擦（FixedUpdate 中人为施加恒定减速度，PhysX 不自带滚动阻力）
//   3. 把碰撞事件上报给 GameManager（用于"首触犯规"判定）
//
// v0.36 新增：白球自旋（加塞）物理。
//   PhysX 只有刚体 + 库仑摩擦，无法表达台球的自旋（球-台呢接触是"滚+滑"而非滑动摩擦），
//   因此白球改走专用模型：把角速度存成世界坐标向量 spin，每个物理步算接触点滑移速度
//   u = v + ω × (-r·ŷ)，用滑动摩擦同时修正【线速度】与【角速度】，两者自然收敛到纯滚动。
//   由此："高杆"（正向自旋）在撞球后推着白球继续前冲、"低杆"（反向自旋）把白球拉回来、
//   侧塞在撞库时被库边"抓住"把球横甩出去 —— 全部是模型自然涌现的结果，不是写死的动画。
//   其余 21 颗球仍完全交给 PhysX/滚动阻力（它们没有人施加旋转），行为与 v0.35 一致。
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

    /// v0.36：白球自旋（世界坐标角速度，rad/s）。只有白球会非零，其余球恒为 0。
    /// 分量含义：竖直分量 ω_y = 左右塞；水平分量 = 高低杆（相对当前滚动方向的"附加自旋"）。
    private Vector3 spin;

    /// 自旋的可读副本（调试/HUD 用）。
    public Vector3 Spin { get { return spin; } }

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
        spin = Vector3.zero;                                             // v0.36：落袋即清自旋
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
        spin = Vector3.zero;                             // v0.36：重置自旋（回点/布球后不该带走旧旋转）
        rb.detectCollisions = true;                      // 恢复碰撞
        rb.velocity = Vector3.zero;                      // 清空旧速度
        rb.angularVelocity = Vector3.zero;
        transform.position = pos + Vector3.up * (G.BallR + 0.002f);  // 抬高 2mm 放置
        rb.position = transform.position;                // 刚体位置同步（防插值回跳）
        rb.WakeUp();                                     // 唤醒刚体（休眠体不会自动积分）
        gameObject.SetActive(true);                      // 若之前落袋被隐藏则重新显示
    }

    /// <summary>
    /// v0.36："球在手"拖动摆放（只在开球区 D 内使用）。
    /// 与 Place 的区别：保留球当前高度、不停协程、不做"复活"处理——
    /// 因为拖动时球本来就在台面上正常参与物理，只是被手指挪了个位置。
    /// 每帧调用的开销必须小，所以这里只清速度+同步位置+唤醒。
    /// </summary>
    public void MoveTo(Vector3 xz)
    {
        Vector3 p = new Vector3(xz.x, transform.position.y, xz.z);
        transform.position = p;
        rb.position = p;                                 // 两份都写：防插值回跳（与 Place 同理）
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        spin = Vector3.zero;
        rb.WakeUp();
    }

    /// <summary>
    /// v0.36 出杆时施加加塞：把"击球点偏移"换算成白球的初始角速度。
    /// 参数（均由 CueController 的加塞圆盘给出）：
    ///   dir   出杆方向（单位向量，XZ 平面）
    ///   speed 白球初速（m/s）
    ///   spinV 高低杆：+1 = 高杆（跟杆/上旋），-1 = 低杆（缩杆/下旋），0 = 中杆
    ///   spinH 左右塞：-1 = 左塞，+1 = 右塞（圆盘上的左右），0 = 无塞
    ///
    /// 角速度按滚动基准换算（baseW = v / r）：
    ///   高低杆 = (1 + spinV·K) × baseW，沿 (up × dir) 轴 —— spinV=0 时正好是纯滚动，
    ///   所以"中杆"与旧版行为完全一致（这是不回归的关键）。
    ///   高杆用 SpinTopK、低杆用 SpinLowK（见 G.cs 的说明：低杆侧必须更强，
    ///   否则玩家拖到"低杆"却只得到无旋滑行）。
    ///   左右塞 = +spinH·SpinSideK × baseW，沿世界 +Y 轴。符号由力偶推导得出：
    ///   俯视看击球点在中心右侧（右塞）时，杆头对白球的力矩 τ = r × F 指向 +Y
    ///   （r 指向球心右侧、F 沿出杆方向），故右塞 spinH=+1 → ω_y > 0。
    /// </summary>
    public void ApplySpin(Vector3 dir, float speed, float spinV, float spinH)
    {
        if (kind != BallKind.Cue) return;
        Vector3 d = new Vector3(dir.x, 0f, dir.z);
        if (d.sqrMagnitude < 1e-8f) return;
        d.Normalize();
        float baseW = speed / G.BallR;
        float v01 = Mathf.Clamp(spinV, -1f, 1f);
        float k = v01 >= 0f ? G.SpinTopK : G.SpinLowK;    // 低杆侧更强（G.cs 有说明）
        Vector3 rollAxis = Vector3.Cross(Vector3.up, d);          // = up × dir（单位向量）
        spin = rollAxis * (baseW * (1f + v01 * k))
             + Vector3.up * (Mathf.Clamp(spinH, -1f, 1f) * G.SpinSideK * baseW);
        rb.angularVelocity = spin;                               // 立刻同步，避免首帧视觉错位
        Debug.Log(string.Format("[SNOOKER] SPIN v={0:F2} h={1:F2} |w|={2:F1} wy={3:F1}",
            spinV, spinH, spin.magnitude, spin.y));
    }

    /// 当前左右塞强度（rad/s，正 = 左塞方向的自旋）。
    public float SideSpin { get { return spin.y; } }

    /// <summary>
    /// 清空自旋与刚体速度（结算时统一刹停用）。
    /// 必须同时清 spin 字段——只清 rb.angularVelocity 的话，下一个物理步
    /// CueRollStep 会把 spin 重新写回刚体，球会继续原地旋转。
    /// </summary>
    public void ClearSpin()
    {
        spin = Vector3.zero;
        if (rb != null) rb.angularVelocity = Vector3.zero;
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
        CushionSpin(c);                                  // v0.36：侧塞撞库的切向踢出
    }

    // ---------------------------------------------------------------------------------
    // v0.36：侧塞撞库效果。
    // 侧塞让接触点的球面带着切向速度压向库边，库边摩擦"抓住"它 → 球被横甩出去
    // （真实台球里"塞吃库后往一边跑"的观感）。推导（r_c 从球心指向接触点）：
    //   接触点球面速度 v_c = ω × r_c = ω × (-n·R) = -R(ω × n)   （n = 库边指向台内的法线）
    //   库边摩擦阻止切向滑动 → 球获得与 v_c 反向的冲量：
    //     Δv = k·R·ω_y·(ŷ × n)
    // 注意法线方向：不用 Collision.contacts[].normal（其正负号在 Unity 各版本/平台
    // 上有方向约定歧义），改用 relativeVelocity 反推——白球撞库前的相对速度方向
    // 就是"指向库边"的方向，取反即台内法线，符号永远正确。
    // 该效果在侧塞为 0 时完全不触发，因此不影响 v0.35 已验收的撞库物理。
    // ---------------------------------------------------------------------------------
    void CushionSpin(Collision c)
    {
        if (kind != BallKind.Cue || potted) return;
        float side = spin.y;
        if (Mathf.Abs(side) < 1f) return;                // 侧塞很弱时不折腾
        if (c.collider != null && c.collider.GetComponentInParent<BallController>() != null)
            return;                                      // 撞的是球：不做库边处理
        Vector3 kick = CushionKick(c.relativeVelocity, side);
        if (kick.sqrMagnitude < 1e-8f) return;
        rb.velocity += kick;
        spin -= Vector3.up * (side * G.CushionSpinLoss); // 库边摩擦消耗一部分侧旋
    }

    /// <summary>
    /// 侧塞撞库的横向踢出量（纯函数，便于离线断言，见 PhysTest.SpinTest）。
    /// inbound：白球撞库前的速度（方向指向库边）；side：当前侧塞 ω_y（rad/s）。
    /// 返回 Δv：垂直于入射方向的横向速度增量，符号由 ω_y 与库边朝向共同决定。
    /// </summary>
    public static Vector3 CushionKick(Vector3 inbound, float side)
    {
        Vector3 d = new Vector3(inbound.x, 0f, inbound.z);
        if (d.sqrMagnitude < 1e-6f) return Vector3.zero;
        Vector3 n = -d.normalized;                                   // 库边指向台内的法线
        return Vector3.Cross(Vector3.up, n) * (side * G.BallR * G.CushionSpinGrab);
    }

    // ---------------------------------------------------------------------------------
    // 摩擦：PhysX 的库仑摩擦只对"滑动"有效，纯滚动的球理论上永不减速，
    // 所以这里人为施加真实台呢的滚动阻力——每个物理步按恒定减速度 RollDecel(0.11 m/s²)
    // 反着水平速度方向削一刀。
    //
    // 生效条件（满足其一）：
    //   - 球心高度 ≤ BallR + 4mm：球贴着台呢滚（抬高 4mm 容差防止浮点抖动导致摩擦时有时无）
    //   - 球心高度 < -0.5m：球已掉到桌子底下的地板上（防出界球在地板上滑行不止）
    //
    // v0.36：白球改走 CueRollStep（带自旋的滑动摩擦模型），其余球仍是原来的纯滚动阻力。
    // ---------------------------------------------------------------------------------
    void FixedUpdate()
    {
        if (potted) return;
        if (transform.position.y > G.BallR + 0.004f && transform.position.y >= -0.5f) return;
        StepOnCloth(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 台呢接触时的摩擦/自旋推进。抽成公开方法是给编辑器离线测试用的：
    /// PhysTest 用 Physics.Simulate 手动步进时【不会触发 MonoBehaviour.FixedUpdate】
    /// （见 PhysTest 文件头的说明），所以测试里必须自己按物理步长调这个函数。
    /// </summary>
    public void StepOnCloth(float dt)
    {
        if (potted) return;
        if (kind == BallKind.Cue) CueRollStep(dt);
        else RollStep(dt);
    }

    /// 普通球（红/彩）：恒定滚动减速度，行为与 v0.35 完全一致。
    void RollStep(float dt)
    {
        Vector3 v = rb.velocity;
        v.y = 0;                                     // 只考虑水平滚动
        float sp = v.magnitude;
        if (sp > 0.0005f)                            // 有水平速度才需要衰减
        {
            float drop = G.RollDecel * dt;           // 本步应减掉的速度量
            if (drop >= sp) v = Vector3.zero;        // 本步就能减完 → 直接停
            else v -= v.normalized * drop;           // 否则沿反方向削一刀
            rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
        }
    }

    /// <summary>
    /// v0.36 白球专用：滑动摩擦 + 自旋耦合。
    ///
    /// 物理模型（标准台球模型，球视为均匀球 I = 0.4·m·r²）：
    ///   ① 接触点滑移速度 u = v + ω × r_c，r_c = (0,-r,0)
    ///   ② u ≠ 0（打滑）：摩擦力沿 -û 作用于接触点 →
    ///        线速度 v   ← v + (-û)·a·dt          （a = SlipDecel）
    ///        角速度 ω   ← ω + (2.5·a/r)·(ŷ × û)·dt （由 τ = r_c × F 推出）
    ///   ③ u ≈ 0（纯滚动）：只施加滚动阻力 RollDecel，角速度锁定为"滚动角速度 + 残余侧塞"
    ///
    /// 该模型的自然结果：
    ///   · 低杆（ω 与滚动方向相反）→ 打滑期线速度被反向摩擦拖住，撞球后残余反旋把白球拉回；
    ///   · 高杆（ω 超过滚动）→ 打滑期摩擦推着球加速，撞球后残余正旋推着白球前冲；
    ///   · 侧塞 → ω 竖直分量在接触点【不产生滑移】（球绕竖直轴自转，接触点速度恒为 0），
    ///     所以侧塞不会让球在台呢上走弧线，只在撞库时起作用（见 CushionSpin）——
    ///     这与真实台球一致（台呢上的侧塞靠"吃库"体现，不是靠弯曲轨迹）。
    /// 打滑期的长度 ≈ 0.22·v₀² 米（v₀ = 出杆初速）：轻杆几厘米内就转成纯滚动（低杆在远处无效），
    /// 重杆可带旋走完整张台面 —— 这正是真实斯诺克"低杆要发足力"的原因。
    /// 中杆（spinV=spinH=0）时初速即为纯滚动，u≈0，与旧版手感逐帧一致。
    /// </summary>
    void CueRollStep(float dt)
    {
        Vector3 v = rb.velocity;
        v.y = 0f;
        Vector3 rc = new Vector3(0f, -G.BallR, 0f);
        Vector3 u = v + Vector3.Cross(spin, rc);              // 接触点滑移速度
        float uMag = u.magnitude;

        if (uMag > G.SlipThreshold)
        {
            // ---- 打滑：滑动摩擦同时改线速度与角速度 ----
            Vector3 uHat = u / uMag;
            v -= uHat * (G.SlipDecel * dt);
            spin += Vector3.Cross(Vector3.up, uHat) * (2.5f * G.SlipDecel * dt / G.BallR);
        }
        else
        {
            // ---- 纯滚动：角速度 = 滚动角速度（保留竖直分量=侧塞）----
            float side = spin.y;
            float sp = v.magnitude;
            if (sp > 0.0005f)
            {
                float drop = G.RollDecel * dt;
                v = drop >= sp ? Vector3.zero : v - v.normalized * drop;
            }
            else v = Vector3.zero;
            spin = Vector3.Cross(Vector3.up, v) / G.BallR + Vector3.up * side;
        }

        // 侧塞的台呢旋转阻尼：竖直轴自旋在接触点没有滑移（球像陀螺一样原地转），
        // 滑动摩擦模型抓不到它，必须单独衰减，否则侧塞会一直留到下一次出杆。
        spin -= Vector3.up * (spin.y * Mathf.Clamp01(G.SpinSideDecay * dt));

        if (v.sqrMagnitude < 1e-8f && spin.sqrMagnitude < 0.0025f)
        {
            v = Vector3.zero;
            spin = Vector3.zero;                              // 彻底停稳，避免永不衰减的残余抖动
        }

        rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
        rb.angularVelocity = spin;                            // 覆盖 PhysX 积分，保证视觉滚动与模型一致
    }
}
