"""Paints the clinic NPC textures (system Python + Pillow, numpy).

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


def paint_cyber(a):
    x, y, w, h = a.tile("cyber")
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
    paint_cyber(a)
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
    f = Face(c)
    sk = c["skin"]
    # Soft shading: darker toward the sides (meets the head's skin tile), a band of hair shadow on the forehead.
    w = f.img.size[0]
    shade = Image.new("L", (w, w), 0)
    sd = ImageDraw.Draw(shade)
    for k in range(40):
        t = k / 40
        sd.rectangle([t * w * .14, 0, w - t * w * .14, w], fill=int(70 * (1 - t)))
    shade = shade.filter(ImageFilter.GaussianBlur(w * .02))
    dark = Image.new("RGBA", (w, w), mul(sk, .84) + (255,))
    f.img = Image.composite(dark, f.img, shade)
    f.d = ImageDraw.Draw(f.img)
    f.poly([(-.09, .115), (.09, .115), (.09, .052), (-.09, .052)], mul(sk, .9) + (255,))
    f.poly([(-.09, .052), (.09, .052), (.09, .044), (-.09, .044)], mul(sk, .95) + (255,))

    young = c["age"] == "young"
    old = c["age"] == "old"
    ink = mul(c["hair"], .35) if not old else (54, 44, 40)
    ink = ink + (255,)
    ez = -.010 if not old else -.004
    ex = .034 if c["sex"] == "f" else .036
    ew = (.017 if young else .016) if c["sex"] == "f" else .013
    eh = (.022 if young else .015) if c["sex"] == "f" else .008

    # Blush.
    if c["sex"] == "f":
        for s in (-1, 1):
            f.blend_ellipse(s * .046, -.038, .015, .006, (236, 120, 120, 80))

    for s in (-1, 1):
        x = s * ex
        cyber = c.get("cyber") == "eye_r" and s == -1
        if eyes_open:
            if cyber:
                f.ellipse(x, ez, ew * 1.05, eh * 1.4 + .004, (24, 22, 24, 255))
                f.ellipse(x, ez, ew * .8, ew * .8, (255, 70, 40, 255))
                f.ellipse(x, ez, ew * .35, ew * .35, (255, 220, 160, 255))
                f.line([(x - ew * 1.4, ez + eh + .006), (x + ew * 1.2, ez + eh + .004)], (30, 30, 34, 255), 3)
                continue
            f.ellipse(x, ez - eh * .1, ew * .95, eh * .92, (250, 248, 244, 255))
            iris = c["eyes"]
            irx, irz = ew * .66, eh * .96
            for k in range(8):
                t = k / 7
                f.ellipse(x + s * ew * .05, ez + irz * (.15 - .1 * t), irx * (1 - t * .1), irz * (1 - t * .55),
                          mix(mul(iris, .45), mul(iris, 1.25), t) + (255,))
            f.ellipse(x + s * ew * .05, ez + irz * .12, irx * .38, irz * .42, mul(iris, .25) + (255,))
            f.ellipse(x - ew * .25, ez + eh * .45, ew * .2, ew * .2 * (eh / ew) ** .3, (255, 255, 255, 255))
            f.ellipse(x + ew * .3, ez - eh * .45, ew * .09, ew * .09, (255, 255, 255, 230))
            # Upper lid with an outer flick, lower lash at the outer corner.
            lid = arc(x, ez - eh * .1, ew * 1.08, eh * 1.05, 10 if s > 0 else 170, 170 if s > 0 else 10)
            f.line(lid, ink, 4.2 if c["lashes"] != "none" else 2)
            if c["lashes"] in ("young", "mature"):
                # Outer wing: a small dark wedge past the corner of the upper lid.
                ox = x + s * ew * 1.02
                f.poly([(ox - s * .004, ez + eh * .55), (ox + s * .006, ez + eh * .30), (ox - s * .001, ez + eh * .05)], ink)
                f.line([(x + s * ew * .45, ez - eh * .98), (x + s * ew * .92, ez - eh * .62)], ink, 1.4)
            if old:
                f.line([(x - ew, ez - eh * 1.8), (x + ew, ez - eh * 2.0)], mul(sk, .7) + (255,), 1)
        else:
            if cyber:
                f.line([(x - ew, ez), (x + ew, ez)], (255, 70, 40, 255), 2.2)
                continue
            f.line(arc(x, ez + eh * .1, ew * 1.05, eh * .45, 190 if s > 0 else 350, 350 if s > 0 else 190), ink, 3)
            if c["lashes"] != "none":
                ox = x + s * ew * 1.0
                f.line([(ox, ez + eh * .02), (ox + s * .005, ez - eh * .25)], ink, 2)

    # Brows.
    bz = ez + (eh + .016 if c["sex"] == "f" else eh + .014)
    for s in (-1, 1):
        x = s * ex
        if c["brows"] == "worried":
            pts = [(x - s * ew * .9, bz - .002), (x + s * ew * 1.1, bz + .004)]
        elif c["brows"] == "heavy":
            pts = [(x - s * ew * 1.2, bz + .001), (x + s * ew * 1.3, bz - .002)]
        else:
            pts = [(x - s * ew * 1.0, bz + .002), (x + s * ew * 1.2, bz + .001)]
        f.line(pts, ink, 3.6 if c["brows"] == "heavy" else 1.8)

    # Nose: a small shade mark.
    f.line([(.002, -.040), (-.002, -.046)], mul(sk, .72) + (255,), 1.6)

    # Mouth.
    mz = -.068 if not old else -.066
    mouth = mul((150, 70, 70), 1.0) + (255,)
    if mouth_open:
        f.ellipse(0, mz - .002, .008 if c["sex"] == "f" else .010, .0055, (90, 30, 34, 255))
        f.ellipse(0, mz - .005, .005, .0025, (200, 100, 100, 255))
    else:
        if c["mouth"] == "smirk":
            f.line([(-.008, mz), (.004, mz - .001), (.010, mz + .003)], mouth, 1.6)
        elif c["mouth"] == "flat":
            f.line([(-.010, mz), (.010, mz)], mouth, 1.6)
        else:
            f.line(arc(0, mz + .004, .007, .004, 200, 340, 6), mouth, 1.6)

    # Half-rim glasses (painted: thin enough to read better on the texture than as geometry).
    if "glasses_half" in c["accessories"]:
        for s in (-1, 1):
            x = s * ex
            f.line([(x - ew * 1.5, ez + eh + .006), (x + ew * 1.5, ez + eh + .006)], (60, 50, 60, 255), 2.4)
            f.line([(x - ew * 1.5, ez + eh + .006), (x - ew * 1.55, ez - eh * .6)], (90, 80, 90, 160), 1)
            f.line([(x + ew * 1.5, ez + eh + .006), (x + ew * 1.55, ez - eh * .6)], (90, 80, 90, 160), 1)
        f.line([(-ex + ew * 1.5, ez + eh + .006), (ex - ew * 1.5, ez + eh + .006)], (60, 50, 60, 255), 1.4)

    # Beard: stubbled grey from the cheeks down, mouth kept clear.
    if "beard" in c["accessories"]:
        rnd = random.Random(3)
        beard = c["hair"]
        f.poly([(-.085, -.035), (-.06, -.105), (.06, -.105), (.085, -.035), (.05, -.055), (.02, -.06), (-.02, -.06), (-.05, -.055)], mul(beard, .9) + (255,))
        for k in range(500):
            x, z = rnd.uniform(-.08, .08), rnd.uniform(-.104, -.04)
            f.ellipse(x, z, .0012, .0012, mul(beard, rnd.uniform(.6, 1.15)) + (255,))
        f.poly([(-.016, mz + .006), (.016, mz + .006), (.012, mz - .008), (-.012, mz - .008)], mul(sk, .9) + (255,))
        if mouth_open:
            f.ellipse(0, mz - .002, .010, .0055, (90, 30, 34, 255))
        else:
            f.line([(-.010, mz), (.010, mz)], mouth, 1.6)
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
