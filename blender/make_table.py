# Blender 4.x/5.x headless script: build a standard snooker table + cue, export OBJ for Unity.
# Run: blender -b -P make_table.py
import bpy, bmesh, math, os
from math import radians, sin, cos, pi
from mathutils import Matrix, Vector

OUT = r"E:\Snooker\assets"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------- dimensions (meters, Blender Z-up) ----------------
L, W = 3.569, 1.778            # playing area between cushion noses
CUSH_D = 0.055                 # cushion depth (nose -> wood)
CUSH_H = 0.040                 # cushion max height above cloth
NOSE_TOP = 0.034
CORN_GAP = 0.072               # cushion end distance from corner along rail
CEN_GAP = 0.056                # half-width of center pocket mouth
JAW_DX = 0.050                 # jaw slant depth along rail
HOLE_CORNER = 0.055            # cloth hole radius, corner pockets
HOLE_CENTER = 0.062            # cloth hole radius, center pockets
CORNER_OFF = 0.008             # pocket center offset outside cloth corner
CENTER_OFF = 0.018
FRAME_OUT_X, FRAME_OUT_Y = L / 2 + 0.19, W / 2 + 0.19
FRAME_IN_X, FRAME_IN_Y = L / 2 + 0.045, W / 2 + 0.045
FRAME_TOP, FRAME_BOT = 0.048, -0.12

BAULK_X = -(L / 2 - 0.737)     # baulk line
D_R = 0.292                    # D radius
SPOTS = {
    "brown":  (BAULK_X, 0.0),
    "green":  (BAULK_X, D_R),
    "yellow": (BAULK_X, -D_R),
    "blue":   (0.0, 0.0),
    "pink":   (L / 4, 0.0),
    "black":  (L / 2 - 0.324, 0.0),
}

POCKETS = []
for sx in (1, -1):
    for sy in (1, -1):
        POCKETS.append((sx * (L / 2 + CORNER_OFF), sy * (W / 2 + CORNER_OFF), HOLE_CORNER))
POCKETS.append((0.0, (W / 2 + CENTER_OFF), HOLE_CENTER))
POCKETS.append((0.0, -(W / 2 + CENTER_OFF), HOLE_CENTER))

# ---------------- materials ----------------
def make_mat(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1.0)
    return m

M_CLOTH = make_mat("Cloth", (0.075, 0.42, 0.18), 0.95)
M_CUSH = make_mat("Cushion", (0.065, 0.38, 0.165), 0.9)
M_WOOD = make_mat("Wood", (0.30, 0.13, 0.055), 0.35)
M_DARK = make_mat("Pocket", (0.012, 0.012, 0.012), 0.9)
M_WHITE = make_mat("Mark", (0.92, 0.92, 0.88), 0.8)
M_SHAFT = make_mat("CueShaft", (0.78, 0.56, 0.33), 0.3)
M_BUTT = make_mat("CueButt", (0.09, 0.05, 0.03), 0.3)
M_FERR = make_mat("Ferrule", (0.95, 0.95, 0.90), 0.3)

table_objs = []
cue_objs = []

# ---------------- helpers ----------------
def mesh_obj(name, verts, faces, material):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if material:
        me.materials.append(material)
    return ob

def box(name, cx, cy, cz, sx, sy, sz, material):
    x0, x1 = cx - sx / 2, cx + sx / 2
    y0, y1 = cy - sy / 2, cy + sy / 2
    z0, z1 = cz - sz / 2, cz + sz / 2
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return mesh_obj(name, v, f, material)

def cylinder(name, cx, cy, cz, r, depth, material, seg=48):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg,
                          radius1=r, radius2=r, depth=depth, matrix=Matrix())
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location = (cx, cy, cz)
    bpy.context.collection.objects.link(ob)
    if material:
        me.materials.append(material)
    return ob

def boolean_diff(target, cutters):
    for c in cutters:
        md = target.modifiers.new("bool", 'BOOLEAN')
        md.operation = 'DIFFERENCE'
        md.solver = 'EXACT'
        md.object = c
    bpy.context.view_layer.objects.active = target
    for md in list(target.modifiers):
        bpy.ops.object.modifier_apply(modifier=md.name)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)
    return target

# ---------------- cloth bed with pocket holes ----------------
bed = box("Cloth_Bed", 0, 0, -0.025, L + 0.12, W + 0.12, 0.05, M_CLOTH)
cutters = [cylinder("cut%d" % i, px, py, -0.02, hr, 0.5, None) for i, (px, py, hr) in enumerate(POCKETS)]
boolean_diff(bed, cutters)
table_objs.append(bed)

# ---------------- cushions (6 segments, jawed ends) ----------------
PROF = [(0.0, 0.0), (CUSH_D, 0.0), (CUSH_D, 0.030), (0.012, CUSH_H), (0.0, NOSE_TOP)]

def cushion(name, T, u0, u1, material):
    """T(u,v,z) -> world; v=0 at nose plane. Pockets lie beyond both u ends;
    the cushion back shears away from each pocket along u."""
    n = len(PROF)
    verts = []
    for (ue, pdir) in ((u0, +1), (u1, -1)):
        for (v, z) in PROF:
            s = pdir * (v / CUSH_D) * JAW_DX
            verts.append(T(ue + s, v, z))
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))       # side quads (wrap closes nose face)
    faces.append(tuple(range(n - 1, -1, -1)))     # end cap u0
    faces.append(tuple(range(n, 2 * n)))          # end cap u1
    ob = mesh_obj(name, verts, faces, material)
    table_objs.append(ob)
    return ob

half_long = [CEN_GAP, L / 2 - CORN_GAP]
half_short = [-(W / 2 - CORN_GAP), W / 2 - CORN_GAP]
cushion("Cushion_TopA", lambda u, v, z: (u, W / 2 + v, z), half_long[0], half_long[1], M_CUSH)
cushion("Cushion_TopB", lambda u, v, z: (u, W / 2 + v, z), -half_long[1], -half_long[0], M_CUSH)
cushion("Cushion_BotA", lambda u, v, z: (u, -(W / 2 + v), z), half_long[0], half_long[1], M_CUSH)
cushion("Cushion_BotB", lambda u, v, z: (u, -(W / 2 + v), z), -half_long[1], -half_long[0], M_CUSH)
cushion("Cushion_Right", lambda u, v, z: (L / 2 + v, u, z), half_short[0], half_short[1], M_CUSH)
cushion("Cushion_Left", lambda u, v, z: (-(L / 2 + v), u, z), half_short[0], half_short[1], M_CUSH)

# ---------------- wooden frame with pocket openings ----------------
frame = box("Frame", 0, 0, (FRAME_TOP + FRAME_BOT) / 2,
            FRAME_OUT_X * 2, FRAME_OUT_Y * 2, FRAME_TOP - FRAME_BOT, M_WOOD)
inner = box("cutInner", 0, 0, (FRAME_TOP + FRAME_BOT) / 2,
            FRAME_IN_X * 2, FRAME_IN_Y * 2, FRAME_TOP - FRAME_BOT + 0.2, None)
boolean_diff(frame, [inner])
pcuts = [cylinder("cutp%d" % i, px, py, 0, hr + 0.02, 1.0, None) for i, (px, py, hr) in enumerate(POCKETS)]
boolean_diff(frame, pcuts)
table_objs.append(frame)

# pocket wells (dark), recessed under frame top
# v0.30：圆柱顶端与台面平齐（顶端 y=-0.002，比台呢面低 2mm 防止共面闪烁）。
# 此前顶端在 FRAME_TOP-0.004=0.044，黑盘凸出台面 44mm 像硬币。
# 圆柱半径 = 开孔+8mm，正好垫在台呢开孔与木框让位之间，从孔里看到的是平齐的黑面。
well_objs = []
for i, (px, py, hr) in enumerate(POCKETS):
    w = cylinder("Pocket_%d" % i, px, py, -0.002 - 0.30, hr + 0.008, 0.60, M_DARK)
    table_objs.append(w)
    well_objs.append(w)

# ---------------- legs ----------------
lx = [-(L / 2 - 0.25), 0.9, L / 2 - 0.25]
ly = [-(W / 2 + 0.06), W / 2 + 0.06]
k = 0
for x in lx:
    for y in ly:
        table_objs.append(box("Leg_%d" % k, x, y, FRAME_BOT - 0.30, 0.22, 0.22, 0.60, M_WOOD))
        k += 1

# ---------------- markings: baulk line, D, spots ----------------
table_objs.append(box("BaulkLine", BAULK_X, 0, 0.0008, 0.008, W - 0.02, 0.0016, M_WHITE))

r_in, r_out, N = D_R - 0.0035, D_R + 0.0035, 44
dv, df = [], []
for i in range(N + 1):
    a = pi / 2 + pi * i / N
    cA, sA = cos(a), sin(a)
    dv += [(BAULK_X + r_in * cA, r_in * sA, 0.0008), (BAULK_X + r_out * cA, r_out * sA, 0.0008)]
for i in range(N):
    b = 2 * i
    df.append((b, b + 1, b + 3, b + 2))
table_objs.append(mesh_obj("DArc", dv, df, M_WHITE))

for nm, (sx, sy) in SPOTS.items():
    table_objs.append(cylinder("Spot_%s" % nm, sx, sy, 0.0008, 0.005, 0.0016, M_WHITE, seg=16))

# ---------------- cue (tip at x=0, butt at x=-1.47) ----------------
def lathe(name, rings, material, seg=24):
    """rings: list of (x, radius); closed tube along X."""
    verts, faces = [], []
    for (x, r) in rings:
        for i in range(seg):
            a = 2 * pi * i / seg
            verts.append((x, r * cos(a), r * sin(a)))
    for k in range(len(rings) - 1):
        for i in range(seg):
            j = (i + 1) % seg
            a = k * seg + i
            b = k * seg + j
            c = (k + 1) * seg + j
            d = (k + 1) * seg + i
            faces.append((a, b, c, d))
    faces.append(tuple(range(seg - 1, -1, -1)))
    faces.append(tuple(range(len(rings) * seg - seg, len(rings) * seg)))
    ob = mesh_obj(name, verts, faces, material)
    cue_objs.append(ob)
    return ob

def taper_rings(x0, x1, r0, r1, n=8):
    return [(x0 + (x1 - x0) * i / n, r0 + (r1 - r0) * i / n) for i in range(n + 1)]

lathe("CueFerrule", [(-0.012, 0.0058), (0.0005, 0.0058)], M_FERR)
lathe("CueTip", [(0.0005, 0.0056), (0.006, 0.0050)], M_DARK)
lathe("CueShaft", taper_rings(-0.012, -1.17, 0.0058, 0.0088), M_SHAFT)
lathe("CueButt", taper_rings(-1.17, -1.47, 0.0088, 0.0145), M_BUTT)

# ---------------- export OBJ ----------------
props = {p.identifier: p for p in bpy.ops.wm.obj_export.get_rna_type().properties}
def axis_kw():
    kw = {}
    if 'forward_axis' in props:
        items = [e.identifier for e in props['forward_axis'].enum_items]
        for cand in ('NEGATIVE_Z', 'Z_BACKWARD'):
            if cand in items:
                kw['forward_axis'] = cand
                break
        items = [e.identifier for e in props['up_axis'].enum_items]
        for cand in ('Y', 'Y_UP'):
            if cand in items:
                kw['up_axis'] = cand
                break
    return kw

def export(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    kw = dict(filepath=path, export_selected_objects=True, export_materials=True,
              export_normals=True, export_uv=False, global_scale=1.0)
    kw.update(axis_kw())
    bpy.ops.wm.obj_export(**kw)
    print("EXPORTED", path)

export(os.path.join(OUT, "table.obj"), table_objs)
export(os.path.join(OUT, "cue.obj"), cue_objs)

# ---------------- preview renders ----------------
def add_cam(name, loc, target, ortho=None, lens=35):
    cam_data = bpy.data.cameras.new(name)
    if ortho:
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = ortho
    else:
        cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam)
    return cam

sun_data = bpy.data.lights.new("Sun", 'SUN')
sun_data.energy = 3.0
sun = bpy.data.objects.new("Sun", sun_data)
sun.rotation_euler = (radians(50), 0, radians(30))
bpy.context.collection.objects.link(sun)

sc = bpy.context.scene
sc.render.resolution_x = 1600
sc.render.resolution_y = 960

def render_to(path, cam):
    sc.camera = cam
    try:
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.display.shading.light = 'STUDIO'
        sc.display.shading.color_type = 'MATERIAL'
        sc.display.shading.show_cavity = True
        print("engine=workbench")
    except Exception as e:
        print("workbench failed", e)
        sc.render.engine = 'CYCLES'
        sc.cycles.samples = 24
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path)

render_to(os.path.join(OUT, "preview_top.png"),
          add_cam("CamTop", (0, 0, 5.2), (0, 0, 0), ortho=4.3))
render_to(os.path.join(OUT, "preview_persp.png"),
          add_cam("CamPersp", (2.4, -3.4, 2.0), (0, 0, 0.1), lens=32))

# ---------------- bounds report ----------------
for o in table_objs + cue_objs:
    if o.type == 'MESH':
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        xs = [v.x for v in bb]; ys = [v.y for v in bb]; zs = [v.z for v in bb]
        print("BOUNDS %-16s x[%7.3f %7.3f] y[%7.3f %7.3f] z[%7.3f %7.3f]" %
              (o.name, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
print("DONE")
