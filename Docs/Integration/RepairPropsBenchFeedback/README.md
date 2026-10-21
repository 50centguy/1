# Repair Prop Bench Feedback

Target: `D:/uetounity/UnityProjects/BorderRepairStation_repairprops` only. Main clinic-bench, original art, both authored scenes and build settings are not edited.

## Changes

- ~~The four repair props opt into camera-orbit inspection.~~ 2026-10-20: reverted at the user's request. Dragging now lifts the item off the bench and turns it in the air. The camera does not move; only scrolling zooms it. The hover height is the item's farthest assembled mesh corner plus 2 cm, so any orientation clears the tabletop. R or reset puts it back down. The pivot is the centre of the assembled visible meshes captured on the bench. Mount-time centring also counts hidden alternate-state meshes, which put the old pivot off-centre. `SetBenchOrbit`/`OrbitMountedItem` were renamed `SetBenchMounted`/`BenchMounted`. The bench seating now uses the resting pose's assembled support, so lifting or turning never shifts the stage or camera. Not yet re-run in Unity.
- Capture a stable assembled support envelope before service. Desktop and VR seating use it instead of recalculating support from whichever moving/removed renderers happen to be visible.
- Canonical RepairPropFlow state, original local transforms and RepairPart snap records remain synchronous. Runtime mesh-only proxies provide the visible unthread/lift/table-placement and return animation. They reuse the original meshes and materials, with no writes to imported assets.
- Removed covers, modules and fasteners receive separate tabletop footprints. Proxies stay outside the inspected model hierarchy, so they cannot inflate its bounds or follow its rotation. The original floating copy's renderers and colliders are suppressed while its proxy is visible.
- Parked meshes have picking colliders mapped to their original flow target. Desktop and VR picking still route through the existing command safety checks. Flying meshes are not selectable.
- Table layout activates only after actual receipt and placement (Inspect stage), not during the initial intake preview. Proxies are destroyed with their owning item and hidden when it is disabled.
- Slot selection checks the physical desk fixtures and default-camera visibility, tries two flat orientations, and can widen the initial overview once. It does not change the inspection center or reframe continuously during service.

## Boundaries

This is an animated repair presentation, not a rigidbody/free-hand tool simulation. Logical operations still commit synchronously; quick successive valid operations can animate together. Original retained cores, diagnosis and electrical isolation gates are unchanged. VR physical-headset acceptance remains separate from simulated tests. Parking checks the static colliders and camera line of sight in this authored desk; the flight animation itself is not a physics/collision simulation.

## Verification

The final PlayMode run `feedback-verified-rotation` passed 23/23, zero skipped. All four queue items fit their parking slots without fallback warnings. The new tests run the authored scene, check intermediate screw motion, table contact, stable inspection center during rotate/zoom, return animation, real parking colliders and destruction cleanup. A real camera ray must pick the parked rear cover instead of the bench fixture. Existing four-order completion, UI, cosmetic and VR simulation tests remain enabled. The final-code EditMode run `feedback-edit-delivery` passed 54/54, zero skipped. Result copies are `Evidence/play-final.xml` and `Evidence/edit-final.xml`.

Camera-render evidence is in `Evidence`; it does not include overlay UI and is not a human acceptance session.

Initial compile failure (missing explicit TwoNight assembly reference) and first PlayMode regression (premature presentation during initial intake) are retained in their logs, not overwritten. Final results must be read independently of these earlier attempts.

Additional failed iterations exposed a carry-pose world-AABB inflation, fixture occlusion, and the large Omamori cover requiring a different flat orientation. They were fixed without dropping the assertions. The final PlayMode log still contains the previously known XR Project Validation window/InputSettings-null exception during editor cleanup after test result output; tests passing is not a claim of an error-free editor log. No physical-headset acceptance or Player build was performed.

Both authored scene hashes and EditorBuildSettings match the saved baseline. No scene regeneration, main-project edits, commit, merge or push was performed. `Source` is a delivery snapshot, not an automatic installer; preserve newer work before reusing it.
