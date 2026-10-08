# Read-only probe: island bounds of shell trim/pipes/plates/notes near the 4 diagonal walls. Never saves.
import bpy, bmesh, math
from mathutils import Vector
A = 3.40
def bu(v): return Vector((-v[0], v[2], -v[1]))
NAMES = ("Shell_Pipes", "Wall_Trim", "Shell_PatchPlates", "Shell_Cables", "South_NoteBoard", "Wall_SE_Notes", "Clutter_South_Cloth", "Clutter_South_DarkSteel", "Storage_LimbShelf_Chart", "Clutter_West_Ivory", "Clutter_West_DarkSteel", "Storage_NWCabinet", "Storage_NECabinet", "Clutter_East_PaintGreen", "Clutter_East_DarkSteel", "Storage_Lockers", "Storage_LimbShelf", "Storage_EastShelves", "Wall_NE", "Wall_SW")
for k, nm in ((1, "NE"), (3, "NW"), (5, "SW"), (7, "SE")):
    a = math.radians(45 * k); n = Vector((math.cos(a), 0, math.sin(a))); t = Vector((-math.sin(a), 0, math.cos(a)))
    print("ISL wall", nm)
    for name in NAMES:
        o = bpy.data.objects.get(name)
        if not o: continue
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        seen = set()
        for v0 in bm.verts:
            if v0.index in seen: continue
            stack, isl = [v0], []
            seen.add(v0.index)
            while stack:
                v = stack.pop(); isl.append(v)
                for e in v.link_edges:
                    w = e.other_vert(v)
                    if w.index not in seen: seen.add(w.index); stack.append(w)
            P = [bu(o.matrix_world @ v.co) for v in isl]
            d = [A - p.dot(n) for p in P]; s = [p.dot(t) for p in P]; y = [p.y for p in P]
            if max(d) < -0.01 or min(d) > 0.65 or min(s) > 1.5 or max(s) < -1.5: continue
            if min(y) > 2.2 and name not in ("Shell_Cables",): pass
            print("ISL   %-24s depth[%.2f,%.2f] s[%.2f,%.2f] y[%.2f,%.2f]" % (name, min(d), max(d), min(s), max(s), min(y), max(y)))
        bm.free()
