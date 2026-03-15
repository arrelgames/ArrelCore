using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{

    using System;
    using System.Collections.Generic;
    using UnityEngine;


    public class UnitManager : MonoBehaviour
    {
        public static UnitManager Instance { get; private set; }
        private List<Unit> allUnits = new List<Unit>();

        // --- Events using Damage class ---
        public static event Action<Damage> OnUnitDamaged;
        public static event Action<Damage> OnUnitKilled;
        public static event Action<Damage> OnUnitDied;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

        public void RegisterUnit(Unit unit)
        {
            if (!allUnits.Contains(unit))
                allUnits.Add(unit);
        }

        public void DeregisterUnit(Unit unit)
        {
            if (allUnits.Contains(unit))
                allUnits.Remove(unit);
        }

        // Get all nearby enemies that are a different team number
        public List<Unit> GetNearbyEnemies(Unit unit, float radius)
        {
            List<Unit> nearbyEnemies = new List<Unit>();

            foreach (Unit otherUnit in allUnits)
            {
                // Skip the unit itself and units from the same team
                if (unit == otherUnit || unit.stats.teamNumber == otherUnit.stats.teamNumber)
                {
                    continue;
                }

                float distance = Vector3.Distance(unit.transform.position, otherUnit.transform.position);

                if (distance <= radius)
                {
                    nearbyEnemies.Add(otherUnit);
                }
            }

            return nearbyEnemies;
        }

        // Trigger events
        public void TriggerUnitDamaged(Damage damage)
        {
            OnUnitDamaged?.Invoke(damage);
        }

        public void TriggerUnitKilled(Damage damage)
        {
            OnUnitKilled?.Invoke(damage);
        }

        public void TriggerUnitDied(Damage damage)
        {
            OnUnitDied?.Invoke(damage);
        }
    }
}