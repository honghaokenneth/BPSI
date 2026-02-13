# BPSI Unity Scripts (CInputer + CMovement)

This package provides **two Unity C# scripts** for a BPSI-style interaction loop:

- **`CInputer`**: routes mouse/touch input.
- **`CMovement`**: handles *Alive* micro-motion, *Move* navigation, **TurnLeft/TurnRight auto-switch**, and **inactivity auto-reset** (return along the recorded path).

> Target scenario: Qi Baishi ink shrimp reanimation — preserve brushstroke integrity in the **rest state**, and only “animate” when interaction occurs.  
> When no interaction happens, the shrimp returns to the exact original pose, so the final view matches the original painting.

---

## 1) Folder structure

```
Assets/
  Scripts/
    BPSI/
      CInputer.cs
      CMovement.cs
README.md
```

---

## 2) Unity version assumptions

- Works in typical **Unity 2020 LTS+** projects (2D or 3D).
- Uses standard Unity API (`FindObjectsOfType`, `Physics2D.OverlapPoint`, `Physics.Raycast`).

---

## 3) Minimal scene setup (recommended 2D workflow)

### Step A — Background painting
1. Import the original painting image (background).
2. Place it as a sprite in the scene (e.g., `Background`).

### Step B — Shrimp GameObjects (the “brushstroke units”)
1. For each shrimp (or shrimp module), create a **root GameObject**:
   - `Shrimp_01`, `Shrimp_02`, ...
2. Add:
   - **SpriteRenderer** (your extracted shrimp sprite)
   - **Collider2D** (PolygonCollider2D / BoxCollider2D; Polygon works best for irregular outlines)
   - **CMovement** component
3. Tag each shrimp root as **`Shrimp`** (default; configurable in `CInputer`).

> If your shrimp is composed of multiple sprites/parts, put colliders on the parts and keep `CMovement` on the root.  
> `CInputer` uses `GetComponentInParent<CMovement>()`.

### Step C — Input router
1. Create an empty object: `BPSI_Input`.
2. Add **CInputer**.
3. Assign:
   - `Target Camera` (or leave empty to use `Camera.main`)
   - `Shrimp Agents` list (optional; you can enable auto-find)

---

## 4) Interaction logic (what you get)

### ✅ Click shrimp body → Alive micro-motion
- Calls `CMovement.TriggerAlive()`
- Adds small rotation/pulse **without changing position**

### ✅ Click blank area → Move 1–2 random shrimps
- Calls `CMovement.MoveTo(targetPoint)`
- During movement, the script determines direction sign and fires:
  - `TurnLeft` when moving mainly left
  - `TurnRight` when moving mainly right

### ✅ No input for 3 seconds → Auto reset (return)
- Each shrimp has an inactivity watchdog.
- After `inactivityReturnSeconds`, it calls `ReturnToOrigin()`:
  - Returns along the **recorded path** in reverse.
  - Snaps to the **exact original pose** at the end.

---

## 5) Animator integration (optional but supported)

If you have an Animator state machine for shrimp behavior, you can use triggers:

- `Alive`
- `Move`
- `TurnLeft`
- `TurnRight`
- `Return`
- `Idle`

These are exposed as inspector strings in `CMovement`, so you can rename them to match your Animator parameters.

> If you do **not** attach an Animator, scripts still work (movement + reset), just without state-driven animation clips.

---

## 6) Common tuning tips (art-focused)

- **Keep `moveSpeed` low** and rely on subtle deformation rather than fast translation.
- Use a small `aliveRotateAmplitudeDeg` (1–3 degrees) to avoid breaking ink rhythm.
- If your sprite's “forward” is not aligned to +X, set `spriteForwardAngle`.
  - Example: if shrimp faces upward by default, set `spriteForwardAngle = 90`.

---

## 7) Troubleshooting

### Clicking shrimp does nothing
- Ensure shrimp has a **Collider2D** (or Collider).
- Ensure the shrimp is on the correct layer mask or has the correct tag.
- Ensure `CInputer.targetCamera` is not null.

### Shrimp rotates strangely
- Adjust `spriteForwardAngle`.
- For pure 2D, keep `useZRotation = true`.

### Return doesn't happen
- Confirm `inactivityReturnSeconds` is set (default 3.0).
- Ensure `CInputer` calls `NotifyUserInteraction()` (it does by default on pointer down).
- Make sure time scale is not zero.

---

## 8) Open-source to GitHub (quick workflow)

1. In your Unity project, keep these scripts under `Assets/Scripts/BPSI/`.
2. Create a new Git repository at the project root.
3. Add Unity `.gitignore` (Unity official template).
4. Commit:
   - `Assets/Scripts/BPSI/*`
   - `README.md`
5. Push to GitHub.

> If you want, I can give you a clean Unity `.gitignore`, and a minimal repo layout for sharing “scripts only” without committing your large art assets.

---

## 9) License suggestion

If you plan to share publicly, a common choice is **MIT License** (simple, permissive).  
Add a `LICENSE` file at repo root if needed.
