using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridNavigation
    {
        private readonly GridWorld grid;

        private readonly Dictionary<GridNode, List<GridEdge>> edges = new();

        // Movement constraints
        private const float MAX_STEP_UP = 1.0f;
        private const float MAX_DROP = 2.5f;
        private const float AGENT_HEIGHT = 1.8f;

        /// <summary>Height delta above which a climb edge should request jump (shallow ramps stay walk).</summary>
        private const float JUMP_REQUEST_MIN_STEP = 0.45f;

        public GridNavigation(GridWorld grid)
        {
            this.grid = grid;
        }

        public IReadOnlyList<GridEdge> GetEdges(GridNode node)
        {
            if (edges.TryGetValue(node, out var list))
                return list;

            return System.Array.Empty<GridEdge>();
        }

        public void RebuildNodeEdges(Vector2Int pos)
        {
            GridStack stack = grid.GetStack(pos);
            if (stack == null) return;

            // 🔴 CRITICAL FIX: Clear ALL cached nodes for this tile
            for (int i = 0; i < 16; i++) // safe upper bound
            {
                edges.Remove(new GridNode(pos.x, pos.y, i));
            }

            for (int s = 0; s < stack.Cells.Count; s++)
            {
                GridNode node = new GridNode(pos.x, pos.y, s);

                List<GridEdge> nodeEdges = new();

                BuildCardinalEdges(node, nodeEdges);
                BuildDiagonalEdges(node, nodeEdges);

                edges[node] = nodeEdges;
            }
        }

        private void BuildCardinalEdges(GridNode node, List<GridEdge> result)
        {
            Vector2Int pos = new Vector2Int(node.x, node.y);
            GridCell fromCell = grid.GetCell(node);

            if (fromCell == null || !fromCell.IsWalkable)
                return;

            foreach (var dir in GridUtilities.CardinalDirs)
            {
                Vector2Int targetPos = pos + dir;
                GridStack stack = grid.GetStack(targetPos);
                if (stack == null) continue;

                for (int i = 0; i < stack.Cells.Count; i++)
                {
                    GridCell toCell = stack.GetCell(i);
                    if (!IsValidMove(fromCell, toCell))
                        continue;

                    float delta = toCell.surfaceHeight - fromCell.surfaceHeight;

                    bool isClimb = delta > 0.05f;
                    bool isFall = delta < -0.05f;
                    if (isFall && IntermediateSurfaceBlocksFall(stack, fromCell, toCell))
                        continue;

                    bool requestsJump = (isClimb && delta > JUMP_REQUEST_MIN_STEP) ||
                                         (isFall && delta < -JUMP_REQUEST_MIN_STEP);

                    GridNode target = new GridNode(targetPos.x, targetPos.y, i);
                    float cost = GridUtilities.GetCost(pos, targetPos);

                    result.Add(new GridEdge(target, cost, isClimb, isFall, requestsJump));
                }
            }
        }

        private void BuildDiagonalEdges(GridNode node, List<GridEdge> result)
        {
            Vector2Int pos = new Vector2Int(node.x, node.y);
            GridCell fromCell = grid.GetCell(node);

            if (fromCell == null || !fromCell.IsWalkable)
                return;

            foreach (var dir in GridUtilities.DiagonalDirs)
            {
                if (!CanMoveDiagonally(node, dir))
                    continue;

                Vector2Int targetPos = pos + dir;
                GridStack stack = grid.GetStack(targetPos);
                if (stack == null) continue;

                for (int i = 0; i < stack.Cells.Count; i++)
                {
                    GridCell toCell = stack.GetCell(i);
                    if (!IsValidMove(fromCell, toCell))
                        continue;

                    float delta = toCell.surfaceHeight - fromCell.surfaceHeight;

                    bool isClimb = delta > 0.05f;
                    bool isFall = delta < -0.05f;
                    if (isFall && IntermediateSurfaceBlocksFall(stack, fromCell, toCell))
                        continue;

                    bool requestsJump = (isClimb && delta > JUMP_REQUEST_MIN_STEP) ||
                                         (isFall && delta < -JUMP_REQUEST_MIN_STEP);

                    GridNode target = new GridNode(targetPos.x, targetPos.y, i);
                    float cost = GridUtilities.GetCost(pos, targetPos);

                    result.Add(new GridEdge(target, cost, isClimb, isFall, requestsJump));
                }
            }
        }

        private bool IsValidMove(GridCell from, GridCell to)
        {
            if (to == null || !to.IsWalkable)
                return false;

            if (!to.HasClearance(AGENT_HEIGHT))
                return false;

            float delta = to.surfaceHeight - from.surfaceHeight;

            if (delta > MAX_STEP_UP)
                return false;

            if (delta < -MAX_DROP)
                return false;

            return true;
        }

        /// <summary>
        /// Lateral move to a lower surface must not pass through another surface in the target column:
        /// a slab between landing and departure heights, or any slab strictly above the departure
        /// (e.g. floating deck above a slightly lower ramp lip still blocks falling to ground below).
        /// </summary>
        private static bool IntermediateSurfaceBlocksFall(
            GridStack stack, GridCell fromCell, GridCell toCell)
        {
            if (stack == null || fromCell == null || toCell == null)
                return false;

            const float eps = 0.05f;
            bool isFall = fromCell.surfaceHeight > toCell.surfaceHeight + eps;

            for (int k = 0; k < stack.Cells.Count; k++)
            {
                GridCell c = stack.GetCell(k);
                if (c == null || ReferenceEquals(c, toCell))
                    continue;

                if (c.surfaceHeight > toCell.surfaceHeight + eps &&
                    c.surfaceHeight <= fromCell.surfaceHeight + eps)
                    return true;

                if (isFall && c.surfaceHeight > fromCell.surfaceHeight + eps)
                    return true;
            }

            return false;
        }

        private bool CanMoveDiagonally(GridNode from, Vector2Int dir)
        {
            Vector2Int pos = new Vector2Int(from.x, from.y);

            Vector2Int dirA = new Vector2Int(dir.x, 0);
            Vector2Int dirB = new Vector2Int(0, dir.y);

            GridCell fromCell = grid.GetCell(from);

            GridCell cellA = grid.GetStack(pos + dirA)?.GetCell(from.surface);
            GridCell cellB = grid.GetStack(pos + dirB)?.GetCell(from.surface);

            if (fromCell == null || cellA == null || cellB == null)
                return false;

            if (!cellA.IsWalkable || !cellB.IsWalkable)
                return false;

            // 🔴 CRITICAL FIX: height validation for diagonals
            if (!GridUtilities.IsWithinHeight(fromCell, cellA, MAX_STEP_UP, MAX_DROP))
                return false;

            if (!GridUtilities.IsWithinHeight(fromCell, cellB, MAX_STEP_UP, MAX_DROP))
                return false;

            return true;
        }
    }
}