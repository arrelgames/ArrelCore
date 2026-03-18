using UnityEngine;

#if false

namespace RLGames.AI
{
    public static class StimulusRegistry
    {
        public static void EmitSound(Vector3 position, float intensity, GameObject source)
        {
            GridWorld grid = GridWorld.Instance;
            if (grid == null)
                return;

            Vector2Int cellPos = grid.WorldToGrid(position);
            GridCell cell = grid.GetCell(cellPos);

            if (cell == null)
                return;

            cell.stimuli.Add(
                new Stimulus(
                    StimulusType.Sound,
                    position,
                    source,
                    intensity
                )
            );
        }
    }
}

#endif

/*

Example Usage:
StimulusRegistry.EmitSound(transform.position, 1f, gameObject);
StimulusRegistry.EmitSound(footPosition, 0.3f, gameObject); 
*/