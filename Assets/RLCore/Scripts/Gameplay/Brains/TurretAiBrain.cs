using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// AI Brain for stationary turrets. Handles target acquisition, aiming, and firing.
    /// </summary>
    public class TurretAiBrain : BrainBase
    {
        [Header("Turret Settings")]
        [SerializeField] private float detectionRadius = 20f;
        [SerializeField] private float yawSpeed = 120f;
        [SerializeField] private float pitchSpeed = 60f;
        [SerializeField] private Transform pitchTransform; // Assign the barrel or pitch part in inspector

        private TurretBehavior turretBehavior;
        private Weapon turretWeapon;

        protected override void Awake()
        {
            base.Awake();

            if (unit == null)
            {
                Debug.LogWarning("[TurretAiBrain] Unit component is missing; disabling brain.", this);
                enabled = false;
                return;
            }

            // Get the Weapon component
            turretWeapon = unit.GetComponentInChildren<Weapon>();
            if (turretWeapon == null)
            {
                Debug.LogError("[TurretAiBrain] No Weapon component found on turret unit!", unit);
                enabled = false;
                return;
            }

            if (pitchTransform == null)
            {
                Debug.LogWarning("[TurretAiBrain] No pitchTransform assigned; vertical aiming disabled.", unit);
            }

            // Initialize behavior
            turretBehavior = new TurretBehavior(unit, turretWeapon, detectionRadius, pitchTransform, yawSpeed, pitchSpeed);
        }

        protected override void Think()
        {
            if (unit == null || turretBehavior == null)
                return;

            // Execute turret AI
            turretBehavior.Execute();

            // Optional: update cosmetic systems like weapon mesh sway
            var weaponMeshController = unit.GetComponentInChildren<WeaponMeshController>();
            if (weaponMeshController != null)
            {
                weaponMeshController.SetADS(false);
                weaponMeshController.SetSwayInput(unit.command.Look);
                weaponMeshController.SetMoveInput(Vector2.zero);
            }
        }
    }
}