using UnityEngine;

namespace RLGames
{
    public struct InputCommand
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Fire; // Add a simple fire button
        public bool Reload; // Optional, if you want reload
        public bool Aim; // Optional, for aiming down sights

        public void Clear()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            Fire = false;
            Reload = false;
            Aim = false;
        }
    }
}