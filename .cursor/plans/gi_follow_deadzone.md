# GI camera-follow square deadzone

## Decisions (final)

- **Shape:** Axis-aligned **square** using **Chebyshev** distance in tile space: `max(|Δx|, |Δz|) ≤ R`. Integer-only math (no `sqrt`) for performance.
- **Inspector ([GiManager.cs](Assets/RLCore/Scripts/Rendering/GI/GiManager.cs) → Camera Follow Window):**
  - **`useSquareFollowDeadzone`** — checkbox; when off, behavior matches the previous per-tile follow (window recenters whenever the anchor tile changes).
  - **`squareDeadzoneRadiusTiles`** — non-negative int; half-extent in **base tiles** from `giDeadzoneOriginTile`. Recenters only when `max(|Δx|, |Δz|) > R`.
- **State:** `giDeadzoneOriginTile` updated when the window is (re)centered: on successful `SetActiveWindowFromAnchor` after leaving the deadzone, and in `InitializeWindowBoundsIfNeeded` when deadzone is enabled.
- **Tuning:** Keep `squareDeadzoneRadiusTiles` comfortably below half of `min(windowWidthTiles, windowDepthTiles)` so the anchor stays inside the GI window before recenter.

## Status

Implemented in [Assets/RLCore/Scripts/Rendering/GI/GiManager.cs](Assets/RLCore/Scripts/Rendering/GI/GiManager.cs).
