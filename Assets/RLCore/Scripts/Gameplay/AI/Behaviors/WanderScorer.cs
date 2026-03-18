using UnityEngine;

namespace RLGames
{
    public class WanderScorer : IUtilityScorer
    {
        public float Score()
        {
            return 0.2f + Random.Range(0f, 0.1f);
        }
    }
}