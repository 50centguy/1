"""Clinic zones V2: setting pass on the trade counter (east) and the UNIT07 dock corner (west) of the four-prop clinic
(Docs/Integration/RepairPropsCyberBench/SETTING_PROPS.md, README section 11).

Room frame = Unity world, as CyberRoomKit: Blender x = Unity x, Blender y = Unity z (north), Blender z = Unity y (up);
Ref_Origin / Ref_Head / Ref_Up let Unity land each root exactly. Layout numbers come from
ArtSource/UnifiedClinic/build_unified_clinic.py (counter CNT_*, pads PAD_*, shelves SHELF_X0, dock, wall A = 3.40).
The old meshes' colliders stay in the scene, so every new body keeps to the old footprint.

Two exports:
  TradeCounterV2  surplus NSP customs counter: ribbed steel front, stainless top, intake trays on the logical pads,
                  status pods for the director's two lamps, an amber intake terminal facing the room (Unity writes its
                  text), receipt printer, bell, spike, stamps, cash box, calculator, ledger, desk fan, radio, operator
                  stool, slotted-angle racking with customers' tagged gear, a hand-painted tariff board, a CCTV camera.
  DockCornerV2    the UNIT07 dock's wall and floor: charging cabinet with meters and a seal, conduit, umbilical on a
                  hook, registration plate, service log, a child's drawing of the robot, spares shelf, caged bulkhead
                  lamp, cable tray, extinguisher, oil-stained mat and drip pan. The dock and the robot are not modelled.

Run: blender -b --factory-startup --python build_clinic_zones_v2.py -- <cc0_texture_dir> [--norender]
Outputs: Export/TradeCounterV2.fbx, Export/DockCornerV2.fbx, ClinicZonesV2.blend, Textures/*.png, report.json, Renders.
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
CC0 = ARGS[0] if ARGS and not ARGS[0].startswith("--") else os.path.join(HERE, "cc0")
NO_RENDER = "--norender" in ARGS
TEX = os.path.join(HERE, "Textures")
BENCH_TEX = os.path.join(HERE, "..", "RepairBenchV2", "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
for d in (EXPORT, RENDERS):
    os.makedirs(d, exist_ok=True)
FONT_HAND = "C:/Windows/Fonts/simkai.ttf"     # brush / marker lettering
FONT_BOLD = "C:/Windows/Fonts/simhei.ttf"     # stencils and print
RNG = np.random.default_rng(1021)

# ---- layout (build_unified_clinic.py, Unity space there; x stays, Unity z -> y, Unity y -> z) -----------------------
A = 3.40
CNT_XM = 1.90
CNT_Y0, CNT_Y1 = -1.00, 0.95
TOP = 0.95
ANGLED_LEN = 1.25
PAD = {"In": (1.84, -0.60), "Out": (1.84, 0.60)}
PAD_W, PAD_D = 0.40, 0.46       # x, y
LAMP_Y = {"In": -0.79, "Out": 0.79}
SHELF_X0 = 3.02
STOOL = (2.52, 0.0)
WALL_W = -3.28                  # front face of Unit07_WallFrame
S2 = math.sqrt(.5)
SEG1 = dict(o=(CNT_XM, CNT_Y0, 0), ex=(1, 0, 0), ey=(0, 1, 0), L=CNT_Y1 - CNT_Y0)
SEG2 = dict(o=(CNT_XM, CNT_Y0, 0), ex=(S2, S2, 0), ey=(S2, -S2, 0), L=ANGLED_LEN)   # ex points to the operator side

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene

MAT_SPEC = {
    # shared with the bench (Unity remaps these slots onto Assets/RepairProps/Bench/Materials)
    "M_BV2_SteelGreen": ("T_BV2_SteelGreen", .35, .32, None, 2.2),
    "M_BV2_SteelBare": ("T_BV2_SteelBare", .9, .48, None, 3.0),
    "M_BV2_Ivory": ("T_BV2_Ivory", 0, .25, None, 3.0),
    "M_BV2_WornRed": ("T_BV2_WornRed", .2, .35, None, 3.0),
    "M_BV2_Yellow": ("T_BV2_Yellow", 0, .3, None, 5.0),
    "M_BV2_Orange": ("T_BV2_Orange", 0, .3, None, 6.0),
    "M_BV2_Rubber": ("T_BV2_Rubber", 0, .15, None, 6.0),
    "M_BV2_DarkPlastic": ("T_BV2_Rubber", 0, .4, None, 4.0),
    "M_BV2_Sticky": ("T_BV2_Sticky", 0, .05, None, 1.0),
    "M_BV2_SealTape": ("T_BV2_SealTape", 0, .2, None, 1.0),
    "M_BV2_AssetStencil": ("T_BV2_AssetStencil", 0, .2, None, 1.0),
    "M_BV2_MeterFace": ("T_BV2_MeterFace", 0, .3, None, 1.0),
    "M_BV2_Brass": (None, .95, .55, None, 1.0),
    "M_BV2_Ceramic": (None, 0, .75, None, 1.0),
    "M_BV2_Hardboard": (None, 0, .2, None, 1.0),
    "M_BV2_Bulb": (None, 0, .6, (1.0, .78, .5), 1.0),
    "M_BV2_LedRed": (None, 0, .6, (1.0, .15, .1), 1.0),
    "M_BV2_LedGreen": (None, 0, .6, (.2, 1.0, .35), 1.0),
    # this pass
    "M_ZN_Stainless": ("T_ZN_Stainless", .8, .5, None, 1.2),
    "M_ZN_Cardboard": ("T_ZN_Cardboard", 0, .08, None, 3.0),
    "M_ZN_Vinyl": ("T_ZN_Vinyl", 0, .35, None, 4.0),
    "M_ZN_Hazard": ("T_ZN_Hazard", 0, .3, None, 6.0),
    "M_ZN_Bag": (None, 0, .75, None, 1.0),
    "M_ZN_Tape": (None, 0, .45, None, 1.0),
    "M_ZN_Mint": (None, 0, .45, None, 1.0),
    "M_ZN_Oil": (None, .2, .92, None, 1.0),
    "M_ZN_Rag": (None, 0, .1, None, 1.0),
    "M_ZN_PaperFlat": (None, 0, .05, None, 1.0),
    "M_ZN_FloorMat": ("T_ZN_FloorMat", 0, .3, None, 1.0),
    "M_ZN_CounterPrints": ("T_ZN_CounterPrints", 0, .1, None, 1.0),
    "M_ZN_DockPrints": ("T_ZN_DockPrints", 0, .15, None, 1.0),
    "M_ZN_TermGlass": ("T_ZN_TermGlass", 0, .85, (1.0, .55, .15), 1.0),
    "M_ZN_LampAmber": (None, 0, .6, (1.0, .55, .12), 1.0),
}
FLAT = {"M_BV2_Brass": (.70, .50, .22), "M_BV2_Ceramic": (.93, .91, .86), "M_BV2_Hardboard": (.30, .23, .16),
        "M_BV2_Bulb": (1, .92, .75), "M_BV2_LedRed": (.9, .1, .1), "M_BV2_LedGreen": (.2, .9, .3),
        "M_ZN_Bag": (.72, .76, .78), "M_ZN_Tape": (.80, .70, .50), "M_ZN_Mint": (.60, .80, .72), "M_ZN_Oil": (.04, .045, .05),
        "M_ZN_Rag": (.36, .32, .27), "M_ZN_PaperFlat": (.86, .83, .74), "M_ZN_LampAmber": (1, .6, .2)}
K.init(SCENE, CC0, TEX, MAT_SPEC)
for ch, rows in {"4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
                 "6": "00110 01000 10000 11110 10001 10001 01110", "8": "01110 10001 10001 01110 10001 10001 01110",
                 "9": "01110 10001 10001 01111 00001 00010 01100", "J": "00111 00010 00010 00010 00010 10010 01100",
                 "Q": "01110 10001 10001 10001 10101 10010 01101", "Z": "11111 00001 00010 00100 01000 10000 11111",
                 ".": "00000 00000 00000 00000 00000 01100 01100", "%": "11001 11010 00010 00100 01000 01011 10011",
                 "+": "00000 00100 00100 11111 00100 00100 00000", ":": "00000 01100 01100 00000 01100 01100 00000",
                 "#": "01010 11111 01010 01010 01010 11111 01010", "!": "00100 00100 00100 00100 00100 00000 00100",
                 " ": "00000 00000 00000 00000 00000 00000 00000"}.items():
    K.FONT.setdefault(ch, rows)

B, C, SW = K.add_box, K.add_cyl, K.add_sweep
hexc = K.hexc

# =====================================================================================================================
# Textures
# =====================================================================================================================
_PL = []


def noise(w, h, seed=0):
    """Zero-mean grain (from the CC0 plastic set) cut to w x h at a seed-dependent offset."""
    if not _PL:
        _PL.append(K.lum(K.load_rgb("Plastic010")))
    pl = _PL[0]
    reps = (h // pl.shape[0] + 2, w // pl.shape[1] + 2)
    t = np.tile(pl, reps)
    oy, ox = (seed * 211) % pl.shape[0], (seed * 137) % pl.shape[1]
    t = t[oy:oy + h, ox:ox + w]
    return t - t.mean()


def paper(w, h, base="#E4DDC5", seed=0, grain=.16):
    return np.ones((h, w, 3)) * hexc(base) * (1 + grain * noise(w, h, seed))[..., None]


def hand(img, text, x, y, w, h, color, font=FONT_HAND, align="CENTER", wobble=.006, alpha=1.0, pad=.05, seed=0):
    """Lettering into the box (x, y-from-top, w, h)."""
    m = K.text_mask(text, font, w, h, pad=pad, align=align, wobble=wobble, seed=seed)
    K.paint(img, m, color, alpha, x, img.shape[0] - y - h)


def pix(img, text, x, y, scale, color):
    K.blit_text(img, text, x, y, scale, color)


def rect(img, x0, y0, x1, y1, color, alpha=1.0):
    """Filled rectangle, y from the top."""
    H = img.shape[0]
    c = hexc(color) if isinstance(color, str) else np.asarray(color)
    img[H - y1:H - y0, x0:x1] = img[H - y1:H - y0, x0:x1] * (1 - alpha) + c * alpha


def stroke(img, pts, color, width=2.0, jitter=0.0, gaps=0.0, seed=0):
    """Crayon / marker polyline (pts in pixels, y from the top): discs stamped along it, optionally broken up."""
    rng = np.random.default_rng(seed)
    H, W = img.shape[:2]
    col = hexc(color) if isinstance(color, str) else color
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
        for t in np.linspace(0, 1, n):
            if gaps and rng.random() < gaps:
                continue
            cx = x0 + (x1 - x0) * t + rng.normal(0, jitter)
            cy = y0 + (y1 - y0) * t + rng.normal(0, jitter)
            r = width / 2
            ys, xs = np.mgrid[int(cy - r):int(cy + r) + 1, int(cx - r):int(cx + r) + 1]
            ok = ((xs - cx) ** 2 + (ys - cy) ** 2 <= r * r) & (xs >= 0) & (xs < W) & (ys >= 0) & (ys < H)
            img[H - 1 - ys[ok], xs[ok]] = col


def circle_pts(cx, cy, r, n=24, ry=None):
    return [(cx + r * math.cos(a), cy + (ry or r) * math.sin(a)) for a in np.linspace(0, 2 * math.pi, n)]


def scribble(img, x0, y0, x1, y1, color, rows, seed=0):
    rng = np.random.default_rng(seed)
    H = img.shape[0]
    for k in range(rows):
        y = y0 + k * (y1 - y0) / max(rows, 1)
        x = x0
        while x < x1 - 6:
            w = int(rng.integers(5, 18))
            for j in range(min(w, x1 - x)):
                yy = int(H - 1 - (y + 1.5 * math.sin((x + j) * .8 + k)))
                img[max(0, yy - 1):yy + 1, x + j] = hexc(color)
            x += w + int(rng.integers(3, 7))


def tape_corners(img, color="#D8CDA8"):
    h, w = img.shape[:2]
    for (x, y) in ((0, 0), (w - 18, 0)):
        rect(img, x, y, x + 18, y + 9, color, .75)


class Sheet:
    """Simple shelf packer for one print texture; rects come back as UV (u0, v0, u1, v1), half-pixel inset."""

    def __init__(self, name, w, h, base="#30302C"):
        self.name, self.W, self.H = name, w, h
        self.img = np.ones((h, w, 3)) * hexc(base)
        self.x = self.y = self.row = 0
        self.uv = {}

    def add(self, key, art):
        h, w = art.shape[:2]
        if self.x + w > self.W:
            self.x, self.y, self.row = 0, self.y + self.row + 2, 0
        assert self.y + h <= self.H, (self.name, key, self.y, h)
        self.img[self.y:self.y + h, self.x:self.x + w] = art
        self.uv[key] = ((self.x + .5) / self.W, (self.y + .5) / self.H, (self.x + w - .5) / self.W, (self.y + h - .5) / self.H)
        self.x += w + 2
        self.row = max(self.row, h)

    def save(self):
        return K.save_png(self.name, self.img)


TAG_NUMBERS = ["038", "041", "044", "047", "052", "053", "058", "061", "063", "066", "070", "072"]


def counter_prints():
    s = Sheet("T_ZN_CounterPrints", 1024, 1024)
    ink, red, amber, cream, blue = hexc("#1E2024"), hexc("#B02A1E"), hexc("#F0B04A"), hexc("#E6DDBF"), hexc("#24346A")
    # tariff board: dark green paint, cream and amber brush lettering
    t = np.ones((200, 640, 3)) * hexc("#1C2523") * (1 + .35 * noise(640, 200, 3))[..., None]
    hand(t, "价目 RATES", 14, 6, 300, 52, amber, seed=1)
    hand(t, "概不赊账 NO CREDIT", 330, 10, 296, 44, hexc("#D8452F"), seed=2)
    rows = [("检测 DIAG", "50"), ("清洁 CLEAN", "80"), ("换件 PARTS", "料+工"), ("封签 SEAL", "120"), ("加急 RUSH", "+50%"), ("夜间 NIGHT", "+20")]
    for i, (a, b) in enumerate(rows):
        col, r = i // 3, i % 3
        x, y = 16 + col * 312, 66 + r * 42
        hand(t, a, x, y, 196, 38, cream, align="LEFT", seed=10 + i)
        hand(t, b, x + 196, y, 100, 38, amber, align="RIGHT", seed=20 + i)
    for k in range(40):   # chalky smudges
        x, y = RNG.integers(0, 600), RNG.integers(0, 180)
        rect(t, int(x), int(y), int(x) + int(RNG.integers(8, 40)), int(y) + 3, "#55605A", .25)
    s.add("tariff", t)
    # notices taped to the counter front
    n = paper(128, 176, "#ECE8DC", 4)
    hand(n, "概不赊账", 6, 20, 116, 60, red, seed=3)
    hand(n, "NO CREDIT", 6, 86, 116, 30, red, seed=4)
    hand(n, "—— 店主", 40, 136, 80, 22, ink, seed=5)
    tape_corners(n)
    s.add("nocredit", n)
    ls = paper(128, 176, "#E7C64A", 5)
    pix(ls, "NSP", 46, 8, 2, ink)
    hand(ls, "租赁设备", 6, 28, 116, 34, ink, font=FONT_BOLD, wobble=0, seed=6)
    hand(ls, "封签破损\n不予维修", 6, 66, 116, 60, red, font=FONT_BOLD, wobble=0, seed=7)
    pix(ls, "VOID IF", 22, 136, 2, red)
    pix(ls, "OPENED", 28, 154, 2, red)
    tape_corners(ls)
    s.add("lease", ls)
    fl = paper(128, 208, "#EEEDE6", 6)
    hand(fl, "寻", 4, 4, 48, 48, ink, font=FONT_BOLD, wobble=0)
    pix(fl, "UNIT 12", 54, 20, 2, ink)
    rect(fl, 12, 56, 116, 126, "#8E9894")
    rect(fl, 46, 66, 82, 92, "#2C3236")     # robot: head, body, legs
    rect(fl, 40, 94, 88, 116, "#2C3236")
    rect(fl, 52, 72, 76, 86, "#9CB8A8")
    hand(fl, "酬谢 300", 8, 130, 112, 26, red, seed=8)
    for k in range(7):                       # tear-off tabs, two already taken
        x0 = 6 + k * 17
        if k in (2, 5):
            rect(fl, x0, 166, x0 + 15, 208, "#3A403D")
        else:
            rect(fl, x0 + 15, 166, x0 + 16, 208, "#8A8A84")
            for j in range(6):
                rect(fl, x0 + 6, 170 + j * 6, x0 + 9, 173 + j * 6, ink)
    tape_corners(fl)
    s.add("flyer", fl)
    st = np.ones((112, 256, 3)) * hexc("#56605B") * (1 + .3 * noise(256, 112, 7))[..., None]
    chip = (noise(256, 112, 9) > -.02)
    pix(st, "NSP CUSTOMS 03", 18, 14, 3, cream)
    m = K.text_mask("收件 · 取件 · 维修", FONT_BOLD, 236, 46, pad=.04)
    K.paint(st, m * chip[:46, :236], cream, .9, 10, 112 - 56 - 46)
    for (x, y) in ((6, 6), (246, 6), (6, 102), (246, 102)):
        rect(st, x - 2, y - 2, x + 3, y + 3, "#2A2F2D")
    s.add("stencil", st)
    for key, text in (("trayIn", "IN 收件"), ("trayOut", "OUT 取件")):
        tl = paper(176, 56, "#D9CBA0", 8)
        hand(tl, text, 8, 6, 160, 44, ink, font=FONT_BOLD, wobble=.01, seed=len(key))
        tl[:, :4] *= .8
        tl[:, -4:] *= .8
        s.add(key, tl)
    rc = paper(48, 224, "#EFEEE8", 9, .06)
    pix(rc, "NSP", 14, 6, 1, ink)
    for k in range(18):
        y = 20 + k * 11
        for x in range(4, 44, 3):
            if RNG.random() < .7:
                rect(rc, x, y, x + 2, y + 2, "#55607A")
    pix(rc, "80", 26, 210, 1, ink)
    s.add("receipt", rc)
    lg = paper(256, 160, "#E8E0C6", 10)
    for y in range(18, 160, 10):
        rect(lg, 0, y, 256, y + 1, "#9FB0C8")
    rect(lg, 26, 0, 27, 160, "#C46A5A")
    rect(lg, 154, 0, 155, 160, "#C46A5A")
    rect(lg, 127, 0, 129, 160, "#B9AF94")
    scribble(lg, 30, 22, 122, 150, "#24346A", 13, 11)
    scribble(lg, 158, 22, 250, 110, "#24346A", 9, 12)
    rect(lg, 158, 120, 230, 122, "#B02A1E")
    s.add("ledger", lg)
    for i, num in enumerate(TAG_NUMBERS):
        tg = paper(40, 64, "#D6C28A", 20 + i)
        for a in np.linspace(0, 2 * math.pi, 20):
            x, y = int(20 + 5 * math.cos(a)), int(9 + 5 * math.sin(a))
            tg[64 - y, x] = hexc("#9C8650")
        rect(tg, 18, 7, 23, 12, "#4A4030")
        pix(tg, num, 2, 22, 2, blue)
        scribble(tg, 4, 44, 36, 60, "#24346A", 2, 30 + i)
        s.add("tag%d" % i, tg)
    fr = paper(96, 40, "#ECEBE4", 30)
    pix(fr, "FRAGILE", 6, 4, 2, red)
    hand(fr, "易碎", 30, 18, 40, 22, red, font=FONT_BOLD, wobble=0)
    s.add("fragile", fr)
    nb = np.ones((40, 96, 3)) * hexc("#9C7448") * (1 + .2 * noise(96, 40, 31))[..., None]
    pix(nb, "NSP 0742", 2, 12, 2, hexc("#1A1A1A"))
    s.add("nspbox", nb)
    crt = paper(96, 40, "#ECEBE4", 32)
    pix(crt, "CRT 14", 12, 6, 2, ink)
    pix(crt, "TUBE", 24, 24, 2, ink)
    s.add("crtbox", crt)
    return s


def dock_prints():
    s = Sheet("T_ZN_DockPrints", 512, 512)
    ink, red = hexc("#1E2024"), hexc("#B02A1E")
    # a child's crayon drawing of UNIT07 ("小七") holding someone's hand under a sun
    d = paper(192, 144, "#F3F0E6", 40, .08)
    stroke(d, circle_pts(26, 24, 13), "#F0A030", 3, .6, .1, 1)
    for a in np.linspace(0, 2 * math.pi, 9)[:-1]:
        stroke(d, [(26 + 17 * math.cos(a), 24 + 17 * math.sin(a)), (26 + 25 * math.cos(a), 24 + 25 * math.sin(a))], "#F0A030", 2.5, .5, 0, 2)
    stroke(d, [(70, 30), (118, 30), (118, 70), (70, 70), (70, 30)], "#3FA34D", 3.5, .8, .05, 3)       # screen head
    for y in range(36, 66, 4):
        stroke(d, [(74, y), (114, y)], "#9ED8A8", 2, .4, .3, 4 + y)
    stroke(d, [(82, 44), (82, 50)], "#2C6A34", 3, .3, 0, 5)
    stroke(d, [(106, 44), (106, 50)], "#2C6A34", 3, .3, 0, 6)
    stroke(d, [(80, 56), (88, 62), (100, 62), (108, 56)], "#2C6A34", 3, .3, 0, 7)                     # smile
    stroke(d, [(80, 72), (108, 72), (108, 110), (80, 110), (80, 72)], "#3D6FB6", 3, .8, .05, 8)       # body
    stroke(d, [(80, 80), (60, 96)], "#3D6FB6", 3, .6, 0, 9)
    stroke(d, [(108, 80), (128, 92)], "#3D6FB6", 3, .6, 0, 10)
    stroke(d, [(86, 110), (84, 132)], "#3D6FB6", 3, .6, 0, 11)
    stroke(d, [(102, 110), (104, 132)], "#3D6FB6", 3, .6, 0, 12)
    stroke(d, circle_pts(150, 74, 8), "#D0412F", 3, .5, 0, 13)                                        # the kid
    stroke(d, [(150, 82), (150, 112)], "#D0412F", 3, .5, 0, 14)
    stroke(d, [(150, 90), (128, 92)], "#D0412F", 3, .5, 0, 15)
    stroke(d, [(150, 112), (142, 132)], "#D0412F", 3, .5, 0, 16)
    stroke(d, [(150, 112), (158, 132)], "#D0412F", 3, .5, 0, 17)
    hand(d, "小七", 136, 8, 52, 34, hexc("#6A4FA0"), wobble=.02, seed=18)
    stroke(d, [(166, 46), (170, 42), (174, 46), (170, 52), (166, 46)], "#D0412F", 2, .3, 0, 19)
    tape_corners(d, "#CFC6A6")
    s.add("drawing", d)
    lg = paper(144, 192, "#E9E4D2", 41)
    hand(lg, "UNIT 07 维护记录", 4, 4, 136, 26, ink, font=FONT_BOLD, wobble=0)
    rect(lg, 4, 32, 140, 33, "#5A5E66")
    for k, (date, n) in enumerate((("10-03", 3), ("10-06", 5), ("10-09", 2), ("10-11", 7), ("10-14", 4), ("10-16", 6))):
        y = 40 + k * 20
        pix(lg, date, 6, y + 4, 1, hexc("#24346A"))
        for j in range(n):
            x = 50 + (j % 5) * 7 + (j // 5) * 42
            stroke(lg, [(x, y + 3), (x + 1, y + 14)], "#24346A", 1.6, .2, 0, k * 10 + j)
            if j % 5 == 4:
                stroke(lg, [(x - 30, y + 12), (x + 3, y + 5)], "#24346A", 1.4, .2, 0, k * 10 + j + 5)
        rect(lg, 4, y + 18, 140, y + 19, "#C9C2AE")
    hand(lg, "10-17 轴承异响?", 4, 162, 136, 24, red, seed=42)
    s.add("log", lg)
    pl = np.ones((96, 256, 3)) * hexc("#B9BEBD")
    pl *= (1 + .06 * np.repeat(RNG.normal(0, 1, (96, 1)), 256, 1))[..., None]
    for (x, y) in ((8, 8), (248, 8), (8, 88), (248, 88)):
        rect(pl, x - 3, y - 3, x + 3, y + 3, "#6A6E6E")
    pix(pl, "NSP LABOUR UNIT", 20, 10, 2, ink)
    pix(pl, "REG 07-0742", 22, 32, 3, ink)
    pix(pl, "CLASS C  LEASED", 20, 62, 2, ink)
    pix(pl, "DO NOT REMOVE", 50, 80, 1, red)
    s.add("plate", pl)
    dg = np.ones((72, 80, 3)) * hexc("#2A2A28")
    for y in range(72):
        half = int((y - 4) * .6) if y > 4 else 0
        if 4 < y < 56:
            dg[71 - y, 40 - half:40 + half] = hexc("#E2B53A")
    stroke(dg, [(40, 20), (40, 40)], "#1A1A1A", 4, 0, 0, 1)
    stroke(dg, [(40, 46), (40, 48)], "#1A1A1A", 4, 0, 0, 2)
    hand(dg, "高压 48V", 2, 56, 76, 16, hexc("#E2B53A"), font=FONT_BOLD, wobble=0)
    s.add("danger", dg)
    cl = np.ones((40, 128, 3)) * hexc("#1C1E1D")
    pix(cl, "DOCK 48V", 16, 6, 2, hexc("#D7D0B8"))
    pix(cl, "CHG  RUN  ERR", 8, 26, 1, hexc("#D7D0B8"))
    s.add("cablabel", cl)
    sp = paper(40, 64, "#D6C28A", 43)
    pix(sp, "07", 8, 20, 3, hexc("#24346A"))
    hand(sp, "备件", 2, 44, 36, 18, hexc("#24346A"))
    s.add("sparetag", sp)
    return s


def build_textures():
    t = {}
    for name in ("T_BV2_SteelGreen", "T_BV2_SteelBare", "T_BV2_Ivory", "T_BV2_WornRed", "T_BV2_Yellow", "T_BV2_Orange",
                 "T_BV2_Rubber", "T_BV2_Sticky", "T_BV2_SealTape", "T_BV2_AssetStencil", "T_BV2_MeterFace"):
        t[name] = bpy.data.images.load(os.path.abspath(os.path.join(BENCH_TEX, name + ".png")), check_existing=True)
    # brushed stainless: row streaks, scratches, dents, faint sticker residue
    S = 512
    base = K.lum(K.down(K.load_rgb("Metal032"), S))
    rows = np.convolve(RNG.normal(0, 1, S + 16), np.ones(9) / 9, "same")[8:S + 8]
    st = np.ones((S, S, 3)) * hexc("#A2A7A8") * (1 + .10 * (base - base.mean()) + .035 * rows[:, None])[..., None]
    for _ in range(420):
        x0, y0 = RNG.uniform(0, S, 2)
        a, L = RNG.normal(0, .5), RNG.uniform(6, 60)
        c = hexc("#D2D6D6") if RNG.random() < .6 else hexc("#6E7374")
        for t_ in np.linspace(0, 1, int(L)):
            x, y = int(x0 + math.cos(a) * L * t_) % S, int(y0 + math.sin(a) * L * t_) % S
            st[y, x] = st[y, x] * .55 + c * .45
    yy, xx = np.mgrid[0:S, 0:S]
    for _ in range(14):
        cx, cy, r = RNG.uniform(0, S, 2).tolist() + [RNG.uniform(6, 18)]
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
        st *= (1 - .18 * np.exp(-(d / r) ** 2))[..., None]
    for _ in range(4):
        x, y = RNG.integers(0, S - 90, 2)
        st[y:y + int(RNG.integers(30, 60)), x:x + int(RNG.integers(50, 90))] *= 1.07
    t["T_ZN_Stainless"] = K.save_png("T_ZN_Stainless", st)
    # kraft cardboard: flute shading, stains, a tape band
    S = 256
    cb = np.ones((S, S, 3)) * hexc("#9A7046") * (1 + .18 * noise(S, S, 50) + .04 * np.sin(np.arange(S) * 2 * math.pi / 6)[None, :])[..., None]
    for _ in range(5):
        cx, cy, r = RNG.uniform(0, S, 2).tolist() + [RNG.uniform(10, 30)]
        d = np.sqrt((np.mgrid[0:S, 0:S][1] - cx) ** 2 + (np.mgrid[0:S, 0:S][0] - cy) ** 2)
        cb *= (1 - .22 * ((d < r) & (d > r - 3)))[..., None]
    cb[112:144] = cb[112:144] * .5 + hexc("#C9A86A") * .5
    t["T_ZN_Cardboard"] = K.save_png("T_ZN_Cardboard", cb)
    # cracked black vinyl with grey tape repairs
    S = 128
    vn = np.ones((S, S, 3)) * hexc("#232527") * (1 + .3 * noise(S, S, 60))[..., None]
    for _ in range(14):
        x, y = RNG.uniform(0, S, 2)
        for _ in range(int(RNG.integers(8, 25))):
            x, y = (x + RNG.normal(0, 1.5)) % S, (y + RNG.normal(0, 1.5)) % S
            vn[int(y), int(x)] = hexc("#5A5C5E")
    vn[40:58, :] = hexc("#8C8B86") * (1 + .1 * noise(S, 18, 61))[..., None]
    vn[:, 70:86] = hexc("#8C8B86") * (1 + .1 * noise(16, S, 62))[..., None]
    t["T_ZN_Vinyl"] = K.save_png("T_ZN_Vinyl", vn)
    yy, xx = np.mgrid[0:64, 0:64]
    stripe = ((xx + yy) // 16) % 2 == 0
    hz = np.where(stripe[..., None], hexc("#D9B13A"), hexc("#1C1C1A")) * (1 + .25 * noise(64, 64, 63))[..., None]
    t["T_ZN_Hazard"] = K.save_png("T_ZN_Hazard", hz)
    # dock floor mat (0.86 x 1.84 m): ribbed rubber, oil stains, scuffs, lighter border
    W, H = 256, 512
    rub = K.lum(K.down(K.load_rgb("Rubber004"), 512))[:H, :W]
    fm = np.ones((H, W, 3)) * hexc("#2A2D2E") * (1 + .35 * (rub - rub.mean()))[..., None]
    fm[::5] *= .8
    fy, fx = np.mgrid[0:H, 0:W]
    for _ in range(9):
        cx, cy = RNG.uniform(30, W - 30), RNG.uniform(40, H - 40)
        rx, ry = RNG.uniform(14, 46), RNG.uniform(10, 34)
        d = ((fx - cx) / rx) ** 2 + ((fy - cy) / ry) ** 2
        fm = fm * (1 - .55 * np.clip(1.2 - d, 0, 1)[..., None]) + hexc("#2C3A3A") * (.25 * np.exp(-((d - 1) / .08) ** 2))[..., None]
    fm[:6], fm[-6:], fm[:, :6], fm[:, -6:] = fm[:6] * 1.5, fm[-6:] * 1.5, fm[:, :6] * 1.5, fm[:, -6:] * 1.5
    t["T_ZN_FloorMat"] = K.save_png("T_ZN_FloorMat", fm)
    # amber terminal glass: dark phosphor, vignette, faint scanlines, a little burn-in
    gw, gh = 256, 192
    gy, gx = np.mgrid[0:gh, 0:gw]
    v = np.clip(1 - ((gx / gw - .5) ** 2 + (gy / gh - .5) ** 2) * 2.0, 0, 1)
    tg = np.stack([v * .16, v * .085, v * .02], -1) * (1 - .25 * ((np.arange(gh) % 3 == 0)[:, None]))[..., None]
    burn = np.zeros((gh, gw, 3))
    pix(burn, "NSP", 90, 80, 5, np.array([.05, .03, .01]))
    t["T_ZN_TermGlass"] = K.save_png("T_ZN_TermGlass", tg + burn)
    global CP, DP
    CP, DP = counter_prints(), dock_prints()
    t["T_ZN_CounterPrints"] = CP.save()
    t["T_ZN_DockPrints"] = DP.save()
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


# =====================================================================================================================
# Geometry helpers
# =====================================================================================================================


def make(name, mats, origin, build, parent=None, uv="box", role="static", smooth=35):
    bm = K.bm_new()
    build(bm)
    return K.new_obj(name, bm, mats, origin=origin, parent=parent, uv=uv, smooth_angle=smooth, role=role)


def seg_box(bm, seg, a0, a1, z0, z1, b0, b1, mat=0, bevel=0.0):
    K.framed(bm, lambda: B(bm, (a0, b0, z0), (a1, b1, z1), mat, bevel=bevel, segs=1), seg["o"], seg["ex"], seg["ey"])


def seg_point(seg, a, b, z=0.0):
    return Vector(seg["o"]) + Vector(seg["ex"]) * a + Vector(seg["ey"]) * b + Vector((0, 0, z))


def hull_prism(bm, pts, z0, z1, mat=0):
    vs = [bm.verts.new((p[0], p[1], z)) for p in pts for z in (z0, z1)]
    res = bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.delete(bm, geom=[v for v in res["geom_interior"] + res["geom_unused"] if isinstance(v, bmesh.types.BMVert)], context="VERTS")
    for f in res["geom"]:
        if isinstance(f, bmesh.types.BMFace) and f.is_valid:
            f.material_index = mat


def corner_pts(a0, a1, b0=-0.02):
    """Outline of the wedge between the straight and the angled counter segment (their start ends)."""
    return [seg_point(SEG1, a, b0).to_2d() for a in (a0, a1)] + [seg_point(SEG2, a, b0).to_2d() for a in (a0, a1)]


def look_matrix(forward, up=(0, 0, 1)):
    """Columns: local x -> forward, local y -> left, local z -> up (right-handed)."""
    f = Vector(forward).normalized()
    left = Vector(up).cross(f).normalized()
    u = f.cross(left)
    return Matrix(((f.x, left.x, u.x), (f.y, left.y, u.y), (f.z, left.z, u.z)))


def facing_quad(bm, centre, forward, w, h, rect, roll=0.0, mat=0):
    """Print facing `forward` (the side a viewer stands on), right = viewer's right, optional in-plane roll (deg)."""
    n = Vector(forward).normalized()
    right = (-n).cross(Vector((0, 0, 1)))
    right = right.normalized() if right.length > 1e-6 else Vector((1, 0, 0))
    up = n.cross(right).normalized()
    rot = Matrix.Rotation(math.radians(roll), 3, n)
    return K.print_quad(bm, centre, rot @ right, rot @ up, w, h, rect, mat)


def flat_quad(bm, centre, w, h, rect, yaw_deg=0.0, mat=0):
    """Print lying flat (normal +z), read by someone standing at -x looking +x: text up = +x, right = -y."""
    a = math.radians(yaw_deg)
    up = Vector((math.cos(a), math.sin(a), 0))
    right = Vector((math.sin(a), -math.cos(a), 0))
    return K.print_quad(bm, centre, right, up, w, h, rect, mat)


def string(bm, p0, p1, sag=.01, mat=0):
    p0, p1 = Vector(p0), Vector(p1)
    mid = (p0 + p1) / 2 - Vector((0, 0, sag))
    SW(bm, [p0, mid, p1], .0011, 3, mat)


# =====================================================================================================================
# Trade counter
# =====================================================================================================================
TAGS = []        # (attach point, tag centre, uv key) for the claim tags: strings and prints built at the end


def counter_body():
    def body(bm):
        for seg in (SEG1, SEG2):
            L = seg["L"]
            seg_box(bm, seg, -0.27, 0.0, 0.10, 0.90, 0.0, L, 0)                  # customer half, painted steel
            seg_box(bm, seg, -0.25, -0.02, 0.0, 0.10, 0.02, L - 0.02, 3)         # recessed plinth
            seg_box(bm, seg, -0.287, -0.27, 0.10, 0.17, 0.0, L, 2)               # hazard toe band
            seg_box(bm, seg, -0.29, -0.27, 0.86, 0.90, 0.0, L, 1)                # steel band under the top
            b = 0.05
            while b < L - 0.03:                                                   # ribbed cladding
                seg_box(bm, seg, -0.283, -0.27, 0.17, 0.86, b - .011, b + .011, 0)
                b += 0.074
            n = max(1, int(L / 0.5))
            for i in range(1, n + 1):                                             # cover strips with rivets
                bb = i * L / (n + 1) if i <= n else L
                seg_box(bm, seg, -0.292, -0.27, 0.10, 0.86, bb - .018, bb + .018, 1)
                for z in (0.20, 0.48, 0.76):
                    K.framed(bm, lambda z=z, bb=bb: C(bm, (-0.294, bb, z), .006, .006, "X", 6, 1), seg["o"], seg["ex"], seg["ey"])
            seg_box(bm, seg, 0.0, 0.28, 0.0, 0.90, L - 0.04, L, 0)               # end panel
            seg_box(bm, seg, 0.02, 0.28, 0.30, 0.33, 0.04, L - 0.04, 1)          # operator under-shelf
            seg_box(bm, seg, -0.02, 0.0, 0.10, 0.90, 0.0, L, 3)                  # body back skin
        B(bm, (1.6065, -.31, .42), (1.6125, .31, .70), 1)                        # stencil plate riveted on the ribs
        hull_prism(bm, corner_pts(-0.27, 0.12), 0.10, 0.90, 0)
        hull_prism(bm, corner_pts(-0.25, 0.10, 0.02), 0.0, 0.10, 3)
        hull_prism(bm, corner_pts(-0.287, -0.25), 0.10, 0.17, 2)
    make("ZC_Counter", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_ZN_Hazard", "M_BV2_DarkPlastic"], (CNT_XM, 0, 0.45), body)

    def top(bm):
        for seg in (SEG1, SEG2):
            L = seg["L"]
            seg_box(bm, seg, -0.32, 0.32, 0.905, TOP, -0.02, L + 0.02, 0, bevel=.003)
            K.framed(bm, lambda L=L: C(bm, (-0.30, L / 2, 0.927), .024, L + 0.04, "Y", 8, 1), seg["o"], seg["ex"], seg["ey"])
        hull_prism(bm, corner_pts(-0.32, 0.32), 0.905, TOP, 0)
        SW(bm, [seg_point(SEG1, -0.30, -0.02, 0.927), seg_point(SEG2, -0.30, -0.02, 0.927)], .024, 8, 1)
    make("ZC_CounterTop", ["M_ZN_Stainless", "M_BV2_Rubber"], (CNT_XM, 0, TOP), top)


def intake_trays():
    for key, mat in (("In", "M_BV2_Orange"), ("Out", "M_BV2_SteelGreen")):
        cx, cy = PAD[key]

        def tray(bm, cx=cx, cy=cy):
            x0, x1, y0, y1 = cx - PAD_W / 2, cx + PAD_W / 2, cy - PAD_D / 2, cy + PAD_D / 2
            B(bm, (x0, y0, TOP), (x1, y1, TOP + .004), 2)                  # galvanised floor, painted rim
            t, h = .006, TOP + .026
            B(bm, (x0, y0, TOP), (x1, y0 + t, h), 0)
            B(bm, (x0, y1 - t, TOP), (x1, y1, h), 0)
            B(bm, (x1 - t, y0, TOP), (x1, y1, h), 0)
            B(bm, (x0, y0, TOP), (x0 + t, cy - .06, TOP + .014), 0)    # low customer lip with a finger gap
            B(bm, (x0, cy + .06, TOP), (x0 + t, y1, TOP + .014), 0)
            for sy in (y0 + .02, y1 - .02):                               # rubber feet strips
                B(bm, (x0 + .03, sy - .006, TOP + .004), (x1 - .03, sy + .006, TOP + .0055), 1)
        make("ZC_Tray_" + key, [mat, "M_BV2_Rubber", "M_BV2_SteelBare"], (cx, cy, TOP), tray)


def status_pods():
    def pods(bm):
        for y in LAMP_Y.values():
            B(bm, (1.600, y - .02, TOP), (1.640, y + .02, TOP + .18), 0)
            B(bm, (1.587, y - .047, TOP + .178), (1.660, y + .047, TOP + .264), 1, bevel=.004, segs=1)
            B(bm, (1.572, y - .052, TOP + .264), (1.664, y + .052, TOP + .272), 2)
            for dy in (-.032, .032):
                SW(bm, [(1.571, y + dy, TOP + .182), (1.567, y + dy, TOP + .222), (1.571, y + dy, TOP + .262)], .0026, 5, 2)
            SW(bm, [(1.567, y - .032, TOP + .222), (1.567, y + .032, TOP + .222)], .0026, 5, 2)
            C(bm, (1.61, y, TOP + .006), .03, .012, "Z", 10, 0)
    make("ZC_StatusPods", ["M_BV2_DarkPlastic", "M_BV2_SteelGreen", "M_BV2_SteelBare"], (1.62, 0, TOP + .2), pods)


def terminal():
    def body(bm):
        B(bm, (2.00, -.15, TOP), (2.20, .15, TOP + .02), 1)                       # plinth
        B(bm, (1.995, -.17, TOP + .02), (2.205, .17, TOP + .33), 0, bevel=.012, segs=1)
        B(bm, (1.978, -.15, TOP + .055), (1.996, -.13, TOP + .30), 1)             # bezel frame around the glass
        B(bm, (1.978, .13, TOP + .055), (1.996, .15, TOP + .30), 1)
        B(bm, (1.978, -.15, TOP + .055), (1.996, .15, TOP + .075), 1)
        B(bm, (1.978, -.15, TOP + .28), (1.996, .15, TOP + .30), 1)
        for k in range(6):                                                        # side vents
            z = TOP + .14 + k * .022
            B(bm, (2.06, -.172, z), (2.17, -.168, z + .008), 1)
            B(bm, (2.06, .168, z), (2.17, .172, z + .008), 1)
        C(bm, (1.99, .10, TOP + .035), .008, .01, "X", 8, 2)                      # power knob
        C(bm, (1.99, .07, TOP + .035), .006, .01, "X", 8, 2)
    make("ZC_Terminal", ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare"], (2.10, 0, TOP), body)

    def glass(bm):
        bm.loops.layers.uv.verify()
        K.print_quad(bm, (1.985, 0, TOP + .1775), (0, -1, 0), (0, 0, 1), .26, .205, (0, 0, 1, 1), 0)
    make("ZC_Terminal_Glass", ["M_ZN_TermGlass"], (1.985, 0, TOP + .1775), glass, uv=None)

    def keyboard(bm):
        B(bm, (1.66, -.17, TOP), (1.82, .17, TOP + .022), 0, bevel=.005, segs=1)
        for r in range(4):
            x = 1.675 + r * .033
            for k in range(8):
                y = -.15 + k * .0375
                B(bm, (x, y, TOP + .022), (x + .026, y + .03, TOP + .034), 1)
        B(bm, (1.675 + 4 * .033, -.08, TOP + .022), (1.675 + 4 * .033 + .022, .08, TOP + .032), 1)   # space bar row
        pts = K.bezier((1.82, .12, TOP + .01), (1.90, .14, TOP + .005), (1.93, .20, TOP + .02), (2.00, .16, TOP + .04), 14)
        coil = [(p[0], p[1] + .006 * math.sin(i * 2.4), p[2] + .006 * math.cos(i * 2.4)) for i, p in enumerate(pts)]
        SW(bm, coil, .004, 5, 1)
    make("ZC_Keyboard", ["M_BV2_Ivory", "M_BV2_DarkPlastic"], (1.74, 0, TOP), keyboard)


def counter_kit():
    """Printer, spike, stamps, ink pad, bell, pen on a chain, cash box, desk fan."""
    def printer(bm):
        B(bm, (2.05, -.80, TOP), (2.21, -.54, TOP + .085), 0, bevel=.008, segs=1)
        B(bm, (2.07, -.72, TOP + .085), (2.19, -.62, TOP + .095), 1)              # roll cover
        B(bm, (2.06, -.71, TOP + .084), (2.08, -.63, TOP + .088), 1)              # paper slot lip
        C(bm, (2.054, -.77, TOP + .03), .006, .008, "X", 8, 2)
    make("ZC_Printer", ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_LedGreen"], (2.13, -.67, TOP), printer)

    def desk(bm):
        C(bm, (2.13, -.36, TOP + .005), .032, .01, "Z", 14, 0)                     # spike
        C(bm, (2.13, -.36, TOP + .07), .0018, .13, "Z", 6, 0)
        C(bm, (2.13, .30, TOP + .008), .05, .016, "Z", 14, 1)                      # stamp carousel
        C(bm, (2.13, .30, TOP + .06), .006, .10, "Z", 8, 0)
        for k in range(4):
            a = k * math.pi / 2 + .4
            px, py = 2.13 + .034 * math.cos(a), .30 + .034 * math.sin(a)
            C(bm, (px, py, TOP + .024), .012, .016, "Z", 10, 3)
            C(bm, (px, py, TOP + .045), .006, .03, "Z", 8, 2)
            C(bm, (px, py, TOP + .066), .011, .016, "Z", 10, 2)
        B(bm, (2.10, .40, TOP), (2.19, .47, TOP + .014), 1, bevel=.003, segs=1)   # ink pad
        B(bm, (2.107, .407, TOP + .014), (2.183, .463, TOP + .016), 4)
        C(bm, (1.68, .27, TOP + .006), .034, .012, "Z", 16, 1)                     # service bell
        K.add_lathe(bm, [(.031, TOP + .012), (.029, TOP + .022), (.020, TOP + .034), (.008, TOP + .039), (0, TOP + .04)], (1.68, .27, 0), "Z", 16, 0)
        C(bm, (1.68, .27, TOP + .045), .004, .012, "Z", 8, 0)
        C(bm, (1.70, -.24, TOP + .006), .005, .14, "Y", 8, 5)                      # pen + bead chain to the edge
        C(bm, (1.70, -.165, TOP + .006), .0045, .012, "Y", 8, 1)
        SW(bm, K.bezier((1.70, -.31, TOP + .006), (1.66, -.34, TOP + .002), (1.62, -.30, TOP + .002), (1.588, -.28, TOP - .01), 10), .0014, 4, 0)
        B(bm, (1.58, -.29, TOP - .03), (1.59, -.27, TOP - .005), 0)
    make("ZC_Desk", ["M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Hardboard", "M_BV2_Rubber", "M_BV2_WornRed", "M_BV2_Orange"],
         (1.95, 0, TOP), desk)

    def cashbox(bm):
        B(bm, (2.07, .48, TOP), (2.21, .76, TOP + .07), 0, bevel=.006, segs=1)
        B(bm, (2.068, .48, TOP + .052), (2.212, .76, TOP + .056), 1)              # lid seam
        SW(bm, [(2.12, .56, TOP + .07), (2.12, .56, TOP + .085), (2.12, .68, TOP + .085), (2.12, .68, TOP + .07)], .004, 6, 2)
        B(bm, (2.064, .61, TOP + .03), (2.07, .63, TOP + .045), 2)                 # lock + brass key with a tag
        B(bm, (2.052, .615, TOP + .033), (2.064, .625, TOP + .042), 3)
        C(bm, (2.046, .62, TOP + .037), .008, .003, "X", 10, 3)
    make("ZC_CashBox", ["M_BV2_SteelGreen", "M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_Brass"], (2.14, .62, TOP), cashbox)

    # desk fan at the north end, facing the room; head oscillates, blades spin (Unity animates both)
    fx, fy, hz = 2.13, .88, TOP + .17

    def fan_base(bm):
        C(bm, (fx, fy, TOP + .012), .052, .024, "Z", 14, 0)
        C(bm, (fx, fy, TOP + .09), .008, .14, "Z", 8, 1)
        B(bm, (fx - .03, fy - .02, TOP + .024), (fx - .02, fy + .02, TOP + .03), 1)
    base = make("ZC_Fan", ["M_BV2_Ivory", "M_BV2_SteelBare"], (fx, fy, TOP), fan_base)

    def fan_head(bm):
        C(bm, (fx + .03, fy, hz), .034, .06, "X", 12, 0)                            # motor
        C(bm, (fx + .066, fy, hz), .02, .012, "X", 10, 0)
        for x in (fx - .028, fx - .002):                                            # cage rings
            SW(bm, [(x, fy + .09 * math.cos(a), hz + .09 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 25)], .0018, 4, 1)
        for a in np.linspace(0, 2 * math.pi, 13)[:-1]:
            SW(bm, [(fx - .002, fy + .09 * math.cos(a), hz + .09 * math.sin(a)), (fx - .03, fy + .09 * math.cos(a), hz + .09 * math.sin(a)),
                    (fx - .042, fy + .03 * math.cos(a), hz + .03 * math.sin(a)), (fx - .044, fy, hz)], .0013, 3, 1)
        C(bm, (fx - .044, fy, hz), .012, .006, "X", 10, 0)
    head = make("ZC_Fan_Head", ["M_BV2_Ivory", "M_BV2_SteelBare"], (fx, fy, hz), fan_head, parent=base)

    def blades(bm):
        C(bm, (fx - .016, fy, hz), .014, .016, "X", 10, 1)
        for k in range(3):
            a = k * 2 * math.pi / 3
            vs = B(bm, (-.003, .012, -.022), (.003, .078, .022), 0)
            K.rotate_verts(vs, math.radians(18), "Y", (0, 0, 0))
            K.rotate_verts(vs, a, "X", (0, 0, 0))
            for v in vs:
                v.co += Vector((fx - .016, fy, hz))
    make("ZC_Fan_Blades", ["M_ZN_Mint", "M_BV2_DarkPlastic"], (fx - .016, fy, hz), blades, parent=head, role="anim")


def angled_segment_items():
    """Calculator, ledger, mug, radio and a parcel stack on the angled segment (local a across, b along)."""
    seg = SEG2

    def items(bm):
        def at(build):
            K.framed(bm, build, seg["o"], seg["ex"], seg["ey"])
        # printing calculator
        at(lambda: B(bm, (-.24, .22, TOP), (-.08, .42, TOP + .045), 0, bevel=.006, segs=1))
        at(lambda: C(bm, (-.16, .44, TOP + .05), .025, .12, "X", 12, 4))
        for r in range(4):
            for k in range(4):
                at(lambda r=r, k=k: B(bm, (-.225 + k * .036, .25 + r * .03, TOP + .045), (-.200 + k * .036, .272 + r * .03, TOP + .052), 1 if (r, k) != (0, 3) else 2))
        # ledger: cover, page block (the open spread is a print)
        at(lambda: B(bm, (-.28, .52, TOP), (.06, .78, TOP + .006), 1))
        at(lambda: B(bm, (-.27, .53, TOP + .006), (.05, .77, TOP + .016), 4))
        # radio with antenna and handle
        at(lambda: B(bm, (.06, .08, TOP), (.27, .20, TOP + .13), 3, bevel=.008, segs=1))
        at(lambda: B(bm, (.055, .095, TOP + .02), (.062, .185, TOP + .11), 1))
        at(lambda: C(bm, (.058, .17, TOP + .118), .008, .006, "X", 8, 5))
        at(lambda: SW(bm, [(.24, .19, TOP + .13), (.25, .26, TOP + .44)], .0018, 4, 5))
        at(lambda: SW(bm, [(.09, .14, TOP + .13), (.10, .14, TOP + .17), (.23, .14, TOP + .17), (.24, .14, TOP + .13)], .005, 6, 1))
        # mug
        at(lambda: K.add_lathe(bm, [(.0, TOP), (.038, TOP), (.04, TOP + .09), (.036, TOP + .09), (.034, TOP + .012), (0, TOP + .012)], (.17, .56, 0), "Z", 14, 6))
        at(lambda: SW(bm, [(.208, .56, TOP + .07), (.228, .56, TOP + .066), (.228, .56, TOP + .03), (.208, .56, TOP + .026)], .005, 5, 6))
        # parcels awaiting pickup
        at(lambda: B(bm, (-.20, .88, TOP), (.06, 1.10, TOP + .14), 7, bevel=.004, segs=1))
        at(lambda: B(bm, (-.205, .96, TOP), (.065, 1.02, TOP + .1405), 8))
        at(lambda: B(bm, (.08, .90, TOP), (.26, 1.08, TOP + .10), 7, bevel=.004, segs=1))
        at(lambda: B(bm, (-.16, .92, TOP + .14), (.02, 1.06, TOP + .22), 7, bevel=.004, segs=1))
    make("ZC_Corner", ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_WornRed", "M_BV2_Rubber", "M_ZN_PaperFlat", "M_BV2_SteelBare",
                       "M_BV2_Ceramic", "M_ZN_Cardboard", "M_ZN_Tape"], tuple(seg_point(seg, 0, .6)), items)
    TAGS.append((seg_point(seg, -.20, .99, TOP + .10), seg_point(seg, -.215, .99, TOP + .06) + Vector((-.012, -.012, 0)), "tag11", -Vector(seg["ex"])))


def stool():
    sx, sy = STOOL

    def build(bm):
        for i in range(4):
            a = math.radians(45 + 90 * i)
            SW(bm, [(sx + .07 * math.cos(a), sy + .07 * math.sin(a), .66), (sx + .19 * math.cos(a), sy + .19 * math.sin(a), .0)], .012, 6, 0)
            B(bm, (sx + .19 * math.cos(a) - .015, sy + .19 * math.sin(a) - .015, 0), (sx + .19 * math.cos(a) + .015, sy + .19 * math.sin(a) + .015, .012), 2)
        SW(bm, [(sx + .15 * math.cos(a), sy + .15 * math.sin(a), .25) for a in np.linspace(0, 2 * math.pi, 17)], .009, 5, 0)
        C(bm, (sx, sy, .655), .07, .02, "Z", 12, 2)
        C(bm, (sx, sy, .69), .18, .05, "Z", 16, 1)
        C(bm, (sx, sy, .664), .182, .012, "Z", 16, 2)
    make("ZC_Stool", ["M_BV2_SteelBare", "M_ZN_Vinyl", "M_BV2_DarkPlastic"], (sx, sy, .35), build)


def racking():
    bays = ((-1.30, -0.05), (0.05, 1.30))
    levels = (0.08, 0.50, 0.92, 1.34, 1.76)

    def build(bm):
        for y0, y1 in bays:
            for y in (y0 + .02, y1 - .02):
                for x in (SHELF_X0 + .025, A - .03):
                    sx = 1 if x < 3.2 else -1
                    sy = 1 if y < (y0 + y1) / 2 else -1
                    B(bm, (min(x, x + sx * .04), y - .0015, 0), (max(x, x + sx * .04), y + .0015, 2.0), 0)
                    B(bm, (x - .0015, min(y, y + sy * .04), 0), (x + .0015, max(y, y + sy * .04), 2.0), 0)
                    B(bm, (x - .03, y - .03, 0), (x + .03, y + .03, .006), 2)
            for z in levels:
                B(bm, (SHELF_X0 + .01, y0, z), (A - .015, y1, z + .02), 1)
                B(bm, (SHELF_X0 + .005, y0, z - .022), (SHELF_X0 + .012, y1, z + .02), 0)
            for (ya, yb) in ((y0, y1), (y1, y0)):
                SW(bm, [(A - .018, ya + .03 * (1 if ya < yb else -1), .12), (A - .018, yb - .03 * (1 if ya < yb else -1), 1.70)], .003, 4, 1)
    make("ZC_Racking", ["M_BV2_SteelGreen", "M_BV2_SteelBare", "M_BV2_DarkPlastic"], (3.2, 0, 1.0), build)


def shelf_goods():
    L = [0.10, 0.52, 0.94, 1.36, 1.78]      # shelf top surfaces
    X0 = 3.06                               # front of the goods

    def hardhat(bm, cx, cy, z, mat_shell, mat_band):
        K.add_lathe(bm, [(.0, z + .13), (.05, z + .125), (.095, z + .10), (.115, z + .06), (.12, z + .02), (.12, z + .01), (.0, z + .01)], (cx, cy, 0), "Z", 16, mat_shell)
        K.add_lathe(bm, [(.10, z), (.15, z), (.15, z + .012), (.10, z + .012)], (cx, cy, 0), "Z", 16, mat_shell)
        B(bm, (cx - .012, cy - .11, z + .02), (cx + .012, cy + .11, z + .135), mat_shell)
        K.add_lathe(bm, [(.118, z + .02), (.121, z + .02), (.121, z + .04), (.118, z + .04)], (cx, cy, 0), "Z", 16, mat_band)

    def limb_arm(bm, x, y0, y1, z, mats):
        ivory, dark, steel = mats
        C(bm, (x + .05, (y0 + y1) / 2 - .03, z + .045), .038, (y1 - y0) * .55, "Y", 10, ivory)
        C(bm, (x + .05, y0 + (y1 - y0) * .78, z + .045), .046, .07, "Y", 12, dark)
        C(bm, (x + .05, y1 - .03, z + .04), .03, .06, "Y", 10, steel)
        B(bm, (x + .015, y0, z), (x + .085, y0 + .09, z + .05), dark, bevel=.006, segs=1)
        for k in range(3):
            B(bm, (x + .02 + k * .022, y0 - .06, z + .005), (x + .036 + k * .022, y0, z + .025), steel)

    def limb_leg(bm, x, y0, y1, z, mats):
        ivory, dark, steel = mats
        C(bm, (x + .06, (y0 + y1) / 2, z + .03), .024, (y1 - y0) * .6, "Y", 8, steel)
        B(bm, (x + .02, y1 - .14, z), (x + .10, y1, z + .09), ivory, bevel=.01, segs=1)
        B(bm, (x + .025, y0, z), (x + .10, y0 + .13, z + .045), dark, bevel=.008, segs=1)
        C(bm, (x + .06, y1 - .16, z + .045), .04, .05, "Y", 10, dark)

    def bag(bm, x0, y0, z, w, d, h, mat_bag, mat_band):
        B(bm, (x0, y0, z), (x0 + d, y0 + w, z + h), mat_bag, bevel=min(w, d, h) * .25, segs=2)
        B(bm, (x0 - .004, y0 + w * .3, z - .002), (x0 + d + .004, y0 + w * .3 + .012, z + h + .004), mat_band)

    def box(bm, x0, y0, z, w, d, h, mat_box, mat_tape):
        # Storage pass (2026-10-21): stackable dark plastic totes with a rim and a paper label, not kraft cartons.
        B(bm, (x0 + .006, y0 + .006, z), (x0 + d - .006, y0 + w - .006, z + h - .02), 1, bevel=.006, segs=1)
        B(bm, (x0, y0, z + h - .025), (x0 + d, y0 + w, z + h), 1, bevel=.003, segs=1)
        B(bm, (x0 - .002, y0 + w * .25, z + h * .35), (x0 + .004, y0 + w * .75, z + h * .62), mat_tape)

    def build(bm):
        IV, DK, ST, YE, RB, BG, CB, TP, RD, OR, MI = range(11)
        arm_m, leg_m = (IV, DK, ST), (IV, DK, ST)
        # floor level: battery pack, cable coil | two boxes, wrapped bundle
        B(bm, (X0, -1.22, L[0]), (X0 + .26, -.88, L[0] + .20), DK, bevel=.008, segs=1)
        B(bm, (X0 - .002, -1.16, L[0] + .08), (X0 + .001, -.94, L[0] + .15), OR)
        C(bm, (X0 + .10, -1.15, L[0] + .215), .016, .03, "Z", 10, RD)
        C(bm, (X0 + .10, -.95, L[0] + .215), .016, .03, "Z", 10, DK)
        coil = [(X0 + .14 + .11 * math.cos(a), -.55 + .11 * math.sin(a), L[0] + .012 + .006 * a / (2 * math.pi)) for a in np.linspace(0, 10 * math.pi, 120)]
        SW(bm, coil, .008, 5, RB)
        box(bm, X0, .12, L[0], .34, .28, .18, CB, TP)
        box(bm, X0 + .02, .16, L[0] + .18, .26, .24, .14, CB, TP)
        bag(bm, X0, .72, L[0], .44, .28, .22, BG, TP)
        # shin level: prosthetic legs, a wrapped bundle, a radio
        limb_leg(bm, X0, -1.20, -.64, L[1], leg_m)
        bag(bm, X0, -.52, L[1], .40, .26, .16, BG, TP)
        limb_leg(bm, X0, .18, .74, L[1], leg_m)
        B(bm, (X0 + .02, .86, L[1]), (X0 + .14, 1.16, L[1] + .17), IV, bevel=.008, segs=1)
        B(bm, (X0 + .016, .90, L[1] + .03), (X0 + .02, 1.02, L[1] + .14), DK)
        SW(bm, [(X0 + .08, 1.14, L[1] + .17), (X0 + .08, 1.18, L[1] + .38)], .0018, 4, ST)
        # counter level: hard hat, cargo scanner, cassette deck | prosthetic arm, bundle
        hardhat(bm, X0 + .14, -1.00, L[2], YE, DK)
        B(bm, (X0 + .02, -.70, L[2]), (X0 + .22, -.58, L[2] + .07), DK, bevel=.006, segs=1)     # scanner
        B(bm, (X0 + .14, -.68, L[2] + .07), (X0 + .19, -.60, L[2] + .15), YE, bevel=.006, segs=1)
        B(bm, (X0 + .018, -.68, L[2] + .02), (X0 + .022, -.60, L[2] + .05), RD)
        B(bm, (X0 + .02, -.45, L[2]), (X0 + .26, -.10, L[2] + .09), DK, bevel=.006, segs=1)     # cassette deck
        B(bm, (X0 + .016, -.43, L[2] + .02), (X0 + .02, -.12, L[2] + .07), ST)
        limb_arm(bm, X0, .14, .70, L[2], arm_m)
        bag(bm, X0, .80, L[2], .36, .26, .18, BG, TP)
        # head level: boxes and a CRT tube carton | second arm, spare UNIT-series face bezel
        box(bm, X0, -1.24, L[3], .26, .28, .20, CB, TP)
        box(bm, X0 + .02, -.95, L[3], .22, .24, .14, CB, TP)
        box(bm, X0, -.64, L[3], .46, .32, .30, CB, TP)
        limb_arm(bm, X0, .16, .66, L[3], arm_m)
        B(bm, (X0 + .04, .80, L[3]), (X0 + .08, 1.06, L[3] + .20), MI, bevel=.012, segs=1)
        B(bm, (X0 + .035, .84, L[3] + .035), (X0 + .041, 1.02, L[3] + .165), DK)
        # top: stock boxes and a coil
        box(bm, X0, -1.22, L[4], .30, .30, .20, CB, TP)
        box(bm, X0 + .03, -.88, L[4], .30, .26, .16, CB, TP)
        box(bm, X0 + .05, -.92, L[4] + .16, .22, .20, .12, CB, TP)
        box(bm, X0, .20, L[4], .40, .30, .18, CB, TP)
        coil = [(X0 + .15 + .10 * math.cos(a), .90 + .10 * math.sin(a), L[4] + .01 + .008 * a / (2 * math.pi)) for a in np.linspace(0, 8 * math.pi, 96)]
        SW(bm, coil, .007, 5, RD)
        # under the counter (operator side): a binder stack and two boxes
        box(bm, 1.94, -.80, .33, .24, .20, .16, CB, TP)
        box(bm, 1.95, .30, .33, .30, .20, .12, CB, TP)
        B(bm, (1.95, -.30, .33), (2.20, -.05, .40), DK)
    make("ZC_ShelfGoods", ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_Yellow", "M_BV2_Rubber", "M_ZN_Bag",
                           "M_ZN_Cardboard", "M_ZN_Tape", "M_BV2_WornRed", "M_BV2_Orange", "M_ZN_Mint"], (3.2, 0, 1.0), build)
    # claim tags (attach point on the item, tag centre a little in front and below)
    for i, (y, z) in enumerate(((-1.05, L[0] + .20), (.40, L[0] + .30), (-.90, L[1] + .09), (.45, L[1] + .09), (.98, L[1] + .17),
                                (-1.00, L[2] + .08), (.40, L[2] + .05), (.98, L[2] + .18), (-.42, L[3] + .30), (.40, L[3] + .05), (.92, L[3] + .20))):
        TAGS.append((Vector((X0, y, z)), Vector((X0 - .012, y + .02, z - .055)), "tag%d" % (i % len(TAG_NUMBERS)), Vector((-1, 0, 0))))


def tariff_and_cctv():
    def board(bm):
        B(bm, (A - .025, -.82, 2.00), (A - .005, .82, 2.52), 0)
        for y in (-.70, .70):
            C(bm, (A - .028, y, 2.48), .007, .006, "X", 8, 1)
    make("ZC_TariffBoard", ["M_BV2_DarkPlastic", "M_BV2_SteelBare"], (A - .015, 0, 2.26), board)

    # CCTV camera on the east wall's north end, looking at the counter front
    mount = Vector((A - .02, 1.10, 2.56))
    aim = (Vector((1.35, -.35, 1.05)) - mount).normalized()
    M = look_matrix(aim)

    def cam(bm):
        B(bm, (A - .03, 1.04, 2.50), (A - .005, 1.16, 2.62), 2)
        SW(bm, [mount + Vector((-.02, 0, 0)), mount + Vector((-.09, 0, -.02))], .008, 6, 1)
        K.xform(bm, lambda: (B(bm, (-.04, -.045, -.04), (.17, .045, .04), 0, bevel=.006, segs=1),
                             B(bm, (-.06, -.055, .04), (.19, .055, .05), 1),
                             C(bm, (.175, 0, 0), .028, .02, "X", 12, 1)), M, mount + Vector((-.13, 0, -.04)))
    make("ZC_Cctv", ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare"], tuple(mount), cam)

    def led(bm):
        K.xform(bm, lambda: C(bm, (.10, .02, .043), .006, .006, "Z", 8, 0), M, mount + Vector((-.13, 0, -.04)))
    make("ZC_Cctv_Led", ["M_BV2_LedRed"], tuple(mount), led, role="anim")


def counter_prints_geo():
    """Every counter print in one print-only object (keeps its own sharp texture through the atlas bake)."""
    uv = CP.uv

    def build(bm):
        bm.loops.layers.uv.verify()
        facing_quad(bm, (A - .027, 0, 2.26), (-1, 0, 0), 1.60, .50, uv["tariff"])
        fwd1 = Vector((-1, 0, 0))
        facing_quad(bm, (1.6105, -.69, .66), fwd1, .17, .234, uv["nocredit"], roll=-2)
        facing_quad(bm, (1.6105, .70, .63), fwd1, .16, .26, uv["flyer"], roll=3)
        facing_quad(bm, (1.6055, 0, .56), fwd1, .60, .262, uv["stencil"])
        p = seg_point(SEG2, -.2915, .62, .64)
        facing_quad(bm, p, -Vector(SEG2["ex"]), .17, .234, uv["lease"], roll=-1)
        flat_quad(bm, (1.84, -.89, TOP + .0012), .30, .095, uv["trayIn"], yaw_deg=0)
        flat_quad(bm, (1.84, .89, TOP + .0012), .30, .095, uv["trayOut"], yaw_deg=0)
        # ledger spread lying on the page block (angled segment)
        c = seg_point(SEG2, -.11, .65, TOP + .0165)
        e = Vector(SEG2["ex"])
        K.print_quad(bm, c, Vector(SEG2["ey"]), e, .31, .225, uv["ledger"])
        # impaled tickets on the spike
        for k in range(5):
            flat_quad(bm, (2.13 + RNG.normal(0, .004), -.36 + RNG.normal(0, .004), TOP + .015 + k * .012), .05, .07,
                      (uv["receipt"][0], uv["receipt"][1] + (uv["receipt"][3] - uv["receipt"][1]) * .55, uv["receipt"][2], uv["receipt"][3]),
                      yaw_deg=float(RNG.uniform(0, 360)))
        # receipt strip curling out of the printer and over its front onto the counter
        path = [(2.07, -.67, TOP + .088), (2.065, -.67, TOP + .115), (2.045, -.67, TOP + .128), (2.025, -.67, TOP + .115),
                (2.035, -.67, TOP + .07), (2.032, -.67, TOP + .02), (2.015, -.67, TOP + .004), (1.96, -.67, TOP + .002)]
        u0, v0, u1, v1 = uv["receipt"]
        total = sum((Vector(b) - Vector(a)).length for a, b in zip(path[:-1], path[1:]))
        acc, rows = 0.0, []
        uvl = bm.loops.layers.uv.verify()
        for i, p in enumerate(path):
            if i:
                acc += (Vector(p) - Vector(path[i - 1])).length
            rows.append((bm.verts.new((p[0], p[1] - .024, p[2])), bm.verts.new((p[0], p[1] + .024, p[2])), v1 - (v1 - v0) * acc / total))
        for (a0, a1, va), (b0, b1, vb) in zip(rows[:-1], rows[1:]):
            f = bm.faces.new((a0, a1, b1, b0))
            for l, t in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))):
                l[uvl].uv = t
        # box labels on carton fronts (racking)
        facing_quad(bm, (3.056, -.41, 1.36 + .17), (-1, 0, 0), .20, .083, uv["fragile"])
        facing_quad(bm, (3.056, -1.11, 1.36 + .10), (-1, 0, 0), .14, .058, uv["nspbox"])
        facing_quad(bm, (3.056, -.41, 1.36 + .07), (-1, 0, 0), .16, .066, uv["crtbox"])
        facing_quad(bm, (3.056, .32, .10 + .09), (-1, 0, 0), .14, .058, uv["nspbox"], roll=4)
        # claim tags
        for _, centre, key, fwd in TAGS:
            facing_quad(bm, centre, fwd, .036, .058, uv[key], roll=float(RNG.normal(0, 6)))
    make("ZC_Prints", ["M_ZN_CounterPrints"], (2.6, 0, 1.2), build, uv=None)

    def strings(bm):
        for attach, centre, _, fwd in TAGS:
            string(bm, attach, Vector(centre) + Vector((0, 0, .029)), .008, 0)
    make("ZC_TagStrings", ["M_ZN_Tape"], (3.0, 0, 1.0), strings)


def trade_counter():
    root = K.empty("TradeCounterV2", (0, 0, 0))
    K.set_root(root)
    for n, p in (("Ref_Origin", (0, 0, 0)), ("Ref_Head", (0, 1, 0)), ("Ref_Up", (0, 0, 1))):
        K.empty(n, p, root)
    counter_body()
    intake_trays()
    status_pods()
    terminal()
    counter_kit()
    angled_segment_items()
    stool()
    racking()
    shelf_goods()
    tariff_and_cctv()
    counter_prints_geo()
    return root


# =====================================================================================================================
# Dock corner
# =====================================================================================================================


def dock_corner():
    root = K.empty("DockCornerV2", (0, 0, 0))
    K.set_root(root)
    for n, p in (("Ref_Origin", (0, 0, 0)), ("Ref_Head", (0, 1, 0)), ("Ref_Up", (0, 0, 1))):
        K.empty(n, p, root)
    W = WALL_W
    uv = DP.uv

    def cabinet(bm):
        B(bm, (W, .42, 1.05), (W + .18, .92, 1.72), 0, bevel=.008, segs=1)
        F = W + .181
        for (y0, y1, z0, z1) in ((.44, .90, 1.07, 1.075), (.44, .90, 1.695, 1.70), (.44, .445, 1.07, 1.70), (.895, .90, 1.07, 1.70)):
            B(bm, (F - .002, y0, z0), (F + .001, y1, z1), 1)
        for z in (1.15, 1.62):
            C(bm, (F, .435, z), .007, .05, "Z", 8, 2)
        B(bm, (F, .865, 1.33), (F + .02, .88, 1.43), 2)
        for y in (.56, .78):
            C(bm, (F + .006, y, 1.56), .046, .014, "X", 16, 2)
            B(bm, (F + .0148, y - .002, 1.54), (F + .016, y + .002, 1.585), 1)
        B(bm, (F, .61, 1.24), (F + .004, .73, 1.36), 3)                        # isolator: yellow plate, red handle
        C(bm, (F + .012, .67, 1.30), .026, .016, "X", 14, 4)
        B(bm, (F + .02, .655, 1.27), (F + .032, .685, 1.33), 4)
        for y in (.58, .67, .76):
            C(bm, (F + .004, y, 1.44), .011, .01, "X", 10, 2)
        B(bm, (W + .03, .40, 1.72), (W + .15, .44, 1.76), 2)
    make("ZD_Cabinet", ["M_BV2_SteelGreen", "M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_Yellow", "M_BV2_WornRed"], (W + .09, .67, 1.38), cabinet)

    def lamps(bm):
        C(bm, (W + .19, .58, 1.44), .008, .008, "X", 10, 0)
    make("ZD_RunLamp", ["M_BV2_LedGreen"], (W + .186, .58, 1.44), lamps)

    def chg(bm):
        C(bm, (W + .19, .67, 1.44), .008, .008, "X", 10, 0)
    make("ZD_ChargeLamp", ["M_ZN_LampAmber"], (W + .186, .67, 1.44), chg, role="anim")

    def err(bm):
        C(bm, (W + .19, .76, 1.44), .008, .008, "X", 10, 0)
    make("ZD_ErrLamp", ["M_BV2_DarkPlastic"], (W + .186, .76, 1.44), err)

    def conduit(bm):
        SW(bm, [(W + .06, .60, 1.05), (W + .05, .60, .90), (W + .04, .60, .12), (W + .06, .56, .03), (W + .12, .42, .022), (-3.13, .14, .022)], .017, 8, 0)
        SW(bm, [(W + .09, .88, 1.72), (W + .07, .97, 1.86), (W + .05, .97, 2.40)], .013, 8, 0)
        for z in (.40, .75):
            B(bm, (W, .58, z - .012), (W + .065, .62, z + .012), 1)
        SW(bm, [(W + .02, -.20, 2.40), (W + .03, -.10, 2.31), (W + .03, 0, 2.33)], .008, 6, 2)
    make("ZD_Conduit", ["M_BV2_Rubber", "M_BV2_SteelBare", "M_BV2_DarkPlastic"], (W + .05, .5, .8), conduit)

    def tray(bm):
        for x in (W + .02, W + .18):
            B(bm, (x - .004, -1.0, 2.40), (x + .004, 1.0, 2.46), 0)
        y = -.98
        while y < 1.0:
            B(bm, (W + .02, y - .008, 2.40), (W + .18, y + .008, 2.41), 0)
            y += .22
        for y in (-.80, 0, .80):
            B(bm, (W, y - .015, 2.36), (W + .19, y + .015, 2.40), 0)
        for k, (mat, off) in enumerate(((1, .05), (2, .10), (3, .145))):
            pts = [(W + off, -1.05, 2.43 + k * .004), (W + off, 0, 2.425 + k * .004), (W + off, 1.05, 2.43 + k * .004)]
            SW(bm, pts, .011, 6, mat)
        SW(bm, [(W + .10, 1.02, 2.43), (W + .12, 1.10, 2.30), (W + .10, 1.16, 2.20)], .011, 6, 2)
    make("ZD_CableTray", ["M_BV2_SteelBare", "M_BV2_Rubber", "M_BV2_DarkPlastic", "M_BV2_Orange"], (W + .1, 0, 2.42), tray)

    def umbilical(bm):
        SW(bm, [(W, -.56, 1.50), (W + .05, -.56, 1.50), (W + .06, -.56, 1.53)], .005, 6, 1)
        for k in range(4):
            r, rz = .085 + k * .006, .17 + k * .01
            pts = [(W + .05 + .012 * k, -.56 + r * math.sin(a), 1.52 - rz * (1 - math.cos(a)) / 2 * 1.0 - .02 * math.sin(a / 2)) for a in np.linspace(0, 2 * math.pi, 25)]
            SW(bm, pts, .011, 6, 0)
        SW(bm, [(W + .09, -.47, 1.36), (W + .11, -.44, 1.20), (W + .12, -.45, 1.10)], .011, 6, 0)
        C(bm, (W + .12, -.45, 1.06), .034, .07, "Z", 12, 2)
        C(bm, (W + .12, -.45, 1.10), .037, .012, "Z", 12, 3)
        C(bm, (W + .12, -.45, 1.02), .02, .012, "Z", 10, 1)
    make("ZD_Umbilical", ["M_BV2_Rubber", "M_BV2_SteelBare", "M_BV2_Ivory", "M_BV2_Orange"], (W + .07, -.52, 1.32), umbilical)

    def plate(bm):
        B(bm, (W, -.27, 2.08), (W + .008, .27, 2.27), 0)
        for y in (-.25, .25):
            for z in (2.10, 2.25):
                C(bm, (W + .009, y, z), .006, .004, "X", 8, 0)
    make("ZD_Plate", ["M_BV2_SteelBare"], (W + .004, 0, 2.175), plate)

    def lamp(bm):
        B(bm, (W, -.075, 1.87), (W + .03, .075, 1.99), 0, bevel=.006, segs=1)
        for dy in (-.04, -.013, .013, .04):
            SW(bm, [(W + .03, dy, 1.88), (W + .085, dy * .9, 1.90), (W + .088, dy * .9, 1.96), (W + .03, dy, 1.98)], .0028, 4, 1)
        SW(bm, [(W + .086, -.045, 1.93), (W + .086, .045, 1.93)], .0028, 4, 1)
    make("ZD_Lamp", ["M_BV2_DarkPlastic", "M_BV2_SteelBare"], (W + .02, 0, 1.93), lamp)

    def glass(bm):
        C(bm, (W + .055, 0, 1.93), .04, .05, "X", 14, 0)
    make("ZD_Lamp_Glass", ["M_BV2_Bulb"], (W + .055, 0, 1.93), glass)

    def log_and_drawing(bm):
        B(bm, (W, -.95, 1.08), (W + .006, -.77, 1.34), 0)                       # clipboard
        B(bm, (W + .006, -.90, 1.31), (W + .016, -.82, 1.34), 1)
        C(bm, (W + .01, -.86, 1.37), .003, .02, "X", 6, 1)                       # nail
    make("ZD_Clipboard", ["M_BV2_Hardboard", "M_BV2_SteelBare"], (W + .008, -.86, 1.21), log_and_drawing)

    def extinguisher(bm):
        C(bm, (W + .085, -.86, .62), .07, .40, "Z", 16, 0, bevel=.01)
        C(bm, (W + .085, -.86, .85), .025, .06, "Z", 10, 1)
        B(bm, (W + .06, -.87, .87), (W + .14, -.85, .885), 1)
        SW(bm, [(W + .11, -.86, .86), (W + .17, -.84, .80), (W + .16, -.83, .60), (W + .15, -.84, .50)], .008, 6, 2)
        B(bm, (W, -.89, .50), (W + .02, -.83, .80), 3)
        SW(bm, [(W + .02, -.93, .72), (W + .09, -.94, .72), (W + .16, -.86, .72), (W + .09, -.78, .72), (W + .02, -.79, .72)], .006, 5, 3)
    make("ZD_Extinguisher", ["M_BV2_WornRed", "M_BV2_SteelBare", "M_BV2_Rubber", "M_BV2_DarkPlastic"], (W + .085, -.86, .62), extinguisher)

    def spares(bm):
        B(bm, (W, .40, 1.84), (W + .17, .94, 1.86), 0)
        for y in (.45, .89):
            SW(bm, [(W, y, 1.76), (W + .13, y, 1.84)], .006, 5, 0)
        B(bm, (W + .02, .46, 1.86), (W + .05, .66, 2.01), 1, bevel=.008, segs=1)    # spare face bezel, leaning
        B(bm, (W + .048, .49, 1.89), (W + .053, .63, 1.98), 2)
        B(bm, (W + .07, .70, 1.86), (W + .14, .76, 1.89), 3, bevel=.005, segs=1)     # spare hand: palm and fingers
        for k in range(3):
            B(bm, (W + .065 - k * .0, .70 + k * .022, 1.89), (W + .085, .713 + k * .022, 1.95 - k * .01), 2)
        C(bm, (W + .09, .83, 1.92), .03, .12, "Z", 12, 4)                              # bearing jar (milky)
        C(bm, (W + .09, .83, 1.985), .032, .012, "Z", 12, 5)
        C(bm, (W + .045, .90, 1.90), .025, .08, "Z", 12, 2)                            # grease can
        C(bm, (W + .045, .90, 1.90), .0255, .03, "Z", 12, 6)
    make("ZD_Spares", ["M_BV2_SteelBare", "M_ZN_Mint", "M_BV2_DarkPlastic", "M_BV2_Ivory", "M_ZN_Bag", "M_BV2_Orange", "M_BV2_WornRed"],
         (W + .08, .67, 1.9), spares)

    def floor(bm):
        B(bm, (-3.24, -.92, 0.0), (-2.38, .92, .006), 0)
        K.planar_uv(bm, (1, 0, 0), (0, 1, 0), -3.24, -2.38, -.92, .92)
    make("ZD_FloorMat", ["M_ZN_FloorMat"], (-2.81, 0, .003), floor, uv=None)

    def pan(bm):
        x0, x1, y0, y1 = -2.84, -2.52, -.17, .17
        B(bm, (x0, y0, .006), (x1, y1, .010), 0)
        for (a, b) in (((x0, y0), (x1, y0 + .006)), ((x0, y1 - .006), (x1, y1)), ((x0, y0), (x0 + .006, y1)), ((x1 - .006, y0), (x1, y1))):
            B(bm, (a[0], a[1], .006), (b[0], b[1], .036), 0)
        B(bm, (x0 + .006, y0 + .006, .010), (x1 - .006, y1 - .006, .02), 1)
        vs = [bm.verts.new(p) for p in ((x1 - .03, y0 + .02, .037), (x1 + .06, y0 - .02, .007), (x1 + .08, y0 + .12, .007), (x1 - .02, y0 + .14, .037))]
        f = bm.faces.new(vs)
        f.material_index = 2
    make("ZD_DripPan", ["M_BV2_SteelBare", "M_ZN_Oil", "M_ZN_Rag"], (-2.68, 0, .02), pan)

    def prints(bm):
        bm.loops.layers.uv.verify()
        fwd = Vector((1, 0, 0))
        facing_quad(bm, (W + .0095, 0, 2.175), fwd, .52, .195, uv["plate"])
        facing_quad(bm, (W + .002, -.52, 1.76), fwd, .25, .19, uv["drawing"], roll=-3)
        facing_quad(bm, (W + .0072, -.86, 1.19), fwd, .165, .22, uv["log"])
        facing_quad(bm, (W + .1825, .52, 1.64), fwd, .08, .072, uv["danger"])
        facing_quad(bm, (W + .1825, .67, 1.485), fwd, .17, .053, uv["cablabel"])
        facing_quad(bm, (W + .02, .73, 1.80), fwd, .035, .056, uv["sparetag"], roll=8)
    make("ZD_Prints", ["M_ZN_DockPrints"], (W + .05, 0, 1.6), prints, uv=None)

    def meters(bm):
        bm.loops.layers.uv.verify()
        for y in (.56, .78):
            K.print_quad(bm, (W + .1952, y, 1.56), (0, 1, 0), (0, 0, 1), .07, .07, (0, 0, 1, 1))
    make("ZD_Meters", ["M_BV2_MeterFace"], (W + .1955, .67, 1.56), meters, uv=None)

    def seal(bm):
        bm.loops.layers.uv.verify()
        K.print_quad(bm, (W + .1825, .893, 1.50), Vector((0, .5, .87)).normalized(), Vector((0, -.87, .5)).normalized(), .13, .02, (0, 0, 1, 1))
    make("ZD_Seal", ["M_BV2_SealTape"], (W + .1825, .893, 1.50), seal, uv=None)

    def stencil(bm):
        bm.loops.layers.uv.verify()
        K.print_quad(bm, (W + .1825, .67, 1.12), (0, 1, 0), (0, 0, 1), .30, .075, (0, 0, 1, 1))
    make("ZD_Stencil", ["M_BV2_AssetStencil"], (W + .1825, .67, 1.12), stencil, uv=None)
    return root


# =====================================================================================================================
# Export, report, previews
# =====================================================================================================================


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


def report(roots):
    bpy.context.view_layer.update()
    data = {}
    for root in roots:
        rows, total = [], 0
        for o in root.children_recursive:
            if o.type != "MESH":
                continue
            tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
            total += tris
            bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
            rows.append({"name": o.name, "tris": tris, "materials": [m.name for m in o.data.materials],
                         "min": [round(min(v[i] for v in bb), 4) for i in range(3)], "max": [round(max(v[i] for v in bb), 4) for i in range(3)]})
        data[root.name] = {"triangles": total, "objects": rows}
        print(root.name, "triangles", total, "objects", len(rows))
    # Footprint checks against the zones build_unified_clinic.py keeps clear of environment geometry.
    zones = {"dock_work": (-2.39, -1.65, -0.95, 0.95), "dock_side_access_L": (-3.00, -2.30, 0.95, 1.30),
             "dock_side_access_R": (-3.00, -2.30, -1.30, -0.95), "trade_customer_strip": (1.22, 1.555, -1.00, 0.95)}
    hits = []
    for root in roots:
        for o in data[root.name]["objects"]:
            if o["name"] in ("ZD_FloorMat", "ZD_DripPan"):
                continue
            for zn, (x0, x1, y0, y1) in zones.items():
                if o["min"][0] < x1 and o["max"][0] > x0 and o["min"][1] < y1 and o["max"][1] > y0 and o["min"][2] < 1.9:
                    hits.append((o["name"], zn))
    data["zone_hits"] = hits
    print("zone hits", hits)
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


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
    shots = {"counter_front": ((0.55, -0.35, 1.60), (2.4, -0.2, 1.0), 28), "counter_side": ((1.0, -2.6, 1.6), (2.4, 0.1, 1.0), 30),
             "counter_top": ((1.35, 0.0, 1.55), (2.0, 0.0, 0.95), 32), "shelves": ((1.3, 0.0, 1.35), (3.3, 0.0, 1.05), 28),
             "dock_wall": ((-1.2, 0.0, 1.55), (-3.3, 0.0, 1.45), 30), "dock_floor": ((-1.6, -1.2, 1.4), (-2.8, 0.0, 0.3), 32)}
    for name, (loc, target, lens) in shots.items():
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = lens
        SCENE.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)


CP = DP = None
textures = build_textures()
build_materials(textures)
counter = trade_counter()
dock = dock_corner()
paths = [export(counter, "TradeCounterV2"), export(dock, "DockCornerV2")]
report([counter, dock])
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "ClinicZonesV2.blend"))
if not NO_RENDER:
    render()
print("ZONES_DONE", paths)
