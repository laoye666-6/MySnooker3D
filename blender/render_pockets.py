# render_pockets.py —— 袋口特写渲染（v0.41 用于校验弧形颚部）
# 用法: blender.exe -b -P render_pockets.py
# 输出: E:\Snooker\shots\pocketrender_*.png （正交俯视特写）
import bpy, os, math
from mathutils import Vector

ASSET = r"E:\Snooker\assets"
OUT = r"E:\Snooker\shots"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=os.path.join(ASSET, "table.obj"))

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
try:
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True
    sc.display.shading.show_object_outline = True
except Exception as e:
    print("shading setup:", e)

def cam(name, loc, target, ortho):
    d = bpy.data.cameras.new(name)
    d.type = 'ORTHO'
    d.ortho_scale = ortho
    o = bpy.data.objects.new(name, d)
    o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(o)
    return o

def shot(path, o, px=1100):
    sc.camera = o
    sc.render.resolution_x = px
    sc.render.resolution_y = px
    # 正交俯视时用 1:1 像素，细节最清晰
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path)

# Blender 是 Z-up；OBJ 里的 x/y 对应 Unity 的 x/z。
# 角袋（+x,+y 象限）：中心 (1.7925, 0.897)
corner = cam("CamCorner", (1.7925, 0.897, 3.0), (1.7925, 0.897, 0.0), 0.42)
shot(os.path.join(OUT, "pocketrender_corner.png"), corner)

# 中袋（0, 0.907）
mid = cam("CamMid", (0.0, 0.907, 3.0), (0.0, 0.907, 0.0), 0.42)
shot(os.path.join(OUT, "pocketrender_middle.png"), mid)

# 斜视看颚部三维形状
obl = cam("CamOblique", (1.55, 0.55, 0.32), (1.775, 0.875, 0.0), 0.34)
shot(os.path.join(OUT, "pocketrender_oblique.png"), obl)
