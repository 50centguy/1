# Full-project regression interpretation

Full-project results are separate from scoped shared-clinic acceptance. No old test was skipped, deleted or weakened to claim a green full suite.

The independently restored archive at `8ddf418074afeb62a112dff377abfe5c43642f4e` completed full EditMode on 2026-10-08T07:50:08Z: actual exit 2, 118 total, 109 passed, 9 failed. The nine failure names and assertion messages exactly matched the integration run before the two additional importer-safety tests. See `baseline_full_edit.xml` and `baseline_full_edit.log`.

| Failures | Meaning |
| --- | --- |
| 3 `SourceArt_NotModified` | Legacy tests expect RobotV4 MD5 `80b94f89c3dfbd62e03214e7487e5d96`. The user's inherited current source and recovered archive both use `b47c9c43006cbef29e820d022d37560b`. The unification did not change that model. |
| 3 `DropZones_SitOnRealBenchObjects_InClearSpace` | Legacy fixture cover placement is about 11.447 mm above the mat. |
| 3 `BenchPlacements_DoNotPenetrate` | Same legacy fixture placement fails its surface-contact check; it does not report penetration. |

Robot FBX SHA-256 in both archive and integration:

`0C1E967288B64DB9339808502B43B8046E4125A5A475BB0EB75D774CC87CAFB3`

The corresponding legacy test source, three fixture scenes and Robot importer metadata also match the archive. This isolates the failures as inherited behavior, but does not prove that FBX changes alone caused the placement mismatch. They remain real unresolved legacy issues, not accepted passes. Source geometry was not reverted and fixture landing positions were not silently changed.

Final integration `UnifiedClinicEditFinalCode.xml` has 153 total / 144 passed / the same nine failed, with actual process exit 2. Full PlayMode has 111 passed / 0 failed / 7 original opt-in screenshot skips; the final shared-only HUD correction subsequently passes 11/11 shared flow tests and two independent native processes. XML/logs are delivered alongside this baseline. Use `acceptance.md` for scope and limits. Shared-room motion checks do not imply that these legacy mat-contact assertions passed.
