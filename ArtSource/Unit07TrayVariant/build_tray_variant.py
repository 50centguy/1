"""
边境维修站 · UNIT 07 维修座零件盘 · 提手抬高变体（Blender 5.x 建模脚本）
由 Claude 辅助编写。运行：run_blender.bat（或 blender -b --factory-startup --python build_tray_variant.py [-- --norender]）

为什么要做：七号右手按美术审计姿态端原零件盘时，下爪爪身穿进盘端壁约 20 mm（集成线 72838e9 的
Docs/Integration/TwoNightSlice/README_N1N2.md 第 4 节）。本变体只改两端提手：握杆抬高 45 mm、改成立柱式支腿，
盘体（底板、四壁、隔板）完全按原脚本 build_unit07_dock.py 的同一组尺寸重建：260 × 180 mm、壁高 35 mm（含倒角约 36.5 mm）、
原点 = 盘底中心，落架基准不变。七号、维修座、托盘架、原 FBX、共享材质都不改。

参数（集成线 Unity 里实测选出，见 Reports/param_search.md）：
  HANDLE_RAISE = 45 mm   握杆中心从 z 47 mm 抬到 92 mm
  握杆为“悬臂式”：两根立柱都在夹爪尖一侧（盘本地 Y = −84、−30 mm），握杆从 −84 mm 伸到 +30 mm 封端。
    原因：七号是纵握，夹爪铰链在握点的 +Y 侧约 25 mm 处；握杆再往 +Y 伸会穿进爪身（第一版 ±84 mm 两端立柱即因此失败，
    见 Reports/jaw_detail_probe.md 第 3 节：握杆端 ≤ +30 mm 时下爪身 2.05 mm；立柱在 y ≤ −24 mm 时离右手 ≥ 12.5 mm）。
  Y_SIGN       = 盘本地 +Y（Unity）对应 Blender 的哪个方向（Unity 导入后用 AxisMarker_PlusY 核对）
  BAR_X        = ±151.6 mm  握杆离盘中心（与原握杆相同，离端壁外面 21.6 mm）
  管径 10 mm（与原提手相同）、12 段；盘体倒角 1.5 mm 与原脚本相同

生成（都在本目录下）：
  Unit07TrayVariant.blend                     源文件（变体 + 隐藏的原盘参考）
  Export/UNIT07_PartsTray_HandleRaised.fbx    变体（根空物体 + 盘体 + 两个提手 + 握杆中心空物体）
  Renders/*.png                               Cycles 渲染（Blender 图；不含七号，七号握持图在 Unity 隔离验证里拍）
  stats.json                                  三角面、物体、材质、贴图统计

坐标：Blender Z 向上，单位米，与原维修座脚本相同；导出设置与原脚本 export_fbx 相同（-Z 前、Y 上、FBX_SCALE_UNITS）。
"""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
DOCK_DIR = os.path.join(HERE, "..", "Unit07ServiceDock")
sys.path.insert(0, os.path.join(HERE, "..", "Common"))
sys.path.insert(0, DOCK_DIR)
import br_hardsurface as hs  # noqa: E402
import build_unit07_dock as dock  # noqa: E402   只取材质规格，不运行它的 main()，不写它的目录

EXPORT_DIR = os.path.join(HERE, "Export")
RENDER_DIR = os.path.join(HERE, "Renders")
for d in (EXPORT_DIR, RENDER_DIR):
    os.makedirs(d, exist_ok=True)
NO_RENDER = "--norender" in sys.argv

HANDLE_RAISE = 0.045
LEG_Y = 0.084           # 远端立柱
LEG2_Y = 0.030          # 近握点立柱（夹爪尖一侧）
BAR_END_Y = 0.030       # 握杆封端（握点就在这里，铰链一侧不再有握杆）
Y_SIGN = float(os.environ.get("TRAY_Y_SIGN", "1"))
BAR_X = 0.1516
BAR_Z0 = 0.047                 # 原握杆中心高（盘底为 0）
TUBE_R = 0.005
TUBE_SEGS = 12              # 原提手 10 段；12 段每面 30°，低于自动平滑 35°，圆管不会出现棱面
WALL_OUT_X = 0.130
FOOT_Z = 0.028                 # 支腿脚落在端壁外面上的高度（壁顶 35 mm 以下）
FILLET_R = 0.008
BEVEL = 0.0015
ROOT_NAME = "Dock_PartsTray_HandleRaised"


# ---------------------------------------------------------------------------
# 几何
# ---------------------------------------------------------------------------

def box(lo, hi):
    lo, hi = Vector(lo), Vector(hi)
    return hs.bm_box(tuple(hi - lo), tuple((lo + hi) / 2))


def tray_body_bm():
    """盘体：与 build_unit07_dock.py 第 386–393 行相同的尺寸，改写成盘本地坐标（原点 = 盘底中心）。"""
    lo, hi = Vector((-0.13, -0.09, 0.0)), Vector((0.13, 0.09, 0.0))
    parts = [box((lo.x, lo.y, 0.0), (hi.x, hi.y, 0.004)),
             box((lo.x, lo.y, 0.004), (hi.x, lo.y + 0.004, 0.035)),
             box((lo.x, hi.y - 0.004, 0.004), (hi.x, hi.y, 0.035)),
             box((lo.x, lo.y, 0.004), (lo.x + 0.004, hi.y, 0.035)),
             box((hi.x - 0.004, lo.y, 0.004), (hi.x, hi.y, 0.035)),
             box((-0.03, lo.y + 0.004, 0.004), (-0.027, hi.y - 0.004, 0.023))]
    return dock.merge(*parts)


def original_handles_bm():
    """原提手（参考用），同原脚本第 395–396 行，改写成盘本地坐标。"""
    out = []
    for s in (1, -1):
        x = 0.13 * s
        out.append(hs.bm_tube([(x, -0.045, 0.035), (x + 0.022 * s, -0.045, 0.047), (x + 0.022 * s, 0.045, 0.047), (x, 0.045, 0.035)], [0.005] * 4, segs=10))
    return out


def fillet(points, r, steps=3):
    """折线倒圆角：每个内拐点换成一段半径 r 的圆弧（管子在拐角处不会变细）。"""
    pts = [Vector(p) for p in points]
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, b, c = pts[i - 1], pts[i], pts[i + 1]
        u, v = (a - b).normalized(), (c - b).normalized()
        ang = u.angle(v)
        if ang < 1e-3 or abs(ang - math.pi) < 1e-3:
            out.append(b)
            continue
        t = r / math.tan(ang / 2)
        t = min(t, (a - b).length * 0.45, (c - b).length * 0.45)
        p0, p1 = b + u * t, b + v * t
        for k in range(steps + 1):          # 二次贝塞尔近似圆弧
            s = k / steps
            out.append(p0 * (1 - s) ** 2 + b * 2 * s * (1 - s) + p1 * s * s)
    out.append(pts[-1])
    return out


def handle_axis(side):
    """提手轴线（盘本地）：端壁外面上的脚 → 立柱 → 握杆 → 立柱 → 脚。side = ±1（盘的 ±X 端）。"""
    zb = BAR_Z0 + HANDLE_RAISE
    x0, x1 = WALL_OUT_X * side, BAR_X * side
    y = lambda v: v * Y_SIGN
    main = [(x0, y(-LEG_Y), FOOT_Z), (x1, y(-LEG_Y), FOOT_Z + 0.008), (x1, y(-LEG_Y), zb), (x1, y(BAR_END_Y), zb)]
    strut = [(x1, y(-LEG2_Y), zb), (x1, y(-LEG2_Y), FOOT_Z + 0.008), (x0, y(-LEG2_Y), FOOT_Z)]
    return main, strut


def handle_bm(side):
    main, strut = handle_axis(side)
    a1 = fillet(main, FILLET_R); a2 = fillet(strut, FILLET_R)
    tube = hs.bm_tube([tuple(p) for p in a1], [TUBE_R] * len(a1), segs=TUBE_SEGS)
    tube2 = hs.bm_tube([tuple(p) for p in a2], [TUBE_R] * len(a2), segs=TUBE_SEGS)   # 第二根立柱从握杆下面接出（T 形接头埋在握杆里）
    pads = [tube2]
    for y in (-LEG_Y * Y_SIGN, -LEG2_Y * Y_SIGN):                 # 支腿脚下的安装座：贴在端壁外面，看得出提手是固定在盘上的
        x_in, x_out = WALL_OUT_X * side, (WALL_OUT_X + 0.005) * side
        y0, y1 = max(y - 0.007, -0.0895), min(y + 0.007, 0.0895)
        pad = box((min(x_in, x_out), y0, FOOT_Z - 0.009), (max(x_in, x_out), y1, FOOT_Z + 0.006))
        bmesh.ops.bevel(pad, geom=list(pad.edges), offset=BEVEL, segments=2, affect="EDGES", profile=0.5)   # 只给安装座倒角；圆管本身光滑，不用倒角
        pads.append(pad)
    return dock.merge(tube, *pads)


def make_obj(name, bm, coll, mat, bevel=True):
    obj = hs.make_object(name, bm, coll, mat, smooth=True, uv=True)
    if bevel:
        hs.add_bevel(obj, BEVEL, segments=2, angle=35)
    return obj


def empty(name, loc, parent, coll, size=0.01):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"
    e.empty_display_size = size
    e.location = Vector(loc)
    coll.objects.link(e)
    e.parent = parent
    return e


# ---------------------------------------------------------------------------
# 导出 / 统计 / 渲染
# ---------------------------------------------------------------------------

def export_fbx(root, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in [root] + list(root.children_recursive):
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True, mesh_smooth_type="OFF", use_custom_props=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", use_triangles=False)


def eval_bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        for v in me.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        ev.to_mesh_clear()
    return lo, hi


def main():
    scene, G = hs.reset_scene("UNIT07_PartsTray_HandleRaised", ["Variant", "_Reference_Original"], "")
    grime = bpy.data.images.load(os.path.join(DOCK_DIR, "Textures", "T_Dock_Grime.png"), check_existing=True)
    hs.build_materials({"M_Dock_Ivory": dock.MAT_SPECS["M_Dock_Ivory"]}, {"T_Dock_Grime.png": grime})

    # 变体
    root = bpy.data.objects.new(ROOT_NAME, None)
    root.empty_display_type = "CUBE"
    root.empty_display_size = 0.02
    G["Variant"].objects.link(root)
    body = make_obj("Tray_Body", tray_body_bm(), G["Variant"], "M_Dock_Ivory")
    hpx = make_obj("Tray_Handle_PX", handle_bm(1), G["Variant"], "M_Dock_Ivory", bevel=False)
    hnx = make_obj("Tray_Handle_NX", handle_bm(-1), G["Variant"], "M_Dock_Ivory", bevel=False)
    for o in (body, hpx, hnx):
        o.parent = root
    zb = BAR_Z0 + HANDLE_RAISE
    ymid = (-LEG_Y + BAR_END_Y) / 2 * Y_SIGN
    for nm, sx in (("PX", 1), ("NX", -1)):   # 握杆中点、直段两端（圆角之外；Unity 里按这两点区分“握杆直段”与立柱）
        empty("GripBar_" + nm, (BAR_X * sx, ymid, zb), root, G["Variant"])
        empty("GripBarStart_" + nm, (BAR_X * sx, (-LEG_Y + FILLET_R + 0.002) * Y_SIGN, zb), root, G["Variant"])
        empty("GripBarEnd_" + nm, (BAR_X * sx, BAR_END_Y * Y_SIGN, zb), root, G["Variant"])
    empty("AxisMarker_PlusY", (0.0, 0.05, 0.01), root, G["Variant"])   # 只用来核对 Unity 里的 +Y 方向
    for k, v in dict(dock_role="parts_tray", removable=True, variant="handle_raised_45mm_cantilever", handle_raise_mm=45.0, leg_y_mm=-84.0, leg2_y_mm=-30.0, bar_end_y_mm=30.0, y_sign=Y_SIGN, bar_x_mm=151.6,
                     bar_z_mm=zb * 1000, tube_d_mm=10.0, grip="end handles, 10 mm bar; right-hand one-hand carry").items():
        root[k] = v

    # 原盘参考（同脚本同尺寸重建，只渲染对比，不导出）
    ref_root = bpy.data.objects.new("REF_Dock_PartsTray_Original", None)
    G["_Reference_Original"].objects.link(ref_root)
    ref = dock.make("REF_Tray_Original", dock.merge(tray_body_bm(), *original_handles_bm()), G["_Reference_Original"], "M_Dock_Ivory", (0, 0, 0), bevel=BEVEL)
    ref.parent = ref_root

    bpy.context.view_layer.update()
    export_fbx(root, os.path.join(EXPORT_DIR, "UNIT07_PartsTray_HandleRaised.fbx"))

    lo, hi = eval_bounds([body, hpx, hnx])
    blo, bhi = eval_bounds([body])
    rlo, rhi = eval_bounds([ref])
    stats = {
        "variant": ROOT_NAME, "units": "m (Blender), Z up; exported -Z forward / Y up, FBX_SCALE_UNITS",
        "objects": {o.name: {"triangles": hs.triangle_count(o), "material": o.data.materials[0].name if o.data.materials else None} for o in (body, hpx, hnx)},
        "triangles_total": sum(hs.triangle_count(o) for o in (body, hpx, hnx)),
        "original_triangles_rebuilt_reference": hs.triangle_count(ref),
        "renderers": 3, "original_renderers": 1, "materials": ["M_Dock_Ivory (reused, not modified)"], "textures": ["T_Dock_Grime.png (reused from Unit07ServiceDock/Textures, not copied)"],
        "bounds_mm": {"min": [round(c * 1000, 1) for c in lo], "max": [round(c * 1000, 1) for c in hi]},
        "body_bounds_mm": {"min": [round(c * 1000, 1) for c in blo], "max": [round(c * 1000, 1) for c in bhi]},
        "original_bounds_mm": {"min": [round(c * 1000, 1) for c in rlo], "max": [round(c * 1000, 1) for c in rhi]},
        "grip_bar_straight_y_mm": [round((-LEG_Y + FILLET_R + 0.002) * Y_SIGN * 1000, 1), round(BAR_END_Y * Y_SIGN * 1000, 1)], "grip_bar_x_z_mm": [BAR_X * 1000, round(zb * 1000, 1)], "handle_raise_mm": HANDLE_RAISE * 1000, "leg_y_mm": LEG_Y * 1000,
    }
    with open(os.path.join(HERE, "stats.json"), "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=2)
    print("[tray] stats", json.dumps(stats, ensure_ascii=False))

    if not NO_RENDER:
        cam = hs.setup_studio(scene, 0.0, target=(0, 0, 0.04))
        scene.render.resolution_x, scene.render.resolution_y = 1400, 900
        for o in bpy.data.objects:                 # 布光按整座维修座调的；只拍零件盘时离得近，整体降亮
            if o.type == "LIGHT":
                o.data.energy *= 0.22
        scene.cycles.samples = 64
        try:
            prefs = bpy.context.preferences.addons["cycles"].preferences
            for dev in ("OPTIX", "CUDA"):
                try:
                    prefs.compute_device_type = dev
                    prefs.get_devices()
                    if any(d.type == dev for d in prefs.devices):
                        for d in prefs.devices:
                            d.use = d.type == dev
                        scene.cycles.device = "GPU"
                        break
                except Exception:
                    continue
        except Exception as e:
            print("[render] CPU:", e)

        def shot(name, loc, target, lens):
            hs.render_view(scene, cam, os.path.join(RENDER_DIR, name), loc, target, lens)

        # 同尺度对比：原盘在左（-X 方向平移），变体在右；同一机位、同一焦距
        root.location = (0.20, 0, 0)
        ref_root.location = (-0.20, 0, 0)
        bpy.context.view_layer.update()
        shot("B01_compare_front_same_scale.png", (0.0, -1.10, 0.30), (0.0, 0.0, 0.05), 50)
        shot("B02_compare_threequarter_same_scale.png", (0.85, -0.85, 0.55), (0.0, 0.0, 0.04), 50)
        shot("B03_compare_side_same_scale.png", (1.25, 0.0, 0.10), (0.0, 0.0, 0.05), 60)
        # 单独：变体
        root.location = (0, 0, 0)
        ref_root.hide_render = True
        for o in ref_root.children_recursive:
            o.hide_render = True
        bpy.context.view_layer.update()
        shot("B04_variant_front.png", (0.0, -0.70, 0.18), (0.0, 0.0, 0.05), 50)
        shot("B05_variant_end_handle_closeup.png", (0.42, -0.20, 0.20), (0.15, 0.0, 0.06), 70)
        shot("B06_variant_top.png", (0.0, -0.05, 0.85), (0.0, 0.0, 0.03), 50)
        ref_root.hide_render = False
        for o in ref_root.children_recursive:
            o.hide_render = False
        ref_root.location = (0, 0, 0)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Unit07TrayVariant.blend"), relative_remap=True)
    print("[tray] done")


if __name__ == "__main__":
    main()
