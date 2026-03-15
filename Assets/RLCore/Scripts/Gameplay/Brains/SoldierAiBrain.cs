using UnityEngine;

namespace RLGames
{
    public class SoldierAiBrain : BrainBase
    {
        // Inputs
        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool fireInput;
        private bool aimInput;

        // External behavior or decision-making component
        private IBehavior currentBehavior;

        // Optionally assign a behavior at runtime
        public void SetBehavior(IBehavior behavior)
        {
            currentBehavior = behavior;
        }

        protected override void Think()
        {
            // --- AI Behavior decides inputs ---
            if (currentBehavior != null)
            {
                // Execute current behavior (e.g., patrol, chase, attack)
                TaskStatus status = currentBehavior.Execute();

                // Default: no look input from AI (can be overridden by behavior)
                lookInput = Vector2.zero;

                // If the behavior provides movement intent, use it
                if (currentBehavior is IMovementIntentProvider mover)
                {
                    moveInput = mover.CurrentMoveInput;
                }
                else
                {
                    moveInput = Vector2.zero;
                }

                _ = status; // ignore for now
            }

            // --- Send commands to the Unit ---
            command.Move = moveInput;
            command.Look = lookInput;
            command.Fire = fireInput;

            // --- Update cosmetic weapon mesh if exists ---
            if (unit != null)
            {
                var weaponMeshController = unit.GetComponentInChildren<WeaponMeshController>();
                if (weaponMeshController != null)
                {
                    weaponMeshController.SetADS(aimInput);
                    weaponMeshController.SetSwayInput(lookInput);
                    weaponMeshController.SetMoveInput(moveInput);
                }
            }
        }

        #region Public API for AI to control inputs
        public void SetMoveInput(Vector2 move) => moveInput = move;
        public void SetLookInput(Vector2 look) => lookInput = look;
        public void SetFireInput(bool fire) => fireInput = fire;
        public void SetAimInput(bool aim) => aimInput = aim;
        #endregion
    }
}