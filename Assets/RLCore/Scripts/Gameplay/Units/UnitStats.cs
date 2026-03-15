using UnityEngine;

namespace RLGames
{
    [DisallowMultipleComponent]
    public class UnitStats : MonoBehaviour
    {
        [Header("Team Info")]
        public int teamNumber = 0;

        [Header("Health")]
        public float hpMax = 100f;
        public float hpCurrent;

        public bool IsAlive => hpCurrent > 0f;

        private void Awake()
        {
            hpCurrent = hpMax; // Initialize full health
        }

        /// <summary>
        /// Apply damage using a Damage object.
        /// Returns true if the unit died from this damage.
        /// Automatically triggers UnitManager events.
        /// </summary>
        public bool TakeDamage(Damage damage)
        {
            if (!IsAlive)
                return false;

            // Reduce health
            hpCurrent -= damage.DamageAmount;
            hpCurrent = Mathf.Max(hpCurrent, 0f);

            // Trigger damage event
            UnitManager.Instance.TriggerUnitDamaged(damage);

            if (!IsAlive)
            {
                Debug.Log($"{gameObject.name} has died!");

                // Trigger death/kill events
                UnitManager.Instance.TriggerUnitKilled(damage);
                UnitManager.Instance.TriggerUnitDied(damage);

                return true;
            }
            else
            {
                Debug.Log($"{gameObject.name} took {damage.DamageAmount} damage, HP left: {hpCurrent}");
                return false;
            }
        }
    }
}