using UnityEditor;

/// <summary>
/// Opt-in custom editors that enable collapsible [Header] groups for each script.
/// Each class inherits all behaviour from FoldoutHeaderEditor -- no extra code needed.
/// To add a new script, just add another 2-line entry below.
/// </summary>

[CustomEditor(typeof(Combat))]
public class CombatEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(SimpleEnemyAI))]
public class SimpleEnemyAIEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(EnemyHealth))]
public class EnemyHealthEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(PlayerController))]
public class PlayerControllerEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(LockOnSystem))]
public class LockOnSystemEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(DebugSettings))]
public class DebugSettingsEditor : FoldoutHeaderEditor { }

[CustomEditor(typeof(ThirdPersonCamera))]
public class ThirdPersonCameraEditor : FoldoutHeaderEditor { }
