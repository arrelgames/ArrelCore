using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Walkable ramp deck along the footprint axis. Default deck Y is pivot + PropHeight × t; enable
    /// align-deck-to-vertical-cell-centers to snap endpoints to GridWorld vertical cell centers (CellSizeY).
    /// </summary>
    public class GridPropRamp : GridProp
    {
        public enum RampAxisMode
        {
            Auto,
            AlongFootprintX,
            AlongFootprintY
        }

        [Header("Ramp Shape")]
        [Tooltip("Solid fill from ground (transform Y) up to the deck; no walking underneath.")]
        [SerializeField] private bool filled;

        [Tooltip("When unfilled, cells below the deck use this underside height for ceiling clearance.")]
        [SerializeField] private float deckThickness = 0.05f;

        [SerializeField] private RampAxisMode rampAxis = RampAxisMode.Auto;

        [Tooltip("If true, low end is at min index on the ramp axis; if false, low end is at max index.")]
        [SerializeField] private bool lowAtMinOnAxis = true;

        [Tooltip(
            "When enabled, deck Y lerps between vertical cell centers: low = pivot + SurfaceHeight + CellSizeY/2, high = that + PropHeight - CellSizeY (GridWorld CellSizeY). " +
            "Use SurfaceHeight 0 for a ramp from the first cell center upward. When disabled, deck uses pivot Y + PropHeight × t (ignores SurfaceHeight).")]
        [SerializeField] private bool alignDeckToVerticalCellCenters;

        public bool Filled => filled;

        public float DeckThickness => Mathf.Max(0.001f, deckThickness * AuthoringHeightScale);

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

        private void OnValidate()
        {
            UpdateFromTransform();
        }

        public float GetDeckWorldYAtWorldGrid(int gridX, int gridY)
        {
            Vector2Int o = GetOrigin();
            return GetDeckWorldYAtFootprintLocal(gridX - o.x, gridY - o.y);
        }

        /// <summary>Local footprint indices relative to ramp origin (0 .. Size-1).</summary>
        public float GetDeckWorldYAtFootprintLocal(int footprintLocalX, int footprintLocalY)
        {
            Vector2Int sz = Size;
            RampAxisMode axis = ResolveAxis(sz);

            float t;
            if (axis == RampAxisMode.AlongFootprintX)
            {
                float span = Mathf.Max(1, sz.x - 1);
                float u = lowAtMinOnAxis ? footprintLocalX : (sz.x - 1 - footprintLocalX);
                u = Mathf.Clamp(u, 0f, sz.x - 1);
                t = u / span;
            }
            else
            {
                float span = Mathf.Max(1, sz.y - 1);
                float u = lowAtMinOnAxis ? footprintLocalY : (sz.y - 1 - footprintLocalY);
                u = Mathf.Clamp(u, 0f, sz.y - 1);
                t = u / span;
            }

            float propWorld = PropHeight * AuthoringHeightScale;

            if (alignDeckToVerticalCellCenters && GridWorld.Instance != null)
            {
                float cy = GridWorld.Instance.CellSizeY;
                float baseWorld = transform.position.y + ScaledAuthoringSurfaceOffset;
                float yLow = baseWorld + 0.5f * cy;
                float yHigh = baseWorld + propWorld - 0.5f * cy;
                if (yHigh < yLow)
                    yHigh = yLow;
                return Mathf.Lerp(yLow, yHigh, t);
            }

            return transform.position.y + propWorld * t;
        }

        private RampAxisMode ResolveAxis(Vector2Int sz)
        {
            if (rampAxis != RampAxisMode.Auto)
                return rampAxis;

            if (sz.x > 1 && sz.y <= 1)
                return RampAxisMode.AlongFootprintX;
            if (sz.y > 1 && sz.x <= 1)
                return RampAxisMode.AlongFootprintY;
            return sz.x >= sz.y ? RampAxisMode.AlongFootprintX : RampAxisMode.AlongFootprintY;
        }
    }
}
