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

        [Range(0, 1)]
        [SerializeField] private float soundSuppression = 0f;

        [Range(0, 1)]
        [SerializeField] private float visionSuppression = 0f;

        public bool CreatesSurface => createsSurface;
        public bool Solid => solid;

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

        public Vector2Int GetOrigin()
        {
            Vector3 pos = transform.position;
            return GridWorld.Instance.WorldToGridXZ(pos);
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