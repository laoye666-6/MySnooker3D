# =====================================================================================
# shrink_pockets.py —— 无头模式：把球桌六个袋口的黑色圆柱（Pocket_0~5）缩小
#
# 运行方式：
#   blender.exe -b -P E:\Snooker\blender\shrink_pockets.py
#
# 说明：
#   - 只缩 X/Y 半径（×0.85），Z 深度保持不变——深度变了会把袋口黑圈沉到台面以下，
#     视觉上黑圈会消失
#   - 缩后半径：角袋 0.079→0.067，中袋 0.086→0.073，仍大于台呢开孔
#     (0.055/0.062)，黑圈完整包住开孔；且小于捕获半径 (0.070/0.066) 不影响进球判定
#   - 物理与 C# 脚本完全不需要改动（袋口捕获逻辑独立于视觉模型）
#   - 其余物体（台呢/库边带 UV）原样保留
# =====================================================================================
import bpy

SRC = r"E:\Snooker\assets\table.obj"
SCALE = 0.85                                   # 半径缩小到 85%

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=SRC)

n = 0
for ob in bpy.context.scene.objects:
    if ob.type == 'MESH' and ob.name.startswith("Pocket"):
        ob.scale = (SCALE, SCALE, 1.0)         # 只缩半径，不动深度
        n += 1
print("[SHRINK] scaled %d pocket cylinders to %.0f%%" % (n, SCALE * 100))

bpy.ops.object.select_all(action='DESELECT')
for ob in bpy.context.scene.objects:
    ob.select_set(True)
bpy.context.view_layer.objects.active = bpy.context.scene.objects[0]
bpy.ops.wm.obj_export(
    filepath=SRC,                              # 原地覆盖（带 UV、材质名不变）
    export_selected_objects=False,
    export_materials=True,
    export_normals=True,
    export_uv=True,
    global_scale=1.0,
)
print("[SHRINK] table.obj re-exported")
print("[SHRINK] ALL DONE")
