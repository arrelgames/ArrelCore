using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class HierarchicalNavigator
    {
        private readonly GridWorld grid;
        private readonly AStarNavigator lowLevel;

        private readonly Dictionary<Vector2Int, GridCluster> clusters = new();
        private readonly int clusterSize = 8;

        public HierarchicalNavigator(GridWorld grid, NavigationSettings settings)
        {
            this.grid = grid;
            lowLevel = new AStarNavigator(grid, settings);
            BuildClusters();
        }

        private Vector2Int GetClusterCoord(GridNode node)
        {
            return new Vector2Int(node.x / clusterSize, node.y / clusterSize);
        }

        private void BuildClusters()
        {
            clusters.Clear();

            foreach (var kv in grid.GetAllStacks())
            {
                Vector2Int pos = kv.Key;
                Vector2Int c = new Vector2Int(pos.x / clusterSize, pos.y / clusterSize);

                if (!clusters.ContainsKey(c))
                    clusters[c] = new GridCluster(c);
            }
        }

        public List<GridNode> FindPath(GridNode start, GridNode goal)
        {
            Vector2Int startCluster = GetClusterCoord(start);
            Vector2Int goalCluster = GetClusterCoord(goal);

            if (startCluster == goalCluster)
                return lowLevel.FindPath(start, goal);

            List<GridNode> result = new();

            List<GridNode> first =
                lowLevel.FindPath(start, GetClusterCenter(startCluster));

            List<GridNode> last =
                lowLevel.FindPath(GetClusterCenter(goalCluster), goal);

            if (first != null)
                result.AddRange(first);

            if (last != null)
                result.AddRange(last);

            return result;
        }

        private GridNode GetClusterCenter(Vector2Int cluster)
        {
            int x = cluster.x * clusterSize + clusterSize / 2;
            int y = cluster.y * clusterSize + clusterSize / 2;
            return grid.GetClosestNode(new Vector3(x, 0, y));
        }
    }
}