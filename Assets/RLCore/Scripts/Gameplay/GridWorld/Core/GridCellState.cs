using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class GridCellState
    {
        public bool blocksMovement = false;

        public float soundSuppression = 0f;
        public float visionSuppression = 0f;

        private readonly List<GridProp> props = new();
        public IReadOnlyList<GridProp> Props => props;

        public void AddProp(GridProp prop)
        {
            if (prop == null) return;

            props.Add(prop);
            Recalculate();
        }

        public void RemoveProp(GridProp prop)
        {
            if (prop == null) return;

            if (props.Remove(prop))
                Recalculate();
        }

        private void Recalculate()
        {
            blocksMovement = false;
            soundSuppression = 0f;
            visionSuppression = 0f;

            foreach (var p in props)
            {
                if (p.Solid || p is GridPropRamp { Filled: true })
                    blocksMovement = true;

                soundSuppression = Mathf.Max(soundSuppression, p.SoundSuppression);
                visionSuppression = Mathf.Max(visionSuppression, p.VisionSuppression);
            }
        }
    }
}