"""
UNIT 07 左引擎外露线缆 · 连接器几何核对（只读）
由 Claude 辅助编写。运行：blender -b --factory-startup --python analyze_connectors.py
只导入 Assets/RobotV4/Model/robot-final.fbx 到内存里分析，不保存 .blend、不写回 FBX。
输出：Reports/connector_geometry.json、Reports/connector_geometry.md、Renders/C*_*.png（工作台渲染，标注用）。

对每个对象：
- 网格岛（相连的面集合）：三角面数、包围盒（物体本地）、中心；
- 沿审计记录的拔出方向（物体本地 X，PlugBody = +X，PlugEngine = −X）的截面宽度变化；
对每根线缆：
- 按骨骼权重找两端（Body 权重最高的一圈、Engine_L_Hinge 权重最高的一圈），端面中心与端面半径；
- 端面中心到每个插头网格岛的最近距离、落在哪个岛里面。
"""

import bmesh
import bpy
import json
import math
import os
import sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FBX = os.path.join(ROOT, "Assets", "RobotV4", "Model", "robot-final.fbx")
REP = os.path.join(HERE, "Reports")
REN = os.path.join(HERE, "Renders")
for d in (REP, REN):
    os.makedirs(d, exist_ok=True)

CABLES = ["ExternalCable_L", "AuxCable_L"]
PLUGS = ["ExternalCable_L_PlugBody", "ExternalCable_L_PlugEngine", "AuxCable_L_PlugBody", "AuxCable_L_PlugEngine",
         "Harness_Engine_L_PlugA", "Harness_Engine_L_PlugB"]


def islands(obj):
    """返回 [(三角面数, 顶点索引集合, 面索引列表)]，按物体本地坐标。"""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, faces, verts = [f], [], set()
        seen.add(f.index)
        while stack:
            g = stack.pop()
            faces.append(g.index)
            for v in g.verts:
                verts.add(v.index)
                for h in v.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        tris = sum(len(bm.faces[i].verts) - 2 for i in faces)
        out.append((tris, verts, faces))
    bm.free()
    return sorted(out, key=lambda x: -x[0])


def bounds(points):
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return lo, hi


def mm(v):
    return [round(c * 1000, 1) for c in v]


def material_info(obj):
    out = []
    for slot in obj.material_slots:
        m = slot.material
        if m is None:
            out.append(None)
            continue
        tex = None
        if m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image is not None:
                    tex = n.image
                    break
        out.append({"name": m.name, "image": tex.name if tex else None, "image_size": list(tex.size) if tex else None})
    return out


def sample_uv_color(obj, verts):
    """在材质贴图上取这些顶点 UV 的平均颜色（判断黄 / 灰）。"""
    m = obj.material_slots[0].material if obj.material_slots else None
    img = None
    if m and m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image is not None:
                img = n.image
    if img is None or not obj.data.uv_layers:
        return None
    w, h = img.size
    px = list(img.pixels)
    uv = obj.data.uv_layers.active.data
    cols, n = [0.0, 0.0, 0.0], 0
    for poly in obj.data.polygons:
        for li in poly.loop_indices:
            vi = obj.data.loops[li].vertex_index
            if vi not in verts:
                continue
            u, v = uv[li].uv
            x = int((u % 1.0) * (w - 1)); y = int((v % 1.0) * (h - 1))
            k = (y * w + x) * 4
            for c in range(3):
                cols[c] += px[k + c]
            n += 1
    return [round(c / n, 3) for c in cols] if n else None


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    objs = {o.name: o for o in bpy.data.objects}
    rep = {"fbx": FBX, "objects": {}, "cables": {}}
    bvh_by_island = {}

    for name in PLUGS + CABLES:
        o = objs.get(name)
        if o is None:
            rep["objects"][name] = "缺失"
            continue
        isl = islands(o)
        mw = o.matrix_world
        info = {"type": o.type, "parent": o.parent.name if o.parent else None, "parent_bone": o.parent_bone, "verts": len(o.data.vertices),
                "tris": sum(t for t, _, _ in isl), "materials": material_info(o), "islands": []}
        for k, (t, vs, fs) in enumerate(isl):
            pts = [o.data.vertices[i].co for i in vs]
            lo, hi = bounds(pts)
            wpts = [mw @ p for p in pts]
            # 沿本地 X 分 10 段的截面 YZ 尺寸（看哪头粗、哪头细）
            prof = []
            for s in range(10):
                x0 = lo.x + (hi.x - lo.x) * s / 10; x1 = lo.x + (hi.x - lo.x) * (s + 1) / 10
                sl = [p for p in pts if x0 <= p.x <= x1 + 1e-9]
                if sl:
                    a, b = bounds(sl)
                    prof.append([round((b.y - a.y) * 1000, 1), round((b.z - a.z) * 1000, 1)])
                else:
                    prof.append(None)
            info["islands"].append({"index": k, "tris": t, "verts": len(vs), "bounds_local_mm": [mm(lo), mm(hi)], "size_local_mm": mm(hi - lo),
                                    "center_world": [round(c, 5) for c in (sum(wpts, Vector()) / len(wpts))], "profile_along_local_x_yz_mm": prof,
                                    "color_uv": sample_uv_color(o, vs) if o.type == "MESH" else None})
            if name in PLUGS:
                wv = [mw @ v.co for v in o.data.vertices]
                polys = [list(o.data.polygons[f].vertices) for f in fs]
                bvh_by_island[(name, k)] = BVHTree.FromPolygons(wv, polys)
        rep["objects"][name] = info

    # 线缆两端：按骨骼权重
    for name in CABLES:
        o = objs.get(name)
        if o is None:
            continue
        vg = {g.index: g.name for g in o.vertex_groups}
        mw = o.matrix_world
        def w_of(v, bone):
            for g in v.groups:
                if vg.get(g.group) == bone:
                    return g.weight
            return 0.0
        ends = {}
        for bone in ("Body", "Engine_L_Hinge"):
            ws = [(w_of(v, bone), v) for v in o.data.vertices]
            top = max(w for w, _ in ws)
            ring = [mw @ v.co for w, v in ws if w >= top - 1e-3]
            c = sum(ring, Vector()) / len(ring)
            r = sum((p - c).length for p in ring) / len(ring)
            near = []
            for (pn, k), tree in bvh_by_island.items():
                hit = tree.find_nearest(c)
                if hit[0] is not None:
                    near.append((hit[3], pn, k))
            near.sort()
            ends[bone] = {"ring_verts": len(ring), "center_world": [round(x, 5) for x in c], "ring_radius_mm": round(r * 1000, 2), "max_weight": round(top, 3),
                          "nearest_islands": [{"plug": pn, "island": k, "dist_mm": round(d * 1000, 2)} for d, pn, k in near[:3]]}
        length = (Vector(ends["Body"]["center_world"]) - Vector(ends["Engine_L_Hinge"]["center_world"])).length
        rep["cables"][name] = {"ends": ends, "end_to_end_mm": round(length * 1000, 1), "vertex_groups": sorted(set(vg.values())),
                               "color_uv": sample_uv_color(o, set(range(len(o.data.vertices))))}

    with open(os.path.join(REP, "connector_geometry.json"), "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=1)

    md = ["# 连接器几何核对（Blender 只读导入 robot-final.fbx）\n", f"- FBX：`{FBX}`（未修改）。坐标为 Blender 世界（米，Z 上）；物体本地 X = 审计记录的拔出轴（PlugBody 记为 +X，PlugEngine 记为 −X）。\n"]
    for name in PLUGS + CABLES:
        i = rep["objects"].get(name)
        if not isinstance(i, dict):
            md.append(f"- {name}：{i}\n"); continue
        md.append(f"## {name}\n\n- 类型 {i['type']}，父对象 {i['parent']} / 骨骼 {i['parent_bone']}，{i['verts']} 顶点，{i['tris']} 三角面，材质 {i['materials']}\n")
        md.append("| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |\n|---|---|---|---|---|---|")
        for s in i["islands"]:
            md.append(f"| {s['index']} | {s['tris']} | {s['size_local_mm']} | {s['bounds_local_mm']} | {s['profile_along_local_x_yz_mm']} | {s['color_uv']} |")
        md.append("")
    for name, c in rep["cables"].items():
        md.append(f"## {name} 两端\n\n- 端到端 {c['end_to_end_mm']} mm；顶点组 {c['vertex_groups']}；整根贴图取色 {c['color_uv']}\n")
        for bone, e in c["ends"].items():
            md.append(f"- {bone} 端：权重最高 {e['max_weight']} 的一圈 {e['ring_verts']} 个顶点，端面半径 {e['ring_radius_mm']} mm，中心 {e['center_world']}；最近：{e['nearest_islands']}")
        md.append("")
    with open(os.path.join(REP, "connector_geometry.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(md))
    print("[connectors] done")


if __name__ == "__main__":
    main()
