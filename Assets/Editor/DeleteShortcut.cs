using UnityEngine;
using UnityEditor;

namespace RLGames
{
    [InitializeOnLoad]
    public static class DeleteShortcut
    {
        static DeleteShortcut()
        {
            // Called when Unity loads
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;

            // Check for Delete key (mac keyboards usually map to KeyCode.Backspace for delete)
            if ((e.type == EventType.KeyDown) &&
                (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
            {
                foreach (GameObject obj in Selection.gameObjects)
                {
                    Undo.DestroyObjectImmediate(obj);
                }

                // Mark event as used so Unity doesn’t process it twice
                e.Use();
            }
        }
    }
}