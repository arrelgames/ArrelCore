using UnityEditor;

namespace RadiantGI.Universal {

    [CustomEditor(typeof(RadiantRenderFeature))]
    public class RadiantRenderFeatureEditor : Editor {

        SerializedProperty renderingPath, ignorePostProcessingOption;
        SerializedProperty ignoreOverlayCameras, camerasLayerMask;

        private void OnEnable() {
            renderingPath = serializedObject.FindProperty("renderingPath");
            ignorePostProcessingOption = serializedObject.FindProperty("ignorePostProcessingOption");
            ignoreOverlayCameras = serializedObject.FindProperty("ignoreOverlayCameras");
            camerasLayerMask = serializedObject.FindProperty("camerasLayerMask");
        }

        public override void OnInspectorGUI() {
            EditorGUILayout.PropertyField(ignorePostProcessingOption);
            EditorGUILayout.PropertyField(renderingPath);
            EditorGUILayout.PropertyField(ignoreOverlayCameras);
            EditorGUILayout.PropertyField(camerasLayerMask);
        }
    }
}
