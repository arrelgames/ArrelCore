using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridCluster
    {
        public Vector2Int coord;

        public List<GridNode> portals = new();

        public GridCluster(Vector2Int coord)
        {
            this.coord = coord;
        }
    }
}