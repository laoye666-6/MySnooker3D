# -*- coding: utf-8 -*-
# render_pocket_diag.py —— v0.53 袋口库边诊断渲染（定位"库边下部未渲染"）
# 基于 render_pockets.py 的可用机位写法（不额外旋转；OBJ 导入即 Z-up，与游戏一致）。
# WORKBENCH + MATERIAL + cavity 能凸显法线朝向异常/缝隙。
# 用法: blender -b --factory-startup --python render_pocket_diag.py
import bpy, os
from mathutils import Vector

ASSET = r"E:\Snooker\assets"
OUT = r"E:\Snooker\shots"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=os.path.join(ASSET, "table.obj"))
print("[DIAG] mesh objects:", len([o for o in bpy.data.objects if o.type == 'MESH']))

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
try:
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True
    sc.display.shading.show_object_outline = True
except Exception as e:
    print("shading:", e)

def cam(name, loc, target, ortho):
    d = bpy.data.cameras.new(name)
    d.type = 'ORTHO'; d.ortho_scale = ortho
    o = bpy.data.objects.new(name, d)
    o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(o)
    return o

def shot(path, o, px=1100):
    sc.camera = o
    sc.render.resolution_x = px; sc.render.resolution_y = px
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path)

# 低位斜视：几乎贴着台呢（z≈0.05）从台内看向袋口 —— 正是游戏跟球相机的高度。
mid_low   = cam("MidLow",   (0.0, 0.62, 0.06),  (0.0, 0.92, 0.015), 0.30)
cor_low   = cam("CorLow",   (1.55, 0.62, 0.06), (1.7925, 0.897, 0.015), 0.30)
# 与 render_pockets.py 同款斜视（可对照）
cor_obl   = cam("CorObl",   (1.55, 0.55, 0.32), (1.775, 0.875, 0.0), 0.34)
# 俯视整体（确认没有其他缺面）
top       = cam("Top",      (0.0, 0.0, 3.0),    (0.0, 0.0, 0.0), 4.2)

shot(os.path.join(OUT, "diag_mid_low.png"),  mid_low)
shot(os.path.join(OUT, "diag_corner_low.png"), cor_low)
shot(os.path.join(OUT, "diag_corner_oblique.png"), cor_obl)
shot(os.path.join(OUT, "diag_top.png"), top, 1400)
