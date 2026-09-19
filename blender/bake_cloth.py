# =====================================================================================
# bake_cloth.py —— 无头模式：为 Unity 游戏烘制台呢/木纹贴图 + 重导出带 UV 的球桌 OBJ
#
# 运行方式（不打开界面）：
#   blender.exe -b -P E:\Snooker\blender\bake_cloth.py
#
# 产物：
#   1. E:\Snooker\assets\cloth_texture.png —— 1024²，代表 1m×1m 台呢（无缝平铺）
#   2. E:\Snooker\assets\wood_texture.png  —— 1024²，代表 1m×1m 木纹（桌框/桌腿用）
#   3. E:\Snooker\assets\table.obj / table.mtl —— 重导出（台呢/库边/桌框/桌腿带 UV）
# =====================================================================================
import bpy
import os

ASSET_DIR = r"E:\Snooker\assets"
TABLE_OBJ = os.path.join(ASSET_DIR, "table.obj")
TEX_OUT = os.path.join(ASSET_DIR, "cloth_texture.png")
WOOD_OUT = os.path.join(ASSET_DIR, "wood_texture.png")
os.makedirs(ASSET_DIR, exist_ok=True)

# 共用的颜色逻辑：深浅绿噪声斑驳 + 交叉织纹（与 open_table_edit.py 一致）
def build_cloth_color_nodes(nt, weft_strength=1.0):
    nt.nodes.clear()
    tex = nt.nodes.new('ShaderNodeTexCoord');  tex.location = (-1100, 0)
    map1 = nt.nodes.new('ShaderNodeMapping');  map1.location = (-900, 0)
    n1 = nt.nodes.new('ShaderNodeTexNoise');   n1.location = (-700, 300)
    n1.inputs['Scale'].default_value = 90.0
    n1.inputs['Detail'].default_value = 6.0
    n2 = nt.nodes.new('ShaderNodeTexNoise');   n2.location = (-700, 0)
    n2.inputs['Scale'].default_value = 10.0
    n2.inputs['Detail'].default_value = 3.0
    r1 = nt.nodes.new('ShaderNodeValToRGB');   r1.location = (-500, 300)
    r1.color_ramp.elements[0].color = (0.075, 0.3525, 0.165, 1)
    r1.color_ramp.elements[1].color = (0.118, 0.5546, 0.2596, 1)
    r2 = nt.nodes.new('ShaderNodeValToRGB');   r2.location = (-500, 0)
    r2.color_ramp.elements[0].color = (0.085, 0.3995, 0.187, 1)
    r2.color_ramp.elements[1].color = (0.11, 0.517, 0.242, 1)
    mix = nt.nodes.new('ShaderNodeMix');       mix.location = (-300, 150)
    mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 0.6
    wx = nt.nodes.new('ShaderNodeTexWave');    wx.location = (-700, -300)
    wx.wave_type = 'BANDS'; wx.bands_direction = 'X'; wx.wave_profile = 'SIN'
    wx.inputs['Scale'].default_value = 128.0
    wy = nt.nodes.new('ShaderNodeTexWave');    wy.location = (-700, -500)
    wy.wave_type = 'BANDS'; wy.bands_direction = 'Y'; wy.wave_profile = 'SIN'
    wy.inputs['Scale'].default_value = 128.0
    weave = nt.nodes.new('ShaderNodeMix');     weave.location = (-450, -400)
    weave.data_type = 'FLOAT'; weave.blend_type = 'MULTIPLY'
    weave.inputs['Factor'].default_value = 1.0
    r3 = nt.nodes.new('ShaderNodeValToRGB');   r3.location = (-250, -400)
    lo = 1.0 - 0.12 * weft_strength
    hi = 1.0 + 0.08 * weft_strength
    r3.color_ramp.elements[0].color = (lo, lo, lo, 1)
    r3.color_ramp.elements[1].color = (hi, hi, hi, 1)
    mix2 = nt.nodes.new('ShaderNodeMix');      mix2.location = (-60, 150)
    mix2.data_type = 'RGBA'; mix2.blend_type = 'MULTIPLY'
    mix2.inputs['Factor'].default_value = 1.0
    nt.links.new(tex.outputs['Object'], map1.inputs['Vector'])
    nt.links.new(map1.outputs['Vector'], n1.inputs['Vector'])
    nt.links.new(map1.outputs['Vector'], n2.inputs['Vector'])
    nt.links.new(map1.outputs['Vector'], wx.inputs['Vector'])
    nt.links.new(map1.outputs['Vector'], wy.inputs['Vector'])
    nt.links.new(n1.outputs['Fac'], r1.inputs['Fac'])
    nt.links.new(n2.outputs['Fac'], r2.inputs['Fac'])
    nt.links.new(r1.outputs['Color'], mix.inputs['A'])
    nt.links.new(r2.outputs['Color'], mix.inputs['B'])
    nt.links.new(wx.outputs['Fac'], weave.inputs['A'])
    nt.links.new(wy.outputs['Fac'], weave.inputs['B'])
    nt.links.new(weave.outputs['Result'], r3.inputs['Fac'])
    nt.links.new(mix.outputs['Result'], mix2.inputs['A'])
    nt.links.new(r3.outputs['Color'], mix2.inputs['B'])
    return mix2.outputs['Result']

# ============================ 第 1 步：烘台呢贴图 ============================
bpy.ops.wm.read_factory_settings(use_empty=True)

# 1×1 米平面（摆在 0..1 象限，与 UV 的每米一个周期对齐）
bpy.ops.mesh.primitive_plane_add(size=1, location=(0.5, 0.5, 0))
plane = bpy.context.active_object
mat = bpy.data.materials.new("ClothBake")
mat.use_nodes = True
nt = mat.node_tree
result = build_cloth_color_nodes(nt)
out = nt.nodes.new('ShaderNodeOutputMaterial'); out.location = (400, 0)
emit = nt.nodes.new('ShaderNodeEmission');      emit.location = (200, 0)
nt.links.new(result, emit.inputs['Color'])
nt.links.new(emit.outputs['Emission'], out.inputs['Surface'])
plane.data.materials.append(mat)

# 正交相机俯拍 1×1m
cam_data = bpy.data.cameras.new("BakeCam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 1.0
cam = bpy.data.objects.new("BakeCam", cam_data)
cam.location = (0.5, 0.5, 2.0)          # 默认朝 -Z，即垂直俯拍
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 16
sc.render.resolution_x = 1024
sc.render.resolution_y = 1024
sc.view_settings.view_transform = 'Standard'   # 保持原色不被 Filmic/AgX 压灰
sc.render.filepath = TEX_OUT
bpy.ops.render.render(write_still=True)
print("[BAKE] texture saved:", TEX_OUT)

# ==================== 第 1b 步：烘木纹贴图（桌框/桌腿用） ====================
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_plane_add(size=1, location=(0.5, 0.5, 0))
plane = bpy.context.active_object
wmat = bpy.data.materials.new("WoodBake")
wmat.use_nodes = True
wnt = wmat.node_tree
wnt.nodes.clear()
wout = wnt.nodes.new('ShaderNodeOutputMaterial'); wout.location = (700, 0)
wemit = wnt.nodes.new('ShaderNodeEmission');      wemit.location = (450, 0)

# 木纹节点：细长噪声拉伸成顺纹条纹 + 大尺度年轮状波纹扰动 + 细孔噪点
wtex = wnt.nodes.new('ShaderNodeTexCoord');  wtex.location = (-1100, 0)
wmap = wnt.nodes.new('ShaderNodeMapping');   wmap.location = (-900, 0)
wmap.inputs['Scale'].default_value = (2.0, 28.0, 1.0)   # X 拉长 Y 压密 → 顺 X 的长条纹
wn1 = wnt.nodes.new('ShaderNodeTexNoise');   wn1.location = (-700, 200)
wn1.inputs['Scale'].default_value = 5.0
wn1.inputs['Detail'].default_value = 8.0
wn1.inputs['Distortion'].default_value = 1.5            # 扭曲让纹理有天然波折
wr1 = wnt.nodes.new('ShaderNodeValToRGB');   wr1.location = (-480, 200)
wr1.color_ramp.elements[0].position = 0.30
wr1.color_ramp.elements[0].color = (0.085, 0.045, 0.022, 1)    # 深红棕
wr1.color_ramp.elements[1].position = 0.75
wr1.color_ramp.elements[1].color = (0.310, 0.185, 0.095, 1)    # 亮红棕
wn2 = wnt.nodes.new('ShaderNodeTexNoise');   wn2.location = (-700, -150)
wn2.inputs['Scale'].default_value = 180.0               # 细噪点 = 木孔
wn2.inputs['Detail'].default_value = 4.0
wr2 = wnt.nodes.new('ShaderNodeValToRGB');   wr2.location = (-480, -150)
wr2.color_ramp.elements[0].color = (0.80, 0.75, 0.70, 1)
wr2.color_ramp.elements[1].color = (1.05, 1.02, 1.0, 1)
wmix = wnt.nodes.new('ShaderNodeMix');       wmix.location = (-150, 40)
wmix.data_type = 'RGBA'; wmix.blend_type = 'MULTIPLY'
wmix.inputs['Factor'].default_value = 0.45
wnt.links.new(wtex.outputs['Object'], wmap.inputs['Vector'])
wnt.links.new(wmap.outputs['Vector'], wn1.inputs['Vector'])
wnt.links.new(wmap.outputs['Vector'], wn2.inputs['Vector'])
wnt.links.new(wn1.outputs['Fac'], wr1.inputs['Fac'])
wnt.links.new(wn2.outputs['Fac'], wr2.inputs['Fac'])
wnt.links.new(wr1.outputs['Color'], wmix.inputs['A'])
wnt.links.new(wr2.outputs['Color'], wmix.inputs['B'])
wnt.links.new(wmix.outputs['Result'], wemit.inputs['Color'])
wnt.links.new(wemit.outputs['Emission'], wout.inputs['Surface'])
plane.data.materials.append(wmat)

wcam_data = bpy.data.cameras.new("WoodCam")
wcam_data.type = 'ORTHO'
wcam_data.ortho_scale = 1.0
wcam = bpy.data.objects.new("WoodCam", wcam_data)
wcam.location = (0.5, 0.5, 2.0)
bpy.context.collection.objects.link(wcam)
bpy.context.scene.camera = wcam
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 16
sc.render.resolution_x = 1024
sc.render.resolution_y = 1024
sc.view_settings.view_transform = 'Standard'
sc.render.filepath = WOOD_OUT
bpy.ops.render.render(write_still=True)
print("[BAKE] wood saved:", WOOD_OUT)

# ==================== 第 2 步：给球桌加 UV 并重导出 OBJ ====================
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=TABLE_OBJ)

def add_uv(obj):
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=1.0)     # 1 米一个贴图周期
    bpy.ops.object.mode_set(mode='OBJECT')

for ob in bpy.context.scene.objects:
    if ob.type != 'MESH':
        continue
    # 台呢/库边用布纹 UV；桌框/桌腿用木纹 UV；袋口顺手加（黑面无纹理也无妨）
    if ob.name.startswith("Cloth") or ob.name.startswith("Cushion") or \
       ob.name.startswith("Frame") or ob.name.startswith("Leg") or ob.name.startswith("Pocket"):
        add_uv(ob)

bpy.ops.object.select_all(action='DESELECT')
for ob in bpy.context.scene.objects:
    ob.select_set(True)
bpy.context.view_layer.objects.active = bpy.context.scene.objects[0]
bpy.ops.wm.obj_export(
    filepath=TABLE_OBJ,
    export_selected_objects=False,
    export_materials=True,
    export_normals=True,
    export_uv=True,                # 关键：这次带 UV 出
    global_scale=1.0,
)
print("[BAKE] table.obj re-exported with UVs")
print("[BAKE] ALL DONE")
