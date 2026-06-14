using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Visual authoring window for Unity AnimationEvents used by combat/throw clips.
/// Supports timeline marker editing, clip preview, function picking, and save/append workflows.
/// </summary>
public class AnimationEventAuthoringWindow : EditorWindow
{
    [Serializable]
    private struct TransformSnapshot
    {
        public bool hasValue;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
    }

    private enum MethodPickerMode
    {
        Curated = 0,
        Advanced = 1,
        Both = 2
    }

    private enum SfxHelperSource
    {
        AttackMove = 0,
        Throw = 1
    }

    [Serializable]
    private class FunctionDescriptorDb
    {
        public List<FunctionDescriptorEntry> functions = new List<FunctionDescriptorEntry>();
    }

    [Serializable]
    private class FunctionDescriptorEntry
    {
        public string functionName;
        public string intLabel;
        public string intHelp;
        public List<IntPresetEntry> intPresets = new List<IntPresetEntry>();
    }

    [Serializable]
    private class IntPresetEntry
    {
        public string label;
        public int value;
    }

    [Serializable]
    private class EventMarker
    {
        public float normalizedTime = 0f;
        public string functionName = "BeginHitbox";
        public AnimationEventParameterKind parameterKind = AnimationEventParameterKind.Int;
        public string loadedFunctionName = "BeginHitbox";
        public AnimationEventParameterKind loadedParameterKind = AnimationEventParameterKind.Int;
        public bool hasAmbiguousOverload;
        public string ambiguityReason = string.Empty;
        public int intParameter = 0;
        public float floatParameter = 0f;
        public string stringParameter = string.Empty;
        public UnityEngine.Object objectParameter;
        
    }

    private static readonly GUIContent[] HitboxPresetLabels =
    {
        new GUIContent("Custom"),
        new GUIContent("0 - Weapon"),
        new GUIContent("1 - Left Hand"),
        new GUIContent("2 - Foot")
    };

    private static readonly int[] HitboxPresetValues = { int.MinValue, 0, 1, 2 };
    private static readonly GUIContent[] TogglePresetLabels =
    {
        new GUIContent("Custom"),
        new GUIContent("0 - Off"),
        new GUIContent("1 - On")
    };
    private static readonly int[] TogglePresetValues = { int.MinValue, 0, 1 };
    private static readonly GUIContent[] ProfilePresetLabels =
    {
        new GUIContent("Custom"),
        new GUIContent("-1 - Default"),
        new GUIContent("0 - Profile 0"),
        new GUIContent("1 - Profile 1")
    };
    private static readonly int[] ProfilePresetValues = { int.MinValue, -1, 0, 1 };
    private static readonly List<FieldInfo> ComboMoveFields = new List<FieldInfo>();
    private static readonly List<string> ComboMoveNames = new List<string>();

    private Animator targetAnimator;
    private AnimationClip targetClip;
    private AnimationClip previousClip;
    private Animator previousAnimator;
    private float normalizedTime;
    private bool isPlaying;
    private double lastEditorTime;
    private MethodPickerMode methodPickerMode = MethodPickerMode.Curated;
    private Vector2 rootScroll;
    private Vector2 eventListScroll;
    private bool showSelectedEventQuickEditor = true;
    private bool previewEnabled = true;
    private bool lockRootPositionDuringPreview = true;
    private bool lockRootRotationDuringPreview = true;
    private TransformSnapshot rootSnapshot;
    private ComboSet sfxComboSet;
    private SfxHelperSource sfxHelperSource = SfxHelperSource.AttackMove;
    private int sfxMoveIndex = -1;
    private int sfxCueIdSelectionIndex;
    private readonly List<AnimationClip> animatorClipOptions = new List<AnimationClip>();
    private int animatorClipPopupIndex;

    private readonly List<EventMarker> markers = new List<EventMarker>();
    private readonly List<AnimationEventMethodCatalog.MethodOption> methodOptions = new List<AnimationEventMethodCatalog.MethodOption>();
    private readonly List<int> sfxAnimEventIds = new List<int>();
    private readonly Dictionary<string, FunctionDescriptorEntry> functionDescriptorMap = new Dictionary<string, FunctionDescriptorEntry>();
    private readonly HashSet<string> importedClipWriteConfirmedPaths = new HashSet<string>();
    private int selectedMarkerIndex = -1;
    // Bug 1: separate source clip for the copy-events workflow (see DrawMarkerActions).
    private AnimationClip copySourceClip;

    private int draggingMarkerIndex = -1;
    private DateTime functionDescriptorLastWriteUtc = DateTime.MinValue;

    private const string FunctionDescriptorDocPath = "Assets/Features/Combat/AnimationEventFunctions.md";
    private const string FunctionDescriptorStartMarker = "<!-- EVENT_PARAM_DESCRIPTORS_START -->";
    private const string FunctionDescriptorEndMarker = "<!-- EVENT_PARAM_DESCRIPTORS_END -->";
    private static readonly Color SectionHeaderColor = new Color(0.20f, 0.20f, 0.20f, 1f);
    private static readonly Color SectionBorderColor = new Color(0.30f, 0.30f, 0.30f, 1f);
    private readonly Dictionary<string, bool> sectionExpanded = new Dictionary<string, bool>();

    [MenuItem("Tools/Combat/Animation Event Authoring")]
    public static void ShowWindow()
    {
        AnimationEventAuthoringWindow window = GetWindow<AnimationEventAuthoringWindow>("Event Authoring");
        window.minSize = new Vector2(560f, 420f);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        RefreshMethodOptions();
        ReloadFunctionDescriptors(force: true);
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        isPlaying = false;
        StopPreviewAndRestore();
    }

    private void OnEditorUpdate()
    {
        // Keep descriptor metadata fresh without reparsing every frame unless file timestamp changes.
        ReloadFunctionDescriptors(force: false);
        if (!previewEnabled) return;
        if (!isPlaying || targetClip == null) return;
        if (targetClip.length <= 0f) return;

        double now = EditorApplication.timeSinceStartup;
        if (lastEditorTime <= 0d) lastEditorTime = now;
        float deltaTime = (float)(now - lastEditorTime);
        lastEditorTime = now;

        float next = normalizedTime + (deltaTime / targetClip.length);
        normalizedTime = Mathf.Repeat(next, 1f);
        SampleCurrentPose();
        Repaint();
    }

    private void OnGUI()
    {
        // Root scroll view: without it, content below the window height (event list, save
        // buttons) is clipped with no way to reach it once a clip's events are loaded.
        rootScroll = EditorGUILayout.BeginScrollView(rootScroll);
        // UI is organized into collapsible sections so long event lists stay manageable.
        EditorGUILayout.Space(4f);
        if (BeginSection("Selection & Mode"))
        {
            DrawSelectionControls();
            EndSection();
        }

        using (new EditorGUI.DisabledScope(targetAnimator == null || targetClip == null))
        {
            if (BeginSection("Preview"))
            {
                DrawPlaybackControls();
                DrawTimeline();
                EndSection();
            }

            if (BeginSection("Marker Actions"))
            {
                DrawMarkerActions();
                EndSection();
            }

            if (BeginSection("Move SFX Helper"))
            {
                DrawSfxMoveHelper();
                EndSection();
            }

            if (BeginSection("Selected Event"))
            {
                DrawSelectedEventQuickEditor();
                EndSection();
            }

            if (BeginSection("Event List"))
            {
                DrawEventList();
                EndSection();
            }

            if (BeginSection("Save"))
            {
                DrawSaveActions();
                EndSection();
            }
        }
        EditorGUILayout.Space(8f);
        EditorGUILayout.EndScrollView();
    }

    private bool BeginSection(string title)
    {
        EditorGUILayout.Space(6f);
        Rect header = EditorGUILayout.GetControlRect(false, 22f);
        EditorGUI.DrawRect(header, SectionHeaderColor);
        if (!sectionExpanded.ContainsKey(title))
            sectionExpanded[title] = true;

        Rect foldoutRect = new Rect(header.x + 8f, header.y + 2f, header.width - 16f, header.height - 4f);
        sectionExpanded[title] = EditorGUI.Foldout(foldoutRect, sectionExpanded[title], title, true);
        Rect border = new Rect(header.x, header.yMax - 1f, header.width, 1f);
        EditorGUI.DrawRect(border, SectionBorderColor);
        if (!sectionExpanded[title]) return false;
        EditorGUILayout.BeginVertical("box");
        return true;
    }

    private void EndSection()
    {
        EditorGUILayout.EndVertical();
    }

    private void DrawSelectionControls()
    {
        EditorGUI.BeginChangeCheck();
        Animator nextAnimator = (Animator)EditorGUILayout.ObjectField("Target Animator", targetAnimator, typeof(Animator), true);
        bool animatorChanged = nextAnimator != targetAnimator;
        targetAnimator = nextAnimator;
        if (animatorChanged)
            RefreshAnimatorClipOptions();

        DrawTargetClipSelectorFromAnimator();
        methodPickerMode = (MethodPickerMode)EditorGUILayout.EnumPopup("Function List", methodPickerMode);
        bool nextPreviewEnabled = EditorGUILayout.Toggle("Enable Preview", previewEnabled);
        lockRootPositionDuringPreview = EditorGUILayout.Toggle("Lock Root Position", lockRootPositionDuringPreview);
        lockRootRotationDuringPreview = EditorGUILayout.Toggle("Lock Root Rotation", lockRootRotationDuringPreview);
        if (EditorGUI.EndChangeCheck())
        {
            if (previewEnabled != nextPreviewEnabled)
            {
                previewEnabled = nextPreviewEnabled;
                if (!previewEnabled)
                {
                    isPlaying = false;
                    StopPreviewAndRestore();
                }
                else
                {
                    normalizedTime = Mathf.Clamp01(normalizedTime);
                    SampleCurrentPose();
                }
            }
            HandleTargetOrClipChanged();
        }

        if (GUILayout.Button("Restore Character Transform"))
            RestoreRootTransform();
        if (GUILayout.Button("Reload Function Descriptors"))
            ReloadFunctionDescriptors(force: true);

        if (targetAnimator == null || targetClip == null)
        {
            EditorGUILayout.HelpBox("Assign both Animator and AnimationClip to preview and edit events.", MessageType.Info);
        }

        if (IsClipReadOnly(targetClip))
        {
            EditorGUILayout.HelpBox("This clip appears to come from a model import (.fbx). Direct save is allowed; Unity will store events on importer clip settings.", MessageType.Warning);
            if (GUILayout.Button("Duplicate Clip For Editing..."))
                DuplicateClipForEditing();
        }
    }

private void DrawTargetClipSelectorFromAnimator()
    {
        if (targetAnimator == null)
        {
            targetClip = (AnimationClip)EditorGUILayout.ObjectField("Target Clip", targetClip, typeof(AnimationClip), false);
            return;
        }

        if (animatorClipOptions.Count == 0)
            RefreshAnimatorClipOptions();

        // Bug 4 fix: when the current clip is not in this animator's controller keep it visible
        // with an "(ext)" prefix and a warning instead of silently nulling it, which would
        // discard any unsaved marker work.
        bool clipIsExternal = targetClip != null && !animatorClipOptions.Contains(targetClip);
        if (clipIsExternal)
        {
            EditorGUILayout.HelpBox(
                "'" + targetClip.name + "' is not in this animator's controller. " +
                "Select a different clip from the list, or choose <None> to clear.",
                MessageType.Warning);

            string[] clipNames = new string[animatorClipOptions.Count + 2];
            clipNames[0] = "<None>";
            clipNames[1] = "(ext) " + targetClip.name;
            for (int i = 0; i < animatorClipOptions.Count; i++)
            {
                AnimationClip clip = animatorClipOptions[i];
                clipNames[i + 2] = clip != null ? clip.name : "<Missing>";
            }

            int nextIndex = EditorGUILayout.Popup("Target Clip", 1, clipNames);
            if (nextIndex == 0)
                targetClip = null;
            else if (nextIndex >= 2 && nextIndex - 2 < animatorClipOptions.Count)
                targetClip = animatorClipOptions[nextIndex - 2];
            // nextIndex == 1 means keep current external clip, no change
        }
        else
        {
            string[] clipNames = new string[animatorClipOptions.Count + 1];
            clipNames[0] = "<None>";
            int foundIndex = 0;
            for (int i = 0; i < animatorClipOptions.Count; i++)
            {
                AnimationClip clip = animatorClipOptions[i];
                clipNames[i + 1] = clip != null ? clip.name : "<Missing>";
                if (clip == targetClip)
                    foundIndex = i + 1;
            }

            animatorClipPopupIndex = EditorGUILayout.Popup("Target Clip", foundIndex, clipNames);
            targetClip = animatorClipPopupIndex <= 0
                ? null
                : animatorClipOptions[Mathf.Clamp(animatorClipPopupIndex - 1, 0, animatorClipOptions.Count - 1)];
        }
    }

    private void RefreshAnimatorClipOptions()
    {
        animatorClipOptions.Clear();
        animatorClipPopupIndex = 0;
        if (targetAnimator == null) return;
        RuntimeAnimatorController controller = targetAnimator.runtimeAnimatorController;
        if (controller == null) return;

        HashSet<AnimationClip> unique = new HashSet<AnimationClip>();
        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null) continue;
            if (!unique.Add(clip)) continue;
            animatorClipOptions.Add(clip);
        }

        animatorClipOptions.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
    }

    private void DrawPlaybackControls()
    {
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!previewEnabled))
        {
        if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Width(80f)))
        {
            isPlaying = !isPlaying;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        if (GUILayout.Button("Step -1f", GUILayout.Width(90f)))
            StepByFrames(-1);
        if (GUILayout.Button("Step +1f", GUILayout.Width(90f)))
            StepByFrames(1);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Time", GUILayout.Width(36f));
        float clipTime = targetClip != null ? normalizedTime * targetClip.length : 0f;
        EditorGUILayout.LabelField(clipTime.ToString("0.000") + "s", GUILayout.Width(70f));
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        float next = EditorGUILayout.Slider("Preview", normalizedTime, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            normalizedTime = Mathf.Clamp01(next);
            SampleCurrentPose();
        }

        if (!previewEnabled)
            EditorGUILayout.HelpBox("Preview disabled. Enable preview to scrub/sample animation.", MessageType.None);
    }

    private void DrawTimeline()
    {
        Rect timelineRect = GUILayoutUtility.GetRect(10f, 40f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(timelineRect, new Color(0.16f, 0.16f, 0.16f, 1f));
        EditorGUI.DrawRect(new Rect(timelineRect.x, timelineRect.yMax - 1f, timelineRect.width, 1f), new Color(0.32f, 0.32f, 0.32f, 1f));

        // Cyan line = current preview time used for sampling + add-marker operations.
        float playheadX = timelineRect.x + timelineRect.width * normalizedTime;
        EditorGUI.DrawRect(new Rect(playheadX - 1f, timelineRect.y, 2f, timelineRect.height), new Color(0.2f, 0.9f, 1f, 0.9f));

        for (int i = 0; i < markers.Count; i++)
        {
            float markerX = timelineRect.x + timelineRect.width * Mathf.Clamp01(markers[i].normalizedTime);
            Color color = i == selectedMarkerIndex ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(1f, 0.4f, 0.2f, 1f);
            EditorGUI.DrawRect(new Rect(markerX - 2f, timelineRect.y + 2f, 4f, timelineRect.height - 4f), color);
        }

        HandleTimelineInput(timelineRect);
    }

    private void HandleTimelineInput(Rect timelineRect)
    {
        Event evt = Event.current;
        if (!timelineRect.Contains(evt.mousePosition) && draggingMarkerIndex < 0) return;

        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            int hit = FindMarkerAtPosition(timelineRect, evt.mousePosition);
            if (hit >= 0)
            {
                selectedMarkerIndex = hit;
                draggingMarkerIndex = hit;
            }
            else
            {
                selectedMarkerIndex = -1;
                normalizedTime = NormalizedFromTimeline(timelineRect, evt.mousePosition.x);
                SampleCurrentPose();
            }
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && evt.button == 0 && draggingMarkerIndex >= 0)
        {
            markers[draggingMarkerIndex].normalizedTime = NormalizedFromTimeline(timelineRect, evt.mousePosition.x);
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseUp && evt.button == 0)
        {
            draggingMarkerIndex = -1;
        }
    }

private void DrawMarkerActions()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Marker At Preview Time", GUILayout.Width(200f)))
        {
            EventMarker marker = CreateDefaultMarker(normalizedTime);
            markers.Add(marker);
            selectedMarkerIndex = markers.Count - 1;
        }
        if (GUILayout.Button("Reload Events From Clip", GUILayout.Width(180f)))
        {
            LoadMarkersFromClip();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Bug 1 fix: copy source workflow — load markers from a different clip without
        // changing targetClip so events can be transferred between clips.
        EditorGUILayout.BeginHorizontal();
        copySourceClip = (AnimationClip)EditorGUILayout.ObjectField(
            "Copy Source Clip", copySourceClip, typeof(AnimationClip), false);
        using (new EditorGUI.DisabledScope(copySourceClip == null))
        {
            if (GUILayout.Button("Load Events From Source", GUILayout.Width(180f)))
                LoadMarkersFromClip(copySourceClip);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSelectedEventQuickEditor()
    {
        showSelectedEventQuickEditor = EditorGUILayout.Foldout(
            showSelectedEventQuickEditor,
            "Selected Event Quick Editor",
            true);
        if (!showSelectedEventQuickEditor) return;

        EditorGUILayout.BeginVertical("box");
        if (selectedMarkerIndex < 0 || selectedMarkerIndex >= markers.Count)
        {
            EditorGUILayout.HelpBox("Select an event marker to edit it here.", MessageType.None);
            EditorGUILayout.EndVertical();
            return;
        }

        // Mirrors the selected list item so users can edit without scrolling.
        EventMarker marker = markers[selectedMarkerIndex];
        EditorGUILayout.LabelField("Selected: Event " + selectedMarkerIndex, EditorStyles.boldLabel);
        marker.normalizedTime = Mathf.Clamp01(EditorGUILayout.Slider("t", marker.normalizedTime, 0f, 1f));
        DrawMethodAndParameterFields(marker);
        float seconds = targetClip != null ? marker.normalizedTime * targetClip.length : 0f;
        EditorGUILayout.LabelField("Time: " + seconds.ToString("0.000") + "s");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Snap Preview To This Event"))
        {
            normalizedTime = marker.normalizedTime;
            SampleCurrentPose();
        }
        if (GUILayout.Button("Snap Event To Preview Time"))
        {
            marker.normalizedTime = Mathf.Clamp01(normalizedTime);
        }
        if (GUILayout.Button("Delete Selected Event"))
        {
            markers.RemoveAt(selectedMarkerIndex);
            selectedMarkerIndex = Mathf.Clamp(selectedMarkerIndex - 1, -1, markers.Count - 1);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawEventList()
    {
        eventListScroll = EditorGUILayout.BeginScrollView(eventListScroll, GUILayout.MinHeight(160f));

        if (markers.Count == 0)
        {
            EditorGUILayout.HelpBox("No markers yet. Add one from the timeline controls.", MessageType.None);
        }

        for (int i = 0; i < markers.Count; i++)
        {
            EventMarker marker = markers[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            bool selected = i == selectedMarkerIndex;
            if (GUILayout.Toggle(selected, "Event " + i, "Button", GUILayout.Width(80f)))
                selectedMarkerIndex = i;

            marker.normalizedTime = Mathf.Clamp01(EditorGUILayout.Slider("t", marker.normalizedTime, 0f, 1f));
            if (GUILayout.Button("X", GUILayout.Width(24f)))
            {
                markers.RemoveAt(i);
                if (selectedMarkerIndex >= markers.Count) selectedMarkerIndex = markers.Count - 1;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            DrawMethodAndParameterFields(marker);

            float seconds = targetClip != null ? marker.normalizedTime * targetClip.length : 0f;
            EditorGUILayout.LabelField("Time: " + seconds.ToString("0.000") + "s");
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSfxMoveHelper()
    {
        EditorGUI.BeginChangeCheck();
        sfxComboSet = (ComboSet)EditorGUILayout.ObjectField("Combo Set", sfxComboSet, typeof(ComboSet), false);
        sfxHelperSource = (SfxHelperSource)EditorGUILayout.EnumPopup("Source", sfxHelperSource);
        if (sfxHelperSource == SfxHelperSource.AttackMove)
        {
            if (ComboMoveNames.Count == 0)
                BuildComboMoveFieldCache();
            int safeIndex = ComboMoveNames.Count > 0 ? Mathf.Clamp(sfxMoveIndex, 0, ComboMoveNames.Count - 1) : -1;
            sfxMoveIndex = ComboMoveNames.Count > 0
                ? EditorGUILayout.Popup("Move", safeIndex, ComboMoveNames.ToArray())
                : -1;
        }
        if (EditorGUI.EndChangeCheck())
            RefreshSfxCueIdsFromMove();

        if (sfxComboSet == null)
        {
            EditorGUILayout.HelpBox("Assign a ComboSet to pull SFX cue IDs from a move.", MessageType.None);
            return;
        }
        if (sfxHelperSource == SfxHelperSource.AttackMove && (ComboMoveNames.Count == 0 || sfxMoveIndex < 0))
        {
            EditorGUILayout.HelpBox("No AttackData move fields were found on ComboSet.", MessageType.Warning);
            return;
        }

        if (sfxAnimEventIds.Count == 0)
        {
            EditorGUILayout.HelpBox("Selected source has no OnAnimEvent SFX cue IDs.", MessageType.None);
            return;
        }

        string[] cueLabels = new string[sfxAnimEventIds.Count];
        for (int i = 0; i < sfxAnimEventIds.Count; i++)
            cueLabels[i] = "eventId " + sfxAnimEventIds[i];
        sfxCueIdSelectionIndex = Mathf.Clamp(sfxCueIdSelectionIndex, 0, cueLabels.Length - 1);
        sfxCueIdSelectionIndex = EditorGUILayout.Popup("Cue Event ID", sfxCueIdSelectionIndex, cueLabels);

        using (new EditorGUI.DisabledScope(sfxCueIdSelectionIndex < 0 || sfxCueIdSelectionIndex >= sfxAnimEventIds.Count))
        {
            if (GUILayout.Button("Add SFX Marker At Preview Time"))
            {
                int eventId = sfxAnimEventIds[sfxCueIdSelectionIndex];
                EventMarker marker = CreateDefaultMarker(normalizedTime);
                marker.functionName = sfxHelperSource == SfxHelperSource.Throw
                    ? "OnThrowSfxEvent"
                    : "OnAttackSfxEvent";
                marker.parameterKind = AnimationEventParameterKind.Int;
                marker.loadedFunctionName = marker.functionName;
                marker.loadedParameterKind = marker.parameterKind;
                marker.intParameter = eventId;
                markers.Add(marker);
                selectedMarkerIndex = markers.Count - 1;
            }
        }
    }

    private void DrawMethodAndParameterFields(EventMarker marker)
    {
        if (marker.hasAmbiguousOverload)
        {
            string reason = string.IsNullOrWhiteSpace(marker.ambiguityReason)
                ? "This event matches multiple method signatures."
                : marker.ambiguityReason;
            EditorGUILayout.HelpBox("Ambiguous overload: " + reason, MessageType.Warning);
        }

        // "Current (clip)" is what was loaded; "Assign Function" is what will be written next save.
        string loadedLabel = string.IsNullOrWhiteSpace(marker.loadedFunctionName)
            ? "(none)"
            : marker.loadedFunctionName + AnimationEventMethodCatalog.SignatureFromKind(marker.loadedParameterKind);
        EditorGUILayout.LabelField("Current (clip)", loadedLabel);

        if (methodOptions.Count == 0)
        {
            EditorGUILayout.HelpBox("No compatible methods found for current mode.", MessageType.Warning);
            return;
        }

        int selectedOptionIndex = FindMethodOptionIndex(marker.functionName, marker.parameterKind);
        bool hasResolvedSelection = selectedOptionIndex >= 0;
        int uiIndex = hasResolvedSelection ? selectedOptionIndex : 0;

        string[] labels;
        if (hasResolvedSelection)
        {
            labels = new string[methodOptions.Count];
            for (int i = 0; i < methodOptions.Count; i++)
                labels[i] = methodOptions[i].displayName;
        }
        else
        {
            labels = new string[methodOptions.Count + 1];
            labels[0] = "<Keep loaded event mapping>";
            for (int i = 0; i < methodOptions.Count; i++)
                labels[i + 1] = methodOptions[i].displayName;
        }

        int nextIndex = EditorGUILayout.Popup("Assign Function", uiIndex, labels);
        if (hasResolvedSelection && nextIndex >= 0 && nextIndex < methodOptions.Count)
        {
            AnimationEventMethodCatalog.MethodOption option = methodOptions[nextIndex];
            marker.functionName = option.functionName;
            marker.parameterKind = option.parameterKind;
        }
        else if (!hasResolvedSelection && nextIndex > 0 && (nextIndex - 1) < methodOptions.Count)
        {
            AnimationEventMethodCatalog.MethodOption option = methodOptions[nextIndex - 1];
            marker.functionName = option.functionName;
            marker.parameterKind = option.parameterKind;
        }

        switch (marker.parameterKind)
        {
            case AnimationEventParameterKind.Int:
                DrawIntParameterField(marker);
                break;
            case AnimationEventParameterKind.Float:
                marker.floatParameter = EditorGUILayout.FloatField("Float Param", marker.floatParameter);
                break;
            case AnimationEventParameterKind.String:
                marker.stringParameter = EditorGUILayout.TextField("String Param", marker.stringParameter ?? string.Empty);
                break;
            case AnimationEventParameterKind.Object:
                marker.objectParameter = EditorGUILayout.ObjectField("Object Param", marker.objectParameter, typeof(UnityEngine.Object), true);
                break;
        }
    }

    private void DrawIntParameterField(EventMarker marker)
    {
        string label = "Int Param";
        string helpText = string.Empty;
        GUIContent[] presetLabels = null;
        int[] presetValues = null;
        if (!TryGetIntUiFromDescriptor(marker.functionName, ref label, ref helpText, ref presetLabels, ref presetValues))
            GetIntParameterUiForFunction(marker.functionName, ref label, ref helpText, ref presetLabels, ref presetValues);

        if (!string.IsNullOrWhiteSpace(helpText))
            EditorGUILayout.HelpBox(helpText, MessageType.None);

        if (presetLabels != null && presetValues != null && presetLabels.Length == presetValues.Length && presetLabels.Length > 0)
        {
        int presetIndex = 0;
            for (int i = 1; i < presetValues.Length; i++)
        {
                if (presetValues[i] == marker.intParameter)
            {
                presetIndex = i;
                break;
            }
        }

            int nextPreset = EditorGUILayout.Popup(new GUIContent(label + " Preset"), presetIndex, presetLabels);
            if (nextPreset > 0 && nextPreset < presetValues.Length)
                marker.intParameter = presetValues[nextPreset];
        }

        marker.intParameter = EditorGUILayout.IntField(label, marker.intParameter);
    }

    private bool TryGetIntUiFromDescriptor(
        string functionName,
        ref string label,
        ref string helpText,
        ref GUIContent[] presetLabels,
        ref int[] presetValues)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return false;
        if (!functionDescriptorMap.TryGetValue(functionName, out FunctionDescriptorEntry entry) || entry == null) return false;

        if (!string.IsNullOrWhiteSpace(entry.intLabel))
            label = entry.intLabel;
        if (!string.IsNullOrWhiteSpace(entry.intHelp))
            helpText = entry.intHelp;

        if (entry.intPresets != null && entry.intPresets.Count > 0)
        {
            presetLabels = new GUIContent[entry.intPresets.Count + 1];
            presetValues = new int[entry.intPresets.Count + 1];
            presetLabels[0] = new GUIContent("Custom");
            presetValues[0] = int.MinValue;
            for (int i = 0; i < entry.intPresets.Count; i++)
            {
                IntPresetEntry preset = entry.intPresets[i];
                string presetLabel = preset != null && !string.IsNullOrWhiteSpace(preset.label)
                    ? preset.label
                    : "Value " + (preset != null ? preset.value : 0);
                int presetValue = preset != null ? preset.value : 0;
                presetLabels[i + 1] = new GUIContent(presetLabel);
                presetValues[i + 1] = presetValue;
            }
        }

        return true;
    }

    private void ReloadFunctionDescriptors(bool force)
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), FunctionDescriptorDocPath);
        if (!File.Exists(fullPath))
        {
            if (force)
                functionDescriptorMap.Clear();
            return;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(fullPath);
        if (!force && writeTime == functionDescriptorLastWriteUtc) return;

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch
        {
            return;
        }

        int startIdx = content.IndexOf(FunctionDescriptorStartMarker, StringComparison.Ordinal);
        int endIdx = content.IndexOf(FunctionDescriptorEndMarker, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
        {
            functionDescriptorMap.Clear();
            functionDescriptorLastWriteUtc = writeTime;
            return;
        }

        int jsonStart = startIdx + FunctionDescriptorStartMarker.Length;
        string jsonRaw = content.Substring(jsonStart, endIdx - jsonStart).Trim();
        if (string.IsNullOrWhiteSpace(jsonRaw))
        {
            functionDescriptorMap.Clear();
            functionDescriptorLastWriteUtc = writeTime;
            return;
        }

        // Allow JSON to live inside fenced markdown blocks.
        if (jsonRaw.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = jsonRaw.IndexOf('\n');
            int fenceEnd = jsonRaw.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && fenceEnd > firstNewline)
                jsonRaw = jsonRaw.Substring(firstNewline + 1, fenceEnd - firstNewline - 1).Trim();
        }

        FunctionDescriptorDb db = null;
        try
        {
            db = JsonUtility.FromJson<FunctionDescriptorDb>(jsonRaw);
        }
        catch
        {
            db = null;
        }

        functionDescriptorMap.Clear();
        if (db != null && db.functions != null)
        {
            for (int i = 0; i < db.functions.Count; i++)
            {
                FunctionDescriptorEntry entry = db.functions[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.functionName)) continue;
                functionDescriptorMap[entry.functionName] = entry;
            }
        }
        functionDescriptorLastWriteUtc = writeTime;
    }

    private static void GetIntParameterUiForFunction(
        string functionName,
        ref string label,
        ref string helpText,
        ref GUIContent[] presetLabels,
        ref int[] presetValues)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return;

        switch (functionName)
        {
            case "BeginHitbox":
            case "EndHitbox":
            case "OnBeginHitbox":
            case "OnEndHitbox":
                label = "Hitbox ID";
                helpText = "Hitbox slot ID. Example: 0 = weapon, 1 = left hand, 2 = foot.";
                presetLabels = HitboxPresetLabels;
                presetValues = HitboxPresetValues;
                return;

            case "OnThrowVictimRootMotion":
                label = "Victim Root Motion Enabled";
                helpText = "0 = off (restore original), non-zero = on.";
                presetLabels = TogglePresetLabels;
                presetValues = TogglePresetValues;
                return;

            case "OnThrowPlayerRootMotion":
                label = "Player Root Motion Enabled";
                helpText = "0 = off (restore original), non-zero = on.";
                presetLabels = TogglePresetLabels;
                presetValues = TogglePresetValues;
                return;

            case "OnThrowRelease":
                // No parameters — release uses ThrowData values directly.
                return;

            case "OnThrowDamage":
                // No parameters — applies damage only at this frame; knockback fires at OnThrowRelease.
                return;

            case "OnAttackSfxEvent":
            case "OnAttackSFXEvent":
                label = "Attack SFX Event ID";
                helpText = "Matches AttackData.sfxCues entries where trigger is OnAnimEvent and eventId matches.";
                return;

            case "OnThrowSfxEvent":
                label = "Throw SFX Event ID";
                helpText = "Matches ThrowData.sfxCues entries where trigger is OnAnimEvent and eventId matches.";
                return;

            case "SetGrip":
                label = "Grip ID";
                helpText = "Matches SwordGrip grip preset id.";
                return;
        }
    }

    private void DrawSaveActions()
    {
        using (new EditorGUI.DisabledScope(targetClip == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Overwrite Clip Events"))
            {
                if (EditorUtility.DisplayDialog("Overwrite clip events?",
                    "This will replace all existing AnimationEvents on the clip.",
                    "Overwrite",
                    "Cancel"))
                {
                    if (CanWriteToCurrentClip())
                        SaveMarkersToClip(overwrite: true);
                }
            }

            if (GUILayout.Button("Append To Clip Events"))
            {
                if (CanWriteToCurrentClip())
                    SaveMarkersToClip(overwrite: false);
            }

            if (GUILayout.Button("Remove Selected From Clip"))
            {
                if (CanWriteToCurrentClip())
                    RemoveSelectedFromClip();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    private bool CanWriteToCurrentClip()
    {
        if (targetClip == null) return false;
        if (!IsClipReadOnly(targetClip)) return true;

        string clipPath = AssetDatabase.GetAssetPath(targetClip);
        if (string.IsNullOrWhiteSpace(clipPath)) return true;
        if (importedClipWriteConfirmedPaths.Contains(clipPath)) return true;

        bool proceed = EditorUtility.DisplayDialog(
            "Write Events To Imported Clip?",
            "This clip comes from a model import. Unity will save events on importer clip settings for this asset.\n\nContinue?",
            "Continue",
            "Cancel");
        if (proceed)
            importedClipWriteConfirmedPaths.Add(clipPath);
        return proceed;
    }

    private void HandleTargetOrClipChanged()
    {
        bool changed = previousAnimator != targetAnimator || previousClip != targetClip;
        if (!changed)
        {
            RefreshMethodOptions();
            return;
        }

        StopPreviewAndRestore();
        isPlaying = false;
        previousAnimator = targetAnimator;
        previousClip = targetClip;

        LoadMarkersFromClip();
        RefreshMethodOptions();
        normalizedTime = 0f;
        SampleCurrentPose();
    }

    private void RefreshMethodOptions()
    {
        methodOptions.Clear();
        GameObject root = targetAnimator != null ? targetAnimator.gameObject : null;
        if (root == null) return;

        // Curated = known combat-safe functions; Advanced = reflection over compatible root methods.
        if (methodPickerMode == MethodPickerMode.Curated || methodPickerMode == MethodPickerMode.Both)
            methodOptions.AddRange(AnimationEventMethodCatalog.GetCuratedOptions(root));
        if (methodPickerMode == MethodPickerMode.Advanced || methodPickerMode == MethodPickerMode.Both)
            methodOptions.AddRange(AnimationEventMethodCatalog.GetReflectionOptions(root));
    }

    private void RefreshSfxCueIdsFromMove()
    {
        sfxAnimEventIds.Clear();
        sfxCueIdSelectionIndex = 0;
        if (sfxComboSet == null) return;
        List<AttackSfxCue> sourceCues = null;
        if (sfxHelperSource == SfxHelperSource.Throw)
        {
            ThrowData throwData = sfxComboSet.throwData;
            sourceCues = throwData.sfxCues;
        }
        else
        {
            if (ComboMoveNames.Count == 0)
                BuildComboMoveFieldCache();
            if (sfxMoveIndex < 0 || sfxMoveIndex >= ComboMoveFields.Count) return;
            AttackData attack = GetMoveAttackData(sfxComboSet, sfxMoveIndex);
            if (attack != null) sourceCues = attack.sfxCues;
        }

        if (sourceCues == null) return;
        for (int i = 0; i < sourceCues.Count; i++)
        {
            AttackSfxCue cue = sourceCues[i];
            if (cue == null) continue;
            if (cue.trigger != AttackSfxTriggerType.OnAnimEvent) continue;
            if (sfxAnimEventIds.Contains(cue.eventId)) continue;
            sfxAnimEventIds.Add(cue.eventId);
        }

        sfxAnimEventIds.Sort();
    }

    private static AttackData GetMoveAttackData(ComboSet comboSet, int moveIndex)
    {
        if (comboSet == null) return null;
        if (moveIndex < 0 || moveIndex >= ComboMoveFields.Count) return null;
        FieldInfo field = ComboMoveFields[moveIndex];
        if (field == null) return null;
        return field.GetValue(comboSet) as AttackData;
    }

    private static void BuildComboMoveFieldCache()
    {
        ComboMoveFields.Clear();
        ComboMoveNames.Clear();

        FieldInfo[] fields = typeof(ComboSet).GetFields(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field == null) continue;
            if (field.FieldType != typeof(AttackData)) continue;

            string displayName = ObjectNames.NicifyVariableName(field.Name);
            HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
            if (header != null && !string.IsNullOrWhiteSpace(header.header))
                displayName = header.header;

            ComboMoveFields.Add(field);
            ComboMoveNames.Add(displayName);
        }
    }

    private void StepByFrames(int frameDelta)
    {
        if (targetClip == null || targetClip.length <= 0f) return;
        float fps = Mathf.Max(1f, targetClip.frameRate);
        float deltaNormalized = (frameDelta / fps) / targetClip.length;
        normalizedTime = Mathf.Clamp01(normalizedTime + deltaNormalized);
        SampleCurrentPose();
    }

    private void SampleCurrentPose()
    {
        if (!previewEnabled) return;
        if (targetAnimator == null || targetClip == null || targetClip.length <= 0f) return;
        EnsureAnimationMode();
        float sampleTime = Mathf.Clamp01(normalizedTime) * targetClip.length;
        AnimationMode.SampleAnimationClip(targetAnimator.gameObject, targetClip, sampleTime);
        // Optional root locks avoid drifting scene objects while previewing root-motion clips.
        RestoreRootTransformForPreview();
        SceneView.RepaintAll();
    }

    private void EnsureAnimationMode()
    {
        if (AnimationMode.InAnimationMode()) return;
        CacheRootTransform();
        AnimationMode.StartAnimationMode();
    }

    private void StopPreviewAndRestore()
    {
        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
        RestoreRootTransform();
        SceneView.RepaintAll();
    }

    private void CacheRootTransform()
    {
        if (targetAnimator == null) return;
        Transform t = targetAnimator.transform;
        rootSnapshot.hasValue = true;
        rootSnapshot.position = t.position;
        rootSnapshot.rotation = t.rotation;
        rootSnapshot.localScale = t.localScale;
    }

    private void RestoreRootTransform()
    {
        if (!rootSnapshot.hasValue || targetAnimator == null) return;
        Transform t = targetAnimator.transform;
        t.position = rootSnapshot.position;
        t.rotation = rootSnapshot.rotation;
        t.localScale = rootSnapshot.localScale;
    }

    private void RestoreRootTransformForPreview()
    {
        if (!rootSnapshot.hasValue || targetAnimator == null) return;
        Transform t = targetAnimator.transform;
        if (lockRootPositionDuringPreview)
            t.position = rootSnapshot.position;
        if (lockRootRotationDuringPreview)
            t.rotation = rootSnapshot.rotation;
    }

// clipOverride: when set, loads from that clip instead of targetClip (Bug 1 — copy source).
    private void LoadMarkersFromClip(AnimationClip clipOverride = null)
    {
        markers.Clear();
        selectedMarkerIndex = -1;
        AnimationClip source = clipOverride != null ? clipOverride : targetClip;
        if (source == null) return;

        AnimationEvent[] events = AnimationUtility.GetAnimationEvents(source);
        for (int i = 0; i < events.Length; i++)
        {
            AnimationEvent ev = events[i];
            EventMarker marker = new EventMarker();
            marker.normalizedTime = source.length > 0f ? Mathf.Clamp01(ev.time / source.length) : 0f;
            marker.functionName = ev.functionName;
            marker.intParameter = ev.intParameter;
            marker.floatParameter = ev.floatParameter;
            marker.stringParameter = ev.stringParameter;
            marker.objectParameter = ev.objectReferenceParameter;
            // Infer parameter kind from stored values, then refine via animator reflection.
            marker.parameterKind = AnimationEventWindowUtils.GuessParameterKind(ev);
            marker.parameterKind = ResolveParameterKindFromAnimator(marker.functionName, marker.parameterKind, out marker.hasAmbiguousOverload, out marker.ambiguityReason);
            // Bug 3 fix: events with intParameter == 0 are guessed as None, which breaks known
            // combat functions like BeginHitbox(0) (weapon slot). Force Int for those functions.
            if (marker.parameterKind == AnimationEventParameterKind.None
                && AnimationEventWindowUtils.IsKnownIntParamFunction(marker.functionName))
                marker.parameterKind = AnimationEventParameterKind.Int;
            marker.loadedFunctionName = marker.functionName;
            marker.loadedParameterKind = marker.parameterKind;
            markers.Add(marker);
        }
    }

private void SaveMarkersToClip(bool overwrite)
    {
        if (targetClip == null) return;
        if (!ValidateMarkers()) return;

        // Both Overwrite and Append write all current in-memory markers.
        // The only difference is that Overwrite shows a confirmation dialog (handled by caller).
        // Bug 2 fix: the old Append path filtered by isNewMarker, which silently dropped any
        // edits the user made to previously-loaded markers before saving.
        List<AnimationEvent> output = new List<AnimationEvent>();
        for (int i = 0; i < markers.Count; i++)
            output.Add(BuildAnimationEvent(markers[i]));

        output.Sort((a, b) => a.time.CompareTo(b.time));
        if (IsClipReadOnly(targetClip))
        {
            if (!TrySaveEventsToImportedClip(output.ToArray()))
                return;
        }
        else
        {
            AnimationUtility.SetAnimationEvents(targetClip, output.ToArray());
            EditorUtility.SetDirty(targetClip);
            AssetDatabase.SaveAssets();
        }
        AssetDatabase.Refresh();
        LoadMarkersFromClip();
    }

    private bool TrySaveEventsToImportedClip(AnimationEvent[] eventsToWrite)
    {
        if (targetClip == null) return false;
        string clipPath = AssetDatabase.GetAssetPath(targetClip);
        if (string.IsNullOrWhiteSpace(clipPath)) return false;

        ModelImporter importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;
        if (importer == null) return false;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            EditorUtility.DisplayDialog("Unable To Save Events",
                "No import clip settings were found for this model asset.",
                "OK");
            return false;
        }

        int clipIndex = FindImportedClipIndex(clips, targetClip.name);
        if (clipIndex < 0)
        {
            EditorUtility.DisplayDialog("Unable To Save Events",
                "Could not match the selected clip in model import settings. Try duplicating the clip for editing, or verify clip names are unique.",
                "OK");
            return false;
        }

        // Importer clip events store NORMALIZED time (0-1 across the clip), not seconds —
        // see any .fbx.meta "events:" block. Incoming events are in seconds (runtime clip
        // convention), so convert here or everything past 1 second clamps to the clip end.
        float clipLength = targetClip.length;
        for (int i = 0; i < eventsToWrite.Length; i++)
            eventsToWrite[i].time = clipLength > 0f
                ? Mathf.Clamp01(eventsToWrite[i].time / clipLength)
                : 0f;

        ModelImporterClipAnimation clip = clips[clipIndex];
        clip.events = eventsToWrite;
        clips[clipIndex] = clip;

        importer.clipAnimations = clips;
        EditorUtility.SetDirty(importer);

        // Capture clip name before SaveAndReimport() — reimport destroys the sub-asset reference
        // and accessing targetClip.name afterwards returns "" on the invalidated object.
        string clipName = targetClip.name;
        importer.SaveAndReimport();

        // Reimport recreates clip sub-assets; reacquire selected clip by name.
        AnimationClip resolved = LoadImportedClipByName(clipPath, clipName);
        if (resolved != null)
        {
            targetClip = resolved;
            previousClip = targetClip;
        }

        return true;
    }

private static int FindImportedClipIndex(ModelImporterClipAnimation[] clips, string clipName)
        => AnimationEventWindowUtils.FindImportedClipIndex(clips, clipName);

    private static AnimationClip LoadImportedClipByName(string clipPath, string clipName)
    {
        if (string.IsNullOrWhiteSpace(clipPath) || string.IsNullOrWhiteSpace(clipName))
            return null;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(clipPath);

        // Exact match first.
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null) continue;
            if (string.Equals(clip.name, clipName, StringComparison.Ordinal))
                return clip;
        }

        // Case-insensitive fallback to match FindImportedClipIndex behaviour.
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null) continue;
            if (string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                return clip;
        }

        return null;
    }

    private void RemoveSelectedFromClip()
    {
        if (targetClip == null) return;
        if (selectedMarkerIndex < 0 || selectedMarkerIndex >= markers.Count) return;
        markers.RemoveAt(selectedMarkerIndex);
        selectedMarkerIndex = Mathf.Clamp(selectedMarkerIndex - 1, -1, markers.Count - 1);
        SaveMarkersToClip(overwrite: true);
    }

    private bool ValidateMarkers()
    {
        for (int i = 0; i < markers.Count; i++)
        {
            EventMarker marker = markers[i];
            if (string.IsNullOrWhiteSpace(marker.functionName))
            {
                EditorUtility.DisplayDialog("Invalid Event", "Event " + i + " has no function selected.", "OK");
                return false;
            }
            if (marker.parameterKind == AnimationEventParameterKind.Object && marker.objectParameter == null)
            {
                bool proceed = EditorUtility.DisplayDialog("Null Object Parameter",
                    "Event " + i + " uses an Object parameter but has no assigned object. Continue?",
                    "Continue",
                    "Cancel");
                if (!proceed) return false;
            }
        }
        return true;
    }

    private AnimationEvent BuildAnimationEvent(EventMarker marker)
    {
        AnimationEvent ev = new AnimationEvent();
        ev.functionName = marker.functionName;
        ev.time = targetClip != null ? Mathf.Clamp01(marker.normalizedTime) * targetClip.length : 0f;
        switch (marker.parameterKind)
        {
            case AnimationEventParameterKind.Int:
                ev.intParameter = marker.intParameter;
                break;
            case AnimationEventParameterKind.Float:
                ev.floatParameter = marker.floatParameter;
                break;
            case AnimationEventParameterKind.String:
                ev.stringParameter = marker.stringParameter ?? string.Empty;
                break;
            case AnimationEventParameterKind.Object:
                ev.objectReferenceParameter = marker.objectParameter;
                break;
        }
        return ev;
    }

    private EventMarker CreateDefaultMarker(float timeNormalized)
    {
        EventMarker marker = new EventMarker();
        marker.normalizedTime = Mathf.Clamp01(timeNormalized);
        if (methodOptions.Count > 0)
        {
            AnimationEventMethodCatalog.MethodOption option = methodOptions[0];
            marker.functionName = option.functionName;
            marker.parameterKind = option.parameterKind;
        }
        marker.loadedFunctionName = marker.functionName;
        marker.loadedParameterKind = marker.parameterKind;
        return marker;
    }

    private int FindMarkerAtPosition(Rect timelineRect, Vector2 mousePosition)
    {
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            float markerX = timelineRect.x + timelineRect.width * Mathf.Clamp01(markers[i].normalizedTime);
            Rect markerRect = new Rect(markerX - 6f, timelineRect.y, 12f, timelineRect.height);
            if (markerRect.Contains(mousePosition))
                return i;
        }
        return -1;
    }

    private int FindMethodOptionIndex(string functionName, AnimationEventParameterKind parameterKind)
    {
        for (int i = 0; i < methodOptions.Count; i++)
        {
            AnimationEventMethodCatalog.MethodOption option = methodOptions[i];
            if (option.functionName != functionName) continue;
            if (option.parameterKind != parameterKind) continue;
            return i;
        }
        return -1;
    }

private static float NormalizedFromTimeline(Rect timelineRect, float mouseX)
        => AnimationEventWindowUtils.NormalizedFromTimeline(timelineRect, mouseX);

    private static bool IsClipReadOnly(AnimationClip clip)
    {
        if (clip == null) return false;
        string clipPath = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(clipPath)) return false;
        AssetImporter importer = AssetImporter.GetAtPath(clipPath);
        return importer is ModelImporter;
    }

    private void DuplicateClipForEditing()
    {
        if (targetClip == null) return;
        string sourcePath = AssetDatabase.GetAssetPath(targetClip);
        string suggestedName = targetClip.name + "_Events";
        string savePath = EditorUtility.SaveFilePanelInProject("Duplicate Clip", suggestedName, "anim", "Save duplicated editable clip");
        if (string.IsNullOrEmpty(savePath)) return;

        AnimationClip duplicate = new AnimationClip();
        EditorUtility.CopySerialized(targetClip, duplicate);
        AssetDatabase.CreateAsset(duplicate, savePath);
        AssetDatabase.SaveAssets();

        targetClip = duplicate;
        previousClip = targetClip;
        LoadMarkersFromClip();
        Debug.Log("Duplicated clip from " + sourcePath + " to " + savePath);
    }



    private AnimationEventParameterKind ResolveParameterKindFromAnimator(
        string functionName,
        AnimationEventParameterKind inferredKind,
        out bool ambiguous,
        out string reason)
    {
        ambiguous = false;
        reason = string.Empty;

        if (targetAnimator == null || string.IsNullOrWhiteSpace(functionName))
            return inferredKind;

        List<AnimationEventMethodCatalog.MethodOption> reflectionOptions =
            AnimationEventMethodCatalog.GetReflectionOptions(targetAnimator.gameObject);
        bool hasNone = false;
        bool hasInt = false;
        bool hasFloat = false;
        bool hasString = false;
        bool hasObject = false;
        int matchCount = 0;

        for (int i = 0; i < reflectionOptions.Count; i++)
        {
            AnimationEventMethodCatalog.MethodOption option = reflectionOptions[i];
            if (option.functionName != functionName) continue;
            matchCount++;
            switch (option.parameterKind)
            {
                case AnimationEventParameterKind.None: hasNone = true; break;
                case AnimationEventParameterKind.Int: hasInt = true; break;
                case AnimationEventParameterKind.Float: hasFloat = true; break;
                case AnimationEventParameterKind.String: hasString = true; break;
                case AnimationEventParameterKind.Object: hasObject = true; break;
            }
        }

        if (matchCount == 0) return inferredKind;
        if (matchCount == 1)
        {
            if (hasNone) return AnimationEventParameterKind.None;
            if (hasInt) return AnimationEventParameterKind.Int;
            if (hasFloat) return AnimationEventParameterKind.Float;
            if (hasString) return AnimationEventParameterKind.String;
            if (hasObject) return AnimationEventParameterKind.Object;
        }
        else
        {
            ambiguous = true;
            reason = "Multiple overloads exist for '" + functionName + "'.";
        }

        // Prefer inferred kind when it matches one of the available overloads.
        if (inferredKind == AnimationEventParameterKind.None && hasNone) return inferredKind;
        if (inferredKind == AnimationEventParameterKind.Int && hasInt) return inferredKind;
        if (inferredKind == AnimationEventParameterKind.Float && hasFloat) return inferredKind;
        if (inferredKind == AnimationEventParameterKind.String && hasString) return inferredKind;
        if (inferredKind == AnimationEventParameterKind.Object && hasObject) return inferredKind;

        // If no explicit parameter values are set, prefer int over none for combat IDs.
        if (inferredKind == AnimationEventParameterKind.None && hasInt && !hasNone)
            return AnimationEventParameterKind.Int;

        if (ambiguous && inferredKind == AnimationEventParameterKind.None && hasNone && hasInt)
            reason = "Cannot distinguish () vs (int) when parameter values are default/zero.";

        // SFX anim events are generally authored as int event IDs; prefer int when overloads are ambiguous.
        if (hasNone && hasInt &&
            (functionName == "OnAttackSfxEvent" || functionName == "OnThrowSfxEvent"))
            return AnimationEventParameterKind.Int;

        return inferredKind;
    }
}
