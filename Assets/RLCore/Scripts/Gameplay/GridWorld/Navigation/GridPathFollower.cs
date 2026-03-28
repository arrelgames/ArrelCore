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

        // Repath gating: avoid re-requesting the route while we are still making progress.
        private GridNode lastRequestedStart;
        private bool hasLastRequestedStart;
        private float lastProgressTime;
        private int lastProgressIndex = -1;
        private const float STUCK_TIME = 1.25f;

        // Arrival hysteresis: avoids index-advance flicker around the threshold.
        [Header("Arrival Hysteresis")]
        [SerializeField] private float advanceDistanceEnter = 0.2f;
        [SerializeField] private float advanceDistanceExit = 0.25f;
        [SerializeField] private int advanceFramesRequired = 2;

        private int advanceFrameCounter;

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

            hasLastRequestedStart = false;
            lastProgressTime = 0f;
            lastProgressIndex = -1;
            advanceFrameCounter = 0;
            lastLoggedIndex = -1;
        }

        /// <summary>
        /// Clears destination and path state (e.g. after arrival or when switching behaviors).
        /// </summary>
        public void ClearDestination()
        {
            destination = null;
            currentPath = null;
            currentIndex = 0;
            isPathPending = false;
            hasLastRequestedStart = false;
            lastProgressTime = 0f;
            lastProgressIndex = -1;
            advanceFrameCounter = 0;
            lastLoggedIndex = -1;
            CurrentMoveInput = Vector2.zero;
            JumpRequested = false;
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

            if (currentPath == null && !isPathPending)
            {
                RequestPath(start, destination.Value);
                repathTimer = REPATHTIME;
                lastRequestedStart = start;
                hasLastRequestedStart = true;
                lastProgressTime = Time.time;
                lastProgressIndex = currentIndex;
                return TaskStatus.Running;
            }

            if (repathTimer <= 0f && !isPathPending && currentPath != null)
            {
                bool pathExhausted = currentPath == null || currentIndex >= currentPath.Count;
                // Closest cell can differ from currentPath[currentIndex] while legitimately moving between
                // waypoints (e.g. still in (6,5) while steering toward (5,5)). Only repath when the agent
                // is not on any node of the current path.
                bool startNotOnPath = false;
                if (hasLastRequestedStart && currentPath != null && currentIndex < currentPath.Count)
                {
                    startNotOnPath = true;
                    for (int k = 0; k < currentPath.Count; k++)
                    {
                        if (start.Equals(currentPath[k]))
                        {
                            startNotOnPath = false;
                            break;
                        }
                    }
                }

                bool stuck = (Time.time - lastProgressTime) > STUCK_TIME;

                if (pathExhausted || stuck || startNotOnPath)
                {
                    RequestPath(start, destination.Value);
                    repathTimer = REPATHTIME;
                    lastRequestedStart = start;
                    hasLastRequestedStart = true;
                    // Keep lastProgressTime/index as-is; it will be updated when we advance.
                    return TaskStatus.Running;
                }

                // Not stale: just reset timer and continue.
                repathTimer = REPATHTIME;
            }

            if (currentPath == null || currentIndex >= currentPath.Count)
            {
                if (destination.HasValue && start.Equals(destination.Value))
                    return TaskStatus.RanToCompletion;
                return TaskStatus.Running;
            }

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
                        JumpRequested = edge.requestsJump;
                        break;
                    }
                }
            }

            // Advance to next node when close
            int prevIndex = currentIndex;

            if (dist < advanceDistanceEnter)
            {
                advanceFrameCounter++;
            }
            else if (dist > advanceDistanceExit)
            {
                advanceFrameCounter = 0;
            }

            if (advanceFrameCounter >= advanceFramesRequired)
            {
                currentIndex++;
                advanceFrameCounter = 0;
            }

            if (currentIndex != prevIndex)
            {
                lastProgressTime = Time.time;
                lastProgressIndex = currentIndex;
            }

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