using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class FleeFromUnits : ITask
    {
        private Transform unitTransform;
        private List<Transform> nearbyUnits;
        private float fleeDistance;
        private float moveSpeed;

        public FleeFromUnits(Transform unitTransform, List<Transform> nearbyUnits, float fleeDistance, float moveSpeed)
        {
            this.unitTransform = unitTransform;
            this.nearbyUnits = nearbyUnits;
            this.fleeDistance = fleeDistance;
            this.moveSpeed = moveSpeed;
        }

        public TaskStatus Execute()
        {
            foreach (var nearbyUnit in nearbyUnits)
            {
                float distance = Vector3.Distance(unitTransform.position, nearbyUnit.position);
                if (distance < fleeDistance)
                {
                    // Flee from the unit
                    Vector3 direction = (unitTransform.position - nearbyUnit.position).normalized;
                    unitTransform.position += direction * moveSpeed * Time.deltaTime;
                    return TaskStatus.Running; // Fleeing from the unit
                }
            }

            return TaskStatus.Success; // No nearby units to flee from
        }
    }
}