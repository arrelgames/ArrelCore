namespace RLGames
{
    /// <summary>
    /// Behaviors that own a GridPathFollower can implement this for debug visualization.
    /// </summary>
    public interface IDebugPathFollowerProvider
    {
        GridPathFollower DebugPathFollower { get; }
    }
}
