using System;
using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Lightweight, gameplay-agnostic data model for grid-based GI.
    /// Backed by <see cref="GridWorld"/> XZ tiles, currently 2D (one node per stack).
    /// </summary>
    [Serializable]
    public struct GiNode
    {
        public int index;
        public Vector2Int gridPos;
        public Vector3 worldPos;

        // Irradiance in linear RGB (approximate).
        public Color currentIrradiance;
        public Color sourceIrradiance;
    }

    /// <summary>
    /// Pure data container + propagation logic for GI values on top of the existing GridWorld.
    /// Does not touch gameplay/navigation and is intended to be driven by <see cref="GiManager"/>.
    /// </summary>
    public sealed class GiGrid
    {
        private readonly GridWorld gridWorld;

        // Compact arrays for cache-friendly iteration.
        private readonly List<GiNode> nodes = new();
        private readonly List<int[]> neighbors = new();

        // Lookup from XZ grid position to node index, for fast source writes / sampling.
        private readonly Dictionary<Vector2Int, int> indexByPos = new();

        // Double-buffer for propagation.
        private readonly Color[] workingIrradiance;

        public IReadOnlyList<GiNode> Nodes => nodes;

        // Propagation parameters (tuned by GiManager).
        public float DiffusionStrength { get; set; } = 0.6f;
        public float Damping { get; set; } = 0.9f;
        public float OcclusionFloorHeightCells { get; set; } = 4f;
        public float OcclusionCutoff { get; set; } = 0.05f;

        public GiGrid(GridWorld world)
        {
            gridWorld = world ?? throw new ArgumentNullException(nameof(world));

            // Build nodes immediately from current GridWorld state.
            BuildNodesFromGridWorld();

            workingIrradiance = new Color[nodes.Count];
        }

        private void BuildNodesFromGridWorld()
        {
            nodes.Clear();
            neighbors.Clear();
            indexByPos.Clear();

            IReadOnlyDictionary<Vector2Int, GridStack> stacks = gridWorld.GetAllStacks();
            foreach (var kvp in stacks)
            {
                Vector2Int pos = kvp.Key;
                GridStack stack = kvp.Value;
                if (stack == null)
                    continue;

                // Pick the closest walkable cell to some default height (0) as our sample.
                // This keeps GI probes aligned with floors.
                int surfaceIndex = stack.GetClosestSurface(0f);
                if (surfaceIndex < 0)
                    continue;

                GridCell cell = stack.GetCell(surfaceIndex);
                if (cell == null)
                    continue;

                // Optional: only care about walkable surfaces for now.
                if (!cell.IsWalkable)
                    continue;

                Vector3 worldXZ = gridWorld.GridToWorldXZ(pos);
                Vector3 worldPos = new Vector3(worldXZ.x, cell.surfaceHeight, worldXZ.z);

                GiNode node = new GiNode
                {
                    index = nodes.Count,
                    gridPos = pos,
                    worldPos = worldPos,
                    currentIrradiance = Color.black,
                    sourceIrradiance = Color.black
                };

                indexByPos[pos] = node.index;
                nodes.Add(node);
                neighbors.Add(Array.Empty<int>());
            }

            // Precompute neighbors using cardinal adjacency and grid passage blockers.
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2Int p = nodes[i].gridPos;
                var nbrs = new List<int>(4);

                foreach (var dir in GridUtilities.CardinalDirs)
                {
                    Vector2Int q = p + dir;
                    if (!indexByPos.TryGetValue(q, out int j))
                        continue;

                    // Use the same passage-blocking data as navigation, so GI
                    // does not "leak" through solid walls.
                    if (gridWorld.BlocksPassageOutgoing(p, dir))
                        continue;

                    nbrs.Add(j);
                }

                neighbors[i] = nbrs.ToArray();
            }
        }

        #region Sources

        /// <summary>
        /// Clears all per-frame source irradiance contributions. Call before re-applying sources.
        /// </summary>
        public void ClearSources()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GiNode n = nodes[i];
                n.sourceIrradiance = Color.black;
                nodes[i] = n;
            }
        }

        /// <summary>
        /// Adds irradiance to the node at the given grid position, if it exists.
        /// </summary>
        public void AddSourceAtGrid(Vector2Int gridPos, Color irradiance)
        {
            if (!indexByPos.TryGetValue(gridPos, out int index))
                return;

            GiNode n = nodes[index];
            n.sourceIrradiance += irradiance;
            nodes[index] = n;
        }

        /// <summary>
        /// Adds source irradiance to all nodes within a given radius (in grid cells) of a world position.
        /// Uses simple distance falloff and optional LOS via <paramref name="respectOcclusion"/>.
        /// </summary>
        public void AddRadialSource(Vector3 worldPos, float radiusWorld, Color peakIrradiance, bool respectOcclusion)
        {
            if (nodes.Count == 0 || radiusWorld <= 0f)
                return;

            float cellSize = gridWorld.CellSizeXZ;
            float radiusCells = radiusWorld / Mathf.Max(cellSize, 1e-4f);

            Vector2Int center = gridWorld.WorldToGridXZ(worldPos);
            int r = Mathf.CeilToInt(radiusCells);

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    Vector2Int p = new Vector2Int(center.x + dx, center.y + dy);
                    if (!indexByPos.TryGetValue(p, out int index))
                        continue;

                    GiNode node = nodes[index];
                    Vector3 toNode = node.worldPos - worldPos;
                    float dist = toNode.magnitude;
                    if (dist > radiusWorld)
                        continue;

                    float falloff = Mathf.Clamp01(1f - dist / radiusWorld);
                    Color contribution = peakIrradiance * falloff;

                    if (respectOcclusion)
                    {
                        bool blockedPassage;
                        float transmittance = ComputeLineTransmittance(worldPos, node.worldPos, center, p, out blockedPassage);
                        if (blockedPassage || transmittance <= OcclusionCutoff)
                            continue;

                        contribution *= transmittance;
                    }

                    node.sourceIrradiance += contribution;
                    nodes[index] = node;
                }
            }
        }

        #endregion

        #region Propagation

        /// <summary>
        /// Performs a single diffusion step over all nodes.
        /// </summary>
        public void StepPropagation()
        {
            int count = nodes.Count;
            if (count == 0)
                return;

            float diffusion = Mathf.Clamp01(DiffusionStrength);
            float damping = Mathf.Clamp01(Damping);

            for (int i = 0; i < count; i++)
            {
                GiNode n = nodes[i];
                int[] nbrs = neighbors[i];

                Color neighborSum = Color.black;
                if (nbrs != null && nbrs.Length > 0)
                {
                    for (int k = 0; k < nbrs.Length; k++)
                    {
                        neighborSum += nodes[nbrs[k]].currentIrradiance;
                    }
                    neighborSum /= nbrs.Length;
                }

                Color diffused = Color.Lerp(n.currentIrradiance, neighborSum, diffusion);

                // Energy comes from sources plus diffused neighbor contribution, then damped.
                Color next = (n.sourceIrradiance + diffused) * damping;
                workingIrradiance[i] = next;
            }

            for (int i = 0; i < count; i++)
            {
                GiNode n = nodes[i];
                n.currentIrradiance = workingIrradiance[i];
                nodes[i] = n;
            }
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

            Vector2Int gridPos = gridWorld.WorldToGridXZNearest(worldPos);
            if (!indexByPos.TryGetValue(gridPos, out int index))
                return Color.black;

            return nodes[index].currentIrradiance;
        }

        /// <summary>
        /// Simple grid-based LOS test between two tiles, respecting passage blockers.
        /// </summary>
        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        {
            bool blockedPassage;
            float transmittance = ComputeLineTransmittance(
                gridWorld.GridToWorldXZ(from),
                gridWorld.GridToWorldXZ(to),
                from,
                to,
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
                return 1f;

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

                        if (gridWorld.BlocksPassageOutgoing(prevCell, dx) ||
                            gridWorld.BlocksPassageOutgoing(prevCell, dy))
                        {
                            blockedPassage = true;
                            return 0f;
                        }

                        transmittance *= gridWorld.ComputeGiStepTransmittance(prevCell, prevCell + dx, sampleHeight, OcclusionFloorHeightCells);
                        transmittance *= gridWorld.ComputeGiStepTransmittance(prevCell, prevCell + dy, sampleHeight, OcclusionFloorHeightCells);
                    }
                    else
                    {
                        // Cardinal step.
                        if (gridWorld.BlocksPassageOutgoing(prevCell, dir))
                        {
                            blockedPassage = true;
                            return 0f;
                        }

                        transmittance *= gridWorld.ComputeGiStepTransmittance(prevCell, cell, sampleHeight, OcclusionFloorHeightCells);
                    }

                    prevCell = cell;
                }
            }

            return Mathf.Clamp01(transmittance);
        }

        #endregion
    }
}

