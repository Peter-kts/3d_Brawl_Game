# Animation Event Functions Reference

This is a quick reference for animation events you can call in this project.

## Visual Event Authoring Tool (Editor)

Menu:
- `Tools > Combat > Animation Event Authoring`

What it does:
- Lets you preview/scrub a clip on a target `Animator` in-editor.
- Add/drag/remove event markers on a timeline (no manual function-name typing).
- Choose functions from a dropdown:
  - Curated mode: common combat event functions.
  - Advanced mode: reflection-based compatible public methods on the animator root.
- Supports parameter editing by signature (`int`, `float`, `string`, `Object`).
- Saves as native Unity `AnimationEvent`s (`Overwrite` or `Append`).

Notes:
- Imported model clips (like `.fbx` sub-clips) are treated as read-only in-place by the tool UI.
- Use `Duplicate Clip For Editing...` to create an editable `.anim` clip before saving events.
- Parameter labels/help in the Event Authoring tool can auto-sync from the descriptor block in this file.

### Event Parameter Descriptors (Editor Auto-Sync)

These descriptors are read by `AnimationEventAuthoringWindow` and refresh automatically when this file is saved.
Keep this block valid JSON.

<!-- EVENT_PARAM_DESCRIPTORS_START -->
```json
{
  "functions": [
    {
      "functionName": "BeginHitbox",
      "intLabel": "Hitbox ID",
      "intHelp": "Hitbox slot ID. Example: 0 = weapon, 1 = left hand, 2 = foot.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "EndHitbox",
      "intLabel": "Hitbox ID",
      "intHelp": "Hitbox slot ID. Example: 0 = weapon, 1 = left hand, 2 = foot.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "OnBeginHitbox",
      "intLabel": "Hitbox ID",
      "intHelp": "Hitbox slot ID forwarded to WeaponCombat.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "OnEndHitbox",
      "intLabel": "Hitbox ID",
      "intHelp": "Hitbox slot ID forwarded to WeaponCombat.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "OnThrowVictimRootMotion",
      "intLabel": "Victim Root Motion Enabled",
      "intHelp": "0 = off (restore original), non-zero = on.",
      "intPresets": [
        { "label": "0 - Off", "value": 0 },
        { "label": "1 - On", "value": 1 }
      ]
    },
    {
      "functionName": "OnThrowPlayerRootMotion",
      "intLabel": "Player Root Motion Enabled",
      "intHelp": "0 = off (restore original), non-zero = on.",
      "intPresets": [
        { "label": "0 - Off", "value": 0 },
        { "label": "1 - On", "value": 1 }
      ]
    },
    {
      "functionName": "OnThrowRelease",
      "intLabel": "Release Profile Index",
      "intHelp": "-1 = default throw values, 0+ = ThrowData.releaseProfiles[index] when available.",
      "intPresets": [
        { "label": "-1 - Default", "value": -1 },
        { "label": "0 - Profile 0", "value": 0 },
        { "label": "1 - Profile 1", "value": 1 }
      ]
    },
    {
      "functionName": "OnThrowDamage",
      "intLabel": "Damage Profile Index",
      "intHelp": "-1 = default throw values, 0+ = ThrowData.releaseProfiles[index] when available.",
      "intPresets": [
        { "label": "-1 - Default", "value": -1 },
        { "label": "0 - Profile 0", "value": 0 },
        { "label": "1 - Profile 1", "value": 1 }
      ]
    },
    {
      "functionName": "OnAttackSfxEvent",
      "intLabel": "Attack SFX Event ID",
      "intHelp": "Matches AttackData.sfxCues entries where trigger is OnAnimEvent and eventId matches."
    },
    {
      "functionName": "OnThrowSfxEvent",
      "intLabel": "Throw SFX Event ID",
      "intHelp": "Matches ThrowData.sfxCues entries where trigger is OnAnimEvent and eventId matches."
    },
    {
      "functionName": "SetGrip",
      "intLabel": "Grip ID",
      "intHelp": "Matches SwordGrip grip preset id."
    }
  ]
}
```
<!-- EVENT_PARAM_DESCRIPTORS_END -->

## Player Combat (`Combat`)

Attach events to clips played by the player attack/throw animator.

### Attack SFX
- `OnAttackSfxEvent()`
- `OnAttackSfxEvent(int eventId)`
- `OnAttackSfxEvent(float eventId)` (rounded to int)
- `OnAttackSfxEvent(string eventId)` (parsed to int)
- `OnAttackSFXEvent()`
- `OnAttackSFXEvent(int eventId)`

What it does:
- Plays attack SFX cue entries in `AttackData.sfxCues` where trigger is `OnAnimEvent` and `eventId` matches.

### Charge Window (weapon charge)
- `OnChargeWindowStart()`
- `OnChargeWindowEnd()`
- `OnAttackChargeWindowStart()`
- `OnAttackChargeWindowEnd()`

What it does:
- Start: opens charge window and begins charge if attack input is held.
- End: closes charge window and forces charge release behavior (including release speed boost logic).

## Player Throw Events (`Combat.Throw`)

- `OnThrowUnparent()`
- `OnThrowVictimRootMotion(int enabled)` (`0` off, non-zero on)
- `OnThrowVictimRootMotionOn()`
- `OnThrowVictimRootMotionOff()`
- `OnThrowPlayerRootMotion(int enabled)` (`0` off, non-zero on)
- `OnThrowPlayerRootMotionOn()`
- `OnThrowPlayerRootMotionOff()`
- `OnThrowRelease()`
- `OnThrowRelease(int releaseProfileIndex)`
- `OnThrowDamage(int profileIndex)`
- `OnThrowEndVfxEvent()`
- `OnThrowSfxEvent()`
- `OnThrowSfxEvent(int eventId)`

What it does:
- Controls throw timing, root-motion toggles, release timing, throw damage profile selection, throw-end VFX, and throw SFX cue events.

## Weapon Combat (`WeaponCombat`)

- `BeginHitbox(int id)`
- `EndHitbox(int id)`
- `BeginWeaponTipActiveFrames()`
- `EndWeaponTipActiveFrames()`

What it does:
- Preferred: opens/closes the configured hitbox slot for `id` (example mapping: `0` weapon tip, `1` left hand, `2` foot).
- Opens/closes weapon tip hitbox active frames for `AttackHitboxType.WeaponStrike` attacks.
- `BeginWeaponTipActiveFrames()` / `EndWeaponTipActiveFrames()` remain backward-compatible aliases for ID `0`.
- Put begin/end events on clips at first/last active frame.

## Weapon Grip (`SwordGrip`)

- `SetGrip(int gripId)`

What it does:
- Applies a configured grip preset (position/rotation/optional scale) to the weapon transform.

## Enemy Attack SFX (`EnemyCombat`)

- `OnAttackSfxEvent()`
- `OnAttackSfxEvent(int eventId)`
- `OnAttackSfxEvent(float eventId)` (rounded to int)
- `OnAttackSfxEvent(string eventId)` (parsed to int)
- `OnAttackSFXEvent()`
- `OnAttackSFXEvent(int eventId)`

What it does:
- Same pattern as player attack SFX events, but for enemy attacks.

## Enemy Death (`SimpleEnemyAI`)

- `OnDeathAnimationComplete()`

What it does:
- Called at the end of death animation to finalize death (`EnemyHealth.CompleteDeath()`).

## Forwarders (for child animators)

Use these only when clips run on a child animator and you need to forward events up to combat scripts.

### `AttackAnimationEventForwarder`
Supports forwarding:
- `OnAttackSfxEvent(...)` variants
- `OnAttackSFXEvent(...)` variants
- `OnBeginHitbox(int id)` / `OnEndHitbox(int id)`
- `OnBeginHitbox()` / `OnEndHitbox()` (defaults to ID `0`)
- `OnChargeWindowStart()`
- `OnChargeWindowEnd()`
- `OnAttackChargeWindowStart()`
- `OnAttackChargeWindowEnd()`

### `ThrowAnimationEventForwarder`
Defines throw event names:
- `OnThrowUnparent()`
- `OnThrowVictimRootMotion(int)`
- `OnThrowVictimRootMotionOn()`
- `OnThrowVictimRootMotionOff()`
- `OnThrowPlayerRootMotion(int)`
- `OnThrowPlayerRootMotionOn()`
- `OnThrowPlayerRootMotionOff()`
- `OnThrowRelease()`
- `OnThrowRelease(int)`
- `OnThrowDamage(int)`

Note:
- Current implementation logs warnings (placeholder), because throw handlers are on player `Combat.Throw`, not enemy `EnemyCombat`.

