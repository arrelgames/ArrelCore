using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridStack
    {
        private readonly List<GridCell> cells = new();

        /// <summary>Refcounts matching <see cref="GridUtilities.CardinalDirs"/> order: up, down, left, right.</summary>
        private readonly int[] outgoingPassageBlockCount = new int[4];

        public IReadOnlyList<GridCell> Cells => cells;

        private static int CardinalDirIndex(Vector2Int dir)
        {
            if (dir == Vector2Int.up) return 0;
            if (dir == Vector2Int.down) return 1;
            if (dir == Vector2Int.left) return 2;
            if (dir == Vector2Int.right) return 3;
            return -1;
        }

        public void AddOutgoingPassageBlock(Vector2Int dir, int delta)
        {
            int i = CardinalDirIndex(dir);
            if (i < 0) return;

            outgoingPassageBlockCount[i] = Mathf.Max(0, outgoingPassageBlockCount[i] + delta);
        }

        public bool BlocksOutgoingPassage(Vector2Int dir)
        {
            int i = CardinalDirIndex(dir);
            return i >= 0 && outgoingPassageBlockCount[i] > 0;
        }

        public int AddSurface(float surfaceHeight)
        {
            const float EPS = 0.01f;

            GridCell cell = new GridCell(surfaceHeight);

            int index = cells.FindIndex(c => c.surfaceHeight > surfaceHeight + EPS);

            if (index < 0)
            {
                cells.Add(cell);
                return cells.Count - 1;
            }

            cells.Insert(index, cell);
            return index;
        }

        public GridCell GetCell(int index)
        {
            if (index < 0 || index >= cells.Count)
                return null;

            return cells[index];
        }

        public int GetClosestSurface(float worldHeight)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < cells.Count; i++)
            {
                float dist = UnityEngine.Mathf.Abs(cells[i].surfaceHeight - worldHeight);

                if (dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }

            return best;
        }

        /// <summary>Returns surface index if one exists within eps of the height, else -1.</summary>
        public int FindSurfaceIndexNear(float surfaceHeight, float eps = 0.01f)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (UnityEngine.Mathf.Abs(cells[i].surfaceHeight - surfaceHeight) <= eps)
                    return i;
            }

            return -1;
        }
    }
}