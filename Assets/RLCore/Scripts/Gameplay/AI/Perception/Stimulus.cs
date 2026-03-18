using UnityEngine;

#if false

namespace RLGames.AI
{
    public enum StimulusType
    {
        Sound
    }

    public struct Stimulus
    {
        public StimulusType Type;
        public Vector3 Position;
        public GameObject Source;
        public float Intensity;
        public float Time;

        public Stimulus(StimulusType type, Vector3 position, GameObject source, float intensity)
        {
            Type = type;
            Position = position;
            Source = source;
            Intensity = intensity;
            Time = Time.time;
        }
    }
}

#endif

/*

Examples:

StimulusRegistry.EmitStimulus(
    new Stimulus(
        StimulusType.Sound,
        transform.position,
        gameObject,
        1f
    )
);

StimulusRegistry.EmitStimulus(
    new Stimulus(
        StimulusType.Sound,
        footPosition,
        gameObject,
        0.3f
    )
);


*/