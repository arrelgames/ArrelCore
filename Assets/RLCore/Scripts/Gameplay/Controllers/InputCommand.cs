using UnityEngine;

namespace RLGames
{
    public struct InputCommand
    {
        public Vector2 Move;
        public Vector2 Look;

        public void Clear()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
        }
    }
}