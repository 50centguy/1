"""
边境维修站 · 回收无人机（SalvageDrone）建模脚本
生成方式：Claude 辅助编写的 Blender Python 建模脚本（程序化建模 + 程序化贴图），未使用图生 3D 服务或外部素材。
规格：Docs/AssetSpecs/SalvageDrone_Spec.md

用法（Blender 5.2）：
    blender.exe -b --factory-startup --python build_salvagedrone.py -- [--no-render] [--no-export]

坐标：米，Z 向上，机头朝 -Y。5 个 FBX（Body/MotorFL/Mainboard/Rotor/Camera）共用世界原点。
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
UNITY_ART = os.path.join(PROJECT, "Assets", "BorderRepair", "Art", "SalvageDrone")
MODEL_DIR = os.path.join(UNITY_ART, "Models")
TEX_DIR = os.path.join(UNITY_ART, "Textures")
SHOT_DIR = os.path.join(PROJECT, "Docs", "AssetEvaluation", "Screenshots")
SHOT_PREFIX = "20260924_SalvageDrone"

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "--no-render" not in ARGS
DO_EXPORT = "--no-export" not in ARGS

RNG = random.Random(20260926)

GRIME_TEX = "T_Drone_Grime.png"
BOARD_TEX = "T_Drone_Board.png"
DAMAGE_TEX = "T_Drone_Damage.png"

MATERIALS = {
    "M_Drone_Shell": dict(color="#BFC3BD", metallic=0.0, roughness=0.55, base_map=GRIME_TEX),
    "M_Drone_Dark": dict(color="#26292C", metallic=0.0, roughness=0.6, base_map=GRIME_TEX),
    # 金属度 0.8：检查台场景没有反射探针，全金属会发黑
    "M_Drone_Metal": dict(color="#9AA1A6", metallic=0.8, roughness=0.35, base_map=GRIME_TEX),
    "M_Drone_Board": dict(color="#FFFFFF", metallic=0.0, roughness=0.5, base_map=BOARD_TEX),
    "M_Drone_Damage": dict(color="#FFFFFF", metallic=0.0, roughness=0.8, base_map=DAMAGE_TEX),
    "M_Drone_Copper": dict(color="#C8844A", metallic=0.7, roughness=0.35),
    "M_Drone_Lens": dict(color="#15222E", metallic=0.2, roughness=0.05),
    "M_Drone_Accent": dict(color="#E0561B", metallic=0.0, roughness=0.5, base_map=GRIME_TEX),
}

GROUPS = ["Body", "MotorFL", "Mainboard", "Rotor", "Camera"]

# 主要尺寸
BODY_W, BODY_L, BODY_H = 0.11, 0.17, 0.045
BODY_TOP = BODY_H / 2
TRAY_FLOOR = BODY_TOP - 0.014
MOTOR_OFF = 0.12                                   # 电机中心到机身中心在 X、Y 上的距离
ARM_Z = -0.004
ARM_T = 0.013
ARM_TOP = ARM_Z + ARM_T / 2
MOUNT_Z0 = ARM_TOP
BASE_Z0 = MOUNT_Z0 + 0.003
WIND_Z0 = BASE_Z0 + 0.006
BELL_Z0 = WIND_Z0 + 0.007
BELL_H = 0.012
ADAPT_Z0 = BELL_Z0 + BELL_H
PROP_Z = ADAPT_Z0 + 0.006 + 0.0025
SKID_Z = -0.060

# 主控板
BOARD_W, BOARD_D = 0.078, 0.068
BOARD_CY = -0.036
BOARD_Z = TRAY_FLOOR + 0.0018

# 损伤贴图集区域 (u0, v0, u_span, v_span)
SCORCH_UV = (0.02, 0.02, 0.46, 0.96)
MUD_UV = (0.52, 0.52, 0.46, 0.46)                 # 右上：干泥
CRUST_UV = (0.52, 0.02, 0.46, 0.46)               # 右下：腐蚀结晶

MOTORS = {                                          # 名称: (x, y, 桨叶角度)
    "fl": (-MOTOR_OFF, -MOTOR_OFF, 20),
    "fr": (MOTOR_OFF, -MOTOR_OFF, 70),
    "bl": (-MOTOR_OFF, MOTOR_OFF, 110),
    "br": (MOTOR_OFF, MOTOR_OFF, 35),
}


def add_box(bm, size, center, rot=None):
    m = Matrix.Translation(Vector(center)) @ ((rot.to_4x4() if rot is not None else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1.0)))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def add_sphere(bm, radius, center, scale=(1, 1, 1), u=10, v=6):
    m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((*scale, 1.0))
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius, matrix=m)


def merge_into(dst, src):
    tmp = bpy.data.meshes.new("_tmp")
    src.to_mesh(tmp)
    src.free()
    dst.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)


def region_uv(bm, region, mapper):
    """mapper(co) -> (s, t) ∈ [0,1]²，再映射到贴图集的 region 区域。"""
    u0, v0, us, vs = region
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for loop in f.loops:
            s, t = mapper(loop.vert.co)
            s, t = min(max(s, 0.0), 1.0), min(max(t, 0.0), 1.0)   # 不越出贴图集的指定区域
            loop[uv].uv = (u0 + s * us, v0 + t * vs)


def cyl_mapper(cx, cy, z0, h):
    def f(co):
        a = (math.atan2(co.y - cy, co.x - cx) / (2 * math.pi)) % 1.0
        return a, (co.z - z0) / h
    return f


def jagged_patch(bm, center, u_axis, v_axis, size_u, size_v, amount, points=14):
    """不规则的斑块（熏黑、泥痕贴片），顶点沿椭圆随机起伏。"""
    c, ua, va = Vector(center), Vector(u_axis).normalized(), Vector(v_axis).normalized()
    verts = []
    for i in range(points):
        a = 2 * math.pi * i / points
        r = 1.0 + RNG.uniform(-amount, amount)
        verts.append(bm.verts.new(c + ua * math.cos(a) * size_u / 2 * r + va * math.sin(a) * size_v / 2 * r))
    return bm.faces.new(verts)


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def make_board_texture(size=512):
    """进水腐蚀的飞控板（行 0 为顶部）。整张都显示在板面上，不在这里放贴图集小块。"""
    rng = np.random.default_rng(21)
    mask = hs.periodic_noise(size, 3, rng)
    img = np.array([0.06, 0.28, 0.16]) * (0.9 + 0.1 * mask[..., None])
    trace = np.array([0.1, 0.4, 0.2])
    pad = np.array([0.42, 0.44, 0.38])            # 氧化后发暗的焊盘
    silk = np.array([0.9, 0.91, 0.88])

    prng = random.Random(22)
    for _ in range(60):
        x0, y0 = prng.randrange(16, size - 16), prng.randrange(16, size - 16)
        x1 = min(size - 16, max(16, x0 + prng.randrange(-180, 180)))
        y1 = min(size - 16, max(16, y0 + prng.randrange(-180, 180)))
        hs.rect(img, min(x0, x1), y0, max(x0, x1) + 3, y0 + 3, trace)
        hs.rect(img, x1, min(y0, y1), x1 + 3, max(y0, y1) + 3, trace)
    for _ in range(90):
        hs.disc(img, prng.randrange(20, size - 20), prng.randrange(20, size - 20), prng.randrange(4, 8), pad)
    # 四个电调焊盘组与丝印
    for i, (x, y) in enumerate(((40, 40), (size - 110, 40), (40, size - 150), (size - 110, size - 150))):
        for k in range(3):
            hs.rect(img, x + k * 22, y, x + k * 22 + 14, y + 26, pad)
        hs.draw_text(img, f"ESC{i + 1}", x, y + 34, 2, silk)
    hs.draw_text(img, "FC-7 REV C", 150, 20, 3, silk)

    # 腐蚀：绿白色结晶斑块（低频噪声阈值）
    blot = hs.periodic_noise(size, 18, rng)
    fine = hs.periodic_noise(size, 1.5, rng)
    crust = np.clip((blot - 0.55) * 4.0, 0, 1) * (0.6 + 0.4 * fine)
    verdigris = np.array([0.62, 0.83, 0.72])
    white = np.array([0.9, 0.93, 0.9])
    tint = verdigris * (1 - fine[..., None] * 0.5) + white * (fine[..., None] * 0.5)
    img = img * (1 - crust[..., None]) + tint * crust[..., None]

    # 褐色锈斑（沿焊盘扩散）
    rust = np.clip((hs.periodic_noise(size, 8, rng) - 0.7) * 5.0, 0, 1)
    img = img * (1 - 0.8 * rust[..., None]) + np.array([0.45, 0.26, 0.12]) * 0.8 * rust[..., None]

    # 干涸的泥水线：斜向一条褐色带，带沉积颗粒
    yy, xx = np.mgrid[0:size, 0:size]
    line = np.abs((yy - 0.55 * xx) - 300) < 7
    img[line] = img[line] * 0.3 + np.array([0.5, 0.42, 0.3]) * 0.7
    below = (yy - 0.55 * xx) > 300
    img[below] = img[below] * 0.82 + np.array([0.45, 0.38, 0.28]) * 0.18
    for _ in range(400):
        x, y = prng.randrange(0, size), prng.randrange(0, size)
        if y - 0.55 * x > 300:
            img[y, x] = np.array([0.35, 0.3, 0.22])

    return np.clip(img, 0, 1)


def make_damage_texture(w=512, h=256):
    """损伤贴图集（行 0 为顶部）：
    左半 256×256 = 烧焦（近黑的积碳 + 热变色 + 裂开的焦红）；
    右上 256×128 = 干泥（对应 MUD_UV，v 0.5–1）；右下 256×128 = 腐蚀结晶（对应 CRUST_UV，v 0–0.5）。"""
    rng = np.random.default_rng(31)
    img = np.zeros((h, w, 3))
    n1 = hs.periodic_noise(256, 6, rng)
    n2 = hs.periodic_noise(256, 1.5, rng)
    # 烧焦：以近黑积碳为主，局部是钢材受热的黄褐→紫→蓝变色，少量焦红裂纹
    soot = np.array([0.035, 0.03, 0.028]) * (0.6 + 0.8 * n2[..., None])
    heat = np.clip((n1 - 0.62) * 4, 0, 1)
    heat_col = np.where(heat[..., None] < 0.5,
                        np.array([0.42, 0.3, 0.14]) * (1 - heat[..., None] * 2) + np.array([0.3, 0.16, 0.36]) * heat[..., None] * 2,
                        np.array([0.3, 0.16, 0.36]) * (2 - heat[..., None] * 2) + np.array([0.12, 0.2, 0.42]) * (heat[..., None] * 2 - 1))
    heat_mask = np.clip((n1 - 0.58) * 5, 0, 1)[..., None] * 0.75
    scorch = soot * (1 - heat_mask) + heat_col * heat_mask
    ember = np.clip((n2 - 0.9) * 10, 0, 1)[..., None]
    img[:, :256] = scorch * (1 - ember) + np.array([0.55, 0.18, 0.05]) * ember

    # 干泥（右上）
    m1 = hs.periodic_noise(128, 8, rng)
    m2 = hs.periodic_noise(128, 1.2, rng)
    mud = np.array([0.52, 0.44, 0.32]) * (0.8 + 0.3 * m1[..., None]) * (0.9 + 0.2 * m2[..., None])
    img[0:128, 256:384] = mud
    img[0:128, 384:512] = mud
    prng = random.Random(32)
    for _ in range(400):
        x, y = prng.randrange(256, 512), prng.randrange(0, 128)
        img[y, x] = np.array([0.3, 0.26, 0.2]) if prng.random() < 0.5 else np.array([0.7, 0.66, 0.58])

    # 腐蚀结晶（右下）：铜绿底，白色盐晶颗粒，夹暗色杂质
    c1 = hs.periodic_noise(128, 3, rng)
    c2 = hs.periodic_noise(128, 0.8, rng)
    crust = np.array([0.36, 0.62, 0.5]) * (0.75 + 0.4 * c1[..., None])
    salt = np.clip((c2 - 0.6) * 4, 0, 1)[..., None]
    crust = crust * (1 - salt) + np.array([0.92, 0.95, 0.9]) * salt
    img[128:256, 256:384] = crust
    img[128:256, 384:512] = crust
    for _ in range(300):
        x, y = prng.randrange(256, 512), prng.randrange(128, 256)
        img[y, x] = np.array([0.16, 0.22, 0.18])
    return np.clip(img, 0, 1)


# ---------------------------------------------------------------------------
# 部件
# ---------------------------------------------------------------------------

def bm_blade(length, root_chord, tip_chord, thick, root_pitch, tip_pitch, r0, sections=6):
    """一片沿 +X 的桨叶：截面为矩形，弦长渐细、桨距渐小。"""
    bm = bmesh.new()
    rings = []
    for i in range(sections + 1):
        t = i / sections
        x = r0 + length * t
        chord = root_chord + (tip_chord - root_chord) * t
        pitch = math.radians(root_pitch + (tip_pitch - root_pitch) * t)
        rot = Matrix.Rotation(pitch, 3, "X")
        corners = [(-chord / 2, -thick / 2), (chord / 2, -thick / 2), (chord / 2, thick / 2), (-chord / 2, thick / 2)]
        rings.append([bm.verts.new(Vector((x, 0, 0)) + rot @ Vector((0, cy, cz))) for cy, cz in corners])
    for i in range(sections):
        a, b = rings[i], rings[i + 1]
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_prop(coll, name, x, y, angle_deg):
    """两叶桨：深色桨叶 + 橙色桨尖（桨尖涂装略有磨损，桨叶完整平直）。"""
    rot = Matrix.Rotation(math.radians(angle_deg), 4, "Z")
    place = Matrix.Translation((x, y, PROP_Z)) @ rot
    body = bmesh.new()
    tips = bmesh.new()
    for side in (0, 180):
        spin = Matrix.Rotation(math.radians(side), 4, "Z")
        b = bm_blade(0.056, 0.014, 0.010, 0.0016, 14, 7, 0.006)
        bmesh.ops.transform(b, matrix=place @ spin, verts=b.verts)
        merge_into(body, b)
        t = bm_blade(0.010, 0.010, 0.008, 0.0017, 7, 6, 0.062, sections=2)
        bmesh.ops.transform(t, matrix=place @ spin, verts=t.verts)
        merge_into(tips, t)
    hub, _, cap = hs.bm_prism(hs.circle_profile(0.0065, 16), -0.0025, 0.005)
    bmesh.ops.transform(hub, matrix=Matrix.Translation((x, y, PROP_Z)), verts=hub.verts)
    merge_into(body, hub)
    blades = hs.make_object(name + "_Blades", body, coll, "M_Drone_Dark", sharp_angle=40)
    hs.make_object(name + "_Tips", tips, coll, "M_Drone_Accent", sharp_angle=40)
    return blades


def build_motor(coll, name, x, y, burnt):
    """电机：安装座、定子底座、线圈、外转子钟罩、桨座。burnt=True 时钟罩与线圈烧焦。"""
    bm, _, _ = hs.bm_prism(hs.circle_profile(0.019, 20), MOUNT_Z0, 0.003, (x, y))
    hs.make_object(name + "_Mount", bm, coll, "M_Drone_Dark", sharp_angle=30)

    bm, _, cap = hs.bm_prism(hs.circle_profile(0.017, 24), BASE_Z0, 0.006, (x, y))
    base = hs.make_object(name + "_Base", bm, coll, "M_Drone_Dark", sharp_angle=30)

    bm, _, _ = hs.bm_prism(hs.knurl_profile(0.0148, 0.0126, 12), WIND_Z0, 0.007, (x, y))
    if burnt:
        region_uv(bm, SCORCH_UV, cyl_mapper(x, y, WIND_Z0, 0.007))
        hs.make_object(name + "_Windings", bm, coll, "M_Drone_Damage", sharp_angle=20, uv=False)
    else:
        hs.make_object(name + "_Windings", bm, coll, "M_Drone_Copper", sharp_angle=20)

    bm, _, cap = hs.bm_prism(hs.circle_profile(0.0165, 24), BELL_Z0, BELL_H, (x, y))
    hs.mark_bevel_weight(bm, cap)
    if burnt:
        # 烧焦钟罩：轻微变形（受热不均）
        for v in bm.verts:
            if v.co.z > BELL_Z0 + BELL_H * 0.5:
                v.co.x += (v.co.x - x) * RNG.uniform(-0.04, 0.06)
                v.co.y += (v.co.y - y) * RNG.uniform(-0.04, 0.06)
        region_uv(bm, SCORCH_UV, cyl_mapper(x, y, BELL_Z0, BELL_H))
        bell = hs.make_object(name + "_Bell", bm, coll, "M_Drone_Damage", sharp_angle=30, uv=False)
    else:
        bell = hs.make_object(name + "_Bell", bm, coll, "M_Drone_Metal", sharp_angle=30)
    hs.add_bevel(bell, 0.0012, 2, weighted=True)

    bm, _, _ = hs.bm_prism(hs.circle_profile(0.0045, 12), ADAPT_Z0, 0.006, (x, y))
    hs.make_object(name + "_Adapter", bm, coll, "M_Drone_Metal", sharp_angle=40)


def build_body(c):
    # 机身：圆角盒，顶部下凹成舱
    bm = hs.bm_box((BODY_W, BODY_L, BODY_H))
    hs.round_edges(bm, (0, 0, 1), 0.02, 4)
    hs.inset_and_push(bm, hs.face_towards(bm, (0, 0, 1)), 0.006, 0.014)
    shell = hs.make_object("Drone_Shell", bm, c, "M_Drone_Shell", sharp_angle=35)
    hs.add_bevel(shell, 0.0015, 2, angle=40)

    # 机头传感器窗、橙色机头带、尾部散热槽
    bm = hs.bm_box((0.05, 0.004, 0.012), (0, -BODY_L / 2 - 0.0015, 0.0))
    hs.round_edges(bm, (0, 1, 0), 0.004, 2)
    hs.make_object("Drone_NoseSensor", bm, c, "M_Drone_Dark")
    bm = hs.bm_box((0.07, 0.003, 0.004), (0, -BODY_L / 2 - 0.0005, -0.014))
    hs.make_object("Drone_NoseBand", bm, c, "M_Drone_Accent")
    bm = hs.bm_box((0.006, 0.003, 0.02), (-0.024, BODY_L / 2 + 0.0008, 0.0))
    vent = hs.make_object("Drone_RearVent", bm, c, "M_Drone_Dark")
    hs.add_array(vent, (0.012, 0, 0), 5)

    # 机臂（X 形）与臂端航灯
    for key, (mx, my, _) in MOTORS.items():
        ang = math.atan2(my, mx)
        length = math.hypot(mx, my)
        rot = Matrix.Rotation(ang, 3, "Z")
        bm = bmesh.new()
        ext = length + 0.03                           # 机臂伸出电机 3 cm，托住航灯
        d = Vector((mx, my, 0)) / length
        add_box(bm, (ext, 0.016, ARM_T), (d.x * ext / 2, d.y * ext / 2, ARM_Z), rot)
        arm = hs.make_object(f"Drone_Arm_{key}", bm, c, "M_Drone_Dark")
        hs.add_bevel(arm, 0.002, 2, angle=40)
        bm = bmesh.new()
        tip = d * (length + 0.024)
        add_box(bm, (0.01, 0.014, 0.005), (tip.x, tip.y, ARM_Z - ARM_T / 2 - 0.0022), rot)
        hs.make_object(f"Drone_ArmLight_{key}", bm, c, "M_Drone_Accent")

    # 正常的三台电机与三副桨
    for key in ("fr", "bl", "br"):
        build_motor(c, f"Drone_Motor_{key}", MOTORS[key][0], MOTORS[key][1], burnt=False)
    for key in ("fl", "fr", "bl"):
        build_prop(c, f"Drone_Prop_{key}", *MOTORS[key])

    # 电池（舱内后半）
    bm = hs.bm_box((0.086, 0.072, 0.028), (0, 0.042, TRAY_FLOOR + 0.014))
    hs.round_edges(bm, (0, 0, 1), 0.006, 2)
    batt = hs.make_object("Drone_Battery", bm, c, "M_Drone_Dark")
    hs.add_bevel(batt, 0.0015, 2, angle=40)
    bm = hs.bm_box((0.06, 0.012, 0.0012), (0, 0.042, TRAY_FLOOR + 0.0286))
    hs.make_object("Drone_BatteryStripe", bm, c, "M_Drone_Accent")

    # 主控舱盖：以后缘为铰链向上翻开约 100°，内侧有水渍
    hinge_y, hinge_z = 0.0, BODY_TOP
    bm = hs.bm_box((0.088, 0.074, 0.003), (0, hinge_y - 0.037, hinge_z + 0.0015))
    hs.round_edges(bm, (0, 0, 1), 0.008, 2)
    bmesh.ops.rotate(bm, cent=(0, hinge_y, hinge_z), matrix=Matrix.Rotation(math.radians(-100), 3, "X"), verts=bm.verts)
    lid = hs.make_object("Drone_Lid", bm, c, "M_Drone_Shell")
    hs.add_bevel(lid, 0.0008, 1, angle=40)
    # 舱盖内侧（朝前）的泥水渍
    lid_rot = Matrix.Rotation(math.radians(-100), 3, "X")
    inner_center = Vector((0, hinge_y, hinge_z)) + lid_rot @ Vector((0, -0.03, -0.0002))
    bm = bmesh.new()
    jagged_patch(bm, inner_center, (1, 0, 0), lid_rot @ Vector((0, 1, 0)), 0.06, 0.03, 0.25)
    bm.normal_update()
    for f in bm.faces:
        if f.normal.y > 0:
            f.normal_flip()
    region_uv(bm, MUD_UV, lambda co: ((co.x + 0.035) / 0.07, (co.z - hinge_z) / 0.08))
    hs.make_object("Drone_LidWaterStain", bm, c, "M_Drone_Damage", smooth=False, uv=False)

    # 起落架：两根滑橇 + 四根支柱
    for sx in (-1, 1):
        pts = [(sx * 0.042, -0.078, SKID_Z + 0.008), (sx * 0.042, -0.066, SKID_Z)]
        pts += [(sx * 0.042, -0.066 + 0.132 * t / 4, SKID_Z) for t in range(1, 5)]
        pts += [(sx * 0.042, 0.078, SKID_Z + 0.008)]
        hs.make_object(f"Drone_Skid_{'L' if sx < 0 else 'R'}", hs.bm_tube(pts, [0.0035] * len(pts), segs=8), c, "M_Drone_Dark", sharp_angle=50)
        for sy in (-1, 1):
            strut = [(sx * 0.03, sy * 0.045, -BODY_H / 2 + 0.002), (sx * 0.042, sy * 0.048, SKID_Z + 0.002)]
            hs.make_object(f"Drone_Strut_{sx}_{sy}", hs.bm_tube(strut, [0.0028, 0.0028], segs=8), c, "M_Drone_Dark", sharp_angle=50)

    # 河滩干泥痕：机身两侧下部与滑橇
    bm = bmesh.new()
    for sx in (-1, 1):
        # 两块泥痕位置固定、互不重叠，并前后错开 0.1 mm（共面重叠会在 Cycles 中渲染出黑斑）
        for k, cy in enumerate((-0.036, 0.03)):
            f = jagged_patch(bm, (sx * (BODY_W / 2 + 0.0004 + 0.0001 * k), cy, -0.013), (0, 1, 0), (0, 0, 1),
                             RNG.uniform(0.028, 0.042), 0.012, 0.25)
    bm.normal_update()
    for f in bm.faces:
        cx = f.calc_center_median().x
        if f.normal.x * cx < 0:
            f.normal_flip()
    region_uv(bm, MUD_UV, lambda co: ((co.y + 0.08) / 0.16, (co.z + 0.022) / 0.02))
    hs.make_object("Drone_MudStains", bm, c, "M_Drone_Damage", smooth=False, uv=False)


def build_motor_fl(c):
    x, y, _ = MOTORS["fl"]
    build_motor(c, "Drone_Motor_fl", x, y, burnt=True)

    # 机臂上的熏黑痕（从电机向机身方向拖出）
    bm = bmesh.new()
    arm_dir = Vector((-x, -y, 0)).normalized()
    side = Vector((-arm_dir.y, arm_dir.x, 0))
    soot_c = Vector((x, y, ARM_TOP + 0.0003)) + arm_dir * 0.024
    jagged_patch(bm, soot_c, arm_dir, side, 0.05, 0.02, 0.35)
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    region_uv(bm, SCORCH_UV, lambda co: ((co.x - soot_c.x) / 0.07 + 0.5, (co.y - soot_c.y) / 0.07 + 0.5))
    hs.make_object("Drone_Motor_fl_Soot", bm, c, "M_Drone_Damage", smooth=False, uv=False)

    # 电调：热缩管熔化鼓包（挂在机臂下方）
    bm = bmesh.new()
    esc_c = Vector((x, y, ARM_Z - ARM_T / 2 - 0.004)) + arm_dir * 0.03
    add_sphere(bm, 0.008, esc_c, scale=(1.5, 0.9, 0.6), u=12, v=8)
    rot = Matrix.Rotation(math.atan2(arm_dir.y, arm_dir.x), 3, "Z")
    bmesh.ops.rotate(bm, cent=esc_c, matrix=rot, verts=bm.verts)
    for v in bm.verts:                     # 熔化后不规则
        v.co += Vector((RNG.uniform(-1, 1), RNG.uniform(-1, 1), RNG.uniform(-1, 0.4))) * 0.0012
    region_uv(bm, SCORCH_UV, lambda co: ((co.x - esc_c.x) / 0.03 + 0.5, (co.y - esc_c.y) / 0.03 + 0.5))
    hs.make_object("Drone_Motor_fl_ESC", bm, c, "M_Drone_Damage", smooth=True, uv=False, sharp_angle=80)

    # 从电调伸出的烧焦导线
    for k, off in enumerate((-0.003, 0.0, 0.003)):
        start = esc_c + arm_dir * 0.012 + side * off
        pts = [start, start + arm_dir * 0.006 + Vector((0, 0, -0.002)), start + arm_dir * 0.012 + side * off + Vector((0, 0, 0.001))]
        bm = hs.bm_tube(pts, [0.0009] * 3, segs=6)
        region_uv(bm, SCORCH_UV, lambda co: (0.3 + 0.1 * k, 0.5))
        hs.make_object(f"Drone_Motor_fl_Wire{k + 1}", bm, c, "M_Drone_Damage", sharp_angle=60, uv=False)


def build_mainboard(c):
    # 板基与带贴图的板面
    bm = hs.bm_box((BOARD_W, BOARD_D, 0.0016), (0, BOARD_CY, BOARD_Z - 0.0008))
    hs.make_object("Drone_BoardSubstrate", bm, c, "M_Drone_Dark", smooth=False)
    bm = bmesh.new()
    v = [bm.verts.new((-BOARD_W / 2, BOARD_CY - BOARD_D / 2, BOARD_Z)), bm.verts.new((BOARD_W / 2, BOARD_CY - BOARD_D / 2, BOARD_Z)),
         bm.verts.new((BOARD_W / 2, BOARD_CY + BOARD_D / 2, BOARD_Z)), bm.verts.new((-BOARD_W / 2, BOARD_CY + BOARD_D / 2, BOARD_Z))]
    f = bm.faces.new(v)
    bm.normal_update()
    if f.normal.z < 0:
        f.normal_flip()
    # 从前上方看：u 向右（+X），v 向后（+Y，远离镜头）
    hs.planar_uv(bm, (0, BOARD_CY, BOARD_Z), (1, 0, 0), (0, 1, 0), BOARD_W, BOARD_D)
    hs.make_object("Drone_BoardTop", bm, c, "M_Drone_Board", smooth=False, uv=False)

    # 元件：主控芯片、IMU、两个电容（顶部鼓起）、四个电调插座、线束
    bm = bmesh.new()
    add_box(bm, (0.014, 0.014, 0.0022), (0.012, BOARD_CY + 0.004, BOARD_Z + 0.0011))
    add_box(bm, (0.007, 0.007, 0.0016), (-0.008, BOARD_CY + 0.012, BOARD_Z + 0.0008))
    for i, (x, yy) in enumerate(((-0.03, -0.025), (0.03, -0.025), (-0.03, 0.025), (0.03, 0.025))):
        add_box(bm, (0.009, 0.006, 0.004), (x, BOARD_CY + yy, BOARD_Z + 0.002))
    hs.make_object("Drone_BoardChips", bm, c, "M_Drone_Dark")

    bm = bmesh.new()
    for x in (-0.02, -0.011):
        sub, _, cap = hs.bm_prism(hs.circle_profile(0.0034, 14), BOARD_Z, 0.0075, (x, BOARD_CY - 0.02))
        for vtx in cap.verts:                 # 电解电容顶部鼓起
            vtx.co.z += 0.0008
        merge_into(bm, sub)
    hs.make_object("Drone_BoardCaps", bm, c, "M_Drone_Metal", sharp_angle=40)

    # 腐蚀结晶：板面上鼓起的绿白色结晶块
    bm = bmesh.new()
    for (x, yy, r) in ((-0.022, -0.012, 0.004), (0.004, -0.024, 0.0032), (0.026, -0.008, 0.0036), (-0.006, 0.02, 0.003),
                       (0.02, 0.022, 0.0028), (-0.03, 0.006, 0.0026), (0.0, 0.0, 0.0034)):
        add_sphere(bm, r, (x, BOARD_CY + yy, BOARD_Z + 0.0004), scale=(RNG.uniform(1.0, 1.6), RNG.uniform(0.8, 1.3), 0.45), u=10, v=6)
    for vtx in bm.verts:                      # 结晶表面凹凸不平
        vtx.co += Vector((RNG.uniform(-1, 1), RNG.uniform(-1, 1), RNG.uniform(0, 1.5))) * 0.0006
    region_uv(bm, CRUST_UV, lambda co: ((co.x * 97.1) % 1.0, (co.y * 83.3) % 1.0))
    hs.make_object("Drone_BoardCrust", bm, c, "M_Drone_Damage", smooth=True, uv=False, sharp_angle=80)

    # 通往四个机臂的线束
    for i, (mx, my, _) in enumerate(MOTORS.values()):
        start = Vector((math.copysign(0.03, mx), BOARD_CY + math.copysign(0.025, my), BOARD_Z + 0.004))
        end = Vector((math.copysign(0.047, mx), BOARD_CY + math.copysign(0.03, my), TRAY_FLOOR + 0.002))
        mid = (start + end) / 2 + Vector((0, 0, 0.004))
        hs.make_object(f"Drone_BoardHarness{i + 1}", hs.bm_tube([start, mid, end], [0.0012] * 3, segs=6), c, "M_Drone_Dark", sharp_angle=60)


def build_rotor(c):
    x, y, ang = MOTORS["br"]
    build_prop(c, "Drone_Prop_br", x, y, ang)


def build_camera(c):
    # 机头下方的云台支臂 + U 形云台 + 相机
    bm = hs.bm_box((0.014, 0.024, 0.006), (0, -BODY_L / 2 - 0.004, -BODY_H / 2 - 0.002))
    hs.round_edges(bm, (0, 0, 1), 0.003, 2)
    boom = hs.make_object("Drone_GimbalBoom", bm, c, "M_Drone_Dark")
    hs.add_bevel(boom, 0.001, 1, angle=40)

    cy = -BODY_L / 2 - 0.013
    for sx in (-1, 1):
        pts = [(0.0, cy, -BODY_H / 2 - 0.005), (sx * 0.017, cy, -BODY_H / 2 - 0.005), (sx * 0.017, cy, -0.043)]
        hs.make_object(f"Drone_GimbalYoke_{sx}", hs.bm_tube(pts, [0.0022] * 3, segs=8), c, "M_Drone_Dark", sharp_angle=50)

    bm = hs.bm_box((0.026, 0.022, 0.018), (0, cy, -0.040))
    hs.round_edges(bm, (0, 1, 0), 0.005, 3)
    housing = hs.make_object("Drone_CameraHousing", bm, c, "M_Drone_Shell")
    hs.add_bevel(housing, 0.001, 2, angle=40)

    front_y = cy - 0.011
    ring, _, _ = hs.bm_prism(hs.circle_profile(0.0068, 20), 0.0, 0.004)
    bmesh.ops.transform(ring, matrix=Matrix.Translation((0, front_y, -0.040)) @ Matrix.Rotation(math.radians(90), 4, "X"), verts=ring.verts)
    hs.make_object("Drone_LensRing", ring, c, "M_Drone_Metal", sharp_angle=40)
    bm = bmesh.new()
    add_sphere(bm, 0.0055, (0, front_y - 0.003, -0.040), scale=(1, 0.45, 1), u=16, v=8)
    hs.make_object("Drone_Lens", bm, c, "M_Drone_Lens", sharp_angle=80)


# ---------------------------------------------------------------------------

def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(SHOT_DIR, exist_ok=True)
    scene, groups = hs.reset_scene("SalvageDrone", GROUPS, "Drone_")

    images = {
        GRIME_TEX: hs.save_image(GRIME_TEX, hs.make_grime_texture(1024, seed=27), TEX_DIR),
        BOARD_TEX: hs.save_image(BOARD_TEX, make_board_texture(), TEX_DIR),
        DAMAGE_TEX: hs.save_image(DAMAGE_TEX, make_damage_texture(), TEX_DIR),
    }
    hs.build_materials(MATERIALS, images)

    build_body(groups["Body"])
    build_motor_fl(groups["MotorFL"])
    build_mainboard(groups["Mainboard"])
    build_rotor(groups["Rotor"])
    build_camera(groups["Camera"])

    stats = hs.collect_stats(groups, images, MATERIALS.keys())
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[SalvageDrone] stats:", json.dumps(stats, ensure_ascii=False))

    if DO_EXPORT:
        hs.export_groups(groups, MODEL_DIR, "SalvageDrone")
        hs.write_material_manifest(MATERIALS, os.path.join(UNITY_ART, "SalvageDrone_Materials.json"))

    cam = hs.setup_studio(scene, SKID_Z - 0.0035, target=(0, 0, 0.0))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "SalvageDrone.blend"))

    if DO_RENDER:
        def shot(name, loc, target, lens):
            hs.render_view(scene, cam, os.path.join(SHOT_DIR, f"{SHOT_PREFIX}_{name}.png"), loc, target, lens)

        shot("01_front", (0.28, -0.62, 0.36), (0, -0.01, -0.005), 55)
        shot("02_back", (-0.3, 0.6, 0.34), (0, 0.0, -0.005), 55)
        shot("03_motor_fl_closeup", (-0.22, -0.27, 0.11), (MOTORS["fl"][0] + 0.01, MOTORS["fl"][1] + 0.01, 0.008), 85)
        shot("04_mainboard_closeup", (0.05, -0.2, 0.2), (0, BOARD_CY, BOARD_Z), 80)
        shot("05_camera_normal", (0.09, -0.3, -0.01), (0, -BODY_L / 2 - 0.015, -0.038), 90)
        shot("06_rotor_normal", (0.29, 0.02, 0.18), (MOTORS["br"][0], MOTORS["br"][1], PROP_Z), 60)
    print("[SalvageDrone] done")


if __name__ == "__main__":
    main()
