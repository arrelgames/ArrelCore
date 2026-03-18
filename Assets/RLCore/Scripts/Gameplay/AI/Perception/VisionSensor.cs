using UnityEngine;
using RLGames.AI;

#if false

namespace RLGames.AI
{
    public class VisionSensor
    {
        private readonly Transform transform;
        private readonly AiMemory memory;
        private readonly float viewDistance;
        private readonly float viewAngle;
        private readonly float senseInterval;
        private float nextSenseTime;

        public VisionSensor(Transform transform, AiMemory memory, float distance, float angle, float interval = 0.25f)
        {
            this.transform = transform;
            this.memory = memory;
            this.viewDistance = distance;
            this.viewAngle = angle;
            this.senseInterval = interval;
        }

        public void Update()
        {
            if (Time.time < nextSenseTime) return;
            nextSenseTime = Time.time + senseInterval;

            memory.ClearEnemy();

            UnitManager manager = UnitManager.Instance;
            if (manager == null) return;

            var nearbyUnits = manager.GetNearbyEnemies(null, viewDistance);
            foreach (Unit unit in nearbyUnits)
            {
                if (unit == null || unit.transform == transform) continue;

                if (CanSee(unit))
                {
                    memory.RememberEnemy(unit);
                    return; // track only first visible enemy
                }
            }
        }

        public bool CanSee(Unit target)
        {
            Vector3 dir = target.transform.position - transform.position;
            float dist = dir.magnitude;

            if (dist > viewDistance) return false;

            dir.Normalize();
            float angle = Vector3.Angle(transform.forward, dir);
            if (angle > viewAngle * 0.5f) return false;

            // Grid-based vision suppression
            float suppression = GridVisionHelper.GetVisionSuppression(transform.position, target.transform.position);
            if (suppression >= 1f) return false;

            return true;
        }
    }
}

#endif