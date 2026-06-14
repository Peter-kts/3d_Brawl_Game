/*
 * ============================================================================
 * IDAMAGEABLE.CS - Interface for any entity that can take damage
 * ============================================================================
 * 
 * INTERFACES IN UNITY:
 * --------------------
 * 
 * An interface defines a "contract" - a set of methods that any implementing
 * class MUST provide. This enables polymorphism: treating different objects
 * the same way if they share common behavior.
 * 
 * Example:
 *   - EnemyHealth implements IDamageable
 *   - PlayerHealth implements IDamageable (future)
 *   - BreakableObject implements IDamageable (future)
 * 
 * The Combat script can call TakeHit() on ANY IDamageable, without knowing
 * whether it's hitting an enemy, player, or destructible crate.
 * 
 * WHY USE INTERFACES?
 * -------------------
 * 
 * 1. ABSTRACTION: Combat.cs doesn't need to know about EnemyHealth specifically
 * 2. EXTENSIBILITY: Add new hittable types without modifying Combat.cs
 * 3. TESTABILITY: Can create mock objects for testing
 * 4. FLEXIBILITY: Same attack can damage enemies, players, objects
 * 
 * USAGE:
 * ------
 * 
 * Instead of:
 *   var enemy = collider.GetComponent<EnemyHealth>();
 *   if (enemy != null) enemy.TakeHit(...);
 * 
 * You can do:
 *   var damageable = collider.GetComponentInParent<IDamageable>();
 *   if (damageable != null) damageable.TakeHit(...);
 * 
 * This works for ANY class implementing IDamageable!
 * 
 * ============================================================================
 */

using UnityEngine;

/*
 * IDamageable:
 * Any entity that can receive damage, knockback, and hitstun
 * 
 * Implement this interface to make something "hittable" by attacks
 */
public interface IDamageable
{
    /*
     * TakeHit: Receive an attack
     * 
     * Parameters:
     *   - damage: HP to subtract
     *   - knockback: Velocity vector (direction × force, can include vertical)
     *   - hitstun: Duration in seconds the entity is stunned
     *   - airborneDuration: Duration in seconds the entity is airborne (0 = not launched)
     *   - hitStopDuration: Duration in seconds to freeze position (0 = no hitstop). Knockback is applied after freeze ends.
     * 
     * The implementing class decides:
     *   - How to apply damage (subtract from HP, show damage numbers, etc.)
     *   - How to apply knockback (physics, CharacterController, etc.)
     *   - How to handle stun (pause AI, disable input, etc.)
     *   - How to handle airborne state (suspend gravity, enable juggling, etc.)
     *   - What happens on death
     */
    void TakeHit(
        int damage,
        Vector3 knockback,
        float hitstun,
        float airborneDuration,
        float hitStopDuration = 0f,
        AttackHeaviness heaviness = AttackHeaviness.Medium,
        AttackHeight height = AttackHeight.Mid,
        GameObject attacker = null
    );
    
    /*
     * IsHitstunned: Check if entity is currently in hitstun
     *
     * Other systems can check this to:
     *   - Pause AI behavior
     *   - Disable player input
     *   - Play hit reaction animations
     */
    bool IsHitstunned { get; }
    
    /*
     * IsAirborne: Check if entity is currently launched in the air
     * 
     * While airborne:
     *   - Gravity may be reduced or suspended
     *   - Entity can be "juggled" with additional attacks
     *   - Different hit reactions may apply
     */
    bool IsAirborne { get; }
    
    /*
     * CurrentHp / MaxHp: Health access for UI or other systems
     */
    int CurrentHp { get; }
    int MaxHp { get; }
}
