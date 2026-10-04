# -*- coding: utf-8 -*-
# hall_verify_fbx.py —— 反向导入导出的 FBX 到空场景，清点网格/材质/灯体
# 用法: blender -b --factory-startup --python hall_verify_fbx.py
import bpy

FBX = r"E:\Snooker\assets\hall\pool_hall.fbx"

# 清空工厂场景
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.import_scene.fbx(filepath=FBX)

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print("[VERIFY] mesh objects in FBX =", len(meshes))

names = sorted({(o.data.materials[0].name if o.data.materials else "<none>") for o in meshes})
print("[VERIFY] unique material slots =", len(names))

def wminmax(o):
    if not o.data.vertices:
        return (0, 0)
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    return (min(zs), max(zs))

lamps = [o for o in meshes if o.name.startswith(("Shade", "Rod"))]
print("[VERIFY] lamp/rod objects =", len(lamps))
for o in lamps:
    z0, z1 = wminmax(o)
    print("[VERIFY]   %-16s z=(%.3f..%.3f)" % (o.name, z0, z1))

# 关键道具计数（应齐全）
for key in ("GreenChair", "Armchair", "ArmChair", "Plant", "Cue", "Clock", "Poster", "Score", "Downlight"):
    n = len([o for o in meshes if key.lower() in o.name.lower()])
    print("[VERIFY] %-12s x%d" % (key, n))
