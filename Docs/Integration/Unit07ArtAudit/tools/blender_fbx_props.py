# 只读：导入 RobotV4 FBX，导出每个对象的父对象、类型、自定义属性（removable / remove_dir / look / texset 等）与骨骼权重组名。
# 用法：blender -b --factory-startup -P blender_fbx_props.py -- <fbx> <out.json>   （不保存任何 .blend）
import bpy, sys, json
argv = sys.argv[sys.argv.index("--") + 1:]
fbx, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
data = {}
for o in bpy.data.objects:
    props = {k: (v if isinstance(v, (int, float, str, bool)) else str(v)) for k, v in o.items() if not k.startswith("_")}
    d = {"type": o.type, "parent": o.parent.name if o.parent else None, "parent_type": o.parent_type,
         "parent_bone": o.parent_bone or None, "props": props}
    if o.type == "MESH":
        d["vertex_groups"] = [g.name for g in o.vertex_groups]
        d["modifiers"] = [m.type for m in o.modifiers]
    if o.type == "ARMATURE":
        d["bones"] = {b.name: {"parent": b.parent.name if b.parent else None} for b in o.data.bones}
    data[o.name] = d
json.dump(data, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("WROTE", out, len(data))
