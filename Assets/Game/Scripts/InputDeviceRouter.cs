using UnityEngine;

/// <summary>
/// Runs <see cref="GameInput.RefreshLastActiveDevice"/> early each frame so inventory/magic can branch gamepad vs mouse/kb,
/// and <see cref="GameActions.Refresh"/>, which follows the weapon hand onto the pad's triggers.
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class InputDeviceRouter : MonoBehaviour
{
    private static bool s_bootstrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped)
            return;
        s_bootstrapped = true;
        var go = new GameObject(nameof(InputDeviceRouter));
        DontDestroyOnLoad(go);
        go.AddComponent<InputDeviceRouter>();
    }

    private void Update()
    {
        GameInput.RefreshLastActiveDevice();
        GameActions.Refresh();
    }
}
