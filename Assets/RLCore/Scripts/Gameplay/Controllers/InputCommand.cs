using UnityEngine;

namespace RLGames
{
    public struct InputCommand
    {
        /// <summary>Pre-processed movement input (x = strafe, y = forward/back).</summary>
        public Vector2 Move;
        /// <summary>Pre-processed yaw/pitch rotation deltas. Brains are responsible for all sensitivity, curves, and smoothing before writing here.</summary>
        public Vector2 Look;
        public bool Fire;
        public bool Reload;
        public bool Aim;

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
