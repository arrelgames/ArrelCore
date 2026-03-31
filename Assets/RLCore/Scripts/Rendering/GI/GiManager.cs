using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Drives grid-based GI propagation and exposes sampling/texture data to URP.
    /// Visual-only: reads from GridWorld, does not affect navigation or gameplay.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GiManager : MonoBehaviour
    {
        public static GiManager Instance { get; private set; }

        [Header("Propagation")]
        [Range(0f, 1f)]
        [SerializeField] private float diffusionStrength = 0.6f;

        [Range(0f, 1f)]
        [SerializeField] private float damping = 0.9f;

        [SerializeField] private float propagationTickInterval = 0.1f;

        [Header("Occlusion")]
        [Min(1f)]
        [SerializeField] private float occlusionFloorHeightCells = 4f;

        [Range(0f, 1f)]
        [SerializeField] private float occlusionCutoff = 0.05f;

        [Header("Texture Output")]
        [SerializeField] private bool buildTexture = true;

        [SerializeField] private string globalTextureName = "_GiVolume";

        [Tooltip("Downsample factor relative to the GridWorld XZ grid.\n1 = one texel per tile, 2 = one texel per 2x2 tiles.")]
        [Min(1)]
        [SerializeField] private int xzDownsample = 2;

        [Tooltip("Fixed Y resolution of the GI volume. Useful if your gameplay mostly happens near a single floor height.")]
        [Min(1)]
        [SerializeField] private int yResolution = 4;

        [Header("Debug")]
        [SerializeField] private bool drawNodeGizmos = false;
        [SerializeField] private float gizmoSphereRadius = 0.15f;

        private GiGrid giGrid;
        private Texture3D giTexture;
        private GridWorld gridWorld;

        // Extents of the GridWorld XZ coverage, for mapping into texture space.
        private int minX, maxX, minY, maxY;

        // Reusable list of active sources for the current frame (GiSource components).
        private readonly List<GiSource> sources = new();

        private float tickTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            gridWorld = GridWorld.Instance;
            if (gridWorld == null)
            {
                Debug.LogError("GiManager: GridWorld.Instance is null; GI will be disabled.");
                enabled = false;
                return;
            }

            // Try immediate initialization; if GridWorld has not populated stacks yet,
            // we'll defer and retry during Update.
            TryInitializeGrid(force: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (gridWorld == null)
                return;

            // Late-population fix: if stacks were empty in Awake, keep retrying
            // until GridWorld has content and GI nodes can be built.
            if (giGrid == null || giGrid.Nodes.Count == 0)
                TryInitializeGrid(force: false);

            if (giGrid == null || giGrid.Nodes.Count == 0)
                return;

            giGrid.DiffusionStrength = diffusionStrength;
            giGrid.Damping = damping;
            giGrid.OcclusionFloorHeightCells = occlusionFloorHeightCells;
            giGrid.OcclusionCutoff = occlusionCutoff;

            tickTimer += Time.deltaTime;
            if (tickTimer < propagationTickInterval)
                return;
            tickTimer = 0f;

            // Gather and apply sources for this tick.
            giGrid.ClearSources();
            CollectSources();
            ApplySourcesToGrid();

            giGrid.StepPropagation();

            if (buildTexture)
                UpdateTextureFromGrid();
        }

        private void TryInitializeGrid(bool force)
        {
            if (gridWorld == null)
                return;

            int stackCount = gridWorld.GetAllStacks()?.Count ?? 0;
            if (!force && stackCount == 0)
                return;

            giGrid = new GiGrid(gridWorld)
            {
                DiffusionStrength = diffusionStrength,
                Damping = damping,
                OcclusionFloorHeightCells = occlusionFloorHeightCells,
                OcclusionCutoff = occlusionCutoff
            };

            // Reset extents from the latest grid content.
            minX = maxX = minY = maxY = 0;
            ComputeGridExtents(gridWorld);

            if (buildTexture)
            {
                if (giTexture == null)
                    AllocateTexture();
                else
                    UpdateTextureFromGrid();
            }
        }

        private void ComputeGridExtents(GridWorld world)
        {
            IReadOnlyDictionary<Vector2Int, GridStack> stacks = world.GetAllStacks();
            bool first = true;
            foreach (var kvp in stacks)
            {
                Vector2Int pos = kvp.Key;
                if (first)
                {
                    first = false;
                    minX = maxX = pos.x;
                    minY = maxY = pos.y;
                }
                else
                {
                    if (pos.x < minX) minX = pos.x;
                    if (pos.x > maxX) maxX = pos.x;
                    if (pos.y < minY) minY = pos.y;
                    if (pos.y > maxY) maxY = pos.y;
                }
            }
        }

        private void AllocateTexture()
        {
            int sizeX = Mathf.Max(1, (maxX - minX + 1) / xzDownsample);
            int sizeZ = Mathf.Max(1, (maxY - minY + 1) / xzDownsample);

            giTexture = new Texture3D(sizeX, yResolution, sizeZ, TextureFormat.RGBAHalf, false)
            {
                name = "GiVolume",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Shader.SetGlobalTexture(globalTextureName, giTexture);
        }

        private void UpdateTextureFromGrid()
        {
            if (giTexture == null)
                return;

            int sizeX = giTexture.width;
            int sizeY = giTexture.height;
            int sizeZ = giTexture.depth;

            Color[] data = new Color[sizeX * sizeY * sizeZ];

            IReadOnlyList<GiNode> nodes = giGrid.Nodes;
            if (nodes.Count == 0)
            {
                giTexture.SetPixels(data);
                giTexture.Apply(false, false);
                return;
            }

            // Simple accumulation: for each node, write into its corresponding texel.
            for (int i = 0; i < nodes.Count; i++)
            {
                GiNode node = nodes[i];
                Vector2Int p = node.gridPos;

                int tx = Mathf.Clamp((p.x - minX) / xzDownsample, 0, sizeX - 1);
                int tz = Mathf.Clamp((p.y - minY) / xzDownsample, 0, sizeZ - 1);

                // For now, pack along Y evenly; most use cases will sample near the center.
                int ty = sizeY / 2;

                int index = tx + sizeX * (ty + sizeY * tz);
                data[index] += node.currentIrradiance;
            }

            giTexture.SetPixels(data);
            giTexture.Apply(false, false);
        }

        private void CollectSources()
        {
            sources.Clear();
            GiSource[] found = FindObjectsOfType<GiSource>();
            if (found == null || found.Length == 0)
                return;

            sources.AddRange(found);
        }

        private void ApplySourcesToGrid()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                GiSource src = sources[i];
                if (src == null || !src.isActiveAndEnabled)
                    continue;

                src.ApplyToGrid(giGrid);
            }
        }

        /// <summary>
        /// Samples the current GI irradiance at a world position.
        /// </summary>
        public static Color SampleGi(Vector3 worldPos)
        {
            if (Instance == null || Instance.giGrid == null)
                return Color.black;

            return Instance.giGrid.SampleAtWorldPos(worldPos);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawNodeGizmos || giGrid == null)
                return;

            Gizmos.color = Color.yellow;
            foreach (GiNode node in giGrid.Nodes)
            {
                float intensity = node.currentIrradiance.maxColorComponent;
                if (intensity <= 0.001f)
                    continue;

                Gizmos.color = node.currentIrradiance;
                Gizmos.DrawSphere(node.worldPos, gizmoSphereRadius);
            }
        }
    }
}

