# -*- coding: utf-8 -*-
# =====================================================================================
# make_surface_maps.py —— 程序化生成三张"写实化"贴图（v0.56）
#
#   1. cloth_normal.png  1024²  台呢织纹法线贴图（切线空间，1m×1m 无缝平铺）
#   2. wood_normal.png   1024²  木纹法线贴图（顺纹细棱 + 年轮微起伏，1m×1m 无缝平铺）
#   3. ball_shadow.png    256²  贴地假阴影（径向渐变黑斑，PCOL 同款接触阴影）
#
# 用法：python E:\Snooker\tools\make_surface_maps.py
# 然后把 assets\*.png 拷到工程 Assets\Resources\Textures\：
#   cloth_normal.png / wood_normal.png / ball_shadow.png
# （sync.bat 只同步 *.cs，贴图与 shader/音频一样需手工拷入工程。）
#
# 设计参照（PCOL 逆向结论）：
#   · 它的台呢有 256² 高频织纹法线 ×30 平铺（pcolCarpet shader 的 chaotic normal），
#     灯下呢面才有"绒面"的细密凹凸，而不是一块平塑料；
#   · 球影是 2.6×球径的径向渐变面片（alpha 0.8），补足接触阴影。
# =====================================================================================
import math, os, struct, random, zlib

ASSET_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")
SIZE = 1024


def write_png(path, w, h, rgb_rows):
    """最小化 PNG 写出（8bit RGB，无滤波）：只依赖 zlib，不引入第三方库。"""
    raw = b''.join(b'\x00' + bytes(row) for row in rgb_rows)

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(raw, 9))
    png += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(png)
    print("%s  %dx%d  %d bytes" % (os.path.abspath(path), w, h, os.path.getsize(path)))


def write_png_rgba(path, w, h, rgba_rows):
    raw = b''.join(b'\x00' + bytes(row) for row in rgba_rows)

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(raw, 9))
    png += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(png)
    print("%s  %dx%d  %d bytes" % (os.path.abspath(path), w, h, os.path.getsize(path)))


def value_noise(seed, cells):
    """平铺 value noise：cells×cells 整数格点 + 双三次平滑插值，返回 SIZE² 高度场 [0,1]。"""
    rnd = random.Random(seed)
    grid = [[rnd.random() for _ in range(cells)] for _ in range(cells)]

    def smooth(t):
        return t * t * (3 - 2 * t)

    def at(x, y):
        x0, y0 = int(math.floor(x)) % cells, int(math.floor(y)) % cells
        x1, y1 = (x0 + 1) % cells, (y0 + 1) % cells
        fx, fy = smooth(x - math.floor(x)), smooth(y - math.floor(y))
        a = grid[y0][x0] * (1 - fx) + grid[y0][x1] * fx
        b = grid[y1][x0] * (1 - fx) + grid[y1][x1] * fx
        return a * (1 - fy) + b * fy

    step = SIZE / cells
    field = [[0.0] * SIZE for _ in range(SIZE)]
    for j in range(SIZE):
        y = j / step
        for i in range(SIZE):
            field[j][i] = at(i / step, y)
    return field


def height_to_normal(field, strength):
    """高度场 → 切线空间法线 RGB（有限差分；中值 128 灰紫）。"""
    rows = []
    for j in range(SIZE):
        row = [0] * (SIZE * 3)
        jm, jp = (j - 1) % SIZE, (j + 1) % SIZE
        for i in range(SIZE):
            im, ip = (i - 1) % SIZE, (i + 1) % SIZE
            dx = (field[j][ip] - field[j][im]) * strength
            dy = (field[jp][i] - field[jm][i]) * strength
            nx, ny, nz = -dx, -dy, 1.0
            inv = 1.0 / math.sqrt(nx * nx + ny * ny + nz * nz)
            row[i * 3 + 0] = max(0, min(255, int((nx * inv * 0.5 + 0.5) * 255)))
            row[i * 3 + 1] = max(0, min(255, int((ny * inv * 0.5 + 0.5) * 255)))
            row[i * 3 + 2] = max(0, min(255, int((nz * inv * 0.5 + 0.5) * 255)))
        rows.append(row)
    return rows


def make_cloth_normal():
    """台呢绒面法线（v0.57 重写）：【纯无序绒毛噪声，零网格】。
    旧版用经纬正弦乘积模拟织纹——正弦乘积天然产生"亮点阵列"，灯下读作用户反馈的
    方块状纹路。真实台呢绒面（拉绒后的 128g 绒呢）在近看是无序的纤维丛：
    高频细绒（~2.5px）+ 中频起伏 + 低频云斑三层叠乘即可。"""
    fuzz = value_noise(4102, 420)       # 细绒（~2.4px，= 2.4mm 纤维丛）
    fuzz2 = value_noise(7717, 180)      # 中频起伏（~5.7mm 绒簇）
    fuzz3 = value_noise(3313, 60)       # 低频云斑（~17mm 深浅斑）
    field = [[0.0] * SIZE for _ in range(SIZE)]
    for j in range(SIZE):
        for i in range(SIZE):
            field[j][i] = fuzz[j][i] * 0.52 + fuzz2[j][i] * 0.30 + fuzz3[j][i] * 0.18
    write_png(os.path.join(ASSET_DIR, "cloth_normal.png"), SIZE, SIZE,
              height_to_normal(field, strength=5.0))


def make_wood_normal():
    """木纹法线：顺 X 的细长木纤维棱（低频顺纹起伏 + 高频细丝），幅度比台呢大——
    桌框侧面的木棱在灯下要有可触的立体感。"""
    fiber = value_noise(911, 8)         # 大尺度顺纹走向（X 拉长：采样时 x/8）
    grain = value_noise(233, 160)       # 中频年轮扰动
    fine = value_noise(5150, 700)       # 细木丝
    field = [[0.0] * SIZE for _ in range(SIZE)]
    for j in range(SIZE):
        for i in range(SIZE):
            # 顺纹：X 方向低频拉伸（大格点噪声按 1/427 采样 → 沿 X 几乎不变），
            # Y 方向叠加年轮状正弦（被中频噪声扰动）+ 高频细木丝
            long_ = fiber[int(j / 227)][int(i / 427)]
            ridge = 0.5 + 0.5 * math.sin(2 * math.pi * (i / 22.0 + grain[j][i] * 1.8))
            field[j][i] = long_ * 0.35 + ridge * 0.35 + fine[j][i] * 0.30
    write_png(os.path.join(ASSET_DIR, "wood_normal.png"), SIZE, SIZE,
              height_to_normal(field, strength=26.0))


def make_ball_shadow():
    """贴地假阴影：径向渐变黑斑。中心 alpha ~0.9 → 边缘 0，
    中心 60% 半径内较实（接触阴影），外围快速淡出（半影）。"""
    n = 256
    rows = []
    for j in range(n):
        row = [0] * (n * 4)
        for i in range(n):
            dx = (i + 0.5) / n * 2 - 1
            dy = (j + 0.5) / n * 2 - 1
            r = math.sqrt(dx * dx + dy * dy)
            if r >= 1.0:
                a = 0.0
            else:
                # 平方缓出：0.55 半径内 ≈0.9 实心，向外 4 次幂衰减
                t = min(1.0, max(0.0, (r - 0.35) / 0.65))
                a = 0.9 * (1.0 - t) ** 2.2
            row[i * 4 + 0] = 0
            row[i * 4 + 1] = 0
            row[i * 4 + 2] = 0
            row[i * 4 + 3] = int(max(0.0, min(1.0, a)) * 255)
        rows.append(row)
    write_png_rgba(os.path.join(ASSET_DIR, "ball_shadow.png"), n, n, rows)


if __name__ == "__main__":
    os.makedirs(ASSET_DIR, exist_ok=True)
    make_cloth_normal()
    make_wood_normal()
    make_ball_shadow()
    print("[SURFACE MAPS] ALL DONE")
