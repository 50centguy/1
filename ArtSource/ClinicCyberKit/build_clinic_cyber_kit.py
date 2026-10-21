"""Cyberpunk dressing for the four-prop clinic (Docs/Integration/RepairPropsCyberBench/ART_DIRECTION.md, cyber layer).

Three exports, each with its own alignment refs, so Unity can land each on the frame it belongs to:
  CyberRoomKit     room frame = Unity world (Blender x = Unity x, Blender y = Unity z, Blender z = Unity y):
                   magenta "义体维修" neon over the south door, cyan "24H", a vertical orange "收件处" on the SE wall,
                   two orange-red tube lights hung over the trade counter.
  CyberSurgeryKit  bed frame (same as SurgeryTableV2 / SurgeryGantryV2): cyan neon ring under the gantry ring, amber and
                   red indicator specks on its outer face, a PVC strip curtain skirt above head height (2.02-2.38 m,
                   outside every aisle's head room), three holo panels and a scan plane over the table.
  CyberBenchKit    bench frame (same as RepairBenchV2, Ref_BenchTopOrigin): three small holo panels above the riser.
Holo panel glyph textures are generated here (white on black; the shader tints them).

Run: blender -b --factory-startup --python build_clinic_cyber_kit.py -- [--norender]
"""
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "RepairBenchV2"))
import bv2_kit as K  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NO_RENDER = "--norender" in ARGS
TEX = os.path.join(HERE, "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
for d in (EXPORT, RENDERS):
    os.makedirs(d, exist_ok=True)
FONT_CJK = "C:/Windows/Fonts/Dengb.ttf"
FONT_LATIN = "C:/Windows/Fonts/bahnschrift.ttf"
RING_LO = 2.40

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene
MAT_SPEC = {n: (None, 0, .5, None, 1.0) for n in (
    "M_Neon_Magenta", "M_Neon_Cyan", "M_Neon_Orange", "M_Neon_Red", "M_Neon_Amber", "M_GT_DarkSteel",
    "M_Neon_MagentaFaulty", "M_Neon_CyanDim", "M_Film", "M_Curtain")}
FLAT = {"M_Neon_Magenta": (1, .2, .6), "M_Neon_Cyan": (.3, .95, 1), "M_Neon_Orange": (1, .45, .15), "M_Neon_Red": (1, .1, .08),
        "M_Neon_Amber": (1, .7, .2), "M_GT_DarkSteel": (.18, .2, .2), "M_Holo_Frame": (.3, .9, 1), "M_Curtain": (.7, .85, .9)}
K.init(SCENE, "", TEX, MAT_SPEC)

for ch, rows in {"4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
                 "6": "00110 01000 10000 11110 10001 10001 01110", "8": "01110 10001 10001 01110 10001 10001 01110",
                 "9": "01110 10001 10001 01111 00001 00010 01100", "J": "00111 00010 00010 00010 00010 10010 01100",
                 "Q": "01110 10001 10001 10001 10101 10010 01101", "Z": "11111 00001 00010 00100 01000 10000 11111",
                 ".": "00000 00000 00000 00000 00000 01100 01100", "%": "11001 11010 00010 00100 01000 01011 10011",
                 "+": "00000 00100 00100 11111 00100 00100 00000", ">": "01000 00100 00010 00001 00010 00100 01000",
                 "<": "00010 00100 01000 10000 01000 00100 00010", "[": "01110 01000 01000 01000 01000 01000 01110",
                 "]": "01110 00010 00010 00010 00010 00010 01110", "=": "00000 00000 11111 00000 11111 00000 00000"}.items():
    K.FONT.setdefault(ch, rows)


# ---------------------------------------------------------------- materials (preview only; Unity builds its own)
def build_materials():
    for name in MAT_SPEC:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        c = FLAT.get(name, (.3, .9, 1))
        bsdf.inputs["Base Color"].default_value = (*c, 1)
        if name.startswith(("M_Neon", "M_Holo")):
            bsdf.inputs["Emission Color"].default_value = (*c, 1)
            bsdf.inputs["Emission Strength"].default_value = 3
        K.MATS[name] = m


# ---------------------------------------------------------------- holo glyph textures
S = 256


def canvas():
    return np.zeros((S, S, 3))


def line(img, x0, y0, x1, y1, v=1.0, w=1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for t in np.linspace(0, 1, n):
        x, y = int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t))
        img[max(0, y - w // 2):y + w // 2 + 1, max(0, x - w // 2):x + w // 2 + 1] = v   # row 0 = bottom (Blender)


def rect(img, x0, y0, x1, y1, v=1.0, fill=None):
    if fill is not None:
        img[y0:y1 + 1, x0:x1 + 1] = fill
    for a, b, c, d in ((x0, y0, x1, y0), (x0, y1, x1, y1), (x0, y0, x0, y1), (x1, y0, x1, y1)):
        line(img, a, b, c, d, v)


def circle(img, cx, cy, r, v=1.0, seg=40):
    pts = [(cx + r * math.cos(2 * math.pi * k / seg), cy + r * math.sin(2 * math.pi * k / seg)) for k in range(seg + 1)]
    for p, q in zip(pts, pts[1:]):
        line(img, p[0], p[1], q[0], q[1], v)


def text(img, s, x, y_top, scale=2, v=1.0):
    K.blit_text(img, s, x, y_top, scale, np.array([v, v, v]))


def frame(img):
    rect(img, 2, 2, S - 3, S - 3, .7)
    for (x, y, dx, dy) in ((2, 2, 1, 1), (S - 3, 2, -1, 1), (2, S - 3, 1, -1), (S - 3, S - 3, -1, -1)):
        line(img, x, y, x + dx * 22, y, 1, 3)
        line(img, x, y, x, y + dy * 22, 1, 3)


def holo_textures():
    rng = np.random.default_rng(77)
    out = {}
    # limb schematic film: front-view figure, right forearm flagged (inverted to dark-on-light by the film material)
    img = canvas(); frame(img)
    text(img, "SCAN 07 // LIMB-R", 12, 10, 2)
    cx = 110
    circle(img, cx, 196, 16)
    line(img, cx, 180, cx, 172)
    rect(img, cx - 26, 104, cx + 26, 172, .8)
    for y in range(110, 170, 8):
        line(img, cx - 22, y, cx + 22, y, .25)
    line(img, cx - 26, 168, cx - 50, 120, .9, 2); line(img, cx - 50, 120, cx - 56, 78, .9, 2)
    line(img, cx + 26, 168, cx + 50, 120, 1, 3); line(img, cx + 50, 120, cx + 58, 76, 1, 3)
    for y in range(80, 120, 6):
        line(img, cx + 44, y, cx + 62, y, .55)
    line(img, cx - 14, 104, cx - 18, 30, .9, 2); line(img, cx + 14, 104, cx + 18, 30, .9, 2)
    rect(img, cx + 36, 68, cx + 72, 126, 1)
    line(img, cx + 72, 98, 190, 140, .8)
    text(img, "FAULT", 182, S - 172, 2)
    text(img, "SERVO4", 178, S - 152, 2)
    for i in range(6):
        w = int(rng.integers(20, 60))
        rect(img, 170, 20 + i * 12, 170 + w, 26 + i * 12, .5, fill=.35)
    out["T_Film_Body"] = 1.0 - img * .88    # a film: dark linework on the light the lightbox shines through
    for name, data in out.items():
        K.save_png(name, data)
    return out


# ---------------------------------------------------------------- geometry helpers
def text_mesh(body, font_path, size, location, rot_euler, mat, name, parent):
    """Neon tube lettering: the glyph outlines swept as round tubes (text curve, no fill, bevel)."""
    curve = bpy.data.curves.new(name + "_curve", "FONT")
    curve.body = body
    curve.font = bpy.data.fonts.load(font_path, check_existing=True)
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.fill_mode = "NONE"
    curve.bevel_depth = size * .018
    curve.bevel_resolution = 1
    curve.resolution_u = 2
    ob = bpy.data.objects.new(name + "_tmp", curve)
    SCENE.collection.objects.link(ob)
    ob.location = location
    ob.rotation_euler = rot_euler
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(ob.matrix_world)
    for f in bm.faces:
        f.material_index = 0
    bpy.data.objects.remove(ob)
    return K.new_obj(name, bm, [mat], origin=tuple(Vector(location)), parent=parent, uv=None, role="neon")


def quad(name, centre, width, height, normal_angle_deg, tilt_deg, mat, parent):
    """Upright quad facing heading `normal_angle_deg` (0 = +X, 90 = +Y), tilted back by `tilt_deg`, UV 0..1."""
    bm = K.bm_new()
    w, h = width / 2, height / 2
    vs = [bm.verts.new(p) for p in ((-w, 0, -h), (w, 0, -h), (w, 0, h), (-w, 0, h))]
    f = bm.faces.new(vs)
    uvl = bm.loops.layers.uv.verify()
    for l, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        l[uvl].uv = uv
    m = Matrix.Rotation(math.radians(normal_angle_deg - 90 + 180), 3, "Z") @ Matrix.Rotation(math.radians(-tilt_deg), 3, "X")
    for v in bm.verts:
        v.co = m @ v.co + Vector(centre)
    return K.new_obj(name, bm, [mat], origin=tuple(centre), parent=parent, uv=None, role="holo")


def holo_panel(name, centre, width, height, facing, tilt, mat, parent):
    panel = quad(name, centre, width, height, facing, tilt, mat, parent)
    bm = K.bm_new()   # thin bright frame corners just in front of the panel
    m = Matrix.Rotation(math.radians(facing - 90 + 180), 3, "Z") @ Matrix.Rotation(math.radians(-tilt), 3, "X")
    w, h, t = width / 2 + .006, height / 2 + .006, .004
    for sx in (-1, 1):
        for sz in (-1, 1):
            for (a, b) in (((sx * w, -t, sz * h), (sx * (w - .05), t, sz * (h - t * 2))), ((sx * w, -t, sz * h), (sx * (w - t * 2), t, sz * (h - .04)))):
                lo = Vector((min(a[0], b[0]), min(a[1], b[1]), min(a[2], b[2])))
                hi = Vector((max(a[0], b[0]), max(a[1], b[1]), max(a[2], b[2])))
                vs = K.add_box(bm, lo, hi, 0)
                for v in vs:
                    v.co = m @ v.co + Vector(centre)
    K.new_obj(name + "_Frame", bm, ["M_Holo_Frame"], origin=tuple(centre), parent=panel, uv=None, role="holo")
    return panel


# ---------------------------------------------------------------- room kit (Unity world)
def room_kit():
    root = K.empty("CyberRoomKit", (0, 0, 0))
    K.set_root(root)
    for n, p in (("Ref_Origin", (0, 0, 0)), ("Ref_Head", (0, 1, 0)), ("Ref_Up", (0, 0, 1))):
        K.empty(n, p, root)
    # Over the south door (inner wall face y = -3.40): face +Y, read from inside (advance toward -X).
    face_in = (math.radians(90), 0, math.radians(180))
    plate = K.bm_new()
    K.add_box(plate, (-0.80, -3.395, 2.36), (0.80, -3.375, 2.80), 0, bevel=.006)
    for x in (-0.74, 0.74):
        for z in (2.40, 2.76):
            K.add_cyl(plate, (x, -3.372, z), .008, .01, "Y", 8, 0)
    K.new_obj("CY_SignPlate_Main", plate, ["M_GT_DarkSteel"], origin=(0, -3.385, 2.58))
    # one tube per character (advance toward -X as seen from inside); the third, "维", has a failing transformer
    for i, ch in enumerate("义体维修"):
        text_mesh(ch, FONT_CJK, .30, (0.465 - i * .31, -3.355, 2.58), face_in,
                  "M_Neon_MagentaFaulty" if ch == "维" else "M_Neon_Magenta", "CY_Sign_Main_%d" % i, root)
    plate = K.bm_new()
    K.add_box(plate, (-1.22, -3.395, 2.44), (-0.90, -3.375, 2.66), 0, bevel=.004)
    K.new_obj("CY_SignPlate_24H", plate, ["M_GT_DarkSteel"], origin=(-1.06, -3.385, 2.55))
    text_mesh("24H", FONT_LATIN, .15, (-1.06, -3.358, 2.55), face_in, "M_Neon_Cyan", "CY_Sign_24H", root)
    # Vertical "收件处" on the SE wall, 30 % along from its south end; inward normal (-1, +1)/sqrt2.
    a, b = Vector((1.408, -3.40)), Vector((3.40, -1.408))
    p = a.lerp(b, .30)
    n = Vector((-1, 1)).normalized()
    heading = math.degrees(math.atan2(n.y, n.x))          # 135 deg
    rot = (math.radians(90), 0, math.radians(heading - 90))
    base = p + n * .045
    plate = K.bm_new()
    vs = K.add_box(plate, (-0.13, -0.012, 1.42), (0.13, 0.012, 2.42), 0, bevel=.006)
    for v in plate.verts:
        v.co = Matrix.Rotation(math.radians(heading - 90), 3, "Z") @ v.co + Vector((p.x + n.x * .02, p.y + n.y * .02, 0))
    K.new_obj("CY_SignPlate_Receive", plate, ["M_GT_DarkSteel"], origin=(p.x, p.y, 1.92))
    for i, ch in enumerate("收件处"):
        text_mesh(ch, FONT_CJK, .24, (base.x, base.y, 2.20 - i * .30), rot, "M_Neon_Orange", "CY_Sign_Receive_%d" % i, root)
    # Two hot tube lights in dark housings, hung on wires over the trade counter (x 1.62..2.18, z -1..0.95).
    tubes, housing = K.bm_new(), K.bm_new()
    for x in (1.78, 2.04):
        K.add_cyl(tubes, (x, 0.0, 2.52), .016, 1.30, "Y", 10, 0)
        K.add_box(housing, (x - .035, -0.68, 2.54), (x + .035, 0.68, 2.58), 0, bevel=.004)
        for y in (-0.6, 0.6):
            K.add_cyl(housing, (x, y, 2.79), .002, .42, "Z", 4, 0)
    K.new_obj("CY_CounterTubes", tubes, ["M_Neon_Orange"], origin=(1.91, 0, 2.52), role="neon")
    K.new_obj("CY_CounterHousing", housing, ["M_GT_DarkSteel"], origin=(1.91, 0, 2.56))
    return root


# ---------------------------------------------------------------- surgery kit (bed frame)
def surgery_kit():
    root = K.empty("CyberSurgeryKit", (0, 0, 0))
    K.set_root(root)
    for n, p in (("Ref_Origin", (0, 0, 0)), ("Ref_Head", (0, 1, 0)), ("Ref_Up", (0, 0, 1))):
        K.empty(n, p, root)
    bm = K.bm_new()   # cyan neon ring tucked under the gantry ring's inner flange
    pts = [(.93 * math.cos(a), .93 * math.sin(a), RING_LO - .018) for a in np.linspace(0, 2 * math.pi, 97)]
    K.add_sweep(bm, pts, .007, 6, 0)
    K.new_obj("CY_RingNeon", bm, ["M_Neon_CyanDim"], origin=(0, 0, RING_LO), role="neon")
    amber, red = K.bm_new(), K.bm_new()
    for k in range(40):   # indicator specks on the ring's outer face
        a = 2 * math.pi * (k + .5) / 40
        target = red if k % 5 == 0 else amber
        vs = K.add_box(target, (1.066, -.006, 2.475), (1.072, .006, 2.485), 0)
        for v in vs:
            v.co = Matrix.Rotation(a, 3, "Z") @ v.co
    K.new_obj("CY_RingSpecks_Amber", amber, ["M_Neon_Amber"], origin=(0, 0, 2.48), role="neon")
    K.new_obj("CY_RingSpecks_Red", red, ["M_Neon_Red"], origin=(0, 0, 2.48), role="neon")
    # PVC strip curtain skirt at r 1.13 (outside every arm carriage), 2.02-2.38 m: above head height on all aisles.
    rail, strips = K.bm_new(), K.bm_new()
    K.add_sweep(rail, [(1.13 * math.cos(a), 1.13 * math.sin(a), 2.385) for a in np.linspace(0, 2 * math.pi, 73)], .008, 6, 0)
    rng = np.random.default_rng(5)
    n = 38
    for k in range(n):
        a = 2 * math.pi * k / n + rng.normal(0, .01)
        bottom = 2.02 + rng.uniform(0, .05)
        hang = rng.normal(0, .015)
        for i in range(3):   # three columns of verts per strip, gentle fold
            pass
        w = .105
        p0 = Vector((1.13 * math.cos(a), 1.13 * math.sin(a), 0))
        tangent = Vector((-math.sin(a), math.cos(a), 0))
        radial = Vector((math.cos(a), math.sin(a), 0))
        grid = []
        for row, z in enumerate(np.linspace(2.375, bottom, 4)):
            sway = radial * (hang * (2.375 - z) / .35)
            grid.append([strips.verts.new(p0 + tangent * (u * w) + radial * (.008 * math.sin(u * 3.1 + k)) + sway + Vector((0, 0, z)))
                         for u in (-1, 0, 1)])
        uvl = strips.loops.layers.uv.verify()
        for r in range(3):
            for c in range(2):
                f = strips.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
                for l, uv in zip(f.loops, ((c / 2, 1 - r / 3), ((c + 1) / 2, 1 - r / 3), ((c + 1) / 2, 1 - (r + 1) / 3), (c / 2, 1 - (r + 1) / 3))):
                    l[uvl].uv = uv
    K.new_obj("CY_CurtainRail", rail, ["M_GT_DarkSteel"], origin=(0, 0, 2.385))
    K.new_obj("CY_Curtain", strips, ["M_Curtain"], origin=(0, 0, 2.2), uv=None, role="curtain")
    # Film viewer: a steel lightbox hung on two rods from the gantry ring at the head end, the limb schematic clipped
    # on as a backlit film, facing the table (-Y in the bed frame).
    cx, cy, cz, w, h = 0.0, 0.97, 1.62, .46, .32
    bm = K.bm_new()
    K.add_box(bm, (cx - w / 2 - .02, cy, cz - h / 2 - .02), (cx + w / 2 + .02, cy + .05, cz + h / 2 + .02), 0, bevel=.006)
    for sx in (-1, 1):
        K.add_cyl(bm, (cx + sx * .19, cy + .025, (cz + h / 2 + .02 + 2.40) / 2), .005, 2.40 - (cz + h / 2 + .02), "Z", 8, 0)
        K.add_box(bm, (cx + sx * .12 - .015, cy - .006, cz + h / 2 - .01), (cx + sx * .12 + .015, cy + .002, cz + h / 2 + .022), 0)  # film clips
    K.new_obj("CY_FilmViewer", bm, ["M_GT_DarkSteel"], origin=(cx, cy + .025, cz))
    quad("CY_FilmViewer_Film", (cx, cy - .002, cz), w, h, -90, 0, "M_Film", root)
    return root


# ---------------------------------------------------------------- bench kit (bench frame)
def bench_kit():
    root = K.empty("CyberBenchKit", (0, 0, 0))
    K.set_root(root)
    K.empty("Ref_BenchTopOrigin", (0, 0.55, 0.90), root)
    # (holo panels removed: the bench's real test fixture and cartridge reader carry its readouts)
    return root


def export(root, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in K.OBJECTS:
        p = o
        while p.parent is not None:
            p = p.parent
        if p == root:
            o.select_set(True)
    path = os.path.join(EXPORT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)


build_materials()
holo_textures()
roots = {"CyberRoomKit": room_kit(), "CyberSurgeryKit": surgery_kit()}   # bench kit retired with the holograms
report = {}
for name, root in roots.items():
    export(root, name)
    tris = 0
    for o in K.OBJECTS:
        p = o
        while p.parent is not None:
            p = p.parent
        if p == root and o.type == "MESH":
            tris += sum(len(f.vertices) - 2 for f in o.data.polygons)
    report[name] = tris
print("CY triangles", report, "total", sum(report.values()))
with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "ClinicCyberKit.blend"))
print("CY_DONE")
