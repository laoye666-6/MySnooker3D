// =====================================================================================
// Sfx.cs —— 碰撞/击球音效管理（v0.46）
//
// 三类音效（各 3 个合成变体，Assets/Resources/Audio/ 下，由 tools\make_audio.py 生成）：
//   Cue   杆头击白球"嗒"      —— GameManager.Shoot 出杆瞬间
//   Ball  球碰球"咔"          —— BallController.OnCollisionEnter（撞到球）
//   Cush  球碰库边/袋衬"噗"    —— BallController.OnCollisionEnter（撞到非球）
//
// "不同力度、速度要有区别"的实现（真实台球声学特征）：
//   · 响度随撞击速度非线性增长（幂 <1：中低速已明显可辨，高速不炸）；
//   · 音调随速度略微升高（真实硬碰撞激发的高频振型更多、听感更"亮"）；
//   · 随机挑选变体 + 音调微抖，避免同一波形连放的"机枪感"。
//
// 工程约束（沿用本项目惯例）：
//   · 音频资产走 Resources 预导入（AudioClip），【不做】运行时 AudioClip.Create ——
//     踩坑 25：运行时创建的资源在真机（MuMu GLES3）上表现不可靠，预导入才稳。
//   · 2D 播放（spatialBlend=0）：相机是跟球/全景机位来回飞的，3D 空间衰减会让
//     响度跟着相机跑，与"力度决定响度"的需求冲突。
//   · 节流：开球一帧内十几次碰撞回调，同类音效最小间隔 + 轮转音源池防爆音。
//   · 编辑器离线测试（PhysTest 手动步进）不触发 OnCollisionEnter，且不调用 Init
//     （I==null 直接静默返回），对现有回归零影响。
// =====================================================================================
using System.Collections.Generic;
using UnityEngine;

public class Sfx : MonoBehaviour
{
    public enum Kind { Cue = 0, Ball = 1, Cush = 2 }

    private static Sfx I;                       // 单例；Bootstrapper.Init 创建，离线测试不创建
    private AudioSource[] pool;                 // 轮转音源池（同一帧多响互不打断）
    private int nextVoice;
    private AudioClip[][] variants;             // [3 类][各 ≤3 个变体]

    // ---- 每类音效的速度→响度/音调映射参数（下标 = Kind）----
    // VRef：达到满响度的速度（开球球堆内互撞可到 7m/s，故 Ball 取 7）；
    // VFloor：低于此法向速度不出声（静置贴球/初始化接触抖动不该有声音）；
    // VolPow：<1 的幂让中低速就有明显响度差；Min/MaxVol + KindGain 整体配平
    //（球碰球最脆最响，库边闷响本身听感弱，给最高的相对增益）。
    private static readonly float[] VRef     = { 6.0f, 7.0f, 6.0f };
    private static readonly float[] VFloor   = { 0.00f, 0.12f, 0.15f };
    private static readonly float[] MinVol   = { 0.35f, 0.16f, 0.12f };
    private static readonly float[] MaxVol   = { 0.95f, 1.00f, 0.85f };
    private static readonly float[] VolPow   = { 1.0f, 0.6f, 0.7f };
    private static readonly float[] MinPitch = { 0.95f, 0.92f, 0.93f };
    private static readonly float[] PitchSpan = { 0.12f, 0.18f, 0.12f };
    private static readonly float[] KindGain = { 0.90f, 1.00f, 0.85f };
    // 同类最小播放间隔（秒）：开球瞬间十几颗球同帧互撞，不节流会挤成一片噪声
    private static readonly float[] MinGap   = { 0.05f, 0.025f, 0.035f };
    private static readonly float[] lastPlayAt = { -9f, -9f, -9f };
    private static int playLog;                 // 启动后前 8 次播放打日志（真机验收用，防刷屏）

    /// 由 Bootstrapper 调用：创建音源池并加载 Resources/Audio 下的变体。
    public static void Init(Transform parent)
    {
        if (I != null) return;
        var go = new GameObject("Sfx");
        go.transform.SetParent(parent, false);
        I = go.AddComponent<Sfx>();
        I.Build();
    }

    private void Build()
    {
        variants = new AudioClip[3][];
        variants[(int)Kind.Cue] = LoadSet("sfx_cue");
        variants[(int)Kind.Ball] = LoadSet("sfx_ball");
        variants[(int)Kind.Cush] = LoadSet("sfx_cush");
        pool = new AudioSource[10];
        for (int i = 0; i < pool.Length; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;              // 2D：响度只由撞击速度决定（见文件头）
            pool[i] = src;
        }
        Debug.Log("[SFX] init clips cue=" + Count(variants[0]) +
                  " ball=" + Count(variants[1]) + " cush=" + Count(variants[2]));
    }

    private AudioClip[] LoadSet(string baseName)
    {
        var list = new List<AudioClip>();
        for (int i = 1; i <= 3; i++)
        {
            var c = Resources.Load<AudioClip>("Audio/" + baseName + "_" + i);
            if (c != null) list.Add(c);
        }
        return list.ToArray();
    }

    private static int Count(AudioClip[] s) { return s != null ? s.Length : 0; }

    /// 杆头击白球（speed = 出杆初速 m/s）。GameManager.Shoot 调用。
    public static void Cue(float speed) { Play(Kind.Cue, speed); }

    /// 球-球碰撞（speed = 接触法线方向相对速度 m/s）。BallController 调用。
    public static void Ball(float speed) { Play(Kind.Ball, speed); }

    /// 球-库边/颚部/袋衬碰撞（speed = 接触法线方向相对速度 m/s）。BallController 调用。
    public static void Cush(float speed) { Play(Kind.Cush, speed); }

    private static void Play(Kind k, float speed)
    {
        if (I == null || !GameSettings.SfxOn) return;     // 测试环境/用户关音效：静默
        int ki = (int)k;
        var set = I.variants[ki];
        if (set == null || set.Length == 0) return;
        if (speed < VFloor[ki]) return;                   // 轻触/静置接触不出声
        float now = Time.unscaledTime;
        if (now - lastPlayAt[ki] < MinGap[ki]) return;    // 开球爆堆节流
        lastPlayAt[ki] = now;

        float v01 = Mathf.Clamp01(speed / VRef[ki]);
        float vol = Mathf.Lerp(MinVol[ki], MaxVol[ki], Mathf.Pow(v01, VolPow[ki]))
                  * KindGain[ki] * (0.95f + 0.05f * Random.value);
        // 音调随速度升高（sqrt 曲线：中速已有可感的"变亮"），再叠加 ±2% 微抖防重复感
        float pit = (MinPitch[ki] + PitchSpan[ki] * Mathf.Sqrt(v01)) * (0.98f + 0.04f * Random.value);

        var src = I.pool[I.nextVoice];
        I.nextVoice = (I.nextVoice + 1) % I.pool.Length;
        src.pitch = pit;
        src.PlayOneShot(set[Random.Range(0, set.Length)], vol);
        if (playLog < 8)
        {
            playLog++;
            Debug.Log("[SFX] play " + k + " v=" + speed.ToString("F2") +
                      " vol=" + vol.ToString("F2") + " pitch=" + pit.ToString("F2"));
        }
    }
}
