using UnityEngine;

/// <summary>
/// Clears <see cref="GuiInput"/> modal rects once per frame before gameplay/UI <see cref="Update"/> runs.
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class GuiInputGate : MonoBehaviour
{
    private static bool s_bootstrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped)
        {
            return;
        }

        s_bootstrapped = true;
        var go = new GameObject(nameof(GuiInputGate));
        DontDestroyOnLoad(go);
        go.AddComponent<GuiInputGate>();
        go.AddComponent<GuiInputEndFrame>();
    }

    private void Update()
    {
        GuiInput.BeginFrame();
    }
}

/// <summary>Runs after <see cref="Interaction"/> LateUpdate so modal rects from OnGUI are still available for end-of-frame cache.</summary>
[DefaultExecutionOrder(1100)]
public sealed class GuiInputEndFrame : MonoBehaviour
{
    private void LateUpdate()
    {
        GuiInput.EndFrame();
    }
}
