# Measured performance and VR risk

## Current saved-scene inventory

`final_scene_audit.json` inventories the integrated shared room, without changing mesh Read/Write settings or decimating assets:

| Metric | Value |
| --- | ---: |
| All / active renderer instances | 589 / 552 |
| All / active instance triangles | 275,291 / 271,725 |
| Unique meshes / triangles | 585 / 275,211 |
| Unique materials / referenced textures | 63 / 43 |
| Editor estimated texture runtime bytes | 95,056,115 |
| Missing scripts | 0 |

The inherited current robot root has 241 renderers and 235,849 instance triangles including attached fault/feedback geometry. The historical clean V4 export was 202,233 triangles / 234 meshes. They are different inventories, not interchangeable figures. The model was not remeshed or reverted during unification. Saved active counts are not camera-visible counts and exclude dynamically spawned runtime objects/UI.

Texture runtime-size estimates are not measured GPU VRAM. Residency/import/cache conditions differ from the earlier baseline; do not attribute their difference solely to the new room.

## Native development-player measurement

Final report: `Standalone/PerformanceFinal/standalone_performance.json`, 2026-10-08T08:53:29Z. Hardware: i5-12400F / RTX 3070, Unity 6000.0.84f1, 1920 x 1080. Each view warms 120 frames and samples 240 frames. VSync/cap are disabled only in the opt-in probe. The startup manual is closed for the Night2 views.

The Windows process is hidden. A real native camera is explicitly rendered once per frame to an offscreen target, including temporarily camera-space UI. This avoids an invalid hidden-window backbuffer. PNGs must pass a nonblank check. Normal game cameras and canvases are restored; normal launches do not run this probe.

| View | Mean frame / P95 ms | CPU timing mean ms | Draw submissions | Submitted triangles | Unity allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Night1 awaiting receive | 1.381 / 1.755 | 1.380 | 887.29 | 570,090 | 117,990,628 |
| Night2 overview | 1.460 / 1.846 | 1.462 | 869.36 | 569,600 | 119,169,019 |
| Night2 engine close | 1.495 / 1.802 | 1.495 | 707.04 | 621,467 | 119,396,700 |

Draw/triangle counters were available. **GPU timing had zero valid samples in all three views**, recorded as -1, not 0 ms. These frame values describe this uncapped offscreen development run; they are not a displayed FPS guarantee or a complete onscreen CPU/GPU budget. Render submissions include shadow/depth/UI passes, not just unique model geometry. Unity allocated memory is not total process RAM or GPU VRAM.

## Risk and next profiling targets

The roughly 20万-triangle robot alone is not enough to judge readiness. The integrated scene's 552 active renderers, 63 materials and approximately 707-887 submissions are concrete areas to investigate, especially for stereo and lower-power devices. CPU timings on this desktop do not establish GPU headroom, interactive worst-case cost or sustained VR frame pacing.

Before reducing repair geometry, measure normal onscreen gameplay on target hardware: renderer/submesh submission cost, shadow passes, sealed-versus-open internal visibility, material grouping, allocations during transport, and both eye views under the surgery ring. Preserve independent removable parts and silhouettes. No blind decimation, LOD substitution, texture downgrade or renderer ownership merge was performed in this delivery.

Headset reach/readability, stereo resolution, VR interaction, motion comfort and sustained thermal/frame-time behavior remain unverified. No VR approval is claimed.
