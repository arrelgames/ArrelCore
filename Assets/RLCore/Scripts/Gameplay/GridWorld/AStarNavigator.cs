using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class AStarNavigator
    {
        private GridWorld gridWorld;
        private int jumpHeight;

        public AStarNavigator(GridWorld gridWorld)
        {
            this.gridWorld = gridWorld;
        }

        public void SetJumpHeight(int jumpHeight)
        {
            this.jumpHeight = jumpHeight;
        }

        // Asynchronous method to find a path without blocking the main thread
        public async Task<List<Vector2Int>> FindPathAsync(Vector2Int start, Vector2Int goal)
        {
            return await Task.Run(() => FindPath(start, goal));
        }

        // Synchronous version of pathfinding (called from the background thread)
        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
        {
            List<Vector2Int> openList = new List<Vector2Int>();
            HashSet<Vector2Int> closedList = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
            Dictionary<Vector2Int, float> fScore = new Dictionary<Vector2Int, float>();

            openList.Add(start);
            gScore[start] = 0;
            fScore[start] = Heuristic(start, goal);

            while (openList.Count > 0)
            {
                Vector2Int current = GetNodeWithLowestFScore(openList, fScore);
                if (current == goal)
                {
                    return ReconstructPath(cameFrom, current);
                }

                openList.Remove(current);
                closedList.Add(current);

                foreach (var neighbor in GetNeighbors(current))
                {
                    if (closedList.Contains(neighbor)) continue;

                    float tentativeGScore = gScore[current] + 1; // Assume uniform cost for neighbors
                    if (!openList.Contains(neighbor))
                    {
                        openList.Add(neighbor);
                    }
                    else if (tentativeGScore >= gScore.GetValueOrDefault(neighbor, float.MaxValue))
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, goal);
                }
            }

            return null; // No path found
        }

        // Get the neighboring cells that are navigable
        private List<Vector2Int> GetNeighbors(Vector2Int current)
        {
            List<Vector2Int> neighbors = new List<Vector2Int>();

            // Add adjacent cells (4-directional grid movement)
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;
                if (gridWorld.CanTraverse(current, neighbor, jumpHeight))
                {
                    neighbors.Add(neighbor);
                }
            }

            return neighbors;
        }

        // Manhattan Distance as a heuristic
        private float Heuristic(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // Manhattan Distance
        }

        // Get the node with the lowest F-score from the open list
        private Vector2Int GetNodeWithLowestFScore(List<Vector2Int> openList, Dictionary<Vector2Int, float> fScore)
        {
            Vector2Int lowest = openList[0];
            foreach (var node in openList)
            {
                if (fScore.GetValueOrDefault(node, float.MaxValue) < fScore.GetValueOrDefault(lowest, float.MaxValue))
                {
                    lowest = node;
                }
            }
            return lowest;
        }

        // Reconstruct the path from the cameFrom dictionary
        private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            while (cameFrom.ContainsKey(current))
            {
                path.Insert(0, current);
                current = cameFrom[current];
            }
            return path;
        }
    }
}