"""Shared Blender helpers for the V2 clinic props (repair bench, surgery table): CC0-derived texture synthesis,
bmesh primitives with box UVs, and object creation in a world-authored, rotation-free hierarchy.
Callers set CC0, TEX, MAT_SPEC and call init() before building."""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

CC0 = ""
TEX = ""
SCENE = None
MAT_SPEC = {}
MATS = {}
def load_rgb(asset, kind="Color"):
    path = os.path.join(CC0, asset, "%s_1K-JPG_%s.jpg" % (asset, kind))
    img = bpy.data.images.load(path, check_existing=True)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[..., :3]


def down(a, size):
    h = a.shape[0]
    f = h // size
    return a[:f * size, :f * size].reshape(size, f, size, f, -1).mean(axis=(1, 3))


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def lum(a):
    return a[..., 0] * .3 + a[..., 1] * .59 + a[..., 2] * .11


def sat(a):
    mx, mn = a.max(axis=-1), a.min(axis=-1)
    return (mx - mn) / np.maximum(mx, 1e-4)


def save_png(name, rgb, alpha=None):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=alpha is not None)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(rgb, 0, 1)
    if alpha is not None:
        rgba[..., 3] = np.clip(alpha, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    path = os.path.join(TEX, name + ".png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def chips_from(asset, size):
    """Bare-metal chips and scratches of a painted-metal set as a 0..1 mask (low saturation = paint gone)."""
    c = down(load_rgb(asset), size)
    m = np.clip((0.55 - sat(c)) * 2.5, 0, 1)
    return m


def painted(name, base, chip_mask, steel, grain, size, grime=0.25, rust=0.3):
    g = (lum(grain) - lum(grain).mean()) * 0.6
    col = base[None, None, :] * (1 + g[..., None])
    col = col * (1 - chip_mask[..., None]) + steel * chip_mask[..., None]
    # soft grime gathering in darker grain
    col *= (1 - grime * np.clip(-g * 4, 0, 1))[..., None]
    # rust blooms: a second, offset chip pattern, brown-orange, strongest where paint is already gone
    if rust > 0:
        r = np.roll(np.roll(chip_mask, size // 3, axis=0), size // 5, axis=1)
        r = np.clip(r * 1.4 + chip_mask * .5 - .35, 0, 1) * rust
        col = col * (1 - r[..., None]) + np.array([.42, .22, .11]) * (0.75 + .5 * (lum(grain) - .5))[..., None] * r[..., None]
    return save_png(name, col)


FONT = {  # 5x7
    "A": "01110 10001 10001 11111 10001 10001 10001", "B": "11110 10001 11110 10001 10001 10001 11110",
    "C": "01111 10000 10000 10000 10000 10000 01111", "D": "11110 10001 10001 10001 10001 10001 11110",
    "E": "11111 10000 11110 10000 10000 10000 11111", "F": "11111 10000 11110 10000 10000 10000 10000",
    "G": "01111 10000 10000 10011 10001 10001 01111", "H": "10001 10001 11111 10001 10001 10001 10001",
    "I": "11111 00100 00100 00100 00100 00100 11111", "K": "10001 10010 11100 10010 10001 10001 10001",
    "L": "10000 10000 10000 10000 10000 10000 11111", "M": "10001 11011 10101 10001 10001 10001 10001",
    "N": "10001 11001 10101 10011 10001 10001 10001", "O": "01110 10001 10001 10001 10001 10001 01110",
    "P": "11110 10001 10001 11110 10000 10000 10000", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 01110 00001 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    "U": "10001 10001 10001 10001 10001 10001 01110", "V": "10001 10001 10001 10001 10001 01010 00100",
    "W": "10001 10001 10001 10101 10101 11011 10001", "X": "10001 01010 00100 00100 00100 01010 10001",
    "Y": "10001 01010 00100 00100 00100 00100 00100", "0": "01110 10011 10101 11001 10001 10001 01110",
    "1": "00100 01100 00100 00100 00100 00100 01110", "2": "01110 10001 00001 00110 01000 10000 11111",
    "3": "11110 00001 01110 00001 00001 00001 11110", "7": "11111 00001 00010 00100 01000 01000 01000",
    "-": "00000 00000 00000 11111 00000 00000 00000", "/": "00001 00010 00010 00100 01000 01000 10000",
    ":": "00000 01100 01100 00000 01100 01100 00000", " ": "00000 00000 00000 00000 00000 00000 00000",
}


def blit_text(img, text, x, y, scale, color):
    """Pixel text, (x, y) = top-left in image rows from the top (images are stored bottom-up in Blender)."""
    H = img.shape[0]
    for ch in text:
        g = FONT.get(ch.upper(), FONT[" "]).split()
        for r, row in enumerate(g):
            for c, bit in enumerate(row):
                if bit == "1":
                    y0 = H - (y + (r + 1) * scale)
                    img[max(0, y0):y0 + scale, x + c * scale:x + (c + 1) * scale] = color
        x += 6 * scale


ROOT = None
OBJECTS = []
INTERACT = {}


def new_obj(name, bm, mats, origin=(0, 0, 0), parent=None, uv="box", uv_scale=None, smooth_angle=35, note=None, role="static"):
    """bm is in WORLD coordinates; the object origin is placed at `origin` (world) and vertices re-expressed."""
    o = Vector(origin)
    for v in bm.verts:
        v.co -= o
    me = bpy.data.meshes.new(name)
    if uv == "box":
        box_uv(bm, mats, o, uv_scale)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(MATS[m])
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    ob = bpy.data.objects.new(name, me)
    SCENE.collection.objects.link(ob)
    ob.location = o
    par = parent or ROOT
    if par is not None:
        ob.parent = par
        ob.matrix_parent_inverse = world(par).inverted()
    ob["bv2_role"] = role
    if note:
        ob["bv2_note"] = note
    OBJECTS.append(ob)
    return ob


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    SCENE.collection.objects.link(e)
    e.location = Vector(loc)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = world(parent).inverted()
    OBJECTS.append(e)
    return e


def world(ob):
    """World matrix from the authored locations (objects here are never rotated or scaled), so it is valid before
    the depsgraph has evaluated freshly created objects."""
    # Every object's location is authored in world space and its parent inverse cancels the parent, so the world
    # matrix is just the translation.
    return Matrix.Translation(ob.location)


def box_uv(bm, mats, origin, scale):
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        s = scale if scale else MAT_SPEC[mats[min(f.material_index, len(mats) - 1)]][4]
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co + origin
            if ax == 2:
                u, v = p.x, p.y
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.y, p.z
            l[uvl].uv = (u * s, v * s)


def bm_new():
    return bmesh.new()


def add_box(bm, lo, hi, mat=0, bevel=0.0, segs=2):
    lo, hi = Vector(lo), Vector(hi)
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    for v in verts:
        v.co = Vector((lo.x if v.co.x < 0 else hi.x, lo.y if v.co.y < 0 else hi.y, lo.z if v.co.z < 0 else hi.z))
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=segs, affect="EDGES", profile=.5, material=mat)
        verts = [v for v in verts if v.is_valid]
    return verts


def add_cyl(bm, center, r, h, axis="Z", segs=16, mat=0, r2=None, bevel=0.0):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h)
    verts = res["verts"]
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(math.pi / 2, 3, "X")}[axis]
    for v in verts:
        v.co = rot @ v.co + Vector(center)
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat
    if bevel > 0:
        edges = [e for e in {e for v in verts for e in v.link_edges} if len(e.link_faces) == 2 and e.calc_face_angle(0) > .6]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect="EDGES", profile=.5, material=mat)
        verts = [v for v in verts if v.is_valid]
    return verts


def add_lathe(bm, profile, center, axis="Z", segs=24, mat=0):
    """profile: list of (radius, height) from bottom to top; revolved around `axis` through `center`."""
    rings = []
    for r, h in profile:
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), h)))
        rings.append(ring)
    faces = []
    for k in range(len(rings) - 1):
        for i in range(segs):
            a, b = rings[k][i], rings[k][(i + 1) % segs]
            c, d = rings[k + 1][(i + 1) % segs], rings[k + 1][i]
            if a.co == d.co and b.co == c.co:
                continue
            try:
                faces.append(bm.faces.new((a, b, c, d)))
            except ValueError:
                pass
    if profile[0][0] > 1e-5:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if profile[-1][0] > 1e-5:
        faces.append(bm.faces.new(rings[-1]))
    bmesh.ops.remove_doubles(bm, verts=[v for r in rings for v in r], dist=1e-6)
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(-math.pi / 2, 3, "X"),
           "-Y": Matrix.Rotation(math.pi / 2, 3, "X")}[axis]
    vs = [v for v in bm.verts if v.is_valid and v in set(x for r in rings for x in r if x.is_valid)]
    for v in vs:
        v.co = rot @ v.co + Vector(center)
    for f in faces:
        if f.is_valid:
            f.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in faces if f.is_valid])
    return vs


def add_sweep(bm, points, r, sides=8, mat=0):
    """Tube along a polyline with parallel-transported frames (cables, wire, lamp springs)."""
    pts = [Vector(p) for p in points]
    rings = []
    prev_n = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev_n is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
            n = t.cross(ref).normalized()
        else:
            n = (prev_n - t * prev_n.dot(t)).normalized()
        b = t.cross(n)
        prev_n = n
        rings.append([bm.verts.new(p + (n * math.cos(2 * math.pi * k / sides) + b * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    for k in range(len(rings) - 1):
        for i in range(sides):
            f = bm.faces.new((rings[k][i], rings[k][(i + 1) % sides], rings[k + 1][(i + 1) % sides], rings[k + 1][i]))
            f.material_index = mat
    for ring in (rings[0], rings[-1]):
        try:
            f = bm.faces.new(ring)
            f.material_index = mat
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=list({f for r in rings for v in r for f in v.link_faces}))


def bezier(p0, p1, p2, p3, n=16):
    out = []
    for i in range(n + 1):
        t = i / n
        a = [(1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3]
        out.append(tuple(sum(a[k] * [p0, p1, p2, p3][k][j] for k in range(4)) for j in range(3)))
    return out


def rotate_verts(verts, angle, axis, pivot):
    m = Matrix.Rotation(angle, 3, axis)
    for v in verts:
        v.co = m @ (v.co - Vector(pivot)) + Vector(pivot)




def init(scene, cc0, tex, mat_spec):
    global SCENE, CC0, TEX, MAT_SPEC
    SCENE, CC0, TEX, MAT_SPEC = scene, cc0, tex, mat_spec
    os.makedirs(tex, exist_ok=True)


def set_root(root):
    global ROOT
    ROOT = root


# ---------------------------------------------------------------- zone-pass helpers (bench wall, counter, dock)

def xform(bm, build, m, origin):
    """Run build() (which adds geometry to bm in local coordinates) and move just the new vertices: p = m @ p + origin.
    Works with bevelled boxes (every new vertex moves); a mirroring m gets its faces turned back outward."""
    before = set(bm.verts)
    build()
    o = Vector(origin)
    new = [v for v in bm.verts if v not in before]
    for v in new:
        v.co = m @ v.co + o
    if m.determinant() < 0:
        moved = set(new)
        bmesh.ops.reverse_faces(bm, faces=[f for f in bm.faces if all(v in moved for v in f.verts)])


def framed(bm, build, origin, ex, ey):
    """xform() into a frame lying on the floor: local x along ex, y along ey (both horizontal), z up."""
    ex, ey = Vector(ex).normalized(), Vector(ey).normalized()
    xform(bm, build, Matrix(((ex.x, ey.x, 0), (ex.y, ey.y, 0), (ex.z, ey.z, 1))), origin)


def print_quad(bm, centre, right, up, w, h, rect, mat=0):
    """A flat quad (normal = right x up) whose UVs cover `rect` = (u0, v0, u1, v1) of a print sheet."""
    c, r, u = Vector(centre), Vector(right).normalized() * (w / 2), Vector(up).normalized() * (h / 2)
    vs = [bm.verts.new(p) for p in (c - r - u, c + r - u, c + r + u, c - r + u)]
    f = bm.faces.new(vs)
    f.material_index = mat
    uvl = bm.loops.layers.uv.verify()
    u0, v0, u1, v1 = rect
    for l, uv in zip(f.loops, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))):
        l[uvl].uv = uv
    return f


def planar_uv(bm, axis_u, axis_v, lo_u, hi_u, lo_v, hi_v):
    """Planar UVs for every face: u along axis_u mapped lo_u..hi_u -> 0..1 (same for v); edges just stretch."""
    uvl = bm.loops.layers.uv.verify()
    au, av = Vector(axis_u), Vector(axis_v)
    for f in bm.faces:
        for l in f.loops:
            p = l.vert.co
            l[uvl].uv = ((p.dot(au) - lo_u) / (hi_u - lo_u), (p.dot(av) - lo_v) / (hi_v - lo_v))


def text_mask(text, font_path, w, h, pad=.08, align="CENTER", wobble=0.0, seed=1, ss=3):
    """Rasterise `text` (any script, from a TrueType font; '\n' for lines) into an h x w 0..1 mask, rows bottom-up
    like Blender images. The glyph outlines come from a Blender text curve, filled and triangulated, then scan-filled
    with `ss` x `ss` supersampling. `wobble` jitters vertices (fraction of the text height) for hand lettering."""
    curve = bpy.data.curves.new("_mask_txt", "FONT")
    curve.font = bpy.data.fonts.load(font_path, check_existing=True)
    curve.body = text
    curve.align_x = {"CENTER": "CENTER", "LEFT": "LEFT", "RIGHT": "RIGHT"}[align]
    curve.align_y = "CENTER"
    curve.fill_mode = "BOTH"
    curve.resolution_u = 3
    ob = bpy.data.objects.new("_mask_txt", curve)
    SCENE.collection.objects.link(ob)
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    me.calc_loop_triangles()
    vs = np.array([v.co[:2] for v in me.vertices], dtype=np.float64)
    tris = np.array([t.vertices[:] for t in me.loop_triangles], dtype=np.int64)
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(curve)
    bpy.data.meshes.remove(me)
    W, H = w * ss, h * ss
    mask = np.zeros((H, W), dtype=np.float32)
    if len(tris) == 0:
        return mask[::ss, ::ss]
    lo, hi = vs.min(0), vs.max(0)
    if wobble > 0:
        rng = np.random.default_rng(seed)
        vs = vs + rng.normal(0, wobble * (hi[1] - lo[1]), vs.shape)
    s = min(W * (1 - 2 * pad) / max(hi[0] - lo[0], 1e-6), H * (1 - 2 * pad) / max(hi[1] - lo[1], 1e-6))
    ox = {"CENTER": W / 2 - (lo[0] + hi[0]) / 2 * s, "LEFT": W * pad - lo[0] * s, "RIGHT": W * (1 - pad) - hi[0] * s}[align]
    oy = H / 2 - (lo[1] + hi[1]) / 2 * s
    px = vs * s + np.array([ox, oy])
    for a, b, c in tris:
        p0, p1, p2 = px[a], px[b], px[c]
        x0, x1 = int(max(0, np.floor(min(p0[0], p1[0], p2[0])))), int(min(W - 1, np.ceil(max(p0[0], p1[0], p2[0]))))
        y0, y1 = int(max(0, np.floor(min(p0[1], p1[1], p2[1])))), int(min(H - 1, np.ceil(max(p0[1], p1[1], p2[1]))))
        if x1 < x0 or y1 < y0:
            continue
        yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1] + .5
        d = (p1[0] - p0[0]) * (p2[1] - p0[1]) - (p1[1] - p0[1]) * (p2[0] - p0[0])
        if abs(d) < 1e-12:
            continue
        w0 = ((p1[0] - xx) * (p2[1] - yy) - (p1[1] - yy) * (p2[0] - xx)) / d
        w1 = ((p2[0] - xx) * (p0[1] - yy) - (p2[1] - yy) * (p0[0] - xx)) / d
        inside = (w0 >= 0) & (w1 >= 0) & (w0 + w1 <= 1)
        mask[y0:y1 + 1, x0:x1 + 1][inside] = 1
    return mask.reshape(h, ss, w, ss).mean(axis=(1, 3))


def paint(img, mask, color, alpha=1.0, x=0, y=0):
    """Composite a colour through `mask` into img at column x, row y (rows bottom-up), clipped to the image."""
    h, w = mask.shape
    H, W = img.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    m = mask[y0 - y:y1 - y, x0 - x:x1 - x, None] * alpha
    img[y0:y1, x0:x1] = img[y0:y1, x0:x1] * (1 - m) + np.asarray(color)[None, None, :] * m
