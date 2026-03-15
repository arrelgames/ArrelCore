using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridCell
    {
        public List<GridProp> props;
        public bool isBlocked;
        public int blockedHeight;

        public GridCell()
        {
            props = new List<GridProp>();
            isBlocked = false;
            blockedHeight = 0;
        }

        public void AddProp(GridProp prop)
        {
            props.Add(prop);

            if (prop.isBlocked && prop.Height > blockedHeight)
                blockedHeight = prop.Height;
        }
    }
}