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
      "intHelp": "Hitbox slot ID forwarded to WeaponCombat or EnemyCombat.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "OnEndHitbox",
      "intLabel": "Hitbox ID",
      "intHelp": "Hitbox slot ID forwarded to WeaponCombat or EnemyCombat.",
      "intPresets": [
        { "label": "0 - Weapon", "value": 0 },
        { "label": "1 - Left Hand", "value": 1 },
        { "label": "2 - Foot", "value": 2 }
      ]
    },
    {
      "functionName": "OnThrowAttach",
      "intLabel": "Grab Socket Index",
      "intHelp": "Index into ComboSet.throwGrabSocketNames. Can fire multiple times to swap sockets mid-throw.",
      "intPresets": [
        { "label": "0 - Socket 0", "value": 0 },
        { "label": "1 - Socket 1", "value": 1 },
        { "label": "2 - Socket 2", "value": 2 }
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
      "functionName": "OnThrowHitStop",
      "intLabel": "Hit Stop Profile Index",
      "intHelp": "Index into ComboSet.throwHitStops array. Freezes both thrower and victim animators for the configured duration.",
      "intPresets": [
        { "label": "0 - Profile 0", "value": 0 },
        { "label": "1 - Profile 1", "value": 1 },
        { "label": "2 - Profile 2", "value": 2 }
      ]
    },
    {
      "functionName": "SetGrip",
      "intLabel": "Grip ID",
      "intHelp": "Matches SwordGrip grip preset id."
    }
  ]
}
```



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

### Throw Attach / Socket Swap

- `OnThrowAttach()` — snaps the victim to the default grab socket (per-throw `grabSocketName` or the `grabSocket` reference on Combat)
- `OnThrowAttach(int socketIndex)` — snaps the victim to the socket at `ComboSet.throwGrabSocketNames[socketIndex]`. Can fire multiple times during a single throw to swap between attachment points mid-animation.

What it does:

- On the first call during a throw, plays the victim's receive animation, attaches them to the specified socket, and begins per-frame NT synchronization.
- On subsequent calls, swaps the active socket so the victim follows the new attachment point for the remainder of the throw.
- Socket names are configured once in `ComboSet.throwGrabSocketNames` and shared across all throw types.
- `OnThrowVictimNudge(Object nudgeAsset)` — smoothly nudge the throw victim by a local-space offset over time. Drag a `ThrowVictimNudge` ScriptableObject into the event's Object field. Works both while pseudo-parented (offset relative to grab socket) and after `OnThrowUnparent` (additive world-space delta using the socket rotation at unparent time). Multiple nudges accumulate additively. Safe to fire multiple times on the same clip.
- `OnThrowChargeWindowStart()` — opens the throw charge window; player can begin charging a throw
- `OnThrowChargeWindowEnd()` — closes the throw charge window
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
- `OnThrowHitStop(int index)` — freezes both thrower and victim animators for the duration configured in `ComboSet.throwHitStops[index]`. Multiple profiles let you author different freeze durations at different throw beats. Reuses the existing hit-stop pipeline (`UpdateHitStop`). Optional gamepad rumble per profile.

What it does:

- Controls throw timing, root-motion toggles, release timing, throw damage profile selection, throw-end VFX, throw SFX cue events, and throw hit stops.

## Enemy Throw Relay (`EnemyHealth`)

- `OnThrowRelease()`
- `OnThrowRelease(int releaseProfileIndex)`

What it does:

- Victim-side fallback relay for throw release events.
- Finds the active throw owner (`Combat`) currently holding this victim and forwards `OnThrowRelease(...)` to it.

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

## Enemy Wall Bounce (`SimpleEnemyAI`)

- `OnWallBounceAnimationComplete()`

What it does:

- Called at the end of wall-bounce animation.
- Clears wall-bounce latch and, if still hitstunned, hands off into prone (`EnemyProneSystem.Enter`).

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

Forwarding target:

- If a `WeaponCombat` parent exists, forwards to `WeaponCombat`.
- Otherwise, forwards to `EnemyCombat` when present.

### `ThrowAnimationEventForwarder`

Defines throw event names:

- `OnThrowVictimNudge(Object)` — relays nudge to the holding `Combat`
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
- `OnThrowHitStop(int)` — relays hit stop to the holding `Combat`

Note:

- Current implementation logs warnings (placeholder), because throw handlers are on player `Combat.Throw`, not enemy `EnemyCombat`.

