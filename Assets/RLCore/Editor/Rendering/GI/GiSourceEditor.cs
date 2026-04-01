using UnityEditor;
using UnityEngine;

namespace RLGames.Editor
{
    [CustomEditor(typeof(GiSource))]
    [CanEditMultipleObjects]
    public sealed class GiSourceEditor : UnityEditor.Editor
    {
        private SerializedProperty lightType;

        private SerializedProperty irradiance;
        private SerializedProperty radius;
        private SerializedProperty respectOcclusion;
        private SerializedProperty intensityMultiplier;

        private SerializedProperty innerAngle;
        private SerializedProperty outerAngle;

        private SerializedProperty rectWidth;
        private SerializedProperty rectHeight;
        private SerializedProperty rectSamplesX;
        private SerializedProperty rectSamplesY;
        private SerializedProperty rectNormalFalloff;

        private SerializedProperty directionalMaxDistance;
        private SerializedProperty directionalMaxAffectedNodes;

        private SerializedProperty drawGizmos;
        private SerializedProperty drawOnlyWhenSelected;
        private SerializedProperty gizmoPointColor;
        private SerializedProperty gizmoSpotColor;
        private SerializedProperty gizmoRectColor;
        private SerializedProperty gizmoDirectionalColor;

        private void OnEnable()
        {
            lightType = serializedObject.FindProperty("lightType");

            irradiance = serializedObject.FindProperty("irradiance");
            radius = serializedObject.FindProperty("radius");
            respectOcclusion = serializedObject.FindProperty("respectOcclusion");
            intensityMultiplier = serializedObject.FindProperty("intensityMultiplier");

            innerAngle = serializedObject.FindProperty("innerAngle");
            outerAngle = serializedObject.FindProperty("outerAngle");

            rectWidth = serializedObject.FindProperty("rectWidth");
            rectHeight = serializedObject.FindProperty("rectHeight");
            rectSamplesX = serializedObject.FindProperty("rectSamplesX");
            rectSamplesY = serializedObject.FindProperty("rectSamplesY");
            rectNormalFalloff = serializedObject.FindProperty("rectNormalFalloff");

            directionalMaxDistance = serializedObject.FindProperty("directionalMaxDistance");
            directionalMaxAffectedNodes = serializedObject.FindProperty("directionalMaxAffectedNodes");

            drawGizmos = serializedObject.FindProperty("drawGizmos");
            drawOnlyWhenSelected = serializedObject.FindProperty("drawOnlyWhenSelected");
            gizmoPointColor = serializedObject.FindProperty("gizmoPointColor");
            gizmoSpotColor = serializedObject.FindProperty("gizmoSpotColor");
            gizmoRectColor = serializedObject.FindProperty("gizmoRectColor");
            gizmoDirectionalColor = serializedObject.FindProperty("gizmoDirectionalColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Type", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightType);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Common", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(irradiance);
            EditorGUILayout.PropertyField(radius);
            EditorGUILayout.PropertyField(respectOcclusion);
            EditorGUILayout.PropertyField(intensityMultiplier);
            EditorGUILayout.Space();

            DrawLightTypeSection();

            EditorGUILayout.LabelField("Gizmos", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(drawGizmos);
            EditorGUILayout.PropertyField(drawOnlyWhenSelected);
            EditorGUILayout.PropertyField(gizmoPointColor);
            EditorGUILayout.PropertyField(gizmoSpotColor);
            EditorGUILayout.PropertyField(gizmoRectColor);
            EditorGUILayout.PropertyField(gizmoDirectionalColor);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLightTypeSection()
        {
            var selectedType = (GiSource.LightType)lightType.enumValueIndex;
            switch (selectedType)
            {
                case GiSource.LightType.Spot:
                    EditorGUILayout.LabelField("Spot", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Spot uses radius as range and applies inner/outer cone falloff.", MessageType.None);
                    EditorGUILayout.PropertyField(innerAngle);
                    EditorGUILayout.PropertyField(outerAngle);
                    EditorGUILayout.Space();
                    break;

                case GiSource.LightType.Rect:
                    EditorGUILayout.LabelField("Rect", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Rect approximates area lighting with a sampled point lattice.", MessageType.None);
                    EditorGUILayout.PropertyField(rectWidth);
                    EditorGUILayout.PropertyField(rectHeight);
                    EditorGUILayout.PropertyField(rectSamplesX);
                    EditorGUILayout.PropertyField(rectSamplesY);
                    EditorGUILayout.PropertyField(rectNormalFalloff);
                    EditorGUILayout.Space();
                    break;

                case GiSource.LightType.Directional:
                    EditorGUILayout.LabelField("Directional", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Directional injects bounded light using max distance and affected-node budget.", MessageType.None);
                    EditorGUILayout.PropertyField(directionalMaxDistance);
                    EditorGUILayout.PropertyField(directionalMaxAffectedNodes);
                    EditorGUILayout.Space();
                    break;
            }
        }
    }
}
