# Throw system: position and orientation reference

This document explains the different “position-like” and direction values in Unity, in the context of the throw code where `vt = (currentThrowVictim as Component)?.transform` (the victim’s root transform).

---

## Direction vs rotation (short version)

- **In the Editor:** The **colored arrows** on the transform gizmo (when you select an object) are the three directions from the object’s rotation: red = right, green = up, blue = forward. The **colored circles** are the rotation controls — you rotate *around* those axes. The arrows show the result; the circles let you change it.

- **Direction** = “which way” — a vector with no location. Example: “forward” is the direction something is facing, e.g. `(0, 0, 1)` = toward world Z. The vector’s x, y, z say how much you go along each world axis; it’s not a position.
- **Rotation** = how the object is turned in 3D (pitch, yaw, roll), stored as a Quaternion. Rotation doesn’t “have” a direction; it **defines** the object’s orientation. From that, Unity derives three directions: **forward**, **right**, and **up** (e.g. `transform.forward`). So: rotation
- **Why "forward", "right", "up"?** They're not random — they're the usual way to describe orientation for a **character or camera**. **Forward** = the way you're facing (where you look or move). **Right** = your right side (90° from forward). **Up** = the top of your head / the sky. So the three names form a logical set: "which way I'm facing," "which way is to my right," and "which way is up." That's easier to think about than "axis 1, 2, 3" when you're moving a character or placing things in front of them. “which way am I turned”; direction = “one of the axes that come from that rotation.”

---

## 1. `transform.position` (world position)

- **What it is:** The transform’s position in **world space** (relative to the world origin `(0,0,0)`).
- **Type:** `Vector3` (x, y, z in metres).
- **Meaning of x, y, z:**
  - **x** = left/right (world X axis).
  - **y** = up/down (world Y axis).
  - **z** = forward/back (world Z axis).
- **How it’s computed when there’s a parent:** Unity takes your **localPosition** (your offset from the parent). That offset is in the parent’s axes (e.g. “1 unit in the parent’s forward direction”). Unity then uses the parent’s **rotation** to turn that offset into world-space, applies the parent’s **scale**, and adds the parent’s **world position**. So your world position isn’t just “parent position + local position” — the parent’s rotation and scale decide where that local offset actually lands in the world. (No parent → `position` is just your position in the scene.)
- **In throw code:** We read `vt.position` as the victim root’s world position. When the victim is parented to `grabSocket`, `vt.position` is the same as the socket’s world position (plus any local offset). We also set `vt.position = bakePosition` to place the victim in the world after release.

---

## 2. `transform.localPosition` (local position)

- **What it is:** The transform’s position relative to its **parent**. If the parent is null, this equals `position`.
- **Type:** `Vector3` (x, y, z).
- **Meaning of x, y, z:** Same axes as world, but **in the parent’s local space** (parent’s right = local X, parent’s up = local Y, parent’s forward = local Z).
- **In throw code:** When the victim is parented to `grabSocket`, we set `victimTransform.localPosition = Vector3.zero` so the victim root sits exactly on the socket. After release we set `victimAnim.transform.localPosition = Vector3.zero` (and localRotation to identity) so the mesh child matches the root in world space before we unparent.

---

## 3. `transform.rotation` (world rotation)

- **What it is:** The transform’s rotation in **world space** (how the object is oriented in the world).
- **Type:** `Quaternion`. Often inspected in the Editor as Euler angles (degrees): **x** = pitch (tilt up/down), **y** = yaw (turn left/right), **z** = roll (lean).
- **In throw code:** We set `vt.rotation = standingRotation` so the victim ends upright (yaw only, no tilt).

---

## 4. `transform.localRotation` (local rotation)

- **What it is:** Rotation relative to the **parent**. If parent is null, equals `rotation`.
- **Type:** `Quaternion`.
- **In throw code:** We zero the victim mesh’s `localRotation` to identity before unparenting so the mesh’s world pose matches the root’s world pose.

---

## 5. `transform.forward` / `right` / `up` (direction vectors)

- **What they are:** Unit vectors (length 1) in **world space** that point in the transform’s “forward”, “right”, and “up” directions. They are derived from `rotation`; they are **not** positions.
- **Type:** `Vector3`.
- **Meaning of their x, y, z:** The components are the vector’s projection onto world X, Y, Z. For example, if the object faces world +Z, `forward` might be `(0, 0, 1)`; if it faces 45° right and up, `forward` has non-zero x, y, z.
- **forward** = “blue” axis in Editor (where the object is facing).  
  **right** = “red” axis.  
  **up** = “green” axis.
- **In throw code:** We use `transform.forward` (and similar) elsewhere for attack direction; for the victim we mainly use position/rotation, not these vectors directly.

---

## 6. `transform.localScale`

- **What it is:** Scale relative to the parent (1,1,1 = same size as parent). Affects how `localPosition` is interpreted when converting to world position.
- **In throw code:** We preserve the victim’s world scale when parenting to the socket by setting `localScale` so that `lossyScale` (world scale) stays the same.

---

## 7. `transform.lossyScale` (world scale)

- **What it is:** Effective scale in **world space**. Read-only. Combines this transform’s and all ancestors’ scales.
- **In throw code:** We read `worldScaleBefore = victimTransform.lossyScale` before parenting, then set `localScale` so that after parenting to the socket, the victim’s world size doesn’t change.

---

## 8. `Animator.bodyPosition` and `Animator.bodyRotation`

- **What they are:** The Animator’s internal “body” position and rotation in **world space**, used for root motion. They may not match `transform.position`/`rotation` exactly in the same frame (timing / application order).
- **bodyPosition:** `Vector3` (world x, y, z).
- **bodyRotation:** `Quaternion` (world orientation).
- **In throw code:** We use `victimAnim.bodyPosition` and `victimAnim.bodyRotation` to bake where the victim’s body ended up after root motion, then we set `vt.position` and `vt.rotation` (standing) from that.

---

## 9. Summary table

| Concept              | Space    | Meaning                                      |
|----------------------|----------|----------------------------------------------|
| `position`           | World    | Where the transform is in the scene          |
| `localPosition`      | Parent   | Offset from the parent’s origin              |
| `rotation`           | World    | How the transform is oriented in the world   |
| `localRotation`      | Parent   | Orientation relative to parent               |
| `forward` / `right` / `up` | World | Direction vectors (from rotation), not position |
| `localScale`         | Parent   | Scale relative to parent                     |
| `lossyScale`         | World    | Effective world scale (read-only)             |
| `Animator.bodyPosition` | World | Animator’s root-motion body position       |
| `Animator.bodyRotation` | World | Animator’s root-motion body rotation       |

---

## 10. In the throw flow (with `vt` = victim root)

- **Grab:** `vt` is parented to `grabSocket`, `vt.localPosition = Vector3.zero` → `vt.position` equals the socket’s world position.
- **Hold:** Root motion may move the **mesh** (Animator’s transform) in world space; `vt.position` can stay at the socket if the Animator is on a child.
- **Release:** We read `bodyPosition` / `bodyRotation` (or root pose), build `standingRotation` (yaw only), set `vt.position = bakePosition`, `vt.rotation = standingRotation`, zero the mesh’s local position/rotation, then unparent so the victim stays where we baked them, upright.
