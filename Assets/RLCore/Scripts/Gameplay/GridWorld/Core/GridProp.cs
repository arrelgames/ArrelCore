using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridProp : MonoBehaviour
    {
        [Header("Grid Footprint")]
        [SerializeField] private Vector2Int baseSize = Vector2Int.one;

        [Header("Surface Creation")]
        [SerializeField] private bool createsSurface = false;

        [SerializeField] private float surfaceHeight = 0f;

        [Header("Prop Shape")]
        [SerializeField] private float propHeight = 1f;

        [Header("Solid Blocking")]
        [UnityEngine.Serialization.FormerlySerializedAs("blocksMovement")]
        [SerializeField] private bool solid = false;

        [Tooltip(
            "When solid: the tile stays walkable. Blocks cardinal passage across the face given by " +
            "transform.forward projected on XZ (snapped to grid axes). Use for thin walls.")]
        [SerializeField] private bool passageBlockingSolid;

        [Range(0, 1)]
        [SerializeField] private float soundSuppression = 0f;

        [Range(0, 1)]
        [SerializeField] private float visionSuppression = 0f;

        public bool CreatesSurface => createsSurface;
        public bool Solid => solid;

        public bool PassageBlockingSolid => passageBlockingSolid;

        public float SurfaceHeight => surfaceHeight;
        public float PropHeight => propHeight;

        public float SoundSuppression => Mathf.Clamp01(soundSuppression);
        public float VisionSuppression => Mathf.Clamp01(visionSuppression);

        public Vector2Int Size { get; private set; }

        private readonly List<GridCell> occupiedCells = new();

        public IReadOnlyList<GridCell> OccupiedCells => occupiedCells;

        private void Awake()
        {
            UpdateFromTransform();

            if (GridWorld.Instance != null)
                GridWorld.Instance.RegisterProp(this);
        }

        private void OnDestroy()
        {
            if (GridWorld.Instance != null)
                GridWorld.Instance.UnregisterProp(this);
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            UpdateFromTransform();
        }
#endif

        public void RegisterCell(GridCell cell)
        {
            if (!occupiedCells.Contains(cell))
                occupiedCells.Add(cell);
        }

        public void ClearCells()
        {
            occupiedCells.Clear();
        }

        public void UpdateFromTransform()
        {
            Vector3 scale = transform.localScale;

            int sx = Mathf.Max(1, Mathf.RoundToInt(baseSize.x * scale.x));
            int sy = Mathf.Max(1, Mathf.RoundToInt(baseSize.y * scale.z));

            Size = new Vector2Int(sx, sy);
        }

        /// <summary>
        /// World-space multiplier for serialized vertical offsets (surfaceHeight, propHeight).
        /// Matches how footprint uses localScale on X/Z; uses lossyScale so parent scale is respected.
        /// </summary>
        protected float AuthoringHeightScale => Mathf.Abs(transform.lossyScale.y);

        /// <summary>Serialized surfaceHeight converted to world offset along local up (scaled).</summary>
        protected float ScaledAuthoringSurfaceOffset => surfaceHeight * AuthoringHeightScale;

        /// <summary>
        /// Pivot cell (world position); footprint tiles may extend outside this AABB when rotated.
        /// Registration uses <see cref="EnumerateOccupiedGridCells"/> for actual coverage.
        /// </summary>
        public Vector2Int GetOrigin()
        {
            Vector3 pos = transform.position;
            return GridWorld.Instance != null
                ? GridWorld.Instance.WorldToGridXZ(pos)
                : new Vector2Int(
                    Mathf.FloorToInt(pos.x),
                    Mathf.FloorToInt(pos.z));
        }

        /// <summary>
        /// Full edge vectors in world space from pivot along local +X and +Z (rotation only; tile counts in <see cref="Size"/>).
        /// </summary>
        protected void GetFootprintEdgeVectorsWorld(out Vector3 ex, out Vector3 ez)
        {
            float cs = GridWorld.Instance != null ? GridWorld.Instance.CellSizeXZ : 1f;
            Vector2Int sz = Size;
            ex = transform.TransformDirection(Vector3.right) * (sz.x * cs);
            ez = transform.TransformDirection(Vector3.forward) * (sz.y * cs);
        }

        /// <summary>
        /// Grid tiles whose XZ footprint intersects the oriented parallelogram at the pivot (local +X by Size.x, +Z by Size.y in tile widths).
        /// </summary>
        public IEnumerable<Vector2Int> EnumerateOccupiedGridCells()
        {
            GridWorld gw = GridWorld.Instance;
            if (gw == null)
            {
                Vector2Int o = GetOrigin();
                for (int x = o.x; x < o.x + Size.x; x++)
                {
                    for (int y = o.y; y < o.y + Size.y; y++)
                        yield return new Vector2Int(x, y);
                }

                yield break;
            }

            float cs = gw.CellSizeXZ;
            Vector3 p = transform.position;
            Vector2 p0 = new Vector2(p.x, p.z);
            GetFootprintEdgeVectorsWorld(out Vector3 ex3, out Vector3 ez3);
            Vector2 ex = new Vector2(ex3.x, ex3.z);
            Vector2 ez = new Vector2(ez3.x, ez3.z);

            const float pad = 1e-4f;
            Vector2[] corners =
            {
                p0,
                p0 + ex,
                p0 + ez,
                p0 + ex + ez
            };

            float minX = corners[0].x, maxX = corners[0].x;
            float minZ = corners[0].y, maxZ = corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                minX = Mathf.Min(minX, corners[i].x);
                maxX = Mathf.Max(maxX, corners[i].x);
                minZ = Mathf.Min(minZ, corners[i].y);
                maxZ = Mathf.Max(maxZ, corners[i].y);
            }

            int gx0 = Mathf.FloorToInt((minX + pad) / cs);
            int gx1 = Mathf.FloorToInt((maxX - pad) / cs);
            int gz0 = Mathf.FloorToInt((minZ + pad) / cs);
            int gz1 = Mathf.FloorToInt((maxZ - pad) / cs);

            for (int gx = gx0; gx <= gx1; gx++)
            {
                for (int gz = gz0; gz <= gz1; gz++)
                {
                    if (ParallelogramIntersectsGridCellXZ(p0, ex, ez, cs, gx, gz))
                        yield return new Vector2Int(gx, gz);
                }
            }
        }

        /// <summary>Whether this prop's footprint (oriented) covers the given grid cell.</summary>
        public bool OccupiesGridCell(Vector2Int gridPos)
        {
            GridWorld gw = GridWorld.Instance;
            if (gw == null)
            {
                Vector2Int o = GetOrigin();
                Vector2Int sz = Size;
                return gridPos.x >= o.x && gridPos.x < o.x + sz.x &&
                       gridPos.y >= o.y && gridPos.y < o.y + sz.y;
            }

            float cs = gw.CellSizeXZ;
            Vector3 p = transform.position;
            Vector2 p0 = new Vector2(p.x, p.z);
            GetFootprintEdgeVectorsWorld(out Vector3 ex3, out Vector3 ez3);
            Vector2 ex = new Vector2(ex3.x, ex3.z);
            Vector2 ez = new Vector2(ez3.x, ez3.z);
            return ParallelogramIntersectsGridCellXZ(p0, ex, ez, cs, gridPos.x, gridPos.y);
        }

        private static bool ParallelogramIntersectsGridCellXZ(
            Vector2 p0, Vector2 ex, Vector2 ez, float cellSize, int gx, int gy)
        {
            float minX = gx * cellSize;
            float maxX = (gx + 1) * cellSize;
            float minZ = gy * cellSize;
            float maxZ = (gy + 1) * cellSize;

            Vector2[] quad = { p0, p0 + ex, p0 + ex + ez, p0 + ez };
            Vector2[] rect =
            {
                new Vector2(minX, minZ),
                new Vector2(maxX, minZ),
                new Vector2(maxX, maxZ),
                new Vector2(minX, maxZ)
            };

            Vector2 axisEx = new Vector2(-ex.y, ex.x);
            if (axisEx.sqrMagnitude > 1e-8f)
                axisEx.Normalize();
            else
                axisEx = Vector2.right;

            Vector2 axisEz = new Vector2(-ez.y, ez.x);
            if (axisEz.sqrMagnitude > 1e-8f)
                axisEz.Normalize();
            else
                axisEz = Vector2.up;

            Vector2[] axes = { Vector2.right, Vector2.up, axisEx, axisEz };

            foreach (Vector2 axis in axes)
            {
                if (axis.sqrMagnitude < 1e-8f)
                    continue;

                ProjectPolygon(axis, quad, out float qMin, out float qMax);
                ProjectPolygon(axis, rect, out float rMin, out float rMax);
                if (qMax < rMin - 1e-4f || rMax < qMin - 1e-4f)
                    return false;
            }

            return true;
        }

        private static void ProjectPolygon(Vector2 axis, Vector2[] pts, out float min, out float max)
        {
            min = max = Vector2.Dot(pts[0], axis);
            for (int i = 1; i < pts.Length; i++)
            {
                float d = Vector2.Dot(pts[i], axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }

        public float GetSurfaceWorldHeight()
        {
            return transform.position.y + surfaceHeight * AuthoringHeightScale;
        }

        public float GetTopWorldHeight()
        {
            return transform.position.y + propHeight * AuthoringHeightScale;
        }
    }
}

/*
Example Usage:

Ground Floor Tile:
CreatesSurface = true
SurfaceHeight = 0
PropHeight = 0

BalconyFloor:
CreatesSurface = true
SurfaceHeight = 4

Crate (Half-Cover):
CreatesSurface = false
PropHeight = 1
BlocksMovement = false


Refrigerator (Full-Cover):
CreatesSurface = false
PropHeight = 2
BlocksMovement = true


*/