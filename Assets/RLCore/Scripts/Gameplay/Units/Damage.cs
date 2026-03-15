using UnityEngine;

namespace RLGames
{
    public class Damage
    {
        public Unit InstigatorUnit;          // Who caused the damage
        public Unit TargetUnit;              // Who was damaged
        public float DamageAmount;           // How much damage
        public GameObject DamageCauserGameObject; // Weapon, projectile, trap, etc.
        public Vector3 HitPoint;             // Optional, where the hit occurred
        public bool IsCritical;              // Optional, was it a critical hit?

        public Damage(Unit instigator, Unit target, float damageAmount, GameObject damageCauser = null, Vector3 hitPoint = default, bool isCritical = false)
        {
            InstigatorUnit = instigator;
            TargetUnit = target;
            DamageAmount = damageAmount;
            DamageCauserGameObject = damageCauser;
            HitPoint = hitPoint;
            IsCritical = isCritical;
        }
    }
}