# PNJ Ambulant (Patrol Route) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a PNJ a directed waypoint route (A→B→...→loop back to A) that it walks on its own
schedule, independent of the player's position, pausing for dialogue and for combat (if
`canFight`), and resuming the route afterward instead of snapping back to its original spawn.

**Architecture:** All new state and logic lives in `Entities/PNJ.cs` only. `Combat/
CombatAIController.cs` (shared with `Mob.cs`) is never modified — the route system coexists with
it purely through the controller's existing public `Initialize()` method (re-called to shift its
internal "home" anchor to the PNJ's current position while it marches) and its existing public
`CurrentState` property (read to know when to yield movement control during a fight).

**Tech Stack:** Unity C#, NavMeshAgent, no automated test framework — verification is manual
Play Mode testing, each task below spells out exactly what to click/drag/observe.

**Spec:** `docs/superpowers/specs/2026-10-04-pnj-patrol-route-design.md`

## Global Constraints

- `Combat/CombatAIController.cs` is READ-ONLY for this plan. Zero lines changed in that file, in
  any task. Confirm this with `git diff --stat` before every commit in this plan.
- All new fields/methods live in `Entities/PNJ.cs`. `Data/PNJ/PNJData.cs` is READ-ONLY (no new
  fields added there — route config is scene-level, not shared-asset-level, per the spec).
- No automated test framework exists in this project. Every task's verification is a precise
  manual Play Mode procedure (exact clicks/values/observations) — never "add appropriate tests."
- Comments in new code follow the existing project convention: French, explain WHY (a non-obvious
  constraint or a bug this avoids), never WHAT (the code already says that).
- Direct commits to `master` — this repo's established convention, no feature branches.

## Review Focus

- **Missing `NavMeshAgent` on a PNJ prefab that newly needs movement.** Before this feature, only
  `canFight` PNJ needed a `NavMeshAgent` on their prefab; a non-combat PNJ (merchant, decorative)
  never did. The first time Florian flips `isPatrolRoute = true` on such a prefab, `GetComponent
  <NavMeshAgent>()` returns null and the very next line (`_agent.speed = ...`) throws a
  `NullReferenceException` in `Awake()`, silently breaking that PNJ instance. Task 1 must guard
  this with a clear warning instead of a crash.
- **`_routeIndex` surviving a respawn.** `RespawnCoroutine()` resets `transform.position` back to
  `_spawnPos` but has no idea `_routeIndex` exists (it's a field this plan introduces). Without an
  explicit reset, a PNJ that dies after reaching point 3 of 5 respawns at point 0's position but
  immediately tries to path to point 4 — walking away from its own spawn instead of resuming leg
  0→1. Task 2 must add `_routeIndex = 0;` to `RespawnCoroutine()`.
- **A null entry in `patrolPoints`** (a scene Transform deleted after being dragged into the list)
  must not throw — `TickPatrolRoute()` should just stall harmlessly on that index forever. Task 2's
  manual verification must include this exact case, not just assume the null-check works.
- **Dialogue closed by something other than `PNJ.EndDialogue()`** (e.g. the player presses the
  general "close panel" key/UI while talking, or changes scenes) could leave `talkingTo` non-null
  forever, permanently freezing the route (since `IsTalking` would stay true). Task 3's manual
  verification must explicitly test closing dialogue by whatever means Florian normally uses
  in-game (not just letting a dialogue run to its natural end), and flag to Florian if the route
  never resumes — this would be a pre-existing dialogue-lifecycle gap, not something to silently
  patch inside this plan without his sign-off.
- **Hook invocation order.** `OnReachedRoutePoint` must fire with the index just arrived at,
  BEFORE `_routeIndex` is advanced — an implementer skimming the method could easily swap the two
  lines. Task 2's code block below has them in the correct order; the step that writes this method
  must match it exactly, not "clean it up."

---

### Task 1: Patrol fields + component attachment in `Awake()`

**Files:**
- Modify: `Entities/PNJ.cs:61` (insert new fields after `interactionRadius`)
- Modify: `Entities/PNJ.cs:153-174` (widen the movement-component condition, guard missing agent)

**Interfaces:**
- Consumes: `CombatAIController.Initialize(Entity owner, NavMeshAgent agent, SkillSystem
  skillSystem, ICombatAIProfile profile, ICombatAnimator animator, Vector3 spawnPos)` (existing,
  public, pure field assignment — already used unchanged at `PNJ.cs:173`).
- Produces: `public bool isPatrolRoute`, `public List<Transform> patrolPoints`, `public float
  waitAtPointSeconds` — consumed by Task 2 (`TickPatrolRoute()`) and Task 4 (gizmo). `private
  NavMeshAgent _agent` is now populated whenever `data.canFight || isPatrolRoute` is true (was
  `data.canFight` only) — Task 2 relies on `_agent` being non-null under that wider condition.

- [ ] **Step 1: Add the 3 new public fields right after `interactionRadius`**

In `Entities/PNJ.cs`, find:

```csharp
    // ── Interaction ───────────────────────────────────────────
    [Header("Interaction")]
    [Tooltip("Rayon dans lequel le joueur peut interagir avec ce PNJ")]
    public float interactionRadius = 3f;

    // ── Mémoire joueurs connus ────────────────────────────────
```

Replace with:

```csharp
    // ── Interaction ───────────────────────────────────────────
    [Header("Interaction")]
    [Tooltip("Rayon dans lequel le joueur peut interagir avec ce PNJ")]
    public float interactionRadius = 3f;

    // ── Patrol Route (PNJ ambulant) ────────────────────────────
    // Système INDÉPENDANT de la patrouille de combat (CombatAIController.Patrol, réservée aux
    // Gardes — déambulation aléatoire autour du spawn). Voir docs/superpowers/specs/
    // 2026-10-04-pnj-patrol-route-design.md — toute la logique vit dans TickPatrolRoute()
    // ci-dessous, CombatAIController.cs n'est jamais modifié.
    [Header("Patrol Route (PNJ ambulant)")]
    [Tooltip("Active la marche dirigée A→B→... Glisser des GameObjects de la SCÈNE (pas des\n" +
             "prefabs/assets) dans patrolPoints — au moins 2 points requis. Répéter un point\n" +
             "dans la liste pour un aller-retour (A,B,C,B) plutôt qu'une boucle fermée (A,B,C) —\n" +
             "même mécanique, pas de réglage séparé.")]
    public bool isPatrolRoute = false;

    [ShowIf(nameof(isPatrolRoute), true)]
    public List<Transform> patrolPoints = new List<Transform>();

    [Tooltip("Temps d'arrêt (idle) à CHAQUE point avant de repartir vers le suivant, y compris\n" +
             "au bouclage sur patrolPoints[0].")]
    [ShowIf(nameof(isPatrolRoute), true)]
    public float waitAtPointSeconds = 3f;

    // ── Mémoire joueurs connus ────────────────────────────────
```

- [ ] **Step 2: Widen the movement-component condition in `Awake()`, guard a missing agent**

Find:

```csharp
        if (data != null && data.canFight)
        {
            // CombatEntityAnimatorController ET CombatAIController sont ajoutés dynamiquement ici,
            // PAS via [RequireComponent] sur la classe PNJ — un PNJ non combattant (marchand,
            // décoratif...) ne doit JAMAIS se voir forcer un Animator/NavMeshAgent (trouvé en test
            // manuel : "Creating missing Animator component" sur un PNJ Cuisinier sans la moindre
            // notion de combat). `_agent` est relu APRÈS AddComponent<CombatAIController>, PAS
            // avant — CombatAIController requiert NavMeshAgent ([RequireComponent]), donc si le
            // prefab n'en portait pas déjà un (il le devrait, même exigence qu'avant cette
            // migration), Unity en ajoute un par défaut à l'instant de cet AddComponent ; lire
            // _agent avant ce point l'aurait laissé null, et Initialize() aurait reçu ce null au
            // lieu du NavMeshAgent réellement présent sur le GameObject — trouvé en relisant ce
            // plan avant exécution.
            _animatorController = GetComponent<CombatEntityAnimatorController>();
            if (_animatorController == null)
                _animatorController = gameObject.AddComponent<CombatEntityAnimatorController>();

            _combatAI = gameObject.AddComponent<CombatAIController>();
            _agent    = GetComponent<NavMeshAgent>();
            _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
            _combatAI.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
        }
    }
```

Replace with:

```csharp
        // needsMovement élargit la condition historique (canFight seul) à "canFight OU route" —
        // un PNJ ambulant non-combattant a besoin d'un NavMeshAgent pour marcher, exactement
        // comme un PNJ canFight en avait déjà besoin pour son IA de combat.
        bool needsMovement = (data != null && data.canFight) || isPatrolRoute;

        if (needsMovement)
        {
            // CombatEntityAnimatorController ET NavMeshAgent sont lus/ajoutés ici, PAS via
            // [RequireComponent] sur la classe PNJ — un PNJ qui n'a ni combat ni route ne doit
            // JAMAIS se voir forcer un Animator/NavMeshAgent (trouvé en test manuel : "Creating
            // missing Animator component" sur un PNJ Cuisinier sans la moindre notion de
            // mouvement).
            _animatorController = GetComponent<CombatEntityAnimatorController>();
            if (_animatorController == null)
                _animatorController = gameObject.AddComponent<CombatEntityAnimatorController>();

            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
            {
                // Un PNJ ambulant/combattant DOIT avoir un NavMeshAgent posé sur son prefab —
                // contrairement à CombatAIController (RequireComponent, Unity en ajoute un par
                // défaut), rien ici ne force son ajout automatique : un NavMeshAgent auto-ajouté
                // par Unity a des réglages par défaut (rayon/hauteur) qui ne correspondent presque
                // jamais au PNJ réel, mieux vaut un avertissement clair que marcher avec une
                // mauvaise forme de collision. Log + sortie propre plutôt qu'un
                // NullReferenceException au prochain `_agent.speed = ...` (vécu en écrivant ce
                // plan : un PNJ non-combattant existant n'a jamais eu de NavMeshAgent).
                Debug.LogWarning($"[PNJ] {name} : besoin de mouvement (canFight ou isPatrolRoute) " +
                                  "mais aucun NavMeshAgent sur le prefab — l'ajouter manuellement.");
            }
            else
            {
                _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
                SetMoveSpeed(data.baseMoveSpeed);
            }
        }

        if (data != null && data.canFight && _agent != null)
        {
            _combatAI = gameObject.AddComponent<CombatAIController>();
            _combatAI.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
        }

        if (isPatrolRoute && patrolPoints.Count < 2)
            Debug.LogWarning($"[PNJ] {name} : isPatrolRoute = true mais moins de 2 points assignés dans patrolPoints.");
        if (isPatrolRoute && data != null && data.walkClip == null)
            Debug.LogWarning($"[PNJ] {name} : isPatrolRoute = true mais data.walkClip non assigné.");
    }
```

Note the added `_agent != null` guard on the `canFight` branch too — `CombatAIController` has
`[RequireComponent(typeof(NavMeshAgent))]`, so `AddComponent<CombatAIController>()` would silently
have Unity auto-add a default-configured agent if `_agent` were null; skipping the whole branch
when `_agent` is null keeps today's warning path consistent (no combat AI without a real agent on
the prefab, exactly like before this change — a `canFight` PNJ always needed an agent on its
prefab too, this doesn't relax that, it just also warns instead of silently auto-adding one with
wrong collision settings).

- [ ] **Step 3: Compile check**

Open Unity Editor, let it recompile, check the Console: 0 errors. (No automated test command in
this project — this is the verification step for this task.)

- [ ] **Step 4: Manual verification**

1. Pick an existing non-combat PNJ prefab (e.g. a Decorative or Merchant type) that has NO
   NavMeshAgent component today. Set `isPatrolRoute = true` on its instance in a scene, leave
   `patrolPoints` empty, enter Play Mode. Expected: Console shows the "aucun NavMeshAgent" warning,
   no exception, the PNJ behaves exactly as before (stands still, dialogue still works).
2. Add a NavMeshAgent component to that same prefab/instance, re-enter Play Mode with
   `patrolPoints` still empty. Expected: Console shows the "moins de 2 points" warning, no
   exception, PNJ stands still (nothing paths yet — Task 2 makes it actually walk).
3. Pick an existing `canFight = true` Guard-type PNJ (already has a NavMeshAgent). Re-enter Play
   Mode. Expected: behaves EXACTLY as before this change — patrols/fights normally, no new
   warnings, confirming `needsMovement` didn't disturb the existing combat-only path.

- [ ] **Step 5: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: add patrol-route fields, widen movement components beyond canFight-only"
```

---

### Task 2: `TickPatrolRoute()` + `Update()` integration + quest hook + respawn index reset

**Files:**
- Modify: `Entities/PNJ.cs:194-223` (`Update()` — call `TickPatrolRoute()` before the `canFight`
  early-return)
- Modify: `Entities/PNJ.cs` (new private fields + new `TickPatrolRoute()` method + new public
  `OnReachedRoutePoint` event — placed right after the `CurrentState` property, before `Awake()`)
- Modify: `Entities/PNJ.cs:768-801` (`RespawnCoroutine()` — reset `_routeIndex` to 0)

**Interfaces:**
- Consumes: `isPatrolRoute`, `patrolPoints`, `waitAtPointSeconds`, `_agent`, `_combatAI` (all from
  Task 1). `CombatAIController.CurrentState` (existing public property) and
  `CombatAIController.Initialize(...)` (existing public method, same signature as Task 1's call).
  `IsTalking` (existing public property, `Entities/PNJ.cs:809`).
- Produces: `public event System.Action<PNJ, int> OnReachedRoutePoint` — no consumer yet in this
  plan (deferred quest hook, per spec). Private fields `_routeIndex`, `_routeWaiting`,
  `_routeWaitTimer` — not consumed outside this task, but `_routeIndex` must stay named exactly
  this for Task 2's own `RespawnCoroutine()` edit in this same task.

- [ ] **Step 1: Add private route-state fields and the event, right after `CurrentState`**

Find:

```csharp
    public CombatAIState CurrentState => _combatAI != null ? _combatAI.CurrentState : CombatAIState.Patrol;

    // =========================================================
    // INITIALISATION
    // =========================================================
```

Replace with:

```csharp
    public CombatAIState CurrentState => _combatAI != null ? _combatAI.CurrentState : CombatAIState.Patrol;

    // ── Patrol Route — état privé ──────────────────────────────
    private int   _routeIndex     = 0;
    private bool  _routeWaiting   = false;
    private float _routeWaitTimer = 0f;

    /// <summary>Déclenché à CHAQUE arrivée à un point de la route (pas seulement le dernier) —
    /// pointIndex donne l'index dans patrolPoints. Pas encore consommé — point d'accroche pour une
    /// future récompense de quête (QuestSystem), à brancher lors de la revue dialogue/quest-giver/
    /// quête multi-step (hors scope ici, voir la spec).</summary>
    public event System.Action<PNJ, int> OnReachedRoutePoint;

    // =========================================================
    // INITIALISATION
    // =========================================================
```

- [ ] **Step 2: Write `TickPatrolRoute()` — place it right after `Update()`**

Find:

```csharp
        _combatAI.Tick();
    }

    // =========================================================
    // INTERACTION JOUEUR
    // =========================================================
```

Replace with:

```csharp
        _combatAI.Tick();
    }

    /// <summary>Marche dirigée A→B→...→boucle sur patrolPoints[0] — système INDÉPENDANT du combat/
    /// de la patrouille aléatoire (CombatAIController.Patrol, réservée aux Gardes). Zéro ligne de
    /// CombatAIController.cs n'est touchée : ce PNJ se contente de ré-appeler sa méthode PUBLIQUE
    /// Initialize() (pure assignation de champs, déjà appelée une fois dans Awake(), rappel sans
    /// risque) pour déplacer son "chez-soi" de combat sur la position actuelle de la route — si un
    /// combat démarre, CombatAIController.Return ramène ainsi vers LÀ, pas vers le Point A
    /// d'origine. Voir docs/superpowers/specs/2026-10-04-pnj-patrol-route-design.md.</summary>
    private void TickPatrolRoute()
    {
        if (!isPatrolRoute || patrolPoints.Count < 2) return;
        if (IsTalking) return;                                                    // dialogue gèle la marche
        if (_combatAI != null && _combatAI.CurrentState != CombatAIState.Patrol) return; // combat/retour en cours

        if (_routeWaiting)
        {
            _routeWaitTimer -= Time.deltaTime;
            if (_routeWaitTimer <= 0f) _routeWaiting = false;
            return;
        }

        Transform current = patrolPoints[_routeIndex];
        if (current == null) return; // point supprimé de la scène après assignation — reste figé plutôt que planter

        _combatAI?.Initialize(this, _agent, _skillSystem, this, _animatorController, transform.position);

        if (_agent != null) _agent.SetDestination(current.position);

        if (_agent != null && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
        {
            // Invoke AVANT l'avance d'index — le hook rapporte le point qu'on vient d'atteindre,
            // pas le prochain visé.
            OnReachedRoutePoint?.Invoke(this, _routeIndex);

            _routeIndex     = (_routeIndex + 1) % patrolPoints.Count; // boucle automatique sur 0
            _routeWaiting   = true;
            _routeWaitTimer = waitAtPointSeconds;
        }
    }

    // =========================================================
    // INTERACTION JOUEUR
    // =========================================================
```

- [ ] **Step 3: Call `TickPatrolRoute()` in `Update()`, BEFORE the `canFight` early-return**

Find:

```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;
        if (data == null || !data.canFight) return;

        // Poll d'interrupt de canalisation — DOIT rester avant le freeze CC ci-dessous : un hard
```

Replace with:

```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;

        // TickPatrolRoute() AVANT ce early-return — un PNJ ambulant NON-combattant doit marcher
        // même si data.canFight est false, sinon ce return couperait court avant d'y arriver.
        TickPatrolRoute();

        if (data == null || !data.canFight) return;

        // Poll d'interrupt de canalisation — DOIT rester avant le freeze CC ci-dessous : un hard
```

- [ ] **Step 4: Reset `_routeIndex` on respawn**

Find (inside `RespawnCoroutine()`):

```csharp
        isDead             = false;
        currentHP          = maxHP;
        currentMana        = maxMana;
        transform.position = _spawnPos;
        _combatAI?.ResetCooldowns();
```

Replace with:

```csharp
        isDead             = false;
        currentHP          = maxHP;
        currentMana        = maxMana;
        transform.position = _spawnPos;
        _routeIndex         = 0; // sinon le PNJ réapparaît au Point A mais vise encore un point loin dans la route
        _combatAI?.ResetCooldowns();
```

- [ ] **Step 5: Compile check**

Unity Editor recompile, Console: 0 errors.

- [ ] **Step 6: Manual verification**

1. Create 3 empty GameObjects in a test scene at distinct positions, name them clearly (e.g.
   `RoutePointA/B/C`). On a PNJ with `isPatrolRoute = true`, `canFight = false`, drag them into
   `patrolPoints` in that order. Enter Play Mode. Expected: PNJ walks A→B→C→A→B→C→... in a closed
   loop, pausing `waitAtPointSeconds` at each arrival including the wrap back to A.
2. Change `patrolPoints` to `[A, B, C, B]` (drag B again as a 4th entry). Re-enter Play Mode.
   Expected: PNJ walks A→B→C→B→A→B→C→B→... (back-and-forth), same code, no settings changed beyond
   the list content.
3. Delete `RoutePointC` from the scene while `patrolPoints` still references it (back to the
   `[A,B,C]` list). Enter Play Mode. Expected: PNJ walks A→B, then stalls at B forever (no
   exception in Console) — confirms the null-point guard.
4. Set `canFight = true` on the PNJ with a `basicAttackSkill` assigned, place an aggressive mob
   near the route path. Enter Play Mode, let the PNJ walk into aggro range. Expected: PNJ stops
   marching, fights the mob exactly like a Guard (chase/attack). After the mob dies, **the PNJ
   resumes walking from near where it fought — it does NOT walk back to Point A.** This is the
   single most important check in this task — if it walks back to A, the `Initialize()` re-anchor
   isn't working and must be debugged before moving on.
5. Kill the PNJ (if `canDie = true`) partway through its route (e.g. after reaching point index 2
   of 3). After it respawns at Point A, confirm it walks toward point index 1 (B) next, not
   whatever point was "3 steps ahead" — confirms the `_routeIndex = 0` reset.
6. Hook invocation order — `OnReachedRoutePoint` has no subscriber yet (deferred quest hook), so
   this needs a throwaway probe to observe it: temporarily add `pnj.OnReachedRoutePoint += (p, i)
   => Debug.Log($"reached {i}");` in any `Start()` you can reach from the Inspector (or just add
   one line directly inside `TickPatrolRoute()` right after the `Invoke` call, run once, then
   remove it — don't commit it). With `patrolPoints = [A, B, C]`, confirm the Console prints `0`
   when arriving at A, `1` at B, `2` at C, `0` again at the next A — each log fires for the point
   just reached, never one step ahead. Remove the throwaway log before Step 7.

- [ ] **Step 7: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: add PNJ route-walking (TickPatrolRoute), reset route index on respawn"
```

---

### Task 3: Dialogue pause + combat interruption

**Files:**
- Modify: `Entities/PNJ.cs:233-237` (`Interact()` — refuse during combat)
- Modify: `Entities/PNJ.cs:182-188` (`TakeDamage()` — close dialogue on any hit)

**Interfaces:**
- Consumes: `CombatAIController.CurrentState`/`CombatAIState.Engage` (existing, read-only, same
  property already used elsewhere). `IsTalking` (existing). `EndDialogue()` (existing private
  method, `Entities/PNJ.cs:512-518`).
- Produces: nothing new consumed by later tasks — this task is self-contained.

- [ ] **Step 1: Refuse `Interact()` while fighting**

Find:

```csharp
    public void Interact(Player player)
    {
        if (player == null || data == null || isDead) return;
        if (Vector3.Distance(transform.position, player.transform.position) > interactionRadius)
            return;

        talkingTo = player;
```

Replace with:

```csharp
    public void Interact(Player player)
    {
        if (player == null || data == null || isDead) return;
        if (Vector3.Distance(transform.position, player.transform.position) > interactionRadius)
            return;
        // Pas de dialogue en plein combat — cohérent avec TakeDamage() qui ferme un dialogue déjà
        // ouvert dès qu'un coup arrive (voir plus bas) : le combat et le dialogue ne se mélangent
        // jamais, dans aucun des deux sens.
        if (_combatAI != null && _combatAI.CurrentState == CombatAIState.Engage) return;

        talkingTo = player;
```

- [ ] **Step 2: Close an open dialogue on any damage taken**

Find:

```csharp
    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
    {
        base.TakeDamage(amount, sourceElement, source);

        if (!isDead && data != null && data.canFight)
            _combatAI.ForceEngage(source);
    }
```

Replace with:

```csharp
    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
    {
        base.TakeDamage(amount, sourceElement, source);

        // Coupe un dialogue en cours AVANT ForceEngage() — s'applique même si !canFight (un PNJ
        // passif qui encaisse sans riposter voit aussi son dialogue interrompu par un coup reçu).
        if (IsTalking) EndDialogue();

        if (!isDead && data != null && data.canFight)
            _combatAI.ForceEngage(source);
    }
```

- [ ] **Step 3: Compile check**

Unity Editor recompile, Console: 0 errors.

- [ ] **Step 4: Manual verification**

1. On a route PNJ (`isPatrolRoute = true`), talk to it while it's walking. Expected: it stops
   marching for the duration of the dialogue.
2. Close the dialogue the way you normally would in-game (not necessarily by clicking through
   every option to the natural end — whatever close gesture exists). Expected: the PNJ resumes
   marching toward the SAME point it was heading to before the dialogue opened (no skipped point,
   no reset to index 0). If it does NOT resume, this is the pre-existing dialogue-lifecycle gap
   flagged in Review Focus — stop and report it rather than patching around it silently.
3. With `canFight = true`, open a dialogue with the PNJ, then have a mob attack it while the
   dialogue window is open. Expected: the dialogue closes immediately and the PNJ switches to
   fighting the mob.
4. With `canFight = true`, get the PNJ into an active fight (`CombatAIState.Engage`), then try to
   `Interact()` with it (talk key/click) mid-fight. Expected: nothing happens — no dialogue opens
   while it's fighting.

- [ ] **Step 5: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: PNJ dialogue pauses route, combat interrupts an open dialogue"
```

---

### Task 4: Route gizmo (editor-only, cosmetic)

**Files:**
- Modify: `Entities/PNJ.cs:816-828` (`OnDrawGizmosSelected()`)

**Interfaces:**
- Consumes: `isPatrolRoute`, `patrolPoints` (from Task 1).
- Produces: nothing consumed by later tasks — purely a level-design aid, no gameplay effect.

- [ ] **Step 1: Draw the route when the PNJ is selected in the Scene view**

Find:

```csharp
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        if (data != null && data.canFight)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, data.aggroRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _combatAI != null ? _combatAI.EngageRange : 0f);
        }
    }
}
```

Replace with:

```csharp
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        if (data != null && data.canFight)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, data.aggroRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _combatAI != null ? _combatAI.EngageRange : 0f);
        }

        if (isPatrolRoute && patrolPoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Count; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(patrolPoints[i].position, 0.3f);
                Transform next = patrolPoints[(i + 1) % patrolPoints.Count];
                if (next != null) Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }
    }
}
```

- [ ] **Step 2: Compile check**

Unity Editor recompile, Console: 0 errors.

- [ ] **Step 3: Manual verification**

Select a PNJ with `isPatrolRoute = true` and 3+ `patrolPoints` in the Scene view. Expected: a
green sphere at each point and a green line connecting them in order, including a line from the
last point back to the first (this line is purely visual — the NavMeshAgent paths around
obstacles on its own, it won't necessarily walk the straight gizmo line).

- [ ] **Step 4: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: draw patrol route gizmo for PNJ with isPatrolRoute"
```

---

### Task 5: Full manual verification pass

No code changes — this task is the spec's complete verification checklist run end-to-end on one
PNJ configuration after another, to catch anything an individual task's narrower test missed.

**Files:** none.

**Interfaces:** none — this task only exercises what Tasks 1-4 already produced.

- [ ] **Step 1: Non-combat ambulant, closed loop**

`isPatrolRoute = true`, `canFight = false`, `patrolPoints = [A, B, C]`. Confirm: walks A→B→C→A→...
indefinitely, idles `waitAtPointSeconds` at each point, never throws.

- [ ] **Step 2: Non-combat ambulant, back-and-forth**

Same PNJ, `patrolPoints = [A, B, C, B]`. Confirm: walks A→B→C→B→A→B→C→B→...

- [ ] **Step 3: Dialogue pause + resume**

Talk to the PNJ mid-route, close the dialogue, confirm it resumes toward the same point (not
reset, not skipped).

- [ ] **Step 4: Combat reuse + resume without snapping to Point A**

`canFight = true`, valid `basicAttackSkill`. Let a mob attack it mid-route. Confirm it fights like
a Guard, then resumes marching near where the fight happened — NOT back at Point A.

- [ ] **Step 5: Combat interrupts an open dialogue**

Open a dialogue, then let a mob attack the PNJ. Confirm the dialogue closes and combat starts.

- [ ] **Step 6: Death and respawn mid-route**

`canDie = true`. Kill the PNJ after it has advanced a few points into its route. Confirm it
respawns at Point A and resumes toward point index 1, not wherever "3 steps ahead" would have
been.

- [ ] **Step 7: Existing Guard PNJ, zero regression**

Pick a PNJ that already existed before this plan (`isPatrolRoute = false`, `canFight = true`,
using the old random-wander Patrol). Confirm it behaves EXACTLY as before — no behavior change,
no new warnings in Console. This is the check that the two systems (route vs. existing combat
patrol) really stayed separate, per Florian's explicit constraint.

- [ ] **Step 8: Report**

Tell Florian which of the above passed/failed. Any failure in Steps 3 or 4 is a real bug in this
plan's code (not a pre-existing gap) and must be fixed before calling this feature done.
