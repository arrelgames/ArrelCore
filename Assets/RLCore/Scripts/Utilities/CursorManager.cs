using UnityEngine;

namespace RLGames
{
    public class CursorManager : MonoBehaviour
    {
        [Header("Cursor Settings")]
        [SerializeField] private bool hideCursor = true;
        [SerializeField] private CursorLockMode lockMode = CursorLockMode.Locked;

        private void Start()
        {
            if (hideCursor)
            {
                Cursor.visible = false;
                Cursor.lockState = lockMode;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hideCursor && hasFocus)
            {
                Cursor.visible = false;
                Cursor.lockState = lockMode;
            }
        }

        private void OnApplicationQuit()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }
}