# Final local scene disposition

## Removed, integration worktree only

The following pair was removed on 2026-10-08 after shared-room acceptance (65 scoped EditMode / 20 scoped PlayMode passes), native archive import/dependency recovery, and a fresh code/path/GUID reference search:

- `Assets/Scenes/SampleScene.unity`
- `Assets/Scenes/SampleScene.unity.meta`

Before removal, each absolute target was checked to remain under `C:/Users/Administrator/Documents/Codex/clinic-int/`. Each file's raw SHA-256 matched the independently restored remote archive copy:

| File | SHA-256 |
| --- | --- |
| SampleScene.unity | 4B8D685638A7DD12961A78647DC661FF1963363F10FBEA53056B5B258063355B |
| SampleScene.unity.meta | BAF1A15DF993D52223CE52DAA6BF340FD183FB7E5C3C173ED76FD7F614D6E7F0 |

Only the general build-list and project-template references remained. The build entry was removed and `templateDefaultScene` now points to `UnifiedClinic_Menu.unity`. `SampleSceneProfile.asset` and all shared render resources remain. No recursive directory deletion was used.

## Retained

All other legacy scenes in `scene_archive_audit.md` remain as test fixtures, builder inputs, model acceptance scenes or unknown-purpose assets. In particular, the current second-night source scene is needed for regeneration; deleting it would make the shared-scene builder non-reproducible. The unknown InitTest scene is retained. Existing regression tests were not removed to enable cleanup.

The general Editor build list retains legacy regression fixtures, but `ClinicPlayerBuilder` explicitly packages only the shared menu and shared clinic. Thus these old fixtures are not playable release rooms. The original Unity workspaces were not cleaned.

## Cloud recovery

Remote branch `archive/clinic-preunify-20261008`, commit `8ddf418074afeb62a112dff377abfe5c43642f4e`, contains the removed pair and the pre-unification source snapshot. `archive_verified.md` records the independent clone, Git integrity and native Unity dependency checks. Restore into a new directory first; copy scene and matching meta together only after checking the destination. Do not reset a dirty working tree.

This final disposition supersedes the pending/conditional wording in the earlier read-only survey. It is deliberately narrower than deleting every old scene: functional dependencies and user-owned work stay protected.
