using RLGames;
using UnityEngine;

public class ScoreListener : MonoBehaviour
{
    private void OnEnable()
    {
        UnitManager.OnUnitDamaged += HandleUnitDamaged;
        UnitManager.OnUnitKilled += HandleUnitKilled;
        UnitManager.OnUnitDied += HandleUnitDied;
    }

    private void OnDisable()
    {
        UnitManager.OnUnitDamaged -= HandleUnitDamaged;
        UnitManager.OnUnitKilled -= HandleUnitKilled;
        UnitManager.OnUnitDied -= HandleUnitDied;
    }

    private void HandleUnitDamaged(Damage damage)
    {
        Debug.Log($"{damage.InstigatorUnit.name} damaged {damage.TargetUnit.name} for {damage.DamageAmount} HP");
    }

    private void HandleUnitKilled(Damage damage)
    {
        Debug.Log($"{damage.InstigatorUnit.name} killed {damage.TargetUnit.name}!");
    }

    private void HandleUnitDied(Damage damage)
    {
        Debug.Log($"{damage.TargetUnit.name} has died.");
    }
}