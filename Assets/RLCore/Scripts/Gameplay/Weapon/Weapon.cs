using UnityEngine;

namespace RLGames
{

    public class Weapon : MonoBehaviour
    {
        [Header("Weapon Settings")]
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireRate = 0.2f;
        [SerializeField] private float maxRange = 100f;
        [SerializeField] private float damageAmount = 25f;
        [SerializeField] WeaponMeshController weaponMeshController;

        [Header("Audio")]
        [SerializeField] private AudioClip fireSound;
        [SerializeField] private AudioSource fireAudioSource;
        [SerializeField] [Range(0f, 1f)] private float fireSoundVolumeScale = 1f;

        private float lastFireTime;

        private void Awake()
        {
            if (fireAudioSource == null)
                fireAudioSource = GetComponent<AudioSource>();
        }

        public bool CanFire()
        {
            if (firePoint == null)
                return false;

            return Time.time >= lastFireTime + fireRate;
        }

        public void Fire()
        {
            if (Time.time < lastFireTime + fireRate)
                return;

            lastFireTime = Time.time;

            if (fireSound != null && fireAudioSource != null)
                fireAudioSource.PlayOneShot(fireSound, fireSoundVolumeScale);

            // Apply cosmetic recoil
            weaponMeshController?.ApplyRecoil();

            // Emit a Sound Stimulus so Ai can hear the weapon fire
#if false
            StimulusRegistry.EmitStimulus(
                new Stimulus(
                    StimulusType.Sound,
                    transform.position,
                    gameObject,
                    1f
                )
            );
#endif



            // Fire the weapon and check for hits
            Ray ray = new Ray(firePoint.position, firePoint.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, maxRange))
            {
                UnitStats targetStats = hit.collider.GetComponentInParent<UnitStats>();
                if (targetStats != null && targetStats.IsAlive)
                {
                    Unit attacker = GetComponentInParent<Unit>();
                    Unit targetUnit = targetStats.GetComponent<Unit>();

                    if (attacker != null && targetUnit != null)
                    {
                        // Create Damage object
                        Damage damage = new Damage(
                            instigator: attacker,
                            target: targetUnit,
                            damageAmount: damageAmount,
                            damageCauser: gameObject,
                            hitPoint: hit.point
                        );

                        // Apply damage, UnitStats now handles events
                        targetStats.TakeDamage(damage);
                    }

                    Debug.Log($"Weapon hit {hit.collider.name} at {hit.point}");
                }
            }
            else
            {
                Debug.Log("Weapon hit nothing.");
            }
        }
    }
}