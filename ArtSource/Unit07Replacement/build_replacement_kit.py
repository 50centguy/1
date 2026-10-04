"""
边境维修站 · UNIT 07 左引擎替换补件（Blender 5.x 生成脚本）
由 Claude 辅助编写。运行：run_blender.bat（或 blender -b --factory-startup --python build_replacement_kit.py [-- --norender]）

只读导入 Assets/RobotV4/Model/robot-final.fbx（不写回），生成：
  1) 两根外露线缆的“机身端拔下”静态网格（黄 ExternalCable_L、灰 AuxCable_L）：
     - 用原蒙皮线的网格复制、逐圈弯到一条**等弧长**的新曲线上（不拉长；引擎端那一圈与原线完全重合，引擎端保持连接）；
     - 自由端插头 = 原 *_PlugBody 里的“插头圆筒”网格岛（原线机身端就接在这个岛上），按原相对位姿跟着线端走；
  2) 机身固定插座补件：原 *_PlugBody 里的“法兰”网格岛（固定在机身的面板座）+ 新加的插座内腔（插头拔出后，法兰孔里看到的是座底和针脚，不会透到机身内部）；
  3) 替换左引擎材质的贴图变体：T_Engine_BaseColor / T_Internal_BaseColor 去掉轻度污渍与细划痕（只提亮比局部均值暗、且对比度低的像素；文字、孔位、
     面板线等高对比细节保留），不改 UV、不改网格；
  4) 挂点空物体、标定用复制件（Unity 里核对坐标系用）、统计、渲染。
理由见 Reports/connector_geometry.md 与 ../../Assets/BorderRepair/Art/Unit07Replacement/README.md（connector_contract.md）。

坐标：Blender Z 上、米。断开线缆的网格写在原 *_PlugEngine 的物体本地坐标里（运行时挂在 Engine_L_Hinge 下、与 *_PlugEngine 同一本地位姿），
固定插座补件写在原 *_PlugBody 的物体本地坐标里（挂在 Body 下、与 *_PlugBody 同一本地位姿）。
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
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FBX = os.path.join(ROOT, "Assets", "RobotV4", "Model", "robot-final.fbx")
TEXSRC = os.path.join(ROOT, "Assets", "RobotV4", "Model", "textures")
EXPORT = os.path.join(HERE, "Export")
TEXOUT = os.path.join(HERE, "Textures")
REN = os.path.join(HERE, "Renders")
REP = os.path.join(HERE, "Reports")
for d in (EXPORT, TEXOUT, REN, REP):
    os.makedirs(d, exist_ok=True)
NO_RENDER = "--norender" in sys.argv

CABLES = [  # 名称、颜色说明、机身端插头、引擎端插头
    ("ExternalCable_L", "黄", "ExternalCable_L_PlugBody", "ExternalCable_L_PlugEngine"),
    ("AuxCable_L", "灰", "AuxCable_L_PlugBody", "AuxCable_L_PlugEngine"),
]
STRAIGHT_FRAC = 0.18      # 弯曲前先直行的弧长比例（离开引擎端插头一小段）
FLANGE_CLEAR = 0.015      # 自由插头离机身法兰外面至少 15 mm（确实拔出来了）


# ---------------------------------------------------------------------------
# 网格工具
# ---------------------------------------------------------------------------

def islands(me):
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, faces = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop(); faces.append(g.index)
            for v in g.verts:
                for h in v.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        out.append(faces)
    bm.free()
    return sorted(out, key=lambda fs: -len(fs))


def mesh_from_faces(src_me, faces, xform, name):
    """把 src_me 的若干面（含 UV、材质）复制成新网格；顶点经 xform 变换。"""
    bm = bmesh.new(); bm.from_mesh(src_me); bm.faces.ensure_lookup_table()
    keep = set(faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.transform(bm, matrix=xform, verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in src_me.materials:
        me.materials.append(m)
    return me


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def cable_rings(me):
    """原线是 17 圈 × 12 顶点、两端 12 边形封口。返回按顺序的顶点圈（从一端封口开始）。"""
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    caps = [f for f in bm.faces if len(f.verts) > 4]
    ring = [v.index for v in caps[0].verts]
    rings, used = [ring], set(ring)
    while True:
        nxt = []
        for vi in rings[-1]:
            for e in bm.verts[vi].link_edges:
                o = e.other_vert(bm.verts[vi]).index
                if o not in used and o not in nxt:
                    nxt.append(o)
        if not nxt:
            break
        rings.append(nxt); used.update(nxt)
    bm.free()
    return rings


def frames_along(points, n0):
    """平行移动标架：返回每点 (T, N, B)。"""
    T = []
    for k in range(len(points)):
        a = points[max(k - 1, 0)]; b = points[min(k + 1, len(points) - 1)]
        T.append((b - a).normalized())
    N = [(n0 - T[0] * n0.dot(T[0])).normalized()]
    for k in range(1, len(points)):
        n = N[-1] - T[k] * N[-1].dot(T[k])
        N.append(n.normalized())
    return [(T[k], N[k], T[k].cross(N[k])) for k in range(len(points))]


def frame_matrix(c, f):
    T, N, B = f
    m = Matrix((N, B, T)).transposed()
    out = m.to_4x4(); out.translation = c
    return out


def curve_points(start, t0, bend_dir, length, total_deg, s_list):
    """等弧长曲线：先直行 STRAIGHT_FRAC·L，然后在 (t0, bend_dir) 平面内匀速转 total_deg。按弧长 s_list 取点（数值积分，1 mm 步）。"""
    a = STRAIGHT_FRAC * length
    k_rate = math.radians(total_deg) / max(length - a, 1e-6)
    steps = max(200, int(length / 0.0005))
    ds = length / steps
    pts, s, p = [start.copy()], 0.0, start.copy()
    samples = [0.0]
    for i in range(steps):
        sm = s + ds / 2
        th = 0.0 if sm < a else (sm - a) * k_rate
        d = (t0 * math.cos(th) + bend_dir * math.sin(th)).normalized()
        p = p + d * ds; s += ds
        pts.append(p.copy()); samples.append(s)
    out = []
    for sv in s_list:
        i = min(int(sv / ds), steps - 1); f = (sv - samples[i]) / ds
        out.append(pts[i].lerp(pts[i + 1], f))
    return out


# ---------------------------------------------------------------------------
# 贴图清洁（新件材质）
# ---------------------------------------------------------------------------

def box_blur(a, r):
    """a: H×W×C，盒式模糊（积分图）。"""
    p = np.pad(a, ((r + 1, r), (r + 1, r), (0, 0)), mode="edge")
    c = p.cumsum(0).cumsum(1)
    s = c[2 * r + 1:, 2 * r + 1:] - c[:-2 * r - 1, 2 * r + 1:] - c[2 * r + 1:, :-2 * r - 1] + c[:-2 * r - 1, :-2 * r - 1]
    return s / float((2 * r + 1) ** 2)


def clean_texture(src_name, dst_name):
    img = bpy.data.images.load(os.path.join(TEXSRC, src_name))
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    rgb = px[:, :, :3]
    lum = rgb.mean(axis=2, keepdims=True)
    mask = (lum > 0.03).astype(np.float32)                         # 贴图岛（背景是纯黑）
    blur = box_blur(rgb * mask, 5) / np.maximum(box_blur(mask, 5), 1e-4)   # 只在岛内取局部均值，不把黑底混进来
    d = (blur - rgb).mean(axis=2, keepdims=True)                    # >0：比周围暗（污渍 / 细划痕 / 也包括文字和孔）
    weak = np.clip(1.0 - (d - 0.09) / 0.07, 0.0, 1.0)               # d ≤ 0.09 全量处理，≥ 0.16（文字、孔、面板线等高对比）不动
    lift = np.where(d > 0, d * 0.9 * weak, 0.0)
    out = rgb + lift * mask
    # 象牙白外壳（亮、低饱和）：向局部均值再收 30%（去掉细碎旧化），并略减泛黄（旧漆发黄、新漆稍白）；黄色线缆、警示件等高饱和区域不动
    mx, mn = rgb.max(axis=2, keepdims=True), rgb.min(axis=2, keepdims=True)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    ivory = ((lum > 0.55) & (sat < 0.30)).astype(np.float32) * mask
    out = out * (1 - 0.30 * ivory) + np.maximum(blur, out) * 0.30 * ivory
    gray = out.mean(axis=2, keepdims=True)
    out = out * (1 - 0.10 * ivory) + gray * 0.10 * ivory            # 饱和度降 10%（少一点泛黄）
    out = np.clip(out * (1.0 + 0.025 * ivory), 0.0, 1.0)            # 外壳提亮 2.5%
    res = np.concatenate([out, px[:, :, 3:4]], axis=2)
    dst = bpy.data.images.new(dst_name, w, h, alpha=True)
    dst.pixels[:] = res.ravel()
    dst.filepath_raw = os.path.join(TEXOUT, dst_name + ".png")
    dst.file_format = "PNG"
    dst.save()
    changed = float((np.abs(out - rgb).max(axis=2) > 1.0 / 255).mean())
    return {"source": src_name, "output": dst_name + ".png", "size": [w, h], "pixels_changed_ratio": round(changed, 4),
            "max_lift": round(float(lift.max()), 4)}, img, dst


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    scene = bpy.context.scene
    O = {o.name: o for o in bpy.data.objects}
    ref_coll = bpy.data.collections.new("_RobotV4_Reference（只读导入，不导出）")
    scene.collection.children.link(ref_coll)
    for o in list(scene.collection.objects):
        scene.collection.objects.unlink(o); ref_coll.objects.link(o)
    kit = bpy.data.collections.new("Unit07_ReplacementKit")
    scene.collection.children.link(kit)
    dg = bpy.context.evaluated_depsgraph_get()

    # 障碍：七号全部网格（静止姿态，蒙皮线按原状）——两根原线、两个机身端插头不算（断开状态里都隐藏）
    CLIPS = {"ExternalCable_L": "ExternalCable_Clip_L"}      # 黄线上的卡箍：原挂在 Body 骨骼下、没有蒙皮，拔线后要跟着线走
    hidden_when_unplugged = {c for c, *_ in CABLES} | {pb for _, _, pb, _ in CABLES} | set(CLIPS.values())
    obst_tris, obst_verts, obst_names = [], [], []
    for o in ref_coll.objects:
        if o.type != "MESH" or o.name in hidden_when_unplugged:
            continue
        ev = o.evaluated_get(dg); me = ev.to_mesh()
        base = len(obst_verts)
        obst_verts += [o.matrix_world @ v.co for v in me.vertices]
        for p in me.polygons:
            obst_tris.append([base + i for i in p.vertices]); obst_names.append(o.name)
        ev.to_mesh_clear()
    # 旧总成在工作台上直立摆放（审计 engine_path：只绕竖直轴转），最低点 = 引擎件最低点；断开线缆不能低于它 + 15 mm，否则会压进操作垫
    eng_min_z = min((o.matrix_world @ v.co).z for o in ref_coll.objects if o.type == "MESH" and o.parent_bone == "Engine_L_Hinge" for v in o.data.vertices)
    LOW_MARGIN = 0.015
    stats = {"cables": {}, "sockets": {}, "textures": {}, "notes": [f"引擎件最低点 z = {eng_min_z:.4f}（Blender 世界，静止姿态）；断开线缆最低点要求 ≥ {eng_min_z + LOW_MARGIN:.4f}"]}
    anchors = {}

    for cname, color, pbn, pen in CABLES:
        cab, pb, pe = O[cname], O[pbn], O[pen]
        # --- 原线（静止姿态）的顶点与圈
        ev = cab.evaluated_get(dg); cme = ev.to_mesh().copy(); ev.to_mesh_clear()
        cw = cab.matrix_world
        V = [cw @ v.co for v in cme.vertices]
        rings = cable_rings(cme)
        vg = {g.index: g.name for g in cab.vertex_groups}
        def we(i):
            for g in cab.data.vertices[i].groups:
                if vg[g.group] == "Engine_L_Hinge":
                    return g.weight
            return 0.0
        if sum(we(i) for i in rings[0]) < sum(we(i) for i in rings[-1]):
            rings.reverse()                     # 让第 0 圈是引擎端（保持连接的一端）
        C = [sum((V[i] for i in r), Vector()) / len(r) for r in rings]
        s = [0.0]
        for k in range(1, len(C)):
            s.append(s[-1] + (C[k] - C[k - 1]).length)
        L = s[-1]
        down = Vector((0, 0, -1))
        t0 = (C[1] - C[0]).normalized()
        n0 = (down - t0 * down.dot(t0)).normalized()
        F0 = frames_along(C, n0)
        local = {}
        for k, r in enumerate(rings):
            T, N, B = F0[k]
            for i in r:
                d = V[i] - C[k]
                local[i] = (k, d.dot(N), d.dot(B), d.dot(T))

        # --- 机身端插头：两个网格岛（法兰 + 插头圆筒）；线的机身端接在离它最近的那个岛上
        isl = islands(pb.data)
        pbw = pb.matrix_world
        def island_center(fs):
            vs = {i for f in fs for i in pb.data.polygons[f].vertices}
            return sum((pbw @ pb.data.vertices[i].co for i in vs), Vector()) / len(vs)
        def island_dist(fs, p):
            vs = [pbw @ pb.data.vertices[i].co for f in fs for i in pb.data.polygons[f].vertices]
            return min((q - p).length for q in vs)
        body_end = C[-1]
        plug_isl = min(isl, key=lambda fs: island_dist(fs, body_end))
        flange_isl = [fs for fs in isl if fs is not plug_isl][0]
        flange_pts = [pb.data.vertices[i].co for f in flange_isl for i in pb.data.polygons[f].vertices]
        flange_out_x = max(p.x for p in flange_pts)                      # 法兰外表面（插头本地 X）
        pull_dir_w = (pbw.to_3x3() @ Vector((1, 0, 0))).normalized()   # 原线机身端在插头本地 +X 一侧 → +X 是拔出方向

        # --- 自由插头相对线端的位姿（原状）
        M_end0 = frame_matrix(C[-1], F0[-1])

        # --- 搜索弯曲：等弧长、引擎端不动；最大化离七号 / 另一根线的最小距离
        bvh = BVHTree.FromPolygons(obst_verts, obst_tris)
        side = t0.cross(n0).normalized()
        best = None
        plug_pts0 = [pbw @ pb.data.vertices[i].co for f in plug_isl for i in pb.data.polygons[f].vertices]
        flange_center_w = pbw @ Vector((flange_out_x, 0, 0))
        for total in range(40, 181, 10):
            for phi in range(-60, 61, 10):
                bd = (n0 * math.cos(math.radians(phi)) + side * math.sin(math.radians(phi))).normalized()
                P = curve_points(C[0], t0, bd, L, total, s)
                F1 = frames_along(P, n0)
                newV = []
                for i in range(len(V)):
                    k, a, b, c = local[i]
                    T, N, B = F1[k]
                    newV.append(P[k] + N * a + B * b + T * c)
                M_end1 = frame_matrix(P[-1], F1[-1])
                Xp = M_end1 @ M_end0.inverted()
                plugV = [Xp @ q for q in plug_pts0]
                # 约束：自由插头离开法兰外面 ≥ 15 mm；全部高于引擎最低点 + 20 mm
                pc = sum(plugV, Vector()) / len(plugV)
                if min(min(v.z for v in newV), min(q.z for q in plugV)) < eng_min_z + LOW_MARGIN:
                    continue
                if (pc - flange_center_w).dot(pull_dir_w) < FLANGE_CLEAR and (pc - flange_center_w).length < 0.03:
                    continue
                dmin = 1.0
                # 只看离引擎端 ≥ 3 圈的部分（引擎端那几圈与原线相同、本来就贴着插头）
                for k in range(3, len(rings)):
                    for i in rings[k]:
                        hit = bvh.find_nearest(newV[i])
                        if hit[0] is not None:
                            dmin = min(dmin, hit[3])
                for q in plugV:
                    hit = bvh.find_nearest(q)
                    if hit[0] is not None:
                        dmin = min(dmin, hit[3])
                if best is None or dmin > best[0]:
                    best = (dmin, total, phi, newV, Xp, P, F1)
        dmin, total, phi, newV, Xp, P, F1 = best

        # --- 写网格：断开线缆（引擎端插头本地坐标）
        pew_inv = pe.matrix_world.inverted()
        cme2 = cme.copy(); cme2.name = f"{cname}_Unplugged_mesh"
        for i, v in enumerate(cme2.vertices):
            v.co = pew_inv @ newV[i]
        root = bpy.data.objects.new(f"Kit_{cname}_Unplugged", None)
        root.empty_display_type = "ARROWS"; root.empty_display_size = 0.02
        kit.objects.link(root); root.matrix_world = pe.matrix_world.copy()
        cobj = bpy.data.objects.new(f"{cname}_Unplugged", cme2); kit.objects.link(cobj); cobj.parent = root
        pme = mesh_from_faces(pb.data, plug_isl, pew_inv @ Xp @ pbw, f"{cname}_FreePlug_mesh")
        pobj = bpy.data.objects.new(f"{cname}_FreePlug", pme); kit.objects.link(pobj); pobj.parent = root
        clip_info = None
        if cname in CLIPS:
            clip = O[CLIPS[cname]]
            cc = sum((clip.matrix_world @ v.co for v in clip.data.vertices), Vector()) / len(clip.data.vertices)
            kc = min(range(len(C)), key=lambda k: (C[k] - cc).length)
            Xc = frame_matrix(P[kc], F1[kc]) @ frame_matrix(C[kc], F0[kc]).inverted()
            clme = mesh_from_faces(clip.data, list(range(len(clip.data.polygons))), pew_inv @ Xc @ clip.matrix_world, f"{cname}_Clip_Unplugged_mesh")
            clobj = bpy.data.objects.new(f"{cname}_Clip_Unplugged", clme); kit.objects.link(clobj); clobj.parent = root
            clip_info = {"source": clip.name, "ring_index": kc, "arc_mm": round(s[kc] * 1000, 1), "tris": tri_count(clme)}
        # 挂点：安装端（引擎端第 0 圈中心）、自由端（插头圆筒插入端中心，插头本地 −X 端）
        def add_anchor(name, parent, m_world):
            e = bpy.data.objects.new(name, None); e.empty_display_type = "ARROWS"; e.empty_display_size = 0.012
            kit.objects.link(e); e.parent = parent; e.matrix_parent_inverse = Matrix.Identity(4)
            e.matrix_basis = parent.matrix_world.inverted() @ m_world
            return e
        plug_loc_pts = [pb.data.vertices[i].co for f in plug_isl for i in pb.data.polygons[f].vertices]
        tip_local = Vector((min(p.x for p in plug_loc_pts), 0, 0))
        m_mount = frame_matrix(P[0], F1[0])
        m_free = Xp @ pbw @ Matrix.Translation(tip_local)
        a1 = add_anchor(f"Anchor_{cname}_MountEngineEnd", root, m_mount)
        a2 = add_anchor(f"Anchor_{cname}_FreePlugTip", root, m_free)

        # --- 固定插座补件（机身端插头本地坐标）：法兰 + 内腔
        sroot = bpy.data.objects.new(f"Kit_{cname}_BodySocketFixed", None)
        sroot.empty_display_type = "ARROWS"; sroot.empty_display_size = 0.02
        kit.objects.link(sroot); sroot.matrix_world = pbw.copy()
        # 原法兰是实心圆片（正面只画了一圈），插头拔掉后会像一颗空白按钮。这里按原尺寸重做成“带孔的法兰”（外径、厚度、颜色同原法兰，
        # 孔径 = 原插头圆筒外径），孔里是插座内腔（座底 + 三根针脚）。内腔完全在原插头圆筒占过的空间里，不会碰到机身外壳。
        r_barrel = max(max(abs(p.y), abs(p.z)) for p in plug_loc_pts)
        x_in = min(p.x for p in plug_loc_pts)                      # 原插头插进去最深处
        r_flange = max(max(abs(p.y), abs(p.z)) for p in flange_pts)
        flange_in_x = min(p.x for p in flange_pts)
        puv = pb.data.uv_layers.active.data
        def flat_uv(me, faces_src):
            pick = pb.data.polygons[faces_src[len(faces_src) // 2]].loop_indices[0]
            uvl = me.uv_layers.new(name="UVMap")
            for d in uvl.data:
                d.uv = puv[pick].uv
        def lathe(profile, seg, name, uv_src):
            """profile: [(x, r), ...]，绕本地 X 旋转成网格（首尾 r = 0 时自动收口）。"""
            bm = bmesh.new()
            rings_v = [[bm.verts.new((x, r * math.cos(2 * math.pi * j / seg), r * math.sin(2 * math.pi * j / seg))) for j in range(seg)] for x, r in profile]
            for a, b in zip(rings_v[:-1], rings_v[1:]):
                for j in range(seg):
                    bm.faces.new((a[j], a[(j + 1) % seg], b[(j + 1) % seg], b[j]))
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)       # 封闭网格：法线朝外（URP 背面剔除）
            me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
            me.materials.append(pb.data.materials[0])
            flat_uv(me, uv_src)
            return me
        ch = 0.0006
        fme = lathe([(flange_in_x, r_barrel), (flange_in_x, r_flange), (flange_out_x - ch, r_flange), (flange_out_x, r_flange - ch),
                     (flange_out_x, r_barrel + ch), (flange_out_x - ch, r_barrel), (flange_in_x, r_barrel)], 32, f"{cname}_SocketFlange_mesh", flange_isl)
        fobj = bpy.data.objects.new(f"{cname}_SocketFlange", fme); kit.objects.link(fobj); fobj.parent = sroot
        ro, ri = r_barrel - 0.00005, r_barrel - 0.0010
        x0, x1 = x_in + 0.0005, flange_out_x - ch
        ime = lathe([(x1, ro), (x0, ro), (x0, 0.0), (x0 + 0.0010, 0.0), (x0 + 0.0010, ri), (x1 - 0.0004, ri), (x1, ro)], 24, f"{cname}_SocketInsert_mesh", plug_isl)
        bm = bmesh.new(); bm.from_mesh(ime)
        for ang in (90, 210, 330):                                  # 三根针脚
            cy, cz = 0.45 * ri * math.cos(math.radians(ang)), 0.45 * ri * math.sin(math.radians(ang))
            pv = [[bm.verts.new((x, cy + 0.0007 * math.cos(2 * math.pi * j / 6), cz + 0.0007 * math.sin(2 * math.pi * j / 6))) for j in range(6)] for x in (x0 + 0.0010, x0 + 0.0070)]
            pin = [bm.faces.new((pv[0][j], pv[0][(j + 1) % 6], pv[1][(j + 1) % 6], pv[1][j])) for j in range(6)]
            pin += [bm.faces.new(pv[1]), bm.faces.new(pv[0][::-1])]
            bmesh.ops.recalc_face_normals(bm, faces=pin)
        bm.to_mesh(ime); bm.free()
        ime.uv_layers.remove(ime.uv_layers[0]); flat_uv(ime, plug_isl)
        iobj = bpy.data.objects.new(f"{cname}_SocketInsert", ime); kit.objects.link(iobj); iobj.parent = sroot
        a3 = add_anchor(f"Anchor_{cname}_SocketFace", sroot, pbw @ Matrix.Translation(Vector((flange_out_x, 0, 0))))
        # 标定用：原两个插头的完整复制（Unity 里核对“本补件的本地坐标 = 原物体本地坐标”；不进预制体）
        cal1 = bpy.data.objects.new(f"Calib_{pbn}", pb.data.copy()); kit.objects.link(cal1); cal1.parent = sroot
        cal2 = bpy.data.objects.new(f"Calib_{pen}", pe.data.copy()); kit.objects.link(cal2); cal2.parent = root

        # 这根断开线缆（含自由插头）加进障碍，后一根要避开它（两根一起拔下）
        base = len(obst_verts); obst_verts += newV
        obst_tris += [[base + i for i in p.vertices] for p in cme.polygons]
        base = len(obst_verts); obst_verts += [Xp @ pbw @ v.co for v in pb.data.vertices]
        obst_tris += [[base + i for i in pb.data.polygons[f].vertices] for f in plug_isl]
        free_c = Xp @ pbw @ Vector((0, 0, 0))
        low = min(min(v.z for v in newV), min((Xp @ q).z for q in plug_pts0))
        stats["cables"][cname] = {
            "color": color, "original_tris": tri_count(cme), "unplugged_tris": tri_count(cme2), "free_plug_tris": tri_count(pme),
            "arc_length_mm": round(L * 1000, 2), "bend_total_deg": total, "bend_plane_phi_deg": phi,
            "min_dist_to_robot_mm_blender_bvh": round(dmin * 1000, 2),
            "free_plug_out_of_flange_mm": round((free_c - flange_center_w).dot(pull_dir_w) * 1000, 1),
            "lowest_point_z": round(low, 4), "lowest_above_engine_bottom_mm": round((low - eng_min_z) * 1000, 1), "engine_end_ring_shift_mm": round(max((newV[i] - V[i]).length for i in rings[0]) * 1000, 4),
            "frame_parent": f"{pen} 的物体本地坐标（运行时父对象 Engine_L_Hinge，本地位姿同 {pen}）", "clip": clip_info,
        }
        stats["sockets"][cname] = {"plug_body_islands_tris": [len(fs) for fs in isl], "flange_tris": tri_count(fme), "insert_tris": tri_count(ime),
                                   "barrel_radius_mm": round(r_barrel * 1000, 2), "insert_depth_mm": round((x1 - x0) * 1000, 1),
                                   "pull_axis": f"{pbn} 本地 +X（世界 {[round(c, 3) for c in pull_dir_w]}）",
                                   "frame_parent": f"{pbn} 的物体本地坐标（运行时父对象 Body，本地位姿同 {pbn}）"}
        anchors[cname] = {a.name: {"parent": a.parent.name, "local_pos_mm": [round(c * 1000, 3) for c in a.matrix_basis.translation],
                                   "local_rot_wxyz": [round(c, 6) for c in a.matrix_basis.to_quaternion()]} for a in (a1, a2, a3)}

    # --- 新件贴图
    for src, dst in (("T_Engine_BaseColor.png", "T_Engine_BaseColor_ReplacementL"), ("T_Internal_BaseColor.png", "T_Internal_BaseColor_ReplacementL")):
        info, _, _ = clean_texture(src, dst)
        stats["textures"][dst] = info

    with open(os.path.join(REP, "kit_stats.json"), "w", encoding="utf-8") as f:
        json.dump({"stats": stats, "anchors_blender": anchors}, f, ensure_ascii=False, indent=1)
    print("[kit] stats", json.dumps(stats, ensure_ascii=False))

    # --- 导出（只导出补件集合；参考七号不导出）
    for o in scene.objects:
        o.select_set(False)
    for o in kit.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = kit.objects[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXPORT, "UNIT07_ReplacementKit_Cables.fbx"), use_selection=True, object_types={"EMPTY", "MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Unit07ReplacementKit.blend"), relative_remap=True)
    print("[kit] done")


if __name__ == "__main__":
    main()
