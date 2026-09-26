"""
边境维修站 · 导航信标（NavBeacon）建模脚本
生成方式：Claude 辅助编写的 Blender Python 建模脚本（程序化建模 + 程序化贴图），未使用图生 3D 服务或外部素材。
规格：Docs/AssetSpecs/NavBeacon_Spec.md

用法（Blender 5.2）：
    blender.exe -b --factory-startup --python build_navbeacon.py -- [--no-render] [--no-export]

坐标：米，Z 向上，正面朝 -Y。5 个 FBX（Body/Seal/Port/Lamp/Mast）共用世界原点。
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
sys.path.insert(0, os.path.join(HERE, "..", "Common"))
import br_hardsurface as hs  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY_ART = os.path.join(PROJECT, "Assets", "BorderRepair", "Art", "NavBeacon")
MODEL_DIR = os.path.join(UNITY_ART, "Models")
TEX_DIR = os.path.join(UNITY_ART, "Textures")
SHOT_DIR = os.path.join(PROJECT, "Docs", "AssetEvaluation", "Screenshots")
SHOT_PREFIX = "20260924_NavBeacon"

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "--no-render" not in ARGS
DO_EXPORT = "--no-export" not in ARGS

RNG = random.Random(20260925)

GRIME_TEX = "T_Beacon_Grime.png"
SEAL_TEX = "T_Beacon_Seal.png"
PCB_TEX = "T_Beacon_PCB.png"

MATERIALS = {
    "M_Beacon_Housing": dict(color="#E8B21E", metallic=0.0, roughness=0.5, base_map=GRIME_TEX),
    "M_Beacon_Dark": dict(color="#23272B", metallic=0.0, roughness=0.72, base_map=GRIME_TEX),
    "M_Beacon_Metal": dict(color="#8E959A", metallic=1.0, roughness=0.42, base_map=GRIME_TEX),
    "M_Beacon_Lens": dict(color="#FFB23F", metallic=0.0, roughness=0.08, emission_color="#FFA12E", emission_strength=2.0),
    "M_Beacon_Seal": dict(color="#FFFFFF", metallic=0.0, roughness=0.6, base_map=SEAL_TEX),
    "M_Beacon_PCB": dict(color="#FFFFFF", metallic=0.0, roughness=0.45, base_map=PCB_TEX),
    "M_Beacon_Wire": dict(color="#C81E1E", metallic=0.0, roughness=0.5),
    # 新焊锡 / 新鲜刮痕：亮银。金属度 0.6，在没有反射探针的场景里也不会发黑
    "M_Beacon_Solder": dict(color="#F2F4F6", metallic=0.6, roughness=0.18),
}

GROUPS = ["Body", "Seal", "Port", "Lamp", "Mast"]

# 主要尺寸
R_BODY = 0.07                                  # 八棱柱外接圆半径
OCT_PHASE = math.pi / 8                        # 让平面朝向 ±X、±Y
APOTHEM = R_BODY * math.cos(math.pi / 8)       # 中心到平面的距离 ≈ 0.0647
FRONT_Y = -APOTHEM
BACK_Y = APOTHEM
BASE_Z = -0.100
BODY_Z0, BODY_Z1 = -0.074, 0.052
PLATE_Z = 0.066                                # 顶板上表面
MAST_XY = (0.048, 0.020)

# 调试电路板（背面维护舱内）
PORT_CZ = 0.012                                # 维护舱中心：接近包围盒中心高度，放大检查时不会被推到画面底部
PCB_W, PCB_H = 0.036, 0.026
PCB_Y = BACK_Y + 0.0034                        # 比舱底（BACK_Y + 0.0032）高 0.2 mm，避免 Z-fighting


def octagon(r):
    return hs.circle_profile(r, 8, OCT_PHASE)


def add_box(bm, size, center, rot=None):
    """往已有 bmesh 里追加一个盒子（rot 为 3×3 旋转矩阵）。"""
    m = Matrix.Translation(Vector(center)) @ ((rot.to_4x4() if rot is not None else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1.0)))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def add_sphere(bm, radius, center, scale=(1, 1, 1), u=8, v=6):
    m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((*scale, 1.0))
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius, matrix=m)


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def make_seal_texture(w=512, h=256):
    """封签贴图集（行 0 为顶部）：
    左 384×192 = 封签正面；右上 128×128 = 撕开后露出的 VOID 防伪底纹；右下 128×128 = 残胶。"""
    img = np.tile(np.array([0.55, 0.57, 0.58]), (h, w, 1)).astype(np.float64)
    cream = np.array([0.94, 0.91, 0.83])
    red = np.array([0.75, 0.08, 0.07])
    ink = np.array([0.12, 0.12, 0.14])

    # 封签正面
    hs.rect(img, 0, 0, 384, 192, cream)
    hs.rect_outline(img, 4, 4, 380, 188, 8, red)
    hs.disc(img, 66, 96, 46, red, ring=7)
    hs.disc(img, 66, 96, 30, red, ring=3)
    for k in range(5):                                   # 章内的放射纹
        a = math.pi / 2 + k * 2 * math.pi / 5
        for t in range(0, 26):
            x, y = int(66 + math.cos(a) * t), int(96 - math.sin(a) * t)
            img[y - 1:y + 2, x - 1:x + 2] = red
    hs.draw_text(img, "BDR", 49, 138, 2, red)
    hs.draw_text(img, "INSPECTION", 128, 28, 3, ink)
    hs.draw_text(img, "SEAL", 128, 62, 5, red)
    hs.draw_text(img, "NO 0417-B", 128, 116, 3, ink)
    hs.draw_text(img, "DO NOT REMOVE", 128, 152, 2, ink)

    # VOID 底纹
    hs.rect(img, 384, 0, 512, 128, np.array([0.9, 0.88, 0.84]))
    for row in range(0, 128, 22):
        off = (row // 22) % 2 * 22
        for col in range(-22, 128, 56):
            x = 384 + col + off
            if 384 <= x and x + 46 <= 512:
                hs.draw_text(img, "VOID", x, row + 4, 2, np.array([0.8, 0.2, 0.18]))

    # 残胶：发黄、发黏、带纤维
    rng = np.random.default_rng(5)
    blot = hs.periodic_noise(128, 5, rng)
    residue = np.stack([0.58 + 0.12 * blot, 0.52 + 0.1 * blot, 0.3 + 0.06 * blot], axis=-1)
    img[128:256, 384:512] = residue
    prng = random.Random(9)
    for _ in range(60):                          # 粘上的灰尘颗粒
        x, y = prng.randrange(2, 126), prng.randrange(2, 126)
        img[128 + y - 1:128 + y + 1, 384 + x - 1:384 + x + 1] = np.array([0.22, 0.2, 0.16])
    for _ in range(30):                          # 撕下时残留的纸纤维
        x0, y0 = prng.uniform(0, 128), prng.uniform(0, 128)
        a = prng.uniform(0, math.pi)
        for t in range(int(prng.uniform(6, 16))):
            x, y = int(x0 + math.cos(a) * t) % 128, int(y0 + math.sin(a) * t) % 128
            img[128 + y, 384 + x] = np.array([0.88, 0.86, 0.8])
    return np.clip(img, 0, 1)


def pcb_px(lx, lz, size=512):
    """电路板局部坐标（米，u 方向 = 从背面看向右）→ 贴图像素 (x, y)，y 行 0 在顶部。"""
    return int((lx / PCB_W + 0.5) * size), int((1.0 - (lz / PCB_H + 0.5)) * size)


# 元件在电路板局部坐标中的位置（米）
HEADER_C = (-0.0095, 0.0055)
U7_C = (0.0085, 0.0050)
ROGUE_C = (0.0055, -0.0072)
TX_PAD = (0.0150, -0.0100)
HEADER_PAD = (-0.0120, -0.0010)


def make_pcb_texture(size=512):
    rng = np.random.default_rng(3)
    mask = hs.periodic_noise(size, 3, rng)
    base = np.array([0.05, 0.3, 0.15])
    img = base * (0.92 + 0.08 * mask[..., None])
    trace = np.array([0.1, 0.44, 0.21])
    pad = np.array([0.6, 0.6, 0.56])          # 出厂焊点：暗灰
    silk = np.array([0.92, 0.93, 0.9])

    prng = random.Random(4)
    for _ in range(46):                          # 走线
        x0, y0 = prng.randrange(20, size - 20), prng.randrange(20, size - 20)
        if prng.random() < 0.5:
            x1 = min(size - 20, max(20, x0 + prng.randrange(-200, 200)))
            hs.rect(img, min(x0, x1), y0, max(x0, x1) + 4, y0 + 4, trace)
            y1 = min(size - 20, max(20, y0 + prng.randrange(-120, 120)))
            hs.rect(img, x1, min(y0, y1), x1 + 4, max(y0, y1) + 4, trace)
        else:
            y1 = min(size - 20, max(20, y0 + prng.randrange(-200, 200)))
            hs.rect(img, x0, min(y0, y1), x0 + 4, max(y0, y1) + 4, trace)

    # J3 调试排针焊盘（2×5）
    hx, hy = pcb_px(*HEADER_C)
    for r in range(2):
        for c in range(5):
            px, py = hx - 52 + c * 26, hy - 13 + r * 26
            hs.disc(img, px, py, 8, pad)
    hs.rect_outline(img, hx - 68, hy - 30, hx + 68, hy + 30, 3, silk)
    hs.draw_text(img, "J3 DBG", hx - 60, hy - 92, 3, silk)    # 放在排针底座上方，不被遮挡

    # U7 主芯片焊盘
    ux, uy = pcb_px(*U7_C)
    for k in range(6):
        for side in (-1, 1):
            hs.rect(img, ux - 60 + k * 22, uy + side * 70 - 8, ux - 50 + k * 22, uy + side * 70 + 8, pad)
            hs.rect(img, ux + side * 70 - 8, uy - 60 + k * 22, ux + side * 70 + 8, uy - 50 + k * 22, pad)
    hs.draw_text(img, "U7", ux - 12, uy - 110, 3, silk)
    hs.draw_text(img, "TX CAL", *[v - d for v, d in zip(pcb_px(*TX_PAD), (40, 44))], 2, silk)
    tx, ty = pcb_px(*TX_PAD)
    hs.disc(img, tx, ty, 9, pad)
    hpx, hpy = pcb_px(*HEADER_PAD)
    hs.disc(img, hpx, hpy, 8, pad)

    # NC：这个位置按设计不应安装元件（未登记芯片就粘在这里）
    rx, ry = pcb_px(*ROGUE_C)
    for t in range(0, 120, 16):                   # 虚线框
        hs.rect(img, rx - 60 + t, ry - 60, rx - 52 + t, ry - 57, silk)
        hs.rect(img, rx - 60 + t, ry + 57, rx - 52 + t, ry + 60, silk)
        hs.rect(img, rx - 60, ry - 60 + t, rx - 57, ry - 52 + t, silk)
        hs.rect(img, rx + 57, ry - 60 + t, rx + 60, ry - 52 + t, silk)
    hs.draw_text(img, "NC", rx + 66, ry - 20, 3, silk)

    hs.draw_text(img, "BRD-NAV 2.1", 24, size - 40, 2, silk)
    for cx in (18, size - 18):                     # 安装孔
        for cy in (18, size - 18):
            hs.disc(img, cx, cy, 11, pad)
            hs.disc(img, cx, cy, 5, np.array([0.1, 0.1, 0.1]))
    return np.clip(img, 0, 1)


# ---------------------------------------------------------------------------
# 机身（正常部分）
# ---------------------------------------------------------------------------

def build_body(c):
    bm, _, cap = hs.bm_prism(octagon(0.084), BASE_Z, 0.016)
    hs.mark_bevel_weight(bm, cap)
    base = hs.make_object("Beacon_Base", bm, c, "M_Beacon_Dark", sharp_angle=30)
    hs.add_bevel(base, 0.003, 2, weighted=True)

    # 三只防陷雪支脚
    bm = bmesh.new()
    for ang in (90, 210, 330):
        a = math.radians(ang)
        rot = Matrix.Rotation(a, 3, "Z")        # 盒子长边沿半径向外
        add_box(bm, (0.032, 0.016, 0.007), (math.cos(a) * 0.082, math.sin(a) * 0.082, BASE_Z - 0.0005), rot)
    feet = hs.make_object("Beacon_Feet", bm, c, "M_Beacon_Dark")
    hs.add_bevel(feet, 0.0015, 2)

    for name, z0, h, r in (("Beacon_RingLow", BASE_Z + 0.016, 0.010, 0.0735),
                           ("Beacon_RingMid", -0.050, 0.007, 0.0718),
                           ("Beacon_RingTop", BODY_Z1, 0.008, 0.0735)):
        bm, _, cap = hs.bm_prism(octagon(r), z0, h)
        ring = hs.make_object(name, bm, c, "M_Beacon_Dark", sharp_angle=30)
        hs.add_bevel(ring, 0.0012, 2, angle=40)

    bm, _, _ = hs.bm_prism(octagon(R_BODY), BODY_Z0, BODY_Z1 - BODY_Z0)
    housing = hs.make_object("Beacon_Housing", bm, c, "M_Beacon_Housing", sharp_angle=30)
    hs.add_bevel(housing, 0.0016, 2, angle=40)

    bm, _, cap = hs.bm_prism(hs.circle_profile(0.066, 24), BODY_Z1 + 0.008, PLATE_Z - BODY_Z1 - 0.008)
    hs.mark_bevel_weight(bm, cap)
    plate = hs.make_object("Beacon_TopPlate", bm, c, "M_Beacon_Metal", sharp_angle=30)
    hs.add_bevel(plate, 0.0012, 1, weighted=True)

    # 两侧起吊环（半圆环）与侧面铭牌
    pts = []
    for i in range(11):
        a = math.pi * i / 10
        pts.append((APOTHEM + 0.001 + math.sin(a) * 0.009, 0.0, 0.030 + math.cos(a) * 0.009))
    lug = hs.make_object("Beacon_LiftLug", hs.bm_tube(pts, [0.0022] * len(pts), segs=8), c, "M_Beacon_Metal", sharp_angle=60)
    hs.add_mirror(lug, x=True)

    bm = hs.bm_box((0.0015, 0.030, 0.018), (APOTHEM + 0.0006, 0.0, -0.030))
    hs.round_edges(bm, (1, 0, 0), 0.002, 2)
    hs.make_object("Beacon_DataPlate", bm, c, "M_Beacon_Metal")

    # 背面维护舱外壳（舱内元件属于 Port 组）
    bm = hs.bm_box((0.046, 0.012, 0.036), (0.0, BACK_Y + 0.006, PORT_CZ))
    hs.round_edges(bm, (0, 1, 0), 0.003, 2)
    hs.inset_and_push(bm, hs.face_towards(bm, (0, 1, 0)), 0.004, 0.0088)
    bay = hs.make_object("Beacon_PortBay", bm, c, "M_Beacon_Dark")
    hs.add_bevel(bay, 0.0008, 1, angle=40)


# ---------------------------------------------------------------------------
# 检修封签（关键证据：撕开后重贴、残胶、螺丝划痕）
# ---------------------------------------------------------------------------

PANEL_T = 0.0015
PANEL_Y = FRONT_Y - PANEL_T                 # 面板前表面
SEAM_X = 0.016                              # 面板右侧接缝
SEAL_Z = 0.018                              # 封签中心：同上，靠近包围盒中心
SEAL_H = 0.012


def jagged_quad(bm, x0, x1, z0, z1, y, jag_side, amount, steps=7):
    """一张平面，jag_side = 'left'/'right' 的那条竖边呈撕裂锯齿。返回新面。"""
    zs = [z0 + (z1 - z0) * i / steps for i in range(steps + 1)]
    jit = [RNG.uniform(-amount, amount) if 0 < i < steps else 0.0 for i in range(steps + 1)]
    if jag_side == "right":
        # 左下 → 右边（自下而上，锯齿）→ 左上
        coords = [(x0, z0)] + [(x1 + j, z) for z, j in zip(zs, jit)] + [(x0, z1)]
    else:
        # 右下 → 右上 → 左边（自上而下，锯齿）
        coords = [(x1, z0), (x1, z1)] + [(x0 + j, z) for z, j in zip(reversed(zs), reversed(jit))]
    f = bm.faces.new([bm.verts.new((x, y, z)) for x, z in coords])
    bm.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    return f


def build_seal(c):
    # 金属检修面板
    bm = hs.bm_box((0.032, PANEL_T, 0.052), (0.0, FRONT_Y - PANEL_T / 2, 0.012))
    hs.round_edges(bm, (0, 1, 0), 0.003, 2)
    panel = hs.make_object("Beacon_SealPanel", bm, c, "M_Beacon_Metal")
    hs.add_bevel(panel, 0.0004, 1, angle=40)

    # 四颗涂漆螺丝（沿 -Y 方向）
    screw_pos = [(-0.012, 0.034), (0.012, 0.034), (-0.012, -0.010), (0.012, -0.010)]
    bm = bmesh.new()
    for (x, z) in screw_pos:
        prof = hs.circle_profile(0.0022, 12)
        m = Matrix.Translation((x, PANEL_Y, z)) @ Matrix.Rotation(math.radians(90), 4, "X")
        sub, _, _ = hs.bm_prism(prof, 0.0, 0.0012)
        bmesh.ops.transform(sub, matrix=m, verts=sub.verts)
        tmp = bpy.data.meshes.new("_tmp")
        sub.to_mesh(tmp)
        sub.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        add_box(bm, (0.0034, 0.0005, 0.0006), (x, PANEL_Y - 0.0012, z))          # 一字槽
    hs.make_object("Beacon_SealScrews", bm, c, "M_Beacon_Dark", sharp_angle=40)

    # 新鲜划痕：右侧两颗螺丝头和旁边面板上的亮金属刮痕（非原厂工具）
    bm = bmesh.new()
    for (x, z) in ((0.012, 0.034), (0.012, -0.010)):
        # 螺丝头上的刮痕：限制在螺丝头直径内，贴平表面
        for k in range(4):
            ang = RNG.uniform(-60, 60)
            length = RNG.uniform(0.0016, 0.0030)
            rot = Matrix.Rotation(math.radians(ang), 3, "Y")
            add_box(bm, (length, 0.00008, 0.00030), (x + RNG.uniform(-0.0005, 0.0005), PANEL_Y - 0.00124, z + RNG.uniform(-0.0005, 0.0005)), rot)
        # 螺丝旁面板上的滑刀痕
        for k in range(2):
            ang = RNG.uniform(-35, 35)
            rot = Matrix.Rotation(math.radians(ang), 3, "Y")
            add_box(bm, (RNG.uniform(0.003, 0.005), 0.00008, 0.00028), (x - 0.0045, PANEL_Y - 0.00005, z + RNG.uniform(-0.002, 0.002)), rot)
    hs.make_object("Beacon_ToolScratches", bm, c, "M_Beacon_Solder", smooth=False)

    # 封签：左半在面板上（未动），右半在机身上（重贴时下移约 1.5 mm、歪斜约 5°）
    x_left0, x_right1 = 0.005, 0.0262
    z0, z1 = SEAL_Z - SEAL_H / 2, SEAL_Z + SEAL_H / 2
    sticker_w = x_right1 - x_left0

    bm = bmesh.new()
    jagged_quad(bm, x_left0, SEAM_X, z0, z1, PANEL_Y - 0.00015, "right", 0.0007)
    hs.planar_uv(bm, ((x_left0 + x_right1) / 2, 0, SEAL_Z), (1, 0, 0), (0, 0, 1), sticker_w, SEAL_H, 0.0, 0.25, 0.75, 0.75)
    hs.make_object("Beacon_SealStickerLeft", bm, c, "M_Beacon_Seal", smooth=False, uv=False)

    bm = bmesh.new()
    jagged_quad(bm, SEAM_X + 0.0004, x_right1, z0, z1, FRONT_Y - 0.0003, "left", 0.0007)
    hs.planar_uv(bm, ((x_left0 + x_right1) / 2, 0, SEAL_Z), (1, 0, 0), (0, 0, 1), sticker_w, SEAL_H, 0.0, 0.25, 0.75, 0.75)
    hs.rotate_verts(bm, "Y", 5.0, (SEAM_X, FRONT_Y, SEAL_Z))
    bmesh.ops.translate(bm, vec=(0.0, 0.0, -0.0015), verts=bm.verts)
    hs.make_object("Beacon_SealStickerRight", bm, c, "M_Beacon_Seal", smooth=False, uv=False)

    # 原位置的残胶（右半下移后，上沿露出一条）
    bm = bmesh.new()
    f = jagged_quad(bm, SEAM_X + 0.0002, x_right1 + 0.0006, z0 - 0.0004, z1 + 0.0006, FRONT_Y - 0.0001, "left", 0.0003, steps=3)
    hs.planar_uv(bm, (SEAM_X + 0.005, 0, SEAL_Z), (1, 0, 0), (0, 0, 1), 0.012, 0.014, 0.75, 0.0, 0.25, 0.5)
    hs.make_object("Beacon_SealResidue", bm, c, "M_Beacon_Seal", smooth=False, uv=False)

    # 接缝处露出的 VOID 防伪底纹
    bm = bmesh.new()
    jagged_quad(bm, SEAM_X, SEAM_X + 0.0026, z0 - 0.0015, z1, FRONT_Y - 0.0002, "left", 0.0002, steps=3)
    hs.planar_uv(bm, (SEAM_X + 0.0013, 0, SEAL_Z), (1, 0, 0), (0, 0, 1), 0.006, 0.012, 0.75, 0.5, 0.25, 0.5)
    hs.make_object("Beacon_SealVoid", bm, c, "M_Beacon_Seal", smooth=False, uv=False)


# ---------------------------------------------------------------------------
# 调试接口（关键证据：新焊跳线 + 未登记芯片）
# ---------------------------------------------------------------------------

def pcb_world(lx, lz, h=0.0):
    """电路板局部坐标 → 世界坐标。从背面看，局部 +x 在右侧 = 世界 -X。"""
    return Vector((-lx, PCB_Y + h, PORT_CZ + lz))


def build_port(c):
    # 电路板（带贴图）
    bm = bmesh.new()
    v = [bm.verts.new(pcb_world(-PCB_W / 2, -PCB_H / 2)), bm.verts.new(pcb_world(PCB_W / 2, -PCB_H / 2)),
         bm.verts.new(pcb_world(PCB_W / 2, PCB_H / 2)), bm.verts.new(pcb_world(-PCB_W / 2, PCB_H / 2))]
    f = bm.faces.new(v)
    bm.normal_update()
    if f.normal.y < 0:
        f.normal_flip()
    hs.planar_uv(bm, pcb_world(0, 0), (-1, 0, 0), (0, 0, 1), PCB_W, PCB_H)
    hs.make_object("Beacon_PCB", bm, c, "M_Beacon_PCB", smooth=False, uv=False)

    up = Matrix.Identity(3)

    # J3 调试排针（2×5）
    bm = bmesh.new()
    hx, hz = HEADER_C
    add_box(bm, (0.0128, 0.0025, 0.0052), pcb_world(hx, hz, 0.00125))
    hs.make_object("Beacon_HeaderBase", bm, c, "M_Beacon_Dark")
    bm = bmesh.new()
    for r in range(2):
        for col in range(5):
            add_box(bm, (0.0006, 0.0060, 0.0006), pcb_world(hx - 0.00508 + col * 0.00254, hz - 0.00127 + r * 0.00254, 0.003))
    hs.make_object("Beacon_HeaderPins", bm, c, "M_Beacon_Metal", smooth=False)

    # U7 出厂主芯片（整齐、四边引脚）
    ux, uz = U7_C
    bm = bmesh.new()
    add_box(bm, (0.0082, 0.0012, 0.0082), pcb_world(ux, uz, 0.0006))
    hs.make_object("Beacon_U7", bm, c, "M_Beacon_Dark")
    bm = bmesh.new()
    for k in range(6):
        off = -0.00275 + k * 0.0011
        add_box(bm, (0.0004, 0.0003, 0.0014), pcb_world(ux + off, uz + 0.0048, 0.00015))
        add_box(bm, (0.0004, 0.0003, 0.0014), pcb_world(ux + off, uz - 0.0048, 0.00015))
        add_box(bm, (0.0014, 0.0003, 0.0004), pcb_world(ux + 0.0048, uz + off, 0.00015))
        add_box(bm, (0.0014, 0.0003, 0.0004), pcb_world(ux - 0.0048, uz + off, 0.00015))
    hs.make_object("Beacon_U7Pins", bm, c, "M_Beacon_Metal", smooth=False)

    # 未登记芯片：歪斜地粘在 NC 位置上，下面是残胶
    rx, rz = ROGUE_C
    tilt = Matrix.Rotation(math.radians(14), 3, "Y")
    bm = bmesh.new()
    add_sphere(bm, 0.0042, pcb_world(rx, rz, 0.0003), scale=(1.0, 0.22, 0.85), u=10, v=6)
    glue = hs.make_object("Beacon_RogueGlue", bm, c, "M_Beacon_Seal", smooth=True, uv=False)
    # 残胶使用封签贴图集的“残胶”区域
    uv_layer = glue.data.uv_layers.new(name="UVMap")
    for loop in glue.data.loops:
        co = glue.data.vertices[loop.vertex_index].co
        uv_layer.data[loop.index].uv = (0.75 + ((co.x + rx) / 0.01 + 0.5) * 0.25, ((co.z - PORT_CZ - rz) / 0.01 + 0.5) * 0.5)

    bm = bmesh.new()
    add_box(bm, (0.0056, 0.0011, 0.0056), pcb_world(rx, rz, 0.0012), tilt)
    hs.make_object("Beacon_RogueChip", bm, c, "M_Beacon_Dark")
    bm = bmesh.new()
    for side in (-1, 1):
        for k in range(2):
            local = Vector((side * 0.0034, 0, -0.0012 + k * 0.0024))
            add_box(bm, (0.0014, 0.0003, 0.0005), pcb_world(rx, rz, 0.0009) + tilt @ Vector((-local.x, 0, local.z)), tilt)
    hs.make_object("Beacon_RogueChipLegs", bm, c, "M_Beacon_Solder", smooth=False)

    # 两根红色飞线：调试排针 → 未登记芯片 → TX 校准焊盘
    def arc(a, b, lift, n=8):
        pa, pb = pcb_world(*a, 0.0008), pcb_world(*b, 0.0008)
        pts = []
        for i in range(n):
            t = i / (n - 1)
            p = pa.lerp(pb, t)
            p.y += math.sin(math.pi * t) * lift
            p.x += math.sin(math.pi * t) * 0.0015
            pts.append(p)
        return pts

    chip_left = (rx - 0.0034, rz + 0.0012)
    chip_right = (rx + 0.0036, rz - 0.0010)
    w1 = hs.bm_tube(arc(HEADER_PAD, chip_left, 0.0045), [0.00055] * 8, segs=8)
    hs.make_object("Beacon_JumperA", w1, c, "M_Beacon_Wire", sharp_angle=70)
    w2 = hs.bm_tube(arc(chip_right, TX_PAD, 0.0040), [0.00055] * 8, segs=8)
    hs.make_object("Beacon_JumperB", w2, c, "M_Beacon_Wire", sharp_angle=70)

    # 新焊点：亮银、偏大、形状不规整（与出厂暗灰焊点对比）
    bm = bmesh.new()
    for p in (HEADER_PAD, chip_left, chip_right, TX_PAD):
        add_sphere(bm, 0.0012, pcb_world(*p, 0.0004), scale=(RNG.uniform(1.0, 1.4), 0.6, RNG.uniform(0.9, 1.3)), u=10, v=6)
    hs.make_object("Beacon_FreshSolder", bm, c, "M_Beacon_Solder", smooth=True, uv=False, sharp_angle=80)

    # 橡胶防尘盖：打开并翻到一侧（铰链在维护舱左缘，从背面看）
    hinge = Vector((0.023, BACK_Y + 0.012, PORT_CZ))
    bm = hs.bm_box((0.044, 0.0025, 0.034), (hinge.x - 0.022, hinge.y + 0.00125, PORT_CZ))
    hs.round_edges(bm, (0, 1, 0), 0.003, 2)
    # 翻开 150°：盖子贴着舱体外侧斜向张开，不会大幅撑大整体包围盒
    bmesh.ops.rotate(bm, cent=hinge, matrix=Matrix.Rotation(math.radians(-150), 3, "Z"), verts=bm.verts)
    cover = hs.make_object("Beacon_PortCover", bm, c, "M_Beacon_Dark")
    hs.add_bevel(cover, 0.0007, 1, angle=40)


# ---------------------------------------------------------------------------
# 灯组（正常）
# ---------------------------------------------------------------------------

def build_lamp(c):
    bm, _, cap = hs.bm_prism(hs.circle_profile(0.032, 24), PLATE_Z, 0.008)
    hs.mark_bevel_weight(bm, cap)
    collar = hs.make_object("Beacon_LampCollar", bm, c, "M_Beacon_Dark", sharp_angle=30)
    hs.add_bevel(collar, 0.001, 1, weighted=True)

    # 菲涅尔灯罩：三级阶梯
    z = PLATE_Z + 0.008
    for i, (r, h) in enumerate(((0.029, 0.016), (0.0262, 0.014), (0.0222, 0.012))):
        bm, _, cap = hs.bm_prism(hs.circle_profile(r, 24), z, h)
        hs.mark_bevel_weight(bm, cap)
        lens = hs.make_object(f"Beacon_Lens{i + 1}", bm, c, "M_Beacon_Lens", sharp_angle=30)
        hs.add_bevel(lens, 0.0012, 1, weighted=True)
        z += h
    lens_top = z

    bm, _, cap = hs.bm_prism(hs.circle_profile(0.030, 24), lens_top, 0.006)
    hs.mark_bevel_weight(bm, cap)
    capobj = hs.make_object("Beacon_LampCap", bm, c, "M_Beacon_Dark", sharp_angle=30)
    hs.add_bevel(capobj, 0.0015, 2, weighted=True)
    bm, _, cap = hs.bm_prism(hs.knurl_profile(0.009, 0.0082, 10), lens_top + 0.006, 0.005)
    hs.mark_bevel_weight(bm, cap)
    vent = hs.make_object("Beacon_LampVent", bm, c, "M_Beacon_Dark", sharp_angle=30)
    hs.add_bevel(vent, 0.0008, 1, weighted=True)

    # 护笼：6 根立柱 + 中环 + 上环，完整无变形
    bm = bmesh.new()
    cage_r = 0.0335
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        pts = [(math.cos(a) * cage_r, math.sin(a) * cage_r, PLATE_Z + 0.004), (math.cos(a) * cage_r, math.sin(a) * cage_r, lens_top + 0.003)]
        sub = hs.bm_tube(pts, [0.0017, 0.0017], segs=8)
        tmp = bpy.data.meshes.new("_tmp")
        sub.to_mesh(tmp)
        sub.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    for zr in (PLATE_Z + 0.030, lens_top + 0.002):
        ring_pts = [(math.cos(2 * math.pi * i / 24) * cage_r, math.sin(2 * math.pi * i / 24) * cage_r, zr) for i in range(24)]
        sub = hs.bm_tube(ring_pts, [0.0015] * 24, segs=6, closed_loop=True)
        tmp = bpy.data.meshes.new("_tmp")
        sub.to_mesh(tmp)
        sub.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    hs.make_object("Beacon_LampCage", bm, c, "M_Beacon_Metal", sharp_angle=60)


# ---------------------------------------------------------------------------
# 外置天线（正常）
# ---------------------------------------------------------------------------

def build_mast(c):
    mx, my = MAST_XY
    bm, _, cap = hs.bm_prism(hs.circle_profile(0.0065, 6), PLATE_Z, 0.003, (mx, my))
    hs.make_object("Beacon_MastNut", bm, c, "M_Beacon_Metal", sharp_angle=25)
    bm, _, cap = hs.bm_prism(hs.knurl_profile(0.0055, 0.005, 12), PLATE_Z + 0.003, 0.008, (mx, my))
    hs.mark_bevel_weight(bm, cap)
    conn = hs.make_object("Beacon_MastConnector", bm, c, "M_Beacon_Metal", sharp_angle=25)
    hs.add_bevel(conn, 0.0005, 1, weighted=True)

    # 弹簧减震段（笔直、匝距均匀）
    z0 = PLATE_Z + 0.011
    pts = []
    turns, per = 4, 7
    for i in range(turns * per + 1):
        a = 2 * math.pi * i / per
        pts.append((mx + math.cos(a) * 0.0034, my + math.sin(a) * 0.0034, z0 + 0.018 * i / (turns * per)))
    hs.make_object("Beacon_MastSpring", hs.bm_tube(pts, [0.0008] * len(pts), segs=6), c, "M_Beacon_Metal", sharp_angle=70)

    whip_pts = [(mx, my, z0 + 0.018 + 0.1 * t / 5) for t in range(6)]
    radii = [0.0032 - 0.001 * t / 5 for t in range(6)]
    hs.make_object("Beacon_MastWhip", hs.bm_tube(whip_pts, radii, segs=10), c, "M_Beacon_Dark", sharp_angle=50)

    bm = bmesh.new()
    add_sphere(bm, 0.0032, (mx, my, whip_pts[-1][2] + 0.0015), u=10, v=6)
    hs.make_object("Beacon_MastTip", bm, c, "M_Beacon_Dark", sharp_angle=80)


# ---------------------------------------------------------------------------

def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(SHOT_DIR, exist_ok=True)
    scene, groups = hs.reset_scene("NavBeacon", GROUPS, "Beacon_")

    images = {
        GRIME_TEX: hs.save_image(GRIME_TEX, hs.make_grime_texture(1024, seed=17), TEX_DIR),
        SEAL_TEX: hs.save_image(SEAL_TEX, make_seal_texture(), TEX_DIR),
        PCB_TEX: hs.save_image(PCB_TEX, make_pcb_texture(), TEX_DIR),
    }
    hs.build_materials(MATERIALS, images)

    build_body(groups["Body"])
    build_seal(groups["Seal"])
    build_port(groups["Port"])
    build_lamp(groups["Lamp"])
    build_mast(groups["Mast"])

    stats = hs.collect_stats(groups, images, MATERIALS.keys())
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[NavBeacon] stats:", json.dumps(stats, ensure_ascii=False))

    if DO_EXPORT:
        hs.export_groups(groups, MODEL_DIR, "NavBeacon")
        hs.write_material_manifest(MATERIALS, os.path.join(UNITY_ART, "NavBeacon_Materials.json"))

    cam = hs.setup_studio(scene, BASE_Z - 0.004, target=(0, 0, 0.02))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "NavBeacon.blend"))

    if DO_RENDER:
        def shot(name, loc, target, lens):
            hs.render_view(scene, cam, os.path.join(SHOT_DIR, f"{SHOT_PREFIX}_{name}.png"), loc, target, lens)

        shot("01_front", (0.16, -0.86, 0.18), (0, 0, 0.035), 70)
        shot("02_back", (-0.18, 0.86, 0.17), (0, 0, 0.035), 70)
        shot("03_seal_closeup", (0.05, -0.20, 0.0), (0.014, FRONT_Y, SEAL_Z - 0.004), 100)
        shot("04_port_closeup", (-0.045, 0.20, 0.01), (0.0, BACK_Y + 0.004, PORT_CZ), 95)
        shot("05_lamp_mast_normal", (0.17, -0.26, 0.30), (0.015, 0.0, 0.12), 60)
    print("[NavBeacon] done")


if __name__ == "__main__":
    main()
