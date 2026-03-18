using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class FleeScorer : IUtilityScorer
    {
        private Transform self;
        private List<Transform> enemies;
        private float detectionRadius;

        public FleeScorer(Transform self, List<Transform> enemies, float detectionRadius)
        {
            this.self = self;
            this.enemies = enemies;
            this.detectionRadius = detectionRadius;
        }

        public float Score()
        {
            float closest = float.MaxValue;

            foreach (var e in enemies)
            {
                if (e == null) continue;

                float d = Vector3.Distance(self.position, e.position);
                if (d < closest)
                    closest = d;
            }

            if (closest > detectionRadius)
                return 0f;

            float normalized = 1f - (closest / detectionRadius);

            // Exponential panic curve
            return normalized * normalized;
        }
    }
}