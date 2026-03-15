using UnityEngine;
using UnityEngine.InputSystem;

namespace RLGames
{
    public class PlayerBrain : BrainBase
    {
        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool fireInput;
        private bool aimInput;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction fireAction;
        private InputAction aimAction;

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                Debug.LogWarning("[PlayerBrain] No PlayerInput found on GameObject; input will not be processed.", this);
                return;
            }

            var actions = playerInput.actions;
            if (actions == null)
            {
                Debug.LogWarning("[PlayerBrain] PlayerInput has no actions asset; input will not be processed.", this);
                return;
            }

            moveAction = actions["Move"];
            lookAction = actions["Look"];
            fireAction = actions["Attack"];
            aimAction = actions["Aim"];

            if (fireAction != null)
            {
                fireAction.started += OnFireStarted;
                fireAction.canceled += OnFireCanceled;
            }

            if (aimAction != null)
            {
                aimAction.started += OnAimStarted;
                aimAction.canceled += OnAimCanceled;
            }
        }

        private void OnDisable()
        {
            if (fireAction != null)
            {
                fireAction.started -= OnFireStarted;
                fireAction.canceled -= OnFireCanceled;
            }

            if (aimAction != null)
            {
                aimAction.started -= OnAimStarted;
                aimAction.canceled -= OnAimCanceled;
            }
        }

        private void OnFireStarted(InputAction.CallbackContext ctx) => fireInput = true;
        private void OnFireCanceled(InputAction.CallbackContext ctx) => fireInput = false;
        private void OnAimStarted(InputAction.CallbackContext ctx) => aimInput = true;
        private void OnAimCanceled(InputAction.CallbackContext ctx) => aimInput = false;

        protected override void Think()
        {
            if (moveAction != null)
                moveInput = moveAction.ReadValue<Vector2>();
            else
                moveInput = Vector2.zero;

            if (lookAction != null)
                lookInput = lookAction.ReadValue<Vector2>();
            else
                lookInput = Vector2.zero;

            command.Move = moveInput;
            command.Look = lookInput;
            command.Fire = fireInput;

            // Update WeaponMeshController
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
    }
}