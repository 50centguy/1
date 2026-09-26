"""
工人义手 · lookdev v2（地下义体诊所）
由 Claude 辅助编写。build_workerhand.py 在 --look=v2（默认）时调用本模块；--look=v1 复现第一阶段的外观，用于同机位前后对比。

方向：
- 外壳 = 企业租赁品：压暗、去饱和、降低光泽的车队漆；租赁资产条码牌、“PROPERTY OF NSP PORT · LEASED EQUIPMENT” 标识条、
  螺丝 B 上的防拆漆点。整洁、统一、被管理的感觉。
- 内部 = 超负荷使用 + 非正规维修：电机过热发黑、舱底烟熏与油渍、齿轮糊着黑油；电机供电线有一段缠着电工胶布的旧补接
  （非原厂黄线）；腱绳用扎带勒住、有断丝。没有任何跨接保险丝或限力器的旁路；控制板和限力器保持原厂
  （与案件文字一致：致伤主因是雇主远程关闭软件限力）。
- 手套、工具：脏污、磨旧、有胶布修补；不再是新品。
不使用霓虹或大量发光条；唯一的发光仍是限力器红灯与控制板绿灯（原有的状态指示）。
"""

import json
import math
import os
import random

import bmesh
import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

import br_hardsurface as hs

ENAMEL_TEX = "T_Hand_EnamelGrime.png"
BAY_TEX = "T_Hand_Bay.png"
GLOVE_TEX = "T_Glove_Grime.png"
TOOL_TEX = "T_Tool_Grime.png"

UV_LEASE_STRIP = (0.5, 0.0, 0.5, 0.0625)       # 512×64
UV_ASSET_TAG = (0.5, 0.0625, 0.25, 0.1875)     # 256×192

RNG = random.Random(4417)


# ---------------------------------------------------------------------------
# 材质
# ---------------------------------------------------------------------------

def material_overrides():
    """v2 的材质：在 v1 的同名材质上改颜色 / 粗糙度 / 贴图，另加几种新材质。"""
    return {
        # 车队漆：灰绿、去饱和、哑光（原来 #5E7F72、粗糙度 0.5 在棚灯下像薄荷糖）
        "M_Hand_Enamel": dict(color="#56655F", metallic=0.05, roughness=0.72, base_map=ENAMEL_TEX),
        "M_Hand_Cream": dict(color="#A9A28E", metallic=0.0, roughness=0.74, base_map=ENAMEL_TEX),
        "M_Hand_Steel": dict(color="#8C8E8B", metallic=0.75, roughness=0.46, base_map=TOOL_TEX),
        "M_Hand_WornSteel": dict(color="#5C554C", metallic=0.55, roughness=0.72, base_map=TOOL_TEX),
        "M_Hand_Brass": dict(color="#8A7046", metallic=0.7, roughness=0.55, base_map=TOOL_TEX),
        "M_Hand_Rubber": dict(color="#1F2021", metallic=0.0, roughness=0.88, base_map=ENAMEL_TEX),
        "M_Hand_Dark": dict(color="#2A2D2E", metallic=0.25, roughness=0.62, base_map=ENAMEL_TEX),
        "M_Hand_Cable": dict(color="#4A3829", metallic=0.0, roughness=0.9, base_map=GLOVE_TEX),
        "M_Hand_Accent": dict(color="#A57D22", metallic=0.0, roughness=0.65),
        "M_Tool_Bakelite": dict(color="#35241A", metallic=0.0, roughness=0.55, base_map=TOOL_TEX),
        "M_Tool_Orange": dict(color="#86502F", metallic=0.0, roughness=0.68, base_map=TOOL_TEX),
        "M_Tool_Red": dict(color="#7A2B24", metallic=0.0, roughness=0.62, base_map=TOOL_TEX),
        "M_Tool_Case": dict(color="#6A6656", metallic=0.0, roughness=0.72, base_map=TOOL_TEX),
        "M_Glove": dict(color="#6A6450", metallic=0.0, roughness=0.92, base_map=GLOVE_TEX),
        "M_Glove_Palm": dict(color="#34302A", metallic=0.0, roughness=0.9, base_map=GLOVE_TEX),
        # 新材质
        "M_Hand_BayLiner": dict(color="#FFFFFF", metallic=0.3, roughness=0.62, base_map=BAY_TEX),
        "M_Hand_Heat": dict(color="#4A3C37", metallic=0.55, roughness=0.5, base_map=TOOL_TEX),
        "M_Hand_Grease": dict(color="#15120F", metallic=0.0, roughness=0.28),
        # 铜线金属度不能太高：暗环境下高金属度只反射黑色，线就“消失”了
        "M_Hand_Copper": dict(color="#C07A48", metallic=0.55, roughness=0.42),
        "M_Hand_Tape": dict(color="#161616", metallic=0.0, roughness=0.55),
        "M_Hand_ZipTie": dict(color="#B7B0A0", metallic=0.0, roughness=0.5),
        "M_Hand_WireYellow": dict(color="#9C8428", metallic=0.0, roughness=0.55),
        "M_Hand_TamperPaint": dict(color="#C2561E", metallic=0.0, roughness=0.45),
        "M_Glove_Patch": dict(color="#5C4A33", metallic=0.0, roughness=0.8, base_map=GLOVE_TEX),
        "M_Clinic_Bench": dict(color="#4A4B48", metallic=0.35, roughness=0.6, base_map=TOOL_TEX),
        "M_Clinic_Mat": dict(color="#2E3632", metallic=0.0, roughness=0.85, base_map=ENAMEL_TEX),
        "M_Clinic_Wall": dict(color="#2B2A27", metallic=0.0, roughness=0.9, base_map=ENAMEL_TEX),
        "M_Clinic_Lamp": dict(color="#3E4A45", metallic=0.3, roughness=0.55, base_map=ENAMEL_TEX),
        "M_Clinic_Mug": dict(color="#8E8A80", metallic=0.0, roughness=0.5, base_map=ENAMEL_TEX),
        "M_Clinic_Bulb": dict(color="#FFE2B8", metallic=0.0, roughness=0.3, emission_color="#FFD9A8", emission_strength=8.0),
    }


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def _blotches(size, rng, sigma, lo, hi):
    n = hs.periodic_noise(size, sigma, rng)
    return lo + (hi - lo) * n


def make_enamel_grime(size=1024):
    """车队漆：比 v1 更脏——手汗油污的大块暗斑、细划痕、少量漆面崩点。"""
    rng = np.random.default_rng(71)
    base = hs.make_grime_texture(size, seed=73, scratch_count=700)[..., 0]
    # 大块污渍幅度要小：幅度大了会像迷彩而不是脏
    dirt = _blotches(size, rng, 45, 0.86, 1.0)
    mid = _blotches(size, rng, 8, 0.94, 1.0)
    v = base * dirt * mid
    chips = hs.periodic_noise(size, 1.2, rng)
    v = np.where(chips > 0.955, np.minimum(1.0, v + 0.2), v)          # 崩漆露底的小亮点
    v = np.clip(v, 0, 1)
    return np.stack([v, v * 0.99, v * 0.96], axis=-1)


def make_tool_grime(size=1024):
    """工具 / 金属件：更粗的磨痕、手握处发暗、点状锈斑。"""
    rng = np.random.default_rng(83)
    base = hs.make_grime_texture(size, seed=85, scratch_count=1100)[..., 0]
    wear = _blotches(size, rng, 25, 0.8, 1.0)
    rust = hs.periodic_noise(size, 3.0, rng)
    v = np.clip(base * wear, 0, 1)
    rgb = np.stack([v, v * 0.97, v * 0.93], axis=-1)
    mask = rust > 0.86
    rgb[mask] = rgb[mask] * np.array([0.78, 0.55, 0.38])                # 锈斑偏红褐
    return np.clip(rgb, 0, 1)


def make_glove_grime(size=1024):
    """帆布手套：细密的布纹 + 油污 + 指尖磨黑。"""
    rng = np.random.default_rng(91)
    yy, xx = np.mgrid[0:size, 0:size]
    weave = 0.9 + 0.06 * np.sin(xx * 2 * math.pi / 6) * np.sin(yy * 2 * math.pi / 6)
    stains = _blotches(size, rng, 35, 0.76, 1.0)
    fine = _blotches(size, rng, 2.0, 0.9, 1.0)
    v = np.clip(weave * stains * fine, 0, 1)
    return np.stack([v, v * 0.97, v * 0.9], axis=-1)


def bay_uv(x, z, B):
    return (x - B.BAY_X0) / (B.BAY_X1 - B.BAY_X0), (z + B.BAY_Z) / (2 * B.BAY_Z)


def make_bay_texture(B, W=1024, H=292):
    """舱底衬板：深枪灰金属，电机周围烟熏、过热变色，齿轮下方油渍往下流，靠近保险丝座一块焦痕。"""
    rng = np.random.default_rng(97)
    img = np.zeros((H, W, 3))
    grime = hs.make_grime_texture(1024, seed=99, scratch_count=900)[:H, :W, 0]
    base = 0.34 * grime
    img[:] = np.stack([base, base * 1.0, base * 1.02], axis=-1)
    yy, xx = np.mgrid[0:H, 0:W]

    def at(x, z):
        u, v = bay_uv(x, z, B)
        return u * W, (1 - v) * H

    # 电机与保险丝座周围的烟熏（径向变暗）+ 过热的褐 / 蓝色氧化环
    for (x, z, r, k) in ((-0.064, -0.012, 150, 0.75), (-0.041, -0.016, 70, 0.8)):
        cx, cy = at(x, z)
        d = np.sqrt((xx - cx) ** 2 + ((yy - cy) * 1.0) ** 2) / r
        soot = np.clip(1 - d, 0, 1) ** 1.5 * k
        img *= (1 - soot)[..., None]
        ring = np.exp(-((d - 0.7) ** 2) / 0.02) * 0.08
        img += ring[..., None] * np.array([0.9, 0.55, 0.3])
    # 齿轮下方的油渍：几道往下流的暗色竖条
    for i in range(7):
        x = -0.07 + i * 0.006 + RNG.uniform(-0.002, 0.002)
        cx, cy = at(x, 0.002)
        length = RNG.uniform(40, 120)
        w = RNG.uniform(2.5, 6)
        m = (np.abs(xx - cx) < w) & (yy > cy) & (yy < cy + length)
        fade = np.clip(1 - (yy - cy) / length, 0, 1)
        img[m] *= (1 - 0.55 * fade[m])[..., None]
    # 散落的油点
    for _ in range(40):
        cx, cy = at(RNG.uniform(-0.09, -0.02), RNG.uniform(-0.02, 0.02))
        hs.disc(img, int(cx), int(cy), RNG.randint(2, 6), img[int(np.clip(cy, 0, H - 1)), int(np.clip(cx, 0, W - 1))] * 0.4)
    return np.clip(img, 0, 1)


def extend_decals(img):
    """在 v1 贴图集的空余区域加上租赁标识条和资产条码牌。"""
    W = H = img.shape[0]
    ink = np.array([0.1, 0.09, 0.08])
    x0, y0, x1, y1 = _px(UV_LEASE_STRIP, W, H)
    img[y0:y1, x0:x1] = [0.55, 0.43, 0.18]                                 # 企业色：暗赭
    _text_c(img, "PROPERTY OF NSP PORT - LEASED", (x0, y0, x1, y1), 2, ink)
    x0, y0, x1, y1 = _px(UV_ASSET_TAG, W, H)
    img[y0:y1, x0:x1] = [0.82, 0.8, 0.72]
    hs.rect_outline(img, x0 + 4, y0 + 4, x1 - 4, y1 - 4, 2, ink)
    hs.draw_text(img, "NSP ASSET", x0 + 18, y0 + 16, 3, ink)
    prng = random.Random(417)
    bx = x0 + 18
    while bx < x1 - 20:                                                  # 条码
        w = prng.choice((2, 2, 3, 5))
        if prng.random() < 0.62:
            img[y0 + 50:y0 + 128, bx:bx + w] = ink
        bx += w + prng.choice((2, 3))
    hs.draw_text(img, "0417-L", x0 + 18, y0 + 140, 4, ink)
    return img


def _px(region, w, h):
    u0, v0, us, vs = region
    return int(u0 * w), int((1 - v0 - vs) * h), int((u0 + us) * w), int((1 - v0) * h)


def _text_c(img, text, box, scale, color):
    x0, y0, x1, y1 = box
    tw = len(text) * 6 * scale - scale
    hs.draw_text(img, text, x0 + (x1 - x0 - tw) // 2, y0 + (y1 - y0 - 7 * scale) // 2, scale, color)


def build_images(B):
    d = B.TEX_DIR
    return {
        ENAMEL_TEX: hs.save_image(ENAMEL_TEX, make_enamel_grime(), d),
        TOOL_TEX: hs.save_image(TOOL_TEX, make_tool_grime(), d),
        GLOVE_TEX: hs.save_image(GLOVE_TEX, make_glove_grime(), d),
        BAY_TEX: hs.save_image(BAY_TEX, make_bay_texture(B), d),
    }


# ---------------------------------------------------------------------------
# 几何补充（总共几百个三角形）
# ---------------------------------------------------------------------------

def _set_material(name, mat):
    o = bpy.data.objects[name]
    o.data.materials.clear()
    o.data.materials.append(hs.MATS[mat])


def apply(B):
    """在 v1 模型上叠加 v2 的外观细节。B = build_workerhand 模块。"""
    # 舱底衬板换成专用贴图，按舱口平面投影 UV
    liner = bpy.data.objects["Hand_BayLiner"]
    bm = bmesh.new()
    bm.from_mesh(liner.data)
    hs.planar_uv(bm, ((B.BAY_X0 + B.BAY_X1) / 2, 0, 0), (1, 0, 0), (0, 0, 1), B.BAY_X1 - B.BAY_X0, 2 * B.BAY_Z)
    bm.to_mesh(liner.data)
    bm.free()
    _set_material("Hand_BayLiner", "M_Hand_BayLiner")
    _set_material("Hand_DriveMotor", "M_Hand_Heat")

    # --- 外壳：企业租赁品 ---
    y = B.COVER_FRONT - 0.0003
    strip = hs.bm_box((0.07, 0.0004, 0.0087), (-0.09, y, -0.0175))
    B.decal_uv(strip, UV_LEASE_STRIP, (1, 0, 0), (0, 0, 1), (-0.09, 0, -0.0175), 0.07, 0.0087)
    B.obj("Hand_LeaseStrip", strip, "Shell", "M_Hand_Decal", uv=False)
    tag = hs.bm_box((0.0115, 0.0004, 0.0086), (-0.1535, B.FRONT_Y - 0.0004, 0.004))
    B.decal_uv(tag, UV_ASSET_TAG, (1, 0, 0), (0, 0, 1), (-0.1535, 0, 0.004), 0.0115, 0.0086)
    B.obj("Hand_AssetTag", tag, "Body", "M_Hand_Decal", uv=False)
    # 螺丝 B 的防拆漆：一点压在螺丝头边缘（随螺丝走），一段在盖板上（留在原处，卸螺丝后会看到裂开的一半）
    sx, sz = B.SCREW_B
    dab = B.sphere(0.0017, (sx + 0.0036, B.COVER_FRONT - 0.001, sz + 0.0012), (1.2, 0.35, 0.8), 12, 6)
    B.obj("Hand_FastenerB_TamperPaint", dab, "FastenerB", "M_Hand_TamperPaint")
    dab = B.sphere(0.0016, (sx + 0.0058, B.COVER_FRONT - 0.0003, sz + 0.0018), (1.3, 0.3, 0.8), 12, 6)
    B.obj("Hand_CoverTamperPaint", dab, "Shell", "M_Hand_TamperPaint")

    # --- 内部：超负荷 + 非正规维修 ---
    by = B.BAY_BACK
    # 电机供电线的旧补接（以前的非正规维修痕迹，不是旁路）：原厂黑线在电机前方被剪断过，
    # 中间接了一段非原厂黄线，两个接头都缠着电工胶布。只是修补供电线，不跨接任何保险丝或限力器。
    # 致伤主因仍是雇主远程关闭软件限力；这段补接只说明这只手长期超负荷、在正规渠道之外修过。
    fy = by - 0.0138
    j0, j1, jz = (-0.0485, fy, -0.0185), (-0.036, fy, -0.019), -0.0185
    pts = [(-0.0515, by - 0.009, -0.0135), (-0.0525, by - 0.0115, -0.0165), j0]
    B.obj("Hand_PowerLeadOriginal", hs.bm_tube(pts, [0.0008] * len(pts), segs=8), "DriveStatic", "M_Hand_Dark", sharp=70)
    pts = [j0, (-0.044, fy - 0.0012, jz - 0.0012), (-0.04, fy - 0.0012, jz - 0.0014), j1]
    B.obj("Hand_PowerSpliceWire", hs.bm_tube(pts, [0.00085] * len(pts), segs=8), "DriveStatic", "M_Hand_WireYellow", sharp=70)
    pts = [j1, (-0.031, fy + 0.002, jz - 0.0005), (-0.029, by - 0.004, jz)]
    B.obj("Hand_PowerLeadOriginal2", hs.bm_tube(pts, [0.0008] * len(pts), segs=8), "DriveStatic", "M_Hand_Dark", sharp=70)
    tape = bmesh.new()
    for j, d in ((j0, (1, -0.1, -0.2)), (j1, (1, 0.05, 0.1))):
        B.merge(tape, B.cyl(0.0017, 0.0055, 14, d, j))                   # 胶布缠绕：略鼓、比线粗一圈
        B.merge(tape, B.cyl(0.0015, 0.0075, 14, d, j))
    B.obj("Hand_PowerSpliceTape", tape, "DriveStatic", "M_Hand_Tape", sharp=50)
    # 腱绳：扎带勒住 + 上面一根断了几股
    zt = B.rounded_box((0.0022, 0.0045, 0.0115), (-0.011, by - 0.0075, 0.0125), (1, 0, 0), 0.0006)
    B.obj("Hand_TendonZipTie", zt, "DriveStatic", "M_Hand_ZipTie")
    frays = bmesh.new()
    for i in range(4):
        a = math.radians(-40 + i * 25)
        p0 = Vector((-0.004, by - 0.0075, 0.0165))
        p1 = p0 + Vector((math.cos(a) * 0.0055, -0.0022 - 0.001 * (i % 2), -0.001 + math.sin(a) * 0.003))
        B.merge(frays, hs.bm_tube([p0, (p0 + p1) / 2 + Vector((0, -0.0006, 0.0008)), p1], [0.0003] * 3, segs=5))
    B.obj("Hand_TendonFrays", frays, "DriveStatic", "M_Hand_Steel", sharp=80)
    # 齿轮与衬板上的黑油
    grease = bmesh.new()
    # 压扁的不规则油斑（不要做成小黑球）
    for (x, z, r) in ((-0.058, -0.004, 0.0042), (-0.068, 0.012, 0.0032), (-0.047, 0.0005, 0.0026), (-0.061, 0.0045, 0.0022)):
        B.merge(grease, B.sphere(r, (x, by - 0.0011, z), (1.6, 0.06, 1.0), 12, 6))
    for (x, z, r) in ((-0.066, 0.0, 0.0028), (-0.0455, 0.0075, 0.002)):
        B.merge(grease, B.sphere(r, (x, by - 0.0092, z), (1.5, 0.12, 1.1), 10, 6))
    B.obj("Hand_Grease", grease, "DriveWorn", "M_Hand_Grease", sharp=80)

    # --- 工具与手套：磨旧、胶布修补 ---
    bm = bmesh.new()
    for z in (0.1, 0.104, 0.108):
        B.merge(bm, B.cyl(0.01405, 0.0038, 24, (0, 0, 1), (0, 0, z)))
    B.obj("Tool_Screwdriver_TapeWrap", bm, "Tool_Screwdriver", "M_Hand_Tape", sharp=40)
    B.obj("Tool_Probe_TapeWrap", B.cyl(0.0051, 0.008, 16, (0, 0, 1), (0, 0, 0.07)), "Tool_Probe", "M_Hand_Tape", sharp=40)
    B.obj("Tool_Tester_TapeRepair", hs.bm_box((0.034, 0.0006, 0.012), (0.012, -0.0132, 0.094)), "Tool_TesterBody", "M_Hand_Tape")
    for g, r in (("Glove", 0.0136), ("GlovePinch", 0.0055)):
        palm_x = -(r + 0.013) - 0.004
        patch = B.rounded_box((0.0024, 0.024, 0.026), (palm_x - 0.0118, 0.008, 0.008), (1, 0, 0), 0.004, 2)
        B.obj(f"{g}_Patch", patch, g, "M_Glove_Patch", sharp=50)


# ---------------------------------------------------------------------------
# 诊所工作台场景
# ---------------------------------------------------------------------------

def build_clinic(B, scene):
    coll = bpy.data.collections.new("_Clinic")
    scene.collection.children.link(coll)
    floor = B.TRAY_Z - 0.003

    def mk(name, bm, mat, smooth=True):
        o = hs.make_object(name, bm, coll, mat, smooth=smooth)
        return o

    # 工作台面：磨旧的钢板 + 橡胶垫
    mk("Clinic_Bench", hs.bm_box((1.4, 0.8, 0.04), (0.0, 0.15, floor - 0.02)), "M_Clinic_Bench")
    mk("Clinic_Mat", hs.bm_box((0.46, 0.3, 0.003), (-0.03, -0.02, floor + 0.0015)), "M_Clinic_Mat")
    # 墙：水泥墙 + 洞洞板
    mk("Clinic_Wall", hs.bm_box((1.6, 0.04, 0.9), (0.0, 0.52, floor + 0.43)), "M_Clinic_Wall")
    mk("Clinic_Pegboard", hs.bm_box((0.9, 0.012, 0.42), (0.05, 0.49, floor + 0.36)), "M_Hand_Dark")
    # 墙上挂的线缆
    for i, x in enumerate((-0.25, -0.18, 0.3)):
        pts = [(x, 0.48, floor + 0.52), (x + 0.03, 0.47, floor + 0.35 - i * 0.03), (x + 0.07, 0.46, floor + 0.3), (x + 0.1, 0.48, floor + 0.46)]
        mk(f"Clinic_HangingCable{i}", hs.bm_tube(pts, [0.004] * 4, segs=8), "M_Hand_Rubber")
    # 台灯：底座、两节臂、灯罩（灯罩里是暖白的聚光灯）
    base = Vector((-0.34, 0.2, floor))
    mk("Clinic_LampBase", B.cyl(0.05, 0.012, 24, (0, 0, 1), base + Vector((0, 0, 0.006))), "M_Clinic_Lamp")
    # 灯头拉到技师这一侧，斜着照进打开的维修舱（从正上方照，舱内会被外壳顶沿的阴影挡住）
    j1 = base + Vector((0.02, -0.06, 0.3))
    head = Vector((-0.2, -0.26, 0.2))
    mk("Clinic_LampArm1", hs.bm_tube([base + Vector((0, 0, 0.01)), j1], [0.006, 0.006], segs=10), "M_Hand_Steel")
    mk("Clinic_LampArm2", hs.bm_tube([j1, head], [0.005, 0.005], segs=10), "M_Hand_Steel")
    target = Vector((-0.05, -0.015, -0.004))
    d = (target - head).normalized()
    shade = B.cyl(0.028, 0.07, 24, d, head + d * 0.03, r2=0.055)
    mk("Clinic_LampShade", shade, "M_Clinic_Lamp")
    mk("Clinic_LampBulb", B.sphere(0.018, head + d * 0.045, (1, 1, 1), 12, 8), "M_Clinic_Bulb")
    spot = bpy.data.lights.new("Clinic_LampSpot", type="SPOT")
    spot.energy = 12.0
    spot.spot_size = math.radians(62)
    spot.spot_blend = 0.55
    spot.shadow_soft_size = 0.02
    spot.color = (1.0, 0.86, 0.7)
    so = bpy.data.objects.new("Clinic_LampSpot", spot)
    so.location = head + d * 0.072          # 放在灯泡球体和灯罩口之外，否则光被灯泡挡住
    so.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    coll.objects.link(so)
    # 很弱的冷色环境补光（窗外 / 走廊的光），让暗部不至于死黑
    fill = bpy.data.lights.new("Clinic_Fill", type="AREA")
    fill.energy = 5.0
    fill.size = 0.8
    fill.color = (0.72, 0.8, 0.95)
    fo = bpy.data.objects.new("Clinic_Fill", fill)
    fo.location = (0.6, -0.6, 0.5)
    fo.rotation_euler = (Vector((0, 0, 0)) - Vector(fo.location)).to_track_quat("-Z", "Y").to_euler()
    coll.objects.link(fo)
    # 台面杂物：检测仪主机、螺丝刀和撬片（复用工具网格）、胶布卷、马克杯、散落的螺丝
    def place_copy(group, loc, rot):
        for o in B.G[group].objects:
            c = o.copy()
            c.hide_render = False
            c.hide_viewport = False
            c.rotation_mode = "XYZ"
            c.location = Vector(loc)
            c.rotation_euler = rot
            coll.objects.link(c)
    place_copy("Tool_TesterBody", (0.21, 0.07, floor), (0, 0, math.radians(-18)))
    place_copy("Tool_Screwdriver", (0.14, -0.2, floor + 0.0138), (0, math.radians(90), math.radians(15)))
    place_copy("Tool_Pry", (0.16, -0.25, floor + 0.0066), (0, math.radians(90), math.radians(-8)))
    ring = B.cyl(0.03, 0.02, 24, (0, 0, 1), (0.36, -0.14, floor + 0.01))
    mk("Clinic_TapeRoll", ring, "M_Hand_Tape")
    mk("Clinic_TapeCore", B.cyl(0.016, 0.0205, 20, (0, 0, 1), (0.36, -0.14, floor + 0.01)), "M_Clinic_Mug")
    mug = B.cyl(0.03, 0.085, 24, (0, 0, 1), (-0.36, -0.12, floor + 0.0425))
    mk("Clinic_Mug", mug, "M_Clinic_Mug")
    pts = [(-0.33, -0.12, floor + 0.07), (-0.315, -0.12, floor + 0.062), (-0.315, -0.12, floor + 0.03), (-0.33, -0.12, floor + 0.022)]
    mk("Clinic_MugHandle", hs.bm_tube(pts, [0.004] * 4, segs=8), "M_Clinic_Mug")
    bm = bmesh.new()
    for i in range(7):
        x, yy = 0.24 + RNG.uniform(-0.03, 0.03), -0.28 + RNG.uniform(-0.02, 0.02)
        B.merge(bm, B.cyl(0.0045, 0.002, 12, (RNG.uniform(-1, 1), RNG.uniform(-1, 1), 0.2), (x, yy, floor + 0.0045)))
    mk("Clinic_LooseScrews", bm, "M_Hand_Steel")
    return coll


# ---------------------------------------------------------------------------
# 渲染、可见度测量、对比图
# ---------------------------------------------------------------------------

def _hide_collection(coll, hidden):
    for o in coll.all_objects:
        o.hide_render = hidden
        o.hide_viewport = hidden


def screen_rect(scene, cam, objs, pad=0):
    """一组物体的包围盒投影到渲染画面上的像素矩形（x0, y0, x1, y1，y 从图像顶部算起）。"""
    W, H = scene.render.resolution_x, scene.render.resolution_y
    xs, ys = [], []
    for o in objs:
        for c in o.bound_box:
            p = world_to_camera_view(scene, cam, o.matrix_world @ Vector(c))
            xs.append(p.x * W)
            ys.append((1 - p.y) * H)
    if not xs:
        return None
    return (max(0, int(min(xs)) - pad), max(0, int(min(ys)) - pad), min(W, int(max(xs)) + pad), min(H, int(max(ys)) + pad))


def load_rgb(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3]
    bpy.data.images.remove(img)
    return px


def region_stats(rgb, rect):
    x0, y0, x1, y1 = rect
    lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    inner = lum[y0:y1, x0:x1]
    m = 14
    X0, Y0, X1, Y1 = max(0, x0 - m), max(0, y0 - m), min(lum.shape[1], x1 + m), min(lum.shape[0], y1 + m)
    ring = lum[Y0:Y1, X0:X1].copy()
    ring[y0 - Y0:y1 - Y0, x0 - X0:x1 - X0] = np.nan
    return float(inner.mean() * 255), float(inner.std() * 255), float(np.nanmean(ring) * 255)


def composite(paths, labels, out_path, directory):
    imgs = [load_rgb(p)[::2, ::2] for p in paths]
    h = max(i.shape[0] for i in imgs)
    band = 44
    sheet = np.full((h + band, sum(i.shape[1] for i in imgs) + 8 * (len(imgs) - 1), 3), 0.08)
    x = 0
    for img, label in zip(imgs, labels):
        sheet[band:band + img.shape[0], x:x + img.shape[1]] = img
        hs.draw_text(sheet, label, x + 14, 10, 4, np.array([0.92, 0.9, 0.84]))
        x += img.shape[1] + 8
    hs.save_image(os.path.basename(out_path), np.clip(sheet, 0, 1), directory)


def lookdev_shots(B, scene, cam, look, prefix):
    """同机位的一组：合盖、开盖（整只手，同一机位）、舱内特写（检测后）、封条特写、工具与手套。"""
    variants = ["SealTorn", "DriveNew", "DriveTestClip", "LimiterLedRed", "BoardLedGreen"]
    opened = ["Shell", "SealIntact", "FastenerA", "FastenerB"]
    out = {}

    def shot(key, loc, target, lens, show=(), hide=()):
        B.set_group_visible(B.GROUPS, True)
        B.set_group_visible(variants, False)
        B.set_group_visible(B.TOOL_GROUPS, False)
        B.set_group_visible(show, True)
        B.set_group_visible(hide, False)
        path = os.path.join(B.SHOT_DIR, f"{prefix}_{look}_{key}.png")
        hs.render_view(scene, cam, path, loc, target, lens)
        out[key] = path

    cam_whole = ((-0.12, -0.42, 0.17), (-0.03, -0.02, -0.012), 46)
    shot("1_closed", *cam_whole)
    shot("2_open", *cam_whole, show=["LimiterLedRed"], hide=opened)
    shot("3_bay_closeup", (-0.058, -0.27, 0.1), (-0.058, -0.015, 0.0), 58, show=["LimiterLedRed"], hide=opened)
    shot("4_seal_closeup", (-0.09, -0.26, 0.08), (-0.066, -0.035, 0.004), 48)
    B.place_tools_for_lineup()
    hand = [g for g in B.GROUPS if g not in B.TOOL_GROUPS]
    shot("5_tools", (0.5, -0.62, 0.26), (0.51, -0.08, -0.05), 42, show=B.TOOL_GROUPS, hide=hand)
    B.reset_tool_transforms()
    return out


def clinic_shots(B, scene, cam, prefix, metrics):
    studio = bpy.data.collections.get("_Studio")
    clinic = build_clinic(B, scene)
    if studio:
        for o in studio.all_objects:
            if o.name != "StudioCam":
                o.hide_render = True
    world_bg = scene.world.node_tree.nodes.get("Background") if scene.world else None
    old_bg = None
    if world_bg:
        old_bg = (tuple(world_bg.inputs["Color"].default_value), world_bg.inputs["Strength"].default_value)
        world_bg.inputs["Color"].default_value = (0.012, 0.013, 0.015, 1)
        world_bg.inputs["Strength"].default_value = 1.0
    old_exp = scene.view_settings.exposure
    scene.view_settings.exposure = 0.0
    variants = ["SealTorn", "DriveNew", "DriveTestClip", "LimiterLedRed", "BoardLedGreen"]
    opened = ["Shell", "SealIntact", "FastenerA", "FastenerB"]
    loc, target, lens = (-0.06, -0.46, 0.21), (-0.005, 0.0, -0.02), 40
    results = {}
    for key, show, hide in (("clinic_closed", [], []), ("clinic_open", ["LimiterLedRed"], opened)):
        B.set_group_visible(B.GROUPS, True)
        B.set_group_visible(variants, False)
        B.set_group_visible(B.TOOL_GROUPS, False)
        B.set_group_visible(show, True)
        B.set_group_visible(hide, False)
        path = os.path.join(B.SHOT_DIR, f"{prefix}_v2_{key}.png")
        hs.render_view(scene, cam, path, loc, target, lens)
        results[key] = path
        # 可见度：关键物件在昏暗诊所光下的平均亮度、对比度（区域内亮度标准差）、与紧邻周围的亮度差
        rgb = load_rgb(path)
        checks = {
            "clinic_closed": {"seal": ["Hand_Seal"], "fastener_a": ["Hand_FastenerA_Head"], "lease_strip": ["Hand_LeaseStrip"],
                              "cover_wear": ["Hand_CoverWear"]},
            "clinic_open": {"limiter_body": ["Hand_LimiterBody"], "limiter_red_led": ["Hand_LimiterLedRed"],
                            "bypass_tag": ["Hand_LimiterBypassTag"], "power_splice": ["Hand_PowerSpliceWire", "Hand_PowerSpliceTape"],
                            "worn_gears": ["Hand_DriveWorn_Gears"], "grease": ["Hand_Grease"], "board": ["Hand_BoardPcb"]},
        }[key]
        for name, objs in checks.items():
            rect = screen_rect(scene, cam, [bpy.data.objects[n] for n in objs])
            mean, std, around = region_stats(rgb, rect)
            metrics[f"{key}.{name}"] = {"rect_px": rect, "mean_luma": round(mean, 1), "contrast_std": round(std, 1),
                                        "surround_luma": round(around, 1), "diff_vs_surround": round(mean - around, 1)}
    _hide_collection(clinic, True)
    if studio:
        for o in studio.all_objects:
            o.hide_render = False
    if world_bg and old_bg:
        world_bg.inputs["Color"].default_value = old_bg[0]
        world_bg.inputs["Strength"].default_value = old_bg[1]
    scene.view_settings.exposure = old_exp
    return results


def finish(B, prefix):
    """两种外观都渲染完后：生成前后对比图。"""
    d = B.SHOT_DIR
    pairs = [("1_closed", "COVER ON"), ("2_open", "COVER OFF"), ("3_bay_closeup", "BAY"), ("4_seal_closeup", "SEAL"), ("5_tools", "TOOLS")]
    for key, _ in pairs:
        a = os.path.join(d, f"{prefix}_v1_{key}.png")
        b = os.path.join(d, f"{prefix}_v2_{key}.png")
        if os.path.exists(a) and os.path.exists(b):
            composite([a, b], ["BEFORE", "AFTER"], os.path.join(d, f"{prefix}_compare_{key}.png"), d)
    # 开盖前后（v2）
    a, b = os.path.join(d, f"{prefix}_v2_1_closed.png"), os.path.join(d, f"{prefix}_v2_2_open.png")
    if os.path.exists(a) and os.path.exists(b):
        composite([a, b], ["COVER ON", "COVER OFF"], os.path.join(d, f"{prefix}_compare_v2_cover.png"), d)
