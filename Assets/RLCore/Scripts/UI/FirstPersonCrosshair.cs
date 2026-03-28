using System.Collections;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Center-dot crosshair with four arms. Call <see cref="PlayFireKick"/> from <see cref="Weapon"/> on each shot.
    /// Assign arm <see cref="RectTransform"/>s; rest poses are captured in <see cref="Awake"/>.
    /// </summary>
    public class FirstPersonCrosshair : MonoBehaviour
    {
        [SerializeField] private RectTransform armTop;
        [SerializeField] private RectTransform armBottom;
        [SerializeField] private RectTransform armLeft;
        [SerializeField] private RectTransform armRight;

        [Header("Kick")]
        [SerializeField] private float kickExpandPixels = 10f;
        [SerializeField] private float kickDuration = 0.12f;
        [SerializeField] private AnimationCurve kickShape;

        private static readonly Vector2[] s_outward =
        {
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        };

        private RectTransform[] _arms;
        private Vector2[] _rest;
        private Coroutine _kickRoutine;

        private void Awake()
        {
            _arms = new[] { armTop, armBottom, armLeft, armRight };
            _rest = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                if (_arms[i] != null)
                    _rest[i] = _arms[i].anchoredPosition;
            }

            if (kickShape == null || kickShape.length < 2)
                kickShape = DefaultKickCurve();
        }

#if UNITY_EDITOR
        private void Reset()
        {
            kickShape = DefaultKickCurve();
        }
#endif

        public void PlayFireKick()
        {
            if (!isActiveAndEnabled)
                return;

            if (_kickRoutine != null)
                StopCoroutine(_kickRoutine);
            _kickRoutine = StartCoroutine(KickRoutine());
        }

        private IEnumerator KickRoutine()
        {
            float dur = Mathf.Max(0.0001f, kickDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(t / dur);
                float blend = kickShape.Evaluate(n);
                ApplyBlend(blend);
                yield return null;
            }

            ApplyBlend(0f);
            _kickRoutine = null;
        }

        private void ApplyBlend(float blend)
        {
            float d = kickExpandPixels * blend;
            for (int i = 0; i < 4; i++)
            {
                if (_arms[i] == null)
                    continue;
                _arms[i].anchoredPosition = _rest[i] + s_outward[i] * d;
            }
        }

        private static AnimationCurve DefaultKickCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 6f),
                new Keyframe(0.18f, 1f, 0f, 0f),
                new Keyframe(1f, 0f, -2f, 0f));
        }
    }
}
