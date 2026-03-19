using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public class UtilityAiBrain : BrainBase
    {
        private readonly List<UtilityOption> options = new List<UtilityOption>();

        private UtilityOption currentOption;

        [SerializeField] private float switchThreshold = 0.1f; // hysteresis
        [SerializeField] private float stickinessBonus = 0.1f;

        protected override void Awake()
        {
            base.Awake();

            BuildOptions();
        }

        protected virtual void BuildOptions()
        {
            // You will override this in a setup component OR initialize here
        }

        protected override void Think()
        {
            if (options.Count == 0)
                return;

            UtilityOption best = null;
            float bestScore = -1f;

            foreach (var option in options)
            {
                float score = option.Evaluate();

                // Add stickiness
                if (option == currentOption)
                {
                    score += stickinessBonus;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = option;
                }
            }

            // Hysteresis: avoid rapid switching
            if (currentOption != null && best != currentOption)
            {
                float currentScore = currentOption.LastScore;

                if (bestScore < currentScore + switchThreshold)
                {
                    best = currentOption;
                }
            }

            currentOption = best;

            // Execute
            TaskStatus status = currentOption.Behavior.Execute();

            command.Look = Vector2.zero;

            if (currentOption.Behavior is IMovementIntentProvider mover)
            {
                command.Move = mover.CurrentMoveInput;
                command.Jump = mover.JumpRequested;
            }
            else
            {
                command.Move = Vector2.zero;
                command.Jump = false;
            }

            _ = status;
        }

        protected void AddOption(UtilityOption option)
        {
            options.Add(option);
        }
    }
}