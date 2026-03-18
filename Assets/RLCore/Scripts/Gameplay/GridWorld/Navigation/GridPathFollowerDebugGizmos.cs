using System.Collections.Generic;
using UnityEngine;

#if false

namespace RLGames
{
    [ExecuteAlways]
    public class GridPathFollowerDebugGizmos : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridWorld grid;
        [SerializeField] private Unit unit;

        [Header("Display")]
        [SerializeField] private bool drawPath = true;
        [SerializeField] private bool drawTarget = true;
        [SerializeField] private bool drawDirection = true;

        [SerializeField] private float nodeSize = 0.12f;
        [SerializeField] private float lineOffset = 0.1f;

        [Header("Colors")]
        [SerializeField] private Color pathColor = Color.yellow;
        [SerializeField] private Color currentColor = Color.cyan;
        [SerializeField] private Color targetColor = Color.magenta;

        [SerializeField] private Color climbColor = Color.blue;
        [SerializeField] private Color fallColor = Color.red;

        private void OnDrawGizmos()
        {
            if (grid == null)
                grid = GridWorld.Instance;

            if (unit == null || grid == null)
                return;

            GridPathFollower follower = unit.GetPathFollower();
            if (follower == null)
                return;

            List<GridNode> path = follower.Debug_GetPath();
            int index = follower.Debug_GetCurrentIndex();

            if (path == null || path.Count == 0)
                return;

            DrawPath(path, index, follower);
            DrawCurrentTarget(path, index);
            DrawDirection();
        }

        private void DrawPath(List<GridNode> path, int currentIndex, GridPathFollower follower)
        {
            if (!drawPath) return;

            for (int i = 0; i < path.Count; i++)
            {
                GridNode node = path[i];
                GridCell cell = grid.GetCell(node);
                if (cell == null) continue;

                Vector3 world = grid.GridToWorldXZ(new Vector2Int(node.x, node.y));
                world.y = cell.surfaceHeight;

                // Current node highlight
                Gizmos.color = (i == currentIndex) ? currentColor : pathColor;
                Gizmos.DrawSphere(world, nodeSize);

                // Draw connection to next
                if (i < path.Count - 1)
                {
                    GridNode next = path[i + 1];
                    GridCell nextCell = grid.GetCell(next);
                    if (nextCell == null) continue;

                    Vector3 nextWorld = grid.GridToWorldXZ(
                        new Vector2Int(next.x, next.y));
                    nextWorld.y = nextCell.surfaceHeight;

                    Vector3 a = world + Vector3.up * lineOffset;
                    Vector3 b = nextWorld + Vector3.up * lineOffset;

                    // 🔥 Color based on edge type
                    var edges = grid.GetEdges(node);
                    foreach (var edge in edges)
                    {
                        if (edge.target.Equals(next))
                        {
                            if (edge.isClimb)
                                Gizmos.color = climbColor;
                            else if (edge.isFall)
                                Gizmos.color = fallColor;
                            else
                                Gizmos.color = pathColor;

                            break;
                        }
                    }

                    Gizmos.DrawLine(a, b);
                }
            }
        }

        private void DrawCurrentTarget(List<GridNode> path, int index)
        {
            if (!drawTarget) return;

            if (index >= path.Count)
                return;

            GridNode node = path[index];
            GridCell cell = grid.GetCell(node);
            if (cell == null) return;

            Vector3 world = grid.GridToWorldXZ(new Vector2Int(node.x, node.y));
            world.y = cell.surfaceHeight + 0.2f;

            Gizmos.color = targetColor;
            Gizmos.DrawWireSphere(world, nodeSize * 2f);
        }

        private void DrawDirection()
        {
            if (!drawDirection) return;

            Vector3 pos = unit.transform.position;
            Vector2 input = unit.GetPathFollower().CurrentMoveInput;

            Vector3 worldDir = unit.transform.TransformDirection(
                new Vector3(input.x, 0f, input.y));

            Gizmos.color = Color.green;
            Gizmos.DrawRay(pos, worldDir);
        }
    }
}

#endif