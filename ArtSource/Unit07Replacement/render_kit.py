"""
补件渲染（Cycles，读 Unit07ReplacementKit.blend，不改它、不保存）：
  K01 连接状态（原物体）    K02 机身端拔下（原线、原机身端插头隐藏；固定插座补件 + 断开线缆显示）
  K03 旧总成取出后机身一侧（只剩固定插座）   K04 旧总成单独（断开线缆随引擎件）
  K05 重新连接（新引擎材质 + 原线、原插头）  K06 机身固定端近景（拔下后）
  M01 旧 / 新引擎同灯光同机位（左：原 M_Engine / M_Internal；右：新件贴图）
标注：编号文字只在渲染里出现（不导出、不保存）。所有图都是本次生成的资源实际渲染。
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REN = os.path.join(HERE, "Renders")
os.makedirs(REN, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "Unit07ReplacementKit.blend"))
scene = bpy.context.scene
O = {o.name: o for o in bpy.data.objects}

# --- 灯光与相机
world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.055, 0.06, 1)
for name, loc, e in (("Key", (1.2, -1.0, 1.6), 160), ("Fill", (-1.0, -1.2, 0.8), 60), ("Rim", (0.4, 1.4, 1.2), 90)):
    ld = bpy.data.lights.new(name, "AREA"); ld.energy = e; ld.size = 1.0
    lo = bpy.data.objects.new(name, ld); scene.collection.objects.link(lo); lo.location = loc
    lo.rotation_euler = (Vector((0.25, 0, 0.4)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
scene.render.engine = "CYCLES"; scene.cycles.samples = 48; scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
try:
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for dev in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = dev; prefs.get_devices()
            if any(d.type == dev for d in prefs.devices):
                for d in prefs.devices: d.use = d.type == dev
                scene.cycles.device = "GPU"; break
        except Exception:
            continue
except Exception:
    pass
scene.view_settings.view_transform = "Standard"


def look(loc, target, lens):
    cam.location = Vector(loc); cam.data.lens = lens
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()


labels = []


def label(text, pos, color=(1.0, 0.85, 0.1)):
    cu = bpy.data.curves.new("L" + text, "FONT"); cu.body = text; cu.size = 0.012
    ob = bpy.data.objects.new("Label_" + text, cu); scene.collection.objects.link(ob); ob.location = Vector(pos)
    m = bpy.data.materials.new("LM" + text); m.use_nodes = True
    em = m.node_tree.nodes.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (*color, 1); em.inputs["Strength"].default_value = 6
    m.node_tree.links.new(em.outputs[0], m.node_tree.nodes["Material Output"].inputs["Surface"])
    cu.materials.append(m)
    c = ob.constraints.new("TRACK_TO"); c.target = cam; c.track_axis = "TRACK_Z"; c.up_axis = "UP_Y"
    labels.append(ob)


def clear_labels():
    for ob in labels:
        bpy.data.objects.remove(ob, do_unlink=True)
    labels.clear()


ENGINE = [o for o in bpy.data.objects if o.parent_bone == "Engine_L_Hinge" and o.type == "MESH"]
CABLES = ["ExternalCable_L", "AuxCable_L"]
KIT_UNPLUG = {c: [o for o in O[f"Kit_{c}_Unplugged"].children_recursive if o.type == "MESH" and not o.name.startswith("Calib_")] for c in CABLES}
KIT_SOCKET = {c: [o for o in O[f"Kit_{c}_BodySocketFixed"].children_recursive if o.type == "MESH" and not o.name.startswith("Calib_")] for c in CABLES}
CALIB = [o for o in bpy.data.objects if o.name.startswith("Calib_")]


def show(objs, on):
    for o in objs:
        o.hide_render = not on


def state(name):
    """按显示表切换。"""
    all_kit = [o for c in CABLES for o in KIT_UNPLUG[c] + KIT_SOCKET[c]]
    show(CALIB, False)
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent_bone in ("Engine_L_Hinge", "Body") or o.name in CABLES:
            o.hide_render = False
    show(all_kit, False)
    if name == "connected":
        return
    if name in ("unplugged", "removed", "old_assembly"):
        show([O[c] for c in CABLES] + [O[c + "_PlugBody"] for c in CABLES] + [O["ExternalCable_Clip_L"]], False)
        show([o for c in CABLES for o in KIT_SOCKET[c]], True)
        show([o for c in CABLES for o in KIT_UNPLUG[c]], True)
    if name == "removed":            # 机身一侧：引擎件、随引擎的断开线缆都不在
        show(ENGINE, False); show([o for c in CABLES for o in KIT_UNPLUG[c]], False)
    if name == "old_assembly":       # 只看旧总成：机身件全隐藏
        show([o for o in bpy.data.objects if o.type == "MESH" and o.parent_bone != "Engine_L_Hinge" and o not in [x for c in CABLES for x in KIT_UNPLUG[c]]], False)


sock = O["ExternalCable_L_PlugBody"].matrix_world.translation
eng_plug = O["ExternalCable_L_PlugEngine"].matrix_world.translation
mid = (sock + eng_plug) / 2 + Vector((0, 0.0, -0.02))
cam_iface = mid + Vector((0.24, -0.42, 0.06))   # 七号左前方看黄线一侧（外壳照常显示）
aux_mid = (O["AuxCable_L_PlugBody"].matrix_world.translation + O["AuxCable_L_PlugEngine"].matrix_world.translation) / 2 + Vector((0, 0, -0.02))
cam_rear = aux_mid + Vector((0.24, 0.42, 0.06))  # 七号左后方看灰线一侧


def render(name):
    scene.render.filepath = os.path.join(REN, name); bpy.ops.render.render(write_still=True)


if "--skip" not in sys.argv:
    # 连接
    state("connected"); look(cam_iface, mid + Vector((0, 0, -0.02)), 55)
    for txt, nm in (("1", "ExternalCable_L_PlugBody"), ("2", "ExternalCable_L_PlugEngine"), ("3", "AuxCable_L_PlugBody"), ("4", "AuxCable_L_PlugEngine")):
        label(txt, O[nm].matrix_world.translation + Vector((0, -0.012, 0.014)))
    render("K01_connected_labeled.png")
    look(cam_rear, aux_mid + Vector((0, 0, -0.02)), 55); render("K01b_connected_labeled_rear.png"); clear_labels()
    # 拔下
    state("unplugged"); look(cam_iface, mid + Vector((0, 0, -0.02)), 55)
    for c, a, b in (("ExternalCable_L", "5", "6"), ("AuxCable_L", "7", "8")):
        label(a, O[f"Anchor_{c}_SocketFace"].matrix_world.translation + Vector((0, -0.012, 0.012)), (0.3, 1.0, 0.4))
        label(b, O[f"Anchor_{c}_FreePlugTip"].matrix_world.translation + Vector((0, -0.012, -0.012)), (0.3, 0.8, 1.0))
    render("K02_unplugged_labeled.png")
    look(cam_rear, aux_mid + Vector((0, 0, -0.02)), 55); render("K02b_unplugged_labeled_rear.png"); clear_labels()
    # 机身固定端近景
    look(sock + Vector((0.12, -0.10, 0.05)), sock, 85); render("K06_body_socket_fixed_closeup.png")
    # 旧总成取出后（机身一侧）
    state("removed"); look(cam_iface, mid + Vector((0, 0, -0.02)), 55); render("K03_engine_removed_body_side.png")
    # 旧总成单独
    state("old_assembly"); look(mid + Vector((0.35, -0.45, 0.10)), mid + Vector((0.08, 0, -0.04)), 55); render("K04_old_assembly_with_unplugged_cables.png")
    # 重新连接（新材质）
    state("connected")
    new_eng = bpy.data.materials.get("M_Engine").copy(); new_eng.name = "M_Engine_ReplacementL"
    new_int = bpy.data.materials.get("M_Internal").copy(); new_int.name = "M_Internal_ReplacementL"
    for m, tex in ((new_eng, "T_Engine_BaseColor_ReplacementL.png"), (new_int, "T_Internal_BaseColor_ReplacementL.png")):
        img = bpy.data.images.load(os.path.join(HERE, "Textures", tex))
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image is not None and "BaseColor" in n.image.name:
                n.image = img
    orig = {}
    for o in ENGINE + [O[c] for c in CABLES] + [O[c + "_PlugEngine"] for c in CABLES]:
        orig[o.name] = [s.material for s in o.material_slots]
    def apply_new():
        for o in ENGINE:
            for s in o.material_slots:
                if s.material and s.material.name == "M_Engine": s.material = new_eng
                elif s.material and s.material.name == "M_Internal": s.material = new_int
    def restore():
        for o in ENGINE:
            for s, m in zip(o.material_slots, orig[o.name]): s.material = m
    apply_new(); look(cam_iface, mid + Vector((0, 0, -0.02)), 55); render("K05_reconnected_new_engine.png")
    # 旧 / 新同灯光同机位
    eng_c = sum((o.matrix_world.translation for o in ENGINE), Vector()) / len(ENGINE)
    look(eng_c + Vector((0.55, -0.55, 0.25)), eng_c, 50)
    scene.view_settings.exposure = -1.0                           # 布光对白色外壳偏亮；对照图降一档，看得清表面
    restore(); render("M01a_engine_old_material.png")
    apply_new(); render("M01b_engine_replacement_material.png")
    restore(); scene.view_settings.exposure = 0.0
print("[render] done")
