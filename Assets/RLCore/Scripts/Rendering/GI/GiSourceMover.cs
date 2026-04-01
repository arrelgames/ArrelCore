using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Moves a GameObject between two positions over time.
    /// Useful for testing dynamic GI emitters.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiSourceMover : MonoBehaviour
    {
        [SerializeField] private bool useLocalSpace = false;
        [SerializeField] private bool pingPong = true;
        [SerializeField] private bool autoCaptureStartOnPlay = true;

        [Min(0.01f)]
        [SerializeField] private float duration = 3f;

        [SerializeField] private Vector3 startPosition;
        [SerializeField] private Vector3 endPosition = new Vector3(2f, 0f, 0f);

        private float elapsed;
        private float direction = 1f;

        private void Awake()
        {
            if (autoCaptureStartOnPlay)
                startPosition = GetCurrentPosition();
        }

        private void Update()
        {
            float safeDuration = Mathf.Max(0.01f, duration);
            elapsed += Time.deltaTime;

            if (pingPong)
            {
                float t = Mathf.PingPong(elapsed / safeDuration, 1f);
                SetCurrentPosition(Vector3.Lerp(startPosition, endPosition, t));
                return;
            }

            float tForward = elapsed / safeDuration;
            if (tForward >= 1f)
            {
                tForward = 1f;
                if (direction > 0f)
                {
                    direction = -1f;
                    elapsed = 0f;
                }
                else
                {
                    direction = 1f;
                    elapsed = 0f;
                }
            }

            float travelT = direction > 0f ? tForward : 1f - tForward;
            SetCurrentPosition(Vector3.Lerp(startPosition, endPosition, travelT));
        }

        private Vector3 GetCurrentPosition()
        {
            return useLocalSpace ? transform.localPosition : transform.position;
        }

        private void SetCurrentPosition(Vector3 position)
        {
            if (useLocalSpace)
                transform.localPosition = position;
            else
                transform.position = position;
        }
    }
}
