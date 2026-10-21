# Cosmetics Prototype

User approved decals, whole-shell image replacement and spray, with play stopped/saved.

Scope: independent D:/uetounity/UnityProjects/BorderRepairStation_repairprops only.
Main project, model sources, imported meshes/textures, scenes and build settings stay untouched.

Installed and native-verified prototype. Shared mouse/VR per-item cosmetics layer with exact renderer/slot allowlist.
Original base texture resolution, normal/metallic/occlusion maps retained; only a private 512-square paint layer is allocated per edited slot.
GPU world-space stamps bake onto the selected mesh UVs; not a renderer-feature/global-pipeline change.
Input ownership suppresses repair/scanning during cosmetics. Off restores prior scanner and inspector state.
Optional cosmetics requires the item mounted, assembled, switched off and test-fixture disconnected.
No gameplay reward/save schema changes. Layers live on the current order instance, not persistent across scene reload.

## Verified 2026-10-18

- Native Unity compile and final source/shader audit succeeded, 11 exact exterior slots. Original receiver base maps remain4096 and meshes remain non-readable. No CPU mesh readback enabled.
- Related EditMode54/54 and PlayMode17/17 passed, no skips. PlayMode includes original4 tests,4 VR simulations,5 synthetic GPU tests and4 desktop cosmetics scene tests.
- Real GPU alpha/readback validates brush locality, transparent image alpha/180-degree decal rotation, material/normal/source identity, undo/redo/clear and whole-shell replacement.
- Authored desktop scene tests exercise queued Input System mouse events, real graphic UI blocking, release-to-resume, room occlusion, safety gates and per-order history isolation across all4 customer props.
- Virtual tracked-controller trigger events drive the actual VR session Update. XRI controller pose drives its stabilized ray origin, paints the imported shell, leaves repair history unchanged; tracking loss exits cosmetics. Not physical headset proof.
- Desktop offscreen diagnostic image: Evidence/desktop-panel-preview.png. Original overlay HUD intentionally not included in that capture; this is not a human desktop screenshot.
- VR image: Evidence/vr-cosmetic-simulation.png. Monoscopic editor simulation only. Panel text, physical reach, stereo, tracking comfort and frame time still need HMD review; upper panels can be outside the downward-looking capture.
- Actual catalog path: C:/Users/Administrator/AppData/LocalLow/DefaultCompany/BorderRepairStation/CosmeticImages.
- Generated2 shader feature combinations for runtime cloning in a module-local Resources ShaderVariantCollection. No global graphics/URP setting changed. No Player build verified.
- Source desktop scene SHA256 remains2762B06703944C1D9E21716B277D6FF6E18D298706B5A666538274249A9B1722; VR scene remains3C75528859CF169FAB553F6F7FAFBD988864FFAE2AE48AC061EF1262DC7B8662; EditorBuildSettings remainsEF4BB03EB7C932EA54EFEFEBEEEDBBDC9AFC328FFDD04458DFA1662285BAB1D8.

Initial native failures were retained in the VR work folder logs/XML. Fixed material clone texture binding, collapsed inactive status ScrollRect lookup; corrected test fixtures to ignore trigger volumes and await Canvas layout. Corrected VR test to move the input controller, not XRI's stabilized child transform.

Not a full-project regression: previous9 unrelated full-project EditMode failures are not reclassified as fixed. No merge, commit or push.

Limits: current order's cosmetics live only for this Play/scene lifetime; no automatic paint persistence in the game's save. Input/raw models/shared maps/main project unchanged. Art/human acceptance and headset/VR performance remain unverified.
