using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    [CreateAssetMenu(fileName = "BulletImpactEffectLibrary", menuName = "RL Games/Bullet Impact Effect Library")]
    public class BulletImpactEffectLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public PhysicsMaterial material;
            public GameObject prefab;
        }

        [SerializeField] private List<Entry> mappings = new List<Entry>();
        [SerializeField] private GameObject defaultPrefab;

        public GameObject ResolvePrefab(PhysicsMaterial hitMaterial)
        {
            if (hitMaterial != null)
            {
                for (int i = 0; i < mappings.Count; i++)
                {
                    if (mappings[i].material == hitMaterial)
                        return mappings[i].prefab;
                }
            }

            return defaultPrefab;
        }
    }
}
