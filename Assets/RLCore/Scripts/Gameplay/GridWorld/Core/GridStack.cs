using System.Collections.Generic;

namespace RLGames
{
    public class GridStack
    {
        private readonly List<GridCell> cells = new();

        public IReadOnlyList<GridCell> Cells => cells;

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