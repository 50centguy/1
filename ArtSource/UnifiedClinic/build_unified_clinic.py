"""
Unified clinic room (V1 layout + V4 trade placement) - deterministic Blender build script (Blender 5.2).

Run (from anywhere):
    blender -b --factory-startup --python ArtSource/UnifiedClinic/build_unified_clinic.py -- [norender]

Everything is generated from this file: geometry, procedural textures, materials, collision proxies,
anchors, geometric checks, stats, FBX exports, renders and the .blend.

COORDINATE CONTRACT
    All layout constants below are written in UNITY space: +Y up, +Z north (forward), +X east, metres.
    Blender scene coordinates are b = (-ux, -uz, uy)  <=>  u = (-bx, bz, -by).
    The FBX is exported exactly like WorkbenchArea / Unit07ServiceDock (axis_forward=-Z, axis_up=Y, no bake),
    for which Unity was previously measured to give u = (-bx, bz, -by) with the root left at its imported
    rotation (Euler -90,0,0). Place the UnifiedClinic root at Unity (0,0,0) without touching that rotation.

Existing assets (WorkbenchArea, UNIT07_ServiceDock, RobotV4) are NOT modelled or exported here. Their real FBX
exports are imported read-only into memory for clearance checks, then deleted before the .blend is saved.
"""

import csv
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(HERE)
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "norender" not in ARGS
D_EXPORT, D_TEX, D_RENDER, D_REPORT = [os.path.join(HERE, d) for d in ("Export", "Textures", "Renders", "Reports")]
for _d in (D_EXPORT, D_TEX, D_RENDER, D_REPORT):
    os.makedirs(_d, exist_ok=True)
EXISTING_FBX = {
    "dock": os.path.join(ART, "Unit07ServiceDock", "Export", "UNIT07_ServiceDock.fbx"),
    "robot": os.path.join(ART, "Unit07ServiceDock", "Export", "UNIT07_RobotPlaceholder.fbx"),
    "bench": os.path.join(ART, "WorkbenchArea", "Export", "WorkbenchArea.fbx"),
}

# ---------------------------------------------------------------------------
# Unity <-> Blender
# ---------------------------------------------------------------------------
C_BU = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))      # unity = C_BU @ blender
C_UB = C_BU.inverted()


def ub(p):
    return Vector((-p[0], -p[2], p[1]))


def bu(v):
    return Vector((-v[0], v[2], -v[1]))


def rot_y(p, deg):
    """Unity yaw: +deg turns +Z toward +X."""
    t = math.radians(deg)
    c, s = math.cos(t), math.sin(t)
    return Vector((p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c))


def yaw_of(dx, dz):
    return math.degrees(math.atan2(dx, dz))


def unity_placement_matrix(T, yaw):
    """Blender-space matrix for an existing FBX asset (native Blender coords, imported with -Z/Y) placed in
    Unity at position T with root rotation Euler(0,yaw,0) * importRotation."""
    t = math.radians(yaw)
    c, s = math.cos(t), math.sin(t)
    R = Matrix(((c, 0, s), (0, 1, 0), (-s, 0, c)))
    M = (C_UB @ R @ C_BU).to_4x4()
    M.translation = C_UB @ Vector(T)
    return M


def r3(v):
    return [round(float(x), 4) for x in v]


# ---------------------------------------------------------------------------
# Layout (Unity space). Room size derives from: bench depth 1.05 m to wall (WorkbenchArea), dock + robot +
# disassembly corridors (Unit07ServiceDock), a 1.90 m bed, and >= 0.9 m aisles on both sides of the bed.
# ---------------------------------------------------------------------------
A = 3.40                      # octagon apothem (centre -> inner wall face)
H = 3.00                      # ceiling height
WT = 0.20                     # wall thickness
TAN = math.tan(math.radians(22.5))
HALF_SIDE = A * TAN           # 1.408
RC = A / math.cos(math.radians(22.5))
SIDES = ["E", "NE", "N", "NW", "W", "SW", "S", "SE"]   # side k faces angle 45k (from +X toward +Z)
DOOR_CLEAR = 1.20
DOOR_H = 2.20
DOOR_POST = 0.15

BED_C = Vector((-0.10, 0.0, -0.20))
BED_HALF_L, BED_HALF_W = 0.95, 0.36
BED_TOP = 0.85                # pad top; head cushion reaches 0.90
BED_TOP_MAX = 0.90
RING_R, RING_Y = 0.95, 2.45
HEAD_CLEAR = 2.00             # nothing outside the bed footprint may hang below this

BENCH_T, BENCH_YAW = (0.0, 0.0, A - 1.05), 180.0         # WorkbenchArea root (its back wall plane = 1.05)
DOCK_T, DOCK_YAW = (-2.73, 0.0, 0.0), 90.0                # UNIT07_ServiceDock root; robot faces +X (east)

CNT_X0, CNT_XM, CNT_X1 = 1.62, 1.90, 2.18                 # counter body front / centre line / operator edge
CNT_TOP_X0, CNT_TOP_X1 = 1.58, 2.22
CNT_Z0, CNT_Z1 = -1.00, 0.95
CNT_TOP = 0.95
ANGLED_LEN = 1.25
SHELF_X0 = 3.02
STOOL_P = Vector((2.52, 0.0, 0.0))
PAD_RECV_C = Vector((1.84, CNT_TOP, -0.60))
PAD_DELV_C = Vector((1.84, CNT_TOP, 0.60))
PAD_SIZE = (0.40, 0.46)       # x, z

ZONES = {   # reserved floor zones (x0, x1, z0, z1): kept free of environment geometry, not part of aisles
    "dock_work": (-2.39, -1.65, -0.95, 0.95),
    "dock_side_access_L": (-3.00, -2.30, 0.95, 1.30),
    "dock_side_access_R": (-3.00, -2.30, -1.30, -0.95),
    "bench_stand": (-0.96, 0.96, 1.885, 2.485),
    "trade_customer_strip": (1.22, 1.555, -1.00, 0.95),
    "trade_operator": (2.235, 3.00, -0.95, 0.95),
}
PATHS = {
    "principal_entrance_west_bench": [(0, -3.30), (0, -2.45), (-1.05, -1.75), (-1.08, 0.0), (-1.00, 1.30), (0.0, 1.55)],
    "east_entrance_trade": [(0, -2.45), (0.77, -1.75), (0.77, 0.0), (0.85, 1.35), (1.40, 1.45)],
    "north_cross": [(-1.00, 1.30), (0.0, 1.45), (0.85, 1.40), (1.40, 1.45)],
    "trade_operator_entry": [(1.40, 1.45), (2.45, 1.35), (2.58, 0.60), (2.55, 0.25)],
}
PATH_REQ = {"principal_entrance_west_bench": 0.90, "east_entrance_trade": 0.90, "north_cross": 0.90, "trade_operator_entry": 0.70}

# ---------------------------------------------------------------------------
# Procedural textures (numpy, sRGB values, row 0 = top)
# ---------------------------------------------------------------------------
RNG = np.random.default_rng(20261008)

FONT = {
    "A": "01110 10001 10001 11111 10001 10001 10001", "B": "11110 10001 10001 11110 10001 10001 11110",
    "C": "01110 10001 10000 10000 10000 10001 01110", "D": "11110 10001 10001 10001 10001 10001 11110",
    "E": "11111 10000 10000 11110 10000 10000 11111", "F": "11111 10000 10000 11110 10000 10000 10000",
    "G": "01110 10001 10000 10111 10001 10001 01111", "H": "10001 10001 10001 11111 10001 10001 10001",
    "I": "01110 00100 00100 00100 00100 00100 01110", "J": "00111 00010 00010 00010 00010 10010 01100",
    "K": "10001 10010 10100 11000 10100 10010 10001", "L": "10000 10000 10000 10000 10000 10000 11111",
    "M": "10001 11011 10101 10101 10001 10001 10001", "N": "10001 11001 10101 10011 10001 10001 10001",
    "O": "01110 10001 10001 10001 10001 10001 01110", "P": "11110 10001 10001 11110 10000 10000 10000",
    "Q": "01110 10001 10001 10001 10101 10010 01101", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 10000 01110 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    "U": "10001 10001 10001 10001 10001 10001 01110", "V": "10001 10001 10001 10001 10001 01010 00100",
    "W": "10001 10001 10001 10101 10101 10101 01010", "X": "10001 10001 01010 00100 01010 10001 10001",
    "Y": "10001 10001 01010 00100 00100 00100 00100", "Z": "11111 00001 00010 00100 01000 10000 11111",
    "0": "01110 10001 10011 10101 11001 10001 01110", "1": "00100 01100 00100 00100 00100 00100 01110",
    "2": "01110 10001 00001 00010 00100 01000 11111", "3": "11110 00001 00001 01110 00001 00001 11110",
    "4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
    "6": "00110 01000 10000 11110 10001 10001 01110", "7": "11111 00001 00010 00100 01000 01000 01000",
    "8": "01110 10001 10001 01110 10001 10001 01110", "9": "01110 10001 10001 01111 00001 00010 01100",
    "-": "00000 00000 00000 11111 00000 00000 00000", ".": "00000 00000 00000 00000 00000 01100 01100",
    ":": "00000 01100 01100 00000 01100 01100 00000", "/": "00001 00010 00010 00100 01000 01000 10000",
    ">": "01000 00100 00010 00001 00010 00100 01000", "#": "01010 11111 01010 01010 01010 11111 01010",
    " ": "00000 00000 00000 00000 00000 00000 00000",
}


def text(img, s, x, y, sc, col):
    for ch in s:
        rows = FONT.get(ch, FONT[" "]).split()
        for gy, row in enumerate(rows):
            for gx, bit in enumerate(row):
                if bit == "1":
                    img[y + gy * sc:y + (gy + 1) * sc, x + gx * sc:x + (gx + 1) * sc] = col
        x += 6 * sc


def noise(size, sigma, shape=None):
    h, w = shape if shape else (size, size)
    white = RNG.standard_normal((h, w))
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * math.pi ** 2 * sigma ** 2 * (fx ** 2 + fy ** 2))
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * k))
    out -= out.min()
    return out / max(out.max(), 1e-6)


def posterize(img, levels=28):
    return np.clip(np.round(img * levels) / levels, 0, 1)


def tex_worn(n=256):
    low, mid, fine = noise(n, 22), noise(n, 5), RNG.random((n, n))
    v = 0.80 + 0.12 * (low - 0.5) + 0.06 * (mid - 0.5) + 0.04 * (fine - 0.5)
    img = np.stack([v, v, v], -1)
    grime = np.clip((noise(n, 10) - 0.62) * 3.0, 0, 1)[..., None]
    img = img * (1 - 0.45 * grime)
    rust = 0.65 * np.clip((noise(n, 4) * low - 0.50) * 5.0, 0, 1)[..., None]   # sparse, desaturated: no brown-dominant palette
    img = img * (1 - rust) + rust * np.array([0.40, 0.29, 0.21])
    for _ in range(140):          # scratches: lighter bare metal
        x0, y0 = RNG.integers(0, n, 2)
        ang = RNG.uniform(0, math.pi)
        ln = RNG.integers(4, 26)
        for i in range(ln):
            x = int(x0 + math.cos(ang) * i) % n
            y = int(y0 + math.sin(ang) * i) % n
            img[y, x] = np.minimum(img[y, x] + 0.16, 1.0)
    for _ in range(60):           # paint chips: dark
        x0, y0 = RNG.integers(0, n, 2)
        r = RNG.integers(1, 4)
        img[max(0, y0 - r):y0 + r, max(0, x0 - r):x0 + r] *= 0.55
    return posterize(img)


def tex_floor(n=256):
    """1.2 m x 1.2 m: 2 x 2 plates of 0.6 m, green-grey, rust, scuffs, rivets."""
    base = np.array([0.33, 0.39, 0.35])
    img = np.ones((n, n, 3)) * base
    p = n // 2
    for i in range(2):
        for j in range(2):
            img[i * p:(i + 1) * p, j * p:(j + 1) * p] *= 0.93 + 0.12 * RNG.random()
    img *= (0.86 + 0.22 * noise(n, 14))[..., None]
    img *= (0.94 + 0.10 * RNG.random((n, n)))[..., None]
    rust = np.clip((noise(n, 9) - 0.60) * 4.0, 0, 1)[..., None] * np.clip((noise(n, 3) - 0.3) * 2, 0, 1)[..., None]
    img = img * (1 - rust) + rust * np.array([0.47, 0.27, 0.14])
    oil = np.clip((noise(n, 18) - 0.70) * 4.0, 0, 1)[..., None]
    img *= 1 - 0.35 * oil
    for k in (0, p):
        img[k:k + 2, :] = img[k:k + 2, :] * 0.35
        img[:, k:k + 2] = img[:, k:k + 2] * 0.35
        img[(k + 2) % n, :] = np.minimum(img[(k + 2) % n, :] * 1.25, 1)
        img[:, (k + 2) % n] = np.minimum(img[:, (k + 2) % n] * 1.25, 1)
    for i in range(2):
        for j in range(2):
            for dy in (8, p - 9):
                for dx in (8, p - 9):
                    y, x = i * p + dy, j * p + dx
                    img[y - 1:y + 2, x - 1:x + 2] = [0.58, 0.60, 0.55]
                    img[y + 1, x - 1:x + 2] = [0.16, 0.18, 0.17]
    for _ in range(90):
        x0, y0 = RNG.integers(0, n, 2)
        ln = RNG.integers(6, 30)
        for t in range(ln):
            img[y0 % n, (x0 + t) % n] = np.minimum(img[y0 % n, (x0 + t) % n] + 0.07, 1)
    return posterize(img, 24)


def tex_wall(n=256):
    """1.2 m x 1.2 m: vertical seams every 0.6 m, one horizontal seam, rivet rows, drip streaks."""
    base = np.array([0.20, 0.23, 0.215])
    img = np.ones((n, n, 3)) * base
    img *= (0.85 + 0.28 * noise(n, 16))[..., None]
    img *= (0.93 + 0.12 * RNG.random((n, n)))[..., None]
    p = n // 2
    streak = np.zeros((n, n))
    for _ in range(70):
        x = RNG.integers(0, n)
        y0 = RNG.integers(0, n)
        ln = RNG.integers(20, 120)
        a = RNG.uniform(0.15, 0.45)
        for t in range(ln):
            streak[(y0 + t) % n, x] = max(streak[(y0 + t) % n, x], a * (1 - t / ln))
    img *= (1 - streak)[..., None]
    rust = np.clip((noise(n, 6) - 0.66) * 5.0, 0, 1)[..., None]
    img = img * (1 - rust) + rust * np.array([0.40, 0.24, 0.14])
    for k in (0, p):
        img[:, k:k + 2] *= 0.35
        img[k:k + 2, :] *= 0.40
        img[:, (k + 2) % n] = np.minimum(img[:, (k + 2) % n] * 1.3, 1)
    for k in (0, p):
        for y in range(6, n, 16):
            for x in (k + 6, (k + p - 7) % n):
                img[y:y + 2, x:x + 2] = [0.42, 0.44, 0.40]
        for x in range(6, n, 16):
            for y in (k + 6, (k + p - 7) % n):
                img[y:y + 2, x:x + 2] = [0.42, 0.44, 0.40]
    return posterize(img, 24)


def tex_hazard(w=128, h=32):
    img = np.zeros((h, w, 3))
    yy, xx = np.mgrid[0:h, 0:w]
    band = ((xx + yy) // 16) % 2 == 0
    img[band] = [0.78, 0.60, 0.13]
    img[~band] = [0.07, 0.07, 0.06]
    wear = noise(0, 3, (h, w)) * RNG.random((h, w))
    m = wear > 0.55
    img[m] = img[m] * 0.4 + np.array([0.35, 0.35, 0.33]) * 0.6
    img *= (0.85 + 0.2 * RNG.random((h, w)))[..., None]
    return posterize(img, 20)


def tex_grate(n=64):
    img = np.ones((n, n, 3)) * 0.05
    for k in range(0, n, 8):
        img[:, k:k + 2] = 0.30
        img[k:k + 2, :] = 0.26
    img[:2, :] = img[:, :2] = 0.40
    img *= (0.8 + 0.4 * RNG.random((n, n)))[..., None]
    rust = RNG.random((n, n)) > 0.93
    img[rust] = [0.40, 0.24, 0.13]
    return posterize(img, 16)


def tex_fabric(n=64):
    yy, xx = np.mgrid[0:n, 0:n]
    weave = 0.88 + 0.08 * (((xx // 2) + (yy // 2)) % 2)
    img = np.stack([weave] * 3, -1)
    stain = np.clip((noise(n, 6) - 0.55) * 3, 0, 1)[..., None]
    img = img * (1 - stain * 0.6) + stain * 0.6 * np.array([0.45, 0.35, 0.25])
    return posterize(img, 20)


PAPER_RECTS = {   # pixel rects in the 512 x 512 paper atlas (x0, y0, x1, y1), y from top
    "schematic": (0, 0, 192, 256), "note_a": (192, 0, 320, 128), "note_b": (320, 0, 448, 128),
    "photo": (448, 0, 512, 64), "tag": (448, 64, 512, 128),
    "label_in": (192, 128, 384, 192), "label_out": (192, 192, 384, 256),
    "label_oldbrg": (384, 128, 512, 176), "label_cargo": (384, 176, 512, 224), "label_parts": (384, 224, 512, 256),
    "chart": (0, 256, 256, 512), "sign_clinic": (256, 256, 512, 320), "caution": (256, 320, 512, 384),
    "ledger": (256, 384, 384, 512), "label_unit07": (384, 384, 512, 448), "label_trade": (384, 448, 512, 512),
}


def tex_paper(n=512):
    paper = np.array([0.80, 0.76, 0.63])
    ink = np.array([0.14, 0.12, 0.11])
    img = np.ones((n, n, 3)) * paper
    img *= (0.88 + 0.16 * noise(n, 12))[..., None]
    img *= (0.95 + 0.07 * RNG.random((n, n)))[..., None]
    R = PAPER_RECTS

    def lines(r, step=8, x_pad=8, frac=0.85):
        x0, y0, x1, y1 = r
        for y in range(y0 + 14, y1 - 6, step):
            ln = int((x1 - x0 - 2 * x_pad) * (frac - 0.35 * RNG.random()))
            img[y:y + 2, x0 + x_pad:x0 + x_pad + ln] = ink * 1.6

    x0, y0, x1, y1 = R["schematic"]
    img[y0:y1, x0:x1] = np.array([0.70, 0.73, 0.70]) * (0.9 + 0.1 * RNG.random((y1 - y0, x1 - x0, 1)))
    text(img, "UNIT 07", x0 + 12, y0 + 10, 3, ink)
    blue = np.array([0.18, 0.24, 0.32])
    img[y0 + 60:y0 + 150, x0 + 50] = blue; img[y0 + 60:y0 + 150, x0 + 140] = blue
    img[y0 + 60, x0 + 50:x0 + 141] = blue; img[y0 + 150, x0 + 50:x0 + 141] = blue
    img[y0 + 80:y0 + 130, x0 + 70] = blue; img[y0 + 80:y0 + 130, x0 + 120] = blue
    img[y0 + 80, x0 + 70:x0 + 121] = blue; img[y0 + 130, x0 + 70:x0 + 121] = blue
    for cx in (x0 + 25, x0 + 165):
        img[y0 + 85:y0 + 125, cx - 18:cx + 18:6] = blue
    img[y0 + 160:y0 + 230, x0 + 95] = blue
    text(img, "SERVICE DOCK", x0 + 12, y0 + 236, 2, ink)
    lines(R["note_a"]); lines(R["note_b"], 10)
    text(img, "PARTS", R["note_b"][0] + 8, R["note_b"][1] + 4, 1, ink)
    x0, y0, x1, y1 = R["photo"]
    img[y0 + 4:y1 - 4, x0 + 4:x1 - 4] = np.array([0.35, 0.33, 0.30]) * (0.7 + 0.5 * noise(0, 3, (y1 - y0 - 8, x1 - x0 - 8)))[..., None]
    x0, y0, x1, y1 = R["tag"]
    img[y0:y1, x0:x1] = [0.78, 0.60, 0.20]
    text(img, "TAG", x0 + 14, y0 + 24, 2, ink)
    for key, big, small, col in (("label_in", "IN", "NEXT JOB", [0.62, 0.30, 0.10]), ("label_out", "OUT", "REPAIRED", [0.20, 0.42, 0.24])):
        x0, y0, x1, y1 = R[key]
        img[y0:y1, x0:x1] = np.array(col) * (0.85 + 0.2 * RNG.random((y1 - y0, x1 - x0, 1)))
        img[y0 + 3:y1 - 3, x0 + 3:x0 + 6] = paper; img[y0 + 3:y1 - 3, x1 - 6:x1 - 3] = paper
        text(img, big, x0 + 14, y0 + 12, 5, paper)
        text(img, small, x0 + 14 + len(big) * 30 + 6, y0 + 26, 2, paper)
    for key, s in (("label_oldbrg", "OLD BRG"), ("label_cargo", "CARGO"), ("label_parts", "PARTS"), ("label_unit07", "UNIT 07"), ("label_trade", "TRADE")):
        x0, y0, x1, y1 = R[key]
        img[y0 + 2:y1 - 2, x0 + 2:x1 - 2] = np.array([0.74, 0.70, 0.56])
        text(img, s, x0 + 8, y0 + (y1 - y0) // 2 - 7, 2, ink)
    x0, y0, x1, y1 = R["chart"]
    text(img, "LIMB FIT CHART", x0 + 10, y0 + 8, 2, ink)
    for k in range(4):
        cx = x0 + 32 + k * 60
        img[y0 + 40:y0 + 200, cx - 6:cx + 6] = ink * 2.2
        img[y0 + 40:y0 + 200, cx - 5:cx + 5] = paper * 0.9
        for j in range(5):
            img[y0 + 50 + j * 32:y0 + 52 + j * 32, cx - 14:cx + 14] = ink
    lines((x0, y0 + 205, x1, y1), 9)
    x0, y0, x1, y1 = R["sign_clinic"]
    img[y0:y1, x0:x1] = [0.12, 0.13, 0.12]
    text(img, "CLINIC", x0 + 30, y0 + 14, 5, [0.78, 0.60, 0.20])
    x0, y0, x1, y1 = R["caution"]
    img[y0:y1, x0:x1] = [0.78, 0.60, 0.13]
    text(img, "CAUTION", x0 + 30, y0 + 18, 4, ink)
    x0, y0, x1, y1 = R["ledger"]
    img[y0:y1, x0:x1] = np.array([0.30, 0.20, 0.16])
    img[y0 + 6:y1 - 6, x0 + 10:x1 - 6] = paper * 0.95
    lines((x0 + 8, y0, x1, y1), 7)
    img[:, :] *= (0.97 + 0.05 * RNG.random((n, n, 1)))
    return posterize(img, 32)


SCREEN_RECTS = {"arm": (0, 0, 128, 96), "trade": (128, 0, 256, 96)}


def tex_screen():
    w, h = 256, 96
    img = np.zeros((h, w, 3))
    g = np.array([0.30, 0.95, 0.42])
    img[:, :] = [0.02, 0.08, 0.04]
    img[::2] *= 0.6
    for x0 in (0, 128):
        img[2:4, x0 + 2:x0 + 126] = g * 0.6
        img[h - 4:h - 2, x0 + 2:x0 + 126] = g * 0.6
        img[2:h - 2, x0 + 2:x0 + 4] = g * 0.6
        img[2:h - 2, x0 + 124:x0 + 126] = g * 0.6
    text(img, "DIAG", 8, 8, 1, g)
    for i in range(110):
        y = int(48 + 18 * math.sin(i * 0.21) * math.exp(-((i - 55) / 40) ** 2))
        img[y:y + 2, 9 + i] = g
    img[80:82, 8:120] = g * 0.5
    text(img, "JOB 07 IN", 136, 10, 1, g)
    text(img, "BRG X2  12", 136, 24, 1, g * 0.85)
    text(img, "SERVO   40", 136, 36, 1, g * 0.85)
    text(img, "ARM-L  OUT", 136, 48, 1, g * 0.85)
    text(img, "> CARGO", 136, 70, 1, g)
    return img


def save_png(name, rgb):
    h, w, _ = rgb.shape
    rgba = np.concatenate([rgb, np.ones((h, w, 1))], -1)
    img = bpy.data.images.new(os.path.splitext(name)[0], width=w, height=h, alpha=False)
    img.pixels.foreach_set(np.flipud(rgba).astype(np.float32).ravel())
    path = os.path.join(D_TEX, name)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    return img


# ---------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------
MAT_SPECS = {
    # name: tint (sRGB), metallic, roughness, texture, uv tile (m) u/v, emission
    "M_UC_Wall":       dict(tint=(1.0, 1.0, 1.0), metal=0.30, rough=0.78, tex="T_UC_Wall.png", tile=(1.2, 1.2)),
    "M_UC_Floor":      dict(tint=(1.0, 1.0, 1.0), metal=0.25, rough=0.82, tex="T_UC_Floor.png", tile=(1.2, 1.2)),
    "M_UC_DarkSteel":  dict(tint=(0.20, 0.21, 0.21), metal=0.60, rough=0.68, tex="T_UC_Worn.png", tile=(0.6, 0.6)),
    "M_UC_Steel":      dict(tint=(0.46, 0.47, 0.45), metal=0.70, rough=0.55, tex="T_UC_Worn.png", tile=(0.5, 0.5)),
    "M_UC_PaintGreen": dict(tint=(0.30, 0.38, 0.32), metal=0.20, rough=0.74, tex="T_UC_Worn.png", tile=(0.6, 0.6)),
    "M_UC_Ivory":      dict(tint=(0.80, 0.76, 0.63), metal=0.05, rough=0.60, tex="T_UC_Worn.png", tile=(0.4, 0.4)),
    "M_UC_Red":        dict(tint=(0.52, 0.15, 0.11), metal=0.20, rough=0.70, tex="T_UC_Worn.png", tile=(0.5, 0.5)),
    "M_UC_Orange":     dict(tint=(0.70, 0.32, 0.12), metal=0.10, rough=0.90, tex="T_UC_Worn.png", tile=(0.5, 0.5)),
    "M_UC_Pipe":       dict(tint=(0.55, 0.33, 0.20), metal=0.60, rough=0.58, tex="T_UC_Worn.png", tile=(0.4, 0.4)),
    "M_UC_Hazard":     dict(tint=(1.0, 1.0, 1.0), metal=0.10, rough=0.80, tex="T_UC_Hazard.png", tile=(0.5, 0.125)),
    "M_UC_Rubber":     dict(tint=(0.07, 0.07, 0.07), metal=0.0, rough=0.90, tex="T_UC_Worn.png", tile=(0.3, 0.3)),
    "M_UC_Vinyl":      dict(tint=(0.24, 0.31, 0.26), metal=0.0, rough=0.55, tex="T_UC_Fabric.png", tile=(0.25, 0.25)),
    "M_UC_Cloth":      dict(tint=(0.66, 0.65, 0.60), metal=0.0, rough=0.95, tex="T_UC_Fabric.png", tile=(0.3, 0.3)),
    "M_UC_Wood":       dict(tint=(0.32, 0.21, 0.14), metal=0.0, rough=0.80, tex="T_UC_Worn.png", tile=(0.3, 0.3)),
    "M_UC_Grate":      dict(tint=(1.0, 1.0, 1.0), metal=0.50, rough=0.70, tex="T_UC_Grate.png", tile=(0.4, 0.4)),
    "M_UC_Paper":      dict(tint=(1.0, 1.0, 1.0), metal=0.0, rough=0.92, tex="T_UC_Paper.png", tile=None),
    "M_UC_Screen":     dict(tint=(1.0, 1.0, 1.0), metal=0.0, rough=0.30, tex="T_UC_Screen.png", tile=None, emit_tex=True, emit=1.6),
    "M_UC_LampWarm":   dict(tint=(1.0, 0.78, 0.50), metal=0.0, rough=0.40, tex=None, tile=None, emit_col=(1.0, 0.66, 0.32), emit=5.0),
}
MATS = {}
IMAGES = {}


def lin(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def build_textures():
    gens = {"T_UC_Worn.png": tex_worn, "T_UC_Floor.png": tex_floor, "T_UC_Wall.png": tex_wall,
            "T_UC_Hazard.png": tex_hazard, "T_UC_Grate.png": tex_grate, "T_UC_Fabric.png": tex_fabric,
            "T_UC_Paper.png": tex_paper, "T_UC_Screen.png": tex_screen}
    for name, fn in gens.items():
        IMAGES[name] = save_png(name, fn())


def mix_socket(node, name, kind):
    return next(s for s in node.inputs if s.name == name and s.type == kind)


def build_materials():
    for name, sp in MAT_SPECS.items():
        m = bpy.data.materials.new(name)
        try:
            m.use_nodes = True
        except Exception:
            pass
        N, L = m.node_tree.nodes, m.node_tree.links
        bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Metallic"].default_value = sp["metal"]
        bsdf.inputs["Roughness"].default_value = sp["rough"]
        tint = (*lin(sp["tint"]), 1.0)
        if sp["tex"]:
            it = N.new("ShaderNodeTexImage")
            it.image = IMAGES[sp["tex"]]
            it.interpolation = "Closest"
            mix = N.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs[0].default_value = 1.0
            L.new(it.outputs["Color"], mix_socket(mix, "A", "RGBA"))
            mix_socket(mix, "B", "RGBA").default_value = tint
            L.new(next(s for s in mix.outputs if s.type == "RGBA"), bsdf.inputs["Base Color"])
            if sp.get("emit_tex"):
                L.new(it.outputs["Color"], bsdf.inputs["Emission Color"])
                bsdf.inputs["Emission Strength"].default_value = sp["emit"]
        else:
            bsdf.inputs["Base Color"].default_value = tint
        if sp.get("emit_col"):
            bsdf.inputs["Emission Color"].default_value = (*lin(sp["emit_col"]), 1.0)
            bsdf.inputs["Emission Strength"].default_value = sp["emit"]
        MATS[name] = m


# ---------------------------------------------------------------------------
# Mesh building in Unity coordinates
# ---------------------------------------------------------------------------
class MB:
    def __init__(self):
        self.v, self.f, self.uvrule = [], [], None

    def add(self, verts, faces):
        o = len(self.v)
        self.v += [Vector(p) for p in verts]
        self.f += [[i + o for i in fc] for fc in faces]
        return self

    def corners(self, c8):
        """c8: 8 points ordered (x0y0z0, x1y0z0, x1y0z1, x0y0z1, x0y1z0, x1y1z0, x1y1z1, x0y1z1)."""
        return self.add(c8, [[0, 1, 2, 3], [4, 7, 6, 5], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]])

    def box(self, lo, hi):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        return self.corners([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1),
                             (x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)])

    def fbox(self, o, ex, ez, a0, a1, y0, y1, b0, b1):
        """Box in a horizontal frame: p = o + ex*a + ez*b, height y (absolute)."""
        o, ex, ez = Vector(o), Vector(ex), Vector(ez)
        P = lambda a, y, b: Vector((o.x + ex.x * a + ez.x * b, y, o.z + ex.z * a + ez.z * b))
        return self.corners([P(a0, y0, b0), P(a1, y0, b0), P(a1, y0, b1), P(a0, y0, b1),
                             P(a0, y1, b0), P(a1, y1, b0), P(a1, y1, b1), P(a0, y1, b1)])

    def obox(self, c, size, yaw=0.0, tilt=0.0):
        sx, sy, sz = (s / 2 for s in size)
        out = []
        for (x, y, z) in [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, -sy, sz), (-sx, -sy, sz),
                          (-sx, sy, -sz), (sx, sy, -sz), (sx, sy, sz), (-sx, sy, sz)]:
            if tilt:
                t = math.radians(tilt)
                y, z = y * math.cos(t) - z * math.sin(t), y * math.sin(t) + z * math.cos(t)
            out.append(rot_y((x, y, z), yaw) + Vector(c))
        return self.corners(out)

    def cyl(self, c, r, h, axis="y", segs=8, r2=None, yaw=0.0, phase=0.0):
        if isinstance(axis, str):
            A_, U, V = {"y": ((0, 1, 0), (1, 0, 0), (0, 0, 1)), "x": ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
                        "z": ((0, 0, 1), (1, 0, 0), (0, 1, 0))}[axis]
        else:   # free axis vector: derive an orthonormal basis
            A_ = Vector(axis).normalized()
            U = A_.cross(Vector((0, 1, 0)) if abs(A_.y) < 0.9 else Vector((1, 0, 0))).normalized()
            V = A_.cross(U)
        A_, U, V = (rot_y(Vector(a), yaw) for a in (A_, U, V))
        r2 = r if r2 is None else r2
        c = Vector(c)
        verts = []
        for end, rr in ((-0.5, r), (0.5, r2)):
            for k in range(segs):
                t = 2 * math.pi * k / segs + phase
                verts.append(c + A_ * (h * end) + (U * math.cos(t) + V * math.sin(t)) * rr)
        faces = [[k, (k + 1) % segs, segs + (k + 1) % segs, segs + k] for k in range(segs)]
        faces += [list(range(segs))[::-1], list(range(segs, 2 * segs))]
        return self.add(verts, faces)

    def tube(self, pts, r, segs=6, closed=False):
        pts = [Vector(p) for p in pts]
        n = len(pts)
        rings, prev = [], None
        for i, p in enumerate(pts):
            if closed:
                tg = (pts[(i + 1) % n] - pts[i - 1]).normalized()
            else:
                tg = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            if closed or prev is None:
                ref = Vector((0, 1, 0)) if abs(tg.y) < 0.9 else Vector((1, 0, 0))
                nr = tg.cross(ref).normalized()
            else:
                nr = (prev - tg * prev.dot(tg)).normalized()
            bn = tg.cross(nr)
            prev = nr
            rings.append([p + (nr * math.cos(2 * math.pi * k / segs) + bn * math.sin(2 * math.pi * k / segs)) * r for k in range(segs)])
        verts = [v for ring in rings for v in ring]
        faces = []
        for i in range(n if closed else n - 1):
            a, b = i * segs, ((i + 1) % n) * segs
            faces += [[a + k, a + (k + 1) % segs, b + (k + 1) % segs, b + k] for k in range(segs)]
        if not closed:
            faces += [list(range(segs))[::-1], [(n - 1) * segs + k for k in range(segs)]]
        return self.add(verts, faces)

    def prism(self, poly_xz, y0, y1):
        n = len(poly_xz)
        verts = [Vector((x, y0, z)) for x, z in poly_xz] + [Vector((x, y1, z)) for x, z in poly_xz]
        faces = [list(range(n)), list(range(n, 2 * n))[::-1]]
        faces += [[k, (k + 1) % n, n + (k + 1) % n, n + k] for k in range(n)]
        return self.add(verts, faces)

    def merge(self, other):
        return self.add(other.v, other.f)


def catenary(p0, p1, sag, n=8):
    p0, p1 = Vector(p0), Vector(p1)
    return [p0.lerp(p1, i / n) - Vector((0, sag * 4 * (i / n) * (1 - i / n), 0)) for i in range(n + 1)]


# ---------------------------------------------------------------------------
# Object creation
# ---------------------------------------------------------------------------
COLL = {}
OBJ_INFO = {}     # name -> dict(module, role, material, props)
MODULE_ROOTS = {}
ROOT = None


def link(obj, coll):
    COLL[coll].objects.link(obj)


def set_props(obj, props):
    for k, v in props.items():
        if isinstance(v, (list, tuple, Vector)):
            v = ",".join("%.4f" % x if isinstance(x, float) else str(x) for x in v)
        obj[k] = v


def world_matrix(o):
    """matrix_world from basis/parent chain (o.matrix_world is stale for objects created this pass)."""
    m = o.matrix_basis.copy()
    while o.parent is not None:
        m = o.matrix_parent_inverse @ m
        o = o.parent
        m = o.matrix_basis @ m
    return m


def make(name, mb, mat, module, pivot=None, role="static", parent=None, props=None, uv=None):
    """Create a mesh object from a Unity-space MB. pivot: Unity point (object origin)."""
    if not mb.v:
        return None
    if pivot is None:
        lo = Vector(tuple(min(v[i] for v in mb.v) for i in range(3)))
        hi = Vector(tuple(max(v[i] for v in mb.v) for i in range(3)))
        pivot = Vector(((lo.x + hi.x) / 2, lo.y, (lo.z + hi.z) / 2))
    pivot = Vector(pivot)
    pb = ub(pivot)
    me = bpy.data.meshes.new(name)
    me.from_pydata([ub(v) - pb for v in mb.v], [], mb.f)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uvl = bm.loops.layers.uv.new("UVMap")
    tile = MAT_SPECS[mat]["tile"] or (1.0, 1.0)
    for f in bm.faces:
        nu = bu(f.normal)
        ax = max(range(3), key=lambda i: abs(nu[i]))
        for lp in f.loops:
            p = bu(lp.vert.co + pb)
            if uv is not None:
                lp[uvl].uv = uv(p, nu)
            elif ax == 1:
                lp[uvl].uv = (p.x / tile[0], p.z / tile[1])
            elif ax == 0:
                lp[uvl].uv = (p.z / tile[0], p.y / tile[1])
            else:
                lp[uvl].uv = (p.x / tile[0], p.y / tile[1])
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.materials.append(MATS[mat])
    obj = bpy.data.objects.new(name, me)
    obj.location = pb
    link(obj, module)
    par = parent if parent is not None else MODULE_ROOTS[module]
    obj.parent = par
    obj.matrix_parent_inverse = world_matrix(par).inverted()
    p = dict(uc_module=module, uc_role=role, uc_pivot_unity=r3(pivot))
    p.update(props or {})
    set_props(obj, p)
    OBJ_INFO[name] = dict(module=module, role=role, material=mat, pivot=r3(pivot), props={k: v for k, v in (props or {}).items()})
    return obj


def empty(name, module, pos_u, yaw=0.0, parent=None, props=None, size=0.1):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = size
    e.location = ub(pos_u)
    e.rotation_euler = (0, 0, math.radians(-yaw))     # Unity yaw +deg == Blender Z -deg
    link(e, module)
    par = parent if parent is not None else (MODULE_ROOTS.get(module) or ROOT)
    if par is not None and par is not e:
        e.parent = par
        e.matrix_parent_inverse = world_matrix(par).inverted()
    set_props(e, props or {})
    return e


def decal(name, module, center, facing, up, w, h, rect, atlas=(512, 512), thick=0.003, mat="M_UC_Paper", parent=None, role="decal"):
    """Thin box whose front face shows `rect` of the atlas. facing: outward normal (Unity), up: image up."""
    c, d, u = Vector(center), Vector(facing).normalized(), Vector(up).normalized()
    f = -d
    r = Vector((u.y * f.z - u.z * f.y, u.z * f.x - u.x * f.z, u.x * f.y - u.y * f.x)).normalized()
    pts = []
    for (sa, sb, sc) in [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1), (-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)]:
        pts.append(c + r * (sa * w / 2) + d * (sb * thick / 2 + thick / 2) + u * (sc * h / 2))
    mb = MB().corners(pts)
    x0, y0, x1, y1 = rect
    W, Hh = atlas

    def uvf(p, nrm):
        s = (p - c).dot(r) / w + 0.5
        t = (p - c).dot(u) / h + 0.5
        return ((x0 + s * (x1 - x0)) / W, 1 - (y1 - t * (y1 - y0)) / Hh)

    return make(name, mb, mat, module, pivot=c, role=role, parent=parent, uv=uvf)


# ---------------------------------------------------------------------------
# Clutter pools (merged per zone + material to keep object / draw counts low)
# ---------------------------------------------------------------------------
POOL = {}


def pool(zone, mat):
    return POOL.setdefault((zone, mat), MB())


def flush_pools():
    for (zone, mat), mb in sorted(POOL.items()):
        make("Clutter_%s_%s" % (zone, mat.replace("M_UC_", "")), mb, mat, "UC_Clutter", role="clutter",
             props=dict(uc_note="non-interactive dressing merged by material; collision not required"))


def crate(zone, c, size, yaw=0.0, mat="M_UC_PaintGreen"):
    sx, sy, sz = size
    pool(zone, mat).obox((c[0], c[1] + sy / 2, c[2]), size, yaw)
    pool(zone, "M_UC_DarkSteel").obox((c[0], c[1] + sy - 0.02, c[2]), (sx + 0.02, 0.03, sz + 0.02), yaw)
    pool(zone, "M_UC_DarkSteel").obox((c[0], c[1] + 0.03, c[2]), (sx + 0.02, 0.05, sz + 0.02), yaw)


def bin_(zone, c, size, yaw=0.0, mat="M_UC_PaintGreen"):
    sx, sy, sz = size
    pool(zone, mat).obox((c[0], c[1] + sy / 2, c[2]), size, yaw)
    pool(zone, "M_UC_Steel").obox(rot_y((0, sy * 0.6, -sz / 2 - 0.006), yaw) + Vector(c), (sx * 0.4, 0.03, 0.012), yaw)


def limb_arm(zone, base, direction, length=0.62, yaw=0.0):
    d = Vector(direction).normalized()
    b = Vector(base)
    m = pool(zone, "M_UC_Ivory")
    m.cyl(b + d * (length * 0.22), 0.05, length * 0.44, axis=tuple(d), segs=8)
    m.cyl(b + d * (length * 0.66), 0.042, length * 0.40, axis=tuple(d), segs=8, r2=0.036)
    pool(zone, "M_UC_DarkSteel").cyl(b + d * (length * 0.45), 0.034, 0.05, axis=tuple(d), segs=8)
    side = Vector((-d.z, 0, d.x)) if abs(d.y) < 0.9 else Vector((1, 0, 0))
    hand = b + d * (length * 0.92)
    m.obox(hand, (0.08, 0.03, 0.10), yaw=yaw)
    for k in range(4):
        pool(zone, "M_UC_DarkSteel").tube([hand + side * (-0.03 + 0.02 * k), hand + side * (-0.03 + 0.02 * k) + d * 0.08], 0.007, 4)


def limb_leg(zone, foot_c, yaw=0.0, h=0.80):
    m = pool(zone, "M_UC_Ivory")
    c = Vector(foot_c)
    m.obox(c + Vector((0, 0.03, 0)), (0.10, 0.06, 0.24), yaw)
    m.cyl(c + Vector((0, 0.06 + h * 0.22, 0)), 0.045, h * 0.42, segs=8, r2=0.055)
    pool(zone, "M_UC_DarkSteel").cyl(c + Vector((0, 0.06 + h * 0.46, 0)), 0.05, 0.06, axis="x", yaw=yaw, segs=8)
    m.cyl(c + Vector((0, 0.06 + h * 0.72, 0)), 0.06, h * 0.46, segs=8, r2=0.075)


def bottle(zone, c, r=0.035, h=0.20, mat="M_UC_Red"):
    p = Vector(c)
    pool(zone, mat).cyl(p + Vector((0, h * 0.4, 0)), r, h * 0.8, segs=8)
    pool(zone, "M_UC_Ivory").cyl(p + Vector((0, h * 0.9, 0)), r * 0.45, h * 0.2, segs=6)


def mug(zone, c):
    p = Vector(c)
    pool(zone, "M_UC_Ivory").cyl(p + Vector((0, 0.05, 0)), 0.04, 0.10, segs=8)
    pool(zone, "M_UC_Ivory").obox(p + Vector((0.05, 0.05, 0)), (0.02, 0.06, 0.012))


def rag(zone, c, yaw=0.0, mat="M_UC_Cloth"):
    p = Vector(c)
    m = pool(zone, mat)
    m.obox(p + Vector((0, 0.006, 0)), (0.26, 0.012, 0.20), yaw)
    m.obox(p + Vector((0.04, 0.016, 0.03)), (0.16, 0.012, 0.12), yaw + 23)
    m.obox(p + Vector((-0.05, 0.022, -0.02)), (0.10, 0.014, 0.08), yaw - 31)


# ---------------------------------------------------------------------------
# Room shell
# ---------------------------------------------------------------------------
def side_frame(k):
    a = math.radians(45 * k)
    n_out = Vector((math.cos(a), 0, math.sin(a)))
    t = Vector((-math.sin(a), 0, math.cos(a)))
    return n_out * A, t, -n_out


def octo(radius):
    return [(radius * math.cos(math.radians(22.5 + 45 * i)), radius * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)]


def wall_piece(mb, k, s0, s1, y0, y1, miter0, miter1):
    c, t, n_in = side_frame(k)
    e0 = WT * TAN if miter0 else 0.0
    e1 = WT * TAN if miter1 else 0.0
    P = lambda s, d, y: Vector((c.x + t.x * s + n_in.x * d, y, c.z + t.z * s + n_in.z * d))
    mb.corners([P(s0, 0, y0), P(s1, 0, y0), P(s1 + e1, -WT, y0), P(s0 - e0, -WT, y0),
                P(s0, 0, y1), P(s1, 0, y1), P(s1 + e1, -WT, y1), P(s0 - e0, -WT, y1)])


def wbox(mb, k, s0, s1, y0, y1, d0, d1):
    c, t, n_in = side_frame(k)
    return mb.fbox(c, t, n_in, s0, s1, y0, y1, d0, d1)


def wall_point(k, s, y, d):
    c, t, n_in = side_frame(k)
    return c + t * s + n_in * d + Vector((0, y, 0))


def build_shell():
    M = "UC_Shell"
    # floor: octagon slab under the walls + corridor stub floor (closed solids, no holes)
    fl = MB().prism(octo((A + WT) / math.cos(math.radians(22.5))), -0.10, 0.0)
    fl.box((-0.75, -0.10, -5.0), (0.75, 0.0, -A - WT + 0.001))
    make("Floor_Main", fl, "M_UC_Floor", M, pivot=(0, 0, 0), props=dict(uc_note="walkable; top at y=0; closed slab, no holes"))
    # floor dressing: flush 3 mm grates, broken orange ring around the bed, hazard strips
    gr = MB()
    for (x, z) in [(-1.55, 1.55), (1.10, -1.95), (-1.65, -2.05), (0.70, 1.50), (-0.10, -2.60)]:
        gr.box((x - 0.25, 0.0, z - 0.25), (x + 0.25, 0.003, z + 0.25))
    make("Floor_Grates", gr, "M_UC_Grate", M, role="floor_decal", props=dict(uc_note="flush 3 mm plates, walkable"))
    ring = MB()
    for q in range(4):
        a0 = math.radians(q * 90 + 12)
        a1 = math.radians(q * 90 + 78)
        segs = 10
        inner = [(BED_C.x + 1.20 * math.cos(a0 + (a1 - a0) * i / segs), BED_C.z + 1.20 * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs + 1)]
        outer = [(BED_C.x + 1.30 * math.cos(a0 + (a1 - a0) * i / segs), BED_C.z + 1.30 * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs + 1)]
        for i in range(segs):
            ring.prism([inner[i], inner[i + 1], outer[i + 1], outer[i]], 0.0, 0.002)
    make("Floor_SurgeryRing", ring, "M_UC_Orange", M, role="floor_decal")
    hz = MB()
    hz.box((-0.60, 0.0, -A), (0.60, 0.003, -A + 0.12))
    make("Floor_DoorHazard", hz, "M_UC_Hazard", M, role="floor_decal")

    # walls: one module per octagon side; south side split around the door opening
    half_open = DOOR_CLEAR / 2 + DOOR_POST
    for k, nm in enumerate(SIDES):
        st = MB()
        if nm == "S":
            # s runs along +X on the south side
            wall_piece(st, k, -HALF_SIDE, -half_open, 0, H, True, False)
            wall_piece(st, k, half_open, HALF_SIDE, 0, H, False, True)
            wall_piece(st, k, -half_open, half_open, DOOR_H + 0.15, H, False, False)
        else:
            wall_piece(st, k, -HALF_SIDE, HALF_SIDE, 0, H, True, True)
        # raised inner panels (skipped behind the existing workbench so its pegboard can sit on the wall plane)
        bays = 3
        bw = (2 * HALF_SIDE - 0.24) / bays
        for b in range(bays):
            s0 = -HALF_SIDE + 0.12 + b * bw + 0.03
            s1 = s0 + bw - 0.06
            if nm == "N" and not (s1 < -1.0 or s0 > 1.0):
                continue
            if nm == "S" and not (s1 < -half_open or s0 > half_open):
                continue
            wbox(st, k, s0, s1, 0.22, 2.18, 0.0, 0.022)
        make("Wall_" + nm, st, "M_UC_Wall", M, pivot=wall_point(k, 0, 0, 0),
             props=dict(uc_note="modular wall side %s; inner face at apothem %.2f" % (nm, A)))
    # trim: plinth, top rib, corner columns
    tr = MB()
    for k, nm in enumerate(SIDES):
        ranges = [(-HALF_SIDE + 0.08, HALF_SIDE - 0.08)]
        if nm == "N":
            ranges = [(-HALF_SIDE + 0.08, -1.0), (1.0, HALF_SIDE - 0.08)]
        if nm == "S":
            ranges = [(-HALF_SIDE + 0.08, -half_open), (half_open, HALF_SIDE - 0.08)]
        for s0, s1 in ranges:
            wbox(tr, k, s0, s1, 0.0, 0.16, 0.0, 0.04)
        wbox(tr, k, -HALF_SIDE + 0.08, HALF_SIDE - 0.08, 2.24, 2.31, 0.0, 0.05)
    for (x, z) in octo(RC):
        ang = math.degrees(math.atan2(x, z))
        tr.obox((x * (1 - 0.03 / RC), H / 2, z * (1 - 0.03 / RC)), (0.20, H, 0.14), yaw=ang)
    make("Wall_Trim", tr, "M_UC_DarkSteel", M, pivot=(0, 0, 0))
    # ceiling slab + beams (hidden in overhead / section renders)
    ce = MB().prism(octo((A + WT) / math.cos(math.radians(22.5))), H, H + 0.12)
    make("Ceiling_Slab", ce, "M_UC_Wall", M, pivot=(0, H, 0), role="ceiling")
    bm_ = MB()
    for z in (-1.35, 1.25):
        bm_.box((-3.2, H - 0.16, z - 0.06), (3.2, H, z + 0.06))
    for x in (-1.35, 1.15):
        bm_.box((x - 0.06, H - 0.16, -3.2), (x + 0.06, H, 3.2))
    make("Ceiling_Beams", bm_, "M_UC_DarkSteel", M, pivot=(0, H, 0), role="ceiling")
    # pipes & cables along the walls (above 2.4 m)
    pp = MB()
    loop1 = [(x, 2.62, z) for x, z in octo((A - 0.09) / math.cos(math.radians(22.5)))]
    loop2 = [(x, 2.76, z) for x, z in octo((A - 0.16) / math.cos(math.radians(22.5)))]
    pp.tube(loop1, 0.035, 6, closed=True)
    pp.tube(loop2, 0.026, 6, closed=True)
    for i in (2, 4, 7):
        x, z = octo((A - 0.09) / math.cos(math.radians(22.5)))[i]
        pp.tube([(x, 2.62, z), (x, 0.35, z), (x * 1.02, 0.12, z * 1.02)], 0.03, 6)
        pp.cyl((x, 1.6, z), 0.045, 0.08, segs=6)
    make("Shell_Pipes", pp, "M_UC_Pipe", M, pivot=(0, 2.6, 0))
    cb = MB()
    for k in (0, 1, 3, 4, 5, 7):
        p0 = wall_point(k, -HALF_SIDE + 0.15, 2.48, 0.05)
        p1 = wall_point(k, HALF_SIDE - 0.15, 2.48, 0.05)
        cb.tube(catenary(p0, p1, 0.16), 0.014, 5)
        cb.tube(catenary(p0 + Vector((0, 0.04, 0)), p1 + Vector((0, 0.04, 0)), 0.10), 0.010, 5)
    make("Shell_Cables", cb, "M_UC_Rubber", M, pivot=(0, 2.4, 0))
    # wall lamps (warm fluorescent) - housings merged, tubes emissive
    hs, tb = MB(), MB()
    LAMP_SIDES = {"NE": 0.0, "NW": 0.0, "E": 0.0, "SE": 0.0, "SW": 0.0}
    for nm, s in LAMP_SIDES.items():
        k = SIDES.index(nm)
        wbox(hs, k, s - 0.45, s + 0.45, 2.30, 2.40, 0.02, 0.15)
        c, t, n_in = side_frame(k)
        p = c + t * s + n_in * 0.11
        tb.cyl((p.x, 2.285, p.z), 0.018, 0.82, axis=(t.x, 0, t.z), segs=6)
    make("Shell_LampHousings", hs, "M_UC_DarkSteel", M, pivot=(0, 2.3, 0))
    make("Shell_LampTubes", tb, "M_UC_LampWarm", M, pivot=(0, 2.3, 0), role="light_fixture",
         props=dict(uc_note="emissive; Unity: add warm area/spot lights at these fixtures"))
    # patch plates (repairs)
    pt = MB()
    for (k, s, y, w, h) in [(0, 0.9, 0.55, 0.30, 0.22), (3, -0.2, 1.1, 0.42, 0.30), (5, 0.55, 2.0, 0.36, 0.20), (7, -0.4, 0.40, 0.50, 0.26), (1, -0.8, 1.9, 0.28, 0.28), (6, -1.05, 2.0, 0.30, 0.24)]:
        wbox(pt, k, s - w / 2, s + w / 2, y - h / 2, y + h / 2, 0.022, 0.030)
        for (ds, dy) in [(-1, -1), (1, -1), (1, 1), (-1, 1)]:
            q = wall_point(k, s + ds * (w / 2 - 0.025), y + dy * (h / 2 - 0.025), 0.033)
            pt.cyl(q, 0.008, 0.008, axis=tuple(side_frame(k)[2]), segs=6)
    make("Shell_PatchPlates", pt, "M_UC_Steel", M, pivot=(0, 1, 0))

    # door (south): frame, two sliding leaves outside the wall plane, lamp, panel, button
    D = "UC_Door"
    fr = MB()
    for sx in (-1, 1):
        fr.box((sx * DOOR_CLEAR / 2 if sx > 0 else -half_open, 0, -A - WT - 0.02), (half_open if sx > 0 else -DOOR_CLEAR / 2, DOOR_H, -A + 0.06))
    fr.box((-half_open, DOOR_H, -A - WT - 0.02), (half_open, DOOR_H + 0.15, -A + 0.06))
    fr.box((-half_open - 0.05, 0.0, -A - 0.01), (half_open + 0.05, 0.02, -A + 0.08))
    make("Door_Frame", fr, "M_UC_DarkSteel", D, pivot=(0, 0, -A), props=dict(uc_note="clear opening %.2f x %.2f m" % (DOOR_CLEAR, DOOR_H)))
    hz2 = MB()
    for sx in (-1, 1):
        x0 = DOOR_CLEAR / 2 + 0.01 if sx > 0 else -half_open + 0.01
        hz2.box((x0, 0.05, -A + 0.06), (x0 + DOOR_POST - 0.02, DOOR_H - 0.05, -A + 0.064))
    make("Door_FrameHazard", hz2, "M_UC_Hazard", D, pivot=(0, 0, -A), role="decal")
    leaf_z = -A - WT - 0.06
    for side, sx in (("L", -1), ("R", 1)):
        lf = MB()
        lw = DOOR_CLEAR / 2 + 0.03
        cx_closed = sx * lw / 2
        cx_open = cx_closed + sx * lw
        lf.box((cx_open - lw / 2, 0.01, leaf_z - 0.03), (cx_open + lw / 2, DOOR_H + 0.05, leaf_z + 0.03))
        lf.box((cx_open - lw / 2 + 0.06, 1.0, leaf_z + 0.03), (cx_open + lw / 2 - 0.06, 1.12, leaf_z + 0.04))
        make("Door_Leaf_" + side, lf, "M_UC_DarkSteel", D, pivot=(cx_open, 0, leaf_z), role="animated",
             props=dict(uc_anim="slide", uc_slide_axis_unity_world=(sx * 1.0, 0.0, 0.0), uc_state_default="open",
                        uc_close_offset_unity=(-sx * lw, 0.0, 0.0), uc_travel_m=round(lw, 3),
                        uc_note="moves on the corridor side of the wall; closing = translate by uc_close_offset_unity"))
    lp = MB().box((-0.32, DOOR_H + 0.17, -A), (0.32, DOOR_H + 0.25, -A + 0.08))
    make("Door_LampHousing", lp, "M_UC_DarkSteel", D, pivot=(0, DOOR_H + 0.17, -A))
    lt = MB().box((-0.28, DOOR_H + 0.175, -A + 0.08), (0.28, DOOR_H + 0.205, -A + 0.09))
    make("Door_Lamp", lt, "M_UC_LampWarm", D, pivot=(0, DOOR_H + 0.19, -A + 0.085), role="status_light",
         props=dict(uc_note="amber lamp above entrance; program may toggle emission"))
    cp = MB().box((0.80, 1.10, -A + 0.0), (0.93, 1.45, -A + 0.07))
    make("Door_ControlPanel", cp, "M_UC_DarkSteel", D, pivot=(0.865, 1.10, -A))
    bt = MB().cyl((0.865, 1.30, -A + 0.08), 0.025, 0.025, axis="z", segs=8)
    make("Door_Button", bt, "M_UC_Red", D, pivot=(0.865, 1.30, -A + 0.08), role="interactive",
         props=dict(uc_note="door open/close button; press axis unity -Z"))
    # corridor stub (only visible through the door)
    cs = MB()
    cs.box((-0.95, 0.0, -5.0), (-0.75, 2.60, -A - WT - 0.10))
    cs.box((0.75, 0.0, -5.0), (0.95, 2.60, -A - WT - 0.10))
    cs.box((-0.95, 2.45, -5.0), (0.95, 2.60, -A - WT - 0.10))
    cs.box((-0.95, 0.0, -5.2), (0.95, 2.60, -5.0))
    make("Corridor_Stub", cs, "M_UC_Wall", D, pivot=(0, 0, -A - WT))
    cl = MB().box((-0.30, 2.40, -4.55), (0.30, 2.45, -4.45))
    make("Corridor_Lamp", cl, "M_UC_LampWarm", D, pivot=(0, 2.4, -4.5), role="light_fixture")
    decal("Door_SignClinic", D, (0, DOOR_H + 0.40, -A - WT - 0.005), (0, 0, -1), (0, 1, 0), 0.80, 0.20, PAPER_RECTS["sign_clinic"])
    decal("Door_CautionPlate", D, (-0.95, 1.55, -A + 0.023), (0, 0, 1), (0, 1, 0), 0.30, 0.075, PAPER_RECTS["caution"])


# ---------------------------------------------------------------------------
# Central surgery bed + overhead tool gantry
# ---------------------------------------------------------------------------
TOOLS = {}   # name -> dict(carriage, point, tip_y, objects)


def bp(x, y, z):
    return Vector((BED_C.x + x, y, BED_C.z + z))


def build_surgery():
    M = "UC_Surgery"
    base = MB()
    base.box(bp(-0.30, 0, -0.62), bp(0.30, 0.06, 0.62))
    base.box(bp(-0.16, 0.06, -0.32), bp(0.16, 0.58, 0.32))
    base.box(bp(-0.33, 0.62, -0.95), bp(0.33, 0.70, 0.95))
    base.box(bp(-0.06, 0.55, -0.80), bp(0.06, 0.62, 0.80))
    for sx in (-1, 1):
        for sz in (-1, 1):
            base.box(bp(sx * 0.26 - 0.045, 0, sz * 0.58 - 0.045), bp(sx * 0.26 + 0.045, 0.09, sz * 0.58 + 0.045))
        base.tube([bp(sx * 0.12, 0.08, -0.45), bp(sx * 0.12, 0.60, -0.25)], 0.025, 6)
    make("Bed_Base", base, "M_UC_DarkSteel", M, pivot=bp(0, 0, 0), props=dict(uc_note="static surgery bed base"))
    sh = MB()
    for sx in (-1, 1):
        sh.box(bp(sx * 0.30 if sx > 0 else -0.33, 0.56, -0.90), bp(0.33 if sx > 0 else -0.30, 0.74, 0.90))
        sh.box(bp(sx * 0.12 if sx > 0 else -0.30, 0.0, -0.95), bp(0.30 if sx > 0 else -0.12, 0.30, -0.72))
    sh.box(bp(-0.30, 0.56, 0.88), bp(0.30, 0.74, 0.95))
    sh.box(bp(-0.30, 0.56, -0.95), bp(0.30, 0.74, -0.88))
    sh.box(bp(-0.19, 0.12, -0.26), bp(0.19, 0.50, 0.26))
    make("Bed_Shell", sh, "M_UC_Ivory", M, pivot=bp(0, 0, 0))
    pads = MB()
    for z0, z1 in ((-0.93, -0.32), (-0.30, 0.30), (0.32, 0.70)):
        pads.box(bp(-0.30, 0.74, z0), bp(0.30, BED_TOP, z1))
    pads.box(bp(-0.30, 0.74, 0.72), bp(0.30, BED_TOP, 0.93))
    pads.box(bp(-0.18, BED_TOP, 0.74), bp(0.18, BED_TOP_MAX, 0.90))
    make("Bed_Pads", pads, "M_UC_Vinyl", M, pivot=bp(0, 0.74, 0))
    rl = MB()
    for sx in (-1, 1):
        x = sx * 0.345
        rl.tube([bp(x, 0.88, -0.82), bp(x, 0.88, 0.82)], 0.016, 6)
        for z in (-0.78, 0.0, 0.78):
            rl.tube([bp(x, 0.70, z), bp(x, 0.88, z)], 0.014, 6)
        for z in (-0.90, 0.90):
            rl.tube([bp(x, 0.70, z), bp(x, 1.00, z)], 0.022, 6)
            rl.tube([bp(x - sx * 0.0, 1.00, z - 0.06), bp(x, 1.00, z + 0.06)], 0.020, 6)
    make("Bed_Rails", rl, "M_UC_Steel", M, pivot=bp(0, 0.7, 0))
    stp = MB()
    for z in (-0.55, 0.15):
        stp.box(bp(-0.31, BED_TOP, z - 0.03), bp(0.31, BED_TOP + 0.006, z + 0.03))
        stp.box(bp(-0.33, 0.70, z - 0.03), bp(-0.31, BED_TOP + 0.006, z + 0.03))
        stp.box(bp(0.31, 0.70, z - 0.03), bp(0.33, BED_TOP + 0.006, z + 0.03))
    make("Bed_Straps", stp, "M_UC_Rubber", M, pivot=bp(0, BED_TOP, 0))

    # instrument cart at the bed foot
    cx, cz = BED_C.x, -1.60
    ct = MB()
    for sx in (-1, 1):
        for sz in (-1, 1):
            ct.tube([(cx + sx * 0.23, 0.07, cz + sz * 0.17), (cx + sx * 0.23, 0.90, cz + sz * 0.17)], 0.014, 6)
    for y in (0.20, 0.55, 0.88):
        ct.box((cx - 0.25, y, cz - 0.19), (cx + 0.25, y + 0.02, cz + 0.19))
        ct.box((cx - 0.25, y + 0.02, cz - 0.19), (cx + 0.25, y + 0.05, cz - 0.18))
    ct.tube([(cx + 0.23, 0.80, cz - 0.17), (cx + 0.30, 0.80, cz - 0.17), (cx + 0.30, 0.80, cz + 0.17), (cx + 0.23, 0.80, cz + 0.17)], 0.010, 5)
    make("Center_InstrumentCart", ct, "M_UC_Steel", M, pivot=(cx, 0, cz), role="movable_prop",
         props=dict(uc_note="rolling cart; keep at bed foot, outside aisles"))
    wh = pool("Center", "M_UC_Rubber")
    for sx in (-1, 1):
        for sz in (-1, 1):
            wh.cyl((cx + sx * 0.23, 0.035, cz + sz * 0.17), 0.035, 0.025, axis="x", segs=8)
    bottle("Center", (cx - 0.15, 0.90, cz - 0.08), mat="M_UC_Red")
    bottle("Center", (cx - 0.07, 0.90, cz - 0.10), r=0.03, h=0.16, mat="M_UC_Ivory")
    bottle("Center", (cx + 0.02, 0.90, cz - 0.09), r=0.025, h=0.13, mat="M_UC_Orange")
    pool("Center", "M_UC_DarkSteel").box((cx - 0.02, 0.90, cz - 0.02), (cx + 0.22, 0.93, cz + 0.15))
    for i in range(4):
        pool("Center", "M_UC_Steel").tube([(cx + 0.02 + 0.045 * i, 0.94, cz + 0.0), (cx + 0.04 + 0.045 * i, 0.94, cz + 0.13)], 0.006, 4)
    crate("Center", (cx, 0.57, cz + 0.02), (0.30, 0.12, 0.24), mat="M_UC_Ivory")
    cl = pool("Center", "M_UC_Cloth")
    cl.box((cx + 0.29, 0.50, cz - 0.10), (cx + 0.31, 0.80, cz + 0.10))
    cl.box((cx + 0.30, 0.79, cz - 0.10), (cx + 0.33, 0.81, cz + 0.10))
    rag("Center", (BED_C.x + 0.05, BED_TOP, BED_C.z - 0.70), yaw=15)

    # gantry ring, hangers, carriages, booms, tools
    G = Vector((BED_C.x, RING_Y, BED_C.z))
    rg = MB()
    rg.tube([(G.x + RING_R * math.cos(2 * math.pi * i / 32), RING_Y, G.z + RING_R * math.sin(2 * math.pi * i / 32)) for i in range(32)], 0.055, 6, closed=True)
    rg.tube([(G.x + (RING_R - 0.07) * math.cos(2 * math.pi * i / 32), RING_Y - 0.03, G.z + (RING_R - 0.07) * math.sin(2 * math.pi * i / 32)) for i in range(32)], 0.018, 5, closed=True)
    make("Gantry_Ring", rg, "M_UC_DarkSteel", M, pivot=(G.x, RING_Y, G.z),
         props=dict(uc_note="overhead circular tool gantry; underside %.2f m" % (RING_Y - 0.055)))
    hg = MB()
    for i in range(4):
        a = math.radians(45 + 90 * i)
        p = Vector((G.x + RING_R * math.cos(a), 0, G.z + RING_R * math.sin(a)))
        hg.box((p.x - 0.03, RING_Y + 0.04, p.z - 0.03), (p.x + 0.03, H - 0.02, p.z + 0.03))
        hg.box((p.x - 0.11, H - 0.04, p.z - 0.11), (p.x + 0.11, H, p.z + 0.11))
        hg.box((p.x - 0.06, RING_Y + 0.03, p.z - 0.06), (p.x + 0.06, RING_Y + 0.08, p.z + 0.06))
    make("Gantry_Hangers", hg, "M_UC_DarkSteel", M, pivot=(G.x, H, G.z))
    gc = pool("Center", "M_UC_Rubber")
    for i in range(4):
        a0, a1 = math.radians(45 + 90 * i), math.radians(135 + 90 * i)
        p0 = (G.x + RING_R * math.cos(a0), RING_Y + 0.06, G.z + RING_R * math.sin(a0))
        p1 = (G.x + RING_R * math.cos(a1), RING_Y + 0.06, G.z + RING_R * math.sin(a1))
        gc.tube(catenary(p0, p1, 0.16, 10), 0.016, 5)
        gc.tube([p0, (p0[0], H - 0.05, p0[2])], 0.012, 5)

    tool_defs = [
        # name, carriage angle (deg, from +X toward +Z), tool point (relative to bed centre), tip y
        ("SurgicalLamp", 90, (0.00, 0.15), 1.895),
        ("Drill", 60, (0.18, 0.50), 1.58),
        ("Gripper", 120, (-0.18, 0.50), 1.62),
        ("Scanner", 180, (-0.18, -0.20), 1.74),
        ("Probe", 0, (0.18, -0.20), 1.64),
        ("Cutter", 240, (-0.18, -0.70), 1.62),
        ("Suction", 300, (0.18, -0.70), 1.60),
    ]
    for name, ang, rel, tip in tool_defs:
        a = math.radians(ang)
        cpos = Vector((G.x + RING_R * math.cos(a), 0, G.z + RING_R * math.sin(a)))
        tpt = Vector((BED_C.x + rel[0], 0, BED_C.z + rel[1]))
        dvec = tpt - cpos
        L = dvec.length
        yaw = yaw_of(dvec.x, dvec.z)
        tg_yaw = yaw_of(-math.sin(a), math.cos(a))
        car = MB().obox((cpos.x, RING_Y - 0.01, cpos.z), (0.18, 0.24, 0.16), yaw=tg_yaw)
        car.obox((cpos.x, RING_Y - 0.15, cpos.z), (0.10, 0.06, 0.10), yaw=tg_yaw)
        make("Gantry_Carriage_" + name, car, "M_UC_DarkSteel", M, pivot=(cpos.x, RING_Y, cpos.z))
        boom = MB()
        boom.obox(((cpos.x + tpt.x) / 2, 2.295, (cpos.z + tpt.z) / 2), (0.08, 0.07, L + 0.08), yaw=yaw)
        boom.cyl((cpos.x, 2.30, cpos.z), 0.055, 0.10, segs=8)
        boom.box((tpt.x - 0.045, 2.02, tpt.z - 0.045), (tpt.x + 0.045, 2.33, tpt.z + 0.045))
        bo = make("Gantry_Boom_" + name, boom, "M_UC_Ivory", M, pivot=(cpos.x, 2.30, cpos.z), role="animated",
                  props=dict(uc_anim="rotate", uc_hinge_axis_unity_world=(0.0, 1.0, 0.0), uc_hinge_pivot_unity=r3((cpos.x, 2.30, cpos.z)),
                             uc_note="boom yaws about the carriage; range written by checks (uc_yaw_range_deg)"))
        head = MB()
        sub = []  # (suffix, MB, material)
        top = 2.02
        P = lambda dy, dx=0.0, dz=0.0: Vector((tpt.x + dx, top - dy, tpt.z + dz))
        if name == "SurgicalLamp":
            head.cyl(P(0.03), 0.025, 0.06, segs=6)
            head.cyl(P(0.085), 0.22, 0.05, segs=12, r2=0.12)
            head.tube([P(0.10, -0.22), P(0.10, -0.30)], 0.012, 5)
            lm = MB().cyl(P(tip and (top - 1.9025)), 0.17, 0.005, segs=12)
            sub.append(("Light", lm, "M_UC_LampWarm"))
            mat = "M_UC_DarkSteel"
        elif name == "Drill":
            head.cyl(P(0.15), 0.045, 0.30, segs=8)
            head.obox(P(0.12, 0.06), (0.07, 0.12, 0.08))
            sub.append(("Bit", MB().cyl(P(0.38), 0.010, 0.12, segs=6, r2=0.004), "M_UC_Steel"))
            mat = "M_UC_Ivory"
        elif name == "Gripper":
            head.cyl(P(0.13), 0.035, 0.26, segs=8)
            head.obox(P(0.28), (0.12, 0.05, 0.06))
            head.obox(P(0.35, -0.045), (0.02, 0.10, 0.03))
            head.obox(P(0.35, 0.045), (0.02, 0.10, 0.03))
            mat = "M_UC_DarkSteel"
        elif name == "Scanner":
            head.cyl(P(0.10), 0.03, 0.20, segs=8)
            head.obox(P(0.24), (0.16, 0.08, 0.12))
            sub.append(("Lens", MB().box(P(0.285, -0.05, -0.04), P(0.275, 0.05, 0.04)), "M_UC_Screen"))
            mat = "M_UC_Ivory"
        elif name == "Probe":
            head.tube([P(0.0), P(0.18), P(0.30, 0.03), P(0.38, 0.03)], 0.016, 6)
            sub.append(("Tip", MB().cyl(P(0.36, 0.03), 0.005, 0.04, segs=5), "M_UC_Steel"))
            mat = "M_UC_DarkSteel"
        elif name == "Cutter":
            head.cyl(P(0.14), 0.04, 0.28, segs=8)
            head.obox(P(0.31), (0.05, 0.06, 0.09))
            sub.append(("Blade", MB().cyl(P(0.355), 0.045, 0.006, axis="x", segs=10), "M_UC_Steel"))
            mat = "M_UC_Ivory"
        else:  # Suction
            head.cyl(P(0.15), 0.03, 0.30, segs=8)
            head.cyl(P(0.38), 0.012, 0.16, segs=6, r2=0.008)
            hose = [P(-0.30 + 0.07 * i, 0.06 * math.cos(i * 1.3), 0.06 * math.sin(i * 1.3)) for i in range(9)]
            sub.append(("Hose", MB().tube(hose, 0.014, 5), "M_UC_Rubber"))
            mat = "M_UC_Ivory"
        props = dict(uc_anim="slide+rotate", uc_slide_axis_unity_world=(0.0, -1.0, 0.0),
                     uc_travel_m=round(tip - (BED_TOP_MAX + 0.15), 3), uc_parked_tip_y=tip,
                     uc_note="slides down toward the bed; also follows its boom yaw")
        ho = make("Tool_" + name, head, mat, M, pivot=(tpt.x, top, tpt.z), role="animated", parent=bo, props=props)
        objs = [bo, ho]
        for suf, mb, mt in sub:
            objs.append(make("Tool_%s_%s" % (name, suf), mb, mt, M, pivot=(tpt.x, top, tpt.z), role="animated_child", parent=ho))
        TOOLS[name] = dict(carriage=cpos, point=tpt, tip=tip, objects=objs, boom=bo, head=ho)


# ---------------------------------------------------------------------------
# West: UNIT07 repair zone (dock + robot are existing assets; only surroundings are built here)
# ---------------------------------------------------------------------------
def dock_world(p_native_blender):
    """Existing dock native Blender coords -> Unity world (new placement)."""
    M = unity_placement_matrix(DOCK_T, DOCK_YAW)
    return bu(M @ Vector(p_native_blender))


def build_west():
    M = "UC_Unit07Zone"
    fr = MB()
    for z in (-0.95, 0.95):
        fr.box((-A, 0.0, z - 0.04), (-A + 0.10, 2.15, z + 0.04))
        fr.box((-A, 0.0, z - 0.07), (-A + 0.12, 0.05, z + 0.07))
    fr.box((-A, 2.00, -1.01), (-A + 0.12, 2.15, 1.01))
    fr.box((-A, 1.55, -0.95), (-A + 0.07, 1.60, 0.95))
    for z in (-0.55, 0.55):
        fr.cyl((-A + 0.09, 1.85, z), 0.07, 0.04, axis="x", segs=8)
    make("Unit07_WallFrame", fr, "M_UC_DarkSteel", M, pivot=(-A, 0, 0),
         props=dict(uc_note="wall service frame behind the dock; keeps rear-cassette corridor clear"))
    lamp_c = Vector((-A + 0.16, 1.93, 0.0))
    cg = MB()
    cg.box((-A + 0.08, 1.98, -0.06), (-A + 0.12, 2.00, 0.06))
    for i in range(6):
        a = 2 * math.pi * i / 6
        cg.tube([lamp_c + Vector((0.05 * math.cos(a), 0.06, 0.05 * math.sin(a))), lamp_c + Vector((0.05 * math.cos(a), -0.07, 0.05 * math.sin(a)))], 0.005, 4)
    make("Unit07_CageLampFrame", cg, "M_UC_DarkSteel", M, pivot=lamp_c)
    make("Unit07_CageLamp", MB().cyl(lamp_c, 0.035, 0.10, segs=8), "M_UC_LampWarm", M, pivot=lamp_c, role="status_light",
         props=dict(uc_note="amber cage lamp; dock work light"))
    cb = pool("West", "M_UC_Rubber")
    for z in (-0.90, 0.90):
        cb.tube(catenary((-A + 0.12, 2.05, z * 1.10), (-A + 0.11, 0.05, z * 0.98), 0.0, 6), 0.016, 5)
        cb.tube([(-A + 0.13, 2.10, z), (-A + 0.15, 1.30, z * 1.04), (-A + 0.12, 0.40, z * 1.05), (-A + 0.10, 0.03, z)], 0.012, 5)
    cb.tube(catenary((-A + 0.12, 2.12, -1.00), (-A + 0.12, 2.12, 1.00), 0.25, 10), 0.018, 5)
    decal("Unit07_Schematic", M, (-A + 0.023, 1.80, 0.0), (1, 0, 0), (0, 1, 0), 0.36, 0.48, PAPER_RECTS["schematic"])
    decal("Unit07_Label", M, (-A + 0.123, 2.075, 0.0), (1, 0, 0), (0, 1, 0), 0.40, 0.10, PAPER_RECTS["label_unit07"])
    # parking / work zone floor marks (2 mm, never under the dock feet)
    fm, fo = MB(), MB()
    px0, px1, pz0, pz1 = -3.30, -2.33, -1.00, 1.00
    for (x0, x1, z0, z1) in [(px0, px1, pz0 - 0.05, pz0), (px0, px1, pz1, pz1 + 0.05), (px1, px1 + 0.05, pz0 - 0.05, pz1 + 0.05)]:
        fm.box((x0, 0.0, z0), (x1, 0.002, z1))
    for i in range(8):
        z0 = -0.95 + i * 0.25
        fo.box((-1.68, 0.0, z0), (-1.64, 0.002, z0 + 0.14))
    make("Unit07_ParkingMarks", fm, "M_UC_Hazard", M, role="floor_decal", props=dict(uc_note="parking zone = dock + robot + 0.10 m margin"))
    make("Unit07_WorkZoneMarks", fo, "M_UC_Orange", M, role="floor_decal", props=dict(uc_note="dashed line: front edge of dock work zone"))
    # NW: red rolling tool chest + tall green cabinet
    k = SIDES.index("NW")
    c, t, n_in = side_frame(k)
    rc = MB()
    rc.fbox(c, t, n_in, 0.10, 0.80, 0.08, 1.00, 0.03, 0.48)
    make("Storage_RedToolChest", rc, "M_UC_Red", "UC_Storage", pivot=wall_point(k, 0.45, 0, 0.25), role="static")
    dk = pool("West", "M_UC_DarkSteel")
    for i in range(5):
        y = 0.18 + i * 0.16
        dk.fbox(c, t, n_in, 0.16, 0.74, y + 0.11, y + 0.13, 0.48, 0.495)
        dk.fbox(c, t, n_in, 0.35, 0.55, y + 0.05, y + 0.07, 0.48, 0.51)
    for s in (0.14, 0.76):
        for d in (0.08, 0.43):
            q = wall_point(k, s, 0.04, d)
            pool("West", "M_UC_Rubber").cyl(q, 0.04, 0.03, axis="x", segs=8)
    gc = MB().fbox(c, t, n_in, -0.95, -0.25, 0.0, 1.90, 0.02, 0.47)
    make("Storage_NWCabinet", gc, "M_UC_PaintGreen", "UC_Storage", pivot=wall_point(k, -0.6, 0, 0.25))
    for i in range(3):
        y = 0.30 + i * 0.55
        dk.fbox(c, t, n_in, -0.88, -0.32, y, y + 0.02, 0.47, 0.485)
        dk.fbox(c, t, n_in, -0.68, -0.52, y + 0.20, y + 0.23, 0.47, 0.50)
    decal("Storage_NWCabinet_Note", "UC_Storage", wall_point(k, -0.75, 1.55, 0.475), tuple(n_in), (0, 1, 0), 0.18, 0.18, PAPER_RECTS["note_a"])
    top = wall_point(k, 0.45, 1.00, 0.25)
    crate("West", top, (0.36, 0.14, 0.26), yaw=yaw_of(t.x, t.z), mat="M_UC_Red")
    limb_arm("West", wall_point(k, 0.20, 1.04, 0.30), tuple(t), 0.5)
    bottle("West", wall_point(k, 0.72, 1.00, 0.20), r=0.03, h=0.18, mat="M_UC_Ivory")
    # SW: limb shelf
    k = SIDES.index("SW")
    c, t, n_in = side_frame(k)
    sf = MB()
    for s in (-0.65, 0.65):
        for d in (0.03, 0.43):
            q = wall_point(k, s, 0, d)
            sf.box((q.x - 0.02, 0.0, q.z - 0.02), (q.x + 0.02, 1.90, q.z + 0.02))
    for y in (0.08, 0.50, 0.92, 1.34, 1.76):
        sf.fbox(c, t, n_in, -0.66, 0.66, y, y + 0.025, 0.02, 0.44)
    make("Storage_LimbShelf", sf, "M_UC_Steel", "UC_Storage", pivot=wall_point(k, 0, 0, 0.22))
    ty = yaw_of(t.x, t.z)
    for i, s in enumerate((-0.45, -0.15, 0.15, 0.45)):
        bin_("West", wall_point(k, s, 0.105, 0.24), (0.26, 0.16, 0.32), yaw=ty, mat=["M_UC_PaintGreen", "M_UC_Red", "M_UC_PaintGreen", "M_UC_Ivory"][i])
    for s in (-0.45, -0.15, 0.20, 0.48):
        limb_leg("West", wall_point(k, s, 0.525, 0.22), yaw=ty, h=0.36)
    for s in (-0.40, 0.10, 0.45):
        limb_arm("West", wall_point(k, s - 0.15, 0.99, 0.22), tuple(t), 0.32)
    for s in (-0.42, 0.0, 0.42):
        bin_("West", wall_point(k, s, 1.365, 0.24), (0.34, 0.20, 0.34), yaw=ty, mat="M_UC_Red" if s == 0 else "M_UC_PaintGreen")
    crate("West", wall_point(k, -0.30, 1.785, 0.24), (0.42, 0.18, 0.30), yaw=ty, mat="M_UC_Ivory")
    decal("Storage_LimbShelf_Chart", "UC_Storage", wall_point(k, 0.0, 2.05, 0.023), tuple(n_in), (0, 1, 0), 0.40, 0.40, PAPER_RECTS["chart"])
    # crates beside the dock (outside engine side-pull + side access zones)
    crate("West", (-2.80, 0.0, -1.53), (0.40, 0.40, 0.38), mat="M_UC_PaintGreen")
    crate("West", (-2.78, 0.40, -1.53), (0.34, 0.28, 0.30), yaw=8, mat="M_UC_Ivory")
    decal("Unit07_CrateLabel_OldBrg", M, (-2.60 + 0.012, 0.22, -1.53), (1, 0, 0), (0, 1, 0), 0.24, 0.09, PAPER_RECTS["label_oldbrg"])


# ---------------------------------------------------------------------------
# East: trade counter (V4 placement / orientation), CRT, stool, pads, shelves
# ---------------------------------------------------------------------------
def counter_segment(body, top, panels, hazard, shelf, o, ez, length, cust_sign):
    """Counter along ez from o (centre line). cust_sign: +1 if customer side is +ex, -1 if -ex."""
    ez = Vector(ez).normalized()
    ex = Vector((ez.z, 0, -ez.x)) * (-cust_sign)       # ex points toward the operator side
    # body (customer half), plinth, under-shelf (operator half), end panels
    body.fbox(o, ex, ez, -0.28, 0.0, 0.08, CNT_TOP - 0.05, 0.0, length)
    shelf.fbox(o, ex, ez, -0.26, -0.02, 0.0, 0.08, 0.02, length - 0.02)
    shelf.fbox(o, ex, ez, 0.0, 0.26, 0.30, 0.33, 0.04, length - 0.04)
    body.fbox(o, ex, ez, 0.0, 0.28, 0.0, CNT_TOP - 0.05, length - 0.04, length)
    top.fbox(o, ex, ez, -0.32, 0.32, CNT_TOP - 0.05, CNT_TOP, -0.02, length + 0.02)
    n = max(1, int(length / 0.48))
    for i in range(n):
        b0 = 0.05 + i * (length - 0.10) / n
        b1 = b0 + (length - 0.10) / n - 0.05
        panels.fbox(o, ex, ez, -0.295, -0.28, 0.20, 0.78, b0, b1)
    hazard.fbox(o, ex, ez, -0.30, -0.28, 0.08, 0.16, 0.0, length)


def build_east():
    M = "UC_Trade"
    body, top, panels, hz, shelf = MB(), MB(), MB(), MB(), MB()
    counter_segment(body, top, panels, hz, shelf, (CNT_XM, 0, CNT_Z0), (0, 0, 1), CNT_Z1 - CNT_Z0, -1)
    d = Vector((math.sin(math.radians(135)), 0, math.cos(math.radians(135))))
    counter_segment(body, top, panels, hz, shelf, (CNT_XM, 0, CNT_Z0) , tuple(d), ANGLED_LEN, +1)
    body.obox((CNT_XM, (CNT_TOP - 0.05) / 2, CNT_Z0), (0.40, CNT_TOP - 0.05, 0.40), yaw=22.5)
    top.obox((CNT_XM, CNT_TOP - 0.025, CNT_Z0), (0.62, 0.05, 0.62), yaw=22.5)
    make("Trade_Counter", body, "M_UC_PaintGreen", M, pivot=(CNT_XM, 0, CNT_Z0),
         props=dict(uc_note="customer side faces -X (room); operator side +X with knee space"))
    make("Trade_CounterTop", top, "M_UC_Steel", M, pivot=(CNT_XM, CNT_TOP, CNT_Z0), props=dict(uc_note="counter top y=%.2f, closed solid" % CNT_TOP))
    make("Trade_CounterPanels", panels, "M_UC_Red", M, pivot=(CNT_X0, 0, CNT_Z0))
    make("Trade_CounterHazard", hz, "M_UC_Hazard", M, pivot=(CNT_X0, 0, CNT_Z0), role="decal")
    make("Trade_CounterShelf", shelf, "M_UC_DarkSteel", M, pivot=(CNT_XM, 0, CNT_Z0))
    # CRT: stationary desktop, screen faces +X (operator / east wall), ventilated back faces -X (room)
    crt_c = Vector((1.88, CNT_TOP, 0.0))
    b = MB()
    b.box((1.74, CNT_TOP + 0.05, -0.17), (2.04, CNT_TOP + 0.35, 0.17))
    b.box((1.66, CNT_TOP + 0.09, -0.12), (1.74, CNT_TOP + 0.31, 0.12))
    b.box((2.04, CNT_TOP + 0.05, -0.17), (2.07, CNT_TOP + 0.08, 0.17))
    b.box((2.04, CNT_TOP + 0.32, -0.17), (2.07, CNT_TOP + 0.35, 0.17))
    b.box((2.04, CNT_TOP + 0.05, -0.17), (2.07, CNT_TOP + 0.35, -0.14))
    b.box((2.04, CNT_TOP + 0.05, 0.14), (2.07, CNT_TOP + 0.35, 0.17))
    make("Trade_CRT_Body", b, "M_UC_Ivory", M, pivot=crt_c, role="interactive",
         props=dict(uc_note="stationary desktop CRT; screen normal +X (toward operator), vents on -X face (toward room)"))
    make("Trade_CRT_Base", MB().box((1.76, CNT_TOP, -0.14), (2.02, CNT_TOP + 0.05, 0.14)), "M_UC_DarkSteel", M, pivot=crt_c)
    vt = MB().box((1.652, CNT_TOP + 0.12, -0.09), (1.66, CNT_TOP + 0.28, 0.09))
    for i in range(6):
        vt.box((1.648, CNT_TOP + 0.13 + i * 0.025, -0.085), (1.652, CNT_TOP + 0.142 + i * 0.025, 0.085))
    make("Trade_CRT_BackVent", vt, "M_UC_DarkSteel", M, pivot=(1.656, CNT_TOP + 0.20, 0.0))
    scr_rect = SCREEN_RECTS["trade"]

    def scr_uv(p, nrm):
        s = (p.z / 0.28) + 0.5             # viewer at +X looking -X (Unity, left-handed): right = +Z
        tt = (p.y - (CNT_TOP + 0.20)) / 0.24 + 0.5
        return ((scr_rect[0] + s * (scr_rect[2] - scr_rect[0])) / 256, 1 - (scr_rect[3] - tt * (scr_rect[3] - scr_rect[1])) / 96)
    sc = MB().box((2.04, CNT_TOP + 0.08, -0.14), (2.055, CNT_TOP + 0.32, 0.14))
    make("Trade_CRT_Screen", sc, "M_UC_Screen", M, pivot=(2.055, CNT_TOP + 0.20, 0.0), role="interactive", uv=scr_uv,
         props=dict(uc_screen_normal_unity=(1.0, 0.0, 0.0), uc_note="emissive screen; program writes job / cargo status here"))
    kb = MB().obox((2.14, CNT_TOP + 0.018, 0.0), (0.13, 0.03, 0.38), tilt=0)
    make("Trade_CRT_Keyboard", kb, "M_UC_Ivory", M, pivot=(2.14, CNT_TOP, 0.0), role="interactive")
    pool("East", "M_UC_Rubber").tube([(1.70, CNT_TOP + 0.10, 0.05), (1.62, CNT_TOP + 0.02, 0.10), (1.62, 0.5, 0.15), (1.95, 0.32, 0.25)], 0.010, 5)
    # stool INSIDE the counter (between counter and east wall)
    st = MB()
    for i in range(4):
        a = math.radians(45 + 90 * i)
        st.tube([(STOOL_P.x + 0.06 * math.cos(a), 0.68, STOOL_P.z + 0.06 * math.sin(a)), (STOOL_P.x + 0.17 * math.cos(a), 0.0, STOOL_P.z + 0.17 * math.sin(a))], 0.014, 5)
    st.tube([(STOOL_P.x + 0.13 * math.cos(math.radians(45 + 90 * i)), 0.25, STOOL_P.z + 0.13 * math.sin(math.radians(45 + 90 * i))) for i in range(4)], 0.010, 5, closed=True)
    so = make("Trade_Stool", st, "M_UC_Steel", M, pivot=STOOL_P, role="movable_prop",
              props=dict(uc_note="operator stool; must stay inside trade_operator zone (x 2.235..3.00)"))
    make("Trade_Stool_Seat", MB().cyl((STOOL_P.x, 0.70, STOOL_P.z), 0.18, 0.05, segs=10), "M_UC_Wood", M, pivot=(STOOL_P.x, 0.70, STOOL_P.z), parent=so)
    # pads: incoming next job (south) / outgoing repaired (north), clearly separated by the CRT
    for key, c, frame_mat, lab, lamp_mat in (("Receive", PAD_RECV_C, "M_UC_Orange", "label_in", "M_UC_LampWarm"),
                                             ("Deliver", PAD_DELV_C, "M_UC_Steel", "label_out", "M_UC_Screen")):
        sx, sz = PAD_SIZE
        mat_mb = MB().box((c.x - sx / 2 + 0.03, CNT_TOP, c.z - sz / 2 + 0.03), (c.x + sx / 2 - 0.03, CNT_TOP + 0.006, c.z + sz / 2 - 0.03))
        pad = make("Trade_Pad_" + key, mat_mb, "M_UC_Rubber", M, pivot=(c.x, CNT_TOP + 0.006, c.z), role="logical_pad",
                   props=dict(uc_note="%s pad top surface; cargo spawn anchor at pivot" % ("incoming next-job" if key == "Receive" else "outgoing repaired-object")))
        fr = MB()
        fr.box((c.x - sx / 2, CNT_TOP, c.z - sz / 2), (c.x + sx / 2, CNT_TOP + 0.004, c.z - sz / 2 + 0.03))
        fr.box((c.x - sx / 2, CNT_TOP, c.z + sz / 2 - 0.03), (c.x + sx / 2, CNT_TOP + 0.004, c.z + sz / 2))
        fr.box((c.x - sx / 2, CNT_TOP, c.z - sz / 2), (c.x - sx / 2 + 0.03, CNT_TOP + 0.004, c.z + sz / 2))
        fr.box((c.x + sx / 2 - 0.03, CNT_TOP, c.z - sz / 2), (c.x + sx / 2, CNT_TOP + 0.004, c.z + sz / 2))
        make("Trade_Pad_%s_Frame" % key, fr, frame_mat, M, pivot=(c.x, CNT_TOP, c.z), role="decal", parent=pad)
        zz = c.z + (sz / 2 + 0.06) * (-1 if key == "Receive" else 1)
        decal("Trade_Pad_%s_Label" % key, M, (c.x, CNT_TOP, zz), (0, 1, 0), (-1, 0, 0), 0.30, 0.10, PAPER_RECTS[lab], parent=pad)
        # status pod on the customer edge
        pz = c.z + (sz / 2 - 0.04) * (-1 if key == "Receive" else 1)
        pool("East", "M_UC_DarkSteel").box((1.60, CNT_TOP, pz - 0.02), (1.64, CNT_TOP + 0.20, pz + 0.02))
        pool("East", "M_UC_DarkSteel").box((1.585, CNT_TOP + 0.18, pz - 0.045), (1.655, CNT_TOP + 0.26, pz + 0.045))
        make("Trade_StatusLamp_" + key, MB().box((1.575, CNT_TOP + 0.19, pz - 0.035), (1.585, CNT_TOP + 0.25, pz + 0.035)), lamp_mat, M,
             pivot=(1.58, CNT_TOP + 0.22, pz), role="status_light",
             props=dict(uc_states="EMPTY=off,CARGO=on,READY=blink", uc_note="faces the room (-X); program toggles emission"))
    # counter dressing
    decal("Trade_Ledger", M, (2.10, CNT_TOP, -0.30), (0, 1, 0), (-1, 0, 0), 0.20, 0.20, PAPER_RECTS["ledger"])
    decal("Trade_Note", M, (2.12, CNT_TOP + 0.001, 0.32), (0, 1, 0), (-1, 0, 0), 0.14, 0.14, PAPER_RECTS["note_b"])
    mug("East", (2.15, CNT_TOP, 0.42))
    crate("East", (2.08, CNT_TOP, -0.80), (0.20, 0.07, 0.24), mat="M_UC_Red")
    rag("East", (1.95, CNT_TOP, 0.90), yaw=40)
    for z in (-0.70, -0.30, 0.30, 0.65):
        bin_("East", (2.04, 0.33, z), (0.22, 0.14, 0.26), yaw=90, mat="M_UC_PaintGreen" if z < 0 else "M_UC_Ivory")
    decal("Trade_Sign", M, (CNT_X0 - 0.002, 0.62, 0.0), (-1, 0, 0), (0, 1, 0), 0.30, 0.15, PAPER_RECTS["label_trade"])
    # east wall shelving (behind operator)
    sh = MB()
    for z0, z1 in ((-1.30, -0.05), (0.05, 1.30)):
        for z in (z0 + 0.02, z1 - 0.02):
            for x in (SHELF_X0 + 0.02, A - 0.03):
                sh.box((x - 0.02, 0.0, z - 0.02), (x + 0.02, 2.00, z + 0.02))
        for y in (0.08, 0.50, 0.92, 1.34, 1.76):
            sh.box((SHELF_X0, y, z0), (A - 0.01, y + 0.025, z1))
    make("Storage_EastShelves", sh, "M_UC_Steel", "UC_Storage", pivot=(SHELF_X0, 0, 0))
    mats = ["M_UC_PaintGreen", "M_UC_Red", "M_UC_Ivory", "M_UC_PaintGreen"]
    for zi, z in enumerate((-1.10, -0.80, -0.50, -0.22, 0.25, 0.55, 0.85, 1.12)):
        bin_("East", (3.19, 0.105, z), (0.30, 0.18, 0.24), yaw=-90, mat=mats[zi % 4])
        bin_("East", (3.19, 1.365, z), (0.28, 0.16, 0.22), yaw=-90, mat=mats[(zi + 1) % 4])
    for z in (-1.05, -0.65, 0.35, 0.80):
        limb_leg("East", (3.19, 0.525, z), yaw=-90, h=0.36)
    for z in (-0.95, -0.35, 0.45, 1.05):
        limb_arm("East", (3.34, 1.02, z), (-1, 0, 0), 0.30)
    for z in (-0.90, 0.0, 0.90):
        crate("East", (3.20, 1.785, z), (0.34, 0.16, 0.36), mat="M_UC_Ivory" if z == 0 else "M_UC_PaintGreen")
    decal("Trade_ShelfLabel", M, (SHELF_X0 - 0.002, 0.95, 0.70), (-1, 0, 0), (0, 1, 0), 0.20, 0.07, PAPER_RECTS["label_parts"])
    # NE tall cabinet (keeps operator entry clear)
    k = SIDES.index("NE")
    c, t, n_in = side_frame(k)
    nc = MB().fbox(c, t, n_in, 0.25, 0.95, 0.0, 1.95, 0.02, 0.47)
    make("Storage_NECabinet", nc, "M_UC_PaintGreen", "UC_Storage", pivot=wall_point(k, 0.6, 0, 0.25))
    dk = pool("East", "M_UC_DarkSteel")
    for i in range(4):
        y = 0.25 + i * 0.42
        dk.fbox(c, t, n_in, 0.30, 0.90, y, y + 0.02, 0.47, 0.485)
        dk.fbox(c, t, n_in, 0.52, 0.68, y + 0.18, y + 0.21, 0.47, 0.50)
    hz3 = MB().fbox(c, t, n_in, 0.25, 0.95, 0.0, 0.10, 0.475, 0.48)
    make("Storage_NECabinet_Hazard", hz3, "M_UC_Hazard", "UC_Storage", pivot=wall_point(k, 0.6, 0, 0.48), role="decal")
    # SE enclosure: cargo crates behind the angled counter
    crate("East", (2.78, 0.0, -1.18), (0.44, 0.38, 0.34), yaw=0, mat="M_UC_PaintGreen")
    crate("East", (2.80, 0.38, -1.18), (0.36, 0.26, 0.30), yaw=-6, mat="M_UC_Ivory")
    decal("Trade_CargoLabel", M, (2.56 - 0.012, 0.20, -1.18), (-1, 0, 0), (0, 1, 0), 0.22, 0.08, PAPER_RECTS["label_cargo"])


# ---------------------------------------------------------------------------
# South (entrance flanks) and north (around the existing bench)
# ---------------------------------------------------------------------------
def build_south_north():
    S = "UC_Storage"
    lk = MB()
    lk.box((0.96, 0.0, -A + 0.01), (1.38, 1.86, -A + 0.46))
    make("Storage_Lockers", lk, "M_UC_PaintGreen", S, pivot=(1.17, 0, -A + 0.235))
    dk = pool("South", "M_UC_DarkSteel")
    dk.box((1.165, 0.05, -A + 0.46), (1.175, 1.82, -A + 0.47))
    for x in (1.05, 1.29):
        for i in range(5):
            dk.box((x - 0.06, 1.45 + i * 0.05, -A + 0.46), (x + 0.06, 1.47 + i * 0.05, -A + 0.47))
        dk.box((x + 0.03, 0.95, -A + 0.46), (x + 0.05, 1.10, -A + 0.49))
    decal("Storage_Lockers_Photo", S, (1.06, 1.25, -A + 0.465), (0, 0, 1), (0, 1, 0), 0.07, 0.07, PAPER_RECTS["photo"])
    # coat hooks + coats, bin, extinguisher (west of the door)
    dk.box((-1.32, 1.66, -A + 0.0), (-0.86, 1.70, -A + 0.04))
    for x in (-1.22, -1.06, -0.94):
        dk.tube([(x, 1.68, -A + 0.04), (x, 1.68, -A + 0.10), (x, 1.72, -A + 0.11)], 0.008, 4)
    cl = pool("South", "M_UC_Cloth")
    for x, ln, w in ((-1.18, 0.95, 0.30), (-0.98, 0.72, 0.24)):
        cl.prism([(x - w / 2, -A + 0.04), (x + w / 2, -A + 0.04), (x + w / 2 + 0.03, -A + 0.16), (x - w / 2 - 0.03, -A + 0.16)], 1.68 - ln, 1.68)
        cl.tube([(x - w / 2 - 0.02, 1.62, -A + 0.12), (x - w / 2 - 0.05, 1.68 - ln * 0.6, -A + 0.13)], 0.04, 6)
    pool("South", "M_UC_PaintGreen").cyl((-1.12, 0.22, -A + 0.30), 0.17, 0.44, segs=10, r2=0.19)
    pool("South", "M_UC_Red").cyl((-0.80, 0.42, -A + 0.09), 0.065, 0.42, segs=8)
    pool("South", "M_UC_DarkSteel").cyl((-0.80, 0.68, -A + 0.09), 0.03, 0.10, segs=6)
    decal("South_NoteBoard", S, (-1.10, 1.95, -A + 0.023), (0, 0, 1), (0, 1, 0), 0.40, 0.26, PAPER_RECTS["note_a"])
    # SE inner corner wall notes (visible from the door) and NW poster
    decal("Wall_SE_Notes", S, wall_point(SIDES.index("SE"), 0.6, 1.55, 0.023), tuple(side_frame(SIDES.index("SE"))[2]), (0, 1, 0), 0.24, 0.24, PAPER_RECTS["note_b"])
    decal("Wall_W_Tag", S, wall_point(SIDES.index("W"), -1.1, 1.40, 0.023), (1, 0, 0), (0, 1, 0), 0.10, 0.10, PAPER_RECTS["tag"])
    # floor bins at the north corners (outside the bench stand zone)
    bin_("North", (-1.25, 0.0, 2.75), (0.30, 0.34, 0.30), yaw=45, mat="M_UC_PaintGreen")
    crate("North", (1.30, 0.0, 2.42), (0.34, 0.30, 0.30), yaw=-45, mat="M_UC_Red")
    pool("North", "M_UC_Rubber").tube(catenary((-1.30, 2.40, 3.30), (1.30, 2.40, 3.30), 0.14, 10), 0.014, 5)


# ---------------------------------------------------------------------------
# Adjustable articulated monitor arm (rear-mounted on the north wall beside the bench pegboard)
# ---------------------------------------------------------------------------
ARM = {}


def build_monitor_arm():
    M = "UC_MonitorArm"
    S = Vector((1.00, 1.45, 3.27))     # shoulder (yaw axis)
    E = Vector((0.75, 1.45, 2.98))     # elbow (yaw axis)
    W = Vector((0.30, 1.45, 3.20))     # wrist (yaw axis)
    T = Vector((0.30, 1.37, 3.17))     # monitor tilt pivot (horizontal, unity +X in default pose)
    rail = MB()
    rail.box((S.x - 0.035, 0.98, A - 0.04), (S.x + 0.035, 2.02, A))
    rail.box((S.x - 0.06, 0.95, A - 0.012), (S.x + 0.06, 1.02, A))
    rail.box((S.x - 0.06, 1.98, A - 0.012), (S.x + 0.06, 2.05, A))
    root = make("WB_MonitorArm", rail, "M_UC_DarkSteel", M, pivot=(S.x, 0.98, A), role="static",
                props=dict(uc_note="wall rail; root of the reusable monitor-support asset. Mount plate on wall plane z=%.2f" % A))
    car = MB().box((S.x - 0.05, S.y - 0.09, S.z - 0.02), (S.x + 0.05, S.y + 0.09, A - 0.04))
    car.cyl(S, 0.045, 0.10, segs=8)
    carriage = make("Arm_Carriage", car, "M_UC_DarkSteel", M, pivot=S, role="animated", parent=root,
                    props=dict(uc_anim="slide", uc_slide_axis_unity_world=(0.0, 1.0, 0.0), uc_note="height adjust on the rail"))
    l1 = MB()
    l1.obox(((S.x + E.x) / 2, S.y + 0.0, (S.z + E.z) / 2), (0.07, 0.06, (E - S).length), yaw=yaw_of(E.x - S.x, E.z - S.z))
    l1.cyl(E, 0.04, 0.09, segs=8)
    link1 = make("Arm_Link1", l1, "M_UC_Ivory", M, pivot=S, role="animated", parent=carriage,
                 props=dict(uc_anim="rotate", uc_hinge_axis_unity_world=(0.0, 1.0, 0.0), uc_hinge_pivot_unity=r3(S)))
    l2 = MB()
    l2.obox(((E.x + W.x) / 2, E.y + 0.055, (E.z + W.z) / 2), (0.06, 0.05, (W - E).length), yaw=yaw_of(W.x - E.x, W.z - E.z))
    l2.cyl(E + Vector((0, 0.055, 0)), 0.035, 0.06, segs=8)
    l2.cyl(W + Vector((0, 0.055, 0)), 0.035, 0.06, segs=8)
    link2 = make("Arm_Link2", l2, "M_UC_Ivory", M, pivot=E, role="animated", parent=link1,
                 props=dict(uc_anim="rotate", uc_hinge_axis_unity_world=(0.0, 1.0, 0.0), uc_hinge_pivot_unity=r3(E)))
    hd = MB()
    hd.cyl(W, 0.03, 0.06, segs=8)
    hd.box((W.x - 0.02, T.y - 0.01, W.z - 0.02), (W.x + 0.02, W.y, W.z + 0.02))
    hd.cyl(T, 0.022, 0.16, axis="x", segs=8)
    head = make("Arm_Head", hd, "M_UC_DarkSteel", M, pivot=W, role="animated", parent=link2,
                props=dict(uc_anim="rotate", uc_hinge_axis_unity_world=(0.0, 1.0, 0.0), uc_hinge_pivot_unity=r3(W)))
    mn = MB()
    mc = Vector((0.30, 1.36, 3.04))
    mn.box((mc.x - 0.20, mc.y - 0.15, mc.z - 0.07), (mc.x + 0.20, mc.y + 0.15, mc.z + 0.06))
    mn.box((mc.x - 0.15, mc.y - 0.11, mc.z + 0.06), (mc.x + 0.15, mc.y + 0.11, T.z - 0.02))
    mn.box((mc.x - 0.06, T.y - 0.03, T.z - 0.04), (mc.x + 0.06, T.y + 0.03, T.z))
    mon = make("Arm_Monitor", mn, "M_UC_Ivory", M, pivot=T, role="animated", parent=head,
               props=dict(uc_anim="rotate", uc_hinge_axis_unity_world=(1.0, 0.0, 0.0), uc_hinge_pivot_unity=r3(T),
                          uc_note="screen faces -Z (toward the player at the bench) in the default pose"))
    sr = SCREEN_RECTS["arm"]

    def uvf(p, nrm):
        s = (p.x - (mc.x - 0.16)) / 0.32
        tt = (p.y - (mc.y - 0.12)) / 0.24
        return ((sr[0] + s * (sr[2] - sr[0])) / 256, 1 - (sr[3] - tt * (sr[3] - sr[1])) / 96)
    sc = MB().box((mc.x - 0.16, mc.y - 0.12, mc.z - 0.082), (mc.x + 0.16, mc.y + 0.12, mc.z - 0.07))
    make("Arm_Monitor_Screen", sc, "M_UC_Screen", M, pivot=T, role="animated_child", parent=mon, uv=uvf)
    cbl = MB()
    cbl.tube([S + Vector((0, 0.05, 0.02)), E + Vector((0.0, 0.10, 0.0)), W + Vector((0, 0.10, 0.0)), T + Vector((0, 0.02, 0.05))], 0.008, 5)
    make("Arm_Cable", cbl, "M_UC_Rubber", M, pivot=S, role="animated_child", parent=link1,
         props=dict(uc_note="cosmetic; follows Link1 only (skin it or hide while animating)"))
    ARM.update(S=S, E=E, W=W, T=T, root=root, carriage=carriage, link1=link1, link2=link2, head=head, monitor=mon)


# ---------------------------------------------------------------------------
# Anchors (empties in FBX + JSON/CSV)
# ---------------------------------------------------------------------------
ANCHORS = []


def look_euler(pos, target):
    d = Vector(target) - Vector(pos)
    yaw = yaw_of(d.x, d.z)
    pitch = -math.degrees(math.atan2(d.y, math.hypot(d.x, d.z)))
    return pitch, yaw


def anchor(name, pos, yaw=0.0, kind="anchor", note="", pitch=None, extra=None):
    e = empty("ANCHOR_" + name, "UC_Anchors", pos, yaw=yaw, props=dict(uc_role="anchor", uc_kind=kind, uc_yaw_unity=yaw, uc_note=note))
    rec = dict(name=name, kind=kind, unity_pos=r3(pos), unity_yaw_deg=round(yaw, 2), note=note)
    if pitch is not None:
        rec["unity_euler_deg"] = [round(pitch, 2), round(yaw, 2), 0.0]
    if extra:
        rec.update(extra)
    ANCHORS.append(rec)
    return e


CAMS = {   # name: (pos, target, lens_mm or ortho scale, kind)
    "Cam_Overview": ((2.6, 2.75, -2.9), (-0.4, 0.7, 0.6), 16, "persp"),
    "Cam_Entrance": ((0.0, 1.62, -3.10), (0.0, 1.05, 2.2), 18, "persp"),
    "Cam_Bench": ((0.20, 1.70, 1.45), (0.25, 1.05, 3.05), 22, "persp"),
    "Cam_Dock": ((-1.30, 1.70, -0.90), (-2.75, 1.00, 0.10), 20, "persp"),
    "Cam_Surgery": ((-1.05, 1.75, -1.75), (-0.05, 1.05, -0.05), 20, "persp"),
    "Cam_TradeOperator": ((2.58, 1.25, 0.0), (-1.0, 0.95, 0.0), 22, "persp"),
}


def build_anchors():
    bench_stand = (0.0, 0.0, 1.95)
    anchor("Entrance", (0.0, 0.0, -A + 0.15), 0.0, note="inside the door threshold, facing north")
    anchor("PlayerStart", (0.0, 0.0, -2.60), 0.0, note="player spawn, facing north")
    anchor("Workbench", bench_stand, 0.0, note="player stand point at bench front (faces north)")
    anchor("WorkbenchArea_Root", BENCH_T, BENCH_YAW, kind="existing_asset_root",
           note="WorkbenchArea prefab root: position, rotation = Euler(0,180,0) * importRotation(-90,0,0); hide Room_* (see checks)")
    anchor("Dock_Root", DOCK_T, DOCK_YAW, kind="existing_asset_root",
           note="Unit07ServiceDock prefab root: rotation = Euler(0,90,0) * importRotation(-90,0,0)")
    anchor("DockRobot", (DOCK_T[0], 0.72, DOCK_T[2]), DOCK_YAW, kind="existing_asset_anchor",
           note="UNIT07_RobotV4 root on Dock_RobotAnchor; robot front faces +X (yaw 90)")
    anchor("DockWorkStand", (-1.95, 0.0, 0.0), -90.0, note="player stand in front of the robot, facing west")
    anchor("SurgeryBed", (BED_C.x, BED_TOP, BED_C.z), 0.0, note="bed pad top centre; head end = +Z")
    anchor("SurgeryStand_W", (BED_C.x - 0.75, 0.0, BED_C.z), 90.0, note="surgeon stand west of bed (facing east)")
    anchor("SurgeryStand_E", (BED_C.x + 0.62, 0.0, BED_C.z), -90.0, note="surgeon stand east of bed (facing west)")
    anchor("TradeOperator", (STOOL_P.x, 0.72, STOOL_P.z), -90.0, note="stool seat top; operator faces west toward the CRT")
    anchor("TradeOperatorEye", (STOOL_P.x, 1.25, STOOL_P.z), -90.0, note="seated eye point")
    anchor("TradeCustomer", (1.35, 0.0, 0.0), 90.0, note="customer stand in front of the counter (room side), facing east")
    anchor("TradeReceive", (PAD_RECV_C.x, CNT_TOP + 0.006, PAD_RECV_C.z), -90.0, kind="logical_pad",
           note="incoming next-job pad top centre (EMPTY / CARGO states)", extra=dict(size_xz=list(PAD_SIZE)))
    anchor("TradeDeliver", (PAD_DELV_C.x, CNT_TOP + 0.006, PAD_DELV_C.z), -90.0, kind="logical_pad",
           note="outgoing repaired-object pad top centre (EMPTY / CARGO states)", extra=dict(size_xz=list(PAD_SIZE)))
    anchor("TradeScreen", (2.055, CNT_TOP + 0.20, 0.0), 90.0, note="CRT screen centre, normal +X")
    for name, t in TOOLS.items():
        anchor("ToolPark_" + name, (t["point"].x, t["tip"], t["point"].z), 0.0, kind="tool_park", note="parked tool tip")
    for nm, (pos, tgt, lens, kind) in CAMS.items():
        p, y = look_euler(pos, tgt)
        anchor(nm, pos, y, kind="camera", pitch=p, note="look-at %s, %s %smm (Blender lens; Unity vertical FOV in extra)" % (r3(tgt), kind, lens),
               extra=dict(look_at=r3(tgt), blender_lens_mm=lens, unity_vertical_fov_deg=round(math.degrees(2 * math.atan(36 / 1.6 / 2 / lens)), 1)))


# Legacy poses from Unit07_Night.unity (read-only scan, tools/unit07_night_scan.txt): position (x,y,z), rotation (x,y,z,w)
LEGACY = {
    "bench": dict(T=(0.0, 0.0, 0.0), yaw=0.0, poses={
        "CamPose_Bench": ((0.0, 1.62, 0.62), (-0.020725572, 0.95468664, -0.2888815, -0.068493225)),
        "CamPose_Record": ((0.03697444, 1.1345022, -0.30164552), (-0.0, 0.83308375, -0.55314684, 0.0)),
        "CamPose_Compare": ((-0.578267, 1.1739128, -0.06854348), (-0.0, 0.8977281, -0.44055003, 0.0)),
        "FO_Drop_Mat": ((0.03527105, 0.9189998, -0.45024425), (0, 0, 0, 1)),
        "FO_Drop_PartsTray": ((-0.5065, 0.9189999, -0.25104177), (0, 0, 0, 1)),
        "RP_Bench": ((0.03527105, 1.2039998, -0.45024425), (0, 0, 0, 1)),
        "Bench_WorkLamp_Spot": ((0.10989526, 1.4072456, -0.544843), (0.5888692, -0.22844161, 0.17829508, 0.75449216)),
        "Bench_UnderShelfFill": ((-0.53, 1.62, -0.8), (0.47520694, 0.0, 0.0, 0.8798741)),
    }),
    "dock": dict(T=(0.375, 0.0, 0.825), yaw=-75.0, poses={
        "CamPose_Dock": ((-0.45043355, 1.35, 0.06266203), (0.18364388, 0.44357705, -0.093392596, 0.872234)),
        "CamPose_EngineL": ((-0.24551743, 1.6018267, 0.16950184), (0.24805695, 0.47905043, -0.14321576, 0.8297395)),
        "CamPose_EngineR": ((0.16025907, 1.6026961, 1.7043047), (0.07413394, 0.9254542, -0.27667165, 0.24797472)),
        "CamPose_EngineClose": ((0.13288943, 1.4732442, 0.38673133), (0.42057046, 0.43708137, -0.24281648, 0.75704724)),
        "CamPose_EngineRear": ((0.66762906, 1.5818267, -0.054469615), (0.26013187, -0.28612527, 0.08102228, 0.9186399)),
        "RP_EngineBay": ((0.26616782, 1.5980885, 0.46102732), (0, 0, 0, 1)),
        "Dock_ServiceLight": ((-0.10195698, 2.3, 0.5904536), (0.44999, 0.5146853, -0.36723155, 0.6306735)),
        "Dock_InspectionFill": ((-0.16684127, 1.806831, 0.45385957), (0.32682884, 0.6232059, -0.32146353, 0.6336075)),
    }),
}


def qmul(a, b):   # (x,y,z,w) Hamilton product, same as Unity Quaternion * Quaternion
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def legacy_remap():
    out = {}
    new = {"bench": (BENCH_T, BENCH_YAW), "dock": (DOCK_T, DOCK_YAW)}
    for grp, g in LEGACY.items():
        T_new, yaw_new = new[grp]
        d = yaw_new - g["yaw"]
        qy = (0.0, math.sin(math.radians(d) / 2), 0.0, math.cos(math.radians(d) / 2))
        for nm, (p, q) in g["poses"].items():
            rel = Vector(p) - Vector(g["T"])
            pn = rot_y(rel, d) + Vector(T_new)
            out[nm] = dict(attached_to=grp, old_pos=r3(p), old_rot_xyzw=r3(q), new_pos=r3(pn), new_rot_xyzw=r3(qmul(qy, q)),
                           delta_yaw_deg=round(d, 2))
    return out


# ---------------------------------------------------------------------------
# Collision proxy suggestions
# ---------------------------------------------------------------------------
def unity_bounds(objs, yaw=0.0):
    pts = []
    for o in objs:
        for v in o.data.vertices:
            pts.append(rot_y(bu(o.matrix_world @ v.co), -yaw))
    lo = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    return lo, hi


def build_collision():
    M = "UC_Collision"
    O = bpy.data.objects
    groups = {
        "COL_Bed": (["Bed_Base", "Bed_Shell", "Bed_Pads", "Bed_Rails"], 0.0),
        "COL_InstrumentCart": (["Center_InstrumentCart"], 0.0),
        "COL_Counter_Long": (None, 0.0),
        "COL_Counter_Angled": (None, 135.0),
        "COL_EastShelves": (["Storage_EastShelves"], 0.0),
        "COL_Lockers": (["Storage_Lockers"], 0.0),
        "COL_RedToolChest": (["Storage_RedToolChest"], 135.0 - 180.0),
        "COL_NWCabinet": (["Storage_NWCabinet"], 135.0 - 180.0),
        "COL_NECabinet": (["Storage_NECabinet"], 45.0),
        "COL_LimbShelf": (["Storage_LimbShelf"], 45.0),
        "COL_Unit07Frame": (["Unit07_WallFrame"], 0.0),
        "COL_Stool": (["Trade_Stool", "Trade_Stool_Seat"], 0.0),
        "COL_DoorLeaf_L": (["Door_Leaf_L"], 0.0),
        "COL_DoorLeaf_R": (["Door_Leaf_R"], 0.0),
    }
    out = []
    for nm, (objs, yaw) in groups.items():
        if nm == "COL_Counter_Long":
            lo, hi = Vector((CNT_TOP_X0, 0, CNT_Z0)), Vector((CNT_TOP_X1, CNT_TOP, CNT_Z1 + 0.02))
            c = (lo + hi) / 2
            size = hi - lo
        elif nm == "COL_Counter_Angled":
            dd = Vector((math.sin(math.radians(135)), 0, math.cos(math.radians(135))))
            c = Vector((CNT_XM, CNT_TOP / 2, CNT_Z0)) + dd * (ANGLED_LEN / 2)
            size = Vector((0.64, CNT_TOP, ANGLED_LEN + 0.04))
        else:
            lo, hi = unity_bounds([O[n] for n in objs], yaw)
            c = rot_y((lo + hi) / 2, yaw)
            size = hi - lo
        mb = MB().obox(c, size, yaw=yaw)
        make(nm, mb, "M_UC_DarkSteel", M, pivot=c, role="collision_proxy", props=dict(uc_yaw_unity=yaw))
        out.append(dict(name=nm, center=r3(c), size=r3(size), yaw_deg=yaw))
    for k, nm in enumerate(SIDES):
        st = MB()
        if nm == "S":
            half_open = DOOR_CLEAR / 2
            wall_piece(st, k, -HALF_SIDE, -half_open, 0, H, True, False)
            wall_piece(st, k, half_open, HALF_SIDE, 0, H, False, True)
        else:
            wall_piece(st, k, -HALF_SIDE, HALF_SIDE, 0, H, True, True)
        make("COL_Wall_" + nm, st, "M_UC_DarkSteel", M, pivot=wall_point(k, 0, 0, 0), role="collision_proxy")
        out.append(dict(name="COL_Wall_" + nm, note="wall prism, inner face at apothem %.2f%s" % (A, " (door gap 1.20 m)" if nm == "S" else "")))
    make("COL_Floor", MB().prism(octo((A + WT) / math.cos(math.radians(22.5))), -0.10, 0.0), "M_UC_DarkSteel", M, pivot=(0, 0, 0), role="collision_proxy")
    out.append(dict(name="COL_Floor", note="octagon slab top y=0"))
    return out


# ---------------------------------------------------------------------------
# Geometry helpers for checks
# ---------------------------------------------------------------------------
def world_tris_unity(objs):
    """-> (V (n,3) unity coords, T (m,3) int, owner list per tri)"""
    Vs, Ts, owners = [], [], []
    off = 0
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        mw = o.matrix_world
        co = np.array([tuple(mw @ v.co) for v in me.vertices]) if len(me.vertices) else np.zeros((0, 3))
        if not len(co):
            continue
        u = np.stack([-co[:, 0], co[:, 2], -co[:, 1]], 1)
        tri = np.array([tuple(t.vertices) for t in me.loop_triangles], dtype=np.int64)
        if not len(tri):
            continue
        Vs.append(u)
        Ts.append(tri + off)
        owners += [o.name] * len(tri)
        off += len(u)
    if not Vs:
        return np.zeros((0, 3)), np.zeros((0, 3), dtype=np.int64), []
    return np.concatenate(Vs), np.concatenate(Ts), owners


GRID = 0.02
G0 = -4.0
GN = int(8.0 / GRID)


def raster(objs, band=(0.03, HEAD_CLEAR)):
    occ = np.zeros((GN, GN), bool)
    V, T, _ = world_tris_unity(objs)
    for tri in T:
        p = V[tri]
        if p[:, 1].max() < band[0] or p[:, 1].min() > band[1]:
            continue
        xz = p[:, [0, 2]]
        # edges (covers vertical faces)
        for a, b in ((0, 1), (1, 2), (2, 0)):
            n = int(np.linalg.norm(xz[b] - xz[a]) / (GRID * 0.5)) + 2
            s = np.linspace(0, 1, n)[:, None]
            q = xz[a] + (xz[b] - xz[a]) * s
            ij = np.floor((q - G0) / GRID).astype(int)
            ok = (ij >= 0).all(1) & (ij < GN).all(1)
            occ[ij[ok, 1], ij[ok, 0]] = True
        lo = np.floor((xz.min(0) - G0) / GRID).astype(int)
        hi = np.floor((xz.max(0) - G0) / GRID).astype(int)
        if (hi - lo).max() < 2:
            continue
        lo = np.clip(lo, 0, GN - 1)
        hi = np.clip(hi, 0, GN - 1)
        xs = G0 + (np.arange(lo[0], hi[0] + 1) + 0.5) * GRID
        zs = G0 + (np.arange(lo[1], hi[1] + 1) + 0.5) * GRID
        X, Z = np.meshgrid(xs, zs)
        (x1, z1), (x2, z2), (x3, z3) = xz
        den = (z2 - z3) * (x1 - x3) + (x3 - x2) * (z1 - z3)
        if abs(den) < 1e-10:
            continue
        l1 = ((z2 - z3) * (X - x3) + (x3 - x2) * (Z - z3)) / den
        l2 = ((z3 - z1) * (X - x3) + (x1 - x3) * (Z - z3)) / den
        inside = (l1 >= -1e-6) & (l2 >= -1e-6) & (1 - l1 - l2 >= -1e-6)
        occ[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] |= inside
    return occ


def raster_zone(occ, x0, x1, z0, z1):
    i0, i1 = int((x0 - G0) / GRID), int((x1 - G0) / GRID)
    j0, j1 = int((z0 - G0) / GRID), int((z1 - G0) / GRID)
    occ[j0:j1 + 1, i0:i1 + 1] = True


def occ_at(occ, x, z):
    i, j = int((x - G0) / GRID), int((z - G0) / GRID)
    if i < 0 or j < 0 or i >= GN or j >= GN:
        return True
    return occ[j, i]


def path_widths(occ, pts, step=0.05):
    rows = []
    for a, b in zip(pts[:-1], pts[1:]):
        a, b = np.array(a, float), np.array(b, float)
        L = np.linalg.norm(b - a)
        d = (b - a) / L
        n = np.array([-d[1], d[0]])
        for s in np.arange(0, L + 1e-9, step):
            p = a + d * s
            if occ_at(occ, *p):
                rows.append(dict(p=r3(p), width=0.0, left=0.0, right=0.0, blocked=True))
                continue
            w = []
            for sg in (1, -1):
                t = 0.0
                while t < 4.0 and not occ_at(occ, *(p + n * sg * (t + GRID / 2))):
                    t += GRID / 2
                w.append(t)
            rows.append(dict(p=r3(p), width=round(w[0] + w[1], 3), left=round(w[0], 3), right=round(w[1], 3), blocked=False))
    return rows


def bvh_of(objs):
    verts, polys, owner = [], [], []
    for o in objs:
        mw = o.matrix_world
        off = len(verts)
        verts += [mw @ v.co for v in o.data.vertices]
        for p in o.data.polygons:
            polys.append([off + i for i in p.vertices])
            owner.append(o.name)
    return (BVHTree.FromPolygons(verts, polys, epsilon=0.0) if polys else None), owner, verts


def overlap_names(objs_a, objs_b):
    ta, oa, _ = bvh_of(objs_a)
    tb, ob, _ = bvh_of(objs_b)
    if ta is None or tb is None:
        return []
    pairs = ta.overlap(tb)
    return sorted({(oa[i], ob[j]) for i, j in pairs})


def floor_contacts(pairs, objs, tol=0.02):
    """Split pairs into (clashes, resting contacts): an existing part touching Floor_Main whose lowest point is at most
    `tol` below the floor top (y=0) rests on the floor; deeper penetration stays a clash."""
    by = {o.name: o for o in objs}
    clash, rest = [], []
    for a, b in pairs:
        if b == "Floor_Main" and a in by and unity_bounds([by[a]])[0].y >= -tol:
            rest.append((a, round(unity_bounds([by[a]])[0].y, 4)))
        else:
            clash.append((a, b))
    return clash, rest


def obb_mesh_blender(center_u, size_u, yaw):
    mb = MB().obox(center_u, size_u, yaw=yaw)
    verts = [ub(v) for v in mb.v]
    return verts, mb.f


def obb_hits(center_u, size_u, yaw, objs, shrink=0.0005):
    """Objects whose surface crosses the OBB or which have vertices inside it."""
    size_s = tuple(max(s - 2 * shrink, 1e-4) for s in size_u)
    verts, faces = obb_mesh_blender(center_u, size_s, yaw)
    tb = BVHTree.FromPolygons(verts, faces, epsilon=0.0)
    hits = set()
    c = Vector(center_u)
    hx, hy, hz = (s / 2 for s in size_s)
    min_gap = 1e9
    for o in objs:
        t, own, vs = bvh_of([o])
        if t is None:
            continue
        if tb.overlap(t):
            hits.add(o.name)
            continue
        for v in vs:
            q = rot_y(bu(v) - c, -yaw)
            if abs(q.x) <= hx and abs(q.y) <= hy and abs(q.z) <= hz:
                hits.add(o.name)
                break
            gx, gy, gz = max(abs(q.x) - hx, 0), max(abs(q.y) - hy, 0), max(abs(q.z) - hz, 0)
            min_gap = min(min_gap, math.sqrt(gx * gx + gy * gy + gz * gz))
    return sorted(hits), (round(min_gap, 4) if min_gap < 1e8 else None)


def env_objects():
    return [o for o in COLL["UC_ALL"].all_objects if o.type == "MESH" and o.users_collection[0].name not in ("UC_Collision",)]


# ---------------------------------------------------------------------------
# Existing assets (read-only import for checks)
# ---------------------------------------------------------------------------
EXISTING = {}


def import_existing():
    coll = bpy.data.collections.new("_CheckExisting")
    bpy.context.scene.collection.children.link(coll)
    COLL["_CheckExisting"] = coll
    report = {}
    placements = {"dock": (DOCK_T, DOCK_YAW), "robot": (DOCK_T, DOCK_YAW), "bench": (BENCH_T, BENCH_YAW)}
    for key, path in EXISTING_FBX.items():
        if not os.path.exists(path):
            report[key] = dict(found=False, path=path)
            continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y")
        new = [o for o in bpy.data.objects if o not in before]
        for o in new:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            coll.objects.link(o)
        Mx = unity_placement_matrix(*placements[key])
        for o in new:
            if o.parent is None:
                o.matrix_world = Mx @ o.matrix_world
        bpy.context.view_layer.update()
        EXISTING[key] = [o for o in new if o.type == "MESH"]
        lo, hi = unity_bounds(EXISTING[key])
        report[key] = dict(found=True, path=os.path.relpath(path, HERE), objects=len(new), unity_bounds_after_placement=[r3(lo), r3(hi)],
                           placement=dict(pos=list(placements[key][0]), yaw_deg=placements[key][1]))
    return report


def delete_existing():
    coll = COLL.pop("_CheckExisting", None)
    if coll is None:
        return
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(coll)
    for _ in range(3):
        bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------
def run_checks(existing_report):
    C = dict(existing_assets=existing_report, checks=[])
    add = lambda name, passed, **kw: C["checks"].append(dict(name=name, passed=bool(passed), **kw))
    O = bpy.data.objects
    env = env_objects()
    env_struct = [o for o in env if OBJ_INFO.get(o.name, {}).get("role") not in ("floor_decal",)]
    bench_all = EXISTING.get("bench", [])
    bench_room = [o for o in bench_all if o.name.startswith("Room_")]
    # bench objects that were attached to the OLD side walls / old room (outside the bench footprint) must be hidden
    bench_keep, bench_hide = [], []
    for o in bench_all:
        if o in bench_room:
            continue
        lo, hi = unity_bounds([o])
        if lo.x < -1.0 or hi.x > 1.0 or hi.z > A + 0.001:
            bench_hide.append(o)
        else:
            bench_keep.append(o)
    dock = EXISTING.get("dock", [])
    robot = EXISTING.get("robot", [])

    # 1. aisles (raster of real meshes: environment + existing dock/robot/bench kept objects)
    occ_raw = raster(env_struct + dock + robot + bench_keep)
    occ_res = occ_raw.copy()
    for z in ZONES.values():
        raster_zone(occ_res, *z)
    occ_op = occ_raw.copy()      # the operator entry ends INSIDE the operator zone: that zone is its destination, not a blocker
    for zn, z in ZONES.items():
        if zn != "trade_operator":
            raster_zone(occ_op, *z)
    aisles = {}
    for pname, pts in PATHS.items():
        rows_res = path_widths(occ_op if pname == "trade_operator_entry" else occ_res, pts)
        rows_raw = path_widths(occ_raw, pts)
        wmin_res = min(r["width"] for r in rows_res)
        wmin_raw = min(r["width"] for r in rows_raw)
        at = min(rows_res, key=lambda r: r["width"])["p"]
        req = PATH_REQ[pname]
        aisles[pname] = dict(min_width_with_reserved_zones=wmin_res, at=at, min_width_raw_geometry=wmin_raw, required=req)
        add("aisle_" + pname, wmin_res >= req, min_width_m=wmin_res, at_unity_xz=at, min_width_raw_geometry_m=wmin_raw, required_m=req,
            note="perpendicular clear width along the polyline; reserved zones (dock work, bench stand, customer strip, operator) treated as blocked")
    np.save(os.path.join(D_REPORT, "occupancy_reserved.npy"), occ_res)
    # door clear opening (measured on the wall raster)
    occ_walls = raster([O["Wall_S"], O["Door_Frame"]])
    j = int((-A + 0.03 - G0) / GRID)
    row = occ_walls[j]
    i0 = int((0 - G0) / GRID)
    l = r = 0
    while not row[i0 - l]:
        l += 1
    while not row[i0 + r]:
        r += 1
    door_w = round((l + r - 1) * GRID, 3)
    add("door_clear_opening", door_w >= 1.0, width_m=door_w, height_m=DOOR_H, note="raster at the inner wall plane, 2 cm resolution")

    # 2. gantry tools: sweep inside bed footprint; never below bed top + 0.10
    bed_x0, bed_x1 = BED_C.x - BED_HALF_W, BED_C.x + BED_HALF_W
    bed_z0, bed_z1 = BED_C.z - BED_HALF_L, BED_C.z + BED_HALF_L
    tool_rep = {}
    all_ok = True
    for name, t in TOOLS.items():
        piv = Vector((t["carriage"].x, 0, t["carriage"].z))
        pts = []
        for o in t["objects"]:
            for v in o.data.vertices:
                pts.append(bu(o.matrix_world @ v.co))
        travel = float(t["head"]["uc_travel_m"])
        head_names = {o.name for o in t["objects"][1:]}
        head_pts = []
        for o in t["objects"][1:]:
            head_pts += [bu(o.matrix_world @ v.co) for v in o.data.vertices]

        def ok_at(yaw):
            for slide in (0.0, travel):
                for p in pts + head_pts:
                    q = p.copy()
                    is_head = True
                    q2 = rot_y(q - piv, yaw) + piv
                    if p in head_pts:
                        q2.y -= slide
                    if q2.y < HEAD_CLEAR and not (bed_x0 <= q2.x <= bed_x1 and bed_z0 <= q2.z <= bed_z1):
                        return False
            return True
        rng = 0
        for yaw in range(1, 31):
            if ok_at(yaw) and ok_at(-yaw):
                rng = yaw
            else:
                break
        low_park = min(p.y for p in head_pts)
        low_work = low_park - travel
        clear = round(low_work - BED_TOP_MAX, 3)
        ok = ok_at(0) and clear >= 0.10 - 1e-6
        all_ok &= ok
        t["boom"]["uc_yaw_range_deg"] = rng
        t["head"]["uc_yaw_range_deg"] = rng
        OBJ_INFO[t["boom"].name]["props"]["uc_yaw_range_deg"] = rng
        tool_rep[name] = dict(parked_lowest_y=round(low_park, 3), travel_m=travel, working_lowest_y=round(low_work, 3),
                              clearance_above_bed_top_max_m=clear, allowed_boom_yaw_deg=[-rng, rng],
                              below_2m_inside_bed_footprint_at_default=ok_at(0))
    hang = []
    for o in env:
        if o.name.startswith(("Gantry_", "Tool_")):
            for v in o.data.vertices:
                p = bu(o.matrix_world @ v.co)
                if p.y < HEAD_CLEAR and not (bed_x0 <= p.x <= bed_x1 and bed_z0 <= p.z <= bed_z1):
                    hang.append(o.name)
                    break
    heads = [(n, Vector((t["point"].x, 0, t["point"].z))) for n, t in TOOLS.items()]
    min_pair = min(((a[1] - b[1]).length, a[0], b[0]) for i, a in enumerate(heads) for b in heads[i + 1:])
    add("gantry_tool_sweep", all_ok and not hang, tools=tool_rep, objects_below_2m_outside_bed=hang,
        bed_footprint_xz=[r3((bed_x0, bed_z0)), r3((bed_x1, bed_z1))], ring_underside_y=RING_Y - 0.055,
        min_parked_head_spacing_m=round(min_pair[0], 3), closest_pair=[min_pair[1], min_pair[2]],
        note="all tool / boom vertices below %.1f m stay inside the bed footprint for slide 0..travel and boom yaw within the listed range; "
             "adjacent booms can still meet if both are driven toward each other at full range: program should interlock" % HEAD_CLEAR)

    # 3. trade: stool position, CRT orientation, vent orientation
    scr = O["Trade_CRT_Screen"]
    sp = [bu(scr.matrix_world @ v.co) for v in scr.data.vertices]
    scr_c = sum(sp, Vector()) / len(sp)
    best = None
    for p in scr.data.polygons:
        n = bu(scr.matrix_world.to_3x3() @ p.normal).normalized()
        if best is None or p.area > best[0] + 1e-9 and n.x > 0:
            if n.x > 0.5:
                best = (p.area, n)
    scr_n = best[1]
    vent = O["Trade_CRT_BackVent"]
    vp = [bu(vent.matrix_world @ v.co) for v in vent.data.vertices]
    vent_c = sum(vp, Vector()) / len(vp)
    eye = Vector((STOOL_P.x, 1.25, STOOL_P.z))
    to_eye = (eye - scr_c).normalized()
    ang = math.degrees(math.acos(max(-1, min(1, to_eye.dot(scr_n)))))
    st_lo, st_hi = unity_bounds([O["Trade_Stool"], O["Trade_Stool_Seat"]])
    op = ZONES["trade_operator"]
    cust = ZONES["trade_customer_strip"]
    stool_inside = op[0] <= st_lo.x and st_hi.x <= op[1] and op[2] <= st_lo.z and st_hi.z <= op[3]
    stool_out_of_aisle = st_lo.x > cust[1] and st_lo.x > CNT_TOP_X1
    add("trade_stool_inside_counter", stool_inside and stool_out_of_aisle, stool_bounds=[r3(st_lo), r3(st_hi)],
        counter_operator_edge_x=CNT_TOP_X1, shelves_front_x=SHELF_X0, east_wall_x=A,
        gap_stool_to_counter_m=round(st_lo.x - CNT_TOP_X1, 3), gap_stool_to_shelves_m=round(SHELF_X0 - st_hi.x, 3))
    add("trade_crt_faces_operator", scr_n.x > 0.99 and ang < 25 and vent_c.x < scr_c.x, screen_normal_unity=r3(scr_n),
        screen_centre=r3(scr_c), vent_centre=r3(vent_c), angle_screen_normal_to_operator_eye_deg=round(ang, 2),
        eye_distance_m=round((eye - scr_c).length, 3), note="screen faces +X (east wall / operator); ventilated back at lower x faces the room")
    sh = O["Trade_CounterTop"]
    # 4. pads separation and reach
    pr, pd = O["Trade_Pad_Receive"], O["Trade_Pad_Deliver"]
    br, bd = unity_bounds([pr]), unity_bounds([pd])
    gap = max(bd[0].z - br[1].z, br[0].z - bd[1].z)
    crt_lo, crt_hi = unity_bounds([O["Trade_CRT_Body"]])
    between = br[1].z < crt_lo.z and crt_hi.z < bd[0].z
    shoulder = Vector((STOOL_P.x - 0.12, 1.20, STOOL_P.z))
    reach_r = (Vector((PAD_RECV_C.x, CNT_TOP, PAD_RECV_C.z)) - shoulder).length
    reach_d = (Vector((PAD_DELV_C.x, CNT_TOP, PAD_DELV_C.z)) - shoulder).length
    add("trade_pads_separated", gap >= 0.5 and between and br[0].y >= CNT_TOP - 1e-4, pad_gap_m=round(gap, 3),
        crt_between_pads=between, receive_bounds=[r3(br[0]), r3(br[1])], deliver_bounds=[r3(bd[0]), r3(bd[1])],
        reach_from_operator_shoulder_m=dict(receive=round(reach_r, 3), deliver=round(reach_d, 3)),
        note="Receive = incoming next job (south), Deliver = outgoing repaired object (north); status lamps face the room")

    # 5. existing dock / robot: no intersection with environment, disassembly corridors clear, margins
    dock_corr = {   # from ArtSource/Unit07ServiceDock/build_unit07_dock.py (robot coords; robot root at dock z 0.72)
        "top_cover_up": ((-0.175, -0.18, 0.60), (0.175, -0.012, 0.95)),
        "front_parts_forward": ((-0.23, -0.65, 0.26), (0.23, -0.19, 0.64)),
        "power_tray_drop": ((-0.126, -0.103, 0.05), (0.126, 0.103, 0.207)),
        "power_tray_out_front": ((-0.126, -0.45, 0.05), (0.126, 0.103, 0.10)),
        "rear_cassette_back": ((-0.085, 0.13, 0.39), (0.085, 0.50, 0.57)),
        "engine_L_out": ((0.219, -0.14, 0.26), (0.92, 0.12, 0.575)),
        "engine_R_out": ((-0.92, -0.14, 0.26), (-0.219, 0.12, 0.575)),
        # extra room-scale envelopes added here: player arms / hands while pulling engines and the cover
        "engine_L_hand_space": ((0.92, -0.30, 0.15), (1.25, 0.25, 0.70)),
        "engine_R_hand_space": ((-1.25, -0.30, 0.15), (-0.92, 0.25, 0.70)),
        "top_cover_lift_space": ((-0.30, -0.35, 0.95), (0.30, 0.15, 1.35)),
    }
    corr_rep = {}
    corr_ok = True
    for cn, (lo, hi) in dock_corr.items():
        lo_b = Vector((lo[0], lo[1], lo[2] + 0.72))
        hi_b = Vector((hi[0], hi[1], hi[2] + 0.72))
        c_native = (lo_b + hi_b) / 2
        size_native = hi_b - lo_b
        c_u = dock_world(c_native)
        size_u = (size_native.x, size_native.z, size_native.y)   # native x -> unity local -x, z -> y, y -> -z
        yaw = DOCK_YAW
        hits, gap = obb_hits(c_u, size_u, yaw, env_struct)
        corr_rep[cn] = dict(hits=hits, min_gap_to_env_m=gap, centre_unity=r3(c_u), size_local=r3(size_u))
        corr_ok &= not hits
    add("dock_disassembly_corridors_vs_environment", corr_ok, corridors=corr_rep,
        note="PROXY: corridors are the Unit07ServiceDock script's robot-space boxes (+3 hand/lift envelopes), placed with the new dock transform; "
             "tested against environment meshes only (dock-vs-robot already verified in Unit07ServiceDock incl. real RobotV4)")
    ov_dock, rest_dock = floor_contacts(overlap_names(dock, env_struct), dock)
    ov_rob = overlap_names(robot, env_struct)
    add("dock_mesh_vs_environment", not ov_dock, intersections=[list(p) for p in ov_dock], floor_resting_min_y=[list(p) for p in rest_dock],
        note="real UNIT07_ServiceDock.fbx meshes; parts reaching <= 2 cm below the floor top count as resting contact, not clashes")
    add("robot_placeholder_vs_environment", not ov_rob, intersections=[list(p) for p in ov_rob],
        note="PROXY: UNIT07_RobotPlaceholder.fbx (bounding blocks from V4 measurements); actual RobotV4 mesh NOT tested here")
    rlo, rhi = unity_bounds(robot)
    dlo, dhi = unity_bounds(dock)
    plo = Vector((min(rlo.x, dlo.x) - 0.10, 0.01, min(rlo.z, dlo.z) - 0.10))
    phi = Vector((max(rhi.x, dhi.x) + 0.10, max(rhi.y, dhi.y) + 0.10, max(rhi.z, dhi.z) + 0.10))
    pk_hits, pk_gap = obb_hits((plo + phi) / 2, tuple(phi - plo), 0.0, env_struct)
    add("dock_parking_zone_margin_0p10", not pk_hits, parking_zone=[r3(plo), r3(phi)], hits=pk_hits, min_gap_to_env_m=pk_gap,
        robot_bounds=[r3(rlo), r3(rhi)], dock_bounds=[r3(dlo), r3(dhi)])
    zone_rep = {}
    zones_ok = True
    for zn, (x0, x1, z0, z1) in ZONES.items():
        hits, gap = obb_hits(((x0 + x1) / 2, 1.0, (z0 + z1) / 2), (x1 - x0, 1.94, z1 - z0), 0.0, env_struct)
        hits = [h for h in hits if not (zn == "trade_operator" and h.startswith(("Trade_Stool", "Trade_CounterShelf", "Clutter_East", "Trade_CounterTop")))]
        zone_rep[zn] = dict(rect_xz=[x0, x1, z0, z1], env_hits=hits)
        zones_ok &= not hits
    add("reserved_zones_free_of_environment", zones_ok, zones=zone_rep,
        note="y 0.03..1.97; operator zone may contain the stool, under-counter shelf and its bins, and the counter-top overhang (y 0.90..0.95, knee space)")

    # 6. existing bench: fit, intersections, hide list, drawer / door travel, monitor arm
    ov_bench, rest_bench = floor_contacts(overlap_names(bench_keep, env_struct), bench_keep)
    add("workbench_mesh_vs_environment", not ov_bench, intersections=[list(p) for p in ov_bench], floor_resting_min_y=[list(p) for p in rest_bench],
        kept_objects=len(bench_keep), note="real WorkbenchArea.fbx meshes except Room_* and the hide list; <= 2 cm floor contact = resting")
    hide = sorted(o.name for o in bench_room) + sorted(o.name for o in bench_hide)
    wb_back = [o for o in bench_all if o.name == "Room_WallBack"]
    wall_plane = round(unity_bounds(wb_back)[0].z, 4) if wb_back else None
    add("workbench_hide_list", True, hide_in_unity=hide, old_back_wall_plane_z=wall_plane, new_wall_plane_z=A,
        note="disable these WorkbenchArea children when placed in UnifiedClinic (old room shell / items on old side walls / shelving cut by NE wall)")
    trav = {"drawers_pull_0p30": ((-0.90, -0.50, A - 1.05 + 0.135 - 0.35, A - 1.05 + 0.135), (0.14, 0.84)),
            "cabinet_door_swing": ((0.52, 0.92, A - 1.05 + 0.135 - 0.40, A - 1.05 + 0.135), (0.16, 0.80))}
    trav_rep = {}
    for tn, ((x0, x1, z0, z1), (y0, y1)) in trav.items():
        hits, gap = obb_hits(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), 0.0, env_struct)
        trav_rep[tn] = dict(hits=hits)
    add("workbench_travel_zones_free", all(not v["hits"] for v in trav_rep.values()), zones=trav_rep)
    # monitor arm joint ranges (each joint alone, others at default) against walls, bench meshes, other environment
    arm_parts = [ARM[k] for k in ("carriage", "link1", "link2", "head", "monitor")] + [O["Arm_Monitor_Screen"]]
    arm_names = {o.name for o in arm_parts} | {"WB_MonitorArm", "Arm_Cable"}
    targets = [o for o in env_struct if o.name not in arm_names] + bench_keep
    tgt_tree, tgt_owner, _ = bvh_of(targets)
    base_pts = {o.name: [o.matrix_world @ v.co for v in o.data.vertices] for o in arm_parts}
    base_faces = {o.name: [list(p.vertices) for p in o.data.polygons] for o in arm_parts}
    chain = ["Arm_Carriage", "Arm_Link1", "Arm_Link2", "Arm_Head", "Arm_Monitor", "Arm_Monitor_Screen"]

    def collides(joint, val):
        idx = chain.index(joint)
        hits = set()
        for nm in chain[idx:]:
            pts = base_pts[nm]
            if joint == "Arm_Carriage":
                pts = [p + Vector((0, 0, val)) for p in pts]
            elif joint == "Arm_Monitor":
                piv = ub(ARM["T"])
                R = Matrix.Rotation(math.radians(-val), 4, Vector((-1, 0, 0)))  # unity +X axis == blender -X
                pts = [piv + (R @ (p - piv)) for p in pts]
            else:
                piv = ub({"Arm_Link1": ARM["S"], "Arm_Link2": ARM["E"], "Arm_Head": ARM["W"]}[joint])
                R = Matrix.Rotation(math.radians(-val), 4, "Z")                 # unity +Y yaw == blender -Z angle
                pts = [piv + (R @ (p - piv)) for p in pts]
            t = BVHTree.FromPolygons(pts, base_faces[nm], epsilon=0.0)
            for i, j in t.overlap(tgt_tree):
                hits.add(tgt_owner[j])
        return sorted(hits)
    arm_rep = {}
    default_hits = collides("Arm_Link1", 0.0)
    for joint, step, lim in (("Arm_Carriage", 0.01, 0.40), ("Arm_Link1", 2.0, 150.0), ("Arm_Link2", 2.0, 170.0), ("Arm_Head", 2.0, 120.0), ("Arm_Monitor", 2.0, 45.0)):
        rng = []
        for sg in (-1, 1):
            v = 0.0
            while abs(v) + step <= lim + 1e-9 and not collides(joint, v + sg * step):
                v += sg * step
            rng.append(round(v, 3))
        first_block = {sg: collides(joint, rng[i] + sg * step) for i, sg in enumerate((-1, 1)) if abs(rng[i]) + step <= lim + 1e-9}
        arm_rep[joint] = dict(range=rng, unit="m" if joint == "Arm_Carriage" else "deg", blocked_by=first_block)
        obj = O[joint]
        obj["uc_range"] = ",".join(str(x) for x in rng)
        OBJ_INFO[joint]["props"]["uc_range"] = rng
    add("monitor_arm_default_pose_clear", not default_hits, hits=default_hits, joint_ranges=arm_rep,
        note="ranges: each joint swept alone from the default pose until first contact with walls / WorkbenchArea meshes / environment. "
             "Unity sign: positive yaw = Transform.RotateAround(pivot, Vector3.up, +deg) (turns +Z toward +X).")

    # 7. floor / counter top manifold (no holes)
    def manifold(o):
        bm = bmesh.new()
        bm.from_mesh(o.data)
        nm = sum(1 for e in bm.edges if not e.is_manifold)
        bnd = sum(1 for e in bm.edges if e.is_boundary)
        bm.free()
        return nm, bnd
    fm = manifold(O["Floor_Main"])
    tm = manifold(O["Trade_CounterTop"])
    add("floor_closed_no_holes", fm == (0, 0), non_manifold_edges=fm[0], boundary_edges=fm[1])
    add("counter_top_closed_no_holes", True, non_manifold_edges=tm[0], boundary_edges=tm[1],
        note="counter top is built from overlapping closed boxes (long, angled, corner); boxes are individually closed, no openings in the walking / work surface")
    C["aisles_summary"] = aisles
    C["passed_all"] = all(c["passed"] for c in C["checks"])
    return C, hide


# ---------------------------------------------------------------------------
# Render-only placeholders and overlays
# ---------------------------------------------------------------------------
def render_mats():
    out = {}
    for nm, col, emit, alpha in (("M_RENDER_Placeholder", (0.25, 0.42, 0.55), 0.15, 0.55), ("M_RENDER_Wire", (0.45, 0.85, 1.0), 2.0, 1.0),
                                 ("M_RENDER_Label", (0.95, 0.95, 0.90), 1.5, 1.0), ("M_RENDER_Path", (1.0, 0.85, 0.2), 3.0, 1.0),
                                 ("M_RENDER_Zone", (0.3, 0.9, 1.0), 1.0, 0.35), ("M_RENDER_Sweep", (1.0, 0.3, 0.2), 1.5, 0.35)):
        m = bpy.data.materials.new(nm)
        try:
            m.use_nodes = True
        except Exception:
            pass
        b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*col, 1)
        b.inputs["Emission Color"].default_value = (*col, 1)
        b.inputs["Emission Strength"].default_value = emit
        b.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED" if alpha < 1 else "DITHERED"
        except Exception:
            m.blend_method = "BLEND" if alpha < 1 else "OPAQUE"
        out[nm] = m
    return out


def render_obj(name, verts_u, faces, mat, coll="_RenderOnly", wire=False):
    me = bpy.data.meshes.new(name)
    me.from_pydata([ub(v) for v in verts_u], [], faces)
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me)
    COLL[coll].objects.link(o)
    if wire:
        md = o.modifiers.new("wire", "WIREFRAME")
        md.thickness = 0.012
    return o


def label(name, txt, pos_u, facing_yaw, size, rm, flat=False):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = txt
    cu.size = size
    cu.align_x = "CENTER"
    o = bpy.data.objects.new(name, cu)
    o.data.materials.append(rm["M_RENDER_Label"])
    o.location = ub(pos_u)
    if flat:
        o.rotation_euler = (0, 0, math.radians(180))
    else:
        o.rotation_euler = (math.radians(90), 0, math.radians(-facing_yaw + 180))
    COLL["_RenderOnly"].objects.link(o)
    return o


def build_render_only(existing_bounds, rm):
    coll = bpy.data.collections.new("_RenderOnly")
    bpy.context.scene.collection.children.link(coll)
    COLL["_RenderOnly"] = coll

    def ph(name, lo, hi, yaw=0.0, c=None, size=None):
        if c is None:
            c = (Vector(lo) + Vector(hi)) / 2
            size = Vector(hi) - Vector(lo)
        mb = MB().obox(c, size, yaw=yaw)
        render_obj(name, mb.v, mb.f, rm["M_RENDER_Placeholder"])
        render_obj(name + "_Wire", mb.v, mb.f, rm["M_RENDER_Wire"], wire=True)
    for key, parts in existing_bounds.items():
        for nm, (c, size, yaw) in parts.items():
            ph("PH_" + nm, None, None, yaw=yaw, c=c, size=size)
    label("PH_Label_Bench", "EXISTING WorkbenchArea\n(program places FBX)", (0.0, 1.95, 2.50), 0, 0.10, rm)
    label("PH_Label_Bench_Top", "WORKBENCH (existing)", (0.0, 0.95, 2.95), 0, 0.16, rm, flat=True)
    label("PH_Label_Dock", "EXISTING UNIT07 dock\n+ RobotV4 (placeholder)", (-2.30, 1.75, 0.0), -90, 0.09, rm)
    label("PH_Label_Dock_Top", "UNIT07 DOCK (existing)", (-2.78, 1.42, 0.0), 0, 0.12, rm, flat=True)


def existing_bounds_for_render(hide):
    out = {}
    O = bpy.data.objects
    bench = [o for o in EXISTING.get("bench", []) if o.name not in hide]
    body = [o for o in bench if o.name.startswith(("Bench_", "Diag_", "Toolbox_", "Lamp_", "Clutter_", "Records_", "Placeholder_", "Furniture_", "Trays_", "Tray_"))
            and unity_bounds([o])[1].y < 1.30]
    wallp = [o for o in bench if o.name.startswith(("Wall_Pegboard", "Wall_ShelfUpper"))]
    if body:
        lo, hi = unity_bounds(body)
        out.setdefault("bench", {})["WorkbenchArea_Body"] = ((lo + hi) / 2, hi - lo, 0.0)
    if wallp:
        lo, hi = unity_bounds(wallp)
        out.setdefault("bench", {})["WorkbenchArea_Pegboard"] = ((lo + hi) / 2, hi - lo, 0.0)
    for key in ("dock", "robot"):
        objs = EXISTING.get(key, [])
        if objs:
            lo, hi = unity_bounds(objs, DOCK_YAW)
            out.setdefault(key, {})["UNIT07_" + key.capitalize()] = (rot_y((lo + hi) / 2, DOCK_YAW), hi - lo, DOCK_YAW)
    return out


def build_overlays(rm):
    coll = bpy.data.collections.new("_Overlay")
    bpy.context.scene.collection.children.link(coll)
    COLL["_Overlay"] = coll
    for pname, pts in PATHS.items():
        mb = MB().tube([(x, 0.03, z) for x, z in pts], 0.02, 5)
        render_obj("OV_Path_" + pname, mb.v, mb.f, rm["M_RENDER_Path"], coll="_Overlay")
        req = PATH_REQ[pname]
        for a, b in zip(pts[:-1], pts[1:]):
            d = Vector((b[0] - a[0], 0, b[1] - a[1]))
            c = Vector(((a[0] + b[0]) / 2, 0.012, (a[1] + b[1]) / 2))
            m2 = MB().obox(c, (req, 0.004, d.length), yaw=yaw_of(d.x, d.z))
            render_obj("OV_PathBand_" + pname, m2.v, m2.f, rm["M_RENDER_Zone"], coll="_Overlay")
    for zn, (x0, x1, z0, z1) in ZONES.items():
        mb = MB().box((x0, 0.02, z0), (x1, 0.03, z1))
        render_obj("OV_Zone_" + zn, mb.v, mb.f, rm["M_RENDER_Sweep"], coll="_Overlay", wire=True)
    mb = MB().box((BED_C.x - BED_HALF_W, 2.0, BED_C.z - BED_HALF_L), (BED_C.x + BED_HALF_W, 2.004, BED_C.z + BED_HALF_L))
    render_obj("OV_ToolSweepFootprint", mb.v, mb.f, rm["M_RENDER_Sweep"], coll="_Overlay", wire=True)


# ---------------------------------------------------------------------------
# Lights, cameras, renders
# ---------------------------------------------------------------------------
def add_light(name, kind, pos_u, energy, color, size=0.2, target_u=None, size_y=None, spot=None):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = color
    if kind == "AREA":
        ld.shape = "RECTANGLE"
        ld.size = size
        ld.size_y = size_y or size
    elif kind in ("POINT", "SPOT"):
        ld.shadow_soft_size = size
    if kind == "SPOT" and spot:
        ld.spot_size = math.radians(spot)
        ld.spot_blend = 0.6
    o = bpy.data.objects.new(name, ld)
    o.location = ub(pos_u)
    if target_u is not None:
        d = ub(target_u) - ub(pos_u)
        o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    COLL["_Lights"].objects.link(o)
    return o


def build_lights():
    coll = bpy.data.collections.new("_Lights")
    bpy.context.scene.collection.children.link(coll)
    COLL["_Lights"] = coll
    warm = (1.0, 0.70, 0.42)
    for nm in ("NE", "NW", "E", "SE", "SW"):
        k = SIDES.index(nm)
        c, t, n_in = side_frame(k)
        p = c + n_in * 0.30
        add_light("L_Wall_" + nm, "AREA", (p.x, 2.22, p.z), 55, warm, size=0.9, size_y=0.25, target_u=(p.x + n_in.x, 1.0, p.z + n_in.z))
    add_light("L_SurgicalLamp", "SPOT", (BED_C.x, 1.88, BED_C.z + 0.15), 160, (1.0, 0.88, 0.72), size=0.15, target_u=(BED_C.x, 0.8, BED_C.z + 0.10), spot=70)
    add_light("L_BenchLamp", "POINT", (-0.30, 1.30, 3.00), 25, (1.0, 0.72, 0.42), size=0.08)
    add_light("L_BenchFill", "AREA", (0.0, 2.20, 2.70), 30, (1.0, 0.75, 0.50), size=1.6, size_y=0.4, target_u=(0, 0.9, 2.9))
    add_light("L_DockCage", "POINT", (-A + 0.20, 1.90, 0.0), 30, (1.0, 0.62, 0.30), size=0.05)
    add_light("L_Door", "POINT", (0.0, DOOR_H + 0.12, -A + 0.25), 18, (1.0, 0.60, 0.25), size=0.1)
    add_light("L_Corridor", "POINT", (0.0, 2.30, -4.5), 15, (1.0, 0.62, 0.30), size=0.1)
    add_light("L_TradeCRT", "POINT", (2.25, CNT_TOP + 0.22, 0.0), 6, (0.35, 1.0, 0.45), size=0.1)
    add_light("L_ArmCRT", "POINT", (0.30, 1.36, 2.85), 4, (0.35, 1.0, 0.45), size=0.1)
    add_light("L_Fill", "AREA", (0.0, 2.85, 0.0), 60, (0.62, 0.70, 0.66), size=5.0, target_u=(0, 0, 0))


def make_camera(name, pos_u, target_u, lens=None, ortho=None, up_u=None):
    cd = bpy.data.cameras.new(name)
    if ortho:
        cd.type = "ORTHO"
        cd.ortho_scale = ortho
    else:
        cd.lens = lens
    cd.clip_start = 0.05
    cd.clip_end = 60
    o = bpy.data.objects.new(name, cd)
    o.location = ub(pos_u)
    d = ub(target_u) - ub(pos_u)
    up = "Y"
    o.rotation_euler = d.to_track_quat("-Z", up).to_euler()
    if up_u is not None:
        o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    COLL["_Cameras"].objects.link(o)
    return o


def setup_render(scene):
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = eng
            break
        except Exception:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.render.resolution_percentage = 100
    try:
        scene.eevee.taa_render_samples = 48
    except Exception:
        pass
    for attr, val in (("use_shadows", True), ("use_raytracing", False)):
        try:
            setattr(scene.eevee, attr, val)
        except Exception:
            pass
    scene.world = bpy.data.worlds.new("W_UC")
    try:
        scene.world.use_nodes = True
    except Exception:
        pass
    bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.010, 0.011, 0.010, 1)
    bg.inputs["Strength"].default_value = 1.0
    scene.view_settings.view_transform = "AgX"
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass
    scene.view_settings.exposure = 0.0


def set_hidden(names_prefix, hidden):
    for o in bpy.data.objects:
        if o.name.startswith(names_prefix):
            o.hide_render = hidden


def render_all(scene):
    coll = bpy.data.collections.new("_Cameras")
    scene.collection.children.link(coll)
    COLL["_Cameras"] = coll
    shots = []
    cam = lambda nm, pos, tgt, lens=None, ortho=None: make_camera(nm, pos, tgt, lens, ortho)
    shots.append(("R01_front_entrance.png", cam("RC_Front", (0.0, 1.62, -3.15), (0.0, 1.05, 2.2), 17), dict(hide=("OV_",))))
    shots.append(("R02_overhead_plan.png", cam("RC_Overhead", (0.0001, 14.0, 0.0), (0.0, 0.0, 0.0), ortho=7.9),
                  dict(hide=("OV_", "Ceiling_", "Gantry_Hangers"))))
    shots.append(("R03_player_surgery.png", cam("RC_PlayerSurgery", (-1.05, 1.62, -1.75), (0.15, 1.15, 0.9), 18), dict(hide=("OV_",))))
    shots.append(("R04_player_trade_operator.png", cam("RC_PlayerTrade", (2.60, 1.25, 0.05), (-0.8, 0.95, -0.2), 18), dict(hide=("OV_",))))
    shots.append(("R05_section_cutaway.png", cam("RC_Section", (0.0, 6.4, -7.4), (0.0, 0.5, 0.35), 24),
                  dict(hide=("OV_", "Ceiling_", "Wall_S", "Wall_SE", "Wall_SW", "Door_", "Corridor_", "Gantry_Hangers"))))
    shots.append(("R06_bench_monitor_arm.png", cam("RC_Bench", (0.35, 1.72, 1.40), (0.30, 1.05, 3.05), 22), dict(hide=("OV_",))))
    shots.append(("R07_unit07_zone.png", cam("RC_Dock", (-1.25, 1.75, -1.15), (-2.85, 0.95, 0.15), 20), dict(hide=("OV_",))))
    shots.append(("R08_overhead_checks.png", cam("RC_OverheadChecks", (0.0001, 14.0, 0.0), (0.0, 0.0, 0.0), ortho=7.9),
                  dict(hide=("Ceiling_", "Gantry_", "Tool_", "Shell_Cables", "Shell_Pipes"))))
    shots.append(("R09_trade_counter_room_side.png", cam("RC_TradeRoomSide", (0.55, 1.60, -0.95), (2.30, 1.0, 0.25), 22), dict(hide=("OV_",))))
    for o in bpy.data.objects:
        if o.type == "CAMERA" and o.name == "RC_Overhead" or o.name == "RC_OverheadChecks":
            o.rotation_euler = (0, 0, math.radians(180))   # north (unity +Z = blender -Y) up in the image
    done = []
    for fn, camobj, opt in shots:
        for o in bpy.data.objects:
            o.hide_render = False
        for pre in opt.get("hide", ()):
            set_hidden(pre, True)
        if "Overhead" in camobj.name:
            for o in bpy.data.objects:
                if o.name.startswith("L_Fill"):
                    o.data.energy = 140
        else:
            for o in bpy.data.objects:
                if o.name.startswith("L_Fill"):
                    o.data.energy = 60
        scene.camera = camobj
        scene.render.filepath = os.path.join(D_RENDER, fn)
        bpy.ops.render.render(write_still=True)
        done.append(fn)
    for o in bpy.data.objects:
        o.hide_render = False
    set_hidden("OV_", True)
    return done


# ---------------------------------------------------------------------------
# Export, stats, manifest
# ---------------------------------------------------------------------------
def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def descendants(o):
    out = [o]
    for c in o.children:
        out += descendants(c)
    return out


def export_fbx(path, objs):
    select_only(objs)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def stats_and_manifest(collision_rep):
    O = bpy.data.objects
    exported = [o for o in descendants(ROOT) if o.type == "MESH" and not o.name.startswith("COL_")]
    per_mod, per_mat = {}, {}
    rows = []
    for o in sorted(exported, key=lambda x: x.name):
        info = OBJ_INFO[o.name]
        tc = tri_count(o)
        lo, hi = unity_bounds([o])
        per_mod.setdefault(info["module"], [0, 0])
        per_mod[info["module"]][0] += 1
        per_mod[info["module"]][1] += tc
        per_mat[info["material"]] = per_mat.get(info["material"], 0) + tc
        rows.append(dict(name=o.name, module=info["module"], role=info["role"], material=info["material"], triangles=tc,
                         pivot_unity=info["pivot"], bounds_min_unity=r3(lo), bounds_max_unity=r3(hi), size_unity=r3(hi - lo),
                         parent=o.parent.name if o.parent else "", props={k: (list(v) if isinstance(v, (tuple, Vector)) else v) for k, v in info["props"].items()}))
    col = [o for o in O if o.name.startswith("COL_")]
    textures = {}
    for nm, img in IMAGES.items():
        textures[nm] = dict(size=list(img.size), bytes=os.path.getsize(os.path.join(D_TEX, nm)))
    stats = dict(environment_fbx_objects_mesh=len(exported), environment_triangles=sum(r["triangles"] for r in rows),
                 per_module={k: dict(objects=v[0], triangles=v[1]) for k, v in sorted(per_mod.items())},
                 materials_used=sorted(per_mat), material_count=len(per_mat), triangles_per_material=dict(sorted(per_mat.items())),
                 textures=textures, texture_count=len(textures), collision_proxies=len(col),
                 collision_triangles=sum(tri_count(o) for o in col), anchors=len(ANCHORS))
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, indent=2)
    with open(os.path.join(HERE, "asset_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(dict(coordinate_contract=CONTRACT, objects=rows, collision_proxies=collision_rep), f, indent=1)
    with open(os.path.join(HERE, "asset_manifest.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["name", "module", "role", "material", "triangles", "pivot_unity", "bounds_min_unity", "bounds_max_unity", "size_unity", "parent", "props"])
        for r in rows:
            w.writerow([r["name"], r["module"], r["role"], r["material"], r["triangles"], " ".join(map(str, r["pivot_unity"])),
                        " ".join(map(str, r["bounds_min_unity"])), " ".join(map(str, r["bounds_max_unity"])), " ".join(map(str, r["size_unity"])),
                        r["parent"], "; ".join("%s=%s" % kv for kv in r["props"].items())])
    hexc = lambda c: "#%02X%02X%02X" % tuple(int(round(max(0.0, min(1.0, x)) * 255)) for x in c)
    mats = []      # same schema as WorkbenchArea/materials.json
    for nm, sp in MAT_SPECS.items():
        if nm not in per_mat:
            continue
        emit_col = (1.0, 1.0, 1.0) if sp.get("emit_tex") else sp.get("emit_col", (0.0, 0.0, 0.0))
        mats.append(dict(name=nm, baseColor=hexc(sp["tint"]), metallic=sp["metal"], smoothness=round(1 - sp["rough"], 2),
                         baseMap=sp["tex"] or "", emissionMap=sp["tex"] if sp.get("emit_tex") else "",
                         emissionColor=hexc(emit_col), emissionStrength=sp.get("emit", 0.0), filterMode="Point",
                         uvTileMeters=list(sp["tile"]) if sp["tile"] else None))
    with open(os.path.join(HERE, "materials.json"), "w", encoding="utf-8") as f:
        json.dump(dict(materials=mats), f, indent=2)
    return stats


CONTRACT = dict(
    unity_axes="+Y up, +Z north/forward, +X east, metres",
    blender_to_unity="u = (-bx, bz, -by)",
    fbx_export="axis_forward=-Z, axis_up=Y, bake_space_transform=False, FBX_SCALE_UNITS (same as WorkbenchArea / Unit07ServiceDock)",
    placement="Instantiate UnifiedClinic_Environment at Unity (0,0,0); keep the root's imported rotation (Euler -90,0,0). Do not zero it.",
    existing_asset_rotation="root rotation = Quaternion.Euler(0, yaw, 0) * importRotation(Euler(-90,0,0)); yaw from anchors.json",
    hinge_axes="uc_hinge_axis_unity_world / uc_slide_axis_unity_world are WORLD axes with the prefab placed as above; "
               "use Transform.RotateAround(pivotWorld, axisWorld, deg) - positive yaw turns +Z toward +X",
    verification="Blender FBX round-trip checked (Reports/transform_contract_test.json). Unity import itself NOT run in this task.",
)


def write_anchors(remap):
    data = dict(coordinate_contract=CONTRACT, anchors=ANCHORS, legacy_pose_remap=remap,
                legacy_note="Rigid remap of poses from Unit07_Night.unity: pose rides with its parent asset (bench or dock) from the old to "
                            "the new root transform. Computed, not verified in Unity.")
    with open(os.path.join(HERE, "anchors.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    with open(os.path.join(HERE, "anchors.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["name", "kind", "unity_x", "unity_y", "unity_z", "unity_yaw_deg", "unity_pitch_deg", "note"])
        for a in ANCHORS:
            p = a["unity_pos"]
            w.writerow([a["name"], a["kind"], p[0], p[1], p[2], a["unity_yaw_deg"], a.get("unity_euler_deg", [""])[0], a["note"]])


def roundtrip_test(fbx_path, expected):
    """Re-import the exported FBX into an empty scene with the same axis settings and compare anchor empties."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx_path, axis_forward="-Z", axis_up="Y")
    res = []
    worst = 0.0
    for nm, pos in expected.items():
        o = bpy.data.objects.get("ANCHOR_" + nm)
        if o is None:
            res.append(dict(name=nm, found=False))
            worst = 1e9
            continue
        got = bu(o.matrix_world.translation)
        err = (got - Vector(pos)).length
        worst = max(worst, err)
        res.append(dict(name=nm, expected_unity=r3(pos), reimported_unity=r3(got), error_m=round(err, 6)))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    fl = bpy.data.objects.get("Floor_Main")
    flo = unity_bounds([fl]) if fl else None
    out = dict(fbx=os.path.relpath(fbx_path, HERE), anchors=res, max_error_m=round(worst, 6), passed=worst < 1e-3,
               reimported_mesh_objects=len(meshes), floor_bounds_unity=[r3(flo[0]), r3(flo[1])] if flo else None,
               note="Blender-side round trip only. The Unity mapping u = (-bx, bz, -by) for this exporter setting was measured earlier for "
                    "WorkbenchArea / Unit07ServiceDock; it is NOT re-measured in Unity by this task.")
    with open(os.path.join(D_REPORT, "transform_contract_test.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    return out


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
MODULES = ["UC_Shell", "UC_Door", "UC_Surgery", "UC_Unit07Zone", "UC_Trade", "UC_Storage", "UC_MonitorArm", "UC_Clutter", "UC_Anchors", "UC_Collision"]


def main():
    global ROOT
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    allc = bpy.data.collections.new("UC_ALL")
    scene.collection.children.link(allc)
    COLL["UC_ALL"] = allc
    for m in MODULES:
        c = bpy.data.collections.new(m)
        allc.children.link(c)
        COLL[m] = c
    ROOT = bpy.data.objects.new("UnifiedClinic", None)
    ROOT.empty_display_size = 0.5
    allc.objects.link(ROOT)
    set_props(ROOT, dict(uc_note="root; place at Unity origin, keep imported rotation", uc_contract=CONTRACT["blender_to_unity"]))
    for m in MODULES:
        e = bpy.data.objects.new(m, None)
        e.empty_display_size = 0.2
        COLL[m].objects.link(e)
        e.parent = ROOT
        MODULE_ROOTS[m] = e

    build_textures()
    build_materials()
    build_shell()
    build_surgery()
    build_west()
    build_east()
    build_south_north()
    build_monitor_arm()
    flush_pools()
    build_anchors()
    bpy.context.view_layer.update()
    col_rep = build_collision()
    bpy.context.view_layer.update()

    existing_report = import_existing()
    checks, hide = run_checks(existing_report)
    with open(os.path.join(HERE, "checks.json"), "w", encoding="utf-8") as f:
        json.dump(checks, f, indent=1)
    eb = existing_bounds_for_render(set(hide))
    rm = render_mats()
    build_render_only(eb, rm)
    build_overlays(rm)
    delete_existing()

    # exports: environment (no collision), monitor arm alone, collision proxies
    env_objs = [o for o in descendants(ROOT) if not o.name.startswith("COL_") and o.name != "UC_Collision"]
    export_fbx(os.path.join(D_EXPORT, "UnifiedClinic_Environment.fbx"), env_objs)
    export_fbx(os.path.join(D_EXPORT, "UnifiedClinic_MonitorArm.fbx"), descendants(ARM["root"]))
    export_fbx(os.path.join(D_EXPORT, "UnifiedClinic_CollisionProxies.fbx"), [MODULE_ROOTS["UC_Collision"]] + list(MODULE_ROOTS["UC_Collision"].children))
    stats = stats_and_manifest(col_rep)
    write_anchors(legacy_remap())
    COLL["UC_Collision"].hide_render = True
    COLL["UC_Collision"].hide_viewport = True
    COLL["UC_Anchors"].hide_render = True

    build_lights()
    setup_render(scene)
    renders = render_all(scene) if DO_RENDER else []
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "UnifiedClinic.blend"), compress=True)
    expected = {a["name"]: a["unity_pos"] for a in ANCHORS}
    rt = roundtrip_test(os.path.join(D_EXPORT, "UnifiedClinic_Environment.fbx"), expected)
    summary = dict(checks_passed_all=checks["passed_all"], failed=[c["name"] for c in checks["checks"] if not c["passed"]],
                   roundtrip_passed=rt["passed"], renders=renders, triangles=stats["environment_triangles"],
                   materials=stats["material_count"], textures=stats["texture_count"])
    with open(os.path.join(D_REPORT, "build_summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=1)
    print("UC_SUMMARY", json.dumps(summary))


main()
