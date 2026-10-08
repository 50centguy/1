# Read-only: exact protrusion of wall panels / trim on the diagonal walls. Never saves.
import bpy, math
from mathutils import Vector
A = 3.40
def bu(v): return Vector((-v[0], v[2], -v[1]))
for nm, k in (("Wall_SE", 7), ("Wall_SW", 5), ("Wall_NW", 3), ("Wall_NE", 1), ("Wall_Trim", 7)):
    o = bpy.data.objects[nm]; a = math.radians(45 * k); n = Vector((math.cos(a), 0, math.sin(a)))
    ds = sorted({round(A - bu(o.matrix_world @ v.co).dot(n), 5) for v in o.data.vertices})
    print("PD", nm, [d for d in ds if -0.01 < d < 0.2])
