using UnityEngine;

#if false

namespace RLGames.AI
{
    public class AiMemory
    {
        private Stimulus? lastStimulus;

        private Unit visibleEnemy;
        private Vector3 lastKnownEnemyPosition;

        public void RememberStimulus(Stimulus stimulus)
        {
            lastStimulus = stimulus;
        }

        public bool HasSuspiciousStimulus()
        {
            return lastStimulus.HasValue;
        }

        public Vector3 GetSuspiciousPosition()
        {
            return lastStimulus.Value.Position;
        }

        public void ClearStimulus()
        {
            lastStimulus = null;
        }

        public void RememberEnemy(Unit enemy)
        {
            visibleEnemy = enemy;
            lastKnownEnemyPosition = enemy.transform.position;
        }

        public bool HasVisibleEnemy()
        {
            return visibleEnemy != null;
        }

        public Unit GetVisibleEnemy()
        {
            return visibleEnemy;
        }

        public Vector3 GetLastKnownEnemyPosition()
        {
            return lastKnownEnemyPosition;
        }

        public void ClearEnemy()
        {
            visibleEnemy = null;
        }
    }
}

#endif