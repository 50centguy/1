# Unified clinic integration

This is an independent delivery on `integ/unified-clinic-20261008`, not an automatic merge into main. It starts from the user's current second-night scene and its 34-file source snapshot. The original Unity workspaces are not build or cleanup targets.

## Play and rebuild

First-person integration follow-up: the shared room now starts in walking mode in both nights. WASD moves, mouse delta looks, Shift walks faster, and C toggles a lowered eye position. The crosshair selects nearby objects (1.3 m reach); Tab/Esc releases the mouse for UI. Fixed repair views remain available and movement keys or the Back/Walk button return to the player's unchanged body location. Diagnostic console, dialogue, ledger, repair manual and night-end panels pause movement. See `FirstPerson/README.md` for the latest input-specific evidence; earlier full-suite and performance reports remain historical results, not a claim that the entire suite was rerun after this follow-up.

- Open `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity`, then Play and choose New Game or Continue.
- Both nights use `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity`. The customer-night console and internal UNIT07 flow are phase-gated rather than loaded into different rooms.
- The room-side receive/deliver pads are clickable. Receiving opens the existing diagnostic console; returning to the room permits physical handoff. Completing a repair alone does not post income. An unfinished item cannot be delivered. Authored refusal/replacement advice permits an unpaid return.
- The authored first-night shift currently contains one collector case. The queue implementation supports subsequent authored cases; it does not invent extra customers or narrative content.
- The playable Windows build is `Builds/UnifiedClinic-20261008/BorderRepairClinic.exe`. See `player_build.txt` for the actual build result, not merely the presence of this path.
- Rebuild the integrated room with `Border Repair/Unified Clinic/Rebuild Shared Gameplay With Clinic Environment` (`ClinicGameplayIntegrator.BuildBatch` in batch mode). Do not call the lower-level `UnifiedClinicBuilder.Build` alone for final art placement.
- Scene generation replaces the generated shared scene. Back up hand-edited shared scenes before explicitly rebuilding. The integrator rejects unsaved edits in every loaded scene, preflights imports and restores published scenes, generated art/importer metas, reports, build list and prior scene setup if generation fails.

## Room and art

The octagonal room has one south entrance, a north workbench, central surgery bed and overhead hanging-tool ring, west robot repair zone and east trading counter. Most styling/layout follows V1; trading follows V4: the stool is behind the counter inside the operator area, and the desktop CRT faces the east wall/operator. The V1 workbench articulated arm is included once, not duplicated with its reusable export.

Claude generated real Blender sources, FBX, textures and renders in `ArtSource/UnifiedClinic/` and `ArtSource/UnifiedClinicClutter/`. Base room: 13,700 triangles / 150 meshes / 18 materials. Additive wall clutter: 2,752 valid triangles / 11 meshes / 4 materials. Unity imports and material mappings are separately checked under `ArtImport/`. Source renders contain declared placeholders; the playable Unity scene uses the existing RobotV4, dock and workbench assets.

The 23 low-poly collision proxies retain their authored mesh shape, including diagonal walls and the door opening. They are static nonconvex MeshColliders, not enlarged axis-aligned boxes. Receive/deliver and console interaction volumes are triggers. No RobotV4 mesh was decimated or regenerated.

Only this shared scene opts into a 1.5 m/s carried-part speed setting. Legacy scenes retain their default transport timings. UI-aware acceptance searches still issue virtual mouse device events through the game's input path; they do not count direct method calls as mouse-play acceptance.

## Acceptance evidence

Final results and measurement limitations are recorded in `acceptance.md`, `final_scene_audit.json`, `player_build.txt` and `Standalone/`. `performance_baseline.md` and `static_scene_audit.json` intentionally retain the pre-environment baseline. Earlier failed Unity runs are not final acceptance results.

Archive recovery is recorded in `archive_verified.md`. The original reference survey is `scene_archive_audit.md`; the final cleanup disposition is `cleanup.md`. Test fixtures and shared resources remain local even when they are absent from the two-scene release build.

## Boundaries

- No automatic main merge, original-workspace cleanup, Tripo upload or credit spend.
- Automated virtual-mouse tests are not a human playthrough.
- Geometry/sample-frame collision checks are not continuous collision or VR-hand validation.
- Standalone performance on this PC is not a target-device or VR guarantee.
- Final lighting, clutter density, readability and narrative presentation still need human art/play sign-off.
