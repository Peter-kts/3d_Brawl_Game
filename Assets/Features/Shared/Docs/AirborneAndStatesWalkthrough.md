# Airborne Mechanic and State Flow: Step-by-Step

This doc walks through how the airborne mechanic works and how it ties into **stun**, **crash**, **get-up**, and **normal**. It also explains **how the enemy decides which state to be in**.

---

## 1. The six “logical” states

From the game’s point of view, the enemy is in exactly one of these at any time (checked in this order):

| Priority | State      | Meaning |
|----------|------------|--------|
| 1        | **Dying**  | HP ≤ 0, death animation (or airborne-as-death) playing. |
| 2        | **Airborne** | Launched by an attack; floating in the air, can be juggled. |
| 3        | **Crashed**  | Just landed from airborne; playing crash/land animation, can’t act. |
| 4        | **Stunned**  | Hitstun from a non-launch hit; playing hit reaction, can’t act. |
| 5        | **GettingUp**| Crash finished; playing get-up animation (or holding crash pose during delay), can’t act. |
| 6        | **Normal**   | None of the above; AI runs (chase, standoff, attack). |

That order is the **decision order**: e.g. if both `IsStunned` and `IsCrashed` were true, we’d treat the enemy as **Crashed** because Crashed is checked first in the “recovery from hit” chain. In practice the timers are set so only one of these is active at a time.

---

## 2. Where each state comes from (EnemyHealth timers)

All of these are driven by **time** in `EnemyHealth`:

- **IsDying**  
  Set when `hp <= 0` in `TakeHit()`. Cleared only when the death/airborne sequence is finished (e.g. `CompleteDeath()` or `OnAirborneSequenceComplete()`).

- **IsAirborne**  
  `Time.time < airborneUntil`.  
  `airborneUntil` is set in **ApplyKnockback()** when a **pending launch** is applied (see below). So “airborne” starts when **hitstop** ends and we actually add the launch velocity. Launch is timed from hitstop (not hitstun), so airborne can start while still in hitstun; Airborne takes priority over Stunned.

- **IsStunned**  
  `Time.time < stunUntil`.  
  Set in `TakeHit()` from the attack’s `hitstun`. Covers the initial hit reaction. Launch is applied after hitstop, so airborne can begin before hitstun ends; we prioritize Airborne when `IsAirborne` is true.

- **IsCrashed**  
  `Time.time < crashUntil`.  
  Set when we **leave airborne** and start the “crash” segment:  
  - **PATH A (simple crash):** `SimpleEnemyAI` calls `health.StartCrashPhase(crashStateDuration)` when `IsAirborne` goes false.  
  - **PATH B (Liftoff/Loop/Crash):** crash is driven by animation phase; `crashUntil` is not used for PATH B. So **IsCrashed** is only true in PATH A during the crash state.

- **IsGettingUp**  
  `getUpUntil > 0 && Time.time < getUpUntil`.  
  Set when the crash phase **ends** and we start the get-up sequence: `StartGetUp(delay, duration)` (called from `SimpleEnemyAI` when crash finishes). Covers both the optional “hold crash pose” delay and the get-up animation.

So: **EnemyHealth** owns the **timers**; **SimpleEnemyAI** (and the animator) decide **when** to start crash and get-up and what to play.

---

## 3. Step-by-step: non-airborne hit (e.g. light attack)

1. **TakeHit(damage, knockback, hitstun, airborneDuration: 0)**
   - HP reduced, `stunUntil = Time.time + hitstun`, knockback added to `kbVel`, hitstop optional.
   - No pending launch; `airborneUntil` and `crashUntil` stay 0.

2. **Hit reaction**
   - `TriggerHitAnimation(hitstun)` plays the hit state on the Stun layer; animator **Stunned** (or equivalent) is true.
   - **IsStunned** is true; **IsAirborne**, **IsCrashed**, **IsGettingUp** false.
   - AI is blocked in `HandleMovement()` by `health.IsStunned`.

3. **Stun ends**
   - When `Time.time >= stunUntil`, **IsStunned** becomes false.
   - No crash or get-up; enemy goes straight back to **Normal** and behavior runs again.

So: **Normal → Stunned → Normal**. No airborne, no crash, no get-up.

---

## 4. Step-by-step: airborne hit (e.g. heavy / launcher)

Assume **TakeHit(damage, knockback, hitstun, airborneDuration > 0)**.

### Phase A: Hit and hitstun (still on the ground)

1. **TakeHit**
   - HP reduced.
   - **Knockback:** Not applied yet. We store `pendingKnockback`, `pendingAirborneDuration`, and set  
     `pendingLaunchApplyTime = Time.time + hitStopDuration` (or `Time.time` if no hitstop). So **launch is delayed until after hitstop**.
   - **Stun:** `stunUntil = Time.time + hitstun`.
   - **Hit animation:** `TriggerHitAnimation(hitstun)` — enemy plays hit reaction.
   - **IsStunned** true, **IsAirborne** false (launch not applied yet).

2. **Each frame in EnemyHealth.Update()**
   - **ApplyKnockback()** runs. While `Time.time < pendingLaunchApplyTime`, it doesn’t apply the pending launch; it may apply hitstop (freeze position).
   - When `Time.time >= pendingLaunchApplyTime`:
     - `kbVel += pendingKnockback`
     - `airborneUntil = Time.time + pendingAirborneDuration`
     - Pending cleared.
   - So **IsAirborne** becomes true **when hitstop ends**; hitstun may still be running, but Airborne takes priority and we “cut to midair” with the stored velocity.

So the order is: **hit reaction on ground (stunned)** → **launch applied** → **airborne**.

### Phase B: Airborne (in the air)

3. **IsAirborne is true**
   - **EnemyHealth:** `airborneUntil` is in the future; **IsAirborne** is true. No crash yet: **IsCrashed** false, **IsGettingUp** false.
   - **SimpleEnemyAI.ApplyGravity():** Uses reduced gravity (e.g. 0.3x) so the enemy floats.
   - **SimpleEnemyAI** drives the **Animator**:
     - **PATH B (Liftoff/Loop/Crash):**  
       On **rising edge** of `IsAirborne`, `UpdateAirborneAnimation()` sets `airbornePhase = Liftoff` and plays the airborne clip from `liftoffStart`. Then it advances: Liftoff → Loop (loop while airborne) → when `IsAirborne` goes false, phase becomes **Crash** and it plays from `crashStart` to `crashEnd`.
     - **PATH A (simple crash):**  
       Animator’s **IsAirborne** bool is set from `health.IsAirborne`; the Animator Controller transitions to the airborne state. When airborne ends, SimpleEnemyAI will force-play the crash state and call `StartCrashPhase` (see below).
   - **HandleMovement()** doesn’t run normal behavior: either `health.IsDying` is true (we’re dead) or we’re still in the airborne animation. For PATH B, **InAirborneCrash** is true during the Crash phase; for PATH A, **IsCrashed** becomes true after we call `StartCrashPhase`. So the enemy doesn’t “decide” to chase/attack while airborne; they’re blocked.

So during this time the **state** is **Airborne** (and the animator shows liftoff/loop, or the simple airborne state).

### Phase C: Airborne ends → Crash

4. **airborneUntil expires**
   - **EnemyHealth:** `Time.time >= airborneUntil` → **IsAirborne** becomes false.
   - **SimpleEnemyAI** sees the **falling edge** of `IsAirborne`:
     - **PATH B:** Already in `UpdateAirborneAnimation()`: phase is set to **Crash**, and the same clip is played from `crashStart`. When `normalizedTime >= crashEnd`, we call `OnAirborneCrashFinished(health.IsDying)` (get-up or complete death).
     - **PATH A:** In `UpdateAnimator()`, we force-play the crash state on the Stun layer and call `health.StartCrashPhase(crashStateDuration)`. So **IsCrashed** becomes true for the next `crashStateDuration` seconds. When **crashUntil** expires, **EnemyHealth** calls `enemyAI.OnCrashPhaseComplete(IsDying)`, which calls `OnAirborneCrashFinished` (same as PATH B).

So: **Airborne** → **Crashed** (playing crash/land animation, can’t act). The “decision” is: we’re not airborne anymore, so we’re in crash until the crash duration (PATH A) or the crash portion of the clip (PATH B) finishes.

### Phase D: Crash ends → Get-up

5. **Crash phase ends**
   - **PATH B:** When the Crash phase reaches `crashEnd`, we call `OnAirborneCrashFinished(isDying)`. If not dying, that calls `health.StartGetUp(getUpDelay, getUpDuration)`.
   - **PATH A:** When `crashUntil` expires, `UpdateGetUpOnCrash()` calls `enemyAI.OnCrashPhaseComplete(IsDying)`; that calls `OnAirborneCrashFinished`, which calls `StartGetUp(...)` if not dying.
   - **StartGetUp(delay, duration):**
     - Clears crash: `crashUntil = 0` → **IsCrashed** false.
     - Sets `getUpUntil = Time.time + delay + duration` and (if delay > 0) schedules the get-up animation at `Time.time + delay`. Optionally freezes the animator for the delay so we hold the crash pose.
   - So we’re now **GettingUp**: **IsGettingUp** true, **IsCrashed** false.

So: **Crashed** → **GettingUp**.

### Phase E: Get-up ends → Normal

6. **getUpUntil expires**
   - **EnemyHealth:** When `Time.time >= getUpUntil`, we clear `getUpUntil` and `getUpAnimationStartTime`. **IsGettingUp** becomes false.
   - No other timers are set; the enemy is **Normal** again and `HandleMovement()` runs behavior (chase, standoff, attack).

So: **GettingUp** → **Normal**.

---

## 5. How the enemy “decides” which state to go into

The enemy doesn’t choose between states with a single state machine. The **state** is determined by **which timer (or phase) is active**, in a fixed priority:

1. **Dying**  
   If `hp <= 0` and we haven’t finished the death/airborne sequence, we’re Dying.

2. **Airborne**  
   If `Time.time < airborneUntil`, we’re Airborne. That’s set only when a launch is applied in `ApplyKnockback()` after **hitstop** ends.

3. **Crashed**  
   If `Time.time < crashUntil` (PATH A) or we’re in `airbornePhase == Crash` (PATH B), we’re in the crash state. So we “go into” Crashed when:
   - We were Airborne and **airborneUntil** expired (airborne ends), and  
   - SimpleEnemyAI started the crash (PATH A: `StartCrashPhase`; PATH B: phase = Crash).  
   We **leave** Crashed when the crash duration or crash animation finishes and we call **StartGetUp**.

4. **Stunned**  
   If `Time.time < stunUntil` and we’re not in the airborne/crash/get-up chain above, we’re Stunned. So we “go into” Stunned on any hit (from `TakeHit`). We leave when `stunUntil` expires. For a non-airborne hit, that’s straight back to Normal.

5. **GettingUp**  
   If `getUpUntil > 0` and `Time.time < getUpUntil`, we’re GettingUp. We “go into” it when **crash ends** and `StartGetUp` is called (from either PATH A or PATH B). We leave when `getUpUntil` expires.

6. **Normal**  
   If none of the above, we’re Normal. So we “decide” to be Normal only by **not** being in any of the other states.

The **animator** is driven to match this:
- **Stun layer:** Stunned = hit reaction; IsAirborne = airborne state (or Liftoff/Loop/Crash); crash state = crash/land; get-up state = get-up. SimpleEnemyAI sets `stunParameter`, `airborneParameter`, and plays the right state so the **visual** state matches the **logical** state (Dying / Airborne / Crashed / Stunned / GettingUp / Normal).

---

## 6. Flow diagram (airborne hit)

```
  [Normal]
      |
  TakeHit(airborneDuration > 0)
      |
  Stunned (hit reaction, stunUntil)
      |
  pendingLaunchApplyTime reached → kbVel += pendingKnockback, airborneUntil set
      |
  Airborne (liftoff → loop → airborneUntil expires)
      |
  Crashed (crash anim / crashUntil)
      |
  OnCrashPhaseComplete / OnAirborneCrashFinished → StartGetUp
      |
  GettingUp (delay + get-up anim, getUpUntil)
      |
  getUpUntil expires
      |
  [Normal]
```

If the attack **kills** the enemy (hp ≤ 0), we still run the airborne→crash sequence; when it finishes, we call `OnAirborneSequenceComplete()` instead of `StartGetUp`, and the enemy completes death (no get-up, no return to Normal).

---

## 7. Summary table: who sets what

| State      | Set by / when |
|-----------|----------------|
| Dying     | `TakeHit()` when `hp <= 0`; cleared when death/airborne sequence completes. |
| Airborne  | `ApplyKnockback()` when applying pending launch: `airborneUntil = Time.time + pendingAirborneDuration`. |
| Stunned   | `TakeHit()`: `stunUntil = Time.time + hitstun`. |
| Crashed   | **PATH A:** `StartCrashPhase(duration)` from SimpleEnemyAI when IsAirborne goes false. **PATH B:** `airbornePhase = Crash` in UpdateAirborneAnimation; no crashUntil. |
| GettingUp | `StartGetUp(delay, duration)` from SimpleEnemyAI when crash phase ends (PATH A: when crashUntil expires; PATH B: when Crash phase reaches crashEnd). |
| Normal    | When none of the above timers/phases are active. |

The **enemy** doesn’t “decide” in the sense of choosing a state; the **attack** (hitstun, airborne duration) and **EnemyHealth** timers plus **SimpleEnemyAI** (crash/get-up triggers) define the sequence. The “decision” is just **evaluating the timers and phases in a fixed order** (Dying > Airborne > Crashed > Stunned > GettingUp > Normal). This is centralized in **SimpleEnemyAI.CurrentState** (and **CanAct** for "can run behavior?"); `EnemyStateDebugVisual` and `HandleMovement()` use those instead of reimplementing the priority.
