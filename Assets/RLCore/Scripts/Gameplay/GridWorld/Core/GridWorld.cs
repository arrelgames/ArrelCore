using System;
using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    [DefaultExecutionOrder(-1000)]
    public class GridWorld : MonoBehaviour
    {
        public static GridWorld Instance { get; private set; }

        [Header("Grid Dimensions")]
        [SerializeField] private float cellSizeXZ = 1.5f;
        [SerializeField] private float cellSizeY = 1.0f;

        public float CellSizeXZ => cellSizeXZ;
        public float CellSizeY => cellSizeY;

        private readonly Dictionary<Vector2Int, GridStack> grid = new();
        private readonly HashSet<Vector2Int> dirtyTiles = new();

        private GridNavigation navigation;

        // Solid props may register before any surfaces exist at their footprint tiles.
        // If that happens, they can't attach to the blocked cell states and would
        // remain ineffective unless we retry later.
        private readonly List<GridProp> pendingSolidProps = new();

        private readonly List<GridPropRamp> pendingFilledRampProps = new();
        private readonly HashSet<GridPropRamp> registeredRamps = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            navigation = new GridNavigation(this);
        }

        #region Coordinate Conversions

        public Vector2Int WorldToGridXZ(Vector3 worldPos)
        {
            int gx = Mathf.FloorToInt(worldPos.x / cellSizeXZ);
            int gy = Mathf.FloorToInt(worldPos.z / cellSizeXZ);
            return new Vector2Int(gx, gy);
        }

        public Vector3 GridToWorldXZ(Vector2Int gridPos)
        {
            return new Vector3(
                (gridPos.x + 0.5f) * cellSizeXZ,
                0f,
                (gridPos.y + 0.5f) * cellSizeXZ
            );
        }

        #endregion

        #region Stack Access

        public GridStack GetStack(Vector2Int pos)
        {
            grid.TryGetValue(pos, out var stack);
            return stack;
        }

        public GridCell GetCell(GridNode node)
        {
            if (!grid.TryGetValue(new Vector2Int(node.x, node.y), out GridStack stack))
                return null;

            return stack.GetCell(node.surface);
        }

        public GridNode GetClosestNode(Vector3 worldPos)
        {
            Vector2Int pos = WorldToGridXZ(worldPos);

            if (!grid.TryGetValue(pos, out GridStack stack))
                return new GridNode(pos.x, pos.y, 0);

            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < stack.Cells.Count; i++)
            {
                GridCell cell = stack.GetCell(i);
                if (cell == null || !cell.IsWalkable)
                    continue;

                float dist = Mathf.Abs(cell.surfaceHeight - worldPos.y);

                if (dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }

            if (best < 0)
                best = 0;

            return new GridNode(pos.x, pos.y, best);
        }

        public IReadOnlyDictionary<Vector2Int, GridStack> GetAllStacks() => grid;

        private GridStack EnsureStack(Vector2Int pos)
        {
            if (!grid.TryGetValue(pos, out GridStack stack))
            {
                stack = new GridStack();
                grid[pos] = stack;
            }

            return stack;
        }

        #endregion

        #region Prop Management

        public void RegisterProp(GridProp prop)
        {
            if (prop is GridPropRamp ramp)
            {
                RegisterRampProp(ramp);
                return;
            }

            Vector2Int origin = prop.GetOrigin();
            Vector2Int size = prop.Size;

            const float SURFACE_EPS = 0.001f;

            float baseHeight = prop.transform.position.y;
            float topHeight = prop.GetSurfaceWorldHeight();
            bool isSolid = prop.Solid;

            bool needsRetry = false;

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                {
                    Vector2Int pos = new Vector2Int(x, y);
                    GridStack stack = EnsureStack(pos);

                    if (prop.CreatesSurface)
                    {
                        // Ensure the top surface exists for walk/climb.
                        float height = topHeight;
                        int surfaceIndex = stack.AddSurface(height);
                        GridCell createdCell = stack.GetCell(surfaceIndex);

                        // For solid props, we intentionally do NOT add the prop to the top cell
                        // so the top remains walkable.
                        if (!isSolid && createdCell != null)
                        {
                            createdCell.AddProp(prop);
                        }
                    }

                    if (isSolid)
                    {
                        // Attach the solid prop to all existing surfaces between base and top.
                        // blocked range: [baseHeight, topHeight)
                        bool foundBlockedSurface = false;

                        for (int s = 0; s < stack.Cells.Count; s++)
                        {
                            GridCell cell = stack.GetCell(s);
                            if (cell == null) continue;

                            float h = cell.surfaceHeight;
                            bool inBlockedRange = h >= baseHeight - SURFACE_EPS && h < topHeight - SURFACE_EPS;
                            if (!inBlockedRange)
                                continue;

                            bool alreadyOccupied = false;
                            foreach (var occupied in prop.OccupiedCells)
                            {
                                if (occupied == cell)
                                {
                                    alreadyOccupied = true;
                                    break;
                                }
                            }
                            if (!alreadyOccupied)
                            {
                                cell.AddProp(prop);
                            }

                            foundBlockedSurface = true;
                        }

                        if (!foundBlockedSurface)
                            needsRetry = true;
                    }
                    else if (!prop.CreatesSurface)
                    {
                        // Non-solid, non-surface props attach to the closest surface so
                        // suppression/covers still work on an existing layer.
                        int closest = stack.GetClosestSurface(baseHeight);
                        if (closest >= 0)
                        {
                            GridCell cell = stack.GetCell(closest);
                            if (cell != null)
                            {
                                cell.AddProp(prop);
                            }
                        }
                        else
                        {
                            needsRetry = true;
                        }
                    }

                    MarkDirty(pos);
                }
            }

            // If a solid prop couldn't attach to the blocked layer(s) yet, retry
            // after future surface props are registered.
            if (needsRetry && isSolid && !pendingSolidProps.Contains(prop))
                pendingSolidProps.Add(prop);

            // When new surfaces are created, retry any pending solid blockers.
            if (prop.CreatesSurface && pendingSolidProps.Count > 0)
                TryAttachPendingSolidProps();

            if (prop.CreatesSurface && pendingFilledRampProps.Count > 0)
                TryAttachPendingRampProps();

            UpdateDirtyNavigation();
        }

        private void RegisterRampProp(GridPropRamp ramp)
        {
            pendingFilledRampProps.Remove(ramp);
            registeredRamps.Add(ramp);

            Vector2Int origin = ramp.GetOrigin();
            Vector2Int size = ramp.Size;

            const float SURFACE_EPS = 0.001f;
            const float SURFACE_MATCH_EPS = 0.01f;
            float deckMatchEps = Mathf.Max(SURFACE_MATCH_EPS, cellSizeY * 0.25f);
            if (ramp.DeckSurfaceSnapEpsilon > 0f)
                deckMatchEps = Mathf.Max(deckMatchEps, ramp.DeckSurfaceSnapEpsilon);

            float baseHeight = ramp.transform.position.y;
            bool needsRetry = false;
            bool isFilled = ramp.Filled;

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                {
                    Vector2Int pos = new Vector2Int(x, y);
                    GridStack stack = EnsureStack(pos);
                    float deckWorldY = ramp.GetDeckWorldYAtWorldGrid(x, y);

                    int surfaceIndex = stack.FindSurfaceIndexNear(deckWorldY, deckMatchEps);
                    if (surfaceIndex < 0)
                        surfaceIndex = stack.AddSurface(deckWorldY);

                    GridCell deckCell = stack.GetCell(surfaceIndex);

                    if (!isFilled && deckCell != null)
                        deckCell.AddProp(ramp);

                    if (isFilled)
                    {
                        bool foundBlockedSurface = false;

                        for (int s = 0; s < stack.Cells.Count; s++)
                        {
                            GridCell cell = stack.GetCell(s);
                            if (cell == null) continue;

                            float h = cell.surfaceHeight;
                            bool inBlockedRange = h >= baseHeight - SURFACE_EPS && h < deckWorldY - SURFACE_EPS;
                            if (!inBlockedRange)
                                continue;

                            foundBlockedSurface = true;

                            bool alreadyOccupied = false;
                            foreach (var occupied in ramp.OccupiedCells)
                            {
                                if (occupied == cell)
                                {
                                    alreadyOccupied = true;
                                    break;
                                }
                            }

                            if (!alreadyOccupied)
                                cell.AddProp(ramp);
                        }

                        if (!foundBlockedSurface)
                            needsRetry = true;
                    }

                    MarkDirty(pos);
                }
            }

            if (needsRetry && isFilled && !pendingFilledRampProps.Contains(ramp))
                pendingFilledRampProps.Add(ramp);

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                    RecomputeStackCeilings(new Vector2Int(x, y));
            }

            TryAttachPendingSolidProps();
            TryAttachPendingRampProps();

            UpdateDirtyNavigation();
        }

        private void RecomputeStackCeilings(Vector2Int pos)
        {
            GridStack stack = GetStack(pos);
            if (stack == null) return;

            const float SURFACE_EPS = 0.001f;
            float defaultCeilingDelta = cellSizeY * 4f;

            for (int s = 0; s < stack.Cells.Count; s++)
            {
                GridCell cell = stack.GetCell(s);
                if (cell == null) continue;
                cell.ceilingHeight = cell.surfaceHeight + defaultCeilingDelta;
            }

            foreach (var ramp in registeredRamps)
            {
                if (ramp == null) continue;

                Vector2Int ro = ramp.GetOrigin();
                Vector2Int rs = ramp.Size;
                if (pos.x < ro.x || pos.x >= ro.x + rs.x || pos.y < ro.y || pos.y >= ro.y + rs.y)
                    continue;

                if (ramp.Filled)
                    continue;

                float deckWorldY = ramp.GetDeckWorldYAtWorldGrid(pos.x, pos.y);
                float underside = deckWorldY - ramp.DeckThickness;

                for (int s = 0; s < stack.Cells.Count; s++)
                {
                    GridCell cell = stack.GetCell(s);
                    if (cell == null) continue;

                    if (cell.surfaceHeight < deckWorldY - SURFACE_EPS)
                        cell.ceilingHeight = Mathf.Min(cell.ceilingHeight, underside);
                }
            }
        }

        private void TryAttachPendingRampProps()
        {
            if (pendingFilledRampProps.Count == 0)
                return;

            const float SURFACE_EPS = 0.001f;

            for (int i = pendingFilledRampProps.Count - 1; i >= 0; i--)
            {
                GridPropRamp ramp = pendingFilledRampProps[i];

                Vector2Int origin = ramp.GetOrigin();
                Vector2Int size = ramp.Size;

                float baseHeight = ramp.transform.position.y;

                bool stillMissingSomeTiles = false;

                for (int x = origin.x; x < origin.x + size.x; x++)
                {
                    for (int y = origin.y; y < origin.y + size.y; y++)
                    {
                        Vector2Int pos = new Vector2Int(x, y);
                        GridStack stack = GetStack(pos);

                        if (stack == null || stack.Cells.Count == 0)
                        {
                            stillMissingSomeTiles = true;
                            continue;
                        }

                        float deckWorldY = ramp.GetDeckWorldYAtWorldGrid(x, y);

                        bool foundBlockedSurface = false;

                        for (int s = 0; s < stack.Cells.Count; s++)
                        {
                            GridCell cell = stack.GetCell(s);
                            if (cell == null) continue;

                            float h = cell.surfaceHeight;
                            bool inBlockedRange = h >= baseHeight - SURFACE_EPS && h < deckWorldY - SURFACE_EPS;
                            if (!inBlockedRange)
                                continue;

                            foundBlockedSurface = true;

                            bool alreadyOccupied = false;
                            foreach (var occupied in ramp.OccupiedCells)
                            {
                                if (occupied == cell)
                                {
                                    alreadyOccupied = true;
                                    break;
                                }
                            }

                            if (!alreadyOccupied)
                            {
                                cell.AddProp(ramp);
                                MarkDirty(pos);
                            }
                        }

                        if (!foundBlockedSurface)
                            stillMissingSomeTiles = true;
                    }
                }

                if (!stillMissingSomeTiles)
                {
                    pendingFilledRampProps.RemoveAt(i);

                    for (int x = origin.x; x < origin.x + size.x; x++)
                    {
                        for (int y = origin.y; y < origin.y + size.y; y++)
                            RecomputeStackCeilings(new Vector2Int(x, y));
                    }
                }
            }
        }

        public void UnregisterProp(GridProp prop)
        {
            pendingSolidProps.Remove(prop);

            if (prop is GridPropRamp rampUnregister)
            {
                pendingFilledRampProps.Remove(rampUnregister);
                registeredRamps.Remove(rampUnregister);
            }

            // Remove from all cells
            foreach (var cell in prop.OccupiedCells)
            {
                cell.RemoveProp(prop);
            }

            prop.ClearCells();

            // Mark affected area dirty
            Vector2Int origin = prop.GetOrigin();
            Vector2Int size = prop.Size;

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                {
                    Vector2Int p = new Vector2Int(x, y);
                    MarkDirty(p);
                    if (prop is GridPropRamp)
                        RecomputeStackCeilings(p);
                }
            }

            UpdateDirtyNavigation();
        }

        #endregion

        private void TryAttachPendingSolidProps()
        {
            if (pendingSolidProps.Count == 0)
                return;

            // Iterate backwards since we may remove items.
            for (int i = pendingSolidProps.Count - 1; i >= 0; i--)
            {
                GridProp prop = pendingSolidProps[i];

                Vector2Int origin = prop.GetOrigin();
                Vector2Int size = prop.Size;

                const float SURFACE_EPS = 0.001f;

                float baseHeight = prop.transform.position.y;
                float topHeight = prop.GetSurfaceWorldHeight();

                bool stillMissingSomeTiles = false;

                for (int x = origin.x; x < origin.x + size.x; x++)
                {
                    for (int y = origin.y; y < origin.y + size.y; y++)
                    {
                        Vector2Int pos = new Vector2Int(x, y);
                        GridStack stack = GetStack(pos);

                        if (stack == null || stack.Cells.Count == 0)
                        {
                            stillMissingSomeTiles = true;
                            continue;
                        }

                        bool foundBlockedSurface = false;

                        for (int s = 0; s < stack.Cells.Count; s++)
                        {
                            GridCell cell = stack.GetCell(s);
                            if (cell == null) continue;

                            float h = cell.surfaceHeight;
                            bool inBlockedRange = h >= baseHeight - SURFACE_EPS && h < topHeight - SURFACE_EPS;
                            if (!inBlockedRange)
                                continue;

                            foundBlockedSurface = true;

                            // Avoid duplicate prop entries inside GridCellState.
                            bool alreadyOccupied = false;
                            foreach (var occupied in prop.OccupiedCells)
                            {
                                if (occupied == cell)
                                {
                                    alreadyOccupied = true;
                                    break;
                                }
                            }

                            if (!alreadyOccupied)
                            {
                                cell.AddProp(prop);
                                MarkDirty(pos);
                            }
                        }

                        if (!foundBlockedSurface)
                            stillMissingSomeTiles = true;
                    }
                }

                if (!stillMissingSomeTiles)
                    pendingSolidProps.RemoveAt(i);
            }
        }

        #region Dirty Tile Management

        private void MarkDirty(Vector2Int pos)
        {
            dirtyTiles.Add(pos);

            // Include all neighbors (cardinal + diagonal)
            foreach (var dir in GridUtilities.AllDirs())
            {
                dirtyTiles.Add(pos + dir);
            }
        }

        private void UpdateDirtyNavigation()
        {
            foreach (var pos in dirtyTiles)
            {
                navigation.RebuildNodeEdges(pos);
            }

            dirtyTiles.Clear();
        }

        #endregion

        #region Navigation

        public IReadOnlyList<GridEdge> GetEdges(GridNode node)
        {
            return navigation.GetEdges(node);
        }

        #endregion
    }
}