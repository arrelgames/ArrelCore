using UnityEngine;

namespace RLGames
{
    public class DestroyAfterDelay : MonoBehaviour
    {
        [SerializeField] private float delaySeconds = 2f;

        private void Start()
        {
            Destroy(gameObject, delaySeconds);
        }
    }
}
