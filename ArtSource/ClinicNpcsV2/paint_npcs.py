"""Paints the clinic NPC textures, V2 (system Python + Pillow, numpy).

    python paint_npcs.py            -> Textures/T_NPC_<Name>.png (body atlas) and T_NPC_<Name>_Face.png (2x2 face frames)

Everything is painted at final resolution with hard-ish edges (point-filtered in Unity, like the PS2-era sheets in the
reference library). The face sheet holds four frames, each 256 px: [eyes open, mouth closed] [eyes closed, mouth
closed] / [eyes open, mouth open] [eyes closed, mouth open]; the mesh maps the face into the top-left frame and the
runtime shifts the UV offset by half a sheet to blink or talk.
"""
import os
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import npc_spec as S

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "Textures")


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def mul(c, k):
    return tuple(max(0, min(255, int(round(v * k)))) for v in c)


# ---------------------------------------------------------------------------------------------------------- body atlas
class Atlas:
    def __init__(self):
        self.img = Image.new("RGB", (S.ATLAS, S.ATLAS), (128, 128, 128))
        self.d = ImageDraw.Draw(self.img)

    def tile(self, name):
        return S.TILES[name]

    def grad(self, name, top, bottom, noise=0.0, seed=0):
        x, y, w, h = self.tile(name)
        rnd = random.Random(seed)
        for j in range(h):
            c = mix(top, bottom, j / max(1, h - 1))
            for i in range(w):
                k = 1 + (rnd.random() - .5) * noise
                self.img.putpixel((x + i, y + j), mul(c, k))

    def rect(self, name, u0, v0, u1, v1, color):
        x, y, w, h = self.tile(name)
        self.d.rectangle([x + u0 * w, y + v0 * h, x + u1 * w - 1, y + v1 * h - 1], fill=color)

    def hline(self, name, v, color, width=1, u0=0.0, u1=1.0):
        x, y, w, h = self.tile(name)
        yy = y + v * h
        self.d.line([x + u0 * w, yy, x + u1 * w - 1, yy], fill=color, width=width)

    def vline(self, name, u, color, width=1, v0=0.0, v1=1.0):
        x, y, w, h = self.tile(name)
        xx = x + u * w
        self.d.line([xx, y + v0 * h, xx, y + v1 * h - 1], fill=color, width=width)

    def dot(self, name, u, v, r, color):
        x, y, w, h = self.tile(name)
        cx, cy = x + u * w, y + v * h
        self.d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)

    def swatch(self, index, color):
        x, y, w, h = self.tile("swatch")
        sx, sy = x + (index % 8) * S.SWATCH, y + (index // 8) * S.SWATCH
        self.d.rectangle([sx, sy, sx + S.SWATCH - 1, sy + S.SWATCH - 1], fill=color)


def paint_hair(a, c, name="hair", dark=False):
    base = mul(c["hair"], .62 if dark else 1.0)
    hi = c["hair_hi"]
    x, y, w, h = a.tile(name)
    rnd = random.Random(len(name) + sum(c["hair"]))
    for j in range(h):
        t = j / (h - 1)
        # Root a little darker, tips a little lighter; a soft shadow band two thirds down.
        col = mix(mul(base, .78), mul(base, 1.08), t)
        if .55 < t < .75:
            col = mul(col, .88)
        for i in range(w):
            a.img.putpixel((x + i, y + j), col)
    if not dark:
        # Angel ring: a zigzag highlight band near the crown.
        pts = []
        for i in range(0, w + 1, 6):
            pts.append((x + i, y + h * (.22 + (.04 if (i // 6) % 2 else 0))))
        for k in range(4):
            a.d.line([(px, py + k) for px, py in pts], fill=mix(mix(hi, base, .35), base, k / 4), width=1)
    # Strand lines.
    for s in range(w // 4):
        u = x + s * 4 + rnd.randint(0, 2)
        v0 = y + rnd.randint(0, h // 3)
        v1 = y + rnd.randint(h // 2, h)
        a.d.line([u, v0, u + rnd.randint(-2, 2), v1], fill=mul(base, .72), width=1)


def paint_top(a, c):
    top = c["top"]
    col = top["color"]
    a.grad("top", mul(col, 1.08), mul(col, .82), noise=.04, seed=1)
    d = top["detail"]
    if d == "turtleneck":
        for k in range(8):
            a.hline("top", .02 + k * .018, mul(col, .78))
        for k in range(16):
            a.vline("top", k / 16, mul(col, .9), v0=.18, v1=1)
    elif d == "scrubs":
        x, y, w, h = a.tile("top")
        cx = x + w * .5
        a.d.polygon([(cx - w * .09, y), (cx + w * .09, y), (cx, y + h * .26)], fill=mul(c["skin"], .96))
        a.d.line([(cx - w * .09, y), (cx, y + h * .26), (cx + w * .09, y)], fill=mul(col, .62), width=2)
        a.rect("top", .58, .30, .68, .44, mul(col, .9))
        a.hline("top", .30, mul(col, .65), u0=.58, u1=.68)
        a.vline("top", .615, (40, 40, 44), v0=.24, v1=.34)  # pen
    elif d == "tank":
        x, y, w, h = a.tile("top")
        a.d.ellipse([x + w * .38, y - h * .14, x + w * .62, y + h * .16], fill=mul(c["skin"], .9))
        a.d.ellipse([x - w * .12, y - h * .14, x + w * .12, y + h * .16], fill=mul(c["skin"], .9))
        a.d.ellipse([x + w * .88, y - h * .14, x + w * 1.12, y + h * .16], fill=mul(c["skin"], .9))
        for k in range(6):
            a.dot("top", .3 + k * .07, .62 + (k % 2) * .05, 2, mul(col, .7))  # grease spots
    elif d == "tee":
        x, y, w, h = a.tile("top")
        a.d.ellipse([x + w * .40, y - h * .10, x + w * .60, y + h * .10], fill=mul(c["skin"], .92))
        a.d.arc([x + w * .40, y - h * .10, x + w * .60, y + h * .10], 0, 180, fill=mul(col, .65), width=2)
        a.rect("top", .42, .30, .58, .44, mul(col, .78))          # faded crew print
        a.hline("top", .37, mul(col, 1.15), u0=.44, u1=.56)
    elif d == "hoodie":
        a.rect("top", .40, .55, .60, .78, mul(col, .88))
        a.hline("top", .55, mul(col, .62), u0=.40, u1=.60)
        a.vline("top", .47, (220, 220, 220), v0=.05, v1=.30)
        a.vline("top", .53, (220, 220, 220), v0=.05, v1=.26)
        a.hline("top", .04, mul(col, .65), width=3)
    a.hline("top", .985, mul(col, .6), width=2)


def paint_jacket(a, c):
    j = c["jacket"]
    col = j["color"]
    a.grad("jacket", mul(col, 1.06), mul(col, .80), noise=.05, seed=2)
    a.grad("sleeve", mul(col, 1.04), mul(col, .78), noise=.05, seed=3)
    p = j["pattern"]
    x, y, w, h = a.tile("jacket")
    if p == "haori":
        # Hemp-leaf (asanoha) hint: a sparse lattice of small light stars.
        for gy in range(6, h, 16):
            for gx in range((gy // 16) % 2 * 8, w, 16):
                cx, cy = x + gx, y + gy
                for ang in range(0, 360, 60):
                    r = math.radians(ang)
                    a.d.line([cx, cy, cx + 5 * math.cos(r), cy + 5 * math.sin(r)], fill=mul(col, 1.35), width=1)
        a.rect("jacket", .44, 0, .56, 1, mul(col, .62))   # dark front band (eri)
        a.rect("sleeve", 0, .86, 1, 1, mul(col, .62))
    elif p == "cardigan":
        for k in range(0, h, 3):
            a.hline("jacket", k / h, mul(col, .93))
        for k in range(5):
            a.dot("jacket", .455, .12 + k * .17, 2, (180, 160, 120))
        a.rect("sleeve", 0, .88, 1, 1, mul(col, .85))
        for k in range(0, 128, 3):
            a.hline("sleeve", k / 128, mul(col, .93))
    elif p == "workcoat":
        a.rect("jacket", .26, .55, .40, .72, mul(col, .86)); a.hline("jacket", .55, mul(col, .6), u0=.26, u1=.40)
        a.rect("jacket", .60, .55, .74, .72, mul(col, .86)); a.hline("jacket", .55, mul(col, .6), u0=.60, u1=.74)
        a.rect("jacket", .44, .04, .47, .9, mul(col, .7))
        rnd = random.Random(7)
        for k in range(18):
            a.dot("jacket", rnd.random(), .3 + rnd.random() * .65, rnd.randint(1, 3), mul(col, .66))
        a.rect("sleeve", 0, .88, 1, 1, mul(col, .7))
        # Patch with a hand-stitched port number.
        a.rect("jacket", .12, .22, .22, .32, (150, 130, 96))
    elif p == "dockjacket":
        # Port-authority work jacket: reflective shoulder band, chest pockets, crane-crew patch "3".
        a.rect("jacket", 0, .08, 1, .14, (206, 210, 196))
        a.hline("jacket", .08, mul(col, .6)); a.hline("jacket", .14, mul(col, .6))
        a.rect("sleeve", 0, .10, 1, .16, (206, 210, 196))
        for u in (.30, .62):
            a.rect("jacket", u, .26, u + .10, .38, mul(col, .84)); a.hline("jacket", .26, mul(col, .55), u0=u, u1=u + .10)
        a.rect("jacket", .32, .42, .40, .50, (220, 150, 40))
        a.d.text((x + w * .335, y + h * .42), "3", fill=(30, 30, 30))
        a.rect("jacket", .44, 0, .56, 1, mul(col, .7))
        a.rect("sleeve", 0, .86, 1, 1, mul(col, .7))
        rnd = random.Random(11)
        for k in range(14):
            a.dot("jacket", rnd.random(), .5 + rnd.random() * .45, rnd.randint(1, 2), mul(col, .74))
    elif p == "hivis":
        for v in (.42, .62):
            a.rect("jacket", 0, v, 1, v + .07, (214, 214, 200))
            a.hline("jacket", v, mul(col, .6), u0=0, u1=1)
        a.rect("jacket", .44, 0, .56, 1, mul(col, .78))
        a.rect("jacket", .04, .14, .2, .26, (36, 36, 40))    # back print block: "NSP"
        a.rect("jacket", .80, .14, .96, .26, (36, 36, 40))


def paint_bottom(a, c):
    col = c["bottom"]["color"]
    a.grad("bottom", mul(col, 1.05), mul(col, .78), noise=.05, seed=4)
    a.hline("bottom", .03, mul(col, .55), width=4)          # belt / waistband
    a.vline("bottom", .25, mul(col, .7)); a.vline("bottom", .75, mul(col, .7))
    a.rect("bottom", .18, .52, .32, .58, mul(col, .86)); a.rect("bottom", .68, .52, .82, .58, mul(col, .86))  # knees
    lw = c["legwear"]
    a.grad("legwear", mul(lw, 1.04), mul(lw, .8), noise=.04, seed=5)
    sh = c["shoes"]
    a.grad("shoes", mul(sh, 1.15), mul(sh, .8), noise=.05, seed=6)
    a.rect("shoes", 0, .82, 1, 1, mul(sh, .5))
    a.hline("shoes", .5, mul(sh, 1.4), u0=.4, u1=.6)


def paint_skin(a, c):
    sk = c["skin"]
    a.grad("skin", mul(sk, 1.04), mul(sk, .9), noise=.02, seed=8)
    a.grad("skin2", mul(sk, .86), mul(sk, .76), noise=.02, seed=9)
    a.grad("hands", mul(sk, 1.02), mul(sk, .86), noise=.02, seed=10)
    for k in range(1, 4):
        a.vline("hands", .55 + k * .1, mul(sk, .78), v0=.55, v1=1)


def paint_cyber(a, c=None):
    x, y, w, h = a.tile("cyber")
    if c is not None and c.get("cyber") == "arm_r":
        # Leased industrial arm, remotely locked: grey plates, hazard stripes at the socket, dead red lock lamps,
        # and a strip of tape with his own marker writing.
        a.grad("cyber", (150, 152, 156), (92, 94, 100), noise=.06, seed=12)
        for v in (.22, .46, .70):
            a.hline("cyber", v, (52, 54, 60), width=2)
        for k in range(0, w, 8):
            a.d.polygon([(x + k, y), (x + k + 4, y), (x + k + 10, y + h * .08), (x + k + 6, y + h * .08)], fill=(220, 170, 40))
        a.rect("cyber", .30, .52, .70, .62, (232, 226, 206))
        for k in range(5):
            a.hline("cyber", .555, (40, 40, 60), u0=.33 + k * .07, u1=.37 + k * .07)
        for k in range(2):
            a.dot("cyber", .40 + k * .2, .36, 2, (200, 30, 30))
        return
    a.grad("cyber", (52, 52, 58), (24, 24, 28), noise=.06, seed=11)
    for v in (.18, .40, .62, .84):
        a.hline("cyber", v, (12, 12, 14), width=2)
        a.hline("cyber", v + .015, (90, 90, 96))
    for k in range(3):
        a.dot("cyber", .3 + k * .2, .5, 2, (255, 150, 40))
    a.vline("cyber", .5, (70, 70, 76), v0=0, v1=1)


def paint_swatches(a, c):
    sw = S.SW
    a.swatch(sw["black"], (22, 22, 24)); a.swatch(sw["white"], (232, 232, 226))
    a.swatch(sw["metal"], (150, 150, 156)); a.swatch(sw["darkmetal"], (60, 60, 66))
    a.swatch(sw["red_led"], (255, 60, 40)); a.swatch(sw["amber_led"], (255, 170, 60))
    a.swatch(sw["glass"], (80, 96, 100)); a.swatch(sw["ember"], (255, 110, 40))
    a.swatch(sw["yellow"], (238, 196, 40)); a.swatch(sw["reflective"], (210, 214, 200))
    a.swatch(sw["gold"], (210, 170, 80)); a.swatch(sw["strap"], (40, 40, 44))
    a.swatch(sw["lens"], (30, 30, 34)); a.swatch(sw["beard"], mul(c["hair"], .95))
    a.swatch(sw["cig"], (236, 230, 216)); a.swatch(sw["clip"], (240, 120, 150))


def paint_body(name, c):
    a = Atlas()
    paint_skin(a, c)
    paint_hair(a, c)
    paint_hair(a, c, "hairdark", dark=True)
    paint_top(a, c)
    paint_jacket(a, c)
    paint_bottom(a, c)
    paint_cyber(a, c)
    paint_swatches(a, c)
    return a.img


# ----------------------------------------------------------------------------------------------------------- face sheet
class Face:
    """Draws in head-local metres (x right on the image, z up) into one 256 px frame of the sheet."""
    SS = 4   # supersample, then downsample: soft but still chunky edges

    def __init__(self, c):
        self.c = c
        n = 256 * self.SS
        self.img = Image.new("RGBA", (n, n), c["skin"] + (255,))
        self.d = ImageDraw.Draw(self.img)
        self.sx = n / (2 * S.FACE_HW)
        self.sz = n / (S.FACE_Z1 - S.FACE_Z0)

    def p(self, x, z):
        return ((x + S.FACE_HW) * self.sx, (S.FACE_Z1 - z) * self.sz)

    def ellipse(self, x, z, rx, rz, fill, outline=None, width=0):
        x0, y0 = self.p(x - rx, z + rz)
        x1, y1 = self.p(x + rx, z - rz)
        self.d.ellipse([x0, y0, x1, y1], fill=fill, outline=outline, width=int(width * self.SS))

    def blend_ellipse(self, x, z, rx, rz, fill):
        layer = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        x0, y0 = self.p(x - rx, z + rz)
        x1, y1 = self.p(x + rx, z - rz)
        ImageDraw.Draw(layer).ellipse([x0, y0, x1, y1], fill=fill)
        layer = layer.filter(ImageFilter.GaussianBlur(self.SS * 2))
        self.img = Image.alpha_composite(self.img, layer)
        self.d = ImageDraw.Draw(self.img)

    def poly(self, pts, fill):
        self.d.polygon([self.p(x, z) for x, z in pts], fill=fill)

    def line(self, pts, fill, width):
        self.d.line([self.p(x, z) for x, z in pts], fill=fill, width=max(1, int(width * self.SS)), joint="curve")

    def done(self):
        return self.img.resize((256, 256), Image.LANCZOS)


def arc(cx, cz, rx, rz, a0, a1, n=12):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cz + rz * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def draw_face(c, eyes_open, mouth_open):
    """V2 face: larger, lower-set anime eyes (thick upper lash line with an outer flick, double-lid crease, iris
    gradient with two highlights), tapered brows, a nose shadow under the modelled nose, hatched blush, and
    per-character extras (glasses, beard, stubble, cyber eye)."""
    f = Face(c)
    sk = c["skin"]
    w = f.img.size[0]
    # Soft side shading so the face sheet meets the head's skin tile, plus the hair's shadow on the forehead.
    shade = Image.new("L", (w, w), 0)
    sd = ImageDraw.Draw(shade)
    for k in range(40):
        t = k / 40
        sd.rectangle([t * w * .13, 0, w - t * w * .13, w], fill=int(64 * (1 - t)))
    shade = shade.filter(ImageFilter.GaussianBlur(w * .02))
    f.img = Image.composite(Image.new("RGBA", (w, w), mul(sk, .84) + (255,)), f.img, shade)
    f.d = ImageDraw.Draw(f.img)
    f.blend_ellipse(0, .090, .10, .030, mul(sk, .80) + (150,))
    f.blend_ellipse(0, -.100, .045, .010, mul(sk, .78) + (170,))

    m = c["sex"] == "m"
    young = c["age"] == "young"
    old = c["age"] == "old"
    ink = (mul(c["hair"], .30) if not old else (54, 44, 40)) + (255,)
    ez = -.018 if not m else -.014
    ex = .036 if not m else .037
    ew = (.021 if young else .019) if not m else .016
    eh = (.026 if young else .020) if not m else .011

    if not m:
        for s_ in (-1, 1):
            f.blend_ellipse(s_ * .048, -.050, .016, .007, (236, 120, 120, 90))
            for k in range(3):   # hatch lines
                x0 = s_ * (.040 + k * .006)
                f.line([(x0 - .003, -.046), (x0 + .002, -.054)], (214, 110, 110, 150), .9)

    for s_ in (-1, 1):
        x = s_ * ex
        cyber = c.get("cyber") == "eye_r" and s_ == -1
        if eyes_open:
            if cyber:
                f.ellipse(x, ez, ew * 1.15, eh * 1.6 + .006, (24, 22, 24, 255))
                f.ellipse(x, ez, ew * .85, ew * .85, (255, 70, 40, 255))
                f.ellipse(x, ez, ew * .36, ew * .36, (255, 220, 160, 255))
                f.line([(x - ew * 1.5, ez + eh + .008), (x + ew * 1.3, ez + eh + .006)], (30, 30, 34, 255), 3)
                continue
            # Almond eye white, flatter at the bottom.
            white = arc(x, ez, ew * .98, eh * .95, 0, 180, 14) + arc(x, ez, ew * .98, eh * .55, 180, 360, 14)
            f.poly(white, (250, 248, 244, 255))
            iris = c["eyes"]
            irx, irz = ew * .62, eh * .92
            ix = x + s_ * ew * .04
            for k in range(10):
                t = k / 9
                f.ellipse(ix, ez - irz * .05 * t, irx * (1 - t * .12), irz * (1 - t * .45), mix(mul(iris, .40), mul(iris, 1.30), t) + (255,))
            f.ellipse(ix, ez + irz * .10, irx * .40, irz * .40, mul(iris, .22) + (255,))
            f.poly(arc(ix, ez, irx, irz, 0, 180, 10), mul(iris, .55) + (255,))   # lid shadow over the iris top
            f.ellipse(ix, ez + irz * .10, irx * .40, irz * .30, mul(iris, .22) + (255,))
            f.ellipse(x - s_ * ew * .22, ez + eh * .40, ew * .22, ew * .22, (255, 255, 255, 255))
            f.ellipse(x + s_ * ew * .28, ez - eh * .42, ew * .10, ew * .10, (255, 255, 255, 230))
            # Upper lash line: thick, heavier toward the outer corner, with a flick.
            lid = arc(x, ez + eh * .02, ew * 1.10, eh * 1.0, 8 if s_ > 0 else 172, 172 if s_ > 0 else 8, 16)
            f.line(lid, ink, 5.5 if not m else 3.0)
            if not m:
                ox = x + s_ * ew * 1.06
                f.poly([(ox - s_ * .006, ez + eh * .60), (ox + s_ * .008, ez + eh * .42), (ox + s_ * .001, ez + eh * .08)], ink)
                f.line([(x + s_ * ew * .40, ez - eh * .62), (x + s_ * ew * .96, ez - eh * .30)], ink, 1.4)
            # Double-lid crease.
            f.line(arc(x, ez + eh * .30, ew * .95, eh * 1.0, 30 if s_ > 0 else 150, 150 if s_ > 0 else 30, 10), mul(sk, .70) + (255,), 1.2)
            if old:
                f.line([(x - ew, ez - eh * 1.9), (x + ew, ez - eh * 2.1)], mul(sk, .7) + (255,), 1)
        else:
            if cyber:
                f.line([(x - ew, ez), (x + ew, ez)], (255, 70, 40, 255), 2.4)
                continue
            f.line(arc(x, ez + eh * .1, ew * 1.08, eh * .42, 192 if s_ > 0 else 348, 348 if s_ > 0 else 192), ink, 3.6 if not m else 2.6)
            if not m:
                ox = x + s_ * ew * 1.02
                f.line([(ox, ez + eh * .02), (ox + s_ * .006, ez - eh * .28)], ink, 2)

    # Brows: tapered strokes (thick at the inner end).
    bz = ez + eh + (.018 if not m else .016)
    for s_ in (-1, 1):
        x = s_ * ex
        if c["brows"] == "worried":
            inner, outer = (x - s_ * ew * .95, bz + .004), (x + s_ * ew * 1.15, bz - .002)
        elif c["brows"] == "heavy":
            # Straight, thick and a little low at the inner end: stern rather than sad.
            ix, ox = x - s_ * ew * 1.25, x + s_ * ew * 1.40
            f.poly([(ix, bz + .002), (ox, bz + .004), (ox, bz + .0005), (ix, bz - .003)], ink)
            continue
        else:
            inner, outer = (x - s_ * ew * 1.0, bz + .001), (x + s_ * ew * 1.25, bz + .003)
        th = .0034 if c["brows"] == "heavy" else (.0022 if not m else .0026)
        f.poly([(inner[0], inner[1] + th), outer, (inner[0], inner[1] - th * .6)], ink)

    # Nose: shadow under the modelled tip, a faint bridge highlight.
    f.poly([(-.006, -.052), (.007, -.050), (.000, -.056)], mul(sk, .72) + (255,))
    f.line([(.002, -.030), (.003, -.044)], mul(sk, 1.06) + (255,), 1.0)

    mz = -.071 if not m else -.073
    mouth = (150, 70, 70, 255)
    if mouth_open:
        f.ellipse(0, mz - .002, .009 if not m else .011, .006, (90, 30, 34, 255))
        f.ellipse(0, mz - .006, .005, .0025, (200, 100, 100, 255))
    elif c["mouth"] == "smirk":
        f.line([(-.009, mz), (.004, mz - .001), (.011, mz + .003)], mouth, 1.6)
    elif c["mouth"] == "flat":
        f.line([(-.011, mz), (.011, mz)], mouth, 1.7)
    else:
        f.line(arc(0, mz + .004, .008, .004, 200, 340, 6), mouth, 1.6)

    if "glasses_half" in c["accessories"]:
        for s_ in (-1, 1):
            x = s_ * ex
            top = ez + eh + .008
            f.line([(x - ew * 1.5, top), (x + ew * 1.5, top)], (60, 50, 60, 255), 2.4)
            f.line([(x - ew * 1.5, top), (x - ew * 1.55, ez - eh * .7)], (90, 80, 90, 160), 1)
            f.line([(x + ew * 1.5, top), (x + ew * 1.55, ez - eh * .7)], (90, 80, 90, 160), 1)
        f.line([(-ex + ew * 1.5, ez + eh + .008), (ex - ew * 1.5, ez + eh + .008)], (60, 50, 60, 255), 1.4)

    if "beard" in c["accessories"] or "stubble" in c["accessories"]:
        rnd = random.Random(3)
        full = "beard" in c["accessories"]
        beard = c["hair"]
        if full:
            f.poly([(-.088, -.045), (-.055, -.104), (.055, -.104), (.088, -.045), (.050, -.060), (.020, -.063), (-.020, -.063), (-.050, -.060)], mul(beard, .9) + (255,))
        for k in range(700 if full else 500):
            x, z = rnd.uniform(-.085, .085), rnd.uniform(-.104, -.050)
            if not full and abs(x) > .072 - (z + .104) * .2:
                continue
            f.ellipse(x, z, .0011, .0011, mul(beard, rnd.uniform(.6, 1.1)) + ((255,) if full else (120,)))
        f.poly([(-.016, mz + .006), (.016, mz + .006), (.012, mz - .008), (-.012, mz - .008)], mul(sk, .92) + (255,))
        if mouth_open:
            f.ellipse(0, mz - .002, .011, .006, (90, 30, 34, 255))
        else:
            f.line([(-.011, mz), (.011, mz)], mouth, 1.7)
    return f.done()


def paint_face(c):
    sheet = Image.new("RGBA", (S.FACE, S.FACE))
    sheet.paste(draw_face(c, True, False), (0, 0))
    sheet.paste(draw_face(c, False, False), (256, 0))
    sheet.paste(draw_face(c, True, True), (0, 256))
    sheet.paste(draw_face(c, False, True), (256, 256))
    return sheet.convert("RGB")


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, c in S.CHARACTERS.items():
        paint_body(name, c).save(os.path.join(OUT, "T_NPC_%s.png" % name))
        paint_face(c).save(os.path.join(OUT, "T_NPC_%s_Face.png" % name))
        print("painted", name)


if __name__ == "__main__":
    main()
