# Airborne Hit Flow: From Punch to Get-Up

Complete walkthrough of what happens when the player hits an enemy with an attack
that has `makesAirborne = true` (e.g. the heavy attack), tracing every function
call and state change across all involved scripts.

---

## Overview Timeline

```
Player presses   Hitbox fires   Hit stop     Knockback      Liftoff       Loop          Airborne       Crash         Get-up        Get-up
heavy attack     (delayed)      freeze       + launch       anim plays    anim repeats  timer expires  anim plays    delay (hold   anim plays
     |               |             |             |              |             |              |             |          crash pose)       |
     v               v             v             v              v             v              v             v              v              v
[Combat.DoAttack] -> [ExecuteHitbox] -> [TakeHit] -> [Liftoff] -> [Loop...Loop...] -> [Crash] -> [Freeze pose] -> [GetUp anim] -> [AI resumes]
```

---

## Step 1: Player Presses Heavy Attack

**File:** `Combat.cs` -> `Update()`

The player presses the heavy attack button (K key, or right trigger on gamepad).
`Update()` detects the input and calls `DoAttack()` with the heavy attack data
from the ComboSet.

```csharp
// Combat.cs, line ~235
DoAttack(comboSet.heavyAttack, debug.heavyAttackColor);
```

**File:** `ComboSet.cs` -> `heavyAttack` field

The heavy attack is configured in the ComboSet asset with airborne properties:

```csharp
// ComboSet.cs, lines 87-103
public AttackData heavyAttack = new AttackData
{
    damage = 22,
    knockback = 10f,
    knockbackUp = 2f,          // Upward force for the launch
    hitstun = 0.4f,
    makesAirborne = true,      // THIS IS THE KEY FLAG
    airborneDuration = 0.6f,   // How long the enemy floats
    hitStopDuration = ...,
    // ... other fields
};
```

---

## Step 2: Attack Executes and Hitbox Fires

**File:** `Combat.cs` -> `DoAttack()`

`DoAttack()` sets up the attack state (cooldown, lock duration, animation, lunge)
and schedules the hitbox.

```csharp
// Combat.cs, line ~515-526
if (attack.hitboxDelay > 0f)
{
    hitboxPending = true;
    hitboxTriggerTime = Time.time + attack.hitboxDelay;
    pendingAttackData = attack;
}
else
{
    ExecuteHitbox(attack);
}
```

After `hitboxDelay` seconds, `UpdatePendingHitbox()` fires `ExecuteHitbox()`.

---

## Step 3: Hitbox Connects - Damage and Knockback

**File:** `Combat.cs` -> `ExecuteHitbox()`

The hitbox does a `Physics.OverlapSphere` to find colliders. For each hit target,
it calculates the knockback direction (away from attacker) and calls `TakeHit()`
on the enemy's `IDamageable` (which is `EnemyHealth`).

```csharp
// Combat.cs, lines 636-648
Vector3 horizontalDir = (targetTransform.position - transform.position);
horizontalDir.y = 0f;
horizontalDir.Normalize();

Vector3 knockbackVector = (horizontalDir * attack.knockback) + (Vector3.up * attack.knockbackUp);

float airborne = attack.makesAirborne ? attack.airborneDuration : 0f;
damageable.TakeHit(attack.damage, knockbackVector, attack.hitstun, airborne, attack.hitStopDuration);
```

The knockback vector has both a horizontal component (pushes enemy away) and a
vertical component (`knockbackUp = 2f` pushes them upward).

The `airborne` value is `0.6f` because `makesAirborne` is true.

---

## Step 4: EnemyHealth Receives the Hit

**File:** `EnemyHealth.cs` -> `TakeHit()`

`TakeHit()` processes the hit in steps:

### 4a. Subtract HP

```csharp
// EnemyHealth.cs, line 321
hp -= damage;
```

### 4b. Store knockback for delayed application

Since this is an airborne attack (`airborneDuration > 0`), the knockback is NOT
applied immediately. It's stored as pending so the enemy plays the hit reaction
first, then "cuts to midair" when hit stop ends.

```csharp
// EnemyHealth.cs, lines 327-331
if (airborneDuration > 0f)
{
    pendingKnockback = knockback;
    pendingAirborneDuration = airborneDuration;
}
```

### 4c. Set hit stun timer

```csharp
// EnemyHealth.cs, line 352
stunUntil = Mathf.Max(stunUntil, Time.time + hitstun);
```

While `Time.time < stunUntil`, `IsStunned` returns true. `SimpleEnemyAI.HandleMovement()`
checks this and skips ALL behavior execution (chase, standoff, attack) while stunned.

### 4d. Schedule the launch for when hit stop ends

```csharp
// EnemyHealth.cs, lines 353-354
if (airborneDuration > 0f)
    pendingLaunchApplyTime = (hitStopDuration > 0f)
        ? (Time.time + hitStopDuration) : Time.time;
```

### 4e. Trigger hit animation

```csharp
// EnemyHealth.cs, lines 368-371
if (enemyAI != null)
{
    enemyAI.TriggerHitAnimation(hitstun);
}
```

**File:** `SimpleEnemyAI.cs` -> `TriggerHitAnimation()`

This force-plays a hit reaction animation on the Stun layer, speed-scaled to
match the hitstun duration:

```csharp
// SimpleEnemyAI.cs, lines 571-597
float speedMultiplier = baseHitAnimDuration / Mathf.Max(hitstun, 0.01f);
animator.SetFloat(hitSpeedParameter, speedMultiplier);
// ... picks a random hit state from hitStateNames[] ...
animator.Play(stateToPlay, hitAnimationLayer, 0f);
animator.Update(0f);
```

---

## Step 5: Hit Stop Freeze

**File:** `Combat.cs` -> `ExecuteHitbox()` (continued)

Both the attacker's and target's animators are frozen for `hitStopDuration`:

```csharp
// Combat.cs, lines 657-665
Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, ... });
targetAnim.speed = 0f;
```

This creates the classic fighting game "impact freeze" moment. Animations pause
but game timers keep running.

After `hitStopDuration`, `UpdateHitStop()` unfreezes all animators:

```csharp
// Combat.cs, lines 713-725
if (frozenAnimators.Count > 0 && Time.time >= hitStopEndTime)
{
    foreach (var frozen in frozenAnimators)
        frozen.animator.speed = frozen.originalSpeed;
    frozenAnimators.Clear();
}
```

---

## Step 6: Delayed Launch Applies (Enemy Goes Airborne)

**File:** `EnemyHealth.cs` -> `ApplyKnockback()` (runs every frame in `Update`)

When the hit stop ends and `Time.time >= pendingLaunchApplyTime`, the stored
knockback and airborne duration are finally applied:

```csharp
// EnemyHealth.cs, lines 193-198
if (pendingLaunchApplyTime > 0f && Time.time >= pendingLaunchApplyTime)
{
    kbVel += pendingKnockback;
    airborneUntil = Mathf.Max(airborneUntil, Time.time + pendingAirborneDuration);
    pendingLaunchApplyTime = 0f;
}
```

Now:
- `kbVel` has both horizontal and upward velocity -> enemy physically moves
- `IsAirborne` returns `true` (for `airborneDuration` seconds)

**File:** `SimpleEnemyAI.cs` -> `ApplyGravity()`

While `IsAirborne` is true, gravity is reduced to 30% so the enemy floats:

```csharp
// SimpleEnemyAI.cs, lines 749-770
bool isAirborne = health != null && health.IsAirborne;
float gravityMultiplier = isAirborne ? 0.3f : 1f;
velocity.y += gravity * gravityMultiplier * Time.deltaTime;
```

The knockback velocity in `EnemyHealth.ApplyKnockback()` handles the actual
upward/horizontal movement each frame, decaying over time.

---

## Step 7: Airborne Animation - Liftoff Phase

**File:** `SimpleEnemyAI.cs` -> `UpdateAirborneAnimation()`

This runs every frame. It detects the **rising edge** of `IsAirborne` (was false
last frame, is true this frame) and starts the Liftoff phase:

```csharp
// SimpleEnemyAI.cs, lines 370-388
if (isAirborne && !wasAirborne)
{
    airbornePhase = AirbornePhase.Liftoff;

    if (airborneAnimation.crossfadeDuration > 0f)
    {
        animator.CrossFadeInFixedTime(
            airborneAnimation.airborneStateName,
            airborneAnimation.crossfadeDuration,
            airborneAnimation.airborneAnimationLayer,
            airborneAnimation.liftoffStart);    // e.g. 0.0
    }
    else
    {
        animator.Play(
            airborneAnimation.airborneStateName,
            airborneAnimation.airborneAnimationLayer,
            airborneAnimation.liftoffStart);
    }
}
```

**File:** `AirborneAnimationSettings.cs`

The animation clip is divided into three portions using normalized time (0-1):

```
|---- Liftoff ----|-------- Loop --------|-------- Crash --------|
0.0          liftoffEnd            loopEnd/crashStart            1.0
              (0.2)                    (0.6)
```

The Liftoff portion (0.0 to 0.2) plays the initial hit reaction and launch-up
part of the animation. It plays once, not looped.

---

## Step 8: Airborne Animation - Loop Phase

**File:** `SimpleEnemyAI.cs` -> `UpdateAirborneAnimation()` (per-frame switch)

Each frame, the method checks `normalizedTime` of the animator state. When it
reaches `liftoffEnd`, it transitions to the Loop phase:

```csharp
// SimpleEnemyAI.cs, lines 425-437
case AirbornePhase.Liftoff:
    if (normalizedTime >= airborneAnimation.liftoffEnd)
    {
        airbornePhase = AirbornePhase.Loop;
        animator.Play(
            airborneAnimation.airborneStateName,
            airborneAnimation.airborneAnimationLayer,
            airborneAnimation.loopStart);    // e.g. 0.2
    }
    break;
```

During the Loop phase, the animation plays between `loopStart` and `loopEnd`.
When it reaches `loopEnd`, it jumps back to `loopStart`, creating a seamless
spinning cycle:

```csharp
// SimpleEnemyAI.cs, lines 440-456
case AirbornePhase.Loop:
    if (normalizedTime >= airborneAnimation.loopEnd)
    {
        animator.Play(
            airborneAnimation.airborneStateName,
            airborneAnimation.airborneAnimationLayer,
            airborneAnimation.loopStart);   // Jump back -> seamless spin
    }
    break;
```

This repeats indefinitely until `IsAirborne` becomes false.

---

## Step 9: Airborne Timer Expires -> Crash Phase

**File:** `EnemyHealth.cs`

When `Time.time >= airborneUntil`, the property `IsAirborne` returns false.
Gravity returns to 100% in `SimpleEnemyAI.ApplyGravity()`.

**File:** `SimpleEnemyAI.cs` -> `UpdateAirborneAnimation()`

The method detects the **falling edge** of `IsAirborne` (was true, now false)
and starts the Crash phase:

```csharp
// SimpleEnemyAI.cs, lines 398-405
if (!isAirborne && wasAirborne && airbornePhase != AirbornePhase.None)
{
    airbornePhase = AirbornePhase.Crash;
    animator.Play(
        airborneAnimation.airborneStateName,
        airborneAnimation.airborneAnimationLayer,
        airborneAnimation.crashStart);     // e.g. 0.6
}
```

### Critical: Keeping the Animator Bool True During Crash

**File:** `SimpleEnemyAI.cs` -> `UpdateAnimator()`

Even though `health.IsAirborne` is now false, the Animator bool parameter must
stay true during the Crash phase. Otherwise the Animator Controller would
transition away from the airborne state before crash finishes.

```csharp
// SimpleEnemyAI.cs, lines 538-540
bool airborneForAnimator = isAirborne
    || (airborneAnimation.IsConfigured && airbornePhase != AirbornePhase.None);
animator.SetBool(airborneParameter, airborneForAnimator);
```

The bool only goes false once `airbornePhase` returns to `None` (after crash
finishes).

---

## Step 10: Crash Finishes -> Get-Up Sequence Starts

**File:** `SimpleEnemyAI.cs` -> `UpdateAirborneAnimation()` (Crash case)

When the animator's `normalizedTime` reaches `crashEnd`, the crash is done:

```csharp
// SimpleEnemyAI.cs, lines 458-470
case AirbornePhase.Crash:
    if (normalizedTime >= airborneAnimation.crashEnd)
    {
        airbornePhase = AirbornePhase.None;
        if (health != null)
            health.StartGetUp(getUpDelay, getUpDuration);
    }
    break;
```

**File:** `EnemyHealth.cs` -> `StartGetUp()`

This begins the get-up sequence. It has two sub-phases:

1. **Delay phase**: The enemy holds the crash landing pose (animator frozen on
   last frame) for `getUpDelay` seconds.
2. **Get-up animation**: Plays the get-up animation for `getUpDuration` seconds.

```csharp
// EnemyHealth.cs, lines 161-168
public void StartGetUp(float delay, float duration)
{
    getUpDurationThisRun = duration;
    if (delay > 0f && enemyAI != null)
        enemyAI.FreezeAnimatorForGetUpDelay();  // Freeze on crash pose
    getUpUntil = Time.time + delay + duration;  // Total stun time
    getUpAnimationStartTime = delay > 0f
        ? Time.time + delay : Time.time;
}
```

While `IsGettingUp` is true, the AI cannot act:

```csharp
// EnemyHealth.cs, line 284
public bool IsGettingUp => getUpUntil > 0f && Time.time < getUpUntil;

// SimpleEnemyAI.cs, line 672
if (health != null && (health.IsStunned || health.IsGettingUp)) return;
```

---

## Step 11: Get-Up Animation Plays

**File:** `EnemyHealth.cs` -> `UpdateGetUpOnCrash()`

This runs every frame. When the delay expires, it triggers the get-up animation:

```csharp
// EnemyHealth.cs, lines 147-152
if (getUpAnimationStartTime > 0f && Time.time >= getUpAnimationStartTime)
{
    getUpAnimationStartTime = 0f;
    if (enemyAI != null)
        enemyAI.TriggerGetUpAnimation(getUpDurationThisRun);
}
```

**File:** `SimpleEnemyAI.cs` -> `TriggerGetUpAnimation()`

Unfreezes the animator (was frozen on crash pose) and plays the get-up state,
speed-scaled to match `getUpDuration`:

```csharp
// SimpleEnemyAI.cs, lines 618-628
public void TriggerGetUpAnimation(float duration)
{
    if (animator == null || string.IsNullOrEmpty(getUpStateName)) return;
    animator.speed = animatorSpeedBeforeGetUpFreeze;   // Unfreeze
    if (baseGetUpAnimDuration > 0f && duration > 0f)
    {
        float speedMultiplier = baseGetUpAnimDuration / duration;
        animator.SetFloat(hitSpeedParameter, speedMultiplier);
    }
    animator.Play(getUpStateName, getUpLayer, 0f);
}
```

---

## Step 12: Get-Up Timer Expires -> AI Resumes

**File:** `EnemyHealth.cs` -> `UpdateGetUpOnCrash()`

When `Time.time >= getUpUntil`, the get-up stun clears:

```csharp
// EnemyHealth.cs, lines 142-146
if (getUpUntil > 0f && Time.time >= getUpUntil)
{
    getUpUntil = 0f;
    getUpAnimationStartTime = 0f;
}
```

Now `IsGettingUp` returns false, `IsStunned` is already false (expired earlier),
so `SimpleEnemyAI.HandleMovement()` proceeds past the stun check and the current
behavior (Chase or Standoff) executes normally again.

---

## Complete File Responsibility Map

| Step | What Happens | File | Method |
|------|-------------|------|--------|
| 1 | Player presses attack | `Combat.cs` | `Update()` -> `DoAttack()` |
| 2 | Hitbox fires after delay | `Combat.cs` | `UpdatePendingHitbox()` -> `ExecuteHitbox()` |
| 3 | Damage + knockback sent | `Combat.cs` | `ExecuteHitbox()` -> `damageable.TakeHit()` |
| 4 | HP subtracted, stun set | `EnemyHealth.cs` | `TakeHit()` |
| 4 | Hit anim triggered | `SimpleEnemyAI.cs` | `TriggerHitAnimation()` |
| 5 | Animators frozen | `Combat.cs` | `ExecuteHitbox()` (hit stop) |
| 5 | Animators unfrozen | `Combat.cs` | `UpdateHitStop()` |
| 6 | Knockback + airborne applied | `EnemyHealth.cs` | `ApplyKnockback()` (pending launch) |
| 6 | Gravity reduced to 30% | `SimpleEnemyAI.cs` | `ApplyGravity()` |
| 7 | Liftoff animation starts | `SimpleEnemyAI.cs` | `UpdateAirborneAnimation()` |
| 8 | Loop animation repeats | `SimpleEnemyAI.cs` | `UpdateAirborneAnimation()` |
| 9 | Airborne expires, crash starts | `SimpleEnemyAI.cs` | `UpdateAirborneAnimation()` |
| 9 | Animator bool kept true | `SimpleEnemyAI.cs` | `UpdateAnimator()` |
| 10 | Crash ends, get-up starts | `EnemyHealth.cs` | `StartGetUp()` |
| 10 | Animator frozen on crash pose | `SimpleEnemyAI.cs` | `FreezeAnimatorForGetUpDelay()` |
| 11 | Get-up anim plays | `SimpleEnemyAI.cs` | `TriggerGetUpAnimation()` |
| 12 | Get-up expires, AI resumes | `EnemyHealth.cs` | `UpdateGetUpOnCrash()` |

---

## Inspector Settings That Control This Flow

### On the AttackData (ComboSet asset):
- `makesAirborne` - enables the airborne launch
- `airborneDuration` - how long the enemy floats (loop duration)
- `knockbackUp` - upward launch force
- `hitstun` - how long until the enemy can be hit again
- `hitStopDuration` - impact freeze duration

### On SimpleEnemyAI (enemy GameObject):
- `airborneAnimation.airborneStateName` - Animator state with the full clip
- `airborneAnimation.liftoffStart/End` - where the launch portion is in the clip
- `airborneAnimation.loopStart/End` - where the spinning portion is in the clip
- `airborneAnimation.crashStart/End` - where the landing portion is in the clip
- `getUpDelay` - pause on crash pose before getting up
- `getUpDuration` - how long the get-up animation + stun lasts
- `getUpStateName` - Animator state for the get-up animation

### On EnemyHealth (enemy GameObject):
- `knockbackFriction` - how fast the knockback velocity decays
