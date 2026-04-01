using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Marks a light/emissive object as a GI source.
    /// Does not create actual Unity lights; instead, it injects irradiance into <see cref="GiGrid"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiSource : MonoBehaviour
    {
        private struct SourceState
        {
            public Vector3 position;
            public Quaternion rotation;
            public LightType type;
            public Color irradiance;
            public float radius;
            public float intensityMultiplier;
            public float innerAngle;
            public float outerAngle;
            public float rectWidth;
            public float rectHeight;
            public int rectSamplesX;
            public int rectSamplesY;
            public float rectNormalFalloff;
            public float directionalMaxDistance;
            public int directionalMaxAffectedNodes;
            public bool respectOcclusion;
        }

        public enum LightType
        {
            Point = 0,
            Spot = 1,
            Rect = 2,
            Directional = 3
        }

        [Header("Type")]
        [SerializeField] private LightType lightType = LightType.Point;

        [Header("Common")]
        [Tooltip("Peak irradiance contributed to nearby GI nodes.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color irradiance = Color.white;

        [Tooltip("World-space radius within which this source affects GI nodes.")]
        [Min(0.1f)]
        [SerializeField] private float radius = 5f;

        [Tooltip("If true, grid-based LOS is used so light does not pass through blocked tiles.")]
        [SerializeField] private bool respectOcclusion = true;

        [Tooltip("Optional per-source intensity multiplier, useful for animation.")]
        [SerializeField] private float intensityMultiplier = 1f;

        [Header("Spot")]
        [Range(1f, 179f)]
        [SerializeField] private float innerAngle = 25f;
        [Range(1f, 179f)]
        [SerializeField] private float outerAngle = 45f;

        [Header("Rect")]
        [Min(0.1f)]
        [SerializeField] private float rectWidth = 2f;
        [Min(0.1f)]
        [SerializeField] private float rectHeight = 1f;
        [Range(1, 4)]
        [SerializeField] private int rectSamplesX = 2;
        [Range(1, 4)]
        [SerializeField] private int rectSamplesY = 2;
        [Range(0f, 1f)]
        [SerializeField] private float rectNormalFalloff = 0.5f;

        [Header("Directional")]
        [Min(0.1f)]
        [SerializeField] private float directionalMaxDistance = 8f;
        [Range(1, 4096)]
        [SerializeField] private int directionalMaxAffectedNodes = 512;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool drawOnlyWhenSelected = true;
        [SerializeField] private Color gizmoPointColor = new Color(1f, 0.9f, 0.2f, 0.85f);
        [SerializeField] private Color gizmoSpotColor = new Color(1f, 0.5f, 0.2f, 0.85f);
        [SerializeField] private Color gizmoRectColor = new Color(0.2f, 0.9f, 1f, 0.85f);
        [SerializeField] private Color gizmoDirectionalColor = new Color(0.5f, 1f, 0.3f, 0.85f);

        private SourceState lastState;
        private bool stateInitialized;

        /// <summary>
        /// Called by <see cref="GiManager"/> once per propagation tick to contribute source energy.
        /// </summary>
        public void ApplyToGrid(GiGrid grid)
        {
            if (grid == null)
                return;

            Color contribution = irradiance * Mathf.Max(0f, intensityMultiplier);
            if (contribution.maxColorComponent <= 0f)
                return;

            switch (lightType)
            {
                case LightType.Spot:
                {
                    float clampedInner;
                    float clampedOuter;
                    GetClampedSpotAngles(out clampedInner, out clampedOuter);
                    grid.AddSpotSource(
                        transform.position,
                        transform.forward,
                        GetClampedRadius(),
                        contribution,
                        clampedInner,
                        clampedOuter,
                        respectOcclusion
                    );
                    break;
                }
                case LightType.Rect:
                    grid.AddRectSourceApprox(
                        transform.position,
                        transform.right,
                        transform.forward,
                        transform.up,
                        GetClampedRectWidth(),
                        GetClampedRectHeight(),
                        contribution,
                        GetClampedRectSamplesX(),
                        GetClampedRectSamplesY(),
                        Mathf.Clamp01(rectNormalFalloff),
                        GetClampedRadius(),
                        respectOcclusion
                    );
                    break;
                case LightType.Directional:
                    grid.AddDirectionalSource(
                        transform.forward,
                        contribution,
                        GetClampedDirectionalMaxDistance(),
                        GetClampedDirectionalMaxAffectedNodes(),
                        respectOcclusion
                    );
                    break;
                default:
                    grid.AddRadialSource(transform.position, GetClampedRadius(), contribution, respectOcclusion);
                    break;
            }
        }

        public void SetIntensityMultiplier(float value)
        {
            intensityMultiplier = value;
        }

        public bool ConsumeDirtyState()
        {
            SourceState current = CaptureState();
            if (!stateInitialized)
            {
                lastState = current;
                stateInitialized = true;
                return true;
            }

            bool dirty = !StatesEqual(lastState, current);
            if (dirty)
                lastState = current;
            return dirty;
        }

        private float GetClampedRadius() => Mathf.Max(0.1f, radius);
        private float GetClampedRectWidth() => Mathf.Max(0.1f, rectWidth);
        private float GetClampedRectHeight() => Mathf.Max(0.1f, rectHeight);
        private int GetClampedRectSamplesX() => Mathf.Clamp(rectSamplesX, 1, 4);
        private int GetClampedRectSamplesY() => Mathf.Clamp(rectSamplesY, 1, 4);
        private float GetClampedDirectionalMaxDistance() => Mathf.Max(0.1f, directionalMaxDistance);
        private int GetClampedDirectionalMaxAffectedNodes() => Mathf.Clamp(directionalMaxAffectedNodes, 1, 4096);

        private void GetClampedSpotAngles(out float inner, out float outer)
        {
            inner = Mathf.Clamp(innerAngle, 1f, 179f);
            outer = Mathf.Clamp(Mathf.Max(inner, outerAngle), 1f, 179f);
        }

        private SourceState CaptureState()
        {
            return new SourceState
            {
                position = transform.position,
                rotation = transform.rotation,
                type = lightType,
                irradiance = irradiance,
                radius = radius,
                intensityMultiplier = intensityMultiplier,
                innerAngle = innerAngle,
                outerAngle = outerAngle,
                rectWidth = rectWidth,
                rectHeight = rectHeight,
                rectSamplesX = rectSamplesX,
                rectSamplesY = rectSamplesY,
                rectNormalFalloff = rectNormalFalloff,
                directionalMaxDistance = directionalMaxDistance,
                directionalMaxAffectedNodes = directionalMaxAffectedNodes,
                respectOcclusion = respectOcclusion
            };
        }

        private static bool StatesEqual(SourceState a, SourceState b)
        {
            return a.position == b.position &&
                   a.rotation == b.rotation &&
                   a.type == b.type &&
                   a.irradiance == b.irradiance &&
                   Mathf.Approximately(a.radius, b.radius) &&
                   Mathf.Approximately(a.intensityMultiplier, b.intensityMultiplier) &&
                   Mathf.Approximately(a.innerAngle, b.innerAngle) &&
                   Mathf.Approximately(a.outerAngle, b.outerAngle) &&
                   Mathf.Approximately(a.rectWidth, b.rectWidth) &&
                   Mathf.Approximately(a.rectHeight, b.rectHeight) &&
                   a.rectSamplesX == b.rectSamplesX &&
                   a.rectSamplesY == b.rectSamplesY &&
                   Mathf.Approximately(a.rectNormalFalloff, b.rectNormalFalloff) &&
                   Mathf.Approximately(a.directionalMaxDistance, b.directionalMaxDistance) &&
                   a.directionalMaxAffectedNodes == b.directionalMaxAffectedNodes &&
                   a.respectOcclusion == b.respectOcclusion;
        }

        private void OnEnable()
        {
            GiSourceRegistry.Register(this);
            stateInitialized = false;
        }

        private void OnDisable()
        {
            GiSourceRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            DrawSourceGizmo(isSelected: false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawSourceGizmo(isSelected: true);
        }

        private void DrawSourceGizmo(bool isSelected)
        {
            if (!drawGizmos)
                return;

            if (drawOnlyWhenSelected && !isSelected)
                return;

            switch (lightType)
            {
                case LightType.Spot:
                    DrawSpotGizmo();
                    break;
                case LightType.Rect:
                    DrawRectGizmo();
                    break;
                case LightType.Directional:
                    DrawDirectionalGizmo();
                    break;
                default:
                    DrawPointGizmo();
                    break;
            }
        }

        private void DrawPointGizmo()
        {
            Gizmos.color = gizmoPointColor;
            Gizmos.DrawWireSphere(transform.position, GetClampedRadius());
        }

        private void DrawSpotGizmo()
        {
            float clampedInner;
            float clampedOuter;
            GetClampedSpotAngles(out clampedInner, out clampedOuter);

            Vector3 origin = transform.position;
            Vector3 forwardDir = transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;
            Vector3 rightDir = transform.right.sqrMagnitude > 1e-6f ? transform.right.normalized : Vector3.right;
            Vector3 upDir = transform.up.sqrMagnitude > 1e-6f ? transform.up.normalized : Vector3.up;
            float range = GetClampedRadius();
            float outerRadius = Mathf.Tan(0.5f * clampedOuter * Mathf.Deg2Rad) * range;
            float innerRadius = Mathf.Tan(0.5f * clampedInner * Mathf.Deg2Rad) * range;
            Vector3 capCenter = origin + forwardDir * range;

            Gizmos.color = gizmoSpotColor;
            Gizmos.DrawLine(origin, capCenter + rightDir * outerRadius);
            Gizmos.DrawLine(origin, capCenter - rightDir * outerRadius);
            Gizmos.DrawLine(origin, capCenter + upDir * outerRadius);
            Gizmos.DrawLine(origin, capCenter - upDir * outerRadius);

            DrawWireCircle(capCenter, forwardDir, outerRadius, 20);
            Gizmos.color = new Color(gizmoSpotColor.r, gizmoSpotColor.g, gizmoSpotColor.b, gizmoSpotColor.a * 0.75f);
            DrawWireCircle(capCenter, forwardDir, innerRadius, 20);
        }

        private void DrawRectGizmo()
        {
            Vector3 origin = transform.position;
            Vector3 rightDir = transform.right.sqrMagnitude > 1e-6f ? transform.right.normalized : Vector3.right;
            Vector3 forwardDir = transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;
            Vector3 upDir = transform.up.sqrMagnitude > 1e-6f ? transform.up.normalized : Vector3.up;
            float halfW = GetClampedRectWidth() * 0.5f;
            float halfH = GetClampedRectHeight() * 0.5f;

            Vector3 p0 = origin + rightDir * halfW + forwardDir * halfH;
            Vector3 p1 = origin - rightDir * halfW + forwardDir * halfH;
            Vector3 p2 = origin - rightDir * halfW - forwardDir * halfH;
            Vector3 p3 = origin + rightDir * halfW - forwardDir * halfH;

            Gizmos.color = gizmoRectColor;
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);

            int sx = GetClampedRectSamplesX();
            int sy = GetClampedRectSamplesY();
            for (int x = 0; x < sx; x++)
            {
                float tx = (x + 0.5f) / sx;
                float ox = Mathf.Lerp(-halfW, halfW, tx);
                for (int y = 0; y < sy; y++)
                {
                    float ty = (y + 0.5f) / sy;
                    float oy = Mathf.Lerp(-halfH, halfH, ty);
                    Vector3 samplePos = origin + rightDir * ox + forwardDir * oy;
                    Gizmos.DrawWireSphere(samplePos, 0.04f);
                }
            }

            Gizmos.color = new Color(gizmoRectColor.r, gizmoRectColor.g, gizmoRectColor.b, gizmoRectColor.a * 0.55f);
            Gizmos.DrawWireSphere(origin, GetClampedRadius());
            Gizmos.DrawLine(origin, origin + upDir * Mathf.Min(GetClampedRadius(), 1.5f));
        }

        private void DrawDirectionalGizmo()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;
            float distance = GetClampedDirectionalMaxDistance();
            Vector3 end = origin + dir * distance;
            float headSize = Mathf.Min(0.5f, distance * 0.2f);
            Vector3 side = Vector3.Cross(dir, transform.up).normalized;
            if (side.sqrMagnitude <= 1e-6f)
                side = Vector3.Cross(dir, Vector3.up).normalized;

            Gizmos.color = gizmoDirectionalColor;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawLine(end, end - dir * headSize + side * headSize * 0.5f);
            Gizmos.DrawLine(end, end - dir * headSize - side * headSize * 0.5f);

            Vector3 center = origin + dir * (distance * 0.5f);
            Vector3 size = new Vector3(distance * 0.25f, distance * 0.25f, distance);
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(dir, transform.up), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = old;
        }

        private static void DrawWireCircle(Vector3 center, Vector3 normal, float radius, int segments)
        {
            if (radius <= 0f || segments < 3)
                return;

            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude <= 1e-6f)
                tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

            float step = Mathf.PI * 2f / segments;
            Vector3 prev = center + tangent * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * step;
                Vector3 next = center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}

