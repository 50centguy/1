# Plan

## Plan v1
Status: awaiting lock
Source & version: G-0002; 3640f11 plus working tree observed 2026-10-22. Existing v1 was an empty, unlocked template.
Goals & boundaries: Review Claude's recent changes and maintain the isolated repairprops project. Preserve unrelated edits and player data. Do not interrupt active Unity/Claude writers. Reports in Chinese.
【Basic features】 Identify reproducible defects in repair, shop, navigation and new assistant/cord behavior; apply focused maintenance where ownership is clear; provide findings and verified results.
【Detail / advanced features】 Required: check UI input/occlusion, economy/save lifecycle, scene/resource cleanup and test credibility. Required: distinguish static proof, actual tests and visual acceptance. Optional: broader architectural refactors, excluded unless needed for a confirmed defect.
Acceptance: findings cite concrete paths/lines and triggers; fixes have focused regression evidence; unresolved runtime or ownership blocks remain explicit.
Slices: S1 | read-only audit and change ownership | none | coordinator and independent jurors | reproducible findings and maintenance scope; S2 | focused fixes | S1 and no competing writer | coordinator | affected tests and diffs; S3 | independent recheck | S2 | jurors | evidence and open-item disposition.
Revisions: 2026-10-22 populated previously empty plan. Active editor and Claude process observed; no claim that either is idle.
