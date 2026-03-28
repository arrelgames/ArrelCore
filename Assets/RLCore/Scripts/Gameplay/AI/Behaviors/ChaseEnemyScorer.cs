using UnityEngine;

namespace RLGames
{
    public class ChaseEnemyScorer : IUtilityScorer
    {
        private readonly Unit self;
        private readonly float detectionRadius;

        public ChaseEnemyScorer(Unit self, float detectionRadius)
        {
            this.self = self;
            this.detectionRadius = detectionRadius;
        }

        public float Score()
        {
            if (self == null || self.stats == null)
                return 0f;

            var manager = UnitManager.Instance;
            if (manager == null)
                return 0f;

            var enemies = manager.GetNearbyEnemies(self, detectionRadius);
            return enemies != null && enemies.Count > 0 ? 1f : 0f;
        }
    }
}
