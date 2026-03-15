using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    [DefaultExecutionOrder(-1000)]
    public class GridWorld : MonoBehaviour
    {
        public static GridWorld Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;

        private Dictionary<Vector2Int, GridCell> gridCells = new Dictionary<Vector2Int, GridCell>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning(
                        $"[GridWorld] Duplicate instance detected on '{name}' in scene '{gameObject.scene.name}'. " +
                        $"Existing Instance is on '{Instance.name}'. Destroying this duplicate.",
                        this);
                }

                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[GridWorld] Awake. Became Instance. name='{name}', scene='{gameObject.scene.name}', " +
                    $"instanceID={GetInstanceID()}",
                    this);
            }
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void AssertInstance(string caller = null)
        {
            if (Instance != null)
                return;

            string callerInfo = string.IsNullOrEmpty(caller) ? "UnknownCaller" : caller;

            Debug.LogError(
                $"[GridWorld] AssertInstance failed (caller={callerInfo}). GridWorld.Instance is NULL. " +
                "Ensure there is exactly one active GridWorld GameObject in the starting scene.");
        }

        // =========================
        // PROP REGISTRATION
        // =========================

        public void RegisterProp(GridProp prop)
        {
            if (prop == null)
                return;

            // Ensure prop updates origin + scaled size from its transform
            prop.UpdateFromTransform();

            Vector2Int origin = prop.GetOrigin();
            Vector2Int size = prop.Size;

            for (int x = origin.x; x < origin.x + size.x; x++)
            {
                for (int y = origin.y; y < origin.y + size.y; y++)
                {
                    Vector2Int cellPosition = new Vector2Int(x, y);

                    RegisterCell(cellPosition);
                    gridCells[cellPosition].AddProp(prop);

                    if (prop.isBlocked)
                        gridCells[cellPosition].isBlocked = true;
                }
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[GridWorld] Registered prop '{prop.name}' at {origin} size {size}", prop);
            }
        }

        private void RegisterCell(Vector2Int position)
        {
            if (!gridCells.ContainsKey(position))
                gridCells[position] = new GridCell();
        }

        public GridCell GetCell(Vector2Int position)
        {
            gridCells.TryGetValue(position, out GridCell cell);
            return cell;
        }

        // =========================
        // GRID CONVERSION
        // =========================

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x),
                Mathf.FloorToInt(worldPos.z)
            );
        }

        public Vector3 GridToWorld(Vector2Int gridPos)
        {
            return new Vector3(
                gridPos.x + 0.5f,
                0f,
                gridPos.y + 0.5f
            );
        }

        // =========================
        // NAVIGATION
        // =========================

        public bool IsPositionNavigable(Vector2Int gridPos)
        {
            GridCell cell = GetCell(gridPos);

            if (cell == null)
                return true;

            return !cell.isBlocked;
        }

        public Vector3 GetClosestNavigablePosition(Vector3 position)
        {
            Vector2Int gridPos = WorldToGrid(position);

            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    Vector2Int checkPos = new Vector2Int(gridPos.x + x, gridPos.y + y);

                    if (IsPositionNavigable(checkPos))
                        return GridToWorld(checkPos);
                }
            }

            return position;
        }

        public List<Vector3> GetNearbyNavigablePositions(Vector3 position, float radius)
        {
            List<Vector3> navigablePositions = new List<Vector3>();
            Vector2Int gridPos = WorldToGrid(position);

            int r = Mathf.FloorToInt(radius);

            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    Vector2Int checkPos = new Vector2Int(gridPos.x + x, gridPos.y + y);

                    if (IsPositionNavigable(checkPos))
                        navigablePositions.Add(GridToWorld(checkPos));
                }
            }

            return navigablePositions;
        }
    }
}