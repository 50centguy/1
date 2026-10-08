# Pre-unification recovery checkpoint

This branch preserves the complete project tree at integ/two-night-slice commit 088fac4e43f662f6bf09a31102fd0cb992340607, plus the current 34-file tracked working snapshot from BorderRepairStation_slice. No source worktree was reset, cleaned or committed by this operation.

The exact input SHA-256 manifest is source_snapshot.json. It includes current second-night and first-night scenes, their meta files and shared resources. Keeping the entire dependency tree in Git avoids archiving scenes without their assets.

Recovery: fetch archive/clinic-preunify-20261008 from origin, then create a separate short-path worktree on that ref with git -c core.longpaths=true worktree add. Open that project in Unity 6000.0.84f1. Do not restore by overwriting a dirty active project. The user approved the existing GitHub repository as the archive destination.

This is a recovery archive, not a claim that new unified gameplay or art has passed acceptance. Local scene deletion is a separate operation, gated on remote restore verification, dependency review and validation of the replacement scene. Prototype/regression scenes referenced by tests are not assumed disposable.
