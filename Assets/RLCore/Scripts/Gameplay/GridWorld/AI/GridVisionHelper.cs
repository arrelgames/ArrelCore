using UnityEngine;

namespace RLGames.AI
{
    public static class GridVisionHelper
    {
        public static float GetVisionSuppression(Vector3 from, Vector3 to, float eyeHeight = 1.6f)
        {
            GridWorld grid = GridWorld.Instance;
            if (grid == null) return 0f;

            Vector2Int start = grid.WorldToGridXZ(from);
            Vector2Int end = grid.WorldToGridXZ(to);

            int dx = Mathf.Abs(end.x - start.x);
            int dy = Mathf.Abs(end.y - start.y);
            int sx = start.x < end.x ? 1 : -1;
            int sy = start.y < end.y ? 1 : -1;
            int err = dx - dy;

            float accumulated = 0f;

            int x = start.x;
            int y = start.y;

            while (true)
            {
                GridStack stack = grid.GetStack(new Vector2Int(x, y));

                if (stack != null)
                {
                    int surface = stack.GetClosestSurface(eyeHeight);

                    GridCell cell = stack.GetCell(surface);

                    if (cell != null)
                    {
                        accumulated = Mathf.Max(accumulated, cell.state.visionSuppression);

                        if (accumulated >= 1f)
                            return 1f;
                    }
                }

                if (x == end.x && y == end.y)
                    break;

                int e2 = 2 * err;

                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }

            return Mathf.Clamp01(accumulated);
        }

        public static float GetSoundSuppression(Vector3 from, Vector3 to)
        {
            GridWorld grid = GridWorld.Instance;
            if (grid == null) return 0f;

            Vector2Int start = grid.WorldToGridXZ(from);
            Vector2Int end = grid.WorldToGridXZ(to);

            int dx = Mathf.Abs(end.x - start.x);
            int dy = Mathf.Abs(end.y - start.y);
            int sx = start.x < end.x ? 1 : -1;
            int sy = start.y < end.y ? 1 : -1;
            int err = dx - dy;

            float accumulated = 0f;

            int x = start.x;
            int y = start.y;

            while (true)
            {
                GridStack stack = grid.GetStack(new Vector2Int(x, y));

                if (stack != null)
                {
                    int surface = stack.GetClosestSurface(from.y);

                    GridCell cell = stack.GetCell(surface);

                    if (cell != null)
                    {
                        accumulated = Mathf.Max(accumulated, cell.state.soundSuppression);

                        if (accumulated >= 1f)
                            return 1f;
                    }
                }

                if (x == end.x && y == end.y)
                    break;

                int e2 = 2 * err;

                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }

            return Mathf.Clamp01(accumulated);
        }
    }
}