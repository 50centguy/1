"""
边境维修站 · UNIT 07（七号）维修座资产包 · Blender 5.2 建模脚本
由 Claude 辅助编写。运行：run_blender.bat（或 blender -b --factory-startup --python build_unit07_dock.py）

生成内容（都在本目录下）：
  Unit07ServiceDock.blend            源文件（含占位体与检查体，已隐藏）
  Export/UNIT07_ServiceDock.fbx      维修座（带层级、原点和自定义属性；不含占位体）
  Export/UNIT07_RobotPlaceholder.fbx 七号尺寸占位体（仅供程序摆位 / 检查，不是美术资产）
  Textures/*.png                     程序化贴图
  Renders/*.png                      Cycles 渲染图
  stats.json / clearance_report.json / materials.json

坐标：Blender Z 向上，机器人正面朝 -Y（与 V4 模型一致），玩家站在 -Y 一侧。单位：米。
不读取、不复制 V4 模型网格；七号占位体只用 V4 交接件的包围盒测量数字（见 ROBOT_BLOCKS）。
"""

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Common"))
import br_hardsurface as hs  # noqa: E402

# 共用字库缺 W、F（POWER / OFF 要用）；只在本脚本运行时补充，不改共用库文件
hs.FONT.setdefault("W", ["10001", "10001", "10001", "10101", "10101", "11011", "10001"])
hs.FONT.setdefault("F", ["11111", "10000", "10000", "11110", "10000", "10000", "10000"])

TEX_DIR = os.path.join(HERE, "Textures")
EXPORT_DIR = os.path.join(HERE, "Export")
RENDER_DIR = os.path.join(HERE, "Renders")
for d in (TEX_DIR, EXPORT_DIR, RENDER_DIR):
    os.makedirs(d, exist_ok=True)

NO_RENDER = "--norender" in sys.argv

# ---------------------------------------------------------------------------
# 1. 七号占位体（机器人坐标：根原点在最低点正下方，Z 向上，正面 -Y）
#    数字来自 V4 交接件的只读包围盒测量（工作姿态 = Idle_Hover 第 0 帧；收起姿态 = Arm_Deploy 第 0 帧）。
#    总包围盒 1.0196 × 0.4775 × 0.6429 m（宽 × 深 × 高），与交接件一致。
# ---------------------------------------------------------------------------
ROBOT_Z0 = 0.72          # 停靠时机器人根原点的世界高度：引擎中心约 1.14 m，站姿双手易达；机身底面 0.913 m
ROBOT_BLOCKS = {
    # 名称: (min, max)，机器人坐标
    "Body_Shell":      ((-0.2176, -0.1900, 0.2600), (0.2176, 0.1900, 0.6360)),
    "Front_Bezel":     ((-0.2166, -0.2195, 0.2690), (0.2166, -0.1902, 0.6350)),
    "Lower_Module":    ((-0.1900, -0.1300, 0.2050), (0.1900, 0.1700, 0.2680)),
    "Power_Tray":      ((-0.1260, -0.1030, 0.2070), (0.1260, 0.1030, 0.2490)),
    "Chin":            ((-0.1680, -0.2258, 0.1882), (0.1680, -0.1180, 0.2634)),
    "Hardpoint_L":     ((0.1240, -0.0210, 0.1930), (0.1760, 0.1910, 0.2050)),
    "Hardpoint_R":     ((-0.1760, -0.0210, 0.1930), (-0.1240, 0.1910, 0.2050)),
    # 背部安装轨按高度分两段：外侧面位置用真实 RobotV4 网格射线实测（夹具夹面所在的下段外侧 |x| ≤ 0.1427）
    "MountRail_L_Lower": ((0.0980, 0.1690, 0.1950), (0.1427, 0.2020, 0.3000)),
    "MountRail_L_Upper": ((0.0980, 0.1690, 0.3000), (0.1437, 0.2020, 0.4300)),
    "MountRail_R_Lower": ((-0.1427, 0.1690, 0.1950), (-0.0980, 0.2020, 0.3000)),
    "MountRail_R_Upper": ((-0.1437, 0.1690, 0.3000), (-0.0980, 0.2020, 0.4300)),
    "Rear_Grille":     ((-0.0800, 0.1690, 0.2150), (0.0800, 0.1750, 0.2590)),
    "Rear_Cassette":   ((-0.0810, 0.1300, 0.3950), (0.0810, 0.2075, 0.5675)),   # 含面板上的螺栓、状态条、警示牌
    "Rear_Bolt_Low_L": ((0.1616, 0.1890, 0.2910), (0.1797, 0.1970, 0.3090)),   # 机背螺栓（下）：紧挨夹具夹面上方
    "Rear_Bolt_Low_R": ((-0.1797, 0.1890, 0.2910), (-0.1616, 0.1970, 0.3090)),
    "Rear_Bolt_High_L": ((0.1621, 0.1890, 0.5619), (0.1802, 0.1970, 0.5801)),
    "Rear_Bolt_High_R": ((-0.1802, 0.1890, 0.5619), (-0.1621, 0.1970, 0.5801)),
    "Rear_Vent_L":     ((0.1297, 0.1890, 0.5576), (0.1723, 0.1930, 0.5953)),
    "Rear_Vent_R":     ((-0.1723, 0.1890, 0.5576), (-0.1297, 0.1930, 0.5953)),
    "Top_Cover":       ((-0.1710, -0.1760, 0.6181), (0.1710, -0.0160, 0.6465)),
    "Engine_L":        ((0.2190, -0.1360, 0.2613), (0.5098, 0.1161, 0.5723)),
    "Engine_R":        ((-0.5098, -0.1359, 0.2622), (-0.2190, 0.1168, 0.5731)),
    "Arm_L_Working":   ((0.1300, -0.2700, 0.0036), (0.2840, 0.0200, 0.1924)),
    "Arm_R_Working":   ((-0.2840, -0.2700, 0.0036), (-0.1300, 0.0200, 0.1924)),
}
ROBOT_STOWED_ARMS = {
    "Arm_L_Stowed":    ((0.1252, -0.1730, 0.0935), (0.2849, 0.0870, 0.2134)),
    "Arm_R_Stowed":    ((-0.2849, -0.1730, 0.0935), (-0.1252, 0.0870, 0.2134)),
}
ROBOT_OVERALL = ((-0.5098, -0.2700, 0.0036), (0.5098, 0.2075, 0.6465))
ROBOT_COM = (-0.002, -0.034, 0.427)   # 均匀密度粗估，仅用于判断支撑是否跨过重心

# 维修座可调尺寸（机器人坐标 y / z）。2026-10-01 按真实 RobotV4 复测调整：
#   托架纵梁、鞍座、接触垫前端后移，使电源托盘完整拆卸路径与维修座 ≥ 15 mm；
#   夹面顶端降低，使夹具开合全程与机背螺栓 ≥ 5 mm。可用环境变量覆盖以便迭代实测。
YOKE_FRONT_Y = float(os.environ.get("DOCK_YOKE_FRONT_Y", 0.118))    # 原 0.095
SADDLE_FRONT_Y = float(os.environ.get("DOCK_SADDLE_FRONT_Y", 0.112))  # 原 0.098
PAD_FRONT_Y = float(os.environ.get("DOCK_PAD_FRONT_Y", 0.114))        # 原 0.100
PAD_REAR_Y = 0.186
JAW_TOP_Z = float(os.environ.get("DOCK_JAW_TOP_Z", 0.272))            # 原 0.284
TRAY_DOCK_MIN_GAP_MM = 15.0
CLAMP_BOLT_MIN_GAP_MM = 5.0


def rw(p):
    """机器人坐标 → 世界坐标"""
    return Vector((p[0], p[1], p[2] + ROBOT_Z0))


# ---------------------------------------------------------------------------
# 2. 贴图
# ---------------------------------------------------------------------------

def make_warning_texture(w=512, h=128, seed=21):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    stripe = ((xx + yy) // 40) % 2 == 0
    yellow = np.array(hs.hex_rgb("#D9A418"))
    black = np.array(hs.hex_rgb("#1C1C1C"))
    img = np.where(stripe[..., None], yellow, black).astype(float)
    grime = hs.periodic_noise(max(w, h), 18, rng)[:h, :w]
    img *= (0.86 + 0.14 * grime)[..., None]
    # 长期踩踏 / 手摸造成的磨耗：黄漆褪色、露出灰底
    wear = hs.periodic_noise(max(w, h), 5, rng)[:h, :w]
    worn = wear > 0.78
    img[worn] = img[worn] * 0.45 + np.array(hs.hex_rgb("#6A6A66")) * 0.55
    scratches = np.zeros((h, w))
    import random as _r
    pr = _r.Random(seed)
    for _ in range(90):
        x0, y0 = pr.uniform(0, w), pr.uniform(0, h)
        a = pr.uniform(-0.3, 0.3)
        L = pr.uniform(10, 60)
        hs.draw_line_wrap(scratches, x0, y0, x0 + math.cos(a) * L, y0 + math.sin(a) * L, pr.uniform(0.15, 0.35))
    img = np.clip(img * (1 - scratches[..., None]) + scratches[..., None] * 0.55, 0, 1)
    return img


LABEL_W = LABEL_H = 512
# 贴图集区域（像素，行 0 在顶部）：x0, y0, x1, y1
REGION_SWITCH = (0, 0, 200, 260)
REGION_UNIT = (0, 300, 300, 380)
REGION_BOX = (320, 0, 512, 96)


def region_uv(region):
    x0, y0, x1, y1 = region
    return x0 / LABEL_W, 1 - y1 / LABEL_H, (x1 - x0) / LABEL_W, (y1 - y0) / LABEL_H


def make_label_texture(seed=33):
    rng = np.random.default_rng(seed)
    img = np.ones((LABEL_H, LABEL_W, 3)) * np.array(hs.hex_rgb("#3A3D40"))
    yellow = np.array(hs.hex_rgb("#E0B02A"))
    ivory = np.array(hs.hex_rgb("#E6DCC4"))
    dark = np.array(hs.hex_rgb("#1E1F21"))
    # 开关铭牌：黄黑斜纹边框 + POWER / ON / OFF
    x0, y0, x1, y1 = REGION_SWITCH
    yy, xx = np.mgrid[y0:y1, x0:x1]
    border = (xx - x0 < 16) | (x1 - xx <= 16) | (yy - y0 < 16) | (y1 - yy <= 16)
    stripe = ((xx + yy) // 14) % 2 == 0
    sub = img[y0:y1, x0:x1]
    sub[border & stripe] = yellow
    sub[border & ~stripe] = dark
    hs.draw_text(img, "POWER", x0 + 30, y0 + 30, 4, ivory)
    hs.draw_text(img, "ON", x0 + 20, y0 + 88, 5, yellow)       # 左侧；手柄在右侧，ON / OFF 两个位置都不挡字
    hs.rect(img, x0 + 20, y0 + 142, x0 + 100, y0 + 146, ivory)
    hs.draw_text(img, "OFF", x0 + 20, y0 + 170, 5, ivory)
    # UNIT 07 铭牌
    x0, y0, x1, y1 = REGION_UNIT
    hs.rect(img, x0, y0, x1, y1, ivory)
    hs.rect_outline(img, x0, y0, x1, y1, 4, dark)
    hs.draw_text(img, "UNIT 07", x0 + 24, y0 + 19, 6, dark)
    # 磁性零件盒标签
    x0, y0, x1, y1 = REGION_BOX
    hs.rect(img, x0, y0, x1, y1, yellow)
    hs.rect_outline(img, x0, y0, x1, y1, 3, dark)
    hs.draw_text(img, "OLD BRG", x0 + 12, y0 + 34, 4, dark)
    # 整张加一层旧化
    grime = hs.periodic_noise(LABEL_W, 10, rng)
    img *= (0.88 + 0.12 * grime)[..., None]
    return np.clip(img, 0, 1)


# ---------------------------------------------------------------------------
# 3. 材质（导出只用前 7 种；占位 / 检查材质不导出）
# ---------------------------------------------------------------------------
MAT_SPECS = {
    "M_Dock_Ivory":   dict(color="#D6CCB2", metallic=0.0, roughness=0.55, base_map="T_Dock_Grime.png"),
    "M_Dock_Gray":    dict(color="#5B6066", metallic=0.35, roughness=0.5, base_map="T_Dock_Grime.png"),
    "M_Dock_Steel":   dict(color="#A2A6AA", metallic=1.0, roughness=0.36, base_map="T_Dock_Grime.png"),
    "M_Dock_Rubber":  dict(color="#1D1E20", metallic=0.0, roughness=0.86),
    "M_Dock_Warning": dict(color="#FFFFFF", metallic=0.0, roughness=0.55, base_map="T_Dock_Warning.png"),
    "M_Dock_Label":   dict(color="#FFFFFF", metallic=0.0, roughness=0.45, base_map="T_Dock_Labels.png"),
    "M_Dock_Lamp":    dict(color="#FFB23A", metallic=0.0, roughness=0.3, emission_color="#FFB23A", emission_strength=4.0),
}
AUX_SPECS = {
    "M_Placeholder_Robot": dict(color="#6F8196", metallic=0.0, roughness=0.8),
    "M_Prop_OldBearing":   dict(color="#8C8378", metallic=1.0, roughness=0.55),
}


# ---------------------------------------------------------------------------
# 4. 几何工具
# ---------------------------------------------------------------------------

def box(lo, hi):
    lo, hi = Vector(lo), Vector(hi)
    return hs.bm_box(tuple(hi - lo), tuple((lo + hi) / 2))


def cyl(axis, center, r, length, segs=24):
    bm, _, _ = hs.bm_prism(hs.circle_profile(r, segs), -length / 2, length)
    if axis == "X":
        hs.rotate_verts(bm, "Y", 90, (0, 0, 0))
    elif axis == "Y":
        hs.rotate_verts(bm, "X", 90, (0, 0, 0))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return bm


def merge(*bms):
    out = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("_tmp")
        b.to_mesh(me)
        out.from_mesh(me)
        bpy.data.meshes.remove(me)
        b.free()
    return out


def make(name, bm, coll, mat, origin=(0, 0, 0), bevel=0.0, segs=2, smooth=True, uv="box", uv_args=None):
    """在世界坐标里建好 bm，再把原点移到 origin（物体位置 = origin，网格相对 origin）。"""
    o = Vector(origin)
    bmesh.ops.translate(bm, vec=-o, verts=bm.verts)
    if uv == "planar":
        hs.planar_uv(bm, **uv_args)
    obj = hs.make_object(name, bm, coll, mat, smooth=smooth, uv=(uv == "box"))
    obj.location = o
    if bevel > 0:
        hs.add_bevel(obj, bevel, segments=segs, angle=35)
    return obj


def parent_keep(child, parent):
    bpy.context.view_layer.update()   # 新建对象的 matrix_world 要刷新后才有效，否则子对象会被重复偏移
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def tag(obj, **props):
    for k, v in props.items():
        obj[k] = v


# ---------------------------------------------------------------------------
# 5. 建模
# ---------------------------------------------------------------------------
GROUPS = ["Frame", "ContactPads", "Clamps", "PowerSwitch", "PartsTray", "MagneticBox"]


def build_dock(G):
    objs = {}
    Z = ROBOT_Z0
    pad_top = Z + 0.193                          # 手臂硬点板底面（接触面）

    # --- 底座：中央底板 + 两条前伸支腿（中间留空，玩家可以站近） ---
    objs["Dock_Base"] = make("Dock_Base", box((-0.24, 0.12, 0.0), (0.24, 0.52, 0.05)), G["Frame"], "M_Dock_Gray", (0, 0.32, 0), bevel=0.008)
    for s, sx in (("L", 1), ("R", -1)):
        beam = merge(box((sx * 0.24, 0.26, 0.0), (sx * 0.48, 0.34, 0.05)) if sx > 0 else box((sx * 0.48, 0.26, 0.0), (sx * 0.24, 0.34, 0.05)),
                     box((min(sx * 0.40, sx * 0.48), -0.30, 0.0), (max(sx * 0.40, sx * 0.48), 0.34, 0.05)))
        objs[f"Dock_Outrigger_{s}"] = make(f"Dock_Outrigger_{s}", beam, G["Frame"], "M_Dock_Gray", (sx * 0.44, 0.02, 0), bevel=0.006)
        # 支腿前端的黄黑警示条
        objs[f"Dock_Warning_Outrigger_{s}"] = make(
            f"Dock_Warning_Outrigger_{s}", box((min(sx * 0.402, sx * 0.478), -0.298, 0.05), (max(sx * 0.402, sx * 0.478), -0.16, 0.052)),
            G["Frame"], "M_Dock_Warning", (sx * 0.44, -0.23, 0.051), smooth=False, uv="planar",
            uv_args=dict(origin=(0, 0, 0), u_axis=(0, 1, 0), v_axis=(1, 0, 0), u_size=0.138, v_size=0.076, u_span=1.0, v_span=1.0))
        # 调平脚
        for fy in (-0.26, 0.30):
            fx = sx * 0.44
            objs[f"Dock_Foot_{s}_{'F' if fy < 0 else 'B'}"] = make(
                f"Dock_Foot_{s}_{'F' if fy < 0 else 'B'}", cyl("Z", (fx, fy, -0.004), 0.028, 0.012, 20), G["Frame"], "M_Dock_Rubber", (fx, fy, -0.01), bevel=0.002)
    objs["Dock_Warning_BaseFront"] = make(
        "Dock_Warning_BaseFront", box((-0.238, 0.122, 0.05), (0.238, 0.16, 0.052)), G["Frame"], "M_Dock_Warning", (0, 0.141, 0.051), smooth=False,
        uv="planar", uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 1, 0), u_size=0.476, v_size=0.038, u_span=3.0, v_span=1.0))

    # --- 立柱：工业灰框架 + 象牙白护板 + UNIT 07 铭牌 ---
    objs["Dock_Column"] = make("Dock_Column", box((-0.10, 0.26, 0.05), (0.10, 0.40, 0.86)), G["Frame"], "M_Dock_Gray", (0, 0.33, 0.05), bevel=0.01)
    objs["Dock_Column_Panel"] = make("Dock_Column_Panel", box((-0.088, 0.252, 0.14), (0.088, 0.262, 0.80)), G["Frame"], "M_Dock_Ivory", (0, 0.257, 0.47), bevel=0.004)
    u0, v0, us, vs = region_uv(REGION_UNIT)
    objs["Dock_Label_Unit07"] = make(
        "Dock_Label_Unit07", box((-0.075, 0.249, 0.70), (0.075, 0.252, 0.74)), G["Frame"], "M_Dock_Label", (0, 0.2505, 0.72), smooth=False, uv="planar",
        uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=0.15, v_size=0.04, u0=u0, v0=v0, u_span=us, v_span=vs))

    # --- 托架：两条纵梁 + 后横梁 + 导轨座（全部在机身底面以下、手臂和电源托盘下放区之外） ---
    for s, sx in (("L", 1), ("R", -1)):
        lo_x, hi_x = sorted((sx * 0.135, sx * 0.195))
        objs[f"Dock_Yoke_Beam_{s}"] = make(f"Dock_Yoke_Beam_{s}", box((lo_x, YOKE_FRONT_Y, Z + 0.11), (hi_x, 0.36, Z + 0.16)), G["Frame"], "M_Dock_Gray",
                                           (sx * 0.165, (YOKE_FRONT_Y + 0.36) / 2, Z + 0.11), bevel=0.006)
        # 鞍座钢板（静态）
        lo_x, hi_x = sorted((sx * 0.138, sx * 0.176))
        objs[f"Dock_Saddle_{s}"] = make(f"Dock_Saddle_{s}", box((lo_x, SADDLE_FRONT_Y, Z + 0.16), (hi_x, 0.188, pad_top - 0.012)), G["Frame"], "M_Dock_Steel",
                                        (sx * 0.157, (SADDLE_FRONT_Y + 0.188) / 2, Z + 0.16), bevel=0.003)
        # 导轨座：从横梁向前伸到安装轨正后方
        lo_x, hi_x = sorted((sx * 0.09, sx * 0.20))
        objs[f"Dock_RailMount_{s}"] = make(f"Dock_RailMount_{s}", box((lo_x, 0.206, Z + 0.11), (hi_x, 0.30, Z + 0.16)), G["Frame"], "M_Dock_Gray",
                                           (sx * 0.145, 0.253, Z + 0.11), bevel=0.005)
        # 固定导板：后挡板（轨后 4 mm）+ 内侧导片（轨内侧 2 mm），高度止于背部维修盒下方
        lo_x, hi_x = sorted((sx * 0.090, sx * 0.160))
        back = box((lo_x, 0.206, Z + 0.16), (hi_x, 0.222, Z + 0.31))
        lo_x, hi_x = sorted((sx * 0.086, sx * 0.096))
        fin = box((lo_x, 0.192, Z + 0.21), (hi_x, 0.222, Z + 0.31))
        objs[f"Dock_RailGuide_{s}"] = make(f"Dock_RailGuide_{s}", merge(back, fin), G["Frame"], "M_Dock_Steel", (sx * 0.125, 0.214, Z + 0.16), bevel=0.003)
        # 夹具铰耳
        for ey in (0.196, 0.228):
            lo_x, hi_x = sorted((sx * 0.168, sx * 0.196))
            objs[f"Dock_ClampEar_{s}_{int(ey * 1000)}"] = make(f"Dock_ClampEar_{s}_{int(ey * 1000)}", box((lo_x, ey, Z + 0.16), (hi_x, ey + 0.004, Z + 0.205)),
                                                               G["Frame"], "M_Dock_Steel", (sx * 0.182, ey + 0.002, Z + 0.16), bevel=0.001)
    objs["Dock_Yoke_Cross"] = make("Dock_Yoke_Cross", box((-0.195, 0.30, Z + 0.11), (0.195, 0.36, Z + 0.16)), G["Frame"], "M_Dock_Gray", (0, 0.33, Z + 0.11), bevel=0.006)

    # --- 机身接触点（独立对象，原点 = 接触面中心） ---
    pads = {}
    for s, sx in (("L", 1), ("R", -1)):
        lo_x, hi_x = sorted((sx * 0.140, sx * 0.174))
        pads[s] = make(f"Dock_ContactPad_{s}", box((lo_x, PAD_FRONT_Y, pad_top - 0.012), (hi_x, PAD_REAR_Y, pad_top)), G["ContactPads"], "M_Dock_Rubber",
                       (sx * 0.157, (PAD_FRONT_Y + PAD_REAR_Y) / 2, pad_top), bevel=0.003)
        tag(pads[s], dock_role="contact_pad", contact="robot Chassis_ArmHardpoint underside", contact_normal_local="+Z")
    objs.update({p.name: p for p in pads.values()})

    # --- 左右限位夹具（独立对象，原点 = 铰轴；绕本地 Y 轴转动；0° = 夹紧） ---
    clamps = {}
    for s, sx in (("L", 1), ("R", -1)):
        piv = Vector((sx * 0.182, 0.214, Z + 0.19))
        hub = cyl("Y", piv, 0.014, 0.028, 20)
        lo_x, hi_x = sorted((sx * 0.146, sx * 0.158))
        jaw = box((lo_x, 0.194, Z + 0.215), (hi_x, 0.216, Z + JAW_TOP_Z))   # 顶端低于机背下螺栓（z 0.291），并留出开合扫掠余量
        web_lo, web_hi = sorted((sx * 0.150, sx * 0.182))
        web = box((web_lo, 0.200, Z + 0.185), (web_hi, 0.212, Z + 0.225))
        handle = hs.bm_tube([piv, piv + Vector((sx * 0.06, 0, -0.05)), piv + Vector((sx * 0.13, 0, -0.12))], [0.009, 0.009, 0.009], segs=12)
        clamps[s] = make(f"Dock_Clamp_{s}", merge(hub, jaw, web, handle), G["Clamps"], "M_Dock_Steel", piv, bevel=0.002)
        open_deg = 30.0 if sx > 0 else -30.0
        tag(clamps[s], dock_role="side_clamp", rot_axis_local="Y", closed_deg=0.0, open_deg=open_deg, unity_open_deg=-open_deg,
            note="rotate about local Y; open moves the jaw outward away from the mount rail")
        # 黄色握把与橡胶夹面（子对象，随夹具转动）
        grip = hs.bm_tube([piv + Vector((sx * 0.095, 0, -0.085)), piv + Vector((sx * 0.13, 0, -0.12))], [0.013, 0.013], segs=14)
        g = make(f"Dock_Clamp_{s}_Grip", grip, G["Clamps"], "M_Dock_Warning", piv + Vector((sx * 0.1125, 0, -0.1025)))
        parent_keep(g, clamps[s])
        lo_x, hi_x = sorted((sx * 0.1428, sx * 0.1465))   # 夹面内侧 0.1428：夹紧时贴住安装轨下段（真实网格实测间隙约 0.3 mm）
        jp = make(f"Dock_Clamp_{s}_JawPad", box((lo_x, 0.195, Z + 0.22), (hi_x, 0.215, Z + JAW_TOP_Z - 0.004)), G["Clamps"], "M_Dock_Rubber",
                  (sx * 0.14465, 0.205, Z + (0.22 + JAW_TOP_Z - 0.004) / 2), smooth=False)
        parent_keep(jp, clamps[s])
    objs.update({c.name: c for c in clamps.values()})

    # --- 断电开关：右前立柱 + 开关盒 + 手柄（绕本地 X 轴；+35° = 向下 = OFF，-35° = 向上 = ON）---
    objs["Dock_SwitchPost"] = make("Dock_SwitchPost", box((0.425, -0.215, 0.05), (0.455, -0.185, 0.66)), G["Frame"], "M_Dock_Gray", (0.44, -0.20, 0.05), bevel=0.004)
    sw_box = make("Dock_PowerSwitch", box((0.38, -0.26, 0.66), (0.50, -0.16, 0.82)), G["PowerSwitch"], "M_Dock_Gray", (0.44, -0.21, 0.66), bevel=0.008)
    tag(sw_box, dock_role="power_switch_housing")
    u0, v0, us, vs = region_uv(REGION_SWITCH)
    plate = make("Dock_PowerSwitch_Label", box((0.39, -0.263, 0.672), (0.49, -0.260, 0.808)), G["PowerSwitch"], "M_Dock_Label", (0.44, -0.2615, 0.74),
                 smooth=False, uv="planar",
                 uv_args=dict(origin=(0.0, 0.0, 0.0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=0.10, v_size=0.136, u0=u0, v0=v0, u_span=us, v_span=vs))
    parent_keep(plate, sw_box)
    lp = Vector((0.465, -0.27, 0.74))
    lever_hub = cyl("X", lp, 0.012, 0.03, 20)
    lever_arm = hs.bm_tube([lp, lp + Vector((0, -0.06, 0))], [0.007, 0.007], segs=12)
    lever = make("Dock_PowerSwitch_Lever", merge(lever_hub, lever_arm), G["PowerSwitch"], "M_Dock_Steel", lp, bevel=0.0015)
    t_grip = cyl("X", lp + Vector((0, -0.062, 0)), 0.013, 0.034, 16)
    tg = make("Dock_PowerSwitch_LeverGrip", t_grip, G["PowerSwitch"], "M_Dock_Warning", lp + Vector((0, -0.062, 0)), bevel=0.004)
    parent_keep(tg, lever)
    parent_keep(lever, sw_box)
    tag(lever, dock_role="power_switch_lever", rot_axis_local="X", off_deg=35.0, on_deg=-35.0, unity_off_deg=35.0, unity_on_deg=-35.0, default_state="OFF",
        note="+35 deg about local X = lever down = OFF (docked, rotors stopped); -35 deg = lever up = ON")
    lever.rotation_euler = (math.radians(35.0), 0, 0)
    lamp = make("Dock_PowerSwitch_Lamp", cyl("Z", (0.475, -0.235, 0.826), 0.012, 0.012, 16), G["PowerSwitch"], "M_Dock_Lamp", (0.475, -0.235, 0.82), bevel=0.003)
    parent_keep(lamp, sw_box)
    tag(lamp, dock_role="status_lamp", note="emissive placeholder; program toggles emission for docked / power-off state")
    objs.update({o.name: o for o in (sw_box, plate, lever, tg, lamp)})
    # 线管：开关立柱 → 右支腿 → 立柱
    conduit = hs.bm_tube([(0.44, -0.185, 0.10), (0.44, -0.10, 0.07), (0.44, 0.24, 0.07), (0.30, 0.30, 0.07), (0.10, 0.33, 0.10)], [0.012] * 5, segs=12)
    objs["Dock_Conduit"] = make("Dock_Conduit", conduit, G["Frame"], "M_Dock_Rubber", (0.44, 0.02, 0.07))

    # --- 零件盘托架：左前立柱 + 托盘架（带挡边） ---
    objs["Dock_ShelfPost"] = make("Dock_ShelfPost", box((-0.455, -0.215, 0.05), (-0.425, -0.185, 0.77)), G["Frame"], "M_Dock_Gray", (-0.44, -0.20, 0.05), bevel=0.004)
    shelf = merge(box((-0.72, -0.30, 0.77), (-0.32, -0.06, 0.785)),
                  box((-0.72, -0.30, 0.785), (-0.32, -0.29, 0.80)),
                  box((-0.72, -0.07, 0.785), (-0.32, -0.06, 0.80)),
                  box((-0.72, -0.29, 0.785), (-0.71, -0.07, 0.80)))
    objs["Dock_TrayShelf"] = make("Dock_TrayShelf", shelf, G["Frame"], "M_Dock_Gray", (-0.52, -0.18, 0.785), bevel=0.003)
    objs["Dock_Warning_Shelf"] = make(
        "Dock_Warning_Shelf", box((-0.72, -0.302, 0.787), (-0.32, -0.300, 0.798)), G["Frame"], "M_Dock_Warning", (-0.52, -0.301, 0.7925), smooth=False,
        uv="planar", uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=0.40, v_size=0.011, u_span=4.0, v_span=0.3))

    # --- 零件盘（独立、可取下；原点 = 盘底中心） ---
    tray_lo, tray_hi = Vector((-0.70, -0.28, 0.785)), Vector((-0.44, -0.10, 0.785))
    t_floor = box((tray_lo.x, tray_lo.y, 0.785), (tray_hi.x, tray_hi.y, 0.789))
    walls = [box((tray_lo.x, tray_lo.y, 0.789), (tray_hi.x, tray_lo.y + 0.004, 0.82)),
             box((tray_lo.x, tray_hi.y - 0.004, 0.789), (tray_hi.x, tray_hi.y, 0.82)),
             box((tray_lo.x, tray_lo.y, 0.789), (tray_lo.x + 0.004, tray_hi.y, 0.82)),
             box((tray_hi.x - 0.004, tray_lo.y, 0.789), (tray_hi.x, tray_hi.y, 0.82)),
             box((-0.60, tray_lo.y + 0.004, 0.789), (-0.597, tray_hi.y - 0.004, 0.808))]
    # 两端提手（夹爪全开 94.9 mm，提手横杆 20 mm，便于七号或玩家抓取）
    handles = [hs.bm_tube([(x, -0.235, 0.82), (x + (0.022 if x > -0.5 else -0.022), -0.235, 0.832), (x + (0.022 if x > -0.5 else -0.022), -0.145, 0.832),
                           (x, -0.145, 0.82)], [0.005] * 4, segs=10) for x in (tray_hi.x, tray_lo.x)]
    tray = make("Dock_PartsTray", merge(t_floor, *walls, *handles), G["PartsTray"], "M_Dock_Ivory", ((tray_lo.x + tray_hi.x) / 2, -0.19, 0.785), bevel=0.0015)
    tag(tray, dock_role="parts_tray", removable=True, grip="end handles, 20 mm bar")
    objs["Dock_PartsTray"] = tray

    # --- 磁性零件盒（放旧轴承；独立、可取下；原点 = 盒底中心） ---
    mb_lo, mb_hi = Vector((-0.405, -0.25, 0.785)), Vector((-0.32, -0.19, 0.825))
    mb = merge(box((mb_lo.x, mb_lo.y, mb_lo.z + 0.006), (mb_hi.x, mb_hi.y, mb_lo.z + 0.009)),
               box((mb_lo.x, mb_lo.y, mb_lo.z + 0.006), (mb_hi.x, mb_lo.y + 0.003, mb_hi.z)),
               box((mb_lo.x, mb_hi.y - 0.003, mb_lo.z + 0.006), (mb_hi.x, mb_hi.y, mb_hi.z)),
               box((mb_lo.x, mb_lo.y, mb_lo.z + 0.006), (mb_lo.x + 0.003, mb_hi.y, mb_hi.z)),
               box((mb_hi.x - 0.003, mb_lo.y, mb_lo.z + 0.006), (mb_hi.x, mb_hi.y, mb_hi.z)))
    mbox = make("Dock_MagneticBox", mb, G["MagneticBox"], "M_Dock_Gray", ((mb_lo.x + mb_hi.x) / 2, (mb_lo.y + mb_hi.y) / 2, mb_lo.z), bevel=0.001)
    tag(mbox, dock_role="magnetic_parts_box", removable=True, purpose="old bearing")
    magnet = make("Dock_MagneticBox_Magnet", box((mb_lo.x + 0.006, mb_lo.y + 0.006, mb_lo.z), (mb_hi.x - 0.006, mb_hi.y - 0.006, mb_lo.z + 0.006)),
                  G["MagneticBox"], "M_Dock_Rubber", ((mb_lo.x + mb_hi.x) / 2, (mb_lo.y + mb_hi.y) / 2, mb_lo.z), bevel=0.001)
    parent_keep(magnet, mbox)
    u0, v0, us, vs = region_uv(REGION_BOX)
    lbl = make("Dock_MagneticBox_Label", box((mb_lo.x + 0.008, mb_lo.y - 0.0012, mb_lo.z + 0.012), (mb_hi.x - 0.008, mb_lo.y, mb_hi.z - 0.006)),
               G["MagneticBox"], "M_Dock_Label", ((mb_lo.x + mb_hi.x) / 2, mb_lo.y - 0.0006, mb_lo.z + 0.0235), smooth=False, uv="planar",
               uv_args=dict(origin=(0, 0, 0), u_axis=(1, 0, 0), v_axis=(0, 0, 1), u_size=mb_hi.x - mb_lo.x - 0.016, v_size=mb_hi.z - mb_lo.z - 0.018,
                            u0=u0, v0=v0, u_span=us, v_span=vs))
    parent_keep(lbl, mbox)
    objs.update({o.name: o for o in (mbox, magnet, lbl)})
    return objs, pads, clamps, lever, tray, mbox


def build_placeholder(coll, coll_stowed):
    out = {}
    for name, (lo, hi) in ROBOT_BLOCKS.items():
        o = make("PH_" + name, box(rw(lo), rw(hi)), coll, "M_Placeholder_Robot", rw(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2])),
                 bevel=0.004 if "Engine" in name or "Body" in name else 0.0, smooth=False)
        out[name] = o
    for name, (lo, hi) in ROBOT_STOWED_ARMS.items():
        o = make("PH_" + name, box(rw(lo), rw(hi)), coll_stowed, "M_Placeholder_Robot", rw(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2])), smooth=False)
        out[name] = o
    return out


# ---------------------------------------------------------------------------
# 6. 检查（占位体 / 拆装通道 / 手部接近 / 转动范围）
# ---------------------------------------------------------------------------

def mesh_world(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    eo = obj.evaluated_get(dg)
    me = eo.to_mesh()
    mw = eo.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    polys = [list(p.vertices) for p in me.polygons]
    eo.to_mesh_clear()
    return verts, polys


def box_bvh(lo, hi):
    lo, hi = Vector(lo), Vector(hi)
    v = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return BVHTree.FromPolygons(v, f)


def aabb_gap(p, lo, hi):
    d = Vector((max(lo[i] - p[i], 0, p[i] - hi[i]) for i in range(3)))
    return d.length


def check_box(objs, lo, hi, shrink=0.0005):
    """返回 (是否相交, 最小顶点间隙, 相交对象列表)。相交 = 表面相交或顶点落在盒内（盒先内缩 shrink，避免把贴面接触算作穿插）。"""
    lo_s = Vector(lo) + Vector((shrink,) * 3)
    hi_s = Vector(hi) - Vector((shrink,) * 3)
    tb = box_bvh(lo_s, hi_s)
    hits, gap = [], 1e9
    for o in objs:
        verts, polys = mesh_world(o)
        inside = any(all(lo_s[i] < v[i] < hi_s[i] for i in range(3)) for v in verts)
        ov = BVHTree.FromPolygons(verts, polys).overlap(tb) if polys else []
        if inside or ov:
            hits.append(o.name)
        gap = min(gap, min(aabb_gap(v, lo, hi) for v in verts))
    return bool(hits), round(gap * 1000, 1), hits


def run_checks(dock_meshes, pads, clamps, lever, tray, mbox):
    rep = {"units": "mm (gaps) / m (volumes)", "robot_root_world_z": ROBOT_Z0, "checks": []}

    def add(name, ok, **kw):
        rep["checks"].append(dict(name=name, passed=bool(ok), **kw))

    movable = set(c.name for c in clamps.values()) | {lever.name}

    # A. 占位体与维修座不穿插（工作姿态 / 双臂收起姿态），夹具在夹紧位置
    for pose, blocks in (("working", ROBOT_BLOCKS), ("stowed", {**{k: v for k, v in ROBOT_BLOCKS.items() if "Arm" not in k}, **ROBOT_STOWED_ARMS})):
        worst = []
        for bn, (lo, hi) in blocks.items():
            hit, gap, who = check_box(dock_meshes, rw(lo), rw(hi))
            if hit:
                worst.append(f"{bn}: {who}")
        add(f"robot_placeholder_vs_dock_{pose}", not worst, intersections=worst)

    # B. 接触与间隙
    hp_bottom = ROBOT_Z0 + 0.193
    for s, p in pads.items():
        verts, _ = mesh_world(p)
        top = max(v.z for v in verts)
        add(f"contact_pad_{s}_touches_hardpoint", abs(top - hp_bottom) < 0.0005, pad_top=round(top, 4), hardpoint_bottom=round(hp_bottom, 4))
    rail_gaps = {}
    for s in ("L", "R"):
        lo, hi = ROBOT_BLOCKS[f"MountRail_{s}_Lower"]
        for nm in (f"Dock_RailGuide_{s}", f"Dock_Clamp_{s}"):
            o = bpy.data.objects[nm]
            _, gap, _ = check_box([o], rw(lo), rw(hi))
            rail_gaps[nm] = gap
    add("rail_guides_and_clamps_close_to_mount_rails", all(0 < g <= 5 for g in rail_gaps.values()), gaps_mm=rail_gaps)
    for s in ("L", "R"):
        lo, hi = ROBOT_BLOCKS[f"MountRail_{s}_Lower"]
        hit, gap, _ = check_box([bpy.data.objects[f"Dock_Clamp_{s}_JawPad"]], rw(lo), rw(hi))
        add(f"clamp_{s}_jaw_pad_contacts_mount_rail", not hit and gap <= 0.5, gap_mm=gap,
            note="closed jaw pad touches the lower rail face (about 0.1 mm to the placeholder box, about 0.3 mm to the real mesh)")

    # 支撑是否跨过重心：接触垫在后，前后倾由背部导轨与夹具承担（只作说明，不判失败）
    rep["support_note"] = ("Contact pads span robot y 0.100..0.186; estimated COM y = %.3f lies forward of the pads, "
                           "so forward tipping is resisted by the rail backstops and side clamps gripping the rear mount rails." % ROBOT_COM[1])

    # C. 拆装通道（13 个物理子步骤涉及的取出方向）不被维修座挡住：夹具闭合、张开两种状态都查
    corridors = {
        "top_cover_up":          ((-0.175, -0.18, 0.60), (0.175, -0.012, 0.95)),
        "front_parts_forward":   ((-0.23, -0.65, 0.26), (0.23, -0.19, 0.64)),        # 前框上提后前抽、玻璃、屏幕盒、主板托板
        "front_bezel_lift":      ((-0.2166, -0.2195, 0.635), (0.2166, -0.1902, 0.66)),
        "power_tray_drop":       ((-0.126, -0.103, 0.05), (0.126, 0.103, 0.207)),    # 电源托盘向下放出
        "power_tray_out_front":  ((-0.126, -0.45, 0.05), (0.126, 0.103, 0.10)),      # 放下后从两臂之间向前取出
        "rear_cassette_back":    ((-0.085, 0.13, 0.39), (0.085, 0.50, 0.57)),
        "engine_L_out":          ((0.219, -0.14, 0.26), (0.92, 0.12, 0.575)),
        "engine_R_out":          ((-0.92, -0.14, 0.26), (-0.219, 0.12, 0.575)),
    }
    for state in ("closed", "open"):
        for s, c in clamps.items():
            c.rotation_euler = (0, math.radians(c["open_deg"] if state == "open" else 0.0), 0)
        bpy.context.view_layer.update()
        for cn, (lo, hi) in corridors.items():
            hit, gap, who = check_box(dock_meshes, rw(lo), rw(hi))
            need = TRAY_DOCK_MIN_GAP_MM if cn.startswith("power_tray") else 0.0
            add(f"corridor_{cn}_clamps_{state}", not hit and gap >= need, min_gap_mm=gap, required_min_gap_mm=need, blocked_by=who)
    for c in clamps.values():
        c.rotation_euler = (0, 0, 0)

    # D. 竖直落座通道：夹具张开时，占位体从上方 12 cm 落到位的扫掠体不碰维修座
    for c in clamps.values():
        c.rotation_euler = (0, math.radians(c["open_deg"]), 0)
    bpy.context.view_layer.update()
    worst = []
    for bn, (lo, hi) in ROBOT_BLOCKS.items():
        hit, gap, who = check_box(dock_meshes, rw(lo), rw((hi[0], hi[1], hi[2] + 0.12)))
        # 落座扫掠只看机器人从上往下经过的空间；盒子向上延伸 12 cm 即扫掠体
        hit2, _, who2 = check_box(dock_meshes, rw(lo), rw(hi))
        if hit or hit2:
            worst.append(f"{bn}: {sorted(set(who + who2))}")
    add("docking_descent_120mm_clamps_open", not worst, intersections=worst)
    for c in clamps.values():
        c.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()

    # E. 夹具、开关手柄转动范围内不碰占位体与维修座其它部件
    robot_all = {**ROBOT_BLOCKS, **ROBOT_STOWED_ARMS}
    others = lambda excl: [o for o in dock_meshes if o.name not in excl and not (o.parent and o.parent.name in excl)]
    sweep_hits = []
    bolt_gap = {}   # 夹具开合全程到机背下螺栓的最小间隙（顶点估算；真实网格精确值见隔离复测）
    for s, c in clamps.items():
        excl = {c.name} | {ch.name for ch in c.children}
        bolt = ROBOT_BLOCKS[f"Rear_Bolt_Low_{s}"]
        for k in range(0, 25):
            ang = c["open_deg"] * k / 24
            c.rotation_euler = (0, math.radians(ang), 0)
            bpy.context.view_layer.update()
            parts = [c] + list(c.children)
            for bn, (lo, hi) in robot_all.items():
                hit, _, _ = check_box(parts, rw(lo), rw(hi))
                if hit:
                    sweep_hits.append(f"{c.name}@{ang:.1f}deg vs PH_{bn}")
            _, g, _ = check_box(parts, rw(bolt[0]), rw(bolt[1]))
            if g < bolt_gap.get(s, (1e9, 0))[0]:
                bolt_gap[s] = (g, round(ang, 2))
        c.rotation_euler = (0, 0, 0)
    for s, (g, ang) in bolt_gap.items():
        add(f"clamp_{s}_sweep_rear_bolt_gap", g >= CLAMP_BOLT_MIN_GAP_MM, min_gap_mm=g, at_deg=ang, required_min_gap_mm=CLAMP_BOLT_MIN_GAP_MM,
            note="vertex estimate against the placeholder bolt box; exact value from the real-mesh check")
    for k in range(0, 9):
        ang = 35 - 70 * k / 8
        lever.rotation_euler = (math.radians(ang), 0, 0)
        bpy.context.view_layer.update()
        parts = [lever] + list(lever.children)
        sw_box = lever.parent
        verts, polys = mesh_world(sw_box)
        box_tree = BVHTree.FromPolygons(verts, polys)
        for p in parts:
            pv, pp = mesh_world(p)
            if p.name == lever.name:
                continue   # 手柄轴毂本来就装在开关盒正面
            if BVHTree.FromPolygons(pv, pp).overlap(box_tree):
                sweep_hits.append(f"{p.name}@{ang:.0f}deg vs Dock_PowerSwitch")
    lever.rotation_euler = (math.radians(35.0), 0, 0)
    bpy.context.view_layer.update()
    add("clamp_and_lever_sweeps_free", not sweep_hits, hits=sweep_hits)

    # F. 手部接近区（鼠标正面视角 / VR 手部）：空间内没有占位体以外的障碍，也不穿过占位体
    hand_zones = {
        "engine_L_front_face":  ((0.222, -0.36, 0.26), (0.52, -0.137, 0.575)),
        "engine_R_front_face":  ((-0.52, -0.36, 0.26), (-0.222, -0.137, 0.575)),
        "engine_L_outer_side":  ((0.511, -0.14, 0.26), (0.70, 0.12, 0.575)),
        "engine_R_outer_side":  ((-0.70, -0.14, 0.26), (-0.511, 0.12, 0.575)),
        "switch_lever_front":   ((0.37, -0.52, -0.07), (0.51, -0.37, 0.13)),          # 手柄 T 形握把前方（机器人坐标）
        "tray_lift_above":      ((-0.70, -0.28, 0.105), (-0.44, -0.10, 0.25)),
        "magnetic_box_above":   ((-0.405, -0.25, 0.106), (-0.32, -0.19, 0.25)),
        "clamp_L_grip_reach":   ((0.33, 0.16, 0.0), (0.42, 0.27, 0.12)),
        "clamp_R_grip_reach":   ((-0.42, 0.16, 0.0), (-0.33, 0.27, 0.12)),
    }
    for hn, (lo, hi) in hand_zones.items():
        hit, gap, who = check_box(dock_meshes, rw(lo), rw(hi))
        rob = []
        for bn, (rlo, rhi) in robot_all.items():
            if all(max(lo[i], rlo[i]) < min(hi[i], rhi[i]) for i in range(3)):
                rob.append("PH_" + bn)
        add(f"hand_zone_{hn}_clear", not hit and not rob, dock_hits=who, robot_hits=rob)
    rep["corridors_robot_coords"] = {k: [list(v[0]), list(v[1])] for k, v in corridors.items()}
    rep["hand_zones_robot_coords"] = {k: [list(v[0]), list(v[1])] for k, v in hand_zones.items()}
    rep["all_passed"] = all(c["passed"] for c in rep["checks"])
    return rep


# ---------------------------------------------------------------------------
# 7. 导出
# ---------------------------------------------------------------------------

def export_fbx(root, path, extra_types=("EMPTY", "MESH")):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    todo = [root] + list(root.children_recursive)
    for o in todo:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types=set(extra_types),
        use_mesh_modifiers=True, mesh_smooth_type="OFF", use_custom_props=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", use_triangles=False)


# ---------------------------------------------------------------------------
# 8. 渲染
# ---------------------------------------------------------------------------

def setup_render(scene):
    cam = hs.setup_studio(scene, 0.0, target=(0, 0, 0.9))
    studio = bpy.data.collections["_Studio"]
    floor = bpy.data.objects["Studio_Floor"]
    floor.scale = (2.2, 2.2, 1)
    t = Vector((0, 0.05, 0.9))
    for name, loc, energy, size in (("Key", (-1.7, -2.0, 2.3), 250, 1.4), ("Fill", (2.0, -1.5, 1.2), 95, 1.8),
                                    ("Rim", (0.9, 2.0, 2.1), 190, 1.2), ("RimBack", (-1.7, 1.7, 1.4), 85, 1.4)):
        L = bpy.data.objects[name]
        L.location = t + Vector(loc)
        L.data.energy = energy
        L.data.size = size
        hs.look_at(L, t)
    scene.cycles.samples = 128
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for dev_type in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dev_type
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == dev_type]
                if gpus:
                    for d in prefs.devices:
                        d.use = d.type == dev_type
                    scene.cycles.device = "GPU"
                    print("[render] device", dev_type, [d.name for d in gpus])
                    break
            except Exception:
                continue
    except Exception as e:
        print("[render] GPU unavailable, CPU:", e)
    return cam


def main():
    scene, G = hs.reset_scene("UNIT07_ServiceDock", GROUPS, "Dock_")
    ph_coll = bpy.data.collections.new("_Placeholder_UNIT07")
    scene.collection.children.link(ph_coll)
    ph_stowed = bpy.data.collections.new("_Placeholder_UNIT07_StowedArms")
    scene.collection.children.link(ph_stowed)

    images = {
        "T_Dock_Grime.png": hs.save_image("T_Dock_Grime.png", 0.5 * hs.make_grime_texture(1024, seed=11, scratch_count=260) + 0.5 * 0.9, TEX_DIR),   # 轻微旧化：对比度减半
        "T_Dock_Warning.png": hs.save_image("T_Dock_Warning.png", make_warning_texture(), TEX_DIR),
        "T_Dock_Labels.png": hs.save_image("T_Dock_Labels.png", make_label_texture(), TEX_DIR),
    }
    hs.build_materials({**MAT_SPECS, **AUX_SPECS}, images)
    hs.write_material_manifest(MAT_SPECS, os.path.join(HERE, "materials.json"))

    objs, pads, clamps, lever, tray, mbox = build_dock(G)
    ph = build_placeholder(ph_coll, ph_stowed)
    ph_stowed.hide_render = True

    # 根对象与机器人锚点
    root = bpy.data.objects.new("UNIT07_ServiceDock", None)
    root.empty_display_type = "PLAIN_AXES"
    G["Frame"].objects.link(root)
    anchor = bpy.data.objects.new("Dock_RobotAnchor", None)
    anchor.empty_display_type = "ARROWS"
    anchor.empty_display_size = 0.15
    anchor.location = (0, 0, ROBOT_Z0)
    G["Frame"].objects.link(anchor)
    tag(anchor, dock_role="robot_anchor", note="place UNIT 07 model root here (model root = V4 Root bone, lowest point); robot faces -Y in Blender")
    for o in list(bpy.data.objects):
        if o.name.startswith("Dock_") and o.parent is None and o is not root:
            parent_keep(o, root)
    ph_root = bpy.data.objects.new("UNIT07_RobotPlaceholder", None)
    ph_coll.objects.link(ph_root)
    for o in ph.values():
        parent_keep(o, ph_root)
    tag(ph_root, note="placeholder only: bounding blocks measured from the V4 handoff; not art, not the V4 mesh")

    # 旧轴承道具：只用于使用状态渲染，不导出
    ob = bpy.data.collections.new("_RenderProps")
    scene.collection.children.link(ob)
    ring = bmesh.new()
    bmesh.ops.create_cone(ring, cap_ends=True, segments=28, radius1=0.012, radius2=0.012, depth=0.005)
    bearing = hs.make_object("Prop_OldBearing", ring, ob, "M_Prop_OldBearing", smooth=True)
    bearing.location = (-0.3625, -0.22, 0.797)
    bearing.rotation_euler = (math.radians(12), 0, math.radians(20))

    bpy.context.view_layer.update()
    dock_meshes = [o for o in root.children_recursive if o.type == "MESH"]

    # 检查
    report = run_checks(dock_meshes, pads, clamps, lever, tray, mbox)
    with open(os.path.join(HERE, "clearance_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("[check] all_passed", report["all_passed"])
    for c in report["checks"]:
        if not c["passed"]:
            print("[check] FAIL", c)

    # 统计
    stats = hs.collect_stats(G, images, list(MAT_SPECS.keys()))
    stats["objects"] = {}
    for o in sorted([root, anchor] + dock_meshes, key=lambda x: x.name):
        e = {"type": o.type, "parent": o.parent.name if o.parent else "",
             "origin_world": [round(v, 4) for v in o.matrix_world.translation],
             "rotation_euler_deg": [round(math.degrees(a), 2) for a in o.rotation_euler]}
        if o.type == "MESH":
            e["triangles"] = hs.triangle_count(o)
            e["material"] = o.material_slots[0].material.name if o.material_slots else ""
        for k in ("dock_role", "rot_axis_local", "closed_deg", "open_deg", "unity_open_deg", "on_deg", "off_deg", "unity_on_deg", "unity_off_deg", "default_state"):
            if k in o.keys():
                e[k] = o[k]
        stats["objects"][o.name] = e
    ov_lo, ov_hi = ROBOT_OVERALL
    stats["robot_placeholder_overall_m"] = [round(ov_hi[i] - ov_lo[i], 4) for i in range(3)]
    stats["robot_root_world_z"] = ROBOT_Z0
    dv = [v for o in dock_meshes for v in mesh_world(o)[0]]
    stats["dock_bounds_world"] = [[round(min(v[i] for v in dv), 4) for i in range(3)], [round(max(v[i] for v in dv), 4) for i in range(3)]]
    stats["dock_mesh_objects"] = len(dock_meshes)
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[stats] triangles", stats["total_triangles"], "objects", len(dock_meshes), "bounds", stats["dock_bounds_world"])

    # 导出
    export_fbx(root, os.path.join(EXPORT_DIR, "UNIT07_ServiceDock.fbx"))
    export_fbx(ph_root, os.path.join(EXPORT_DIR, "UNIT07_RobotPlaceholder.fbx"))
    print("[export] done")

    # 保存 .blend（贴图外部引用 Textures/）
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Unit07ServiceDock.blend"), relative_remap=True)
    print("[save] Unit07ServiceDock.blend")

    if NO_RENDER:
        return
    cam = setup_render(scene)

    def shot(name, loc, target, lens, robot=True, clamps_open=False, lever_on=False, props=True):
        ph_coll.hide_render = not robot
        ob.hide_render = not props
        for c in clamps.values():
            c.rotation_euler = (0, math.radians(c["open_deg"] if clamps_open else 0), 0)
        lever.rotation_euler = (math.radians(-35.0 if lever_on else 35.0), 0, 0)
        hs.render_view(scene, cam, os.path.join(RENDER_DIR, name), loc, target, lens)
        print("[render]", name)

    shot("R01_front_docked_placeholder.png", (0, -3.0, 1.30), (0, 0, 0.88), 38)
    shot("R02_side_docked_placeholder.png", (3.0, 0.05, 1.15), (0, 0.05, 0.85), 38)
    shot("R03_use_state_threequarter.png", (1.95, -2.35, 1.75), (0, -0.02, 0.86), 36)
    shot("R04_front_empty.png", (0, -3.0, 1.30), (0, 0, 0.72), 38, robot=False)
    shot("R05_side_empty.png", (3.0, 0.05, 1.15), (0, 0.05, 0.6), 38, robot=False)
    shot("R06_rear_clamps_closed.png", (-0.95, 1.25, 1.30), (0, 0.2, 0.95), 42)
    shot("R07_rear_clamps_open_empty.png", (-0.95, 1.25, 1.30), (0, 0.2, 0.95), 42, robot=False, clamps_open=True)
    shot("R08_switch_off_closeup.png", (0.80, -0.95, 1.00), (0.44, -0.22, 0.74), 55)
    shot("R09_switch_on_closeup.png", (0.80, -0.95, 1.00), (0.44, -0.22, 0.74), 55, lever_on=True)
    shot("R10_tray_and_magnetic_box.png", (-0.95, -0.95, 1.20), (-0.52, -0.19, 0.80), 55)
    shot("R11_mouse_view_front_high.png", (0, -1.55, 1.78), (0, -0.05, 1.0), 30)
    # 恢复默认状态后再存一次，保证 .blend 为默认状态
    for c in clamps.values():
        c.rotation_euler = (0, 0, 0)
    lever.rotation_euler = (math.radians(35.0), 0, 0)
    ph_coll.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Unit07ServiceDock.blend"), relative_remap=True)


if __name__ == "__main__":
    main()
