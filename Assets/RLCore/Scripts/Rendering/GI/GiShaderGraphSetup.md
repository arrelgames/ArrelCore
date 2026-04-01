# GI Shader Graph Setup (URP)

This project publishes GI volume data as global shader parameters from `GiManager`.

## Published Globals

- `_GiVolume` (`Texture3D`)
- `_GiIntensity` (`float`)
- `_GiGridMinXZ` (`float4`, x=minWorldX, y=minWorldZ)
- `_GiGridMaxXZ` (`float4`, x=maxWorldXExclusive, y=maxWorldZExclusive)
- `_GiGridSizeXZ` (`float4`, x=gridWidthCells, y=gridDepthCells, z=cellSizeXZ, w=downsample)
- `_GiVolumeSize` (`float4`, x=texWidth, y=texHeight, z=texDepth)
- `_GiVolumeParams` (`float4`, x=invScaleX, y=invScaleZ, z=biasX, w=biasZ)
- `_GiVolumeParamsY` (`float4`, x=invScaleY, y=biasY, z=minWorldY, w=maxWorldY)

## Canonical UVW Contract

Use this mapping in Shader Graph:

- `u = worldPos.x * _GiVolumeParams.x + _GiVolumeParams.z`
- `w = worldPos.z * _GiVolumeParams.y + _GiVolumeParams.w`
- `v = worldPos.y * _GiVolumeParamsY.x + _GiVolumeParamsY.y`

Equivalent expanded form:

- `u = ((worldX - minWorldX) / cellSizeXZ) / (gridWidthCells / downsample)`
- `w = ((worldZ - minWorldZ) / cellSizeXZ) / (gridDepthCells / downsample)`

Clamp UVW to `0..1` before sampling to avoid edge bleed.

## Thin Floor Bleed Mitigation

If you still see top/bottom bleed through thin floors:

- Increase `GiManager.yResolution` (start at `8`, then `12`/`16` for very thin geometry).
- Keep `GiManager.minWorldYSpanCells` at least `4`.
- Use a small Y-span padding (`GiManager.yRangePaddingCells` around `0.25..0.75`).
- In the graph, prefer normal-biased sampling:
  - `samplePos = worldPos + worldNormal * giSampleBias`
  - Use `samplePos` (not raw world position) to compute `u/v/w`.
  - Typical `giSampleBias`: `0.05 .. 0.20` world units.

## Shader Graph Checklist (Lit Graph)

1. Create a new **URP Lit Shader Graph** (for example `SG_GI_Lit`).
2. Add properties (set each to **Exposed = false** and **Reference** exactly):
   - Texture3D property reference `_GiVolume`
   - Vector4 property reference `_GiVolumeParams`
   - Vector4 property reference `_GiVolumeParamsY`
   - Float property reference `_GiIntensity`
3. Add nodes:
   - `Position` node set to **Absolute World**
   - (Optional but recommended) `Normal Vector` node set to **World**
   - (Optional) add `samplePos = Position + Normal * _GiSampleBias`
   - `Split` position to get `X` and `Z`
   - Build `u`: `Multiply(X, _GiVolumeParams.x)` then `Add(_GiVolumeParams.z)`
   - Build `w`: `Multiply(Z, _GiVolumeParams.y)` then `Add(_GiVolumeParams.w)`
   - Build `v`: `Multiply(Y, _GiVolumeParamsY.x)` then `Add(_GiVolumeParamsY.y)`
   - `Vector3` compose `(u, v, w)`
   - `Saturate` the UVW vector
   - `Sample Texture 3D` using `_GiVolume` and UVW
   - Multiply sampled RGB by `_GiIntensity`
4. Connect result:
   - First pass: add to **Emission** input on Lit master stack.
5. Keep your normal base-color/metallic/smoothness inputs unchanged.
6. Create a new material from this graph and apply it only to selected meshes.

## Validation Steps

1. Enter Play mode with `GiManager` active and sources present.
2. Apply the GI material to a test mesh (`Gi Test Block` or floor panel).
3. Move a `GiSource` and confirm the material emission updates.
4. Toggle `Solid`/`PassageBlockingSolid` test cases and verify GI response.
5. Tune `_GiIntensity` in `GiManager` until the scene reads naturally.

