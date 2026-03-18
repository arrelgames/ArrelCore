using UnityEngine;


/*

🧪 How to Use
1. Add to scene

Create empty GameObject: GridDebug

Attach GridDebugGizmos

2. Assign GridWorld

Drag your GridWorld instance into the field
(or leave null—it auto-finds Instance)

👀 What You Should See
Nodes

⚪ White = walkable

⚫ Black = blocked

Edges

🟢 Green = walk

🔵 Blue = climb

🔴 Red = fall

🔥 What to Look For (VERY IMPORTANT)
✅ Good signs:

Clean grid layout

Edges only between valid tiles

No weird vertical jumps

❌ Bad signs (bugs):

Edges going through walls → diagonal bug

Edges skipping floors → height bug

Missing edges → clearance/step bug

Lines pointing to wrong height → surface index bug
*/

namespace RLGames
{
    [ExecuteAlways]
    public class GridDebugGizmos : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridWorld grid;

        [Header("Display")]
        [SerializeField] private bool drawNodes = true;
        [SerializeField] private bool drawEdges = true;

        [SerializeField] private float nodeSize = 0.1f;
        [SerializeField] private float edgeOffset = 0.05f;

        [Header("Colors")]
        [SerializeField] private Color walkColor = Color.green;
        [SerializeField] private Color climbColor = Color.blue;
        [SerializeField] private Color fallColor = Color.red;
        [SerializeField] private Color blockedColor = Color.black;

        private void OnDrawGizmos()
        {
            if (grid == null)
                grid = GridWorld.Instance;

            if (grid == null)
                return;

            foreach (var kv in grid.GetAllStacks())
            {
                Vector2Int pos = kv.Key;
                GridStack stack = kv.Value;

                for (int s = 0; s < stack.Cells.Count; s++)
                {
                    GridNode node = new GridNode(pos.x, pos.y, s);
                    GridCell cell = stack.GetCell(s);

                    if (cell == null)
                        continue;

                    Vector3 world = grid.GridToWorldXZ(pos);
                    world.y = cell.surfaceHeight;

                    // 🔹 Draw Node
                    if (drawNodes)
                    {
                        Gizmos.color = cell.IsWalkable ? Color.white : blockedColor;
                        Gizmos.DrawSphere(world, nodeSize);
                    }

                    // 🔸 Draw Edges
                    if (drawEdges)
                    {
                        var edges = grid.GetEdges(node);

                        foreach (var edge in edges)
                        {
                            GridCell targetCell = grid.GetCell(edge.target);
                            if (targetCell == null)
                                continue;

                            Vector3 targetWorld = grid.GridToWorldXZ(
                                new Vector2Int(edge.target.x, edge.target.y));
                            targetWorld.y = targetCell.surfaceHeight;

                            // Slight vertical offset so lines don't z-fight
                            Vector3 a = world + Vector3.up * edgeOffset;
                            Vector3 b = targetWorld + Vector3.up * edgeOffset;

                            // 🎨 Color by movement type
                            if (edge.isClimb)
                                Gizmos.color = climbColor;
                            else if (edge.isFall)
                                Gizmos.color = fallColor;
                            else
                                Gizmos.color = walkColor;

                            Gizmos.DrawLine(a, b);
                        }
                    }
                }
            }
        }
    }
}