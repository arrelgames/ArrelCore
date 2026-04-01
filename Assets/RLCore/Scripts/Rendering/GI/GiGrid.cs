using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

namespace RLGames
{
    /// <summary>
    /// Lightweight, gameplay-agnostic data model for grid-based GI.
    /// Backed by <see cref="GridWorld"/> tiles with per-stack vertical GI layers.
    /// </summary>
    [Serializable]
    public struct GiNode
    {
        public int index;
        public Vector2Int gridPos;
        public int verticalLayer;
        public Vector3 worldPos;

        // Irradiance in linear RGB (approximate).
        public Color currentIrradiance;
        public Color sourceIrradiance;
    }

    /// <summary>
    /// Parameters for <see cref="GiGrid.UpdateBounceFromCurrent"/> (indirect feedback from propagated irradiance).
    /// </summary>
    public struct GiBounceSettings
    {
        public float bounceAlbedo;
        /// <summary>When &gt; 0, clamps each RGB channel after albedo scaling.</summary>
        public float maxBounce;
        public bool useNeighborAverageForBounce;
    }

    /// <summary>
    /// Pure data container + propagation logic for GI values on top of the existing GridWorld.
    /// Does not touch gameplay/navigation and is intended to be driven by <see cref="GiManager"/>.
    /// </summary>
    public sealed class GiGrid
    {
        private readonly struct GiNodeKey : IEquatable<GiNodeKey>
        {
            public readonly Vector2Int subPos;
            public readonly int layer;

            public GiNodeKey(Vector2Int subPos, int layer)
            {
                this.subPos = subPos;
                this.layer = layer;
            }

            public bool Equals(GiNodeKey other) => subPos == other.subPos && layer == other.layer;
            public override bool Equals(object obj) => obj is GiNodeKey other && Equals(other);
            public override int GetHashCode() => (subPos.GetHashCode() * 397) ^ layer;
        }

        private readonly GridWorld gridWorld;

        // Compact arrays for cache-friendly iteration.
        private readonly List<GiNode> nodes = new();
        private readonly List<int[]> neighbors = new();

        // Lookup from XZ+layer key to node index, plus per-subcell fanout.
        private readonly Dictionary<GiNodeKey, int> indexByPosLayer = new();
        private readonly Dictionary<Vector2Int, List<int>> indicesBySubcell = new();

        // Double-buffer for propagation.
        private Color[] workingIrradiance;
        private Color[] currentIrradianceByIndex = Array.Empty<Color>();
        /// <summary>Direct irradiance from <see cref="GiSource"/> only; cleared by <see cref="ClearSources"/>.</summary>
        private Color[] sourceIrradianceByIndex = Array.Empty<Color>();
        private Color[] sourceIrradianceFromAboveByIndex = Array.Empty<Color>();
        /// <summary>Indirect term from previous tick; merged with direct for propagation. Not cleared with <see cref="ClearSources"/>.</summary>
        private Color[] bounceSourceIrradianceByIndex = Array.Empty<Color>();
        private Vector2Int[] gridPosByIndex = Array.Empty<Vector2Int>();
        private int[] verticalLayerByIndex = Array.Empty<int>();
        private Vector3[] worldPosByIndex = Array.Empty<Vector3>();
        private bool nodesNeedSyncFromArrays;

        private int resolutionMultiplier = 1;
        public bool UseJobsBurst { get; set; }

        /// <summary>
        /// When true, <see cref="StepPropagation"/> uses the main-thread neighbor path and merges bounce sources.
        /// Burst propagation does not match neighbor diffusion; keep this in sync with GiManager bounce toggle.
        /// </summary>
        public bool BounceFeedbackEnabled { get; set; }

        public IReadOnlyList<GiNode> Nodes
        {
            get
            {
                SyncNodesFromArraysIfNeeded();
                return nodes;
            }
        }
        public int ResolutionMultiplier
        {
            get => Mathf.Max(1, resolutionMultiplier);
            set => resolutionMultiplier = Mathf.Max(1, value);
        }

        // Propagation parameters (tuned by GiManager).
        public float DiffusionStrength { get; set; } = 0.6f;
        public float Damping { get; set; } = 0.9f;
        public float OcclusionFloorHeightCells { get; set; } = 4f;
        public float OcclusionCutoff { get; set; } = 0.05f;

        public GiGrid(GridWorld world)
        {
            gridWorld = world ?? throw new ArgumentNullException(nameof(world));
            RebuildAll();
        }

        public void RebuildAll()
        {
            RebuildAllInternal(useBounds: false, 0, 0, 0, 0);
        }

        public void RebuildAll(int minBaseX, int maxBaseX, int minBaseY, int maxBaseY)
        {
            RebuildAllInternal(useBounds: true, minBaseX, maxBaseX, minBaseY, maxBaseY);
        }

        private void RebuildAllInternal(bool useBounds, int minBaseX, int maxBaseX, int minBaseY, int maxBaseY)
        {
            nodes.Clear();
            neighbors.Clear();
            indexByPosLayer.Clear();
            indicesBySubcell.Clear();

            IReadOnlyDictionary<Vector2Int, GridStack> stacks = gridWorld.GetAllStacks();
            foreach (var kvp in stacks)
            {
                Vector2Int basePos = kvp.Key;
                if (useBounds && !IsBaseTileInBounds(basePos, minBaseX, maxBaseX, minBaseY, maxBaseY))
                    continue;
                if (!TryGetWalkableSurfaceHeights(basePos, out List<float> surfaceHeights) || surfaceHeights.Count == 0)
                    continue;

                int m = ResolutionMultiplier;
                for (int sx = 0; sx < m; sx++)
                {
                    for (int sz = 0; sz < m; sz++)
                    {
                        Vector2Int subPos = new Vector2Int(basePos.x * m + sx, basePos.y * m + sz);
                        for (int layer = 0; layer < surfaceHeights.Count; layer++)
                        {
                            GiNode node = BuildNode(subPos, layer, surfaceHeights[layer]);
                            node.index = nodes.Count;
                            indexByPosLayer[new GiNodeKey(subPos, layer)] = node.index;
                            nodes.Add(node);
                            neighbors.Add(Array.Empty<int>());
                        }
                    }
                }
            }

            RebuildNeighbors();
            workingIrradiance = new Color[nodes.Count];
            RebuildArrayStorageFromNodes();
            ValidateIndexIntegrity();
        }

        public void RebuildRegion(IReadOnlyList<Vector2Int> dirtyTiles)
        {
            RebuildRegionInternal(dirtyTiles, useBounds: false, 0, 0, 0, 0);
        }

        public void RebuildRegion(IReadOnlyList<Vector2Int> dirtyTiles, int minBaseX, int maxBaseX, int minBaseY, int maxBaseY)
        {
            RebuildRegionInternal(dirtyTiles, useBounds: true, minBaseX, maxBaseX, minBaseY, maxBaseY);
        }

        private void RebuildRegionInternal(IReadOnlyList<Vector2Int> dirtyTiles, bool useBounds, int minBaseX, int maxBaseX, int minBaseY, int maxBaseY)
        {
            if (dirtyTiles == null || dirtyTiles.Count == 0)
                return;

            var affected = new HashSet<Vector2Int>();
            for (int i = 0; i < dirtyTiles.Count; i++)
            {
                Vector2Int p = dirtyTiles[i]; // base-tile coordinates
                AddAllSubcellsForBaseTile(p, affected);
                foreach (Vector2Int dir in GridUtilities.AllDirs())
                    AddAllSubcellsForBaseTile(p + dir, affected);
            }

            var preservedByPosLayer = new Dictionary<GiNodeKey, GiNode>();
            var survivors = new List<GiNode>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                GiNode node = nodes[i];
                Vector2Int nodeBase = SubcellToBaseTile(node.gridPos);
                if (useBounds && !IsBaseTileInBounds(nodeBase, minBaseX, maxBaseX, minBaseY, maxBaseY))
                    continue;
                if (affected.Contains(node.gridPos))
                {
                    preservedByPosLayer[new GiNodeKey(node.gridPos, node.verticalLayer)] = node;
                    continue;
                }

                survivors.Add(node);
            }

            nodes.Clear();
            indexByPosLayer.Clear();
            indicesBySubcell.Clear();
            neighbors.Clear();

            for (int i = 0; i < survivors.Count; i++)
            {
                GiNode node = survivors[i];
                node.index = nodes.Count;
                nodes.Add(node);
                indexByPosLayer[new GiNodeKey(node.gridPos, node.verticalLayer)] = node.index;
                neighbors.Add(Array.Empty<int>());
            }

            foreach (Vector2Int subPos in affected)
            {
                Vector2Int basePos = SubcellToBaseTile(subPos);
                if (useBounds && !IsBaseTileInBounds(basePos, minBaseX, maxBaseX, minBaseY, maxBaseY))
                    continue;
                if (!TryGetWalkableSurfaceHeights(basePos, out List<float> surfaceHeights) || surfaceHeights.Count == 0)
                    continue;
                for (int layer = 0; layer < surfaceHeights.Count; layer++)
                {
                    GiNode rebuilt = BuildNode(subPos, layer, surfaceHeights[layer]);
                    if (preservedByPosLayer.TryGetValue(new GiNodeKey(subPos, layer), out GiNode old))
                    {
                        rebuilt.currentIrradiance = old.currentIrradiance;
                        rebuilt.sourceIrradiance = old.sourceIrradiance;
                    }

                    rebuilt.index = nodes.Count;
                    nodes.Add(rebuilt);
                    indexByPosLayer[new GiNodeKey(subPos, layer)] = rebuilt.index;
                    neighbors.Add(Array.Empty<int>());
                }
            }

            RebuildNeighbors();
            workingIrradiance = new Color[nodes.Count];
            RebuildArrayStorageFromNodes();
            ValidateIndexIntegrity();
        }

        private static bool IsBaseTileInBounds(Vector2Int baseTile, int minBaseX, int maxBaseX, int minBaseY, int maxBaseY)
        {
            return baseTile.x >= minBaseX && baseTile.x <= maxBaseX &&
                   baseTile.y >= minBaseY && baseTile.y <= maxBaseY;
        }

        private bool TryGetWalkableSurfaceHeights(Vector2Int basePos, out List<float> surfaceHeights)
        {
            surfaceHeights = null;
            GridStack stack = gridWorld.GetStack(basePos);
            if (stack == null)
                return false;

            surfaceHeights = new List<float>(stack.Cells.Count);
            for (int i = 0; i < stack.Cells.Count; i++)
            {
                GridCell cell = stack.GetCell(i);
                if (cell != null && cell.IsWalkable)
                    surfaceHeights.Add(cell.surfaceHeight);
            }

            return surfaceHeights.Count > 0;
        }

        private GiNode BuildNode(Vector2Int subPos, int layer, float surfaceHeight)
        {
            float m = ResolutionMultiplier;
            float cell = gridWorld.CellSizeXZ;
            float worldX = (subPos.x + 0.5f) * (cell / m);
            float worldZ = (subPos.y + 0.5f) * (cell / m);
            return new GiNode
            {
                gridPos = subPos,
                verticalLayer = layer,
                worldPos = new Vector3(worldX, surfaceHeight, worldZ),
                currentIrradiance = Color.black,
                sourceIrradiance = Color.black
            };
        }

        private void AddAllSubcellsForBaseTile(Vector2Int basePos, HashSet<Vector2Int> output)
        {
            int m = ResolutionMultiplier;
            int ox = basePos.x * m;
            int oz = basePos.y * m;
            for (int sx = 0; sx < m; sx++)
            {
                for (int sz = 0; sz < m; sz++)
                    output.Add(new Vector2Int(ox + sx, oz + sz));
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r > 0) != (divisor > 0)))
                q--;
            return q;
        }

        private Vector2Int SubcellToBaseTile(Vector2Int subPos)
        {
            int m = ResolutionMultiplier;
            return new Vector2Int(FloorDiv(subPos.x, m), FloorDiv(subPos.y, m));
        }

        private Vector2Int WorldToSubcellXZ(Vector3 worldPos)
        {
            float m = ResolutionMultiplier;
            float cell = gridWorld.CellSizeXZ / m;
            return new Vector2Int(Mathf.FloorToInt(worldPos.x / cell), Mathf.FloorToInt(worldPos.z / cell));
        }

        private Vector2Int WorldToSubcellXZNearest(Vector3 worldPos)
        {
            float m = ResolutionMultiplier;
            float cell = gridWorld.CellSizeXZ / m;
            return new Vector2Int(
                Mathf.RoundToInt(worldPos.x / cell - 0.5f),
                Mathf.RoundToInt(worldPos.z / cell - 0.5f)
            );
        }

        private Vector2Int BaseToCenterSubcell(Vector2Int basePos)
        {
            int m = ResolutionMultiplier;
            int center = Mathf.Clamp(m / 2, 0, m - 1);
            return new Vector2Int(basePos.x * m + center, basePos.y * m + center);
        }

        private void RebuildNeighbors()
        {
            // Precompute neighbors using cardinal adjacency and same-subcell vertical adjacency.
            var localIndicesBySubcell = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2Int subPos = nodes[i].gridPos;
                if (!localIndicesBySubcell.TryGetValue(subPos, out List<int> list))
                {
                    list = new List<int>(4);
                    localIndicesBySubcell[subPos] = list;
                }
                list.Add(i);
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2Int p = nodes[i].gridPos;
                float y = nodes[i].worldPos.y;
                var nbrs = new List<int>(4);

                foreach (var dir in GridUtilities.CardinalDirs)
                {
                    Vector2Int q = p + dir;
                    if (!localIndicesBySubcell.TryGetValue(q, out List<int> candidates) || candidates.Count == 0)
                        continue;
                    int j = candidates[0];
                    float bestDist = Mathf.Abs(nodes[j].worldPos.y - y);
                    for (int c = 1; c < candidates.Count; c++)
                    {
                        int idx = candidates[c];
                        float dist = Mathf.Abs(nodes[idx].worldPos.y - y);
                        if (dist < bestDist)
                        {
                            j = idx;
                            bestDist = dist;
                        }
                    }

                    Vector2Int baseP = SubcellToBaseTile(p);
                    Vector2Int baseQ = SubcellToBaseTile(q);
                    if (baseP != baseQ)
                    {
                        Vector2Int baseDir = baseQ - baseP;
                        // Use the same passage-blocking data as navigation, so GI
                        // does not "leak" through solid walls.
                        if (gridWorld.BlocksPassageOutgoing(baseP, baseDir))
                            continue;
                    }

                    nbrs.Add(j);
                }

                // Vertical coupling at same XZ subcell keeps stacked levels coherent while preserving separation.
                if (TryGetAdjacentLayerIndex(p, nodes[i].verticalLayer - 1, out int below))
                    nbrs.Add(below);
                if (TryGetAdjacentLayerIndex(p, nodes[i].verticalLayer + 1, out int above))
                    nbrs.Add(above);

                neighbors[i] = nbrs.ToArray();
            }
        }

        private bool TryGetAdjacentLayerIndex(Vector2Int subPos, int layer, out int index)
        {
            return indexByPosLayer.TryGetValue(new GiNodeKey(subPos, layer), out index);
        }

        private bool TryGetAllLayerIndices(Vector2Int subPos, out List<int> candidates)
        {
            if (!indicesBySubcell.TryGetValue(subPos, out candidates) || candidates == null || candidates.Count == 0)
            {
                candidates = null;
                return false;
            }
            return true;
        }

        private bool TryGetNearestLayerIndex(Vector2Int subPos, float worldY, out int index)
        {
            index = -1;
            if (!indicesBySubcell.TryGetValue(subPos, out List<int> candidates) || candidates == null || candidates.Count == 0)
                return false;

            int best = candidates[0];
            float bestDist = Mathf.Abs(worldPosByIndex[best].y - worldY);
            for (int i = 1; i < candidates.Count; i++)
            {
                int c = candidates[i];
                float d = Mathf.Abs(worldPosByIndex[c].y - worldY);
                if (d < bestDist)
                {
                    best = c;
                    bestDist = d;
                }
            }

            index = best;
            return true;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void ValidateIndexIntegrity()
        {
            if (nodes.Count != neighbors.Count)
                Debug.LogError("GiGrid: nodes/neighbors count mismatch after rebuild.");

            if (nodes.Count != indexByPosLayer.Count)
                Debug.LogError("GiGrid: nodes/index map count mismatch after rebuild.");

            for (int i = 0; i < nodes.Count; i++)
            {
                GiNode node = nodes[i];
                if (node.index != i)
                    Debug.LogError("GiGrid: node index mismatch at " + i + ".");

                if (!indexByPosLayer.TryGetValue(new GiNodeKey(node.gridPos, node.verticalLayer), out int mapped) || mapped != i)
                    Debug.LogError("GiGrid: index map mismatch at " + node.gridPos + " layer " + node.verticalLayer + ".");
            }
        }

        #region Sources

        /// <summary>
        /// Clears direct source irradiance from <see cref="GiSource"/> only.
        /// Does not clear <see cref="bounceSourceIrradianceByIndex"/> (indirect feedback persists until updated).
        /// </summary>
        public void ClearSources()
        {
            EnsureArrayStorageSize(nodes.Count);
            Array.Clear(sourceIrradianceByIndex, 0, nodes.Count);
            Array.Clear(sourceIrradianceFromAboveByIndex, 0, nodes.Count);
            nodesNeedSyncFromArrays = true;
        }

        /// <summary>
        /// Zeros indirect bounce sources (e.g. when bounce is disabled or after a full grid rebuild).
        /// </summary>
        public void ClearBounceSources()
        {
            if (nodes.Count == 0)
                return;
            EnsureArrayStorageSize(nodes.Count);
            Array.Clear(bounceSourceIrradianceByIndex, 0, nodes.Count);
            nodesNeedSyncFromArrays = true;
        }

        /// <summary>
        /// Adds irradiance to the node at the given grid position, if it exists.
        /// </summary>
        public void AddSourceAtGrid(Vector2Int gridPos, Color irradiance)
        {
            if (!TryGetNearestLayerIndex(gridPos, 0f, out int index))
                return;

            sourceIrradianceByIndex[index] += irradiance;
            sourceIrradianceFromAboveByIndex[index] += irradiance;
            nodesNeedSyncFromArrays = true;
        }

        /// <summary>
        /// Adds source irradiance to all nodes within a given radius (in grid cells) of a world position.
        /// Uses simple distance falloff and optional LOS via <paramref name="respectOcclusion"/>.
        /// </summary>
        public void AddRadialSource(Vector3 worldPos, float radiusWorld, Color peakIrradiance, bool respectOcclusion)
        {
            if (nodes.Count == 0 || radiusWorld <= 0f)
                return;

            float cellSize = gridWorld.CellSizeXZ / ResolutionMultiplier;
            float radiusCells = radiusWorld / Mathf.Max(cellSize, 1e-4f);

            Vector2Int center = WorldToSubcellXZ(worldPos);
            int r = Mathf.CeilToInt(radiusCells);

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    Vector2Int p = new Vector2Int(center.x + dx, center.y + dy);
                    if (!TryGetAllLayerIndices(p, out List<int> layerCandidates))
                        continue;

                    for (int li = 0; li < layerCandidates.Count; li++)
                    {
                        int index = layerCandidates[li];
                        Vector3 toNode = worldPosByIndex[index] - worldPos;
                        float dist = toNode.magnitude;
                        if (dist > radiusWorld)
                            continue;

                        float falloff = Mathf.Clamp01(1f - dist / radiusWorld);
                        Color contribution = peakIrradiance * falloff;

                        if (respectOcclusion)
                        {
                            bool blockedPassage;
                            float transmittance = ComputeLineTransmittance(worldPos, worldPosByIndex[index], center, p, out blockedPassage);
                            if (blockedPassage || transmittance <= OcclusionCutoff)
                                continue;

                            contribution *= transmittance;
                        }

                        sourceIrradianceByIndex[index] += contribution;
                        if (worldPos.y >= worldPosByIndex[index].y)
                            sourceIrradianceFromAboveByIndex[index] += contribution;
                    }
                }
            }
            nodesNeedSyncFromArrays = true;
        }

        public void AddSpotSource(
            Vector3 worldPos,
            Vector3 forward,
            float rangeWorld,
            Color peakIrradiance,
            float innerAngleDeg,
            float outerAngleDeg,
            bool respectOcclusion)
        {
            if (nodes.Count == 0 || rangeWorld <= 0f)
                return;

            Vector3 dir = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            float cosInner = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(innerAngleDeg, 1f, 179f) * 0.5f);
            float cosOuter = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(outerAngleDeg, 1f, 179f) * 0.5f);
            if (cosInner < cosOuter)
            {
                float t = cosInner;
                cosInner = cosOuter;
                cosOuter = t;
            }

            float cellSize = gridWorld.CellSizeXZ / ResolutionMultiplier;
            float radiusCells = rangeWorld / Mathf.Max(cellSize, 1e-4f);
            Vector2Int center = WorldToSubcellXZ(worldPos);
            int r = Mathf.CeilToInt(radiusCells);

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    Vector2Int p = new Vector2Int(center.x + dx, center.y + dy);
                    if (!TryGetAllLayerIndices(p, out List<int> layerCandidates))
                        continue;

                    for (int li = 0; li < layerCandidates.Count; li++)
                    {
                        int index = layerCandidates[li];
                        Vector3 toNode = worldPosByIndex[index] - worldPos;
                        float dist = toNode.magnitude;
                        if (dist > rangeWorld || dist <= 1e-6f)
                            continue;

                        Vector3 toNodeDir = toNode / dist;
                        float cosTheta = Vector3.Dot(dir, toNodeDir);
                        if (cosTheta < cosOuter)
                            continue;

                        float distanceFalloff = Mathf.Clamp01(1f - dist / rangeWorld);
                        float angleFalloff = cosTheta >= cosInner ? 1f : Mathf.InverseLerp(cosOuter, cosInner, cosTheta);
                        Color contribution = peakIrradiance * (distanceFalloff * angleFalloff);

                        if (respectOcclusion)
                        {
                            bool blockedPassage;
                            float transmittance = ComputeLineTransmittance(worldPos, worldPosByIndex[index], center, p, out blockedPassage);
                            if (blockedPassage || transmittance <= OcclusionCutoff)
                                continue;

                            contribution *= transmittance;
                        }

                        sourceIrradianceByIndex[index] += contribution;
                        if (worldPos.y >= worldPosByIndex[index].y)
                            sourceIrradianceFromAboveByIndex[index] += contribution;
                    }
                }
            }
            nodesNeedSyncFromArrays = true;
        }

        public void AddRectSourceApprox(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            Vector3 normal,
            float width,
            float height,
            Color peakIrradiance,
            int samplesX,
            int samplesY,
            float normalFalloff,
            float rangeWorld,
            bool respectOcclusion)
        {
            int sx = Mathf.Clamp(samplesX, 1, 4);
            int sy = Mathf.Clamp(samplesY, 1, 4);
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 rx = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Vector3 fy = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            float halfW = Mathf.Max(0.05f, width) * 0.5f;
            float halfH = Mathf.Max(0.05f, height) * 0.5f;
            float perSample = 1f / (sx * sy);

            for (int ix = 0; ix < sx; ix++)
            {
                float tx = (ix + 0.5f) / sx;
                float ox = Mathf.Lerp(-halfW, halfW, tx);
                for (int iy = 0; iy < sy; iy++)
                {
                    float ty = (iy + 0.5f) / sy;
                    float oy = Mathf.Lerp(-halfH, halfH, ty);
                    Vector3 samplePos = center + rx * ox + fy * oy;

                    // Weight sample intensity by its facing to avoid overly flat rect contribution.
                    Color sampleEnergy = peakIrradiance * perSample * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(normalFalloff));
                    AddSpotSource(
                        samplePos,
                        n,
                        rangeWorld,
                        sampleEnergy,
                        179f,
                        179f,
                        respectOcclusion
                    );
                }
            }
        }

        public void AddDirectionalSource(
            Vector3 direction,
            Color irradiance,
            float maxDistanceWorld,
            int maxAffectedNodes,
            bool respectOcclusion)
        {
            if (nodes.Count == 0 || maxDistanceWorld <= 0f)
                return;

            Vector3 lightDir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.down;
            int budget = Mathf.Clamp(maxAffectedNodes, 1, nodes.Count);
            int affected = 0;

            for (int i = 0; i < nodes.Count && affected < budget; i++)
            {
                Color contribution = irradiance;

                if (respectOcclusion)
                {
                    Vector3 from = worldPosByIndex[i] - lightDir * maxDistanceWorld;
                    Vector3 to = worldPosByIndex[i];
                    Vector2Int fromSub = WorldToSubcellXZ(from);
                    bool blockedPassage;
                    float transmittance = ComputeLineTransmittance(from, to, fromSub, gridPosByIndex[i], out blockedPassage);
                    if (blockedPassage || transmittance <= OcclusionCutoff)
                        continue;

                    contribution *= transmittance;
                }

                sourceIrradianceByIndex[i] += contribution;
                if (lightDir.y < 0f)
                    sourceIrradianceFromAboveByIndex[i] += contribution;
                affected++;
            }
            nodesNeedSyncFromArrays = true;
        }

        #endregion

        #region Propagation

        /// <summary>
        /// Performs a single diffusion step over all nodes.
        /// When <see cref="BounceFeedbackEnabled"/> is true, merges <see cref="bounceSourceIrradianceByIndex"/> with direct sources and always uses the main-thread neighbor path (Burst path omits neighbor diffusion).
        /// </summary>
        public void StepPropagation()
        {
            int count = nodes.Count;
            if (count == 0)
                return;

            float diffusion = Mathf.Clamp01(DiffusionStrength);
            float damping = Mathf.Clamp01(Damping);

            bool useBurst = UseJobsBurst && !BounceFeedbackEnabled;

            if (useBurst)
            {
                NativeArray<Color> current = new NativeArray<Color>(currentIrradianceByIndex, Allocator.TempJob);
                NativeArray<Color> source = new NativeArray<Color>(sourceIrradianceByIndex, Allocator.TempJob);
                NativeArray<Color> next = new NativeArray<Color>(count, Allocator.TempJob);
                var job = new PropagationJob
                {
                    current = current,
                    source = source,
                    next = next,
                    diffusion = diffusion,
                    damping = damping
                };
                JobHandle handle = job.Schedule(count, 64);
                handle.Complete();
                next.CopyTo(currentIrradianceByIndex);
                current.Dispose();
                source.Dispose();
                next.Dispose();
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int[] nbrs = neighbors[i];

                    Color neighborSum = Color.black;
                    if (nbrs != null && nbrs.Length > 0)
                    {
                        for (int k = 0; k < nbrs.Length; k++)
                            neighborSum += currentIrradianceByIndex[nbrs[k]];
                        neighborSum /= nbrs.Length;
                    }

                    Color diffused = Color.Lerp(currentIrradianceByIndex[i], neighborSum, diffusion);
                    Color direct = sourceIrradianceByIndex[i];
                    Color bounce = BounceFeedbackEnabled && bounceSourceIrradianceByIndex != null && i < bounceSourceIrradianceByIndex.Length
                        ? bounceSourceIrradianceByIndex[i]
                        : Color.black;
                    workingIrradiance[i] = (direct + bounce + diffused) * damping;
                }

                for (int i = 0; i < count; i++)
                    currentIrradianceByIndex[i] = workingIrradiance[i];
            }
            nodesNeedSyncFromArrays = true;
        }

        /// <summary>
        /// Updates <see cref="bounceSourceIrradianceByIndex"/> from the current propagated field for use on the next tick.
        /// Call after <see cref="StepPropagation"/> when <see cref="BounceFeedbackEnabled"/> is true.
        /// </summary>
        public void UpdateBounceFromCurrent(in GiBounceSettings settings)
        {
            if (!BounceFeedbackEnabled || nodes.Count == 0)
                return;

            float albedo = Mathf.Clamp01(settings.bounceAlbedo);
            if (albedo <= 0f)
            {
                ClearBounceSources();
                return;
            }

            int count = nodes.Count;
            EnsureArrayStorageSize(count);
            float maxB = settings.maxBounce;

            for (int i = 0; i < count; i++)
            {
                Color basis = settings.useNeighborAverageForBounce ? GetNeighborAverageCurrent(i) : currentIrradianceByIndex[i];
                Color b = basis * albedo;
                if (maxB > 0f)
                {
                    b.r = Mathf.Min(b.r, maxB);
                    b.g = Mathf.Min(b.g, maxB);
                    b.b = Mathf.Min(b.b, maxB);
                }

                bounceSourceIrradianceByIndex[i] = b;
            }

            nodesNeedSyncFromArrays = true;
        }

        private Color GetNeighborAverageCurrent(int i)
        {
            int[] nbrs = neighbors[i];
            if (nbrs == null || nbrs.Length == 0)
                return currentIrradianceByIndex[i];

            Color neighborSum = Color.black;
            for (int k = 0; k < nbrs.Length; k++)
                neighborSum += currentIrradianceByIndex[nbrs[k]];
            neighborSum /= nbrs.Length;
            return neighborSum;
        }

        #endregion

        #region Sampling & LOS

        /// <summary>
        /// Returns the irradiance at a given world position by snapping to the nearest grid stack.
        /// </summary>
        public Color SampleAtWorldPos(Vector3 worldPos)
        {
            if (nodes.Count == 0)
                return Color.black;

            Vector2Int gridPos = WorldToSubcellXZNearest(worldPos);
            if (!TryGetNearestLayerIndex(gridPos, worldPos.y, out int index))
                return Color.black;

            return currentIrradianceByIndex[index];
        }

        /// <summary>
        /// Simple grid-based LOS test between two tiles, respecting passage blockers.
        /// </summary>
        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        {
            Vector2Int fromSub = BaseToCenterSubcell(from);
            Vector2Int toSub = BaseToCenterSubcell(to);
            bool blockedPassage;
            float transmittance = ComputeLineTransmittance(
                gridWorld.GridToWorldXZ(from),
                gridWorld.GridToWorldXZ(to),
                fromSub,
                toSub,
                out blockedPassage
            );

            return !blockedPassage && transmittance > OcclusionCutoff;
        }

        private float ComputeLineTransmittance(Vector3 fromWorld, Vector3 toWorld, Vector2Int from, Vector2Int to, out bool blockedPassage)
        {
            blockedPassage = false;

            Vector2Int delta = to - from;
            int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (steps == 0)
            {
                Vector2Int baseCell = SubcellToBaseTile(from);
                return gridWorld.ComputeGiVerticalTransmittance(baseCell, fromWorld.y, toWorld.y, OcclusionFloorHeightCells);
            }

            Vector2 step = new Vector2(delta.x / (float)steps, delta.y / (float)steps);
            Vector2 pos = new Vector2(from.x + 0.5f, from.y + 0.5f);
            float transmittance = 1f;

            Vector2Int prevCell = from;
            for (int i = 0; i < steps; i++)
            {
                pos += step;
                Vector2Int cell = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
                float t = (i + 1f) / steps;
                float sampleHeight = Mathf.Lerp(fromWorld.y, toWorld.y, t);

                Vector2Int dir = cell - prevCell;
                if (dir != Vector2Int.zero)
                {
                    bool diagonalStep = Mathf.Abs(dir.x) > 0 && Mathf.Abs(dir.y) > 0;
                    if (diagonalStep)
                    {
                        // Diagonal movement is not represented in BlocksPassageOutgoing (cardinal only),
                        // so test both cardinal legs to avoid "leaking" light through corner walls.
                        Vector2Int dx = new Vector2Int(dir.x > 0 ? 1 : -1, 0);
                        Vector2Int dy = new Vector2Int(0, dir.y > 0 ? 1 : -1);

                        Vector2Int prevBase = SubcellToBaseTile(prevCell);
                        Vector2Int stepXBase = SubcellToBaseTile(prevCell + dx);
                        Vector2Int stepYBase = SubcellToBaseTile(prevCell + dy);

                        if ((prevBase != stepXBase && gridWorld.BlocksPassageOutgoing(prevBase, stepXBase - prevBase)) ||
                            (prevBase != stepYBase && gridWorld.BlocksPassageOutgoing(prevBase, stepYBase - prevBase)))
                        {
                            blockedPassage = true;
                            return 0f;
                        }

                        if (prevBase != stepXBase)
                            transmittance *= gridWorld.ComputeGiStepTransmittance(prevBase, stepXBase, sampleHeight, OcclusionFloorHeightCells);
                        if (prevBase != stepYBase)
                            transmittance *= gridWorld.ComputeGiStepTransmittance(prevBase, stepYBase, sampleHeight, OcclusionFloorHeightCells);
                    }
                    else
                    {
                        // Cardinal step.
                        Vector2Int prevBase = SubcellToBaseTile(prevCell);
                        Vector2Int cellBase = SubcellToBaseTile(cell);
                        if (prevBase != cellBase && gridWorld.BlocksPassageOutgoing(prevBase, cellBase - prevBase))
                        {
                            blockedPassage = true;
                            return 0f;
                        }

                        if (prevBase != cellBase)
                            transmittance *= gridWorld.ComputeGiStepTransmittance(prevBase, cellBase, sampleHeight, OcclusionFloorHeightCells);
                    }

                    prevCell = cell;
                }
            }

            return Mathf.Clamp01(transmittance);
        }

        public void AppendSubcellsForBaseTile(Vector2Int baseTile, List<Vector2Int> output)
        {
            if (output == null)
                return;

            int m = ResolutionMultiplier;
            int ox = baseTile.x * m;
            int oz = baseTile.y * m;
            for (int sx = 0; sx < m; sx++)
            {
                for (int sz = 0; sz < m; sz++)
                    output.Add(new Vector2Int(ox + sx, oz + sz));
            }
        }

        public Color GetCurrentIrradianceAt(int index)
        {
            if ((uint)index >= (uint)currentIrradianceByIndex.Length)
                return Color.black;
            return currentIrradianceByIndex[index];
        }

        /// <summary>
        /// Returns [0..1] representing what fraction of this node's <b>direct</b> source irradiance
        /// (from <see cref="GiSource"/> only) came from above (emitter y &gt;= node y).
        /// Indirect bounce energy is excluded so the Y texture split stays consistent with emissive semantics.
        /// </summary>
        public float GetSourceFractionAbove(int index)
        {
            if ((uint)index >= (uint)sourceIrradianceByIndex.Length)
                return 1f;
            float total = sourceIrradianceByIndex[index].maxColorComponent;
            if (total <= 1e-6f)
                return 1f;
            float above = sourceIrradianceFromAboveByIndex[index].maxColorComponent;
            return Mathf.Clamp01(above / total);
        }

        public int GetNodeCount() => gridPosByIndex.Length;

        public Vector2Int GetNodeGridPosAt(int index)
        {
            if ((uint)index >= (uint)gridPosByIndex.Length)
                return default;
            return gridPosByIndex[index];
        }

        public Vector3 GetNodeWorldPosAt(int index)
        {
            if ((uint)index >= (uint)worldPosByIndex.Length)
                return Vector3.zero;
            return worldPosByIndex[index];
        }

        public bool TryGetWorldYRange(out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;
            if (worldPosByIndex.Length == 0)
                return false;

            minY = maxY = worldPosByIndex[0].y;
            for (int i = 1; i < worldPosByIndex.Length; i++)
            {
                float y = worldPosByIndex[i].y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return true;
        }

        private void RebuildArrayStorageFromNodes()
        {
            int count = nodes.Count;
            EnsureArrayStorageSize(count);
            indicesBySubcell.Clear();
            for (int i = 0; i < count; i++)
            {
                GiNode n = nodes[i];
                gridPosByIndex[i] = n.gridPos;
                verticalLayerByIndex[i] = n.verticalLayer;
                worldPosByIndex[i] = n.worldPos;
                currentIrradianceByIndex[i] = n.currentIrradiance;
                sourceIrradianceByIndex[i] = n.sourceIrradiance;
                sourceIrradianceFromAboveByIndex[i] = Color.black;

                if (!indicesBySubcell.TryGetValue(n.gridPos, out List<int> list))
                {
                    list = new List<int>(4);
                    indicesBySubcell[n.gridPos] = list;
                }
                list.Add(i);
            }

            nodesNeedSyncFromArrays = false;
        }

        private void EnsureArrayStorageSize(int count)
        {
            if (gridPosByIndex.Length != count)
            {
                gridPosByIndex = new Vector2Int[count];
                verticalLayerByIndex = new int[count];
                worldPosByIndex = new Vector3[count];
                currentIrradianceByIndex = new Color[count];
                sourceIrradianceByIndex = new Color[count];
                sourceIrradianceFromAboveByIndex = new Color[count];
                bounceSourceIrradianceByIndex = new Color[count];
            }
        }

        private void SyncNodesFromArraysIfNeeded()
        {
            if (!nodesNeedSyncFromArrays)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                GiNode n = nodes[i];
                n.currentIrradiance = currentIrradianceByIndex[i];
                n.sourceIrradiance = sourceIrradianceByIndex[i];
                nodes[i] = n;
            }

            nodesNeedSyncFromArrays = false;
        }

        private bool TryGetIndexFast(Vector2Int p, float worldY, out int index) => TryGetNearestLayerIndex(p, worldY, out index);

        [BurstCompile]
        private struct PropagationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color> current;
            [ReadOnly] public NativeArray<Color> source;
            public NativeArray<Color> next;
            public float diffusion;
            public float damping;

            public void Execute(int index)
            {
                // Neighbor-aware propagation still runs on main thread path.
                // Burst path keeps source+damping update parallel and deterministic.
                Color diffused = Color.Lerp(current[index], current[index], diffusion);
                next[index] = (source[index] + diffused) * damping;
            }
        }

        #endregion
    }
}

