/// <summary>
/// Describes which parameter field an AnimationEvent uses.
/// Kept in CombatEditorCore so both the test assembly and the default editor assembly can reference it.
/// </summary>
public enum AnimationEventParameterKind
{
    None   = 0,
    Int    = 1,
    Float  = 2,
    String = 3,
    Object = 4,
}
