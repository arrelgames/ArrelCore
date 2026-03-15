using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class PatrolBehavior : IBehavior, IMovementIntentProvider
    {
        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1f;
        private float nextDebugLogTime;

        private enum PatrolState
        {
            MovingToA,
            WaitingAtA,
            MovingToB,
            WaitingAtB
        }

        private readonly Unit unit;
        private readonly GridWorld gridWorld;
        private readonly GridPathFollower pathFollower;

        private readonly Vector2Int patrolPointA;
        private readonly Vector2Int patrolPointB;

        private readonly float waitDurationSeconds;

        private PatrolState currentState;
        private PatrolState? lastLoggedState;

        private float waitTimer;

        public Vector2 CurrentMoveInput { get; private set; }
        public bool JumpRequested { get; private set; }

        public PatrolBehavior(
            Unit unit,
            GridWorld gridWorld,
            Vector2Int patrolPointA,
            Vector2Int patrolPointB,
            float waitDurationSeconds = 1f,
            int jumpHeight = 0)
        {
            this.unit = unit;
            this.gridWorld = gridWorld;
            this.patrolPointA = patrolPointA;
            this.patrolPointB = patrolPointB;
            this.waitDurationSeconds = waitDurationSeconds;

            pathFollower = new GridPathFollower(unit, gridWorld, turnSpeed: 18f, arrivalThreshold: 0.1f, jumpHeight: jumpHeight);
            currentState = PatrolState.MovingToA;
        }

        public TaskStatus Execute()
        {
            CurrentMoveInput = Vector2.zero;
            JumpRequested = false;

            LogStateChangeIfNeeded();

            switch (currentState)
            {
                case PatrolState.MovingToA:
                    return HandleMovingState(patrolPointA, PatrolState.WaitingAtA);
                case PatrolState.WaitingAtA:
                    return HandleWaitingState(PatrolState.MovingToB);
                case PatrolState.MovingToB:
                    return HandleMovingState(patrolPointB, PatrolState.WaitingAtB);
                case PatrolState.WaitingAtB:
                    return HandleWaitingState(PatrolState.MovingToA);
                default:
                    return TaskStatus.Running;
            }
        }

        private TaskStatus HandleMovingState(Vector2Int targetGridPos, PatrolState nextWaitingState)
        {
            if (gridWorld == null || unit == null)
            {
                if (DebugEnabled)
                {
                    Debug.LogWarning("[PatrolBehavior] Missing gridWorld or unit reference; cannot move.");
                }
                return TaskStatus.Failure;
            }

            // Delegate movement to shared GridPathFollower.
            pathFollower.SetDestination(targetGridPos);
            TaskStatus moveStatus = pathFollower.Update();

            CurrentMoveInput = pathFollower.CurrentMoveInput;
            JumpRequested = pathFollower.JumpRequested;

            if (DebugEnabled && Time.time >= nextDebugLogTime)
            {
                nextDebugLogTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log($"[PatrolBehavior] Moving state={currentState} targetGridPos={targetGridPos} moveStatus={moveStatus} pos={unit.transform.position}");
            }

            if (moveStatus == TaskStatus.Success)
            {
                // Arrived at this patrol point; transition to waiting state.
                waitTimer = 0f;
                currentState = nextWaitingState;
                if (DebugEnabled)
                {
                    Debug.Log($"[PatrolBehavior] Arrived at target={targetGridPos}. Transitioning to state={currentState} wait={waitDurationSeconds:0.00}s");
                }
                return TaskStatus.Running;
            }

            if (moveStatus == TaskStatus.Failure)
            {
                if (DebugEnabled)
                {
                    Debug.LogWarning($"[PatrolBehavior] Movement to target={targetGridPos} failed.");
                }
                return TaskStatus.Failure;
            }

            return TaskStatus.Running;
        }

        private TaskStatus HandleWaitingState(PatrolState nextMovingState)
        {
            waitTimer += Time.deltaTime;

            if (DebugEnabled && Time.time >= nextDebugLogTime)
            {
                nextDebugLogTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log($"[PatrolBehavior] Waiting state={currentState} t={waitTimer:0.00}/{waitDurationSeconds:0.00}");
            }

            if (waitTimer < waitDurationSeconds)
            {
                return TaskStatus.Running;
            }

            // Done waiting, switch to moving state; a new path will be requested on next Execute
            currentState = nextMovingState;

            if (DebugEnabled)
            {
                Debug.Log($"[PatrolBehavior] Done waiting. Transitioning to state={currentState}");
            }

            return TaskStatus.Running;
        }

        private void LogStateChangeIfNeeded()
        {
            if (!DebugEnabled)
            {
                return;
            }

            if (lastLoggedState == null || lastLoggedState.Value != currentState)
            {
                Debug.Log($"[PatrolBehavior] State={currentState}");
                lastLoggedState = currentState;
            }
        }
    }
}

