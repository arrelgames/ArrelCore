using UnityEngine;

namespace RLGames
{
    public class AiBrain : BrainBase
    {
        private IBehavior currentBehavior;

        // Assign the active behavior externally (e.g., from PatrolAiBehavior, DeerAiBehavior, ZombieAiBehavior)
        public void SetBehavior(IBehavior newBehavior)
        {
            currentBehavior = newBehavior;
        }

        protected override void Think()
        {
            if (currentBehavior == null)
            {
                return;
            }

            // Execute the current high-level behavior.
            TaskStatus status = currentBehavior.Execute();

            // By default, no look input from AI.
            command.Look = Vector2.zero;

            // If the behavior provides movement intent, use it to drive the Unit/CharacterMotor.
            var mover = currentBehavior as IMovementIntentProvider;
            if (mover != null)
            {
                command.Move = mover.CurrentMoveInput;
            }
            else
            {
                command.Move = Vector2.zero;
            }

            // Optional: you could react to Success/Failure here later if you add sequences/trees.
            _ = status;
        }
    }
}