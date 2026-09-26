"""
边境维修站 · Blender 硬表面建模共用工具（Blender 5.2）。
由 Claude 辅助编写。工具函数整理自 ArtSource/Communicator/build_communicator.py（含开发者修复的倒角权重写法）；
通讯器脚本本身保持独立、未改动。新物品的建模脚本通过 sys.path 引入本模块。
"""

import json
import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

MATS = {}          # 材质名 -> bpy.types.Material，由 build_materials 填充
UV_SCALE = 0.06    # 立方体投影：每 6 cm 平铺一次磨损贴图


# ---------------------------------------------------------------------------
# 颜色
# ---------------------------------------------------------------------------

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


# ---------------------------------------------------------------------------
# 场景
# ---------------------------------------------------------------------------

def reset_scene(root_name, groups, prefix):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    root = bpy.data.collections.new(root_name)
    scene.collection.children.link(root)
    out = {}
    for g in groups:
        c = bpy.data.collections.new(prefix + g)
        root.children.link(c)
        out[g] = c
    return scene, out


# ---------------------------------------------------------------------------
# 贴图
# ---------------------------------------------------------------------------

def periodic_noise(size, sigma, rng):
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


def make_grime_texture(size=1024, seed=7, scratch_count=420):
    rng = np.random.default_rng(seed)
    low = periodic_noise(size, 60, rng)
    mid = periodic_noise(size, 6, rng)
    fine = periodic_noise(size, 1.2, rng)
    v = 0.84 + 0.07 * (low - 0.5) + 0.05 * (mid - 0.5) + 0.03 * (fine - 0.5)
    v -= 0.12 * np.clip((low - 0.62) * 3.0, 0, 1)
    scratches = np.zeros((size, size))
    prng = random.Random(seed + 4)
    for _ in range(scratch_count):
        x0, y0 = prng.uniform(0, size), prng.uniform(0, size)
        ang = prng.uniform(0, math.pi)
        length = prng.uniform(8, 70)
        draw_line_wrap(scratches, x0, y0, x0 + math.cos(ang) * length, y0 + math.sin(ang) * length, prng.uniform(0.06, 0.16))
    v = np.clip(v + scratches, 0, 1)
    return np.clip(np.stack([v, v * 0.985, v * 0.955], axis=-1), 0, 1)


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
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "C": ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "J": ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "X": ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    "/": ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
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


def disc(img, cx, cy, r, color, ring=0):
    h, w, _ = img.shape
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    mask = d <= r if ring <= 0 else (d <= r) & (d >= r - ring)
    img[mask] = color


def save_image(name, rgb, directory):
    """rgb: (h, w, 3)，行 0 为图像顶部；保存为 sRGB 8 位 PNG。"""
    h, w, _ = rgb.shape
    rgba = np.concatenate([rgb, np.ones((h, w, 1))], axis=-1)
    rgba = np.flipud(rgba).astype(np.float32)
    img = bpy.data.images.new(os.path.splitext(name)[0], width=w, height=h, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(directory, name)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    return img


# ---------------------------------------------------------------------------
# 材质
# ---------------------------------------------------------------------------

def ensure_node_tree(owner):
    if getattr(owner, "node_tree", None) is None:
        try:
            owner.use_nodes = True
        except Exception:
            pass
    return owner.node_tree


def build_materials(specs, images):
    """specs: {name: dict(color, metallic, roughness, base_map, emission_map, emission_color, emission_strength)}"""
    MATS.clear()
    for name, spec in specs.items():
        mat = bpy.data.materials.new(name)
        tree = ensure_node_tree(mat)
        nodes, links = tree.nodes, tree.links
        bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
        tint = [srgb_to_linear(c) for c in hex_rgb(spec["color"])]
        bsdf.inputs["Metallic"].default_value = spec["metallic"]
        bsdf.inputs["Roughness"].default_value = spec["roughness"]
        tex = None
        if spec.get("base_map"):
            tex = nodes.new("ShaderNodeTexImage")
            tex.image = images[spec["base_map"]]
            mul = nodes.new("ShaderNodeVectorMath")
            mul.operation = "MULTIPLY"
            mul.inputs[1].default_value = tint
            links.new(tex.outputs["Color"], mul.inputs[0])
            links.new(mul.outputs["Vector"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (*tint, 1.0)
        if spec.get("emission_strength", 0) > 0:
            ecol = [srgb_to_linear(c) for c in hex_rgb(spec.get("emission_color", "#FFFFFF"))]
            if spec.get("emission_map") and tex is not None:
                links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
            else:
                bsdf.inputs["Emission Color"].default_value = (*ecol, 1.0)
            bsdf.inputs["Emission Strength"].default_value = spec["emission_strength"]
        MATS[name] = mat
    return MATS


def write_material_manifest(specs, path):
    out = {"materials": []}
    for name, spec in specs.items():
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
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)


# ---------------------------------------------------------------------------
# 几何
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


def circle_profile(r, segs, phase=0.0):
    return [(math.cos(2 * math.pi * i / segs + phase) * r, math.sin(2 * math.pi * i / segs + phase) * r) for i in range(segs)]


def mark_bevel_weight(bm, face, value=1.0):
    """给一个面的边界边打倒角权重。先建图层再取边，避免图层新建后边引用失效。"""
    layer = bm.edges.layers.float.get("bevel_weight_edge") or bm.edges.layers.float.new("bevel_weight_edge")
    for e in face.edges:
        e[layer] = value


def bm_tube(points, radii, segs=12, jag_end=0.0, rng=None, closed_loop=False):
    bm = bmesh.new()
    pts = [Vector(p) for p in points]
    n_pts = len(pts)
    rings = []
    normal = None
    for i, p in enumerate(pts):
        if closed_loop:
            t = pts[(i + 1) % n_pts] - pts[i - 1]
        elif i == 0:
            t = pts[1] - pts[0]
        elif i == n_pts - 1:
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
        last = i == n_pts - 1 and not closed_loop
        for k in range(segs):
            a = 2 * math.pi * k / segs
            r = radii[i]
            off_t = 0.0
            if last and jag_end > 0 and rng is not None:
                r *= rng.uniform(0.65, 1.08)
                off_t = -rng.uniform(0.0, jag_end)
            ring.append(bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * r + t * off_t))
        rings.append(ring)
    count = n_pts if closed_loop else n_pts - 1
    for i in range(count):
        a, b = rings[i], rings[(i + 1) % n_pts]
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    if not closed_loop:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def box_uv(bm, scale=UV_SCALE):
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            co = loop.vert.co
            u, v = (co.y, co.z) if ax == 0 else (co.x, co.z) if ax == 1 else (co.x, co.y)
            loop[uv].uv = (u / scale, v / scale)


def planar_uv(bm, origin, u_axis, v_axis, u_size, v_size, u0=0.0, v0=0.0, u_span=1.0, v_span=1.0):
    """把网格投影到一个平面上的 UV 区域：(u0,v0) 起、宽 u_span、高 v_span（用于贴图集中的指定区域）。"""
    uv = bm.loops.layers.uv.verify()
    o, ua, va = Vector(origin), Vector(u_axis).normalized(), Vector(v_axis).normalized()
    for f in bm.faces:
        for loop in f.loops:
            d = loop.vert.co - o
            loop[uv].uv = (u0 + (d.dot(ua) / u_size + 0.5) * u_span, v0 + (d.dot(va) / v_size + 0.5) * v_span)


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
# 统计 / 导出
# ---------------------------------------------------------------------------

def triangle_count(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eo = obj.evaluated_get(depsgraph)
    me = eo.to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    eo.to_mesh_clear()
    return n


def collect_stats(groups, images, material_names):
    stats = {"groups": {}, "materials": sorted(material_names), "textures": {}}
    total = 0
    for g, coll in groups.items():
        objs = [o for o in coll.objects if o.type == "MESH"]
        tris = sum(triangle_count(o) for o in objs)
        total += tris
        mats = sorted({s.material.name for o in objs for s in o.material_slots if s.material})
        stats["groups"][g] = {"triangles": tris, "objects": len(objs), "materials": mats}
    stats["total_triangles"] = total
    for name, img in images.items():
        stats["textures"][name] = list(img.size)
    return stats


def export_groups(groups, directory, prefix):
    os.makedirs(directory, exist_ok=True)
    view_layer = bpy.context.view_layer
    for g, coll in groups.items():
        for o in bpy.context.scene.objects:
            o.select_set(False)
        objs = [o for o in coll.objects if o.type == "MESH"]
        for o in objs:
            o.hide_set(False)
            o.select_set(True)
        view_layer.objects.active = objs[0]
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(directory, f"{prefix}_{g}.fbx"),
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


# ---------------------------------------------------------------------------
# 渲染
# ---------------------------------------------------------------------------

def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def setup_studio(scene, floor_z, target=(0, 0, 0.0)):
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
    fb = next(n for n in ensure_node_tree(floor_mat).nodes if n.type == "BSDF_PRINCIPLED")
    fb.inputs["Base Color"].default_value = (0.09, 0.1, 0.11, 1.0)
    fb.inputs["Roughness"].default_value = 0.6
    bm = bm_box((2.0, 2.0, 0.002), (0, 0, floor_z - 0.001))
    me = bpy.data.meshes.new("Studio_Floor")
    bm.to_mesh(me)
    bm.free()
    floor = bpy.data.objects.new("Studio_Floor", me)
    me.materials.append(floor_mat)
    studio.objects.link(floor)

    t = Vector(target)

    def light(name, loc, energy, size, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = energy
        data.size = size
        data.color = color
        obj = bpy.data.objects.new(name, data)
        obj.location = Vector(loc) + t
        look_at(obj, t)
        studio.objects.link(obj)

    light("Key", (-0.45, -0.55, 0.5), 40, 0.4, (1.0, 0.96, 0.9))
    light("Fill", (0.6, -0.4, 0.15), 14, 0.6, (0.85, 0.92, 1.0))
    light("Rim", (0.2, 0.6, 0.55), 30, 0.35, (0.8, 0.9, 1.0))
    light("RimBack", (-0.5, 0.55, 0.25), 16, 0.45)

    cam = bpy.data.objects.new("StudioCam", bpy.data.cameras.new("StudioCam"))
    studio.objects.link(cam)
    scene.camera = cam

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1050
    scene.render.image_settings.file_format = "PNG"
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
    except Exception:
        pass
    return cam


def render_view(scene, cam, path, loc, target, lens):
    cam.location = Vector(loc)
    cam.data.lens = lens
    look_at(cam, Vector(target))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
