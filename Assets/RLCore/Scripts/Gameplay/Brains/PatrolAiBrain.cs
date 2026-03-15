using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Brain for patrol units that runs a PatrolBehavior and feeds its movement intent
    /// into the normal BrainBase -> Unit -> CharacterMotor pipeline.
    /// </summary>
    [RequireComponent(typeof(Unit))]
    public class PatrolAiBrain : BrainBase
    {
        [Header("Patrol")]
        [SerializeField] private Vector2Int patrolPointA;
        [SerializeField] private Vector2Int patrolPointB;
        [SerializeField] private float waitDurationSeconds = 1f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private float debugLogIntervalSeconds = 1f;

        private GridWorld gridWorld;
        private IBehavior currentBehavior;

        private float nextDebugLogTime;

        protected override void Awake()
        {
            base.Awake();

            if (enableDebugLogs)
            {
                Debug.Log($"[PatrolAiBrain] Awake on '{name}'.", this);
            }
        }

        private void Start()
        {
            gridWorld = GridWorld.Instance;

            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[PatrolAiBrain] Start on '{name}'. " +
                    $"Unit={(unit != null ? "OK" : "MISSING")}, " +
                    $"GridWorld.Instance={(gridWorld != null ? "OK" : "NULL")}, " +
                    $"patrolPointA={patrolPointA}, patrolPointB={patrolPointB}, " +
                    $"waitDurationSeconds={waitDurationSeconds}",
                    this);
            }

            if (unit == null)
            {
                Debug.LogError("[PatrolAiBrain] Unit reference is missing.", this);
                enabled = false;
                return;
            }

            if (gridWorld == null)
            {
                Debug.LogError("[PatrolAiBrain] GridWorld.Instance is NULL. Ensure GridWorld exists in the scene.", this);
                enabled = false;
                return;
            }

            currentBehavior = new PatrolBehavior(
                unit,
                gridWorld,
                patrolPointA,
                patrolPointB,
                waitDurationSeconds
            );

            if (enableDebugLogs)
            {
                Debug.Log("[PatrolAiBrain] PatrolBehavior constructed successfully.", this);
            }
        }

        protected override void Think()
        {
            if (currentBehavior == null)
            {
                command.Move = Vector2.zero;
                command.Look = Vector2.zero;
                return;
            }

            TaskStatus status = currentBehavior.Execute();

            command.Look = Vector2.zero;

            if (currentBehavior is IMovementIntentProvider mover)
            {
                command.Move = mover.CurrentMoveInput;
            }
            else
            {
                command.Move = Vector2.zero;
            }

            if (enableDebugLogs && Time.time >= nextDebugLogTime)
            {
                nextDebugLogTime = Time.time + debugLogIntervalSeconds;

                string behaviorName = currentBehavior.GetType().Name;

                Debug.Log(
                    $"[PatrolAiBrain] Think '{name}' | " +
                    $"pos={unit.transform.position} | " +
                    $"behavior={behaviorName} | " +
                    $"move=({command.Move.x:0.00},{command.Move.y:0.00}) | " +
                    $"status={status}",
                    this);
            }
        }
    }
}