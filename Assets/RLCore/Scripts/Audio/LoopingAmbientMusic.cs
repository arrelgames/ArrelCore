using UnityEngine;
using UnityEngine.Audio;

namespace RLGames
{
    /// <summary>
    /// 2D looping music driven by an <see cref="AudioSource"/> on the same GameObject (optionally routed to the Music mixer group).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class LoopingAmbientMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip clip;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private AudioMixerGroup musicGroup;

        private AudioSource _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            if (musicGroup != null)
                _source.outputAudioMixerGroup = musicGroup;
            if (clip != null)
                _source.clip = clip;
        }

        private void OnEnable()
        {
            if (!playOnEnable || _source == null || _source.clip == null)
                return;
            if (!_source.isPlaying)
                _source.Play();
        }

        private void OnDisable()
        {
            if (_source != null)
                _source.Stop();
        }
    }
}
