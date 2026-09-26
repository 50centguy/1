"""
边境维修站 · 通讯器（Communicator）建模脚本
生成方式：Claude 辅助编写的 Blender Python 建模脚本（程序化建模 + 程序化贴图），未使用图生 3D 服务。

用法（Blender 5.2）：
    blender.exe -b --factory-startup --python build_communicator.py -- [--no-render] [--no-export]

输出：
    ArtSource/Communicator/Communicator.blend           可继续手工修改的源文件（修改器均保留未应用）
    ArtSource/Communicator/stats.json                   面数 / 材质 / 贴图统计
    Assets/BorderRepair/Art/Communicator/Models/*.fbx   按功能拆分的 5 个 FBX（共用同一原点）
    Assets/BorderRepair/Art/Communicator/Textures/*.png
    Assets/BorderRepair/Art/Communicator/Communicator_Materials.json   Unity 端据此创建 URP 材质
    Docs/AssetEvaluation/Screenshots/20260924_Communicator_*.png       渲染截图

坐标约定：Blender 单位为米，Z 向上，物品正面朝 -Y（Blender 前视图看到的就是正面）。
"""

import bpy
import bmesh
import json
import math
import os
import random
import sys

import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY_ART = os.path.join(PROJECT, "Assets", "BorderRepair", "Art", "Communicator")
MODEL_DIR = os.path.join(UNITY_ART, "Models")
TEX_DIR = os.path.join(UNITY_ART, "Textures")
SHOT_DIR = os.path.join(PROJECT, "Docs", "AssetEvaluation", "Screenshots")
SHOT_PREFIX = "20260924_Communicator"

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
DO_RENDER = "--no-render" not in ARGS
DO_EXPORT = "--no-export" not in ARGS

RNG = random.Random(20260924)

# ---------------------------------------------------------------------------
# 材质（颜色为 sRGB，Unity 端直接使用；Blender 端转线性）
# ---------------------------------------------------------------------------

GRIME_TEX = "T_Comm_Grime.png"
SCREEN_TEX = "T_Comm_Screen.png"

MATERIALS = {
    "M_Comm_Housing": dict(color="#3A4750", metallic=0.0, roughness=0.55, base_map=GRIME_TEX, tiling=1.0),
    "M_Comm_Rubber": dict(color="#1D2023", metallic=0.0, roughness=0.85, base_map=GRIME_TEX, tiling=1.0),
    "M_Comm_Accent": dict(color="#E0561B", metallic=0.0, roughness=0.5, base_map=GRIME_TEX, tiling=1.0),
    "M_Comm_Metal": dict(color="#9AA1A6", metallic=1.0, roughness=0.38, base_map=GRIME_TEX, tiling=1.0),
    "M_Comm_Screen": dict(color="#FFFFFF", metallic=0.0, roughness=0.15, base_map=SCREEN_TEX, emission_map=SCREEN_TEX,
                          emission_color="#FFFFFF", emission_strength=1.3, tiling=1.0),
    "M_Comm_Copper": dict(color="#D98A4E", metallic=1.0, roughness=0.3, base_map=None, tiling=1.0),
}

# 每个功能组导出为一个 FBX；Unity 包装 prefab 只引用 FBX 根对象，不依赖子物体名称。
GROUPS = ["Body", "Screen", "Battery", "AntennaBroken", "AntennaRepaired"]

UV_SCALE = 0.06  # 每 6 cm 平铺一次磨损贴图

# 主要尺寸（米）
HW, HD, HH = 0.074, 0.036, 0.150          # 机身宽 / 厚 / 高
FRONT_Y = -HD / 2                          # 机身正面
RECESS_Y = FRONT_Y + 0.0025                # 正面凹槽底面
TOP_Z = HH / 2
ANT_X = 0.0165                             # 天线座 X
ANT_BASE_Z = TOP_Z + 0.008                 # 可更换天线模块的安装面


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


# ---------------------------------------------------------------------------
# 场景
# ---------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    root = bpy.data.collections.new("Communicator")
    scene.collection.children.link(root)
    groups = {}
    for g in GROUPS:
        c = bpy.data.collections.new("Comm_" + g)
        root.children.link(c)
        groups[g] = c
    return scene, groups


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def periodic_noise(size, sigma, rng):
    """用 FFT 高斯模糊白噪声，得到可平铺的平滑噪声，归一化到 0..1。"""
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    kernel = np.exp(-2 * (math.pi ** 2) * (sigma ** 2) * (fx ** 2 + fy ** 2))
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * kernel))
    out -= out.min()
    return out / max(out.max(), 1e-6)


def draw_line_wrap(img, x0, y0, x1, y1, value):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    h, w = img.shape
    for i in range(n):
        t = i / max(n - 1, 1)
        x = int(round(x0 + (x1 - x0) * t)) % w
        y = int(round(y0 + (y1 - y0) * t)) % h
        img[y, x] = max(img[y, x], value)


def make_grime_texture(size=1024):
    """中性偏暖的磨损贴图：低频污渍 + 中频颗粒 + 细划痕。Unity/Blender 中与材质色相乘。"""
    rng = np.random.default_rng(7)
    low = periodic_noise(size, 60, rng)
    mid = periodic_noise(size, 6, rng)
    fine = periodic_noise(size, 1.2, rng)
    v = 0.84 + 0.07 * (low - 0.5) + 0.05 * (mid - 0.5) + 0.03 * (fine - 0.5)
    dirt = np.clip((low - 0.62) * 3.0, 0, 1)
    v -= 0.12 * dirt

    scratches = np.zeros((size, size))
    prng = random.Random(11)
    for _ in range(420):
        x0, y0 = prng.uniform(0, size), prng.uniform(0, size)
        ang = prng.uniform(0, math.pi)
        length = prng.uniform(8, 70)
        draw_line_wrap(scratches, x0, y0, x0 + math.cos(ang) * length, y0 + math.sin(ang) * length, prng.uniform(0.06, 0.16))
    v = np.clip(v + scratches, 0, 1)

    rgb = np.stack([v * 1.0, v * 0.985, v * 0.955], axis=-1)
    return np.clip(rgb, 0, 1)


# 5×7 点阵字体（只含屏幕上用到的字符）
FONT = {
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    "C": ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    "°": ["01100", "10010", "10010", "01100", "00000", "00000", "00000"],
    " ": ["00000"] * 7,
}


def draw_text(img, text, x, y, scale, color):
    cx = x
    for ch in text:
        glyph = FONT.get(ch, FONT[" "])
        for gy, row in enumerate(glyph):
            for gx, bit in enumerate(row):
                if bit == "1":
                    img[y + gy * scale:y + (gy + 1) * scale, cx + gx * scale:cx + (gx + 1) * scale] = color
        cx += 6 * scale


def rect(img, x0, y0, x1, y1, color):
    img[y0:y1, x0:x1] = color


def rect_outline(img, x0, y0, x1, y1, t, color):
    rect(img, x0, y0, x1, y0 + t, color)
    rect(img, x0, y1 - t, x1, y1, color)
    rect(img, x0, y0, x0 + t, y1, color)
    rect(img, x1 - t, y0, x1, y1, color)


def seven_seg(img, digit, x, y, w, h, t, color):
    segs = {
        "0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc",
        "5": "afgcd", "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcdfg",
    }[digit]
    half = h // 2
    boxes = {
        "a": (x + t, y, x + w - t, y + t),
        "g": (x + t, y + half - t // 2, x + w - t, y + half + t // 2 + 1),
        "d": (x + t, y + h - t, x + w - t, y + h),
        "f": (x, y + t, x + t, y + half),
        "b": (x + w - t, y + t, x + w, y + half),
        "e": (x, y + half, x + t, y + h - t),
        "c": (x + w - t, y + half, x + w, y + h - t),
    }
    for s in segs:
        rect(img, *boxes[s], color)


def make_screen_texture(w=512, h=256):
    """单色 LCD 界面：无信号告警、频道 07、低温读数、平坦的频谱条。行 0 在顶部。"""
    bg = np.array([0.04, 0.13, 0.13])
    fg = np.array([0.45, 0.98, 0.82])
    dim = np.array([0.12, 0.32, 0.29])
    warn = np.array([1.0, 0.45, 0.18])
    img = np.tile(bg, (h, w, 1)).astype(np.float64)

    # 顶栏：信号格（带 ×）、NO SIG、电量
    for i in range(4):
        bh = 8 + i * 6
        rect(img, 20 + i * 11, 40 - bh, 28 + i * 11, 40, dim)
    for k in range(-2, 3):
        draw_line_img = [(20 + t, 12 + t + k) for t in range(0, 34)] + [(54 - t, 12 + t + k) for t in range(0, 34)]
        for (px, py) in draw_line_img:
            if 0 <= py < h:
                img[py, px] = warn
    draw_text(img, "NO SIG", 76, 14, 3, warn)
    rect_outline(img, 420, 14, 482, 40, 3, fg)
    rect(img, 482, 21, 488, 33, fg)
    rect(img, 425, 19, 466, 35, fg)
    rect(img, 0, 50, w, 53, dim)

    # 频道
    draw_text(img, "CH", 24, 78, 4, fg)
    seven_seg(img, "0", 80, 70, 52, 100, 11, fg)
    seven_seg(img, "7", 146, 70, 52, 100, 11, fg)

    # 低温读数与频率
    draw_text(img, "-38°C", 262, 76, 5, fg)
    draw_text(img, "146.52MHZ", 262, 130, 3, dim * 1.6)

    # 底部：几乎平坦的频谱（没有信号）
    rect(img, 0, 184, w, 186, dim)
    prng = random.Random(3)
    for i in range(40):
        bh = prng.randint(2, 7)
        rect(img, 20 + i * 12, 236 - bh, 28 + i * 12, 236, dim * 1.4)

    # 扫描线与暗角
    img[::3] *= 0.86
    yy, xx = np.mgrid[0:h, 0:w]
    vign = 1.0 - 0.35 * (((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) ** 1.5
    img *= np.clip(vign, 0.55, 1.0)[..., None]
    return np.clip(img, 0, 1)


def save_image(name, rgb):
    """rgb: (h, w, 3)，行 0 为图像顶部；按 sRGB 8 位 PNG 保存，并返回 Blender Image。"""
    h, w, _ = rgb.shape
    rgba = np.concatenate([rgb, np.ones((h, w, 1))], axis=-1)
    rgba = np.flipud(rgba).astype(np.float32)  # Blender 像素从左下角开始
    img = bpy.data.images.new(os.path.splitext(name)[0], width=w, height=h, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(TEX_DIR, name)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    return img


# ---------------------------------------------------------------------------
# 材质节点
# ---------------------------------------------------------------------------

def ensure_node_tree(owner):
    if getattr(owner, "node_tree", None) is None:
        try:
            owner.use_nodes = True
        except Exception:
            pass
    return owner.node_tree


def build_materials(images):
    mats = {}
    for name, spec in MATERIALS.items():
        mat = bpy.data.materials.new(name)
        tree = ensure_node_tree(mat)
        nodes, links = tree.nodes, tree.links
        bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
        tint = [srgb_to_linear(c) for c in hex_rgb(spec["color"])]
        bsdf.inputs["Metallic"].default_value = spec["metallic"]
        bsdf.inputs["Roughness"].default_value = spec["roughness"]
        if spec.get("base_map"):
            tex = nodes.new("ShaderNodeTexImage")
            tex.image = images[spec["base_map"]]
            tex.location = (-600, 200)
            mul = nodes.new("ShaderNodeVectorMath")
            mul.operation = "MULTIPLY"
            mul.inputs[1].default_value = tint
            mul.location = (-300, 200)
            links.new(tex.outputs["Color"], mul.inputs[0])
            links.new(mul.outputs["Vector"], bsdf.inputs["Base Color"])
            if spec.get("emission_map"):
                links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
                bsdf.inputs["Emission Strength"].default_value = spec.get("emission_strength", 1.0)
        else:
            bsdf.inputs["Base Color"].default_value = (*tint, 1.0)
        mats[name] = mat
    return mats


# ---------------------------------------------------------------------------
# 几何工具
# ---------------------------------------------------------------------------

def bm_box(size, center=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    return bm


def edges_parallel(bm, axis):
    axis = Vector(axis)
    out = []
    for e in bm.edges:
        d = e.verts[1].co - e.verts[0].co
        if d.length > 1e-9 and abs(d.normalized().dot(axis)) > 0.99:
            out.append(e)
    return out


def round_edges(bm, axis, offset, segments):
    """倒圆平行于 axis 的棱（例如机身四个竖棱）。"""
    edges = edges_parallel(bm, axis)
    if edges:
        bmesh.ops.bevel(bm, geom=edges, offset=offset, offset_type="OFFSET", segments=segments,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.normal_update()


def face_towards(bm, direction):
    d = Vector(direction)
    bm.normal_update()
    return max(bm.faces, key=lambda f: f.normal.dot(d) * 10 + f.calc_area())


def inset_and_push(bm, face, thickness, push):
    bmesh.ops.inset_region(bm, faces=[face], thickness=thickness, depth=0.0, use_even_offset=True)
    bmesh.ops.translate(bm, vec=face.normal * -push, verts=face.verts)
    bm.normal_update()


def rotate_verts(bm, axis, degrees, pivot, verts=None):
    m = Matrix.Rotation(math.radians(degrees), 3, axis)
    bmesh.ops.rotate(bm, cent=pivot, matrix=m, verts=verts if verts is not None else bm.verts)


def bm_prism(profile, z0, depth, center_xy=(0, 0)):
    """沿 Z 拉伸二维轮廓（逆时针），带上下封盖。"""
    bm = bmesh.new()
    cx, cy = center_xy
    bottom = [bm.verts.new((cx + x, cy + y, z0)) for x, y in profile]
    top = [bm.verts.new((cx + x, cy + y, z0 + depth)) for x, y in profile]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bm.faces.new(list(reversed(bottom)))
    cap = bm.faces.new(top)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    return bm, top, cap


def knurl_profile(r_out, r_in, teeth):
    pts = []
    for i in range(teeth * 2):
        a = math.pi * 2 * i / (teeth * 2)
        r = r_out if i % 2 == 0 else r_in
        pts.append((math.cos(a) * r, math.sin(a) * r))
    return pts


def circle_profile(r, segs):
    return [(math.cos(2 * math.pi * i / segs) * r, math.sin(2 * math.pi * i / segs) * r) for i in range(segs)]


def mark_bevel_weight(bm, cap, value=1.0):
    layer = bm.edges.layers.float.get("bevel_weight_edge") or bm.edges.layers.float.new("bevel_weight_edge")
    for e in cap.edges:
        e[layer] = value


def bm_tube(points, radii, segs=12, jag_end=0.0, rng=None):
    """沿折线生成管子（平行移动标架），可让末端参差不齐以表现断口。"""
    bm = bmesh.new()
    pts = [Vector(p) for p in points]
    rings = []
    normal = None
    for i, p in enumerate(pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == len(pts) - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        t.normalize()
        if normal is None:
            ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
            normal = (ref - t * ref.dot(t)).normalized()
        else:
            normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal)
        ring = []
        last = i == len(pts) - 1
        for k in range(segs):
            a = 2 * math.pi * k / segs
            r = radii[i]
            offset_t = 0.0
            if last and jag_end > 0 and rng is not None:
                r *= rng.uniform(0.65, 1.08)
                offset_t = -rng.uniform(0.0, jag_end)
            ring.append(bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * r + t * offset_t))
        rings.append(ring)
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def box_uv(bm, scale=UV_SCALE):
    """按面法线主轴做立方体投影 UV，所有部件的贴图密度一致。"""
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            co = loop.vert.co
            if ax == 0:
                u, v = co.y, co.z
            elif ax == 1:
                u, v = co.x, co.z
            else:
                u, v = co.x, co.y
            loop[uv].uv = (u / scale, v / scale)


def make_object(name, bm, coll, material, smooth=True, sharp_angle=35.0, uv=True):
    if uv:
        box_uv(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    me.materials.append(MATS[material])
    if smooth:
        me.shade_smooth()
        try:
            me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        except Exception:
            pass
    return obj


def add_bevel(obj, width, segments=2, angle=30.0, weighted=False, harden=True):
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.use_clamp_overlap = True
    if weighted:
        m.limit_method = "WEIGHT"
    else:
        m.limit_method = "ANGLE"
        m.angle_limit = math.radians(angle)
    m.harden_normals = harden
    return m


def add_array(obj, offset, count):
    m = obj.modifiers.new("Array", "ARRAY")
    m.count = count
    m.use_relative_offset = False
    m.use_constant_offset = True
    m.constant_offset_displace = offset
    return m


def add_mirror(obj, x=False, y=False, z=False):
    m = obj.modifiers.new("Mirror", "MIRROR")
    m.use_axis[0], m.use_axis[1], m.use_axis[2] = x, y, z
    return m


# ---------------------------------------------------------------------------
# 机身
# ---------------------------------------------------------------------------

def build_body(c):
    # 外壳：圆角机身，正面整体下凹形成一圈防撞边框
    bm = bm_box((HW, HD, HH))
    round_edges(bm, (0, 0, 1), 0.009, 4)
    inset_and_push(bm, face_towards(bm, (0, -1, 0)), 0.0045, 0.0025)
    inset_and_push(bm, face_towards(bm, (0, 1, 0)), 0.004, 0.0012)
    housing = make_object("Comm_Housing", bm, c, "M_Comm_Housing")
    add_bevel(housing, 0.0011, 2, angle=40)

    # 屏幕边框（橡胶），内部凹槽放屏幕
    bez_depth = 0.0045
    bm = bm_box((0.060, bez_depth, 0.046), (0, RECESS_Y - bez_depth / 2, 0.037))
    round_edges(bm, (0, 1, 0), 0.004, 3)
    inset_and_push(bm, face_towards(bm, (0, -1, 0)), 0.005, 0.0028)
    bezel = make_object("Comm_ScreenBezel", bm, c, "M_Comm_Rubber")
    add_bevel(bezel, 0.0007, 2, angle=40)

    # 扬声器槽
    bm = bm_box((0.034, 0.0018, 0.0016), (0, RECESS_Y - 0.0006, 0.0100))
    slot = make_object("Comm_SpeakerSlot", bm, c, "M_Comm_Rubber")
    add_bevel(slot, 0.0006, 1)
    add_array(slot, (0, 0, -0.0034), 3)

    # 功能键：左侧普通键、右侧橙色求救键
    bm = bm_box((0.025, 0.004, 0.006), (-0.0145, RECESS_Y - 0.0015, -0.0045))
    round_edges(bm, (0, 1, 0), 0.0024, 2)
    fkey = make_object("Comm_FunctionKey", bm, c, "M_Comm_Housing")
    add_bevel(fkey, 0.0007, 1)
    bm = bm_box((0.025, 0.004, 0.006), (0.0145, RECESS_Y - 0.0015, -0.0045))
    round_edges(bm, (0, 1, 0), 0.0024, 2)
    sos = make_object("Comm_SOSKey", bm, c, "M_Comm_Accent")
    add_bevel(sos, 0.0007, 1)

    # 键盘底板与 3×4 大按键（适合戴手套操作）
    bm = bm_box((0.058, 0.003, 0.056), (0, RECESS_Y - 0.0015, -0.039))
    round_edges(bm, (0, 1, 0), 0.004, 3)
    plate = make_object("Comm_KeypadPlate", bm, c, "M_Comm_Rubber")
    add_bevel(plate, 0.0006, 1)

    bm = bm_box((0.0145, 0.0035, 0.0095), (-0.018, RECESS_Y - 0.003 - 0.00175, -0.017))
    round_edges(bm, (0, 1, 0), 0.0022, 2)
    key = make_object("Comm_Key", bm, c, "M_Comm_Housing")
    add_bevel(key, 0.0008, 1)
    add_array(key, (0.018, 0, 0), 3)
    add_array(key, (0, 0, -0.0125), 4)

    # 正面螺丝
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.0017, radius2=0.0017, depth=0.0012)
    rotate_verts(bm, "X", 90, (0, 0, 0))
    bmesh.ops.translate(bm, vec=(0.0347, FRONT_Y - 0.0004, 0.050), verts=bm.verts)
    screw = make_object("Comm_FrontScrew", bm, c, "M_Comm_Metal")
    add_mirror(screw, x=True, z=True)

    # 顶部：四角防撞块（上橙下黑）
    bm = bm_box((0.0145, HD + 0.003, 0.02), (HW / 2 - 0.0058, 0, TOP_Z - 0.0075))
    round_edges(bm, (0, 1, 0), 0.0055, 3)
    bumper_top = make_object("Comm_BumperTop", bm, c, "M_Comm_Accent")
    add_bevel(bumper_top, 0.0012, 2)
    add_mirror(bumper_top, x=True)

    bm = bm_box((0.0145, HD + 0.003, 0.02), (HW / 2 - 0.0058, 0, -TOP_Z + 0.0075))
    round_edges(bm, (0, 1, 0), 0.0055, 3)
    bumper_bottom = make_object("Comm_BumperBottom", bm, c, "M_Comm_Rubber")
    add_bevel(bumper_bottom, 0.0012, 2)
    add_mirror(bumper_bottom, x=True)

    # 顶部旋钮：频道（滚花）与音量
    bm, _, cap = bm_prism(knurl_profile(0.0085, 0.0077, 18), TOP_Z, 0.013, (-0.012, 0.0))
    mark_bevel_weight(bm, cap)
    knob = make_object("Comm_KnobChannel", bm, c, "M_Comm_Rubber", sharp_angle=20)
    add_bevel(knob, 0.0012, 2, weighted=True)
    bm = bm_box((0.0016, 0.006, 0.0012), (-0.012, -0.0035, TOP_Z + 0.0133))
    make_object("Comm_KnobPointer", bm, c, "M_Comm_Accent")

    bm, _, cap = bm_prism(knurl_profile(0.0056, 0.0051, 14), TOP_Z, 0.010, (0.004, 0.0))
    mark_bevel_weight(bm, cap)
    vol = make_object("Comm_KnobVolume", bm, c, "M_Comm_Rubber", sharp_angle=20)
    add_bevel(vol, 0.0009, 2, weighted=True)

    # 天线座（固定在机身上，不随天线模块更换）
    bm, _, _ = bm_prism(circle_profile(0.0064, 6), TOP_Z, 0.003, (ANT_X, 0.0))
    nut = make_object("Comm_AntennaNut", bm, c, "M_Comm_Metal", sharp_angle=25)
    add_bevel(nut, 0.0004, 1, angle=50)
    bm, _, cap = bm_prism(circle_profile(0.0044, 16), TOP_Z + 0.003, 0.005, (ANT_X, 0.0))
    mark_bevel_weight(bm, cap)
    stud = make_object("Comm_AntennaStud", bm, c, "M_Comm_Metal", sharp_angle=40)
    add_bevel(stud, 0.0005, 1, weighted=True)

    # 左侧 PTT 大按键 + 两侧防滑肋
    bm = bm_box((0.003, 0.020, 0.036), (-HW / 2 - 0.0012, 0, 0.022))
    round_edges(bm, (1, 0, 0), 0.0045, 3)
    ptt = make_object("Comm_PTT", bm, c, "M_Comm_Rubber")
    add_bevel(ptt, 0.0008, 2)

    bm = bm_box((0.0016, 0.024, 0.003), (-HW / 2 - 0.0006, 0, -0.012))
    rib = make_object("Comm_GripRib", bm, c, "M_Comm_Rubber")
    add_bevel(rib, 0.0006, 1)
    add_array(rib, (0, 0, -0.0065), 7)
    add_mirror(rib, x=True)

    bm = bm_box((0.003, 0.012, 0.012), (HW / 2 + 0.0012, 0, 0.030))
    round_edges(bm, (1, 0, 0), 0.003, 2)
    side = make_object("Comm_SideKey", bm, c, "M_Comm_Accent")
    add_bevel(side, 0.0007, 1)

    # 背面腰夹
    bm = bm_box((0.034, 0.005, 0.026), (0, HD / 2 + 0.0025, 0.047))
    round_edges(bm, (0, 1, 0), 0.004, 3)
    clip = make_object("Comm_BeltClip", bm, c, "M_Comm_Rubber")
    add_bevel(clip, 0.001, 2)

    # 底部挂绳环（半圆环）
    bm = bmesh.new()
    segs_major, segs_minor, R, r = 10, 6, 0.0065, 0.0013
    rings = []
    for i in range(segs_major + 1):
        a = math.pi + math.pi * i / segs_major
        centre = Vector((math.cos(a) * R, HD / 2 - 0.006, -TOP_Z + math.sin(a) * R * 0.9))
        tangent = Vector((-math.sin(a), 0, math.cos(a))).normalized()
        n1 = Vector((0, 1, 0))
        n2 = tangent.cross(n1)
        rings.append([bm.verts.new(centre + (n1 * math.cos(2 * math.pi * k / segs_minor) + n2 * math.sin(2 * math.pi * k / segs_minor)) * r)
                      for k in range(segs_minor)])
    for i in range(segs_major):
        for k in range(segs_minor):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % segs_minor], rings[i + 1][(k + 1) % segs_minor], rings[i + 1][k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    make_object("Comm_LanyardLoop", bm, c, "M_Comm_Metal", sharp_angle=60)


# ---------------------------------------------------------------------------
# 屏幕
# ---------------------------------------------------------------------------

def build_screen(c):
    w, h = 0.0496, 0.0356
    y = RECESS_Y - 0.0045 + 0.0028 - 0.0003
    z = 0.037
    bm = bmesh.new()
    v = [bm.verts.new((-w / 2, y, z - h / 2)), bm.verts.new((w / 2, y, z - h / 2)),
         bm.verts.new((w / 2, y, z + h / 2)), bm.verts.new((-w / 2, y, z + h / 2))]
    f = bm.faces.new(v)
    bm.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    uv = bm.loops.layers.uv.verify()
    for loop in f.loops:
        co = loop.vert.co
        loop[uv].uv = ((co.x + w / 2) / w, (co.z - (z - h / 2)) / h)
    make_object("Comm_Screen", bm, c, "M_Comm_Screen", smooth=False, uv=False)


# ---------------------------------------------------------------------------
# 电池仓（防寒电池包）
# ---------------------------------------------------------------------------

def build_battery(c):
    back = HD / 2
    bm = bm_box((0.066, 0.012, 0.098), (0, back + 0.006, -0.022))
    round_edges(bm, (0, 1, 0), 0.006, 3)
    pack = make_object("Comm_BatteryPack", bm, c, "M_Comm_Housing")
    add_bevel(pack, 0.0012, 2)

    # 保温绗缝垫：3×3 软垫
    bm = bm_box((0.0172, 0.003, 0.0235), (-0.019, back + 0.012 + 0.0012, 0.0065))
    pad = make_object("Comm_BatteryThermalPad", bm, c, "M_Comm_Rubber")
    add_bevel(pad, 0.0013, 2, harden=False)
    add_array(pad, (0.019, 0, 0), 3)
    add_array(pad, (0, 0, -0.0255), 3)

    # 底部醒目色带 + 斜纹
    bm = bm_box((0.0672, 0.0128, 0.008), (0, back + 0.0064, -0.059))
    band = make_object("Comm_BatteryBand", bm, c, "M_Comm_Accent")
    add_bevel(band, 0.0005, 1)

    bm = bm_box((0.0028, 0.0006, 0.0075), (-0.027, back + 0.0128 + 0.0003, -0.059))
    rotate_verts(bm, "Y", 35, (-0.027, back + 0.0131, -0.059))
    stripe = make_object("Comm_BatteryStripe", bm, c, "M_Comm_Rubber", smooth=False)
    add_array(stripe, (0.009, 0, 0), 7)

    # 顶部卡扣
    bm = bm_box((0.012, 0.006, 0.006), (0.02, back + 0.009, 0.026))
    round_edges(bm, (0, 1, 0), 0.0015, 2)
    latch = make_object("Comm_BatteryLatch", bm, c, "M_Comm_Accent")
    add_bevel(latch, 0.0006, 1)
    add_mirror(latch, x=True)


# ---------------------------------------------------------------------------
# 天线模块：损坏 / 修好
# ---------------------------------------------------------------------------

def build_collar(c, name, tilt_deg, jagged):
    bm, top, cap = bm_prism(knurl_profile(0.0052, 0.0048, 16), ANT_BASE_Z, 0.009, (ANT_X, 0.0))
    if jagged:
        # 根部断口：套筒上沿碎裂不齐
        for vert in top:
            vert.co.z -= RNG.uniform(0.0, 0.0022)
            vert.co.x += (vert.co.x - ANT_X) * RNG.uniform(-0.12, 0.05)
    if tilt_deg:
        rotate_verts(bm, "Y", tilt_deg, (ANT_X, 0.0, ANT_BASE_Z))
    mark_bevel_weight(bm, cap)
    obj = make_object(name, bm, c, "M_Comm_Metal", sharp_angle=20)
    add_bevel(obj, 0.0004, 1, weighted=True)
    return obj


def build_antenna_broken(c):
    tilt = 9.0
    build_collar(c, "Comm_AntBroken_Collar", tilt, jagged=True)

    base = Vector((ANT_X, 0.0, ANT_BASE_Z))
    rot0 = Matrix.Rotation(math.radians(tilt), 3, "Y")
    rot1 = Matrix.Rotation(math.radians(tilt + 42), 3, "Y")
    up = Vector((0, 0, 1))
    p0 = base + rot0 @ (up * 0.0082)
    p1 = p0 + rot0 @ (up * 0.0035)
    p2 = p1 + rot1 @ (up * 0.0055)
    p3 = p2 + rot1 @ (up * 0.0055)
    bm = bm_tube([p0, p1, p2, p3], [0.0035, 0.0035, 0.0033, 0.0031], segs=12, jag_end=0.0018, rng=RNG)
    make_object("Comm_AntBroken_Stub", bm, c, "M_Comm_Rubber", sharp_angle=50)

    # 断口处外露的屏蔽编织层
    d1 = rot1 @ up
    bm = bm_tube([p3 - d1 * 0.001, p3 + d1 * 0.0022], [0.0017, 0.0015], segs=10, jag_end=0.0012, rng=RNG)
    make_object("Comm_AntBroken_Braid", bm, c, "M_Comm_Metal", sharp_angle=50)

    # 外露馈线（三股短铜芯，从断口伸出后卷曲，长度约 5–8 mm）
    side = Vector((0, 1, 0)).cross(d1).normalized()
    for i, spread in enumerate((-1.0, 0.2, 1.1)):
        start = p3 - d1 * 0.0012 + side * spread * 0.0008 + Vector((0, (i - 1) * 0.0007, 0))
        pts = [start]
        direction = (d1 + side * spread * 0.4 + Vector((0, (i - 1) * 0.3, 0))).normalized()
        for k in range(5):
            direction = (direction + side * spread * 0.45 - d1 * 0.25 * k).normalized()
            pts.append(pts[-1] + direction * (0.0012 + 0.0003 * i))
        bm = bm_tube(pts, [0.00055] * len(pts), segs=6)
        make_object(f"Comm_AntBroken_Wire{i + 1}", bm, c, "M_Comm_Copper", sharp_angle=60)


def build_antenna_repaired(c):
    build_collar(c, "Comm_AntNew_Collar", 0.0, jagged=False)

    # 新件标识色环
    bm, _, _ = bm_prism(circle_profile(0.0055, 16), ANT_BASE_Z + 0.0055, 0.0018, (ANT_X, 0.0))
    make_object("Comm_AntNew_Band", bm, c, "M_Comm_Accent", sharp_angle=40)

    # 鞭状天线：根部柔性波纹段 + 渐细主体
    z0 = ANT_BASE_Z + 0.0088
    pts, radii = [], []
    for i in range(7):
        pts.append((ANT_X, 0.0, z0 + i * 0.0022))
        radii.append(0.0039 if i % 2 == 0 else 0.0033)
    for i in range(1, 7):
        t = i / 6
        pts.append((ANT_X, 0.0, z0 + 6 * 0.0022 + t * 0.052))
        radii.append(0.0033 + (0.0021 - 0.0033) * t)
    bm = bm_tube(pts, radii, segs=12)
    make_object("Comm_AntNew_Whip", bm, c, "M_Comm_Rubber", sharp_angle=50)

    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.0027)
    bmesh.ops.translate(bm, vec=(ANT_X, 0.0, pts[-1][2] + 0.0012), verts=bm.verts)
    make_object("Comm_AntNew_Tip", bm, c, "M_Comm_Rubber", sharp_angle=80)


# ---------------------------------------------------------------------------
# 统计与导出
# ---------------------------------------------------------------------------

def triangle_count(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eo = obj.evaluated_get(depsgraph)
    me = eo.to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    eo.to_mesh_clear()
    return n


def collect_stats(groups, images):
    stats = {"groups": {}, "materials": sorted(MATERIALS.keys()), "textures": {}}
    for g, coll in groups.items():
        objs = [o for o in coll.objects if o.type == "MESH"]
        tris = sum(triangle_count(o) for o in objs)
        mats = sorted({s.material.name for o in objs for s in o.material_slots if s.material})
        stats["groups"][g] = {"triangles": tris, "objects": len(objs), "materials": mats}
    for name, img in images.items():
        stats["textures"][name] = list(img.size)
    body_set = sum(stats["groups"][g]["triangles"] for g in ("Body", "Screen", "Battery"))
    stats["body_screen_battery_triangles"] = body_set
    stats["max_on_screen_triangles"] = body_set + max(stats["groups"]["AntennaBroken"]["triangles"],
                                                      stats["groups"]["AntennaRepaired"]["triangles"])
    return stats


def export_groups(groups):
    os.makedirs(MODEL_DIR, exist_ok=True)
    view_layer = bpy.context.view_layer
    for g, coll in groups.items():
        for o in bpy.context.scene.objects:
            o.select_set(False)
        objs = [o for o in coll.objects if o.type == "MESH"]
        for o in objs:
            o.select_set(True)
        view_layer.objects.active = objs[0]
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(MODEL_DIR, f"Communicator_{g}.fbx"),
            use_selection=True,
            object_types={"MESH"},
            use_mesh_modifiers=True,
            mesh_smooth_type="OFF",
            apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z",
            axis_up="Y",
            bake_space_transform=True,
            add_leaf_bones=False,
            bake_anim=False,
            path_mode="STRIP",
        )


def write_material_manifest():
    out = {"materials": []}
    for name, spec in MATERIALS.items():
        out["materials"].append({
            "name": name,
            "baseColor": spec["color"],
            "metallic": spec["metallic"],
            "smoothness": round(1.0 - spec["roughness"], 3),
            "baseMap": spec.get("base_map") or "",
            "emissionMap": spec.get("emission_map") or "",
            "emissionColor": spec.get("emission_color", "#000000"),
            "emissionStrength": spec.get("emission_strength", 0.0),
        })
    with open(os.path.join(UNITY_ART, "Communicator_Materials.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)


# ---------------------------------------------------------------------------
# 渲染（不导出的 Studio 集合）
# ---------------------------------------------------------------------------

def setup_studio(scene):
    studio = bpy.data.collections.new("_Studio")
    scene.collection.children.link(studio)

    world = bpy.data.worlds.new("StudioWorld")
    scene.world = world
    tree = ensure_node_tree(world)
    if tree is not None:
        bg = next((n for n in tree.nodes if n.type == "BACKGROUND"), None)
        if bg is not None:
            bg.inputs["Color"].default_value = (0.045, 0.05, 0.058, 1.0)
            bg.inputs["Strength"].default_value = 1.0

    floor_mat = bpy.data.materials.new("M_Studio_Floor")
    ftree = ensure_node_tree(floor_mat)
    fb = next(n for n in ftree.nodes if n.type == "BSDF_PRINCIPLED")
    fb.inputs["Base Color"].default_value = (0.09, 0.1, 0.11, 1.0)
    fb.inputs["Roughness"].default_value = 0.6
    bm = bm_box((1.5, 1.5, 0.002), (0, 0, -TOP_Z - 0.0095))
    me = bpy.data.meshes.new("Studio_Floor")
    bm.to_mesh(me)
    bm.free()
    floor = bpy.data.objects.new("Studio_Floor", me)
    me.materials.append(floor_mat)
    studio.objects.link(floor)

    def light(name, loc, energy, size, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = energy
        data.size = size
        data.color = color
        obj = bpy.data.objects.new(name, data)
        obj.location = loc
        look_at(obj, Vector((0, 0, 0.01)))
        studio.objects.link(obj)

    light("Key", (-0.35, -0.45, 0.4), 28, 0.35, (1.0, 0.96, 0.9))
    light("Fill", (0.5, -0.3, 0.1), 9, 0.5, (0.85, 0.92, 1.0))
    light("Rim", (0.15, 0.5, 0.45), 22, 0.3, (0.8, 0.9, 1.0))
    light("RimBack", (-0.4, 0.45, 0.2), 10, 0.4)

    cam_data = bpy.data.cameras.new("StudioCam")
    cam = bpy.data.objects.new("StudioCam", cam_data)
    studio.objects.link(cam)
    scene.camera = cam

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1050
    scene.render.image_settings.file_format = "PNG"
    # Standard 映射更接近 Unity URP 默认（无 tonemapping）的颜色，便于对照
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
    except Exception:
        pass
    return cam


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def render_view(scene, cam, name, loc, target, lens, show_broken=True):
    for o in bpy.data.collections["Comm_AntennaBroken"].objects:
        o.hide_render = not show_broken
    for o in bpy.data.collections["Comm_AntennaRepaired"].objects:
        o.hide_render = show_broken
    cam.location = Vector(loc)
    cam.data.lens = lens
    look_at(cam, Vector(target))
    os.makedirs(SHOT_DIR, exist_ok=True)
    scene.render.filepath = os.path.join(SHOT_DIR, f"{SHOT_PREFIX}_{name}.png")
    bpy.ops.render.render(write_still=True)


# ---------------------------------------------------------------------------

def main():
    global MATS
    os.makedirs(TEX_DIR, exist_ok=True)
    scene, groups = reset_scene()

    images = {
        GRIME_TEX: save_image(GRIME_TEX, make_grime_texture(1024)),
        SCREEN_TEX: save_image(SCREEN_TEX, make_screen_texture(512, 256)),
    }
    MATS = build_materials(images)

    build_body(groups["Body"])
    build_screen(groups["Screen"])
    build_battery(groups["Battery"])
    build_antenna_broken(groups["AntennaBroken"])
    build_antenna_repaired(groups["AntennaRepaired"])

    # 默认视图显示损坏天线；修好的天线隐藏但保留，可在大纲中切换
    groups["AntennaRepaired"].hide_viewport = False
    for o in groups["AntennaRepaired"].objects:
        o.hide_set(True)

    stats = collect_stats(groups, images)
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[Communicator] stats:", json.dumps(stats, ensure_ascii=False))

    if DO_EXPORT:
        for o in groups["AntennaRepaired"].objects:
            o.hide_set(False)
        export_groups(groups)
        write_material_manifest()
        for o in groups["AntennaRepaired"].objects:
            o.hide_set(True)

    cam = setup_studio(scene)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Communicator.blend"))

    if DO_RENDER:
        # 正反面：竖直视野约 0.24 m，完整包含底部挂绳环到天线顶端
        render_view(scene, cam, "01_front", (0.10, -0.62, 0.09), (0, 0, 0.014), 70)
        render_view(scene, cam, "02_back", (-0.12, 0.62, 0.10), (0, 0, 0.014), 70)
        render_view(scene, cam, "03_damage_closeup", (0.075, -0.095, 0.125), (ANT_X, 0, ANT_BASE_Z + 0.008), 90)
        render_view(scene, cam, "04_repaired_closeup", (0.10, -0.16, 0.16), (ANT_X, 0, ANT_BASE_Z + 0.036), 60, show_broken=False)
    print("[Communicator] done")


MATS = {}

if __name__ == "__main__":
    main()
