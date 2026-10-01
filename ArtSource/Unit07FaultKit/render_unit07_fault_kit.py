"""
对比渲染（由 build_unit07_fault_kit.py 在同一会话里 exec，共用其中的 R / FRAME / 资源对象）：
  装在引擎里（故障）  R01 进气口堵塞（上盖装着）     R02 拆下上盖后的磨损轴承（原位）   R02b 磨损轴承近看
  拆下放工作台         R03 工作台总览                 R04 磨损件 / 新件近看              R05 上盖翻过来看内侧保养标记
  清理后               R06 进气口清理后（上盖装着）   R07 新轴承装在原位                 R07b 新轴承近看
另外拼三张左右对比图 C01–C03。七号本体只作参考（只读导入），不导出。
只渲染 RENDER_ONLY 环境变量里列出的编号时，其它跳过（调试用）。
"""
import os as _os

RENDER_ONLY = [s for s in _os.environ.get("FK_RENDER_ONLY", "").split(",") if s]
SAMPLES = int(_os.environ.get("FK_SAMPLES", "128"))


def _enable_gpu():
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for kind in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = kind
                prefs.get_devices()
                devs = [d for d in prefs.devices if d.type == kind]
                if devs:
                    for d in prefs.devices:
                        d.use = d.type == kind
                    scene.cycles.device = "GPU"
                    return kind
            except Exception:
                continue
    except Exception:
        pass
    scene.cycles.device = "CPU"
    return "CPU"


scene.render.engine = "CYCLES"
DEVICE = _enable_gpu()
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.render.resolution_x = 1400
scene.render.resolution_y = 1050
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Base Contrast"

world = bpy.data.worlds.new("ClinicWorld")
scene.world = world
_bg = next(n for n in hs.ensure_node_tree(world).nodes if n.type == "BACKGROUND")
_bg.inputs["Color"].default_value = (0.020, 0.024, 0.024, 1)
_bg.inputs["Strength"].default_value = 1.0

studio = bpy.data.collections.new("_RenderStudio")
scene.collection.children.link(studio)

# 七号参考体的贴图（只读加载 Assets/RobotV4/Model/textures，渲染时看得出真实材质；不改原图、不导出）
_TEXDIR = os.path.join(ROOT, "Assets", "RobotV4", "Model", "textures")
for _m in {s.material for o in R.values() for s in o.material_slots if s.material}:
    _key = _m.name.replace("M_", "")
    if not os.path.exists(os.path.join(_TEXDIR, f"T_{_key}_BaseColor.png")):
        continue
    _nt = hs.ensure_node_tree(_m)
    _b = next((n for n in _nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if _b is None:
        continue
    for _suffix, _input, _noncolor in (("BaseColor", "Base Color", False), ("Roughness", "Roughness", True), ("Metallic", "Metallic", True)):
        _p = os.path.join(_TEXDIR, f"T_{_key}_{_suffix}.png")
        if not os.path.exists(_p):
            continue
        _t = _nt.nodes.new("ShaderNodeTexImage")
        _t.image = bpy.data.images.load(_p, check_existing=True)
        if _noncolor:
            _t.image.colorspace_settings.name = "Non-Color"
        _nt.links.new(_t.outputs["Color"], _b.inputs[_input])


def _light(name, loc, target, energy, size, color):
    data = bpy.data.lights.new(name, type="AREA")
    data.energy, data.size, data.color = energy, size, color
    ob = bpy.data.objects.new(name, data)
    ob.location = Vector(loc)
    hs.look_at(ob, Vector(target))
    studio.objects.link(ob)
    return ob


cam = bpy.data.objects.new("FK_Cam", bpy.data.cameras.new("FK_Cam"))
studio.objects.link(cam)
scene.camera = cam
cam.data.clip_start = 0.005

# 七号旁的检修灯（暖）+ 冷色顶光 + 轮廓光：旧诊所工作灯的感觉，不做霓虹
ENG = Vector(at_axis(0.03))
_light("Clinic_WorkLamp", ENG + Vector((0.35, -0.45, 0.55)), ENG, 26, 0.25, (1.0, 0.86, 0.68))
_light("Clinic_Ceiling", ENG + Vector((-0.2, 0.1, 0.9)), ENG, 10, 0.8, (0.82, 0.9, 0.88))
_light("Clinic_Rim", ENG + Vector((0.45, 0.45, 0.25)), ENG, 10, 0.3, (0.75, 0.85, 1.0))

# ---- 工作台（只为渲染搭的操作垫，不导出）
bench = bpy.data.collections.new("_Bench")
scene.collection.children.link(bench)
BENCH = Vector((2.0, 0.0, 0.9))
_mat_bench = bpy.data.materials.new("M_Render_BenchMat")
_bsdf = next(n for n in hs.ensure_node_tree(_mat_bench).nodes if n.type == "BSDF_PRINCIPLED")
_bsdf.inputs["Base Color"].default_value = (0.045, 0.06, 0.05, 1)
_bsdf.inputs["Roughness"].default_value = 0.85
_bm = hs.bm_box((0.7, 0.45, 0.006), (BENCH.x, BENCH.y, BENCH.z - 0.003))
_me = bpy.data.meshes.new("Render_BenchMat")
_bm.to_mesh(_me)
_bm.free()
_me.materials.append(_mat_bench)
bench.objects.link(bpy.data.objects.new("Render_BenchMat", _me))
_light("Bench_Lamp", BENCH + Vector((-0.15, -0.3, 0.55)), BENCH, 24, 0.3, (1.0, 0.82, 0.6))
_light("Bench_Fill", BENCH + Vector((0.4, 0.2, 0.6)), BENCH, 14, 0.8, (0.8, 0.88, 0.9))


def _copy(ob, matrix, coll=bench):
    c = ob.copy()
    c.parent = None
    coll.objects.link(c)
    c.matrix_world = matrix
    return c


def _rest_on_mat(objs, xy, extra_rot=Matrix.Identity(4)):
    """把一组对象（以轴承 / 进气口坐标系摆放）平放到操作垫上：先算放置后的最低点，再落到垫面。"""
    inv = FRAME.inverted()
    rel = [(o, inv @ o.matrix_world) for o in objs]
    place = Matrix.Translation(Vector((xy[0], xy[1], 0)) + Vector((0, 0, BENCH.z))) @ extra_rot
    tmp = [_copy(o, place @ m) for o, m in rel]
    bpy.context.view_layer.update()
    zmin = min((c.matrix_world @ v.co).z for c in tmp for v in c.data.vertices)
    for c in tmp:
        c.matrix_world = Matrix.Translation(Vector((0, 0, BENCH.z + 0.0005 - zmin))) @ c.matrix_world
    return tmp


worn_bench = _rest_on_mat([bearing_worn, chips], (BENCH.x - 0.08, BENCH.y - 0.04), Matrix.Rotation(math.radians(25), 4, "Z"))
new_bench = _rest_on_mat([bearing_new], (BENCH.x - 0.01, BENCH.y - 0.05))
clog_bench = _rest_on_mat([clog_root, fibers], (BENCH.x - 0.05, BENCH.y + 0.07), Matrix.Rotation(math.radians(8), 4, "X"))
cover_parts = [R[n] for n in ("Engine_UpperCover_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L")] + [label]
cover_bench = _rest_on_mat(cover_parts, (BENCH.x + 0.17, BENCH.y + 0.02), Matrix.Rotation(math.radians(180), 4, "X") @ Matrix.Rotation(math.radians(-90), 4, "Z"))

COVER = [R[n] for n in ("Engine_UpperCover_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L",
                        "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L")]
CLOG = [clog_root, fibers, guard_dust, rim]


def state(fault, cover_on):
    R["Engine_BearingTop_L"].hide_render = True            # 原件被替换（推荐的挂载方式：关掉原件的渲染器）
    for o in (bearing_worn, chips):
        o.hide_render = not fault
    bearing_new.hide_render = fault
    for o in CLOG:
        o.hide_render = not (fault and cover_on)
    label.hide_render = not cover_on
    for o in COVER:
        o.hide_render = not cover_on


def shot(code, name, loc, target, lens):
    if RENDER_ONLY and code not in RENDER_ONLY:
        return None
    path = os.path.join(RENDERS, f"{code}_{name}.png")
    hs.render_view(scene, cam, path, loc, target, lens)
    print("RENDERED", path)
    return path


INTAKE_C = Vector(at_axis(0.062))
V_TOP = (INTAKE_C + Vector((0.20, -0.30, 0.26)), INTAKE_C, 50)
V_SEAT = (CEN + Vector((0.13, -0.19, 0.20)), CEN + Vector((0.0, 0.0, -0.01)), 50)
V_SEAT_MACRO = (CEN + Vector((0.05, -0.075, 0.085)), CEN, 70)
V_INTAKE_MACRO = (INTAKE_C + Vector((0.05, -0.09, 0.11)), INTAKE_C, 70)

state(fault=True, cover_on=True)
shot("R01", "installed_fault_intake_clogged", *V_TOP)
shot("R01b", "installed_fault_intake_macro", *V_INTAKE_MACRO)
state(fault=True, cover_on=False)
shot("R02", "installed_fault_cover_off_worn_bearing", *V_SEAT)
shot("R02b", "installed_fault_worn_bearing_macro", *V_SEAT_MACRO)

# 工作台：七号上只留新轴承（不影响工作台画面）
state(fault=False, cover_on=False)
shot("R03", "bench_removed_parts_overview", BENCH + Vector((0.05, -0.42, 0.40)), BENCH + Vector((0.03, 0.01, 0.0)), 45)
shot("R04", "bench_worn_vs_new_bearing_macro", BENCH + Vector((-0.045, -0.16, 0.12)), BENCH + Vector((-0.045, -0.045, 0.0)), 75)
_lab = cover_bench[-1]
lab_c = sum((_lab.matrix_world @ v.co for v in _lab.data.vertices), Vector()) / len(_lab.data.vertices)
_lab_n = sum((_lab.matrix_world.to_3x3() @ p.normal for p in _lab.data.polygons), Vector()).normalized()   # 贴纸朝外的方向
shot("R05", "bench_cover_inner_maintenance_label", lab_c + _lab_n * 0.13 + Vector((0.0, -0.025, 0.0)), lab_c, 60)

state(fault=False, cover_on=True)
shot("R06", "after_clean_intake_clear", *V_TOP)
shot("R06b", "after_clean_intake_macro", *V_INTAKE_MACRO)
state(fault=False, cover_on=False)
shot("R07", "after_clean_new_bearing_seated", *V_SEAT)
shot("R07b", "after_clean_new_bearing_macro", *V_SEAT_MACRO)


# ---- 左右对比图（读回渲染图拼接，顶部加一条说明）
def _load(p):
    img = bpy.data.images.load(p)
    w_, h_ = img.size
    arr = np.array(img.pixels[:]).reshape(h_, w_, 4)[::-1, :, :3]
    bpy.data.images.remove(img)
    return arr


def compare(code, left, right, ltxt, rtxt):
    if RENDER_ONLY:
        return
    a, b = _load(left), _load(right)
    bar = np.full((60, a.shape[1] * 2 + 12, 3), 0.08)
    hs.draw_text(bar, ltxt, 24, 16, 4, (0.85, 0.82, 0.74))
    hs.draw_text(bar, rtxt, a.shape[1] + 36, 16, 4, (0.85, 0.82, 0.74))
    sep = np.full((a.shape[0], 12, 3), 0.08)
    sheet = np.concatenate([bar, np.concatenate([a, sep, b], axis=1)], axis=0)
    rgba = np.concatenate([sheet, np.ones(sheet.shape[:2] + (1,))], axis=-1)
    img = bpy.data.images.new(code, width=sheet.shape[1], height=sheet.shape[0])
    img.pixels.foreach_set(np.flipud(rgba).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(RENDERS, f"{code}.png")
    img.file_format = "PNG"
    img.save()
    print("RENDERED", img.filepath_raw)


r = lambda c: next(os.path.join(RENDERS, f) for f in os.listdir(RENDERS) if f.startswith(c + "_"))
if not RENDER_ONLY:
    compare("C01_intake_installed_vs_cleaned", r("R01"), r("R06"), "INSTALLED - CLOGGED", "AFTER CLEAN")
    compare("C02_bearing_seated_worn_vs_new", r("R02"), r("R07"), "INSTALLED - WORN BEARING", "AFTER - NEW BEARING")
    compare("C03_bearing_macro_worn_vs_new", r("R02b"), r("R07b"), "WORN - CLOSE", "NEW - CLOSE")
    compare("C04_intake_macro_clogged_vs_cleaned", r("R01b"), r("R06b"), "CLOGGED - CLOSE", "CLEANED - CLOSE")
print("RENDER_DEVICE", DEVICE)
