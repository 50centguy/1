"""Repair bench V2 for the four-prop clinic: a detailed workbench and desk props that replace the primitive
WorkbenchArea bench top, drawers, desk clutter, pegboard, wall shelf and everything pinned to the wall.

Same frame and FBX conventions as ArtSource/WorkbenchArea/build_workbench_area.py (Blender Z-up, x along the bench,
-y toward the player, bench top at z 0.90; FBX axis_forward=-Z, axis_up=Y, FBX_SCALE_UNITS, no bake), so Unity can lock
the new root onto the old Bench_Top through the empty Ref_BenchTopOrigin (old Bench_Top origin (0, 0.55, 0.90)).

Run:  blender -b --factory-startup --python build_repair_bench_v2.py -- <cc0_texture_dir>
Outputs: Export/RepairBenchV2.fbx, Textures/*.png, Renders/*.png, report.json. Inputs are only read.
Textures: wear and grain masks derive from ambientCG CC0 sets (see SOURCES.md); colours are this project's palette.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
CC0 = ARGS[0] if ARGS else os.path.join(HERE, "cc0")
TEX = os.path.join(HERE, "Textures")
EXPORT = os.path.join(HERE, "Export")
RENDERS = os.path.join(HERE, "Renders")
for d in (TEX, EXPORT, RENDERS):
    os.makedirs(d, exist_ok=True)
NO_RENDER = "--norender" in ARGS
T = 0.90            # bench top (matches WorkbenchArea BENCH_TOP)
MAT_Z = T + 0.004
RNG = random.Random(2010)

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene
sys.path.insert(0, HERE)
import bv2_kit as K  # noqa: E402  (text rasterising and print-quad helpers for the wall pass)
K.init(SCENE, CC0, TEX, {})

# =====================================================================================================================
# Textures
# =====================================================================================================================


def load_rgb(asset, kind="Color"):
    path = os.path.join(CC0, asset, "%s_1K-JPG_%s.jpg" % (asset, kind))
    img = bpy.data.images.load(path, check_existing=True)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[..., :3]


def down(a, size):
    h = a.shape[0]
    f = h // size
    return a[:f * size, :f * size].reshape(size, f, size, f, -1).mean(axis=(1, 3))


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def lum(a):
    return a[..., 0] * .3 + a[..., 1] * .59 + a[..., 2] * .11


def sat(a):
    mx, mn = a.max(axis=-1), a.min(axis=-1)
    return (mx - mn) / np.maximum(mx, 1e-4)


def save_png(name, rgb, alpha=None):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=alpha is not None)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(rgb, 0, 1)
    if alpha is not None:
        rgba[..., 3] = np.clip(alpha, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(TEX, name + ".png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def chips_from(asset, size):
    """Bare-metal chips and scratches of a painted-metal set as a 0..1 mask (low saturation = paint gone)."""
    c = down(load_rgb(asset), size)
    m = np.clip((0.55 - sat(c)) * 2.5, 0, 1)
    return m


def painted(name, base, chip_mask, steel, grain, size, grime=0.25, rust=0.3):
    g = (lum(grain) - lum(grain).mean()) * 0.6
    col = base[None, None, :] * (1 + g[..., None])
    col = col * (1 - chip_mask[..., None]) + steel * chip_mask[..., None]
    # soft grime gathering in darker grain
    col *= (1 - grime * np.clip(-g * 4, 0, 1))[..., None]
    # rust blooms: a second, offset chip pattern, brown-orange, strongest where paint is already gone
    if rust > 0:
        r = np.roll(np.roll(chip_mask, size // 3, axis=0), size // 5, axis=1)
        r = np.clip(r * 1.4 + chip_mask * .5 - .35, 0, 1) * rust
        col = col * (1 - r[..., None]) + np.array([.42, .22, .11]) * (0.75 + .5 * (lum(grain) - .5))[..., None] * r[..., None]
    return save_png(name, col)


def build_textures():
    S = 256
    steel = down(load_rgb("Metal032"), S)
    steel_l = lum(steel)
    steel_rgb = np.stack([steel_l * .92, steel_l * .95, steel_l * .97], -1)
    grain = down(load_rgb("Plastic010"), S)
    chips_a = chips_from("PaintedMetal004", S)
    chips_b = chips_from("PaintedMetal002", S)
    t = {}
    t["T_BV2_SteelGreen"] = painted("T_BV2_SteelGreen", hexc("#46564F"), np.clip(chips_a * .9, 0, 1), steel_rgb * .9, grain, S)
    t["T_BV2_SteelBare"] = save_png("T_BV2_SteelBare", steel_rgb * 1.05)
    t["T_BV2_Ivory"] = painted("T_BV2_Ivory", hexc("#B8AC8A"), np.clip(chips_b * .35, 0, 1), hexc("#8E8670"), grain, S, grime=.35)
    t["T_BV2_WornRed"] = painted("T_BV2_WornRed", hexc("#8E2A22"), np.clip(chips_a, 0, 1), steel_rgb, grain, S)
    t["T_BV2_Orange"] = painted("T_BV2_Orange", hexc("#D07A2C"), np.clip(chips_b * .5, 0, 1), hexc("#5A4A3A"), grain, S)
    t["T_BV2_Yellow"] = painted("T_BV2_Yellow", hexc("#D6B03A"), np.clip(chips_b * .5, 0, 1), hexc("#3A3A36"), grain, S)
    rubber = down(load_rgb("Rubber004"), 128)
    rl = lum(rubber)
    t["T_BV2_Rubber"] = save_png("T_BV2_Rubber", np.stack([rl, rl, rl], -1) * .35 + .03)
    t["T_BV2_Foam"] = save_png("T_BV2_Foam", np.stack([rl, rl * 1.02, rl * 1.05], -1) * .28 + .06)

    # worktop: dark graphite phenolic (an electronics-repair bench top): mottled, scratched, solder burns, cup rings,
    # a few amber epoxy drips, worn lighter toward the front edge where forearms rest
    rng = np.random.default_rng(7)
    base = lum(down(load_rgb("Metal032"), 512))
    top = np.ones((512, 512, 3)) * hexc("#33393B") * (0.92 + .22 * (base - base.mean()))[..., None]
    yy, xx = np.mgrid[0:512, 0:512] / 512.0
    top *= (1.0 + .10 * np.clip(.35 - yy, 0, 1) / .35)[..., None]
    for _ in range(260):    # scratches: thin light lines, mostly along the bench
        x0, y0 = rng.uniform(0, 512, 2)
        a = rng.normal(0, .35)
        L = rng.uniform(8, 70)
        for t_ in np.linspace(0, 1, int(L)):
            x, y = int(x0 + math.cos(a) * L * t_) % 512, int(y0 + math.sin(a) * L * t_) % 512
            top[y, x] = top[y, x] * .6 + hexc("#8C9496") * .4
    for _ in range(7):      # cup rings
        cx, cy, r = rng.random(), rng.random(), rng.uniform(.025, .05)
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
        top *= (1 + .18 * np.exp(-((d - r) / .003) ** 2))[..., None]
    for _ in range(90):     # solder burns
        x, y = rng.integers(0, 512, 2)
        top[max(0, y - 1):y + 2, max(0, x - 1):x + 2] *= .35
    for _ in range(6):      # amber epoxy drips
        x, y = rng.integers(0, 512, 2)
        top[max(0, y - 2):y + 3, max(0, x - 2):x + 3] = hexc("#B07A2A")
    t["T_BV2_Worktop"] = save_png("T_BV2_Worktop", top)

    # ESD mat, 1.00 x 0.56 m mapped to 512 x 288: 10 mm grid, 50 mm major, ruler ticks, corner legend.
    W, H = 1024, 576
    rub = down(load_rgb("Rubber004"), 576)[:, :, :]
    rub = np.concatenate([rub, rub[:, ::-1]], axis=1)[:H, :W]
    rl = lum(rub)
    mat = np.ones((H, W, 3)) * hexc("#22403B") * (0.85 + .3 * (rl - rl.mean()))[..., None]
    px_per_cm = W / 100.0
    for i in range(0, 101):
        x = int(round(i * px_per_cm))
        major = i % 5 == 0
        mat[:, max(0, x - (1 if major else 0)):x + 1] = mat[:, max(0, x - (1 if major else 0)):x + 1] * .55 + hexc("#7FA89C") * (.45 if major else .18)
    for j in range(0, 57):
        y = int(round(j * px_per_cm))
        if y >= H:
            continue
        major = j % 5 == 0
        mat[max(0, y - (1 if major else 0)):y + 1, :] = mat[max(0, y - (1 if major else 0)):y + 1, :] * .55 + hexc("#7FA89C") * (.45 if major else .18)
    for i in range(0, 101):          # ruler along the near edge
        x = int(round(i * px_per_cm))
        L = 22 if i % 10 == 0 else 14 if i % 5 == 0 else 8
        mat[H - L:H, max(0, x - 1):x + 1] = hexc("#E3A64A")
    blit_text(mat, "ESD SAFE // BRS-07 SERVICE MAT", 14, 14, 3, hexc("#E3A64A"))
    blit_text(mat, "1:1  CM", W - 120, 14, 3, hexc("#E3A64A"))
    t["T_BV2_Mat"] = save_png("T_BV2_Mat", mat)

    # paper with handwriting-like scribbles
    P = 256
    paper = np.ones((P, P, 3)) * hexc("#E2DCC6") * (0.92 + .1 * (lum(down(load_rgb("Plastic010"), P)) - .5))[..., None]
    for row in range(20, P - 10, 18):
        x = 14
        while x < P - 30:
            wlen = rng.integers(10, 40)
            for k in range(min(wlen, P - 1 - x)):
                yv = row + int(3 * math.sin((x + k) * .7 + row))
                paper[yv:yv + 2, x + k] = hexc("#2D3550")
            x += wlen + rng.integers(5, 12)
    t["T_BV2_Paper"] = save_png("T_BV2_Paper", paper)
    sticky = paper * hexc("#F2D35A") / hexc("#E2DCC6")
    t["T_BV2_Sticky"] = save_png("T_BV2_Sticky", sticky)

    # CRT glass: dark phosphor with faint scanlines and vignetting (the live readout is a Unity canvas on top)
    C = 256
    yy, xx = np.mgrid[0:C, 0:C] / C
    v = np.clip(1 - ((xx - .5) ** 2 + (yy - .5) ** 2) * 2.2, 0, 1)
    glass = np.stack([v * .05, v * .16, v * .10], -1) * (1 - .25 * ((np.arange(C) % 3 == 0)[:, None]))
    t["T_BV2_CrtGlass"] = save_png("T_BV2_CrtGlass", glass)

    # label strip for drawers
    lab = np.ones((64, 256, 3)) * hexc("#E8E2CC")
    blit_text(lab, "SCREWS M2-M3", 10, 20, 3, hexc("#202020"))
    t["T_BV2_Label"] = save_png("T_BV2_Label", lab)
    t.update(setting_textures(rng))
    t.update(wall_textures(rng))
    return t


WALL_UV = {}


def wall_textures(rng):
    """Wall pass: the steel pegboard (1.90 x 0.83 m, 25.4 mm hole pitch, a painted shadow board around the hanging
    tools and one empty screwdriver outline, stickers), the wall print sheet and kraft cardboard."""
    t = {}
    W, H = 1024, 448
    pxm = W / 1.90
    g = lum(down(load_rgb("Metal032"), 1024))[:H, :W]
    peg = np.ones((H, W, 3)) * hexc("#3D4845") * (1 + .18 * (g - g.mean()))[..., None]
    peg *= (.84 + .16 * np.arange(H)[:, None] / H)[..., None]                      # grime gathers low (rows bottom-up)
    pitch = .0254 * pxm
    Y, X = np.mgrid[0:H, 0:W]
    dx, dy = X % pitch - pitch / 2, Y % pitch - pitch / 2
    d = np.sqrt(dx ** 2 + dy ** 2)
    peg[(d >= 2.4) & (d < 3.4) & (dy < 0)] *= 1.35
    peg[(d >= 2.4) & (d < 3.4) & (dy > 0)] *= .7
    peg[d < 2.4] = hexc("#0E1110")
    for _ in range(26):                                                            # rust runs below a few holes
        hx = int((rng.integers(0, int(W / pitch))) * pitch + pitch / 2)
        hy = int((rng.integers(0, 10)) * pitch + pitch / 2)
        for k in range(int(rng.integers(6, 26))):
            if 0 <= hy - 3 - k < H:
                peg[hy - 3 - k, hx - 1:hx + 1] = peg[hy - 3 - k, hx - 1:hx + 1] * .6 + hexc("#6A3A1E") * .4 * (1 - k / 26)

    def U(x):
        return (x + .95) * pxm

    def V(z):
        return (z - .93) * H / .83

    paint_c = hexc("#CFC8AE")

    def seg(a, b, w=1.6, c=paint_c, alpha=.8):
        (x0, z0), (x1, z1) = a, b
        n = int(max(abs(U(x1) - U(x0)), abs(V(z1) - V(z0))) * 2) + 2
        for tt in np.linspace(0, 1, n):
            cx, cy = U(x0 + (x1 - x0) * tt), V(z0 + (z1 - z0) * tt)
            ys, xs = np.mgrid[int(cy - w):int(cy + w) + 1, int(cx - w):int(cx + w) + 1]
            ok = ((xs - cx) ** 2 + (ys - cy) ** 2 <= w * w) & (xs >= 0) & (xs < W) & (ys >= 0) & (ys < H)
            peg[ys[ok], xs[ok]] = peg[ys[ok], xs[ok]] * (1 - alpha) + c * alpha

    def poly(pts, closed=True):
        for a, b in zip(pts, pts[1:] + (pts[:1] if closed else [])):
            seg(a, b)

    def ring(cx, cz, r):
        poly([(cx + r * math.cos(a), cz + r * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 20)[:-1]])

    for x, L in ((-0.30, .16), (-0.26, .14), (-0.225, .12)):                       # wrenches
        poly([(x - .013, 1.465 - L), (x - .013, 1.465), (x + .013, 1.465), (x + .013, 1.465 - L)])
        ring(x, 1.477, .022)
        ring(x, 1.465 - L - .008, .018)
    for x in (-0.62, -0.50):                                                       # pliers / cutters
        poly([(x - .036, 1.268), (x - .014, 1.40), (x - .009, 1.485), (x + .009, 1.485), (x + .014, 1.40), (x + .036, 1.268)])
    sx = -0.14                                                                      # the screwdriver now on the desk
    poly([(sx - .017, 1.40), (sx - .017, 1.50), (sx + .017, 1.50), (sx + .017, 1.40), (sx + .005, 1.40), (sx + .005, 1.27),
          (sx - .005, 1.27), (sx - .005, 1.40)])
    cx, cz = U(.62), V(1.10)                                                       # union sticker
    dd = np.sqrt((X - cx) ** 2 + (Y - cz) ** 2)
    peg[dd < 24] = hexc("#E6E1D2")
    peg[(dd >= 19) & (dd < 24)] = hexc("#A8231C")
    blit_text(peg, "LOCAL", int(cx - 15), H - int(cz) - 9, 1, hexc("#1E2024"))
    blit_text(peg, "7", int(cx - 3), H - int(cz) + 1, 1, hexc("#A8231C"))
    x0, z0 = int(U(.76)), int(V(.99))                                              # lease sticker
    peg[z0:z0 + 20, x0:x0 + 50] = hexc("#E7C64A")
    blit_text(peg, "NSP 0742", x0 + 2, H - z0 - 14, 1, hexc("#1E2024"))
    t["T_BV2_Pegboard"] = save_png("T_BV2_Pegboard", peg)

    # kraft cardboard (shelf boxes): flutes, a tape band
    S = 256
    cb = np.ones((S, S, 3)) * hexc("#9A7046") * (1 + .18 * (lum(down(load_rgb("Plastic010"), S)) - .5)
                                                + .04 * np.sin(np.arange(S) * 2 * math.pi / 6)[None, :])[..., None]
    cb[112:144] = cb[112:144] * .5 + hexc("#C9A86A") * .5
    t["T_BV2_Cardboard"] = save_png("T_BV2_Cardboard", cb)

    # wall print sheet
    sheet = np.ones((512, 512, 3)) * hexc("#30302C")
    cur = {"x": 0, "y": 0, "row": 0}

    def add(key, art):
        h, w = art.shape[:2]
        if cur["x"] + w > 512:
            cur["x"], cur["y"], cur["row"] = 0, cur["y"] + cur["row"] + 2, 0
        assert cur["y"] + h <= 512, key
        sheet[cur["y"]:cur["y"] + h, cur["x"]:cur["x"] + w] = art
        WALL_UV[key] = ((cur["x"] + .5) / 512, (cur["y"] + .5) / 512, (cur["x"] + w - .5) / 512, (cur["y"] + h - .5) / 512)
        cur["x"] += w + 2
        cur["row"] = max(cur["row"], h)

    def line(img, x0, y0, x1, y1, c, w=1):
        hh = img.shape[0]
        for tt in np.linspace(0, 1, int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 2):
            x, y = int(x0 + (x1 - x0) * tt), int(y0 + (y1 - y0) * tt)
            img[max(0, hh - 1 - y - w // 2):hh - y + w // 2, max(0, x - w // 2):x + w // 2 + 1] = c

    def box(img, x0, y0, x1, y1, c, w=1):
        for a, b in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
            line(img, a[0], a[1], b[0], b[1], c, w)

    def paperish(w, h, base):
        return np.ones((h, w, 3)) * hexc(base) * (1 + .12 * (lum(down(load_rgb("Plastic010"), 512))[:h, :w] - .5))[..., None]

    ink, red, blue, white = hexc("#1E2024"), hexc("#A8231C"), hexc("#24346A"), hexc("#D8E4F0")
    sc = paperish(160, 220, "#284A80")                                             # UNIT 07 cyanotype schematic
    for k in range(0, 220, 16):
        sc[k, :] = hexc("#33578E")
    for k in range(0, 160, 16):
        sc[:, k] = hexc("#33578E")
    box(sc, 4, 4, 155, 215, white)
    box(sc, 48, 26, 112, 78, white, 2)
    box(sc, 56, 33, 104, 70, white)
    box(sc, 58, 82, 102, 150, white, 2)
    line(sc, 58, 92, 30, 128, white, 2)
    line(sc, 102, 92, 130, 128, white, 2)
    box(sc, 66, 150, 94, 178, white)
    line(sc, 80, 120, 132, 100, white)
    blit_text(sc, "BRG 608", 104, 92, 1, white)
    line(sc, 70, 140, 20, 160, white)
    blit_text(sc, "PSU 48V", 8, 162, 1, white)
    line(sc, 100, 50, 132, 40, white)
    blit_text(sc, "J7", 134, 36, 1, white)
    box(sc, 92, 188, 155, 215, white)
    blit_text(sc, "UNIT 07", 96, 192, 1, white)
    blit_text(sc, "REV C", 96, 202, 1, white)
    add("schematic", sc)
    cal = paperish(128, 192, "#ECEBE4")                                            # port shipping calendar
    cal[192 - 84:192 - 4, 4:124] = hexc("#8DA3AE")
    cal[192 - 30:192 - 4, 4:124] = hexc("#2F4F5E")
    cal[192 - 40:192 - 26, 20:110] = hexc("#5E2A22")
    for i, c in enumerate(("#B5562E", "#2E6E8E", "#C9A04A", "#3B6B4A", "#B5562E", "#2E6E8E")):
        cal[192 - 50:192 - 40, 26 + i * 13:37 + i * 13] = hexc(c)
    cal[192 - 76:192 - 30, 100:104] = hexc("#2C3236")
    cal[192 - 78:192 - 74, 70:118] = hexc("#2C3236")
    blit_text(cal, "NSP SHIPPING", 6, 8, 1, white)
    blit_text(cal, "OCT", 6, 90, 2, red)
    for dday in range(31):
        r, c = dday // 7, dday % 7
        x, y = 4 + c * 17, 108 + r * 16
        blit_text(cal, str(dday + 1), x + 1, y + 2, 1, ink)
        if dday < 19:
            line(cal, x, y, x + 14, y + 13, red)
            line(cal, x + 14, y, x, y + 13, red)
    cx, cy = 4 + 6 * 17 + 7, 108 + 2 * 16 + 7
    for a in np.linspace(0, 2 * math.pi, 30):
        x, y = int(cx + 10 * math.cos(a)), int(cy + 8 * math.sin(a))
        cal[191 - y, x:x + 2] = red
    K.paint(cal, K.text_mask("房租", "C:/Windows/Fonts/simkai.ttf", 34, 16, pad=.02, wobble=.01), red, 1.0, 90, 192 - 108 - 16 * 2 - 18)
    add("calendar", cal)
    pm = paperish(144, 96, "#E9E1C6")                                              # repair permit
    box(pm, 3, 3, 140, 92, hexc("#7A6A4A"))
    box(pm, 6, 6, 137, 89, hexc("#7A6A4A"))
    blit_text(pm, "NSP", 12, 12, 2, ink)
    blit_text(pm, "REPAIR PERMIT", 52, 16, 1, ink)
    blit_text(pm, "NO. 0742", 12, 40, 2, blue)
    for a in np.linspace(0, 2 * math.pi, 40):
        for rr in (16, 12):
            x, y = int(112 + rr * math.cos(a)), int(68 + rr * math.sin(a))
            pm[95 - y, x] = red
    blit_text(pm, "NSP", 103, 64, 1, red)
    for k in range(40):
        x = 14 + k
        y = 74 + int(4 * math.sin(k * .5))
        pm[95 - y, x] = blue
    add("permit", pm)
    for i, txt in enumerate(("M3", "FUSE", "608", "CAP", "RELAY", "SEAL")):
        lb = paperish(64, 24, "#E8E2CC")
        blit_text(lb, txt, max(2, 32 - len(txt) * 6), 5, 2, ink)
        add("bin%d" % i, lb)
    for i, txt in enumerate(("UNIT SVC", "NSP REG", "CRT 14", "PARTS", "TAX")):
        sp = paperish(136, 24, "#E8E2CC")
        blit_text(sp, txt, 6, 5, 2, ink)
        add("spine%d" % i, np.rot90(sp, 1).copy())
    fr = paperish(96, 40, "#ECEBE4")
    blit_text(fr, "FRAGILE", 6, 4, 2, red)
    K.paint(fr, K.text_mask("易碎", "C:/Windows/Fonts/simhei.ttf", 40, 18, pad=.02), red, 1.0, 28, 2)
    add("fragile", fr)
    nb = np.ones((40, 96, 3)) * hexc("#9C7448")
    blit_text(nb, "NSP 0742", 2, 12, 2, hexc("#1A1A1A"))
    add("nspbox", nb)
    t["T_BV2_WallPrints"] = save_png("T_BV2_WallPrints", sheet)
    return t


def setting_textures(rng):
    """Prints that carry the setting: NSP port lease tags, work order, rent notice, seal tape, meter faces, LCD."""
    t = {}
    ink, red, amber = hexc("#1E2024"), hexc("#A8231C"), hexc("#FFB23A")

    def paper(w, h, base="#E4DDC5"):
        g = lum(down(load_rgb("Plastic010"), max(w, h)))[:h, :w]
        return np.ones((h, w, 3)) * hexc(base) * (0.9 + .14 * (g - .5))[..., None]

    def hline(img, y, x0, x1, c, th=1):
        img[img.shape[0] - y - th:img.shape[0] - y, x0:x1] = c

    def box(img, x0, y0, x1, y1, c):
        hline(img, y0, x0, x1, c); hline(img, y1, x0, x1, c)
        img[img.shape[0] - y1:img.shape[0] - y0, x0:x0 + 1] = c
        img[img.shape[0] - y1:img.shape[0] - y0, x1 - 1:x1] = c

    def stamp(img, cx, cy, r, c):
        H = img.shape[0]
        yy, xx = np.mgrid[0:H, 0:img.shape[1]]
        d = np.sqrt((xx - cx) ** 2 + (yy - (H - cy)) ** 2)
        ring = (abs(d - r) < 2.2) | (abs(d - r * .78) < 1.4)
        img[ring] = img[ring] * .35 + c * .65

    # yellow port-lease tag (on the leased test fixture and the tool tray)
    tag = paper(256, 96, "#E7C64A")
    blit_text(tag, "NSP PORT", 10, 8, 3, ink)
    blit_text(tag, "LEASED 0742", 10, 34, 3, ink)
    blit_text(tag, "VOID IF OPENED", 10, 64, 2, red)
    t["T_BV2_LeaseTag"] = save_png("T_BV2_LeaseTag", tag)
    # work order on the clipboard
    wo = paper(192, 256)
    blit_text(wo, "NSP REPAIR ORDER", 8, 8, 2, ink)
    blit_text(wo, "NO. 1017-04", 8, 26, 2, ink)
    for i, row in enumerate(("ITEM", "OWNER", "SEAL", "FEE", "RETEST")):
        blit_text(wo, row, 8, 54 + i * 26, 2, ink)
        hline(wo, 256 - (70 + i * 26), 70, 184, hexc("#5A5E66"))
    for k in range(5):     # handwritten fill-ins
        x = 74
        while x < 176:
            w = int(rng.integers(6, 22))
            for j in range(w):
                yv = 256 - (64 + k * 26) + int(2 * math.sin((x + j) * .8 + k))
                wo[yv:yv + 2, x + j] = hexc("#24346A")
            x += w + int(rng.integers(3, 8))
    stamp(wo, 150, 214, 26, red)
    blit_text(wo, "SEAL", 136, 206, 2, red)
    t["T_BV2_WorkOrder"] = save_png("T_BV2_WorkOrder", wo)
    # rent notice (pink slip)
    rn = paper(160, 112, "#E8B9B4")
    blit_text(rn, "RENT DUE", 8, 8, 3, red)
    blit_text(rn, "1200", 8, 38, 4, ink)
    blit_text(rn, "BY NIGHT 3", 8, 78, 2, ink)
    box(rn, 4, 4, 156, 108, red)
    t["T_BV2_RentNote"] = save_png("T_BV2_RentNote", rn)
    # inspection seal tape (repeated print; the roll's outer wrap)
    st = paper(256, 32, "#EFEDE6")
    for x in range(0, 256, 128):
        blit_text(st, "VOID IF OPENED", x + 4, 9, 2, red)
    t["T_BV2_SealTape"] = save_png("T_BV2_SealTape", st)
    # port asset stencil for the cabinet door (surplus port furniture)
    sc = np.ones((64, 256, 3)) * hexc("#3E4C45")
    blit_text(sc, "NSP ASSET 0412", 8, 8, 3, hexc("#C9C2A8"))
    blit_text(sc, "RETURN TO DEPOT", 8, 38, 2, hexc("#C9C2A8"))
    t["T_BV2_AssetStencil"] = save_png("T_BV2_AssetStencil", sc)
    # analogue meter face: ivory, arc scale, ticks, unit
    M = 128
    mf = paper(M, M, "#E9E2CB")
    cx, cy, r = 64, 26, 86
    for k in range(21):
        a = math.radians(135 - k * 4.5)
        L = 10 if k % 5 == 0 else 5
        for s_ in np.linspace(r - L, r, 12):
            x, y = int(cx + s_ * math.cos(a)), int(cy + s_ * math.sin(a))
            if 0 <= x < M and 0 <= y < M:
                mf[y, max(0, x - 1):x + 1] = ink
    for k in range(60):
        a = math.radians(135 - k * 1.5)
        x, y = int(cx + r * math.cos(a)), int(cy + r * math.sin(a))
        if 0 <= x < M and 0 <= y < M:
            mf[y, x] = ink
    a0 = math.radians(135 - 16 * 4.5)
    for k in range(int(4 * 4.5 / 1.5)):    # red zone at the top of the scale
        a = a0 - math.radians(k * 1.5)
        for s_ in np.linspace(r - 4, r, 4):
            x, y = int(cx + s_ * math.cos(a)), int(cy + s_ * math.sin(a))
            if 0 <= x < M and 0 <= y < M:
                mf[y, x] = red
    t["T_BV2_MeterFace"] = save_png("T_BV2_MeterFace", mf)
    # amber segment LCD for the handheld tester (emissive)
    lcd = np.ones((48, 128, 3)) * hexc("#1A1206")
    blit_text(lcd, "88.4", 8, 10, 4, amber)
    blit_text(lcd, "HZ", 100, 22, 2, amber * .8)
    t["T_BV2_Lcd"] = save_png("T_BV2_Lcd", lcd)
    # fixture front panel print: labels around jacks and switches
    fp = np.ones((96, 256, 3)) * hexc("#2E3634")
    blit_text(fp, "TEST FIXTURE / LOAD", 8, 6, 2, hexc("#D7D0B8"))
    blit_text(fp, "V", 52, 70, 2, hexc("#D7D0B8"))
    blit_text(fp, "MA", 140, 70, 2, hexc("#D7D0B8"))
    blit_text(fp, "PWR", 200, 30, 2, hexc("#D7D0B8"))
    blit_text(fp, "LINK", 196, 70, 2, hexc("#D7D0B8"))
    t["T_BV2_FixturePanel"] = save_png("T_BV2_FixturePanel", fp)
    # polaroid: two small figures in front of a harbour crane, faded, white frame
    ph = np.ones((128, 112, 3)) * hexc("#ECE8DD")
    img = np.ones((96, 96, 3)) * hexc("#5F7C86")
    img[:40] = hexc("#3A4A44")                                    # quay
    img[40:44] = hexc("#2A3230")
    for (x0, x1, y0, y1, c) in ((60, 64, 40, 92, "#2C3236"), (40, 90, 86, 90, "#2C3236"), (30, 40, 40, 70, "#C9A07A"), (46, 56, 40, 64, "#B88C6A")):
        img[y0:y1, x0:x1] = hexc(c)                                # crane mast + jib, two people
    img[64:72, 32:39] = hexc("#2A2A2A"); img[58:64, 48:55] = hexc("#2A2A2A")
    img = img * .85 + hexc("#E8C9A0") * .15                        # faded warm cast
    ph[24:120, 8:104] = img
    t["T_BV2_Photo"] = save_png("T_BV2_Photo", ph)
    tl = paper(128, 32, "#F1EFE6")                                 # handwritten cassette label
    blit_text(tl, "MIX 07 / NIGHT", 6, 9, 2, hexc("#2A3C8A"))
    t["T_BV2_TapeLabel"] = save_png("T_BV2_TapeLabel", tl)
    mf = np.ones((32, 40, 3)) * hexc("#0E2A18")                    # mini UNIT 07 face: green smile
    g = hexc("#62FF8E")
    mf[20:24, 10:13] = g; mf[20:24, 27:30] = g
    mf[9:11, 12:28] = g; mf[11:13, 10:12] = g; mf[11:13, 28:30] = g
    t["T_BV2_MiniFace"] = save_png("T_BV2_MiniFace", mf)
    return t


FONT = {  # 5x7
    "A": "01110 10001 10001 11111 10001 10001 10001", "B": "11110 10001 11110 10001 10001 10001 11110",
    "C": "01111 10000 10000 10000 10000 10000 01111", "D": "11110 10001 10001 10001 10001 10001 11110",
    "E": "11111 10000 11110 10000 10000 10000 11111", "F": "11111 10000 11110 10000 10000 10000 10000",
    "G": "01111 10000 10000 10011 10001 10001 01111", "H": "10001 10001 11111 10001 10001 10001 10001",
    "I": "11111 00100 00100 00100 00100 00100 11111", "K": "10001 10010 11100 10010 10001 10001 10001",
    "L": "10000 10000 10000 10000 10000 10000 11111", "M": "10001 11011 10101 10001 10001 10001 10001",
    "N": "10001 11001 10101 10011 10001 10001 10001", "O": "01110 10001 10001 10001 10001 10001 01110",
    "P": "11110 10001 10001 11110 10000 10000 10000", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 01110 00001 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    "U": "10001 10001 10001 10001 10001 10001 01110", "V": "10001 10001 10001 10001 10001 01010 00100",
    "W": "10001 10001 10001 10101 10101 11011 10001", "X": "10001 01010 00100 00100 00100 01010 10001",
    "Y": "10001 01010 00100 00100 00100 00100 00100", "0": "01110 10011 10101 11001 10001 10001 01110",
    "1": "00100 01100 00100 00100 00100 00100 01110", "2": "01110 10001 00001 00110 01000 10000 11111",
    "3": "11110 00001 01110 00001 00001 00001 11110", "7": "11111 00001 00010 00100 01000 01000 01000",
    "-": "00000 00000 00000 11111 00000 00000 00000", "/": "00001 00010 00010 00100 01000 01000 10000",
    ":": "00000 01100 01100 00000 01100 01100 00000", " ": "00000 00000 00000 00000 00000 00000 00000",
    "4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
    "6": "00110 01000 10000 11110 10001 10001 01110", "8": "01110 10001 10001 01110 10001 10001 01110",
    "9": "01110 10001 10001 01111 00001 00010 01100", "J": "00111 00010 00010 00010 00010 10010 01100",
    "Q": "01110 10001 10001 10001 10101 10010 01101", "Z": "11111 00001 00010 00100 01000 10000 11111",
    ".": "00000 00000 00000 00000 00000 01100 01100", "#": "01010 11111 01010 01010 01010 11111 01010",
    "%": "11001 11010 00010 00100 01000 01011 10011", "+": "00000 00100 00100 11111 00100 00100 00000",
}


def blit_text(img, text, x, y, scale, color):
    """Pixel text, (x, y) = top-left in image rows from the top (images are stored bottom-up in Blender)."""
    H = img.shape[0]
    for ch in text:
        g = FONT.get(ch.upper(), FONT[" "]).split()
        for r, row in enumerate(g):
            for c, bit in enumerate(row):
                if bit == "1":
                    y0 = H - (y + (r + 1) * scale)
                    img[max(0, y0):y0 + scale, x + c * scale:x + (c + 1) * scale] = color
        x += 6 * scale


# =====================================================================================================================
# Materials
# =====================================================================================================================

MATS = {}
MAT_SPEC = {
    # name: (texture, metallic, smoothness, emission rgb or None, uv_scale m->uv)
    "M_BV2_SteelGreen": ("T_BV2_SteelGreen", .35, .32, None, 2.2),
    "M_BV2_SteelBare": ("T_BV2_SteelBare", .9, .48, None, 3.0),
    "M_BV2_Worktop": ("T_BV2_Worktop", 0, .22, None, .6),
    "M_BV2_Mat": ("T_BV2_Mat", 0, .12, None, 1.0),
    "M_BV2_Ivory": ("T_BV2_Ivory", 0, .25, None, 3.0),
    "M_BV2_WornRed": ("T_BV2_WornRed", .2, .35, None, 3.0),
    "M_BV2_Orange": ("T_BV2_Orange", 0, .3, None, 6.0),
    "M_BV2_Yellow": ("T_BV2_Yellow", 0, .3, None, 5.0),
    "M_BV2_Rubber": ("T_BV2_Rubber", 0, .15, None, 6.0),
    "M_BV2_Foam": ("T_BV2_Foam", 0, .05, None, 4.0),
    "M_BV2_Paper": ("T_BV2_Paper", 0, .05, None, 1.0),
    "M_BV2_Sticky": ("T_BV2_Sticky", 0, .05, None, 1.0),
    "M_BV2_Label": ("T_BV2_Label", 0, .1, None, 1.0),
    "M_BV2_CrtGlass": ("T_BV2_CrtGlass", 0, .85, (.05, .25, .14), 1.0),
    "M_BV2_LeaseTag": ("T_BV2_LeaseTag", 0, .15, None, 1.0),
    "M_BV2_Hardboard": (None, 0, .2, None, 1.0),
    "M_BV2_Ceramic": (None, 0, .75, None, 1.0),
    "M_BV2_Plant": (None, 0, .3, None, 1.0),
    "M_BV2_Holo": (None, 0, .5, (.3, .95, 1.0), 1.0),
    "M_BV2_Photo": ("T_BV2_Photo", 0, .4, None, 1.0),
    "M_BV2_TapeLabel": ("T_BV2_TapeLabel", 0, .1, None, 1.0),
    "M_BV2_MiniFace": ("T_BV2_MiniFace", 0, .7, (.3, 1.0, .5), 1.0),
    "M_BV2_WorkOrder": ("T_BV2_WorkOrder", 0, .05, None, 1.0),
    "M_BV2_RentNote": ("T_BV2_RentNote", 0, .05, None, 1.0),
    "M_BV2_SealTape": ("T_BV2_SealTape", 0, .2, None, 1.0),
    "M_BV2_AssetStencil": ("T_BV2_AssetStencil", 0, .2, None, 1.0),
    "M_BV2_MeterFace": ("T_BV2_MeterFace", 0, .3, None, 1.0),
    "M_BV2_Lcd": ("T_BV2_Lcd", 0, .6, (1.0, .6, .15), 1.0),
    "M_BV2_FixturePanel": ("T_BV2_FixturePanel", .3, .3, None, 1.0),
    "M_BV2_Brass": (None, .95, .55, None, 1.0),
    "M_BV2_Copper": (None, .95, .5, None, 1.0),
    "M_BV2_Bulb": (None, 0, .6, (1.0, .78, .5), 1.0),
    "M_BV2_LedRed": (None, 0, .6, (1.0, .15, .1), 1.0),
    "M_BV2_LedGreen": (None, 0, .6, (.2, 1.0, .35), 1.0),
    "M_BV2_DarkPlastic": ("T_BV2_Rubber", 0, .4, None, 4.0),
    # wall pass
    "M_BV2_Pegboard": ("T_BV2_Pegboard", .3, .35, None, 1.0),
    "M_BV2_WallPrints": ("T_BV2_WallPrints", 0, .1, None, 1.0),
    "M_BV2_Cardboard": ("T_BV2_Cardboard", 0, .08, None, 3.0),
    "M_BV2_Tape": (None, 0, .45, None, 1.0),
    "M_BV2_Binder": (None, 0, .3, None, 1.0),
}
FLAT = {"M_BV2_Tape": (.80, .70, .50), "M_BV2_Binder": (.25, .30, .36), "M_BV2_Hardboard": (.30, .23, .16), "M_BV2_Ceramic": (.93, .91, .86), "M_BV2_Plant": (.33, .47, .32),
        "M_BV2_Holo": (.3, .95, 1.0), "M_BV2_HoloBeam": (.2, .7, .9),
        "M_BV2_Brass": (.70, .50, .22), "M_BV2_Copper": (.72, .36, .2), "M_BV2_Bulb": (1, .92, .75),
        "M_BV2_LedRed": (.9, .1, .1), "M_BV2_LedGreen": (.2, .9, .3)}


def build_materials(textures):
    for name, (tex, metal, smooth, emis, _) in MAT_SPEC.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = 1 - smooth
        if tex:
            node = nt.nodes.new("ShaderNodeTexImage")
            node.image = textures[tex]
            node.interpolation = "Closest"
            nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (*FLAT.get(name, (.5, .5, .5)), 1)
        if emis:
            bsdf.inputs["Emission Color"].default_value = (*emis, 1)
            bsdf.inputs["Emission Strength"].default_value = 2.0
        MATS[name] = m


# =====================================================================================================================
# Mesh helpers (bmesh). Every object gets box-projected UVs at its material's texel scale unless noted.
# =====================================================================================================================

ROOT = None
OBJECTS = []
INTERACT = {}


def new_obj(name, bm, mats, origin=(0, 0, 0), parent=None, uv="box", uv_scale=None, smooth_angle=35, note=None, role="static"):
    """bm is in WORLD coordinates; the object origin is placed at `origin` (world) and vertices re-expressed."""
    o = Vector(origin)
    for v in bm.verts:
        v.co -= o
    me = bpy.data.meshes.new(name)
    if uv == "box":
        box_uv(bm, mats, o, uv_scale)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(MATS[m])
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    ob = bpy.data.objects.new(name, me)
    SCENE.collection.objects.link(ob)
    ob.location = o
    par = parent or ROOT
    if par is not None:
        ob.parent = par
        ob.matrix_parent_inverse = world(par).inverted()
    ob["bv2_role"] = role
    if note:
        ob["bv2_note"] = note
    OBJECTS.append(ob)
    return ob


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    SCENE.collection.objects.link(e)
    e.location = Vector(loc)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = world(parent).inverted()
    OBJECTS.append(e)
    return e


def world(ob):
    """World matrix from the authored locations (objects here are never rotated or scaled), so it is valid before
    the depsgraph has evaluated freshly created objects."""
    # Every object's location is authored in world space and its parent inverse cancels the parent, so the world
    # matrix is just the translation.
    return Matrix.Translation(ob.location)


def box_uv(bm, mats, origin, scale):
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        s = scale if scale else MAT_SPEC[mats[min(f.material_index, len(mats) - 1)]][4]
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co + origin
            if ax == 2:
                u, v = p.x, p.y
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.y, p.z
            l[uvl].uv = (u * s, v * s)


def bm_new():
    return bmesh.new()


def add_box(bm, lo, hi, mat=0, bevel=0.0, segs=2):
    lo, hi = Vector(lo), Vector(hi)
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    for v in verts:
        v.co = Vector((lo.x if v.co.x < 0 else hi.x, lo.y if v.co.y < 0 else hi.y, lo.z if v.co.z < 0 else hi.z))
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=segs, affect="EDGES", profile=.5, material=mat)
        verts = [v for v in verts if v.is_valid]
    return verts


def add_cyl(bm, center, r, h, axis="Z", segs=16, mat=0, r2=None, bevel=0.0):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h)
    verts = res["verts"]
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(math.pi / 2, 3, "X")}[axis]
    for v in verts:
        v.co = rot @ v.co + Vector(center)
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat
    if bevel > 0:
        edges = [e for e in {e for v in verts for e in v.link_edges} if len(e.link_faces) == 2 and e.calc_face_angle(0) > .6]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect="EDGES", profile=.5, material=mat)
        verts = [v for v in verts if v.is_valid]
    return verts


def add_lathe(bm, profile, center, axis="Z", segs=24, mat=0):
    """profile: list of (radius, height) from bottom to top; revolved around `axis` through `center`."""
    rings = []
    for r, h in profile:
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), h)))
        rings.append(ring)
    faces = []
    for k in range(len(rings) - 1):
        for i in range(segs):
            a, b = rings[k][i], rings[k][(i + 1) % segs]
            c, d = rings[k + 1][(i + 1) % segs], rings[k + 1][i]
            if a.co == d.co and b.co == c.co:
                continue
            try:
                faces.append(bm.faces.new((a, b, c, d)))
            except ValueError:
                pass
    if profile[0][0] > 1e-5:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if profile[-1][0] > 1e-5:
        faces.append(bm.faces.new(rings[-1]))
    bmesh.ops.remove_doubles(bm, verts=[v for r in rings for v in r], dist=1e-6)
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(-math.pi / 2, 3, "X"),
           "-Y": Matrix.Rotation(math.pi / 2, 3, "X")}[axis]
    vs = [v for v in bm.verts if v.is_valid and v in set(x for r in rings for x in r if x.is_valid)]
    for v in vs:
        v.co = rot @ v.co + Vector(center)
    for f in faces:
        if f.is_valid:
            f.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in faces if f.is_valid])
    return vs


def add_sweep(bm, points, r, sides=8, mat=0):
    """Tube along a polyline with parallel-transported frames (cables, wire, lamp springs)."""
    pts = [Vector(p) for p in points]
    rings = []
    prev_n = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev_n is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
            n = t.cross(ref).normalized()
        else:
            n = (prev_n - t * prev_n.dot(t)).normalized()
        b = t.cross(n)
        prev_n = n
        rings.append([bm.verts.new(p + (n * math.cos(2 * math.pi * k / sides) + b * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    for k in range(len(rings) - 1):
        for i in range(sides):
            f = bm.faces.new((rings[k][i], rings[k][(i + 1) % sides], rings[k + 1][(i + 1) % sides], rings[k + 1][i]))
            f.material_index = mat
    for ring in (rings[0], rings[-1]):
        try:
            f = bm.faces.new(ring)
            f.material_index = mat
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=list({f for r in rings for v in r for f in v.link_faces}))


def bezier(p0, p1, p2, p3, n=16):
    out = []
    for i in range(n + 1):
        t = i / n
        a = [(1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3]
        out.append(tuple(sum(a[k] * [p0, p1, p2, p3][k][j] for k in range(4)) for j in range(3)))
    return out


def print_quad(name, mat, corners, parent=None, role="print"):
    """Flat printed label: corners (4 world points, counter-clockwise from bottom-left as seen from the front),
    UV 0..1 over it."""
    bm = bm_new()
    vs = [bm.verts.new(c) for c in corners]
    f = bm.faces.new(vs)
    uvl = bm.loops.layers.uv.verify()
    for l, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        l[uvl].uv = uv
    c = sum((Vector(v) for v in corners), Vector()) / 4
    return new_obj(name, bm, [mat], origin=tuple(c), parent=parent, uv=None, role=role)


def front_quad(name, mat, x0, x1, z0, z1, y, parent=None):
    """Label on a face looking toward -Y (the player)."""
    return print_quad(name, mat, [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], parent)


def top_quad(name, mat, x0, x1, y0, y1, z, parent=None):
    """Label lying flat, readable from the player's side (-Y at the bottom)."""
    return print_quad(name, mat, [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], parent)


def rotate_verts(verts, angle, axis, pivot):
    m = Matrix.Rotation(angle, 3, axis)
    for v in verts:
        v.co = m @ (v.co - Vector(pivot)) + Vector(pivot)


# =====================================================================================================================
# Bench structure
# =====================================================================================================================


def build_bench():
    # Worktop: 40 mm sealed chipboard, rounded front corners, steel C-lip on the front and both ends.
    bm = bm_new()
    add_box(bm, (-0.955, 0.150, T - 0.040), (0.955, 0.950, T), 0, bevel=.004, segs=2)
    new_obj("BV2_Top", bm, ["M_BV2_Worktop"], origin=(0, 0.55, T), role="static", note="work surface z 0.90")
    bm = bm_new()
    add_box(bm, (-0.962, 0.138, T - 0.052), (0.962, 0.151, T + 0.003), 0, bevel=.0025)
    add_box(bm, (-0.962, 0.138, T - 0.052), (-0.952, 0.95, T + 0.003), 0, bevel=.0025)
    add_box(bm, (0.952, 0.138, T - 0.052), (0.962, 0.95, T + 0.003), 0, bevel=.0025)
    for x in np.linspace(-0.9, 0.9, 13):  # rivets along the lip
        add_cyl(bm, (x, 0.137, T - 0.024), .0045, .004, "Y", 8, 0)
    new_obj("BV2_TopLip", bm, ["M_BV2_SteelBare"], origin=(0, 0.14, T))

    # ESD mat with bevelled rubber edge
    bm = bm_new()
    add_box(bm, (-0.20, 0.19, T), (0.52, 0.75, MAT_Z), 0, bevel=.0015, segs=1)
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            p = l.vert.co
            l[uvl].uv = ((p.x + 0.20) / 1.0, (p.y - 0.19) / 0.56)   # 1 texel-cm stays 1 cm; the right of the print is unused
    new_obj("BV2_Mat", bm, ["M_BV2_Mat"], origin=(0.16, 0.47, T), uv=None, role="static")

    # Frame: 40 mm square tube legs, levelling feet, rails and a slatted lower shelf
    bm = bm_new()
    for x in (-0.92, 0.92):
        for y in (0.19, 0.91):
            add_box(bm, (x - .02, y - .02, 0.035), (x + .02, y + .02, T - 0.040), 0, bevel=.003)
    for y in (0.19, 0.91):
        add_box(bm, (-0.90, y - .015, 0.70), (0.90, y + .015, 0.73), 0, bevel=.003)
        add_box(bm, (-0.90, y - .015, 0.12), (0.90, y + .015, 0.15), 0, bevel=.003)
    for x in (-0.92, 0.92):
        add_box(bm, (x - .015, 0.21, 0.12), (x + .015, 0.89, 0.15), 0, bevel=.003)
    for i in range(9):
        y = 0.24 + i * 0.077
        add_box(bm, (-0.90, y - .028, 0.15), (0.90, y + .028, 0.165), 0, bevel=.002)
    new_obj("BV2_Frame", bm, ["M_BV2_SteelGreen"], origin=(0, 0.55, 0))
    bm = bm_new()
    for x in (-0.92, 0.92):
        for y in (0.19, 0.91):
            add_cyl(bm, (x, y, 0.024), .008, .03, "Z", 10, 0)
            add_cyl(bm, (x, y, 0.006), .026, .012, "Z", 16, 1, r2=.022)
    new_obj("BV2_Feet", bm, ["M_BV2_SteelBare", "M_BV2_Rubber"], origin=(0, 0.55, 0))

    # Left drawer pedestal (3 drawers) and right cabinet with a hinged door
    bm = bm_new()
    add_box(bm, (-0.90, 0.20, 0.17), (-0.88, 0.90, T - 0.045), 0, bevel=.002)
    add_box(bm, (-0.52, 0.20, 0.17), (-0.50, 0.90, T - 0.045), 0, bevel=.002)
    add_box(bm, (-0.90, 0.88, 0.17), (-0.50, 0.90, T - 0.045), 0)
    add_box(bm, (-0.90, 0.20, 0.17), (-0.50, 0.90, 0.19), 0, bevel=.002)
    new_obj("BV2_Pedestal", bm, ["M_BV2_SteelGreen"], origin=(-0.70, 0.55, 0.17))
    heights = [(0.62, 0.84), (0.40, 0.61), (0.19, 0.39)]
    for k, (z0, z1) in enumerate(heights):
        bm = bm_new()
        add_box(bm, (-0.875, 0.188, z0 + .004), (-0.525, 0.204, z1 - .004), 0, bevel=.003)          # front
        add_box(bm, (-0.865, 0.204, z0 + .01), (-0.535, 0.86, z0 + .02), 0)                         # bottom
        for x in (-0.865, -0.545):
            add_box(bm, (x, 0.204, z0 + .01), (x + .01, 0.86, z1 - .03), 0)                         # sides
        add_box(bm, (-0.865, 0.85, z0 + .01), (-0.535, 0.86, z1 - .03), 0)                          # back
        for x in (-0.76, -0.64):                                                                    # bar handle
            add_box(bm, (x - .006, 0.172, (z0 + z1) / 2 + .03 - .006), (x + .006, 0.188, (z0 + z1) / 2 + .03 + .006), 1)
        add_cyl(bm, (-0.70, 0.168, (z0 + z1) / 2 + .03), .006, .14, "X", 10, 1)
        add_box(bm, (-0.745, 0.186, (z0 + z1) / 2 - .035), (-0.655, 0.188, (z0 + z1) / 2 - .005), 1)  # label holder
        bm2 = bm
        ob = new_obj("BV2_Drawer_%d" % (k + 1), bm2, ["M_BV2_SteelGreen", "M_BV2_SteelBare"], origin=(-0.70, 0.188, (z0 + z1) / 2),
                     role="drawer", note="slides along -Y up to 0.30 m")
        bm = bm_new()
        add_box(bm, (-0.742, 0.1855, (z0 + z1) / 2 - .032), (-0.658, 0.1862, (z0 + z1) / 2 - .008), 0)
        uvl = bm.loops.layers.uv.verify()
        for f in bm.faces:
            for l in f.loops:
                p = l.vert.co
                l[uvl].uv = ((p.x + 0.742) / 0.084, (p.z - ((z0 + z1) / 2 - .032)) / 0.024)
        new_obj("BV2_Drawer_%d_Label" % (k + 1), bm, ["M_BV2_Label"], origin=(-0.70, 0.186, (z0 + z1) / 2), parent=ob, uv=None)
        INTERACT[ob.name] = {"kind": "drawer", "travel": 0.30}
        # drawer contents: a handful of parts so an open drawer is not empty
        bm = bm_new()
        for i in range(6):
            x = -0.84 + i * 0.05
            add_cyl(bm, (x, 0.30 + (i % 3) * .12, z0 + .035), .012, .03, "Z", 10, i % 2)
        new_obj("BV2_Drawer_%d_Contents" % (k + 1), bm, ["M_BV2_Brass", "M_BV2_Rubber"], origin=(-0.70, 0.40, z0 + .02), parent=ob)

    bm = bm_new()
    add_box(bm, (0.50, 0.20, 0.17), (0.52, 0.90, T - 0.045), 0, bevel=.002)
    add_box(bm, (0.88, 0.20, 0.17), (0.90, 0.90, T - 0.045), 0, bevel=.002)
    add_box(bm, (0.50, 0.88, 0.17), (0.90, 0.90, T - 0.045), 0)
    add_box(bm, (0.50, 0.20, 0.17), (0.90, 0.90, 0.19), 0, bevel=.002)
    add_box(bm, (0.52, 0.22, 0.50), (0.88, 0.88, 0.515), 0)
    new_obj("BV2_Cabinet", bm, ["M_BV2_SteelGreen"], origin=(0.70, 0.55, 0.17))
    bm = bm_new()
    add_box(bm, (0.525, 0.186, 0.195), (0.875, 0.202, T - 0.05), 0, bevel=.003)
    for z in np.linspace(0.62, 0.78, 7):  # vent slots (raised louvres)
        add_box(bm, (0.60, 0.182, z), (0.80, 0.187, z + .008), 0, bevel=.0015, segs=1)
    add_box(bm, (0.835, 0.168, 0.42), (0.85, 0.186, 0.60), 1, bevel=.003)
    new_obj("BV2_CabinetDoor", bm, ["M_BV2_SteelGreen", "M_BV2_SteelBare"], origin=(0.525, 0.194, 0.50), role="door",
            note="hinge on the left edge, opens to -100 deg")
    INTERACT["BV2_CabinetDoor"] = {"kind": "door", "angle": -100}
    door = [o for o in OBJECTS if o.name == "BV2_CabinetDoor"][0]
    front_quad("BV2_CabinetDoor_Stencil", "M_BV2_AssetStencil", 0.56, 0.79, 0.30, 0.36, 0.1855, parent=door)
    bm = bm_new()  # what is behind the door
    add_box(bm, (0.56, 0.30, 0.19), (0.72, 0.62, 0.31), 0, bevel=.004)
    add_box(bm, (0.74, 0.40, 0.19), (0.86, 0.70, 0.27), 1, bevel=.004)
    add_cyl(bm, (0.64, 0.60, 0.585), .07, .14, "Z", 16, 1)
    new_obj("BV2_CabinetContents", bm, ["M_BV2_WornRed", "M_BV2_Ivory"], origin=(0.70, 0.5, 0.19))

    # Back riser: a low steel shelf along the back edge with a power strip and a small-parts organiser
    bm = bm_new()
    add_box(bm, (-0.46, 0.86, T), (0.46, 0.95, T + 0.012), 0, bevel=.002)
    for x in (-0.45, 0.44):
        add_box(bm, (x, 0.86, T), (x + .012, 0.95, T + 0.16), 0, bevel=.002)
    add_box(bm, (-0.46, 0.86, T + 0.15), (0.46, 0.95, T + 0.162), 0, bevel=.002)
    add_box(bm, (-0.46, 0.94, T), (0.46, 0.95, T + 0.16), 0)
    new_obj("BV2_Riser", bm, ["M_BV2_SteelGreen"], origin=(0, 0.9, T))
    bm = bm_new()
    add_box(bm, (-0.40, 0.875, T + 0.012), (0.10, 0.925, T + 0.048), 0, bevel=.004)
    for i in range(6):
        x = -0.36 + i * .075
        add_box(bm, (x - .012, 0.874, T + .022), (x + .012, 0.876, T + .04), 1)
        add_box(bm, (x - .004, 0.8738, T + .03), (x - .001, 0.8742, T + .036), 2)
        add_box(bm, (x + .001, 0.8738, T + .03), (x + .004, 0.8742, T + .036), 2)
    add_box(bm, (0.06, 0.873, T + .026), (0.085, 0.875, T + .038), 3, bevel=.002)
    new_obj("BV2_PowerStrip", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_LedRed"], origin=(-0.15, 0.9, T + 0.012))
    bm = bm_new()
    add_box(bm, (0.14, 0.865, T + 0.162), (0.44, 0.945, T + 0.30), 0, bevel=.003)
    for r in range(3):
        for c in range(4):
            x0, z0 = 0.15 + c * 0.072, T + 0.172 + r * 0.043
            add_box(bm, (x0, 0.859, z0), (x0 + 0.066, 0.866, z0 + 0.037), 1, bevel=.002, segs=1)
            add_box(bm, (x0 + .026, 0.855, z0 + .012), (x0 + .04, 0.859, z0 + .018), 2)
    new_obj("BV2_Organizer", bm, ["M_BV2_SteelGreen", "M_BV2_Ivory", "M_BV2_DarkPlastic"], origin=(0.29, 0.9, T + 0.162))
    build_test_fixture()


def build_test_fixture():
    """The repair flow's "external test connection / load" and "test power", made physical: a leased port test box
    on the riser with two needle meters, PWR and LINK lamps, banana jacks and a lead running to a clip pad at the back
    of the mat. Unity drives the needles and lamps from the flow (BV2_Fixture_Needle*, BV2_Fixture_Led*). Next to it a
    cartridge reader for the read-only checks."""
    z0, z1, y0, y1, x0, x1 = T + 0.162, T + 0.322, 0.868, 0.946, -0.45, -0.10
    bm = bm_new()
    add_box(bm, (x0, y0, z0), (x1, y1, z1), 0, bevel=.006)
    add_box(bm, (x0 + .01, y0 - .004, z0 + .006), (x1 - .01, y0, z1 - .006), 1, bevel=.002, segs=1)      # front plate
    for x in (x0 + .01, x1 - .03):
        add_box(bm, (x, y0 + .01, z1), (x + .02, y1 - .01, z1 + .012), 2, bevel=.003)                     # carry feet / handles
    add_cyl(bm, (x0 + .25, y0 - .008, z0 + .11), .006, .014, "Y", 10, 2)                                 # PWR toggle base
    add_box(bm, (x0 + .247, y0 - .03, z0 + .108), (x0 + .253, y0 - .006, z0 + .124), 2)                 # toggle lever
    for i, (x, m) in enumerate(((x0 + .235, 3), (x0 + .255, 4), (x0 + .275, 3), (x0 + .295, 4))):      # banana jacks
        add_cyl(bm, (x, y0 - .006, z0 + .035), .0055, .012, "Y", 10, m)
        add_cyl(bm, (x, y0 - .010, z0 + .035), .0025, .004, "Y", 8, 2)
    for x in (x0 + .06, x0 + .16):                                                                       # square meter bezels (open frames)
        zc = z0 + .085
        for (a0, a1, b0, b1) in ((x - .040, x + .040, zc + .034, zc + .040), (x - .040, x + .040, zc - .040, zc - .034),
                                 (x - .040, x - .034, zc - .034, zc + .034), (x + .034, x + .040, zc - .034, zc + .034)):
            add_box(bm, (a0, y0 - .010, b0), (a1, y0 - .002, b1), 2, bevel=.001, segs=1)
    new_obj("BV2_Fixture", bm, ["M_BV2_SteelGreen", "M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_WornRed", "M_BV2_DarkPlastic"],
            origin=((x0 + x1) / 2, (y0 + y1) / 2, z0), role="fixture")
    front_quad("BV2_Fixture_Panel", "M_BV2_FixturePanel", x0 + .012, x1 - .012, z1 - .052, z1 - .012, y0 - .0045)
    for name, x in (("V", x0 + .06), ("A", x0 + .16)):
        front_quad("BV2_Fixture_Meter" + name, "M_BV2_MeterFace", x - .034, x + .034, z0 + .052, z0 + .118, y0 - .0045)
        bm = bm_new()   # needle: pivot at the bottom centre of the dial, pointing straight up at rest (Unity swings it)
        add_box(bm, (x - .0022, y0 - .0062, z0 + .058), (x + .0022, y0 - .0052, z0 + .110), 0)
        add_cyl(bm, (x, y0 - .0062, z0 + .058), .004, .003, "Y", 10, 1)
        new_obj("BV2_Fixture_Needle" + name, bm, ["M_BV2_WornRed", "M_BV2_DarkPlastic"], origin=(x, y0 - .0058, z0 + .058), role="needle")
    for name, x, mat in (("Power", x0 + .30, "M_BV2_LedRed"), ("Link", x0 + .32, "M_BV2_LedGreen")):
        bm = bm_new()
        add_cyl(bm, (x, y0 - .006, z0 + .115), .0045, .004, "Y", 10, 0)
        new_obj("BV2_Fixture_Led" + name, bm, [mat], origin=(x, y0 - .006, z0 + .115), role="lamp")
    print_quad("BV2_Fixture_LeaseTag", "M_BV2_LeaseTag", [(x1 + .0005, y0 + .012, z0 + .02), (x1 + .0005, y0 + .062, z0 + .02),
                                                           (x1 + .0005, y0 + .062, z0 + .04), (x1 + .0005, y0 + .012, z0 + .04)])
    # test lead from the LINK jacks down over the riser edge to a clip pad at the back of the mat
    bm = bm_new()
    for dx, m in ((x0 + .235, 0), (x0 + .255, 1)):
        pts = bezier((dx, y0 - .014, z0 + .035), (dx, y0 - .08, z0 + .02), (0.0 + dx * .1, 0.74, T + .08), (0.02 + dx * .05, 0.705, T + .012), 16)
        add_sweep(bm, pts, .0026, 6, m)
    add_box(bm, (-0.03, 0.69, T + .004), (0.07, 0.725, T + .016), 2, bevel=.003)                          # clip pad
    for x in (-0.015, 0.055):
        add_box(bm, (x - .008, 0.684, T + .012), (x + .008, 0.70, T + .024), 3, bevel=.002)              # crocodile clips
    new_obj("BV2_FixtureLead", bm, ["M_BV2_WornRed", "M_BV2_Rubber", "M_BV2_DarkPlastic", "M_BV2_SteelBare"], origin=(0.02, 0.75, T + .03))
    # cartridge reader: slot, a half-inserted grey cartridge, amber lamp
    rx0, rx1 = -0.08, 0.10
    bm = bm_new()
    add_box(bm, (rx0, y0 + .005, z0), (rx1, y1 - .005, z0 + .07), 0, bevel=.006)
    add_box(bm, (rx0 + .03, y0 + .02, z0 + .07), (rx1 - .03, y1 - .02, z0 + .074), 1)                   # slot lip
    add_box(bm, (rx0 + .04, y0 + .03, z0 + .07), (rx1 - .04, y1 - .03, z0 + .13), 2, bevel=.003)        # cartridge
    add_box(bm, (rx0 + .045, y0 + .028, z0 + .10), (rx1 - .045, y0 + .03, z0 + .125), 3)                # cartridge label strip
    add_cyl(bm, (rx1 - .02, y0 + .004, z0 + .05), .004, .004, "Y", 10, 4)
    new_obj("BV2_CartReader", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelGreen", "M_BV2_Yellow", "M_BV2_LedGreen"],
            origin=((rx0 + rx1) / 2, (y0 + y1) / 2, z0))
    # rent notice taped under the riser's top shelf
    front_quad("BV2_RentNote", "M_BV2_RentNote", 0.17, 0.29, T + .055, T + .139, 0.857)
    bm = bm_new()
    add_box(bm, (0.20, 0.8565, T + .135), (0.26, 0.8575, T + .148), 0)
    new_obj("BV2_RentNoteTape", bm, ["M_BV2_Ivory"], origin=(0.23, 0.857, T + .14))


# =====================================================================================================================
# Desk props
# =====================================================================================================================


def build_tools():
    """Tool tray at the front left with the four repair tools. Each tool is its own object with its pivot at the
    centre of its foam slot; Unity lifts the selected one out of the tray."""
    OX, OY = 0.34, 0.12    # tray moved from the drawer side into the bench camera's view, left of the work area
    x0, x1, y0, y1 = -0.84 + OX, -0.54 + OX, 0.18 + OY, 0.37 + OY
    bm = bm_new()
    add_box(bm, (x0, y0, T), (x1, y1, T + 0.012), 0, bevel=.004)
    for (a, b, c, d) in ((x0, y0, x1, y0 + .008), (x0, y1 - .008, x1, y1), (x0, y0, x0 + .008, y1), (x1 - .008, y0, x1, y1)):
        add_box(bm, (a, b, T), (c, d, T + 0.032), 0, bevel=.002)
    add_box(bm, (x0 + .008, y0 + .008, T + 0.012), (x1 - .008, y1 - .008, T + 0.026), 1)
    new_obj("BV2_ToolTray", bm, ["M_BV2_SteelGreen", "M_BV2_Foam"], origin=((x0 + x1) / 2, (y0 + y1) / 2, T), role="tool_tray")
    front_quad("BV2_ToolTray_Tag", "M_BV2_LeaseTag", x0 + .03, x0 + .13, T + .006, T + .028, y0 - .0005)
    slots = {}
    top = T + 0.026

    # Screwdriver (螺丝刀): fluted orange/black handle, steel shaft, cross tip. Lies along X.
    cx, cy = -0.69 + OX, 0.205 + OY
    bm = bm_new()
    prof = [(0.0, 0.0), (0.009, 0.0), (0.0115, 0.006), (0.0125, 0.030), (0.0118, 0.070), (0.0105, 0.090), (0.0060, 0.098), (0.0035, 0.100)]
    add_lathe(bm, prof, (0, 0, 0), "Z", 12, 0)
    for z0_, z1_ in ((.012, .030), (.050, .062)):   # black electrical tape wraps over a cracked grip
        add_cyl(bm, (0, 0, (z0_ + z1_) / 2), .0131, z1_ - z0_, "Z", 12, 1)
    add_cyl(bm, (0, 0, .081), .0122, .008, "Z", 12, 3)    # white paint band (the shop's tool number)
    add_cyl(bm, (0, 0, 0.165), .0028, .13, "Z", 10, 2)
    add_cyl(bm, (0, 0, 0.233), .0028, .006, "Z", 10, 2, r2=.0008)
    vs = list(bm.verts)
    rotate_verts(vs, math.pi / 2, "Y", (0, 0, 0))
    for v in vs:
        v.co += Vector((cx - .12, cy, top + .0125))
    ob = new_obj("BV2_Tool_Screwdriver", bm, ["M_BV2_Orange", "M_BV2_Rubber", "M_BV2_SteelBare", "M_BV2_Ivory"], origin=(cx, cy, top), role="tool")
    INTERACT[ob.name] = {"kind": "tool", "tool": "RemoveFastener", "label": "螺丝刀"}

    # Opening tool (开壳): a ground-down flat steel pry bar with a bent tip, its handle end wrapped in cloth tape, a
    # thin pick taped alongside with yellow tape. Along X.
    cx, cy = -0.69 + OX, 0.245 + OY
    bm = bm_new()
    add_box(bm, (cx - .085, cy - .006, top), (cx + .060, cy + .006, top + .004), 0, bevel=.001, segs=1)
    tip = add_box(bm, (cx + .060, cy - .005, top), (cx + .085, cy + .005, top + .0025), 0)
    rotate_verts(tip, math.radians(-14), "Y", (cx + .060, cy, top))
    add_box(bm, (cx - .085, cy - .0085, top - .0015), (cx - .025, cy + .0085, top + .0055), 1, bevel=.002, segs=1)   # cloth tape grip
    add_cyl(bm, (cx - .01, cy + .012, top + .003), .0025, .13, "X", 8, 0)
    add_cyl(bm, (cx + .067, cy + .012, top + .003), .0025, .024, "X", 8, 0, r2=.0004)
    add_box(bm, (cx - .045, cy - .009, top - .002), (cx - .035, cy + .016, top + .007), 2, bevel=.001, segs=1)       # yellow tape binding
    ob = new_obj("BV2_Tool_Spudger", bm, ["M_BV2_SteelBare", "M_BV2_Foam", "M_BV2_Yellow"], origin=(cx, cy, top), role="tool")
    INTERACT[ob.name] = {"kind": "tool", "tool": "OpenHousing", "label": "开壳工具"}

    # Handheld tester (检测仪): a scuffed ivory field unit with a test cartridge in its top slot, rubber corners from a
    # different brand, a taped-on battery door, amber segment LCD, two leads. Lies flat.
    cx, cy = -0.635 + OX, 0.315 + OY
    bm = bm_new()
    add_box(bm, (cx - .042, cy - .05, top), (cx + .042, cy + .045, top + .028), 0, bevel=.006, segs=2)
    for sx in (-1, 1):
        for sy in (-1, 1):   # mismatched black rubber corner bumpers
            add_box(bm, (cx + sx * .042 - .01, cy + sy * .047 - .01, top - .001), (cx + sx * .042 + .01, cy + sy * .047 + .01, top + .03), 1, bevel=.003)
    add_box(bm, (cx - .030, cy + .045, top + .006), (cx + .030, cy + .075, top + .022), 2, bevel=.002)   # test cartridge sticking out
    add_box(bm, (cx - .028, cy + .060, top + .0215), (cx + .028, cy + .072, top + .0225), 3)            # its label
    add_cyl(bm, (cx + .018, cy - .022, top + .030), .011, .006, "Z", 16, 1, bevel=.001)                # range knob
    add_box(bm, (cx - .036, cy - .046, top + .028), (cx + .004, cy - .030, top + .030), 3)              # tape over battery door
    for dx in (-.026, -.010):
        add_cyl(bm, (cx + dx, cy - .038, top + .032), .0035, .006, "Z", 10, 4)
    ob = new_obj("BV2_Tool_Meter", bm, ["M_BV2_Ivory", "M_BV2_Rubber", "M_BV2_SteelGreen", "M_BV2_Yellow", "M_BV2_SteelBare"],
                 origin=(cx, cy, top), role="tool")
    top_quad("BV2_Tool_Meter_Lcd", "M_BV2_Lcd", cx - .030, cx + .010, cy + .004, cy + .032, top + .0285, parent=ob)
    INTERACT[ob.name] = {"kind": "tool", "tool": "ServiceModule", "label": "检测仪"}
    bm = bm_new()
    for dx, m, end in ((-.025, 0, (-0.78 + OX, 0.325 + OY)), (.025, 1, (-0.77 + OX, 0.345 + OY))):
        start = (cx + dx, cy - .036, top + .038)
        pts = bezier(start, (cx + dx, cy - .08, top + .05), (end[0] + .04, end[1] - .04, top + .01), (end[0], end[1], top + .006), 18)
        add_sweep(bm, pts, .0022, 6, m)
        add_cyl(bm, (end[0] - .03, end[1], top + .006), .005, .06, "X", 8, m)
        add_cyl(bm, (end[0] - .065, end[1], top + .006), .0012, .016, "X", 6, 2)
    new_obj("BV2_Tool_MeterLeads", bm, ["M_BV2_WornRed", "M_BV2_Rubber", "M_BV2_SteelBare"], origin=(cx, cy, top), parent=ob)

    # Loupe on a stand (检查 / 空手): ring, lens, handle. Lies at the right of the tray.
    cx, cy = -0.575 + OX, 0.22 + OY
    bm = bm_new()
    add_lathe(bm, [(0.020, 0.0), (0.027, 0.0), (0.027, 0.008), (0.020, 0.008)], (cx, cy + .012, top + .002), "Z", 24, 0)
    add_cyl(bm, (cx, cy + .012, top + .006), .020, .003, "Z", 24, 1)
    add_cyl(bm, (cx, cy - .035, top + .006), .006, .045, "Y", 10, 2)
    add_cyl(bm, (cx, cy - .040, top + .006), .0068, .014, "Y", 10, 3)       # cracked handle, yellow tape
    ob = new_obj("BV2_Tool_Loupe", bm, ["M_BV2_DarkPlastic", "M_BV2_CrtGlass", "M_BV2_Rubber", "M_BV2_Yellow"], origin=(cx, cy, top), role="tool")
    INTERACT[ob.name] = {"kind": "tool", "tool": "Inspect", "label": "检查（放大镜）"}


def build_lamp():
    """Spring-balanced task lamp clamped on the back edge, arching over the work area. Unity adds the light."""
    base = Vector((0.36, 0.935, T))
    shoulder = base + Vector((0, 0, 0.07))
    elbow = Vector((0.30, 0.78, T + 0.46))
    head = Vector((0.12, 0.56, T + 0.50))
    bm = bm_new()
    add_box(bm, (base.x - .03, base.y - .03, T - .05), (base.x + .03, base.y + .03, T + .012), 0, bevel=.004)
    add_cyl(bm, (base.x, base.y + .0, T - .035), .006, .07, "Y", 8, 1)
    add_cyl(bm, (base.x, base.y, T + 0.04), .014, .06, "Z", 12, 0)
    root = new_obj("BV2_Lamp", bm, ["M_BV2_SteelGreen", "M_BV2_SteelBare"], origin=tuple(base), role="lamp",
                   note="click to switch; Unity adds a spot light at BV2_Lamp_Bulb")
    INTERACT["BV2_Lamp"] = {"kind": "lamp"}
    bm = bm_new()
    for off in (-.012, .012):
        o = Vector((off, 0, 0))
        add_sweep(bm, [shoulder + o, elbow + o], .0045, 8, 0)
    add_sweep(bm, [shoulder + Vector((0, .02, .02)), elbow + Vector((0, .02, -.02))], .0035, 6, 1)  # spring
    add_cyl(bm, tuple(shoulder), .012, .04, "X", 12, 0)
    add_cyl(bm, tuple(elbow), .011, .04, "X", 12, 0)
    new_obj("BV2_Lamp_ArmLower", bm, ["M_BV2_SteelBare", "M_BV2_Copper"], origin=tuple(shoulder), parent=root)
    bm = bm_new()
    for off in (-.010, .010):
        o = Vector((off, 0, 0))
        add_sweep(bm, [elbow + o, head + o + Vector((0, 0, .04))], .004, 8, 0)
    new_obj("BV2_Lamp_ArmUpper", bm, ["M_BV2_SteelBare"], origin=tuple(elbow), parent=root)
    # head: a conical shade pointing down and slightly forward, with a vented cap
    bm = bm_new()
    prof = [(0.0, 0.10), (0.022, 0.10), (0.03, 0.085), (0.034, 0.07), (0.062, 0.012), (0.066, 0.0), (0.060, 0.0), (0.056, 0.012), (0.028, 0.07), (0.0, 0.07)]
    vs = add_lathe(bm, prof, (0, 0, 0), "Z", 24, 0)
    for i in range(8):
        a = 2 * math.pi * i / 8
        add_box(bm, (math.cos(a) * .02 - .002, math.sin(a) * .02 - .002, .1), (math.cos(a) * .02 + .002, math.sin(a) * .02 + .002, .106), 1)
    allv = list(bm.verts)
    rotate_verts(allv, math.radians(-20), "X", (0, 0, 0))
    for v in allv:
        v.co += head - Vector((0, 0, .06))
    hd = new_obj("BV2_Lamp_Head", bm, ["M_BV2_WornRed", "M_BV2_SteelBare"], origin=tuple(head), parent=root)
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.016, 0.004), (0.02, 0.018), (0.012, 0.034), (0.008, 0.04), (0.0, 0.04)], (0, 0, 0), "Z", 16, 0)
    vs = list(bm.verts)
    rotate_verts(vs, math.radians(-20), "X", (0, 0, 0))
    for v in vs:
        v.co += head - Vector((0, 0, .055))
    new_obj("BV2_Lamp_Bulb", bm, ["M_BV2_Bulb"], origin=tuple(head - Vector((0, 0, .04))), parent=hd, role="light_fixture")
    # cable down the back
    bm = bm_new()
    add_sweep(bm, bezier(tuple(base + Vector((0, .02, .02))), (base.x + .05, base.y + .03, T - .1), (base.x + .1, 0.94, T - .3), (base.x + .1, 0.94, T - .5), 14), .003, 6, 0)
    new_obj("BV2_Lamp_Cable", bm, ["M_BV2_Rubber"], origin=tuple(base), parent=root)


def build_crt():
    """Small amber/green diagnostic CRT on the back left; Unity draws the live readout on BV2_Crt_Screen."""
    x0, x1, y0, y1, z0 = -0.89, -0.56, 0.60, 0.92, T
    pivot = Vector(((x0 + x1) / 2, (y0 + y1) / 2, z0))

    def turned(name, bm, mats, origin, **kw):
        rotate_verts(list(bm.verts), math.radians(28), "Z", pivot)
        o = Matrix.Rotation(math.radians(28), 3, "Z") @ (Vector(origin) - pivot) + pivot
        return new_obj(name, bm, mats, origin=tuple(o), **kw)
    bm = bm_new()
    add_box(bm, (x0, y0, z0 + .02), (x1, y1 - .06, z0 + .30), 0, bevel=.012, segs=3)          # front body
    add_box(bm, (x0 + .04, y1 - .08, z0 + .05), (x1 - .04, y1, z0 + .27), 0, bevel=.02, segs=3)  # tube housing
    add_box(bm, (x0 + .01, y0 - .006, z0 + .24), (x1 - .11, y0, z0 + .285), 1, bevel=.002, segs=1)
    for i in range(7):  # vents on top
        add_box(bm, (x0 + .05 + i * .03, y0 + .10, z0 + .299), (x0 + .065 + i * .03, y0 + .22, z0 + .303), 1)
    for x in (x0 + .02, x1 - .04):
        add_box(bm, (x, y0 + .03, z0), (x + .02, y1 - .1, z0 + .02), 2, bevel=.003)
    turned("BV2_Crt", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_Rubber"], origin=((x0 + x1) / 2, (y0 + y1) / 2, z0), role="display")
    # bezel and screen (screen = separate object, faces -Y, UV 0..1 so a canvas can sit on it)
    bm = bm_new()
    add_box(bm, (x0 + .015, y0 - .012, z0 + .07), (x1 - .095, y0, z0 + .235), 0, bevel=.006, segs=2)
    turned("BV2_Crt_Bezel", bm, ["M_BV2_DarkPlastic"], origin=((x0 + x1) / 2, y0, z0 + .15))
    bm = bm_new()
    sx0, sx1, sz0, sz1 = x0 + .03, x1 - .11, z0 + .085, z0 + .22
    v = [bm.verts.new((sx0, y0 - .0125, sz0)), bm.verts.new((sx1, y0 - .0125, sz0)), bm.verts.new((sx1, y0 - .0125, sz1)), bm.verts.new((sx0, y0 - .0125, sz1))]
    f = bm.faces.new(v)
    f.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    uvl = bm.loops.layers.uv.verify()
    for l in f.loops:
        l[uvl].uv = ((l.vert.co.x - sx0) / (sx1 - sx0), (l.vert.co.z - sz0) / (sz1 - sz0))
    turned("BV2_Crt_Screen", bm, ["M_BV2_CrtGlass"], origin=((sx0 + sx1) / 2, y0 - .0125, (sz0 + sz1) / 2), uv=None, role="screen",
            note="16:10 face toward -Y; Unity adds a world-space readout canvas")
    # knobs and buttons on the right of the screen
    bm = bm_new()
    for i, z in enumerate((z0 + .20, z0 + .15)):
        add_lathe(bm, [(0.0, 0.0), (0.014, 0.0), (0.014, 0.012), (0.011, 0.018), (0.0, 0.018)], (x1 - .05, y0 - .0, z), "-Y", 14, 0)
    for i in range(3):
        add_box(bm, (x1 - .07 + i * .02, y0 - .008, z0 + .085), (x1 - .058 + i * .02, y0, z0 + .097), 1, bevel=.001, segs=1)
    add_cyl(bm, (x1 - .03, y0 - .002, z0 + .25), .004, .004, "Y", 8, 2)
    turned("BV2_Crt_Controls", bm, ["M_BV2_DarkPlastic", "M_BV2_Ivory", "M_BV2_LedGreen"], origin=(x1 - .05, y0, z0 + .15))
    # signal cable from the CRT down behind the riser
    bm = bm_new()
    add_sweep(bm, bezier((x1 - .06, y1 - .02, z0 + .12), (x1 + .05, y1 - .01, z0 + .05), (-0.40, 0.93, T + .01), (-0.30, 0.90, T + .006), 16), .0035, 6, 0)
    new_obj("BV2_Crt_Cable", bm, ["M_BV2_Rubber"], origin=(x1, y1, z0))


def build_props():
    # Soldering station on the right back: body with knob and LED display, iron in a coil stand, sponge tray.
    sx, sy = 0.70, 0.74
    bm = bm_new()
    add_box(bm, (sx - .07, sy - .06, T), (sx + .07, sy + .06, T + .085), 0, bevel=.008, segs=2)
    add_box(bm, (sx - .05, sy - .062, T + .045), (sx + .0, sy - .058, T + .07), 1)
    add_lathe(bm, [(0.0, 0.0), (0.016, 0.0), (0.016, 0.015), (0.012, 0.022), (0.0, 0.022)], (sx + .035, sy - .06, T + .042), "-Y", 14, 2)
    new_obj("BV2_Solder_Station", bm, ["M_BV2_Ivory", "M_BV2_LedRed", "M_BV2_DarkPlastic"], origin=(sx, sy, T))
    bm = bm_new()
    stx, sty = 0.62, 0.55
    add_box(bm, (stx - .05, sty - .035, T), (stx + .05, sty + .035, T + .012), 0, bevel=.003)
    add_box(bm, (stx - .045, sty - .03, T + .012), (stx - .005, sty + .03, T + .02), 1)
    # holder: a coil sleeve leaning back toward +Y at 50 deg, on a short post; the iron sits in it along the same axis
    lean = Matrix.Rotation(math.radians(-50), 3, "X")
    pivot = Vector((stx + .025, sty - .01, T + .04))
    add_cyl(bm, (stx + .025, sty - .01, T + .026), .004, .028, "Z", 8, 2)
    coil = [pivot + lean @ Vector((.014 * math.cos(a), .014 * math.sin(a), a * .0045)) for a in np.linspace(0, 8 * math.pi, 96)]
    add_sweep(bm, [tuple(p) for p in coil], .0013, 6, 2)
    new_obj("BV2_Solder_Stand", bm, ["M_BV2_SteelGreen", "M_BV2_Yellow", "M_BV2_SteelBare"], origin=(stx, sty, T))
    bm = bm_new()
    add_lathe(bm, [(0.0, -0.04), (0.0008, -0.04), (0.002, 0.0), (0.003, 0.03), (0.008, 0.05), (0.010, 0.07), (0.011, 0.16), (0.007, 0.18), (0.0, 0.18)],
              (0, 0, 0), "Z", 12, 0)
    for v in bm.verts:
        v.co = pivot + lean @ v.co
    new_obj("BV2_Solder_Iron", bm, ["M_BV2_DarkPlastic"], origin=tuple(pivot))
    iron_back = pivot + lean @ Vector((0, 0, .18))
    bm = bm_new()
    add_sweep(bm, bezier((sx - .07, sy, T + .03), (sx - .12, sy, T + .006), (iron_back.x + .02, iron_back.y + .06, T + .006), tuple(iron_back), 18), .0028, 6, 0)
    new_obj("BV2_Solder_Cable", bm, ["M_BV2_Rubber"], origin=(sx, sy, T))

    # Magnetic screw bowl and a compartment parts tray at the right of the mat
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.04, 0.0), (0.05, 0.012), (0.055, 0.03), (0.05, 0.03), (0.045, 0.014), (0.0, 0.006)], (0.58, 0.47, T), "Z", 24, 0)
    for i in range(7):
        a = i * 0.9
        add_cyl(bm, (0.58 + .02 * math.cos(a), 0.47 + .02 * math.sin(a), T + .009), .0025, .006, "Z", 8, 1)
    new_obj("BV2_ScrewBowl", bm, ["M_BV2_SteelBare", "M_BV2_Brass"], origin=(0.58, 0.47, T))
    bm = bm_new()
    px0, px1, py0, py1 = 0.55, 0.85, 0.20, 0.36
    add_box(bm, (px0, py0, T), (px1, py1, T + .006), 0, bevel=.003)
    for x in np.linspace(px0, px1, 5):
        add_box(bm, (x - .003, py0, T), (x + .003, py1, T + .03), 0, bevel=.001, segs=1)
    for y in (py0, (py0 + py1) / 2, py1):
        add_box(bm, (px0, y - .003, T), (px1, y + .003, T + .03), 0, bevel=.001, segs=1)
    for i in range(14):
        add_box(bm, (px0 + .02 + (i % 7) * .04, py0 + .02 + (i // 7) * .08, T + .006), (px0 + .035 + (i % 7) * .04, py0 + .03 + (i // 7) * .08, T + .014), 1 + i % 2)
    new_obj("BV2_PartsTray", bm, ["M_BV2_DarkPlastic", "M_BV2_Copper", "M_BV2_SteelBare"], origin=((px0 + px1) / 2, (py0 + py1) / 2, T))

    # Mug with pens, roll of kapton tape, solder spool, notepad and sticky notes
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.036, 0.0), (0.038, 0.004), (0.038, 0.095), (0.034, 0.095), (0.034, 0.008), (0.0, 0.008)], (-0.40, 0.80, T), "Z", 20, 0)
    handle = [(-0.40 + .036, 0.80, T + .08), (-0.40 + .065, 0.80, T + .075), (-0.40 + .065, 0.80, T + .03), (-0.40 + .036, 0.80, T + .025)]
    add_sweep(bm, bezier(*handle, 10), .006, 8, 0)
    for i, (dx, dy, tilt) in enumerate(((-.01, .0, .12), (.012, .008, -.1), (.0, -.014, .2))):
        p0 = (-0.40 + dx, 0.80 + dy, T + .01)
        p1 = (-0.40 + dx + math.sin(tilt) * .14, 0.80 + dy, T + .01 + math.cos(tilt) * .14)
        add_sweep(bm, [p0, p1], .0045, 6, 1 + i % 2)
    new_obj("BV2_Mug", bm, ["M_BV2_Ivory", "M_BV2_Orange", "M_BV2_DarkPlastic"], origin=(-0.40, 0.80, T))
    bm = bm_new()
    add_lathe(bm, [(0.022, 0.0), (0.034, 0.0), (0.034, 0.02), (0.022, 0.02)], (0.40, 0.80, T), "Z", 20, 0)
    add_lathe(bm, [(0.008, 0.0), (0.03, 0.0), (0.03, 0.003), (0.012, 0.003), (0.012, 0.037), (0.03, 0.037), (0.03, 0.04), (0.008, 0.04)], (0.48, 0.78, T), "Z", 18, 1)
    add_lathe(bm, [(0.012, 0.003), (0.027, 0.003), (0.027, 0.037), (0.012, 0.037)], (0.48, 0.78, T), "Z", 18, 2)
    new_obj("BV2_TapeAndSpool", bm, ["M_BV2_Orange", "M_BV2_DarkPlastic", "M_BV2_Brass"], origin=(0.44, 0.79, T))
    # Work order on a hardboard clipboard (front left), the inspection-seal kit (stamp, ink pad, seal tape roll) and a
    # ticket spike of paid / unpaid slips behind it.
    bm = bm_new()
    add_box(bm, (-0.89, 0.17, T), (-0.68, 0.45, T + .006), 0, bevel=.003)
    add_box(bm, (-0.82, 0.43, T + .006), (-0.75, 0.455, T + .018), 1, bevel=.003)
    new_obj("BV2_Clipboard", bm, ["M_BV2_Hardboard", "M_BV2_SteelBare"], origin=(-0.785, 0.31, T))
    top_quad("BV2_WorkOrder", "M_BV2_WorkOrder", -0.875, -0.695, 0.185, 0.425, T + .0065)
    bm = bm_new()
    add_box(bm, (-0.37, 0.62, T), (-0.29, 0.68, T + .012), 0, bevel=.003)                               # ink pad tin
    add_box(bm, (-0.362, 0.628, T + .012), (-0.298, 0.672, T + .013), 1)
    add_cyl(bm, (-0.25, 0.65, T + .012), .016, .024, "Z", 14, 2, bevel=.002)                            # stamp base
    add_lathe(bm, [(0.0, 0.024), (0.009, 0.024), (0.007, 0.06), (0.012, 0.075), (0.0, 0.08)], (-0.25, 0.65, T), "Z", 12, 3)
    add_lathe(bm, [(0.012, 0.0), (0.03, 0.0), (0.03, 0.032), (0.012, 0.032)], (-0.32, 0.75, T), "Z", 20, 4)   # seal tape roll
    add_cyl(bm, (-0.22, 0.78, T + .004), .028, .008, "Z", 14, 2)                                         # ticket spike
    add_cyl(bm, (-0.22, 0.78, T + .07), .0015, .12, "Z", 6, 5)
    for k in range(6):
        vs = add_box(bm, (-0.255, 0.755, T + .01 + k * .006), (-0.185, 0.805, T + .011 + k * .006), 6)
        rotate_verts(vs, k * .45, "Z", (-0.22, 0.78, T))
    new_obj("BV2_SealKit", bm, ["M_BV2_SteelGreen", "M_BV2_WornRed", "M_BV2_DarkPlastic", "M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_SteelBare",
                               "M_BV2_Ivory"], origin=(-0.29, 0.70, T))
    bm = bm_new()   # outer wrap of the seal roll, printed
    ring = []
    for k in range(25):
        a = 2 * math.pi * k / 24
        ring.append((-0.32 + .0302 * math.cos(a), 0.75 + .0302 * math.sin(a)))
    uvl = bm.loops.layers.uv.verify()
    for k in range(24):
        (ax, ay), (bx, by) = ring[k], ring[k + 1]
        vs = [bm.verts.new(p) for p in ((ax, ay, T + .002), (bx, by, T + .002), (bx, by, T + .030), (ax, ay, T + .030))]
        f = bm.faces.new(vs)
        for l, uv in zip(f.loops, ((k / 24 * 2, 0), ((k + 1) / 24 * 2, 0), ((k + 1) / 24 * 2, 1), (k / 24 * 2, 1))):
            l[uvl].uv = uv
    new_obj("BV2_SealTapeWrap", bm, ["M_BV2_SealTape"], origin=(-0.32, 0.75, T + .016), uv=None)


def build_personal_items():
    """Night-shift life on the bench. Placed on the riser and the bench's back / side margins, off the service mat and
    clear of the tabletop parking area. BV2_Holo_Koi and BV2_LuckyCat_Arm are animated by RepairBenchV2Runtime."""
    # holo projector trinket on the CRT: puck with a cyan ring, a koi hologram circling above it, a faint beam
    px, py, pz = -0.70, 0.78, T + 0.30
    bm = bm_new()
    add_cyl(bm, (px, py, pz + .008), .032, .016, "Z", 20, 0, bevel=.002)
    add_lathe(bm, [(0.022, 0.0), (0.026, 0.0), (0.026, 0.002), (0.022, 0.002)], (px, py, pz + .016), "Z", 20, 1)
    new_obj("BV2_HoloPuck", bm, ["M_BV2_DarkPlastic", "M_BV2_LedGreen"], origin=(px, py, pz))
    bm = bm_new()   # koi: lathe body flattened sideways, tail and fins; pivot at the puck axis, swims around it
    body = add_lathe(bm, [(0.0, 0.0), (0.006, 0.004), (0.010, 0.016), (0.009, 0.03), (0.005, 0.042), (0.0, 0.046)], (0, 0, 0), "Z", 10, 0)
    for v in body:
        v.co.y *= .55
    tail = [bm.verts.new(c) for c in ((0, 0, .044), (0, .014, .062), (0, 0, .054), (0, -.014, .062))]
    bm.faces.new(tail)
    for side in (-1, 1):
        fin = [bm.verts.new(c) for c in ((0, side * .005, .016), (0, side * .018, .010), (0, side * .006, .026))]
        bm.faces.new(fin)
    allv = list(bm.verts)
    rotate_verts(allv, math.radians(90), "Y", (0, 0, 0))        # swim along +X
    for v in allv:
        v.co += Vector((px - .02, py + .045, pz + .08))
    new_obj("BV2_Holo_Koi", bm, ["M_BV2_Holo"], origin=(px, py, pz + .08), role="holo")

    # lucky cat on top of the parts organizer, waving its raised paw (separate arm, pivot at the shoulder)
    cx, cy, cz = 0.40, 0.905, T + 0.30
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.030, 0.0), (0.032, 0.02), (0.028, 0.05), (0.02, 0.06), (0.0, 0.062)], (cx, cy, cz), "Z", 16, 0)
    add_lathe(bm, [(0.0, 0.0), (0.024, 0.004), (0.027, 0.02), (0.022, 0.038), (0.0, 0.042)], (cx, cy, cz + .056), "Z", 16, 0)
    for sx in (-1, 1):
        ear = [bm.verts.new(c) for c in ((cx + sx * .010, cy, cz + .092), (cx + sx * .022, cy, cz + .092), (cx + sx * .018, cy, cz + .108))]
        f = bm.faces.new(ear)
    add_lathe(bm, [(0.026, 0.0), (0.03, 0.0), (0.03, 0.006), (0.026, 0.006)], (cx, cy, cz + .052), "Z", 16, 1)   # red collar
    add_cyl(bm, (cx + .006, cy - .028, cz + .028), .012, .004, "Y", 14, 2)                                      # gold coin
    for ex in (-.008, .008):
        add_box(bm, (cx + ex - .003, cy - .0235, cz + .078), (cx + ex + .003, cy - .022, cz + .081), 3)          # eyes
    new_obj("BV2_LuckyCat", bm, ["M_BV2_Ceramic", "M_BV2_WornRed", "M_BV2_Brass", "M_BV2_DarkPlastic"], origin=(cx, cy, cz))
    bm = bm_new()
    add_cyl(bm, (cx - .028, cy - .006, cz + .085), .007, .036, "Z", 10, 0)
    add_cyl(bm, (cx - .028, cy - .006, cz + .104), .009, .008, "Z", 10, 0)
    new_obj("BV2_LuckyCat_Arm", bm, ["M_BV2_Ceramic"], origin=(cx - .028, cy - .006, cz + .068), role="animated",
            note="waves about the axis across the shoulder")

    # mini UNIT 07 figurine on top of the test fixture (the shop's own robot, as a souvenir)
    fx, fy, fz = -0.36, 0.905, T + 0.334
    bm = bm_new()
    add_box(bm, (fx - .02, fy - .016, fz), (fx + .02, fy + .016, fz + .006), 0, bevel=.002)                  # base
    add_box(bm, (fx - .005, fy - .005, fz + .006), (fx + .005, fy + .005, fz + .03), 1)                      # neck post
    add_box(bm, (fx - .022, fy - .016, fz + .03), (fx + .022, fy + .016, fz + .062), 2, bevel=.003)          # head
    for sx in (-1, 1):
        add_box(bm, (fx + sx * .03 - .008, fy - .01, fz + .036), (fx + sx * .03 + .008, fy + .01, fz + .056), 2, bevel=.003)   # shoulder pods
        add_box(bm, (fx + sx * .03 - .003, fy - .004, fz + .016), (fx + sx * .03 + .003, fy + .004, fz + .036), 1)          # arms
    new_obj("BV2_MiniUnit07", bm, ["M_BV2_DarkPlastic", "M_BV2_SteelBare", "M_BV2_Ivory"], origin=(fx, fy, fz))
    front_quad("BV2_MiniUnit07_Face", "M_BV2_MiniFace", fx - .016, fx + .016, fz + .035, fz + .057, fy - .0165)

    # polaroid taped to the CRT's right side (faces +X, toward the mat)
    x = -0.559
    print_quad("BV2_Polaroid", "M_BV2_Photo", [(x, 0.66, T + .12), (x, 0.73, T + .12), (x, 0.73, T + .20), (x, 0.66, T + .20)])
    bm = bm_new()
    add_box(bm, (x - .001, 0.685, T + .195), (x + .0005, 0.705, T + .21), 0)
    new_obj("BV2_PolaroidTape", bm, ["M_BV2_Ivory"], origin=(x, 0.695, T + .2))

    # night-shift noodle cup with chopsticks, and a crushed can beside it (front-left margin, off the mat)
    nx, ny = -0.59, 0.56
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.035, 0.0), (0.045, 0.095), (0.0, 0.095)], (nx, ny, T), "Z", 18, 0)
    add_lathe(bm, [(0.0405, 0.04), (0.0435, 0.04), (0.0455, 0.065), (0.0425, 0.065)], (nx, ny, T), "Z", 18, 1)   # red band
    lid = add_box(bm, (nx - .04, ny + .01, T + .095), (nx + .04, ny + .07, T + .097), 2)
    rotate_verts(lid, math.radians(55), "X", (nx, ny + .01, T + .095))                                         # peeled foil lid
    for dx in (-.006, .006):
        add_box(bm, (nx + dx - .002, ny - .06, T + .10), (nx + dx + .002, ny + .02, T + .104), 3)              # chopsticks across
    new_obj("BV2_NoodleCup", bm, ["M_BV2_Ivory", "M_BV2_WornRed", "M_BV2_SteelBare", "M_BV2_Hardboard"], origin=(nx, ny, T))
    bm = bm_new()
    add_lathe(bm, [(0.0, 0.0), (0.03, 0.0), (0.033, 0.03), (0.025, 0.05), (0.033, 0.07), (0.0, 0.07)], (0, 0, 0), "Z", 14, 0)
    add_lathe(bm, [(0.0315, 0.02), (0.034, 0.02), (0.034, 0.045), (0.0315, 0.045)], (0, 0, 0), "Z", 14, 1)
    vs = list(bm.verts)
    rotate_verts(vs, math.radians(90), "Y", (0, 0, 0))
    for v in vs:
        v.co += Vector((-0.80, 0.53, T + .03))
    new_obj("BV2_CrushedCan", bm, ["M_BV2_SteelBare", "M_BV2_Orange"], origin=(-0.80, 0.53, T))
    # a full can on the riser's lower shelf, and a succulent planted in a tin can
    bm = bm_new()
    add_cyl(bm, (0.36, 0.91, T + .062), .032, .124, "Z", 16, 0, bevel=.003)
    add_cyl(bm, (0.36, 0.91, T + .07), .0325, .04, "Z", 16, 1)
    new_obj("BV2_DrinkCan", bm, ["M_BV2_SteelBare", "M_BV2_Orange"], origin=(0.36, 0.91, T))
    bm = bm_new()
    add_cyl(bm, (0.42, 0.90, T + .04), .03, .08, "Z", 14, 0)
    for k in range(14):
        a = k * 2.4
        r = .006 + .0016 * k
        leaf = add_box(bm, (-.005, -.002, 0), (.005, .002, .028 - k * .0008), 1)
        rotate_verts(leaf, math.radians(30 + k * 3), "Y", (0, 0, 0))
        rotate_verts(leaf, a, "Z", (0, 0, 0))
        for v in leaf:
            v.co += Vector((0.42 + r * math.cos(a), 0.90 + r * math.sin(a), T + .078))
    new_obj("BV2_Succulent", bm, ["M_BV2_SteelBare", "M_BV2_Plant"], origin=(0.42, 0.90, T))

    # cassette walkman with headphones and two hand-labelled tapes (right margin, off the mat)
    wx, wy = 0.83, 0.47
    bm = bm_new()
    add_box(bm, (wx - .045, wy - .062, T), (wx + .045, wy + .062, T + .03), 0, bevel=.004)
    add_box(bm, (wx - .036, wy - .040, T + .03), (wx + .036, wy + .030, T + .032), 1)
    for i in range(4):
        add_box(bm, (wx + .046, wy - .03 + i * .016, T + .012), (wx + .050, wy - .02 + i * .016, T + .02), 1)
    add_sweep(bm, bezier((wx - .03, wy + .062, T + .02), (wx - .06, wy + .12, T + .02), (wx + .02, wy + .14, T + .006), (wx + .05, wy + .10, T + .006), 14), .0018, 5, 2)
    for ex in (.03, .07):
        add_cyl(bm, (wx + ex, wy + .10, T + .006), .014, .012, "Z", 12, 3)
    new_obj("BV2_Walkman", bm, ["M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_DarkPlastic", "M_BV2_Orange"], origin=(wx, wy, T))
    for k, (tx, ty) in enumerate(((0.84, 0.375), (0.86, 0.39))):
        bm = bm_new()
        add_box(bm, (tx - .05, ty - .032, T + k * .012), (tx + .05, ty + .032, T + .011 + k * .012), 0, bevel=.001, segs=1)
        new_obj("BV2_Cassette_%d" % k, bm, ["M_BV2_DarkPlastic"], origin=(tx, ty, T))
        top_quad("BV2_Cassette_%d_Label" % k, "M_BV2_TapeLabel", tx - .042, tx + .042, ty - .012, ty + .022, T + .0112 + k * .012)


def build_pegboard_tools():
    """Tools hanging on the existing pegboard (front face y 1.025): pliers, three wrenches, side cutters, hooks."""
    y = 1.02
    bm = bm_new()
    for i, (x, L) in enumerate(((-0.30, .16), (-0.26, .14), (-0.225, .12))):  # wrenches
        z = 1.48
        add_box(bm, (x - .008, y - .004, z - L), (x + .008, y + .0, z - .02), 0, bevel=.002, segs=1)
        add_cyl(bm, (x, y - .002, z - .01), .016, .004, "Y", 14, 0)
        add_cyl(bm, (x, y - .002, z - L - .006), .012, .004, "Y", 12, 0)
    for x in (-0.62, -0.50):   # pliers / cutters: two crossed handles with red dips
        vs = []
        for s in (-1, 1):
            p = [(x + s * .025, y - .006, 1.28), (x + s * .012, y - .006, 1.38), (x + s * .004, y - .006, 1.42)]
            add_sweep(bm, p, .0055, 6, 1)
            add_sweep(bm, [(x + s * .004, y - .006, 1.42), (x - s * .003, y - .006, 1.47)], .004, 6, 0)
        add_cyl(bm, (x, y - .006, 1.42), .007, .006, "Y", 10, 0)
    for x in (-0.62, -0.50, -0.30, -0.26, -0.225, 0.02, 0.10):  # hooks
        add_sweep(bm, [(x, 1.03, 1.49), (x, 1.005, 1.49), (x, 1.005, 1.505)], .0018, 5, 0)
    # a coil of spare cable and a hanging hand drill outline on the right
    coil = [(0.06 + .05 * math.cos(a), 1.005 + .004 * math.sin(a * 7), 1.40 + .05 * math.sin(a)) for a in np.linspace(0, 6 * math.pi, 90)]
    add_sweep(bm, coil, .0025, 5, 2)
    new_obj("BV2_PegTools", bm, ["M_BV2_SteelBare", "M_BV2_WornRed", "M_BV2_Rubber"], origin=(-0.3, 1.02, 1.4), role="clutter")


def build_wall():
    """The wall behind the bench: steel pegboard on standoffs (front face y 1.025, the old pegboard's), a galvanised
    shelf on painted gussets where the old upper shelf was, its stock (hopper bins, manuals, cartons, screw jars),
    what is pinned to the board (UNIT 07 schematic, shipping calendar, framed repair permit, the work-order rail) and
    what hangs on it (solder spools, test leads)."""
    uv = WALL_UV
    bm = bm_new()
    add_box(bm, (-0.95, 1.025, 0.93), (0.95, 1.033, 1.76), 0)
    for (x0, x1, z0, z1) in ((-0.95, 0.95, 0.93, 0.94), (-0.95, 0.95, 1.75, 1.76), (-0.95, -0.94, 0.93, 1.76), (0.94, 0.95, 0.93, 1.76)):
        add_box(bm, (x0, 1.033, z0), (x1, 1.048, z1), 0)
    K.planar_uv(bm, (1, 0, 0), (0, 0, 1), -0.95, 0.95, 0.93, 1.76)
    new_obj("BV2_Pegboard", bm, ["M_BV2_Pegboard"], origin=(0, 1.03, 1.345), uv=None)

    T2 = 1.785
    bm = bm_new()
    add_box(bm, (-0.95, 0.80, 1.76), (0.95, 1.048, T2), 1)
    add_box(bm, (-0.95, 0.795, 1.735), (0.95, 0.80, T2), 1)
    for x in (-0.88, 0.88):
        add_box(bm, (x - .012, 1.034, 1.58), (x + .012, 1.048, 1.76), 0)
        add_sweep(bm, [(x, 1.04, 1.60), (x, 0.84, 1.752)], .009, 4, 0)
    new_obj("BV2_WallShelf", bm, ["M_BV2_SteelGreen", "M_BV2_SteelBare"], origin=(0, 0.92, 1.77))

    bm = bm_new()
    IV, DK, BI, OR, CB, TP, CE, RD = range(8)
    for i in range(6):                                                    # hopper bins
        x0 = -0.92 + i * 0.118
        m = IV if i % 2 == 0 else DK
        add_box(bm, (x0, 0.83, T2), (x0 + .11, 1.03, T2 + .10), m)
        add_box(bm, (x0 + .006, 0.812, T2), (x0 + .104, 0.83, T2 + .05), m)
    for i, m in enumerate((BI, IV, OR, DK, IV)):                          # service manuals, the last one leaning
        x = -0.14 + i * .05
        vs = add_box(bm, (x, 0.82, T2), (x + .045, 1.04, T2 + .28), m)
        if i == 4:
            rotate_verts(vs, math.radians(-16), "Y", (x + .045, 0.93, T2))
    add_box(bm, (0.22, 0.82, T2), (0.48, 1.04, T2 + .16), CB, bevel=.004, segs=1)       # cartons
    add_box(bm, (0.25, 0.85, T2 + .16), (0.45, 1.02, T2 + .28), CB, bevel=.004, segs=1)
    add_box(bm, (0.52, 0.83, T2), (0.70, 1.03, T2 + .20), CB, bevel=.004, segs=1)
    for (x0, x1, z) in ((0.22, 0.48, T2 + .16), (0.25, 0.45, T2 + .28), (0.52, 0.70, T2 + .20)):
        add_box(bm, ((x0 + x1) / 2 - .025, 0.819, z - .002), ((x0 + x1) / 2 + .025, 1.041, z + .001), TP)
    for x, lid in ((0.76, OR), (0.83, RD), (0.90, DK)):                    # screw jars
        add_cyl(bm, (x, 0.92, T2 + .045), .03, .09, "Z", 12, CE)
        add_cyl(bm, (x, 0.92, T2 + .095), .032, .014, "Z", 12, lid)
    new_obj("BV2_ShelfGoods", bm, ["M_BV2_Ivory", "M_BV2_DarkPlastic", "M_BV2_Binder", "M_BV2_Orange", "M_BV2_Cardboard",
                                   "M_BV2_Tape", "M_BV2_Ceramic", "M_BV2_WornRed"], origin=(0, 0.93, 1.9))

    bm = bm_new()
    ST, DK, CU, RD, RB = range(5)
    add_box(bm, (0.16, 1.012, 1.715), (0.54, 1.024, 1.73), ST)           # work-order rail with four clips
    for x in (.21, .30, .39, .48):
        add_box(bm, (x - .015, 1.004, 1.70), (x + .015, 1.016, 1.732), DK)
    for x in (.21, .41):                                                   # spool holder
        add_sweep(bm, [(x, 1.03, 1.452), (x, .975, 1.452), (x, .972, 1.462)], .0022, 5, ST)
    add_cyl(bm, (.31, .976, 1.448), .004, .25, "X", 8, ST)
    for x, core, fl in ((.26, ST, DK), (.36, CU, RD)):
        add_cyl(bm, (x, .976, 1.448), .022, .045, "X", 12, core)
        for dx in (-.024, .024):
            add_cyl(bm, (x + dx, .976, 1.448), .036, .005, "X", 14, fl)
    add_sweep(bm, [(.52, 1.03, 1.47), (.52, .99, 1.47), (.52, .986, 1.48)], .0022, 5, ST)   # test leads
    for k, (m, dx) in enumerate(((RD, -.006), (RB, .006))):
        pts = [(.52 + dx + .045 * math.sin(a) * (1 - .1 * k), .99 - .004 * k, 1.47 - .075 * (1 - math.cos(a))) for a in np.linspace(0, 2 * math.pi, 22)]
        add_sweep(bm, pts, .0035, 5, m)
        add_sweep(bm, [(.52 + dx, .99 - .004 * k, 1.47), (.53 + dx * 3, .985, 1.36), (.535 + dx * 4, .984, 1.31)], .0035, 5, m)
        add_cyl(bm, (.535 + dx * 4, .984, 1.295), .006, .03, "Z", 8, m)
    for (x0, x1, z0, z1) in ((-0.587, -0.373, 1.553, 1.563), (-0.587, -0.373, 1.697, 1.707), (-0.587, -0.577, 1.553, 1.707), (-0.383, -0.373, 1.553, 1.707)):
        add_box(bm, (x0, 1.012, z0), (x1, 1.025, z1), DK)                 # permit frame
    for (x, z) in ((-0.73, 1.625), (0.72, 1.665), (-0.48, 1.71)):           # pins and nails
        add_cyl(bm, (x, 1.018, z), .004, .012, "Y", 8, RD)
    new_obj("BV2_WallTools", bm, ["M_BV2_SteelBare", "M_BV2_DarkPlastic", "M_BV2_Copper", "M_BV2_WornRed", "M_BV2_Rubber"],
            origin=(0.1, 1.0, 1.5), role="clutter")

    bm = bm_new()
    bm.loops.layers.uv.verify()
    R, Z = (1, 0, 0), (0, 0, 1)
    K.print_quad(bm, (-0.73, 1.0225, 1.47), R, Z, .22, .30, uv["schematic"])
    K.print_quad(bm, (0.72, 1.0225, 1.48), R, Z, .24, .36, uv["calendar"])
    K.print_quad(bm, (-0.48, 1.0215, 1.63), R, Z, .195, .135, uv["permit"])
    for i in range(6):
        x0 = -0.92 + i * 0.118
        K.print_quad(bm, (x0 + .055, 0.8285, T2 + .075), R, Z, .07, .026, uv["bin%d" % i])
    for i in range(5):
        x = -0.14 + i * .05
        f = K.print_quad(bm, (x + .0225, 0.8185, T2 + .15), R, Z, .03, .17, uv["spine%d" % i])
        if i == 4:
            rotate_verts(list(f.verts), math.radians(-16), "Y", (x + .045, 0.93, T2))
    K.print_quad(bm, (0.61, 0.8285, T2 + .11), R, Z, .15, .062, uv["fragile"])
    K.print_quad(bm, (0.35, 0.8185, T2 + .07), R, Z, .14, .058, uv["nspbox"])
    new_obj("BV2_WallPrints", bm, ["M_BV2_WallPrints"], origin=(0, 0.95, 1.6), uv=None, role="print")

    bm = bm_new()                                                          # the backlog: four slips on the rail
    bm.loops.layers.uv.verify()
    for k, x in enumerate((.21, .30, .39, .48)):
        f = K.print_quad(bm, (x + .004 * k, 1.0195 - .0008 * k, 1.645), R, Z, .088, .13, (0, 0, 1, 1), 0 if k == 1 else 1)
        rotate_verts(list(f.verts), math.radians((-3, 2, -1, 4)[k]), "Y", (x, 1.02, 1.71))
    new_obj("BV2_WallOrders", bm, ["M_BV2_WorkOrder", "M_BV2_Paper"], origin=(0.35, 1.02, 1.645), uv=None, role="print")


def build():
    global ROOT
    textures = build_textures()
    build_materials(textures)
    ROOT = empty("RepairBenchV2", (0, 0, 0))
    empty("Ref_BenchTopOrigin", (0, 0.55, T), ROOT)
    build_bench()
    build_tools()
    build_lamp()
    build_crt()
    build_props()
    build_pegboard_tools()
    build_personal_items()
    build_wall()


def export():
    bpy.ops.object.select_all(action="DESELECT")
    for o in OBJECTS:
        o.select_set(True)
    path = os.path.join(EXPORT, "RepairBenchV2.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)
    return path


def report(fbx):
    rows = []
    total = 0
    for o in OBJECTS:
        if o.type != "MESH":
            continue
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        total += tris
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = [min(v[i] for v in bb) for i in range(3)]
        hi = [max(v[i] for v in bb) for i in range(3)]
        rows.append({"name": o.name, "tris": tris, "materials": [m.name for m in o.data.materials], "role": o.get("bv2_role", ""),
                     "min": [round(x, 4) for x in lo], "max": [round(x, 4) for x in hi]})
    data = {"fbx": fbx, "objects": rows, "triangles": total, "interactive": INTERACT, "bench_top_z": T}
    with open(os.path.join(HERE, "report.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("BV2 triangles", total, "objects", len(rows))


def render():
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
    shots = {"bench_wall": ((0.05, -0.55, 1.62), (0.0, 1.0, 1.45), 30),
             "bench_front": ((0.0, -0.75, 1.55), (0.0, 0.55, 0.95), 35), "bench_tools": ((-0.55, -0.05, 1.20), (-0.68, 0.28, 0.92), 40),
             "bench_left": ((-0.25, 0.05, 1.35), (-0.70, 0.75, 1.0), 40), "bench_right": ((0.45, 0.05, 1.40), (0.55, 0.65, 1.0), 40),
             "bench_wide": ((1.6, -1.4, 1.9), (0.0, 0.55, 0.7), 35)}
    for name, (loc, target, lens) in shots.items():
        cam.location = loc
        d = Vector(target) - Vector(loc)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cam_data.lens = lens
        SCENE.render.filepath = os.path.join(RENDERS, name + ".png")
        bpy.ops.render.render(write_still=True)


build()
fbx = export()
report(fbx)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "RepairBenchV2.blend"))
if not NO_RENDER:
    render()
print("BV2_DONE")
