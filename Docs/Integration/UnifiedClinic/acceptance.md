# Unified clinic acceptance

## Implemented scope

Real Blender room/clutter sources are imported around the current RobotV4, workbench and service dock. The south entrance, north workbench, central surgery bed/hanging-tool ring, west robot zone and east V4-style trading area share one runtime room for both nights. The functional diagnostic console remains. Trade pads receive/deliver actual existing item prefabs; income posts exactly once only after physical delivery. Unfinished deliveries are rejected; authored unpaid returns and retry/next-case queue behavior have tests.

The first-night authored shift still has one collector case. Subsequent authored queue entries are supported and tested; no extra customer or story was fabricated. Night2 remains the existing UNIT07 prototype repair/retest, not a new formal work-order system or physics flight simulation. Its completion now displays repaired/retest-passed state without charging customer income. The saved checkpoint remains the night-end Night1 safe state; mid-Night2 repair progress is not newly persisted.

## Verified results

| Check | Result / evidence |
| --- | --- |
| Scoped integration EditMode | 65/65 at the initial complete runtime pass; later added importer recovery tests are covered by full EditMode |
| Scoped integration PlayMode | 20/20, `Regression/UnifiedClinicFinalPlay.xml` |
| Full project PlayMode | 111 passed, 0 failed, 7 original opt-in screenshot tests skipped, `Regression/UnifiedClinicFullProjectPlay.xml` |
| Final shared UI/flow PlayMode | 11/11 after completed-HUD fix, `Regression/UnifiedClinicFinalHudPlay.xml` |
| Full EditMode | Final code: 153 total, 144 passed, 9 inherited failures; actual exit 2, `Regression/UnifiedClinicEditFinalCode.xml`. All five import/recovery safety cases pass; not a green full-suite claim |
| Whole-room motion | 161 real environment meshes/colliders, 2187 sampled frames / 1422 travel frames; retest passes; zero recorded cooking errors, bearing-visibility errors, jumps, travel penetrations, UI-blocked clicks or click failures |
| Motion extrema | 58.34 mm step / 2.12 degree turn at 1/60 s sampling; exact updated JSON in `Regression/UnifiedClinicIntegratedMotion.json` |
| Native build | Actual process exit 0, 0 build errors/warnings; 211,600,841 bytes; `player_build.txt` |
| Separate native process 1 | PID 38712, Night1 receive/deliver/dock/power-off/checkpoint passes, 5 virtual-mouse clicks, cash 800 / 2 receipts |
| Separate native process 2 | PID 32172, disk Continue and full Night2 repair/retest passes, 25 virtual-mouse clicks; cash 800 / 2 receipts and checkpoint bytes unchanged; completed HUD/caption checked |
| Native graphics | Actual camera-rendered PNGs pass nonblank guards; final room/Continue/retest images are in `Standalone/CheckpointFinal/` |
| Standalone performance | Three 1920 x 1080 native offscreen views measured; GPU timing unavailable, explicitly not a 0 ms result; see `performance.md` |
| Archive / cleanup | Independent GitHub restore/import/dependencies verified; only matched SampleScene pair removed from integration; fixtures/shared resources retained |
| Original source protection | 34/34 raw source hashes and both original HEADs unchanged, `original_workspace_verification.json` |

The full PlayMode pass preceded the final shared-only HUD correction and opt-in capture changes. The final 11-case shared regression and separate-process acceptance exercise that correction. Normal legacy behavior is preserved behind the `unifiedClinic` flag. No ordinary test was disabled to obtain these totals.

Whole-room collision monitoring checks moving convex geometry against static native mesh colliders at sampled frames. It is not continuous swept collision or contact/edge precision certification. AABB-only observations are separately retained in the JSON; they are not promoted to triangle penetrations. Imported unreadable meshes remain unreadable. The additional exact environment colliders are verification-only, not silently added to normal gameplay.

## Known limits / inherited failures

The recovered archive reproduces nine legacy EditMode failures exactly: three obsolete Robot FBX hash expectations and six mat-contact assertions for cover placement about 11.447 mm above the mat in three legacy fixtures. They are not accepted passes and were not hidden by changing tests or reverting the user's newer model. See baseline XML and `Regression/README.md`.

Native mouse acceptance uses input-system virtual mouse events for 3D actions. Menu/register and some diagnostic domain setup use programmatic APIs, not human hand operation. There was no human mouse playthrough or real VR headset test. Final clutter density, lamp strength, very dark areas, surface style and label readability need human sign-off. The new room is an editable first implementation, not final art approval.

Earlier failed integration runs and black hidden-backbuffer screenshots are historical evidence only. `Standalone/History/BlackBackbuffer/` must not be used as visual approval; final acceptance uses guarded offscreen captures. Source Blender renders with placeholders are not substituted for the real playable RobotV4 screenshots.

## Delivery boundaries

The independent integration branch includes the inherited source snapshot separately from new changes. Claude art is pushed on `art/unified-clinic-20261008` through `4170a76`. Cloud archive is `archive/clinic-preunify-20261008` at `8ddf418`. No branch is automatically merged into main, no original dirty workspace is cleaned, and no Tripo task/reference upload/credit spend was performed.
