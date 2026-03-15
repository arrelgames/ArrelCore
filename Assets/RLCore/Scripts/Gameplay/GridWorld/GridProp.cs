using UnityEngine;

namespace RLGames
{
    public class GridProp : MonoBehaviour
    {
        [SerializeField] private Vector2Int origin;

        [Header("Grid Settings")]
        [SerializeField] private Vector2Int baseSize = Vector2Int.one;
        [Tooltip("Height of this prop in grid units. Used for height-aware pathfinding.")]
        [SerializeField] private int height = 1;

        public bool isBlocked = true;

        public int Height => height;

        public Vector2Int Size { get; private set; }

        private void Awake()
        {
            UpdateFromTransform();

            // Ensure this prop is registered with the active GridWorld so that
            // navigation and blocking information is correctly applied.
            if (GridWorld.Instance != null)
            {
                GridWorld.Instance.RegisterProp(this);
            }
            else
            {
                GridWorld.AssertInstance("GridProp.Awake");
            }
        }

        public void UpdateFromTransform()
        {
            UpdateOriginFromWorldPosition();
            UpdateSizeFromScale();
        }

        public void UpdateOriginFromWorldPosition()
        {
            Vector3 worldPos = transform.position;

            origin = new Vector2Int(
                Mathf.RoundToInt(worldPos.x),
                Mathf.RoundToInt(worldPos.z)
            );
        }

        private void UpdateSizeFromScale()
        {
            Vector3 scale = transform.localScale;

            int scaledX = Mathf.RoundToInt(baseSize.x * scale.x);
            int scaledY = Mathf.RoundToInt(baseSize.y * scale.z);

            Size = new Vector2Int(scaledX, scaledY);
        }

        public Vector2Int GetOrigin() => origin;
    }
}