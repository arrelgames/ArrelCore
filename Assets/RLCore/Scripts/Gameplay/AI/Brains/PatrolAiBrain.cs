using UnityEngine;

namespace RLGames
{
    public class PatrolAiBrain : UtilityAiBrain
    {
        [SerializeField] private Vector2Int pointA = new Vector2Int(0, 0);
        [SerializeField] private Vector2Int pointB = new Vector2Int(8, 0);
        [SerializeField] private float waitDurationSeconds = 1f;

        [Header("Movement")]
        [Tooltip("Scales patrol move input sent to the motor: 1 = full path-follow input, 0 = stand still.")]
        [SerializeField] [Range(0f, 1f)] private float movementInputScale = 1f;

        protected override void BuildOptions()
        {
            var unit = GetComponent<Unit>();
            if (unit == null)
                return;

            var grid = GridWorld.Instance;
            if (grid == null)
                return;

            var behavior = new PatrolBehavior(
                unit,
                grid,
                pointA,
                pointB,
                waitDurationSeconds,
                movementInputScale);

            AddOption(new UtilityOption(
                behavior,
                new AlwaysOnScorer()));
        }

        private sealed class AlwaysOnScorer : IUtilityScorer
        {
            public float Score()
            {
                return 1f;
            }
        }
    }
}

