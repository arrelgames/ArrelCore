using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class FleeBehavior : IBehavior, IMovementIntentProvider
    {
        private Transform unitTransform;
        private List<Transform> nearbyUnits;
        private float fleeDistance;

        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1.0f;
        private float _nextDebugTime;

        public Vector2 CurrentMoveInput { get; private set; }
        public bool JumpRequested => false;

        public FleeBehavior(Transform unitTransform, List<Transform> nearbyUnits, float fleeDistance, float moveSpeed)
        {
            this.unitTransform = unitTransform;
            this.nearbyUnits = nearbyUnits;
            this.fleeDistance = fleeDistance;
        }

        public TaskStatus Execute()
        {
            CurrentMoveInput = Vector2.zero;

            if (unitTransform == null || nearbyUnits == null || nearbyUnits.Count == 0)
            {
                if (DebugEnabled && Time.time >= _nextDebugTime)
                {
                    _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                    Debug.Log($"[FleeBehavior] unit='{unitTransform?.name ?? "NULL"}' has no nearbyUnits list or it is empty. Returning Success.");
                }
                return TaskStatus.Success;
            }

            Vector3 fleeDirWorld = Vector3.zero;
            bool shouldFlee = false;

            foreach (var nearbyUnit in nearbyUnits)
            {
                if (nearbyUnit == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(unitTransform.position, nearbyUnit.position);
                if (distance < fleeDistance)
                {
                    // Accumulate flee direction away from nearby threats
                    fleeDirWorld += (unitTransform.position - nearbyUnit.position);
                    shouldFlee = true;
                }
            }

            if (!shouldFlee || fleeDirWorld.sqrMagnitude < 0.0001f)
            {
                if (DebugEnabled && Time.time >= _nextDebugTime)
                {
                    _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                    Debug.Log($"[FleeBehavior] unit='{unitTransform.name}' no valid flee direction (shouldFlee={shouldFlee}, mag2={fleeDirWorld.sqrMagnitude:0.0000}). Returning Success.");
                }
                return TaskStatus.Success; // No nearby units to flee from
            }

            fleeDirWorld.y = 0f;
            Vector3 dir = fleeDirWorld.normalized;

            // Project flee direction into local space (right/forward)
            Vector3 right = unitTransform.right;
            Vector3 forward = unitTransform.forward;

            float x = Vector3.Dot(dir, right);
            float y = Vector3.Dot(dir, forward);

            Vector2 moveInput = new Vector2(x, y);
            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            CurrentMoveInput = moveInput;

            if (DebugEnabled && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log(
                    $"[FleeBehavior] unit='{unitTransform.name}' fleeing. threats={nearbyUnits.Count}, " +
                    $"fleeDirWorld=({fleeDirWorld.x:0.00},{fleeDirWorld.y:0.00},{fleeDirWorld.z:0.00}), " +
                    $"move=({CurrentMoveInput.x:0.00},{CurrentMoveInput.y:0.00})");
            }
            return TaskStatus.Running; // Fleeing from nearby units
        }

        internal void UpdateEnemies(List<Transform> transforms)
        {
            nearbyUnits = transforms;
        }
    }
}