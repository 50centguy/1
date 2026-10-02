"""
只读：导入若干 FBX，按对象名输出几何哈希（顶点坐标 1e-6 m 取整、面顶点索引、UV 1e-5 取整），用来判断重新导出的 FBX 几何是否和原来一样
（FBX 文件里有导出时间戳，直接比文件 MD5 没意义）。
用法：blender -b --factory-startup --python fbx_geometry_hash.py -- a.fbx b.fbx ...
"""
import hashlib
import sys

import bpy

files = sys.argv[sys.argv.index("--") + 1:]
for f in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    for o in sorted((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: o.name):
        me = o.data
        h = hashlib.md5()
        for v in me.vertices:
            h.update(("%d,%d,%d;" % tuple(round(c * 1e6) for c in v.co)).encode())
        for p in me.polygons:
            h.update((",".join(str(i) for i in p.vertices) + ";").encode())
        if me.uv_layers:
            for d in me.uv_layers[0].data:
                h.update(("%d,%d;" % (round(d.uv[0] * 1e5), round(d.uv[1] * 1e5))).encode())
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        print(f"GEOHASH {f.split('/')[-1].split(chr(92))[-1]} {o.name} verts={len(me.vertices)} tris={tris} {h.hexdigest()}")
