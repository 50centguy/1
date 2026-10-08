# UnifiedClinic clutter add-on (wall-bound lived-in detail)

This is an additive asset for the existing UnifiedClinic room. It does not change the room, dock, bench, trade or gantry, their anchors, or any file under `../UnifiedClinic/`. During the build the base `UnifiedClinic.blend` is opened in memory and never saved. The build checks that file's SHA-256 before and after, and it was unchanged: `base_blend_unchanged.unchanged = true` in `stats.json`.

## Command (actually run, Blender 5.2.2 LTS)

```
cd ArtSource/UnifiedClinicClutter
D:/steam/steamapps/common/Blender/blender.exe -b --factory-startup --python-exit-code 1 --python build_clinic_clutter.py > Reports/blender_run.log 2>&1
```
Exit code 0. The summary line in `Reports/blender_run.log` reads:
`UCC_SUMMARY {"counts": {"triangles": 2772, "mesh_objects": 11, "materials": 4, "textures": 2}, "roundtrip": true, "renders": [true, true, true], "base_unchanged": true, "failures": []}`.
If any assertion fails, the script exits 1. Pass `-- norender` to skip the renders. The log also contains EEVEE "Shadow buffer full" messages, which come from the base scene's lights. They are render-quality warnings, not failures. No Blender process was left running.

## Outputs
| Path | What |
|---|---|
| `build_clinic_clutter.py` | the one deterministic build/check/render script (seeded RNG) |
| `UnifiedClinicClutter.blend` | the add-on only: root empty `UnifiedClinicClutter` plus 11 meshes. No cameras or lights. |
| `Export/UnifiedClinic_ClutterAddon.fbx` | add-on FBX. Same settings as UnifiedClinic: `axis_forward=-Z, axis_up=Y, bake_space_transform=False, FBX_SCALE_UNITS`, EMPTY+MESH only |
| `Textures/T_UCC_SoftAtlas.png` (256²), `Textures/T_UCC_PaintAtlas.png` (128²) | procedural bitmaps: fabric/canvas/patch/strap/notes/tape; worn ivory and grey-green paint, steel, dull cable colours |
| `stats.json` | counts, budget, triangles per material, FBX round trip, base-unchanged hash, render list, failures |
| `placement_bounds.json` | per object: Unity AABB, wall-local s/y/d ranges, depth below the strip line, path distances, overlap/gap against base meshes |
| `Renders/UCC_Overview.png` | overview looking north from the entrance side (NW junction, NE junction/canister/note in context) |
| `Renders/UCC_WallDetail_SE.png` | wall detail of the SE entrance wall: coat, folded cloth, patched bag, hook rail, notes |
| `Renders/UCC_Supplementary_SW.png` | extra view: SW canister rack and notes |
| `Reports/blender_run.log` | raw Blender stdout/stderr of the run above |
| `tools/probe_*.py` | read-only probes of the base .blend used to find free wall space |

**Placement in Unity:** instantiate the FBX at Unity (0,0,0) and keep the imported root rotation (Euler -90,0,0), the same as `UnifiedClinic_Environment`. Axes are Unity +Y up, +Z north, +X east, with u = (-bx, bz, -by).

## Content (11 mesh objects, grouped by wall and material)
- **SE wall (inside the door, east):** a grey-green hook rail with 3 hooks. On them hang a worn grey-green coat (lofted low-poly body with flattened sleeves, a patched pocket and a turned collar), a folded old-ivory cloth with stripes, and a grey canvas shoulder bag with a sewn patch, strap and buckle. Two taped notes sit beside them. Objects: `UCC_SE_Fabric`, `UCC_SE_Metal`, `UCC_SE_Paper`.
- **SW wall (left panel beside the limb shelf):** a strap rack holding three reused empty canisters (ivory/green paint with chips and restrained rust), plus a tag and tape. Three taped notes are on the right panel. Objects: `UCC_SW_Metal`, `UCC_SW_Paper`.
- **NW and NE side walls (above y 1.70):** a junction box on each wall with glands, a bundled 4-cable drop that ends under the trim rail, strap wraps, and a low cable run with a taped splice. The NE wall also has one strapped reused canister. Each wall has a note. Objects: `UCC_NW_Metal/Cable/Paper`, `UCC_NE_Metal/Cable/Paper`.
- Materials: `M_UCC_Fabric`, `M_UCC_Paper` (SoftAtlas), `M_UCC_PaintedMetal`, `M_UCC_Cable` (PaintAtlas). The notes carry only abstract pencil and marker marks, with no text and no third-party imagery. Nothing is emissive.

## Measurements (from `stats.json` / `placement_bounds.json`)
- **Budget:** 2772 triangles (limit 4000): Fabric 532, PaintedMetal 1400, Paper 340, Cable 500. 11 mesh objects (limit 20), 4 materials (limit 4), 2 textures (limit 2).
- **Wall strips:** s, y, d are wall-local: s along the wall, y height, d distance off the inner wall face (apothem 3.40).
  - All geometry has d between 0.025 and 0.157 m. The measured panel face is at 0.022 and the limit is 0.20.
  - All geometry has |s| ≤ 1.21. The corner pilasters start at 1.28.
  - The top of everything is y ≤ 2.228. The trim rail is at 2.24 and the existing cable garland at 2.31+.
  - Only `UCC_SE_Fabric` goes below its strip line (coat hem at y 1.12). Its maximum depth below y 1.55 is 0.067 m (limit 0.07). Every other object stays above its strip: 1.55 on SW/SE, 1.70 on NW/NE.
- **Reserved zones:** no vertex is inside any of these:
  - the checks.json reserved zones (+0.05 m margin, y 0.03–1.97)
  - the dock workspace x[-3.25,-1.95] z[-0.95,0.95], at any height
  - the two trade-pad approach boxes (x[1.15,2.10], |z| 0.30–0.90), at any height
  - the dock, robot, bench and bed/gantry bounds expanded by 0.15 m
- **Path centrelines:** the minimum plan distance is 0.68 m (`UCC_NE_Cable` to `trade_operator_entry`, required ≥ 0.45 m and only above y 1.74). Every other object is ≥ 0.70 m from every path; the principal aisle is ≥ 1.43 m away and the east and north aisles are ≥ 1.24 m away.
- **Against the base meshes:** 158 visible base meshes were tested, including the PH_* dock/robot/bench placeholders. There are 0 triangle intersections and the smallest gap is 3 mm, to the wall panels the props are mounted on.
- **FBX round trip:** after a Blender re-import, the maximum bounds error is 0.0 m across all 11 objects. Only EMPTY and MESH types were imported, so the render cameras and light are not in the export.

## Not verified
- **Unity:** neither the FBX import nor the material/texture hookup was run. The u = (-bx, bz, -by) mapping is inherited from the base contract, which itself was only checked by a Blender round trip.
- **Collision:** there are no collision proxies. These are non-interactive wall dressing, and dynamic or physics collision was not tested. The coat and cloth are static solids, not cloth simulation.
- **VR:** player scale, reach and comfort were not tested in a headset.
- **Path checks:** these are 2-D distances to the base path centrelines, not a re-run of the base aisle-width raster. The add-on stays wall-adjacent at 0.157 m deep or less, so the base aisle numbers should still hold, but they were not re-measured.
- **Renders:** these are EEVEE stills of the base scene loaded in memory with the add-on appended. The `RCC_DetailFill` area light (aimed at the SE wall) is lit for all three shots and exists only in that in-memory session.
