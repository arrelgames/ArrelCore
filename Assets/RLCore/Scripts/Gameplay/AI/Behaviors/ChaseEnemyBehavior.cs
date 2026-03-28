using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class ChaseEnemyBehavior : IBehavior, IMovementIntentProvider, IDebugPathFollowerProvider
    {
        private readonly Unit unit;
        private readonly GridWorld gridWorld;
        private readonly float detectionRadius;
        private readonly float enemyPickInterval;

        private readonly GridPathFollower pathFollower;
        private readonly AStarNavigator navigator;

        private float pickCooldownRemaining;
        private int lastNearbyEnemyCount = -1;

        public Vector2 CurrentMoveInput { get; private set; }
        public bool JumpRequested { get; private set; }
        public GridPathFollower DebugPathFollower => pathFollower;

        public ChaseEnemyBehavior(
            Unit unit,
            GridWorld gridWorld,
            float detectionRadius,
            float enemyPickInterval = 0.35f)
        {
            this.unit = unit;
            this.gridWorld = gridWorld;
            this.detectionRadius = detectionRadius;
            this.enemyPickInterval = Mathf.Max(0.05f, enemyPickInterval);
            pathFollower = new GridPathFollower(unit, gridWorld);
            navigator = new AStarNavigator(gridWorld, new NavigationSettings());
        }

        public TaskStatus Execute()
        {
            CurrentMoveInput = Vector2.zero;
            JumpRequested = false;

            if (gridWorld == null || unit == null)
                return TaskStatus.Faulted;

            var manager = UnitManager.Instance;
            if (manager == null)
                return TaskStatus.Running;

            var enemies = manager.GetNearbyEnemies(unit, detectionRadius);

            if (enemies == null || enemies.Count == 0)
            {
                pickCooldownRemaining = 0f;
                lastNearbyEnemyCount = -1;
                return TaskStatus.Running;
            }

            if (enemies.Count != lastNearbyEnemyCount)
            {
                lastNearbyEnemyCount = enemies.Count;
                pickCooldownRemaining = 0f;
            }

            pickCooldownRemaining -= Time.deltaTime;
            if (pickCooldownRemaining <= 0f)
            {
                pickCooldownRemaining = enemyPickInterval;
                RefreshChaseTarget(enemies);
            }

            TaskStatus status = pathFollower.Update();
            CurrentMoveInput = pathFollower.CurrentMoveInput;
            JumpRequested = pathFollower.JumpRequested;

            if (status == TaskStatus.Faulted)
                return TaskStatus.Faulted;

            return TaskStatus.Running;
        }

        private void RefreshChaseTarget(List<Unit> enemies)
        {
            GridNode start = gridWorld.GetClosestNode(unit.transform.position);

            Unit bestEnemy = null;
            GridNode bestGoal = default;
            float bestCost = float.MaxValue;
            bool anyPath = false;

            foreach (Unit enemy in enemies)
            {
                if (enemy == null)
                    continue;

                GridNode goal = gridWorld.GetClosestNode(enemy.transform.position);
                List<GridNode> path = navigator.FindPath(start, goal);

                if (path == null)
                    continue;

                float cost = PathCost(gridWorld, path);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestGoal = goal;
                    bestEnemy = enemy;
                    anyPath = true;
                }
            }

            if (anyPath && bestEnemy != null)
            {
                pathFollower.SetDestination(bestGoal);
                return;
            }

            // No reachable path to any enemy — move toward closest in world XZ.
            Unit fallback = FindClosestEnemyHorizontal(enemies);
            if (fallback != null)
                pathFollower.SetDestination(gridWorld.GetClosestNode(fallback.transform.position));
        }

        private Unit FindClosestEnemyHorizontal(List<Unit> enemies)
        {
            Vector3 p = unit.transform.position;
            p.y = 0f;

            Unit best = null;
            float bestSq = float.MaxValue;

            foreach (Unit enemy in enemies)
            {
                if (enemy == null)
                    continue;

                Vector3 q = enemy.transform.position;
                q.y = 0f;
                float sq = (q - p).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = enemy;
                }
            }

            return best;
        }

        private static float PathCost(GridWorld grid, List<GridNode> path)
        {
            if (path == null || path.Count < 2)
                return 0f;

            float total = 0f;

            for (int i = 0; i < path.Count - 1; i++)
            {
                GridNode from = path[i];
                GridNode to = path[i + 1];
                IReadOnlyList<GridEdge> edges = grid.GetEdges(from);
                bool found = false;

                for (int e = 0; e < edges.Count; e++)
                {
                    GridEdge edge = edges[e];
                    if (edge.target.Equals(to))
                    {
                        total += edge.cost;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return float.MaxValue;
            }

            return total;
        }
    }
}
