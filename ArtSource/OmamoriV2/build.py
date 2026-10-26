# Omamori V2 exterior: a clean low-poly rebuild of the Tripo shell around the existing RK interior kit.
# Blender space (front = -Y, up = +Z, metres); matches how Blender imports the original Omamori.fbx, so exporting with
# the default FBX axes lands every part where the old one was in Unity.
# Outputs (to argv out dir): Omamori_V2.fbx, OmamoriV2_<Part>_basecolor.png, OmamoriV2_Screen_emission.png, previews.
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1]
REF = sys.argv[sys.argv.index('--') + 2] if len(sys.argv) > sys.argv.index('--') + 2 else ''
bpy.ops.wm.read_factory_settings(use_empty=True)

COLORS = {
    'teal': (47, 124, 128), 'teal_dark': (36, 98, 102), 'cream': (216, 205, 178), 'gunmetal': (62, 67, 74),
    'dark': (30, 34, 38), 'steel': (163, 168, 173), 'red': (156, 20, 38), 'amber': (255, 176, 60),
    'glass': (11, 31, 34),
}
EMISSIVE = {'amber'}
for name, c in COLORS.items():
    m = bpy.data.materials.new(name); m.diffuse_color = (*[v / 255 for v in c], 1)

def link(obj):
    bpy.context.scene.collection.objects.link(obj); return obj

def active(obj):
    for o in bpy.context.scene.objects: o.select_set(False)
    obj.select_set(True); bpy.context.view_layer.objects.active = obj

def colour(obj, name):
    obj.data.materials.clear(); obj.data.materials.append(bpy.data.materials[name])
    for p in obj.data.polygons: p.material_index = 0
    return obj

def box(name, x, y, z, col, round_y=0.0, round_segments=4, bevel=0.0006, bevel_segments=2):
    """Axis box over [x0,x1]x[y0,y1]x[z0,z1]; round_y rounds the edges running front-to-back (the outline corners)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((x[0] + x[1]) / 2 + v.co.x * (x[1] - x[0]), (y[0] + y[1]) / 2 + v.co.y * (y[1] - y[0]), (z[0] + z[1]) / 2 + v.co.z * (z[1] - z[0])))
    if round_y > 0:
        edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).normalized().y) > .9]
        bmesh.ops.bevel(bm, geom=edges, offset=round_y, segments=round_segments, affect='EDGES', profile=.5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    obj = link(bpy.data.objects.new(name, me))
    if bevel > 0:
        mod = obj.modifiers.new('bevel', 'BEVEL'); mod.width = bevel; mod.segments = bevel_segments
        mod.limit_method = 'ANGLE'; mod.angle_limit = math.radians(35); mod.harden_normals = False
        apply_mods(obj)
    return colour(obj, col)

def cylinder_y(name, cx, cz, r, y0, y1, col, segments=24, bevel=0.0004):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r, radius2=r, depth=y1 - y0)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((cx, (y0 + y1) / 2, cz)))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    obj = link(bpy.data.objects.new(name, me))
    if bevel > 0:
        mod = obj.modifiers.new('bevel', 'BEVEL'); mod.width = bevel; mod.segments = 2
        mod.limit_method = 'ANGLE'; mod.angle_limit = math.radians(35); apply_mods(obj)
    return colour(obj, col)

def torus_y(name, cx, cy, cz, major, minor, col, sx=1.0, major_seg=20, minor_seg=8):
    bm = bmesh.new()
    for i in range(major_seg):
        a = 2 * math.pi * i / major_seg
        for j in range(minor_seg):
            b = 2 * math.pi * j / minor_seg
            rr = major + minor * math.cos(b)
            bm.verts.new((cx + rr * math.cos(a) * sx, cy + minor * math.sin(b), cz + rr * math.sin(a)))
    bm.verts.ensure_lookup_table()
    for i in range(major_seg):
        for j in range(minor_seg):
            a = i * minor_seg + j; b = ((i + 1) % major_seg) * minor_seg + j
            c = ((i + 1) % major_seg) * minor_seg + (j + 1) % minor_seg; d = i * minor_seg + (j + 1) % minor_seg
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[c], bm.verts[d]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return colour(link(bpy.data.objects.new(name, me)), col)

def blob(name, centre, radii, col, rot_y=0.0, segments=10, rings=6):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2]))
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rot_y, 3, 'Y'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(centre))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return colour(link(bpy.data.objects.new(name, me)), col)

def apply_mods(obj):
    active(obj)
    for m in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)

def cut(target, cutter):
    mod = target.modifiers.new('cut', 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = cutter
    apply_mods(target); bpy.data.objects.remove(cutter, do_unlink=True)

def join(name, parts):
    active(parts[0])
    for p in parts: p.select_set(True)
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active; obj.name = name; obj.data.name = name
    return obj

# ---------------------------------------------------------------- shell (teal body, rails, caps, lugs, knot)
body = box('body', (-.0300, .0300), (-.0060, .0075), (.0000, .0905), 'teal', round_y=.0055, bevel=.0008)
cut(body, box('cavity', (-.0262, .0262), (-.0056, .0056), (.0125, .0875), 'teal', bevel=0))
cut(body, box('pocket', (-.0280, .0280), (.0050, .0200), (.0175, .0885), 'teal', round_y=.003, bevel=0))
cut(body, box('opening', (-.0255, .0255), (.0000, .0200), (.0185, .0870), 'teal', bevel=0))
shell = [body]
for s in (-1, 1):
    shell.append(box('rail', sorted((s * .0193, s * .0285)), (-.0078, -.0057), (.0170, .0800), 'teal_dark', bevel=.0010, bevel_segments=3))
    shell.append(box('cap', sorted((s * .0200, s * .0308)), (-.0074, .0084), (-.0005, .0160), 'cream', round_y=.004, bevel=.0008))
    for y0, y1 in ((-.0080, -.0072), (.0080, .0088)):
        shell.append(cylinder_y('capscrew', s * .0255, .0060, .0013, y0, y1, 'steel', segments=12, bevel=.0003))
    shell.append(cylinder_y('topscrew', s * .0255, .0855, .0013, -.0068, -.0058, 'steel', segments=12, bevel=.0003))
    shell.append(box('lugbase', sorted((s * .0115, s * .0205)), (-.0035, .0035), (.0895, .0930), 'gunmetal', bevel=.0006))
    shell.append(torus_y('lug', s * .0160, 0, .0935, .0045, .0011, 'steel'))
    # Bow knot loops on the crown (the soft cord itself is simulated in Unity from the lugs).
    shell.append(torus_y('bow', s * .0062, -.0098, .0848, .0040, .0011, 'red', sx=1.3, major_seg=16, minor_seg=6))
    shell.append(box('bowtail', sorted((s * .0010, s * .0034)), (-.0106, -.0090), (.0770, .0835), 'red', bevel=.0005))
shell.append(box('topbar', (-.0085, .0085), (-.0055, .0068), (.0895, .0928), 'cream', bevel=.0006))
shell.append(blob('knot', (0, -.0100, .0845), (.0028, .0020, .0026), 'red'))
shell_obj = join('OM_ext_Shell', shell)

# ---------------------------------------------------------------- face (cream plate, switch, fan, knob)
plate = box('plate', (-.0190, .0190), (-.0084, -.0057), (.0405, .0885), 'cream', round_y=.003, bevel=.0006)
cut(plate, box('recess', (-.0023, .0110), (-.0100, -.0072), (.0510, .0753), 'cream', bevel=0))
cut(plate, box('slot', (-.0146, -.0078), (-.0100, -.0072), (.0460, .0620), 'cream', bevel=0))
face = [plate]
face.append(box('slotfloor', (-.0146, -.0078), (-.0074, -.0070), (.0460, .0620), 'dark', bevel=0))
face.append(box('slider', (-.0140, -.0084), (-.0087, -.0072), (.0525, .0600), 'gunmetal', bevel=.0005))
face.append(cylinder_y('fan', -.0005, .0215, .0215, -.0082, -.0057, 'dark', segments=32, bevel=.0006))
for k in range(8):
    a = math.radians(22.5 + 45 * k)
    face.append(blob('petal', (-.0005 + math.cos(a) * .0146, -.0083, .0215 + math.sin(a) * .0146), (.0058, .0016, .0042), 'gunmetal', rot_y=-a, segments=12, rings=6))
face.append(cylinder_y('knob', -.0008, .0225, .0098, -.0110, -.0080, 'gunmetal', segments=28, bevel=.0008))
face.append(torus_y('grip', -.0008, -.0096, .0225, .0098, .0007, 'dark', major_seg=28, minor_seg=6))
face.append(box('mark', (-.0014, -.0002), (-.0113, -.0108), (.0262, .0306), 'cream', bevel=.0002, bevel_segments=1))
face_obj = join('OM_ext_Face', face)

# ---------------------------------------------------------------- screen (glass + glyph, status LED): emissive, not paintable
glass = box('glass', (-.0021, .0108), (-.0076, -.0072), (.0512, .0751), 'glass', bevel=0)
led = cylinder_y('led', -.0112, .0660, .0011, -.0090, -.0080, 'amber', segments=12, bevel=.0002)
screen_obj = join('OM_ext_Screen', [glass, led])

# ---------------------------------------------------------------- rear cover (flow node OM_RearCover)
cover = box('cover', (-.0275, .0275), (.0052, .0090), (.0180, .0880), 'teal', round_y=.003, bevel=.0006)
for s in (-1, 1):
    for z in (.0215, .0845):
        cut(cover, cylinder_y('sink', s * .0235, z, .0021, .0080, .0100, 'teal', segments=16, bevel=0))
cover_obj = join('OM_RearCover', [cover, box('label', (-.0130, .0130), (.0088, .0093), (.0460, .0600), 'cream', bevel=.0003, bevel_segments=1)])

parts = {'Shell': (shell_obj, 512), 'Face': (face_obj, 512), 'Screen': (screen_obj, 128), 'RearCover': (cover_obj, 256)}

# ---------------------------------------------------------------- shading, UVs, baked colour textures
import json
layout = {}
for label, (obj, size) in parts.items():
    active(obj)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=.02, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    me = obj.data; uv = me.uv_layers.active.data
    polys = []
    for p in me.polygons:
        polys.append({'col': me.materials[p.material_index].name, 'normal_y': p.normal.y, 'area': p.area,
                      'uv': [[uv[i].uv.x, uv[i].uv.y] for i in p.loop_indices],
                      'xz': [[me.vertices[me.loops[i].vertex_index].co.x, me.vertices[me.loops[i].vertex_index].co.z] for i in p.loop_indices]})
    layout[label] = {'size': size, 'polys': polys}
    for p in me.polygons: p.material_index = 0
    me.materials.clear(); me.materials.append(bpy.data.materials.new('M_OmamoriV2_' + label))
    print('PART', obj.name, 'verts', len(me.vertices), 'tris', sum(len(p.vertices) - 2 for p in me.polygons))
json.dump({'colors': COLORS, 'emissive': sorted(EMISSIVE), 'parts': layout}, open(os.path.join(OUT, 'uv_layout.json'), 'w'))

# ---------------------------------------------------------------- export
for o in bpy.context.scene.objects: o.select_set(o.type == 'MESH')
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Omamori_V2.fbx'), use_selection=True, object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)

