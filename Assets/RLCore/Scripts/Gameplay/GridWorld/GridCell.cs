using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridCell
    {
        public List<GridProp> props; // List of GridProps registered in this cell
        public bool isBlocked;       // Is this cell blocked? (used for pathfinding)

        public GridCell()
        {
            props = new List<GridProp>();
            isBlocked = false;
        }

        public void AddProp(GridProp prop)
        {
            props.Add(prop);
        }
    }
}