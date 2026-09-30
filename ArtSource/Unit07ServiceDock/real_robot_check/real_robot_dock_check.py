"""
隔离复测：用真实 RobotV4 网格替换占位体，对位 Dock_RobotAnchor，复测维修座空间关系。
- 打开 Unit07ServiceDock.blend（只在内存中），从 robot-final.blend 追加（append）全部对象与动作；不保存任何 .blend。
- 输出：本目录下 real_robot_report.json 与渲染图。
"""
import bpy, json, math, os, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT = os.path.dirname(os.path.abspath(__file__))
V4 = r"C:\Users\Administrator\Documents\Codex\2026-09-29\border-repair-station-art\outputs\claude-tripo-rig-handoff\output\robot-final.blend"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NO_RENDER = "--norender" in args

# ---------------------------------------------------------------- 追加真实机器人
before = set(bpy.data.objects)
with bpy.data.libraries.load(V4, link=False) as (src, dst):
    dst.objects = list(src.objects)
    dst.actions = list(src.actions)
coll = bpy.data.collections.new("_RealRobotV4")
bpy.context.scene.collection.children.link(coll)
robot_objs = [o for o in bpy.data.objects if o not in before]
for o in robot_objs:
    coll.objects.link(o)
rig = next(o for o in robot_objs if o.type == "ARMATURE")
anchor = bpy.data.objects["Dock_RobotAnchor"]
bpy.context.view_layer.update()
rig.location = anchor.matrix_world.translation.copy()        # V4 Root 骨骼在装配原点 = 模型最低点
for n in ("_Placeholder_UNIT07", "_Placeholder_UNIT07_StowedArms"):
    if n in bpy.data.collections:
        bpy.data.collections[n].hide_render = True
        bpy.data.collections[n].hide_viewport = True
bpy.context.view_layer.update()
robot_meshes = [o for o in robot_objs if o.type == "MESH"]
dock_root = bpy.data.objects["UNIT07_ServiceDock"]
dock_meshes = [o for o in dock_root.children_recursive if o.type == "MESH"]
clamps = {s: bpy.data.objects[f"Dock_Clamp_{s}"] for s in ("L", "R")}
print("[setup] robot meshes", len(robot_meshes), "rig at", tuple(round(v, 4) for v in rig.location), "dock meshes", len(dock_meshes))


def set_pose(pose):
    ad = rig.animation_data
    if pose == "working":
        ad.action = bpy.data.actions["Idle_Hover"]
        bpy.context.scene.frame_set(0)
    else:   # 两臂都收起：Deploy_L 第 0 帧的左臂 + Deploy_R 第 0 帧的右臂
        ad.action = bpy.data.actions["Arm_Deploy_L"]
        bpy.context.scene.frame_set(0)
        left = {b.name: b.matrix_basis.copy() for b in rig.pose.bones if b.name.startswith("Arm_L")}
        ad.action = bpy.data.actions["Arm_Deploy_R"]
        bpy.context.scene.frame_set(0)
        right = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
        ad.action = None
        for b in rig.pose.bones:
            b.matrix_basis = left.get(b.name, right[b.name])
    bpy.context.view_layer.update()


def world_geo(objs, offset=Vector()):
    dg = bpy.context.evaluated_depsgraph_get()
    verts, polys, owner = [], [], []
    for o in objs:
        eo = o.evaluated_get(dg)
        me = eo.to_mesh()
        mw = eo.matrix_world
        base = len(verts)
        verts.extend(mw @ v.co + offset for v in me.vertices)
        for p in me.polygons:
            polys.append([base + i for i in p.vertices])
            owner.append(o.name)
        eo.to_mesh_clear()
    return verts, polys, owner


def overlaps(a, b):
    """返回 [(a 对象, b 对象)] 去重列表"""
    va, pa, oa = a
    vb, pb, ob = b
    if not pa or not pb:
        return []
    pairs = BVHTree.FromPolygons(va, pa).overlap(BVHTree.FromPolygons(vb, pb))
    return sorted({(oa[i], ob[j]) for i, j in pairs})


def min_gap(a, b):
    """顶点到对方表面的最小距离（双向），毫米"""
    va, pa, _ = a
    vb, pb, _ = b
    tb, ta = BVHTree.FromPolygons(vb, pb), BVHTree.FromPolygons(va, pa)
    d = min((tb.find_nearest(v)[3] for v in va), default=1e9)
    d = min(d, min((ta.find_nearest(v)[3] for v in vb), default=1e9))
    return round(d * 1000, 1)


def near(objs, lo, hi):
    """只取包围盒与区域相交的对象，减少计算量"""
    out = []
    for o in objs:
        ws = [o.matrix_world @ Vector(c) for c in o.bound_box]
        olo = [min(w[i] for w in ws) for i in range(3)]
        ohi = [max(w[i] for w in ws) for i in range(3)]
        if all(olo[i] <= hi[i] and ohi[i] >= lo[i] for i in range(3)):
            out.append(o)
    return out


rep = {"robot_source": V4, "anchor_world": list(anchor.matrix_world.translation), "checks": []}


def add(name, ok, **kw):
    rep["checks"].append(dict(name=name, passed=bool(ok), **kw))
    print("[check]", "PASS" if ok else "FAIL", name, kw)


def set_clamps(open_):
    for c in clamps.values():
        c.rotation_euler = (0, math.radians(c["open_deg"] if open_ else 0.0), 0)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- A. 静态：真实机器人 vs 维修座
hp = [bpy.data.objects[n] for n in ("Chassis_ArmHardpoint_L", "Chassis_ArmHardpoint_R")]
pads = [bpy.data.objects[n] for n in ("Dock_ContactPad_L", "Dock_ContactPad_R")]
for pose in ("working", "stowed"):
    set_pose(pose)
    set_clamps(False)
    dock_g = world_geo(dock_meshes)
    rob_g = world_geo(robot_meshes)
    ov = overlaps(dock_g, rob_g)
    contact = [p for p in ov if p[0].startswith("Dock_ContactPad") and p[1].startswith("Chassis_ArmHardpoint")]
    other = [p for p in ov if p not in contact]
    # 接触面：垫顶 vs 硬点板底
    hp_bottom = min(v.z for v in world_geo(hp)[0])
    pad_top = max(v.z for v in world_geo(pads)[0])
    add(f"A_static_{pose}_no_interference", not other, interferences=[f"{a} x {b}" for a, b in other],
        pad_contact_pairs=[f"{a} x {b}" for a, b in contact], pad_top_z=round(pad_top, 5), hardpoint_bottom_z=round(hp_bottom, 5),
        contact_offset_mm=round((pad_top - hp_bottom) * 1000, 2))
    if pose == "working":
        # 各关键部件与维修座的最小间隙
        gaps = {}
        for rn, dn in (("Chassis_MountRail_L", "Dock_RailGuide_L"), ("Chassis_MountRail_L", "Dock_Clamp_L"), ("Chassis_MountRail_R", "Dock_RailGuide_R"),
                       ("Chassis_MountRail_R", "Dock_Clamp_R"), ("Body_RearBolt_2", "Dock_Clamp_L"), ("Body_RearBolt_1", "Dock_Clamp_R"),
                       ("Body_RearBolt_2", "Dock_Clamp_L_JawPad"), ("Body_RearBolt_1", "Dock_Clamp_R_JawPad")):
            gaps[f"{rn} ~ {dn}"] = min_gap(world_geo([bpy.data.objects[rn]]), world_geo([bpy.data.objects[dn]]))
        rep["key_gaps_mm_working_clamps_closed"] = gaps
        print("[gaps]", gaps)

# ---------------------------------------------------------------- B. 电源托盘：下放 14 cm，再从两臂之间向前取出 45 cm
TRAY = [o for o in robot_meshes if o.name.startswith("PowerTray") and not o.name.startswith("PowerTray_Screw_") and o.name != "PowerTray_Connector"]
EXCLUDE = {o.name for o in TRAY} | {o.name for o in robot_meshes if o.name.startswith("PowerTray_Screw_")} | {"PowerTray_Connector", "Harness_Power_PlugB"}
rep["power_tray_group"] = sorted(o.name for o in TRAY)
for pose in ("working", "stowed"):
    set_pose(pose)
    set_clamps(False)
    rest_robot = [o for o in robot_meshes if o.name not in EXCLUDE]
    region_lo, region_hi = Vector((-0.30, -0.80, 0.55)), Vector((0.30, 0.30, 1.05))
    dock_g = world_geo(near(dock_meshes, region_lo, region_hi))
    rob_g = world_geo(near(rest_robot, region_lo, region_hi))
    tray0 = world_geo(TRAY)
    path = [Vector((0, 0, -0.002 - 0.005 * k)) for k in range(28)] + [Vector((0, -0.01 * k, -0.137)) for k in range(1, 46)]
    hits_dock, hits_robot, gap_dock, gap_robot = [], [], 1e9, 1e9
    tb_dock = BVHTree.FromPolygons(dock_g[0], dock_g[1])
    tb_rob = BVHTree.FromPolygons(rob_g[0], rob_g[1])
    for off in path:
        v = [p + off for p in tray0[0]]
        tt = BVHTree.FromPolygons(v, tray0[1])
        if tt.overlap(tb_dock):
            hits_dock.append(tuple(round(x, 3) for x in off))
        if tt.overlap(tb_rob):
            hits_robot.append(tuple(round(x, 3) for x in off))
        gap_dock = min(gap_dock, min(tb_dock.find_nearest(p)[3] for p in v))
        gap_robot = min(gap_robot, min(tb_rob.find_nearest(p)[3] for p in v[::3]))
    add(f"B_power_tray_drop_and_front_exit_{pose}_vs_dock", not hits_dock and gap_dock * 1000 >= 15.0, min_gap_mm=round(gap_dock * 1000, 1), required_min_gap_mm=15.0, hit_offsets=hits_dock[:6])
    add(f"B_power_tray_drop_and_front_exit_{pose}_vs_robot", not hits_robot, min_gap_mm=round(gap_robot * 1000, 1), hit_offsets=hits_robot[:6],
        note="REPORT ONLY (V4 model fit, not a dock criterion): robot = all robot meshes except the tray group, its 4 fixing screws, its connector and the unplugged power plug; the ~2 mm minimum is the tray-to-lower-module fit inside the V4 model")
    # 托盘相对维修座纵梁的侧向余量（原占位检查报告 9 mm）
    tray_x = max(abs(p.x) for p in tray0[0])
    beam_x = min(min(abs(p.x) for p in world_geo([bpy.data.objects[f"Dock_Yoke_Beam_{s}"]])[0]) for s in ("L", "R"))
    rep[f"power_tray_side_margin_{pose}"] = {"tray_half_width_m": round(tray_x, 4), "yoke_beam_inner_x_m": round(beam_x, 4),
                                             "lateral_margin_mm": round((beam_x - tray_x) * 1000, 1)}

# ---------------------------------------------------------------- C. 背部夹具开合扫掠 vs 真实机器人（含机背螺栓）
set_pose("working")
sweep = {}
for s, c in clamps.items():
    parts = [c] + list(c.children)
    region_lo = Vector((-0.40, 0.05, 0.80)); region_hi = Vector((0.40, 0.35, 1.10))
    rob_g = world_geo(near(robot_meshes, region_lo, region_hi))
    tb = BVHTree.FromPolygons(rob_g[0], rob_g[1])
    hits, gmin, gbolt, gbolt_ang = [], 1e9, 1e9, 0.0
    bolt = world_geo([bpy.data.objects["Body_RearBolt_2" if s == "L" else "Body_RearBolt_1"]])
    tbolt = BVHTree.FromPolygons(bolt[0], bolt[1])
    for k in range(25):
        ang = c["open_deg"] * k / 24
        c.rotation_euler = (0, math.radians(ang), 0)
        bpy.context.view_layer.update()
        g = world_geo(parts)
        ov = BVHTree.FromPolygons(g[0], g[1]).overlap(tb)
        if ov:
            hits.append(sorted({rob_g[2][j] for _, j in ov})[:4] + [f"@{ang:.1f}deg"])
        gmin = min(gmin, min(tb.find_nearest(p)[3] for p in g[0]))
        tpart = BVHTree.FromPolygons(g[0], g[1])
        gb = min(min(tbolt.find_nearest(p)[3] for p in g[0]), min(tpart.find_nearest(p)[3] for p in bolt[0]))
        if gb < gbolt:
            gbolt, gbolt_ang = gb, ang
    c.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    add(f"C_clamp_{s}_sweep_vs_real_robot", not hits and gbolt * 1000 >= 5.0, hits=hits, min_gap_to_robot_mm=round(gmin * 1000, 1),
        min_gap_to_rear_bolt_mm=round(gbolt * 1000, 1), at_deg=round(gbolt_ang, 2), required_min_gap_to_rear_bolt_mm=5.0)

# ---------------------------------------------------------------- D. 两侧引擎整台向外拔出 40 cm（夹具夹紧 / 张开）
for s, sx in (("L", 1), ("R", -1)):
    eng = [o for o in robot_meshes if (o.parent_type == "BONE" and o.parent_bone.startswith(f"Engine_{s}")) or
           any(o.name.startswith(p) for p in (f"ConnectionKey_{s}", f"EngineAnchor_{s}", f"ExternalCable_{s}_PlugEngine", f"AuxCable_{s}_PlugEngine"))]
    rep[f"engine_{s}_group_count"] = len(eng)
    for state in ("closed", "open"):
        set_pose("working")
        set_clamps(state == "open")
        e0 = world_geo(eng)
        dock_g = world_geo(dock_meshes)
        tb = BVHTree.FromPolygons(dock_g[0], dock_g[1])
        hits, gmin = [], 1e9
        for k in range(41):
            off = Vector((sx * 0.01 * k, 0, 0))
            v = [p + off for p in e0[0]]
            if BVHTree.FromPolygons(v, e0[1]).overlap(tb):
                hits.append(round(0.01 * k, 2))
            gmin = min(gmin, min(tb.find_nearest(p)[3] for p in v[::4]))
        add(f"D_engine_{s}_pull_out_400mm_clamps_{state}", not hits, hit_at_m=hits[:6], min_gap_to_dock_mm=round(gmin * 1000, 1), meshes=len(eng))
set_clamps(False)

# ---------------------------------------------------------------- E. 竖直落座（夹具张开），12 cm → 0
set_pose("working")
set_clamps(True)
dock_g = world_geo(dock_meshes)
tb = BVHTree.FromPolygons(dock_g[0], dock_g[1])
rob0 = world_geo(robot_meshes)
hits = []
for k in range(24, 0, -1):
    off = Vector((0, 0, 0.005 * k))
    v = [p + off for p in rob0[0]]
    ov = BVHTree.FromPolygons(v, rob0[1]).overlap(tb)
    if ov:
        hits.append((round(0.005 * k, 3), sorted({dock_g[2][j] for _, j in ov})[:4]))
add("E_docking_descent_120mm_clamps_open", not hits, hits=hits[:6])
set_clamps(False)


# ---------------------------------------------------------------- F. 夹具夹紧时对安装轨的有效接触（夹面 vs 真实安装轨）
set_pose("working")
set_clamps(False)
for s in ("L", "R"):
    pad = world_geo([bpy.data.objects[f"Dock_Clamp_{s}_JawPad"]])
    rail = world_geo([bpy.data.objects[f"Chassis_MountRail_{s}"]])
    gap = min_gap(pad, rail)
    pz = (min(v.z for v in pad[0]), max(v.z for v in pad[0])); rz = (min(v.z for v in rail[0]), max(v.z for v in rail[0]))
    py = (min(v.y for v in pad[0]), max(v.y for v in pad[0])); ry = (min(v.y for v in rail[0]), max(v.y for v in rail[0]))
    z_span = (min(pz[1], rz[1]) - max(pz[0], rz[0])) * 1000
    y_span = (min(py[1], ry[1]) - max(py[0], ry[0])) * 1000
    add(f"F_clamp_{s}_effective_rail_contact", gap <= 0.5 and z_span >= 40 and y_span >= 5,
        jaw_pad_to_rail_gap_mm=gap, facing_height_mm=round(z_span, 1), facing_depth_mm=round(y_span, 1),
        criteria="closed jaw pad touches the rail face (gap <= 0.5 mm, no interference), facing it over >= 40 mm height and >= 5 mm depth")

# ---------------------------------------------------------------- G. 接触垫完整落在手臂硬点板下面（真实网格）
for s in ("L", "R"):
    pad = world_geo([bpy.data.objects[f"Dock_ContactPad_{s}"]])
    hp_ = world_geo([bpy.data.objects[f"Chassis_ArmHardpoint_{s}"]])
    inside = all(min(v[i] for v in hp_[0]) <= min(v[i] for v in pad[0]) and max(v[i] for v in pad[0]) <= max(v[i] for v in hp_[0]) for i in (0, 1))
    dz = (max(v.z for v in pad[0]) - min(v.z for v in hp_[0])) * 1000
    ax = (max(v.x for v in pad[0]) - min(v.x for v in pad[0])) * 1000; ay = (max(v.y for v in pad[0]) - min(v.y for v in pad[0])) * 1000
    add(f"G_contact_pad_{s}_on_hardpoint", inside and abs(dz) <= 0.2, footprint_inside_hardpoint=inside, contact_offset_mm=round(dz, 2),
        pad_footprint_mm=[round(ax, 1), round(ay, 1)])

rep["all_passed"] = all(c["passed"] for c in rep["checks"])
with open(os.path.join(OUT, "real_robot_report.json"), "w", encoding="utf-8") as f:
    json.dump(rep, f, ensure_ascii=False, indent=2)
print("[done] all_passed", rep["all_passed"])

# ---------------------------------------------------------------- 渲染（真实机器人，工作姿态，夹具夹紧，开关 OFF）
if not NO_RENDER:
    set_pose("working")
    scene = bpy.context.scene
    cam = scene.camera                     # 维修座 .blend 已带布光与相机（StudioCam），直接复用
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type == "OPTIX"
        scene.cycles.device = "GPU"
    except Exception as e:
        print("[render] GPU unavailable:", e)
    for name, loc, tgt, lens in (("RR01_front_real_robot.png", (0, -3.0, 1.30), (0, 0, 0.95), 38),
                                 ("RR02_threequarter_real_robot.png", (1.95, -2.35, 1.75), (0, -0.02, 0.92), 36),
                                 ("RR03_rear_clamps_real_robot.png", (-0.95, 1.25, 1.30), (0, 0.2, 0.98), 42),
                                 ("RR04_under_power_tray_real_robot.png", (0.55, -1.05, 0.45), (0, 0, 0.88), 40)):
        cam.location = Vector(loc)
        cam.data.lens = lens
        cam.rotation_euler = (Vector(tgt) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, name)
        bpy.ops.render.render(write_still=True)
        print("[render]", name)
