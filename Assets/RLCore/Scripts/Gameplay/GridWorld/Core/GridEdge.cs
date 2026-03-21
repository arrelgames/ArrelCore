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

        /// <summary>
        /// When false, path followers should treat the edge as a walkable slope (no jump input).
        /// </summary>
        public bool requestsJump;

        public GridEdge(GridNode target, float cost, bool isClimb = false, bool isFall = false, bool requestsJump = true)
        {
            this.target = target;
            this.cost = cost;
            this.isClimb = isClimb;
            this.isFall = isFall;
            this.requestsJump = requestsJump;
        }

        public override string ToString()
        {
            string type = isClimb ? "Climb" : isFall ? "Fall" : "Walk";
            return $"{type} -> {target} (Cost: {cost:F2})";
        }
    }
}