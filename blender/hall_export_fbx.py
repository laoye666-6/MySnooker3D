# -*- coding: utf-8 -*-
# hall_export_fbx.py —— v0.52 从（已抬灯的）pool_hall.blend 无头导出 FBX（剔主桌）
# 单独成脚本：先 view_layer.update() 刷新 depsgraph，避免 matrix_world 缓存旧值
# （hall_lift_export.py 首跑的教训：location 已改、导出仍用旧矩阵）。
# 用法: blender -b pool_hall.blend --python hall_export_fbx.py
import bpy

OUT = r"E:\Snooker\assets\hall\pool_hall.fbx"

# ---- 0. 刷新求值（关键：location 改动后 matrix_world 需 depsgraph 重算） ----
bpy.context.view_layer.update()

def wminmax(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    zs = [p.z for p in pts]
    return min(zs), max(zs)

# ---- 1. 审计灯体世界高度（应：罩 1.54..1.80 / 发光板 1.60..1.612 / 杆 1.80..2.68） ----
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name.startswith(("Shade_Out_0", "Shade_Panel_0", "Rod_0_0")):
        z0, z1 = wminmax(o)
        print("[FBX] audit %-14s zspan=(%.3f..%.3f)" % (o.name, z0, z1))

# ---- 2. 选中除主桌子树外的【每一个对象】（不只根对象） ----
# 教训：上面首版只选根对象，object_types={'MESH'} 下导出器不遍历"空物体父级"——
# 绿椅挂在 PROP_GreenChair_* 空父级下、Poly Haven 植物叶片挂在 Plant_NW/Plant_E 空父级下，
# 结果这 10 件被静默跳过（blend 99 → fbx 89，绿椅全丢）。改为逐对象判定。
def in_main_table(o):
    p = o
    while p is not None:
        if p.name.startswith("Table_Root") and abs(p.location.x) < 0.01:
            return True
        p = p.parent
    return False

for o in bpy.context.scene.objects:
    o.select_set(False)
picked = skipped = 0
for o in bpy.context.scene.objects:
    if in_main_table(o):
        skipped += 1
        continue
    o.select_set(True)
    picked += 1
print("[FBX] objects picked=%d skipped(main table)=%d of %d" %
      (picked, skipped, len(bpy.context.scene.objects)))

# ---- 3. 导出（v0.50 管线：内嵌贴图、只 MESH） ----
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'},
                         path_mode='ABSOLUTE', embed_textures=True,
                         add_leaf_bones=False, bake_anim=False,
                         mesh_smooth_type='FACE')
print("[FBX] export ok ->", OUT)
