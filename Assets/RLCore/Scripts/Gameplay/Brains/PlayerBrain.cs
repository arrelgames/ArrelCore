using UnityEngine;
using UnityEngine.InputSystem;

namespace RLGames
{
    public class PlayerBrain : BrainBase
    {
        private Vector2 moveInput;
        private Vector2 lookInput;

        protected override void Think()
        {
            command.Move = moveInput;
            command.Look = lookInput;
        }

        public void OnMove(InputValue value)
        {
            moveInput = value.Get<Vector2>();
        }

        public void OnLook(InputValue value)
        {
            lookInput = value.Get<Vector2>();
            Debug.Log(value.Get<Vector2>());
        }
    }
}