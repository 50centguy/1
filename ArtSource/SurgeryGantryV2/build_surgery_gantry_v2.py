"""Surgery gantry V2: the overhead tool ring over the central surgery table, replacing the primitive Gantry_* and
Tool_* meshes of UnifiedClinic_Environment. Same frame as SurgeryTableV2 (origin = floor under the table centre,
+Y = head end, X across, Z up) and the same Ref_Origin / Ref_Head / Ref_Up empties, so Unity aligns both alike.

Ring radius 1.0 m, underside 2.40 m (as Gantry_Ring), hangers to the 3.0 m ceiling. Six articulated arms sit where
the old tools hung (probe 0 deg, drill 60, gripper 120, scanner 180, cutter 240, suction 300; 0 deg = +X) and a
surgical lamp boom at the head (90 deg, offset outward of the arms). Each arm is authored in its parked-ready pose
(upper arm horizontal toward the centre, forearm hanging) as BGT_Arm_<tool>_Shoulder > _Upper > _Fore > _Spin, with
every pivot at its joint, so Unity can pose them. A control box with two buttons hangs on the table's left foot rail.

Run: blender -b --factory-startup --python build_surgery_gantry_v2.py -- <cc0_texture_dir> [--norender]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "RepairBenchV2"))
import bv2_kit as K  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
CC0 = ARGS[0] if ARGS else os.path.join(HERE, "cc0")
NO_RENDER = "--norender" in ARGS
TEX = os.path.join(HERE, "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
for d in (EXPORT, RENDERS):
    os.makedirs(d, exist_ok=True)
R, RING_LO, RING_HI, CEIL = 1.0, 2.40, 2.50, 3.0
SHOULDER_Z, UPPER_L, FORE_L = 2.27, 0.48, 0.45
ARMS = [("Probe", 0), ("Drill", 60), ("Gripper", 120), ("Scanner", 180), ("Cutter", 240), ("Suction", 300)]

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene
MAT_SPEC = {
    "M_BV2_SteelGreen": ("T_BV2_SteelGreen", .35, .32, None, 2.2),
    "M_BV2_SteelBare": ("T_BV2_SteelBare", .9, .48, None, 3.0),
    "M_BV2_Ivory": ("T_BV2_Ivory", 0, .25, None, 3.0),
    "M_BV2_WornRed": ("T_BV2_WornRed", .2, .35, None, 3.0),
    "M_BV2_Rubber": ("T_BV2_Rubber", 0, .15, None, 6.0),
    "M_BV2_DarkPlastic": ("T_BV2_Rubber", 0, .4, None, 4.0),
    "M_BV2_Yellow": ("T_BV2_Yellow", 0, .3, None, 5.0),
    "M_BV2_LedGreen": (None, 0, .6, (.2, 1.0, .35), 1.0),
    "M_BV2_LedRed": (None, 0, .6, (1.0, .15, .1), 1.0),
    "M_BV2_Bulb": (None, 0, .6, (1.0, .78, .5), 1.0),
    "M_ST_Hazard": ("T_ST_Hazard", 0, .3, None, 6.0),
    "M_GT_Lens": (None, 0, .9, (.2, .9, 1.0), 1.0),
    "M_GT_DarkSteel": ("T_GT_DarkSteel", .6, .35, None, 2.5),
}
FLAT = {"M_BV2_LedGreen": (.2, .9, .3), "M_BV2_LedRed": (.9, .1, .1), "M_BV2_Bulb": (1, .92, .75), "M_GT_Lens": (.3, .8, .9)}
K.init(SCENE, CC0, TEX, MAT_SPEC)
B, A = K.add_box, K.add_cyl


def build_textures():
    S = 256
    steel = K.down(K.load_rgb("Metal032"), S)
    sl = K.lum(steel)
    steel_rgb = np.stack([sl * .92, sl * .95, sl * .97], -1)
    grain = K.down(K.load_rgb("Plastic010"), S)
    chips_a = K.chips_from("PaintedMetal004", S)
    chips_b = K.chips_from("PaintedMetal002", S)
    t = {}
    t["T_BV2_SteelGreen"] = K.painted("T_BV2_SteelGreen", K.hexc("#46564F"), np.clip(chips_a * .9, 0, 1), steel_rgb * .9, grain, S)
    t["T_BV2_SteelBare"] = K.save_png("T_BV2_SteelBare", steel_rgb * 1.05)
    t["T_BV2_Ivory"] = K.painted("T_BV2_Ivory", K.hexc("#B8AC8A"), np.clip(chips_b * .35, 0, 1), K.hexc("#8E8670"), grain, S, grime=.35)
    t["T_BV2_WornRed"] = K.painted("T_BV2_WornRed", K.hexc("#8E2A22"), np.clip(chips_a, 0, 1), steel_rgb, grain, S)
    t["T_BV2_Yellow"] = K.painted("T_BV2_Yellow", K.hexc("#D6B03A"), np.clip(chips_b * .5, 0, 1), K.hexc("#3A3A36"), grain, S)
    rl = K.lum(K.down(K.load_rgb("Rubber004"), 128))
    t["T_BV2_Rubber"] = K.save_png("T_BV2_Rubber", np.stack([rl, rl, rl], -1) * .35 + .03)
    yy, xx = np.mgrid[0:64, 0:64]
    stripe = ((xx + yy) // 16) % 2 == 0
    t["T_ST_Hazard"] = K.save_png("T_ST_Hazard", np.where(stripe[..., None], K.hexc("#D9B13A"), K.hexc("#1C1C1A")) * (1 - .25 * K.down(chips_a[..., None].repeat(3, -1), 64)))
    t["T_GT_DarkSteel"] = K.painted("T_GT_DarkSteel", K.hexc("#2E3331"), np.clip(chips_a * .7, 0, 1), steel_rgb * .8, grain, S, grime=.3)
    return t


def build_materials(textures):
    for name, (tex, metal, smooth, emis, _) in MAT_SPEC.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = 1 - smooth
        if tex:
            node = m.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = textures[tex]
            node.interpolation = "Closest"
            m.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (*FLAT.get(name, (.5, .5, .5)), 1)
        if emis:
            bsdf.inputs["Emission Color"].default_value = (*emis, 1)
            bsdf.inputs["Emission Strength"].default_value = 2.0
        K.MATS[name] = m


def place(verts, angle_deg, offset):
    """Local frame (+X = toward the ring centre) -> world: turn about Z, then move."""
    m = Matrix.Rotation(math.radians(angle_deg), 3, "Z")
    for v in verts:
        v.co = m @ v.co + Vector(offset)


def ring():
    bm = K.bm_new()
    n = 72
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        half = R * math.tan((a1 - a0) / 2) * 1.02
        before = set(bm.verts)
        B(bm, (R - .065, -half, RING_LO), (R + .065, half, RING_HI), 0)                     # box section
        B(bm, (R - .05, -half, RING_LO - .012), (R + .05, half, RING_LO), 1)                # lower flange
        B(bm, (R - .04, -half, RING_HI), (R + .04, half, RING_HI + .035), 2)                # cable tray
        if i % 6 == 0:
            B(bm, (R - .02, -.012, RING_LO - .02), (R + .02, .012, RING_LO - .012), 3)       # LED under the ring
        place([v for v in bm.verts if v not in before], math.degrees((a0 + a1) / 2), (0, 0, 0))
    K.new_obj("BGT_Ring", bm, ["M_GT_DarkSteel", "M_BV2_SteelBare", "M_BV2_SteelGreen", "M_BV2_LedGreen"], origin=(0, 0, RING_LO))
    bm = K.bm_new()                                                                       # hazard band on the outer face
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        half = R * math.tan((a1 - a0) / 2) * 1.02
        before = set(bm.verts)
        B(bm, (R + .065, -half, RING_LO + .03), (R + .068, half, RING_LO + .07), 0)
        place([v for v in bm.verts if v not in before], math.degrees((a0 + a1) / 2), (0, 0, 0))
    K.new_obj("BGT_RingHazard", bm, ["M_ST_Hazard"], origin=(0, 0, RING_LO))
    bm = K.bm_new()                                                                       # cables in the tray
    for k, (r, ph) in enumerate(((R - .022, 0), (R, 1.3), (R + .022, 2.1))):
        pts = [(r * math.cos(a), r * math.sin(a), RING_HI + .045 + .01 * math.sin(a * 9 + ph)) for a in np.linspace(0, 2 * math.pi, 145)]
        K.add_sweep(bm, pts, .011, 6, k % 2)
    K.new_obj("BGT_RingCables", bm, ["M_BV2_Rubber", "M_BV2_WornRed"], origin=(0, 0, RING_HI))
    bm = K.bm_new()                                                                       # hangers to the ceiling
    for a in (45, 135, 225, 315):
        before = set(bm.verts)
        B(bm, (R - .03, -.03, RING_HI + .03), (R + .03, .03, CEIL - .02), 0, bevel=.004)
        B(bm, (R - .09, -.09, CEIL - .02), (R + .09, .09, CEIL), 1, bevel=.003)
        B(bm, (R - .05, -.05, RING_HI), (R + .05, .05, RING_HI + .06), 1, bevel=.004)
        for dx in (-.06, .06):
            for dy in (-.06, .06):
                A(bm, (R + dx, dy, CEIL - .025), .008, .01, "Z", 8, 1)
        place([v for v in bm.verts if v not in before], a, (0, 0, 0))
    K.new_obj("BGT_Hangers", bm, ["M_GT_DarkSteel", "M_BV2_SteelBare"], origin=(0, 0, CEIL))


def carriage(bm, angle, tag):
    before = set(bm.verts)
    B(bm, (R - .10, -.11, RING_LO - .10), (R + .10, .11, RING_LO - .012), 0, bevel=.012, segs=2)   # body under the ring
    for sy in (-.08, .08):                                                                 # rollers on the ring sides
        for sx in (-1, 1):
            A(bm, (R + sx * .075, sy, RING_LO + .03), .022, .02, "X", 12, 1)
    B(bm, (R + .095, -.04, RING_LO - .08), (R + .101, .04, RING_LO - .04), 2)            # status window
    B(bm, (R + .1, -.03, RING_LO - .03), (R + .102, -.015, RING_LO - .02), 3)
    place([v for v in bm.verts if v not in before], angle, (0, 0, 0))


def arm(tool, angle):
    """One articulated arm. Local frame: shoulder at origin, +X toward the ring centre (the arm's reach)."""
    radial = Vector((math.cos(math.radians(angle)), math.sin(math.radians(angle)), 0))
    shoulder = radial * R + Vector((0, 0, SHOULDER_Z))
    turn = angle + 180   # local +X -> inward

    def world(p):
        return Matrix.Rotation(math.radians(turn), 3, "Z") @ Vector(p) + shoulder

    bm = K.bm_new()
    carriage(bm, angle, tool)
    K.new_obj("BGT_Carriage_" + tool, bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_DarkPlastic", "M_BV2_LedGreen"],
              origin=tuple(radial * R + Vector((0, 0, RING_LO - .05))))

    bm = K.bm_new()                                                                        # shoulder: hub hanging from the carriage
    A(bm, (0, 0, .055), .03, .05, "Z", 14, 1)
    A(bm, (0, 0, 0), .05, .09, "Y", 18, 0, bevel=.004)
    place(list(bm.verts), turn, shoulder)
    sh = K.new_obj("BGT_Arm_%s_Shoulder" % tool, bm, ["M_GT_DarkSteel", "M_BV2_SteelBare"], origin=tuple(shoulder), role="joint",
                   note="pitch about the horizontal axis across the arm")
    bm = K.bm_new()                                                                        # upper arm with elbow hub and a cable
    B(bm, (.04, -.034, -.042), (UPPER_L - .03, .034, .042), 1, bevel=.012, segs=2)          # dark steel body
    for s in (-1, 1):
        B(bm, (.10, s * .0345 - .001, -.025), (UPPER_L - .09, s * .0345 + .001, .025), 0)   # worn ivory side covers
    A(bm, (UPPER_L, 0, 0), .043, .085, "Y", 18, 1, bevel=.004)
    K.add_sweep(bm, K.bezier((.03, .04, .05), (.15, .05, .08), (.28, .05, .07), (UPPER_L - .02, .045, .04), 10), .007, 6, 3)
    place(list(bm.verts), turn, shoulder)
    up = K.new_obj("BGT_Arm_%s_Upper" % tool, bm, ["M_BV2_Ivory", "M_GT_DarkSteel", "M_BV2_DarkPlastic", "M_BV2_Rubber"],
                   origin=tuple(shoulder), parent=sh, role="joint")
    elbow = world((UPPER_L, 0, 0))
    bm = K.bm_new()                                                                        # forearm hanging from the elbow
    B(bm, (UPPER_L - .03, -.03, -FORE_L + .03), (UPPER_L + .03, .03, -.035), 1, bevel=.01, segs=2)   # dark steel forearm
    A(bm, (UPPER_L, 0, -FORE_L), .034, .07, "Y", 16, 1, bevel=.003)
    A(bm, (UPPER_L, 0, -FORE_L - .05), .03, .05, "Z", 14, 1)
    tool_top = -FORE_L - .075
    spin_center = None
    mats = ["M_BV2_Ivory", "M_GT_DarkSteel", "M_BV2_SteelBare", "M_BV2_Yellow", "M_GT_Lens", "M_BV2_Rubber", "M_BV2_WornRed"]
    if tool == "Cutter":
        B(bm, (UPPER_L - .02, -.05, tool_top - .09), (UPPER_L + .02, .05, tool_top), 3, bevel=.006)
        spin_center = (UPPER_L, 0, tool_top - .06)
    elif tool == "Drill":
        A(bm, (UPPER_L, 0, tool_top - .03), .028, .06, "Z", 14, 3, bevel=.003)
        A(bm, (UPPER_L, 0, tool_top - .075), .016, .03, "Z", 12, 1)
        spin_center = (UPPER_L, 0, tool_top - .09)
    elif tool == "Gripper":
        A(bm, (UPPER_L, 0, tool_top - .02), .03, .04, "Z", 14, 1)
        for k in range(3):
            a = 2 * math.pi * k / 3
            c, s = math.cos(a), math.sin(a)
            K.add_sweep(bm, [(UPPER_L + c * .02, s * .02, tool_top - .04), (UPPER_L + c * .045, s * .045, tool_top - .09),
                             (UPPER_L + c * .025, s * .025, tool_top - .13)], .006, 6, 2)
    elif tool == "Probe":
        A(bm, (UPPER_L, 0, tool_top - .03), .02, .06, "Z", 12, 3)
        A(bm, (UPPER_L, 0, tool_top - .12), .004, .12, "Z", 8, 2, r2=.001)
        for z in (tool_top - .07, tool_top - .09):
            A(bm, (UPPER_L, 0, z), .008, .004, "Z", 10, 6)
    elif tool == "Scanner":
        B(bm, (UPPER_L - .06, -.045, tool_top - .06), (UPPER_L + .06, .045, tool_top), 0, bevel=.01, segs=2)
        B(bm, (UPPER_L - .045, -.032, tool_top - .062), (UPPER_L + .045, .032, tool_top - .058), 4)
    elif tool == "Suction":
        A(bm, (UPPER_L, 0, tool_top - .08), .012, .16, "Z", 10, 2)
        A(bm, (UPPER_L, 0, tool_top - .005), .02, .03, "Z", 12, 1)
        pts = K.bezier((UPPER_L + .01, .0, tool_top - .02), (UPPER_L + .15, .06, tool_top + .05), (.15, .1, .05), (.05, .06, .05), 14)
        for i, p in enumerate(pts[:-1]):
            A(bm, p, .016 if i % 2 else .013, .02, "Z", 10, 5)
        K.add_sweep(bm, pts, .012, 8, 5)
    place(list(bm.verts), turn, shoulder)
    fo = K.new_obj("BGT_Arm_%s_Fore" % tool, bm, mats, origin=tuple(elbow), parent=up, role="joint",
                   note="pitch about the elbow; authored hanging straight down")
    if spin_center:
        bm = K.bm_new()
        if tool == "Cutter":
            A(bm, spin_center, .045, .004, "Y", 28, 0)
            for k in range(14):
                a = 2 * math.pi * k / 14
                B(bm, (spin_center[0] + math.cos(a) * .045 - .003, -.002, spin_center[2] + math.sin(a) * .045 - .003),
                  (spin_center[0] + math.cos(a) * .045 + .003, .002, spin_center[2] + math.sin(a) * .045 + .003), 0)
            axis_note = "spins about the arm's cross axis"
        else:
            A(bm, (spin_center[0], 0, spin_center[2] - .04), .004, .09, "Z", 8, 0, r2=.0015)
            for k in range(4):
                z = spin_center[2] - .01 - k * .018
                A(bm, (spin_center[0], 0, z), .0055, .006, "Z", 8, 0)
            axis_note = "spins about vertical (the forearm axis)"
        place(list(bm.verts), turn, shoulder)
        K.new_obj("BGT_Arm_%s_Spin" % tool, bm, ["M_BV2_SteelBare"], origin=tuple(world(spin_center)), parent=fo, role="spin", note=axis_note)


def lamp():
    """Surgical lamp on its own boom at the head end, outward of the arms: five LED cups in a ring housing."""
    angle = 90
    radial = Vector((0, 1, 0))
    root = radial * R + Vector((0, 0, RING_LO - .05))
    bm = K.bm_new()
    carriage(bm, angle, "Lamp")
    K.new_obj("BGT_Carriage_Lamp", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_DarkPlastic", "M_BV2_LedGreen"], origin=tuple(root))
    head = Vector((0, 0.42, 1.98))
    bm = K.bm_new()
    elbow = Vector((0, 0.85, 2.18))
    K.add_sweep(bm, [tuple(root - Vector((0, 0, .06))), tuple(elbow)], .022, 12, 0)
    K.add_sweep(bm, [tuple(elbow), tuple(head + Vector((0, 0, .12)))], .018, 12, 0)
    A(bm, tuple(elbow), .03, .07, "X", 14, 1)
    A(bm, tuple(head + Vector((0, 0, .12))), .026, .06, "X", 14, 1)
    K.new_obj("BGT_LampBoom", bm, ["M_BV2_Ivory", "M_GT_DarkSteel"], origin=tuple(root))
    bm = K.bm_new()
    K.add_lathe(bm, [(0.0, .10), (0.05, .10), (0.17, .06), (0.23, .02), (0.235, 0.0), (0.0, 0.0)], tuple(head), "Z", 32, 0)
    for k in range(5):
        a = 2 * math.pi * k / 5
        c = head + Vector((math.cos(a) * .135, math.sin(a) * .135, -.004))
        K.add_lathe(bm, [(0.0, 0.0), (0.045, 0.0), (0.05, 0.012), (0.0, 0.012)], tuple(c), "Z", 16, 1)
        A(bm, tuple(c - Vector((0, 0, .002))), .03, .004, "Z", 14, 2)
    A(bm, tuple(head - Vector((0, 0, .004))), .04, .006, "Z", 16, 2)
    K.add_sweep(bm, [tuple(head + Vector((.20, 0, .03))), tuple(head + Vector((.27, 0, .0))), tuple(head + Vector((.27, 0, -.06)))], .012, 8, 3)
    K.new_obj("BGT_LampHead", bm, ["M_BV2_Ivory", "M_GT_DarkSteel", "M_BV2_Bulb", "M_BV2_Rubber"], origin=tuple(head), role="light_fixture",
              note="Unity adds a downward spot light here")


def control_box():
    """Gantry control box hanging on the table's left foot rail (x -0.335): arm deploy / park and lamp switch."""
    x, y = -0.352, -0.70
    bm = K.bm_new()
    B(bm, (x - .024, y - .05, .52), (x + .008, y + .05, .64), 0, bevel=.008, segs=2)
    B(bm, (x - .026, y - .04, .60), (x - .024, y + .04, .625), 1)
    K.add_sweep(bm, [(x - .005, y, .64), (x - .005, y, .70), (-0.335, y, .755)], .004, 6, 2)
    K.new_obj("BGT_Control", bm, ["M_BV2_Ivory", "M_GT_Lens", "M_BV2_Rubber"], origin=(x, y, .58))
    for name, z, mat in (("BGT_Btn_Arms", .575, "M_BV2_Yellow"), ("BGT_Btn_Lamp", .545, "M_BV2_WornRed")):
        bm = K.bm_new()
        B(bm, (x - .034, y - .03, z - .012), (x - .024, y + .03, z + .012), 0, bevel=.003, segs=1)
        K.new_obj(name, bm, [mat], origin=(x - .03, y, z), role="control")
        K.INTERACT[name] = {"kind": name}


def build():
    textures = build_textures()
    build_materials(textures)
    root = K.empty("SurgeryGantryV2", (0, 0, 0))
    K.set_root(root)
    K.empty("Ref_Origin", (0, 0, 0), root)
    K.empty("Ref_Head", (0, 1, 0), root)
    K.empty("Ref_Up", (0, 0, 1), root)
    ring()
    for tool, angle in ARMS:
        arm(tool, angle)
    lamp()
    control_box()


def export():
    bpy.ops.object.select_all(action="DESELECT")
    for o in K.OBJECTS:
        o.select_set(True)
    path = os.path.join(EXPORT, "SurgeryGantryV2.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)
    return path


def report(fbx):
    rows, total = [], 0
    for o in K.OBJECTS:
        if o.type != "MESH":
            continue
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        total += tris
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        rows.append({"name": o.name, "tris": tris, "min": [round(min(v[i] for v in bb), 3) for i in range(3)],
                     "max": [round(max(v[i] for v in bb), 3) for i in range(3)]})
    low = min(r["min"][2] for r in rows if not r["name"].startswith("BGT_Control") and not r["name"].startswith("BGT_Btn"))
    data = {"fbx": fbx, "triangles": total, "objects": rows, "interactive": K.INTERACT, "lowest_parked_point": low}
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("GT triangles", total, "objects", len(rows), "lowest overhead point", round(low, 3))


def render():
    bpy.context.view_layer.update()
    table = os.path.join(HERE, "..", "SurgeryTableV2", "Export", "SurgeryTableV2.fbx")
    if os.path.exists(table):   # context only, not exported
        bpy.ops.import_scene.fbx(filepath=table)
    SCENE.render.engine = "BLENDER_WORKBENCH"
    SCENE.display.shading.light = "STUDIO"
    SCENE.display.shading.color_type = "TEXTURE"
    SCENE.display.shading.show_shadows = True
    SCENE.display.shading.show_cavity = True
    SCENE.render.resolution_x, SCENE.render.resolution_y = 1600, 900
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    SCENE.collection.objects.link(cam)
    SCENE.camera = cam
    for name, (loc, target, lens) in {"gantry_threequarter": ((2.6, -2.8, 2.4), (0, 0, 1.6), 24), "gantry_low": ((1.4, -1.7, 1.2), (0, 0.1, 2.1), 22),
                                      "gantry_arm_detail": ((0.9, -0.4, 1.9), (0.62, 0.0, 2.05), 35)}.items():
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = lens
        SCENE.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)


build()
fbx = export()
report(fbx)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "SurgeryGantryV2.blend"))
if not NO_RENDER:
    render()
print("GT_DONE")
