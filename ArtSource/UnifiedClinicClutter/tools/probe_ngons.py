# Read-only: report self-intersecting / non-planar polygons in every add-on mesh (projected to the polygon's best plane).
import bpy, sys, json
from mathutils import Vector
from mathutils.geometry import intersect_line_line_2d
def check(o):
    bad = []
    for p in o.data.polygons:
        V = [o.data.vertices[i].co for i in p.vertices]
        n = len(V)
        if n < 4: continue
        nrm = Vector((0, 0, 0))
        for i in range(n):   # Newell normal
            a, b = V[i], V[(i + 1) % n]
            nrm += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        nrm.normalize()
        u = (V[1] - V[0]).normalized(); w = nrm.cross(u)
        P = [Vector(((v - V[0]).dot(u), (v - V[0]).dot(w))) for v in V]
        planar = max(abs((v - V[0]).dot(nrm)) for v in V)
        hits = []
        for i in range(n):
            for j in range(i + 2, n):
                if i == 0 and j == n - 1: continue
                x = intersect_line_line_2d(P[i], P[(i + 1) % n], P[j], P[(j + 1) % n])
                if x is not None and min((x - q).length for q in (P[i], P[(i + 1) % n], P[j], P[(j + 1) % n])) > 1e-7:
                    hits.append((i, j))
        if hits or planar > 1e-4:
            bad.append(dict(poly=p.index, verts=n, self_intersections=len(hits), nonplanar_m=round(planar, 6)))
    return bad
res = {o.name: check(o) for o in bpy.data.objects if o.type == "MESH"}
print("NGON", json.dumps({k: v for k, v in res.items() if v}))
print("NGON_COUNTS", json.dumps({o.name: {str(k): sum(1 for p in o.data.polygons if len(p.vertices) == k) for k in sorted({len(p.vertices) for p in o.data.polygons})} for o in bpy.data.objects if o.type == "MESH"}))
