using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Provides local-space movement intent for AI-controlled units.
    /// x = right, y = forward (consumed by CharacterMotor via InputCommand.Move).
    /// </summary>
    public interface IMovementIntentProvider
    {
        Vector2 CurrentMoveInput { get; }
    }
}

