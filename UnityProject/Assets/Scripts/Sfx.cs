// =====================================================================================
// Sfx.cs —— 音效管理（v1.0 起只保留胜利结算号角）
//
// 历史：v0.46 曾提供三类碰撞音效（杆头击球"嗒" / 球碰球"咔" / 球碰库"噗"，各 3 个合成变体），
//       v1.0 按需求**全部删除** —— 运行时不再有击球、碰撞、落袋相关的任何声音，
//       Assets/Resources/Audio 下对应的 9 个 WAV 也已移除（APK 同步瘦身）。
// 现仅保留 v0.51 的胜利结算号角 sfx_win_1（单条旋律，Sfx.Win()），
// 仍受设置面板「音效」开关（GameSettings.SfxOn）控制。
//
// 工程约束（沿用本项目惯例）：
//   · 音频资产走 Resources 预导入（AudioClip），【不做】运行时 AudioClip.Create ——
//     踩坑 25：运行时创建的资源在真机（MuMu GLES3）上表现不可靠，预导入才稳。
//   · 2D 播放（spatialBlend=0）：相机是跟球/全景机位来回飞的，3D 衰减会让响度跟着相机跑。
//   · 编辑器离线测试（PhysTest 手动步进）不调用 Init（I==null 直接静默返回），对回归零影响。
// =====================================================================================
using UnityEngine;

public class Sfx : MonoBehaviour
{
    private static Sfx I;                       // 单例；Bootstrapper.Init 创建，离线测试不创建
    private AudioSource[] pool;                 // 轮转音源池（号角只播一次，2 路足够）
    private int nextVoice;
    private AudioClip winClip;                  // v0.51：胜利结算号角（单条合成小旋律）

    /// 由 Bootstrapper 调用：创建音源池并加载 Resources/Audio 下的胜利号角。
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
        winClip = Resources.Load<AudioClip>("Audio/sfx_win_1");   // 缺失只降级（静音），不报错
        pool = new AudioSource[2];
        for (int i = 0; i < pool.Length; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;              // 2D
            pool[i] = src;
        }
        Debug.Log("[SFX] init win=" + (winClip != null ? 1 : 0) +
                  " (v1.0: 击球/碰撞音效已删除)");
    }

    /// <summary>
    /// v0.51：胜利结算号角（UIManager.ShowGameOver(true) 时播一次）。
    /// 单条完整旋律（三音上行 + 高八度泛音，tools\make_audio.py 合成），
    /// 不走速度映射；同样受 GameSettings.SfxOn 开关与"测试环境 I==null 静默"约束。
    /// </summary>
    public static void Win()
    {
        if (I == null || !GameSettings.SfxOn || I.winClip == null) return;
        var src = I.pool[I.nextVoice];
        I.nextVoice = (I.nextVoice + 1) % I.pool.Length;
        src.pitch = 1f;                          // 旋律固定音高，不做抖动偏移
        src.PlayOneShot(I.winClip, 0.85f);
    }
}
