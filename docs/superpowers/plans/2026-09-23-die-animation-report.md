# Die Animation — Implementation Report

Date: 2026-09-23
Scope: bounded feature addition, no formal plan/spec doc (per Florian's brief) — this file is the record.

## Summary

Added a "Die" animation state to Mob/PNJ/Player, following the already-established Idle/Walk/Chase/Attack
placeholder-clip-swap pattern rather than inventing something new. No `.controller` assets were touched —
Florian will add the `Death` state + `PLACEHOLDER_Death`/`PLACEHOLDER_Death` clips to the two shared
Animator Controllers (Mob/PNJ shared controller, and the Player controller) manually in the Unity Editor.
Until he does, every `PlayDeath()` call is a safe no-op (see "Behavior when unconfigured" below).

## Files changed

1. `Data/Mobs/MobData.cs` — added `public AnimationClip deathClip;` under "Animations locomotion", same
   `[Tooltip]` style as `idleClip`/`walkClip`/`chaseClip`.
2. `Data/PNJ/PNJData.cs` — added `public AnimationClip deathClip;` under the same `canFight`-gated
   `[ShowIf]` block as `idleClip`/`walkClip`/`chaseClip`.
3. `World/CombatEntityAnimatorController.cs`:
   - `ICombatAnimatorProfile` interface: added `AnimationClip DeathClip { get; }`.
   - Added `PlaceholderDeathName = "PLACEHOLDER_Death"` and `DeathState = "Death"` constants, and a
     `_deathPlaceholder` field, resolved in `ResolvePlaceholders()` alongside the other three. The
     missing-placeholder `Debug.LogWarning` deliberately still only checks Idle/Walk/Chase/Attack —
     `_deathPlaceholder` being null is expected until Florian wires up the Death state, so it stays silent
     rather than warning on every Awake in the interim.
   - Generalized `PlayOverrideClip(AnimationClip clip)` into
     `PlayOverrideClip(AnimationClip clip, AnimationClip placeholderSlotKey, string stateName)`.
     `PlayAttack`/`PlayChannel` now call it explicitly with `(clip, _attackPlaceholder, AttackState)` —
     byte-for-byte the same values they used implicitly before, so their behavior is unchanged.
   - Added `public float PlayDeath(AnimationClip clip)`: returns `0f` immediately if `clip == null`
     (no-op); otherwise swaps `_deathPlaceholder` → `clip`, plays the `Death` state, and returns
     `clip.length`.
4. `Entities/Mob.cs`:
   - `ICombatAnimatorProfile.DeathClip => data?.deathClip;` (matches `IdleClip`/`WalkClip`/`ChaseClip`).
   - In `Die()`, right before the existing `Destroy(gameObject, 3f);`, added
     `float deathAnimLength = _animatorController?.PlayDeath(data?.deathClip) ?? 0f;` and changed the
     destroy call to `Destroy(gameObject, deathAnimLength + 3f);`. The existing fixed 3s corpse window is
     always preserved, extended by the clip's length when one is assigned (0f when not — identical to
     today's behavior).
5. `Entities/PNJ.cs`:
   - `ICombatAnimatorProfile.DeathClip => data?.deathClip;` (matches Mob's implementation).
   - In `RespawnCoroutine()`, inserted at the very top, before the existing renderer/collider/agent
     disable code:
     ```csharp
     float deathAnimLength = _animatorController?.PlayDeath(data?.deathClip) ?? 0f;
     if (deathAnimLength > 0f)
         yield return new WaitForSeconds(deathAnimLength);
     ```
     `respawnDelay` itself is untouched — this delay is strictly additive, before the existing
     hide-then-wait-then-respawn sequence. With no `deathClip` assigned, `PlayDeath` returns `0f` and this
     block is a complete no-op — today's behavior (instant hide, no death pose) is preserved exactly.
6. `World/PlayerAnimatorController.cs`:
   - Added `DeathState = "Death"` constant, `[SerializeField] AnimationClip deathPlaceholderClip` (the
     override-key, parallel to `attackPlaceholderClip`), and `[SerializeField] AnimationClip deathClip`
     (the actual clip to play — see judgment call below).
   - Generalized this file's own private `PlayOverrideClip(AnimationClip clip)` into
     `PlayOverrideClip(AnimationClip clip, AnimationClip placeholderSlotKey, string stateName)` — this is
     a SEPARATE helper from `CombatEntityAnimatorController`'s (two independent files, parallel pattern,
     no shared code, per the brief). `PlayAttack`/`PlayChannel` now call it with
     `(clip, attackPlaceholderClip, AttackState)` — same values as before, behavior unchanged.
   - Added `public void PlayDeath()` (no clip argument — see judgment call below) —
     `PlayOverrideClip(deathClip, deathPlaceholderClip, DeathState)`. No-op if `deathClip` is unassigned
     (falls through the existing `clip == null` guard in `PlayOverrideClip`).
7. `Entities/Player.cs` — in `Die()`, added `animatorController?.PlayDeath();` right after `base.Die();`
   in BOTH branches: the instance-dungeon short-circuit branch (line ~1031) and the open-world branch
   (line ~1050). Player is visually dying the same way regardless of which system (InstanceSession vs.
   RespawnSystem) handles what happens next, so both branches play the same death pose.

## Judgment call — Player's clip-sourcing (step 7)

The brief explicitly left this open. Two options were on the table:
- **(A)** Reuse the per-skill placeholder-swap shape (`PlayDeath(AnimationClip clip)`, caller supplies the
  clip), matching `PlayAttack`/`PlayChannel`'s signature exactly.
- **(B)** Give `PlayerAnimatorController` its own fixed `[SerializeField] AnimationClip deathClip` and a
  no-argument `PlayDeath()`.

**Chose (B).** Reasoning: `attackPlaceholderClip` exists because Attack genuinely varies — a different
clip is swapped in per skill, sourced from `SkillData.attackAnimation` at the call site. Death has no such
per-call variability: there is exactly one death animation for the whole character, for the entire game.
Player also has no MobData/PNJData-equivalent ScriptableObject to source a clip from (it's a single
player-controlled rig, not per-species content) — inventing a new field somewhere else to hold "the one
player death clip" and threading it through two `Die()` branches would add indirection for no benefit.
Keeping the clip as a fixed Inspector reference directly on `PlayerAnimatorController` (parallel in
*shape* to `attackPlaceholderClip`, but fixed rather than swapped-per-call) keeps `Player.Die()` simple
(`animatorController?.PlayDeath();`, no clip lookup) and keeps the "what plays" decision co-located with
"how it's played," which is where all the other Animator wiring already lives in this file. If Death is
ever needed to vary (e.g. per weapon or per elemental death-VFX), it can be converted to the
`PlayDeath(AnimationClip clip)` shape later without touching any other file — `Player.Die()`'s call site
would just need a clip source at that point.

## Verification performed

No live Unity Editor / compiler available in this environment, per the brief. Did the following instead:
- Read every touched file in full, both before and after editing.
- Confirmed `ICombatAnimatorProfile` is declared in `World/CombatEntityAnimatorController.cs` (not
  `Combat/CombatAIController.cs` as the brief's own preamble flagged as unconfirmed) — verified directly
  by reading the file; `Mob`/`PNJ` both already implement it there.
- Confirmed the `AnimatorOverrideController` indexer is keyed by the *original* placeholder clip
  (`_overrideController[_attackPlaceholder] = clip;` in the pre-existing code) and carried that exact
  pattern into the generalized `PlayOverrideClip(clip, placeholderSlotKey, stateName)` in both animator
  controller files.
- Confirmed `Mob`'s and `PNJ`'s existing field name for their `CombatEntityAnimatorController` reference
  is `_animatorController` in both classes (grep/read-confirmed, not assumed).
- Manually traced brace balance and every new symbol reference (`DeathClip`, `PlayDeath`,
  `_deathPlaceholder`, `PlaceholderDeathName`, `DeathState`, `deathClip`, `deathPlaceholderClip`) back to
  its declaration in the same read-back pass.
- Confirmed `PlayAttack`/`PlayChannel`/`CancelChannel` are behaviorally unchanged: the generalized
  `PlayOverrideClip` is called with the exact same placeholder/state arguments those methods always used
  implicitly (`_attackPlaceholder`/`AttackState` in `CombatEntityAnimatorController`,
  `attackPlaceholderClip`/`AttackState` in `PlayerAnimatorController`); `CancelChannel` itself was not
  touched at all in either file.

## Behavior when unconfigured (before Florian adds the Death state/clips in-editor)

- Mob/PNJ: `_deathPlaceholder` resolves to `null` (no `PLACEHOLDER_Death` clip exists yet in the shared
  Controller) → `PlayOverrideClip`'s `placeholderSlotKey == null` guard fires → nothing is played. But
  `PlayDeath(clip)` still returns `clip.length` if a `deathClip` happens to be assigned on some MobData/
  PNJData asset even though nothing visibly played (per the brief's explicit spec: "return clip.length"
  unconditionally once past the `clip == null` check) — Mob's corpse timer would silently get slightly
  longer than the (invisible) animation, which is harmless, and disappears once Florian wires the state.
  With no `deathClip` assigned at all (today's every existing Mob/PNJ asset), this whole path is fully
  inert — `Destroy(gameObject, 3f)` timing and PNJ respawn timing are both untouched.
- Player: `deathClip`/`deathPlaceholderClip` both start unassigned → `PlayDeath()`'s
  `clip == null` check on `deathClip` fires first → complete no-op, identical to before this chantier.

## Concerns / follow-ups for Florian

- The shared Mob/PNJ Animator Controller needs a `PLACEHOLDER_Death` clip + `Death` state added (mirror
  of the existing Attack setup), and the Player Animator Controller needs its own `Death` state +
  placeholder motion. Until then the code paths above are inert by design.
- Death state transitions: per the brief, Death must NOT auto-return to locomotion the way Attack does
  (exit-time transition) — this needs to hold indefinitely (Player, held until Revive) or for exactly the
  clip's duration with no exit transition needed (Mob/PNJ, since the GameObject is destroyed/hidden right
  after). This is Editor-graph work, not something the C# in this chantier can enforce.
- No `.meta` files were created for changed `.cs` files beyond what Unity will regenerate — consistent
  with "expected/accepted, not a concern" per the brief.
