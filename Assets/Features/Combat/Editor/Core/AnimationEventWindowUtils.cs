using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pure static helpers extracted from AnimationEventAuthoringWindow so they can be unit-tested
/// without instantiating the EditorWindow.
/// </summary>
public static class AnimationEventWindowUtils
{
    // Functions that are always authored with an int parameter in this project.
    // Used to break the None-vs-Int ambiguity when all stored parameter values are at defaults.
    private static readonly string[] KnownIntParamFunctions =
    {
        "BeginHitbox",
        "EndHitbox",
        "OnBeginHitbox",
        "OnEndHitbox",
        "OnAttackSfxEvent",
        "OnAttackSFXEvent",
        "OnThrowSfxEvent",
        "SetGrip",
    };

    /// <summary>
    /// Returns true when functionName is a known combat function that exclusively uses an int
    /// parameter. Used to break None-vs-Int ambiguity for events whose intParameter happens to
    /// be 0 (e.g. hitbox slot 0 = weapon).
    /// </summary>
    public static bool IsKnownIntParamFunction(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return false;
        for (int i = 0; i < KnownIntParamFunctions.Length; i++)
            if (KnownIntParamFunctions[i] == functionName) return true;
        return false;
    }

    /// <summary>
    /// Infers the parameter kind from the values stored on an AnimationEvent.
    /// Returns None when all values are at their defaults — caller should apply
    /// IsKnownIntParamFunction as a follow-up tiebreaker.
    /// </summary>
    public static AnimationEventParameterKind GuessParameterKind(AnimationEvent ev)
    {
        if (ev == null) return AnimationEventParameterKind.None;
        if (ev.objectReferenceParameter != null) return AnimationEventParameterKind.Object;
        if (!string.IsNullOrEmpty(ev.stringParameter)) return AnimationEventParameterKind.String;
        if (Mathf.Abs(ev.floatParameter) > 0.0001f) return AnimationEventParameterKind.Float;
        if (ev.intParameter != 0) return AnimationEventParameterKind.Int;
        return AnimationEventParameterKind.None;
    }

    /// <summary>
    /// Finds the index of a clip in a ModelImporter clip array by name.
    /// Tries exact match first, then case-insensitive, then falls back to 0 for
    /// single-clip imports where the take name may differ entirely.
    /// </summary>
    public static int FindImportedClipIndex(ModelImporterClipAnimation[] clips, string clipName)
    {
        if (clips == null || clips.Length == 0) return -1;

        for (int i = 0; i < clips.Length; i++)
            if (string.Equals(clips[i].name, clipName, StringComparison.Ordinal))
                return i;

        for (int i = 0; i < clips.Length; i++)
            if (string.Equals(clips[i].name, clipName, StringComparison.OrdinalIgnoreCase))
                return i;

        return clips.Length == 1 ? 0 : -1;
    }

    /// <summary>
    /// Converts a mouse X position inside timelineRect to a [0,1] normalised time.
    /// </summary>
    public static float NormalizedFromTimeline(Rect timelineRect, float mouseX)
    {
        float n = (mouseX - timelineRect.x) / Mathf.Max(1f, timelineRect.width);
        return Mathf.Clamp01(n);
    }
}
