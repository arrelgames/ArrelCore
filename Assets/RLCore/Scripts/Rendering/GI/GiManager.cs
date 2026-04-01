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
        private bool hasActiveWindowBounds;
        private int activeMinX, activeMaxX, activeMinY, activeMaxY;
        private Vector2Int activeWindowAnchorTile;
        private int pendingTexelShiftX;
        private int pendingTexelShiftZ;
        private float volumeMinWorldY;
        private float volumeMaxWorldY;
        private float giYWriteOffset;

        private float tickTimer;
        private float rebuildCoalesceTimer;
        private float materialSyncTimer;
        private float nodeCountLogTimer;
        private bool sourceInjectionDirty = true;
        private bool rendererCacheDirty = true;
        private bool pendingCameraFollowDirtyBatch;

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
            if (sourceInjectionDirty)
            {
                giGrid.ClearSources();
                ApplySourcesToGrid();
                sourceInjectionDirty = false;
            }

            giGrid.StepPropagation();

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

            FillEmptyYSlicesBidirectional(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

            if (useCameraFollowWindow)
                FillEmptyTexelsFromNeighbors(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

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

            if (TryApplyPendingTextureShift())
                pendingTexelShiftX = pendingTexelShiftZ = 0;

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

            FillEmptyYSlicesBidirectional(data, scratchTexelCounts, sizeX, sizeY, sizeZ);

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
        }

        private void SyncGiMaterialProperties()
        {
            Texture giTex = Shader.GetGlobalTexture(globalTextureName);
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
            if (!useCameraFollowWindow)
                return;

            Transform anchor = GetWindowAnchorTransform();
            if (anchor == null)
                return;

            Vector2Int tile = WorldToBaseTile(anchor.position);
            SetActiveWindowFromAnchor(tile, force: true, out _, out _);
        }

        private void UpdateCameraFollowWindow()
        {
            if (!useCameraFollowWindow || gridWorld == null || giGrid == null)
                return;

            Transform anchor = GetWindowAnchorTransform();
            if (anchor == null)
                return;

            Vector2Int tile = WorldToBaseTile(anchor.position);
            if (!SetActiveWindowFromAnchor(tile, force: !hasActiveWindowBounds, out int dx, out int dz))
                return;

            if (!buildTexture || giTexture == null)
                return;

            int width = Mathf.Max(1, activeMaxX - activeMinX + 1);
            int depth = Mathf.Max(1, activeMaxY - activeMinY + 1);
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

            pendingTexelShiftX += subShiftX / ds;
            pendingTexelShiftZ += subShiftZ / ds;
            materialSyncTimer = 0f;
            QueueEnteringStripTiles(dx, dz);
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
        {
            if (windowCameraTransform != null)
                return windowCameraTransform;

            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private Vector2Int WorldToBaseTile(Vector3 worldPos)
        {
            float cell = Mathf.Max(1e-4f, gridWorld.CellSizeXZ);
            return new Vector2Int(Mathf.FloorToInt(worldPos.x / cell), Mathf.FloorToInt(worldPos.z / cell));
        }

        private bool SetActiveWindowFromAnchor(Vector2Int anchorTile, bool force, out int dx, out int dz)
        {
            dx = 0;
            dz = 0;

            int width = Mathf.Max(1, windowWidthTiles);
            int depth = Mathf.Max(1, windowDepthTiles);
            int halfW = width / 2;
            int halfD = depth / 2;
            int newMinX = anchorTile.x - halfW;
            int newMinY = anchorTile.y - halfD;
            int newMaxX = newMinX + width - 1;
            int newMaxY = newMinY + depth - 1;

            if (!force && hasActiveWindowBounds &&
                newMinX == activeMinX && newMaxX == activeMaxX &&
                newMinY == activeMinY && newMaxY == activeMaxY)
            {
                return false;
            }

            if (hasActiveWindowBounds)
            {
                dx = anchorTile.x - activeWindowAnchorTile.x;
                dz = anchorTile.y - activeWindowAnchorTile.y;
            }

            activeMinX = newMinX;
            activeMaxX = newMaxX;
            activeMinY = newMinY;
            activeMaxY = newMaxY;
            activeWindowAnchorTile = anchorTile;
            hasActiveWindowBounds = true;
            return true;
        }

        private void GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY)
        {
            if (useCameraFollowWindow && hasActiveWindowBounds)
            {
                minTileX = activeMinX;
                maxTileX = activeMaxX;
                minTileY = activeMinY;
                maxTileY = activeMaxY;
                return;
            }

            minTileX = minX;
            maxTileX = maxX;
            minTileY = minY;
            maxTileY = maxY;
        }

        private bool IsBaseTileInActiveBounds(Vector2Int tile)
        {
            GetActiveBounds(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            return tile.x >= minTileX && tile.x <= maxTileX && tile.y >= minTileY && tile.y <= maxTileY;
        }

        private bool TryGetActiveBoundsForGiRebuild(out int minTileX, out int maxTileX, out int minTileY, out int maxTileY)
        {
            minTileX = maxTileX = minTileY = maxTileY = 0;
            if (!useCameraFollowWindow || !hasActiveWindowBounds)
                return false;

            minTileX = activeMinX;
            maxTileX = activeMaxX;
            minTileY = activeMinY;
            maxTileY = activeMaxY;
            return true;
        }

        private void QueueEnteringStripTiles(int dx, int dz)
        {
            enteringStripTilesBuffer.Clear();
            if (dx == 0 && dz == 0)
                return;

            int minTileX = activeMinX;
            int maxTileX = activeMaxX;
            int minTileY = activeMinY;
            int maxTileY = activeMaxY;

            if (dx > 0)
            {
                int startX = Mathf.Max(minTileX, maxTileX - dx + 1);
                for (int x = startX; x <= maxTileX; x++)
                    for (int y = minTileY; y <= maxTileY; y++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
            else if (dx < 0)
            {
                int endX = Mathf.Min(maxTileX, minTileX - dx - 1);
                for (int x = minTileX; x <= endX; x++)
                    for (int y = minTileY; y <= maxTileY; y++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }

            if (dz > 0)
            {
                int startY = Mathf.Max(minTileY, maxTileY - dz + 1);
                for (int y = startY; y <= maxTileY; y++)
                    for (int x = minTileX; x <= maxTileX; x++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
            else if (dz < 0)
            {
                int endY = Mathf.Min(maxTileY, minTileY - dz - 1);
                for (int y = minTileY; y <= endY; y++)
                    for (int x = minTileX; x <= maxTileX; x++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
        }

        private bool TryApplyPendingTextureShift()
        {
            if (giTexture == null)
                return false;

            if (pendingTexelShiftX == 0 && pendingTexelShiftZ == 0)
                return false;

            int shiftX = pendingTexelShiftX;
            int shiftZ = pendingTexelShiftZ;
            int sizeX = giTexture.width;
            int sizeY = giTexture.height;
            int sizeZ = giTexture.depth;
            if (Mathf.Abs(shiftX) >= sizeX || Mathf.Abs(shiftZ) >= sizeZ)
                return false;

            int voxelCount = sizeX * sizeY * sizeZ;
            EnsureTextureBuffers(voxelCount);
            Color[] src = pooledTextureData;
            Color[] dst = pooledShiftBuffer;
            System.Array.Clear(dst, 0, voxelCount);
            for (int z = 0; z < sizeZ; z++)
            {
                int fromZ = z + shiftZ;
                if (fromZ < 0 || fromZ >= sizeZ)
                    continue;
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int fromX = x + shiftX;
                        if (fromX < 0 || fromX >= sizeX)
                            continue;

                        int dstIdx = x + sizeX * (y + sizeY * z);
                        int srcIdx = fromX + sizeX * (y + sizeY * fromZ);
                        dst[dstIdx] = src[srcIdx];
                    }
                }
            }

            // Fill entering strip edges by replicating the nearest interior texel
            // so the visual transition is smooth instead of a black flash.
            for (int z = 0; z < sizeZ; z++)
            {
                int clampedZ = z + shiftZ;
                if (clampedZ < 0) clampedZ = 0;
                else if (clampedZ >= sizeZ) clampedZ = sizeZ - 1;

                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int fromX = x + shiftX;
                        int fromZ = z + shiftZ;
                        bool outOfBounds = fromX < 0 || fromX >= sizeX || fromZ < 0 || fromZ >= sizeZ;
                        if (!outOfBounds)
                            continue;

                        int clampedX = fromX < 0 ? 0 : (fromX >= sizeX ? sizeX - 1 : fromX);
                        int clampedFromZ = fromZ < 0 ? 0 : (fromZ >= sizeZ ? sizeZ - 1 : fromZ);
                        int dstIdx = x + sizeX * (y + sizeY * z);
                        int edgeSrcIdx = clampedX + sizeX * (y + sizeY * clampedFromZ);
                        dst[dstIdx] = src[edgeSrcIdx];
                    }
                }
            }

            giTexture.SetPixels(dst);
            giTexture.Apply(false, false);
            var tmp = pooledTextureData;
            pooledTextureData = pooledShiftBuffer;
            pooledShiftBuffer = tmp;
            return true;
        }

        private void EnsureTextureBuffers(int voxelCount)
        {
            if (pooledTextureData.Length != voxelCount)
                pooledTextureData = new Color[voxelCount];
            if (pooledShiftBuffer.Length != voxelCount)
                pooledShiftBuffer = new Color[voxelCount];
        }

        /// <summary>
        /// For each XZ column, fill empty Y slices using distance-weighted blending
        /// between the nearest data slices above and below. Slices between two floors
        /// get a proximity-weighted mix (ceiling sees mostly its own floor's data).
        /// Slices above the highest data carry upward. Slices below the lowest stay
        /// empty so floor undersides facing away from all light remain dark.
        /// </summary>
        private static void FillEmptyYSlicesBidirectional(Color[] data, int[] counts, int sizeX, int sizeY, int sizeZ)
        {
            for (int tz = 0; tz < sizeZ; tz++)
            {
                for (int tx = 0; tx < sizeX; tx++)
                {
                    int highestData = -1;

                    Color belowColor = Color.black;
                    int belowSlice = -1;

                    for (int ty = 0; ty < sizeY; ty++)
                    {
                        int idx = tx + sizeX * (ty + sizeY * tz);
                        if (counts[idx] > 0)
                        {
                            if (belowSlice >= 0 && ty - belowSlice > 1)
                            {
                                Color aboveColor = data[idx];
                                int gap = ty - belowSlice;
                                for (int fy = belowSlice + 1; fy < ty; fy++)
                                {
                                    int distBelow = fy - belowSlice;
                                    int distAbove = ty - fy;
                                    float total = distBelow + distAbove;
                                    float wBelow = distAbove / total;
                                    float wAbove = distBelow / total;
                                    int fIdx = tx + sizeX * (fy + sizeY * tz);
                                    data[fIdx] = belowColor * wBelow + aboveColor * wAbove;
                                    counts[fIdx] = -2;
                                }
                            }

                            belowColor = data[idx];
                            belowSlice = ty;
                            highestData = ty;
                        }
                    }

                    if (highestData >= 0)
                    {
                        int hIdx = tx + sizeX * (highestData + sizeY * tz);
                        Color carry = data[hIdx];
                        for (int ty = highestData + 1; ty < sizeY; ty++)
                        {
                            int idx = tx + sizeX * (ty + sizeY * tz);
                            if (counts[idx] == 0)
                            {
                                data[idx] = carry;
                                counts[idx] = -2;
                            }
                        }
                    }
                }
            }
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

