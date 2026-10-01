"""
边境维修站 · 地下义体医生的维修工作台区域 · Blender 5.2 建模脚本（可重复生成）
由 Claude 辅助编写。运行：run_blender.bat（或 blender -b --factory-startup --python build_workbench_area.py [-- --norender]）

输出（本目录）：
  WorkbenchArea.blend      源文件（含检查体，已隐藏）
  Export/WorkbenchArea.fbx 整个区域（带层级、原点、自定义属性；不含检查体）
  Textures/*.png           程序化低分辨率粗颗粒贴图（Unity 中用点采样）
  Renders/*.png            Cycles 渲染（正面、斜俯视、玩家近距、侧面、移开杂物）
  stats.json / checks.json / asset_inventory.csv / materials.json

坐标：Blender Z 向上，玩家站在 -Y 一侧，工作台靠 +Y 的墙。单位米。
风格：低面数复古 3D、粗颗粒漫反射（金属度 0、高粗糙度），绿灰工业环境 + 局部暖工作灯；红色只用于故障指示。
中央的义肢是“待修义肢占位件”（Placeholder_Prosthetic_*），只用于验证镜头与手部接近，不是美术资产，也不是 RobotV4。
"""

import csv
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
sys.path.insert(0, os.path.join(HERE, "..", "Common"))
import br_hardsurface as hs  # noqa: E402

for ch, g in {"W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
              "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
              "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
              "Q": ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
              "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
              "#": ["01010", "11111", "01010", "01010", "11111", "01010", "00000"],
              ":": ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
              "?": ["01110", "10001", "00001", "00110", "00100", "00000", "00100"],
              "!": ["00100", "00100", "00100", "00100", "00100", "00000", "00100"]}.items():
    hs.FONT.setdefault(ch, g)   # 共用字库缺的字母，只在本脚本运行时补充

TEX_DIR = os.path.join(HERE, "Textures")
EXPORT_DIR = os.path.join(HERE, "Export")
RENDER_DIR = os.path.join(HERE, "Renders")
for d in (TEX_DIR, EXPORT_DIR, RENDER_DIR):
    os.makedirs(d, exist_ok=True)
NO_RENDER = "--norender" in sys.argv

BENCH_TOP = 0.90          # 台面高度（站姿操作；VR 站立双手高度）
MAT_Z = BENCH_TOP + 0.004
RNG = random.Random(1142)

# ===========================================================================
# 1. 贴图（低分辨率，Unity 中点采样）
# ===========================================================================

def hexc(h):
    return np.array(hs.hex_rgb(h))


def grain_texture(size=128, seed=3):
    """粗颗粒灰度底纹：块状低频 + 抖色噪点 + 少量划痕，可平铺。"""
    rng = np.random.default_rng(seed)
    low = hs.periodic_noise(size, 14, rng)
    mid = hs.periodic_noise(size, 3, rng)
    block = np.kron(hs.periodic_noise(size // 4, 2, rng), np.ones((4, 4)))[:size, :size]
    v = 0.80 + 0.10 * (low - 0.5) + 0.06 * (mid - 0.5) + 0.07 * (block - 0.5)
    dither = (rng.random((size, size)) - 0.5) * 0.06
    v = v + dither
    scratches = np.zeros((size, size))
    pr = random.Random(seed)
    for _ in range(40):
        x0, y0 = pr.uniform(0, size), pr.uniform(0, size)
        a = pr.uniform(0, math.pi)
        L = pr.uniform(4, 22)
        hs.draw_line_wrap(scratches, x0, y0, x0 + math.cos(a) * L, y0 + math.sin(a) * L, pr.uniform(0.05, 0.12))
    v = np.clip(v + scratches - 0.1 * np.clip((low - 0.7) * 3, 0, 1), 0, 1)
    return np.clip(np.stack([v, v, v], axis=-1), 0, 1)


def floor_texture(size=128, seed=5):
    rng = np.random.default_rng(seed)
    g = grain_texture(size, seed)[..., 0]
    img = np.ones((size, size, 3)) * hexc("#4C524C")
    img *= g[..., None] * 1.05
    t = size // 4
    for k in range(0, size, t):
        img[k:k + 1, :] *= 0.55
        img[:, k:k + 1] *= 0.55
    stain = hs.periodic_noise(size, 10, rng)
    img *= (0.85 + 0.15 * stain)[..., None]
    return np.clip(img, 0, 1)


PAPER_W = PAPER_H = 512
# 贴图集区域（像素，行 0 在顶部）
R_WO = [(0, 0, 128, 176), (128, 0, 256, 176), (256, 0, 384, 176)]          # 维修工单 ×3
R_NOTE = [(384, 0, 448, 64), (448, 0, 512, 64), (384, 64, 448, 128), (448, 64, 512, 128)]   # 便签 ×4
R_LABEL = [(0, 192 + 32 * i, 112, 224 + 32 * i) for i in range(6)]           # 收纳标签 ×6
R_LABEL2 = [(120, 192 + 32 * i, 232, 224 + 32 * i) for i in range(6)]        # 收纳标签 ×6
R_LOG = (240, 192, 432, 320)                                                 # 摊开的维修记录本
R_PHOTO = (440, 192, 512, 240)                                               # 私人照片
R_PRINT = (240, 336, 432, 368)                                               # 诊断仪打印纸带
R_TAPE = (240, 384, 432, 400)                                                # 手写胶带条
R_PH_TAG = (440, 256, 512, 288)                                              # “PLACEHOLDER” 标签
R_CLIP = (0, 400, 112, 512)                                                  # 夹板上的工单


def region_uv(r):
    x0, y0, x1, y1 = r
    return x0 / PAPER_W, 1 - y1 / PAPER_H, (x1 - x0) / PAPER_W, (y1 - y0) / PAPER_H


def scrawl(img, x0, y0, x1, y1, color, rng, lines=6, pitch=None):
    """伪手写：每行一串抖动的短笔画，用来表现看不清内容的手写记录。"""
    h = y1 - y0
    pitch = pitch or max(6, h // (lines + 1))
    for li in range(lines):
        y = y0 + pitch * (li + 1)
        if y >= y1 - 2:
            break
        x = x0 + rng.integers(0, 4)
        end = x1 - rng.integers(2, max(3, (x1 - x0) // 3))
        while x < end:
            seg = int(rng.integers(3, 9))
            for k in range(seg):
                yy = int(y + math.sin((x + k) * 0.9 + li) * 1.2 + rng.integers(-1, 2))
                xx = x + k
                if x0 <= xx < x1 and y0 <= yy < y1:
                    img[yy, xx] = color
            x += seg + int(rng.integers(2, 5))


def hand_text(img, text, x, y, scale, color, rng):
    """手写感标签：等宽像素字，每个字随机上下偏移 1 像素。"""
    cx = x
    for ch in text:
        glyph = hs.FONT.get(ch, hs.FONT[" "])
        dy = int(rng.integers(-1, 2))
        for gy, row in enumerate(glyph):
            for gx, bit in enumerate(row):
                if bit == "1":
                    img[y + dy + gy * scale:y + dy + (gy + 1) * scale, cx + gx * scale:cx + (gx + 1) * scale] = color
        cx += 6 * scale


def paper_texture(seed=11):
    rng = np.random.default_rng(seed)
    img = np.ones((PAPER_H, PAPER_W, 3)) * hexc("#3A403B")
    paper = hexc("#D9D2BC"); paper2 = hexc("#CFC7AA"); ink = hexc("#2B2E33"); blue = hexc("#2F3F66")
    pencil = hexc("#5A5A55"); yellow = hexc("#D8C27A"); amber = hexc("#B8873A"); green = hexc("#5E7A58")
    # 维修工单
    for i, (x0, y0, x1, y1) in enumerate(R_WO):
        img[y0:y1, x0:x1] = paper if i != 1 else paper2
        hs.rect(img, x0 + 6, y0 + 6, x1 - 6, y0 + 22, hexc("#BDB59C"))
        hand_text(img, f"WO-{1139 + i}", x0 + 10, y0 + 9, 2, ink, rng)
        for k in range(8):
            yy = y0 + 32 + k * 16
            hs.rect(img, x0 + 6, yy, x1 - 6, yy + 1, hexc("#A9A28A"))
        scrawl(img, x0 + 8, y0 + 26, x1 - 8, y1 - 24, blue if i != 2 else pencil, rng, lines=8, pitch=16)
        hand_text(img, ["ARM", "HAND", "SERVO"][i], x0 + 10, y1 - 20, 2, ink, rng)
        if i == 0:
            hs.rect(img, x1 - 40, y0 + 30, x1 - 10, y0 + 44, hexc("#9A3A30"))   # 唯一的红：工单上“故障”印章
    # 便签
    for i, (x0, y0, x1, y1) in enumerate(R_NOTE):
        img[y0:y1, x0:x1] = [yellow, paper, hexc("#C9D2B4"), paper2][i]
        scrawl(img, x0 + 4, y0 + 4, x1 - 4, y1 - 4, [ink, blue, pencil, ink][i], rng, lines=4, pitch=12)
    # 收纳标签（手写）
    words = ["M3 SCREWS", "M4 NUTS", "OLD BRG", "WIRE 22", "SERVO", "FUSES", "SPRINGS", "CAPS", "JOINTS", "SEALS", "PINS", "MISC ?"]
    for i, (x0, y0, x1, y1) in enumerate(R_LABEL + R_LABEL2):
        img[y0:y1, x0:x1] = paper if i % 3 else paper2
        hand_text(img, words[i], x0 + 6, y0 + 9, 2, ink if i % 2 else blue, rng)
    # 维修记录本（摊开的两页）
    x0, y0, x1, y1 = R_LOG
    img[y0:y1, x0:x1] = paper
    hs.rect(img, (x0 + x1) // 2 - 1, y0, (x0 + x1) // 2 + 1, y1, hexc("#8F8873"))
    scrawl(img, x0 + 6, y0 + 6, (x0 + x1) // 2 - 6, y1 - 6, blue, rng, lines=9, pitch=13)
    scrawl(img, (x0 + x1) // 2 + 6, y0 + 6, x1 - 6, y1 - 30, pencil, rng, lines=7, pitch=13)
    hand_text(img, "SEP 30", (x0 + x1) // 2 + 8, y1 - 24, 2, ink, rng)
    # 私人照片（抽象的风景色块）
    x0, y0, x1, y1 = R_PHOTO
    img[y0:y1, x0:x1] = hexc("#E6E0CC")
    hs.rect(img, x0 + 4, y0 + 4, x1 - 4, (y0 + y1) // 2, hexc("#7C93A0"))
    hs.rect(img, x0 + 4, (y0 + y1) // 2, x1 - 4, y1 - 4, hexc("#6C7A55"))
    hs.disc(img, x0 + 20, y0 + 14, 6, hexc("#D8C27A"))
    # 打印纸带：波形
    x0, y0, x1, y1 = R_PRINT
    img[y0:y1, x0:x1] = paper
    for x in range(x0 + 2, x1 - 2):
        y = int((y0 + y1) / 2 + math.sin((x - x0) * 0.25) * 7 * (0.6 + 0.4 * math.sin((x - x0) * 0.031)))
        img[y:y + 2, x] = ink
    # 手写胶带条
    x0, y0, x1, y1 = R_TAPE
    img[y0:y1, x0:x1] = hexc("#C9B98A")
    hand_text(img, "DO NOT USE", x0 + 8, y0 + 1, 2, ink, rng)
    # 占位件标签
    x0, y0, x1, y1 = R_PH_TAG
    img[y0:y1, x0:x1] = hexc("#E8E4D8")
    hand_text(img, "PH", x0 + 6, y0 + 9, 2, hexc("#2F3F66"), rng)
    hs.rect(img, x0 + 36, y0 + 8, x1 - 6, y1 - 8, hexc("#7D93A6"))
    # 夹板工单
    x0, y0, x1, y1 = R_CLIP
    img[y0:y1, x0:x1] = paper
    hand_text(img, "QUEUE", x0 + 8, y0 + 8, 2, ink, rng)
    scrawl(img, x0 + 6, y0 + 26, x1 - 6, y1 - 6, blue, rng, lines=7, pitch=11)
    # 整张加纸张颗粒
    g = grain_texture(PAPER_W, 21)[..., 0]
    img *= (0.92 + 0.5 * (g - 0.8))[..., None]
    return np.clip(img, 0, 1)


SCREEN_W, SCREEN_H = 128, 96


def screen_texture():
    img = np.ones((SCREEN_H, SCREEN_W, 3)) * hexc("#0E1A12")
    grid = hexc("#1C3324")
    for x in range(0, SCREEN_W, 16):
        img[:, x] = grid
    for y in range(0, SCREEN_H, 16):
        img[y, :] = grid
    amber = hexc("#E2B95A"); green = hexc("#7FD08A")
    for x in range(4, SCREEN_W - 4):
        y = int(SCREEN_H * 0.45 + math.sin(x * 0.22) * 14 * (0.5 + 0.5 * math.sin(x * 0.047)) + (6 if 60 < x < 66 else 0))
        img[y:y + 2, x] = amber
    for x in range(4, SCREEN_W - 4, 2):
        img[int(SCREEN_H * 0.8 + math.sin(x * 0.5) * 2), x] = green
    hs.draw_text(img, "CH1 2.4V", 4, 4, 1, green)
    hs.draw_text(img, "ARM 07", 4, SCREEN_H - 10, 1, amber)
    return np.clip(img, 0, 1)


MAT_X0, MAT_X1, MAT_Y0, MAT_Y1 = -0.42, 0.38, 0.22, 0.68
MAT_PX = 400   # 贴图像素 / 米


def mat_texture():
    w = int((MAT_X1 - MAT_X0) * MAT_PX); h = int((MAT_Y1 - MAT_Y0) * MAT_PX)
    rng = np.random.default_rng(7)
    img = np.ones((h, w, 3)) * hexc("#2E3A33")
    g = hs.periodic_noise(max(w, h), 6, rng)[:h, :w]
    img *= (0.85 + 0.25 * g)[..., None]
    line = hexc("#46564C")
    for x in range(0, w, 20):
        img[:, x] = line
    for y in range(0, h, 20):
        img[y, :] = line

    def px(xm, ym):   # 台面坐标 → 贴图像素（行 0 在顶部 = 远离玩家的一侧）
        return int((xm - MAT_X0) * MAT_PX), int((MAT_Y1 - ym) * MAT_PX)

    pale = hexc("#B9B79E")
    # 盖板停放区（右前）与螺钉落点圆
    ax, ay = px(0.15, 0.36); bx, by = px(0.37, 0.24)
    hs.rect_outline(img, ax, ay, bx, by, 2, pale)
    hs.draw_text(img, "COVER", ax + 6, ay + 6, 2, pale)
    # 中央操作区四角标记（保持中心空）
    for (cx, cy) in ((-0.32, 0.30), (0.12, 0.30), (-0.32, 0.60), (0.12, 0.60)):
        x, y = px(cx, cy)
        hs.rect(img, x - 8, y - 1, x + 8, y + 1, pale)
        hs.rect(img, x - 1, y - 8, x + 1, y + 8, pale)
    # 前沿尺子刻度
    for k in range(0, w, 8):
        hs.rect(img, k, h - (10 if k % 40 == 0 else 5), k + 1, h, pale)
    # 磨损：中部被手肘和工具磨亮
    yy, xx = np.mgrid[0:h, 0:w]
    wear = np.exp(-(((xx - w * 0.45) / (w * 0.25)) ** 2 + ((yy - h * 0.6) / (h * 0.3)) ** 2))
    img = img * (1 + 0.18 * wear[..., None])
    return np.clip(img, 0, 1)


# ===========================================================================
# 2. 材质（漫反射为主：金属度 0、粗糙度高；贴图最近邻采样）
# ===========================================================================
MAT_SPECS = {
    "M_WB_WallGreen":   dict(color="#5E6B60", base_map="T_WB_Grain.png", roughness=0.92),
    "M_WB_PaintGreen":  dict(color="#4C5C50", base_map="T_WB_Grain.png", roughness=0.88),
    "M_WB_Ivory":       dict(color="#CFC6AA", base_map="T_WB_Grain.png", roughness=0.85),
    "M_WB_Steel":       dict(color="#8C928D", base_map="T_WB_Grain.png", roughness=0.80),
    "M_WB_Rubber":      dict(color="#2C312D", base_map="T_WB_Grain.png", roughness=0.95),
    "M_WB_Wood":        dict(color="#6E5C46", base_map="T_WB_Grain.png", roughness=0.90),
    "M_WB_Yellow":      dict(color="#B99A45", base_map="T_WB_Grain.png", roughness=0.85),
    "M_WB_Mat":         dict(color="#FFFFFF", base_map="T_WB_Mat.png", roughness=0.95),
    "M_WB_Paper":       dict(color="#FFFFFF", base_map="T_WB_Paper.png", roughness=0.95),
    "M_WB_Floor":       dict(color="#FFFFFF", base_map="T_WB_Floor.png", roughness=0.95),
    "M_WB_Screen":      dict(color="#FFFFFF", base_map="T_WB_Screen.png", roughness=0.6, emission_map=True, emission_strength=1.6),
    "M_WB_LampWarm":    dict(color="#FFD9A0", roughness=0.5, emission_color="#FFCF8A", emission_strength=6.0),
    "M_WB_FaultRed":    dict(color="#7A1A14", roughness=0.5, emission_color="#FF3A22", emission_strength=4.0),
    "M_WB_LampCool":    dict(color="#D8E8DC", roughness=0.5, emission_color="#CFE8D8", emission_strength=3.0),
    "M_WB_Placeholder": dict(color="#7D93A6", base_map="T_WB_Grain.png", roughness=0.85),
}
UV_SCALE = {"M_WB_WallGreen": 0.5, "M_WB_PaintGreen": 0.35, "M_WB_Ivory": 0.25, "M_WB_Steel": 0.25, "M_WB_Rubber": 0.25,
            "M_WB_Wood": 0.4, "M_WB_Yellow": 0.2, "M_WB_Placeholder": 0.25, "M_WB_Floor": 1.0}
MATS = {}


def build_materials(images):
    for name, s in MAT_SPECS.items():
        m = bpy.data.materials.new(name)
        tree = hs.ensure_node_tree(m)
        nodes, links = tree.nodes, tree.links
        bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
        tint = [hs.srgb_to_linear(c) for c in hs.hex_rgb(s["color"])]
        bsdf.inputs["Metallic"].default_value = 0.0
        bsdf.inputs["Roughness"].default_value = s["roughness"]
        try:
            bsdf.inputs["Specular IOR Level"].default_value = 0.2
        except Exception:
            pass
        tex = None
        if s.get("base_map"):
            tex = nodes.new("ShaderNodeTexImage")
            tex.image = images[s["base_map"]]
            tex.interpolation = "Closest"           # 粗颗粒：最近邻
            mul = nodes.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY"
            mul.inputs[1].default_value = tint
            links.new(tex.outputs["Color"], mul.inputs[0])
            links.new(mul.outputs["Vector"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (*tint, 1.0)
        if s.get("emission_strength", 0) > 0:
            if s.get("emission_map") and tex is not None:
                links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
            else:
                ec = [hs.srgb_to_linear(c) for c in hs.hex_rgb(s["emission_color"])]
                bsdf.inputs["Emission Color"].default_value = (*ec, 1.0)
            bsdf.inputs["Emission Strength"].default_value = s["emission_strength"]
        MATS[name] = m
        hs.MATS[name] = m
    out = {"materials": []}
    for name, s in MAT_SPECS.items():
        out["materials"].append({"name": name, "baseColor": s["color"], "metallic": 0.0, "smoothness": round(1 - s["roughness"], 3),
                                 "baseMap": s.get("base_map", ""), "emissionMap": s.get("base_map", "") if s.get("emission_map") else "",
                                 "emissionColor": s.get("emission_color", "#FFFFFF" if s.get("emission_map") else "#000000"),
                                 "emissionStrength": s.get("emission_strength", 0.0), "filterMode": "Point"})
    with open(os.path.join(HERE, "materials.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)


# ===========================================================================
# 3. 几何工具
# ===========================================================================

def box(lo, hi):
    lo, hi = Vector(lo), Vector(hi)
    return hs.bm_box(tuple(hi - lo), tuple((lo + hi) / 2))


def cyl(axis, center, r, length, segs=10, r2=None):
    bm = bmesh.new()
    if r2 is None:
        b, _, _ = hs.bm_prism(hs.circle_profile(r, segs, math.pi / segs), -length / 2, length)
        bm.free(); bm = b
    else:
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r2, depth=length)
    if axis == "X":
        hs.rotate_verts(bm, "Y", 90, (0, 0, 0))
    elif axis == "Y":
        hs.rotate_verts(bm, "X", -90, (0, 0, 0))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return bm


def tube(points, r, segs=6):
    return hs.bm_tube([Vector(p) for p in points], [r] * len(points), segs=segs)


def merge(*bms):
    out = bmesh.new()
    for b in bms:
        if b is None:
            continue
        me = bpy.data.meshes.new("_tmp")
        b.to_mesh(me)
        out.from_mesh(me)
        bpy.data.meshes.remove(me)
        b.free()
    return out


def rot(bm, axis, deg, pivot):
    hs.rotate_verts(bm, axis, deg, pivot)
    return bm


OBJECTS = []   # (obj, group, role)


def part(name, bm, coll, mat, origin=None, role="static", bevel=0.0, smooth=False, uv="box", uv_args=None, **props):
    """世界坐标建好的 bm → 对象；原点放在 origin（抓取点 / 铰轴 / 安装面）。"""
    if origin is None:
        vs = [v.co for v in bm.verts]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        origin = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))   # 默认：底面中心 = 安装面
    o = Vector(origin)
    bmesh.ops.translate(bm, vec=-o, verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # 法线一律朝外：Cycles 双面渲染看不出，Unity 会剔除背面
    if uv == "box":
        hs.box_uv(bm, scale=UV_SCALE.get(mat, 0.25))
    elif uv == "planar":
        hs.planar_uv(bm, **uv_args)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    me.materials.append(MATS[mat])
    if smooth:
        me.shade_smooth()
        try:
            me.set_sharp_from_angle(angle=math.radians(40))
        except Exception:
            pass
    obj.location = o
    if bevel > 0:
        m = obj.modifiers.new("Bevel", "BEVEL")
        m.width = bevel; m.segments = 1; m.limit_method = "ANGLE"; m.angle_limit = math.radians(35); m.harden_normals = False
    obj["wb_role"] = role
    for k, v in props.items():
        obj[k] = " ".join(f"{x:.3f}" for x in v) if isinstance(v, (list, tuple, Vector)) else v   # FBX 只导出标量 / 字符串属性
    OBJECTS.append((obj, coll.name, role))
    return obj


def paper_part(name, lo, hi, region, coll, role="clutter", axis="Z", **props):
    """一张纸 / 标签：平面贴图集区域。axis = 法线方向（Z 平放，-Y 贴在朝玩家的面，Y 背面）。"""
    lo, hi = Vector(lo), Vector(hi)
    bm = box(lo, hi)
    u0, v0, us, vs = region_uv(region)
    c = (lo + hi) / 2
    if axis == "Z":
        args = dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 1, 0), u_size=hi.x - lo.x, v_size=hi.y - lo.y)
    elif axis == "-Y":
        args = dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=hi.x - lo.x, v_size=hi.z - lo.z)
    else:   # "X"：贴在左墙（法线 +X）
        args = dict(origin=(0, 0, 0), u_axis=(0, 1, 0), v_axis=(0, 0, 1), u_size=hi.y - lo.y, v_size=hi.z - lo.z)
    args.update(u0=u0, v0=v0, u_span=us, v_span=vs)
    return part(name, bm, coll, "M_WB_Paper", origin=c, role=role, uv="planar", uv_args=args, **props)


# ===========================================================================
# 4. 建模
# ===========================================================================
GROUPS = ["Room", "Bench", "Mat", "Placeholder", "Trays", "Toolbox", "Diagnostic", "Lamp", "Storage", "Records", "Clutter"]


def build_room(C):
    part("Room_Floor", box((-1.6, -1.7, -0.05), (1.6, 1.15, 0.0)), C["Room"], "M_WB_Floor", origin=(0, 0, 0))
    part("Room_WallBack", box((-1.6, 1.05, 0.0), (1.6, 1.15, 2.6)), C["Room"], "M_WB_WallGreen", origin=(0, 1.05, 0))
    part("Room_WallLeft", box((-1.7, -1.7, 0.0), (-1.6, 1.15, 2.6)), C["Room"], "M_WB_WallGreen", origin=(-1.6, -0.3, 0))
    part("Room_WallRight", box((1.6, -0.2, 0.0), (1.7, 1.15, 2.6)), C["Room"], "M_WB_WallGreen", origin=(1.6, 0.5, 0))
    # 墙裙（深一档的绿灰）与踢脚
    part("Room_Wainscot", merge(box((-1.6, 1.035, 0.0), (1.6, 1.05, 0.62)), box((-1.6, -1.7, 0.0), (-1.585, 1.05, 0.62))),
         C["Room"], "M_WB_PaintGreen", origin=(0, 1.04, 0))
    # 墙上的线槽、接线盒、管道（固定设施）
    conduits = merge(
        cyl("X", (0, 1.025, 2.35), 0.03, 3.2, 8), cyl("X", (0, 1.02, 2.25), 0.018, 3.2, 8),
        cyl("Z", (-1.2, 1.02, 1.3), 0.018, 2.1, 8), box((-1.36, 0.99, 1.15), (-1.04, 1.05, 1.45)),
        cyl("Y", (1.2, 0.5, 2.3), 0.035, 1.3, 8))
    part("Room_Conduits", conduits, C["Room"], "M_WB_Steel", origin=(0, 1.02, 2.3))
    # 顶部吊灯（冷色日光灯，只有灯罩；发光管单独）
    part("Room_CeilingFixture", merge(box((-0.55, 0.15, 2.38), (0.55, 0.35, 2.42)), tube([(-0.4, 0.25, 2.42), (-0.4, 0.25, 2.6)], 0.006),
                                      tube([(0.4, 0.25, 2.42), (0.4, 0.25, 2.6)], 0.006)),
         C["Room"], "M_WB_Steel", origin=(0, 0.25, 2.42))
    part("Room_CeilingTube", cyl("X", (0, 0.25, 2.37), 0.016, 1.0, 8), C["Room"], "M_WB_LampCool", origin=(0, 0.25, 2.37), role="light_fixture",
         note="cool fluorescent tube")
    # 地漏格栅
    part("Room_FloorDrain", box((0.9, -1.2, 0.0), (1.2, -0.9, 0.006)), C["Room"], "M_WB_Steel", origin=(1.05, -1.05, 0))


def build_bench(C):
    T = BENCH_TOP
    # 台面（旧复合板）与前沿钢包边
    part("Bench_Top", box((-0.95, 0.15, T - 0.04), (0.95, 0.95, T)), C["Bench"], "M_WB_Wood", origin=(0, 0.55, T), role="static",
         note="work surface top at z 0.90")
    part("Bench_TopEdge", merge(box((-0.96, 0.135, T - 0.05), (0.96, 0.152, T + 0.004)), box((-0.96, 0.135, T - 0.05), (-0.95, 0.95, T))),
         C["Bench"], "M_WB_Steel", origin=(0, 0.14, T))
    # 框架：腿、横撑、底层搁板
    legs = []
    for x in (-0.92, 0.92):
        for y in (0.19, 0.92):
            legs.append(box((x - 0.025, y - 0.025, 0.0), (x + 0.025, y + 0.025, T - 0.04)))
    legs += [box((-0.92, 0.17, 0.12), (0.92, 0.94, 0.14)), box((-0.92, 0.9, 0.62), (0.92, 0.94, 0.66))]
    part("Bench_Frame", merge(*legs), C["Bench"], "M_WB_PaintGreen", origin=(0, 0.55, 0))
    # 左侧抽屉柜（三层，最上层半开：探针抽屉）
    part("Bench_DrawerCase", merge(box((-0.92, 0.19, 0.14), (-0.90, 0.93, 0.84)), box((-0.50, 0.19, 0.14), (-0.48, 0.93, 0.84)),
                                   box((-0.92, 0.90, 0.14), (-0.48, 0.93, 0.84)), box((-0.92, 0.19, 0.82), (-0.48, 0.93, 0.84))),
         C["Bench"], "M_WB_PaintGreen", origin=(-0.70, 0.56, 0.14))
    drawers = [("Bench_Drawer_1_Probes", 0.62, 0.80, 0.22), ("Bench_Drawer_2", 0.40, 0.60, 0.0), ("Bench_Drawer_3", 0.16, 0.38, 0.0)]
    for nm, z0, z1, open_d in drawers:
        y0 = 0.19 - open_d
        front = box((-0.90, y0 - 0.02, z0), (-0.50, y0, z1))
        body = merge(box((-0.885, y0, z0 + 0.01), (-0.515, y0 + 0.70, z0 + 0.02)),
                     box((-0.885, y0, z0 + 0.02), (-0.875, y0 + 0.70, z1 - 0.04)), box((-0.525, y0, z0 + 0.02), (-0.515, y0 + 0.70, z1 - 0.04)),
                     box((-0.885, y0 + 0.69, z0 + 0.02), (-0.515, y0 + 0.70, z1 - 0.04)))
        handle = box((-0.76, y0 - 0.045, (z0 + z1) / 2 - 0.008), (-0.64, y0 - 0.02, (z0 + z1) / 2 + 0.008))
        grab = Vector((-0.70, y0 - 0.045, (z0 + z1) / 2))
        part(nm, merge(front, body, handle), C["Bench"], "M_WB_PaintGreen", origin=grab, role="inspectable", bevel=0.002,
             slide_axis_local="-Y", travel_m=0.30, open_m=open_d, grab_point="handle center")
    # 半开抽屉里的探针和小工具（合并，非交互）
    probes = [tube([(-0.84 + 0.05 * i, -0.02, 0.645), (-0.84 + 0.05 * i, 0.20, 0.645)], 0.006, 6) for i in range(5)]
    part("Clutter_DrawerProbes", merge(*probes, box((-0.86, 0.22, 0.635), (-0.55, 0.30, 0.66))), C["Clutter"], "M_WB_Yellow", origin=(-0.70, 0.1, 0.635), role="clutter")
    # 右侧柜（门微开 15°，铰轴在门左边缘）
    part("Bench_Cabinet", merge(box((0.50, 0.19, 0.14), (0.52, 0.93, 0.84)), box((0.90, 0.19, 0.14), (0.92, 0.93, 0.84)),
                                box((0.50, 0.90, 0.14), (0.92, 0.93, 0.84)), box((0.50, 0.19, 0.82), (0.92, 0.93, 0.84))),
         C["Bench"], "M_WB_PaintGreen", origin=(0.71, 0.56, 0.14))
    door = merge(box((0.52, 0.17, 0.16), (0.90, 0.19, 0.80)), box((0.84, 0.145, 0.42), (0.86, 0.17, 0.56)))
    rot(door, "Z", -15, (0.52, 0.19, 0))
    part("Bench_CabinetDoor", door, C["Bench"], "M_WB_PaintGreen", origin=(0.52, 0.19, 0.16), role="inspectable", bevel=0.002,
         hinge_axis_local="Z", open_deg=-15.0, note="hinge on the left edge; currently ajar 15 deg")
    # 背板：洞洞板（工具墙），安装面贴墙
    holes = box((-0.95, 1.02, T + 0.02), (0.95, 1.035, 1.75))
    part("Wall_Pegboard", holes, C["Storage"], "M_WB_PaintGreen", origin=(0, 1.035, T + 0.02), role="static")
    # 上层搁板与支架
    part("Wall_ShelfUpper", merge(box((-0.95, 0.78, 1.80), (0.95, 1.035, 1.825)),
                                  box((-0.80, 1.0, 1.70), (-0.78, 1.035, 1.80)), box((0.78, 1.0, 1.70), (0.80, 1.035, 1.80))),
         C["Storage"], "M_WB_Steel", origin=(0, 1.035, 1.80))


def build_mat(C):
    w = MAT_X1 - MAT_X0; h = MAT_Y1 - MAT_Y0
    part("Bench_Mat", box((MAT_X0, MAT_Y0, BENCH_TOP), (MAT_X1, MAT_Y1, MAT_Z)), C["Mat"], "M_WB_Mat",
         origin=((MAT_X0 + MAT_X1) / 2, (MAT_Y0 + MAT_Y1) / 2, BENCH_TOP), uv="planar",
         uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 1, 0), u_size=w, v_size=h), role="static",
         note="wear-resistant work mat; printed cover parking zone at front right; center kept clear")


# 待修义肢占位件：沿 X 放在操作垫中央，手指向 -X
ARM_Y = 0.45
ARM_Z = MAT_Z + 0.026 + 0.046      # 托架高 26 mm，半径 46 mm 的中心高度
COVER = dict(x0=-0.12, x1=0.10, half_w=0.034)
SCREWS = [(-0.11, -0.027), (-0.11, 0.027), (0.09, -0.027), (0.09, 0.027)]
COVER_PARK = Vector((0.26, 0.30, MAT_Z))   # 操作垫右前印有 COVER 停放框（x 0.15–0.37）
SCREW_TRAY_SLOT = Vector((0.47, 0.30, BENCH_TOP + 0.012))


def build_placeholder(C):
    ph = C["Placeholder"]
    # 托架（V 形支块，静止）
    cr = []
    for x in (-0.20, 0.13):
        cr.append(merge(box((x - 0.03, ARM_Y - 0.05, MAT_Z), (x + 0.03, ARM_Y - 0.025, MAT_Z + 0.04)),
                        box((x - 0.03, ARM_Y + 0.025, MAT_Z), (x + 0.03, ARM_Y + 0.05, MAT_Z + 0.04)),
                        box((x - 0.03, ARM_Y - 0.05, MAT_Z), (x + 0.03, ARM_Y + 0.05, MAT_Z + 0.012))))
    part("Bench_ArmCradles", merge(*cr), C["Mat"], "M_WB_Rubber", origin=(-0.035, ARM_Y, MAT_Z), role="static")
    # 前臂外壳：八边形截面、渐细；顶部留出检修口（盖板盖住）
    fore = cyl("X", (-0.01, ARM_Y, ARM_Z), 0.046, 0.42, 8, r2=0.040)
    rot(fore, "X", 180 / 8, (0, ARM_Y, ARM_Z))
    part("Placeholder_Prosthetic_Forearm", fore, ph, "M_WB_Placeholder", origin=(-0.01, ARM_Y, ARM_Z), role="placeholder",
         grab_point="shell center", note="PLACEHOLDER: not art, not RobotV4; validates camera and hand access")
    # 检修口内部（盖板拿开后可见）
    bay = merge(box((COVER["x0"] + 0.01, ARM_Y - 0.026, ARM_Z + 0.006), (COVER["x1"] - 0.01, ARM_Y + 0.026, ARM_Z + 0.040)),
                cyl("X", (-0.02, ARM_Y, ARM_Z + 0.03), 0.018, 0.09, 8))
    part("Placeholder_Prosthetic_BayMotor", bay, ph, "M_WB_Steel", origin=(-0.01, ARM_Y, ARM_Z + 0.04), role="placeholder")
    # 盖板：顶部弧形板，原点 = 抓取点（盖板顶面中心）
    cv = box((COVER["x0"], ARM_Y - COVER["half_w"], ARM_Z + 0.040), (COVER["x1"], ARM_Y + COVER["half_w"], ARM_Z + 0.047))
    part("Placeholder_Prosthetic_Cover", cv, ph, "M_WB_Placeholder", origin=((COVER["x0"] + COVER["x1"]) / 2, ARM_Y, ARM_Z + 0.047),
         role="placeholder_removable", grab_point="cover top center", removal="lift +Z 40 mm, then to mat parking zone COVER",
         park_point=list(COVER_PARK))
    for i, (sx, sy) in enumerate(SCREWS):
        s = merge(cyl("Z", (sx, ARM_Y + sy, ARM_Z + 0.0485), 0.0035, 0.003, 8), cyl("Z", (sx, ARM_Y + sy, ARM_Z + 0.040), 0.0016, 0.014, 6))
        part(f"Placeholder_Prosthetic_Screw_{i + 1}", s, ph, "M_WB_Steel", origin=(sx, ARM_Y + sy, ARM_Z + 0.050), role="placeholder_removable",
             grab_point="screw head top (tool point)", tool_axis_local="Z", removal="unscrew +Z, then to Tray_Screws slot")
    # 腕部与手（合并：块状手掌 + 指段）
    hand = [box((-0.31, ARM_Y - 0.040, ARM_Z - 0.030), (-0.22, ARM_Y + 0.040, ARM_Z + 0.025))]
    for i in range(4):
        y = ARM_Y - 0.030 + i * 0.02
        hand.append(box((-0.39, y - 0.008, ARM_Z - 0.012), (-0.31, y + 0.008, ARM_Z + 0.006)))
    hand.append(box((-0.30, ARM_Y + 0.040, ARM_Z - 0.02), (-0.25, ARM_Y + 0.060, ARM_Z + 0.0)))
    part("Placeholder_Prosthetic_Hand", merge(*hand), ph, "M_WB_Placeholder", origin=(-0.265, ARM_Y, ARM_Z - 0.03), role="placeholder")
    # 肘端接口（检查点）与故障指示（唯一的红色之一）
    conn = merge(cyl("X", (0.215, ARM_Y, ARM_Z), 0.034, 0.03, 8), box((0.228, ARM_Y - 0.016, ARM_Z - 0.008), (0.236, ARM_Y + 0.016, ARM_Z + 0.008)))
    part("Placeholder_Prosthetic_Connector", conn, ph, "M_WB_Steel", origin=(0.236, ARM_Y, ARM_Z), role="placeholder_inspectable",
         grab_point="interface face center", note="probe target")
    part("Placeholder_Prosthetic_FaultLED", cyl("Y", (0.215, ARM_Y - 0.0335, ARM_Z + 0.014), 0.0045, 0.006, 6), ph, "M_WB_FaultRed",   # 接口朝玩家的一面
         origin=(0.215, ARM_Y - 0.0365, ARM_Z + 0.014), role="fault_indicator", note="red = real fault only")
    # 占位标签
    paper_part("Placeholder_Prosthetic_Tag", (-0.20, ARM_Y + 0.047, ARM_Z - 0.01), (-0.15, ARM_Y + 0.049, ARM_Z + 0.012), R_PH_TAG, ph,
               role="placeholder", axis="-Y")


def build_trays(C):
    T = BENCH_TOP
    # 螺钉托盘：4 格，磁性底；原点 = 前沿提手顶面中心
    x0, x1, y0, y1 = 0.40, 0.54, 0.22, 0.36
    walls = [box((x0, y0, T), (x1, y1, T + 0.004)), box((x0, y0, T), (x1, y0 + 0.003, T + 0.022)), box((x0, y1 - 0.003, T), (x1, y1, T + 0.022)),
             box((x0, y0, T), (x0 + 0.003, y1, T + 0.022)), box((x1 - 0.003, y0, T), (x1, y1, T + 0.022)),
             box(((x0 + x1) / 2 - 0.0015, y0, T), ((x0 + x1) / 2 + 0.0015, y1, T + 0.018)), box((x0, (y0 + y1) / 2 - 0.0015, T), (x1, (y0 + y1) / 2 + 0.0015, T + 0.018)),
             box((x0 + 0.04, y0 - 0.02, T + 0.012), (x1 - 0.04, y0 + 0.002, T + 0.018))]
    part("Tray_Screws", merge(*walls), C["Trays"], "M_WB_Steel", origin=((x0 + x1) / 2, y0 - 0.02, T + 0.018), role="removable",
         grab_point="front tab", slots="4 compartments; slot for placeholder screws at front-left", bevel=0.001)
    # 托盘里已有的旧螺钉（合并，跟托盘一起拿走 → 作为子对象）
    old = [cyl("Z", (x0 + 0.03 + 0.012 * k, y1 - 0.025 - 0.01 * (k % 2), T + 0.007), 0.0035, 0.005, 6) for k in range(6)]
    part("Tray_Screws_Contents", merge(*old), C["Trays"], "M_WB_Steel", origin=((x0 + x1) / 2, (y0 + y1) / 2, T + 0.004), role="tray_contents")
    # 旧件托盘：浅周转盘，内有旧轴承、接头（合并为内容物）
    x0, x1, y0, y1 = 0.36, 0.54, 0.42, 0.60
    walls = [box((x0, y0, T), (x1, y1, T + 0.004)), box((x0, y0, T), (x1, y0 + 0.004, T + 0.035)), box((x0, y1 - 0.004, T), (x1, y1, T + 0.035)),
             box((x0, y0, T), (x0 + 0.004, y1, T + 0.035)), box((x1 - 0.004, y0, T), (x1, y1, T + 0.035)),
             box((x0 + 0.05, y0 - 0.018, T + 0.025), (x1 - 0.05, y0 + 0.002, T + 0.032))]
    part("Tray_OldParts", merge(*walls), C["Trays"], "M_WB_Rubber", origin=((x0 + x1) / 2, y0 - 0.018, T + 0.032), role="removable",
         grab_point="front handle", bevel=0.001)
    stuff = [cyl("Z", (0.40, 0.47, T + 0.010), 0.016, 0.010, 10), cyl("Z", (0.44, 0.52, T + 0.012), 0.020, 0.012, 10),
             box((0.47, 0.45, T + 0.004), (0.52, 0.48, T + 0.020)), tube([(0.39, 0.56, T + 0.008), (0.45, 0.57, T + 0.010), (0.50, 0.55, T + 0.008)], 0.004)]
    part("Tray_OldParts_Contents", merge(*stuff), C["Trays"], "M_WB_Steel", origin=((x0 + x1) / 2, (y0 + y1) / 2, T + 0.004), role="tray_contents")


def build_toolbox(C):
    T = BENCH_TOP
    x0, x1, y0, y1 = 0.56, 0.90, 0.70, 0.92
    base = merge(box((x0, y0, T), (x1, y1, T + 0.004)), box((x0, y0, T), (x1, y0 + 0.006, T + 0.15)), box((x0, y1 - 0.006, T), (x1, y1, T + 0.15)),
                 box((x0, y0, T), (x0 + 0.006, y1, T + 0.15)), box((x1 - 0.006, y0, T), (x1, y1, T + 0.15)))
    part("Toolbox_Base", base, C["Toolbox"], "M_WB_PaintGreen", origin=((x0 + x1) / 2, (y0 + y1) / 2, T), role="inspectable", bevel=0.002)
    part("Toolbox_BaseContents", merge(box((x0 + 0.02, y0 + 0.02, T + 0.004), (x0 + 0.16, y1 - 0.02, T + 0.06)),
                                       cyl("X", (0.80, 0.80, T + 0.03), 0.02, 0.14, 8)), C["Clutter"], "M_WB_Steel",
         origin=((x0 + x1) / 2, (y0 + y1) / 2, T + 0.004), role="clutter")
    tiers = [("Toolbox_Tier1", 0.60, 0.80, T + 0.15, 0.04), ("Toolbox_Tier2", 0.50, 0.70, T + 0.20, 0.035)]
    for nm, ty0, ty1, tz, th in tiers:
        tb = merge(box((x0 + 0.01, ty0, tz), (x1 - 0.01, ty1, tz + 0.004)), box((x0 + 0.01, ty0, tz), (x1 - 0.01, ty0 + 0.004, tz + th)),
                   box((x0 + 0.01, ty1 - 0.004, tz), (x1 - 0.01, ty1, tz + th)), box((x0 + 0.01, ty0, tz), (x0 + 0.014, ty1, tz + th)),
                   box((x1 - 0.014, ty0, tz), (x1 - 0.01, ty1, tz + th)),
                   box((x0 + 0.01 + (x1 - x0 - 0.02) / 3, ty0, tz), (x0 + 0.014 + (x1 - x0 - 0.02) / 3, ty1, tz + th * 0.8)),
                   box((x0 + 0.01 + 2 * (x1 - x0 - 0.02) / 3, ty0, tz), (x0 + 0.014 + 2 * (x1 - x0 - 0.02) / 3, ty1, tz + th * 0.8)))
        part(nm, tb, C["Toolbox"], "M_WB_PaintGreen", origin=((x0 + x1) / 2, ty0, tz + th), role="inspectable",
             grab_point="front lip center", note="cantilever tier, shown extended", bevel=0.001)
        # 层内工具（合并）
        stuff = [tube([(x0 + 0.03 + 0.03 * k, ty0 + 0.02, tz + 0.012), (x0 + 0.03 + 0.03 * k, ty1 - 0.02, tz + 0.012)], 0.005, 6) for k in range(3)]
        stuff += [cyl("Z", (x1 - 0.06 + 0.02 * (k % 2), ty0 + 0.04 + 0.04 * (k // 2), tz + 0.01), 0.008, 0.01, 8) for k in range(4)]
        part(nm + "_Contents", merge(*stuff), C["Clutter"], "M_WB_Yellow", origin=((x0 + x1) / 2, (ty0 + ty1) / 2, tz + 0.004), role="clutter")
    arms = [box((x - 0.004, 0.62, T + 0.10), (x + 0.004, 0.78, T + 0.11)) for x in (x0 + 0.004, x1 - 0.004)]
    arms += [box((x - 0.004, 0.55, T + 0.15), (x + 0.004, 0.72, T + 0.16)) for x in (x0 + 0.004, x1 - 0.004)]
    for a in arms[:2]:
        rot(a, "X", 25, (0, 0.70, T + 0.10))
    for a in arms[2:]:
        rot(a, "X", 18, (0, 0.62, T + 0.15))
    part("Toolbox_Arms", merge(*arms), C["Toolbox"], "M_WB_Steel", origin=((x0 + x1) / 2, 0.70, T + 0.10), role="static")
    lid = merge(box((x0, y1 - 0.006, T + 0.15), (x1, y1, T + 0.36)), box((x0 + 0.12, y1 - 0.03, T + 0.30), (x1 - 0.12, y1 - 0.006, T + 0.31)))
    part("Toolbox_Lid", lid, C["Toolbox"], "M_WB_PaintGreen", origin=((x0 + x1) / 2, y1, T + 0.15), role="inspectable",
         hinge_axis_local="X", note="lid hinged at the back top edge, shown open (upright)", bevel=0.002)
    # 盖上的手写胶带标签
    paper_part("Toolbox_LidLabel", (x0 + 0.06, y1 - 0.0075, T + 0.20), (x1 - 0.06, y1 - 0.0062, T + 0.235), R_TAPE, C["Clutter"], axis="-Y")


def build_diagnostic(C):
    T = BENCH_TOP
    x0, x1, y0, y1 = -0.86, -0.44, 0.62, 0.92
    body = merge(box((x0, y0 + 0.06, T + 0.02), (x1, y1, T + 0.26)), box((x0, y0, T + 0.02), (x1, y0 + 0.06, T + 0.14)),
                 box((x0 + 0.02, y0 + 0.02, T), (x0 + 0.06, y0 + 0.06, T + 0.02)), box((x1 - 0.06, y0 + 0.02, T), (x1 - 0.02, y0 + 0.06, T + 0.02)),
                 box((x0 + 0.02, y1 - 0.06, T), (x0 + 0.06, y1 - 0.02, T + 0.02)), box((x1 - 0.06, y1 - 0.06, T), (x1 - 0.02, y1 - 0.02, T + 0.02)),
                 box((x0 + 0.03, y0 + 0.10, T + 0.26), (x0 + 0.20, y0 + 0.24, T + 0.27)))
    part("Diag_Body", body, C["Diagnostic"], "M_WB_Ivory", origin=((x0 + x1) / 2, (y0 + y1) / 2, T), role="inspectable", bevel=0.003,
         note="diagnostic unit casing; mounting face = feet")
    # 屏幕（波形，自发光）：前上斜面
    scr = box((x0 + 0.03, y0 + 0.055, T + 0.15), (x0 + 0.23, y0 + 0.062, T + 0.245))
    part("Diag_Screen", scr, C["Diagnostic"], "M_WB_Screen", origin=(x0 + 0.13, y0 + 0.055, T + 0.1975), role="inspectable", uv="planar",
         uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=0.20, v_size=0.095), note="waveform screen")
    # 实体旋钮（原点 = 轴心在面板上，绕本地 -Y 转）
    for nm, kx, kz in (("Diag_Knob_Gain", x0 + 0.28, T + 0.215), ("Diag_Knob_Time", x0 + 0.36, T + 0.215), ("Diag_Knob_Offset", x0 + 0.28, T + 0.165)):
        k = merge(cyl("Y", (kx, y0 + 0.045, kz), 0.018, 0.022, 10), box((kx - 0.002, y0 + 0.032, kz + 0.006), (kx + 0.002, y0 + 0.036, kz + 0.016)))
        part(nm, k, C["Diagnostic"], "M_WB_Rubber", origin=(kx, y0 + 0.056, kz), role="inspectable", rot_axis_local="Y", smooth=True)
    # 电源拨杆、状态灯（绿 = 正常；红 = 故障，只此一处）
    sw = merge(box((x0 + 0.37, y0 + 0.05, T + 0.155), (x0 + 0.39, y0 + 0.06, T + 0.175)),
               tube([(x0 + 0.38, y0 + 0.05, T + 0.165), (x0 + 0.38, y0 + 0.025, T + 0.18)], 0.003, 6))
    part("Diag_Switch_Power", sw, C["Diagnostic"], "M_WB_Steel", origin=(x0 + 0.38, y0 + 0.06, T + 0.165), role="inspectable", rot_axis_local="X")
    part("Diag_LED_OK", cyl("Y", (x0 + 0.33, y0 + 0.058, T + 0.245), 0.005, 0.006, 6), C["Diagnostic"], "M_WB_Screen",
         origin=(x0 + 0.33, y0 + 0.06, T + 0.245), role="indicator", uv="box")
    part("Diag_LED_Fault", cyl("Y", (x0 + 0.36, y0 + 0.058, T + 0.245), 0.005, 0.006, 6), C["Diagnostic"], "M_WB_FaultRed",
         origin=(x0 + 0.36, y0 + 0.06, T + 0.245), role="fault_indicator", note="red = real fault only")
    # 探针插座（下排）
    jacks = merge(cyl("Y", (x0 + 0.10, y0 - 0.002, T + 0.08), 0.010, 0.012, 8), cyl("Y", (x0 + 0.16, y0 - 0.002, T + 0.08), 0.010, 0.012, 8))
    part("Diag_ProbeJacks", jacks, C["Diagnostic"], "M_WB_Steel", origin=(x0 + 0.13, y0 + 0.004, T + 0.08), role="inspectable")
    # 打印纸带（从下排出口垂到台面）
    paper_part("Diag_PrintoutStrip", (x0 + 0.24, y0 - 0.10, T + 0.0015), (x0 + 0.34, y0 + 0.0, T + 0.0025), R_PRINT, C["Records"], role="inspectable_record")
    # 探针（原点 = 握把中心）+ 补过的线（胶带缠绕）
    probe = merge(cyl("X", (-0.48, 0.33, T + 0.012), 0.008, 0.10, 8), cyl("X", (-0.42, 0.33, T + 0.012), 0.003, 0.03, 6, r2=0.0008))
    part("Diag_Probe", probe, C["Diagnostic"], "M_WB_Yellow", origin=(-0.48, 0.33, T + 0.012), role="removable", grab_point="grip center",
         note="handheld probe; tip reaches the placeholder connector", smooth=True)
    cable_pts = [(x0 + 0.10, y0 - 0.008, T + 0.08), (x0 + 0.10, y0 - 0.06, T + 0.02), (-0.62, 0.42, T + 0.008), (-0.56, 0.34, T + 0.008), (-0.53, 0.33, T + 0.012)]
    part("Diag_ProbeCable", tube(cable_pts, 0.004, 6), C["Diagnostic"], "M_WB_Rubber", origin=cable_pts[2], role="cable", note="patched cable (tape wraps)")
    tapes = merge(*[cyl("X", (p[0], p[1], p[2]), 0.0065, 0.018, 6) for p in [(-0.66, 0.47, T + 0.009), (-0.55, 0.385, T + 0.009)]])
    part("Diag_CablePatchTape", tapes, C["Clutter"], "M_WB_Yellow", origin=(-0.60, 0.43, T + 0.009), role="clutter")


def build_lamp(C):
    T = BENCH_TOP
    base_p = Vector((-0.30, 0.90, T))
    shoulder = Vector((-0.30, 0.90, T + 0.08))
    elbow = Vector((-0.30, 0.86, T + 0.58))
    head = Vector((-0.12, 0.56, T + 0.58))   # 灯头高于“臂上方工具空间”
    target = Vector((-0.04, 0.44, MAT_Z))
    clamp = merge(box((-0.34, 0.86, T), (-0.26, 0.96, T + 0.03)), box((-0.34, 0.95, T - 0.06), (-0.26, 0.97, T)),
                  cyl("Z", (-0.30, 0.90, T + 0.055), 0.018, 0.05, 8))
    part("Lamp_Base", clamp, C["Lamp"], "M_WB_PaintGreen", origin=base_p, role="inspectable", note="clamp on bench back edge")
    part("Lamp_ArmLower", merge(tube([shoulder, elbow], 0.008, 6), cyl("X", shoulder, 0.014, 0.03, 8)), C["Lamp"], "M_WB_Steel",
         origin=shoulder, role="inspectable", pivot="shoulder joint", rot_axis_local="X")
    part("Lamp_ArmUpper", merge(tube([elbow, head], 0.007, 6), cyl("X", elbow, 0.013, 0.03, 8)), C["Lamp"], "M_WB_Steel",
         origin=elbow, role="inspectable", pivot="elbow joint", rot_axis_local="X")
    # 灯罩：圆锥，开口指向目标
    shade = bmesh.new()
    bmesh.ops.create_cone(shade, cap_ends=False, segments=10, radius1=0.085, radius2=0.03, depth=0.10)
    d = (target - head).normalized()
    q = Vector((0, 0, -1)).rotation_difference(d)
    bmesh.ops.rotate(shade, cent=(0, 0, 0), matrix=q.to_matrix(), verts=shade.verts)
    bmesh.ops.translate(shade, vec=head + d * 0.05, verts=shade.verts)
    bmesh.ops.recalc_face_normals(shade, faces=shade.faces)
    # 双面：给锥面加厚
    part("Lamp_Head", merge(shade, cyl("X", head, 0.016, 0.04, 8)), C["Lamp"], "M_WB_PaintGreen", origin=head, role="inspectable",
         pivot="head joint", light_target=list(target), note="Unity spot light child points at the mat center", smooth=True)
    bulb = cyl("Z", (0, 0, 0), 0.025, 0.02, 8)
    bmesh.ops.rotate(bulb, cent=(0, 0, 0), matrix=q.to_matrix(), verts=bulb.verts)
    bmesh.ops.translate(bulb, vec=head + d * 0.075, verts=bulb.verts)
    part("Lamp_Bulb", bulb, C["Lamp"], "M_WB_LampWarm", origin=head + d * 0.075, role="light_fixture")


def build_storage_and_clutter(C):
    T = BENCH_TOP
    cl = C["Clutter"]
    # 洞洞板工具（钩子 + 起子 + 钳子 + 扳手 + 线圈 + 手套）
    tools = []
    for i, x in enumerate((-0.62, -0.55, -0.48, -0.41)):
        tools.append(cyl("Z", (x, 1.01, 1.38), 0.009, 0.09, 6))            # 手柄
        tools.append(cyl("Z", (x, 1.01, 1.27), 0.0025, 0.13, 6))           # 杆
    tools += [box((-0.28, 1.005, 1.25), (-0.24, 1.02, 1.45)), box((-0.22, 1.005, 1.25), (-0.18, 1.02, 1.45))]   # 钳子两腿
    tools += [box((0.02, 1.005, 1.20), (0.05, 1.02, 1.50)), cyl("Y", (0.035, 1.012, 1.52), 0.03, 0.015, 8)]       # 扳手
    tools += [tube([(0.25 + 0.06 * math.cos(a), 1.0, 1.40 + 0.06 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 13)], 0.006, 5)]  # 线圈
    tools += [box((0.45, 1.0, 1.20), (0.58, 1.03, 1.42))]                  # 挂着的手套
    part("Clutter_PegboardTools", merge(*tools), cl, "M_WB_Rubber", origin=(0, 1.02, 1.2), role="clutter")
    part("Clutter_PegboardHandles", merge(*[cyl("Z", (x, 1.01, 1.38), 0.0095, 0.09, 6) for x in (-0.62, -0.55, -0.48, -0.41)]),
         cl, "M_WB_Yellow", origin=(-0.5, 1.01, 1.33), role="clutter")
    # 上层搁板：零件盒（贴手写标签）、活页夹、纸箱
    bins, labels = [], []
    for i in range(6):
        x = -0.90 + i * 0.15
        bins.append(merge(box((x, 0.82, 1.825), (x + 0.13, 1.02, 1.835)), box((x, 0.82, 1.825), (x + 0.13, 0.83, 1.93)),
                          box((x, 0.82, 1.825), (x + 0.005, 1.02, 1.95)), box((x + 0.125, 0.82, 1.825), (x + 0.13, 1.02, 1.95))))
        labels.append((x + 0.015, i))
    part("Storage_PartsBins", merge(*bins), C["Storage"], "M_WB_PaintGreen", origin=(-0.5, 1.02, 1.825), role="storage")
    for x, i in labels:
        paper_part(f"Storage_BinLabel_{i + 1}", (x, 0.8185, 1.86), (x + 0.10, 0.8195, 1.89), R_LABEL[i], C["Storage"], role="label", axis="-Y")
    binders = [box((0.02 + k * 0.035, 0.85, 1.825), (0.05 + k * 0.035, 1.02, 2.10 - 0.01 * (k % 3))) for k in range(7)]
    part("Clutter_Binders", merge(*binders), cl, "M_WB_PaintGreen", origin=(0.14, 1.02, 1.825), role="clutter")
    part("Clutter_ShelfBoxes", merge(box((0.30, 0.83, 1.825), (0.55, 1.02, 1.98)), box((0.58, 0.85, 1.825), (0.90, 1.02, 1.92)),
                                     box((0.62, 0.86, 1.92), (0.82, 1.0, 2.0))), cl, "M_WB_Wood", origin=(0.6, 1.02, 1.825), role="clutter")
    # 右侧高货架：框架 + 旧义肢外壳、旧设备、线束（合并）
    sx0, sx1, sy0, sy1 = 1.05, 1.55, 0.25, 1.0
    frame = [box((x, y, 0.0), (x + 0.03, y + 0.03, 2.0)) for x in (sx0, sx1 - 0.03) for y in (sy0, sy1 - 0.03)]
    frame += [box((sx0, sy0, z), (sx1, sy1, z + 0.02)) for z in (0.25, 0.75, 1.25, 1.75)]
    part("Storage_Shelving", merge(*frame), C["Storage"], "M_WB_Steel", origin=((sx0 + sx1) / 2, sy1, 0), role="storage")
    sh = [cyl("Y", (1.20, 0.6, 0.86), 0.05, 0.38, 8), cyl("Y", (1.40, 0.62, 0.84), 0.045, 0.30, 8),        # 旧义肢外壳
          box((1.10, 0.35, 1.27), (1.32, 0.62, 1.50)), box((1.36, 0.40, 1.27), (1.52, 0.90, 1.40)),          # 旧设备、旧 CRT
          box((1.08, 0.30, 0.27), (1.50, 0.95, 0.50)), box((1.10, 0.40, 1.77), (1.45, 0.90, 1.95))]          # 箱子
    sh += [tube([(1.30 + 0.07 * math.cos(a), 0.30, 0.95 + 0.07 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 11)], 0.008, 5)]
    part("Clutter_Shelving", merge(*sh), cl, "M_WB_Ivory", origin=(1.30, 0.62, 0.25), role="clutter")
    part("Clutter_ShelvingCRT", box((1.38, 0.39, 1.30), (1.50, 0.40, 1.38)), cl, "M_WB_Screen", origin=(1.44, 0.39, 1.30), role="clutter",
         uv="planar", uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=0.12, v_size=0.08))
    # 左墙：便签、工单、照片、补线（墙面杂物集中在墙）
    for i, (y, z, r) in enumerate([(0.55, 1.45, R_WO[0]), (0.35, 1.50, R_WO[1]), (0.70, 1.20, R_NOTE[0]), (0.45, 1.22, R_NOTE[1]),
                                   (0.20, 1.30, R_NOTE[2]), (0.85, 1.55, R_PHOTO)]):
        w = 0.14 if r in R_WO else 0.07
        h = 0.19 if r in R_WO else 0.07
        paper_part(f"Records_WallNote_{i + 1}", (-1.6, y - w / 2, z - h / 2), (-1.598, y + w / 2, z + h / 2), r, C["Records"], role="record", axis="X")
    for i, (x, z, r) in enumerate([(-0.70, 1.62, R_NOTE[3]), (0.70, 1.55, R_WO[2]), (-0.05, 1.66, R_NOTE[0])]):
        w = 0.14 if r in R_WO else 0.07
        h = 0.19 if r in R_WO else 0.07
        paper_part(f"Records_PegNote_{i + 1}", (x - w / 2, 1.018, z - h / 2), (x + w / 2, 1.019, z + h / 2), r, C["Records"], role="record", axis="-Y")
    # 补线：墙上线槽里拉出的线，用胶带补过
    wire = tube([(-1.58, 0.9, 2.2), (-1.58, 0.9, 1.6), (-1.55, 0.98, 1.2), (-1.2, 1.0, 1.0), (-0.9, 1.0, T + 0.05)], 0.006, 6)
    part("Clutter_PatchedWallCable", merge(wire, cyl("Z", (-1.58, 0.9, 1.85), 0.011, 0.03, 6), cyl("Z", (-1.58, 0.9, 1.65), 0.011, 0.03, 6)),
         cl, "M_WB_Rubber", origin=(-1.2, 1.0, 1.0), role="clutter")
    # 台面边缘杂物：积压工单堆、维修记录本、马克杯笔筒、保温壶、胶带卷、焊锡卷、抹布、旧轴承盒
    paper_part("Records_InboxStack", (-0.88, 0.20, T + 0.0), (-0.70, 0.42, T + 0.035), R_WO[1], C["Records"], role="inspectable_record",
               note="backlog of work orders")
    paper_part("Records_RepairLog", (-0.66, 0.18, T + 0.0), (-0.47, 0.30, T + 0.012), R_LOG, C["Records"], role="inspectable_record",
               note="open repair log book")
    part("Records_Clipboard", merge(box((-1.598, 0.0, 1.10), (-1.588, 0.22, 1.40)), box((-1.590, 0.08, 1.38), (-1.580, 0.14, 1.41))),
         C["Records"], "M_WB_Wood", origin=(-1.588, 0.11, 1.40), role="inspectable_record", grab_point="clip top")
    paper_part("Records_ClipboardSheet", (-1.588, 0.01, 1.11), (-1.587, 0.21, 1.37), R_CLIP, C["Records"], role="record", axis="X")
    mug = merge(cyl("Z", (-0.38, 0.86, T + 0.05), 0.04, 0.10, 10), *[tube([(-0.38 + 0.01 * k, 0.86, T + 0.05), (-0.39 + 0.015 * k, 0.87, T + 0.20)], 0.004, 5) for k in range(3)])
    part("Clutter_MugPens", mug, cl, "M_WB_Ivory", origin=(-0.38, 0.86, T), role="clutter", smooth=True)
    part("Clutter_Thermos", cyl("Z", (-0.20, 0.92, T + 0.12), 0.035, 0.24, 10), cl, "M_WB_PaintGreen", origin=(-0.20, 0.92, T), role="clutter", smooth=True)
    part("Clutter_TapeAndSolder", merge(cyl("Z", (0.25, 0.85, T + 0.02), 0.04, 0.04, 12), cyl("Z", (0.36, 0.88, T + 0.025), 0.03, 0.05, 10)),
         cl, "M_WB_Yellow", origin=(0.30, 0.86, T), role="clutter", smooth=True)
    part("Clutter_Rag", box((0.10, 0.80, T), (0.22, 0.94, T + 0.012)), cl, "M_WB_Ivory", origin=(0.16, 0.87, T), role="clutter")
    part("Box_Bearings", merge(box((0.60, 0.22, T), (0.70, 0.32, T + 0.05)), box((0.60, 0.31, T + 0.05), (0.70, 0.32, T + 0.11))),
         C["Trays"], "M_WB_PaintGreen", origin=(0.65, 0.22, T + 0.05), role="inspectable", note="old bearing box, lid open")
    # 私人物品：外套挂在左墙钩上、照片（已在墙上）、小收音机
    part("Clutter_JacketOnHook", merge(box((-1.60, -0.45, 1.05), (-1.48, -0.10, 1.70)), cyl("X", (-1.57, -0.28, 1.72), 0.01, 0.06, 6)),
         cl, "M_WB_PaintGreen", origin=(-1.6, -0.28, 1.72), role="clutter")
    part("Clutter_Radio", merge(box((0.70, 0.84, T), (0.88, 0.92, T + 0.10)), tube([(0.86, 0.90, T + 0.10), (0.92, 0.94, T + 0.28)], 0.002, 4)),
         cl, "M_WB_Ivory", origin=(0.79, 0.88, T), role="clutter")
    # 地面：凳子（推到左侧）、柜下的旧件箱
    stool = [cyl("Z", (-0.85, -0.25, 0.62), 0.17, 0.04, 12)] + [tube([(-0.85 + 0.12 * math.cos(a), -0.25 + 0.12 * math.sin(a), 0.6), (-0.85 + 0.16 * math.cos(a), -0.25 + 0.16 * math.sin(a), 0.0)], 0.012, 5)
                                                                 for a in (0.4, 2.0, 3.6, 5.2)]
    part("Furniture_Stool", merge(*stool), C["Room"], "M_WB_Steel", origin=(-0.85, -0.25, 0.0), role="static")
    part("Clutter_FloorPartsBox", box((0.05, 0.55, 0.14), (0.42, 0.90, 0.40)), cl, "M_WB_Wood", origin=(0.235, 0.725, 0.14), role="clutter")


# ===========================================================================
# 5. 检查：台面高度、镜头遮挡、小零件屏幕尺寸、手部接近、托盘取放、拆装路径
# ===========================================================================
CAMERAS = {
    "Game":     dict(loc=(0.0, -0.80, 1.62), target=(0.0, 0.48, 0.92), fov_v=50.0),
    "CloseUp":  dict(loc=(0.05, -0.12, 1.36), target=(-0.02, 0.44, ARM_Z), fov_v=38.0),
}
SCREEN = (1920, 1080)


def world_geo(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    verts, polys, owner = [], [], []
    for o in objs:
        eo = o.evaluated_get(dg)
        me = eo.to_mesh()
        base = len(verts)
        verts.extend(eo.matrix_world @ v.co for v in me.vertices)
        for p in me.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
        eo.to_mesh_clear()
    return verts, polys, owner


def box_bvh(lo, hi):
    lo, hi = Vector(lo), Vector(hi)
    v = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return BVHTree.FromPolygons(v, f)


def hits_in_box(geo, lo, hi, exclude=()):
    v, p, own = geo
    lo, hi = Vector(lo), Vector(hi)
    tree = BVHTree.FromPolygons(v, p)
    ov = tree.overlap(box_bvh(lo, hi))
    names = {own[i] for i, _ in ov}
    for i, poly in enumerate(p):     # 完全落在盒内的面
        if own[i] in names:
            continue
        c = sum((v[k] for k in poly), Vector()) / len(poly)
        if all(lo[a] < c[a] < hi[a] for a in range(3)):
            names.add(own[i])
    return sorted(n for n in names if n not in exclude)


def run_checks(objs_by_name):
    rep = {"bench_top_z_m": None, "cameras": {}, "checks": []}

    def add(name, ok, **kw):
        rep["checks"].append(dict(name=name, passed=bool(ok), **kw))
        print("[check]", "PASS" if ok else "FAIL", name, kw)

    all_meshes = [o for o in objs_by_name.values() if o.type == "MESH"]
    geo_all = world_geo(all_meshes)
    tree_all = BVHTree.FromPolygons(geo_all[0], geo_all[1])

    # 1. 台面高度
    top = objs_by_name["Bench_Top"]
    v, _, _ = world_geo([top])
    rep["bench_top_z_m"] = round(max(p.z for p in v), 4)
    add("bench_top_height_0_90", abs(rep["bench_top_z_m"] - 0.90) < 1e-4, top_z=rep["bench_top_z_m"])

    # 2. 镜头遮挡 + 小零件屏幕尺寸（按 1920×1080、竖直视场）
    targets = {
        "screws": [n for n in objs_by_name if n.startswith("Placeholder_Prosthetic_Screw_")],
        "cover": ["Placeholder_Prosthetic_Cover"], "connector": ["Placeholder_Prosthetic_Connector"],
        "fault_led": ["Placeholder_Prosthetic_FaultLED"], "tray_screws": ["Tray_Screws"], "tray_oldparts": ["Tray_OldParts"],
        "probe": ["Diag_Probe"], "diag_screen": ["Diag_Screen"], "diag_knobs": ["Diag_Knob_Gain", "Diag_Knob_Time", "Diag_Knob_Offset"],
        "mat_center": ["Bench_Mat"],
    }
    for cname, cam in CAMERAS.items():
        eye = Vector(cam["loc"])
        crep = {}
        for tname, names in targets.items():
            vis = tot = 0
            px_sizes = []
            blockers = {}
            for n in names:
                o = objs_by_name[n]
                ov, op, _ = world_geo([o])
                # 采样：朝向镜头一侧的面中心（背向镜头的面本来就看不到，不计）
                pts = []
                for poly in op:
                    c = sum((ov[k] for k in poly), Vector()) / len(poly)
                    nrm = (ov[poly[1]] - ov[poly[0]]).cross(ov[poly[2]] - ov[poly[1]])
                    if nrm.length > 1e-12 and nrm.normalized().dot((eye - c).normalized()) > 0.05:
                        pts.append(c)
                if tname == "screws":   # 只看钉头顶面：中心 + 一圈 4 点
                    t = o.matrix_world.translation
                    pts = [t + Vector((dx, dy, -0.0002)) for dx, dy in ((0, 0), (0.002, 0), (-0.002, 0), (0, 0.002), (0, -0.002))]
                if tname == "mat_center":
                    pts = [Vector((x, y, MAT_Z + 0.0005)) for x in np.linspace(-0.30, 0.20, 6) for y in np.linspace(0.28, 0.62, 5)]
                    pts = [p for p in pts if not (abs(p.y - ARM_Y) < 0.06 and -0.40 < p.x < 0.25)]   # 去掉被占位件本身盖住的点
                own_set = set(names)
                for p in pts:
                    d = p - eye
                    dist = d.length
                    loc, nrm, idx, hd = tree_all.ray_cast(eye, d.normalized(), dist + 0.01)
                    if loc is None:
                        continue
                    tot += 1
                    hit_owner = geo_all[2][idx]
                    if hit_owner in own_set or (tname == "mat_center" and hit_owner == "Bench_Mat") or (loc - p).length < 0.002:
                        vis += 1
                    else:
                        blockers[hit_owner] = blockers.get(hit_owner, 0) + 1
                # 屏幕尺寸：对象包围球直径投影到像素
                c = sum(ov, Vector()) / len(ov)
                r = 0.0035 if tname == "screws" else max((q - c).length for q in ov)   # 螺钉只算钉头（直径 7 mm）
                dist = (c - eye).length
                px = 2 * r / (2 * dist * math.tan(math.radians(cam["fov_v"] / 2))) * SCREEN[1]
                px_sizes.append(px)
            crep_block = dict(sorted(blockers.items(), key=lambda kv: -kv[1]))
            non_ph = sum(v for k, v in blockers.items() if not k.startswith("Placeholder_") and k not in names and not k.startswith(tuple(n + "_" for n in names)) and k != "Bench_Mat")
            crep[tname] = {"visible_fraction": round(vis / tot, 3) if tot else None, "samples": tot,
                           "min_screen_px": round(min(px_sizes), 1) if px_sizes else None, "occluded_by": crep_block,
                           "occluded_by_clutter_or_props": non_ph}
        rep["cameras"][cname] = {"camera": cam, "targets": crep}
        print("[camera]", cname, {k: (v["visible_fraction"], v["min_screen_px"]) for k, v in crep.items()})
        for k, v in crep.items():
            if v["occluded_by"]:
                print("[occluder]", cname, k, v["occluded_by"])
    gm = rep["cameras"]["Game"]["targets"]; cu = rep["cameras"]["CloseUp"]["targets"]
    add("closeup_sees_screws_cover_connector", all(cu[t]["visible_fraction"] >= 0.5 for t in ("screws", "cover", "connector")),
        screws=cu["screws"]["visible_fraction"], cover=cu["cover"]["visible_fraction"], connector=cu["connector"]["visible_fraction"])
    no_clutter = {c: {t: v["occluded_by_clutter_or_props"] for t, v in rep["cameras"][c]["targets"].items() if v["occluded_by_clutter_or_props"]}
                  for c in CAMERAS}
    add("no_clutter_or_props_occlude_work_targets", not any(no_clutter.values()), offenders=no_clutter,
        note="samples hidden only by the placeholder being repaired, by the target itself or by the mat edge do not count")
    add("game_cam_sees_trays_and_probe", gm["tray_screws"]["visible_fraction"] >= 0.5 and gm["probe"]["visible_fraction"] >= 0.5,
        tray_screws=gm["tray_screws"]["visible_fraction"], probe=gm["probe"]["visible_fraction"],
        mat_center_visible=gm["mat_center"]["visible_fraction"], mat_center_note="the rest is behind the placeholder arm itself")
    add("closeup_screw_heads_at_least_8px", cu["screws"]["min_screen_px"] >= 8, screw_px=cu["screws"]["min_screen_px"],
        note="screw head 7 mm diameter at 1920x1080")

    # 3. 中央操作区不放杂物；双手接近空间（未来 VR）
    ph = {n for n in objs_by_name if n.startswith("Placeholder_")}
    allowed = ph | {"Bench_Mat", "Bench_ArmCradles"}
    central = ((-0.34, 0.24, MAT_Z + 0.0005), (0.20, 0.66, MAT_Z + 0.30))
    add("central_work_area_clear", not hits_in_box(geo_all, *central, exclude=allowed), offenders=hits_in_box(geo_all, *central, exclude=allowed))
    hands = {
        "left_hand_approach": ((-0.36, -0.10, ARM_Z - 0.02), (-0.16, 0.38, ARM_Z + 0.16)),
        "right_hand_approach": ((0.02, -0.10, ARM_Z - 0.02), (0.22, 0.38, ARM_Z + 0.16)),
        "over_arm_tool_space": ((-0.30, 0.38, ARM_Z + 0.06), (0.20, 0.52, ARM_Z + 0.32)),
        "player_standing_knee_space": ((-0.46, -0.45, 0.01), (0.48, 0.13, 1.60)),
    }
    for hn, (lo, hi) in hands.items():
        off = hits_in_box(geo_all, lo, hi, exclude=allowed)
        add(f"hand_zone_{hn}_clear", not off, offenders=off, box=[list(lo), list(hi)])

    # 4. 托盘取放：抬起 8 cm，再朝玩家拉到台前 25 cm 外
    for tn in ("Tray_Screws", "Tray_OldParts"):
        t = objs_by_name[tn]
        parts = [t] + [objs_by_name[n] for n in objs_by_name if n == tn + "_Contents"]
        tv, tp, _ = world_geo(parts)
        lo = Vector((min(p.x for p in tv), min(p.y for p in tv), min(p.z for p in tv)))
        hi = Vector((max(p.x for p in tv), max(p.y for p in tv), max(p.z for p in tv)))
        excl = {p.name for p in parts}
        lift = hits_in_box(geo_all, (lo.x, lo.y, hi.z), (hi.x, hi.y, hi.z + 0.08), exclude=excl)
        pull = hits_in_box(geo_all, (lo.x, -0.25, lo.z + 0.08), (hi.x, hi.y, hi.z + 0.08), exclude=excl)
        add(f"tray_{tn}_lift_and_pull_path_clear", not lift and not pull, lift_hits=lift, pull_hits=pull)

    # 5. 占位件拆装路径：螺钉 → 螺钉托盘；盖板 → 操作垫停放区
    screw_paths = []
    for i, (sx, sy) in enumerate(SCREWS):
        start = Vector((sx, ARM_Y + sy, ARM_Z + 0.05))
        up = start + Vector((0, 0, 0.05))
        over = Vector((SCREW_TRAY_SLOT.x, SCREW_TRAY_SLOT.y, up.z))
        segs = [(start, up), (up, over), (over, SCREW_TRAY_SLOT + Vector((0, 0, 0.004)))]
        for a, b in segs:
            lo = Vector((min(a.x, b.x) - 0.006, min(a.y, b.y) - 0.006, min(a.z, b.z) - 0.002))
            hi = Vector((max(a.x, b.x) + 0.006, max(a.y, b.y) + 0.006, max(a.z, b.z) + 0.006))
            screw_paths += hits_in_box(geo_all, lo, hi, exclude=ph | {"Tray_Screws", "Tray_Screws_Contents"})
    add("placeholder_screws_to_tray_path_clear", not screw_paths, hits=sorted(set(screw_paths)))
    cover = objs_by_name["Placeholder_Prosthetic_Cover"]
    cv, _, _ = world_geo([cover])
    clo = Vector((min(p.x for p in cv), min(p.y for p in cv), min(p.z for p in cv)))
    chi = Vector((max(p.x for p in cv), max(p.y for p in cv), max(p.z for p in cv)))
    size = chi - clo
    lift_box = (clo, chi + Vector((0, 0, 0.05)))
    over_lo = Vector((min(clo.x, COVER_PARK.x - size.x / 2), min(clo.y, COVER_PARK.y - size.y / 2), chi.z + 0.03))
    over_hi = Vector((max(chi.x, COVER_PARK.x + size.x / 2), max(chi.y, COVER_PARK.y + size.y / 2), chi.z + 0.05 + size.z))
    down = (Vector((COVER_PARK.x - size.x / 2, COVER_PARK.y - size.y / 2, MAT_Z + 0.001)), Vector((COVER_PARK.x + size.x / 2, COVER_PARK.y + size.y / 2, chi.z + 0.05)))
    hits = set()
    for lo, hi in (lift_box, (over_lo, over_hi), down):
        hits |= set(hits_in_box(geo_all, lo, hi, exclude=ph | {"Bench_Mat"}))
    add("placeholder_cover_to_parking_path_clear", not hits, hits=sorted(hits))
    rep["all_passed"] = all(c["passed"] for c in rep["checks"])
    return rep


# ===========================================================================
# 6. 统计、清单、导出、渲染
# ===========================================================================

def tri_count(o):
    return hs.triangle_count(o)


def export_fbx(root, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in [root] + list(root.children_recursive):
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                             mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_triangles=False)


def setup_render(scene):
    cam = hs.setup_studio(scene, 0.0, target=(0, 0.4, 0.9))
    bpy.data.objects["Studio_Floor"].hide_render = True       # 用房间自己的地面
    for n in ("Key", "Fill", "Rim", "RimBack"):
        bpy.data.objects[n].hide_render = True
    world = scene.world
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.020, 0.026, 0.022, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    studio = bpy.data.collections["_Studio"]

    def light(name, kind, loc, energy, color, size=0.2, spot=None, target=None):
        d = bpy.data.lights.new(name, type=kind)
        d.energy = energy
        d.color = color
        if kind == "AREA":
            d.size = size
        if kind == "SPOT":
            d.spot_size = math.radians(spot)
            d.spot_blend = 0.4
            d.shadow_soft_size = 0.03
        o = bpy.data.objects.new(name, d)
        o.location = loc
        if target is not None:
            hs.look_at(o, Vector(target))
        studio.objects.link(o)
        return o
    head = bpy.data.objects["Lamp_Head"].matrix_world.translation
    bulb = bpy.data.objects["Lamp_Bulb"].matrix_world.translation
    light("WorkLamp_Spot", "SPOT", bulb, 90, (1.0, 0.80, 0.55), spot=70, target=(-0.04, 0.44, MAT_Z))
    light("Ceiling_Fluo", "AREA", (0, 0.25, 2.33), 85, (0.80, 0.92, 0.85), size=1.0, target=(0, 0.25, 0.0))
    light("Room_Fill", "AREA", (0.2, -1.4, 1.9), 40, (0.70, 0.82, 0.75), size=1.5, target=(0, 0.6, 1.0))
    scene.cycles.samples = 96
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for dv in prefs.devices:
            dv.use = dv.type == "OPTIX"
        scene.cycles.device = "GPU"
    except Exception as e:
        print("[render] GPU unavailable:", e)
    return cam


def main():
    scene, C = hs.reset_scene("WorkbenchArea", GROUPS, "WB_")
    images = {
        "T_WB_Grain.png": hs.save_image("T_WB_Grain.png", grain_texture(), TEX_DIR),
        "T_WB_Floor.png": hs.save_image("T_WB_Floor.png", floor_texture(), TEX_DIR),
        "T_WB_Paper.png": hs.save_image("T_WB_Paper.png", paper_texture(), TEX_DIR),
        "T_WB_Screen.png": hs.save_image("T_WB_Screen.png", screen_texture(), TEX_DIR),
        "T_WB_Mat.png": hs.save_image("T_WB_Mat.png", mat_texture(), TEX_DIR),
    }
    for img in images.values():
        img.colorspace_settings.name = "sRGB"
    build_materials(images)

    build_room(C)
    build_bench(C)
    build_mat(C)
    build_placeholder(C)
    build_trays(C)
    build_toolbox(C)
    build_diagnostic(C)
    build_lamp(C)
    build_storage_and_clutter(C)

    # 根对象与分组空物体（导出层级）
    root = bpy.data.objects.new("WorkbenchArea", None)
    C["Room"].objects.link(root)
    group_roots = {}
    for g in GROUPS:
        e = bpy.data.objects.new(f"WB_{g}", None)
        C[g].objects.link(e)
        e.parent = root
        group_roots[g] = e
    for o, coll_name, role in OBJECTS:
        g = coll_name.replace("WB_", "")
        o.parent = group_roots[g]
    bpy.context.view_layer.update()
    objs = {o.name: o for o, _, _ in OBJECTS}

    rep = run_checks(objs)
    with open(os.path.join(HERE, "checks.json"), "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=2)
    print("[check] all_passed", rep["all_passed"])

    # 统计与资产清单
    stats = {"groups": {}, "materials": sorted(MAT_SPECS.keys()), "textures": {k: list(v.size) for k, v in images.items()}}
    total = 0
    rows = []
    for g in GROUPS:
        ms = [o for o, c, _ in OBJECTS if c == f"WB_{g}"]
        tris = sum(tri_count(o) for o in ms)
        total += tris
        stats["groups"][g] = {"objects": len(ms), "triangles": tris}
    for o, c, role in sorted(OBJECTS, key=lambda t: (t[1], t[0].name)):
        rows.append({"name": o.name, "group": c.replace("WB_", ""), "role": role, "triangles": tri_count(o),
                     "material": o.material_slots[0].material.name if o.material_slots else "",
                     "origin_world": " ".join(f"{v:.3f}" for v in o.matrix_world.translation),
                     "origin_meaning": o.get("grab_point") or o.get("pivot") or ("hinge" if "hinge_axis_local" in o else "") or ("mount face / base center"),
                     "props": "; ".join(f"{k}={o[k]}" for k in o.keys() if k not in ("wb_role",) and not k.startswith("_"))})
    stats["total_triangles"] = total
    stats["mesh_objects"] = len(OBJECTS)
    stats["roles"] = {r: sum(1 for _, _, rr in OBJECTS if rr == r) for r in sorted({r for _, _, r in OBJECTS})}
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    with open(os.path.join(HERE, "asset_inventory.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    print("[stats] triangles", total, "objects", len(OBJECTS), "roles", stats["roles"])

    export_fbx(root, os.path.join(EXPORT_DIR, "WorkbenchArea.fbx"))
    print("[export] done")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "WorkbenchArea.blend"), relative_remap=True)
    print("[save] WorkbenchArea.blend")
    if NO_RENDER:
        return

    cam = setup_render(scene)
    clutter = C["Clutter"]

    def shot(name, loc, target, lens, hide_clutter=False, cutaway=()):
        clutter.hide_render = hide_clutter
        for n in cutaway:
            bpy.data.objects[n].hide_render = True
        hs.render_view(scene, cam, os.path.join(RENDER_DIR, name), loc, target, lens)
        for n in cutaway:
            bpy.data.objects[n].hide_render = False
        print("[render]", name)

    def lens_for(fov_v):   # 竖直视场 → 镜头焦距（16:9，传感器宽 36 mm）
        return 36 / 2 / math.tan(math.radians(fov_v) / 2) * (9 / 16)

    shot("R01_front.png", (0.0, -2.6, 1.35), (0.0, 0.6, 1.15), 30)
    shot("R02_oblique_top.png", (1.6, -1.6, 2.6), (0.0, 0.45, 0.95), 28)
    shot("R03_player_closeup.png", CAMERAS["CloseUp"]["loc"], CAMERAS["CloseUp"]["target"], lens_for(CAMERAS["CloseUp"]["fov_v"]))
    # 侧视为剖切图：隐去右墙与右侧货架，才能看到台面高度、玩家站位与手部空间的侧面轮廓
    shot("R04_side_cutaway.png", (3.4, -0.35, 1.25), (0.0, 0.25, 0.95), 30,
         cutaway=("Room_WallRight", "Storage_Shelving", "Clutter_Shelving", "Clutter_ShelvingCRT"))
    shot("R05_clutter_hidden_work_area.png", (1.6, -1.6, 2.6), (0.0, 0.45, 0.95), 28, hide_clutter=True)
    shot("R06_game_camera.png", CAMERAS["Game"]["loc"], CAMERAS["Game"]["target"], lens_for(CAMERAS["Game"]["fov_v"]))
    clutter.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "WorkbenchArea.blend"), relative_remap=True)


if __name__ == "__main__":
    main()
