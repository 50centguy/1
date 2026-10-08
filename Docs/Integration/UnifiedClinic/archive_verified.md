# Verified cloud recovery checkpoint

- Remote: https://github.com/50centguy/1.git
- Archive branch: archive/clinic-preunify-20261008
- Commit: 8ddf418074afeb62a112dff377abfe5c43642f4e
- Independent remote clone: C:/Users/Administrator/Documents/Codex/clinic-restore
- Restored HEAD exactly matches the pushed archive commit.
- All 34 pre-unification modified source files match the archived Git blobs after checkout. Git's configured line-ending normalization applies to text files; the original raw SHA-256 manifest remains in the archive.
- git fsck --full completed successfully on the independent remote clone.
- Unity 6000.0.84f1 imported the independent recovery clone and ran the native dependency probe successfully (actual batch process return code 0). ArchiveAudit/unity_dependencies.json records all 16 restored scenes and their AssetDatabase dependencies, generated at 2026-10-08T01:55:33Z. This is a recovery/import check, not a Play-mode regression of every archived scene.

This verifies recoverability of the current scene and resource snapshot. It does not verify the replacement clinic or make test-referenced scenes safe to delete. Local deletion remains gated on dependency audit and replacement validation. No old scene has been deleted as part of this verification.

Recovery command (use a new empty short-path directory):

    git -c http.sslBackend=openssl -c core.longpaths=true clone --single-branch --branch archive/clinic-preunify-20261008 https://github.com/50centguy/1.git <new-directory>

The per-command OpenSSL backend avoids this machine's Schannel credential error; certificate verification remains enabled.
