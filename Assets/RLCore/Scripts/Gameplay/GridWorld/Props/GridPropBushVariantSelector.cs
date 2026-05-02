using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RLGames
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GridPropBushVariantSelector : MonoBehaviour
    {
        private const float DefaultCellSizeXZ = 1.25f;

        [Header("Target")]
        [SerializeField] private Transform displayRoot;

        [Header("Variants")]
        [SerializeField] private GameObject[] variantPrefabs = new GameObject[0];
        [SerializeField] private int variantSeed = 1337;

        [Header("Deterministic Scale")]
        [SerializeField] private bool applyDeterministicScale;
        [SerializeField] private Vector2 uniformScaleRange = Vector2.one;

        private Vector3 lastAppliedPosition;
        private int lastAppliedVariant = -1;
        private float lastAppliedScale = 1f;
        private bool hasApplied;
        private bool suppressTransformPolling;

        private void Awake()
        {
            ApplyVariantForCurrentPosition(force: true);
        }

        private void OnEnable()
        {
            ApplyVariantForCurrentPosition(force: true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            suppressTransformPolling = true;
            ApplyVariantForCurrentPosition(force: true);
            suppressTransformPolling = false;
        }

        private void Update()
        {
            if (!enabled || suppressTransformPolling)
                return;

            if (!Application.isPlaying && transform.position != lastAppliedPosition)
                ApplyVariantForCurrentPosition(force: false);
        }
#endif

        private void ApplyVariantForCurrentPosition(bool force)
        {
            if (!TryGetDisplayRoot(out Transform root))
                return;

            if (variantPrefabs == null || variantPrefabs.Length == 0)
                return;

            int gx;
            int gz;
            GetSnappedGridCoords(transform.position, out gx, out gz);

            int tileHash = HashGrid(gx, gz, variantSeed);
            int variantIndex = PositiveMod(tileHash, variantPrefabs.Length);
            GameObject selectedPrefab = variantPrefabs[variantIndex];
            if (selectedPrefab == null)
                return;

            float deterministicScale = 1f;
            if (applyDeterministicScale)
            {
                float t = HashToUnitFloat(tileHash ^ 0x2E89A2D1);
                float min = Mathf.Min(uniformScaleRange.x, uniformScaleRange.y);
                float max = Mathf.Max(uniformScaleRange.x, uniformScaleRange.y);
                deterministicScale = Mathf.Lerp(min, max, t);
            }

            if (!force &&
                hasApplied &&
                lastAppliedVariant == variantIndex &&
                Mathf.Approximately(lastAppliedScale, deterministicScale))
            {
                lastAppliedPosition = transform.position;
                return;
            }

            RebuildDisplayedChild(root, selectedPrefab, deterministicScale);

            lastAppliedPosition = transform.position;
            lastAppliedVariant = variantIndex;
            lastAppliedScale = deterministicScale;
            hasApplied = true;
        }

        private void RebuildDisplayedChild(Transform root, GameObject prefab, float deterministicScale)
        {
            ClearChildren(root);

            GameObject instance;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            else
                instance = Instantiate(prefab, root);
#else
            instance = Instantiate(prefab, root);
#endif
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * deterministicScale;
        }

        private void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(child.gameObject);
                else
                    Destroy(child.gameObject);
#else
                Destroy(child.gameObject);
#endif
            }
        }

        private bool TryGetDisplayRoot(out Transform root)
        {
            root = displayRoot;
            if (root != null)
                return true;

            Transform candidate = transform.Find("PivotPoint/DisplayPrefab");
            if (candidate == null)
                candidate = transform.Find("DisplayPrefab");
            if (candidate == null)
                candidate = transform;

            displayRoot = candidate;
            root = candidate;
            return root != null;
        }

        private static void GetSnappedGridCoords(Vector3 worldPos, out int gx, out int gz)
        {
            float cellSize = GridWorld.Instance != null
                ? Mathf.Max(1e-6f, GridWorld.Instance.CellSizeXZ)
                : DefaultCellSizeXZ;

            float snappedX = Mathf.Round(worldPos.x / cellSize) * cellSize;
            float snappedZ = Mathf.Round(worldPos.z / cellSize) * cellSize;

            gx = Mathf.RoundToInt(snappedX / cellSize);
            gz = Mathf.RoundToInt(snappedZ / cellSize);
        }

        private static int HashGrid(int gx, int gz, int seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)gx * 374761393u;
                h = (h << 13) | (h >> 19);
                h *= 1274126177u;
                h ^= (uint)gz * 668265263u;
                h = (h << 11) | (h >> 21);
                h *= 2246822519u;
                return (int)h;
            }
        }

        private static int PositiveMod(int value, int modulus)
        {
            int m = value % modulus;
            return m < 0 ? m + modulus : m;
        }

        private static float HashToUnitFloat(int value)
        {
            uint bits = (uint)value;
            return bits / (float)uint.MaxValue;
        }
    }
}
