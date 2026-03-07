using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(Unit))]
    public abstract class BrainBase : MonoBehaviour
    {
        protected Unit unit;
        protected InputCommand command;

        protected virtual void Awake()
        {
            unit = GetComponent<Unit>();
        }

        protected virtual void Update()
        {
            command.Clear();

            Think();

            unit.SetCommand(command);
        }

        protected abstract void Think();
    }
}