using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class GridPathFollower
    {
        private readonly Unit unit;
        private readonly GridWorld gridWorld;
        // private readonly HierarchicalNavigator navigator;
        private readonly AStarNavigator navigator;

        private List<GridNode> currentPath;
        private int currentIndex;
        private bool isPathPending;

        private GridNode? destination;

        private float repathTimer;
        private const float REPATHTIME = 0.5f;

        public Vector2 CurrentMoveInput { get; private set; }
        public bool JumpRequested { get; private set; }

        /// <summary>
        /// True when we have an active destination and either a path request is pending
        /// or we still have nodes left to follow.
        /// </summary>
        public bool HasActivePath =>
            destination.HasValue &&
            (isPathPending ||
             (currentPath != null && currentIndex < currentPath.Count));

        [Header("Debug - Path Logging")]
        [SerializeField] private bool logPathOnRequest = true;
        [SerializeField] private bool logNodeProgress = false;

        private int lastLoggedIndex = -1;

        public GridPathFollower(Unit unit, GridWorld gridWorld)
        {
            this.unit = unit;
            this.gridWorld = gridWorld;

            NavigationSettings settings = new NavigationSettings();
            // navigator = new HierarchicalNavigator(gridWorld, settings);
            navigator = new AStarNavigator(gridWorld, settings);
        }

        private static string FormatNode(GridNode n)
        {
            // Using `s{surface}` keeps it unambiguous in logs.
            return $"({n.x},{n.y},s{n.surface})";
        }

        private static string FormatPath(List<GridNode> path)
        {
            if (path == null)
                return "null";

            if (path.Count == 0)
                return "[]";

            // Low-allocation formatting (small paths are typical).
            string result = FormatNode(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                result += " -> " + FormatNode(path[i]);
            }

            return result;
        }

        public void SetDestination(GridNode node)
        {
            destination = node;
            currentPath = null;
            currentIndex = 0;
        }

        public TaskStatus Update()
        {
            CurrentMoveInput = Vector2.zero;
            JumpRequested = false;

            if (!destination.HasValue)
                return TaskStatus.Faulted;

            GridNode start = gridWorld.GetClosestNode(unit.transform.position);

            if (start.Equals(destination.Value))
                return TaskStatus.RanToCompletion;

            repathTimer -= Time.deltaTime;

            if ((currentPath == null || repathTimer <= 0f) && !isPathPending)
            {
                RequestPath(start, destination.Value);
                repathTimer = REPATHTIME;
                return TaskStatus.Running;
            }

            if (currentPath == null || currentIndex >= currentPath.Count)
                return TaskStatus.Running;

            GridNode node = currentPath[currentIndex];
            GridCell cell = gridWorld.GetCell(node);

            if (cell == null)
            {
                currentIndex++;
                return TaskStatus.Running;
            }

            Vector3 targetWorld = gridWorld.GridToWorldXZ(new Vector2Int(node.x, node.y));
            targetWorld.y = cell.surfaceHeight;

            Vector3 direction = targetWorld - unit.transform.position;
            float vertical = direction.y;
            direction.y = 0f;

            float dist = direction.magnitude;

            if (dist > 0.01f)
            {
                Vector3 moveDir = direction.normalized;

                // Smooth rotation
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                unit.transform.rotation = Quaternion.Slerp(
                    unit.transform.rotation,
                    targetRot,
                    18f * Time.deltaTime);

                // Convert to local movement input
                Vector3 local = unit.transform.InverseTransformDirection(moveDir);
                CurrentMoveInput = new Vector2(local.x, local.z);
            }

            // 🔥 XCOM-style: use edge data for jump/fall
            if (currentIndex < currentPath.Count - 1)
            {
                GridNode currentNode = currentPath[currentIndex];
                GridNode nextNode = currentPath[currentIndex + 1];

                var edges = gridWorld.GetEdges(currentNode);

                foreach (var edge in edges)
                {
                    if (edge.target.Equals(nextNode))
                    {
                        JumpRequested = edge.isClimb || edge.isFall;
                        break;
                    }
                }
            }

            // Advance to next node when close
            int prevIndex = currentIndex;
            if (dist < 0.1f)
                currentIndex++;

            if (logNodeProgress &&
                currentPath != null &&
                currentIndex != prevIndex &&
                currentIndex != lastLoggedIndex)
            {
                lastLoggedIndex = currentIndex;

                string reachedNode = (prevIndex >= 0 && prevIndex < currentPath.Count)
                    ? FormatNode(currentPath[prevIndex])
                    : "n/a";

                string nextNode = (currentIndex >= 0 && currentIndex < currentPath.Count)
                    ? FormatNode(currentPath[currentIndex])
                    : "end";

                Debug.Log(
                    $"[GridPathFollower] Advance {prevIndex}->{currentIndex} reached={reachedNode} next={nextNode}");
            }

            return TaskStatus.Running;
        }

        private async void RequestPath(GridNode start, GridNode goal)
        {
            isPathPending = true;

            List<GridNode> path = await navigator.FindPathAsync(start, goal);

            currentPath = path;
            currentIndex = 0;

            if (logPathOnRequest)
            {
                if (path == null || path.Count == 0)
                {
                    Debug.Log($"[GridPathFollower] No path from {FormatNode(start)} to {FormatNode(goal)}");
                }
                else
                {
                    Debug.Log(
                        $"[GridPathFollower] Path {FormatNode(start)} -> {FormatNode(goal)} (count={path.Count}) : {FormatPath(path)}");
                }
            }

            isPathPending = false;
        }

        // =========================
        // 🔍 DEBUG ACCESSORS
        // =========================

        public List<GridNode> Debug_GetPath()
        {
            return currentPath;
        }

        public int Debug_GetCurrentIndex()
        {
            return currentIndex;
        }
    }
}