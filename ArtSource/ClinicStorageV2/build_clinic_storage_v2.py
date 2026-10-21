"""Clinic storage V2: the room's shelves and cabinets redone for the setting (a back-street cyberware clinic in the
NSP port, cassette-futurism, everything second-hand), replacing the plain painted boxes of UnifiedClinicEnvironment
on their old footprints (their colliders stay in the scene):

  NW  small-parts drawer wall (30 labelled drawers, organiser cases on top) + a worn rolling tool chest with a
      soldering station, a stripped forearm and a magnifier lamp
  NE  biological-parts cold cabinet: glowing specimen jars (an eye, a hand, nerve bundles), cassette racks, LCD
  SW  used-limb rack: hooked prosthetic arms with price tags, legs standing in a floor tray, labelled totes, a spare
      UNIT-series face bezel on top
  S   two dented crew lockers with number plates, stickers, a padlock and a hard hat
  W   NSP shipping crates by the dock (old bearings for UNIT 07)
Labels are Japanese / English (DotGothic16 pixel font, Yu Gothic bold), printed onto one sheet T_ST_Prints.

Room frame = Unity world, as the other V2 kits: Blender x = Unity x, Blender y = Unity z, Blender z = Unity y.
Run: blender -b --factory-startup --python build_clinic_storage_v2.py -- <cc0_texture_dir> [--norender]
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "RepairBenchV2"))
import bv2_kit as K  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
CC0 = ARGS[0] if ARGS and not ARGS[0].startswith("--") else os.path.join(HERE, "..", "ClinicZonesV2", "cc0")
NO_RENDER = "--norender" in ARGS
TEX = os.path.join(HERE, "Textures")
BENCH_TEX = os.path.join(HERE, "..", "RepairBenchV2", "Textures")
ZONES_TEX = os.path.join(HERE, "..", "ClinicZonesV2", "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
for d in (EXPORT, RENDERS, TEX):
    os.makedirs(d, exist_ok=True)
FONT_PIX = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/cyber-ui-bench-ps2-2026-10-20/downloads/fonts/DotGothic16-Regular.ttf"
FONT_JP = "C:/Windows/Fonts/YuGothB.ttc"
if not os.path.exists(FONT_JP):
    FONT_JP = "C:/Windows/Fonts/msgothic.ttc"
RNG = np.random.default_rng(2110)
A = 3.40
SIDES = ["E", "NE", "N", "NW", "W", "SW", "S", "SE"]

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene

MAT_SPEC = {
    "M_BV2_SteelGreen": ("T_BV2_SteelGreen", .35, .32, None, 2.2),
    "M_BV2_SteelBare": ("T_BV2_SteelBare", .9, .48, None, 3.0),
    "M_BV2_Ivory": ("T_BV2_Ivory", 0, .25, None, 3.0),
    "M_BV2_WornRed": ("T_BV2_WornRed", .2, .35, None, 3.0),
    "M_BV2_Yellow": ("T_BV2_Yellow", 0, .3, None, 5.0),
    "M_BV2_Rubber": ("T_BV2_Rubber", 0, .15, None, 6.0),
    "M_BV2_DarkPlastic": ("T_BV2_Rubber", 0, .4, None, 4.0),
    "M_BV2_Ceramic": (None, 0, .75, None, 1.0),
    "M_BV2_Bulb": (None, 0, .6, (1.0, .78, .5), 1.0),
    "M_BV2_LedRed": (None, 0, .6, (1.0, .15, .1), 1.0),
    "M_ZN_Hazard": ("T_ZN_Hazard", 0, .3, None, 6.0),
    "M_ZN_Bag": (None, 0, .75, None, 1.0),
    "M_ST_Glass": (None, .1, .92, None, 1.0),
    "M_ST_Gel": (None, 0, .8, (1.0, .45, .55), 1.0),
    "M_ST_GelAmber": (None, 0, .8, (1.0, .62, .25), 1.0),
    "M_ST_Prints": ("T_ST_Prints", 0, .15, None, 1.0),
}
FLAT = {"M_BV2_Ceramic": (.93, .91, .86), "M_BV2_Bulb": (1, .92, .75), "M_BV2_LedRed": (.9, .1, .1), "M_ZN_Bag": (.70, .52, .58),
        "M_ST_Glass": (.10, .12, .13), "M_ST_Gel": (.95, .38, .52), "M_ST_GelAmber": (1, .62, .25)}
K.init(SCENE, CC0, TEX, MAT_SPEC)
B, C, SW = K.add_box, K.add_cyl, K.add_sweep
hexc = K.hexc


# ---------------------------------------------------------------------------------------------------------- prints
def noise(w, h, seed=0):
    rng = np.random.default_rng(seed)
    n = rng.normal(0, 1, (h // 4 + 2, w // 4 + 2))
    n = np.kron(n, np.ones((4, 4)))[:h, :w]
    return n * .25


def paper(w, h, base="#E4DDC5", seed=0, grain=.16):
    return np.ones((h, w, 3)) * hexc(base) * (1 + grain * noise(w, h, seed))[..., None]


def text(img, s, x, y, w, h, color, font=FONT_PIX, align="CENTER", pad=.06, alpha=1.0):
    m = K.text_mask(s, font, w, h, pad=pad, align=align)
    K.paint(img, m, hexc(color) if isinstance(color, str) else color, alpha, x, img.shape[0] - y - h)


def rect(img, x0, y0, x1, y1, color, alpha=1.0):
    H = img.shape[0]
    c = hexc(color) if isinstance(color, str) else np.asarray(color)
    img[H - y1:H - y0, x0:x1] = img[H - y1:H - y0, x0:x1] * (1 - alpha) + c * alpha


class Sheet:
    def __init__(self, name, w, h, base="#30302C"):
        self.name, self.W, self.H = name, w, h
        self.img = np.ones((h, w, 3)) * hexc(base)
        self.x = self.y = self.row = 0
        self.uv = {}

    def add(self, key, art):
        h, w = art.shape[:2]
        if self.x + w > self.W:
            self.x, self.y, self.row = 0, self.y + self.row + 2, 0
        assert self.y + h <= self.H, (key, self.y, h)
        # Sheet rows are stored bottom-up like Blender images: place the art so its UV rect reads upright.
        y0 = self.H - self.y - h
        self.img[y0:y0 + h, self.x:self.x + w] = art
        self.uv[key] = ((self.x + .5) / self.W, (y0 + .5) / self.H, (self.x + w - .5) / self.W, (y0 + h - .5) / self.H)
        self.x += w + 2
        self.row = max(self.row, h)

    def save(self):
        return K.save_png(self.name, self.img)


DRAWERS = ["抵抗 RES", "コンデンサ CAP", "M3 ネジ", "M2 ネジ", "端子 TERM", "ヒューズ FUSE", "LED 赤", "LED 白", "IC 74xx",
           "光 FIBER", "神経 NRV-CON", "シール SEAL", "電池 CELL", "ばね SPRING", "Oリング O-RING", "ギア GEAR",
           "軸受 BRG 6x", "ハンダ SOLDER", "熱収縮 HST", "端末 PIN", "磁石 MAG", "水晶 XTAL", "リレー RELAY",
           "センサ SENS", "モーター MTR", "指先 TIP", "関節 JNT", "皮膚 SKIN", "予備 SPARE", "不明 ???"]


def build_prints():
    s = Sheet("T_ST_Prints", 1024, 512)
    ink = "#1E2024"
    for i, label in enumerate(DRAWERS):
        p = paper(96, 28, "#E9E3CF" if i % 7 else "#E7C64A", i, .1)
        text(p, label, 2, 2, 92, 24, ink)
        s.add("dr%d" % i, p)
    lcd = np.ones((56, 160, 3)) * hexc("#1B0F06")
    text(lcd, "-04.2°C", 4, 4, 100, 30, "#FFA23A", font=FONT_PIX, align="LEFT")
    text(lcd, "生体部品 BIO", 4, 32, 152, 20, "#C06A20", font=FONT_PIX, align="LEFT")
    rect(lcd, 120, 8, 150, 26, "#FF5A3A", .9)
    s.add("lcd", lcd)
    bio = paper(112, 112, "#E8D23A", 3, .08)
    text(bio, "要冷蔵", 6, 6, 100, 34, "#B02A1E", font=FONT_JP)
    text(bio, "KEEP COLD", 6, 42, 100, 22, ink)
    text(bio, "生体部品\nBIO-PARTS", 6, 66, 100, 40, ink)
    s.add("bio", bio)
    for i, (a, b) in enumerate((("中古 USED", "¥4800"), ("要整備", "AS-IS"), ("左腕 L-ARM", "¥7200"), ("予約済", "HOLD"))):
        t = paper(56, 80, "#D6C28A", 10 + i)
        rect(t, 24, 4, 32, 12, "#4A4030")
        text(t, a, 2, 16, 52, 26, ink, font=FONT_JP if i in (1, 3) else FONT_PIX)
        text(t, b, 2, 46, 52, 28, "#B02A1E", font=FONT_PIX)
        s.add("tag%d" % i, t)
    for i, label in enumerate(("義手 ARM-L", "義手 ARM-R", "指 FINGER", "関節 JOINT", "外装 SHELL", "配線 WIRE")):
        p = paper(112, 36, "#ECEBE4", 20 + i, .08)
        text(p, label, 4, 4, 104, 28, ink)
        s.add("tote%d" % i, p)
    for i, n in enumerate(("07", "08")):
        p = np.ones((48, 64, 3)) * hexc("#C9C2A8")
        rect(p, 2, 2, 62, 46, "#2A2C2A")
        text(p, n, 6, 6, 52, 36, "#E8E0C6")
        s.add("lock%d" % i, p)
    st = paper(96, 64, "#24346A", 30, .05)
    text(st, "NSP\nDOCK CREW", 4, 4, 88, 56, "#E8E0C6", font=FONT_PIX)
    s.add("crew", st)
    st = paper(72, 72, "#B02A1E", 31, .05)
    text(st, "立入\n禁止", 6, 6, 60, 60, "#F2EEE4", font=FONT_JP)
    s.add("noentry", st)
    ph = paper(64, 80, "#EEEDE6", 32, .05)
    rect(ph, 6, 6, 58, 58, "#6E7E84")
    rect(ph, 20, 22, 44, 58, "#3A3E44")
    rect(ph, 26, 12, 38, 24, "#D9B79C")
    text(ph, "2071", 6, 60, 52, 18, ink)
    s.add("photo", ph)
    cr = np.ones((96, 224, 3)) * hexc("#3F4A42") * (1 + .25 * noise(224, 96, 33))[..., None]
    text(cr, "旧軸受 OLD BRG", 8, 6, 208, 40, "#E6DDBF", font=FONT_JP)
    text(cr, "UNIT 07 // NSP-0742", 8, 52, 208, 34, "#E6DDBF")
    s.add("crate", cr)
    sd = np.ones((32, 72, 3)) * hexc("#140C06")
    text(sd, "350°C", 2, 2, 68, 28, "#FF9A3A")
    s.add("solder", sd)
    for i, label in enumerate(("棚 A", "棚 B")):
        p = np.ones((40, 72, 3)) * hexc("#1C1E1E")
        text(p, label, 4, 4, 64, 32, "#E7C64A")
        s.add("bay%d" % i, p)
    return s


# ------------------------------------------------------------------------------------------------------- materials
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


def build_textures():
    t = {}
    for name in ("T_BV2_SteelGreen", "T_BV2_SteelBare", "T_BV2_Ivory", "T_BV2_WornRed", "T_BV2_Yellow", "T_BV2_Rubber"):
        t[name] = bpy.data.images.load(os.path.abspath(os.path.join(BENCH_TEX, name + ".png")), check_existing=True)
    t["T_ZN_Hazard"] = bpy.data.images.load(os.path.abspath(os.path.join(ZONES_TEX, "T_ZN_Hazard.png")), check_existing=True)
    global PR
    PR = build_prints()
    t["T_ST_Prints"] = PR.save()
    return t


# -------------------------------------------------------------------------------------------------------- framing
def side(k):
    """Wall side k: (centre at floor, along-wall t, into-room n), Blender axes."""
    a = math.radians(45 * k)
    n_out = Vector((math.cos(a), math.sin(a), 0))
    t = Vector((-math.sin(a), math.cos(a), 0))
    return n_out * A, t, -n_out


def make(name, mats, origin, build, uv="box", role="static"):
    bm = K.bm_new()
    build(bm)
    return K.new_obj(name, bm, mats, origin=origin, uv=uv, role=role)


def on_wall(k, build_local):
    """Wraps a builder written in wall-local coordinates (x along the wall, y out from it, z up) into world space."""
    c, t, n = side(k)

    def build(bm):
        K.framed(bm, lambda: build_local(bm), c, t, n)
    return build


def wall_pt(k, s, d, z):
    c, t, n = side(k)
    return c + t * s + n * d + Vector((0, 0, z))


def prints_on_wall(k, items):
    """items: (s, d, z, w, h, key, roll); quads facing into the room."""
    c, t, n = side(k)

    def build(bm):
        bm.loops.layers.uv.verify()
        for s, d, z, w, h, key, roll in items:
            centre = c + t * s + n * d + Vector((0, 0, z))
            rot = Matrix.Rotation(math.radians(roll), 3, n)
            K.print_quad(bm, centre, rot @ t, rot @ Vector((0, 0, 1)), w, h, PR.uv[key])
    return build


def jar(bm, p, r, h, gel, glass, lid, content=None, eye_mat=5, hand_mat=0):
    x, y, z = p
    # Glowing gel body (no transparency in this pipeline) between a glass foot and shoulder ring.
    C(bm, (x, y, z + .012), r, .024, "Z", 10, glass)
    C(bm, (x, y, z + h * .5), r * .94, h * .9, "Z", 10, gel)
    C(bm, (x, y, z + h - .01), r, .02, "Z", 10, glass)
    C(bm, (x, y, z + h + .012), r * 1.04, .024, "Z", 10, lid)
    if content == "eye":
        K.add_lathe(bm, [(0, -.026), (.02, -.02), (.026, 0), (.02, .02), (0, .026)], (x, y - r * .2, z + h * .5), "Z", 10, eye_mat)
    elif content == "hand":
        B(bm, (x - .03, y - .012, z + h * .25), (x + .03, y + .012, z + h * .62), hand_mat)
        for k in range(4):
            B(bm, (x - .03 + k * .016, y - .01, z + h * .62), (x - .02 + k * .016, y + .01, z + h * .80), hand_mat)


# ------------------------------------------------------------------------------------------------------ the pieces
def nw_drawers():
    k = SIDES.index("NW")
    s0, s1, d1, top = -0.95, -0.25, 0.47, 1.90

    def local(bm):
        G, BARE, DK, HZ, IV = range(5)
        B(bm, (s0, .02, .10), (s1, d1 - .02, top), DK, bevel=.006, segs=1)                 # carcass
        B(bm, (s0 + .01, .03, 0), (s1 - .01, d1 - .04, .10), HZ)                             # hazard plinth
        cols, rows = 3, 10
        w = (s1 - s0 - .04) / cols
        h = (top - .16 - .06) / rows
        for c_ in range(cols):
            for r in range(rows):
                x0 = s0 + .02 + c_ * w + .006
                z0 = .16 + r * h + .006
                pulled = (c_, r) in ((1, 3), (2, 7))
                d = d1 + (.09 if pulled else 0)
                B(bm, (x0, d - .03, z0), (x0 + w - .012, d, z0 + h - .012), G if (c_ + r) % 5 else IV, bevel=.003, segs=1)
                B(bm, (x0 + w * .38, d, z0 + h * .12), (x0 + w * .62, d + .014, z0 + h * .30), BARE)
                if pulled:
                    for j in range(6):
                        C(bm, (x0 + .03 + j * .025, d - .06, z0 + h * .45), .006, .016, "Z", 6, BARE)
        # organiser cases and a fibre coil on top
        B(bm, (s0 + .04, .08, top), (s0 + .36, .40, top + .09), DK, bevel=.008, segs=1)
        B(bm, (s0 + .06, .10, top + .09), (s0 + .33, .38, top + .16), 5, bevel=.008, segs=1)
        coil = [(s1 - .16 + .09 * math.cos(a), .24 + .09 * math.sin(a), top + .01 + .006 * a / (2 * math.pi)) for a in np.linspace(0, 8 * math.pi, 80)]
        SW(bm, coil, .006, 5, 6)
    make("ST_NW_Drawers", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_ZN_Hazard", "M_BV2_Ivory", "M_ZN_Bag", "M_BV2_Yellow"],
         wall_pt(k, -.6, .25, 0), on_wall(k, local))
    cols, rows = 3, 10
    w = (s1 - s0 - .04) / cols
    h = (top - .16 - .06) / rows
    items = []
    for c_ in range(cols):
        for r in range(rows):
            pulled = (c_, r) in ((1, 3), (2, 7))
            x0 = s0 + .02 + c_ * w + .006
            z0 = .16 + r * h + .006
            items.append((x0 + (w - .012) / 2, d1 + (.09 if pulled else 0) + .0015, z0 + h * .64, w * .80, h * .34, "dr%d" % (c_ * rows + r), 0))
    return items


def nw_chest():
    k = SIDES.index("NW")
    s0, s1, d0, d1 = 0.10, 0.80, 0.03, 0.48

    def local(bm):
        R, BARE, DK, RB, IV, BULB = range(6)
        B(bm, (s0, d0, .09), (s1, d1, 1.00), R, bevel=.012, segs=1)
        for i in range(6):
            z = .14 + i * .14
            B(bm, (s0 + .03, d1, z), (s1 - .03, d1 + .008, z + .12), R, bevel=.003, segs=1)
            B(bm, (s0 + .12, d1 + .008, z + .085), (s1 - .12, d1 + .02, z + .10), BARE)
        for x in (s0 - .015, s1 + .015):
            C(bm, (x, d1 - .06, .85), .012, .12, "Z", 8, BARE)
        SW(bm, [(s0 - .015, d1 - .06, .91), (s0 - .04, d1 - .06, .95), (s0 - .04, d0 + .06, .95), (s0 - .015, d0 + .06, .91)], .01, 6, BARE)
        for x in (s0 + .06, s1 - .06):
            for d in (d0 + .06, d1 - .06):
                C(bm, (x, d, .045), .04, .03, "Y", 10, RB)
                B(bm, (x - .02, d - .02, .07), (x + .02, d + .02, .09), BARE)
        B(bm, (s0 + .01, d0 + .01, 1.00), (s1 - .01, d1 - .01, 1.012), RB)                     # top mat
        # soldering station with its iron stand
        B(bm, (s0 + .04, d0 + .06, 1.012), (s0 + .22, d0 + .22, 1.10), DK, bevel=.006, segs=1)
        C(bm, (s0 + .30, d0 + .14, 1.06), .025, .10, "Z", 10, BARE)
        SW(bm, [(s0 + .13, d0 + .20, 1.06), (s0 + .22, d0 + .30, 1.03), (s0 + .30, d0 + .16, 1.11)], .004, 5, RB)
        # stripped prosthetic forearm lying across
        C(bm, (s0 + .50, d0 + .30, 1.05), .04, .26, "X", 10, IV)
        C(bm, (s0 + .34, d0 + .30, 1.05), .046, .06, "X", 10, DK)
        for j in range(5):
            SW(bm, [(s0 + .62, d0 + .28 + j * .01, 1.05), (s0 + .70, d0 + .24 + j * .02, 1.03)], .003, 4, 6)
        # magnifier lamp: clamp, two arm segments, ring head with a lit tube
        C(bm, (s1 - .08, d0 + .08, 1.08), .015, .14, "Z", 8, BARE)
        SW(bm, [(s1 - .08, d0 + .08, 1.15), (s1 - .10, d0 + .22, 1.42), (s1 - .22, d0 + .32, 1.36)], .009, 6, DK)
        K.add_lathe(bm, [(.06, -.012), (.075, -.012), (.075, .012), (.06, .012)], (s1 - .24, d0 + .34, 1.33), "Z", 16, DK)
        C(bm, (s1 - .24, d0 + .34, 1.322), .06, .006, "Z", 16, BULB)
    make("ST_NW_ToolChest", ["M_BV2_WornRed", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Rubber", "M_BV2_Ivory", "M_BV2_Bulb", "M_BV2_Yellow"],
         wall_pt(k, .45, .25, 0), on_wall(k, local))
    return [((s0 + .13), d0 + .221, 1.09, .07, .03, "solder", 0)]


def ne_cold():
    k = SIDES.index("NE")
    s0, s1, d0, d1, top = 0.25, 0.95, 0.02, 0.47, 1.95

    def local(bm):
        IV, BARE, DK, GL, GEL, CER, AMB, BULB, RB = range(9)
        # body: back, sides, top and base, an open front behind a glass door frame
        B(bm, (s0, d0, 0), (s1, d0 + .03, top), IV)
        B(bm, (s0, d0, 0), (s0 + .03, d1, top), IV, bevel=.004, segs=1)
        B(bm, (s1 - .03, d0, 0), (s1, d1, top), IV, bevel=.004, segs=1)
        B(bm, (s0, d0, top - .22), (s1, d1, top), IV, bevel=.006, segs=1)
        B(bm, (s0, d0, 0), (s1, d1, .22), IV, bevel=.006, segs=1)
        for i in range(14):                                             # compressor grille
            B(bm, (s0 + .06 + i * .042, d1, .04), (s0 + .085 + i * .042, d1 + .006, .18), DK)
        B(bm, (s0 + .03, d0 + .03, .22), (s1 - .03, d0 + .05, top - .22), DK)          # dark interior back
        for z in (.62, 1.02, 1.40):
            B(bm, (s0 + .03, d0 + .04, z), (s1 - .03, d1 - .03, z + .01), GL)
        B(bm, (s0 + .05, d1 - .07, top - .235), (s1 - .05, d1 - .05, top - .225), BULB)  # interior light
        # door: steel frame and a dark glass pane, handle
        df = d1 + .005
        for (a0, a1, z0, z1) in ((s0, s0 + .04, .22, top - .22), (s1 - .04, s1, .22, top - .22), (s0, s1, .22, .26), (s0, s1, top - .26, top - .22)):
            B(bm, (a0, df, z0), (a1, df + .03, z1), BARE, bevel=.003, segs=1)
        for z in (.70, 1.25):                                            # door cross-rails; the pane itself is left out (open display)
            B(bm, (s0 + .04, df + .008, z), (s1 - .04, df + .022, z + .02), BARE)
        C(bm, (s1 - .07, df + .05, 1.10), .012, .50, "Z", 8, BARE)
        # specimens: glowing jars (one eye, one hand), nerve-bundle jars, cassette racks below
        for i, (x, kind) in enumerate(((s0 + .14, "eye"), (s0 + .32, None), (s0 + .50, "hand"))):
            jar(bm, (x, d0 + .22, 1.41), .065, .22, GEL if i != 1 else AMB, GL, DK, kind)
        for i, x in enumerate((s0 + .12, s0 + .26, s0 + .40, s0 + .54)):
            jar(bm, (x, d0 + .22, 1.03), .05, .16, AMB if i % 2 else GEL, GL, DK, "eye" if i == 2 else None)
        for i in range(9):
            B(bm, (s0 + .06 + i * .065, d0 + .10, .63), (s0 + .11 + i * .065, d0 + .36, .92), RB if i % 3 else DK, bevel=.004, segs=1)
        for i in range(3):
            B(bm, (s0 + .07 + i * .2, d0 + .08, .26), (s0 + .24 + i * .2, d0 + .38, .55), DK, bevel=.01, segs=1)
    make("ST_NE_ColdCabinet", ["M_BV2_Ivory", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_ST_Glass", "M_ST_Gel", "M_BV2_Ceramic",
                               "M_ST_GelAmber", "M_BV2_Bulb", "M_BV2_Rubber"], wall_pt(k, .6, .25, 0), on_wall(k, local))
    return [((s0 + s1) / 2, d1 + .0012, top - .11, .30, .105, "lcd", 0), (s0 + .14, d1 + .037, 1.62, .11, .11, "bio", -4)]


def sw_limbs():
    k = SIDES.index("SW")
    s0, s1, d0, d1 = -0.66, 0.66, 0.02, 0.44

    def local(bm):
        G, BARE, DK, IV, RB, YE = range(6)
        for s in (s0 + .02, s1 - .02):
            for d in (d0 + .02, d1 - .02):
                B(bm, (s - .02, d - .02, 0), (s + .02, d + .02, 1.92), G)
        B(bm, (s0, d0, .30), (s1, d0 + .015, 1.92), DK)                                     # back board
        for i in range(10):
            for j in range(14):
                B(bm, (s0 + .08 + j * .088, d0 + .015, .40 + i * .14), (s0 + .10 + j * .088, d0 + .02, .44 + i * .14), RB)
        for z in (.08, 1.40, 1.78):
            B(bm, (s0, d0, z), (s1, d1, z + .025), G, bevel=.004, segs=1)
            B(bm, (s0, d1 - .01, z - .03), (s1, d1, z + .025), G)
        # floor tray of standing legs
        for i, s in enumerate((-.48, -.22, .06, .34)):
            B(bm, (s - .05, .16, .105), (s + .05, .30, .145), DK, bevel=.01, segs=1)                       # foot
            C(bm, (s, .22, .30), .03, .32, "Z", 8, BARE)                                                       # pylon
            C(bm, (s, .22, .49), .05, .06, "X", 10, DK)                                                         # knee
            C(bm, (s, .22, .66), .065, .30, "Z", 10, IV if i % 2 == 0 else YE)                                 # socket
        # hooked arms on a rail
        B(bm, (s0 + .04, d0 + .015, 1.30), (s1 - .04, d0 + .05, 1.33), BARE)
        for i, s in enumerate((-.46, -.16, .14, .44)):
            SW(bm, [(s, d0 + .05, 1.315), (s, d0 + .12, 1.30), (s, d0 + .13, 1.25)], .006, 5, BARE)
            C(bm, (s, .18, 1.17), .052, .14, "Z", 10, IV if i != 2 else DK)                                  # shoulder socket
            C(bm, (s, .18, 1.05), .04, .12, "Z", 10, DK if i != 1 else IV)                                    # upper arm
            C(bm, (s, .18, .97), .036, .05, "X", 10, BARE)                                                     # elbow
            C(bm, (s, .18, .85), .034, .20, "Z", 10, IV if i % 2 else YE)                                     # forearm
            B(bm, (s - .035, .165, .66), (s + .035, .195, .75), IV, bevel=.008, segs=1)                       # hand
            for f in range(4):
                B(bm, (s - .03 + f * .017, .17, .60), (s - .02 + f * .017, .19, .66), DK)
            SW(bm, [(s + .02, .20, 1.10), (s + .05, .24, .95), (s + .03, .22, .82)], .004, 4, RB)
        # totes on the upper shelf, a bezel and boxes on top
        for i, s in enumerate((-.50, -.17, .16, .48)):
            B(bm, (s - .14, .06, 1.425), (s + .14, .40, 1.62), DK, bevel=.01, segs=1)
            B(bm, (s - .06, .40, 1.56), (s + .06, .405, 1.59), BARE)
        B(bm, (-.50, .08, 1.805), (-.12, .36, 1.97), G, bevel=.01, segs=1)
        B(bm, (.10, .12, 1.805), (.36, .38, 1.90), DK, bevel=.01, segs=1)
        K.add_lathe(bm, [(.0, -.07), (.12, -.07), (.13, -.05), (.13, .05), (.12, .07), (.0, .07)], (.50, .24, 1.88), "Y", 12, IV)
        B(bm, (.42, .099, 1.84), (.58, .101, 1.92), DK)
    make("ST_SW_LimbRack", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Ivory", "M_BV2_Rubber", "M_BV2_Yellow"],
         wall_pt(k, 0, .22, 0), on_wall(k, local))
    items = []
    for i, s in enumerate((-.46, -.16, .14, .44)):
        items.append((s + .05, .201, .90, .045, .065, "tag%d" % i, (-8, 6, -4, 10)[i]))
    for i, s in enumerate((-.50, -.17, .16, .48)):
        items.append((s, .4015, 1.50, .16, .052, "tote%d" % i, 0))
    items.append((-.31, .3615, 1.89, .11, .061, "bay0", 0))
    return items


def s_lockers():
    # South wall, x .96..1.38 (Blender), front pushed to A - .497 so the old handles sit inside.
    y0, y1 = -A + .01, -A + .497
    x0, x1 = .955, 1.385

    def build(bm):
        G, BARE, DK, YE = range(4)
        B(bm, (x0, y0, 0), (x1, y1 - .02, 1.88), G, bevel=.006, segs=1)
        for i in range(2):
            a0 = x0 + .006 + i * (x1 - x0) / 2
            a1 = a0 + (x1 - x0) / 2 - .012
            dent = .006 if i == 1 else 0
            B(bm, (a0, y1 - .02, .06), (a1, y1 - dent, 1.84), G, bevel=.004, segs=1)
            for j in range(6):
                for z in (1.50, 1.30):
                    B(bm, (a0 + .04, y1, z + j * .022), (a1 - .04, y1 + .006, z + j * .022 + .008), DK)
                for z in (.26,):
                    B(bm, (a0 + .04, y1, z + j * .022), (a1 - .04, y1 + .006, z + j * .022 + .008), DK)
            B(bm, (a1 - .045, y1, .92), (a1 - .025, y1 + .03, 1.08), BARE, bevel=.003, segs=1)
        # padlock on locker 08
        C(bm, (x1 - .03, y1 + .045, .97), .02, .012, "Y", 10, YE)
        B(bm, (x1 - .048, y1 + .036, .90), (x1 - .012, y1 + .054, .95), BARE, bevel=.004, segs=1)
        # hard hat on top
        K.add_lathe(bm, [(.0, .13), (.05, .125), (.095, .10), (.115, .06), (.12, .02), (.15, .012), (.15, 0), (.0, 0)], ((x0 + x1) / 2, y0 + .24, 1.88), "Z", 14, YE)
    make("ST_S_Lockers", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Yellow"], ((x0 + x1) / 2, (y0 + y1) / 2, 0), build)
    fwd = Vector((0, 1, 0))
    return [("S", Vector((x0 + .11, y1 + .007, 1.72)), .05, .0375, "lock0", 0), ("S", Vector((x1 - .11, y1 + .007, 1.72)), .05, .0375, "lock1", 0),
            ("S", Vector((x0 + .11, y1 + .0015, .62)), .10, .067, "crew", -6), ("S", Vector((x1 - .10, y1 - .0045, .66)), .07, .07, "noentry", 4),
            ("S", Vector((x0 + .09, y1 + .0015, 1.22)), .06, .075, "photo", 3)]


def w_crates():
    def build(bm):
        G, BARE, DK, HZ = range(4)
        for (cx, cy, z, sx, sy, sz, yaw) in ((-2.80, -1.53, 0, .40, .38, .40, 0), (-2.78, -1.53, .40, .34, .30, .28, 8)):
            def one(bm=bm, cx=cx, cy=cy, z=z, sx=sx, sy=sy, sz=sz):
                B(bm, (-sx / 2, -sy / 2, z), (sx / 2, sy / 2, z + sz), G, bevel=.008, segs=1)
                for zz in (z + .02, z + sz - .05):
                    B(bm, (-sx / 2 - .006, -sy / 2 - .006, zz), (sx / 2 + .006, sy / 2 + .006, zz + .03), DK)
                for (a, b) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
                    xa, xb = (sx / 2 - .03, sx / 2 + .004) if a > 0 else (-sx / 2 - .004, -sx / 2 + .03)
                    ya, yb = (sy / 2 - .03, sy / 2 + .004) if b > 0 else (-sy / 2 - .004, -sy / 2 + .03)
                    B(bm, (xa, ya, z + .05), (xb, yb, z + sz - .05), HZ)
            m = Matrix.Rotation(math.radians(yaw), 3, "Z")
            K.xform(bm, one, m, (cx, cy, 0))
    make("ST_W_Crates", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_ZN_Hazard"], (-2.80, -1.53, 0), build)
    return [("X", Vector((-2.80 + .2015, -1.53, .22)), .30, .13, "crate", 0)]


def all_prints(nw_items, chest_items, ne_items, sw_items, s_items, w_items):
    def build(bm):
        bm.loops.layers.uv.verify()
        for k_name, items in (("NW", nw_items + chest_items), ("NE", ne_items), ("SW", sw_items)):
            k = SIDES.index(k_name)
            c, t, n = side(k)
            for s, d, z, w, h, key, roll in items:
                centre = c + t * s + n * d + Vector((0, 0, z))
                rot = Matrix.Rotation(math.radians(roll), 3, n)
                # Viewer's right when facing the wall is -t (normal = right x up must point into the room).
                K.print_quad(bm, centre, rot @ (-t), rot @ Vector((0, 0, 1)), w, h, PR.uv[key])
        for kind, centre, w, h, key, roll in s_items + w_items:
            n = Vector((0, 1, 0)) if kind == "S" else Vector((1, 0, 0))
            right = Vector((-1, 0, 0)) if kind == "S" else Vector((0, 1, 0))
            rot = Matrix.Rotation(math.radians(roll), 3, n)
            K.print_quad(bm, centre, rot @ right, rot @ Vector((0, 0, 1)), w, h, PR.uv[key])
    make("ST_Prints", ["M_ST_Prints"], (0, 0, 1.2), build, uv=None)


# ------------------------------------------------------------------------------------------------ export, report
def export(root, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    path = os.path.join(EXPORT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)
    return path


def report(root):
    bpy.context.view_layer.update()
    rows, total = [], 0
    for o in root.children_recursive:
        if o.type != "MESH":
            continue
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        total += tris
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        rows.append({"name": o.name, "tris": tris, "min": [round(min(v[i] for v in bb), 3) for i in range(3)], "max": [round(max(v[i] for v in bb), 3) for i in range(3)]})
    zones = {"dock_work": (-2.39, -1.65, -0.95, 0.95), "dock_side_access_L": (-3.00, -2.30, 0.95, 1.30),
             "dock_side_access_R": (-3.00, -2.30, -1.30, -0.95), "trade_customer_strip": (1.22, 1.555, -1.00, 0.95)}
    hits = [(o["name"], zn) for o in rows for zn, (x0, x1, y0, y1) in zones.items()
            if o["name"] != "ST_Prints" and o["min"][0] < x1 and o["max"][0] > x0 and o["min"][1] < y1 and o["max"][1] > y0]
    print("ClinicStorageV2 triangles", total, "zone hits", hits)
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
        json.dump({"triangles": total, "objects": rows, "zone_hits": hits}, f, ensure_ascii=False, indent=1)


def render():
    bpy.context.view_layer.update()
    SCENE.render.engine = "BLENDER_WORKBENCH"
    SCENE.display.shading.light = "STUDIO"
    SCENE.display.shading.color_type = "TEXTURE"
    SCENE.display.shading.show_shadows = True
    SCENE.display.shading.show_cavity = True
    SCENE.render.resolution_x, SCENE.render.resolution_y = 1400, 900
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    SCENE.collection.objects.link(cam)
    SCENE.camera = cam
    shots = {}
    for name, k, s in (("nw", 3, -.2), ("ne", 1, .6), ("sw", 5, 0)):
        tgt = wall_pt(k, s, .2, 1.0)
        loc = wall_pt(k, s, 3.4, 1.6)
        shots[name] = (loc, tgt, 26)
    shots["s"] = (Vector((1.17, -1.5, 1.5)), Vector((1.17, -3.2, 1.0)), 30)
    shots["w"] = (Vector((-1.4, -1.6, 1.3)), Vector((-2.8, -1.53, .35)), 32)
    for name, (loc, target, lens) in shots.items():
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = lens
        SCENE.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)


PR = None
textures = build_textures()
build_materials(textures)
root = K.empty("ClinicStorageV2", (0, 0, 0))
K.set_root(root)
for n, p in (("Ref_Origin", (0, 0, 0)), ("Ref_Head", (0, 1, 0)), ("Ref_Up", (0, 0, 1))):
    K.empty(n, p, root)
nw = nw_drawers()
chest = nw_chest()
ne = ne_cold()
sw = sw_limbs()
s_items = s_lockers()
w_items = w_crates()
all_prints(nw, chest, ne, sw, s_items, w_items)
report(root)
print("exported", export(root, "ClinicStorageV2"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "ClinicStorageV2.blend"))
if not NO_RENDER:
    render()
