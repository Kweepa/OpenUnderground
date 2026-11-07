using UnityEngine;

/// <summary>
/// V-Sync from the first frame: one frame drawn per refresh of the screen, the title screens included.
/// </summary>
/// <remarks>
/// The project's quality level for Windows (Fantastic) has V-Sync off, and the game used to turn it on only in
/// PlayerObject.Start(), when a game was started or loaded. So the logos, the game folder dialog, the title menu and
/// character creation drew as many frames as the graphics card could, at a higher load than the game itself. Set
/// here, before the first scene, the whole run keeps to the screen's refresh rate, in a window or borderless as in
/// exclusive fullscreen. A driver forcing V-Sync on or off still overrides it, as it did in the game.
/// </remarks>
public static class VSync
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        QualitySettings.vSyncCount = 1;
    }
}
