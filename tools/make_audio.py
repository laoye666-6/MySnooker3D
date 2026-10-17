# -*- coding: utf-8 -*-
# =====================================================================================
# make_audio.py —— 程序化合成音效（v1.0：只剩胜利结算号角）
#
# v1.0 起，v0.46 的击球/球碰球/球碰库三类碰撞音效已从游戏中删除，
# 本脚本只生成 1 个 WAV 到 E:\Snooker\assets\audio\：
#   sfx_win_1.wav   胜利结算号角（v0.51：C 大调 do-mi-so-do 上行琶音 + 高八度泛音）
#
# 三音上行 + 高八度泛音；无噪声分量（与曾经的碰撞音效区分开）。
# 合成模型：阻尼正弦叠加。
#
# 用法：python tools/make_audio.py
# 然后把 assets\audio\sfx_win_1.wav 拷到 E:\Snooker3D\Assets\Resources\Audio\
#（sync.bat 只同步 *.cs，音频与 shader/manifest 一样需手工拷入工程）。
#
# 历史：v0.46~v0.57 的 sfx_cue/ball/cush_1..3.wav（各 3 变体）已随碰撞音效一并移除；
#       原始合成代码见 git 历史（v0.57 及更早的 README 版本记录）。
# =====================================================================================
import math, struct, wave, os, random

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "audio")


def synth(events, dur, attack, seed):
    """events: (freq, tau, gain[, delay]) 阻尼正弦。
    attack: 起振斜坡时长（秒），防止波形从 0 跳变产生咔哒失真。"""
    rnd = random.Random(seed)
    n = int(dur * SR)
    buf = [0.0] * n
    for ev in events:
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


# v0.51 胜利号角：C 大调上行大三和弦琶音（C5→E5→G5→C6，间隔 110ms），
# 每音叠一个高八度短泛音提亮；末音 τ 拉长收尾。
WIN = [
    (523.25, 0.30, 0.42, 0.000),
    (659.25, 0.30, 0.38, 0.110),
    (783.99, 0.30, 0.38, 0.220),
    (1046.50, 0.42, 0.40, 0.330),
    (1046.50, 0.14, 0.10, 0.000),
    (1318.51, 0.14, 0.09, 0.110),
    (1567.98, 0.14, 0.09, 0.220),
    (2093.00, 0.18, 0.10, 0.330),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    buf = synth(WIN, 0.85, 0.002, seed=77)
    p = os.path.join(OUT, "sfx_win_1.wav")
    write_wav(p, buf)
    print("%s  %.3fs  %d bytes" % (os.path.abspath(p), len(buf) / SR, os.path.getsize(p)))


if __name__ == "__main__":
    main()
