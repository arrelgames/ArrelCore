# GI Rebuild Validation Checklist

Use this checklist after changing GridProps at runtime to verify incremental GI rebuild behavior.

## Correctness checks

1. Delete an occluding `GridProp` and confirm nearby GI brightens within 1-2 propagation ticks.
2. Add an occluding `GridProp` and confirm nearby GI dims/occludes within 1-2 propagation ticks.
3. Delete a surface-creating prop and confirm GI node coverage updates (no stale bright texels).
4. Add a surface-creating prop and confirm GI appears on the newly valid walkable area.
5. Move/rotate a passage-blocking solid and confirm GI leakage no longer appears on blocked edges.
6. Perform bulk edits (20+ dirty tiles) and confirm fallback full rebuild keeps output stable.

## Stability checks

1. Confirm there are no exceptions during repeated add/remove cycles.
2. Confirm no out-of-range texture writes when dirty edits happen near map boundaries.
3. Confirm `_GiVolumeParams` and `_GiVolumeSize` remain valid after extent changes.

## Performance checks

1. Profile a single-tile edit and record GI rebuild cost in CPU timeline.
2. Profile a medium burst edit (8-16 tiles) and compare with single-tile cost.
3. Profile a large burst edit (25+ tiles) to verify fallback full rebuild behavior.
4. Compare average frame time over 10s before edits vs during repeated edits.

## Resolution multiplier checks

1. Set `Gi Resolution Multiplier` to `1` and capture a baseline screenshot in play mode.
2. Set `Gi Resolution Multiplier` to `2` with same source/material settings and compare hotspot sharpness.
3. Verify moving `GiSource` still updates smoothly with multiplier `2`.
4. Verify runtime `GridProp` add/remove still updates without stale texels at multiplier `2`.
5. Compare frame-time impact between multiplier `1` and `2` for the same scene.

## Light type checks

1. Set `GiSource` to `Point` and confirm output remains visually compatible with previous behavior.
2. Set `GiSource` to `Spot`; verify cone edge, inner/outer angle falloff, and occlusion clipping behind walls.
3. Set `GiSource` to `Rect`; compare `Rect Samples X/Y` values (`1x1`, `2x2`, `4x4`) for quality vs CPU cost.
4. Set `GiSource` to `Directional`; verify bounded range (`Directional Max Distance`) and no full-scene spikes.
5. Increase source count per type (`1`, `8`, `32`) and record HUD `CPU` and `AVG CPU` deltas.
6. Confirm no obvious leaks through thick occluders across all light types with `Respect Occlusion` enabled.

## Camera-follow window checks

1. Enable camera-follow GI window and set a bounded window (example `48x48`) around the active camera.
2. Walk `1`, `4`, and `8+` tiles and verify GI remains stable near the player with no obvious swimming.
3. Verify entering-strip updates do not leave stale texels at window edges after repeated movement.
4. Compare HUD `CPU` and `AVG CPU` while stationary vs continuous movement.
5. Force a large jump/teleport and verify controlled full refresh path (no persistent black/garbled volume).
6. Disable camera-follow window and confirm behavior matches full-extent baseline.

## Performance tier validation (Tiers 1-6)

1. Confirm source collection no longer uses per-tick scene scans by checking profiler call tree for `FindObjectsOfType<GiSource>`.
2. Confirm material sync does not scan all renderers every frame in steady state.
3. In steady-state play mode, verify GC alloc for GI tick path is near `0 B/frame`.
4. Compare CPU/frame metrics for source counts `1`, `8`, `32`, and `64` before and after enabling source dirty-tracking.
5. Validate propagation parity with `Use GI Jobs Burst` disabled vs enabled (same scene, same source settings).
6. Stress-test camera movement and runtime topology edits together and verify no persistent stale strips or lighting pops.
7. Capture 10s HUD averages for baseline and optimized mode and log `FPS`, `CPU`, and `AVG CPU`.

## Indirect bounce (multi-bounce feedback) checks

1. With `Enable Bounce Feedback` off, baseline matches prior behavior (no extra spill vs last known good).
2. Enable `Enable Bounce Feedback`, set `Bounce Albedo` low (e.g. `0.1`–`0.2`), run 60+ seconds with bright sources — no runaway brightness (tune `Damping` / `Bounce Albedo` / optional `Max Bounce`).
3. With `Use Source Dirty Tracking` on and static sources, confirm indirect light still evolves over several propagation ticks (bounce updates every tick).
4. Toggle `Enable Bounce Feedback` off during play — confirm `ClearBounceSources` path (indirect energy drops; no exceptions).
5. Call `Force Full Gi Rebuild` (or trigger full grid rebuild) with bounce on — no stale bounce or exceptions; volume stabilizes.
6. With `Use GI Jobs Burst` on and `Enable Bounce Feedback` on, confirm propagation uses the main-thread neighbor path (profiler: no `PropagationJob` schedule for the propagation step, or parity with burst-off for the same scene).
7. Compare `Use Neighbor Average For Bounce` on vs off for the same `Bounce Albedo` — smoother spill vs slightly higher CPU; no NaNs or black volume.

## Pass criteria

- No stale lighting artifacts after topology edits.
- No runtime errors during prop add/remove/move operations.
- Incremental path cheaper than full rebuild for small edits.
- Full rebuild fallback remains stable for large edit bursts.
- Point/Spot/Rect/Directional run without exceptions and show bounded runtime behavior.
- Camera-follow window tracks camera tile movement and remains visually stable under normal motion.
- Tiered performance path improves CPU cost while preserving expected GI behavior.
- Indirect bounce remains stable, clears when disabled or on full rebuild, and does not use the Burst propagation job while enabled (neighbor diffusion required).
