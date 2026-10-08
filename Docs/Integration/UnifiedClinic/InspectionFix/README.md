# Customer inspection visibility fix

The reported giant mirrored CONSOLE surface was real foreground geometry, not a missing item asset. The inspection camera and rotating item anchor had been attached to the physical bench; the bench label occupied the close-up view. Earlier native customer acceptance called scan/diagnosis APIs directly and therefore missed this rendered/pointer usability failure.

## Changes

- Shared-scene generation places the existing item anchor and its camera under ClinicInspectionStage at (0, 1.2, 30), independently of the physical console interaction zone. This is a close-up inspection presentation within the same loaded scene, not a separate room or a new item asset.
- Original relative camera direction, item normalization, drag/zoom/reset and scanner bindings remain intact. A 5 m camera far plane excludes the clinic room; two local point lights illuminate the item against a solid dark background.
- Receive/deliver still move the actual item to their physical pads. Returning to the room during repair seats the same item on the actual bench top (measured from its renderer); reopening inspection restores its cached stage pose and preserves findings. Room movement and night2 robot handling are unchanged.
- Room-only trade status hides during inspection and stale reach refusal text clears on opening it. Return-to-clinic and main-menu buttons remain available.
- Native screenshots select the actual enabled camera rather than Camera.main (the inspection camera is intentionally Untagged).
- The native first-night probe now sends real Input System mouse press/release events to ScanButton, the required inspection point, DiagnoseButton, the authored diagnosis option and RepairButton. It no longer calls ScanPoint, TryBeginDiagnosis, SubmitDiagnosis or SubmitDecision for the customer case.

## Evidence

Native/ contains two independent final-build Windows Development player reports and real camera renders with UI, not desktop captures. Phase1 PID27980 exited 0, reports inspectionMousePassed=true and one required point scanned through pointer input. Phase2 PID6180 exited 0, continues the disk checkpoint and finishes internal robot repair/retest. Both report successful walking/mouse yaw; final cash is 800 with two receipts. The phase2 inspection flag is false because that phase repairs the robot, not the customer item. The earlier build's successful native run is retained under History/Native_before_final_build.

Final focused PlayMode: 22/22 passed, 0 failures/skips, actual Unity exit 0. Includes three new inspection tests, seven first-person tests, eleven shared-clinic regressions and the existing night2 UI check. The dedicated inspection tests compare actual rendered item pixels with item-hidden and item-only renders, reject foreground collider/text occlusion, and send virtual keyboard/mouse input for dragging, scrolling, Space, R, required-point scans, diagnosis buttons and return/reopen. Item contribution equals the isolated reference (received: 13315 pixels; dragged: 14262; zoomed: 18367). Room return verifies parent and support height, not only flow state. See PlayMode.xml/log.

The first test run had one drag-angle failure because a fixed number of accelerated editor frames did not let unscaled-time smoothing finish. The test now waits at least 0.6 real seconds as well as its frame minimum; drag/reset thresholds were not relaxed and no SnapToTarget shortcut was introduced. The initial failed run remains under History/.

Final build: 211631864 bytes, 0 build errors/warnings, actual editor exit 0. Latest executable: Builds/UnifiedClinic-20261008-InspectionFix/BorderRepairClinic.exe. The previous first-person delivery executable was not overwritten.

## Limits

Automated pointer input is not human play or headset VR. These tests cover the currently authored first-night communicator case, not every future item's hit regions. Earlier full-suite results and nine inherited EditMode failures remain historical; this fix does not claim those failures were repaired. No main merge, original-workspace edit, model regeneration or credit spending is included.
