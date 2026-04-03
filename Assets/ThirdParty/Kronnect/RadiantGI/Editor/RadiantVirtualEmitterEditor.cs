using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace RadiantGI.Universal {

    [CustomEditor(typeof(RadiantVirtualEmitter))]
    public class RadiantVirtualEmitterEditor : Editor {

        SerializedProperty color, intensity, range;
        SerializedProperty addMaterialEmission, targetRenderer, material, emissionPropertyName, materialIndex;
        SerializedProperty boxCenter, boxSize, boundsInLocalSpace, fadeDistance;

        private readonly BoxBoundsHandle m_BoundsHandle = new BoxBoundsHandle();
        private readonly SphereBoundsHandle m_SphereHandle = new SphereBoundsHandle();

        void OnEnable() {
            color = serializedObject.FindProperty("color");
            intensity = serializedObject.FindProperty("intensity");
            range = serializedObject.FindProperty("range");
            addMaterialEmission = serializedObject.FindProperty("addMaterialEmission");
            targetRenderer = serializedObject.FindProperty("targetRenderer");
            material = serializedObject.FindProperty("material");
            emissionPropertyName = serializedObject.FindProperty("emissionPropertyName");
            materialIndex = serializedObject.FindProperty("materialIndex");
            boxCenter = serializedObject.FindProperty("boxCenter");
            boxSize = serializedObject.FindProperty("boxSize");
            boundsInLocalSpace = serializedObject.FindProperty("boundsInLocalSpace");
            fadeDistance = serializedObject.FindProperty("fadeDistance");
        }

        protected virtual void OnSceneGUI() {
            RadiantVirtualEmitter vi = (RadiantVirtualEmitter)target;

            // draw the handle
            Bounds bounds = vi.GetBounds();
            m_BoundsHandle.center = bounds.center;
            m_BoundsHandle.size = bounds.size;
            EditorGUI.BeginChangeCheck();
            m_BoundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck()) {
                // record the target object before setting new values so changes can be undone/redone
                Undo.RecordObject(vi, "Change Bounds");

                // copy the handle's updated data back to the target object
                Bounds newBounds = new Bounds();
                newBounds.center = m_BoundsHandle.center;
                newBounds.size = m_BoundsHandle.size;
                vi.SetBounds(newBounds);
            }

            // draw sphere radius
            m_SphereHandle.center = vi.transform.position;
            m_SphereHandle.radius = vi.range;
            EditorGUI.BeginChangeCheck();
            m_SphereHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck()) {
                // record the target object before setting new values so changes can be undone/redone
                Undo.RecordObject(vi, "Change Radius");
                vi.range = m_SphereHandle.radius;
            }
        }

        public override void OnInspectorGUI() {

            serializedObject.Update();

            EditorGUILayout.PropertyField(color);
            EditorGUILayout.PropertyField(addMaterialEmission);
            if (addMaterialEmission.boolValue) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(targetRenderer);
                EditorGUILayout.PropertyField(material);
                EditorGUILayout.PropertyField(emissionPropertyName);
                EditorGUILayout.PropertyField(materialIndex);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(intensity);
            EditorGUILayout.PropertyField(range);
            EditorGUILayout.PropertyField(boxCenter);
            EditorGUILayout.PropertyField(boxSize);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(boundsInLocalSpace, new GUIContent("Local Space"));
            if (EditorGUI.EndChangeCheck()) {
                RadiantVirtualEmitter vi = (RadiantVirtualEmitter)target;
                if (boundsInLocalSpace.boolValue) {
                    boxCenter.vector3Value = Vector3.zero;
                } else {
                    boxCenter.vector3Value = vi.transform.position;
                }
                vi.SetBounds(new Bounds(boxCenter.vector3Value, boxSize.vector3Value));
            }
            EditorGUILayout.PropertyField(fadeDistance);
            
            serializedObject.ApplyModifiedProperties();

        }

    }


    public static class RadiantVirtualEmitterEditorExtension {

        [MenuItem("GameObject/Create Other/Radiant GI/Virtual Emitter")]
        static void CreateEmitter(MenuCommand menuCommand) {
            GameObject emitter = new GameObject("Radiant Virtual Emitter", typeof(RadiantVirtualEmitter));

            GameObjectUtility.SetParentAndAlign(emitter, menuCommand.context as GameObject);

            Undo.RegisterCreatedObjectUndo(emitter, "Create Virtual Emitter");
            Selection.activeObject = emitter;
        }

    }
}

