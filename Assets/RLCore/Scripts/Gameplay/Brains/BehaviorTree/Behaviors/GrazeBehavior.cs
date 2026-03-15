using UnityEngine;

namespace RLGames
{
    public class GrazeBehavior : IBehavior
    {
        private Transform unitTransform;
        private float grazeTime;
        private float grazeDuration;

        public GrazeBehavior(Transform unitTransform, float grazeDuration)
        {
            this.unitTransform = unitTransform;
            this.grazeDuration = grazeDuration;
        }

        public TaskStatus Execute()
        {
            grazeTime += Time.deltaTime;
            if (grazeTime < grazeDuration)
            {
                // Perform grazing behavior (e.g., stay in place and graze)
                return TaskStatus.Running;
            }

            return TaskStatus.Success; // Finished grazing
        }
    }
}