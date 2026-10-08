# Read-only probe: import existing FBX exports into an empty scene and print native bounds. Nothing is saved.
import bpy, sys
from mathutils import Vector
paths = sys.argv[sys.argv.index("--")+1:]
for p in paths:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=p, axis_forward="-Z", axis_up="Y")
    lo = Vector((1e9,)*3); hi = Vector((-1e9,)*3)
    roots = [o.name for o in bpy.data.objects if o.parent is None]
    for o in bpy.data.objects:
        if o.type == "MESH":
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    print("PROBE", p.split("/")[-1], "objs", len(bpy.data.objects), "roots", roots[:6])
    print("PROBE   bounds", tuple(round(v,3) for v in lo), tuple(round(v,3) for v in hi))
    for o in bpy.data.objects:
        if o.name.startswith(("Room_", "Clutter_Shelving", "Dock_RobotAnchor", "Furniture", "Bench_Top", "UNIT07")):
            print("PROBE   ", o.name, o.type, tuple(round(v,3) for v in o.matrix_world.translation), [tuple(round(x,3) for x in (o.matrix_world @ Vector(c))) for c in (o.bound_box[0], o.bound_box[6])] if o.type=="MESH" else "")
