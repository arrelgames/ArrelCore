using UnityEngine;

namespace RLGames
{
    public class MoveTowardsPlayer : ITask
    {
        private Transform unitTransform;
        private Transform playerTransform;
        private float moveSpeed;

        public MoveTowardsPlayer(Transform unitTransform, Transform playerTransform, float moveSpeed)
        {
            this.unitTransform = unitTransform;
            this.playerTransform = playerTransform;
            this.moveSpeed = moveSpeed;
        }

        public TaskStatus Execute()
        {
            Vector3 direction = (playerTransform.position - unitTransform.position).normalized;
            unitTransform.position += direction * moveSpeed * Time.deltaTime;

            return TaskStatus.Running; // Continuously move towards the player
        }
    }
}