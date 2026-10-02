# -*- coding: utf-8 -*-
# =====================================================================================
# make_audio.py —— 程序化合成斯诺克碰撞音效（v0.46）
#
# 生成 9 个 44100Hz / 16bit / mono WAV 到 E:\Snooker\assets\audio\：
#   sfx_cue_1..3.wav    杆头击白球"嗒"（皮革杆头 + 酚醛树脂球）
#   sfx_ball_1..3.wav   球碰球"咔"（酚醛树脂球清脆高频 click）
#   sfx_cush_1..3.wav   球碰库边"噗"（包呢库边的低频闷响 + 台呢摩擦噪声）
#
# 合成模型：阻尼正弦叠加 + 短噪声瞬态（one-pole 低通的高斯白噪声）。
# 每类 3 个变体（频率/衰减/混合比例微差），运行时随机挑选 + 音调随速度偏移，
# 避免同一波形反复播放的"机枪感"。
#
# 用法：python tools/make_audio.py
# 然后把 assets\audio\*.wav 拷到 E:\Snooker3D\Assets\Resources\Audio\
#（sync.bat 只同步 *.cs，音频与 shader/manifest 一样需手工拷入工程）。
# =====================================================================================
import math, struct, wave, os, random

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "audio")


def synth(events, dur, attack, seed):
    """events: (freq, tau, gain[, delay]) 阻尼正弦；('noise', tau, gain, delay, lp) 噪声瞬态。
    attack: 起振斜坡时长（秒），防止波形从 0 跳变产生咔哒失真。"""
    rnd = random.Random(seed)
    n = int(dur * SR)
    buf = [0.0] * n
    for ev in events:
        if ev[0] == 'noise':
            _, tau, gain, delay, lp = ev
            m = int(tau * 8 * SR) + 1
            d0 = int(delay * SR)
            a = math.exp(-1.0 / (lp * SR))          # one-pole 低通系数（lp=截止频率）
            prev = 0.0
            for i in range(m):
                idx = d0 + i
                if idx >= n:
                    break
                prev += a * (rnd.gauss(0, 1) - prev)
                env = math.exp(-i / (tau * SR))
                w = min(1.0, i / max(1, int(attack * SR)))
                buf[idx] += gain * env * prev * w
        else:
            f, tau, gain = ev[0], ev[1], ev[2]
            delay = ev[3] if len(ev) > 3 else 0.0
            m = int(min(dur - delay, tau * 9 * SR)) + 1
            d0 = int(delay * SR)
            w0 = 2 * math.pi * f / SR
            ph = rnd.uniform(0, 2 * math.pi)
            for i in range(m):
                idx = d0 + i
                if idx >= n:
                    break
                env = math.exp(-i / (tau * SR))
                w = min(1.0, i / max(1, int(attack * SR)))
                buf[idx] += gain * env * math.sin(w0 * i + ph) * w
    peak = max(abs(x) for x in buf) or 1.0
    g = 0.88 / peak
    fade = int(0.004 * SR)                           # 尾部 4ms 收零，防采样终点截断爆音
    for i in range(n):
        x = buf[i] * g
        if i >= n - fade:
            x *= (n - i) / fade
        buf[i] = x
    return buf


def write_wav(path, buf):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1.0, min(1.0, x)) * 32767)) for x in buf))


def scaled(events, k, seed, noise_lp_k=1.0, gain_jitter=0.0):
    """按频率比例 k 缩放全部正弦分量（噪声低通同比例），增益加一点确定性抖动。"""
    rnd = random.Random(seed)
    out = []
    for ev in events:
        if ev[0] == 'noise':
            out.append(('noise', ev[1], ev[2] * (1.0 + rnd.uniform(-gain_jitter, gain_jitter)),
                        ev[3], ev[4] * noise_lp_k))
        else:
            out.append((ev[0] * k, ev[1] / max(0.6, k ** 0.5),
                        ev[2] * (1.0 + rnd.uniform(-gain_jitter, gain_jitter))) +
                       (ev[3:] if len(ev) > 3 else ()))
    return out


CUE = [      # 杆头击球：低频"皮头+球杆"闷响 + 中频敲击 + 短噪声瞬态
    (170.0, 0.014, 0.50),
    (900.0, 0.006, 0.55),
    (1750.0, 0.0035, 0.35),
    ('noise', 0.0008, 0.50, 0.0, 5000.0),
]
BALL = [     # 球碰球：酚醛树脂高频 click，三段频率快速衰减
    (3200.0, 0.008, 0.50),
    (5600.0, 0.005, 0.40),
    (8400.0, 0.0028, 0.28),
    ('noise', 0.0004, 0.45, 0.0, 8000.0),
]
CUSH = [     # 球碰库：包呢库边低频闷响，起振略缓（橡胶+呢面吸能）
    (185.0, 0.032, 0.60),
    (390.0, 0.014, 0.35),
    (620.0, 0.008, 0.15),
    ('noise', 0.0025, 0.30, 0.0, 1600.0),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    jobs = [
        ("sfx_cue",  CUE,  0.10, 0.0004,  [0.92, 1.00, 1.09], [4200, 5000, 6200]),
        ("sfx_ball", BALL, 0.09, 0.00015, [0.90, 1.00, 1.12], [7000, 8000, 9500]),
        ("sfx_cush", CUSH, 0.18, 0.0012,  [0.90, 1.00, 1.10], [1400, 1600, 1900]),
    ]
    for name, base, dur, atk, scales, lps in jobs:
        for i, k in enumerate(scales):
            evs = scaled(base, k, seed=100 + sum(map(ord, name)) + i,
                         noise_lp_k=lps[i] / (base[-1][4] * k))
            buf = synth(evs, dur, atk, seed=7 + i)
            p = os.path.join(OUT, "%s_%d.wav" % (name, i + 1))
            write_wav(p, buf)
            print("%s  %.3fs  %d bytes" % (os.path.abspath(p), len(buf) / SR, os.path.getsize(p)))


if __name__ == "__main__":
    main()
