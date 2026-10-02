"""
七号第二晚维修 · 左引擎故障美术包（Blender 5.2，脚本完整重建）。由 Claude 辅助编写；程序化建模与程序化贴图，没有外部模型、贴图或字体。

内容（都是新资源，不改 RobotV4 原 FBX）：
  1. BearingWorn  ：左上轴承的磨损件（划痕、油污、金属屑、轻微热变色），可近看辨认，不靠红色高亮。
  2. BearingNew   ：同尺寸、同轴向的新轴承。
  3. IntakeClog   ：左进气口的积尘 / 纤维堵塞，独立对象，可整体移除（清理后隐藏即可）。
  4. CoverLabel   ：左上盖内侧的旧保养标记（贴纸），贴合上盖内表面。

几何对位：脚本把 Assets/RobotV4/Model/robot-final.fbx 只读导入为参考（静止姿态 = Unity 静态姿态），
现场测量轴承中心 / 转轴 / 内外径、进气口开口、上盖内表面，在七号身上的真实位置建模。参考体不导出、不保存修改。
输出：Unit07FaultKit.blend、Export/*.fbx、Textures/*.png、materials.json、stats.json、checks.json、Renders/*.png。
用法：blender -b --factory-startup --python build_unit07_fault_kit.py [-- --norender]
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
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(ART, ".."))
sys.path.insert(0, os.path.join(ART, "Common"))
import br_hardsurface as hs  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NORENDER = "--norender" in ARGS
FBX = os.path.join(ROOT, "Assets", "RobotV4", "Model", "robot-final.fbx")
EXPORT = os.path.join(HERE, "Export")
TEX = os.path.join(HERE, "Textures")
RENDERS = os.path.join(HERE, "Renders")
for d in (EXPORT, TEX, RENDERS):
    os.makedirs(d, exist_ok=True)

PREFIX = "UNIT07_FK"
GROUPS = ["BearingWorn", "BearingNew", "IntakeClog", "CoverLabel"]
MM = 0.001
rng = np.random.default_rng(2207)
prng = random.Random(2207)

# ---------------------------------------------------------------------------
# 0. 场景与只读参考
# ---------------------------------------------------------------------------
scene, groups = hs.reset_scene("UNIT07_FaultKit", GROUPS, "FK_")
ref = bpy.data.collections.new("_RobotV4_Reference_ReadOnly")
scene.collection.children.link(ref)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=FBX)
imported = [o for o in bpy.data.objects if o not in before]
for o in imported:
    for c in list(o.users_collection):
        c.objects.unlink(o)
    ref.objects.link(o)
arm = next(o for o in imported if o.type == "ARMATURE")
# 静态姿态：Unity 里七号的静态姿态是 Idle_Hover 第 0 帧（RobotV4ModelPostprocessor 采样），对应 Blender 动作的第一帧；
# 不能用骨骼的 Rest 姿态（引擎铰链角度略有不同，轴承会偏 1–2 mm）
_idle = next(a for a in bpy.data.actions if a.name.endswith("Idle_Hover"))
arm.data.pose_position = "POSE"
_ad = arm.animation_data or arm.animation_data_create()
_ad.action = _idle
try:
    if _ad.action_slot is None and len(_idle.slots):
        _ad.action_slot = _idle.slots[0]
except AttributeError:
    pass
bpy.context.scene.frame_set(int(_idle.frame_range[0]))
bpy.context.view_layer.update()
R = {o.name: o for o in imported if o.type == "MESH"}
DG = bpy.context.evaluated_depsgraph_get()


def world_verts(o):
    m = o.matrix_world
    return np.array([tuple(m @ v.co) for v in o.data.vertices])


def world_bvh(o):
    m = o.matrix_world
    verts = [m @ v.co for v in o.data.vertices]
    polys = [tuple(p.vertices) for p in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys)


# ---- 轴承：中心、转轴（指向进气口）、内外径、宽度
bw = world_verts(R["Engine_BearingTop_L"])
C = bw.mean(axis=0)
w, v = np.linalg.eigh(np.cov((bw - C).T))
A = v[:, int(np.argmin(w))]
if A[2] < 0:
    A = -A
d = bw - C
h = d @ A
r = np.linalg.norm(d - np.outer(h, A), axis=1)
BEARING = {"od": float(r.max() * 2), "bore": float(r.min() * 2), "width": float(h.max() - h.min())}
CEN, AX = Vector(C), Vector(A).normalized()
REFX = (Vector((1, 0, 0)) - AX * AX.x).normalized()          # 轴承坐标系 X：世界 X 去掉轴向分量
REFY = AX.cross(REFX)
ROT = Matrix((REFX, REFY, AX)).transposed()                  # 列 = 局部 X、Y、Z（Z = 转轴）
FRAME = Matrix.Translation(CEN) @ ROT.to_4x4()                # 轴承坐标系 → 世界


def at_axis(axial, radius=0.0, ang=0.0):
    """轴承坐标系里的点（轴向 axial、半径 radius、角度 ang）→ 世界坐标。"""
    return FRAME @ Vector((math.cos(ang) * radius, math.sin(ang) * radius, axial))


# ---------------------------------------------------------------------------
# 1. 贴图（numpy 程序化绘制）
# ---------------------------------------------------------------------------

def save_png(name, arr, alpha=None, noncolor=False):
    h_, w_ = arr.shape[:2]
    a = alpha if alpha is not None else np.ones((h_, w_))
    rgba = np.concatenate([arr[..., :3], a[..., None]], axis=-1)
    img = bpy.data.images.new(os.path.splitext(name)[0], width=w_, height=h_, alpha=alpha is not None)
    img.pixels.foreach_set(np.flipud(rgba).astype(np.float32).ravel())
    path = os.path.join(TEX, name)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    if noncolor:
        img.colorspace_settings.name = "Non-Color"
    return img


def normal_from_height(hmap, strength):
    gy, gx = np.gradient(hmap)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(hmap)], axis=-1)   # 图像第 0 行在上：v 方向取反
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def lin(hexc):
    return np.array(hs.hex_rgb(hexc))


S = 1024
yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)
U = (xx + 0.5) / S
V = 1.0 - (yy + 0.5) / S          # V 向上，与 UV 一致

# 轴承 UV 布局：上端面 = 左上方块（圆心 0.25, 0.75），下端面 = 右上方块（0.75, 0.75），外圆柱 = v 0.27–0.47，内孔 = v 0.04–0.24
DISK_R = 0.235
R_OUT, R_BORE, HALF_W = BEARING["od"] / 2, BEARING["bore"] / 2, BEARING["width"] / 2


def polar(cu, cv):
    du, dv = U - cu, V - cv
    rr = np.sqrt(du ** 2 + dv ** 2) / DISK_R * R_OUT          # 米
    th = np.arctan2(dv, du)
    return rr, th


def stamp_text(img_h, text, cx_px, cy_px, scale, depth):
    """把文字压进高度图（压印字），中心位置 cx, cy（像素）。"""
    glyph_w = 6 * scale
    x0 = int(cx_px - len(text) * glyph_w / 2)
    y0 = int(cy_px - 7 * scale / 2)
    tmp = np.zeros((img_h.shape[0], img_h.shape[1], 3))
    hs.draw_text(tmp, text, x0, y0, scale, (1, 1, 1))
    img_h -= tmp[..., 0] * depth


def bearing_textures(worn):
    noise_lo = hs.periodic_noise(S, 40, rng)
    noise_md = hs.periodic_noise(S, 6, rng)
    noise_fn = hs.periodic_noise(S, 1.0, rng)
    base = np.zeros((S, S, 3))
    metal = np.ones((S, S))
    smooth = np.full((S, S), 0.62)
    height = np.zeros((S, S))
    steel = lin("#7C7F82")
    ground = lin("#8E9090")                     # 磨削过的套圈端面，略亮
    shield = lin("#74777A")
    base[:] = steel

    for (cu, cv) in [(0.25, 0.75), (0.75, 0.75)]:
        rr, th = polar(cu, cv)
        inside = rr <= R_OUT
        ring_out = inside & (rr >= 22.4 * MM)
        ring_in = inside & (rr <= 12.6 * MM)
        sh = inside & ~ring_out & ~ring_in
        base[ring_out | ring_in] = ground
        base[sh] = shield
        smooth[sh] = 0.5
        # 套圈端面的磨削纹（同心细纹）
        height += np.where(ring_out | ring_in, 0.015 * np.sin(rr / (0.035 * MM)), 0)
        # 防尘盖压印：同心加强筋、卡簧边、规格字
        height -= np.where(np.abs(rr - 17.5 * MM) < 0.25 * MM, 0.6, 0)
        height -= np.where(np.abs(rr - 21.9 * MM) < 0.18 * MM, 0.8, 0)
        if cu < 0.5:
            px_per_m = DISK_R * S / R_OUT
            stamp_text(height, "B18-52 2Z", cu * S, (1 - cv) * S + 17.0 * MM * px_per_m, 2, 0.9)
            stamp_text(height, "07", cu * S, (1 - cv) * S - 17.0 * MM * px_per_m, 2, 0.9)
        if worn:
            # 同心刮痕（局部弧段）与径向划痕：近看可辨认的金属亮痕 + 刻痕
            _y, _x = rr * np.sin(th), rr * np.cos(th)
            text_zone = ((np.abs(_y + 17.0 * MM) < 2.2 * MM) | (np.abs(_y - 17.0 * MM) < 2.2 * MM)) & (np.abs(_x) < 10.5 * MM)   # 不在规格字上划
            for _ in range(22):
                r0 = prng.uniform(12.8, 25.8) * MM
                a0 = prng.uniform(-math.pi, math.pi)
                span = prng.uniform(0.3, 2.4)
                wdt = prng.uniform(0.05, 0.16) * MM
                dth = np.angle(np.exp(1j * (th - a0)))
                m = inside & ~text_zone & (np.abs(rr - r0) < wdt) & (dth > 0) & (dth < span)
                height[m] -= prng.uniform(0.25, 0.6)
                base[m] = base[m] * 0.55 + lin("#D7D8D6") * 0.45
                smooth[m] = 0.78
            for _ in range(12):
                a0 = prng.uniform(-math.pi, math.pi)
                r0, r1 = sorted([prng.uniform(9.5, 25.5) * MM, prng.uniform(9.5, 25.5) * MM])
                m = inside & ~text_zone & (np.abs(np.angle(np.exp(1j * (th - a0)))) * rr < prng.uniform(0.03, 0.07) * MM) & (rr > r0) & (rr < r1)
                height[m] -= 0.45
                base[m] = base[m] * 0.6 + lin("#5E5A55") * 0.4
            # 油污：集中在防尘盖与外圈的缝附近，沿一个扇区拖开，边缘带琥珀色
            oil_center = 0.6 if cu < 0.5 else -2.2
            dth = np.angle(np.exp(1j * (th - oil_center)))
            oil = np.exp(-(dth / 1.1) ** 2) * np.exp(-((rr - 21.5 * MM) / (3.8 * MM)) ** 2)
            oil = oil * (0.55 + 0.75 * noise_md) - 0.18
            oil = np.clip(oil * 2.2, 0, 1) * inside
            edge = np.clip(oil * (1 - oil) * 4, 0, 1)
            base = base * (1 - oil[..., None] * 0.88) + lin("#1E1812")[None, None, :] * (oil[..., None] * 0.88)
            crev = inside & ((np.abs(rr - 12.6 * MM) < 0.45 * MM) | (np.abs(rr - 22.4 * MM) < 0.45 * MM))   # 防尘盖与套圈缝里的黑垢
            base[crev] = base[crev] * 0.35 + lin("#1A1612") * 0.65
            smooth[crev] = 0.3
            base = base * (1 - edge[..., None] * 0.35) + lin("#6E5426")[None, None, :] * (edge[..., None] * 0.35)
            metal -= oil * 0.75
            smooth = smooth * (1 - oil) + 0.82 * oil
            # 金属屑反光点（嵌在油里）+ 点蚀
            for _ in range(70):
                a0 = oil_center + prng.gauss(0, 0.6)
                r0 = prng.gauss(21.6, 1.4) * MM
                m = inside & ((rr * np.cos(th) - r0 * math.cos(a0)) ** 2 + (rr * np.sin(th) - r0 * math.sin(a0)) ** 2 < (prng.uniform(0.08, 0.22) * MM) ** 2)
                base[m] = lin("#E3E1DA")
                metal[m] = 1.0
                smooth[m] = 0.9
                height[m] += 0.5
            pit = (noise_fn > 0.86) & inside & (noise_lo > 0.55)
            height[pit] -= 0.5
            base[pit] *= 0.7
            # 内圈轻微热变色（低饱和的草黄 → 灰蓝），说明过热，不夸张
            heat = inside & (rr <= 12.6 * MM)
            tint = np.clip((12.6 * MM - rr) / (3.6 * MM), 0, 1) * (0.5 + 0.5 * noise_lo)
            col = np.where((tint > 0.45)[..., None], lin("#6A6E78"), lin("#8C7A55"))
            k = (tint * 0.45 * heat)[..., None]
            base = base * (1 - k) + col * k

    # 外圆柱（v 0.27–0.47）与内孔（v 0.04–0.24）
    side = (V >= 0.27) & (V <= 0.47)
    bore = (V >= 0.04) & (V <= 0.24)
    base[side] = steel * 0.97
    base[bore] = steel * 0.9
    height += np.where(side, 0.015 * np.sin(V * S * 1.7), 0)              # 外圈磨削纹
    if worn:
        zf = (V - 0.27) / 0.2                                                # 0 = 下端，1 = 上端
        drip = np.zeros((S, S))
        for _ in range(9):
            u0 = prng.uniform(0.0, 1.0)
            length = prng.uniform(0.3, 0.95)
            wdt = prng.uniform(0.004, 0.012)
            du = np.abs(((U - u0 + 0.5) % 1.0) - 0.5)
            drip = np.maximum(drip, np.clip(1 - du / wdt, 0, 1) * np.clip((zf - (1 - length)) * 6, 0, 1))
        drip = np.clip(drip * (0.6 + 0.6 * noise_md), 0, 1) * side
        base = base * (1 - drip[..., None] * 0.8) + lin("#2A2118")[None, None, :] * (drip[..., None] * 0.8)
        metal -= drip * 0.7
        smooth = smooth * (1 - drip) + 0.8 * drip
        # 外圈周向刮痕（拆装时在座孔里刮出的）
        for _ in range(40):
            v0 = prng.uniform(0.275, 0.465)
            u0, span = prng.uniform(0, 1), prng.uniform(0.04, 0.3)
            m = side & (np.abs(V - v0) < prng.uniform(0.0008, 0.002)) & (((U - u0) % 1.0) < span)
            height[m] -= 0.5
            base[m] = base[m] * 0.5 + lin("#D4D4D0") * 0.5
            smooth[m] = 0.75
        # 外圈下沿两处很小的锈点
        rust = side & (zf < 0.25) & (noise_lo > 0.72) & (noise_fn > 0.5)
        base[rust] = base[rust] * 0.4 + lin("#6B4A33") * 0.6
        metal[rust] = 0.15
        smooth[rust] = 0.2
        base = base * (0.93 + 0.07 * noise_md[..., None])
    else:
        base = base * (0.985 + 0.015 * noise_md[..., None])
        smooth = smooth + 0.04 * (noise_md - 0.5)
    metal = np.clip(metal, 0, 1)
    smooth = np.clip(smooth, 0, 1)
    tag = "Worn" if worn else "New"
    imgs = {
        f"T_FK_Bearing{tag}_BaseColor": save_png(f"T_FK_Bearing{tag}_BaseColor.png", np.clip(base, 0, 1)),
        f"T_FK_Bearing{tag}_MetallicSmoothness": save_png(f"T_FK_Bearing{tag}_MetallicSmoothness.png",
                                                          np.stack([metal, metal, metal], -1), alpha=smooth, noncolor=True),
        f"T_FK_Bearing{tag}_Normal": save_png(f"T_FK_Bearing{tag}_Normal.png", normal_from_height(height, 0.4), noncolor=True),
    }
    return imgs


def clog_texture():
    n512 = 512
    lo = hs.periodic_noise(n512, 30, rng)
    md = hs.periodic_noise(n512, 5, rng)
    fn = hs.periodic_noise(n512, 1.0, rng)
    v_ = 0.5 * lo + 0.35 * md + 0.15 * fn
    dust = lin("#6E6A63")
    dark = lin("#45423D")
    light = lin("#9A968D")
    col = dust[None, None, :] * (0.8 + 0.4 * (v_[..., None] - 0.5))
    col = np.where((v_ < 0.35)[..., None], col * 0.7 + dark * 0.3, col)
    col = np.where((fn > 0.8)[..., None], col * 0.6 + light * 0.4, col)
    # u 0.70–0.75：护栅上的浅色积尘（比护栅的灰亮、偏暖，一眼看得出是灰）
    c0, c1 = int(n512 * 0.70), int(n512 * 0.75)
    col[:, c0:c1] = lin("#A8A194")[None, None, :] * (0.82 + 0.3 * (v_[:, c0:c1, None] - 0.5)) * (0.9 + 0.15 * fn[:, c0:c1, None])
    # 右侧 1/4：纤维色带（灰白、褪色蓝、暗红、米色）
    bands = [lin("#8F8A80"), lin("#4F5B68"), lin("#6A4840"), lin("#A39880")]          # 灰、褪色蓝、暗红、米色：都压暗，不刺眼
    for i, b in enumerate(bands):
        col[:, int(n512 * (0.75 + i * 0.0625)):int(n512 * (0.75 + (i + 1) * 0.0625))] = b * (0.9 + 0.2 * md[:, :16].mean())
    hmap = v_ * 1.0
    return {"T_FK_IntakeClog_BaseColor": save_png("T_FK_IntakeClog_BaseColor.png", np.clip(col, 0, 1)),
            "T_FK_IntakeClog_Normal": save_png("T_FK_IntakeClog_Normal.png", normal_from_height(hmap * 6, 1.0), noncolor=True)}


# 字库补字（共用库缺 F、K、W、Y、:）——只在本脚本里补，不改共用库
hs.FONT.update({
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    ":": ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
})

LABEL_LINES = [
    ("L-03  SERVICE", None),
    ("03.11  BRG CHECK   OK", "a"),
    ("07.02  INTAKE CLEAN", "b"),
    ("11.20  BRG NOISE - WATCH", "c"),
    ("", None),
]


def label_texture():
    w_, h_ = 640, 416                             # 46 × 30 mm 的贴纸：约 0.072 mm / 像素
    lo = hs.periodic_noise(max(w_, h_), 40, rng)[:h_, :w_]
    md = hs.periodic_noise(max(w_, h_), 4, rng)[:h_, :w_]
    paper = lin("#CFC6AE")
    img = paper[None, None, :] * (0.9 + 0.12 * (lo[..., None] - 0.5)) * (0.97 + 0.04 * md[..., None])
    # 老化：边缘发黄、一角油污指印、轻微脏点
    yy_, xx_ = np.mgrid[0:h_, 0:w_]
    edge = np.minimum(np.minimum(xx_, w_ - 1 - xx_), np.minimum(yy_, h_ - 1 - yy_))
    img = img * (1 - np.clip(1 - edge / 40.0, 0, 1)[..., None] * 0.18) + lin("#9C8A5E") * (np.clip(1 - edge / 40.0, 0, 1)[..., None] * 0.18)
    thumb = np.exp(-(((xx_ - 540) / 60.0) ** 2 + ((yy_ - 330) / 44.0) ** 2))
    ridges = 0.5 + 0.5 * np.sin(np.sqrt((xx_ - 540) ** 2 + ((yy_ - 330) * 1.3) ** 2) / 2.2)
    img = img * (1 - (thumb * ridges * 0.3)[..., None]) + lin("#3A3226") * (thumb * ridges * 0.3)[..., None]
    ink = lin("#2C3038")
    print_ink = lin("#3B3B38")
    # 印刷的表头和格线
    hs.rect(img, 20, 20, w_ - 20, 24, print_ink)
    hs.rect(img, 20, 84, w_ - 20, 86, print_ink)
    for y in (150, 214, 278, 342):
        hs.rect(img, 20, y, w_ - 20, y + 1, print_ink * 1.6)
    hs.rect(img, 150, 86, 152, 396, print_ink * 1.6)
    hs.draw_text(img, LABEL_LINES[0][0], 32, 36, 5, print_ink)
    # 手写记录：字形逐字轻微抖动、墨色深浅不一
    rows = [(LABEL_LINES[1][0], 100), (LABEL_LINES[2][0], 164), (LABEL_LINES[3][0], 228)]
    for text, y in rows:
        cx = 34
        for ch in text:
            jitter_y = prng.randint(-2, 2)
            shade = ink * prng.uniform(0.85, 1.25)
            tmp = np.zeros_like(img)
            hs.draw_text(tmp, ch, cx, y + jitter_y, 4, (1, 1, 1))
            m = tmp[..., 0] > 0
            img[m] = img[m] * 0.15 + shade * 0.85
            cx += 6 * 4 + prng.randint(-1, 1)
    # 最后一行下面一条手画的划线（提醒）
    for i in range(330):
        x = 34 + i
        y = 268 + int(2 * math.sin(i / 23.0))
        img[y:y + 3, x] = ink
    img = np.clip(img, 0, 1)
    return {"T_FK_CoverLabel_BaseColor": save_png("T_FK_CoverLabel_BaseColor.png", img)}


images = {}
images.update(bearing_textures(worn=True))
images.update(bearing_textures(worn=False))
images.update(clog_texture())
images.update(label_texture())

# ---------------------------------------------------------------------------
# 2. 材质（Principled，供渲染；Unity 用 materials.json + 贴图建 URP Lit 材质）
# ---------------------------------------------------------------------------
MAT_SPECS = {
    "M_FK_BearingWorn": dict(base="T_FK_BearingWorn_BaseColor", ms="T_FK_BearingWorn_MetallicSmoothness", normal="T_FK_BearingWorn_Normal"),
    "M_FK_BearingNew": dict(base="T_FK_BearingNew_BaseColor", ms="T_FK_BearingNew_MetallicSmoothness", normal="T_FK_BearingNew_Normal"),
    "M_FK_MetalChips": dict(color="#D9D7D0", metallic=1.0, roughness=0.22),
    # tint：贴图乘的底色（sRGB）。场景验收后加：
    # - 积尘 #B38F66 = (0.70, 0.56, 0.40)，暖褐“旧油泥”——在常用左引擎镜头下和米色上盖、灰色护栅都拉得开（不是红色，也不发光）；
    # - 保养标记 #A3A6A8 = (0.64, 0.65, 0.66) 旧纸色、哑光（光滑度 0.08）、关高光和环境反射——台灯直射下不再过曝（qa/unit07-fault-art-scene 验证过）。
    "M_FK_IntakeClog": dict(base="T_FK_IntakeClog_BaseColor", normal="T_FK_IntakeClog_Normal", metallic=0.0, roughness=0.95, tint="#B38F66"),
    "M_FK_CoverLabel": dict(base="T_FK_CoverLabel_BaseColor", metallic=0.0, roughness=0.92, tint="#A3A6A8", specular=False, env_reflections=False),
}
MATS = hs.MATS
MATS.clear()
for name, spec in MAT_SPECS.items():
    mat = bpy.data.materials.new(name)
    tree = hs.ensure_node_tree(mat)
    nodes, links = tree.nodes, tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    if "base" in spec:
        t = nodes.new("ShaderNodeTexImage")
        t.image = images[spec["base"]]
        if "tint" in spec:
            mul = nodes.new("ShaderNodeVectorMath")
            mul.operation = "MULTIPLY"
            mul.inputs[1].default_value = tuple(hs.srgb_to_linear(c) for c in hs.hex_rgb(spec["tint"]))
            links.new(t.outputs["Color"], mul.inputs[0])
            links.new(mul.outputs["Vector"], bsdf.inputs["Base Color"])
        else:
            links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*[hs.srgb_to_linear(c) for c in hs.hex_rgb(spec["color"])], 1)
    if "ms" in spec:
        t = nodes.new("ShaderNodeTexImage")
        t.image = images[spec["ms"]]
        links.new(t.outputs["Color"], bsdf.inputs["Metallic"])
        inv = nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        links.new(t.outputs["Alpha"], inv.inputs[1])
        links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    else:
        bsdf.inputs["Metallic"].default_value = spec.get("metallic", 0.0)
        bsdf.inputs["Roughness"].default_value = spec.get("roughness", 0.5)
    if spec.get("specular", True) is False and "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.0
    if "normal" in spec:
        t = nodes.new("ShaderNodeTexImage")
        t.image = images[spec["normal"]]
        nm = nodes.new("ShaderNodeNormalMap")
        links.new(t.outputs["Color"], nm.inputs["Color"])
        links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    MATS[name] = mat

# ---------------------------------------------------------------------------
# 3. 几何
# ---------------------------------------------------------------------------

def link_obj(name, me, coll, mat, matrix_world):
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    me.materials.append(MATS[mat])
    ob.matrix_world = matrix_world
    return ob


# ---- 3.1 轴承（深沟球轴承，两面防尘盖）：旋转体，轮廓按实测内外径、宽度
def bearing_profile():
    ro, rb, hw = R_OUT / MM, R_BORE / MM, HALF_W / MM
    c = 0.3
    rs_o, rs_i = 22.4, 12.6            # 外圈内沿、内圈外沿
    sh = hw - 0.6                      # 防尘盖内凹 0.6 mm
    pts = [
        (rb + c, hw), (rs_i - c, hw), (rs_i, hw - c), (rs_i, sh), (rs_o, sh), (rs_o, hw - c), (rs_o + c, hw),
        (ro - c, hw), (ro, hw - c), (ro, -hw + c), (ro - c, -hw),
        (rs_o + c, -hw), (rs_o, -hw + c), (rs_o, -sh), (rs_i, -sh), (rs_i, -hw + c), (rs_i - c, -hw),
        (rb + c, -hw), (rb, -hw + c), (rb, hw - c),
    ]
    return [(p[0] * MM, p[1] * MM) for p in pts]


def bearing_mesh(name, segs=72):
    prof = bearing_profile()
    n = len(prof)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    ring = [[bm.verts.new((rr * math.cos(2 * math.pi * k / segs), rr * math.sin(2 * math.pi * k / segs), zz)) for (rr, zz) in prof] for k in range(segs)]
    for k in range(segs):
        k2 = (k + 1) % segs
        for i in range(n):
            j = (i + 1) % n
            corners = [(ring[k][i], k, i), (ring[k2][i], k + 1, i), (ring[k2][j], k + 1, j), (ring[k][j], k, j)]   # 第 k+1 列在接缝处用 segs，不回绕
            f = bm.faces.new([c[0] for c in corners])
            (r1, z1), (r2, z2) = prof[i], prof[j]
            flat = abs(z1 - z2) < 1e-7
            for loop, (_, kk, pi_) in zip(f.loops, corners):
                th = 2 * math.pi * kk / segs
                rr, zz = prof[pi_]
                if flat or (abs(z1) > HALF_W * 0.85 and abs(z2) > HALF_W * 0.85 and min(r1, r2) > R_BORE + 0.2 * MM and max(r1, r2) < R_OUT - 0.2 * MM):
                    cu = 0.25 if (z1 + z2) > 0 else 0.75
                    sgn = 1 if cu == 0.25 else -1          # 下端面镜像，从下方看文字方向正确
                    uu = cu + sgn * math.cos(th) * rr / R_OUT * DISK_R
                    vv = 0.75 + math.sin(th) * rr / R_OUT * DISK_R
                elif min(r1, r2) > (R_OUT + R_BORE) / 2:
                    uu, vv = kk / segs, 0.27 + (zz + HALF_W) / (2 * HALF_W) * 0.2
                else:
                    uu, vv = kk / segs, 0.04 + (zz + HALF_W) / (2 * HALF_W) * 0.2
                loop[uvl].uv = (uu, vv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(40))
    return me


bearing_worn = link_obj("UNIT07_FK_BearingTop_L_Worn", bearing_mesh("UNIT07_FK_BearingTop_L_Worn"), groups["BearingWorn"], "M_FK_BearingWorn", FRAME)
bearing_new = link_obj("UNIT07_FK_BearingTop_L_New", bearing_mesh("UNIT07_FK_BearingTop_L_New"), groups["BearingNew"], "M_FK_BearingNew", FRAME)

# 金属屑：12 片不规则薄片，嵌在上端面防尘盖与外圈之间的油里，以及外圈上的油迹里
bm = bmesh.new()
for i in range(12):
    on_side = i >= 9
    ang = 0.6 + prng.gauss(0, 0.55) if not on_side else prng.uniform(0.2, 1.1)
    size = prng.uniform(0.8, 2.2) * MM
    pts2 = []
    for k in range(prng.randint(4, 6)):
        a = 2 * math.pi * k / 5 + prng.uniform(-0.4, 0.4)
        pts2.append((math.cos(a) * size * prng.uniform(0.35, 0.6), math.sin(a) * size * prng.uniform(0.2, 0.5)))
    thick = 0.08 * MM
    if not on_side:
        rr = prng.gauss(22.0, 0.9) * MM
        center = Vector((math.cos(ang) * rr, math.sin(ang) * rr, HALF_W - 0.6 * MM + thick * 0.5 if rr < 22.4 * MM else HALF_W + thick * 0.5))
        rot = Matrix.Rotation(prng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(prng.uniform(-0.25, 0.25), 3, "X")
    else:
        center = Vector((math.cos(ang) * (R_OUT + thick * 0.5), math.sin(ang) * (R_OUT + thick * 0.5), prng.uniform(-3.5, 3.0) * MM))
        rot = Matrix.Rotation(ang, 3, "Z") @ Matrix.Rotation(math.pi / 2, 3, "Y") @ Matrix.Rotation(prng.uniform(0, math.pi), 3, "Z")
    vb = [bm.verts.new(center + rot @ Vector((x, y, -thick / 2))) for x, y in pts2]
    vt = [bm.verts.new(center + rot @ Vector((x * 0.9, y * 0.9, thick / 2))) for x, y in pts2]
    for k in range(len(pts2)):
        j = (k + 1) % len(pts2)
        bm.faces.new((vb[k], vb[j], vt[j], vt[k]))
    bm.faces.new(list(reversed(vb)))
    bm.faces.new(vt)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("UNIT07_FK_BearingTop_L_Worn_Chips")
bm.to_mesh(me)
bm.free()
chips = link_obj("UNIT07_FK_BearingTop_L_Worn_Chips", me, groups["BearingWorn"], "M_FK_MetalChips", FRAME)
chips.parent = bearing_worn
chips.matrix_parent_inverse = bearing_worn.matrix_world.inverted()

# ---- 3.2 进气口堵塞：护栅下方的积尘毡层 + 挂在护栅上的纤维 + 唇口内壁积尘
guard_bvh = world_bvh(R["Engine_IntakeGuard_L"])
lip_bvh = world_bvh(R["Engine_IntakeLip_L"])
duct_bvh = world_bvh(R["Engine_IntakeDuct_L"])
cover_bvh = world_bvh(R["Engine_UpperCover_L"])
gp = world_verts(R["Engine_IntakeGuard_L"]) - C
g_ax = gp @ A
GUARD_BOT, GUARD_TOP = float(g_ax.min()), float(g_ax.max())
lp_ = world_verts(R["Engine_IntakeLip_L"]) - C
l_r = np.linalg.norm(lp_ - np.outer(lp_ @ A, A), axis=1)
LIP_IN = float(l_r.min())
INTAKE = {"guard_bottom_mm": GUARD_BOT / MM, "guard_top_mm": GUARD_TOP / MM, "lip_inner_radius_mm": LIP_IN / MM}

# 护栅俯视高度图：从上往下打射线，得到护栅条的位置（用于纤维搭在条上）
GRID = 0.5 * MM
gx_ = np.arange(-LIP_IN, LIP_IN + 1e-9, GRID)
bar_top = {}
for xi in gx_:
    for yi in gx_:
        if xi * xi + yi * yi > (LIP_IN - 0.5 * MM) ** 2:
            continue
        o = at_axis(GUARD_TOP + 5 * MM) + REFX * xi + REFY * yi
        hit = guard_bvh.ray_cast(o, -AX, 12 * MM)
        if hit[0] is not None:
            bar_top[(round(xi / GRID), round(yi / GRID))] = (hit[0] - CEN).dot(AX)
BAR_FRACTION = len(bar_top) / max(1, sum(1 for xi in gx_ for yi in gx_ if xi * xi + yi * yi <= (LIP_IN - 0.5 * MM) ** 2))


def bar_height_near(x, y, reach):
    """(x, y) 周围 reach 范围内护栅条的最高处（没有条返回 None）。"""
    n = int(math.ceil(reach / GRID))
    best_h = None
    for dx in range(-n, n + 1):
        for dy in range(-n, n + 1):
            if dx * dx + dy * dy > n * n:
                continue
            t = bar_top.get((round(x / GRID) + dx, round(y / GRID) + dy))
            if t is not None and (best_h is None or t > best_h):
                best_h = t
    return best_h


def bar_height(x, y):
    return bar_top.get((round(x / GRID), round(y / GRID)))


# 积尘毡层：护栅下方 0.6 mm 起往下 2–7 mm 的起伏圆饼；几处“透气薄点”（变薄下陷，网格保持封闭）
mat_top = GUARD_BOT - 0.6 * MM
nr, na = 18, 64
rad = LIP_IN - 0.5 * MM
lo_n = hs.periodic_noise(128, 6, rng)
thin_spots = [(prng.uniform(0.3, 0.85) * rad, prng.uniform(0, 2 * math.pi)) for _ in range(7)]


def dust_point(rr, a):
    nval = lo_n[int(64 + 60 * rr / rad * math.cos(a)) % 128, int(64 + 60 * rr / rad * math.sin(a)) % 128]
    x, y = math.cos(a) * rr, math.sin(a) * rr
    dip = 0.0
    for (sr, sa) in thin_spots:
        dd = math.hypot(x - math.cos(sa) * sr, y - math.sin(sa) * sr)
        dip = max(dip, math.exp(-(dd / (1.8 * MM)) ** 2))
    thick = (2.0 + 5.0 * nval) * MM * (1.0 - 0.35 * (rr / rad) ** 3)
    top_z = mat_top - (1.0 - nval) * 0.8 * MM - dip * thick * 0.8
    bot_z = mat_top - (1.0 - nval) * 0.8 * MM - thick
    return x, y, top_z, bot_z


bm = bmesh.new()
uvl = bm.loops.layers.uv.verify()
cx, cy, ct, cb = dust_point(0.0, 0.0)
c_top = bm.verts.new((0, 0, ct))
c_bot = bm.verts.new((0, 0, cb))
tops, bots = [], []
for i in range(1, nr + 1):
    rr = rad * i / nr
    tr, br_ = [], []
    for k in range(na):
        x, y, tz, bz = dust_point(rr, 2 * math.pi * k / na)
        tr.append(bm.verts.new((x, y, tz)))
        br_.append(bm.verts.new((x, y, bz)))
    tops.append(tr)
    bots.append(br_)


def uv_set(f):
    for loop in f.loops:
        co = loop.vert.co
        loop[uvl].uv = (0.37 + co.x / rad * 0.35, 0.5 + co.y / rad * 0.45)


for k in range(na):
    k2 = (k + 1) % na
    uv_set(bm.faces.new((c_top, tops[0][k], tops[0][k2])))
    uv_set(bm.faces.new((c_bot, bots[0][k2], bots[0][k])))
    for i in range(nr - 1):
        uv_set(bm.faces.new((tops[i][k], tops[i + 1][k], tops[i + 1][k2], tops[i][k2])))
        uv_set(bm.faces.new((bots[i][k2], bots[i + 1][k2], bots[i + 1][k], bots[i][k])))
    uv_set(bm.faces.new((tops[-1][k], bots[-1][k], bots[-1][k2], tops[-1][k2])))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("UNIT07_FK_IntakeClog_L_DustMat")
bm.to_mesh(me)
bm.free()
me.shade_smooth()
clog_root = link_obj("UNIT07_FK_IntakeClog_L_DustMat", me, groups["IntakeClog"], "M_FK_IntakeClog", FRAME)

# 纤维：沿护栅表面拖过的细线，搭在条上、在条间下垂（不低于护栅底面）
# 场景验收（qa/unit07-fault-art-scene → art/unit07-fault-kit-dust-fibers）：游戏镜头下 0.3–0.5 mm 粗的纤维远小于一个像素，运动时忽隐忽现。
# 试验“减少 / 加粗”后采用：隔一根留一根（FIBER_KEEP_EVERY = 2），半径 × 1.4（FIBER_RADIUS_SCALE）。
# 随机数照常抽取（被跳过的纤维也抽），保证其它资源的随机序列不变。
FIBER_KEEP_EVERY = 2
FIBER_RADIUS_SCALE = 1.4
CLEAR = (0.24 * FIBER_RADIUS_SCALE + 0.16) * MM   # 纤维中心离条顶的距离：最粗纤维半径 + 0.16 mm


def fiber_path(seed):
    pr = random.Random(seed)
    a0 = pr.uniform(0, 2 * math.pi)
    start_r = pr.uniform(0.2, 0.9) * (LIP_IN - 2 * MM)
    p = Vector((math.cos(a0) * start_r, math.sin(a0) * start_r))
    heading = pr.uniform(0, 2 * math.pi)
    pts = []
    for s in range(34):
        heading += pr.gauss(0, 0.18)
        p = p + Vector((math.cos(heading), math.sin(heading))) * 1.1 * MM
        if p.length > LIP_IN - 1.2 * MM:
            break
        top = bar_height_near(p.x, p.y, 1.6 * MM)          # 纤维粗 0.2–0.3 mm：附近 1.6 mm 内有条就搭在条顶上
        z = (top + CLEAR) if top is not None else GUARD_TOP - pr.uniform(0.4, 1.2) * MM   # 只在宽缝里稍微下垂
        pts.append(Vector((p.x, p.y, z)))
    return pts


fib_colors = [0.78, 0.81, 0.78, 0.84, 0.87, 0.78, 0.78, 0.81, 0.87, 0.78, 0.84, 0.78, 0.78, 0.87, 0.78, 0.81]   # 纤维色带 u（灰白居多，少量褪色彩线）
bm_all = bmesh.new()
uv_all = bm_all.loops.layers.uv.verify()
fiber_count = 0
for i in range(11):
    pts = fiber_path(100 + i)
    if len(pts) < 6:
        continue
    # 平滑一下高度，避免锯齿
    zs = [p.z for p in pts]
    zs = [max(zs[max(0, j - 1):j + 2]) for j in range(len(zs))]
    pts = [Vector((p.x, p.y, z)) for p, z in zip(pts, zs)]
    radius = prng.uniform(0.14, 0.24) * MM * FIBER_RADIUS_SCALE
    if i % FIBER_KEEP_EVERY:
        continue
    tb = hs.bm_tube([tuple(p) for p in pts], [radius] * len(pts), segs=5)
    uvt = tb.loops.layers.uv.verify()
    for f in tb.faces:
        for loop in f.loops:
            loop[uvt].uv = (fib_colors[i], 0.5)
    me_t = bpy.data.meshes.new("tmp_fiber")
    tb.to_mesh(me_t)
    tb.free()
    bm_all.from_mesh(me_t)
    bpy.data.meshes.remove(me_t)
    fiber_count += 1
# 毛团：护栅中心毂上的一小团短纤维
for j in range(14):
    a = prng.uniform(0, 2 * math.pi)
    rr = prng.uniform(0, 9) * MM
    base_p = Vector((math.cos(a) * rr, math.sin(a) * rr))
    top = bar_height_near(base_p.x, base_p.y, 0.8 * MM)
    if top is None:
        continue
    pts = [Vector((base_p.x, base_p.y, top + CLEAR))]
    hd = prng.uniform(0, 2 * math.pi)
    for s in range(5):
        hd += prng.gauss(0, 0.6)
        q = pts[-1].xy + Vector((math.cos(hd), math.sin(hd))) * 0.9 * MM
        t2 = bar_height_near(q.x, q.y, 0.8 * MM)
        pts.append(Vector((q.x, q.y, (t2 if t2 is not None else top) + CLEAR + s * 0.12 * MM)))
    if j % FIBER_KEEP_EVERY:
        continue
    tb = hs.bm_tube([tuple(p) for p in pts], [0.15 * MM * FIBER_RADIUS_SCALE] * len(pts), segs=4)
    uvt = tb.loops.layers.uv.verify()
    for f in tb.faces:
        for loop in f.loops:
            loop[uvt].uv = (0.78, 0.5)
    me_t = bpy.data.meshes.new("tmp_fuzz")
    tb.to_mesh(me_t)
    tb.free()
    bm_all.from_mesh(me_t)
    bpy.data.meshes.remove(me_t)
me = bpy.data.meshes.new("UNIT07_FK_IntakeClog_L_Fibers")
bm_all.to_mesh(me)
bm_all.free()
me.shade_smooth()
fibers = link_obj("UNIT07_FK_IntakeClog_L_Fibers", me, groups["IntakeClog"], "M_FK_IntakeClog", FRAME)
fibers.parent = clog_root
fibers.matrix_parent_inverse = clog_root.matrix_world.inverted()

# 护栅上的积尘：贴着护栅条朝上的表面（离表面 0.15 mm）的一层斑驳积尘。
# 做法：取护栅网格里朝上的面（只读取几何，不改原件），细分后按噪声留一部分，沿法线抬 0.15 mm——不会钻进护栅里。
gsrc = R["Engine_IntakeGuard_L"]
bm = bmesh.new()
bm.from_mesh(gsrc.data)
bm.transform(FRAME.inverted() @ gsrc.matrix_world)          # 换到轴承 / 进气口坐标系
bm.normal_update()
up_faces = [f for f in bm.faces if f.normal.z > 0.55 and math.hypot(*f.calc_center_median().xy) < LIP_IN - 2.5 * MM]   # 只要辐条和中心毂（外圈压在唇口下面）
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in set(up_faces)], context="FACES")
for _ in range(7):                                             # 反复细分到边长 ≤ 0.6 mm，噪声遮罩才能做出不规则的斑块
    long_e = [e for e in bm.edges if e.calc_length() > 0.6 * MM]
    if not long_e:
        break
    bmesh.ops.subdivide_edges(bm, edges=long_e, cuts=1, use_grid_fill=True)
bmesh.ops.triangulate(bm, faces=bm.faces)
bm.normal_update()
gd_noise = hs.periodic_noise(256, 7, rng)


def dust_keep(c):
    rr_ = math.hypot(c.x, c.y)
    n = 0.6 * gd_noise[int(128 + c.x / (40 * MM) * 240) % 256, int(128 + c.y / (40 * MM) * 240) % 256] + 0.4 * gd_noise[int(c.y / (40 * MM) * 700) % 256, int(c.x / (40 * MM) * 700) % 256]
    density = 0.40 + 0.30 * (rr_ / LIP_IN) + 0.15 * math.cos(math.atan2(c.y, c.x) - 2.3)   # 外圈、上风侧积得多
    return n < density


drop = [f for f in bm.faces if not dust_keep(f.calc_center_median())]
bmesh.ops.delete(bm, geom=drop, context="FACES")
for v in bm.verts:
    if v.link_faces:
        nrm = sum((f.normal for f in v.link_faces), Vector()).normalized()
        fluff = gd_noise[int(128 + v.co.x / (40 * MM) * 120 * 3) % 256, int(128 + v.co.y / (40 * MM) * 120 * 3) % 256]
        v.co += nrm * (0.15 * MM + fluff * 0.35 * MM)              # 起伏的绒层
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
uvl = bm.loops.layers.uv.verify()
for f in bm.faces:
    for loop in f.loops:
        co = loop.vert.co
        loop[uvl].uv = (0.705 + 0.04 * ((co.x / (6 * MM)) % 1.0), 0.05 + 0.9 * ((co.y / rad + 1) / 2))   # 贴图里的浅色积尘带
# 去掉仍碰到护栅 / 唇口 / 上盖 / 风道的面（转到世界坐标做 BVH 相交）
for _ in range(3):
    bm.verts.index_update()
    coat_w = [FRAME @ v.co for v in bm.verts]
    coat_bvh = BVHTree.FromPolygons(coat_w, [tuple(v.index for v in f.verts) for f in bm.faces])
    bm.faces.ensure_lookup_table()
    hit_faces = set()
    for other in (guard_bvh, lip_bvh, cover_bvh, duct_bvh):
        hit_faces.update(i for i, _j in coat_bvh.overlap(other))
    if not hit_faces:
        break
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in hit_faces], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
patch = len(bm.faces)
me = bpy.data.meshes.new("UNIT07_FK_IntakeClog_L_GuardDust")
bm.to_mesh(me)
bm.free()
me.shade_smooth()
guard_dust = link_obj("UNIT07_FK_IntakeClog_L_GuardDust", me, groups["IntakeClog"], "M_FK_IntakeClog", FRAME)
guard_dust.parent = clog_root
guard_dust.matrix_parent_inverse = clog_root.matrix_world.inverted()
GUARD_DUST_PATCHES = patch

# 唇口内壁积尘：贴着内壁（离壁 0.25 mm）的一圈薄层，上沿不齐
band_lo, band_hi = GUARD_TOP + 0.8 * MM, None
lip_ax = lp_ @ A
band_hi = float(lip_ax.max()) - 3.0 * MM
bm = bmesh.new()
uvl = bm.loops.layers.uv.verify()
na2 = 96


def wall_radius(a, axial):
    """从轴线沿径向打射线到唇口 / 护栅外圈内壁，取最近的壁面半径。"""
    o = at_axis(axial)
    dvec = (REFX * math.cos(a) + REFY * math.sin(a)).normalized()
    best_r = None
    for bvh in (lip_bvh, guard_bvh, duct_bvh):
        hit = bvh.ray_cast(o, dvec, 0.08)
        if hit[0] is not None:
            rr_ = (hit[0] - o).length
            if rr_ > 25 * MM and (best_r is None or rr_ < best_r):
                best_r = rr_
    return best_r if best_r is not None else LIP_IN


rows = []
for k in range(na2):
    a = 2 * math.pi * k / na2
    top_h = band_lo + (band_hi - band_lo) * (0.35 + 0.65 * lo_n[k % 128, 17])
    r_lo = wall_radius(a, band_lo + 0.2 * MM) - 0.25 * MM          # 离内壁 0.25 mm
    r_hi = wall_radius(a, top_h - 0.2 * MM) - 0.25 * MM
    col = []
    for (rr, zz) in ((r_lo, band_lo), (r_hi, top_h), (r_hi - 0.6 * MM, top_h - 0.4 * MM), (r_lo - 0.6 * MM, band_lo)):
        col.append(bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, zz)))
    rows.append(col)
for k in range(na2):
    k2 = (k + 1) % na2
    for j in range(4):
        j2 = (j + 1) % 4
        f = bm.faces.new((rows[k][j], rows[k2][j], rows[k2][j2], rows[k][j2]))
        for loop in f.loops:
            loop[uvl].uv = (0.05 + 0.6 * (k / na2), 0.05 + 0.15 * (loop.vert.co.z - band_lo) / max(1e-6, band_hi - band_lo))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("UNIT07_FK_IntakeClog_L_RimDust")
bm.to_mesh(me)
bm.free()
rim = link_obj("UNIT07_FK_IntakeClog_L_RimDust", me, groups["IntakeClog"], "M_FK_IntakeClog", FRAME)
rim.parent = clog_root
rim.matrix_parent_inverse = clog_root.matrix_world.inverted()

# ---- 3.3 上盖内侧保养标记：在上盖内表面找一块最平的区域，贴合内表面的贴纸
LABEL_W, LABEL_H = 46 * MM, 30 * MM
best = None
LIP_OUT = float(l_r.max())
for rr in (66, 72, 80, 88):          # 贴纸内沿要离开唇口外缘（r {:.0f} mm）至少 3 mm
    for deg in range(0, 360, 20):
        a = math.radians(deg)
        ctr = REFX * math.cos(a) * rr * MM + REFY * math.sin(a) * rr * MM
        rad_dir = (REFX * math.cos(a) + REFY * math.sin(a)).normalized()
        tan_dir = AX.cross(rad_dir).normalized()
        hits = []
        ok = True
        for iu in range(-3, 4):
            for iv in range(-2, 3):
                o = CEN + ctr + tan_dir * (iu / 3 * LABEL_W / 2) + rad_dir * (iv / 2 * LABEL_H / 2)
                hit = cover_bvh.ray_cast(o, AX, 0.2)
                if hit[0] is None:
                    ok = False
                    break
                hits.append(((hit[0] - CEN).dot(AX), hit[1]))
            if not ok:
                break
        if not ok:
            continue
        hz = np.array([x[0] for x in hits])
        spread = float(hz.max() - hz.min())
        tilt = max(math.degrees(Vector(x[1]).angle(-AX)) for x in hits)
        score = spread + tilt * 0.0002
        if best is None or score < best[0]:
            best = (score, ctr, rad_dir, tan_dir, spread, tilt, rr, deg)
_, LCTR, LRAD, LTAN, LSPREAD, LTILT, LR, LDEG = best
nu, nv = 24, 16
bm = bmesh.new()
uvl = bm.loops.layers.uv.verify()
grid = []
max_gap = 0.0
for j in range(nv + 1):
    row = []
    for i in range(nu + 1):
        pu, pv = (i / nu - 0.5) * LABEL_W, (j / nv - 0.5) * LABEL_H
        o = CEN + LCTR + LTAN * pu + LRAD * pv
        hit = cover_bvh.ray_cast(o, AX, 0.2)
        if hit[0] is None:
            raise RuntimeError(f"贴纸投影没有打到上盖内表面：u={pu / MM:.1f} mm v={pv / MM:.1f} mm（换一个位置或缩小贴纸）")
        nrm = Vector(hit[1]).normalized()
        if nrm.dot(AX) > 0:
            nrm = -nrm
        p = hit[0] + nrm * 0.35 * MM                       # 离内表面 0.35 mm，朝向引擎内部
        row.append(bm.verts.new(FRAME.inverted() @ p))
    grid.append(row)
for j in range(nv):
    for i in range(nu):
        f = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
        for loop in f.loops:
            for jj in (j, j + 1):
                for ii in (i, i + 1):
                    if loop.vert is grid[jj][ii]:
                        loop[uvl].uv = (1.0 - ii / nu, 1.0 - jj / nv)     # 转 180°：从引擎内侧（上盖翻过来）看时字是正的
bm.normal_update()
# 朝向：面法线应朝向引擎内部（-轴向）
if sum(f.normal.z for f in bm.faces) > 0:
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
me = bpy.data.meshes.new("UNIT07_FK_CoverInnerLabel_L")
bm.to_mesh(me)
bm.free()
label = link_obj("UNIT07_FK_CoverInnerLabel_L", me, groups["CoverLabel"], "M_FK_CoverLabel", FRAME)
# 贴纸原点放到贴纸中心
lc_world = sum((label.matrix_world @ v.co for v in label.data.vertices), Vector()) / len(label.data.vertices)
offset = label.matrix_world.inverted() @ lc_world
label.data.transform(Matrix.Translation(-offset))
label.matrix_world = label.matrix_world @ Matrix.Translation(offset)

# 进气堵塞原点：护栅底面中心（沿轴线），方便挂到护栅下
clog_origin_local = Vector((0, 0, GUARD_BOT))
for ob in (clog_root,):
    ob.data.transform(Matrix.Translation(-clog_origin_local))
    ob.matrix_world = ob.matrix_world @ Matrix.Translation(clog_origin_local)
for ch in (fibers, rim, guard_dust):
    ch.data.transform(Matrix.Translation(-clog_origin_local))
    ch.matrix_parent_inverse = Matrix.Identity(4)
    ch.matrix_world = clog_root.matrix_world
bpy.context.view_layer.update()

# ---------------------------------------------------------------------------
# 4. 检查：尺寸、轴向、与原件 / 周围零件的穿插
# ---------------------------------------------------------------------------

def obj_bvh(o):
    return world_bvh(o)


_BVH_CACHE = {}


def overlap_count(a, b):
    if b.name not in _BVH_CACHE:
        _BVH_CACHE[b.name] = obj_bvh(b)
    return len(obj_bvh(a).overlap(_BVH_CACHE[b.name]))


def bearing_measure(o):
    p = world_verts(o) - C
    hh = p @ A
    rr = np.linalg.norm(p - np.outer(hh, A), axis=1)
    cc = world_verts(o).mean(0)
    ww, vv = np.linalg.eigh(np.cov((world_verts(o) - cc).T))
    ax = vv[:, int(np.argmin(ww))]
    return {"od_mm": round(rr.max() * 2000, 3), "bore_mm": round(rr.min() * 2000, 3), "width_mm": round((hh.max() - hh.min()) * 1000, 3),
            "center_offset_mm": round(float(np.linalg.norm(cc - C)) * 1000, 3),
            "axis_angle_to_original_deg": round(math.degrees(math.acos(min(1.0, abs(float(ax @ A))))), 4)}


neigh = ["Engine_IntakeGuard_L", "Engine_IntakeLip_L", "Engine_IntakeDuct_L", "Engine_UpperCover_L", "Engine_LowerCover_L",
         "Engine_MotorHousing_L", "Engine_Shaft_L", "Engine_Fan_L", "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L"]
checks = {
    "original_bearing": {k: round(v * 1000, 3) for k, v in BEARING.items()},
    "BearingWorn": bearing_measure(bearing_worn),
    "BearingNew": bearing_measure(bearing_new),
    "intake": {**{k: round(v, 2) for k, v in INTAKE.items()}, "guard_bar_coverage": round(BAR_FRACTION, 3), "fiber_strands": fiber_count, "guard_dust_faces": GUARD_DUST_PATCHES},
    "label": {"size_mm": [LABEL_W / MM, LABEL_H / MM], "center_radius_from_axis_mm": LR, "direction_deg": LDEG,
              "surface_spread_mm": round(LSPREAD / MM, 3), "max_normal_tilt_deg": round(LTILT, 2)},
    "overlaps": {},
}
for ob in [bearing_worn, chips, bearing_new, clog_root, fibers, guard_dust, rim, label]:
    checks["overlaps"][ob.name] = {n: overlap_count(ob, R[n]) for n in neigh if n in R}
# 地面：新轴承与原轴承原位重合（应当重合，用来核对对位）
checks["overlaps"]["BearingNew_vs_original_bearing_world_bbox_mm"] = {
    "original": [round(x * 1000, 2) for x in (world_verts(R["Engine_BearingTop_L"]).max(0) - world_verts(R["Engine_BearingTop_L"]).min(0))],
    "new": [round(x * 1000, 2) for x in (world_verts(bearing_new).max(0) - world_verts(bearing_new).min(0))],
}

# ---------------------------------------------------------------------------
# 5. 统计、材质清单、导出
# ---------------------------------------------------------------------------
stats = {"groups": {}, "objects": {}, "textures": {n: list(i.size) for n, i in images.items()}}
total = 0
for g, coll in groups.items():
    objs = [o for o in coll.objects if o.type == "MESH"]
    t = sum(hs.triangle_count(o) for o in objs)
    total += t
    stats["groups"][g] = {"triangles": t, "objects": [o.name for o in objs]}
    for o in objs:
        wv = world_verts(o)
        stats["objects"][o.name] = {"triangles": hs.triangle_count(o), "vertices": len(o.data.vertices),
                                    "size_mm_world": [round(x * 1000, 2) for x in (wv.max(0) - wv.min(0))],
                                    "origin_world": [round(x, 5) for x in o.matrix_world.translation],
                                    "material": o.material_slots[0].material.name if o.material_slots else None}
stats["total_triangles"] = total
stats["reference_original_triangles"] = {n: len(R[n].data.polygons) * 2 for n in ["Engine_BearingTop_L"]}
with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
    json.dump(stats, f, ensure_ascii=False, indent=2)
with open(os.path.join(HERE, "checks.json"), "w", encoding="utf-8") as f:
    json.dump(checks, f, ensure_ascii=False, indent=2)

manifest = {"materials": []}
for name, spec in MAT_SPECS.items():
    manifest["materials"].append({
        "name": name, "shader": "Universal Render Pipeline/Lit",
        "baseMap": (spec.get("base", "") + ".png") if spec.get("base") else "",
        "baseColor": spec.get("tint", spec.get("color", "#FFFFFF")),
        "metallicGlossMap": (spec.get("ms", "") + ".png") if spec.get("ms") else "",
        "smoothnessSource": "Metallic Alpha" if spec.get("ms") else "",
        "metallic": spec.get("metallic", 1.0 if spec.get("ms") else 0.0),
        "smoothness": round(1 - spec.get("roughness", 0.5), 3),
        "normalMap": (spec.get("normal", "") + ".png") if spec.get("normal") else "",
        "specularHighlights": spec.get("specular", True),
        "environmentReflections": spec.get("env_reflections", True),
    })
with open(os.path.join(HERE, "materials.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, ensure_ascii=False, indent=2)

# 导出：每组一个 FBX，带层级（子对象跟着父对象），只导出本组对象
for g, coll in groups.items():
    for o in bpy.context.scene.objects:
        o.select_set(False)
    objs = [o for o in coll.objects if o.type == "MESH"]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT, f"{PREFIX}_{g}.fbx"), use_selection=True, object_types={"MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="OFF", apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, add_leaf_bones=False,
                             bake_anim=False, path_mode="STRIP")

print("BUILD_OK", json.dumps({"total_triangles": total}))

# ---------------------------------------------------------------------------
# 6. 对比渲染
# ---------------------------------------------------------------------------
if not NORENDER:
    exec(open(os.path.join(HERE, "render_unit07_fault_kit.py"), encoding="utf-8").read())

# ---------------------------------------------------------------------------
# 7. 保存源文件：去掉只读参考（RobotV4 的网格不进本资产包的 .blend），重新运行脚本会重新导入参考
# ---------------------------------------------------------------------------
for o in list(ref.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.data.collections.remove(ref)
for c in [c for c in bpy.data.collections if c.name.startswith("_Bench") or c.name.startswith("_Render")]:
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(c)
try:
    bpy.ops.outliner.orphans_purge(do_recursive=True)
except Exception as e:
    print("orphans_purge skipped:", e)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Unit07FaultKit.blend"), relative_remap=True)
print("SAVE_OK")
