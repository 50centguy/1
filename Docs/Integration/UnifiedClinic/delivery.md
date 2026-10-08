# Published delivery

Date: 2026-10-08. Independent integration is published; main is not merged.

## Version and location

- Worktree: C:/Users/Administrator/Documents/Codex/clinic-int
- Branch: integ/unified-clinic-20261008
- Inherited authored snapshot: a32cfc4, separately committed before the new implementation.
- Implementation: 43116353fc8dbf67f2d63dad3f6611ba37a929be, verified on origin. This delivery index is a documentation-only follow-up.
- Art: art/unified-clinic-20261008 at 4170a76a5607ce823cc89a86ddd6c65fef37c612; reproducible Blender sources are also included in integration.
- Archive: archive/clinic-preunify-20261008 at 8ddf418074afeb62a112dff377abfe5c43642f4e; independently cloned and Unity-imported before cleanup.
- Remote main remains 314ac8711c0b411351984987a7a1096611a821c8.
- Comparison: https://github.com/50centguy/1/compare/main...integ/unified-clinic-20261008

## Play locally

First-person follow-up: the current branch and rebuilt local executable now start both nights in walking mode. The existing 5028a74 controller is migrated without replacing the new room with old scenes. Focused tests pass 19/19; separate native processes verify walking, mouse-look, checkpoint continuation and full repair/retest. Detailed boundaries and controls are in FirstPerson/README.md. Main remains unmerged.

Run C:/Users/Administrator/Documents/Codex/clinic-int/Builds/UnifiedClinic-20261008/BorderRepairClinic.exe. The generated executable is a local build, not a committed binary; rebuild with ClinicPlayerBuilder.BuildBatch when checking out the branch elsewhere.

Alternatively, open this worktree as a Unity 6000.0.84f1 project and open Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity. Start Play, then New Game or Continue. Both nights use UnifiedClinic.unity. Receive and deliver through the trade counter; existing diagnostic console functionality is retained. First-night authored content contains one collector case, while the queue supports additional authored cases without inventing them.

## Evidence and boundaries

- Full PlayMode regression: 111 passed, 0 failed, 7 existing opt-in screenshot skips. Final shared HUD/flow regression: 11/11.
- Final full EditMode: 144 passed, 9 failed. An independent recovered pre-unification archive reproduces all nine failure names and messages. These are documented, not suppressed or claimed fixed.
- Native player build: 0 build errors/warnings. Separate processes passed first-night disk checkpoint continuation and the complete second-night repair/retest. Night2 still uses the existing prototype repair semantics; a mid-Night2 saved checkpoint is not newly implemented.
- Final movement checks: 2187 sampled frames, no monitored travel collisions, jumps, bearing visibility conflicts, UI-blocked clicks or collider cooking messages. This is not continuous swept collision or exact final support-contact certification.
- Final screenshots: Standalone/CheckpointFinal/. These are real nonblank camera renders with UI, not desktop screenshots. Human art/play sign-off remains pending.
- Performance: measured in a native Development build with one offscreen camera render per frame at 1920x1080 on RTX 3070. GPU timings were unavailable. Do not interpret these measurements as visible-window FPS, target-device or VR approval; see performance.md.
- Cleanup removed only Assets/Scenes/SampleScene.unity and its matching meta from integration. Other legacy test fixtures and shared resources remain local. No original workspace was cleaned.
- Original authored 34-file snapshot hashes and both original workspace HEADs remain unchanged. No automatic main merge, Tripo upload or credits spent.

Acceptance details: acceptance.md. Recovery and removal: archive_verified.md and cleanup.md. Rebuild and scene-editing precautions: README.md.
