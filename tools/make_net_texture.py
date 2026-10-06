# -*- coding: utf-8 -*-
# make_net_texture.py —— 蜂窝网兜贴图（v0.55，按袋口实拍：白色绳网 + 深色网眼）
# 生成 512x512 可平铺六边形网格 PNG → assets\net_texture.png
# （Unity 侧预导入 Resources/Textures/，踩坑 4：预导入贴图才可靠；不做运行时生成）
# 用法: python tools/make_net_texture.py
import os, math

try:
    from PIL import Image, ImageDraw
except ImportError:
    raise SystemExit("需要 Pillow: pip install pillow")

W = H = 512
CELL = 64          # 六边形外接圆直径（px）
LINE = 7           # 绳网线条宽（px）

im = Image.new("RGB", (W, H), (232, 232, 228))     # 绳网白
d = ImageDraw.Draw(im)
r = CELL / 2.0
h = r * math.sqrt(3) / 2.0                         # 六边形中心到边中点的距离

def hexagon(cx, cy, rr):
    return [(cx + rr * math.cos(math.radians(60 * k - 30)),
             cy + rr * math.sin(math.radians(60 * k - 30))) for k in range(6)]

# 六边形网格（pointy-top 偏移），越界画两圈保证平铺无缝
for row in range(-1, int(H / (1.5 * r)) + 3):
    for col in range(-1, int(W / (2 * h)) + 3):
        cx = col * 2 * h
        cy = row * 1.5 * r + (0 if col % 2 == 0 else 0.75 * r)
        d.line(hexagon(cx, cy, r - LINE / 2.0) + [hexagon(cx, cy, r - LINE / 2.0)[0]],
               fill=(18, 18, 18), width=LINE, joint="curve")

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "net_texture.png")
im.save(out)
print("SAVED", os.path.abspath(out), im.size)
