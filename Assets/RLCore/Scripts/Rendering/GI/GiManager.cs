using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Drives grid-based GI propagation and exposes sampling/texture data to URP.
    /// Visual-only: reads from GridWorld, does not affect navigation or gameplay.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GiManager : MonoBehaviour
    {
        public static GiManager Instance { get; private set; }

        [Header("Propagation")]
        [Range(0f, 1f)]
        [SerializeField] private float diffusionStrength = 0.6f;

        [Range(0f, 1f)]
        [SerializeField] private float damping = 0.9f;

        [SerializeField] private float propagationTickInterval = 0.1f;

        [Header("Indirect bounce")]
        [Tooltip("Feed back propagated irradiance as a source term on the next tick (multi-bounce approximation). Uses the main-thread neighbor diffusion path; when enabled, Burst jobs are not used for propagation even if Use GI Jobs Burst is on.")]
        [SerializeField] private bool enableBounceFeedback = false;

        [Range(0f, 1f)]
        [Tooltip("Scales current irradiance (or neighbor average) into the indirect source. Keep moderate vs Damping to avoid runaway brightness.")]
        [SerializeField] private float bounceAlbedo = 0.15f;

        [Min(0f)]
        [Tooltip("0 = no per-channel cap (bounce is not disabled). Values > 0 clamp each RGB channel after Bounce Albedo. Indirect visibility is mostly Bounce Albedo × Damping × Diffusion, not this field.")]
        [SerializeField] private float maxBounce = 0f;

        [Tooltip("If enabled, bounce basis uses neighbor-average current irradiance (costlier); otherwise uses this node's current value.")]
        [SerializeField] private bool useNeighborAverageForBounce = false;

        [Tooltip("When enabled, propagation and bounce neighbor-averaging only couple neighbors whose world Y differs by at most Propagation Same Floor Max World Y Delta (or half CellSizeY if 0). Uses world height, not stack layer index, so single-surface columns at y=0 and y=4 do not exchange light through horizontal graph edges.")]
        [SerializeField] private bool restrictPropagationToSameVerticalLayer = true;

        [Min(0f)]
        [Tooltip("Max world-Y difference for neighbors to diffuse together when restriction is on. 0 = use half of GridWorld CellSizeY. Increase slightly if same-floor meshes disagree in Y and look patchy.")]
        [SerializeField] private float propagationSameFloorMaxWorldYDelta = 0f;

        [Tooltip("When enabled, bounce feedback is only written on nodes that have non-zero direct GiSource irradiance. Pairs with Limit Diffusion To Gi Source Reach to stop indirect fill outside the lit volume.")]
        [SerializeField] private bool bounceOnlyWhereDirectNonZero = true;

        [Tooltip("When enabled, neighbor diffusion uses the full Diffusion Strength only inside at least one point/spot/rect blend sphere (see Diffusion Reach Blend Radius Scale). Stale clearing still uses the raw GiSource radius. Directional sources do not define a sphere. Turn off for global ambient diffusion.")]
        [SerializeField] private bool limitDiffusionToGiSourceReach = true;

        [Min(1f)]
        [Tooltip("Blend radius for neighbor diffusion = max(source radius, source radius × scale + padding). Indirect bounce needs this band past direct injection; stale GI is cleared using the unscaled source radius only.")]
        [SerializeField] private float diffusionReachBlendRadiusScale = 1.45f;

        [Min(0f)]
        [Tooltip("Extra world units added when computing the diffusion blend radius (after scaling).")]
        [SerializeField] private float diffusionReachBlendPaddingWorld = 0f;

        [Tooltip("When GiSource positions, radii, or intensity state refresh (dirty), zeros propagated current and bounce on nodes outside all point/spot/rect reach spheres so moved lights do not leave slowly fading ghosts.")]
        [SerializeField] private bool clearStaleGiOutsideReachWhenSourcesUpdate = true;

        [Header("Occlusion")]
        [Min(1f)]
        [SerializeField] private float occlusionFloorHeightCells = 4f;

        [Range(0f, 1f)]
        [SerializeField] private float occlusionCutoff = 0.05f;

        [Header("Texture Output")]
        [SerializeField] private bool buildTexture = true;

        [SerializeField] private string globalTextureName = "_GiVolume";
        [SerializeField] private string globalIntensityName = "_GiIntensity";
        [SerializeField] private string globalGridMinName = "_GiGridMinXZ";
        [SerializeField] private string globalGridMaxName = "_GiGridMaxXZ";
        [SerializeField] private string globalGridSizeName = "_GiGridSizeXZ";
        [SerializeField] private string globalVolumeSizeName = "_GiVolumeSize";
        [SerializeField] private string globalVolumeParamsName = "_GiVolumeParams";
        [SerializeField] private string globalVolumeYParamsName = "_GiVolumeParamsY";

        [Range(0f, 8f)]
        [SerializeField] private float giIntensity = 1f;

        [Header("Shader Binding")]
        [SerializeField] private bool forceBindGiMaterialProperties = true;
        [Min(0f)]
        [SerializeField] private float materialSyncInterval = 1f;
        [SerializeField] private bool useSourceRegistry = true;
        [SerializeField] private bool useSourceDirtyTracking = true;

        [Tooltip("Downsample factor relative to the GridWorld XZ grid.\n1 = one texel per tile, 2 = one texel per 2x2 tiles.")]
        [Min(1)]
        [SerializeField] private int xzDownsample = 2;

        [Tooltip("Fixed Y resolution of the GI volume. Useful if your gameplay mostly happens near a single floor height.")]
        [Min(1)]
        [SerializeField] private int yResolution = 8;
        [Tooltip("Pads the published GI Y span above and below observed node heights (in CellSizeY units).")]
        [Min(0f)]
        [SerializeField] private float yRangePaddingCells = 0.5f;
        [Tooltip("Minimum published GI Y span (in CellSizeY units) to avoid thin-range quantization collapse.")]
        [Min(1f)]
        [SerializeField] private float minWorldYSpanCells = 4f;

        [Header("Runtime Rebuild")]
        [Min(1)]
        [SerializeField] private int fullRebuildDirtyTileThreshold = 24;
        [Range(0f, 1f)]
        [SerializeField] private float fullRebuildDirtyNodeFraction = 0.35f;
        [Min(0f)]
        [SerializeField] private float rebuildCoalesceWindow = 0.05f;

        [Header("GI Resolution")]
        [Min(1)]
        [SerializeField] private int giResolutionMultiplier = 1;
        [SerializeField] private bool useGiJobsBurst = false;

        [Header("Camera Follow Window")]
        [SerializeField] private bool useCameraFollowWindow = false;
        [SerializeField] private Transform windowCameraTransform;
        [Min(1)]
        [SerializeField] private int windowWidthTiles = 48;
        [Min(1)]
        [SerializeField] private int windowDepthTiles = 48;
        [Min(1)]
        [SerializeField] private int maxStripShiftTiles = 8;
        [Min(1)]
        [SerializeField] private int maxWindowDirtyTilesPerFrame = 4096;

        [Tooltip("When enabled, the follow window (and GI texture shift) only updates when the anchor leaves a square deadzone around the last recenter position. Reduces work while moving. Uses Chebyshev distance in tiles (max of X/Z tile delta).")]
        [SerializeField] private bool useSquareFollowDeadzone = false;

        [Tooltip("Half-extent in base tiles: recenter only when max(|Δx|, |Δz|) from the deadzone origin exceeds this value. Keep well below half of min(window width, window depth).")]
        [Min(0)]
        [SerializeField] private int squareDeadzoneRadiusTiles = 4;

        [Header("Debug")]
        [SerializeField] private bool drawNodeGizmos = false;
        [SerializeField] private float gizmoSphereRadius = 0.15f;
        [SerializeField] private bool logNodeCountDuringPlay = false;
        [Min(0.1f)]
        [SerializeField] private float nodeCountLogInterval = 1f;

        private GiGrid giGrid;
        private Texture3D giTexture;
        private GridWorld gridWorld;

        // Extents of the GridWorld XZ coverage, for mapping into texture space.
        private int minX, maxX, minY, maxY;

        // Reusable list of active sources for the current frame (GiSource components).
        private readonly List<GiSource> sources = new();
        private readonly List<Renderer> cachedRenderers = new();
        private readonly List<Vector2Int> giDirtyTilesBuffer = new();
        private readonly HashSet<Vector2Int> pendingGiDirtyTiles = new();
        private readonly HashSet<int> scratchTexelIndices = new();
        private readonly List<Vector2Int> enteringStripTilesBuffer = new();
        private readonly List<Vector2Int> reusableSubcellsBuffer = new();
        private int[] scratchTexelCounts = System.Array.Empty<int>();
        private Color[] pooledTextureData = System.Array.Empty<Color>();
        private Color[] pooledShiftBuffer = System.Array.Empty<Color>();
        private GiActiveVolumeWindow.RuntimeState volumeWindowState;
        private float volumeMinWorldY;
        private float volumeMaxWorldY;
        private float giYWriteOffset;

        [Header("Spherical harmonics (L0+L1)")]
        [Tooltip("Tier C: one mono SH volume (direction × propagated L0 color). Tier A: three volumes for per-channel angular variation.")]
        [SerializeField] private GiShGridMode giShGridMode = GiShGridMode.Off;
        [Range(0f, 1f)]
        [Tooltip("Blends direct SH coefficients toward neighbor average (same graph mask as diffusion).")]
        [SerializeField] private float shL1NeighborBlur = 0f;
        [Range(0f, 1f)]
        [Tooltip("Shader lerp: 0 = legacy L0-only sampling; 1 = full SH combine.")]
        [SerializeField] private float giShBlend = 1f;
        [Tooltip("When true, skips interpolating across large empty Y gaps (applies to L0 and SH volumes).")]
        [SerializeField] private bool useGapAwareYFillForVolume = false;
        [Min(1)]
        [SerializeField] private int maxYGapInterpolationSlices = 3;

        private Texture3D giShTex0;
        private Texture3D giShTex1;
        private Texture3D giShTex2;
        private Color[] pooledSh0Data = System.Array.Empty<Color>();
        private Color[] pooledSh1Data = System.Array.Empty<Color>();
        private Color[] pooledSh2Data = System.Array.Empty<Color>();
        private int[] pooledFillCountsSnapshot = System.Array.Empty<int>();

        private float tickTimer;
        private float rebuildCoalesceTimer;
        private float materialSyncTimer;
        private float nodeCountLogTimer;
        private bool sourceInjectionDirty = true;
        private bool rendererCacheDirty = true;
        private bool pendingCameraFollowDirtyBatch;
        private bool lastBounceFeedbackEnabled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            gridWorld = GridWorld.Instance;
            if (gridWorld == null)
            {
                Debug.LogError("GiManager: GridWorld.Instance is null; GI will be disabled.");
                enabled = false;
                return;
            }

            // Try immediate initialization; if GridWorld has not populated stacks yet,
            // we'll defer and retry during Update.
            TryInitializeGrid(force: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            DestroyShVolumeTextures();
            ClearShaderGlobals();
        }

        private void Update()
        {
            if (gridWorld == null)
                return;

            // Late-population fix: if stacks were empty in Awake, keep retrying
            // until GridWorld has content and GI nodes can be built.
            if (giGrid == null || giGrid.Nodes.Count == 0)
                TryInitializeGrid(force: false);

            if (giGrid == null || giGrid.Nodes.Count == 0)
                return;

            UpdateCameraFollowWindow();
            LogNodeCountIfEnabled();

            giGrid.DiffusionStrength = diffusionStrength;
            giGrid.Damping = damping;
            giGrid.OcclusionFloorHeightCells = occlusionFloorHeightCells;
            giGrid.OcclusionCutoff = occlusionCutoff;
            giGrid.UseJobsBurst = useGiJobsBurst;
            giGrid.BounceFeedbackEnabled = enableBounceFeedback;
            giGrid.RestrictPropagationToSameVerticalLayer = restrictPropagationToSameVerticalLayer;
            giGrid.PropagationSameFloorMaxWorldYDelta = propagationSameFloorMaxWorldYDelta;
            giGrid.RestrictSourceInjectionToSourceFloor = restrictPropagationToSameVerticalLayer;
            if (lastBounceFeedbackEnabled && !enableBounceFeedback)
                giGrid.ClearBounceSources();
            lastBounceFeedbackEnabled = enableBounceFeedback;
            int clampedMultiplier = Mathf.Max(1, giResolutionMultiplier);
            if (giGrid.ResolutionMultiplier != clampedMultiplier)
            {
                giGrid.ResolutionMultiplier = clampedMultiplier;
                ForceFullGiRebuild();
            }

            bool rebuilt = ProcessGiRebuildRequests();
            if (rebuilt)
                sourceInjectionDirty = true;
            PublishShaderGlobals();
            if (forceBindGiMaterialProperties)
                SyncGiMaterialPropertiesThrottled();

            tickTimer += Time.deltaTime;
            if (tickTimer < propagationTickInterval)
                return;
            tickTimer = 0f;

            if (useSourceRegistry)
                CollectSourcesFromRegistry();
            else
                CollectSourcesLegacy();
            bool changedSourceDetected = false;
            if (useSourceDirtyTracking)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    GiSource src = sources[i];
                    if (src == null || !src.isActiveAndEnabled)
                        continue;
                    if (src.ConsumeDirtyState())
                    {
                        changedSourceDetected = true;
                        break;
                    }
                }
            }
            else
            {
                changedSourceDetected = true;
            }
            sourceInjectionDirty |= changedSourceDetected;
            bool sourcesJustRefreshed = sourceInjectionDirty;
            if (sourceInjectionDirty)
            {
                giGrid.ClearSources();
                ApplySourcesToGrid();
                sourceInjectionDirty = false;
            }

            giGrid.EnableDiffusionReachLimit = limitDiffusionToGiSourceReach;
            giGrid.ClearDiffusionReachSpheres();
            for (int si = 0; si < sources.Count; si++)
            {
                GiSource gs = sources[si];
                if (gs == null || !gs.isActiveAndEnabled)
                    continue;
                if (gs.TryGetDiffusionReachSphere(out Vector3 c, out float strictR))
                {
                    float blendR = Mathf.Max(
                        strictR,
                        strictR * diffusionReachBlendRadiusScale + diffusionReachBlendPaddingWorld);
                    giGrid.AddDiffusionReachSphere(c, strictR, blendR);
                }
            }

            if (clearStaleGiOutsideReachWhenSourcesUpdate && sourcesJustRefreshed && giGrid.DiffusionReachSphereCount > 0)
                giGrid.ZeroStaleOutsideDiffusionReachSpheres(zeroCurrent: !enableBounceFeedback);

            giGrid.StepPropagation();

            if (shL1NeighborBlur > 0f && giShGridMode != GiShGridMode.Off)
                giGrid.ApplyDirectShNeighborBlur(shL1NeighborBlur);

            if (enableBounceFeedback)
            {
                giGrid.UpdateBounceFromCurrent(new GiBounceSettings
                {
                    bounceAlbedo = bounceAlbedo,
                    maxBounce = maxBounce,
                    useNeighborAverageForBounce = useNeighborAverageForBounce,
                    bounceOnlyWhereDirectNonZero = bounceOnlyWhereDirectNonZero
                });
            }

            if (buildTexture)
                UpdateTextureFromGrid();
        }

        private int EffectiveYResolution => Mathf.Max(8, yResolution);

        private void TryInitializeGrid(bool force)
        {
            if (gridWorld == null)
                return;

            int stackCount = gridWorld.GetAllStacks()?.Count ?? 0;
            if (!force && stackCount == 0)
                return;

            yResolution = EffectiveYResolution;

            giGrid = new GiGrid(gridWorld)
            {
                DiffusionStrength = diffusionStrength,
                Damping = damping,
                OcclusionFloorHeightCells = occlusionFloorHeightCells,
                OcclusionCutoff = occlusionCutoff,
                ResolutionMultiplier = Mathf.Max(1, giResolutionMultiplier),
                UseJobsBurst = useGiJobsBurst
            };

            // Reset extents from the latest grid content.
            minX = maxX = minY = maxY = 0;
            ComputeGridExtents(gridWorld);
            InitializeWindowBoundsIfNeeded();
            if (TryGetActiveBoundsForGiRebuild(out int rebuildMinX, out int rebuildMaxX, out int rebuildMinY, out int rebuildMaxY))
                giGrid.RebuildAll(rebuildMinX, rebuildMaxX, rebuildMinY, rebuildMaxY);

            if (buildTexture)
            {
                EnsureTextureSizeMatchesGrid();
                UpdateTextureFromGrid();
            }
            rendererCacheDirty = true;
            sourceInjectionDirty = true;

            PublishShaderGlobals();
        }

        private void ComputeGridExtents(GridWorld world)
        {
            IReadOnlyDictionary<Vector2Int, GridStack> stacks = world.GetAllStacks();
            bool first = true;
            foreach (var kvp in stacks)
            {
                Vector2Int pos = kvp.Key;
                if (first)
                {
                    first = false;
                    minX = maxX = pos.x;
                    minY = maxY = pos.y;
                }
                else
                {
                    if (pos.x < minX) minX = pos.x;
                    if (pos.x > maxX) maxX = pos.x;
                    if (pos.y < minY) minY = pos.y;
                    if (pos.y > maxY) maxY = pos.y;
                }
            }
        }

        private bool ProcessGiRebuildRequests()
        {
            bool consumedNew = gridWorld.ConsumeGiDirtyTiles(giDirtyTilesBuffer);
            if (consumedNew)
            {
                for (int i = 0; i < giDirtyTilesBuffer.Count; i++)
                {
                    Vector2Int tile = giDirtyTilesBuffer[i];
                    if (IsBaseTileInActiveBounds(tile))
                        pendingGiDirtyTiles.Add(tile);
                }
                rebuildCoalesceTimer = Mathf.Max(0f, rebuildCoalesceWindow);
            }

            if (pendingGiDirtyTiles.Count == 0)
                return false;

            if (rebuildCoalesceTimer > 0f)
            {
                rebuildCoalesceTimer -= Time.deltaTime;
                if (rebuildCoalesceTimer > 0f)
                    return false;
            }

            giDirtyTilesBuffer.Clear();
            foreach (Vector2Int p in pendingGiDirtyTiles)
                giDirtyTilesBuffer.Add(p);
            pendingGiDirtyTiles.Clear();
            if (giDirtyTilesBuffer.Count == 0)
                return false;

            int absoluteThreshold = Mathf.Max(1, fullRebuildDirtyTileThreshold);
            int fractionalThreshold = int.MaxValue;
            if (fullRebuildDirtyNodeFraction > 0f)
            {
                fractionalThreshold = Mathf.Max(
                    1,
                    Mathf.CeilToInt(giGrid.Nodes.Count * Mathf.Clamp01(fullRebuildDirtyNodeFraction))
                );
            }

            bool fullRebuild = giDirtyTilesBuffer.Count >= absoluteThreshold ||
                               giDirtyTilesBuffer.Count >= fractionalThreshold ||
                               giDirtyTilesBuffer.Count > Mathf.Max(1, maxWindowDirtyTilesPerFrame);
            if (pendingCameraFollowDirtyBatch)
            {
                // Camera-follow movement typically queues entering strips every tile step.
                // Prefer regional rebuild unless the dirty batch approaches full window coverage.
                int cameraFollowHardLimit = Mathf.Max(1, windowWidthTiles * windowDepthTiles);
                fullRebuild = giDirtyTilesBuffer.Count >= cameraFollowHardLimit ||
                              giDirtyTilesBuffer.Count > Mathf.Max(1, maxWindowDirtyTilesPerFrame);
            }
            if (fullRebuild)
            {
                if (TryGetActiveBoundsForGiRebuild(out int rebuildMinX, out int rebuildMaxX, out int rebuildMinY, out int rebuildMaxY))
                    giGrid.RebuildAll(rebuildMinX, rebuildMaxX, rebuildMinY, rebuildMaxY);
                else
                    giGrid.RebuildAll();
            }
            else
            {
                if (TryGetActiveBoundsForGiRebuild(out int rebuildMinX, out int rebuildMaxX, out int rebuildMinY, out int rebuildMaxY))
                    giGrid.RebuildRegion(giDirtyTilesBuffer, rebuildMinX, rebuildMaxX, rebuildMinY, rebuildMaxY);
                else
                    giGrid.RebuildRegion(giDirtyTilesBuffer);
            }

            int oldMinX = minX;
            int oldMaxX = maxX;
            int oldMinY = minY;
            int oldMaxY = maxY;
            ComputeGridExtents(gridWorld);
            bool extentsChanged = oldMinX != minX || oldMaxX != maxX || oldMinY != minY || oldMaxY != maxY;

            if (!buildTexture)
                return true;

            if (extentsChanged || giTexture == null)
                EnsureTextureSizeMatchesGrid();

            if (fullRebuild || extentsChanged)
                UpdateTextureFromGrid();
            else
                UpdateTextureFromDirtyTiles(giDirtyTilesBuffer);

            sourceInjectionDirty = true;
            pendingCameraFollowDirtyBatch = false;
            return true;
        }

        private void AllocateTexture()
        {
            int resolution = Mathf.Max(1, giGrid != null ? giGrid.ResolutionMultiplier : giResolutionMultiplier);
            GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            int subWidth = Mathf.Max(1, (maxTileX - minTileX + 1) * resolution);
            int subDepth = Mathf.Max(1, (maxTileY - minTileY + 1) * resolution);
            int sizeX = Mathf.Max(1, Mathf.CeilToInt(subWidth / (float)Mathf.Max(1, xzDownsample)));
            int sizeZ = Mathf.Max(1, Mathf.CeilToInt(subDepth / (float)Mathf.Max(1, xzDownsample)));

            giTexture = new Texture3D(sizeX, yResolution, sizeZ, TextureFormat.RGBAHalf, false)
            {
                name = "GiVolume",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Shader.SetGlobalTexture(globalTextureName, giTexture);
            int voxelCount = sizeX * yResolution * sizeZ;
            EnsureTextureBuffers(voxelCount);
            EnsureShPooledBuffers(voxelCount);
            RebuildShVolumeTextures(sizeX, yResolution, sizeZ);
            PublishShaderGlobals();
        }

        private void EnsureTextureSizeMatchesGrid()
        {
            int resolution = Mathf.Max(1, giGrid != null ? giGrid.ResolutionMultiplier : giResolutionMultiplier);
            GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            int subWidth = Mathf.Max(1, (maxTileX - minTileX + 1) * resolution);
            int subDepth = Mathf.Max(1, (maxTileY - minTileY + 1) * resolution);
            int expectedX = Mathf.Max(1, Mathf.CeilToInt(subWidth / (float)Mathf.Max(1, xzDownsample)));
            int expectedZ = Mathf.Max(1, Mathf.CeilToInt(subDepth / (float)Mathf.Max(1, xzDownsample)));
            int expectedY = Mathf.Max(1, yResolution);

            bool needsNewTexture = giTexture == null ||
                                   giTexture.width != expectedX ||
                                   giTexture.height != expectedY ||
                                   giTexture.depth != expectedZ;
            if (!needsNewTexture)
            {
                if (TryRebuildShVolumeTexturesIfNeeded(expectedX, expectedY, expectedZ))
                    PublishShaderGlobals();
                return;
            }

            if (giTexture != null)
                Destroy(giTexture);

            DestroyShVolumeTextures();
            AllocateTexture();
        }

        private void UpdateTextureFromGrid()
        {
            if (giTexture == null)
                return;

            int sizeX = giTexture.width;
            int sizeY = giTexture.height;
            int sizeZ = giTexture.depth;

            int voxelCount = sizeX * sizeY * sizeZ;
            EnsureTextureBuffers(voxelCount);
            EnsureShPooledBuffers(voxelCount);
            Color[] data = pooledTextureData;
            System.Array.Clear(data, 0, voxelCount);
            EnsureScratchTexelCountsSize(voxelCount);
            System.Array.Clear(scratchTexelCounts, 0, voxelCount);

            bool writeSh = giShGridMode != GiShGridMode.Off && giShTex0 != null;
            bool tierA = giShGridMode == GiShGridMode.TierA_PerChannelRgb;
            Color[] sh0 = writeSh ? pooledSh0Data : null;
            Color[] sh1 = writeSh && tierA ? pooledSh1Data : null;
            Color[] sh2 = writeSh && tierA ? pooledSh2Data : null;
            if (sh0 != null)
                System.Array.Clear(sh0, 0, voxelCount);
            if (sh1 != null)
                System.Array.Clear(sh1, 0, voxelCount);
            if (sh2 != null)
                System.Array.Clear(sh2, 0, voxelCount);

            int nodeCount = giGrid.GetNodeCount();
            if (nodeCount == 0)
            {
                giTexture.SetPixels(data);
                giTexture.Apply(false, false);
                if (writeSh)
                {
                    giShTex0.SetPixels(sh0);
                    giShTex0.Apply(false, false);
                    if (tierA)
                    {
                        giShTex1.SetPixels(sh1);
                        giShTex1.Apply(false, false);
                        giShTex2.SetPixels(sh2);
                        giShTex2.Apply(false, false);
                    }
                }

                PublishShaderGlobals();
                return;
            }

            // Simple accumulation: for each node, write into its corresponding texel.
            GetActiveBounds(out int minTileX, out _, out int minTileY, out _);
            RefreshVolumeWorldYRange();
            giYWriteOffset = ComputeYWriteOffset(sizeY);
            int resolution = Mathf.Max(1, giGrid.ResolutionMultiplier);
            int minSubX = minTileX * resolution;
            int minSubZ = minTileY * resolution;
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2Int p = giGrid.GetNodeGridPosAt(i);
                Vector3 nodeWorldPos = giGrid.GetNodeWorldPosAt(i);
                int baseTileX = FloorDiv(p.x, resolution);
                int baseTileY = FloorDiv(p.y, resolution);
                if (!IsBaseTileInActiveBounds(new Vector2Int(baseTileX, baseTileY)))
                    continue;

                int tx = Mathf.Clamp((p.x - minSubX) / xzDownsample, 0, sizeX - 1);
                int tz = Mathf.Clamp((p.y - minSubZ) / xzDownsample, 0, sizeZ - 1);

                int tyTop = WorldYToVolumeSlice(nodeWorldPos.y + giYWriteOffset, sizeY);
                int tyBot = Mathf.Max(0, tyTop - 1);

                // fAbove uses direct GiSource data only; total irradiance includes indirect bounce.
                float fAbove = giGrid.GetSourceFractionAbove(i);
                Color irr = giGrid.GetCurrentIrradianceAt(i);
                Color irrTop = irr * fAbove;
                Color irrBot = irr * (1f - fAbove);

                int idxTop = tx + sizeX * (tyTop + sizeY * tz);
                data[idxTop] += irrTop;
                scratchTexelCounts[idxTop]++;
                if (writeSh)
                {
                    if (tierA)
                    {
                        giGrid.GetDirectShRgbAt(i, out Vector4 r, out Vector4 g, out Vector4 b);
                        sh0[idxTop] += new Color(r.x, r.y, r.z, r.w) * fAbove;
                        sh1[idxTop] += new Color(g.x, g.y, g.z, g.w) * fAbove;
                        sh2[idxTop] += new Color(b.x, b.y, b.z, b.w) * fAbove;
                    }
                    else
                    {
                        Vector4 m = giGrid.GetDirectShMonoAt(i);
                        sh0[idxTop] += new Color(m.x, m.y, m.z, m.w) * fAbove;
                    }
                }

                if (tyBot != tyTop && irrBot.maxColorComponent > 1e-6f)
                {
                    int idxBot = tx + sizeX * (tyBot + sizeY * tz);
                    data[idxBot] += irrBot;
                    scratchTexelCounts[idxBot]++;
                    if (writeSh)
                    {
                        float fBot = 1f - fAbove;
                        if (tierA)
                        {
                            giGrid.GetDirectShRgbAt(i, out Vector4 r, out Vector4 g, out Vector4 b);
                            sh0[idxBot] += new Color(r.x, r.y, r.z, r.w) * fBot;
                            sh1[idxBot] += new Color(g.x, g.y, g.z, g.w) * fBot;
                            sh2[idxBot] += new Color(b.x, b.y, b.z, b.w) * fBot;
                        }
                        else
                        {
                            Vector4 m = giGrid.GetDirectShMonoAt(i);
                            sh0[idxBot] += new Color(m.x, m.y, m.z, m.w) * fBot;
                        }
                    }
                }
            }

            for (int i = 0; i < voxelCount; i++)
            {
                int count = scratchTexelCounts[i];
                if (count > 1)
                {
                    data[i] /= count;
                    if (writeSh)
                    {
                        sh0[i] /= count;
                        if (tierA)
                        {
                            sh1[i] /= count;
                            sh2[i] /= count;
                        }
                    }
                }
            }

            EnsureFillCountsSnapshot(voxelCount);
            System.Array.Copy(scratchTexelCounts, 0, pooledFillCountsSnapshot, 0, voxelCount);

            RunYSliceFillUtility(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

            if (useCameraFollowWindow)
                FillEmptyTexelsFromNeighbors(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

            if (writeSh && !tierA)
            {
                System.Array.Copy(pooledFillCountsSnapshot, 0, scratchTexelCounts, 0, voxelCount);
                RunYSliceFillUtility(sh0, scratchTexelCounts, sizeX, sizeY, sizeZ);
            }
            else if (tierA && writeSh)
            {
                System.Array.Copy(pooledFillCountsSnapshot, 0, scratchTexelCounts, 0, voxelCount);
                RunYSliceFillUtility(sh0, scratchTexelCounts, sizeX, sizeY, sizeZ);
                System.Array.Copy(pooledFillCountsSnapshot, 0, scratchTexelCounts, 0, voxelCount);
                RunYSliceFillUtility(sh1, scratchTexelCounts, sizeX, sizeY, sizeZ);
                System.Array.Copy(pooledFillCountsSnapshot, 0, scratchTexelCounts, 0, voxelCount);
                RunYSliceFillUtility(sh2, scratchTexelCounts, sizeX, sizeY, sizeZ);
            }

            giTexture.SetPixels(data);
            giTexture.Apply(false, false);
            if (writeSh)
            {
                giShTex0.SetPixels(sh0);
                giShTex0.Apply(false, false);
                if (tierA)
                {
                    giShTex1.SetPixels(sh1);
                    giShTex1.Apply(false, false);
                    giShTex2.SetPixels(sh2);
                    giShTex2.Apply(false, false);
                }
            }

            PublishShaderGlobals();
        }

        private void UpdateTextureFromDirtyTiles(IReadOnlyList<Vector2Int> dirtyTiles)
        {
            if (giTexture == null || dirtyTiles == null || dirtyTiles.Count == 0)
            {
                UpdateTextureFromGrid();
                return;
            }

            if (giShGridMode != GiShGridMode.Off)
            {
                UpdateTextureFromGrid();
                return;
            }

            GiActiveVolumeWindow.TryApplyPendingTextureShift(
                giTexture, ref pooledTextureData, ref pooledShiftBuffer,
                ref volumeWindowState.PendingTexelShiftX, ref volumeWindowState.PendingTexelShiftZ);

            int sizeX = giTexture.width;
            int sizeY = giTexture.height;
            int sizeZ = giTexture.depth;
            int voxelCount = sizeX * sizeY * sizeZ;
            EnsureTextureBuffers(voxelCount);
            if (pooledTextureData.Length != voxelCount)
            {
                UpdateTextureFromGrid();
                return;
            }
            Color[] data = pooledTextureData;
            EnsureScratchTexelCountsSize(voxelCount);

            scratchTexelIndices.Clear();
            GetActiveBounds(out int minTileX, out _, out int minTileY, out _);
            RefreshVolumeWorldYRange();
            giYWriteOffset = ComputeYWriteOffset(sizeY);
            int resolution = Mathf.Max(1, giGrid.ResolutionMultiplier);
            int minSubX = minTileX * resolution;
            int minSubZ = minTileY * resolution;
            for (int i = 0; i < dirtyTiles.Count; i++)
            {
                if (!IsBaseTileInActiveBounds(dirtyTiles[i]))
                    continue;
                reusableSubcellsBuffer.Clear();
                giGrid.AppendSubcellsForBaseTile(dirtyTiles[i], reusableSubcellsBuffer);
                for (int s = 0; s < reusableSubcellsBuffer.Count; s++)
                {
                    Vector2Int p = reusableSubcellsBuffer[s];
                    int tx = Mathf.Clamp((p.x - minSubX) / xzDownsample, 0, sizeX - 1);
                    int tz = Mathf.Clamp((p.y - minSubZ) / xzDownsample, 0, sizeZ - 1);
                    for (int ty = 0; ty < sizeY; ty++)
                    {
                        int index = tx + sizeX * (ty + sizeY * tz);
                        scratchTexelIndices.Add(index);
                    }
                }
            }

            foreach (int idx in scratchTexelIndices)
            {
                data[idx] = Color.black;
                scratchTexelCounts[idx] = 0;
            }

            int nodeCount = giGrid.GetNodeCount();
            for (int i = 0; i < nodeCount; i++)
            {
                Vector2Int nodeGridPos = giGrid.GetNodeGridPosAt(i);
                Vector3 nodeWorldPos = giGrid.GetNodeWorldPosAt(i);
                int baseTileX = FloorDiv(nodeGridPos.x, resolution);
                int baseTileY = FloorDiv(nodeGridPos.y, resolution);
                if (!IsBaseTileInActiveBounds(new Vector2Int(baseTileX, baseTileY)))
                    continue;
                int tx = Mathf.Clamp((nodeGridPos.x - minSubX) / xzDownsample, 0, sizeX - 1);
                int tz = Mathf.Clamp((nodeGridPos.y - minSubZ) / xzDownsample, 0, sizeZ - 1);
                int tyTop = WorldYToVolumeSlice(nodeWorldPos.y + giYWriteOffset, sizeY);
                int tyBot = Mathf.Max(0, tyTop - 1);
                int idxTop = tx + sizeX * (tyTop + sizeY * tz);
                if (!scratchTexelIndices.Contains(idxTop))
                    continue;

                // fAbove uses direct GiSource data only; total irradiance includes indirect bounce.
                float fAbove = giGrid.GetSourceFractionAbove(i);
                Color irr = giGrid.GetCurrentIrradianceAt(i);
                Color irrTop = irr * fAbove;
                Color irrBot = irr * (1f - fAbove);

                data[idxTop] += irrTop;
                scratchTexelCounts[idxTop]++;
                if (tyBot != tyTop && irrBot.maxColorComponent > 1e-6f)
                {
                    int idxBot = tx + sizeX * (tyBot + sizeY * tz);
                    if (scratchTexelIndices.Contains(idxBot))
                    {
                        data[idxBot] += irrBot;
                        scratchTexelCounts[idxBot]++;
                    }
                }
            }

            foreach (int tidx in scratchTexelIndices)
            {
                int count = scratchTexelCounts[tidx];
                if (count > 1)
                    data[tidx] /= count;
            }

            RunYSliceFillUtility(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

            giTexture.SetPixels(data);
            giTexture.Apply(false, false);
            PublishShaderGlobals();
        }

        private void RunYSliceFillUtility(Color[] volData, int[] counts, int sizeX, int sizeY, int sizeZ)
        {
            int maxCarry = ComputeMaxUpwardCarrySlices(sizeY);
            if (useGapAwareYFillForVolume)
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesGapAware(
                    volData, counts, sizeX, sizeY, sizeZ, maxYGapInterpolationSlices, maxCarry);
            }
            else
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesBidirectional(
                    volData, counts, sizeX, sizeY, sizeZ, maxCarry);
            }
        }

        private void EnsureFillCountsSnapshot(int voxelCount)
        {
            if (pooledFillCountsSnapshot.Length != voxelCount)
                pooledFillCountsSnapshot = new int[voxelCount];
        }

        private void EnsureShPooledBuffers(int voxelCount)
        {
            if (pooledSh0Data.Length != voxelCount)
            {
                pooledSh0Data = new Color[voxelCount];
                pooledSh1Data = new Color[voxelCount];
                pooledSh2Data = new Color[voxelCount];
            }
        }

        private void DestroyShVolumeTextures()
        {
            Texture3D t0 = giShTex0;
            Texture3D t1 = giShTex1;
            Texture3D t2 = giShTex2;
            giShTex0 = null;
            giShTex1 = null;
            giShTex2 = null;
            if (t0 != null)
                Destroy(t0);
            if (t1 != null && !ReferenceEquals(t1, t0))
                Destroy(t1);
            if (t2 != null && !ReferenceEquals(t2, t0) && !ReferenceEquals(t2, t1))
                Destroy(t2);
        }

        private bool TryRebuildShVolumeTexturesIfNeeded(int expectedX, int expectedY, int expectedZ)
        {
            if (giShGridMode == GiShGridMode.Off)
            {
                if (giShTex0 == null)
                    return false;
                DestroyShVolumeTextures();
                return true;
            }

            bool sizeBad = giShTex0 == null ||
                           giShTex0.width != expectedX ||
                           giShTex0.height != expectedY ||
                           giShTex0.depth != expectedZ;
            bool layoutBad = false;
            if (giShGridMode == GiShGridMode.TierC_Mono && giShTex0 != null)
                layoutBad = giShTex1 == null || !ReferenceEquals(giShTex1, giShTex0);
            if (giShGridMode == GiShGridMode.TierA_PerChannelRgb)
                layoutBad = giShTex1 == null || ReferenceEquals(giShTex1, giShTex0) ||
                            giShTex2 == null || ReferenceEquals(giShTex2, giShTex0);

            if (!sizeBad && !layoutBad)
                return false;

            RebuildShVolumeTextures(expectedX, expectedY, expectedZ);
            return true;
        }

        private void RebuildShVolumeTextures(int sizeX, int sizeY, int sizeZ)
        {
            DestroyShVolumeTextures();
            if (!buildTexture || giShGridMode == GiShGridMode.Off)
                return;

            giShTex0 = new Texture3D(sizeX, sizeY, sizeZ, TextureFormat.RGBAHalf, false)
            {
                name = "GiVolumeSH0",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            if (giShGridMode == GiShGridMode.TierA_PerChannelRgb)
            {
                giShTex1 = new Texture3D(sizeX, sizeY, sizeZ, TextureFormat.RGBAHalf, false)
                {
                    name = "GiVolumeSH1",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                giShTex2 = new Texture3D(sizeX, sizeY, sizeZ, TextureFormat.RGBAHalf, false)
                {
                    name = "GiVolumeSH2",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            else
            {
                giShTex1 = giShTex0;
                giShTex2 = giShTex0;
            }
        }

        private void EnsureScratchTexelCountsSize(int size)
        {
            if (scratchTexelCounts.Length == size)
                return;

            scratchTexelCounts = new int[size];
        }

        private void PublishShaderGlobals()
        {
            if (gridWorld == null)
                return;

            GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            float worldMinX = minTileX * gridWorld.CellSizeXZ;
            float worldMinZ = minTileY * gridWorld.CellSizeXZ;
            float worldMaxX = (maxTileX + 1) * gridWorld.CellSizeXZ;
            float worldMaxZ = (maxTileY + 1) * gridWorld.CellSizeXZ;

            int gridWidthCells = Mathf.Max(1, maxTileX - minTileX + 1);
            int gridDepthCells = Mathf.Max(1, maxTileY - minTileY + 1);
            int resolution = Mathf.Max(1, giGrid != null ? giGrid.ResolutionMultiplier : giResolutionMultiplier);
            int volumeWidth = giTexture != null
                ? giTexture.width
                : Mathf.Max(1, Mathf.CeilToInt((gridWidthCells * resolution) / (float)Mathf.Max(1, xzDownsample)));
            int volumeDepth = giTexture != null
                ? giTexture.depth
                : Mathf.Max(1, Mathf.CeilToInt((gridDepthCells * resolution) / (float)Mathf.Max(1, xzDownsample)));
            int volumeHeight = giTexture != null ? giTexture.height : Mathf.Max(1, yResolution);

            Shader.SetGlobalFloat(globalIntensityName, giIntensity);
            Shader.SetGlobalVector(globalGridMinName, new Vector4(worldMinX, worldMinZ, 0f, 0f));
            Shader.SetGlobalVector(globalGridMaxName, new Vector4(worldMaxX, worldMaxZ, 0f, 0f));
            Shader.SetGlobalVector(globalGridSizeName, new Vector4(gridWidthCells, gridDepthCells, gridWorld.CellSizeXZ, Mathf.Max(1, xzDownsample)));
            Shader.SetGlobalVector(globalVolumeSizeName, new Vector4(volumeWidth, volumeHeight, volumeDepth, 0f));

            // Canonical mapping helper for Shader Graph:
            // uvw.x = ((worldPos.x - minWorldX) / cellSizeXZ) / (gridWidthCells / downsample)
            // uvw.z = ((worldPos.z - minWorldZ) / cellSizeXZ) / (gridDepthCells / downsample)
            // uvw.y = worldPos.y * _GiVolumeParamsY.x + _GiVolumeParamsY.y
            // where _GiVolumeParamsY stores (invScaleY, biasY, minWorldY, maxWorldY).
            // IMPORTANT: map world X/Z across full grid-world coverage [min,max] => [0,1].
            // Downsampling is already represented in texture write indexing; applying it here
            // doubles the scale and shifts/clamps sampling in Shader Graph.
            float worldSpanX = Mathf.Max(gridWorld.CellSizeXZ, worldMaxX - worldMinX);
            float worldSpanZ = Mathf.Max(gridWorld.CellSizeXZ, worldMaxZ - worldMinZ);
            float invScaleX = 1f / worldSpanX;
            float invScaleZ = 1f / worldSpanZ;
            float biasX = -worldMinX * invScaleX;
            float biasZ = -worldMinZ * invScaleZ;
            Shader.SetGlobalVector(globalVolumeParamsName, new Vector4(invScaleX, invScaleZ, biasX, biasZ));
            RefreshVolumeWorldYRange();
            float worldSpanY = Mathf.Max(Mathf.Max(1e-4f, gridWorld.CellSizeY), volumeMaxWorldY - volumeMinWorldY);
            float invScaleY = 1f / worldSpanY;
            float biasY = -volumeMinWorldY * invScaleY;
            Shader.SetGlobalVector(globalVolumeYParamsName, new Vector4(invScaleY, biasY, volumeMinWorldY, volumeMaxWorldY));

            if (buildTexture && giShGridMode != GiShGridMode.Off && giShTex0 != null)
            {
                Shader.SetGlobalTexture(GiShaderGlobals.VolumeSh0, giShTex0);
                Shader.SetGlobalTexture(GiShaderGlobals.VolumeSh1, giShTex1 != null ? giShTex1 : giShTex0);
                Shader.SetGlobalTexture(GiShaderGlobals.VolumeSh2, giShTex2 != null ? giShTex2 : giShTex0);
                Shader.SetGlobalFloat(GiShaderGlobals.UseSH, giShBlend);
                Shader.SetGlobalFloat(GiShaderGlobals.ShTier,
                    giShGridMode == GiShGridMode.TierA_PerChannelRgb ? 2f : 1f);
            }
            else
            {
                GiShaderGlobals.BindNeutralShGlobals();
            }

            Shader.SetGlobalFloat(GiShaderGlobals.DirectionalActive, 0f);
            Shader.SetGlobalVector(GiShaderGlobals.SunDirWorld, new Vector4(0f, -1f, 0f, 0f));
        }

        private void ClearShaderGlobals()
        {
            Shader.SetGlobalFloat(globalIntensityName, 0f);
            Shader.SetGlobalVector(globalGridMinName, Vector4.zero);
            Shader.SetGlobalVector(globalGridMaxName, Vector4.zero);
            Shader.SetGlobalVector(globalGridSizeName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeSizeName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeParamsName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeYParamsName, Vector4.zero);
            GiShaderGlobals.BindNeutralShGlobals();
            Shader.SetGlobalFloat(GiShaderGlobals.DirectionalActive, 0f);
            Shader.SetGlobalVector(GiShaderGlobals.SunDirWorld, Vector4.zero);
        }

        private void SyncGiMaterialProperties()
        {
            Texture giTex = Shader.GetGlobalTexture(globalTextureName);
            Texture sh0 = Shader.GetGlobalTexture(GiShaderGlobals.VolumeSh0);
            Texture sh1 = Shader.GetGlobalTexture(GiShaderGlobals.VolumeSh1);
            Texture sh2 = Shader.GetGlobalTexture(GiShaderGlobals.VolumeSh2);
            float useSh = Shader.GetGlobalFloat(GiShaderGlobals.UseSH);
            float shTier = Shader.GetGlobalFloat(GiShaderGlobals.ShTier);
            Vector4 giParams = Shader.GetGlobalVector(globalVolumeParamsName);
            Vector4 giParamsY = Shader.GetGlobalVector(globalVolumeYParamsName);
            float giInt = Shader.GetGlobalFloat(globalIntensityName);

            for (int r = 0; r < cachedRenderers.Count; r++)
            {
                Renderer renderer = cachedRenderers[r];
                if (renderer == null)
                    continue;
                Material[] mats = renderer.sharedMaterials;
                if (mats == null)
                    continue;

                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;

                    if (mat.HasProperty("_GiVolume"))
                    {
                        mat.SetTexture("_GiVolume", giTex);
                    }
                    if (mat.HasProperty("_GIVolume"))
                    {
                        mat.SetTexture("_GIVolume", giTex);
                    }
                    if (mat.HasProperty("_GiVolumeParams"))
                    {
                        mat.SetVector("_GiVolumeParams", giParams);
                    }
                    if (mat.HasProperty("_GiIntensity"))
                    {
                        mat.SetFloat("_GiIntensity", giInt);
                    }
                    if (mat.HasProperty("_GiVolumeParamsY"))
                    {
                        mat.SetVector("_GiVolumeParamsY", giParamsY);
                    }
                    if (mat.HasProperty("_GiSampleNormalBias"))
                    {
                        mat.SetFloat("_GiSampleNormalBias", giYWriteOffset);
                    }
                    if (mat.HasProperty(GiShaderGlobals.DirectionalActive))
                        mat.SetFloat(GiShaderGlobals.DirectionalActive, 0f);
                    if (mat.HasProperty(GiShaderGlobals.SunDirWorld))
                        mat.SetVector(GiShaderGlobals.SunDirWorld, new Vector4(0f, -1f, 0f, 0f));
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh0))
                        mat.SetTexture(GiShaderGlobals.VolumeSh0, sh0);
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh1))
                        mat.SetTexture(GiShaderGlobals.VolumeSh1, sh1);
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh2))
                        mat.SetTexture(GiShaderGlobals.VolumeSh2, sh2);
                    if (mat.HasProperty(GiShaderGlobals.UseSH))
                        mat.SetFloat(GiShaderGlobals.UseSH, useSh);
                    if (mat.HasProperty(GiShaderGlobals.ShTier))
                        mat.SetFloat(GiShaderGlobals.ShTier, shTier);
                }
            }
        }

        private void CollectSourcesFromRegistry()
        {
            sources.Clear();
            IReadOnlyList<GiSource> registered = GiSourceRegistry.GetSnapshot();
            for (int i = 0; i < registered.Count; i++)
            {
                GiSource src = registered[i];
                if (src != null)
                    sources.Add(src);
            }
        }

        private void CollectSourcesLegacy()
        {
            sources.Clear();
            GiSource[] found = FindObjectsOfType<GiSource>();
            if (found == null || found.Length == 0)
                return;
            sources.AddRange(found);
        }

        private void ApplySourcesToGrid()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                GiSource src = sources[i];
                if (src == null || !src.isActiveAndEnabled)
                    continue;

                src.ApplyToGrid(giGrid);
            }
        }

        /// <summary>
        /// Samples the current GI irradiance at a world position.
        /// </summary>
        public static Color SampleGi(Vector3 worldPos)
        {
            if (Instance == null || Instance.giGrid == null)
                return Color.black;

            return Instance.giGrid.SampleAtWorldPos(worldPos);
        }

        public void ForceFullGiRebuild()
        {
            if (giGrid == null || gridWorld == null)
                return;

            if (TryGetActiveBoundsForGiRebuild(out int rebuildMinX, out int rebuildMaxX, out int rebuildMinY, out int rebuildMaxY))
                giGrid.RebuildAll(rebuildMinX, rebuildMaxX, rebuildMinY, rebuildMaxY);
            else
                giGrid.RebuildAll();
            giGrid.ClearBounceSources();
            ComputeGridExtents(gridWorld);
            InitializeWindowBoundsIfNeeded();
            if (buildTexture)
            {
                EnsureTextureSizeMatchesGrid();
                UpdateTextureFromGrid();
            }

            PublishShaderGlobals();
            sourceInjectionDirty = true;
            rendererCacheDirty = true;
        }

        private void OnDrawGizmos()
        {
            if (!drawNodeGizmos || giGrid == null)
                return;

            Gizmos.color = Color.yellow;
            foreach (GiNode node in giGrid.Nodes)
            {
                float intensity = node.currentIrradiance.maxColorComponent;
                if (intensity <= 0.001f)
                    continue;

                Gizmos.color = node.currentIrradiance;
                Gizmos.DrawSphere(node.worldPos, gizmoSphereRadius);
            }
        }

        private void InitializeWindowBoundsIfNeeded()
        {
            if (!useCameraFollowWindow || gridWorld == null)
                return;

            Transform anchor = GetWindowAnchorTransform();
            if (anchor == null)
                return;

            GiActiveVolumeWindow.TryInitializeWindow(
                ref volumeWindowState,
                anchor,
                gridWorld.CellSizeXZ,
                windowWidthTiles,
                windowDepthTiles,
                useSquareFollowDeadzone,
                out _);
        }

        private void UpdateCameraFollowWindow()
        {
            if (!useCameraFollowWindow || gridWorld == null || giGrid == null)
                return;

            Transform anchor = GetWindowAnchorTransform();
            if (anchor == null)
                return;

            Vector2Int tile = GiActiveVolumeWindow.WorldToBaseTile(anchor.position, gridWorld.CellSizeXZ);
            if (!GiActiveVolumeWindow.TryRecenterFromAnchor(
                    ref volumeWindowState,
                    tile,
                    windowWidthTiles,
                    windowDepthTiles,
                    useSquareFollowDeadzone,
                    squareDeadzoneRadiusTiles,
                    force: !volumeWindowState.HasActiveWindowBounds,
                    out int dx,
                    out int dz))
                return;

            if (!buildTexture || giTexture == null)
                return;

            int width = Mathf.Max(1, volumeWindowState.ActiveMaxX - volumeWindowState.ActiveMinX + 1);
            int depth = Mathf.Max(1, volumeWindowState.ActiveMaxY - volumeWindowState.ActiveMinY + 1);
            int absDx = Mathf.Abs(dx);
            int absDz = Mathf.Abs(dz);
            bool tooLarge = absDx >= width || absDz >= depth || absDx > Mathf.Max(1, maxStripShiftTiles) || absDz > Mathf.Max(1, maxStripShiftTiles);
            int resolution = Mathf.Max(1, giGrid.ResolutionMultiplier);
            int subShiftX = dx * resolution;
            int subShiftZ = dz * resolution;
            int ds = Mathf.Max(1, xzDownsample);
            bool alignedForTexelShift = subShiftX % ds == 0 && subShiftZ % ds == 0;
            if (tooLarge || !alignedForTexelShift)
            {
                UpdateTextureFromGrid();
                return;
            }

            volumeWindowState.PendingTexelShiftX += subShiftX / ds;
            volumeWindowState.PendingTexelShiftZ += subShiftZ / ds;
            materialSyncTimer = 0f;
            GiActiveVolumeWindow.QueueEnteringStripTiles(in volumeWindowState, dx, dz, enteringStripTilesBuffer);
            if (enteringStripTilesBuffer.Count == 0)
            {
                UpdateTextureFromGrid();
            }
            else
            {
                for (int i = 0; i < enteringStripTilesBuffer.Count; i++)
                    pendingGiDirtyTiles.Add(enteringStripTilesBuffer[i]);
                rebuildCoalesceTimer = 0f;
                pendingCameraFollowDirtyBatch = true;
                sourceInjectionDirty = true;
                tickTimer = Mathf.Max(tickTimer, propagationTickInterval);
            }
        }

        private Transform GetWindowAnchorTransform()
            => GiActiveVolumeWindow.ResolveAnchorTransform(windowCameraTransform);

        private void GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY)
        {
            GiActiveVolumeWindow.GetActiveBounds(
                in volumeWindowState,
                useCameraFollowWindow,
                minX, maxX, minY, maxY,
                out minTileX, out maxTileX, out minTileY, out maxTileY);
        }

        private bool IsBaseTileInActiveBounds(Vector2Int tile)
        {
            return GiActiveVolumeWindow.IsBaseTileInActiveBounds(
                in volumeWindowState,
                useCameraFollowWindow,
                tile,
                minX, maxX, minY, maxY);
        }

        private bool TryGetActiveBoundsForGiRebuild(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY)
        {
            return GiActiveVolumeWindow.TryGetRebuildBounds(
                in volumeWindowState,
                useCameraFollowWindow,
                out minTileX, out maxTileX, out minTileY, out maxTileY);
        }

        private void EnsureTextureBuffers(int voxelCount)
        {
            if (pooledTextureData.Length != voxelCount)
                pooledTextureData = new Color[voxelCount];
            if (pooledShiftBuffer.Length != voxelCount)
                pooledShiftBuffer = new Color[voxelCount];
        }

        private static void FillEmptyTexelsFromNeighbors(Color[] data, int[] counts, int sizeX, int sizeY, int sizeZ)
        {
            // 4 directional passes per Y slice to propagate edge data into empty texels.
            for (int ty = 0; ty < sizeY; ty++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 1; x < sizeX; x++)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int prev = (x - 1) + sizeX * (ty + sizeY * z);
                        if (counts[idx] == 0 && counts[prev] != 0)
                        {
                            data[idx] = data[prev];
                            counts[idx] = -1;
                        }
                    }
                }
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = sizeX - 2; x >= 0; x--)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int next = (x + 1) + sizeX * (ty + sizeY * z);
                        if (counts[idx] == 0 && counts[next] != 0)
                        {
                            data[idx] = data[next];
                            counts[idx] = -1;
                        }
                    }
                }
                for (int x = 0; x < sizeX; x++)
                {
                    for (int z = 1; z < sizeZ; z++)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int prev = x + sizeX * (ty + sizeY * (z - 1));
                        if (counts[idx] == 0 && counts[prev] != 0)
                        {
                            data[idx] = data[prev];
                            counts[idx] = -1;
                        }
                    }
                }
                for (int x = 0; x < sizeX; x++)
                {
                    for (int z = sizeZ - 2; z >= 0; z--)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int next = x + sizeX * (ty + sizeY * (z + 1));
                        if (counts[idx] == 0 && counts[next] != 0)
                        {
                            data[idx] = data[next];
                            counts[idx] = -1;
                        }
                    }
                }
            }
        }

        private void RefreshVolumeWorldYRange()
        {
            if (giGrid != null && giGrid.TryGetWorldYRange(out float minY, out float maxY))
            {
                float cellY = Mathf.Max(1e-4f, gridWorld != null ? gridWorld.CellSizeY : 1f);
                float pad = Mathf.Max(0f, yRangePaddingCells) * cellY;
                float minSpan = Mathf.Max(1f, minWorldYSpanCells) * cellY;
                float observedMin = minY - pad;
                float observedMax = maxY + pad + cellY;
                float center = (observedMin + observedMax) * 0.5f;
                float half = Mathf.Max(minSpan * 0.5f, (observedMax - observedMin) * 0.5f);
                volumeMinWorldY = center - half;
                volumeMaxWorldY = center + half;
            }
            else
            {
                float cellY = Mathf.Max(1e-4f, gridWorld != null ? gridWorld.CellSizeY : 1f);
                float minSpan = Mathf.Max(1f, minWorldYSpanCells) * cellY;
                volumeMinWorldY = 0f;
                volumeMaxWorldY = minSpan;
            }
        }

        private int WorldYToVolumeSlice(float worldY, int sizeY)
        {
            if (sizeY <= 1)
                return 0;

            float span = Mathf.Max(1e-4f, volumeMaxWorldY - volumeMinWorldY);
            float t = Mathf.Clamp01((worldY - volumeMinWorldY) / span);
            return Mathf.Clamp(Mathf.RoundToInt(t * (sizeY - 1)), 0, sizeY - 1);
        }

        private float ComputeYWriteOffset(int sizeY)
        {
            if (sizeY <= 1)
                return 0f;
            float span = Mathf.Max(1e-4f, volumeMaxWorldY - volumeMinWorldY);
            return span / (2f * sizeY);
        }

        private int ComputeMaxUpwardCarrySlices(int sizeY)
        {
            if (sizeY <= 1 || gridWorld == null)
                return 1;
            float span = Mathf.Max(1e-4f, volumeMaxWorldY - volumeMinWorldY);
            float sliceHeight = span / sizeY;
            float halfFloor = Mathf.Max(0.5f, occlusionFloorHeightCells * gridWorld.CellSizeY * 0.4f);
            return Mathf.Clamp(Mathf.CeilToInt(halfFloor / sliceHeight), 1, sizeY);
        }

        private void LogNodeCountIfEnabled()
        {
            if (!logNodeCountDuringPlay || !Application.isPlaying || giGrid == null)
                return;

            nodeCountLogTimer -= Time.deltaTime;
            if (nodeCountLogTimer > 0f)
                return;

            nodeCountLogTimer = Mathf.Max(0.1f, nodeCountLogInterval);
            int nodeCount = giGrid.GetNodeCount();
            int resolution = Mathf.Max(1, giGrid.ResolutionMultiplier);
            GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            int windowWidth = Mathf.Max(1, maxTileX - minTileX + 1);
            int windowDepth = Mathf.Max(1, maxTileY - minTileY + 1);
            int theoreticalMax = windowWidth * windowDepth * resolution * resolution;
            Debug.Log(
                $"GiManager NodeCount: {nodeCount} | Window: {windowWidth}x{windowDepth} | " +
                $"ResolutionMultiplier: {resolution} | TheoreticalMax: {theoreticalMax} | " +
                $"FollowWindow: {useCameraFollowWindow}"
            );
        }

        private void SyncGiMaterialPropertiesThrottled()
        {
            materialSyncTimer -= Time.deltaTime;
            if (materialSyncTimer > 0f && !rendererCacheDirty)
                return;

            RefreshRendererCache();
            SyncGiMaterialProperties();
            materialSyncTimer = Mathf.Max(0f, materialSyncInterval);
        }

        private void RefreshRendererCache()
        {
            if (!rendererCacheDirty && cachedRenderers.Count > 0)
                return;

            cachedRenderers.Clear();
            Renderer[] renderers = FindObjectsOfType<Renderer>();
            if (renderers != null && renderers.Length > 0)
                cachedRenderers.AddRange(renderers);
            rendererCacheDirty = false;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r > 0) != (divisor > 0)))
                q--;
            return q;
        }
    }
}

