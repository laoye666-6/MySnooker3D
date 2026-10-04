# -*- coding: utf-8 -*-
# hall_lift_export.py —— v0.52 抬高台球厅三盏吊灯 + 无头导出 FBX（剔主桌）
#
# 背景：跟球机位高 1.18m，灯罩下沿 0.85（v0.50 在 Unity 侧 +0.30 → 1.15）依旧侵入镜头。
# 本轮直接在 Blender 权威源文件里抬高（模型即最终位置，Unity 不再打偏移补丁）：
#   - 罩体（Shade_Out/In/Panel_*，x=0/±7.2 三盏）整体 z += LIFT → 下沿 0.85→1.60
#   - 吊杆（Rod_*）以天花端 z=2.68 为锚按比例缩短，底端 1.05→1.80 贴住新罩顶
#   - COL_Lights 里位于灯罩下的面光同步 z += LIFT（保持 blend 预览光位一致）
# 然后保存 blend（贴图已打包自包含），并按 v0.50 管线导出 FBX：
#   use_selection、剔主桌（根对象名 Table_Root* 且位于 x≈0）、embed_textures、只 MESH。
#
# 用法: blender -b pool_hall.blend --python hall_lift_export.py
import bpy

LIFT = 0.75                     # 罩体抬升量（米）
CEIL = 2.68                     # 天花高度（杆顶锚点）
ROD_TOP_OLD = 2.680             # 吊杆原顶端
ROD_BOT_OLD = 1.050             # 吊杆原底端（=旧罩顶）
ROD_BOT_NEW = ROD_BOT_OLD + LIFT  # 1.80 = 新罩顶（Shade_Out 上沿 1.050+0.75）
OUT = r"E:\Snooker\assets\hall\pool_hall.fbx"

def wminmax(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    zs = [p.z for p in pts]
    return min(zs), max(zs)

# ---- 1. 罩体抬升 ----
lifted = 0
for o in bpy.data.objects:
    if o.type == 'MESH' and (o.name.startswith("Shade_Out") or o.name.startswith("Shade_In")
                             or o.name.startswith("Shade_Panel")):
        o.location.z += LIFT
        lifted += 1
print("[LIFT] shades lifted:", lifted)

# ---- 2. 吊杆：天花端锚定、按比例缩短（改 mesh 顶点，平移矩阵下安全） ----
rods = 0
k = (CEIL - ROD_BOT_NEW) / (CEIL - ROD_BOT_OLD)
for o in bpy.data.objects:
    if o.type != 'MESH' or not o.name.startswith("Rod"):
        continue
    mw = o.matrix_world
    mwi = mw.inverted()
    for v in o.data.vertices:
        p = mw @ v.co
        p.z = CEIL - (CEIL - p.z) * k
        v.co = mwi @ p
    rods += 1
    z0, z1 = wminmax(o)
    print("[LIFT] %-12s new zspan=(%.3f..%.3f)" % (o.name, z0, z1))
print("[LIFT] rods re-anchored:", rods)

# ---- 3. 灯罩下的面光同步抬升（仅 blend 预览用，FBX 不含灯光） ----
for o in bpy.data.objects:
    if o.type == 'LIGHT' and o.location.z < 2.0:
        print("[LIFT] light %-16s z %.2f -> %.2f" % (o.name, o.location.z, o.location.z + LIFT))
        o.location.z += LIFT

# ---- 4. 审计新罩体高度 ----
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name.startswith(("Shade_Out_0", "Shade_Panel_0")):
        z0, z1 = wminmax(o)
        print("[LIFT] audit %-14s zspan=(%.3f..%.3f)" % (o.name, z0, z1))

# ---- 5. 保存权威 blend ----
bpy.ops.wm.save_mainfile()
print("[LIFT] blend saved")

# ---- 6. 导出 FBX（剔主桌：根对象 Table_Root* 且 x≈0） ----
for o in bpy.context.scene.objects:
    o.select_set(False)
roots = [o for o in bpy.context.scene.objects if o.parent is None]
skipped, picked = 0, 0
for o in roots:
    if o.name.startswith("Table_Root") and abs(o.location.x) < 0.01:
        skipped += 1
        print("[FBX] skip main-table root:", o.name, "x=%.2f" % o.location.x)
        continue
    o.select_set(True)
    picked += 1
print("[FBX] roots picked=%d skipped=%d" % (picked, skipped))

try:
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'},
                             path_mode='ABSOLUTE', embed_textures=True,
                             add_leaf_bones=False, bake_anim=False,
                             mesh_smooth_type='FACE')
    print("[FBX] export ok ->", OUT)
except Exception as e:
    print("[FBX] EXPORT FAILED:", e)
