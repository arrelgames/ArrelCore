using UnityEngine;
using RLGames.AI;

#if false

namespace RLGames.AI
{
    public class HearingSensor
    {
        private readonly Transform transform;
        private readonly AiMemory memory;
        private readonly float hearingRadius;
        private readonly float senseInterval;
        private readonly float suppressionThreshold; // max suppression allowed to detect
        private float nextSenseTime;

        public HearingSensor(Transform transform, AiMemory memory, float radius, float interval = 0.25f, float suppressionThreshold = 0.9f)
        {
            this.transform = transform;
            this.memory = memory;
            this.hearingRadius = radius;
            this.senseInterval = interval;
            this.suppressionThreshold = suppressionThreshold;
        }

        public void Update()
        {
            if (Time.time < nextSenseTime) return;
            nextSenseTime = Time.time + senseInterval;

            memory.ClearSuspiciousStimulus();

            UnitManager manager = UnitManager.Instance;
            if (manager == null) return;

            var nearbyUnits = manager.GetNearbyEnemies(null, hearingRadius);
            foreach (Unit unit in nearbyUnits)
            {
                if (unit == null || unit.transform == transform) continue;

                float suppression = GridVisionHelper.GetSoundSuppression(unit.transform.position, transform.position);
                if (suppression < suppressionThreshold)
                {
                    memory.RememberSuspiciousStimulus(unit.transform.position);
                    return;
                }
            }
        }
    }
}

#endif