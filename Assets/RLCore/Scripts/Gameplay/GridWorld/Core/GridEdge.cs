using System;

namespace RLGames
{
    [Serializable]
    public struct GridEdge
    {
        public GridNode target;
        public float cost;

        public bool isClimb;
        public bool isFall;

        public GridEdge(GridNode target, float cost, bool isClimb = false, bool isFall = false)
        {
            this.target = target;
            this.cost = cost;
            this.isClimb = isClimb;
            this.isFall = isFall;
        }

        public override string ToString()
        {
            string type = isClimb ? "Climb" : isFall ? "Fall" : "Walk";
            return $"{type} -> {target} (Cost: {cost:F2})";
        }
    }
}