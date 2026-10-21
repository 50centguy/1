"""Bakes a V2 clinic asset (.blend from its build script) down to one atlas material plus its emissive materials.

Every mesh gets a new non-overlapping UV (smart project, islands scaled by real area, packed together); the current
look is baked into <Name>_Atlas.png (base colour) and <Name>_AtlasMS.png (URP metallic in R, smoothness in A),
both sampled through the original tiling UVs. Then every non-emissive slot is replaced by M_<Name>_Atlas, the
atlas UV becomes the only UV map, and the result is exported with the same FBX settings. Object names, pivots and
hierarchy are untouched, so the Unity runtimes and builders keep working.

Run: blender -b --factory-startup <asset.blend> --python atlas_bake.py -- <Name> <atlas_px> <out_dir>
"""
import math
import os
import sys

import bpy
import numpy as np

ARGS = sys.argv[sys.argv.index("--") + 1:]
NAME, SIZE, OUT = ARGS[0], int(ARGS[1]), ARGS[2]
os.makedirs(OUT, exist_ok=True)
SCENE = bpy.context.scene
# Flat-colour emissive materials stay separate on any object (UVs do not matter to them). Objects made only of
# prints (mat grid, labels, CRT glass) are left completely as they are, original UVs included, so the prints stay
# sharp; a print on a mixed object (the notepad) is baked into the atlas like everything else.
EMISSIVE = {"M_BV2_Bulb", "M_BV2_LedRed", "M_BV2_LedGreen", "M_GT_Lens", "M_BV2_Holo", "M_ZN_LampAmber", "M_ST_Gel", "M_ST_GelAmber"}
PRINTS = {"M_BV2_CrtGlass", "M_BV2_Mat", "M_BV2_Paper", "M_BV2_Sticky", "M_BV2_Label", "M_ST_Label", "M_ST_Screen",
          "M_BV2_LeaseTag", "M_BV2_WorkOrder", "M_BV2_RentNote", "M_BV2_SealTape", "M_BV2_AssetStencil", "M_BV2_MeterFace",
          "M_BV2_Lcd", "M_BV2_FixturePanel", "M_BV2_Photo", "M_BV2_TapeLabel", "M_BV2_MiniFace",
          "M_BV2_Pegboard", "M_BV2_WallPrints", "M_ZN_CounterPrints", "M_ZN_DockPrints", "M_ZN_FloorMat", "M_ZN_TermGlass", "M_ST_Prints"}
ATLAS_MAT = "M_%s_Atlas" % NAME


def names(o):
    return {m.name for m in o.data.materials if m is not None}


every_mesh = [o for o in SCENE.objects if o.type == "MESH"]
untouched = [o for o in every_mesh if names(o) & PRINTS and names(o) <= PRINTS | EMISSIVE]
meshes = [o for o in every_mesh if o not in untouched]
for o in meshes:
    o.hide_render = False
    o.hide_set(False)
    o.data.uv_layers[0].name = "SourceUV"
    atlas = o.data.uv_layers.new(name="AtlasUV")
    o.data.uv_layers.active = atlas

# ---- one shared atlas layout ---------------------------------------------------------------------------------
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, scale_to_bounds=False)
bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
bpy.ops.object.mode_set(mode="OBJECT")

# ---- bake targets ---------------------------------------------------------------------------------------------
color = bpy.data.images.new(NAME + "_Atlas", SIZE, SIZE, alpha=False)
metal = bpy.data.images.new(NAME + "_AtlasMetal", SIZE, SIZE, alpha=False)
smooth = bpy.data.images.new(NAME + "_AtlasSmooth", SIZE, SIZE, alpha=False)
materials = {m for o in meshes for m in o.data.materials if m is not None and m.name not in EMISSIVE}


def prepare(target):
    """Each baked material samples its textures through SourceUV and writes into `target` through AtlasUV."""
    for m in materials:
        nt = m.node_tree
        for n in [n for n in nt.nodes if n.name.startswith("BakeTarget")]:
            nt.nodes.remove(n)
        for tex in [n for n in nt.nodes if n.type == "TEX_IMAGE"]:
            if not tex.inputs["Vector"].is_linked:
                uv = nt.nodes.new("ShaderNodeUVMap")
                uv.uv_map = "SourceUV"
                nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
            tex.interpolation = "Linear"
        node = nt.nodes.new("ShaderNodeTexImage")
        node.name = "BakeTarget"
        node.image = target
        uvt = nt.nodes.new("ShaderNodeUVMap")
        uvt.name = "BakeTargetUV"
        uvt.uv_map = "AtlasUV"
        nt.links.new(uvt.outputs["UV"], node.inputs["Vector"])
        nt.nodes.active = node
        node.select = True


SCENE.render.engine = "CYCLES"
SCENE.cycles.device = "CPU"
SCENE.cycles.samples = 1
SCENE.render.bake.margin = 4
SCENE.render.bake.use_clear = True
# Only the meshes that carry a baked material.
bpy.ops.object.select_all(action="DESELECT")
baked = [o for o in meshes if any(m is not None and m.name not in EMISSIVE for m in o.data.materials)]
for o in baked:
    o.select_set(True)
bpy.context.view_layer.objects.active = baked[0]

prepare(color)
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})


def bake_constant(target, value_of):
    """Bake a per-material constant via emission (Emit bake ignores lighting)."""
    saved = {}
    for m in materials:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        v = value_of(bsdf)
        saved[m.name] = (tuple(bsdf.inputs["Emission Color"].default_value), bsdf.inputs["Emission Strength"].default_value)
        bsdf.inputs["Emission Color"].default_value = (v, v, v, 1)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    prepare(target)
    bpy.ops.object.bake(type="EMIT")
    for m in materials:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Emission Color"].default_value, bsdf.inputs["Emission Strength"].default_value = saved[m.name]


bake_constant(metal, lambda b: b.inputs["Metallic"].default_value)
bake_constant(smooth, lambda b: 1 - b.inputs["Roughness"].default_value)


def pixels(img):
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(SIZE, SIZE, 4)


def save(img_name, rgba):
    img = bpy.data.images.new(img_name, SIZE, SIZE, alpha=True)
    img.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, img_name + ".png")
    img.file_format = "PNG"
    img.save()


c = pixels(color)
c[..., 3] = 1
save(NAME + "_Atlas", c)
ms = np.zeros_like(c)
ms[..., 0] = pixels(metal)[..., 0]
ms[..., 3] = pixels(smooth)[..., 0]
save(NAME + "_AtlasMS", ms)

# ---- swap to the atlas material and keep only the atlas UV ------------------------------------------------------
atlas_mat = bpy.data.materials.new(ATLAS_MAT)
for o in meshes:
    old = list(o.data.materials)
    keep = [atlas_mat] if any(m is not None and m.name not in EMISSIVE for m in old) else []
    keep += [m for m in dict.fromkeys(old) if m is not None and m.name in EMISSIVE]
    remap = [keep.index(atlas_mat) if (m is None or m.name not in EMISSIVE) else keep.index(m) for m in old]
    indices = [remap[p.material_index] if p.material_index < len(remap) else 0 for p in o.data.polygons]
    o.data.materials.clear()
    for m in keep:
        o.data.materials.append(m)
    for p, i in zip(o.data.polygons, indices):
        p.material_index = i
    o.data.uv_layers.remove(o.data.uv_layers["SourceUV"])

slots = sum(len(o.data.materials) for o in every_mesh)
print("ATLAS", NAME, "objects", len(meshes), "material slots", slots)
bpy.ops.object.select_all(action="DESELECT")
for o in SCENE.objects:
    if o.type in {"MESH", "EMPTY"} and not o.name.startswith("Cam"):
        o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
                         mesh_smooth_type="OFF", use_custom_props=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                         path_mode="STRIP", use_triangles=False)
print("ATLAS_DONE")
