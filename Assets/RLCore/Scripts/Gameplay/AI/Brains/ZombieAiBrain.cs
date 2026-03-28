using UnityEngine;

namespace RLGames
{
    public class ZombieAiBrain : UtilityAiBrain
    {
        [Header("Chase")]
        [Tooltip("Detection range in GridWorld XZ tiles (multiplied by GridWorld.CellSizeXZ for world units). E.g. 10 tiles × 1.25 cell size = 12.5 world units.")]
        [SerializeField] private float chaseDetectionRadius = 10f;
        [Tooltip("How often to re-run A* to pick the cheapest-path enemy (seconds).")]
        [SerializeField] private float enemyPickInterval = 0.35f;

        protected override void BuildOptions()
        {
            var unit = GetComponent<Unit>();
            if (unit == null)
                return;

            var grid = GridWorld.Instance;
            if (grid == null)
                return;

            float worldDetectionRadius = chaseDetectionRadius * grid.CellSizeXZ;

            AddOption(new UtilityOption(
                new ChaseEnemyBehavior(unit, grid, worldDetectionRadius, enemyPickInterval),
                new ChaseEnemyScorer(unit, worldDetectionRadius)));

            AddOption(new UtilityOption(
                new WanderBehavior(unit, grid),
                new FixedHalfScorer()));
        }

        private sealed class FixedHalfScorer : IUtilityScorer
        {
            public float Score() => 0.5f;
        }
    }
}
