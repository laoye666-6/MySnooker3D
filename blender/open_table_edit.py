# =====================================================================================
# open_table_edit.py —— 在 Blender 图形界面中打开球桌模型并给台呢/库边加布纹材质
#
# 启动方式（GUI 模式，窗口会留在屏幕上供手动编辑）：
#   blender.exe --python E:\Snooker\blender\open_table_edit.py
#
# 做了什么：
#   1. 清空场景，导入 E:\Snooker\assets\table.obj（台呢/库边/木框/袋口/标线…）
#   2. 创建程序化"台呢布纹"材质（绒毛噪声 + 明暗斑驳 + 交叉织纹 + 凹凸），
#      赋给台呢(Cloth)与库边(Cushion)，材质名与 Unity 工程按名匹配兼容
#   3. 台呢/库边生成 UV（cube project，1 米一个贴图周期）
#   4. 视口切"材质预览"着色并取景到球桌
#   5. 保存 E:\Snooker\blender\table_edit.blend（以后双击直接打开继续编辑）
#
# 技术说明：--python 脚本在 Blender 启动早期执行，此时窗口/上下文还没就绪，
# 直接跑 bpy.ops 会报 "context is incorrect"。所以主逻辑注册到 bpy.app.timers，
# 等启动完成后再执行；所有 ops 调用都套 3D 视口的 temp_override 上下文。
# =====================================================================================
import bpy

BLENDER_DIR = r"E:\Snooker\blender"
TABLE_OBJ = r"E:\Snooker\assets\table.obj"

# ---------------------------------------------------------------------------
# 3D 视口上下文覆盖器：启动早期 / 空场景下 bpy.ops 需要它才能通过 poll 检查
# ---------------------------------------------------------------------------
def _v3d_override():
    wm = bpy.context.window_manager
    for window in wm.windows:
        for area in window.screen.areas:
            if area.type == 'VIEW_3D':
                for region in area.regions:
                    if region.type == 'WINDOW':
                        return bpy.context.temp_override(window=window, area=area, region=region)
    return None

# ---------------------------------------------------------------------------
# 布纹材质节点（视觉版）： Principled BSDF + 噪声绒毛 + 斑驳 + 交叉织纹 + 凹凸
# base_rgb: 基色；weft: 织纹可见度 0~1
# ---------------------------------------------------------------------------
def build_cloth_nodes(nt, base_rgb, weft):
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial');  out.location = (900, 0)
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); bsdf.location = (600, 0)
    bsdf.inputs['Roughness'].default_value = 0.97            # 台呢几乎不反光
    if 'Sheen Weight' in bsdf.inputs:
        bsdf.inputs['Sheen Weight'].default_value = 0.3      # 布料绒毛感

    tex = nt.nodes.new('ShaderNodeTexCoord');  tex.location = (-1100, 0)
    map1 = nt.nodes.new('ShaderNodeMapping');  map1.location = (-900, 0)

    n1 = nt.nodes.new('ShaderNodeTexNoise');   n1.location = (-700, 300)   # 细噪声：绒毛颗粒
    n1.inputs['Scale'].default_value = 90.0
    n1.inputs['Detail'].default_value = 6.0
    n2 = nt.nodes.new('ShaderNodeTexNoise');   n2.location = (-700, 0)     # 粗噪声：明暗斑驳
    n2.inputs['Scale'].default_value = 10.0
    n2.inputs['Detail'].default_value = 3.0

    r1 = nt.nodes.new('ShaderNodeValToRGB');   r1.location = (-500, 300)   # 细噪声 → 深浅绿
    r1.color_ramp.elements[0].color = (base_rgb[0]*0.75, base_rgb[1]*0.75, base_rgb[2]*0.75, 1)
    r1.color_ramp.elements[1].color = (base_rgb[0]*1.18, base_rgb[1]*1.18, base_rgb[2]*1.18, 1)
    r2 = nt.nodes.new('ShaderNodeValToRGB');   r2.location = (-500, 0)     # 粗噪声 → 更缓的明暗
    r2.color_ramp.elements[0].color = (base_rgb[0]*0.85, base_rgb[1]*0.85, base_rgb[2]*0.85, 1)
    r2.color_ramp.elements[1].color = (base_rgb[0]*1.10, base_rgb[1]*1.10, base_rgb[2]*1.10, 1)

    mix = nt.nodes.new('ShaderNodeMix');       mix.location = (-300, 150)  # 两层斑驳相乘
    mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 0.6

    wx = nt.nodes.new('ShaderNodeTexWave');    wx.location = (-700, -300)  # X 向织纹
    wx.wave_type = 'BANDS'; wx.bands_direction = 'X'; wx.wave_profile = 'SIN'
    wx.inputs['Scale'].default_value = 128.0   # 每米 128 条（整除贴图像素 → 无缝平铺）
    wy = nt.nodes.new('ShaderNodeTexWave');    wy.location = (-700, -500)  # Y 向织纹
    wy.wave_type = 'BANDS'; wy.bands_direction = 'Y'; wy.wave_profile = 'SIN'
    wy.inputs['Scale'].default_value = 128.0
    weave = nt.nodes.new('ShaderNodeMix');     weave.location = (-450, -400)
    weave.data_type = 'FLOAT'; weave.blend_type = 'MULTIPLY'
    weave.inputs['Factor'].default_value = 1.0

    r3 = nt.nodes.new('ShaderNodeValToRGB');   r3.location = (-250, -400)  # 织纹 → 微弱明暗
    lo = 1.0 - 0.12 * weft
    hi = 1.0 + 0.08 * weft
    r3.color_ramp.elements[0].color = (lo, lo, lo, 1)
    r3.color_ramp.elements[1].color = (hi, hi, hi, 1)
    mix2 = nt.nodes.new('ShaderNodeMix');      mix2.location = (-60, 150)
    mix2.data_type = 'RGBA'; mix2.blend_type = 'MULTIPLY'
    mix2.inputs['Factor'].default_value = 1.0

    bmp = nt.nodes.new('ShaderNodeBump');      bmp.location = (350, -250)  # 绒面凹凸
    bmp.inputs['Strength'].default_value = 0.12
    bmp.inputs['Distance'].default_value = 0.0005

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
    nt.links.new(n1.outputs['Fac'], bmp.inputs['Height'])
    nt.links.new(mix2.outputs['Result'], bsdf.inputs['Base Color'])
    nt.links.new(bmp.outputs['Normal'], bsdf.inputs['Normal'])
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])

def get_or_make_material(name, base_rgb, weft):
    """复用 OBJ 导入带进来的同名材质（保证导出 usemtl 名不变），清空重建节点树。"""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    build_cloth_nodes(m.node_tree, base_rgb, weft)
    return m

def add_uv_and_assign(obj, mat):
    """赋材质 + 生成 UV（cube project：UV 以米为单位，贴图每 1 米平铺一次）。"""
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UVMap")
    ov = _v3d_override()
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    with ov if ov else bpy.context.temp_override():
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(cube_size=1.0)
        bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------------------
# 主逻辑（启动完成后由 timer 触发执行）
# ---------------------------------------------------------------------------
def main():
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ov = _v3d_override()
        if ov:
            with ov:
                bpy.ops.wm.obj_import(filepath=TABLE_OBJ)
        else:
            bpy.ops.wm.obj_import(filepath=TABLE_OBJ)

        mat_cloth = get_or_make_material("Cloth", (0.10, 0.47, 0.22), 1.0)     # 台呢：比赛绿
        mat_cushion = get_or_make_material("Cushion", (0.08, 0.42, 0.20), 0.7) # 库边：略深

        for ob in bpy.context.scene.objects:
            if ob.type != 'MESH':
                continue
            if ob.name.startswith("Cloth"):
                add_uv_and_assign(ob, mat_cloth)
            elif ob.name.startswith("Cushion"):
                add_uv_and_assign(ob, mat_cushion)

        # 视口切材质预览着色 + 取景到球桌
        for window in bpy.context.window_manager.windows:
            for area in window.screen.areas:
                if area.type == 'VIEW_3D':
                    for space in area.spaces:
                        if space.type == 'VIEW_3D':
                            space.shading.type = 'MATERIAL'
                    for region in area.regions:
                        if region.type == 'WINDOW':
                            with bpy.context.temp_override(window=window, area=area, region=region):
                                try:
                                    bpy.ops.view3d.view_all(center=False)
                                except Exception:
                                    pass

        bpy.ops.wm.save_as_mainfile(filepath=BLENDER_DIR + r"\table_edit.blend")
        print("[OPEN-TABLE] done: table imported, cloth material applied, saved table_edit.blend")
    except Exception as e:
        print("[OPEN-TABLE] ERROR:", e)
    return None   # timer 返回 None 表示不再重复执行

# 启动完成 1 秒后再执行主逻辑（等窗口与上下文就绪）
bpy.app.timers.register(main, first_interval=1.0)
