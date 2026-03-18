using System;

namespace RLGames
{
    [Serializable]
    public struct GridNode : IEquatable<GridNode>
    {
        public int x;
        public int y;
        public int surface;

        public GridNode(int x, int y, int surface)
        {
            this.x = x;
            this.y = y;
            this.surface = surface;
        }

        public bool Equals(GridNode other)
        {
            return x == other.x && y == other.y && surface == other.surface;
        }

        public override bool Equals(object obj)
        {
            return obj is GridNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y, surface);
        }

        public override string ToString()
        {
            return $"({x},{y},s{surface})";
        }
    }
}