using UnityEngine;

namespace RLGames
{
    public class UtilityOption
    {
        public IBehavior Behavior { get; }
        public IUtilityScorer Scorer { get; }

        public float LastScore { get; private set; }

        public UtilityOption(IBehavior behavior, IUtilityScorer scorer)
        {
            Behavior = behavior;
            Scorer = scorer;
        }

        public float Evaluate()
        {
            LastScore = Mathf.Clamp01(Scorer.Score());
            return LastScore;
        }
    }
}