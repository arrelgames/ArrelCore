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

        // Non-surface props (createsSurface=false) may register before any surfaces
        // exist at their footprint tiles. If that happens, they can't attach to a
        // cell's state and would remain ineffective unless we retry later.
        private readonly List<GridProp> pendingNonSurfaceProps = new();

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
            Vector2Int origin = prop.GetOrigin();
            Vector2Int size = prop.Size;

            bool needsRetry = false;

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                {
                    Vector2Int pos = new Vector2Int(x, y);
                    GridStack stack = EnsureStack(pos);

                    GridCell cell = null;

                    if (prop.CreatesSurface)
                    {
                        float height = prop.GetSurfaceWorldHeight();
                        int surfaceIndex = stack.AddSurface(height);
                        cell = stack.GetCell(surfaceIndex);
                    }
                    else
                    {
                        float baseHeight = prop.transform.position.y;
                        int closest = stack.GetClosestSurface(baseHeight);

                        if (closest >= 0)
                            cell = stack.GetCell(closest);
                        else
                            needsRetry = true;
                    }

                    if (cell != null)
                        cell.AddProp(prop);

                    MarkDirty(pos);
                }
            }

            // If this prop didn't attach because no surfaces existed yet, retry
            // after future surface props are registered.
            if (!prop.CreatesSurface && needsRetry && !pendingNonSurfaceProps.Contains(prop))
                pendingNonSurfaceProps.Add(prop);

            // When new surfaces are created, retry any pending non-surface blockers.
            if (prop.CreatesSurface && pendingNonSurfaceProps.Count > 0)
                TryAttachPendingNonSurfaceProps();

            UpdateDirtyNavigation();
        }

        public void UnregisterProp(GridProp prop)
        {
            pendingNonSurfaceProps.Remove(prop);

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
                    MarkDirty(new Vector2Int(x, y));
                }
            }

            UpdateDirtyNavigation();
        }

        #endregion

        private void TryAttachPendingNonSurfaceProps()
        {
            if (pendingNonSurfaceProps.Count == 0)
                return;

            // Iterate backwards since we may remove items.
            for (int i = pendingNonSurfaceProps.Count - 1; i >= 0; i--)
            {
                GridProp prop = pendingNonSurfaceProps[i];

                Vector2Int origin = prop.GetOrigin();
                Vector2Int size = prop.Size;

                float baseHeight = prop.transform.position.y;

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

                        int closest = stack.GetClosestSurface(baseHeight);
                        if (closest < 0)
                        {
                            stillMissingSomeTiles = true;
                            continue;
                        }

                        GridCell cell = stack.GetCell(closest);
                        if (cell == null)
                        {
                            stillMissingSomeTiles = true;
                            continue;
                        }

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
                }

                if (!stillMissingSomeTiles)
                    pendingNonSurfaceProps.RemoveAt(i);
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