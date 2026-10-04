// =====================================================================================
// UIConfetti.cs —— 胜利结算彩纸层（v0.51 新增）
//
// 零贴图顶点网格（与 UIGlass 同一条真机安全路径，踩坑 4）：一个 MaskableGraphic，
// OnPopulateMesh 里为每片彩纸生成一个旋转四边形（4 顶点 2 三角形），整层一个 draw call。
// 颜色走顶点色；材质用 UIManager 统一下发的 UI/Default（白纹理 × 顶点色，无纹理采样）。
//
// 动画模型：Launch(burst) 起爆 → burst 秒内按速率补发新片；每片带初速下落、
// 受重力加速到终端速度 + 水平正弦摆动 + 自旋，落出屏幕底部即消亡。
// 全部消亡且补发结束后不再每帧重建网格（静止期零开销）。
//
// 坐标系：RectTransform 全屏拉伸，局部单位 = 画布设计像素（CanvasScaler match 0.5
// 与 UIManager.K() 同一套换算），原点在屏幕中心、y 向上。边界从 rectTransform.rect
// 现场读取（20:9 / 4:3 等非 16:9 画布下出屏判定同样正确）。
// =====================================================================================
using UnityEngine;
using UnityEngine.UI;

public class UIConfetti : MaskableGraphic
{
    struct Piece
    {
        public bool alive;
        public float x0, y;              // 水平锚点与当前高度（局部坐标）
        public float vy;                 // 下落速度（px/s，重力加速到终端）
        public float phase, freq, amp;   // 水平正弦摆动
        public float rot, rotVel;        // 自旋（弧度 / rad·s⁻¹）
        public float w, h;               // 纸片长宽
        public Color col;
    }

    private const int MaxPieces = 72;    // 峰值同时在场片数（72×2 三角形 = 288 tri，一个 draw call）
    private const float SpawnRate = 45f; // 补发速率（片/秒）
    private const float Gravity = 640f;  // px/s²
    private const float TermVel = 860f;  // 终端下落速度（px/s）

    private Piece[] pieces;
    private float burstLeft;             // >0 还在补发新片
    private float spawnAcc;
    private float clock;                 // 本轮动画时钟（摆动相位用）
    private int aliveCount;

    /// iOS 系统色系（与 UIManager 的玻璃配色同一语言，白片压场）。
    private static readonly Color[] Palette =
    {
        new Color(0.25f, 0.62f, 1.00f, 1f),   // 蓝
        new Color(0.22f, 0.72f, 0.42f, 1f),   // 绿
        new Color(1.00f, 0.58f, 0.00f, 1f),   // 橙
        new Color(1.00f, 0.45f, 0.70f, 1f),   // 粉
        new Color(0.68f, 0.50f, 1.00f, 1f),   // 紫
        new Color(1.00f, 0.80f, 0.25f, 1f),   // 金黄
        new Color(1.00f, 1.00f, 1.00f, 1f),   // 白
    };

    /// 起爆：burst 秒的补发期。已在播时忽略（防结算动画重入）。
    public void Launch(float burst)
    {
        if (pieces == null) pieces = new Piece[MaxPieces];
        if (burstLeft > 0f) return;
        burstLeft = burst;
        clock = 0f;
    }

    /// 清场（重开一局 / 结算面板关闭时）。
    public void ResetState()
    {
        burstLeft = 0f;
        spawnAcc = 0f;
        if (pieces != null)
            for (int i = 0; i < pieces.Length; i++) pieces[i].alive = false;
        aliveCount = 0;
        SetVerticesDirty();
    }

    void Update()
    {
        if (burstLeft <= 0f && aliveCount == 0) return;  // 静止期零开销
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        clock += dt;
        Rect r = rectTransform.rect;

        // 补发：从屏幕上缘外随机横坐标撒新片
        if (burstLeft > 0f)
        {
            burstLeft -= dt;
            spawnAcc += dt * SpawnRate;
            while (spawnAcc >= 1f && aliveCount < MaxPieces)
            {
                spawnAcc -= 1f;
                Spawn(r);
            }
        }

        // 推进：下落（重力→终端速度）+ 自旋；落出底部即消亡
        int alive = 0;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!pieces[i].alive) continue;
            var p = pieces[i];
            p.vy = Mathf.Min(p.vy + Gravity * dt, TermVel);
            p.y -= p.vy * dt;
            p.rot += p.rotVel * dt;
            if (p.y < -r.height * 0.5f - 80f) p.alive = false;
            else alive++;
            pieces[i] = p;
        }
        aliveCount = alive;
        SetVerticesDirty();                              // 只有走到这说明有活动片
    }

    void Spawn(Rect r)
    {
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].alive) continue;
            var p = new Piece();
            p.alive = true;
            p.x0 = Random.Range(-r.width * 0.5f - 40f, r.width * 0.5f + 40f);
            p.y = r.height * 0.5f + Random.Range(30f, 320f);   // 错峰入场
            p.vy = Random.Range(120f, 360f);
            p.phase = Random.Range(0f, Mathf.PI * 2f);
            p.freq = Random.Range(1.6f, 3.2f);
            p.amp = Random.Range(24f, 90f);
            p.rot = Random.Range(0f, Mathf.PI * 2f);
            p.rotVel = Random.Range(-5.2f, 5.2f);
            p.w = Random.Range(11f, 22f);
            p.h = p.w * Random.Range(0.5f, 1.0f);              // 长条纸屑
            p.col = Palette[Random.Range(0, Palette.Length)];
            pieces[i] = p;
            aliveCount++;
            return;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (pieces == null) return;
        var vert = UIVertex.simpleVert;
        vert.uv0 = Vector4.zero;                         // UI/Default 采白纹理，uv 无意义
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!pieces[i].alive) continue;
            var p = pieces[i];
            float x = p.x0 + Mathf.Sin(clock * p.freq + p.phase) * p.amp;
            float c = Mathf.Cos(p.rot), s = Mathf.Sin(p.rot);
            float hw = p.w * 0.5f, hh = p.h * 0.5f;
            // 旋转纸片的四角（局部中心系）：a→b→d→e 逆时针
            Vector2 a = new Vector2( hw * c + hh * s, -hw * s + hh * c);
            Vector2 b = new Vector2(-hw * c + hh * s,  hw * s + hh * c);
            Vector2 d = new Vector2(-hw * c - hh * s,  hw * s - hh * c);
            Vector2 e = new Vector2( hw * c - hh * s, -hw * s - hh * c);
            vert.color = p.col;
            int v0 = vh.currentVertCount;
            vert.position = new Vector3(x + a.x, p.y + a.y); vh.AddVert(vert);
            vert.position = new Vector3(x + b.x, p.y + b.y); vh.AddVert(vert);
            vert.position = new Vector3(x + d.x, p.y + d.y); vh.AddVert(vert);
            vert.position = new Vector3(x + e.x, p.y + e.y); vh.AddVert(vert);
            vh.AddTriangle(v0, v0 + 1, v0 + 2);
            vh.AddTriangle(v0, v0 + 2, v0 + 3);
        }
    }
}
