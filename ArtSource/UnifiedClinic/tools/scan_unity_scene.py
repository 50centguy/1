"""Read-only scan of a Unity .unity YAML: prints world-ish transform tree.
Usage: blender -b --factory-startup --python scan_unity_scene.py -- <scene.unity> <out.txt>"""
import sys, re
args = sys.argv[sys.argv.index("--")+1:]
src, out = args[0], args[1]
text = open(src, encoding="utf-8").read()
docs = re.split(r"^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$", text, flags=re.M)
objs = {}
for i in range(1, len(docs), 3):
    cls, fid, body = docs[i], docs[i+1], docs[i+2]
    objs[fid] = (cls, body)
def field(body, name):
    m = re.search(r"^\s*" + name + r":\s*(.*)$", body, flags=re.M)
    return m.group(1).strip() if m else None
def vec(s):
    if not s: return None
    d = dict(re.findall(r"(\w+):\s*(-?[\d.eE+-]+)", s))
    return tuple(float(d[k]) for k in ("x","y","z","w") if k in d)
names = {fid: field(b, "m_Name") for fid,(c,b) in objs.items() if c == "1"}
trs = {}
for fid,(c,b) in objs.items():
    if c in ("4","224"):
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)", b)
        fa = re.search(r"m_Father: \{fileID: (-?\d+)", b)
        trs[fid] = dict(go=go.group(1) if go else None, father=fa.group(1) if fa else "0",
            p=vec(field(b,"m_LocalPosition")), r=vec(field(b,"m_LocalRotation")), s=vec(field(b,"m_LocalScale")))
# prefab instances
lines = []
for fid,(c,b) in objs.items():
    if c == "1001":
        src_guid = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: (\w+)", b)
        parent = re.search(r"m_TransformParent: \{fileID: (-?\d+)", b)
        mods = re.findall(r"propertyPath: (\S+)\n\s*value: (.*)", b)
        lines.append(f"PREFAB {fid} guid={src_guid.group(1) if src_guid else '?'} parent={parent.group(1) if parent else '?'}")
        for k,v in mods:
            if k.startswith(("m_LocalPosition","m_LocalRotation","m_LocalScale","m_Name","m_LocalEulerAnglesHint")):
                lines.append(f"    {k} = {v}")
children = {}
for fid,t in trs.items(): children.setdefault(t["father"], []).append(fid)
def walk(fid, depth):
    t = trs[fid]
    n = names.get(t["go"], "?")
    lines.append("  "*depth + f"{n}  p={t['p']} r={t['r']} s={t['s']}")
    for ch in children.get(fid, []): walk(ch, depth+1)
for root in children.get("0", []): walk(root, 0)
open(out, "w", encoding="utf-8").write("\n".join(lines))
print("WROTE", out, len(lines))
