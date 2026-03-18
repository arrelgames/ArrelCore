using UnityEngine;

namespace RLGames
{
    [System.Serializable]
    public class NavigationSettings
    {
        [Header("Movement Rules")]
        public float maxStepUp = 1.0f;
        public float maxDropDown = 2.5f;

        [Header("Agent")]
        public float agentHeight = 1.8f;

        // 🔥 NEW
        [Header("Jumping")]
        public int maxJumpDistance = 2;
        public float maxJumpHeight = 1.5f;
        public float maxDropHeight = 3.0f;
    }
}