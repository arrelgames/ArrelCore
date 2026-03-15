using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Brain for deer units that chooses between fleeing and wandering,
    /// and routes movement intent through the normal BrainBase -> Unit -> CharacterMotor pipeline.
    /// </summary>
    [RequireComponent(typeof(Unit))]
    public class DeerAiBrain : BrainBase
    {
        [SerializeField] private float fleeDistance = 10f;
        [SerializeField] private float enemyDetectionRadius = 15f;

        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1.0f;
        private float _nextDebugTime;

        private readonly List<Transform> nearbyEnemies = new List<Transform>();

        private IBehavior currentBehavior;
        private FleeBehavior fleeBehavior;
        private WanderBehavior wanderBehavior;

        protected override void Awake()
        {
            base.Awake();

            if (unit == null)
            {
                Debug.LogWarning("[DeerAiBrain] Unit component is missing; disabling brain.", this);
                enabled = false;
                return;
            }

            GridWorld gridWorld = GridWorld.Instance;
            if (gridWorld == null)
            {
                Debug.LogWarning("[DeerAiBrain] GridWorld.Instance is null; wandering will be disabled.", this);
            }

            // Create behaviors
            wanderBehavior = (gridWorld != null) ? new WanderBehavior(unit, gridWorld) : null;

            // For flee we pass in the Transform and a list we keep updated.
            fleeBehavior = new FleeBehavior(unit.transform, nearbyEnemies, fleeDistance, moveSpeed: 0f);

            currentBehavior = wanderBehavior ?? (IBehavior)fleeBehavior;

            if (DebugEnabled)
            {
                Debug.Log(
                    $"[DeerAiBrain] Awake on '{gameObject.name}'. unit={(unit != null ? "OK" : "MISSING")}, " +
                    $"gridWorld={(gridWorld != null ? "OK" : "NULL")}, wanderBehavior={(wanderBehavior != null)}, " +
                    $"fleeBehavior={(fleeBehavior != null)}, fleeDistance={fleeDistance}, enemyDetectionRadius={enemyDetectionRadius}",
                    this);
            }
        }

        protected override void Think()
        {
            UpdateNearbyEnemies();

            // Choose behavior
            if (ShouldFlee())
            {
                currentBehavior = fleeBehavior;
            }
            else if (wanderBehavior != null)
            {
                currentBehavior = wanderBehavior;
            }

            if (currentBehavior == null)
            {
                command.Move = Vector2.zero;
                command.Look = Vector2.zero;
                return;
            }

            TaskStatus status = currentBehavior.Execute();

            command.Look = Vector2.zero;

            var mover = currentBehavior as IMovementIntentProvider;
            if (mover != null)
            {
                command.Move = mover.CurrentMoveInput;
            }
            else
            {
                command.Move = Vector2.zero;
            }

            if (DebugEnabled && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                string behaviorName = currentBehavior.GetType().Name;
                Debug.Log(
                    $"[DeerAiBrain] Think on '{gameObject.name}'. pos={unit.transform.position}, " +
                    $"nearbyEnemies={nearbyEnemies.Count}, behavior={behaviorName}, " +
                    $"move=({command.Move.x:0.00},{command.Move.y:0.00}), status={status}",
                    this);
            }

            _ = status;
        }

        private void UpdateNearbyEnemies()
        {
            nearbyEnemies.Clear();

            UnitManager manager = UnitManager.Instance;
            if (manager == null)
            {
                if (DebugEnabled && Time.time >= _nextDebugTime)
                {
                    _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                    Debug.LogWarning("[DeerAiBrain] UnitManager.Instance is null; cannot find enemies.", this);
                }
                return;
            }

            foreach (Unit other in manager.GetNearbyEnemies(unit, enemyDetectionRadius))
            {
                if (other != null && other != unit)
                {
                    nearbyEnemies.Add(other.transform);
                }
            }

            if (DebugEnabled && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log(
                    $"[DeerAiBrain] UpdateNearbyEnemies on '{gameObject.name}'. found={nearbyEnemies.Count}, " +
                    $"radius={enemyDetectionRadius}",
                    this);
            }
        }

        private bool ShouldFlee()
        {
            return nearbyEnemies.Count > 0;
        }
    }
}

