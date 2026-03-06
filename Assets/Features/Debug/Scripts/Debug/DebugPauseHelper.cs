using UnityEngine;

public static class DebugPauseHelper
{
    /// <summary>
    /// Pauses Unity Play Mode when running in the editor.
    /// Safe to call from runtime code; does nothing in builds.
    /// </summary>
    public static void PausePlayMode()
    {
#if UNITY_EDITOR
        UnityEngine.Debug.Break();
#endif
    }
}
