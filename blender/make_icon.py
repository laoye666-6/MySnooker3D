# =====================================================================================
# make_icon.py —— 无头模式：渲染游戏应用图标（简洁风：深绿底 + 红球主角 + 白球点缀）
# 运行：blender.exe -b -P E:\Snooker\blender\make_icon.py
# 产物：E:\Snooker\assets\icon.png（512×512）
# 灯光：大面积柔光（大尺寸面积灯让球面反射呈柔和渐变，避免灯形硬块入镜）
# =====================================================================================
import bpy, os
from math import radians
from mathutils import Vector

OUT = r"E:\Snooker\assets\icon.png"
bpy.ops.wm.read_factory_settings(use_empty=True)

# 世界背景：深绿（铺满整个画面，无多余元素）
w = bpy.data.worlds.new("W")
bpy.context.scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.020, 0.105, 0.048, 1.0)
w.node_tree.nodes["Background"].inputs[1].default_value = 1.0

def ball_mat(name, rgb, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Roughness"].default_value = rough
    if "Coat Weight" in b.inputs:
        b.inputs["Coat Weight"].default_value = 0.4      # 清漆层：真实球体高光
    return m

# 红球主角（画面中心偏右）
bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=(0.18, 0, 0), segments=128, ring_count=48)
red = bpy.context.active_object
red.name = "RedBall"
red.data.materials.append(ball_mat("Red", (0.62, 0.025, 0.02), 0.10))

# 白球点缀（左下角，小而亮）
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.30, location=(-0.98, -0.2, -0.66), segments=64, ring_count=32)
white = bpy.context.active_object
white.name = "WhiteBall"
white.data.materials.append(ball_mat("White", (0.93, 0.93, 0.88), 0.18))

# 灯光：左前上方大面积主光 + 右后大面积轮廓光（尺寸大 → 反射柔和不显灯形）
key = bpy.data.lights.new("Key", 'SUN')
key.energy = 3.0
key.angle = 0.26
ko = bpy.data.objects.new("Key", key)
ko.location = (-2.6, -3.4, 2.8)
bpy.context.collection.objects.link(ko)
d = Vector((0.1, 0, 0)) - Vector(ko.location)
ko.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

rim = bpy.data.lights.new("Rim", 'SUN')
rim.energy = 0.9
rim.angle = 0.35
ro = bpy.data.objects.new("Rim", rim)
ro.location = (3.0, 2.4, 1.6)
bpy.context.collection.objects.link(ro)
d = Vector((0, 0, 0)) - Vector(ro.location)
ro.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

# 正交相机：正面微俯视，红球约占画面 78%
cam_data = bpy.data.cameras.new("IconCam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 2.55
cam = bpy.data.objects.new("IconCam", cam_data)
cam.location = (0.05, -4.6, 0.22)
bpy.context.collection.objects.link(cam)
d = Vector((0.05, 0, 0)) - Vector(cam.location)
cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 128
sc.render.resolution_x = 512
sc.render.resolution_y = 512
sc.view_settings.view_transform = 'Standard'
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("[ICON] saved:", OUT)
