using UnityEngine;

namespace RLGames
{
    public class WanderBehavior : IBehavior, IMovementIntentProvider
    {
        private readonly Unit unit;
        private readonly GridWorld gridWorld;
        private readonly GridPathFollower pathFollower;

        private readonly float wanderRadius = 10f;

        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1.0f;
        private float _nextDebugTime;

        public Vector2 CurrentMoveInput { get; private set; }
        public bool JumpRequested => false;

        public WanderBehavior(Unit unit, GridWorld gridWorld)
        {
            this.unit = unit;
            this.gridWorld = gridWorld;
            pathFollower = new GridPathFollower(unit, gridWorld, turnSpeed: 18f, arrivalThreshold: 0.1f);
        }

        public TaskStatus Execute()
        {
            CurrentMoveInput = Vector2.zero;

            // If we don't have an active path, pick a new random nearby destination cell.
            if (!pathFollower.HasActivePath)
            {
                ChooseNewDestinationCell();
            }

            // Let the shared follower update rotation and movement intent.
            TaskStatus status = pathFollower.Update();
            CurrentMoveInput = pathFollower.CurrentMoveInput;

            if (DebugEnabled && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log(
                    $"[WanderBehavior] unit='{unit.name}' status={status} pos={unit.transform.position} " +
                    $"move=({CurrentMoveInput.x:0.00},{CurrentMoveInput.y:0.00}) hasPath={pathFollower.HasActivePath}");
            }

            // Treat wandering as an ongoing behavior; even when we reach a target,
            // we'll choose a new one on the next Execute call.
            if (status == TaskStatus.Failure)
            {
                return TaskStatus.Failure;
            }

            return TaskStatus.Running;
        }

        private void ChooseNewDestinationCell()
        {
            if (gridWorld == null || unit == null)
            {
                if (DebugEnabled)
                {
                    Debug.LogWarning("[WanderBehavior] Missing gridWorld or unit; cannot choose destination.");
                }
                return;
            }

            Vector2Int currentCell = gridWorld.WorldToGrid(unit.transform.position);
            int maxAttempts = 10;
            int radius = Mathf.CeilToInt(wanderRadius);

            for (int i = 0; i < maxAttempts; i++)
            {
                int dx = Random.Range(-radius, radius + 1);
                int dy = Random.Range(-radius, radius + 1);
                Vector2Int candidate = new Vector2Int(currentCell.x + dx, currentCell.y + dy);

                if (gridWorld.IsPositionNavigable(candidate))
                {
                    pathFollower.SetDestination(candidate);
                    if (DebugEnabled)
                    {
                        Debug.Log($"[WanderBehavior] unit='{unit.name}' chose new destination cell={candidate} from={currentCell}");
                    }
                    return;
                }
            }

            if (DebugEnabled)
            {
                Debug.LogWarning($"[WanderBehavior] unit='{unit.name}' failed to find navigable wander destination from cell={currentCell}");
            }
        }
    }
}