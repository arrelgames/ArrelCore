using UnityEngine;

namespace RLGames
{
    public class Unit : MonoBehaviour
    {
        [SerializeField] private CharacterMotor motor;
        [SerializeField] private Weapon weapon;
        [SerializeField] public UnitStats stats;

        public InputCommand command;

        private void Awake()
        {
            if (motor == null)
                motor = GetComponent<CharacterMotor>();
            if (weapon == null)
                weapon = GetComponentInChildren<Weapon>();
            if (stats == null)
                stats = GetComponent<UnitStats>();
        }

        private void Start()
        {
            UnitManager.Instance.RegisterUnit(this);
        }

        private void OnDestroy()
        {
            UnitManager.Instance.DeregisterUnit(this);
        }

        public void SetCommand(InputCommand newCommand)
        {
            command = newCommand;
        }

        private void Update()
        {
            // Only execute if alive
            if (stats != null && !stats.IsAlive)
                return;

            motor.Execute(command);

            if (weapon != null && command.Fire)
            {
                weapon.Fire();
            }
        }
    }
}