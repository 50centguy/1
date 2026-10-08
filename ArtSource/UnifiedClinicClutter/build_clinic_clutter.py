"""UnifiedClinic clutter add-on: wall-bound lived-in detail for the existing UnifiedClinic room.

Additive asset only. Never edits or saves ../UnifiedClinic (base .blend is opened read-only in memory for checks and
renders; its sha256 is asserted unchanged).

Run:
  blender.exe -b --factory-startup --python build_clinic_clutter.py [-- norender]

Phases (one process):
  A  build add-on in an empty scene -> textures, UnifiedClinicClutter.blend, Export/UnifiedClinic_ClutterAddon.fbx
  B  FBX round trip (re-import, compare per-object Unity bounds)
  C  open base UnifiedClinic.blend in memory, append add-on, geometric checks vs base meshes / zones / paths, renders
     (render-only cameras and the light live only in memory; nothing is saved in phase C)

Unity axes +Y up, +Z north, +X east. Blender -> Unity u = (-bx, bz, -by). Export settings identical to UnifiedClinic.
Props are authored in wall-local coordinates (s along the wall, y height, d distance off the inner wall face).
"""
import hashlib
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(HERE)
BASE_DIR = os.path.join(ART, "UnifiedClinic")
BASE_BLEND = os.path.join(BASE_DIR, "UnifiedClinic.blend")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "norender" not in ARGS
D_EXPORT, D_TEX, D_RENDER, D_REPORT = [os.path.join(HERE, d) for d in ("Export", "Textures", "Renders", "Reports")]
for _d in (D_EXPORT, D_TEX, D_RENDER, D_REPORT):
    os.makedirs(_d, exist_ok=True)
OUT_BLEND = os.path.join(HERE, "UnifiedClinicClutter.blend")
OUT_FBX = os.path.join(D_EXPORT, "UnifiedClinic_ClutterAddon.fbx")
ROOT_NAME = "UnifiedClinicClutter"

# ---------------------------------------------------------------------------
# Immutable base layout (from ../UnifiedClinic build script / anchors.json / checks.json)
# ---------------------------------------------------------------------------
A = 3.40                                   # octagon apothem, inner wall face
WALL_K = {"NE": 1, "NW": 3, "SW": 5, "SE": 7}
STRIP_Y = {"SW": 1.55, "SE": 1.55, "NW": 1.70, "NE": 1.70}   # below: wall-hugging depth only
LOW_DEPTH, MAX_DEPTH = 0.07, 0.20
S_LIMIT = 1.27                             # corner pilasters start at |s| = 1.28
Y_TOP = 2.235                              # trim rail underside 2.24, cable garland 2.31+
PANEL_D = 0.022                            # inset wall panels stand 22 mm proud of the wall face (measured)
ZONES = {   # reserved floor zones, y 0.03..1.97 (checks.json)
    "dock_work": (-2.39, -1.65, -0.95, 0.95), "dock_side_access_L": (-3.00, -2.30, 0.95, 1.30),
    "dock_side_access_R": (-3.00, -2.30, -1.30, -0.95), "bench_stand": (-0.96, 0.96, 1.885, 2.485),
    "trade_customer_strip": (1.22, 1.555, -1.00, 0.95), "trade_operator": (2.235, 3.00, -0.95, 0.95)}
FORBID_ALL_Y = {   # task-level keep-outs, any height
    "dock_workspace": (-3.25, -1.95, -0.95, 0.95),
    "trade_receive_approach": (1.15, 2.10, -0.90, -0.30), "trade_deliver_approach": (1.15, 2.10, 0.30, 0.90)}
ASSET_BOUNDS = {   # existing dock / robot / bench / bed-ring Unity bounds (checks.json), kept >= 0.15 m away
    "dock": ((-3.25, -0.01, -0.7268), (-2.3964, 1.03, 0.5)), "robot": ((-2.9375, 0.7236, -0.5098), (-2.46, 1.3665, 0.5098)),
    "bench": ((-1.7, -0.05, 0.65), (1.7, 2.6, 3.5)), "bed_and_ring": ((-1.05, 0.0, -1.15), (0.85, 3.0, 0.75))}
PATHS = {
    "principal_entrance_west_bench": [(0, -3.30), (0, -2.45), (-1.05, -1.75), (-1.08, 0.0), (-1.00, 1.30), (0.0, 1.55)],
    "east_entrance_trade": [(0, -2.45), (0.77, -1.75), (0.77, 0.0), (0.85, 1.35), (1.40, 1.45)],
    "north_cross": [(-1.00, 1.30), (0.0, 1.45), (0.85, 1.40), (1.40, 1.45)],
    "trade_operator_entry": [(1.40, 1.45), (2.45, 1.35), (2.58, 0.60), (2.55, 0.25)]}
PATH_HALF = {"principal_entrance_west_bench": 0.45, "east_entrance_trade": 0.45, "north_cross": 0.45, "trade_operator_entry": 0.35}
BUDGET = dict(triangles=4000, mesh_objects=20, materials=4, textures=2)

RNG = np.random.default_rng(202610081)


def ub(p):
    return Vector((-p[0], -p[2], p[1]))


def bu(v):
    return Vector((-v[0], v[2], -v[1]))


def r3(v):
    return [round(float(x), 4) for x in v]


def wall_frame(w):
    a = math.radians(45 * WALL_K[w])
    return Vector((math.cos(a), 0, math.sin(a))), Vector((-math.sin(a), 0, math.cos(a)))


def W(w, q):
    """wall-local (s, y, d) -> Unity point"""
    n, t = wall_frame(w)
    return n * (A - q[2]) + t * q[0] + Vector((0, q[1], 0))


def to_wall(w, p):
    n, t = wall_frame(w)
    return p.x * t.x + p.z * t.z, p.y, A - (p.x * n.x + p.z * n.z)


def seg_dist(p, a, b):
    ax, az, bx, bz = a[0], a[1], b[0], b[1]
    dx, dz = bx - ax, bz - az
    L = dx * dx + dz * dz
    u = 0.0 if L == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / L))
    return math.hypot(p[0] - ax - u * dx, p[1] - az - u * dz)


# ---------------------------------------------------------------------------
# Procedural textures (numpy, sRGB 0..1, row 0 = top)
# ---------------------------------------------------------------------------
ATLAS = {"A": ("T_UCC_SoftAtlas.png", 256), "P": ("T_UCC_PaintAtlas.png", 128)}
REG = {   # (atlas, x0, y0, x1, y1) pixel rects, y from top
    "coat": ("A", 0, 0, 128, 128), "cloth": ("A", 128, 0, 256, 48), "bag": ("A", 128, 48, 256, 112),
    "patch": ("A", 128, 112, 160, 128), "strap": ("A", 160, 112, 256, 128),
    "note0": ("A", 0, 128, 64, 192), "note1": ("A", 64, 128, 128, 192), "note2": ("A", 128, 128, 192, 192),
    "note3": ("A", 192, 128, 256, 192), "tape": ("A", 0, 192, 64, 224), "label": ("A", 64, 192, 128, 224),
    "note4": ("A", 128, 192, 192, 256), "tag": ("A", 192, 192, 256, 256),
    "ivory": ("P", 0, 0, 64, 64), "green": ("P", 64, 0, 128, 64), "steel": ("P", 0, 64, 64, 128),
    "cab0": ("P", 64, 64, 128, 80), "cab1": ("P", 64, 80, 128, 96), "cab2": ("P", 64, 96, 128, 112), "cab3": ("P", 64, 112, 128, 128)}


def vnoise(h, w, cell):
    g = RNG.random((h // cell + 2, w // cell + 2))
    ys, xs = np.arange(h) / cell, np.arange(w) / cell
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = (ys - y0)[:, None], (xs - x0)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a, b = g[y0][:, x0], g[y0][:, x0 + 1]
    c, d = g[y0 + 1][:, x0], g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fill(img, rect, col, var=0.05, cell=8):
    x0, y0, x1, y1 = rect
    h, w = y1 - y0, x1 - x0
    n = vnoise(h, w, cell) - 0.5
    f = RNG.random((h, w)) - 0.5
    img[y0:y1, x0:x1] = np.clip(np.array(col)[None, None, :] * (1 + var * 2 * n[..., None] + 0.04 * f[..., None]), 0, 1)


def grime(img, rect, strength=0.35, bottom=True):
    x0, y0, x1, y1 = rect
    h, w = y1 - y0, x1 - x0
    g = (np.linspace(0, 1, h)[:, None] ** 2 if bottom else 0) * 0.6 + vnoise(h, w, 6) * 0.6
    img[y0:y1, x0:x1] *= (1 - strength * np.clip(g, 0, 1))[..., None]


def stitch_rect(img, x0, y0, x1, y1, col, step=3):
    for x in range(x0, x1, step):
        img[y0, x] = col
        img[y1 - 1, x] = col
    for y in range(y0, y1, step):
        img[y, x0] = col
        img[y, x1 - 1] = col


def scribble(img, x0, y0, x1, y1, rows, col=(0.20, 0.20, 0.23)):
    """abstract pencil marks on ruled rows (no text, no imagery)"""
    for r in rows:
        x = x0 + int(RNG.integers(0, 4))
        while x < x1 - 4:
            L = int(RNG.integers(3, 12))
            xe = min(x + L, x1 - 2)
            for xx in range(x, xe):
                yy = r + int(round(math.sin(xx * 1.7 + r) * 0.7))
                img[yy, xx] = col
            x = xe + int(RNG.integers(2, 6))


def tex_soft():
    S = 256
    img = np.zeros((S, S, 3))
    # coat: grey-green twill wool, worn and grimy toward the hem, one sewn patch
    fill(img, (0, 0, 128, 128), (0.36, 0.40, 0.33), 0.10, 10)
    yy, xx = np.mgrid[0:128, 0:128]
    img[0:128, 0:128] *= (1 + 0.05 * (((xx + yy // 2) % 4) < 2))[..., None]
    grime(img, (0, 0, 128, 128), 0.40)
    img[78:104, 66:94] = np.array((0.28, 0.33, 0.27)) * (0.9 + 0.2 * RNG.random((26, 28, 1)))
    stitch_rect(img, 66, 78, 94, 104, (0.72, 0.69, 0.58))
    img[20:28, 14:40] *= 1.12      # rubbed shoulder
    # cloth: old ivory shop cloth with two grey-green border stripes and stains
    fill(img, (128, 0, 256, 48), (0.80, 0.77, 0.66), 0.06, 6)
    for r in (5, 6, 41, 42):
        img[r, 128:256] = (0.40, 0.45, 0.38)
    for _ in range(5):
        cy, cx, rr = int(RNG.integers(8, 40)), int(RNG.integers(136, 248)), int(RNG.integers(3, 7))
        m = (yy[:48, :128] - cy) ** 2 + (xx[:48, :128] + 128 - cx) ** 2 < rr * rr
        img[0:48, 128:256][m] *= (0.78, 0.76, 0.70)
    # bag: grey canvas, sewn green-grey patch, grime
    fill(img, (128, 48, 256, 112), (0.44, 0.45, 0.42), 0.08, 8)
    img[48:112, 128:256] *= (1 + 0.04 * ((xx[:64, :128] + yy[:64, :128]) % 2))[..., None]
    img[70:96, 196:236] = (0.31, 0.37, 0.30)
    stitch_rect(img, 196, 70, 236, 96, (0.74, 0.70, 0.58))
    grime(img, (128, 48, 256, 112), 0.35)
    # patch + strap
    fill(img, (128, 112, 160, 128), (0.30, 0.36, 0.29), 0.08, 4)
    stitch_rect(img, 129, 113, 159, 127, (0.74, 0.70, 0.58), 2)
    fill(img, (160, 112, 256, 128), (0.33, 0.37, 0.31), 0.05, 4)
    for r in (114, 125):
        img[r, 160:256] = (0.24, 0.27, 0.23)
    for c in range(162, 256, 3):
        img[119, c] = (0.55, 0.55, 0.47)
    # notes: aged ivory paper, abstract marks only
    papers = {"note0": (0.86, 0.83, 0.72), "note1": (0.84, 0.84, 0.78), "note2": (0.87, 0.82, 0.66),
              "note3": (0.82, 0.78, 0.62), "note4": (0.74, 0.79, 0.70)}
    for nm, col in papers.items():
        _, x0, y0, x1, y1 = REG[nm]
        fill(img, (x0, y0, x1, y1), col, 0.04, 8)
        grime(img, (x0, y0, x1, y1), 0.18, bottom=False)
        img[y0:y0 + 2, x0:x1] *= 0.85
        img[y1 - 2:y1, x0:x1] *= 0.85
        img[y0:y1, x0:x0 + 2] *= 0.85
        img[y0:y1, x1 - 2:x1] *= 0.85
    _, x0, y0, x1, y1 = REG["note0"]
    for r in range(y0 + 10, y1 - 4, 7):
        img[r, x0 + 3:x1 - 3] = (0.62, 0.66, 0.70)
    scribble(img, x0 + 4, y0, x1 - 4, y1, list(range(y0 + 9, y1 - 6, 7)))
    _, x0, y0, x1, y1 = REG["note1"]      # grid paper with a boxed sketch
    for r in range(y0 + 4, y1, 6):
        img[r, x0 + 2:x1 - 2] = (0.70, 0.74, 0.72)
    for c in range(x0 + 4, x1, 6):
        img[y0 + 2:y1 - 2, c] = (0.70, 0.74, 0.72)
    for (a, b, c_, d_) in ((10, 12, 40, 34), (24, 30, 54, 52), (14, 40, 28, 56)):
        stitch_rect(img, x0 + a, y0 + b, x0 + c_, y0 + d_, (0.22, 0.22, 0.25), 1)
    _, x0, y0, x1, y1 = REG["note2"]      # checklist: boxes, ticks, dashes
    for i, r in enumerate(range(y0 + 8, y1 - 6, 9)):
        stitch_rect(img, x0 + 5, r - 3, x0 + 11, r + 3, (0.22, 0.22, 0.25), 1)
        if i % 2 == 0:
            for k in range(5):
                img[r - 1 + (k if k < 2 else 2 - k), x0 + 6 + k] = (0.45, 0.18, 0.14)
        scribble(img, x0 + 15, y0, x1 - 5, y1, [r])
    _, x0, y0, x1, y1 = REG["note3"]      # card with heavy marker bars
    for i, r in enumerate(range(y0 + 10, y1 - 8, 12)):
        img[r:r + 4, x0 + 6:x0 + 6 + int(RNG.integers(20, 50))] = (0.18, 0.19, 0.20)
    _, x0, y0, x1, y1 = REG["note4"]
    scribble(img, x0 + 5, y0, x1 - 5, y1, list(range(y0 + 8, y1 - 6, 8)), (0.16, 0.18, 0.20))
    # tape, label, tag
    fill(img, (0, 192, 64, 224), (0.78, 0.72, 0.52), 0.05, 4)
    img[192:194, 0:64] *= 0.8
    img[222:224, 0:64] *= 0.8
    fill(img, (64, 192, 128, 224), (0.80, 0.78, 0.68), 0.04, 4)
    stitch_rect(img, 66, 194, 126, 222, (0.15, 0.15, 0.16), 1)
    for i, r in enumerate((200, 207, 214)):
        img[r:r + 3, 72:72 + (44 - 12 * i)] = (0.18, 0.18, 0.19)
    fill(img, (192, 192, 256, 256), (0.55, 0.30, 0.22), 0.08, 6)
    grime(img, (192, 192, 256, 256), 0.3, bottom=False)
    return img


def tex_paint():
    S = 128
    img = np.zeros((S, S, 3))
    for nm, col in (("ivory", (0.78, 0.74, 0.62)), ("green", (0.34, 0.39, 0.33))):
        _, x0, y0, x1, y1 = REG[nm]
        fill(img, (x0, y0, x1, y1), col, 0.06, 8)
        chips = vnoise(64, 64, 4) > 0.80
        img[y0:y1, x0:x1][chips] = (0.30, 0.31, 0.30)
        rust = (vnoise(64, 64, 3) > 0.86) & chips
        img[y0:y1, x0:x1][rust] = (0.42, 0.27, 0.17)
        for c in RNG.integers(x0 + 2, x1 - 2, 3):     # short rust run-off streaks, restrained
            L = int(RNG.integers(8, 20))
            img[y0 + 30:y0 + 30 + L, c] *= (0.82, 0.74, 0.66)
        grime(img, (x0, y0, x1, y1), 0.30)
    fill(img, (0, 64, 64, 128), (0.25, 0.26, 0.26), 0.12, 6)
    rust = vnoise(64, 64, 5) > 0.78
    img[64:128, 0:64][rust] = (0.36, 0.25, 0.18)
    grime(img, (0, 64, 64, 128), 0.25)
    for i, col in enumerate(((0.10, 0.10, 0.10), (0.36, 0.17, 0.14), (0.60, 0.58, 0.51), (0.27, 0.32, 0.27))):
        fill(img, (64, 64 + 16 * i, 128, 80 + 16 * i), col, 0.06, 4)
        img[64 + 16 * i, 64:128] *= 0.8
    return img


IMAGES = {}


def save_png(key):
    name, size = ATLAS[key]
    arr = tex_soft() if key == "A" else tex_paint()
    im = bpy.data.images.new(name.replace(".png", ""), size, size, alpha=False)
    rgba = np.concatenate([arr[::-1], np.ones((size, size, 1))], axis=2).astype(np.float32)
    im.pixels.foreach_set(rgba.ravel())
    path = os.path.join(D_TEX, name)
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    im.filepath = path
    IMAGES[key] = im


# ---------------------------------------------------------------------------
# Materials (4)
# ---------------------------------------------------------------------------
MAT_SPECS = {"M_UCC_Fabric": ("A", 0.0, 0.95), "M_UCC_Paper": ("A", 0.0, 0.85),
             "M_UCC_PaintedMetal": ("P", 0.35, 0.68), "M_UCC_Cable": ("P", 0.0, 0.72)}
MATS = {}


def build_materials():
    for name, (atl, metal, rough) in MAT_SPECS.items():
        m = bpy.data.materials.new(name)
        N, L = m.node_tree.nodes, m.node_tree.links
        bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
        it = N.new("ShaderNodeTexImage")
        it.image = IMAGES[atl]
        it.interpolation = "Closest"
        L.new(it.outputs["Color"], bsdf.inputs["Base Color"])
        MATS[name] = m


# ---------------------------------------------------------------------------
# Mesh builder in wall-local coords (s, y, d) with per-corner UVs into atlas regions
# ---------------------------------------------------------------------------
def reg_uv(region, u, v):
    atl, x0, y0, x1, y1 = REG[region]
    S = ATLAS[atl][1]
    px = x0 + 0.5 + u * (x1 - x0 - 1.0)
    py = y1 - 0.5 - v * (y1 - y0 - 1.0)
    return (px / S, 1 - py / S)


class MB:
    def __init__(self, wall):
        self.w, self.v, self.f, self.uv = wall, [], [], []

    def add(self, pts, faces, uvs):
        o = len(self.v)
        self.v += [Vector(p) for p in pts]
        self.f += [[i + o for i in fc] for fc in faces]
        self.uv += uvs
        return self

    def proj(self, pts, faces, region, frame=None):
        """planar-projected UVs per face (dominant local axis), normalized to the part bounds"""
        P = [Vector(p) for p in pts]
        Q = [Vector(q) for q in frame] if frame else P
        lo = [min(q[i] for q in Q) for i in range(3)]
        hi = [max(q[i] for q in Q) for i in range(3)]
        nz = lambda q, i: (q[i] - lo[i]) / max(hi[i] - lo[i], 1e-6)
        uvs = []
        for fc in faces:
            a, b, c = Q[fc[0]], Q[fc[1]], Q[fc[2]]
            nrm = (b - a).cross(c - a)
            ax = max(range(3), key=lambda i: abs(nrm[i]))
            pair = {2: (0, 1), 0: (2, 1), 1: (0, 2)}[ax]
            uvs.append([reg_uv(region, nz(Q[i], pair[0]), nz(Q[i], pair[1])) for i in fc])
        return self.add(P, faces, uvs)

    def box(self, lo, hi, region):
        (x0, y0, z0), (x1, y1, z1) = lo, hi
        pts = [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]
        return self.proj(pts, BOX_F, region)

    def plate(self, cs, cy, w, h, d0, d1, ang, region):
        """thin rectangle on the wall, rotated by ang (deg) in the wall plane; image aligned with the plate"""
        t = math.radians(ang)
        c, s_ = math.cos(t), math.sin(t)
        pts, loc = [], []
        for (a, b, d) in [(-1, -1, d0), (1, -1, d0), (1, -1, d1), (-1, -1, d1), (-1, 1, d0), (1, 1, d0), (1, 1, d1), (-1, 1, d1)]:
            x, y = a * w / 2, b * h / 2
            pts.append((cs + x * c - y * s_, cy + x * s_ + y * c, d))
            loc.append(Vector((x, y, d)))
        # project with the un-rotated plate frame so the note image stays square to the paper
        return self.proj(pts, BOX_F, region, frame=loc)

    def loft(self, rings, region, caps=True):
        m, n = len(rings), len(rings[0])
        pts = [p for r in rings for p in r]
        faces, uvs = [], []
        for i in range(m - 1):
            for j in range(n):
                k = (j + 1) % n
                faces.append([i * n + j, i * n + k, (i + 1) * n + k, (i + 1) * n + j])
                v0, v1 = 1 - i / (m - 1), 1 - (i + 1) / (m - 1)
                u0, u1 = j / n, (j + 1) / n
                uvs.append([reg_uv(region, u0, v0), reg_uv(region, u1, v0), reg_uv(region, u1, v1), reg_uv(region, u0, v1)])
        if caps:
            for cap in (list(range(n))[::-1], [(m - 1) * n + j for j in range(n)]):
                Q = [Vector(pts[i]) for i in cap]
                lo = [min(q[i] for q in Q) for i in range(3)]
                hi = [max(q[i] for q in Q) for i in range(3)]
                ax = (0, 2) if (hi[1] - lo[1]) < min(hi[0] - lo[0], hi[2] - lo[2]) else (0, 1)
                faces.append(cap)
                uvs.append([reg_uv(region, (q[ax[0]] - lo[ax[0]]) / max(hi[ax[0]] - lo[ax[0]], 1e-6) * 0.3,
                                   (q[ax[1]] - lo[ax[1]]) / max(hi[ax[1]] - lo[ax[1]], 1e-6) * 0.3) for q in Q])
        o = len(self.v)
        self.v += [Vector(p) for p in pts]
        self.f += [[i + o for i in fc] for fc in faces]
        self.uv += uvs
        return self

    def lathe(self, cs, cd, profile, segs, region, phase=0.0):
        """vertical solid of revolution: profile [(y, r), ...] bottom -> top"""
        rings = []
        for (y, r) in profile[::-1]:
            rings.append([(cs + r * math.cos(2 * math.pi * j / segs + phase), y, cd + r * math.sin(2 * math.pi * j / segs + phase))
                          for j in range(segs)])
        return self.loft(rings, region)

    def tube(self, pts, rs, rd, segs, region):
        """tube along a polyline; elliptical section: rd along the wall normal, rs across"""
        P = [Vector(p) for p in pts]
        rings = []
        for i, p in enumerate(P):
            tg = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
            ref = Vector((0, 0, 1)) if abs(tg.z) < 0.9 else Vector((0, 1, 0))
            nr = (ref - tg * ref.dot(tg)).normalized()
            bn = tg.cross(nr)
            rings.append([p + nr * (rd * math.cos(2 * math.pi * j / segs)) + bn * (rs * math.sin(2 * math.pi * j / segs))
                          for j in range(segs)])
        return self.loft(rings, region)


BOX_F = [[0, 1, 2, 3], [4, 7, 6, 5], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]


def bez(p0, p1, p2, n):
    p0, p1, p2 = Vector(p0), Vector(p1), Vector(p2)
    return [p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t for t in (i / n for i in range(n + 1))]


# ---------------------------------------------------------------------------
# Object creation
# ---------------------------------------------------------------------------
ROOT = None
OBJ_INFO = {}


def make(name, mb, mat, note):
    U = [W(mb.w, p) for p in mb.v]
    lo = Vector([min(u[i] for u in U) for i in range(3)])
    hi = Vector([max(u[i] for u in U) for i in range(3)])
    pivot = Vector(((lo.x + hi.x) / 2, lo.y, (lo.z + hi.z) / 2))
    pb = ub(pivot)
    me = bpy.data.meshes.new(name)
    me.from_pydata([ub(u) - pb for u in U], [], mb.f)
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    for poly, fuv in zip(me.polygons, mb.uv):
        for k, loop_index in enumerate(poly.loop_indices):
            uvl.data[loop_index].uv = fuv[k]
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.materials.append(MATS[mat])
    obj = bpy.data.objects.new(name, me)
    obj.location = pb
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = ROOT
    obj["ucc_wall"] = mb.w
    obj["ucc_note"] = note
    obj["ucc_pivot_unity"] = ",".join("%.4f" % x for x in pivot)
    OBJ_INFO[name] = dict(wall=mb.w, material=mat, note=note, pivot_unity=r3(pivot))
    return obj


# ---------------------------------------------------------------------------
# Props
# ---------------------------------------------------------------------------
def coat_ring(s0, y, hw, dd, back=0.026, n=10, skew=0.0):
    out = []
    for j in range(n):
        th = 2 * math.pi * j / n
        sn = math.sin(th)
        out.append((s0 + skew + hw * math.cos(th), y, back + dd * max(sn, 0.0)))
    return out


def hook(mb, s, y):
    mb.tube([(s, y - 0.01, 0.040), (s, y + 0.005, 0.075), (s + 0.002, y + 0.025, 0.098), (s + 0.003, y + 0.042, 0.104)],
            0.0065, 0.0065, 5, "steel")
    mb.box((s - 0.014, y - 0.03, 0.044), (s + 0.014, y + 0.012, 0.050), "steel")     # screwed hook plate


def build_se():
    """SE wall (inside the entrance, east of the door): hook rail with worn coat, folded cloth, patched shoulder bag; notes"""
    fab, met, pap = MB("SE"), MB("SE"), MB("SE")
    # hook rail: painted flat bar + 3 hooks + 2 fixing bolts
    met.box((-0.98, 2.02, 0.025), (-0.06, 2.075, 0.044), "green")
    for s in (-0.95, -0.09):
        met.box((s - 0.008, 2.040, 0.044), (s + 0.008, 2.056, 0.050), "steel")
    for s in (-0.80, -0.50, -0.20):
        hook(met, s, 2.03)
    # worn coat on the first hook: lofted low-poly body; below y 1.55 it hangs flat (depth <= 0.07)
    s0 = -0.80
    prof = [(2.075, 0.035, 0.040), (2.04, 0.07, 0.07), (1.98, 0.15, 0.090), (1.90, 0.205, 0.115), (1.76, 0.215, 0.105),
            (1.62, 0.21, 0.080), (1.56, 0.21, 0.058), (1.54, 0.212, 0.043), (1.34, 0.222, 0.040), (1.12, 0.232, 0.034)]
    rings = [coat_ring(s0, y, hw, dd, skew=0.012 * math.sin(i * 1.3)) for i, (y, hw, dd) in enumerate(prof)]
    fab.loft(rings, "coat")
    for side in (-1, 1):   # flattened sleeves hanging at the sides, elliptical so they stay wall-hugging below 1.55
        sx = s0 + side * 0.20
        fab.tube([(sx, 1.90, 0.060), (sx + side * 0.018, 1.70, 0.054), (sx + side * 0.025, 1.50, 0.047), (sx + side * 0.016, 1.27, 0.045)],
                 0.042, 0.020, 6, "coat")
        fab.box((sx + side * 0.016 - 0.035, 1.24, 0.030), (sx + side * 0.016 + 0.035, 1.28, 0.060), "patch")   # cuff
    fab.plate(s0 + 0.09, 1.42, 0.11, 0.10, 0.058, 0.066, -4, "patch")        # patched pocket
    fab.plate(s0 - 0.03, 1.995, 0.17, 0.05, 0.098, 0.112, 0, "coat")        # turned collar
    # folded cloth over the middle hook: a draped strip extruded along the wall
    sc = -0.50
    path = [(0.052, 1.70), (0.054, 1.96), (0.062, 2.045), (0.080, 2.082), (0.100, 2.084), (0.116, 2.060), (0.122, 2.000), (0.124, 1.76)]
    th = 0.011
    sec = [(d - th, y) for d, y in path] + [(d + th, y) for d, y in path[::-1]]
    cloth_rings = []
    for k, s in enumerate((sc - 0.12, sc - 0.04, sc + 0.04, sc + 0.12)):
        drop = (0.0, 0.025, 0.010, 0.035)[k]
        cloth_rings.append([(s, y - (drop if y < 1.9 else 0.0), d) for d, y in sec])
    fab.loft(cloth_rings, "cloth")
    # patched shoulder bag on the third hook
    bs0, bs1 = -0.355, -0.045
    bag = [(bs0, 1.585, 0.026), (bs1, 1.585, 0.026), (bs1, 1.585, 0.098), (bs0, 1.585, 0.098),
           (bs0 + 0.01, 1.815, 0.026), (bs1 - 0.01, 1.815, 0.026), (bs1 - 0.01, 1.815, 0.080), (bs0 + 0.01, 1.815, 0.080)]
    fab.proj(bag, BOX_F, "bag")
    fab.proj([(bs0 - 0.004, 1.66, 0.096), (bs1 + 0.004, 1.66, 0.096), (bs1 + 0.004, 1.65, 0.106), (bs0 - 0.004, 1.65, 0.106),
              (bs0 + 0.006, 1.825, 0.078), (bs1 - 0.006, 1.825, 0.078), (bs1 - 0.006, 1.83, 0.088), (bs0 + 0.006, 1.83, 0.088)],
             BOX_F, "bag")                                                 # flap
    fab.plate(-0.235, 1.72, 0.09, 0.07, 0.100, 0.106, 6, "patch")           # sewn repair patch on the flap
    for sx in (bs0 + 0.02, bs1 - 0.02):
        fab.tube([(sx, 1.80, 0.060), (sx + (-0.20 - sx) * 0.5, 1.95, 0.090), (-0.20, 2.062, 0.104)], 0.012, 0.004, 4, "strap")
    met.box((-0.215, 1.655, 0.104), (-0.185, 1.685, 0.112), "steel")       # buckle
    # taped notes on the centre panel
    for (cs, cy, w, h, ang, reg) in ((0.20, 1.76, 0.17, 0.21, 4, "note0"), (0.25, 2.00, 0.15, 0.13, -6, "note2")):
        pap.plate(cs, cy, w, h, 0.025, 0.0265, ang, reg)
        for dx in (-1, 1):
            pap.plate(cs + dx * w * 0.42, cy + h * 0.48, 0.05, 0.016, 0.0265, 0.028, ang + dx * 35, "tape")
    return [("UCC_SE_Fabric", fab, "M_UCC_Fabric", "worn coat, folded cloth and patched shoulder bag on the entrance hook rail"),
            ("UCC_SE_Metal", met, "M_UCC_PaintedMetal", "hook rail, hooks, bolts, bag buckle"),
            ("UCC_SE_Paper", pap, "M_UCC_Paper", "taped notes (abstract marks)")]


def canister(mb, s, r, y0, h, region, cd=None):
    cd = 0.044 + r if cd is None else cd
    prof = [(y0, r * 0.92), (y0 + 0.012, r), (y0 + h, r), (y0 + h + 0.035, r * 0.55), (y0 + h + 0.05, r * 0.32),
            (y0 + h + 0.05, 0.016), (y0 + h + 0.085, 0.016)]
    mb.lathe(s, cd, prof, 10, region, phase=math.pi / 10)
    return cd


def strap_band(mb, s, cd, r, y):
    mb.lathe(s, cd, [(y - 0.012, r + 0.005), (y + 0.012, r + 0.005)], 10, "steel", phase=math.pi / 10)


def build_sw():
    """SW wall (west of the door, above the limb shelf corner): strapped rack of reused empty canisters; taped notes"""
    met, pap = MB("SW"), MB("SW")
    met.box((-1.21, 1.85, 0.025), (-0.73, 1.89, 0.040), "green")
    met.box((-1.21, 1.645, 0.025), (-0.73, 1.685, 0.040), "green")
    for s in (-1.19, -0.75):
        met.box((s - 0.012, 1.62, 0.025), (s + 0.012, 1.91, 0.034), "steel")
    for (s, r, h, reg) in ((-1.105, 0.054, 0.29, "ivory"), (-0.975, 0.054, 0.26, "green"), (-0.85, 0.050, 0.30, "ivory")):
        cd = canister(met, s, r, 1.585, h, reg)
        strap_band(met, s, cd, r, 1.665)
        strap_band(met, s, cd, r, 1.870)
    pap.plate(-0.975, 1.76, 0.06, 0.05, 0.042 + 2 * 0.054 - 0.004, 0.042 + 2 * 0.054 + 0.002, 0, "label")   # chalked tag, front
    pap.plate(-1.105, 1.78, 0.05, 0.06, 0.042 + 2 * 0.054 - 0.004, 0.042 + 2 * 0.054 + 0.002, -8, "tape")
    for (cs, cy, w, h, ang, reg) in ((0.93, 1.98, 0.17, 0.22, 3, "note1"), (1.12, 1.92, 0.15, 0.19, -5, "note3"),
                                     (0.96, 1.71, 0.19, 0.14, -2, "note4")):
        pap.plate(cs, cy, w, h, 0.025, 0.0265, ang, reg)
        pap.plate(cs, cy + h * 0.48, 0.06, 0.016, 0.0265, 0.028, ang + 3, "tape")
    return [("UCC_SW_Metal", met, "M_UCC_PaintedMetal", "strap rack with three reused empty canisters"),
            ("UCC_SW_Paper", pap, "M_UCC_Paper", "taped notes and canister tags")]


def junction(met, cab, pap, s0, s1, y0, y1, glands, low_route):
    """wall junction box with a bundled cable drop up to under the trim rail and one taped splice run"""
    met.box((s0, y0, 0.025), (s1, y1, 0.078), "green")
    met.box((s0 + 0.008, y0 + 0.008, 0.078), (s1 - 0.008, y1 - 0.008, 0.084), "green")      # lid
    for (a, b) in ((s0 + 0.016, y0 + 0.016), (s1 - 0.016, y0 + 0.016), (s0 + 0.016, y1 - 0.016), (s1 - 0.016, y1 - 0.016)):
        met.box((a - 0.005, b - 0.005, 0.084), (a + 0.005, b + 0.005, 0.088), "steel")
    pap.plate((s0 + s1) / 2, (y0 + y1) / 2, (s1 - s0) * 0.6, 0.035, 0.084, 0.0855, 0, "label")
    mid = sum(glands) / len(glands)
    for i, gs in enumerate(glands):
        met.lathe(gs, 0.045, [(y1, 0.011), (y1 + 0.02, 0.011)], 6, "steel")
        ts = mid + (gs - mid) * 0.45
        cab.tube([(gs, y1 + 0.02, 0.045), (gs, y1 + 0.05, 0.045), (ts, y1 + 0.10, 0.042 + 0.006 * (i % 2)), (ts, Y_TOP - 0.008, 0.040)],
                 0.0075, 0.0075, 5, "cab%d" % (i % 4))
    for y in (y1 + 0.11, Y_TOP - 0.06):
        span = (max(glands) - min(glands)) * 0.45 / 2 + 0.014
        met.box((mid - span, y - 0.006, 0.030), (mid + span, y + 0.006, 0.058), "steel")   # strap wrap round the bundle
    cab.tube(low_route, 0.008, 0.008, 5, "cab1")
    a, b = Vector(low_route[len(low_route) // 2 - 1]), Vector(low_route[len(low_route) // 2])
    c = (a + b) / 2
    dv = (b - a).normalized()
    pap.tube([c - dv * 0.035, c - dv * 0.02, c + dv * 0.02, c + dv * 0.035], 0.013, 0.013, 6, "tape")   # taped splice


def build_nw():
    """NW wall (dock side, north): junction above the red tool chest, bundled drop, splice run along y 1.72"""
    met, cab, pap = MB("NW"), MB("NW"), MB("NW")
    low = [(0.66, 1.78, 0.044)] + bez((0.66, 1.75, 0.044), (0.67, 1.722, 0.042), (0.74, 1.722, 0.040), 3)[1:] + \
          [(0.90, 1.722, 0.040), (1.02, 1.722, 0.040)] + bez((1.10, 1.722, 0.040), (1.16, 1.722, 0.040), (1.16, 1.80, 0.040), 3) + \
          [(1.16, Y_TOP - 0.008, 0.040)]
    junction(met, cab, pap, 0.50, 0.70, 1.78, 1.98, (0.54, 0.58, 0.62, 0.66), low)
    pap.plate(0.30, 1.86, 0.15, 0.19, 0.025, 0.0265, 5, "note2")
    pap.plate(0.30, 1.86 + 0.19 * 0.48, 0.06, 0.016, 0.0265, 0.028, 8, "tape")
    return [("UCC_NW_Metal", met, "M_UCC_PaintedMetal", "cable junction box, glands, bundle wraps"),
            ("UCC_NW_Cable", cab, "M_UCC_Cable", "bundled cable drop to the trim rail + splice run"),
            ("UCC_NW_Paper", pap, "M_UCC_Paper", "box label, splice tape, taped note")]


def build_ne():
    """NE wall (trade side, north): junction on the centre panel, one strapped reused canister, splice run, note"""
    met, cab, pap = MB("NE"), MB("NE"), MB("NE")
    low = [(-0.29, 1.84, 0.044)] + bez((-0.29, 1.80, 0.044), (-0.30, 1.752, 0.042), (-0.37, 1.752, 0.040), 3)[1:] + \
          [(-0.45, 1.752, 0.040), (-0.53, 1.752, 0.040)] + bez((-0.57, 1.752, 0.040), (-0.625, 1.752, 0.040), (-0.625, 1.82, 0.040), 3) + \
          [(-0.625, Y_TOP - 0.008, 0.040)]
    junction(met, cab, pap, -0.32, -0.14, 1.84, 2.00, (-0.29, -0.25, -0.21, -0.17), low)
    cd = canister(met, 0.08, 0.045, 1.76, 0.26, "green")
    strap_band(met, 0.08, cd, 0.045, 1.83)
    met.box((0.04, 1.815, 0.025), (0.12, 1.845, 0.040), "steel")
    pap.plate(0.08, 1.93, 0.05, 0.045, 0.044 + 2 * 0.045 - 0.004, 0.044 + 2 * 0.045 + 0.002, 0, "tag")
    pap.plate(-1.06, 1.93, 0.15, 0.20, 0.025, 0.0265, -4, "note0")
    pap.plate(-1.06, 1.93 + 0.20 * 0.48, 0.06, 0.016, 0.0265, 0.028, -1, "tape")
    return [("UCC_NE_Metal", met, "M_UCC_PaintedMetal", "cable junction box, wraps, strapped reused canister"),
            ("UCC_NE_Cable", cab, "M_UCC_Cable", "bundled cable drop to the trim rail + splice run"),
            ("UCC_NE_Paper", pap, "M_UCC_Paper", "box label, splice tape, canister tag, taped note")]


# ---------------------------------------------------------------------------
# Measurements / assertions
# ---------------------------------------------------------------------------
def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def unity_pts(o):
    mw = o.matrix_world
    return [bu(mw @ v.co) for v in o.data.vertices]


def bounds(P):
    return [r3([min(p[i] for p in P) for i in range(3)]), r3([max(p[i] for p in P) for i in range(3)])]


def placement_checks(objs):
    rows, fails = [], []
    for o in objs:
        w = o["ucc_wall"]
        P = unity_pts(o)
        L = [to_wall(w, p) for p in P]
        s_rng = (min(l[0] for l in L), max(l[0] for l in L))
        y_rng = (min(l[1] for l in L), max(l[1] for l in L))
        d_rng = (min(l[2] for l in L), max(l[2] for l in L))
        low = [l for l in L if l[1] < STRIP_Y[w]]
        low_d = max((l[2] for l in low), default=0.0)
        r = dict(name=o.name, wall=w, triangles=tri_count(o), material=o.data.materials[0].name,
                 unity_bounds=bounds(P), wall_local=dict(s=r3(s_rng), y=r3(y_rng), d=r3(d_rng)),
                 strip_y=STRIP_Y[w], max_depth_below_strip=round(low_d, 4), verts_below_strip=len(low))
        if d_rng[0] < PANEL_D - 1e-4 or d_rng[1] > MAX_DEPTH:
            fails.append("%s depth %s outside [%.3f, %.2f]" % (o.name, r3(d_rng), PANEL_D, MAX_DEPTH))
        if low_d > LOW_DEPTH + 1e-4:
            fails.append("%s depth %.3f below strip y %.2f" % (o.name, low_d, STRIP_Y[w]))
        if abs(s_rng[0]) > S_LIMIT or abs(s_rng[1]) > S_LIMIT:
            fails.append("%s s %s beyond panel area" % (o.name, r3(s_rng)))
        if y_rng[1] > Y_TOP + 1e-4:
            fails.append("%s top y %.3f above trim rail clearance" % (o.name, y_rng[1]))
        zone_hits = []
        for zn, (x0, x1, z0, z1) in ZONES.items():
            if any(x0 - 0.05 <= p.x <= x1 + 0.05 and z0 - 0.05 <= p.z <= z1 + 0.05 and 0.03 <= p.y <= 1.97 for p in P):
                zone_hits.append(zn)
        for zn, (x0, x1, z0, z1) in FORBID_ALL_Y.items():
            if any(x0 <= p.x <= x1 and z0 <= p.z <= z1 for p in P):
                zone_hits.append(zn)
        for an, (lo, hi) in ASSET_BOUNDS.items():
            if any(all(lo[i] - 0.15 <= p[i] <= hi[i] + 0.15 for i in range(3)) for p in P):
                zone_hits.append(an + "_bounds+0.15")
        if zone_hits:
            fails.append("%s enters %s" % (o.name, zone_hits))
        r["zone_hits"] = zone_hits
        pd = {}
        for pn, pl in PATHS.items():
            pd[pn] = round(min(seg_dist((p.x, p.z), pl[i], pl[i + 1]) for p in P for i in range(len(pl) - 1)), 3)
            if pd[pn] < PATH_HALF[pn] + 0.10:
                fails.append("%s within %.2f m of %s centreline" % (o.name, pd[pn], pn))
        r["min_dist_to_path_centrelines_m"] = pd
        rows.append(r)
    return rows, fails


def file_sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def export_fbx(path, objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)


# ---------------------------------------------------------------------------
# Phase C helpers: base scene (read-only) + renders
# ---------------------------------------------------------------------------
def world_bvh(o, dg):
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    mw = o.matrix_world
    V = [mw @ v.co for v in me.vertices]
    F = [list(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return BVHTree.FromPolygons(V, F) if F else None, V


def base_checks(mine):
    dg = bpy.context.evaluated_depsgraph_get()
    base = [o for o in bpy.data.objects if o.type == "MESH" and o not in mine and o.visible_get()
            and not o.name.startswith(("OV_", "RC_"))]
    trees = {}
    for o in base:
        t, V = world_bvh(o, dg)
        if t:
            trees[o.name] = (t, V)
    out, fails = {}, []
    for o in mine:
        t, V = world_bvh(o, dg)
        hits, gap, gap_obj = [], 1e9, None
        for bn, (bt, BV) in trees.items():
            if t.overlap(bt):
                hits.append(bn)
        for v in V:
            for bn, (bt, BV) in trees.items():
                loc, nrm, idx, dist = bt.find_nearest(v, 0.5)
                if loc is not None and dist < gap:
                    gap, gap_obj = dist, bn
        out[o.name] = dict(intersects=hits, min_vertex_gap_m=round(gap, 4), nearest_base_object=gap_obj)
        if hits:
            fails.append("%s intersects base %s" % (o.name, hits))
    return out, fails, len(trees)


def look_cam(name, pos_u, tgt_u, lens):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cd.clip_start = 0.05
    cam = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = ub(pos_u)
    d = ub(tgt_u) - ub(pos_u)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return cam


def render_views():
    sc = bpy.context.scene
    for f in os.listdir(D_RENDER):   # only this script writes here; drop stale views
        if f.startswith("UCC_") and f.endswith(".png"):
            os.remove(os.path.join(D_RENDER, f))
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    for o in bpy.data.objects:   # base check overlays are not part of the room look
        if o.name.startswith("OV_"):
            o.hide_render = True
    ld = bpy.data.lights.new("RCC_DetailFill", "AREA")
    ld.energy, ld.size = 35.0, 0.8
    ld.color = (1.0, 0.86, 0.68)
    lo = bpy.data.objects.new("RCC_DetailFill", ld)
    sc.collection.objects.link(lo)
    lo.location = ub((1.0, 2.2, -1.6))
    lo.rotation_euler = (ub((2.0, 1.7, -2.6)) - ub((1.0, 2.2, -1.6))).to_track_quat("-Z", "Y").to_euler()
    # overview looks north from the entrance side (the central gantry blocks any high cross-room view of the south walls);
    # the SE wall detail is the required close-up, the SW view is a supplementary check of the canister rack / notes
    views = [("UCC_Overview", (0.0, 1.95, -1.6), (0.0, 1.75, 2.4), 12),
             ("UCC_WallDetail_SE", (0.62, 1.72, -1.12), (1.82, 1.78, -2.72), 20),
             ("UCC_Supplementary_SW", (-1.05, 1.75, -1.0), (-2.4, 1.8, -2.4), 16)]
    out = []
    for nm, p, t, lens in views:
        sc.camera = look_cam("RCC_" + nm, p, t, lens)
        sc.render.filepath = os.path.join(D_RENDER, nm + ".png")
        bpy.ops.render.render(write_still=True)
        out.append(dict(file="Renders/%s.png" % nm, camera_unity=list(p), look_at_unity=list(t), lens_mm=lens,
                        exists=os.path.isfile(sc.render.filepath)))
    return out


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    global ROOT
    report = dict(blender=bpy.app.version_string, base_blend=os.path.relpath(BASE_BLEND, HERE))
    sha_before = file_sha(BASE_BLEND)
    base_mtime = os.path.getmtime(BASE_BLEND)

    # ---- phase A: build ----
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    ROOT = bpy.data.objects.new(ROOT_NAME, None)
    ROOT.empty_display_size = 0.3
    bpy.context.scene.collection.objects.link(ROOT)
    ROOT["ucc_note"] = "add-on root; place at Unity origin, keep imported rotation (Euler -90,0,0) like UnifiedClinic_Environment"
    ROOT["ucc_contract"] = "u = (-bx, bz, -by)"
    for k in ("A", "P"):
        save_png(k)
    build_materials()
    objs = []
    for spec in build_se() + build_sw() + build_nw() + build_ne():
        objs.append(make(*spec))
    bpy.context.view_layer.update()

    rows, fails = placement_checks(objs)
    tris = sum(r["triangles"] for r in rows)
    mats = sorted({r["material"] for r in rows})
    imgs = sorted({n.image.name for m in MATS.values() for n in m.node_tree.nodes if n.type == "TEX_IMAGE"})
    counts = dict(triangles=tris, mesh_objects=len(objs), materials=len(mats), textures=len(imgs))
    for k, lim in BUDGET.items():
        if counts[k] > lim:
            fails.append("budget %s %d > %d" % (k, counts[k], lim))
    nonexport = [o.name for o in bpy.data.objects if o.type not in ("MESH", "EMPTY")]
    if nonexport:
        fails.append("non-mesh objects in add-on scene: %s" % nonexport)
    report.update(counts=counts, budget=BUDGET, materials=mats,
                  textures={ATLAS[k][0]: dict(size=[ATLAS[k][1]] * 2, bytes=os.path.getsize(os.path.join(D_TEX, ATLAS[k][0])))
                            for k in ATLAS},
                  per_object=rows, strip_rules=dict(strip_y=STRIP_Y, low_max_depth=LOW_DEPTH, max_depth=MAX_DEPTH,
                                                    s_limit=S_LIMIT, y_top=Y_TOP, min_depth=PANEL_D))
    tri_per_mat = {}
    for r in rows:
        tri_per_mat[r["material"]] = tri_per_mat.get(r["material"], 0) + r["triangles"]
    report["triangles_per_material"] = tri_per_mat

    export_fbx(OUT_FBX, [ROOT] + objs)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)
    authored = {r["name"]: r["unity_bounds"] for r in rows}

    # ---- phase B: FBX round trip ----
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=OUT_FBX, axis_forward="-Z", axis_up="Y")
    worst, rt = 0.0, {}
    imported = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
    for nm, bb in authored.items():
        o = imported.get(nm)
        if o is None:
            rt[nm] = "missing"
            worst = 1e9
            continue
        got = bounds(unity_pts(o))
        err = max(abs(got[a][i] - bb[a][i]) for a in (0, 1) for i in range(3))
        worst = max(worst, err)
        rt[nm] = round(err, 6)
    root_imp = bpy.data.objects.get(ROOT_NAME)
    report["fbx_roundtrip"] = dict(max_bounds_error_m=round(worst, 6), passed=worst < 1e-3, per_object=rt,
                                   imported_mesh_objects=len(imported),
                                   imported_object_types=sorted({o.type for o in bpy.data.objects}),
                                   root_found=root_imp is not None,
                                   note="Blender re-import only; Unity import not run")
    if worst >= 1e-3:
        fails.append("fbx round trip bounds error %.4f m" % worst)
    if set(o.type for o in bpy.data.objects) - {"MESH", "EMPTY"}:
        fails.append("FBX contains non-mesh/empty objects")

    # ---- phase C: base scene read-only in memory, append add-on, checks + renders ----
    bpy.ops.wm.open_mainfile(filepath=BASE_BLEND, load_ui=False)
    with bpy.data.libraries.load(OUT_BLEND, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(("UCC_", ROOT_NAME))]
    mine = []
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
        if o.type == "MESH":
            mine.append(o)
    bpy.context.view_layer.update()
    bc, bfails, nbase = base_checks(mine)
    fails += bfails
    report["vs_base_meshes"] = dict(base_mesh_objects_tested=nbase, per_object=bc,
                                    note="BVH triangle overlap + nearest-vertex gap vs every visible base mesh incl. render placeholders "
                                         "PH_* for dock/robot/bench; OV_* check overlays excluded")
    renders = render_views() if DO_RENDER else []
    report["renders"] = renders
    report["render_note"] = "base UnifiedClinic.blend opened in memory and never saved; RCC_* cameras/light exist only in that session"

    sha_after = file_sha(BASE_BLEND)
    report["base_blend_unchanged"] = dict(sha256=sha_before, unchanged=(sha_after == sha_before and os.path.getmtime(BASE_BLEND) == base_mtime))
    if sha_after != sha_before:
        fails.append("BASE BLEND CHANGED")
    report["failures"] = fails
    report["passed_all"] = not fails
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(dict((k, report[k]) for k in ("blender", "counts", "budget", "materials", "textures", "triangles_per_material",
                                                  "fbx_roundtrip", "base_blend_unchanged", "renders", "passed_all", "failures")), f, indent=1)
    with open(os.path.join(HERE, "placement_bounds.json"), "w", encoding="utf-8") as f:
        json.dump(dict(coordinate_contract=dict(unity_axes="+Y up, +Z north, +X east, metres", blender_to_unity="u = (-bx, bz, -by)",
                                                fbx_export="axis_forward=-Z, axis_up=Y, bake_space_transform=False, FBX_SCALE_UNITS",
                                                placement="instantiate at Unity (0,0,0), keep imported root rotation (Euler -90,0,0)",
                                                wall_local="s along wall, y height, d off the inner wall face (apothem 3.40)"),
                       strip_rules=report["strip_rules"], objects=report["per_object"], vs_base_meshes=report["vs_base_meshes"]),
                  f, indent=1)
    print("UCC_SUMMARY", json.dumps(dict(counts=counts, roundtrip=report["fbx_roundtrip"]["passed"], renders=[r["exists"] for r in renders],
                                         base_unchanged=report["base_blend_unchanged"]["unchanged"], failures=fails)))
    if fails:
        raise SystemExit(1)


main()
