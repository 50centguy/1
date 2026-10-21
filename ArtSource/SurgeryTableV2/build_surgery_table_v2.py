"""Surgery table V2 for the clinic centre: a heavy, mechanical prosthetic-surgery table replacing the five primitive
Bed_* meshes of UnifiedClinic_Environment (reference: clinic-ring-v2 04-central-prosthetic-surgery-v2.png).

Frame: Blender Z-up, origin = floor under the table centre, +Y = head end, X across. Footprint stays inside the old
bed and its COL_Bed proxy (x +-0.36, y +-0.95); pad top 0.85, head cushion 0.90, as BED_TOP / BED_TOP_MAX in
build_unified_clinic.py. Unity aligns the root by Ref_Origin (floor centre), Ref_Head (head direction) and Ref_Up.
Posable parts are separate objects with their hinge as pivot: BST_Pad_Back (child BST_Pad_Head), BST_Tray_Arm,
BST_Strap_n (closed/open children), BST_Pendant. Materials reuse the bench's M_BV2_* plus M_ST_Vinyl / M_ST_Hazard.

Run: blender -b --factory-startup --python build_surgery_table_v2.py -- <cc0_texture_dir> [--norender]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

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
PAD_LO, PAD_TOP, HEAD_TOP = 0.76, 0.85, 0.90

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
    "M_ST_Label": ("T_ST_Label", 0, .1, None, 1.0),
    "M_BV2_LedGreen": (None, 0, .6, (.2, 1.0, .35), 1.0),
    "M_BV2_LedRed": (None, 0, .6, (1.0, .15, .1), 1.0),
    "M_BV2_Brass": (None, .95, .55, None, 1.0),
    "M_ST_Vinyl": ("T_ST_Vinyl", 0, .22, None, 2.0),
    "M_ST_Hazard": ("T_ST_Hazard", 0, .3, None, 6.0),
    "M_ST_Bag": (None, 0, .8, None, 1.0),
    "M_ST_Screen": ("T_ST_Screen", 0, .8, (.2, 1.0, .5), 1.0),
}
FLAT = {"M_BV2_LedGreen": (.2, .9, .3), "M_BV2_LedRed": (.9, .1, .1), "M_BV2_Brass": (.7, .5, .22), "M_ST_Bag": (.85, .88, .82)}
K.init(SCENE, CC0, TEX, MAT_SPEC)


def build_textures():
    S = 256
    steel = K.down(K.load_rgb("Metal032"), S)
    sl = K.lum(steel)
    steel_rgb = np.stack([sl * .92, sl * .95, sl * .97], -1)
    grain = K.down(K.load_rgb("Plastic010"), S)
    chips_a = K.chips_from("PaintedMetal004", S)
    chips_b = K.chips_from("PaintedMetal002", S)
    t = {}
    # The bench's shared textures (same recipes, so previews match the Unity materials they are remapped to).
    t["T_BV2_SteelGreen"] = K.painted("T_BV2_SteelGreen", K.hexc("#46564F"), np.clip(chips_a * .9, 0, 1), steel_rgb * .9, grain, S)
    t["T_BV2_SteelBare"] = K.save_png("T_BV2_SteelBare", steel_rgb * 1.05)
    t["T_BV2_Ivory"] = K.painted("T_BV2_Ivory", K.hexc("#B8AC8A"), np.clip(chips_b * .35, 0, 1), K.hexc("#8E8670"), grain, S, grime=.35)
    t["T_BV2_WornRed"] = K.painted("T_BV2_WornRed", K.hexc("#8E2A22"), np.clip(chips_a, 0, 1), steel_rgb, grain, S)
    t["T_BV2_Yellow"] = K.painted("T_BV2_Yellow", K.hexc("#D6B03A"), np.clip(chips_b * .5, 0, 1), K.hexc("#3A3A36"), grain, S)
    rl = K.lum(K.down(K.load_rgb("Rubber004"), 128))
    t["T_BV2_Rubber"] = K.save_png("T_BV2_Rubber", np.stack([rl, rl, rl], -1) * .35 + .03)
    # Green surgical vinyl: worn sheen, quilting every 10 cm, darker crease lines, a few cracks taped over.
    rub = K.lum(K.down(K.load_rgb("Rubber004"), S))
    v = np.ones((S, S, 3)) * K.hexc("#38503F") * (.9 + .25 * (rub - rub.mean()))[..., None]
    for k in range(0, S, S // 4):
        v[k:k + 2, :] *= .72
        v[:, k:k + 2] *= .72
    v *= (1 - .18 * np.clip(chips_b * 1.5, 0, 1))[..., None]
    for (y0, x0, w) in ((40, 30, 60), (170, 120, 50)):
        v[y0:y0 + 10, x0:x0 + w] = K.hexc("#B9B39A")
    t["T_ST_Vinyl"] = K.save_png("T_ST_Vinyl", v)
    yy, xx = np.mgrid[0:64, 0:64]
    stripe = ((xx + yy) // 16) % 2 == 0
    hz = np.where(stripe[..., None], K.hexc("#D9B13A"), K.hexc("#1C1C1A")) * (1 - .25 * K.down(chips_a[..., None].repeat(3, -1), 64))
    t["T_ST_Hazard"] = K.save_png("T_ST_Hazard", hz)
    lab = np.ones((64, 256, 3)) * K.hexc("#D8D2BC")
    K.blit_text(lab, "SURG-02 LIFT 180", 8, 10, 3, K.hexc("#202020"))
    K.blit_text(lab, "LOCK BEFORE USE", 8, 36, 2, K.hexc("#8E2A22"))
    t["T_ST_Label"] = K.save_png("T_ST_Label", lab)
    # green-phosphor patient / limb monitor screen: three traces and readouts on a dark glass
    import math as _m
    W2, H2 = 256, 192
    scr = np.ones((H2, W2, 3)) * K.hexc("#06140C")
    phosphor = K.hexc("#5CFF9A")
    rng = np.random.default_rng(11)
    for k, (base, amp, freq) in enumerate(((150, 16, .11), (100, 10, .05), (48, 18, .19))):
        prev = None
        for x in range(10, 176):
            y = base + amp * _m.sin(x * freq) * (1.8 if k == 0 and x % 44 < 5 else 1) + rng.normal(0, 1.0)
            if prev is not None:
                for t_ in np.linspace(0, 1, 4):
                    yy = int(prev + (y - prev) * t_)
                    scr[max(0, yy - 1):yy + 1, x] = phosphor
            prev = y
    K.blit_text(scr, "HR 072", 184, H2 - 160, 2, phosphor)
    K.blit_text(scr, "SPO2 98", 184, H2 - 110, 2, phosphor)
    K.blit_text(scr, "NRL OK", 184, H2 - 58, 2, phosphor)
    K.blit_text(scr, "LIMB-R", 10, 6, 2, phosphor * .7)
    scr *= (1 - .3 * ((np.arange(H2) % 3 == 0)[:, None]))[..., None]
    t["T_ST_Screen"] = K.save_png("T_ST_Screen", scr)
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


B, A = K.add_box, K.add_cyl


def chassis():
    bm = K.bm_new()
    B(bm, (-0.25, -0.72, 0.08), (0.25, 0.72, 0.135), 3, bevel=.006)                        # dark steel skirt
    B(bm, (-0.24, -0.70, 0.13), (0.24, 0.70, 0.22), 0, bevel=.012, segs=2)                  # ivory cover
    B(bm, (-0.20, -0.66, 0.215), (0.20, 0.66, 0.24), 0, bevel=.015, segs=2)                 # chamfered top
    for y0 in (-0.62, 0.30):                                                              # grille plates on the top
        B(bm, (-0.16, y0, .238), (0.16, y0 + .30, .243), 1, bevel=.002, segs=1)
        for i in range(8):
            B(bm, (-0.15, y0 + .02 + i * .034, .242), (0.15, y0 + .034 + i * .034, .245), 3)
    for sx in (-1, 1):                                                                    # bolts
        for y in (-0.66, -0.33, 0.0, 0.33, 0.66):
            A(bm, (sx * .232, y, .232), .006, .006, "Z", 8, 3)
    for s in (-1, 1):                                                                     # side vents
        for i in range(9):
            y = -0.55 + i * .06
            B(bm, (s * .242 - .003, y, .13), (s * .242 + .003, y + .03, .17), 1, bevel=.002, segs=1)
    for y in (-0.70, 0.70):                                                               # bumpers
        B(bm, (-0.22, y - .025 if y < 0 else y, .12), (0.22, y if y < 0 else y + .025, .16), 2, bevel=.01)
    K.new_obj("BST_Base", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_Rubber", "M_BV2_SteelGreen"], origin=(0, 0, .10))
    bm = K.bm_new()                                                                       # power unit on the right of the base
    B(bm, (0.10, -0.52, .24), (0.23, -0.30, .36), 0, bevel=.01, segs=2)
    K.add_lathe(bm, [(0.0, 0.0), (0.03, 0.0), (0.03, 0.012), (0.026, 0.016), (0.0, 0.016)], (0.231, -0.44, .30), "X", 18, 1)
    B(bm, (0.229, -0.36, .33), (0.233, -0.34, .345), 2)
    B(bm, (0.229, -0.36, .30), (0.233, -0.34, .315), 3)
    for i in range(5):
        B(bm, (0.12 + i * .02, -0.525, .26), (0.13 + i * .02, -0.52, .34), 1)
    K.add_sweep(bm, K.bezier((0.17, -0.30, .32), (0.17, -0.2, .36), (0.12, -0.1, .30), (0.12, -0.02, .28), 12), .008, 8, 1)
    K.new_obj("BST_PowerUnit", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_LedGreen", "M_BV2_LedRed"], origin=(0.165, -0.41, .24))
    bm = K.bm_new()                                                                       # lift pistons, head and foot pairs
    for y, lean in ((0.48, -1), (-0.48, 1)):
        for x in (-0.13, 0.13):
            bot, top = Vector((x, y, .245)), Vector((x, y + lean * .12, .695))
            mid = bot.lerp(top, .55)
            K.add_sweep(bm, [tuple(bot), tuple(mid)], .022, 12, 0)
            K.add_sweep(bm, [tuple(mid), tuple(top)], .011, 10, 1)
            B(bm, (x - .02, y - .02, .24), (x + .02, y + .02, .26), 2, bevel=.003)
            B(bm, (x - .016, top.y - .016, .685), (x + .016, top.y + .016, .70), 2, bevel=.003)
    K.new_obj("BST_Pistons", bm, ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic"], origin=(0, 0, .45))
    bm = K.bm_new()                                                                       # cable carrier chain, left side
    pts = K.bezier((-0.13, 0.20, .25), (-0.26, 0.20, .35), (-0.26, 0.10, .62), (-0.20, 0.0, .69), 18)
    for a, b in zip(pts[:-1], pts[1:]):
        c = (Vector(a) + Vector(b)) / 2
        B(bm, (c.x - .022, c.y - .025, c.z - .012), (c.x + .022, c.y + .025, c.z + .012), 0, bevel=.003, segs=1)
    K.new_obj("BST_CableChain", bm, ["M_BV2_DarkPlastic"], origin=(-0.2, 0.15, .45))
    bm = K.bm_new()
    for s in (-1, 1):
        B(bm, (s * .243 - .004, -0.66, .20), (s * .243 + .004, 0.66, .225), 0)
    K.new_obj("BST_BaseHazard", bm, ["M_ST_Hazard"], origin=(0, 0, .2))
    bm = K.bm_new()                                                                       # casters + lock pedals
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * .20, sy * .60
            B(bm, (x - .035, y - .035, .095), (x + .035, y + .035, .105), 0, bevel=.003)
            B(bm, (x - .028, y - .01, .045), (x - .02, y + .03, .095), 0)
            B(bm, (x + .02, y - .01, .045), (x + .028, y + .03, .095), 0)
            A(bm, (x, y + .01, .05), .048, .036, "X", 18, 1, bevel=.004)
            A(bm, (x, y + .01, .05), .016, .046, "X", 10, 0)
            B(bm, (x + sx * .03, y - .05, .07), (x + sx * .075, y - .03, .085), 2, bevel=.003)
    K.new_obj("BST_Casters", bm, ["M_BV2_SteelBare", "M_BV2_Rubber", "M_BV2_WornRed"], origin=(0, 0, 0))
    bm = K.bm_new()                                                                       # foot-end label plate
    B(bm, (-0.10, -0.7265, .15), (0.10, -0.7255, .19), 0)
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            p = l.vert.co
            l[uvl].uv = ((p.x + .10) / .20, (p.z - .15) / .04)
    K.new_obj("BST_LabelPlate", bm, ["M_ST_Label"], origin=(0, -0.726, .17), uv=None)


def column():
    bm = K.bm_new()
    B(bm, (-0.15, -0.20, 0.24), (0.15, 0.20, 0.48), 0, bevel=.012)
    for i in range(5):                                                                    # bellows
        z = .48 + i * .025
        B(bm, (-0.105 + (i % 2) * .008, -0.135 + (i % 2) * .008, z), (0.105 - (i % 2) * .008, 0.135 - (i % 2) * .008, z + .025), 1, bevel=.004, segs=1)
    B(bm, (-0.09, -0.12, .605), (0.09, 0.12, .70), 2, bevel=.006)
    B(bm, (-0.125, -0.02, .30), (-0.121, 0.10, .42), 3)                                   # service hatch
    for z in (.32, .40):
        A(bm, (-0.126, 0.08, z), .006, .004, "X", 8, 2)
    K.new_obj("BST_Column", bm, ["M_BV2_Ivory", "M_BV2_Rubber", "M_BV2_SteelBare", "M_BV2_DarkPlastic"], origin=(0, 0, .24))
    bm = K.bm_new()                                                                       # hydraulic hoses
    for s in (-1, 1):
        K.add_sweep(bm, K.bezier((s * .13, .05, .30), (s * .20, .12, .40), (s * .22, .25, .60), (s * .20, .30, .72), 14), .009, 8, 0)
        K.add_sweep(bm, K.bezier((s * .13, -.06, .28), (s * .19, -.15, .45), (s * .21, -.30, .62), (s * .18, -.40, .72), 14), .007, 8, 0)
        A(bm, (s * .13, .05, .30), .013, .02, "X", 10, 1)
        A(bm, (s * .13, -.06, .28), .011, .02, "X", 10, 1)
    K.new_obj("BST_Hoses", bm, ["M_BV2_WornRed", "M_BV2_SteelBare"], origin=(0, 0, .5))


def frame_and_rails():
    bm = K.bm_new()
    B(bm, (-0.30, -0.93, .70), (0.30, 0.93, .74), 0, bevel=.006)                          # under-pad frame
    for y in (-0.62, -0.31, 0.10, 0.40, 0.69):
        B(bm, (-0.31, y - .015, .695), (0.31, y + .015, .745), 0, bevel=.004)
    B(bm, (-0.06, -0.80, .64), (0.06, 0.80, .70), 0, bevel=.008)                          # spine
    K.new_obj("BST_Frame", bm, ["M_BV2_SteelGreen"], origin=(0, 0, .7))
    bm = K.bm_new()                                                                       # accessory side rails on posts
    for s in (-1, 1):
        A(bm, (s * .335, 0, .755), .011, 1.70, "Y", 12, 0)
        for y in (-0.80, -0.40, 0.0, 0.40, 0.80):
            B(bm, (s * .30, y - .012, .735), (s * .34, y + .012, .765), 1, bevel=.003)
        for y in (-0.55, 0.25):                                                           # clamp posts with knobs
            B(bm, (s * .33 - .016, y - .02, .74), (s * .33 + .016, y + .02, .775), 1, bevel=.003)
            A(bm, (s * .35, y, .757), .009, .03, "X", 10, 2)
            A(bm, (s * .33, y, .80), .008, .05, "Z", 10, 0)
    K.new_obj("BST_Rails", bm, ["M_BV2_SteelBare", "M_BV2_SteelGreen", "M_BV2_DarkPlastic"], origin=(0, 0, .755))


def pad(bm, y0, y1, x=0.30, lo=PAD_LO, top=PAD_TOP):
    B(bm, (-x, y0, lo), (x, y1, lo + .012), 1, bevel=.003)                                # steel backing plate
    B(bm, (-x + .005, y0 + .005, lo + .012), (x - .005, y1 - .005, top), 0, bevel=.022, segs=3)
    for s in (-1, 1):                                                                     # piping along the long edges
        A(bm, (s * (x - .006), (y0 + y1) / 2, top - .012), .005, (y1 - y0) - .03, "Y", 6, 2)


def pads():
    bm = K.bm_new()
    pad(bm, -0.93, -0.32)
    K.new_obj("BST_Pad_Legs", bm, ["M_ST_Vinyl", "M_BV2_SteelBare", "M_BV2_Rubber"], origin=(0, -0.32, PAD_LO))
    bm = K.bm_new()
    pad(bm, -0.31, 0.10)
    K.new_obj("BST_Pad_Seat", bm, ["M_ST_Vinyl", "M_BV2_SteelBare", "M_BV2_Rubber"], origin=(0, 0, PAD_LO))
    bm = K.bm_new()
    pad(bm, 0.11, 0.68)
    for s in (-1, 1):                                                                     # gas struts under the back section
        K.add_sweep(bm, [(s * .2, .14, .70), (s * .2, .30, PAD_LO - .002)], .008, 8, 1)
    back = K.new_obj("BST_Pad_Back", bm, ["M_ST_Vinyl", "M_BV2_SteelBare", "M_BV2_Rubber"], origin=(0, 0.11, PAD_LO), role="posable",
                     note="hinge along X at its origin; chair pose = +38 deg")
    bm = K.bm_new()
    pad(bm, 0.69, 0.93, x=.20)
    B(bm, (-0.12, 0.74, PAD_TOP - .004), (0.12, 0.90, HEAD_TOP), 0, bevel=.02, segs=3)   # head cushion
    K.new_obj("BST_Pad_Head", bm, ["M_ST_Vinyl", "M_BV2_SteelBare", "M_BV2_Rubber"], origin=(0, 0.69, PAD_LO), parent=back, role="posable",
              note="follows the back section; extra -12 deg in chair pose")
    return back


def strap(i, y, parent):
    """Restraint strap across the pads: closed (buckled over the top) and open (hanging off the left side)."""
    holder = K.empty("BST_Strap_%d" % i, (0, y, PAD_TOP), parent)
    bm = K.bm_new()
    B(bm, (-0.315, y - .028, PAD_TOP), (0.315, y + .028, PAD_TOP + .005), 0)
    for s in (-1, 1):
        B(bm, (s * .31 - .005, y - .028, .72), (s * .31 + .005, y + .028, PAD_TOP + .005), 0)
    B(bm, (-.04, y - .034, PAD_TOP + .003), (.04, y + .034, PAD_TOP + .016), 1, bevel=.003)
    B(bm, (-.02, y - .02, PAD_TOP + .014), (.02, y + .02, PAD_TOP + .018), 2)
    K.new_obj("BST_Strap_%d_Closed" % i, bm, ["M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_WornRed"], origin=(0, y, PAD_TOP), parent=holder)
    bm = K.bm_new()
    pts = [(-0.31, y, .72), (-0.33, y, .62), (-0.34, y + .01, .52), (-0.335, y + .02, .46)]
    for k in range(len(pts) - 1):
        a, b = Vector(pts[k]), Vector(pts[k + 1])
        B(bm, (min(a.x, b.x) - .004, y - .028, min(a.z, b.z)), (max(a.x, b.x) + .004, y + .028, max(a.z, b.z)), 0)
    B(bm, (-0.345, y - .034, .44), (-0.325, y + .034, .47), 1, bevel=.003)
    K.new_obj("BST_Strap_%d_Open" % i, bm, ["M_BV2_DarkPlastic", "M_BV2_SteelBare"], origin=(-0.32, y, .6), parent=holder)
    K.INTERACT[holder.name] = {"kind": "strap"}
    return holder


def pendant():
    """Hand pendant hooked on the right rail: lift / tilt buttons and a two-digit pose readout."""
    x, y = 0.352, -0.05
    bm = K.bm_new()
    B(bm, (x - .008, y - .035, .50), (x + .022, y + .035, .64), 0, bevel=.008, segs=2)
    for i, z in enumerate((.615, .59, .565, .54)):
        B(bm, (x + .021, y - .022, z - .008), (x + .026, y - .002, z + .008), 1 + i % 2, bevel=.002, segs=1)
        B(bm, (x + .021, y + .004, z - .008), (x + .026, y + .024, z + .008), 2 - i % 2, bevel=.002, segs=1)
    B(bm, (x + .0215, y - .025, .515), (x + .0235, y + .025, .53), 3)
    A(bm, (x + .005, y, .65), .006, .02, "Z", 8, 4)
    K.new_obj("BST_Pendant", bm, ["M_BV2_Yellow", "M_BV2_DarkPlastic", "M_BV2_Ivory", "M_BV2_LedGreen", "M_BV2_SteelBare"],
              origin=(x, y, .57), role="control")
    bm = K.bm_new()
    coil = [(x + .005 + .009 * math.cos(a), y + .009 * math.sin(a), .66 + a * .0028) for a in np.linspace(0, 10 * math.pi, 120)]
    K.add_sweep(bm, coil, .0022, 5, 0)
    K.add_sweep(bm, [(x + .005, y, .748), (0.335, y, .755)], .003, 5, 0)
    K.new_obj("BST_PendantCable", bm, ["M_BV2_Rubber"], origin=(x, y, .7))
    K.INTERACT["BST_Pendant"] = {"kind": "pendant"}


def instrument_tray():
    """Mayo-style tray on a swing arm at the foot end; pivot on the right foot rail post, swings out to the side."""
    px, py = 0.30, -0.88
    arm = K.bm_new()
    A(arm, (px, py, .80), .014, .10, "Z", 12, 0)
    B(arm, (px - .012, py - .012, .84), (px + .012, py + .012, .95), 0, bevel=.003)
    B(arm, (-0.02, py - .012, .935), (px, py + .012, .95), 0, bevel=.003)
    tray = arm
    B(tray, (-0.24, py - .10, .95), (0.02, py + .10, .958), 1, bevel=.003)
    for (a, b, c, d) in ((-0.24, py - .10, .02, py - .094), (-0.24, py + .094, .02, py + .10), (-0.24, py - .10, -0.234, py + .10), (.014, py - .10, .02, py + .10)):
        B(tray, (a, b, .958), (c, d, .975), 1)
    for i, x in enumerate((-0.20, -0.15, -0.10)):                                         # instruments
        A(tray, (x, py - .02, .962), .005, .13, "Y", 8, 1)
        A(tray, (x, py + .055, .962), .007, .03, "Y", 8, 2)
    B(tray, (-0.06, py - .06, .958), (0.0, py + .06, .963), 3)
    K.new_obj("BST_Tray", tray, ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Ivory"], origin=(px, py, .80), role="swing",
              note="rotates about +Z at its origin; out = -90 deg (toward +X)")
    K.INTERACT["BST_Tray"] = {"kind": "tray"}


def iv_pole():
    x, y = -0.335, 0.86
    bm = K.bm_new()
    B(bm, (x - .02, y - .02, .735), (x + .02, y + .02, .78), 1, bevel=.004)
    A(bm, (x, y, 1.30), .010, 1.08, "Z", 10, 0)
    for a in (0, 2.1, 4.2):
        K.add_sweep(bm, [(x, y, 1.82), (x + .07 * math.cos(a), y + .07 * math.sin(a), 1.86), (x + .07 * math.cos(a), y + .07 * math.sin(a), 1.83)], .003, 5, 0)
    K.new_obj("BST_IVPole", bm, ["M_BV2_SteelBare", "M_BV2_SteelGreen"], origin=(x, y, .78))
    bm = K.bm_new()
    K.add_lathe(bm, [(0.0, 0.0), (0.03, 0.01), (0.045, 0.06), (0.042, 0.15), (0.02, 0.18), (0.0, 0.185)], (x + .07, y, 1.63), "Z", 14, 0)
    for v in [v for v in bm.verts]:
        v.co.y = y + (v.co.y - y) * .45
    K.add_sweep(bm, K.bezier((x + .07, y, 1.63), (x + .07, y, 1.45), (-0.20, 0.82, 1.05), (-0.12, 0.70, PAD_TOP + .03), 16), .0025, 5, 1)
    K.new_obj("BST_IVBag", bm, ["M_ST_Bag", "M_ST_Bag"], origin=(x + .07, y, 1.6))
    # CRT patient monitor clamped on the pole, screen angled toward the surgeon's side (+X) and the foot end
    mx, my, mz = x + .08, y - .09, 1.30
    bm = K.bm_new()
    B(bm, (mx - .11, my - .085, mz - .09), (mx + .11, my + .085, mz + .09), 0, bevel=.012, segs=2)       # ivory housing
    B(bm, (mx - .07, my + .085, mz - .065), (mx + .07, my + .15, mz + .065), 0, bevel=.02, segs=2)      # tube bulge
    B(bm, (mx - .095, my - .089, mz - .075), (mx + .095, my - .084, mz + .075), 1, bevel=.004, segs=1)  # dark bezel
    for i, z in enumerate((mz - .06, mz - .03)):
        A(bm, (mx + .085, my - .09, z), .007, .008, "Y", 10, 1)
    B(bm, (x - .015, y - .02, mz - .02), (x + .015, y + .02, mz + .02), 2, bevel=.003)                   # pole clamp
    B(bm, (x, y - .01, mz - .012), (mx - .11, y + .01, mz + .012), 2)
    mon = K.new_obj("BST_Monitor", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare"], origin=(mx, my, mz))
    bm = K.bm_new()
    sx, sz = .082, .062
    vs = [bm.verts.new(p) for p in ((mx + sx, my - .0905, mz - sz), (mx - sx, my - .0905, mz - sz), (mx - sx, my - .0905, mz + sz), (mx + sx, my - .0905, mz + sz))]
    f = bm.faces.new(vs)
    uvl = bm.loops.layers.uv.verify()
    for l, uv in zip(f.loops, ((1, 0), (0, 0), (0, 1), (1, 1))):
        l[uvl].uv = uv
    f.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    K.new_obj("BST_Monitor_Screen", bm, ["M_ST_Screen"], origin=(mx, my - .0905, mz), parent=mon, uv=None, role="screen")


def build():
    textures = build_textures()
    build_materials(textures)
    root = K.empty("SurgeryTableV2", (0, 0, 0))
    K.set_root(root)
    K.empty("Ref_Origin", (0, 0, 0), root)
    K.empty("Ref_Head", (0, 1, 0), root)
    K.empty("Ref_Up", (0, 0, 1), root)
    chassis()
    column()
    frame_and_rails()
    back = pads()
    seat = [o for o in K.OBJECTS if o.name == "BST_Pad_Seat"][0]
    legs = [o for o in K.OBJECTS if o.name == "BST_Pad_Legs"][0]
    strap(1, 0.45, back)
    strap(2, -0.10, seat)
    strap(3, -0.62, legs)
    pendant()
    instrument_tray()
    iv_pole()


def export():
    bpy.ops.object.select_all(action="DESELECT")
    for o in K.OBJECTS:
        o.select_set(True)
    path = os.path.join(EXPORT, "SurgeryTableV2.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)
    return path


def report(fbx):
    rows, total = [], 0
    lo_all, hi_all = [9] * 3, [-9] * 3
    for o in K.OBJECTS:
        if o.type != "MESH":
            continue
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        total += tris
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = [min(v[i] for v in bb) for i in range(3)]
        hi = [max(v[i] for v in bb) for i in range(3)]
        if not o.name.startswith(("BST_Strap", "BST_Tray", "BST_IV", "BST_Pendant")):
            lo_all = [min(a, b) for a, b in zip(lo_all, lo)]
            hi_all = [max(a, b) for a, b in zip(hi_all, hi)]
        rows.append({"name": o.name, "tris": tris, "materials": [m.name for m in o.data.materials], "min": [round(x, 4) for x in lo], "max": [round(x, 4) for x in hi]})
    data = {"fbx": fbx, "triangles": total, "objects": rows, "interactive": K.INTERACT,
            "body_bounds": {"min": [round(x, 3) for x in lo_all], "max": [round(x, 3) for x in hi_all]},
            "footprint_ok": lo_all[0] >= -0.366 and hi_all[0] <= 0.366 and lo_all[1] >= -0.955 and hi_all[1] <= 0.955}
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("ST triangles", total, "objects", len(rows), "footprint_ok", data["footprint_ok"], data["body_bounds"])


def render():
    bpy.context.view_layer.update()
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
    for o in bpy.data.objects:
        if o.name.endswith("_Open"):
            o.hide_render = True
    shots = {"table_threequarter": ((1.7, -1.9, 1.7), (0, 0, .6), 32), "table_side": ((2.4, 0.0, 1.0), (0, 0, .6), 30),
             "table_foot": ((0.5, -2.2, 1.3), (0, -0.3, .7), 35), "table_head_detail": ((-0.9, 1.4, 1.3), (-0.1, 0.6, .85), 35)}
    for name, (loc, target, lens) in shots.items():
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = lens
        SCENE.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)
    # chair pose preview
    back = bpy.data.objects["BST_Pad_Back"]
    head = bpy.data.objects["BST_Pad_Head"]
    tray = bpy.data.objects["BST_Tray"]
    back.rotation_euler.x = math.radians(38)
    head.rotation_euler.x = math.radians(-12)
    tray.rotation_euler.z = math.radians(90)
    for name in ("BST_Strap_1_Closed", "BST_Strap_2_Closed"):
        bpy.data.objects[name].hide_render = True
    for name in ("BST_Strap_1_Open", "BST_Strap_2_Open", "BST_Strap_3_Open"):
        bpy.data.objects[name].hide_render = name.endswith("3_Open")
    cam.location = (1.7, -1.9, 1.7)
    cam.rotation_euler = (Vector((0, 0, .6)) - Vector(cam.location)).to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 32
    SCENE.render.filepath = os.path.join(RENDERS, "table_chair_pose.png")
    bpy.ops.render.render(write_still=True)


build()
fbx = export()
report(fbx)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "SurgeryTableV2.blend"))
if not NO_RENDER:
    render()
print("ST_DONE")
