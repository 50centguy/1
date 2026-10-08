# Export fix: UCC_SE_Fabric self-intersecting polygons (2026-10-08)

## Defect (Unity acceptance)
On FBX import, Unity reported twice that a polygon of `UCC_SE_Fabric` was self-intersecting and discarded it. Because of that, the strict import triangle-count gate failed.

## Diagnosis
I ran the read-only probe `tools/probe_ngons.py` on the pre-fix `UnifiedClinicClutter.blend`. Its results are frozen in `pre_export_fix_baseline.json`.

- `UCC_SE_Fabric` had exactly 2 self-intersecting n-gons, polygons 204 and 205. These were the 16-vertex end caps of the folded cloth on the middle hook. That explains Unity's two messages: 2 × 14 = 28 source triangles were dropped.
- **Cause:** the cloth's (d, y) cross-section was made by offsetting its centreline ±11 mm along the wall normal (d) only. Where the fold runs almost horizontally over the hook, the inner and outer outlines crossed, giving a bow-tie cap.
- `recalc_face_normals` then gave that cap an arbitrary winding.
- No other object had a self-intersecting n-gon.

## Fix (`build_clinic_clutter.py`)
- **`cloth_section()`:** the strip outline is now offset along the mitred 2-D normal of the centreline, so the outlines can't cross. The fold is still 22 mm thick and has the same centreline. Its outline only gains about 11 mm at the very top, where the old d-only offset had pinched it to zero thickness.
- **`make(..., triangulate=True)` for `UCC_SE_Fabric`:** the mesh is explicitly triangulated during generation, after the normals are recalculated. It uses `bmesh.ops.triangulate(quad_method=FIXED, ngon_method=EAR_CLIP)`, and the loop UVs are carried through, so the atlas mapping is unchanged.
- **New hard gates in the build:**
  - no self-intersecting n-gons in any object
  - no inconsistent winding (no edge used twice in the same direction)
  - the fabric is 100 % triangles
  - FBX re-import polygon histogram and triangle count equal the source, for every object
  - pivots unchanged
  - non-fabric objects unchanged in bounds and topology compared with the frozen pre-fix baseline

## Result (run `Reports/blender_run.log`, exit 0, `failures: []`)
| | before | after |
|---|---|---|
| `UCC_SE_Fabric` polygons | 236 quads, 4 hexagons, 2 decagons, **2 self-intersecting 16-gons** | **532 triangles**, 0 self-intersecting |
| `UCC_SE_Fabric` triangles (source) | 532 | 532 |
| total triangles / mesh objects / materials / textures | 2772 / 11 / 4 / 2 | 2772 / 11 / 4 / 2 |
| `UCC_SE_Fabric` pivot | — | Δ 0.0 m |
| `UCC_SE_Fabric` bounds | — | Δ 0.0116 m (top of cloth fold) |
| the other 10 objects | — | identical bounds, pivots and topology (Δ 0.0) |
| FBX Blender re-import | — | bounds error 0.0 m, fabric = 532 tris, topology matches source for all 11 |
| base `UnifiedClinic.blend` sha256 | b376a97e…d3cd7a | b376a97e…d3cd7a (unchanged) |

Source metrics are reported as before. The source always counted 532 fabric triangles, and the pre-fix Unity import would have lost 28 of them (expected 2744 in total). After the fix, Unity should import 2772 triangles. That is an expectation only: Unity was not run here. **Superseded:** this expectation was wrong. 20 of those 2772 were zero-area triangles in `UCC_SW_Metal` (Unity imported 2752); see the follow-up below.

**FBX name and contract are unchanged:** `Export/UnifiedClinic_ClutterAddon.fbx`, `axis_forward=-Z, axis_up=Y, bake_space_transform=False, FBX_SCALE_UNITS`, root `UnifiedClinicClutter`, u = (-bx, bz, -by).

## Renders
The geometry changed slightly (the cloth fold top), so all three stills were re-rendered by the full run. The SE detail is visually indistinguishable from before. The pre-fix stills are kept in `Renders/pre_export_fix/`.

## Remaining notes, not changed in this fix
- The hook tubes in `UCC_SE_Metal` contain slightly twisted quads (max non-planarity 7.6 mm). They are quads, not n-gons, so Unity triangulates them as two triangles, and Unity did not flag them. They were left unchanged so this fix stays limited to the fabric.
- Not verified: Unity import (the gate must be re-run by acceptance), dynamic collision, VR.

---

# Follow-up: UCC_SW_Metal zero-area triangles (2026-10-08)

## Defect (Unity acceptance, second pass)
There were no more self-intersection warnings, but Unity imported 2752 triangles against a source count of 2772. The parent's read-only probe found exactly 20 loop triangles with `area < 1e-10` in `UCC_SW_Metal`, and none in the other meshes. Unity correctly drops these. The earlier source figure of 2772 therefore included 20 unusable triangles.

## Diagnosis (`tools/probe_degenerate.py` on the pre-fix .blend → `pre_degenerate_fix_probe.json`)
- The 20 triangles are 10 zero-area quads on the third SW canister, the one with r = 0.050.
- In that canister's lathe profile, the shoulder ring `(y0+h+0.05, r*0.32)` equals the neck ring `(y0+h+0.05, 0.016)` exactly, because 0.050 × 0.32 = 0.016.
- That produced two coincident rings joined by a band of flat quads. The other two canisters (r = 0.054) have distinct rings and are fine.

## Fix (`build_clinic_clutter.py`, `make(..., weld_degenerate=True)` for `UCC_SW_Metal` only)
- Only the vertices of faces proven to have zero area are welded, using `bmesh.ops.remove_doubles` at 1e-6 m. This removed exactly 10 faces (all quads, 20 loop triangles).
- The UVs of every surviving face are its unchanged per-loop UVs.
- Vertex positions are untouched, so the pivot and bounds are identical (Δ 0.0 m) and the visible geometry is unchanged.
- Surface area is 0.6654515 m² before and after.

## New hard guards
- **Zero area:** every source mesh, and every FBX re-import, must have 0 loop triangles with area < 1e-10 m². The re-import guard is part of the topology-match check.
- **Area:** every object's surface area must equal the pre-degenerate-fix probe within 1e-7 m².
- **Frozen baseline:** the "non-fabric objects identical to the frozen baseline" rule now allows exactly one documented exception, `EXPECTED_TOPOLOGY_CHANGE["UCC_SW_Metal"]` = {4: 254, 10: 18} polygons and 652 triangles. Any other change in any non-fabric object still fails.

## Result (`Reports/blender_run_norender_degenerate_fix.log`, exit 0, `failures: []`)
| | source before | source after | FBX re-import after |
|---|---|---|---|
| `UCC_SW_Metal` triangles (loop) / zero-area | 672 / **20** | **652 / 0** | 652 / 0 |
| `UCC_SW_Metal` polygons | 264 quads + 18 decagons | 254 quads + 18 decagons | same |
| all 11 objects, triangles | 2772 (2752 usable) | **2752 (2752 usable)** | 2752, 0 zero-area, topology = source for all 11 |
| the other 10 objects | — | unchanged (triangles, polygons, area, bounds, pivots) | — |

- **Budgets and naming:** 2752 triangles, 11 objects, 4 materials, 2 textures. The FBX name and coordinate contract are unchanged.
- **Base file:** `UnifiedClinic.blend` sha256 is b376a97e…d3cd7a, unchanged.
- **Renders:** this was a norender run, so no new stills were made. The stills in `Renders/` are from the 12:04 full run of the fabric fix, and their files were not modified. They remain valid because no visible geometry, UVs or shading changed. `stats.json` marks them `generated_by_this_run: false`.
- **Not verified:** the Unity import. The parent re-runs acceptance, and the expected count is 2752.
- Pre-fix evidence: `Reports/pre_degenerate_fix_probe.json`, `Reports/stats_before_degenerate_fix.json`, `Reports/placement_bounds_before_degenerate_fix.json`.
