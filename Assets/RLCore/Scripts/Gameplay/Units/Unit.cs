using UnityEngine;

namespace RLGames
{
    public class Unit : MonoBehaviour
    {
        [SerializeField] private CharacterMotor motor;

        private InputCommand command;

        private void Awake()
        {
            if (motor == null)
                motor = GetComponent<CharacterMotor>();
        }

        public void SetCommand(InputCommand newCommand)
        {
            command = newCommand;
        }

        private void Update()
        {
            motor.Execute(command);
        }
    }
}