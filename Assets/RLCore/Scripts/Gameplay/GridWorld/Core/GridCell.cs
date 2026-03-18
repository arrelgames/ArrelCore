using UnityEngine;

namespace RLGames
{
    public class GridCell
    {
        // ✅ STATIC (spatial)
        public float surfaceHeight;
        public float ceilingHeight;

        // ✅ DYNAMIC (gameplay)
        public GridCellState state = new();

        // ✅ COVER (still here for now)
        public CoverType coverNorth = CoverType.None;
        public CoverType coverSouth = CoverType.None;
        public CoverType coverEast = CoverType.None;
        public CoverType coverWest = CoverType.None;

        public GridCell(float surfaceHeight)
        {
            this.surfaceHeight = surfaceHeight;
            ceilingHeight = surfaceHeight + GridWorld.Instance.CellSizeY * 4f;
        }

        public bool IsWalkable => !state.blocksMovement;

        public void AddProp(GridProp prop)
        {
            state.AddProp(prop);
            prop.RegisterCell(this);
        }

        public void RemoveProp(GridProp prop)
        {
            state.RemoveProp(prop);
        }

        public bool HasClearance(float requiredHeight)
        {
            return (ceilingHeight - surfaceHeight) >= requiredHeight;
        }

        public void SetCover(Vector2Int dir, CoverType cover)
        {
            if (dir == Vector2Int.up)
                coverNorth = cover;
            else if (dir == Vector2Int.down)
                coverSouth = cover;
            else if (dir == Vector2Int.right)
                coverEast = cover;
            else if (dir == Vector2Int.left)
                coverWest = cover;
        }

        public CoverType GetCover(Vector2Int dir)
        {
            if (dir == Vector2Int.up) return coverNorth;
            if (dir == Vector2Int.down) return coverSouth;
            if (dir == Vector2Int.right) return coverEast;
            if (dir == Vector2Int.left) return coverWest;

            return CoverType.None;
        }
    }
}