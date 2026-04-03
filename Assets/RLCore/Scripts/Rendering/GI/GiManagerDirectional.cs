using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Grid GI driven by a dominant directional sun: per-node transmittance (vertical fast path or full line trace),
    /// optional luma/RGB bounce, camera-follow volume, and gap-aware 3D texture fill. Disable <see cref="GiManager"/> when using this.
    /// </summary>
    [DefaultExecutionOrder(-499)]
    public sealed class GiManagerDirectional : MonoBehaviour
    {
        public static GiManagerDirectional Instance { get; private set; }

        [Header("Sun")]
        [Tooltip("If set, uses transform.forward as the direction toward which light travels (matches GiSource Directional / Unity lights).")]
        [SerializeField] private Transform sunDirectionReference;

        [Tooltip("Used when Sun Direction Reference is null.")]
        [SerializeField] private Vector3 sunDirectionWorld = new Vector3(0f, -1f, 0f);

        [ColorUsage(true, true)]
        [SerializeField] private Color sunIrradiance = Color.white;

        [Min(0.1f)]
        [SerializeField] private float sunMaxTraceDistanceWorld = 48f;

        [SerializeField] private bool respectOcclusion = true;

        [Header("Quality")]
        [SerializeField] private GiDirectionalQualityPreset qualityPreset = GiDirectionalQualityPreset.Balanced;

        [Tooltip("When true, skips interpolating across large empty Y gaps in the volume texture (reduces floor-to-floor bleed).")]
        [SerializeField] private bool useGapAwareYFill = true;

        [Min(1)]
        [SerializeField] private int maxYGapInterpolationSlices = 3;

        [Header("Indirect bounce")]
        [SerializeField] private bool enableBounceFeedback = true;

        [Range(0f, 1f)]
        [SerializeField] private float bounceAlbedo = 0.12f;

        [Min(0f)]
        [SerializeField] private float maxBounce = 0f;

        [SerializeField] private bool useNeighborAverageForBounce = false;

        [SerializeField] private bool bounceOnlyWhereDirectNonZero = true;

        [Header("Propagation")]
        [Range(0f, 1f)]
        [SerializeField] private float damping = 0.95f;

        [SerializeField] private float tickInterval = 0.1f;

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

        [SerializeField] private string globalDirectionalActiveName = "_GiDirectionalActive";

        [SerializeField] private string globalSunDirName = "_GiSunDirWorld";

        [Range(0f, 8f)]
        [SerializeField] private float giIntensity = 1f;

        [Header("Shader Binding")]
        [SerializeField] private bool forceBindGiMaterialProperties = true;

        [Min(0f)]
        [SerializeField] private float materialSyncInterval = 1f;

        [Header("XZ sampling")]
        [Min(1)]
        [SerializeField] private int xzDownsample = 2;

        [Min(1)]
        [SerializeField] private int yResolution = 8;

        [Min(0f)]
        [SerializeField] private float yRangePaddingCells = 0.5f;

        [Min(1f)]
        [SerializeField] private float minWorldYSpanCells = 4f;

        [Header("GI Resolution")]
        [Min(1)]
        [SerializeField] private int giResolutionMultiplier = 1;

        [Header("Runtime Rebuild")]
        [Min(1)]
        [SerializeField] private int fullRebuildDirtyTileThreshold = 24;

        [Range(0f, 1f)]
        [SerializeField] private float fullRebuildDirtyNodeFraction = 0.35f;

        [Min(0f)]
        [SerializeField] private float rebuildCoalesceWindow = 0.05f;

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

        [SerializeField] private bool useSquareFollowDeadzone = false;

        [Min(0)]
        [SerializeField] private int squareDeadzoneRadiusTiles = 4;

        [Header("Future / stubs")]
        [Tooltip("Reserved for a second Texture3D (e.g. L1 / hemisphere). Not used yet.")]
        [SerializeField] private bool secondHarmonicVolumePlanned = false;

        [Tooltip("Reserved: extra deck-height probes per column. Not implemented in this version.")]
        [SerializeField] private bool enableDeckHeightProbes = false;

        [Header("Debug")]
        [SerializeField] private bool drawNodeGizmos = false;

        [SerializeField] private float gizmoSphereRadius = 0.15f;

        private GiGrid giGrid;
        private GiDirectionalGrid directionalGrid;
        private Texture3D giTexture;
        private GridWorld gridWorld;
        private GiActiveVolumeWindow.RuntimeState volumeWindowState;

        private int minX, maxX, minY, maxY;
        private readonly List<Renderer> cachedRenderers = new();
        private readonly List<Vector2Int> giDirtyTilesBuffer = new();
        private readonly HashSet<Vector2Int> pendingGiDirtyTiles = new();
        private readonly HashSet<int> scratchTexelIndices = new();
        private readonly List<Vector2Int> enteringStripTilesBuffer = new();
        private readonly List<Vector2Int> reusableSubcellsBuffer = new();
        private int[] scratchTexelCounts = System.Array.Empty<int>();
        private Color[] pooledTextureData = System.Array.Empty<Color>();
        private Color[] pooledShiftBuffer = System.Array.Empty<Color>();

        private float volumeMinWorldY;
        private float volumeMaxWorldY;
        private float giYWriteOffset;

        private float tickTimer;
        private float rebuildCoalesceTimer;
        private float materialSyncTimer;
        private bool rendererCacheDirty = true;
        private bool pendingCameraFollowDirtyBatch;
        private bool lastBounceEnabled;

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
                Debug.LogError("GiManagerDirectional: GridWorld.Instance is null.");
                enabled = false;
                return;
            }

            TryInitializeGrid(force: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Shader.SetGlobalFloat(globalDirectionalActiveName, 0f);
            ClearShaderVolumeGlobals();
        }

        private void Update()
        {
            _ = secondHarmonicVolumePlanned;
            _ = enableDeckHeightProbes;

            if (gridWorld == null)
                return;

            if (giGrid == null || giGrid.Nodes.Count == 0)
                TryInitializeGrid(force: false);

            if (giGrid == null || giGrid.Nodes.Count == 0)
                return;

            UpdateCameraFollowWindow();
            giGrid.OcclusionFloorHeightCells = occlusionFloorHeightCells;
            giGrid.OcclusionCutoff = occlusionCutoff;
            giGrid.BounceFeedbackEnabled = enableBounceFeedback;
            if (lastBounceEnabled && !enableBounceFeedback)
                giGrid.ClearBounceSources();
            lastBounceEnabled = enableBounceFeedback;

            int clampedMultiplier = Mathf.Max(1, giResolutionMultiplier);
            if (giGrid.ResolutionMultiplier != clampedMultiplier)
            {
                giGrid.ResolutionMultiplier = clampedMultiplier;
                ForceFullGiRebuild();
            }

            bool rebuilt = ProcessGiRebuildRequests();
            PublishShaderGlobals();
            if (forceBindGiMaterialProperties)
                SyncGiMaterialPropertiesThrottled();

            tickTimer += Time.deltaTime;
            if (tickTimer < tickInterval)
                return;
            tickTimer = 0f;

            Vector3 lightDir = ResolveSunDirectionWorld();
            directionalGrid.ApplyDirectionalSun(
                lightDir,
                sunIrradiance,
                sunMaxTraceDistanceWorld,
                respectOcclusion,
                preferVerticalColumnTransmittance: true,
                verticalDownMinAbsY: GetVerticalFastPathMinAbsY());

            giGrid.PublishDirectAndBounceToCurrent(damping);

            var bounceSettings = new GiBounceSettings
            {
                bounceAlbedo = bounceAlbedo,
                maxBounce = maxBounce,
                useNeighborAverageForBounce = useNeighborAverageForBounce,
                bounceOnlyWhereDirectNonZero = bounceOnlyWhereDirectNonZero
            };

            if (enableBounceFeedback)
            {
                if (UseLumaBounceForPreset())
                    giGrid.UpdateBounceFromCurrentLumaOnly(bounceSettings);
                else
                    giGrid.UpdateBounceFromCurrent(bounceSettings);
            }

            if (buildTexture)
                UpdateTextureFromGrid();
        }

        private bool UseLumaBounceForPreset() => qualityPreset != GiDirectionalQualityPreset.High;

        private float GetVerticalFastPathMinAbsY()
        {
            return qualityPreset switch
            {
                GiDirectionalQualityPreset.Performance => 0.92f,
                GiDirectionalQualityPreset.Balanced => 0.88f,
                GiDirectionalQualityPreset.High => 0.85f,
                _ => 0.88f
            };
        }

        private int GetXzBlurRadius()
        {
            return qualityPreset switch
            {
                GiDirectionalQualityPreset.Performance => 0,
                GiDirectionalQualityPreset.Balanced => 1,
                GiDirectionalQualityPreset.High => 2,
                _ => 1
            };
        }

        private Vector3 ResolveSunDirectionWorld()
        {
            if (sunDirectionReference != null)
            {
                Vector3 f = sunDirectionReference.forward;
                return f.sqrMagnitude > 1e-6f ? f.normalized : sunDirectionWorld.normalized;
            }

            return sunDirectionWorld.sqrMagnitude > 1e-6f ? sunDirectionWorld.normalized : Vector3.down;
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
                DiffusionStrength = 0f,
                Damping = damping,
                OcclusionFloorHeightCells = occlusionFloorHeightCells,
                OcclusionCutoff = occlusionCutoff,
                UseJobsBurst = false,
                BounceFeedbackEnabled = enableBounceFeedback,
                RestrictPropagationToSameVerticalLayer = true,
                ResolutionMultiplier = Mathf.Max(1, giResolutionMultiplier)
            };

            directionalGrid = new GiDirectionalGrid(giGrid);

            minX = maxX = minY = maxY = 0;
            ComputeGridExtents(gridWorld);
            InitializeWindowBoundsIfNeeded();
            if (TryGetActiveBoundsForGiRebuild(out int rminX, out int rmaxX, out int rminY, out int rmaxY))
                giGrid.RebuildAll(rminX, rmaxX, rminY, rmaxY);
            else
                giGrid.RebuildAll();

            if (buildTexture)
            {
                EnsureTextureSizeMatchesGrid();
                UpdateTextureFromGrid();
            }

            rendererCacheDirty = true;
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
                    Mathf.CeilToInt(giGrid.Nodes.Count * Mathf.Clamp01(fullRebuildDirtyNodeFraction)));
            }

            bool fullRebuild = giDirtyTilesBuffer.Count >= absoluteThreshold ||
                               giDirtyTilesBuffer.Count >= fractionalThreshold ||
                               giDirtyTilesBuffer.Count > Mathf.Max(1, maxWindowDirtyTilesPerFrame);
            if (pendingCameraFollowDirtyBatch)
            {
                int cameraFollowHardLimit = Mathf.Max(1, windowWidthTiles * windowDepthTiles);
                fullRebuild = giDirtyTilesBuffer.Count >= cameraFollowHardLimit ||
                              giDirtyTilesBuffer.Count > Mathf.Max(1, maxWindowDirtyTilesPerFrame);
            }

            if (fullRebuild)
            {
                if (TryGetActiveBoundsForGiRebuild(out int rminX, out int rmaxX, out int rminY, out int rmaxY))
                    giGrid.RebuildAll(rminX, rmaxX, rminY, rmaxY);
                else
                    giGrid.RebuildAll();
            }
            else
            {
                if (TryGetActiveBoundsForGiRebuild(out int rminX, out int rmaxX, out int rminY, out int rmaxY))
                    giGrid.RebuildRegion(giDirtyTilesBuffer, rminX, rmaxX, rminY, rmaxY);
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
            {
                pendingCameraFollowDirtyBatch = false;
                return true;
            }

            if (extentsChanged || giTexture == null)
                EnsureTextureSizeMatchesGrid();

            if (fullRebuild || extentsChanged)
                UpdateTextureFromGrid();
            else
                UpdateTextureFromDirtyTiles(giDirtyTilesBuffer);

            pendingCameraFollowDirtyBatch = false;
            return true;
        }

        public void ForceFullGiRebuild()
        {
            if (giGrid == null || gridWorld == null)
                return;

            if (TryGetActiveBoundsForGiRebuild(out int rminX, out int rmaxX, out int rminY, out int rmaxY))
                giGrid.RebuildAll(rminX, rmaxX, rminY, rmaxY);
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
            rendererCacheDirty = true;
        }

        public static Color SampleGi(Vector3 worldPos)
        {
            if (Instance == null || Instance.giGrid == null)
                return Color.black;
            return Instance.giGrid.SampleAtWorldPos(worldPos);
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
                name = "GiVolumeDirectional",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Shader.SetGlobalTexture(globalTextureName, giTexture);
            EnsureTextureBuffers(sizeX * yResolution * sizeZ);
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
                return;

            if (giTexture != null)
                Destroy(giTexture);

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
            Color[] data = pooledTextureData;
            System.Array.Clear(data, 0, voxelCount);
            EnsureScratchTexelCountsSize(voxelCount);
            System.Array.Clear(scratchTexelCounts, 0, voxelCount);

            int nodeCount = giGrid.GetNodeCount();
            if (nodeCount == 0)
            {
                giTexture.SetPixels(data);
                giTexture.Apply(false, false);
                PublishShaderGlobals();
                return;
            }

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
                float fAbove = giGrid.GetSourceFractionAbove(i);
                Color irr = giGrid.GetCurrentIrradianceAt(i);
                Color irrTop = irr * fAbove;
                Color irrBot = irr * (1f - fAbove);

                int idxTop = tx + sizeX * (tyTop + sizeY * tz);
                data[idxTop] += irrTop;
                scratchTexelCounts[idxTop]++;
                if (tyBot != tyTop && irrBot.maxColorComponent > 1e-6f)
                {
                    int idxBot = tx + sizeX * (tyBot + sizeY * tz);
                    data[idxBot] += irrBot;
                    scratchTexelCounts[idxBot]++;
                }
            }

            for (int i = 0; i < voxelCount; i++)
            {
                int count = scratchTexelCounts[i];
                if (count > 1)
                    data[i] /= count;
            }

            int maxCarry = ComputeMaxUpwardCarrySlices(sizeY);
            if (useGapAwareYFill)
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesGapAware(
                    data, scratchTexelCounts, sizeX, sizeY, sizeZ, maxYGapInterpolationSlices, maxCarry);
            }
            else
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesBidirectional(
                    data, scratchTexelCounts, sizeX, sizeY, sizeZ, maxCarry);
            }

            if (useCameraFollowWindow)
                GiVolumeTextureUtilities.FillEmptyTexelsFromNeighbors(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

            int blur = GetXzBlurRadius();
            if (blur > 0)
                GiVolumeTextureUtilities.SeparableBoxBlurXZ(data, sizeX, sizeY, sizeZ, blur);

            giTexture.SetPixels(data);
            giTexture.Apply(false, false);
            PublishShaderGlobals();
        }

        private void UpdateTextureFromDirtyTiles(IReadOnlyList<Vector2Int> dirtyTiles)
        {
            if (giTexture == null || dirtyTiles == null || dirtyTiles.Count == 0)
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
                        scratchTexelIndices.Add(tx + sizeX * (ty + sizeY * tz));
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

            int maxCarry = ComputeMaxUpwardCarrySlices(sizeY);
            if (useGapAwareYFill)
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesGapAware(
                    data, scratchTexelCounts, sizeX, sizeY, sizeZ, maxYGapInterpolationSlices, maxCarry);
            }
            else
            {
                GiVolumeTextureUtilities.FillEmptyYSlicesBidirectional(
                    data, scratchTexelCounts, sizeX, sizeY, sizeZ, maxCarry);
            }

            int blur = GetXzBlurRadius();
            if (blur > 0)
                GiVolumeTextureUtilities.SeparableBoxBlurXZ(data, sizeX, sizeY, sizeZ, blur);

            giTexture.SetPixels(data);
            giTexture.Apply(false, false);
            PublishShaderGlobals();
        }

        private void EnsureScratchTexelCountsSize(int size)
        {
            if (scratchTexelCounts.Length == size)
                return;
            scratchTexelCounts = new int[size];
        }

        private void EnsureTextureBuffers(int voxelCount)
        {
            if (pooledTextureData.Length != voxelCount)
                pooledTextureData = new Color[voxelCount];
            if (pooledShiftBuffer.Length != voxelCount)
                pooledShiftBuffer = new Color[voxelCount];
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

            GiShaderGlobals.BindNeutralShGlobals();

            Vector3 sunDir = ResolveSunDirectionWorld();
            Shader.SetGlobalFloat(globalDirectionalActiveName, 1f);
            Shader.SetGlobalVector(globalSunDirName, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        }

        private void ClearShaderVolumeGlobals()
        {
            Shader.SetGlobalFloat(globalIntensityName, 0f);
            Shader.SetGlobalVector(globalGridMinName, Vector4.zero);
            Shader.SetGlobalVector(globalGridMaxName, Vector4.zero);
            Shader.SetGlobalVector(globalGridSizeName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeSizeName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeParamsName, Vector4.zero);
            Shader.SetGlobalVector(globalVolumeYParamsName, Vector4.zero);
            GiShaderGlobals.BindNeutralShGlobals();
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

        private void SyncGiMaterialProperties()
        {
            Texture giTex = Shader.GetGlobalTexture(globalTextureName);
            Vector4 giParams = Shader.GetGlobalVector(globalVolumeParamsName);
            Vector4 giParamsY = Shader.GetGlobalVector(globalVolumeYParamsName);
            float giInt = Shader.GetGlobalFloat(globalIntensityName);
            float dirActive = Shader.GetGlobalFloat(globalDirectionalActiveName);
            Vector4 sunDir = Shader.GetGlobalVector(globalSunDirName);

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
                        mat.SetTexture("_GiVolume", giTex);
                    if (mat.HasProperty("_GIVolume"))
                        mat.SetTexture("_GIVolume", giTex);
                    if (mat.HasProperty("_GiVolumeParams"))
                        mat.SetVector("_GiVolumeParams", giParams);
                    if (mat.HasProperty("_GiIntensity"))
                        mat.SetFloat("_GiIntensity", giInt);
                    if (mat.HasProperty("_GiVolumeParamsY"))
                        mat.SetVector("_GiVolumeParamsY", giParamsY);
                    if (mat.HasProperty("_GiSampleNormalBias"))
                        mat.SetFloat("_GiSampleNormalBias", giYWriteOffset);
                    if (mat.HasProperty(globalDirectionalActiveName))
                        mat.SetFloat(globalDirectionalActiveName, dirActive);
                    if (mat.HasProperty(globalSunDirName))
                        mat.SetVector(globalSunDirName, sunDir);
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh0))
                        mat.SetTexture(GiShaderGlobals.VolumeSh0, GiShaderGlobals.NeutralVolume1x1);
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh1))
                        mat.SetTexture(GiShaderGlobals.VolumeSh1, GiShaderGlobals.NeutralVolume1x1);
                    if (mat.HasProperty(GiShaderGlobals.VolumeSh2))
                        mat.SetTexture(GiShaderGlobals.VolumeSh2, GiShaderGlobals.NeutralVolume1x1);
                    if (mat.HasProperty(GiShaderGlobals.UseSH))
                        mat.SetFloat(GiShaderGlobals.UseSH, 0f);
                    if (mat.HasProperty(GiShaderGlobals.ShTier))
                        mat.SetFloat(GiShaderGlobals.ShTier, 0f);
                }
            }
        }

        private void InitializeWindowBoundsIfNeeded()
        {
            if (!useCameraFollowWindow || gridWorld == null)
                return;

            Transform anchor = GiActiveVolumeWindow.ResolveAnchorTransform(windowCameraTransform);
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

            Transform anchor = GiActiveVolumeWindow.ResolveAnchorTransform(windowCameraTransform);
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
            int res = Mathf.Max(1, giGrid.ResolutionMultiplier);
            int subShiftX = dx * res;
            int subShiftZ = dz * res;
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
                tickTimer = Mathf.Max(tickTimer, tickInterval);
            }
        }

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

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r > 0) != (divisor > 0)))
                q--;
            return q;
        }

        private void OnDrawGizmos()
        {
            if (!drawNodeGizmos || giGrid == null)
                return;

            foreach (GiNode node in giGrid.Nodes)
            {
                float intensity = node.currentIrradiance.maxColorComponent;
                if (intensity <= 0.001f)
                    continue;

                Gizmos.color = node.currentIrradiance;
                Gizmos.DrawSphere(node.worldPos, gizmoSphereRadius);
            }
        }
    }
}
