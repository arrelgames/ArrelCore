using UnityEngine;
using System.Collections.Generic;

namespace RLGames
{
    public static class GridUtilities
    {
        public static readonly Vector2Int[] CardinalDirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        public static readonly Vector2Int[] DiagonalDirs =
        {
            new Vector2Int(-1, 1), // NW
            new Vector2Int(1, 1),  // NE
            new Vector2Int(-1, -1),// SW
            new Vector2Int(1, -1)  // SE
        };

        public static IEnumerable<Vector2Int> AllDirs()
        {
            foreach (var d in CardinalDirs) yield return d;
            foreach (var d in DiagonalDirs) yield return d;
        }

        public static float GetCost(Vector2Int from, Vector2Int to)
        {
            return (from.x != to.x && from.y != to.y) ? 1.4142f : 1f; // diagonal sqrt(2)
        }

        public static bool IsWithinHeight(GridCell from, GridCell to, float maxStepUp, float maxDropDown)
        {
            if (from == null || to == null) return false;

            float delta = to.surfaceHeight - from.surfaceHeight;
            if (delta > maxStepUp) return false;
            if (delta < -maxDropDown) return false;

            return true;
        }
    }
}