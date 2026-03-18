
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class AStarNavigator
    {
        private readonly GridWorld grid;

        private readonly PriorityQueue<GridNode, float> open = new();
        private readonly HashSet<GridNode> closed = new();

        private readonly Dictionary<GridNode, GridNode> cameFrom = new();
        private readonly Dictionary<GridNode, float> gScore = new();

        private readonly List<GridNode> pathBuffer = new(128);

        public AStarNavigator(GridWorld grid, NavigationSettings settings)
        {
            this.grid = grid;
        }

        public async Task<List<GridNode>> FindPathAsync(GridNode start, GridNode goal)
        {
            return await Task.Run(() => FindPath(start, goal));
        }

        public List<GridNode> FindPath(GridNode start, GridNode goal)
        {
            Reset();

            open.Enqueue(start, 0);
            gScore[start] = 0;

            while (open.Count > 0)
            {
                GridNode current = open.Dequeue();

                if (current.Equals(goal))
                    return Reconstruct(current);

                closed.Add(current);

                var edges = grid.GetEdges(current);

                foreach (var edge in edges)
                {
                    GridNode neighbor = edge.target;

                    if (closed.Contains(neighbor))
                        continue;

                    float tentative = gScore[current] + edge.cost;

                    if (!gScore.TryGetValue(neighbor, out float old) || tentative < old)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative;

                        float f = tentative + Heuristic(neighbor, goal);
                        open.Enqueue(neighbor, f);
                    }
                }
            }

            return null;
        }

        private void Reset()
        {
            open.Clear();
            closed.Clear();
            cameFrom.Clear();
            gScore.Clear();
            pathBuffer.Clear();
        }

        private float Heuristic(GridNode a, GridNode b)
        {
            float dx = Mathf.Abs(a.x - b.x);
            float dy = Mathf.Abs(a.y - b.y);

            float diagonal = Mathf.Min(dx, dy);
            float straight = dx + dy - 2 * diagonal;

            float horizontal = diagonal * 1.4142f + straight;

            GridCell aCell = grid.GetCell(a);
            GridCell bCell = grid.GetCell(b);

            float vertical = 0f;

            if (aCell != null && bCell != null)
                vertical = Mathf.Abs(aCell.surfaceHeight - bCell.surfaceHeight);

            return horizontal + vertical;
        }

        private List<GridNode> Reconstruct(GridNode current)
        {
            pathBuffer.Add(current);

            while (cameFrom.TryGetValue(current, out current))
                pathBuffer.Add(current);

            pathBuffer.Reverse();
            return new List<GridNode>(pathBuffer);
        }
    }
}