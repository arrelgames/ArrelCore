namespace RLGames
{
    /// <summary>
    /// Preset for <see cref="GiManagerDirectional"/> CPU cost vs. quality tradeoffs.
    /// </summary>
    public enum GiDirectionalQualityPreset
    {
        /// <summary>Vertical sun fast path, optional small blur, luma bounce only.</summary>
        Performance = 0,
        /// <summary>Moderate blur radius, luma bounce.</summary>
        Balanced = 1,
        /// <summary>Larger blur; RGB bounce when bounce is enabled.</summary>
        High = 2
    }
}
