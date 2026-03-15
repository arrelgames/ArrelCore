using System.Linq;
using UnityEngine;

namespace RLGames
{


    /// <summary>
    /// Turret behavior with smooth yaw (horizontal) and pitch (vertical) aiming at enemies or positions.
    /// Fires the assigned Weapon when aligned with target.
    /// </summary>
    public class TurretBehavior : IBehavior
    {
        private readonly Unit turretUnit;
        private readonly Weapon turretWeapon;
        private readonly float detectionRadius;
        private readonly float yawSpeed;   // Horizontal rotation speed (deg/sec)
        private readonly float pitchSpeed; // Vertical rotation speed (deg/sec)
        private readonly Transform pitchTransform; // Transform that handles vertical pitch (e.g., turret barrel)

        private Unit currentTarget;

        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1f;
        private float _nextDebugTime;

        public TurretBehavior(
            Unit turretUnit,
            Weapon turretWeapon,
            float detectionRadius,
            Transform pitchTransform,
            float yawSpeed = 120f,
            float pitchSpeed = 60f)
        {
            if (turretWeapon == null)
            {
                Debug.LogError("[TurretBehavior] Turret requires a Weapon component!");
            }

            this.turretUnit = turretUnit;
            this.turretWeapon = turretWeapon;
            this.detectionRadius = detectionRadius;
            this.yawSpeed = yawSpeed;
            this.pitchSpeed = pitchSpeed;
            this.pitchTransform = pitchTransform;
        }

        public TaskStatus Execute()
        {
            if (turretUnit == null || turretWeapon == null)
                return TaskStatus.Failure;

            var manager = UnitManager.Instance;
            if (manager == null)
                return TaskStatus.Failure;

            // --- Find nearby enemies ---
            var nearbyEnemies = manager.GetNearbyEnemies(turretUnit, detectionRadius);
            if (nearbyEnemies.Count == 0)
            {
                currentTarget = null;
                turretUnit.SetCommand(new InputCommand()); // clear inputs
                return TaskStatus.Running;
            }

            // Select closest target
            if (currentTarget == null || !nearbyEnemies.Contains(currentTarget))
            {
                currentTarget = nearbyEnemies
                    .OrderBy(u => Vector3.Distance(u.transform.position, turretUnit.transform.position))
                    .First();
            }

            if (currentTarget == null)
                return TaskStatus.Running;

            // --- Compute yaw and pitch separately ---
            Vector3 targetDir = currentTarget.transform.position - turretUnit.transform.position;

            // Yaw (horizontal)
            Vector3 flatDir = new Vector3(targetDir.x, 0f, targetDir.z);
            if (flatDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetYaw = Quaternion.LookRotation(flatDir, Vector3.up);
                turretUnit.transform.rotation = Quaternion.RotateTowards(
                    turretUnit.transform.rotation,
                    targetYaw,
                    yawSpeed * Time.deltaTime
                );
            }

            // Pitch (vertical)
            if (pitchTransform != null)
            {
                Vector3 localDir = turretUnit.transform.InverseTransformDirection(targetDir.normalized);
                float targetPitch = Mathf.Atan2(localDir.y, localDir.z) * Mathf.Rad2Deg;
                float currentPitch = pitchTransform.localEulerAngles.x;
                if (currentPitch > 180f) currentPitch -= 360f;

                float newPitch = Mathf.MoveTowards(currentPitch, targetPitch, pitchSpeed * Time.deltaTime);
                pitchTransform.localEulerAngles = new Vector3(newPitch, 0f, 0f);
            }

            // --- Fire if aligned ---
            float angleToTarget = Vector3.Angle(turretUnit.transform.forward, flatDir.normalized);
            bool facingTarget = angleToTarget < 5f;
            bool shouldFire = facingTarget && turretWeapon.CanFire();

            if (shouldFire)
            {
                turretWeapon.Fire();
            }

            // Update InputCommand for cosmetic systems
            InputCommand cmd = new InputCommand
            {
                Look = Vector2.zero,
                Fire = shouldFire
            };
            turretUnit.SetCommand(cmd);

            if (DebugEnabled && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log(
                    $"[TurretBehavior] '{turretUnit.name}' targeting '{currentTarget.name}' " +
                    $"distance={targetDir.magnitude:0.00} angle={angleToTarget:0.0} fire={shouldFire}",
                    turretUnit
                );
            }

            return TaskStatus.Running;
        }

        /// <summary>
        /// Aim at an arbitrary world position using smooth yaw/pitch.
        /// </summary>
        public void AimAtPosition(Vector3 worldPosition)
        {
            if (turretUnit == null) return;

            Vector3 targetDir = worldPosition - turretUnit.transform.position;

            // Yaw
            Vector3 flatDir = new Vector3(targetDir.x, 0f, targetDir.z);
            if (flatDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetYaw = Quaternion.LookRotation(flatDir, Vector3.up);
                turretUnit.transform.rotation = Quaternion.RotateTowards(
                    turretUnit.transform.rotation,
                    targetYaw,
                    yawSpeed * Time.deltaTime
                );
            }

            // Pitch
            if (pitchTransform != null)
            {
                Vector3 localDir = turretUnit.transform.InverseTransformDirection(targetDir.normalized);
                float targetPitch = Mathf.Atan2(localDir.y, localDir.z) * Mathf.Rad2Deg;
                float currentPitch = pitchTransform.localEulerAngles.x;
                if (currentPitch > 180f) currentPitch -= 360f;

                float newPitch = Mathf.MoveTowards(currentPitch, targetPitch, pitchSpeed * Time.deltaTime);
                pitchTransform.localEulerAngles = new Vector3(newPitch, 0f, 0f);
            }
        }
    }


}