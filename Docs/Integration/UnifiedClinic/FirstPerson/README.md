# First-person integration follow-up

The existing implementation from feat/first-person-move (5028a74) is migrated into the unified clinic. Old scenes are not merged over the new room. The source branch's later RobotV4 regeneration/label changes are outside this movement migration.

## Controls and scene scope

- Both nights start with the serialized ClinicFirstPersonPlayer at the entrance, facing into the room.
- WASD/arrow keys move, mouse delta turns, Shift moves faster, C toggles the lowered eye position (Ctrl held also lowers it). This is not a full capsule-height crouch or VR body system.
- Locked crosshair: left click operates a target within 1.3 m; right click observes under the existing observation rules.
- Tab/Esc releases the cursor for UI. A non-UI click relocks it without operating an object.
- Fixed views are retained for small-part repair. A movement key or the Back/Walk button returns to the player's existing position. Flow progression no longer automatically pulls a walking player into another view.
- The diagnostic console and customer dialogue/ledger/retry panels pause first-night movement. The repair manual, registration/night-end panels and scripted tray incident pause internal-repair movement.
- Authored room collision proxies, bench/dock/robot colliders and a closed-door threshold block the controller. Old room coordinates and obsolete invisible boundaries are not copied.
- Trade picking ignores the player's controller and movement-only blockers, skips unrelated nonblocking triggers, and still stops at solid occluders. Distant trade clicks produce a refusal rather than receiving/delivering.

## Verification

Final focused PlayMode run: 19 tests, 19 passed, 0 failed, actual Unity exit 0. Includes seven new first-person tests plus eleven shared-clinic regressions and the existing night2 UI regression. Input System virtual keyboard/mouse events drive runtime movement; programmatic aiming and a few initial blocker/reach positions are declared test setup, not human play.

The seven new checks cover default walking in both nights, keyboard movement and mouse yaw, manual/console pauses, fixed-view return without body teleport, distant refusal followed by a nearby actual mouse receive, a connected keyboard route entrance-to-trade-to-bench-to-dock around surgery, wall/bed/dock collision and a connected walk behind the robot with an actual crosshair click releasing the rear latch. A synthetic 0.5-second frame also verifies the movement step stays bounded. See PlayMode.xml/log and input_and_routes.json.

First-night early initialization originally selected a fixed view before Walker.Awake; selection is now deferred until actors are ready. A malformed new test GUID initially caused Unity to ignore that test file; the final run explicitly includes all seven tests. The distant trade test derives capsule-clear visible positions instead of assuming a point inside the surgical bed is standable. Reach and occlusion assertions remain intact. An initial native check exposed a long-frame movement jump after screenshot rendering; movement/gravity/eye-height use a maximum 0.05-second time step, and the native input check warms up neutral frames before pressing keys. The failed initial native report remains under History/.

Updated Windows Development player: build succeeded with 0 build errors/warnings (211621965 bytes), actual editor exit 0. Two separate native processes exited 0: phase1 PID16144 and phase2 PID17484. Both report firstPersonPassed=true, keyboard displacement about 0.491 m and mouse yaw 10.8 degrees. Phase1 receives/delivers and saves; phase2 continues that disk checkpoint and completes the internal repair/retest. Cash remains 800 with 2 receipts. The checkpoint bytes stay unchanged during second-night repair.

Native/ contains reports, real camera PNGs with UI and logs. These are not desktop captures. The complete repair path uses the retained fixed close-up virtual-mouse acceptance driver; this is not a claim that every repair step was walked in first person. The first-person tests independently cover room routes, trade clicking and the rear latch. The exported executable is local under Builds/UnifiedClinic-20261008, not committed to Git.

Prior shared-room performance measurements and 9 inherited EditMode failures are not erased. Full project suites were not rerun in this movement-only follow-up. Human mouse play, final art, headset VR and target-device performance remain unverified. The original 34-file authored snapshot hashes and both original workspace HEADs remain unchanged; no RobotV4 source model or old authored scene is replaced.
