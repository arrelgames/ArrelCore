using UnityEngine;
using UnityEngine.Audio;

namespace RLGames
{
    public enum AudioBus
    {
        Master,
        Sfx,
        Music,
        Ui,
    }

    /// <summary>
    /// Drives exposed volume parameters on <see cref="AudioMixer"/>.
    /// Exposed names default to MasterVolume, SfxVolume, MusicVolume, UiVolume.
    /// </summary>
    public class AudioMixerController : MonoBehaviour
    {
        public const float MinDecibels = -80f;

        [SerializeField] private AudioMixer mixer;

        [Header("Exposed parameter names (match Audio Mixer)")]
        [SerializeField] private string masterParam = "MasterVolume";
        [SerializeField] private string sfxParam = "SfxVolume";
        [SerializeField] private string musicParam = "MusicVolume";
        [SerializeField] private string uiParam = "UiVolume";

        public AudioMixer Mixer => mixer;

        public void SetBusVolumeLinear(AudioBus bus, float linear01)
        {
            if (mixer == null)
                return;
            linear01 = Mathf.Clamp01(linear01);
            var db = LinearToDecibels(linear01);
            mixer.SetFloat(ParameterNameFor(bus), db);
        }

        /// <summary>Linear 0–1, or 0 if unset / query fails.</summary>
        public float GetBusVolumeLinear(AudioBus bus)
        {
            if (mixer == null)
                return 0f;
            if (!mixer.GetFloat(ParameterNameFor(bus), out var db))
                return 0f;
            return DecibelsToLinear(db);
        }

        public void SetBusMuted(AudioBus bus, bool muted)
        {
            SetBusVolumeLinear(bus, muted ? 0f : 1f);
        }

        public static float LinearToDecibels(float linear)
        {
            if (linear <= 0.0001f)
                return MinDecibels;
            return 20f * Mathf.Log10(linear);
        }

        public static float DecibelsToLinear(float decibels)
        {
            if (decibels <= MinDecibels)
                return 0f;
            return Mathf.Pow(10f, decibels / 20f);
        }

        string ParameterNameFor(AudioBus bus)
        {
            return bus switch
            {
                AudioBus.Master => masterParam,
                AudioBus.Sfx => sfxParam,
                AudioBus.Music => musicParam,
                AudioBus.Ui => uiParam,
                _ => masterParam
            };
        }
    }
}
