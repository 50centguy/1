# Renders the exported Omamori V2 FBX with its baked textures from the same ortho views as the original reference.
import bpy, os, sys
from mathutils import Vector
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(out, 'Omamori_V2.fbx'))
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    label = o.name.replace('OM_ext_', '').replace('OM_', '')
    mat = o.data.materials[0]; mat.use_nodes = True
    t = mat.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(out, 'OmamoriV2_%s_basecolor.png' % label))
    mat.node_tree.links.new(t.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    print('OBJ', o.name, [round(v, 4) for v in o.matrix_world.translation], [round(v, 3) for v in o.rotation_euler], [round(v, 4) for v in o.scale], 'dims', [round(v, 4) for v in o.dimensions])
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'; scene.display.shading.show_cavity = True
scene.render.resolution_x = 900; scene.render.resolution_y = 1100
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = .1062 * 1.15; cam.data.clip_start = .001
center = Vector((.0099, -.0018, .0531))
for name, d, up in (('front', (0, -1, 0), 'Z'), ('back', (0, 1, 0), 'Z'), ('left', (-1, 0, 0), 'Z'), ('top', (0, 0, 1), 'Y')):
    d = Vector(d); cam.location = center + d * .5
    cam.rotation_euler = (-d).to_track_quat('-Z', up).to_euler()
    scene.render.filepath = os.path.join(out, 'v2_%s.png' % name); bpy.ops.render.render(write_still=True)
cam.data.type = 'PERSP'; cam.data.lens = 85
cam.location = Vector((.17, -.30, .16)); cam.rotation_euler = (Vector((0, 0, .046)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = os.path.join(out, 'v2_persp.png'); bpy.ops.render.render(write_still=True)
