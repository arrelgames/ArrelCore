using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class DeerUtilityBrain : UtilityAiBrain
    {
        [SerializeField] private float fleeDistance = 10f;
        [SerializeField] private float detectionRadius = 15f;

        private List<Transform> nearbyEnemies = new List<Transform>();

        protected override void BuildOptions()
        {
            var unit = GetComponent<Unit>();

            var fleeBehavior = new FleeBehavior(unit.transform, nearbyEnemies, fleeDistance, 0f);
            var wanderBehavior = new WanderBehavior(unit, GridWorld.Instance);

            AddOption(new UtilityOption(
                fleeBehavior,
                new FleeScorer(unit.transform, nearbyEnemies, detectionRadius)
            ));

            AddOption(new UtilityOption(
                wanderBehavior,
                new WanderScorer()
            ));
        }

        protected override void Think()
        {
            UpdateNearbyEnemies();
            base.Think();
        }

        private void UpdateNearbyEnemies()
        {
            nearbyEnemies.Clear();

            var manager = UnitManager.Instance;
            if (manager == null) return;

            foreach (var other in manager.GetNearbyEnemies(GetComponent<Unit>(), detectionRadius))
            {
                nearbyEnemies.Add(other.transform);
            }
        }
    }
}