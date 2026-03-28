using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RLGames
{
    /// <summary>
    /// Brief hitmarker flash and sound when the local player (<see cref="PlayerBrain"/>) damages an enemy unit.
    /// Listens to <see cref="UnitManager.OnUnitDamaged"/>.
    /// </summary>
    public class HitmarkerFeedback : MonoBehaviour
    {
        [SerializeField] private Graphic[] crossPieces;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hitClip;
        [SerializeField] [Range(0f, 1f)] private float hitVolume = 1f;
        [SerializeField] [Range(0f, 1f)] private float hitRgbAlpha = 1f;
        [SerializeField] private float holdSeconds = 0.08f;
        [SerializeField] private float fadeSeconds = 0.18f;

        private Color[] _baseColors;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            if (crossPieces == null || crossPieces.Length == 0)
            {
                Debug.LogWarning("[HitmarkerFeedback] Assign crosspieces (UI Graphics) for hitmarker visibility.", this);
            }
            else
            {
                _baseColors = new Color[crossPieces.Length];
                for (int i = 0; i < crossPieces.Length; i++)
                {
                    if (crossPieces[i] != null)
                        _baseColors[i] = crossPieces[i].color;
                }
            }

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            SetGraphicsAlpha(0f);
            foreach (var g in crossPieces)
            {
                if (g != null)
                    g.raycastTarget = false;
            }
        }

        private void OnEnable()
        {
            UnitManager.OnUnitDamaged += OnUnitDamaged;
        }

        private void OnDisable()
        {
            UnitManager.OnUnitDamaged -= OnUnitDamaged;
        }

        private void OnUnitDamaged(Damage damage)
        {
            if (!ShouldShow(damage))
                return;

            if (hitClip != null && audioSource != null)
                audioSource.PlayOneShot(hitClip, hitVolume);

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        private static bool ShouldShow(Damage damage)
        {
            if (damage.InstigatorUnit == null || damage.TargetUnit == null)
                return false;
            if (damage.InstigatorUnit.stats == null || damage.TargetUnit.stats == null)
                return false;
            if (damage.InstigatorUnit.GetComponent<PlayerBrain>() == null)
                return false;
            if (damage.InstigatorUnit.stats.teamNumber == damage.TargetUnit.stats.teamNumber)
                return false;
            return true;
        }

        private void SetGraphicsAlpha(float alpha)
        {
            if (crossPieces == null || _baseColors == null)
                return;
            for (int i = 0; i < crossPieces.Length; i++)
            {
                if (crossPieces[i] == null)
                    continue;
                Color c = _baseColors[i];
                c.a = Mathf.Clamp01(hitRgbAlpha) * alpha;
                crossPieces[i].color = c;
            }
        }

        private IEnumerator FlashRoutine()
        {
            SetGraphicsAlpha(1f);
            if (holdSeconds > 0f)
                yield return new WaitForSecondsRealtime(holdSeconds);

            float t = 0f;
            float fade = Mathf.Max(0.0001f, fadeSeconds);
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / fade);
                SetGraphicsAlpha(a);
                yield return null;
            }

            SetGraphicsAlpha(0f);
            _flashRoutine = null;
        }
    }
}
