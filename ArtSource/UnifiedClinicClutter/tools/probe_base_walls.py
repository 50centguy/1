# Read-only probe: opens base UnifiedClinic.blend in memory, lists mesh parts near the 4 diagonal walls. Never saves.
import bpy, math, sys
from mathutils import Vector
A = 3.40
def bu(v): return Vector((-v[0], v[2], -v[1]))
print("PROBE cams/lights:", [o.name for o in bpy.data.objects if o.type in ("CAMERA", "LIGHT")])
print("PROBE scene cam:", bpy.context.scene.camera.name if bpy.context.scene.camera else None, bpy.context.scene.render.engine)
for k, nm in ((1, "NE"), (3, "NW"), (5, "SW"), (7, "SE"), (0, "E"), (4, "W"), (6, "S"), (2, "N")):
    a = math.radians(45 * k); n = Vector((math.cos(a), 0, math.sin(a))); t = Vector((-math.sin(a), 0, math.cos(a)))
    print("PROBE wall", nm)
    for o in bpy.data.objects:
        if o.type != "MESH" or not o.visible_get(): continue
        pts = [bu(o.matrix_world @ v.co) for v in o.data.vertices]
        near = [p for p in pts if A - p.dot(n) < 0.6 and abs(p.dot(t)) < 1.5]
        if not near or o.name.startswith(("Shell_Wall", "Shell_Floor", "Shell_Ceil")): continue
        d = [A - p.dot(n) for p in near]; s = [p.dot(t) for p in near]; y = [p.y for p in near]
        print("PROBE   %-34s n=%4d depth[%.2f,%.2f] s[%.2f,%.2f] y[%.2f,%.2f]" % (o.name, len(near), min(d), max(d), min(s), max(s), min(y), max(y)))
