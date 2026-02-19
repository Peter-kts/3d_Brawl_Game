using UnityEngine;

/// <summary>
/// ScriptableObject that holds a single move (AttackData) for use as a template.
/// Create via right-click > Create > Combat > Move Template. Use in ComboSet editor to Load/Save moves.
/// In-game: AttackData defines damage, knockback, hitstun, hitbox timing, etc. for one attack.
/// </summary>
[CreateAssetMenu(fileName = "MoveTemplate", menuName = "Combat/Move Template", order = 1)]
public class MoveTemplate : ScriptableObject
{
    public AttackData attackData = new AttackData();
}
