# Static baseline before environment replacement

Input: the saved UnifiedClinic scene built from the user's current second-night snapshot. See static_scene_audit.json for the actual renderer paths and Unity dependency list. This is a saved-scene inventory; dynamically spawned customer items and runtime-only feedback are not included.

| Metric | Value |
| --- | ---: |
| All renderer instances | 431 |
| Enabled and active renderer instances | 417 |
| All instance triangles | 258875 |
| Enabled and active instance triangles | 256259 |
| Unique mesh triangles | 258771 |
| Unique meshes | 425 |
| Unique materials | 44 |
| Referenced textures | 33 |
| Unity estimated texture runtime bytes | 46948419 |
| Missing scripts | 0 |

The current robot root contains 241 renderers and 235849 instance triangles including its attached fault/feedback geometry. This is not the historical clean V4 export count of 202233 triangles and 234 meshes. The source project has since changed; use the current measured instance inventory rather than substituting the old asset-package numbers.

## Interpretation

Renderer count is not draw-call count: culling, submesh/material slots, batching, shadow/depth passes and stereo rendering can change submission costs. Static triangle totals are also not the number processed per frame. The new room has not yet been included in these figures.

No mesh decimation, Read/Write change, collider simplification or material regrouping was performed to obtain this report. Preserve repair silhouettes and independent interaction ownership. Measure before changing them.

Before performance sign-off, profile a standalone build on target hardware in both night phases, with the robot active, during object transport and under the surgical gantry. Record CPU main/render thread, GPU, GC allocations, camera-visible renderers and draw calls. Compare the same camera and lighting with environment clutter toggled. VR needs headset-specific stereo rendering, render resolution and sustained frame-time measurements; editor results cannot establish headset suitability.

This report contains no measured frame rate, no GPU-budget guarantee, and no VR acceptance claim.
