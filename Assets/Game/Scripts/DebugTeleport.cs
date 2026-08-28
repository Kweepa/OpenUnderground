using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class DebugTeleport : MonoBehaviour
{
#if UNITY_EDITOR

    private void Awake()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDestroy()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (Event.current.type == EventType.KeyDown)
        {
            switch (Event.current.keyCode)
            {
            case KeyCode.P:
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

                    if (Physics.Raycast(ray, out RaycastHit hit, 1000.0f, LayerMasks.EnvironmentAndCeiling))
                    {
                        PlayerObject.Player.transform.position = hit.point + Vector3.up;
                        Selection.activeGameObject = null;
                        EditorApplication.ExecuteMenuItem("Window/General/Game");
                    }
                }
                break;
            case KeyCode.Q:
                if (Selection.activeTransform != null)
                {
                    // teleport the player next to the selected object
                    Vector3 pos = Selection.activeTransform.position;
                    for (int i = 0; i < 4; ++i)
                    {
                        Vector3[] dir = { Vector3.right, Vector3.forward, Vector3.left, Vector3.down };
                        if (!Physics.Raycast(pos + Vector3.up, dir[i], 2.0f, LayerMasks.EnvironmentOnly)
                            && Physics.Raycast(pos + Vector3.up + 2.0f * dir[i], Vector3.down, out RaycastHit hit, 2.0f,
                                LayerMasks.EnvironmentOnly))
                        {
                            PlayerObject.Player.transform.position = hit.point + Vector3.up;
                            Vector3 lookAt = pos;
                            lookAt.y = PlayerObject.Player.transform.position.y;
                            PlayerObject.Player.transform.LookAt(lookAt);
                            Selection.activeGameObject = null;
                            EditorApplication.ExecuteMenuItem("Window/General/Game");
                            break;
                        }
                    }
                }
                break;
            }
        }
    }
#endif
}
