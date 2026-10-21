# Soft Japanese 3D look — 2026-10-09

Applied to the active `feat/clinic-carry-to-bench` workspace, based on `b5b7228`.

The target is the smooth, textured 2000s Japanese game look in the user's references: continuous shading, restrained specular highlights, muted cool shadows and warm task lights. This pass styles the existing clinic environment. It does not remodel characters or replace the authored robot and repair geometry.

## Editor controls

- **Border Repair > Unified Clinic > Retro Look > Apply Soft Japanese 3D Look** applies or reapplies the scene setup.
- **Restore Previous Look** restores the serialized original materials, light colors/intensities, ambient colors and camera settings. Generated resources remain available for reuse.
- **Build Preview Player** builds `Builds/UnifiedClinic-20261009-RetroLook/BorderRepairClinic.exe`.

Resources live in `Assets/BorderRepair/Art/UnifiedClinic/RetroLook`:

- `Room_Profile.asset`: neutral tonemapping, +0.35 exposure, contrast 5, saturation -9, cool shadow grading, bloom 0.24, fine grain 0.028, vignette 0.13.
- `Inspection_Profile.asset`: +0.25 exposure, contrast 3, saturation -5, bloom 0.10, grain 0.008, vignette 0.07.
- Diffusion strength/radius are editable on the scene cameras' `RetroClinicCamera` components (room 0.16; inspection 0.025; radius 1.3 pixels).
- `Materials/` and `Textures/` contain scene-specific variants. World texture variants use bilinear filtering, mipmaps and anisotropy 2. Label, paper, PCB and screen materials keep their original base textures.
- `Renderers/` contains clones of both PC and mobile renderer data. Their original default indices are unchanged; only marked clinic cameras select the new renderers.

The diffusion feature derives from URP's native Full Screen Pass feature, supporting its compatibility and RenderGraph paths. It requests only the color copy and uses a normalized nine-tap kernel before native post-processing. Overlay, reflection and unmarked cameras do not receive this pass. A runtime material instance prevents camera adjustments from modifying the asset.

Room and inspection volumes occupy otherwise unassigned layers 30 and 31; a camera selects one look in addition to its original volume mask. Inspection cameras always select the clear profile. The room camera also switches to the clear profile during non-walking repair views, including the robot flow.

No depth of field, motion blur, chromatic aberration, vertex jitter, affine texture distortion or fixed low-resolution output is enabled. Native UI remains at output resolution. Diagnostic shaders, scan colors, gameplay code, source models and original texture/material assets are retained.

The shared-clinic scene builder now reapplies the look after environment/lighting integration. Existing profile edits are kept on rebuild. The builder's rollback snapshot includes the generated look resources and modified pipeline asset lists.

## Validation

See `validation.md` and `player_build.txt` for evidence: 2/2 look-specific EditMode tests, 22/22 clinic PlayMode regressions, 1/1 runtime capture and 2/2 separate exported Player acceptance processes passed. Windows x64 build succeeded with 0 build errors and 0 build warnings. Automated input/captures are not human playtesting. Validation uses a stable isolated workspace so concurrent gameplay changes in the main workspace are not overwritten.
