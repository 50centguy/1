# Read-only: per mesh loop-triangle count, zero-area (< 1e-10 m^2) loop triangles, zero-area polygons, total area.
# Usage: blender -b <file.blend> --python probe_degenerate.py -- <out.json>
import bpy, sys, json
out = {}
for o in sorted((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: o.name):
    me = o.data
    me.calc_loop_triangles()
    z = [t for t in me.loop_triangles if t.area < 1e-10]
    zp = [p for p in me.polygons if p.area < 1e-10]
    out[o.name] = dict(loop_triangles=len(me.loop_triangles), zero_area_triangles=len(z),
                       zero_area_polygons=[dict(index=p.index, verts=len(p.vertices), y=round(sum(me.vertices[i].co.z for i in p.vertices) / len(p.vertices), 4)) for p in zp],
                       total_area_m2=round(sum(p.area for p in me.polygons), 8))
path = sys.argv[sys.argv.index("--") + 1]
json.dump(out, open(path, "w"), indent=1)
print("DEG", json.dumps({k: (v["loop_triangles"], v["zero_area_triangles"], len(v["zero_area_polygons"])) for k, v in out.items()}))
