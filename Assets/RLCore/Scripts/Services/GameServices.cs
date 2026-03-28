using UnityEngine;

namespace RLGames
{
    public class GameServices : MonoBehaviour
    {
        // Singleton instance
        public static GameServices Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        #region Add your singletons or services here

        [Header("Game Services")]
        public CursorManager cursorManager;
        public AudioMixerController audioMixerController;
        // public SaveManager saveManager;

        // Add more service references as needed

        #endregion

        private void Start()
        {
            // Optional: auto-initialize services if null
            if (cursorManager == null) cursorManager = GetComponent<CursorManager>();
            if (audioMixerController == null) audioMixerController = GetComponent<AudioMixerController>();
            // if (saveManager == null) saveManager = GetComponent<SaveManager>();
        }
    }
}