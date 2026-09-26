"""
边境维修站 · 工人义手（正式模型）+ 三件维修工具 + 简化手套手 建模脚本
生成方式：Claude 辅助编写的 Blender Python 建模脚本（程序化建模 + 程序化贴图），未使用图生 3D 服务或外部素材。
风格：原创复古工业（搪瓷漆钢壳、黄铜件、黑橡胶、布包线缆、胶木手柄），细节集中在玩家会检查的部位。

用法（Blender 5.2）：
    blender.exe -b --factory-startup --python build_workerhand.py -- [--no-render] [--export]

坐标：米，Z 向上，镜头（检查台）一侧为 -Y。前臂沿 X，插座在 -X、手指朝 +X；手背、维修舱盖、封条、螺丝朝 -Y；
手腕顶面（+Z）是数据接口；手指向 +Y（掌心一侧）弯曲。与现有占位 prefab 的布局一致（Unity 中 Y/Z 互换）。
工具的“作用点”在各自原点，工具轴线沿 +Z（刀头 / 探针 / 插头朝 -Z 方向接触物体）；手套手握持轴线也沿 +Z。
第一阶段只生成 .blend、贴图和渲染图；确认后再用 --export 导出 FBX 到 Unity。
"""

import json
import shutil
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
TEX_DIR = os.path.join(HERE, "Textures")                       # 确认前不写入 Unity Assets
EXPORT_DIR = os.path.join(PROJECT, "Assets", "BorderRepair", "Art", "WorkerHand", "Models")
SHOT_DIR = os.path.join(PROJECT, "Docs", "AssetEvaluation", "Screenshots")
SHOT_PREFIX = "20260925_WorkerHandModel"

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "--no-render" not in ARGS
DO_EXPORT = "--export" in ARGS
ONLY = next((a.split("=", 1)[1] for a in ARGS if a.startswith("--only=")), None)   # 只渲染某几张（逗号分隔的编号）
# 外观版本：v1 = 第一阶段（2026-09-25 首次评审）；v2 = 地下诊所 lookdev（默认），见 workerhand_lookdev.py
LOOK = next((a.split("=", 1)[1] for a in ARGS if a.startswith("--look=")), "v2")
LOOKDEV = "--lookdev" in ARGS          # 只渲染 lookdev 对比组（同机位），不覆盖第一阶段的评审图
LOOKDEV_PREFIX = "20260925_WorkerHandLookdev"
sys.path.insert(0, HERE)
import workerhand_lookdev as ld  # noqa: E402

RNG = random.Random(20260925)

GRIME_TEX = "T_Hand_Grime.png"
DECAL_TEX = "T_Hand_Decals.png"
BOARD_TEX = "T_Hand_Board.png"

MATERIALS = {
    # 海绿色搪瓷漆钢壳（前臂、盖板）
    "M_Hand_Enamel": dict(color="#5E7F72", metallic=0.1, roughness=0.5, base_map=GRIME_TEX),
    # 奶油色搪瓷（手背护板、掌部面板）
    "M_Hand_Cream": dict(color="#D6CCB4", metallic=0.0, roughness=0.5, base_map=GRIME_TEX),
    # 裸钢：关节销、螺丝、腱绳；金属度 0.8，Unity 检查台没有高质量反射时不至于发黑
    "M_Hand_Steel": dict(color="#A9ADAE", metallic=0.8, roughness=0.32, base_map=GRIME_TEX),
    # 磨损钢：旧齿轮（发暗、粗糙）
    "M_Hand_WornSteel": dict(color="#7A736A", metallic=0.6, roughness=0.62, base_map=GRIME_TEX),
    "M_Hand_Brass": dict(color="#B8925A", metallic=0.8, roughness=0.38, base_map=GRIME_TEX),
    "M_Hand_Rubber": dict(color="#26272A", metallic=0.0, roughness=0.75, base_map=GRIME_TEX),
    "M_Hand_Dark": dict(color="#34383A", metallic=0.3, roughness=0.5, base_map=GRIME_TEX),
    "M_Hand_Cable": dict(color="#6A4E36", metallic=0.0, roughness=0.8, base_map=GRIME_TEX),
    "M_Hand_Accent": dict(color="#D9A21E", metallic=0.0, roughness=0.5),
    "M_Hand_Board": dict(color="#FFFFFF", metallic=0.0, roughness=0.5, base_map=BOARD_TEX),
    "M_Hand_Decal": dict(color="#FFFFFF", metallic=0.0, roughness=0.55, base_map=DECAL_TEX),
    "M_Hand_LensOff": dict(color="#3A1512", metallic=0.1, roughness=0.15),
    "M_Hand_LedRed": dict(color="#FF3322", metallic=0.0, roughness=0.3, emission_color="#FF2A1A", emission_strength=6.0),
    "M_Hand_LedGreen": dict(color="#44FF66", metallic=0.0, roughness=0.3, emission_color="#33FF55", emission_strength=4.0),
    "M_Tool_Bakelite": dict(color="#4A2E1E", metallic=0.0, roughness=0.35, base_map=GRIME_TEX),
    "M_Tool_Orange": dict(color="#C4561C", metallic=0.0, roughness=0.45, base_map=GRIME_TEX),
    "M_Tool_Red": dict(color="#A5231C", metallic=0.0, roughness=0.4),
    "M_Tool_Case": dict(color="#8C8A6E", metallic=0.0, roughness=0.55, base_map=GRIME_TEX),
    "M_Glove": dict(color="#6E6B55", metallic=0.0, roughness=0.85, base_map=GRIME_TEX),
    "M_Glove_Palm": dict(color="#3E3A33", metallic=0.0, roughness=0.8, base_map=GRIME_TEX),
}

# 分组：每组以后导出为一个 FBX（组名与 Unity 中的部位 / 工具对应）
GROUPS = [
    "Body", "Shell", "SealIntact", "SealTorn", "FastenerA", "FastenerB",
    "DriveStatic", "DriveWorn", "DriveNew", "DriveTestClip",
    "Limiter", "LimiterLedRed", "Board", "BoardLedGreen", "DataPort", "Tray",
    "Tool_Screwdriver", "Tool_Pry", "Tool_TesterBody", "Tool_Probe", "Tool_Plug", "Glove", "GlovePinch",
]
TOOL_GROUPS = ["Tool_Screwdriver", "Tool_Pry", "Tool_TesterBody", "Tool_Probe", "Tool_Plug", "Glove", "GlovePinch"]

# ---------------------------------------------------------------------------
# 主要尺寸（与占位 prefab 对齐）
# ---------------------------------------------------------------------------
FRONT_Y = -0.030            # 前臂前表面（盖板背面贴着它）
COVER_T = 0.004
COVER_FRONT = FRONT_Y - COVER_T - 0.0005          # -0.0345
COVER_X0, COVER_X1 = -0.143, 0.023
COVER_Z = 0.027                                   # 盖板上下半高
BAY_X0, BAY_X1, BAY_Z = -0.138, 0.018, 0.022      # 维修舱开口
BAY_BACK = -0.008                                 # 舱底
SCREW_A = (-0.120, 0.016)
SCREW_B = (0.004, -0.016)
SCREW_R = 0.0045
SEAL = dict(x=-0.120, z=0.0205, w=0.036, h=0.009)
SEAL_TORN_W = 0.014                               # 撕开后留在盖板上的左半边
WRIST_X = 0.068
PORT_TOP = 0.0305
TRAY_Z = -0.078
TRAY = dict(x=-0.06, y=-0.032, w=0.23, d=0.10)          # 右格要放得下平躺的盖板（166 mm）
LIMITER_PAD = (0.0045, -0.008 - 0.00925, -0.0012)        # 限力器外壳正面的测试点（BAY_BACK = -0.008）


def anchor_data():
    """
    工具锚点（Blender 坐标，Unity 构建器换算）：pos = 工具作用端的接触点；out = 从接触点指向工具柄的方向（工具从这里进来）；
    arm = 手套手臂大致伸出的方向（避开关键线索、不顺着工具轴线挡镜头）；lever = 撬片施力时工具轴线倒向的方向。
    所有方向都在物品自身坐标里定义，与相机无关；物品旋转、缩放后锚点随零件一起变换。
    """
    sa, sb = SCREW_A, SCREW_B
    socket_y = COVER_FRONT - 0.0012                         # 螺丝头内六角孔的正面
    return {
        "anchors": {
            "fastener_a": dict(kind="Screwdriver", pos=(sa[0], socket_y, sa[1]), out=(0, -1, 0), arm=(0.45, 0, -1)),
            "fastener_b": dict(kind="Screwdriver", pos=(sb[0], socket_y, sb[1]), out=(0, -1, 0), arm=(0.6, 0, -1)),
            "shell": dict(kind="Pry", pos=(COVER_X1 + 0.0006, FRONT_Y - 0.0012, 0.0), out=(0.55, -1, 0), arm=(0, 0, -1), lever=(1, 0, 0)),
            "force_limiter": dict(kind="Probe", pos=(LIMITER_PAD[0], LIMITER_PAD[1] - 0.0003, LIMITER_PAD[2]), out=(0.45, -1, -0.4), arm=(1, 0, -0.3)),     # 手臂从右侧水平伸入：再往下会让护袖伸进零件盘
            "drive": dict(kind="Probe", pos=(-0.062, BAY_BACK - 0.0105, 0.006), out=(0.35, -1, -0.4), arm=(1, 0, -0.8)),
            "control_board": dict(kind="Probe", pos=(-0.0975, BAY_BACK - 0.0042, -0.0045), out=(0.35, -1, -0.4), arm=(1, 0, -0.8)),
            "data_port": dict(kind="Plug", pos=(WRIST_X, -0.001, PORT_TOP + 0.0048), out=(0, 0, 1), arm=(-0.45, 1, 0)),   # 朝后并偏向前臂，避开拇指
        },
        # 零件盘上的吸附位置（位置；朝向在 Unity 里按“盖板正面朝上、螺丝平躺”计算）
        "snaps": {
            "Snap_ShellOnTray": (-0.0385, -0.030, TRAY_Z + 0.003 + COVER_T / 2 + 0.0005),
            "Snap_FastenerA_OnTray": (-0.14, -0.062, TRAY_Z + 0.003 + SCREW_R),
            "Snap_FastenerB_OnTray": (-0.14, -0.040, TRAY_Z + 0.003 + SCREW_R),
        },
        # 工具与手套：握持中心在工具轴线上的高度（米），手套组
        "tools": {
            "Screwdriver": dict(group="Tool_Screwdriver", glove="Glove", grip=0.12),
            "Pry": dict(group="Tool_Pry", glove="Glove", grip=0.1),
            "Probe": dict(group="Tool_Probe", glove="GlovePinch", grip=0.07),     # 握在笔身后段，手不贴近舱口
            "Plug": dict(group="Tool_Plug", glove="GlovePinch", grip=0.045),      # 握住插头壳和尾套，手掌不压到腕部
        },
    }

# 贴图集区域 (u0, v0, u_span, v_span)
UV_SEAL = (0.0, 0.75, 1.0, 0.25)
UV_PLATE = (0.0, 0.5, 0.5, 0.25)
UV_STENCIL = (0.5, 0.5, 0.5, 0.25)
UV_SCREEN_IDLE = (0.0, 0.25, 1 / 3, 0.25)
UV_SCREEN_BYPASS = (1 / 3, 0.25, 1 / 3, 0.25)
UV_SCREEN_LOG = (2 / 3, 0.25, 1 / 3, 0.25)
UV_NEWTAG = (0.0, 0.0, 0.25, 0.25)
UV_BYPASSTAG = (0.25, 0.0, 0.25, 0.25)

# 缺少的像素字形（hs.FONT 没有 F/K/W/Y/Q）
hs.FONT.update({
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "Q": ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
    ":": ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
    "#": ["01010", "11111", "01010", "01010", "01010", "11111", "01010"],
})


# ---------------------------------------------------------------------------
# 几何工具
# ---------------------------------------------------------------------------

def xf(bm, loc=(0, 0, 0), rot=None, scale=None):
    """rot: (axis, degrees) 或 Matrix 或 None；先缩放、再旋转、后平移。"""
    m = Matrix.Identity(4)
    if scale is not None:
        m = Matrix.Diagonal((*scale, 1.0)) @ m
    if rot is not None:
        r = rot if isinstance(rot, Matrix) else Matrix.Rotation(math.radians(rot[1]), 4, rot[0])
        m = r.to_4x4() @ m
    m = Matrix.Translation(loc) @ m
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bm


def align_z(axis):
    """把 +Z 转到 axis 方向的旋转矩阵。"""
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()


def merge(dst, src):
    """把 src bmesh 合并进 dst（通过临时网格）。"""
    me = bpy.data.meshes.new("_tmp")
    src.to_mesh(me)
    src.free()
    dst.from_mesh(me)
    bpy.data.meshes.remove(me)
    return dst


def se_ring(segs, hy, hz, p):
    pts = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        c, s = math.cos(a), math.sin(a)
        pts.append((hy * math.copysign(abs(c) ** (2 / p), c), hz * math.copysign(abs(s) ** (2 / p), s)))
    return pts


def bm_loft_x(sections, segs=32, cap=True):
    """sections: [(x, hy, hz, p, cy, cz)]，沿 X 放样超椭圆截面。"""
    bm = bmesh.new()
    rings = []
    for (x, hy, hz, p, cy, cz) in sections:
        rings.append([bm.verts.new((x, cy + y, cz + z)) for y, z in se_ring(segs, hy, hz, p)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def cyl(r, length, segs=16, axis=(0, 0, 1), center=(0, 0, 0), r2=None):
    """圆柱（可做成锥台 r→r2），中心在 center，沿 axis。"""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=length)
    return xf(bm, center, align_z(axis))


def sphere(r, center, scale=(1, 1, 1), u=12, v=8):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    return xf(bm, center, None, scale)


def rounded_box(size, center, round_axis=(0, 0, 1), r=0.002, segs=2):
    bm = hs.bm_box(size)
    hs.round_edges(bm, round_axis, r, segs)
    return xf(bm, center)


def obj(name, bm, group, mat, bevel=None, sharp=35.0, uv=True, smooth=True):
    o = hs.make_object(name, bm, G[group], mat, smooth=smooth, sharp_angle=sharp, uv=uv)
    if bevel:
        hs.add_bevel(o, bevel, 2, angle=40)
    return o


def decal_uv(bm, region, axis_u, axis_v, origin, size_u, size_v):
    u0, v0, us, vs = region
    hs.planar_uv(bm, origin, axis_u, axis_v, size_u, size_v, u0, v0, us, vs)


G = {}


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def region_px(region, w, h):
    u0, v0, us, vs = region
    return int(u0 * w), int((1 - v0 - vs) * h), int((u0 + us) * w), int((1 - v0) * h)


def text_centered(img, text, box, scale, color, dy=0):
    x0, y0, x1, y1 = box
    tw = len(text) * 6 * scale - scale
    hs.draw_text(img, text, x0 + (x1 - x0 - tw) // 2, y0 + (y1 - y0 - 7 * scale) // 2 + dy, scale, color)


def make_decals():
    W = H = 1024
    img = np.zeros((H, W, 3))
    cream = np.array([0.98, 0.94, 0.85])
    ink = np.array([0.08, 0.08, 0.07])

    # 租赁封条：红底两行字 + 细边框 + 序号
    x0, y0, x1, y1 = region_px(UV_SEAL, W, H)
    img[y0:y1, x0:x1] = [0.74, 0.12, 0.10]
    hs.rect_outline(img, x0 + 10, y0 + 10, x1 - 10, y1 - 10, 5, cream)
    text_centered(img, "NSP PORT LEASED", (x0, y0 + 14, x1, y0 + 128), 10, cream)
    text_centered(img, "VOID IF OPENED", (x0, y0 + 124, x1, y1 - 14), 10, cream)

    # 铭牌：黄铜底黑字
    x0, y0, x1, y1 = region_px(UV_PLATE, W, H)
    img[y0:y1, x0:x1] = [0.72, 0.58, 0.34]
    hs.rect_outline(img, x0 + 8, y0 + 8, x1 - 8, y1 - 8, 4, ink)
    hs.draw_text(img, "NSP PORT", x0 + 34, y0 + 30, 6, ink)
    hs.draw_text(img, "LA-7 LOADER", x0 + 34, y0 + 100, 5, ink)
    hs.draw_text(img, "SN 0417", x0 + 34, y0 + 170, 5, ink)
    for cx in (x0 + 22, x1 - 22):
        for cy in (y0 + 22, y1 - 22):
            hs.disc(img, cx, cy, 7, ink)

    # 盖板模板喷字：透明区域用漆色，喷字用奶油色（该区域贴在盖板上，底色与搪瓷漆接近）
    x0, y0, x1, y1 = region_px(UV_STENCIL, W, H)
    img[y0:y1, x0:x1] = [0.12, 0.14, 0.13]
    hs.rect_outline(img, x0 + 8, y0 + 8, x1 - 8, y1 - 8, 4, cream)
    text_centered(img, "SERVICE BAY", (x0, y0 + 24, x1, y0 + 130), 7, cream)
    text_centered(img, "LA-7", (x0, y0 + 120, x1, y1 - 24), 9, cream)
    for cx in (x0 + 24, x1 - 24):
        for cy in (y0 + 24, y1 - 24):
            hs.disc(img, cx, cy, 6, np.array([0.7, 0.56, 0.32]))

    # 检测仪屏幕（三种读数）：暗绿底、亮绿字
    screens = [(UV_SCREEN_IDLE, ["READY", "0.00"]), (UV_SCREEN_BYPASS, ["LIM BYPASS", "MAX"]), (UV_SCREEN_LOG, ["LOG DUMP", "312 REC"])]
    for region, lines in screens:
        x0, y0, x1, y1 = region_px(region, W, H)
        img[y0:y1, x0:x1] = [0.05, 0.12, 0.07]
        col = np.array([0.55, 1.0, 0.45]) if region is not UV_SCREEN_BYPASS else np.array([1.0, 0.35, 0.25])
        text_centered(img, lines[0], (x0, y0 + 30, x1, y0 + 128), 5, col)
        text_centered(img, lines[1], (x0, y0 + 128, x1, y1 - 30), 7, col)

    # 新零件标签（绿）与旁路标签（黄黑）
    x0, y0, x1, y1 = region_px(UV_NEWTAG, W, H)
    img[y0:y1, x0:x1] = [0.25, 0.62, 0.3]
    text_centered(img, "NEW", (x0, y0, x1, y1), 12, cream)
    x0, y0, x1, y1 = region_px(UV_BYPASSTAG, W, H)
    img[y0:y1, x0:x1] = [0.86, 0.64, 0.12]
    for k in range(-256, 256, 48):                      # 斜条纹
        for t in range(0, 20):
            for yy in range(y0, y1):
                xx = x0 + (yy - y0) + k + t
                if x0 <= xx < x1:
                    img[yy, xx] = ink
    text_centered(img, "BYPASS", (x0, y0 + 70, x1, y1 - 70), 6, ink)
    return img


def make_board_texture():
    W, H = 640, 416
    rng = np.random.default_rng(5)
    img = np.zeros((H, W, 3))
    img[:] = [0.10, 0.30, 0.18]
    img += rng.normal(0, 0.01, img.shape)
    trace = np.array([0.72, 0.62, 0.28])
    prng = random.Random(9)
    for _ in range(60):                                 # 走线：水平 / 垂直折线
        x, y = prng.randrange(20, W - 20), prng.randrange(20, H - 20)
        for _ in range(3):
            if prng.random() < 0.5:
                x2 = min(W - 20, max(20, x + prng.randrange(-160, 160)))
                hs.rect(img, min(x, x2), y - 2, max(x, x2) + 1, y + 2, trace)
                x = x2
            else:
                y2 = min(H - 20, max(20, y + prng.randrange(-120, 120)))
                hs.rect(img, x - 2, min(y, y2), x + 2, max(y, y2) + 1, trace)
                y = y2
        hs.disc(img, x, y, 6, trace)
    silk = np.array([0.92, 0.92, 0.86])
    hs.draw_text(img, "CTRL-B2 REV3", 24, 20, 4, silk)
    hs.draw_text(img, "FW", 400, 210, 4, silk)
    hs.draw_text(img, "LOG", 90, 300, 4, silk)
    hs.draw_text(img, "J1", 560, 360, 4, silk)
    hs.rect_outline(img, 370, 150, 520, 290, 3, silk)
    hs.rect_outline(img, 70, 270, 190, 350, 3, silk)
    return np.clip(img, 0, 1)


# ---------------------------------------------------------------------------
# 前臂与机身
# ---------------------------------------------------------------------------

def build_body():
    # 前臂外壳：超椭圆放样（前表面平，便于盖板贴合），插座端略粗、腕端收窄
    sections = [
        (-0.165, 0.029, 0.034, 6, 0, 0), (-0.158, 0.030, 0.036, 6, 0, 0), (-0.150, 0.030, 0.036, 6, 0, 0),
        (-0.060, 0.030, 0.0355, 6, 0, 0), (0.020, 0.030, 0.034, 6, 0, 0), (0.034, 0.027, 0.031, 5, 0, 0),
        (0.042, 0.024, 0.028, 4, 0, 0),
    ]
    shell = obj("Hand_ForearmShell", bm_loft_x(sections, 40), "Body", "M_Hand_Enamel", sharp=30)
    # 维修舱：布尔挖出前表面的凹槽
    cutter = bpy.data.objects.new("_BayCutter", bpy.data.meshes.new("_BayCutter"))
    hs.bm_box((BAY_X1 - BAY_X0, 0.06, BAY_Z * 2), ((BAY_X0 + BAY_X1) / 2, BAY_BACK - 0.03, 0)).to_mesh(cutter.data)
    C["_Cutters"].objects.link(cutter)
    cutter.hide_render = True
    cutter.hide_viewport = True
    b = shell.modifiers.new("Bay", "BOOLEAN")
    b.operation = "DIFFERENCE"
    b.object = cutter
    b.solver = "EXACT"
    hs.add_bevel(shell, 0.0012, 2, angle=35)

    # 舱底衬板 + 加强筋 + 螺丝座
    back = rounded_box((BAY_X1 - BAY_X0 - 0.003, 0.0012, BAY_Z * 2 - 0.003), ((BAY_X0 + BAY_X1) / 2, BAY_BACK - 0.0006, 0), (0, 1, 0), 0.002)
    obj("Hand_BayLiner", back, "Body", "M_Hand_Dark")
    bm = bmesh.new()
    for x in (-0.078, -0.03):
        merge(bm, hs.bm_box((0.003, 0.004, BAY_Z * 2 - 0.004), (x, BAY_BACK - 0.003, 0)))
    obj("Hand_BayRibs", bm, "Body", "M_Hand_Dark", bevel=0.0005)
    bm = bmesh.new()
    for (x, z) in (SCREW_A, SCREW_B):
        merge(bm, cyl(0.0042, FRONT_Y - BAY_BACK, 16, (0, 1, 0), (x, (FRONT_Y + BAY_BACK) / 2, z)))
        merge(bm, cyl(0.0052, 0.002, 16, (0, 1, 0), (x, BAY_BACK - 0.001, z)))
    obj("Hand_ScrewBosses", bm, "Body", "M_Hand_Brass", sharp=40)

    # 舱口四周的钢制护边（盖板压在上面）
    bm = bmesh.new()
    t = 0.0022
    for (sx, sz, cx, cz) in (((BAY_X1 - BAY_X0) + 2 * t, t, (BAY_X0 + BAY_X1) / 2, BAY_Z + t / 2),
                             ((BAY_X1 - BAY_X0) + 2 * t, t, (BAY_X0 + BAY_X1) / 2, -BAY_Z - t / 2),
                             (t, BAY_Z * 2, BAY_X0 - t / 2, 0), (t, BAY_Z * 2, BAY_X1 + t / 2, 0)):
        merge(bm, hs.bm_box((sx, 0.0012, sz), (cx, FRONT_Y + 0.0004, cz)))
    obj("Hand_BayLip", bm, "Body", "M_Hand_Steel")

    # 插座护套：带环纹的橡胶套 + 钢箍 + 卡扣
    secs = []
    for i in range(13):
        x = -0.212 + i * 0.0042
        r = 0.041 if i % 2 == 0 else 0.0435
        secs.append((x, r, r, 2, 0, 0))
    secs.append((-0.160, 0.036, 0.038, 2.4, 0, 0))
    cuff = obj("Hand_SocketCuff", bm_loft_x(secs, 36), "Body", "M_Hand_Rubber", sharp=60)
    inner = bm_loft_x([(-0.2125, 0.034, 0.034, 2, 0, 0), (-0.205, 0.034, 0.034, 2, 0, 0)], 32)
    obj("Hand_SocketLiner", inner, "Body", "M_Hand_Dark")
    band = bm_loft_x([(-0.174, 0.0448, 0.0448, 2, 0, 0), (-0.1665, 0.0448, 0.0448, 2, 0, 0)], 36)
    obj("Hand_ClampBand", band, "Body", "M_Hand_Steel", bevel=0.0006)
    bm = rounded_box((0.012, 0.006, 0.014), (-0.170, -0.047, 0.0), (0, 1, 0), 0.002)
    merge(bm, cyl(0.0022, 0.016, 10, (0, 0, 1), (-0.170, -0.0505, 0.0)))
    obj("Hand_ClampBuckle", bm, "Body", "M_Hand_Steel", sharp=40)

    # 顶面铭牌（黄铜，贴图文字）
    bm = hs.bm_box((0.048, 0.022, 0.0012), (-0.100, -0.004, 0.0362))
    decal_uv(bm, UV_PLATE, (1, 0, 0), (0, 1, 0), (-0.100, -0.004, 0.0362), 0.048, 0.022)
    obj("Hand_RatingPlate", bm, "Body", "M_Hand_Decal", uv=False)

    # 背侧（+Y）布包动力线：从插座护套拱起，沿前臂背面进入腕部
    pts = [(-0.186, 0.02, 0.034), (-0.15, 0.03, 0.041), (-0.08, 0.034, 0.041), (-0.01, 0.033, 0.039), (0.035, 0.028, 0.034), (0.058, 0.022, 0.028)]
    obj("Hand_PowerCable", hs.bm_tube(pts, [0.0045] * len(pts), segs=12), "Body", "M_Hand_Cable", sharp=60)
    bm = bmesh.new()
    for x in (-0.12, -0.05, 0.01):
        merge(bm, rounded_box((0.006, 0.012, 0.012), (x, 0.033, 0.040), (1, 0, 0), 0.002))
    obj("Hand_CableClips", bm, "Body", "M_Hand_Steel")

    # 前侧下沿两根外露腱绳导管：从前臂穿过手腕进入掌部
    # 贴着外壳走：从前臂前表面下沿出来，越过波纹护套和腕部轮毂下方，钻进掌部
    for i, z in enumerate((-0.021, -0.0155)):
        pts = [(0.025, -0.0318, z), (0.036, -0.0296, z), (0.05, -0.0264, z - 0.0005), (0.072, -0.0258, z - 0.0005), (0.088, -0.0165, z)]
        obj(f"Hand_TendonConduit{i + 1}", hs.bm_tube(pts, [0.0021] * len(pts), segs=10), "Body", "M_Hand_Steel", sharp=60)
    bm = bmesh.new()
    for x in (0.029, 0.074):
        merge(bm, rounded_box((0.005, 0.004, 0.013), (x, -0.028, -0.018), (1, 0, 0), 0.0012))
    obj("Hand_TendonConduitClamps", bm, "Body", "M_Hand_Brass")

    build_wrist()
    build_palm_and_fingers()


def build_wrist():
    # 波纹护套（橡胶）连接前臂与腕部
    secs = []
    for i in range(9):
        x = 0.040 + i * 0.0024
        r = 1.0 if i % 2 == 0 else 1.1
        secs.append((x, 0.021 * r, 0.025 * r, 3, 0, 0))
    obj("Hand_WristBellows", bm_loft_x(secs, 32), "Body", "M_Hand_Rubber", sharp=60)
    # 腕部轮毂
    hub = bm_loft_x([(0.058, 0.022, 0.029, 4, 0, 0), (0.060, 0.024, 0.030, 4, 0, 0), (0.078, 0.024, 0.030, 4, 0, 0), (0.081, 0.021, 0.028, 4, 0, 0)], 32)
    obj("Hand_WristHub", hub, "Body", "M_Hand_Enamel", bevel=0.0008)
    # 前后两侧的铰链耳 + 大螺栓（前侧面向镜头）
    bm = bmesh.new()
    for sy in (-1, 1):
        merge(bm, cyl(0.013, 0.004, 24, (0, 1, 0), (WRIST_X, sy * 0.026, 0.0)))
    obj("Hand_WristHingeEars", bm, "Body", "M_Hand_Steel", bevel=0.0006)
    bm = bmesh.new()
    for sy in (-1, 1):
        head, _, _ = hs.bm_prism(hs.circle_profile(0.0055, 6), 0, 0.003)
        xf(head, (WRIST_X, sy * 0.028, 0.0), align_z((0, sy, 0)))
        merge(bm, head)
    obj("Hand_WristHingeBolts", bm, "Body", "M_Hand_Brass", bevel=0.0004)


def phalanx(length, height, depth, taper=0.88, ridge=True):
    """一节手指：沿 +X 的硬朗圆角壳（超椭圆指数 6），向指尖略收窄；指背（-Y）一侧有一道加强脊（指尖节不加）。"""
    h0, h1 = height, height * taper
    d0, d1 = depth, depth * taper
    bm = bm_loft_x([(0.0, d0 * 0.42, h0 * 0.42, 4, 0, 0), (length * 0.12, d0 * 0.5, h0 * 0.5, 6, 0, 0),
                    (length * 0.88, d1 * 0.5, h1 * 0.5, 6, 0, 0), (length, d1 * 0.4, h1 * 0.4, 4, 0, 0)], 24)
    if ridge:
        merge(bm, hs.bm_box((length * 0.62, 0.0016, h0 * 0.28), (length * 0.5, -d0 * 0.5 - 0.0004, 0)))
    return bm


def hinge(bm_cheeks, bm_pins, p, width, r=0.0044):
    """关节：两侧的钢制关节片 + 贯穿的细销（沿 Z）。"""
    for sz in (-1, 1):
        merge(bm_cheeks, cyl(r, 0.0016, 16, (0, 0, 1), p + Vector((0, 0, sz * (width / 2 + 0.0004)))))
    merge(bm_pins, cyl(0.0014, width + 0.004, 8, (0, 0, 1), p))


def build_palm_and_fingers():
    # 掌部：圆角块，手背护板（奶油色）+ 指节护板（钢）+ 四颗螺钉
    palm = bm_loft_x([(0.078, 0.014, 0.031, 3.5, 0.002, 0), (0.083, 0.016, 0.036, 4, 0.002, 0),
                      (0.146, 0.016, 0.036, 4, 0.002, 0), (0.153, 0.013, 0.033, 3.5, 0.002, 0)], 32)
    obj("Hand_Palm", palm, "Body", "M_Hand_Dark", bevel=0.0008)
    back = rounded_box((0.058, 0.004, 0.058), (0.112, -0.0155, 0.0), (0, 1, 0), 0.008, 3)
    obj("Hand_BackPlate", back, "Body", "M_Hand_Cream", bevel=0.0006)
    guard = rounded_box((0.014, 0.004, 0.066), (0.145, -0.0165, 0.0), (0, 1, 0), 0.004, 2)
    obj("Hand_KnuckleGuard", guard, "Body", "M_Hand_Steel", bevel=0.0005)
    bm = bmesh.new()
    for x in (0.09, 0.134):
        for z in (-0.022, 0.022):
            h, _, _ = hs.bm_prism(hs.circle_profile(0.0022, 8), 0, 0.0012)
            merge(bm, xf(h, (x, -0.0175, z), align_z((0, -1, 0))))
    obj("Hand_BackPlateScrews", bm, "Body", "M_Hand_Brass")

    # 四根三节手指：半攥紧，向掌心（+Y）弯；关节处有钢销和侧盖
    finger_z = (0.025, 0.0085, -0.0085, -0.025)
    lengths = (0.029, 0.023, 0.019)
    curl = (14.0, 46.0, 82.0)
    scale = (1.0, 1.04, 1.0, 0.9)
    seg_bm = {m: bmesh.new() for m in ("M_Hand_Enamel", "M_Hand_Cream", "M_Hand_Rubber")}
    cheeks = bmesh.new()
    pins = bmesh.new()
    width = 0.0128
    for fi, z in enumerate(finger_z):
        p = Vector((0.153, 0.001, z))
        for si in range(3):
            a = math.radians(curl[si])
            d = Vector((math.cos(a), math.sin(a), 0))
            L = lengths[si] * scale[fi]
            seg = phalanx(L - 0.0036, width - si * 0.0006, 0.016 - si * 0.0012, ridge=si < 2)
            xf(seg, p + d * 0.0018, Matrix.Rotation(a, 4, "Z"))
            mat = "M_Hand_Rubber" if si == 2 else ("M_Hand_Enamel" if si == 0 else "M_Hand_Cream")
            merge(seg_bm[mat], seg)
            hinge(cheeks, pins, p, width - si * 0.0006)
            p = p + d * L
    obj("Hand_FingerBase", seg_bm["M_Hand_Enamel"], "Body", "M_Hand_Enamel", sharp=40)
    obj("Hand_FingerMid", seg_bm["M_Hand_Cream"], "Body", "M_Hand_Cream", sharp=40)
    obj("Hand_FingerTips", seg_bm["M_Hand_Rubber"], "Body", "M_Hand_Rubber", sharp=40)
    obj("Hand_FingerHinges", cheeks, "Body", "M_Hand_Steel", sharp=40)
    obj("Hand_FingerPins", pins, "Body", "M_Hand_Brass", sharp=40)

    # 掌心（+Y）橡胶握垫，带四道防滑槽
    pad = rounded_box((0.06, 0.004, 0.062), (0.114, 0.0185, 0.0), (0, 1, 0), 0.008, 3)
    obj("Hand_PalmPad", pad, "Body", "M_Hand_Rubber", bevel=0.0006)
    bm = bmesh.new()
    for i in range(4):
        merge(bm, hs.bm_box((0.052, 0.0008, 0.0022), (0.114, 0.0207, -0.018 + i * 0.012)))
    obj("Hand_PalmPadGrooves", bm, "Body", "M_Hand_Dark")

    # 拇指：两节，从掌部顶面（+Z）伸出，朝前并向掌心收
    base = Vector((0.098, 0.006, 0.036))
    dirs = [Vector((0.55, 0.25, 0.8)).normalized(), Vector((0.8, 0.45, 0.35)).normalized()]
    bm_t = bmesh.new()
    bm_tip = bmesh.new()
    bm_tj = bmesh.new()
    p = base
    for si, d in enumerate(dirs):
        L = 0.026 if si == 0 else 0.021
        seg = phalanx(L - 0.003, 0.0145, 0.0155)
        rot = Vector((1, 0, 0)).rotation_difference(d).to_matrix().to_4x4()
        xf(seg, p + d * 0.0015, rot)
        merge(bm_t if si == 0 else bm_tip, seg)
        if si == 1:                                   # 拇指中间关节：侧片 + 销，轴线垂直于两节所在的平面
            n = dirs[0].cross(dirs[1]).normalized()
            for s in (-1, 1):
                merge(bm_tj, cyl(0.0046, 0.0016, 16, n, p + n * s * 0.0078))
        p = p + d * L
    obj("Hand_Thumb", bm_t, "Body", "M_Hand_Enamel", sharp=40)
    obj("Hand_ThumbTip", bm_tip, "Body", "M_Hand_Rubber", sharp=40)
    obj("Hand_ThumbHinge", bm_tj, "Body", "M_Hand_Steel", sharp=40)
    bm = bmesh.new()
    merge(bm, cyl(0.0068, 0.012, 16, (1, 0, 0), base + Vector((0.0, 0.0, -0.001))))
    obj("Hand_ThumbJoint", bm, "Body", "M_Hand_Steel", sharp=40)


# ---------------------------------------------------------------------------
# 可检查 / 可拆部件
# ---------------------------------------------------------------------------

def build_shell():
    cx = (COVER_X0 + COVER_X1) / 2
    w = COVER_X1 - COVER_X0
    y = FRONT_Y - 0.0005 - COVER_T / 2
    bm = rounded_box((w, COVER_T, COVER_Z * 2), (cx, y, 0.0), (0, 1, 0), 0.005, 3)
    # 两个螺丝孔的沉头座
    for (x, z) in (SCREW_A, SCREW_B):
        ring = cyl(SCREW_R + 0.0012, 0.0008, 18, (0, 1, 0), (x, COVER_FRONT - 0.0001, z))
        merge(bm, ring)
    cover = obj("Hand_Cover", bm, "Shell", "M_Hand_Enamel", sharp=30)
    hs.add_bevel(cover, 0.0008, 2, angle=35)
    # 过度磨损：上下边缘漆面磨穿露出钢 + 几道刮擦
    # 边缘磨穿露出钢：上下沿长条 + 靠近手腕一端（最常被碰）的角上几块不规则磨斑
    bm = bmesh.new()
    for sz in (1, -1):
        merge(bm, hs.bm_box((w - 0.02, 0.0004, 0.0018), (cx, COVER_FRONT - 0.0002, sz * (COVER_Z - 0.0019))))
    for i in range(5):
        s = hs.bm_box((RNG.uniform(0.004, 0.009), 0.0003, RNG.uniform(0.0016, 0.003)), (0, 0, 0))
        sz = 1 if i % 2 == 0 else -1
        xf(s, (COVER_X1 - RNG.uniform(0.006, 0.03), COVER_FRONT - 0.0002, sz * (COVER_Z - RNG.uniform(0.003, 0.005))), ((0, 1, 0), RNG.uniform(-20, 20)))
        merge(bm, s)
    obj("Hand_CoverWear", bm, "Shell", "M_Hand_Steel", sharp=60)
    # 维修舱标牌：深色漆底奶油色字，四角铆钉
    bm = hs.bm_box((0.046, 0.0004, 0.0115), (-0.040, COVER_FRONT - 0.0003, 0.012))
    decal_uv(bm, UV_STENCIL, (1, 0, 0), (0, 0, 1), (-0.040, 0, 0.012), 0.046, 0.0115)
    obj("Hand_CoverLabel", bm, "Shell", "M_Hand_Decal", uv=False)
    # 两侧撬口（盖板边缘的缺口标记，撬片插入的位置）
    bm = bmesh.new()
    for x in (COVER_X1 - 0.004,):
        merge(bm, hs.bm_box((0.006, 0.0006, 0.004), (x, COVER_FRONT - 0.0002, 0.0)))
    obj("Hand_CoverPryNotch", bm, "Shell", "M_Hand_Dark")


def build_seal():
    x, z, w, h = SEAL["x"], SEAL["z"], SEAL["w"], SEAL["h"]
    y = COVER_FRONT - 0.0012                         # 压在螺丝头上方
    bm = hs.bm_box((w, 0.0006, h), (x, y, z))
    decal_uv(bm, UV_SEAL, (1, 0, 0), (0, 0, 1), (x, y, z), w, h)
    obj("Hand_Seal", bm, "SealIntact", "M_Hand_Decal", uv=False, smooth=False)
    # 撕开：左半边留在原处（只显示印字左侧），右半边翘起卷曲
    lw = SEAL_TORN_W
    lx = x - w / 2 + lw / 2
    bm = hs.bm_box((lw, 0.0006, h), (lx, y, z))
    u0, v0, us, vs = UV_SEAL
    decal_uv(bm, (u0, v0, us * lw / w, vs), (1, 0, 0), (0, 0, 1), (lx, y, z), lw, h)
    obj("Hand_SealTornLeft", bm, "SealTorn", "M_Hand_Decal", uv=False, smooth=False)
    rw = w - lw - 0.002
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=6, y_segments=1, size=0.5)
    xf(bm, (0, 0, 0), ((1, 0, 0), 90), (rw, h, 1))
    # 先在平整状态下取 UV（只取封条印字右侧对应的部分），再卷曲、倾斜——否则 UV 会跑出封条区域
    decal_uv(bm, (u0 + us * (lw + 0.002) / w, v0, us * rw / w, vs), (1, 0, 0), (0, 0, 1), (0, 0, 0), rw, h)
    for v in bm.verts:                                # 从撕口处向外翘起并卷曲
        t = (v.co.x + rw / 2) / rw
        v.co.y -= 0.004 * t * t + 0.001 * t
    xf(bm, (x - w / 2 + lw + 0.002 + rw / 2, y - 0.0004, z - 0.0015), ((0, 1, 0), -12))
    o = obj("Hand_SealTornFlap", bm, "SealTorn", "M_Hand_Decal", uv=False, smooth=True)
    m = o.modifiers.new("Solidify", "SOLIDIFY")
    m.thickness = 0.0005


def build_fastener(group, x, z):
    # 内六角圆头螺钉：头部略鼓，六角孔，螺杆有螺纹环
    head_front = COVER_FRONT - 0.0009
    bm = cyl(SCREW_R, 0.0012, 20, (0, 1, 0), (x, COVER_FRONT - 0.0003, z), r2=SCREW_R * 0.92)
    dome = sphere(SCREW_R * 0.92, (x, head_front + 0.0006, z), (1, 0.18, 1), 20, 8)
    merge(bm, dome)
    head = obj(f"Hand_{group}_Head", bm, group, "M_Hand_Steel", sharp=40)
    hs.add_bevel(head, 0.0002, 1, angle=40)
    hexr = 0.00175                                    # 对边约 3 mm 的六角孔
    hx, _, _ = hs.bm_prism(hs.circle_profile(hexr, 6), 0.0, 0.0012)
    xf(hx, (x, head_front - 0.0003, z), align_z((0, 1, 0)))
    obj(f"Hand_{group}_Socket", hx, group, "M_Hand_Dark", sharp=20)
    shank = cyl(0.0019, 0.016, 12, (0, 1, 0), (x, COVER_FRONT + 0.008, z))
    for i in range(7):
        merge(shank, cyl(0.0022, 0.0007, 12, (0, 1, 0), (x, COVER_FRONT + 0.003 + i * 0.0018, z)))
    obj(f"Hand_{group}_Shank", shank, group, "M_Hand_Steel", sharp=50)


def gear(r_out, r_in, teeth, thick, center, worn=False):
    prof = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4)
        phase = i % 4
        r = r_out if phase in (1, 2) else r_in
        if worn and phase in (1, 2):
            r = r_in + (r_out - r_in) * RNG.uniform(0.35, 0.85)        # 齿面磨短、磨圆
            if RNG.random() < 0.08:
                r = r_in                                                  # 崩齿
        prof.append((math.cos(a) * r, math.sin(a) * r))
    bm, _, _ = hs.bm_prism(prof, -thick / 2, thick)
    merge(bm, cyl(r_in * 0.45, thick * 1.8, 16, (0, 0, 1), (0, 0, 0)))
    return xf(bm, center, align_z((0, -1, 0)))


def build_drive():
    y = BAY_BACK - 0.0075
    big = (-0.062, y, 0.006)
    small = (-0.0425, y, 0.0085 + 0.0005)
    for variant, worn, mat in (("DriveWorn", True, "M_Hand_WornSteel"), ("DriveNew", False, "M_Hand_Steel")):
        bm = gear(0.012, 0.0102, 18, 0.003, big, worn)
        merge(bm, gear(0.0078, 0.0062, 12, 0.003, small, worn))
        obj(f"Hand_{variant}_Gears", bm, variant, mat, sharp=30)
    tag = hs.bm_box((0.008, 0.0004, 0.006), (-0.036, y - 0.0028, -0.002))
    decal_uv(tag, UV_NEWTAG, (1, 0, 0), (0, 0, 1), (-0.036, 0, -0.002), 0.008, 0.006)
    obj("Hand_DriveNew_Tag", tag, "DriveNew", "M_Hand_Decal", uv=False)

    # 静态部分：电机、轴承座、腱绳滑轮与两根腱绳（通往手腕）
    bm = cyl(0.0068, 0.024, 20, (1, 0, 0), (-0.064, BAY_BACK - 0.0075, -0.012))
    merge(bm, cyl(0.0072, 0.003, 20, (1, 0, 0), (-0.0515, BAY_BACK - 0.0075, -0.012)))
    obj("Hand_DriveMotor", bm, "DriveStatic", "M_Hand_Dark", bevel=0.0004)
    bm = bmesh.new()
    for c in ((-0.062, BAY_BACK - 0.0035, 0.006), (-0.0425, BAY_BACK - 0.0035, 0.009)):
        merge(bm, cyl(0.004, 0.004, 16, (0, 1, 0), c))
    merge(bm, cyl(0.0055, 0.006, 20, (0, 1, 0), (-0.024, BAY_BACK - 0.005, 0.0125)))
    obj("Hand_DriveBearings", bm, "DriveStatic", "M_Hand_Brass", sharp=40)
    bm = bmesh.new()
    for zz in (0.0165, 0.0085):
        pts = [(-0.024, BAY_BACK - 0.0075, zz), (-0.005, BAY_BACK - 0.0075, zz), (0.017, BAY_BACK - 0.0075, zz), (0.03, BAY_BACK - 0.006, zz)]
        merge(bm, hs.bm_tube(pts, [0.0011] * len(pts), segs=8))
    obj("Hand_DriveTendons", bm, "DriveStatic", "M_Hand_Steel", sharp=60)

    # 检测夹（检测传动机构后出现）
    clip = rounded_box((0.008, 0.005, 0.004), (-0.062, BAY_BACK - 0.012, 0.0205), (1, 0, 0), 0.001)
    obj("Hand_DriveTestClip", clip, "DriveTestClip", "M_Hand_Accent")


def build_limiter():
    cx, cz = 0.002, -0.001
    y0 = BAY_BACK
    body = rounded_box((0.022, 0.009, 0.016), (cx, y0 - 0.0045, cz), (0, 1, 0), 0.002)
    obj("Hand_LimiterBody", body, "Limiter", "M_Hand_Dark", bevel=0.0004)
    # 黄铜测力块：从壳体顶部伸到腱绳下方，腱绳压在上面
    cell = hs.bm_box((0.006, 0.004, 0.009), (cx - 0.004, y0 - 0.0075, cz + 0.0115))
    merge(cell, cyl(0.0022, 0.005, 12, (0, 1, 0), (cx - 0.004, y0 - 0.0075, 0.0158)))
    obj("Hand_LimiterLoadCell", cell, "Limiter", "M_Hand_Brass", bevel=0.0003)
    # 指示灯灯座与灯罩（未检测：暗红玻璃）
    bez = cyl(0.0032, 0.0012, 16, (0, 1, 0), (cx + 0.006, y0 - 0.0094, cz + 0.003))
    obj("Hand_LimiterLedBezel", bez, "Limiter", "M_Hand_Steel")
    lens = sphere(0.0023, (cx + 0.006, y0 - 0.0098, cz + 0.003), (1, 0.6, 1), 14, 8)
    obj("Hand_LimiterLens", lens, "Limiter", "M_Hand_LensOff")
    # 测试点：检测仪探针接触的位置，在红灯和旁路标签之间，探针不会挡住两者
    pad = cyl(0.0014, 0.0005, 14, (0, 1, 0), LIMITER_PAD)
    obj("Hand_LimiterTestPad", pad, "Limiter", "M_Hand_Brass")
    # 两根信号线接到控制板
    pts = [(cx - 0.011, y0 - 0.004, cz - 0.004), (-0.03, y0 - 0.006, -0.008), (-0.06, y0 - 0.005, -0.019), (-0.084, y0 - 0.004, -0.014)]
    obj("Hand_LimiterWire1", hs.bm_tube(pts, [0.0008] * len(pts), segs=6), "DriveStatic", "M_Tool_Red", sharp=70)
    pts = [(cx - 0.011, y0 - 0.004, cz - 0.0065), (-0.03, y0 - 0.005, -0.011), (-0.06, y0 - 0.004, -0.0215), (-0.084, y0 - 0.004, -0.0165)]
    obj("Hand_LimiterWire2", hs.bm_tube(pts, [0.0008] * len(pts), segs=6), "DriveStatic", "M_Hand_Dark", sharp=70)
    # 检测后：红灯亮 + 旁路标签
    red = sphere(0.0025, (cx + 0.006, y0 - 0.0099, cz + 0.003), (1, 0.62, 1), 14, 8)
    obj("Hand_LimiterLedRed", red, "LimiterLedRed", "M_Hand_LedRed")
    tag = hs.bm_box((0.011, 0.0004, 0.004), (cx - 0.004, y0 - 0.0093, cz - 0.0045))
    decal_uv(tag, UV_BYPASSTAG, (1, 0, 0), (0, 0, 1), (cx - 0.004, 0, cz - 0.0045), 0.011, 0.004)
    obj("Hand_LimiterBypassTag", tag, "LimiterLedRed", "M_Hand_Decal", uv=False)


def build_board():
    cx, cz, w, h = -0.108, -0.0055, 0.048, 0.031
    y = BAY_BACK - 0.0022
    bm = hs.bm_box((w, 0.0016, h), (cx, y, cz))
    decal_uv(bm, (0, 0, 1, 1), (1, 0, 0), (0, 0, 1), (cx, y, cz), w, h)
    obj("Hand_BoardPcb", bm, "Board", "M_Hand_Board", uv=False)
    bm = bmesh.new()
    merge(bm, hs.bm_box((0.013, 0.0022, 0.013), (cx + 0.0105, y - 0.0019, cz + 0.001)))     # MCU
    merge(bm, hs.bm_box((0.009, 0.0018, 0.006), (cx - 0.0135, y - 0.0017, cz - 0.0085)))   # 日志芯片
    merge(bm, hs.bm_box((0.004, 0.0014, 0.003), (cx - 0.004, y - 0.0015, cz + 0.008)))
    obj("Hand_BoardChips", bm, "Board", "M_Hand_Dark", bevel=0.0002)
    bm = hs.bm_box((0.006, 0.004, 0.012), (cx + 0.0215, y - 0.0028, cz - 0.004))            # 排针座
    obj("Hand_BoardConnector", bm, "Board", "M_Hand_Rubber", bevel=0.0003)
    bm = bmesh.new()
    for i in range(4):
        merge(bm, cyl(0.0022, 0.005, 12, (0, 1, 0), (cx - 0.018 + i * 0.0055, y - 0.003, cz + 0.011)))
    obj("Hand_BoardCaps", bm, "Board", "M_Hand_Brass", sharp=40)
    led = sphere(0.0017, (cx + 0.019, y - 0.0012, cz + 0.012), (1, 0.6, 1), 12, 6)
    obj("Hand_BoardLedOff", led, "Board", "M_Hand_Dark")
    led = sphere(0.0019, (cx + 0.019, y - 0.0013, cz + 0.012), (1, 0.62, 1), 12, 6)
    obj("Hand_BoardLedGreen", led, "BoardLedGreen", "M_Hand_LedGreen")


def build_data_port():
    x = WRIST_X
    top = PORT_TOP
    bez = rounded_box((0.024, 0.018, 0.004), (x, 0.0, top + 0.002), (0, 0, 1), 0.003)
    obj("Hand_PortBezel", bez, "DataPort", "M_Hand_Brass", bevel=0.0004)
    slot = hs.bm_box((0.013, 0.0048, 0.003), (x, -0.001, top + 0.0033))
    obj("Hand_PortSlot", slot, "DataPort", "M_Hand_Rubber")
    bm = bmesh.new()
    for i in range(6):
        merge(bm, hs.bm_box((0.0008, 0.0016, 0.0012), (x - 0.005 + i * 0.002, -0.001, top + 0.0042)))
    obj("Hand_PortPins", bm, "DataPort", "M_Hand_Brass")
    # 防尘盖：铰在背侧（+Y），打开竖起
    flap = rounded_box((0.02, 0.0015, 0.012), (0, 0, 0.006), (0, 1, 0), 0.002)
    xf(flap, (x, 0.0098, top + 0.004), ((1, 0, 0), -18))
    merge(flap, cyl(0.0012, 0.02, 10, (1, 0, 0), (x, 0.009, top + 0.004)))
    obj("Hand_PortDustFlap", flap, "DataPort", "M_Hand_Dark", bevel=0.0003)


def build_tray():
    x, y, w, d = TRAY["x"], TRAY["y"], TRAY["w"], TRAY["d"]
    base = rounded_box((w, d, 0.006), (x, y, TRAY_Z), (0, 0, 1), 0.006, 3)
    obj("Hand_TrayBase", base, "Tray", "M_Hand_Rubber", bevel=0.0008)
    bm = bmesh.new()
    t = 0.004
    for (sx, sy, cx, cy) in ((w, t, x, y - d / 2 + t / 2), (w, t, x, y + d / 2 - t / 2), (t, d, x - w / 2 + t / 2, y), (t, d, x + w / 2 - t / 2, y),
                             (t, d * 0.55, -0.13, y - d * 0.2)):
        merge(bm, hs.bm_box((sx, sy, 0.008), (cx, cy, TRAY_Z + 0.006)))
    obj("Hand_TrayLip", bm, "Tray", "M_Hand_Dark", bevel=0.0008)
    stripe = hs.bm_box((w - 0.01, 0.003, 0.0012), (x, y - d / 2 + 0.0015, TRAY_Z + 0.0105))
    obj("Hand_TrayStripe", stripe, "Tray", "M_Hand_Accent")


# ---------------------------------------------------------------------------
# 工具与手套手（原点 = 作用点，轴线沿 +Z）
# ---------------------------------------------------------------------------

def build_screwdriver():
    g = "Tool_Screwdriver"
    bit, _, _ = hs.bm_prism(hs.circle_profile(0.00165, 6), 0.0, 0.009)       # 六角批头：对边约 2.9 mm
    obj("Tool_Screwdriver_Bit", bit, g, "M_Hand_Steel", sharp=20)
    shaft = cyl(0.0026, 0.064, 16, (0, 0, 1), (0, 0, 0.009 + 0.032))
    merge(shaft, cyl(0.0034, 0.004, 16, (0, 0, 1), (0, 0, 0.011), r2=0.0026))
    obj("Tool_Screwdriver_Shaft", shaft, g, "M_Hand_Steel", sharp=40)
    ferrule = cyl(0.0068, 0.009, 20, (0, 0, 1), (0, 0, 0.0775), r2=0.0078)
    obj("Tool_Screwdriver_Ferrule", ferrule, g, "M_Hand_Brass", bevel=0.0004)
    # 手柄：带 8 条凹槽的胶木，中段鼓起
    rings = []
    bm = bmesh.new()
    zs = [0.082, 0.09, 0.105, 0.125, 0.145, 0.158, 0.165]
    rs = [0.0085, 0.0118, 0.0135, 0.0138, 0.0128, 0.0112, 0.006]
    segs = 32
    for z, r in zip(zs, rs):
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            groove = 0.88 if (k % 4 == 0) and 0.088 < z < 0.16 else 1.0
            ring.append(bm.verts.new((math.cos(a) * r * groove, math.sin(a) * r * groove, z)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj("Tool_Screwdriver_Handle", bm, g, "M_Tool_Orange", sharp=60)
    cap = cyl(0.0062, 0.003, 20, (0, 0, 1), (0, 0, 0.1655))
    obj("Tool_Screwdriver_Cap", cap, g, "M_Hand_Dark", bevel=0.0005)


def build_pry():
    g = "Tool_Pry"
    # 扁平撬片：刀口楔形，微微上翘，钢制
    bm = bmesh.new()
    prof = [(0.0, 0.0), (0.004, 0.0009), (0.06, 0.0012), (0.06, -0.0012), (0.004, -0.0009)]
    blade, _, _ = hs.bm_prism([(x, y) for x, y in prof], -0.0045, 0.009)
    xf(blade, (0, 0, 0), ((0, 1, 0), -90))           # 原型沿 X，转到沿 +Z
    merge(bm, blade)
    obj("Tool_Pry_Blade", bm, g, "M_Hand_Steel", sharp=25)
    handle = bm_loft_x([(0.0, 0.0045, 0.009, 3, 0, 0), (0.01, 0.006, 0.012, 3, 0, 0), (0.05, 0.0065, 0.0125, 3, 0, 0),
                        (0.07, 0.006, 0.011, 3, 0, 0), (0.078, 0.0045, 0.008, 2.5, 0, 0)], 24)
    xf(handle, (0, 0, 0.058), ((0, 1, 0), -90))
    obj("Tool_Pry_Handle", handle, g, "M_Tool_Bakelite", sharp=50)
    band = cyl(0.0082, 0.004, 20, (0, 0, 1), (0, 0, 0.061))
    xf(band, (0, 0, 0), None, (1.0, 0.55, 1.0))
    obj("Tool_Pry_Band", band, g, "M_Hand_Brass", bevel=0.0003)


def build_tester():
    # 检测仪主机（放在台面上 / 挂在腰间），带屏幕、旋钮、两个插孔
    g = "Tool_TesterBody"
    body = rounded_box((0.06, 0.026, 0.1), (0, 0, 0.05), (0, 1, 0), 0.008, 3)
    obj("Tool_Tester_Case", body, g, "M_Tool_Case", bevel=0.0008)
    bez = rounded_box((0.046, 0.003, 0.03), (0, -0.0135, 0.072), (0, 1, 0), 0.003)
    obj("Tool_Tester_ScreenBezel", bez, g, "M_Hand_Dark")
    scr = hs.bm_box((0.04, 0.0004, 0.024), (0, -0.0152, 0.072))
    decal_uv(scr, UV_SCREEN_IDLE, (1, 0, 0), (0, 0, 1), (0, -0.0152, 0.072), 0.04, 0.024)
    obj("Tool_Tester_Screen", scr, g, "M_Hand_Decal", uv=False)
    knob = hs.bm_prism(hs.knurl_profile(0.009, 0.0082, 16), 0.0, 0.006)[0]
    xf(knob, (0, -0.013, 0.036), align_z((0, -1, 0)))
    obj("Tool_Tester_Knob", knob, g, "M_Hand_Dark", sharp=30)
    bm = bmesh.new()
    for x in (-0.017, 0.017):
        merge(bm, cyl(0.0035, 0.004, 14, (0, 1, 0), (x, -0.0145, 0.013)))
    obj("Tool_Tester_Jacks", bm, g, "M_Hand_Brass")
    bm = bmesh.new()
    for x in (-0.017, 0.017):
        pts = [(x, -0.016, 0.013), (x, -0.024, 0.008), (x * 1.6, -0.03, -0.004)]
        merge(bm, hs.bm_tube(pts, [0.0018] * 3, segs=8))
    obj("Tool_Tester_Leads", bm, g, "M_Tool_Red", sharp=60)

    # 探针笔：针尖在原点，沿 +Z
    g = "Tool_Probe"
    tip = cyl(0.0006, 0.012, 10, (0, 0, 1), (0, 0, 0.006), r2=0.0011)
    obj("Tool_Probe_Tip", tip, g, "M_Hand_Steel", sharp=30)
    pen = cyl(0.0042, 0.075, 20, (0, 0, 1), (0, 0, 0.012 + 0.0375), r2=0.0048)
    merge(pen, cyl(0.0022, 0.004, 14, (0, 0, 1), (0, 0, 0.011), r2=0.0042))
    obj("Tool_Probe_Pen", pen, g, "M_Tool_Red", bevel=0.0004)
    guard = cyl(0.0068, 0.0025, 20, (0, 0, 1), (0, 0, 0.026))
    obj("Tool_Probe_Guard", guard, g, "M_Hand_Rubber", bevel=0.0004)
    pts = [(0, 0, 0.087), (0, 0.004, 0.1), (0.006, 0.014, 0.112), (0.014, 0.03, 0.12)]
    obj("Tool_Probe_Cable", hs.bm_tube(pts, [0.0017] * 4, segs=8), g, "M_Tool_Red", sharp=60)

    # 数据插头：插片在原点下方（插入方向 -Z），插片尺寸匹配手腕接口
    g = "Tool_Plug"
    blade = hs.bm_box((0.0118, 0.0038, 0.006), (0, 0, 0.003))
    obj("Tool_Plug_Blade", blade, g, "M_Hand_Brass", sharp=30)
    housing = rounded_box((0.018, 0.009, 0.02), (0, 0, 0.016), (0, 0, 1), 0.003)
    obj("Tool_Plug_Housing", housing, g, "M_Hand_Dark", bevel=0.0005)
    grip = bmesh.new()
    for i in range(4):
        merge(grip, hs.bm_box((0.0186, 0.0096, 0.0012), (0, 0, 0.011 + i * 0.0035)))
    obj("Tool_Plug_Grip", grip, g, "M_Hand_Rubber")
    relief = cyl(0.0035, 0.012, 14, (0, 0, 1), (0, 0, 0.032), r2=0.0024)
    obj("Tool_Plug_Relief", relief, g, "M_Hand_Rubber")
    pts = [(0, 0, 0.038), (0, 0, 0.05), (0.004, 0.008, 0.064), (0.012, 0.022, 0.07)]
    obj("Tool_Plug_Cable", hs.bm_tube(pts, [0.0018] * 4, segs=8), g, "M_Hand_Dark", sharp=60)


def build_glove(g, handle_r):
    """
    简化手套手（满握）：握住沿 Z、半径 handle_r 的手柄，原点为握持中心。
    手掌贴在手柄 -X 一侧，四指从 +Y 一侧绕过手柄，食指最靠近工具作用端（-Z），拇指从 -Y 一侧绕过来压在食指上；
    手腕和护腕横向朝 -X（略向 +Z）：动画里让 -X 朝画面上方，手臂从上方伸进来，不会顺着工具轴线挡住镜头。
    """
    fr = (0.0076, 0.0079, 0.0075, 0.0066)               # 食指 → 小指
    zs = (-0.021, -0.0065, 0.0075, 0.0205)
    palm_x = -(handle_r + 0.013)
    # 手掌（含手背）：圆角块
    palm = rounded_box((0.024, 0.048, 0.074), (palm_x - 0.004, 0.004, 0.0), (0, 0, 1), 0.009, 3)
    hs.round_edges(palm, (0, 1, 0), 0.006, 2)
    obj("Glove_Palm", palm, g, "M_Glove", sharp=50)
    # 四指：三节，关节处略粗；最后一节是加厚的深色指尖
    segs_bm = bmesh.new()
    tips = bmesh.new()
    for i, z in enumerate(zs):
        R = handle_r + fr[i] * 0.95
        angles = (150, 98, 48, 6)
        pts = [Vector((math.cos(math.radians(a)) * R, math.sin(math.radians(a)) * R, z)) for a in angles]
        pts[0] += Vector((-0.004, 0.0, 0.0))            # 指根埋进手掌
        for si in range(3):
            r0 = fr[i] * (1.0 - si * 0.07)
            tube = hs.bm_tube([pts[si], pts[si + 1]], [r0, r0 * 0.94], segs=12)
            merge(tips if si == 2 else segs_bm, tube)
            merge(segs_bm, sphere(r0 * 1.02, pts[si], (1, 1, 1), 12, 8))
        merge(tips, sphere(fr[i] * 0.85, pts[3], (1, 1, 1), 12, 8))
    obj("Glove_Fingers", segs_bm, g, "M_Glove", sharp=70)
    obj("Glove_FingerTips", tips, g, "M_Glove_Palm", sharp=70)
    # 拇指：从手掌下部绕过手柄 -Y 一侧，指尖压在食指上
    R = handle_r + 0.0085
    tpts = [Vector((palm_x + 0.002, -0.018, -0.012)), Vector((math.cos(math.radians(235)) * R, math.sin(math.radians(235)) * R, -0.024)),
            Vector((math.cos(math.radians(285)) * R, math.sin(math.radians(285)) * R, -0.029)),
            Vector((math.cos(math.radians(330)) * R, math.sin(math.radians(330)) * R, -0.031))]
    thumb = hs.bm_tube(tpts, [0.0092, 0.0086, 0.0082, 0.0076], segs=12)
    for p in tpts[1:3]:
        merge(thumb, sphere(0.0088, p, (1, 1, 1), 12, 8))
    obj("Glove_Thumb", thumb, g, "M_Glove", sharp=70)
    tip = sphere(0.0072, tpts[3], (1, 1, 1), 12, 8)
    obj("Glove_ThumbTip", tip, g, "M_Glove_Palm", sharp=70)
    # 手腕 + 护腕：横向（-X）并略向后（+Z）——手臂从工具侧上方伸进画面，而不是顺着工具轴线挡在镜头前
    wdir = Vector((-0.93, 0.0, 0.36)).normalized()
    wbase = Vector((palm_x - 0.006, 0.004, 0.018))
    wrist = hs.bm_tube([wbase, wbase + wdir * 0.02], [0.0175, 0.018], segs=20)
    obj("Glove_Wrist", wrist, g, "M_Glove", sharp=70)
    # 长护袖：足够长，镜头拉近时一直延伸到画面外，不会露出袖口端面
    cuff = hs.bm_tube([wbase + wdir * 0.018, wbase + wdir * 0.05, wbase + wdir * 0.16], [0.02, 0.022, 0.0245], segs=20)
    obj("Glove_Cuff", cuff, g, "M_Glove_Palm", sharp=70)
    band = hs.bm_tube([wbase + wdir * 0.03, wbase + wdir * 0.036], [0.0213, 0.0215], segs=20)
    obj("Glove_CuffBand", band, g, "M_Tool_Orange", sharp=70)


# ---------------------------------------------------------------------------
# 统计 / 渲染
# ---------------------------------------------------------------------------

C = {}


def set_group_visible(names, visible):
    for n in names:
        for o in G[n].objects:
            o.hide_render = not visible
            o.hide_viewport = not visible


def place_tools_for_lineup():
    """渲染用：把工具与手套手摆成一排（记录原位置，导出前复原）。"""
    floor = TRAY_Z - 0.003
    # (位置, 绕 Y 的角度)：螺丝刀、撬片、探针平放（轴线沿 +X），检测仪、插头立放，手套手立着
    layout = {
        "Tool_Screwdriver": ((0.30, -0.16, floor + 0.0138), 90), "Tool_Pry": ((0.30, -0.12, floor + 0.0066), 90),
        "Tool_Probe": ((0.30, -0.085, floor + 0.0068), 90), "Tool_TesterBody": ((0.53, -0.06, floor), 0),
        "Tool_Plug": ((0.60, -0.10, floor), 0), "Glove": ((0.66, -0.07, floor + 0.038), 0),
        "GlovePinch": ((0.74, -0.07, floor + 0.038), 0),
    }
    for gname, (loc, ry) in layout.items():
        for o in G[gname].objects:
            o.rotation_mode = "XYZ"
            o.location = Vector(loc)
            # 手套的手臂（局部 -X）转向后方（+Y），不挡住旁边的工具
            o.rotation_euler = (0, math.radians(ry), math.radians(-90) if gname.startswith("Glove") else 0)


def reset_tool_transforms():
    for gname in TOOL_GROUPS:
        for o in G[gname].objects:
            o.rotation_mode = "XYZ"
            o.location = (0, 0, 0)
            o.rotation_euler = (0, 0, 0)
            o.rotation_quaternion = (1, 0, 0, 0)


def pose_glove_with(tool, glove, tip_world, axis_world, grip_z, arm_dir=(0, -0.2, 1)):
    """
    预览：工具作用点放到 tip_world，轴线朝 axis_world（从作用点指向手柄），手套握在手柄 grip_z 处。
    手套绕工具轴转到手腕（局部 -X）尽量朝上方，避免手臂穿进物品（第二阶段在 Unity 里用同样的规则）。
    """
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis_world).normalized())
    for o in G[tool].objects:
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = rot
        o.location = Vector(tip_world)
    grip_center = Vector(tip_world) + rot @ Vector((0, 0, grip_z))
    best = max(range(0, 360, 10),
               key=lambda a: (rot @ Matrix.Rotation(math.radians(a), 3, "Z").to_quaternion() @ Vector((-1, 0, 0))).dot(Vector(arm_dir).normalized()))
    extra = Matrix.Rotation(math.radians(best), 3, "Z").to_quaternion()
    for o in G[glove].objects:
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = rot @ extra
        o.location = grip_center


def setup_gpu(scene):
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for backend in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = backend
                prefs.get_devices()
                devs = [d for d in prefs.devices if d.type == backend]
                if devs:
                    for d in prefs.devices:
                        d.use = d.type == backend
                    scene.cycles.device = "GPU"
                    print(f"[WorkerHand] render on {backend}: {[d.name for d in devs]}")
                    return
            except Exception:
                continue
    except Exception as e:
        print("[WorkerHand] GPU setup failed:", e)
    print("[WorkerHand] render on CPU")


def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(SHOT_DIR, exist_ok=True)
    scene, groups = hs.reset_scene("WorkerHand", GROUPS, "WH_")
    G.update(groups)
    cut = bpy.data.collections.new("_Cutters")
    scene.collection.children.link(cut)
    C["_Cutters"] = cut

    decals = make_decals()
    if LOOK == "v2":
        decals = ld.extend_decals(decals)          # 空余区域加租赁标识；v1 用到的区域不变
    images = {
        GRIME_TEX: hs.save_image(GRIME_TEX, hs.make_grime_texture(1024, seed=41), TEX_DIR),
        DECAL_TEX: hs.save_image(DECAL_TEX, decals, TEX_DIR),
        BOARD_TEX: hs.save_image(BOARD_TEX, make_board_texture(), TEX_DIR),
    }
    if LOOK == "v2":
        MATERIALS.update(ld.material_overrides())
        images.update(ld.build_images(sys.modules[__name__]))
    hs.build_materials(MATERIALS, images)

    build_body()
    build_shell()
    build_seal()
    build_fastener("FastenerA", *SCREW_A)
    build_fastener("FastenerB", *SCREW_B)
    build_drive()
    build_limiter()
    build_board()
    build_data_port()
    build_tray()
    build_screwdriver()
    build_pry()
    build_tester()
    build_glove("Glove", 0.0136)          # 满握：螺丝刀、撬片
    build_glove("GlovePinch", 0.0055)     # 细握：探针、插头
    if LOOK == "v2":
        ld.apply(sys.modules[__name__])

    stats = hs.collect_stats(groups, images, MATERIALS.keys())
    stats["look"] = LOOK
    hand_groups = [g for g in GROUPS if g not in TOOL_GROUPS]
    # 同屏最多：机身 + 盖板 + 完整封条 + 两颗螺丝 + 旧齿轮 + 静态传动 + 限力器 + 控制板 + 接口 + 零件盘（变体不同时出现）
    on_screen = ["Body", "Shell", "SealIntact", "FastenerA", "FastenerB", "DriveStatic", "DriveWorn", "Limiter", "Board", "DataPort", "Tray"]
    stats["hand_max_on_screen_triangles"] = sum(stats["groups"][g]["triangles"] for g in on_screen)
    stats["tools_triangles"] = {g: stats["groups"][g]["triangles"] for g in TOOL_GROUPS}
    stats["snap_targets"] = {
        "Snap_ShellOnTray": [TRAY["x"] + 0.02, TRAY["y"] + 0.005, TRAY_Z + 0.006],
        "Snap_FastenerA_OnTray": [TRAY["x"] - 0.085, TRAY["y"] - 0.03, TRAY_Z + 0.006],
        "Snap_FastenerB_OnTray": [TRAY["x"] - 0.085, TRAY["y"] - 0.01, TRAY_Z + 0.006],
    }
    stats["tool_frames"] = "每件工具的作用点在原点，轴线沿 +Z；手套握持中心在原点，握持轴线沿 +Z，前臂朝 -X"
    with open(os.path.join(HERE, "stats.json" if LOOK == "v2" else f"stats_{LOOK}.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[WorkerHand] stats:", json.dumps(stats, ensure_ascii=False))

    if DO_EXPORT:
        art_dir = os.path.dirname(EXPORT_DIR)
        hs.export_groups(groups, EXPORT_DIR, "WorkerHand")
        # 材质清单只写导出的部件实际用到的材质；贴图也只拷用到的（诊所场景的道具材质不进 Unity）
        used = sorted({m for g in stats["groups"].values() for m in g["materials"]})
        hs.write_material_manifest({k: MATERIALS[k] for k in used}, os.path.join(art_dir, "WorkerHand_Materials.json"))
        tex_out = os.path.join(art_dir, "Textures")
        os.makedirs(tex_out, exist_ok=True)
        for k in used:
            for key in ("base_map", "emission_map"):
                t = MATERIALS[k].get(key)
                if t:
                    shutil.copyfile(os.path.join(TEX_DIR, t), os.path.join(tex_out, t))
        # Unity JsonUtility 不支持字典：写成数组
        a = anchor_data()
        flat = {
            "anchors": [dict(id=k, kind=v["kind"], pos=list(v["pos"]), out=list(v["out"]), arm=list(v["arm"]),
                             lever=list(v.get("lever", (0, 0, 0)))) for k, v in a["anchors"].items()],
            "snaps": [dict(name=k, pos=list(v)) for k, v in a["snaps"].items()],
            "tools": [dict(kind=k, group=v["group"], glove=v["glove"], grip=v["grip"]) for k, v in a["tools"].items()],
        }
        with open(os.path.join(art_dir, "WorkerHand_Anchors.json"), "w", encoding="utf-8") as f:
            json.dump(flat, f, ensure_ascii=False, indent=2)
        print("[WorkerHand] exported", len(groups), "groups,", len(used), "materials ->", art_dir)

    cam = hs.setup_studio(scene, TRAY_Z - 0.003, target=(0, 0, 0))
    setup_gpu(scene)
    scene.cycles.samples = 128
    scene.view_settings.exposure = -0.75          # 共用棚灯偏亮，搪瓷漆会被冲淡成浅薄荷色
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "WorkerHand.blend" if LOOK == "v2" else f"WorkerHand_{LOOK}.blend"))

    if DO_RENDER and LOOKDEV:
        me = sys.modules[__name__]
        ld.lookdev_shots(me, scene, cam, LOOK, LOOKDEV_PREFIX)
        if LOOK == "v2":
            metrics = {}
            ld.clinic_shots(me, scene, cam, LOOKDEV_PREFIX, metrics)
            with open(os.path.join(HERE, "lookdev_metrics.json"), "w", encoding="utf-8") as f:
                json.dump(metrics, f, ensure_ascii=False, indent=2)
            print("[WorkerHand] lookdev metrics:", json.dumps(metrics, ensure_ascii=False))
            ld.finish(me, LOOKDEV_PREFIX)
        print("[WorkerHand] lookdev done")
        return

    if not DO_RENDER:
        print("[WorkerHand] done (no render)")
        return

    variants = ["SealTorn", "DriveNew", "DriveTestClip", "LimiterLedRed", "BoardLedGreen"]

    def shot(num, name, loc, target, lens, show=(), hide=()):
        if ONLY and num not in ONLY.split(","):
            return
        set_group_visible(GROUPS, True)
        set_group_visible(variants, False)
        set_group_visible(TOOL_GROUPS, False)
        set_group_visible(show, True)
        set_group_visible(hide, False)
        hs.render_view(scene, cam, os.path.join(SHOT_DIR, f"{SHOT_PREFIX}_{num}_{name}.png"), loc, target, lens)

    # 手的整体：检查台视角（前上方）、背侧、正上方、侧面
    shot("01", "front34", (-0.18, -0.52, 0.26), (-0.015, 0, -0.01), 50)
    shot("02", "back34", (0.25, 0.5, 0.24), (-0.015, 0, -0.01), 50)
    shot("03", "top", (-0.01, -0.12, 0.55), (-0.015, 0, -0.01), 50)
    shot("04", "fingers_joints", (0.3, -0.22, 0.05), (0.16, 0.01, 0.0), 70)
    # 玩家会检查的部位：封条与螺丝、打开的维修舱、手腕数据接口
    shot("05", "seal_screws", (-0.09, -0.26, 0.08), (-0.066, -0.035, 0.004), 48)
    shot("06", "bay_open", (-0.058, -0.27, 0.1), (-0.058, -0.015, 0.0), 58, hide=["Shell", "SealIntact", "FastenerA", "FastenerB"])
    shot("07", "bay_after_tests", (-0.058, -0.27, 0.1), (-0.058, -0.015, 0.0), 58,
         show=["DriveNew", "LimiterLedRed", "BoardLedGreen", "DriveTestClip"], hide=["Shell", "SealIntact", "FastenerA", "FastenerB", "DriveWorn"])
    shot("08", "seal_torn", (-0.13, -0.2, 0.05), (-0.12, -0.035, 0.016), 90, show=["SealTorn"], hide=["SealIntact", "FastenerA"])
    shot("09", "wrist_port", (0.03, -0.12, 0.16), (WRIST_X, 0.0, 0.03), 80)

    # 工具：一排平放 + 手套手；再预览两个握持姿势（第二阶段动画的起止位置参考）
    place_tools_for_lineup()
    shot("10", "tools_lineup", (0.5, -0.62, 0.26), (0.51, -0.08, -0.05), 42, show=TOOL_GROUPS, hide=hand_groups)
    reset_tool_transforms()
    # 握持预览（第二阶段动画的接触姿势参考）：四个操作各一张
    sx, sz = SCREW_A
    pose_glove_with("Tool_Screwdriver", "Glove", (sx, COVER_FRONT - 0.0009, sz), (0.12, -1.0, 0.3), 0.12)
    shot("11", "grip_screwdriver_fastener_a", (-0.36, -0.36, 0.2), (-0.1, -0.07, 0.03), 50, show=["Tool_Screwdriver", "Glove"])
    reset_tool_transforms()
    pose_glove_with("Tool_Pry", "Glove", (COVER_X1 + 0.0005, COVER_FRONT + 0.0015, 0.0), (0.6, -1.0, 0.25), 0.1, arm_dir=(1, -0.2, 0.8))
    shot("12", "grip_pry_cover_edge", (-0.02, -0.4, 0.16), (0.05, -0.07, 0.01), 45, show=["Tool_Pry", "Glove"])
    reset_tool_transforms()
    pose_glove_with("Tool_Probe", "GlovePinch", (0.008, BAY_BACK - 0.0098, 0.002), (0.25, -1.0, 0.45), 0.052, arm_dir=(1, -0.3, 0.7))
    shot("13", "grip_probe_limiter", (-0.08, -0.34, 0.16), (0.02, -0.05, 0.02), 50, show=["Tool_Probe", "GlovePinch", "LimiterLedRed"],
         hide=["Shell", "SealIntact", "FastenerA", "FastenerB"])
    reset_tool_transforms()
    pose_glove_with("Tool_Plug", "GlovePinch", (WRIST_X, -0.001, PORT_TOP + 0.0022), (0.0, -0.25, 1.0), 0.02)
    shot("14", "grip_plug_data_port", (-0.05, -0.28, 0.26), (0.06, -0.02, 0.05), 55, show=["Tool_Plug", "GlovePinch"])
    reset_tool_transforms()
    print("[WorkerHand] done")


if __name__ == "__main__":
    main()
