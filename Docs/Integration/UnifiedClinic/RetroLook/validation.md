# Retro look validation — 2026-10-09

Validated in `clinic-retro-look` on branch `art/retro-look-20261009`, based on `b5b7228`. The art integration was also applied to `clinic-bench`. The other chat's concurrent whole-engine gameplay changes were not overwritten and are outside this validation baseline.

Unity 6000.0.84f1; URP 17.0.4; Direct3D 11 on NVIDIA GeForce RTX 3070. Tests use a real graphics device (not `-nographics`).

## Completed checks

- Runtime camera capture: 1/1 passed; seven first-person room views plus the on-bench repair view, 1280 × 720.
- EditMode: 2/2 passed. Reapplication creates no material-variant chains or duplicate look roots; original material files and renderer/light bindings restore correctly; pipeline default renderer indices remain unchanged; the inspection profile is sharper and the diffusion shader has no compile errors.
- PlayMode: 22/22 passed across `UnifiedClinicPlayTests`, `UnifiedClinicInspectionTests` and `UnifiedClinicFirstPersonTests`. Covers intake/carry-to-bench/repair/delivery, inspection mouse input, checkpoint reentry, walking and physical click routes.
- Reviewed entrance and repair screenshots. No depth of field, motion blur or chromatic aberration is active. PCB/screen/label source textures are retained. Fine grain and diffusion are greatly reduced while repairing.
- Windows x64 Development Player build succeeded: 0 build errors, 0 build warnings. The hidden diffusion shader was compiled into the Player.
- Exported Player acceptance: 2/2 separate processes passed, with an isolated save directory. Phase 1 covered actual virtual-device input, carry-to-bench, visible inspection/scan and two deliveries (11 clicks, cash 800, two receipts). Phase 2 continued the disk checkpoint, verified first-person input and the Night 2 repair/retest (25 clicks); cash and receipt count stayed at 800/two. Neither process used the player's existing save.
- Reviewed the native Player room image and robot dock image as well; the robot's animated green screen remains visible and its material emission is preserved.

These are automated tests and offscreen captures, not human playtesting or a mobile-device performance certification. Both PC/mobile renderer assets are installed; only the current Windows PC graphics configuration is covered by these captures.

## Image measurements

Luminance is measured from encoded screenshots, not from scene-linear lighting or a perceptual-quality score.

| View | Mean before → after | Near-black pixels before → after |
| --- | --- | --- |
| Entrance | 0.101 → 0.134 | 27.9% → 17.8% |
| Surgery | 0.105 → 0.144 | 26.1% → 13.3% |
| Dock | 0.148 → 0.181 | 33.3% → 18.6% |
| Workbench | 0.150 → 0.196 | 26.0% → 13.7% |
| Trade | 0.169 → 0.215 | 28.7% → 12.4% |
| Back left | 0.092 → 0.126 | 24.5% → 11.2% |
| Back right | 0.112 → 0.151 | 25.4% → 16.5% |
| Bench repair | 0.319 → 0.376 | 0.1% → 0.0% |

Pixels above luminance 0.97 were 0.0% in all eight before/after views.

## Evidence

Test XML files and logs are exported with the user-facing preview under `outputs/retro-look/validation`. Screenshots are in `outputs/retro-look/screenshots`. `player-check/checkpoint_phase1.json` and `checkpoint_phase2.json` record the native acceptance results. See `player_build.txt` for the separate Player build result.
