"""
只读测量：导入 Assets/RobotV4/Model/robot-final.fbx（不修改、不另存），在静止姿态（= Unity 里的静态姿态，Idle_Hover 第 0 帧）下
测量七号左引擎各部件的层级、位置、尺寸、面数，以及左上轴承的转轴、内外径，进气口开口，上盖内表面位置。
输出 robotv4_left_engine.json / .md。坐标：Blender 世界坐标（Z 向上，单位 m）；同时给出每个网格自身坐标系里的数据。
用法：blender -b --factory-startup --python measure_robotv4_left_engine.py
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
FBX = os.path.join(ROOT, "Assets", "RobotV4", "Model", "robot-final.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
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

KEY = ["Engine_BearingTop_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L",
       "Engine_UpperCover_L", "Engine_LowerCover_L", "Engine_Shaft_L", "Engine_Fan_L", "Engine_FanNut_L",
       "Engine_MotorHousing_L", "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L", "Engine_MountBase_L"]


def verts_world(o):
    m = o.matrix_world
    return np.array([tuple(m @ v.co) for v in o.data.vertices])


def verts_local(o):
    return np.array([tuple(v.co) for v in o.data.vertices])


def tris(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def principal_axes(pts):
    c = pts.mean(axis=0)
    w, v = np.linalg.eigh(np.cov((pts - c).T))
    return c, w, v


def radial_profile(pts, center, axis):
    axis = axis / np.linalg.norm(axis)
    d = pts - center
    h = d @ axis
    r = np.linalg.norm(d - np.outer(h, axis), axis=1)
    return h, r


def info(o):
    mw = o.matrix_world
    loc, rot, sca = mw.decompose()
    wp = verts_world(o)
    lp = verts_local(o)
    return {
        "name": o.name,
        "parent": o.parent.name if o.parent else None,
        "parent_type": o.parent_type,
        "parent_bone": o.parent_bone or None,
        "world_location": [round(x, 5) for x in loc],
        "world_rotation_euler_deg": [round(math.degrees(a), 3) for a in rot.to_euler()],
        "world_scale": [round(x, 5) for x in sca],
        "local_bbox_min": [round(x, 5) for x in lp.min(0)],
        "local_bbox_max": [round(x, 5) for x in lp.max(0)],
        "world_bbox_min": [round(x, 5) for x in wp.min(0)],
        "world_bbox_max": [round(x, 5) for x in wp.max(0)],
        "world_size_mm": [round(x * 1000, 1) for x in (wp.max(0) - wp.min(0))],
        "verts": len(o.data.vertices),
        "tris": tris(o),
        "materials": [s.material.name for s in o.material_slots if s.material],
        "uv_layers": [u.name for u in o.data.uv_layers],
    }


out = {"source": "Assets/RobotV4/Model/robot-final.fbx (read only)", "pose": "Idle_Hover first frame (= Unity static pose)", "objects": {}}
objs = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
left = sorted(n for n, o in objs.items() if o.parent_bone and ("_L" in o.parent_bone or o.parent_bone.endswith("L_Rotor")) and n.startswith("Engine"))
out["left_engine_meshes"] = left
out["left_engine_bones"] = sorted(b.name for b in arm.data.bones if b.name.startswith("Engine_L"))
for n in KEY:
    if n in objs:
        out["objects"][n] = info(objs[n])
out["armature"] = {"name": arm.name, "world_location": list(arm.matrix_world.translation), "rotation_deg": [math.degrees(a) for a in arm.matrix_world.to_euler()]}
hinge = arm.data.bones["Engine_L_Hinge"]
hm = arm.matrix_world @ hinge.matrix_local
out["Engine_L_Hinge"] = {"head_world": list(arm.matrix_world @ hinge.head_local), "tail_world": list(arm.matrix_world @ hinge.tail_local),
                         "y_axis_world": list((hm.to_3x3() @ Vector((0, 1, 0))).normalized())}

# ---- 左上轴承：转轴（最薄方向）、内外径、宽度；与转轴的对位
b = objs["Engine_BearingTop_L"]
wp = verts_world(b)
c, w, v = principal_axes(wp)
axis = v[:, int(np.argmin(w))]           # 薄方向 = 转轴
if axis[2] < 0:
    axis = -axis
h, r = radial_profile(wp, c, axis)
lp = verts_local(b)
lc, lw, lv = principal_axes(lp)
laxis = lv[:, int(np.argmin(lw))]
lh, lr = radial_profile(lp, lc, laxis)
rs = np.sort(np.unique(np.round(r, 5)))
out["bearing"] = {
    "center_world": [round(x, 5) for x in c],
    "axis_world": [round(x, 5) for x in axis],
    "axis_tilt_from_world_up_deg": round(math.degrees(math.acos(min(1, abs(axis[2])))), 3),
    "center_local": [round(x, 5) for x in lc],
    "axis_local": [round(x, 5) for x in laxis],
    "outer_diameter_mm": round(r.max() * 2000, 2),
    "bore_diameter_mm": round(r.min() * 2000, 2),
    "width_mm": round((h.max() - h.min()) * 1000, 2),
    "radial_levels_mm": [round(x * 1000, 2) for x in rs[:40]],
    "axial_levels_mm": [round(x * 1000, 2) for x in np.sort(np.unique(np.round(h, 5)))],
}
# 转轴在轴承处的半径
s = objs["Engine_Shaft_L"]
sp = verts_world(s)
sh, sr = radial_profile(sp, c, axis)
near = np.abs(sh) < (h.max() - h.min()) * 0.6
out["shaft_at_bearing"] = {"max_radius_mm_within_bearing_width": round(float(sr[near].max() * 1000), 2) if near.any() else None,
                           "count": int(near.sum())}

# ---- 进气口：护栅、唇口、风道相对轴承轴线的开口
for n in ["Engine_IntakeGuard_L", "Engine_IntakeLip_L", "Engine_IntakeDuct_L"]:
    o = objs[n]
    p = verts_world(o)
    hh, rr = radial_profile(p, c, axis)
    out[n + "_profile"] = {"axial_min_mm": round(hh.min() * 1000, 1), "axial_max_mm": round(hh.max() * 1000, 1),
                           "radius_min_mm": round(rr.min() * 1000, 1), "radius_max_mm": round(rr.max() * 1000, 1),
                           "radius_levels_mm": [round(x * 1000, 1) for x in np.sort(np.unique(np.round(rr, 4)))[:30]]}

# ---- 上盖内表面：从轴承中心沿转轴向上打射线，命中上盖的第一处就是内表面
cover = objs["Engine_UpperCover_L"]
dg = bpy.context.evaluated_depsgraph_get()
bvh = BVHTree.FromObject(cover, dg)
mw_inv = cover.matrix_world.inverted()
hits = []
for dx, dy in [(0, 0), (0.06, 0), (-0.06, 0), (0, 0.06), (0, -0.06), (0.08, 0.04), (-0.05, -0.07)]:
    o_w = Vector(c) + Vector((dx, dy, 0))
    o_l = mw_inv @ o_w
    d_l = (mw_inv.to_3x3() @ Vector(axis)).normalized()
    loc, nor, idx, dist = bvh.ray_cast(o_l, d_l)
    if loc is not None:
        lw_ = cover.matrix_world @ loc
        nw = (cover.matrix_world.to_3x3() @ nor).normalized()
        hits.append({"offset_xy": [dx, dy], "hit_world": [round(x, 5) for x in lw_], "normal_world": [round(x, 4) for x in nw],
                     "distance_mm": round((lw_ - o_w).length * 1000, 1)})
out["cover_inner_hits_from_bearing_center"] = hits
# 上盖：轴向上下限（壳体厚度判断）
cp = verts_world(cover)
ch, cr = radial_profile(cp, c, axis)
out["Engine_UpperCover_L_profile"] = {"axial_min_mm": round(ch.min() * 1000, 1), "axial_max_mm": round(ch.max() * 1000, 1),
                                      "radius_max_mm": round(cr.max() * 1000, 1)}

with open(os.path.join(HERE, "robotv4_left_engine.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
print("MEASURE_OK")
