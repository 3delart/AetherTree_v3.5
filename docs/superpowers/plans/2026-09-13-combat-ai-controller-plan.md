# CombatAIController Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract a shared `CombatAIController` component (Patrol/Engage/Return) that replaces
Mob's split Chase/Attack state machine and PNJ's ad-hoc `HandleCombatAI()`, giving Mob and PNJ
byte-for-byte identical combat AI behavior, while adding Guard patrol and Passive/Aggressive
parity to PNJ.

**Architecture:** One new `MonoBehaviour`, `CombatAIController`, owns the state machine, the
pending-hit/hit-frame-sync machinery, and the channel-cast machinery — all currently duplicated
almost verbatim between `Mob.cs` and `PNJ.cs`. It never references `Mob` or `PNJ` directly; each
owner implements `ICombatAIProfile` (what to hunt, how far to roam, how to leash) and passes its
`ICombatAnimator` (already-existing `MobAnimatorController`/`PNJAnimatorController`) into
`Initialize()`.

**Tech Stack:** Unity C#, no automated test framework — every task ends with a compile check;
final behavioral verification is manual Play Mode testing by Florian (Task 6).

**Spec:** `docs/superpowers/specs/2026-09-13-combat-ai-controller-design.md` (read both — this
plan transcribes the spec's exact logic into file-by-file steps, but the spec's prose carries
the *why* behind several non-obvious decisions this plan only references by section number).

## Global Constraints

- No behavior change intended EXCEPT the ones explicitly called out below (each is a deliberate,
  Florian-approved deviation — never treat one as a bug to "fix back"):
  1. PNJ gains a CC-freeze on hard status effects (`isStunned`/`isSleeping`/`isShocked`/
     `isFreezed`) before `Tick()` — it never had one (spec §6.6).
  2. PNJ gains Rooted/Feared handling inside Engage — it never had it (spec §5).
  3. A Passive Mob/PNJ now targets ONLY entities in its `aggroSet` (who hit it), never a closer
     bystander — deviates from the GDD as currently written (spec §5bis).
  4. The old Mob-only `GetMaxSkillRange() * 1.2f` buffer for "should I fall back to Chase" is
     GONE — the unified Engage model (transcribed from PNJ's simpler linear design) uses
     `GetMaxSkillRange()` with no multiplier as the single approach/engage threshold. Found
     while writing this plan; it's a direct, intended consequence of merging Chase+Attack into
     one recomputed-every-frame state — PNJ's original code never had this buffer either. Add
     this to the Task 6 checklist as its own verification point (not originally in the spec's
     §9 list).
  5. PNJ's leash still anchors to its fixed spawn point (`LeashAnchor` returns a constant) —
     Mob's anchors to a dynamic `aggroPos` updated every time it starts a new engagement
     (`OnEngageStart()`). Do NOT unify these into one mechanism — Florian confirmed the Guard
     must keep defending its fixed post (spec §4.1, `ICombatAIProfile.LeashAnchor`).
- `CombatAIController` must never reference `Mob` or `PNJ` by type — only `Entity`,
  `ICombatAIProfile`, `ICombatAnimator`. If a task needs something from a concrete type, add it
  to one of those interfaces instead.
- Every `[SerializeField]`/public field default added to `PNJData` must preserve existing
  `PNJData` assets' current behavior with zero Inspector changes required (`patrolRadius = 0f`,
  `aiType = MobAIType.Aggressive` — see spec §7 for why `Aggressive`, not `Passive`, is the safe
  default).
- `TryUseSecondarySkill()`'s per-skill range check uses the fallback `skill.range > 0f ?
  skill.range : 2f` (PNJ's existing behavior) — Mob's old `TryUseSkill()` had no such fallback.
  Adopting PNJ's version is a deliberate pick made while writing this plan (defensive, matches
  the pattern already used everywhere else for `basicAttackSkill`), not an oversight.
- Do not delete the `MobState` enum in Task 4 until a project-wide grep for `MobState` (outside
  `Mob.cs` itself) comes back empty. The spec asserts this is already true from its own audit,
  but re-verify at implementation time — do not just trust the spec's claim.

---

### Task 1: `Combat/CombatAIController.cs` (new file)

**Files:**
- Create: `Combat/CombatAIController.cs`

**Interfaces:**
- Produces: `enum CombatAIState { Patrol, Engage, Return }`; `interface ICombatAnimator` (methods
  `PlayAttack(AnimationClip)`, `PlayChannel(AnimationClip)`, `CancelChannel()`); `interface
  ICombatAIProfile` (properties `BasicAttackSkill`, `SecondarySkills`, `PatrolRadius`,
  `LeashDistance`, `LeashAnchor`, `AutoEngageOnSight`; methods `FindClosestEnemy()`,
  `HasAnyEnemyNearby()`, `OnForcedEngage(Entity)`, `OnReturnToPatrol()`, `OnEngageStart()`);
  class `CombatAIController` with public members `CurrentState`, `IsPendingHit`, `IsChanneling`,
  `EngageRange`, and methods `Initialize(...)`, `Tick()`, `PollChannelInterrupt()`,
  `ForceEngage(Entity)`, `NotifyDeath()`, `OnAnimationHitEvent(int)`.
- Consumes: nothing from other tasks — this is the foundation every other task builds on.

- [ ] **Step 1: Write the complete file**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// COMBATAICONTROLLER.CS — IA de combat partagée Mob/PNJ
// Path : Assets/Scripts/Combat/CombatAIController.cs
// Voir docs/superpowers/specs/2026-09-13-combat-ai-controller-design.md
//
// Remplace le duo Mob.HandleChase()/HandleAttack() (deux méthodes qui calculaient chacune leurs
// propres seuils de distance — source d'une chaîne de bugs d'oscillation/désync trouvés en test
// manuel) et PNJ.HandleCombatAI() par UNE machine à 3 états (Patrol/Engage/Return) où Chase+
// Attack fusionnent en un seul état Engage, recalculé à chaque frame, sans sous-état persisté.
// Ce composant ne connaît NI Mob NI PNJ — seulement Entity + les interfaces ci-dessous, fournies
// par l'owner à Initialize().
// =============================================================

public enum CombatAIState { Patrol, Engage, Return }

/// <summary>Pont vers MobAnimatorController/PNJAnimatorController — signatures déjà identiques
/// entre les deux aujourd'hui, cette interface ne fait que les déclarer formellement.</summary>
public interface ICombatAnimator
{
    void PlayAttack(AnimationClip clip);
    void PlayChannel(AnimationClip clip);
    void CancelChannel();
}

/// <summary>Fourni par l'owner (Mob ou PNJ) à Initialize() — encapsule tout ce qui diffère entre
/// les deux : quelle espèce est ennemie, comment le ciblage Passif/Agressif fonctionne, où le
/// leash est ancré. Voir spec §5bis pour le détail complet de chaque membre.</summary>
public interface ICombatAIProfile
{
    SkillData       BasicAttackSkill  { get; }
    List<SkillData> SecondarySkills   { get; }
    float           PatrolRadius      { get; }   // 0 = reste au spawn (pas de roam)
    float           LeashDistance     { get; }   // distance MAX autorisée depuis LeashAnchor
    Vector3         LeashAnchor       { get; }   // Mob : aggroPos (dynamique) ; PNJ : spawn (fixe)
    bool            AutoEngageOnSight { get; }   // true = passe en Engage dès qu'un ennemi est détecté

    Entity FindClosestEnemy();   // taunt-aware, retourne null si aucun candidat
    bool   HasAnyEnemyNearby();  // check bon marché pour Patrol — seulement appelé si AutoEngageOnSight

    void OnForcedEngage(Entity aggressor); // hook — l'owner ajoute aggressor à son aggroSet
    void OnReturnToPatrol();               // hook — appelé UNE FOIS à l'arrivée au spawn (Return → Patrol)
    void OnEngageStart();                  // hook — appelé UNE FOIS à la transition Patrol → Engage
}

[RequireComponent(typeof(NavMeshAgent))]
public class CombatAIController : MonoBehaviour
{
    public CombatAIState CurrentState { get; private set; } = CombatAIState.Patrol;
    public bool IsPendingHit => _pendingSkill != null;
    public bool IsChanneling => _isChanneling;

    /// <summary>Exposé pour les gizmos de debug côté Mob/PNJ — même valeur que l'ancien
    /// GetMaxSkillRange() de chaque fichier.</summary>
    public float EngageRange => GetMaxSkillRange();

    private Entity           _owner;
    private NavMeshAgent      _agent;
    private SkillSystem       _skillSystem;
    private ICombatAIProfile  _profile;
    private ICombatAnimator   _animator;
    private Vector3           _spawnPos;

    // ── Patrouille ────────────────────────────────────────────
    private bool  _patrolPointsSet = false;
    private bool  _isWaiting       = false;
    private float _waitTimer       = 0f;

    // ── Attaque de base — trouvé manquant de la liste des champs rapatriés en écrivant la
    // spec (§4.2) : l'étape 10 de l'état Engage en dépend directement. Décompte au top de
    // Tick() — ne tique donc plus pendant un CC dur (l'owner n'appelle pas Tick() dans ce cas),
    // contrairement à l'ancien Mob.attackTimer qui décomptait même sous CC ; ça l'aligne sur
    // _skillCooldowns, qui lui ne décomptait déjà que hors CC. Effet quasi nul en jeu.
    private float _basicAttackTimer = 0f;

    // ── Cooldowns skills secondaires ──────────────────────────
    private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();

    // ── Hystérésis basicRange — sans buffer, dist(target) oscille de part et d'autre du seuil
    // exact à cause du micro-jitter NavMeshAgent, provoquant un flapping SetDestination/
    // ResetPath en continu ("marche sur place", trouvé en test manuel via logs [MOB-DEBUG]).
    // Une fois dans basicRange, on n'en ressort qu'au-delà de basicRange * BasicRangeExitFactor.
    private const float BasicRangeExitFactor = 1.15f;
    private bool        _wasInBasicRange = false;

    // ── Pending-hit (hit-frame-sync) — un seul hit en vol à la fois ──────────────────────────
    private SkillData _pendingSkill   = null;
    private Entity    _pendingTarget  = null;
    private float     _pendingTimeout = 0f;
    private bool      _pendingIsMulti = false;
    private int       _pendingMultiNextIndex = 0;

    // ── Canalisation (castTime > 0) — mutuellement exclusive de _pendingSkill ────────────────
    private bool           _isChanneling   = false;
    private SkillData      _channelSkill   = null;
    private Entity         _channelTarget  = null;
    private GameObject     _channelVfxCast = null;
    private CastBarSpawner _channelBar     = null;

    public void Initialize(Entity owner, NavMeshAgent agent, SkillSystem skillSystem,
                            ICombatAIProfile profile, ICombatAnimator animator, Vector3 spawnPos)
    {
        _owner       = owner;
        _agent       = agent;
        _skillSystem = skillSystem;
        _profile     = profile;
        _animator    = animator;
        _spawnPos    = spawnPos;
    }

    // =========================================================
    // POLL D'INTERRUPT DE CANALISATION + TIMEOUT PENDING-HIT
    // Appelé SÉPARÉMENT par l'owner, AVANT son propre freeze CC général (voir spec §6.3) — un
    // hard CC doit interrompre la canalisation EN COURS, pas être bloqué par le early-return sur
    // isCCd qui vient juste après côté owner. Le tick du timeout pending-hit est INDÉPENDANT de
    // la canalisation (mutuellement exclusifs mais tick à part, même ordre que le code actuel).
    // =========================================================
    public void PollChannelInterrupt()
    {
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        if (!_isChanneling) return;

        bool hardCC = _owner.statusEffects != null && (_owner.statusEffects.isStunned ||
                      _owner.statusEffects.isShocked || _owner.statusEffects.isFreezed ||
                      _owner.statusEffects.isKnockedBack || _owner.statusEffects.isFeared ||
                      _owner.statusEffects.isSilenced);
        if (hardCC)
            InterruptChannelCast(voluntary: false, reason: "CC");
        else if (_channelTarget != null && _channelTarget.isDead)
            InterruptChannelCast(voluntary: true, reason: "cible morte");
    }

    // =========================================================
    // BOUCLE PRINCIPALE — appelée depuis Update() de l'owner, après son propre freeze CC.
    // =========================================================
    public void Tick()
    {
        _basicAttackTimer -= Time.deltaTime;

        switch (CurrentState)
        {
            case CombatAIState.Patrol: TickPatrol(); break;
            case CombatAIState.Engage: TickEngage(); break;
            case CombatAIState.Return: TickReturn(); break;
        }
    }

    private void TickPatrol()
    {
        if ((_profile.AutoEngageOnSight && _profile.HasAnyEnemyNearby()) || IsTauntedWithValidSource())
        {
            _isWaiting = false;
            _profile.OnEngageStart();
            CurrentState = CombatAIState.Engage;
            return;
        }

        if (_profile.PatrolRadius <= 0f) return; // reste immobile au spawn

        if (!_patrolPointsSet)
        {
            _agent.SetDestination(GetNavMeshPoint(_spawnPos, _profile.PatrolRadius));
            _patrolPointsSet = true;
            return;
        }

        if (_isWaiting)
        {
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
            {
                _isWaiting = false;
                _agent.SetDestination(GetNavMeshPoint(_spawnPos, _profile.PatrolRadius));
            }
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
        {
            _isWaiting = true;
            _waitTimer = Random.Range(2f, 5f);
        }
    }

    private void TickEngage()
    {
        if (Vector3.Distance(_owner.transform.position, _profile.LeashAnchor) > _profile.LeashDistance)
        {
            GoReturn();
            return;
        }

        if (_owner.statusEffects != null && _owner.statusEffects.isRooted)
        {
            StopAgent();
            return;
        }

        if (_owner.statusEffects != null && _owner.statusEffects.isFeared)
        {
            Entity threat = _profile.FindClosestEnemy();
            if (threat != null)
            {
                Vector3 fleeDir = (_owner.transform.position - threat.transform.position).normalized;
                _agent.SetDestination(_owner.transform.position + fleeDir * 5f);
            }
            return;
        }

        Entity target = _profile.FindClosestEnemy();
        if (target == null || target.isDead)
        {
            GoReturn();
            return;
        }

        if (_isChanneling) { StopAgent(); return; }

        float dist = Vector3.Distance(_owner.transform.position, target.transform.position);

        if (dist > GetMaxSkillRange())
        {
            _wasInBasicRange = false;
            _agent.SetDestination(target.transform.position);
            return;
        }

        LookAt(target.transform);

        bool taunted = _owner.statusEffects != null && _owner.statusEffects.isTaunted;
        if (!taunted && TryUseSecondarySkill(target))
        {
            StopAgent();
            return;
        }

        float basicRange = _profile.BasicAttackSkill != null && _profile.BasicAttackSkill.range > 0f
            ? _profile.BasicAttackSkill.range
            : 2f;
        float basicRangeCheck = _wasInBasicRange ? basicRange * BasicRangeExitFactor : basicRange;

        if (dist > basicRangeCheck)
        {
            _wasInBasicRange = false;
            _agent.SetDestination(target.transform.position);
            return;
        }

        _wasInBasicRange = true;
        StopAgent();

        if (_profile.BasicAttackSkill == null)
        {
            if (_basicAttackTimer <= 0f)
            {
                _basicAttackTimer = 2f;
                Debug.LogWarning($"[CombatAIController] {_owner.entityName} n'a pas de basicAttackSkill assigné.");
            }
            return;
        }

        if (_basicAttackTimer <= 0f && !IsPendingHit && !_isChanneling && !_owner.isDead && !target.isDead)
        {
            _basicAttackTimer = _profile.BasicAttackSkill.cooldown > 0f ? _profile.BasicAttackSkill.cooldown : 2f;

            if (_profile.BasicAttackSkill.castTime > 0f)
                StartChannelCast(_profile.BasicAttackSkill, target);
            else
                StartPendingHit(_profile.BasicAttackSkill, target);
        }
    }

    private void TickReturn()
    {
        if (!_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid)
            _agent.SetDestination(_spawnPos);

        if (Vector3.Distance(_owner.transform.position, _spawnPos) <= 2f)
        {
            _agent.ResetPath();
            _agent.Warp(_spawnPos);
            _profile.OnReturnToPatrol();
            _patrolPointsSet = false;
            CurrentState = CombatAIState.Patrol;
        }
    }

    private void GoReturn()
    {
        CurrentState = CombatAIState.Return;
        _agent.SetDestination(_spawnPos);

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        if (_isChanneling)
            InterruptChannelCast(voluntary: true, reason: "désengagement");
    }

    /// <summary>Force la transition Patrol → Engage indépendamment de AutoEngageOnSight — hook
    /// pour Mob.TakeDamage()/AggroFrom() et PNJ.TakeDamage(). aggressor peut être null (ex:
    /// dégât de zone sans source directe) — la transition a lieu quand même, mais rien n'est
    /// ajouté à un éventuel aggroSet côté owner.</summary>
    public void ForceEngage(Entity aggressor)
    {
        _profile.OnForcedEngage(aggressor);
        if (CurrentState == CombatAIState.Patrol)
        {
            _isWaiting = false;
            _profile.OnEngageStart();
            CurrentState = CombatAIState.Engage;
        }
    }

    private bool IsTauntedWithValidSource()
    {
        if (_owner.statusEffects == null || !_owner.statusEffects.isTaunted) return false;
        Entity tauntSource = _owner.statusEffects.GetDebuffSource(DebuffType.Taunt);
        return tauntSource != null && !tauntSource.isDead;
    }

    /// <summary>Nettoyage pending-hit/canalisation — appelé par Die() de l'owner AVANT de
    /// désactiver quoi que ce soit (la bar/le vfxCast ne doivent pas rester affichés sur une
    /// entité déjà marquée morte). N'affecte PAS agent.enabled — ça reste la responsabilité de
    /// l'owner (Mob se désactive définitivement, PNJ respawn plus tard).</summary>
    public void NotifyDeath()
    {
        _agent.ResetPath();

        if (_isChanneling)
            InterruptChannelCast(voluntary: false, reason: "mort");

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;
    }

    /// <summary>Relais direct depuis MobAnimatorController.OnSkillHitFrame /
    /// PNJAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;
        ResolvePendingHit(hitIndex);
    }

    // =========================================================
    // SKILL SECONDAIRE — tick CD + exécution
    // =========================================================
    private bool TryUseSecondarySkill(Entity target)
    {
        if (_profile.SecondarySkills != null)
        {
            foreach (var skill in _profile.SecondarySkills)
            {
                if (skill == null) continue;
                if (_skillCooldowns.ContainsKey(skill))
                    _skillCooldowns[skill] -= Time.deltaTime;
            }
        }

        if (IsPendingHit || _isChanneling) return true;
        if (_profile.SecondarySkills == null || _profile.SecondarySkills.Count == 0) return false;

        foreach (var skill in _profile.SecondarySkills)
        {
            if (skill == null) continue;
            float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
            if (cd > 0f) continue;

            float range = skill.range > 0f ? skill.range : 2f;
            if (Vector3.Distance(_owner.transform.position, target.transform.position) > range) continue;
            if (skill.manaCost > 0f && !_owner.HasMana(skill.manaCost)) continue;

            if (skill.manaCost > 0f) _owner.SpendMana(skill.manaCost);

            LookAt(target.transform);
            if (skill.castTime > 0f)
                StartChannelCast(skill, target);
            else
                StartPendingHit(skill, target);
            return true;
        }

        return false;
    }

    /// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
    /// n'arrive qu'à l'event d'impact ou au timeout de secours. Sans attackAnimation assignée,
    /// résout immédiatement. Un MultiHit SANS attackAnimation reste sur l'ancien chemin
    /// Execute()/ExecuteMultiHit (respecte HitStep.delay via coroutine).</summary>
    private void StartPendingHit(SkillData skill, Entity target)
    {
        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _owner.transform.position, Quaternion.identity);

        bool isMulti = skill.executionType == SkillExecutionType.MultiHit
                       && skill.hitSteps != null && skill.hitSteps.Count > 0;

        if (isMulti && skill.attackAnimation == null)
        {
            _skillSystem?.Execute(skill, _owner, target);
            if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
                _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            return;
        }

        _animator?.PlayAttack(skill.attackAnimation);

        _pendingSkill   = skill;
        _pendingTarget  = target;
        _pendingIsMulti = isMulti;
        _pendingMultiNextIndex = 0;
        _pendingTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingTimeout <= 0f)
            ResolvePendingHit(0);
    }

    /// <summary>Résout le hit en attente — branchement à 3 voies (hasDelayedImpact / isTrajectory
    /// / dispatch standard), routage MultiHit par index. Pose le cooldown du skill secondaire
    /// APRÈS résolution. Rejette un hitIndex hors séquence (event mal numéroté sur le clip).</summary>
    private void ResolvePendingHit(int hitIndex)
    {
        if (_pendingSkill == null) return;

        SkillData skill   = _pendingSkill;
        Entity    target  = _pendingTarget;
        bool      isMulti = _pendingIsMulti;

        if (isMulti && hitIndex != _pendingMultiNextIndex)
        {
            Debug.LogWarning($"[CombatAIController] {_owner.entityName} — Animation Event MultiHit reçu avec hitIndex={hitIndex}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        if (isMulti)
        {
            _skillSystem?.ResolveMultiHitStep(skill, _owner, target, hitIndex);
            _pendingMultiNextIndex++;
            int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
            if (_pendingMultiNextIndex < totalHits) return;
        }
        else
        {
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, _owner, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, _owner);
            else
                _skillSystem?.ResolveExecute(skill, _owner, target);
        }

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    // =========================================================
    // CANALISATION (castTime > 0)
    // =========================================================
    private void StartChannelCast(SkillData skill, Entity target)
    {
        _isChanneling  = true;
        _channelSkill  = skill;
        _channelTarget = target;

        _animator?.PlayChannel(skill.channelAnimation);

        _channelVfxCast = skill.vfxCast != null
            ? Instantiate(skill.vfxCast, _owner.transform.position, Quaternion.identity)
            : null;

        _channelBar = CastBarSpawner.Show(
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            followTarget: _owner.transform,
            onComplete:   ResolveChannelCast,
            onCancel:     () => InterruptChannelCast(voluntary: true, reason: "bar volée")
        );
    }

    private void ResolveChannelCast()
    {
        if (!_isChanneling) return;

        SkillData skill  = _channelSkill;
        Entity    target = _channelTarget;

        EndChannelCastState();

        if (skill.hasDelayedImpact)
            _skillSystem?.PlantDelayedZone(skill, _owner, target);
        else if (skill.isTrajectory)
            _skillSystem?.StartTrajectory(skill, _owner);
        else
            _skillSystem?.Execute(skill, _owner, target);

        if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    /// <summary>voluntary = true (cible morte, bar volée en interne — CD moitié) | false
    /// (CC/mort subie — CD complet). Capture _channelBar AVANT EndChannelCastState() puis
    /// l'annule APRÈS — ORDRE CRITIQUE : si la bar était annulée AVANT, son callback onCancel
    /// réentrant s'exécuterait pendant que _isChanneling est encore vrai et poserait le CD demi
    /// avant que CET appel n'atteigne son propre garde.</summary>
    private void InterruptChannelCast(bool voluntary, string reason)
    {
        if (!_isChanneling) return;

        SkillData      skill = _channelSkill;
        CastBarSpawner bar   = _channelBar;

        EndChannelCastState();

        bar?.Cancel();
        _animator?.CancelChannel();

        if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
        {
            float baseCd = skill.cooldown > 0f ? skill.cooldown : 6f;
            _skillCooldowns[skill] = voluntary ? baseCd * 0.5f : baseCd;
        }

        Debug.Log($"[CombatAIController] {_owner.entityName} — canalisation interrompue ({reason}) — {(voluntary ? "CD demi" : "CD complet")}.");
    }

    private void EndChannelCastState()
    {
        _isChanneling  = false;
        _channelSkill  = null;
        _channelTarget = null;

        if (_channelVfxCast != null) { Destroy(_channelVfxCast); _channelVfxCast = null; }
        _channelBar = null;
    }

    // =========================================================
    // UTILITAIRES PRIVÉS
    // =========================================================

    /// <summary>Plus grande range configurée parmi BasicAttackSkill + SecondarySkills — décide
    /// quand ce owner arrête de s'approcher pour engager.</summary>
    private float GetMaxSkillRange()
    {
        float max = _profile.BasicAttackSkill != null && _profile.BasicAttackSkill.range > 0f
            ? _profile.BasicAttackSkill.range
            : 2f;

        if (_profile.SecondarySkills != null)
            foreach (var skill in _profile.SecondarySkills)
                if (skill != null && skill.range > max) max = skill.range;

        return max;
    }

    private void StopAgent()
    {
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
    }

    private void LookAt(Transform target)
    {
        if (target == null) return;
        Vector3 dir = target.position - _owner.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            _owner.transform.rotation = Quaternion.LookRotation(dir);
    }

    private Vector3 GetNavMeshPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 point = center + Random.insideUnitSphere * radius;
            point.y = center.y;
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;
        }
        return center;
    }
}
```

- [ ] **Step 2: Compile check**

Open Unity, let it recompile. `CombatAIController.cs` will show errors about `DebuffType`,
`SkillExecutionType`, `CastBarSpawner`, `LocalizationManager`, `SkillData`, `SkillSystem`,
`Entity` not being found ONLY if one of those types is actually missing from the project — they
are all pre-existing global-namespace types already used by `Mob.cs`/`PNJ.cs` today, so the
expected result is a CLEAN compile with zero errors (this file has no consumers yet, so nothing
else can break).

Expected: 0 compile errors.

- [ ] **Step 3: Commit**

```bash
git add Combat/CombatAIController.cs
git commit -m "feat: add shared CombatAIController (Patrol/Engage/Return)"
```

---

### Task 2: `Data/PNJ/PNJData.cs` — new fields

**Files:**
- Modify: `Data/PNJ/PNJData.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `PNJData.patrolRadius` (float), `PNJData.aiType` (`MobAIType`) — consumed by Task 5's
  `PNJ.ICombatAIProfile` implementation.

- [ ] **Step 1: Add the two fields**

Find this block (Combat header, right after `combatMoveSpeed`):

```csharp
    [Tooltip("Vitesse de déplacement en mode combat. 0 = utilise baseMoveSpeed.")]
    [ShowIf(nameof(canFight), true)]
    public float combatMoveSpeed = 0f;
```

Add immediately after it:

```csharp

    [Tooltip("Rayon de patrouille autour du spawn quand aucune cible n'est engagée. 0 = reste\n" +
             "immobile au spawn (comportement actuel). Équivalent de MobData.patrolRadius.")]
    [ShowIf(nameof(canFight), true)]
    public float patrolRadius = 0f;

    [Tooltip("Passive : n'engage que s'il est attaqué directement (TakeDamage). Aggressive :\n" +
             "engage dès qu'un ennemi entre dans aggroRadius — comportement actuel, DÉFAUT à ne\n" +
             "jamais changer pour ne pas casser les PNJData existants. Boss non utilisé côté\n" +
             "PNJ (réutilise MobAIType tel quel — même enum que MobData, aucun risque ordinal).")]
    [ShowIf(nameof(canFight), true)]
    public MobAIType aiType = MobAIType.Aggressive;
```

- [ ] **Step 2: Compile check**

Open Unity, let it recompile. `MobAIType` is defined in `Data/Mobs/MobData.cs` in the global
namespace — no `using` needed.

Expected: 0 compile errors.

- [ ] **Step 3: Commit**

```bash
git add Data/PNJ/PNJData.cs
git commit -m "feat: add patrolRadius and aiType to PNJData (both default to current behavior)"
```

---

### Task 3: Animator controllers — declare `ICombatAnimator`

**Files:**
- Modify: `World/MobAnimatorController.cs`
- Modify: `World/PNJAnimatorController.cs`

**Interfaces:**
- Consumes: `ICombatAnimator` (Task 1).
- Produces: `MobAnimatorController : ICombatAnimator`, `PNJAnimatorController : ICombatAnimator`
  — consumed by Task 4 (Mob) and Task 5 (PNJ), whose `Awake()` methods pass these components to
  `CombatAIController.Initialize()` as the `ICombatAnimator` argument.

This task ONLY declares the interface and removes a stale debug log — it deliberately does NOT
touch `MobAnimatorController`'s `IsChasingParam` line yet, because that line reads
`Mob.CurrentState`, whose TYPE only changes from `MobState` to `CombatAIState` in Task 4. Doing
that part here would break the build until Task 4 lands; doing the interface declaration here
(instead of bundled into Task 4/5 as originally drafted) is what makes Task 4 and Task 5 each
independently compilable — found while self-reviewing this plan's task order.

- [ ] **Step 1: `MobAnimatorController.cs` — declare the interface, remove the debug log**

Find:
```csharp
[RequireComponent(typeof(Animator))]
public class MobAnimatorController : MonoBehaviour
```
Replace with:
```csharp
[RequireComponent(typeof(Animator))]
public class MobAnimatorController : MonoBehaviour, ICombatAnimator
```

Find:
```csharp
    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);

        // [MOB-DEBUG] temporaire — à retirer une fois le "marche sur place" résolu.
        if (Time.frameCount % 15 == 0)
            Debug.Log($"[MOB-DEBUG][ANIM] t={Time.time:F2} Speed={speed:F2} " +
                      $"agentPos={(_agent != null ? _agent.transform.position : Vector3.zero)} " +
                      $"hasPath={(_agent != null && _agent.hasPath)}");

        // Distingue walk (Patrol) de chase (Chase) — impossible via Speed seul, les deux
        // utilisent la même vitesse aujourd'hui (Mob.agent.speed ne varie pas selon l'état).
        _animator.SetBool(IsChasingParam, _mob != null && _mob.CurrentState == MobState.Chase);
    }
```
Replace with (note: `IsChasingParam` line is UNCHANGED here on purpose — still `MobState.Chase`,
still valid, since `Mob.CurrentState` hasn't changed type yet; only the debug log is removed):
```csharp
    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);

        // Distingue walk (Patrol) de chase (Chase) — impossible via Speed seul, les deux
        // utilisent la même vitesse aujourd'hui (Mob.agent.speed ne varie pas selon l'état).
        _animator.SetBool(IsChasingParam, _mob != null && _mob.CurrentState == MobState.Chase);
    }
```

- [ ] **Step 2: `PNJAnimatorController.cs` — declare the interface**

Find:
```csharp
[RequireComponent(typeof(Animator))]
public class PNJAnimatorController : MonoBehaviour
```
Replace with:
```csharp
[RequireComponent(typeof(Animator))]
public class PNJAnimatorController : MonoBehaviour, ICombatAnimator
```
No other change needed in this file — `PlayAttack`/`PlayChannel`/`CancelChannel` already have
exactly the signatures `ICombatAnimator` requires.

- [ ] **Step 3: Compile check**

Open Unity, let it recompile.

Expected: 0 compile errors. `Mob.CurrentState`/`MobState` are completely untouched by this task,
so nothing here can break `Mob.cs`.

- [ ] **Step 4: Commit**

```bash
git add World/MobAnimatorController.cs World/PNJAnimatorController.cs
git commit -m "feat: declare ICombatAnimator on MobAnimatorController/PNJAnimatorController"
```

---

### Task 4: `Entities/Mob.cs` migration

**Files:**
- Modify: `Entities/Mob.cs`
- Modify: `World/MobAnimatorController.cs` (one line — see Step 14)

**Interfaces:**
- Consumes: `CombatAIState`, `ICombatAIProfile`, `ICombatAnimator`, `CombatAIController` (Task 1);
  `MobAnimatorController : ICombatAnimator` (Task 3).
- Produces: `Mob.CurrentState` (now type `CombatAIState`, was `MobState`) — consumed by this same
  task's Step 14. `Mob : ICombatAIProfile` — no other file consumes this directly.

This task removes most of `Mob.cs`'s combat-loop code and replaces it with a much smaller
`ICombatAIProfile` implementation. Work through the sub-steps in order — each one is a
self-contained find-and-replace against the file as it stands before this task starts.

- [ ] **Step 1: Add the CombatAIController requirement and new fields**

Find:
```csharp
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(StatusEffectSystem))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(SkillSystem))]
public class Mob : Entity
```
Replace with:
```csharp
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(StatusEffectSystem))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(SkillSystem))]
[RequireComponent(typeof(CombatAIController))]
public class Mob : Entity, ICombatAIProfile
```

Find the field block starting at `protected NavMeshAgent agent;` through
`public MobState CurrentState => currentState;` (this includes `currentState` itself, its
backing field, and the exposing property):
```csharp
    protected NavMeshAgent agent;
    protected Vector3      spawnPos;
    protected MobState     currentState = MobState.Patrol;

    // Exposé pour MobAnimatorController — distingue walk (Patrol) de chase (Chase), impossible
    // via le seul paramètre Speed puisque les deux utilisent la même vitesse aujourd'hui.
    public MobState CurrentState => currentState;
```
Replace with:
```csharp
    protected NavMeshAgent agent;
    protected Vector3      spawnPos;
    private   CombatAIController _combatAI;
    private   Vector3            aggroPos;

    // Exposé pour MobAnimatorController — voir CombatAIController pour le sens de chaque état.
    public CombatAIState CurrentState => _combatAI.CurrentState;
```
(`aggroPos` already existed further down the file as a separate field under `// Aggro` — see
Step 6, it moves up here and its old declaration site is deleted so it isn't declared twice.)

Find:
```csharp
    // Hystérésis sur basicRange — sans buffer, dist(target) oscille de part et d'autre du seuil
    // exact à cause du micro-jitter NavMeshAgent (accélère vers la cible, dépasse le seuil,
    // ResetPath, redevient hors-portée, SetDestination, etc. à CHAQUE frame) : trouvé en test
    // manuel via logs [MOB-DEBUG] — le Mob restait figé en position mais Speed/hasPath
    // flappaient en continu ("marche sur place"). Une fois dans basicRange, on n'en ressort
    // qu'au-delà de basicRange * BasicRangeExitFactor.
    private const float BasicRangeExitFactor = 1.15f;
    private bool         _wasInBasicRange = false;

    private System.Action onDeathCallback;
```
Replace with:
```csharp
    private System.Action onDeathCallback;

    // ── Aggro (§5bis de la spec) — Passive ne cible QUE ce qui l'a frappé ; Aggressive garde le
    // scan de proximité ET mémorise en plus quiconque le frappe (utile si l'attaquant sort
    // ensuite de detectionRange en kitant). Vidé uniquement dans OnReturnToPatrol().
    private HashSet<Entity> aggroSet = new HashSet<Entity>();
```

Find (near the bottom of the field block):
```csharp
    // Aggro
    private Vector3     aggroPos;
```
Delete this block entirely (its declaration moved up to Step 1's replacement above — this
prevents a duplicate-field compile error).

- [ ] **Step 2: Add `using System.Collections.Generic;`**

`Mob.cs` already has `using System.Collections.Generic;` at the top (line 3) — no change needed
here, just confirping it's there before Step 1's `HashSet<Entity>` compiles.

- [ ] **Step 3: Rewrite `Awake()`**

Find:
```csharp
    protected override void Awake()
    {
        base.Awake();
        agent               = GetComponent<NavMeshAgent>();
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<MobAnimatorController>();
        spawnPos = transform.position;
        ApplyData();
    }
```
Replace with:
```csharp
    protected override void Awake()
    {
        base.Awake();
        agent               = GetComponent<NavMeshAgent>();
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<MobAnimatorController>();
        _combatAI           = GetComponent<CombatAIController>();
        spawnPos = transform.position;
        aggroPos = spawnPos;
        ApplyData();
        _combatAI.Initialize(this, agent, _skillSystem, this, _animatorController, spawnPos);
    }
```

- [ ] **Step 4: Rewrite `Update()`**

Find the entire method (from `protected override void Update()` through its closing `}`,
immediately before the `// ENEMYLIST` section header):
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead || data == null) return;

        attackTimer -= Time.deltaTime;

        // Timeout de secours (event d'impact jamais reçu) — tick AVANT tout early-return CC :
        // un coup déjà lancé va au bout, non-interruptible, même principe que le Player
        // (chantier B hit-frame-sync).
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        // Poll d'interrupt de la canalisation — hard CC (non-volontaire, CD complet) ou cible
        // morte (volontaire, CD demi). ...
        if (_isChanneling)
        {
            bool hardCC = statusEffects != null && (statusEffects.isStunned ||
                          statusEffects.isShocked || statusEffects.isFreezed ||
                          statusEffects.isKnockedBack || statusEffects.isFeared ||
                          statusEffects.isSilenced);
            if (hardCC)
                InterruptChannelCast(voluntary: false, reason: "CC");
            else if (_channelTarget != null && _channelTarget.isDead)
                InterruptChannelCast(voluntary: true, reason: "cible morte");
        }

        // Slow × Haste — multiplicatifs
        if (statusEffects != null && data != null)
            agent.speed = data.moveSpeed * statusEffects.slowMultiplier * statusEffects.buffSpeedMultiplier;

        RefreshEnemyList();

        if (IsDashing) return;

        // Stun/Sleep — CC dur, GDD §3.1.1.1 ...
        bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping || statusEffects.isShocked || statusEffects.isFreezed);
        if (agent != null && agent.isOnNavMesh) agent.isStopped = isCCd;
        if (isCCd) return;

        switch (currentState)
        {
            case MobState.Patrol: HandlePatrol(); break;
            case MobState.Chase:  HandleChase();  break;
            case MobState.Attack: HandleAttack(); break;
            case MobState.Return: HandleReturn(); break;
        }
    }
```
Replace with:
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead || data == null) return;

        // Poll d'interrupt de canalisation — DOIT rester avant le freeze CC ci-dessous : un hard
        // CC doit interrompre la canalisation EN COURS, pas être bloqué par le early-return sur
        // isCCd qui vient juste après.
        _combatAI.PollChannelInterrupt();

        // Slow × Haste — multiplicatifs
        if (statusEffects != null && data != null)
            agent.speed = data.moveSpeed * statusEffects.slowMultiplier * statusEffects.buffSpeedMultiplier;

        RefreshEnemyList();

        if (IsDashing) return;

        // Stun/Sleep — CC dur, GDD §3.1.1.1 : "bloque TOUTES les actions".
        bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping || statusEffects.isShocked || statusEffects.isFreezed);
        if (agent != null && agent.isOnNavMesh) agent.isStopped = isCCd;
        if (isCCd) return;

        _combatAI.Tick();
    }
```

- [ ] **Step 5: Rewrite `RefreshEnemyList()`**

Find the entire method:
```csharp
    private void RefreshEnemyList()
    {
        enemyList.Clear();

        // Détecte tous les colliders dans le rayon de détection
        Collider[] hits = Physics.OverlapSphere(transform.position, data.detectionRange);
        foreach (Collider col in hits)
        {
            // Joueur — Stealth = totalement ignoré (jamais détecté, mouvement ou pas — le
            // "clignotement" en bougeant existe seulement côté PvP futur, pas contre l'IA).
            Player player = col.GetComponent<Player>();
            if (player != null && !player.isDead && !(player.statusEffects?.isStealthed ?? false))
            {
                if (!enemyList.Contains(player))
                    enemyList.Add(player);
                continue;
            }

            // Pet — TODO : ajouter quand Pet.cs sera implémenté
            // Pet pet = col.GetComponent<Pet>();
            // if (pet != null && !pet.isDead && !enemyList.Contains(pet))
            //     enemyList.Add(pet);

            // PNJ — les mobs ciblent tous les PNJ (GDD v3.5 §3.4 : tous peuvent mourir)
            // Guards protègent le village en priorité, mais tous peuvent mourir
            // (ex: village envahi — marchands, décoratifs etc. sont attaquables)
            PNJ pnj = col.GetComponentInParent<PNJ>();
            if (pnj != null && !pnj.isDead)
            {
                if (!enemyList.Contains(pnj))
                    enemyList.Add(pnj);
            }
        }
    }
```
Replace with:
```csharp
    private void RefreshEnemyList()
    {
        enemyList.Clear();
        aggroSet.RemoveWhere(e => e == null || e.isDead);

        // Passive (spec §5bis) : PAS de scan de proximité — ce mob ne cible QUE ce qui l'a
        // frappé (aggroSet, fusionné plus bas). Déviation assumée du GDD §3.7 tel qu'écrit
        // aujourd'hui (qui décrit un scan de proximité identique pour les deux aiType) — voir
        // la spec pour le détail complet de cette décision.
        if (data.aiType == MobAIType.Aggressive)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, data.detectionRange);
            foreach (Collider col in hits)
            {
                // Joueur — Stealth = totalement ignoré (jamais détecté, mouvement ou pas — le
                // "clignotement" en bougeant existe seulement côté PvP futur, pas contre l'IA).
                Player player = col.GetComponent<Player>();
                if (player != null && !player.isDead && !(player.statusEffects?.isStealthed ?? false))
                {
                    if (!enemyList.Contains(player))
                        enemyList.Add(player);
                    continue;
                }

                // Pet — TODO : ajouter quand Pet.cs sera implémenté
                // Pet pet = col.GetComponent<Pet>();
                // if (pet != null && !pet.isDead && !enemyList.Contains(pet))
                //     enemyList.Add(pet);

                // PNJ — les mobs ciblent tous les PNJ (GDD v3.5 §3.4 : tous peuvent mourir)
                PNJ pnj = col.GetComponentInParent<PNJ>();
                if (pnj != null && !pnj.isDead)
                {
                    if (!enemyList.Contains(pnj))
                        enemyList.Add(pnj);
                }
            }
        }

        // aggroSet fusionné pour LES DEUX aiType — Aggressive y ajoute ses attaquants sortis de
        // detectionRange en plus du scan ci-dessus ; Passive n'a QUE ça.
        foreach (Entity e in aggroSet)
            if (!enemyList.Contains(e)) enemyList.Add(e);
    }
```

- [ ] **Step 6: Rename `GetClosestEnemy()` to the public `FindClosestEnemy()` and add the rest of
      `ICombatAIProfile`**

Find:
```csharp
    private Entity GetClosestEnemy()
    {
        if (statusEffects != null && statusEffects.isTaunted)
        {
            Entity tauntSource = statusEffects.GetDebuffSource(DebuffType.Taunt);
            if (tauntSource != null && !tauntSource.isDead) return tauntSource;
        }

        Entity closest = null;
        float  minDist = float.MaxValue;

        foreach (Entity e in enemyList)
        {
            if (e == null || e.isDead) continue;
            float dist = Vector3.Distance(transform.position, e.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = e;
            }
        }
        return closest;
    }
```
Replace with:
```csharp
    public Entity FindClosestEnemy()
    {
        if (statusEffects != null && statusEffects.isTaunted)
        {
            Entity tauntSource = statusEffects.GetDebuffSource(DebuffType.Taunt);
            if (tauntSource != null && !tauntSource.isDead) return tauntSource;
        }

        Entity closest = null;
        float  minDist = float.MaxValue;

        foreach (Entity e in enemyList)
        {
            if (e == null || e.isDead) continue;
            float dist = Vector3.Distance(transform.position, e.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = e;
            }
        }
        return closest;
    }

    // =========================================================
    // ICombatAIProfile — voir spec §5bis pour le détail de chaque membre
    // =========================================================
    public SkillData       BasicAttackSkill  => data?.basicAttackSkill;
    public List<SkillData> SecondarySkills   => data?.skills;
    public float           PatrolRadius      => data != null ? data.patrolRadius : 0f;
    public float           LeashDistance     => data != null ? data.detectionRange * data.leashMultiplier : 0f;
    public Vector3         LeashAnchor       => aggroPos;
    public bool            AutoEngageOnSight => data != null && data.aiType == MobAIType.Aggressive;

    public bool HasAnyEnemyNearby() => enemyList.Count > 0;

    public void OnForcedEngage(Entity aggressor)
    {
        if (aggressor != null && !aggressor.isDead) aggroSet.Add(aggressor);
    }

    public void OnEngageStart()
    {
        aggroPos = transform.position;
    }

    /// <summary>Ancien corps de FullReset() — HP/Mana à 100%, cooldowns/pending déjà remis à
    /// zéro par CombatAIController lui-même à ce même instant, il ne reste ici que ce qui est
    /// propre à Mob (contributions, aggro, debuffs).</summary>
    public void OnReturnToPatrol()
    {
        currentHP   = maxHP;
        currentMana = maxMana;
        enemyList.Clear();
        aggroSet.Clear();
        damageContributions.Clear();
        totalDamageTaken = 0f;
        lastSkillByAttacker.Clear();
        statusEffects?.ResetDebuffResistances();
        RequestRecalculate();
    }
```

- [ ] **Step 7: Delete the entire Patrol/Chase/Attack/Return/Skill/Canalisation block**

Delete every method between the `// PATROL` section header and the `// RETURN` section's
`FullReset()` method, INCLUSIVE of both section headers and `FullReset()` itself — that is,
delete `HandlePatrol()`, `HandleChase()`, `HandleAttack()`, `TryUseSkill()`, `StartPendingHit()`,
`OnAnimationHitEvent(int)` (note: a NEW, much smaller version of this one method is re-added in
Step 8 below — deleting it here and re-adding it there is intentional, not a mistake),
`ResolvePendingHit()`, `StartChannelCast()`, `ResolveChannelCast()`, `InterruptChannelCast()`,
`EndChannelCastState()`, `HandleReturn()`, `GoReturn()`, `BeginChase()`, and `FullReset()`. Also
delete the private fields these methods used that have not already been handled in Step 1:
`attackTimer`, `_skillCooldowns`, `_pendingSkill`, `_pendingTarget`, `_pendingTimeout`,
`_pendingIsMulti`, `_pendingMultiNextIndex`, `IsPendingHit` (the property), `_isChanneling`,
`_channelSkill`, `_channelTarget`, `_channelVfxCast`, `_channelBar`, `patrolPointsSet`,
`isWaiting`, `waitTimer`.

- [ ] **Step 8: Re-add a small `OnAnimationHitEvent` that forwards to the controller**

`MobAnimatorController.OnSkillHitFrame()` calls `_mob?.OnAnimationHitEvent(hitIndex)` — this
public method must still exist on `Mob`, just as a one-line forward:

```csharp
    /// <summary>Reçoit l'Animation Event relayé par MobAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0) => _combatAI.OnAnimationHitEvent(hitIndex);
```
Add this method where `HandlePatrol()`/etc. used to live (any position in the file works — put
it right after `FindClosestEnemy()`/the `ICombatAIProfile` block from Step 6 for locality).

- [ ] **Step 9: Rewrite `TakeDamage()`**

Find:
```csharp
        base.TakeDamage(amount, sourceElement, source);

        // ── Aggro automatique — GDD v3.5 §3.3 ────────────────
        // Tout mob agressé entre en Chase même s'il est Passif
        if (!isDead &&
            currentState != MobState.Chase  &&
            currentState != MobState.Attack &&
            currentState != MobState.Return)
        {
            aggroPos = transform.position;
            isWaiting = false;
            currentState = MobState.Chase;
        }
    }
```
Replace with:
```csharp
        base.TakeDamage(amount, sourceElement, source);

        // ── Aggro automatique — GDD v3.5 §3.3 ────────────────
        // Tout mob agressé entre en combat même s'il est Passif. `source` BRUT, pas `attacker`
        // résolu ci-dessus (qui vaut null pour tout ce qui n'est pas un Player) — un Mob frappé
        // par un PNJ Garde doit quand même le cibler en retour (trouvé en écrivant le plan).
        if (!isDead)
            _combatAI.ForceEngage(source);
    }
```

- [ ] **Step 10: Rewrite `Die()`'s cleanup lines**

Find:
```csharp
    protected override void Die()
    {
        base.Die();
        agent.ResetPath();
        agent.enabled = false;

        // Canalisation en vol au moment de la mort (sous-chantier 2) — nettoyée AVANT de
        // désactiver ce composant, sinon la barre/le vfxCast/l'anim resteraient affichés
        // jusqu'à la fin naturelle du timer sur un Mob déjà mort (trouvé en vérification
        // indépendante du plan). NE PAS appeler _channelBar?.Cancel() directement —
        // InterruptChannelCast() s'en charge dans le bon ordre (voir Step 6).
        if (_isChanneling)
            InterruptChannelCast(voluntary: false, reason: "mort");

        this.enabled = false;
```
Replace with:
```csharp
    protected override void Die()
    {
        base.Die();

        // Nettoyage pending-hit/canalisation AVANT de désactiver quoi que ce soit — sinon la
        // barre/le vfxCast/l'anim resteraient affichés sur un Mob déjà mort.
        _combatAI.NotifyDeath();
        agent.enabled = false;

        this.enabled = false;
```

- [ ] **Step 11: Rewrite `AggroFrom()`**

Find:
```csharp
    public void AggroFrom(Entity attacker)
    {
        if (isDead || attacker == null) return;
        if (!enemyList.Contains(attacker))
            enemyList.Add(attacker);
        aggroPos     = transform.position;
        isWaiting    = false;
        currentState = MobState.Chase;
    }
```
Replace with:
```csharp
    public void AggroFrom(Entity attacker)
    {
        if (isDead || attacker == null) return;
        _combatAI.ForceEngage(attacker);
    }
```

- [ ] **Step 12: Update the gizmos**

Find:
```csharp
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, GetMaxSkillRange());
```
Replace with:
```csharp
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _combatAI != null ? _combatAI.EngageRange : 0f);
```
(`OnDrawGizmosSelected()` can run in the Editor outside Play Mode, before `Awake()` has assigned
`_combatAI` — the null check avoids an Editor-only NullReferenceException.)

- [ ] **Step 13: Delete `GetMaxSkillRange()`**

Delete the `private float GetMaxSkillRange() { ... }` method entirely (moved into
`CombatAIController` in Task 1).

- [ ] **Step 14: Fix `MobAnimatorController.cs`'s `IsChasing` line**

This is the one line in the whole codebase outside `Mob.cs` that reads `Mob.CurrentState` — it
must be updated in lockstep with Step 15 below, which deletes the `MobState` type that line
currently references.

Find (in `World/MobAnimatorController.cs`):
```csharp
        // Distingue walk (Patrol) de chase (Chase) — impossible via Speed seul, les deux
        // utilisent la même vitesse aujourd'hui (Mob.agent.speed ne varie pas selon l'état).
        _animator.SetBool(IsChasingParam, _mob != null && _mob.CurrentState == MobState.Chase);
```
Replace with:
```csharp
        // Distingue walk (Patrol) de combat rapproché (Engage) — impossible via Speed seul,
        // Speed=0 pendant Engage (immobile en train d'attaquer) ressemble à Speed=0 en Patrol
        // (arrivé à un point d'attente). Avec la fusion Chase+Attack en un seul état Engage
        // (voir CombatAIController), IsChasing est vrai pendant TOUT Engage — approche ET combat
        // rapproché immobile — combiné à Speed pour le blend Idle/Walk/Run de l'Animator.
        _animator.SetBool(IsChasingParam, _mob != null && _mob.CurrentState == CombatAIState.Engage);
```

- [ ] **Step 15: Delete the `MobState` enum**

Grep the whole project for `MobState` (`grep -rn "MobState" --include=*.cs .` from the
`Assets/Scripts` root, or use the Grep tool) BEFORE deleting the enum declaration
`public enum MobState { Patrol, Chase, Attack, Return }` at the bottom of `Mob.cs`. The spec
asserts zero references outside `Mob.cs` itself — confirm this is still true right now (code may
have changed since the spec was written), NOW THAT Step 14 has removed the one reference that
legitimately existed in `MobAnimatorController.cs`. If any reference turns up anywhere else,
STOP and report it rather than deleting — do not guess a fix.

Expected grep result: only matches inside `Mob.cs` itself (the enum declaration and nothing
else — everything that used to reference `MobState.Patrol/Chase/Attack/Return` was removed by
this task's earlier steps or by Step 14 above). Delete the enum declaration once confirmed.

- [ ] **Step 16: Compile check**

Open Unity, let it recompile.

Expected: 0 compile errors in either file. If Unity shows errors inside `Mob.cs` itself, fix them
before moving on (a missed field reference from Step 7's deletion is the most likely cause).

- [ ] **Step 17: Commit**

```bash
git add Entities/Mob.cs World/MobAnimatorController.cs
git commit -m "refactor: migrate Mob combat AI onto shared CombatAIController"
```

---

### Task 5: `Entities/PNJ.cs` migration

**Files:**
- Modify: `Entities/PNJ.cs`

**Interfaces:**
- Consumes: `CombatAIState`, `ICombatAIProfile`, `ICombatAnimator`, `CombatAIController` (Task 1);
  `PNJData.patrolRadius`/`PNJData.aiType` (Task 2); `PNJAnimatorController : ICombatAnimator`
  (Task 3).
- Produces: `PNJ.CombatAI` (new public property exposing the `CombatAIController`) — not
  currently consumed anywhere else; PNJ doesn't expose a `CurrentState` the way Mob does because
  nothing needs one — `PNJAnimatorController.Update()` only needs `Speed`, which reads the
  `NavMeshAgent` directly and needs no change from this task.

- [ ] **Step 1: Implement `ICombatAIProfile` on the class and add new fields**

Find:
```csharp
[RequireComponent(typeof(SkillSystem))]
public class PNJ : Entity
```
Replace with:
```csharp
[RequireComponent(typeof(SkillSystem))]
public class PNJ : Entity, ICombatAIProfile
```
(No `[RequireComponent(typeof(CombatAIController))]` here — unlike Mob, not every PNJ fights,
and `CombatAIController` requires a `NavMeshAgent` that non-combat PNJ never carry. It's added
dynamically in Step 2, only for `canFight` PNJ.)

Find:
```csharp
    // ── Combat — commun à tous les PNJ canFight ───────────────
    // NavMeshAgent et spawnPos disponibles dès que canFight est actif,
    // pas seulement pour les Gardes.
    private NavMeshAgent _agent;
    private SkillSystem  _skillSystem;
    private Entity       _combatTarget;
    private float        _attackTimer  = 0f;
    private Vector3      _spawnPos;

    // Cooldowns des skills secondaires — même pattern que Mob.cs
    private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();

    // ── Attente de résolution (hit-frame-sync — un seul hit en vol à la fois) —
    // sous-chantier 1, voir docs/superpowers/specs/2026-09-12-mob-pnj-animator-foundations-
    // design.md ──────────────────────────────────────────────────────────────────────────
    private SkillData _pendingSkill   = null;
    private Entity    _pendingTarget  = null;
    private float     _pendingTimeout = 0f;
    private bool      _pendingIsMulti = false;
    private int       _pendingMultiNextIndex = 0;

    public bool IsPendingHit => _pendingSkill != null;

    private PNJAnimatorController _animatorController;

    // ── Canalisation visible (castTime > 0) — sous-chantier 2, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-channel-vfxcast-design.md — mutuellement exclusif du mécanisme
    // _pendingSkill ci-dessus : un skill castTime > 0 ne passe JAMAIS par StartPendingHit() ────
    private bool           _isChanneling   = false;
    private SkillData      _channelSkill   = null;
    private Entity         _channelTarget  = null;
    private GameObject     _channelVfxCast = null;
    private CastBarSpawner _channelBar     = null;

    // Hystérésis sur basicRange — même bug que Mob.cs (voir son commentaire détaillé) : sans
    // buffer, dist(target) oscille de part et d'autre du seuil exact à cause du micro-jitter
    // NavMeshAgent, provoquant un flapping SetDestination/ResetPath en continu ("marche sur
    // place", même bug que Mob.HandleAttack()). Une fois dans basicRange, on n'en ressort qu'au-
    // delà de basicRange * 1.15.
    private const float BasicRangeExitFactor = 1.15f;
    private bool         _wasInBasicRange = false;
```
Replace with:
```csharp
    // ── Combat — commun à tous les PNJ canFight ───────────────
    // NavMeshAgent, spawnPos et CombatAIController disponibles dès que canFight est actif,
    // pas seulement pour les Gardes.
    private NavMeshAgent       _agent;
    private SkillSystem        _skillSystem;
    private CombatAIController _combatAI;
    private Vector3            _spawnPos;

    // enemyList — même pattern que Mob.cs (proximité + aggroSet fusionnés, voir
    // RefreshEnemyList() et FindClosestEnemy() plus bas).
    private List<Entity>    enemyList = new List<Entity>();
    private HashSet<Entity> aggroSet  = new HashSet<Entity>();

    private PNJAnimatorController _animatorController;

    /// <summary>Exposé pour un éventuel consommateur externe (aucun aujourd'hui) — PNJ n'a pas
    /// besoin d'un CurrentState comme Mob, PNJAnimatorController ne lit que Speed.</summary>
    public CombatAIController CombatAI => _combatAI;
```

- [ ] **Step 2: Rewrite `Awake()`'s combat-init block**

Find:
```csharp
        // Cache des composants combat
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<PNJAnimatorController>();
        _spawnPos    = transform.position;

        if (data != null && data.canFight)
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
                _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
        }
    }
```
Replace with:
```csharp
        // Cache des composants combat
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<PNJAnimatorController>();
        _spawnPos    = transform.position;

        if (data != null && data.canFight)
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
                _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;

            // Ajouté dynamiquement, pas via [RequireComponent] sur la classe — un PNJ non
            // combattant (civil, décoratif) ne doit jamais se voir forcer un NavMeshAgent par la
            // chaîne de RequireComponent de CombatAIController. Le prefab d'un PNJ canFight doit
            // déjà porter un NavMeshAgent (même exigence qu'avant cette migration) — sinon Unity
            // en ajoute un par défaut ici via le RequireComponent de CombatAIController, non
            // configuré, à corriger sur le prefab si ça arrive.
            _combatAI = gameObject.AddComponent<CombatAIController>();
            _combatAI.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
        }
    }
```

- [ ] **Step 3: Rewrite `Update()`**

Find the entire method:
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;

        // ── DEBUG TEMPORAIRE — diagnostic sink-au-sol, à retirer une fois trouvé ──
        if (data != null && data.canFight && _agent != null)
        {
            bool onMesh = _agent.isOnNavMesh;
            if (!onMesh || _agent.enabled == false)
                Debug.LogWarning($"[PNJ-DEBUG] {data.pnjName} Y={transform.position.y:F3} " +
                                  $"agentEnabled={_agent.enabled} onMesh={onMesh}");
        }

        // Timeout de secours (event d'impact jamais reçu) — même principe que Mob.cs.
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        // Poll d'interrupt de la canalisation ...
        if (_isChanneling)
        {
            bool hardCC = statusEffects != null && (statusEffects.isStunned ||
                          statusEffects.isShocked || statusEffects.isFreezed ||
                          statusEffects.isKnockedBack || statusEffects.isFeared ||
                          statusEffects.isSilenced);
            if (hardCC)
                InterruptChannelCast(voluntary: false, reason: "CC");
            else if (_channelTarget != null && _channelTarget.isDead)
                InterruptChannelCast(voluntary: true, reason: "cible morte");
        }

        if (data != null && data.canFight)
            HandleCombatAI();
    }
```
Replace with:
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;
        if (data == null || !data.canFight) return;

        // Poll d'interrupt de canalisation — DOIT rester avant le freeze CC ci-dessous : un hard
        // CC doit interrompre la canalisation EN COURS, pas être bloqué par le early-return sur
        // isCCd qui vient juste après.
        _combatAI.PollChannelInterrupt();

        RefreshEnemyList();

        // Stun/Sleep — CC dur, GDD §3.1.1.1 : "bloque TOUTES les actions". PNJ n'avait AUCUN
        // freeze sur CC dur avant cette migration (contrairement à Mob.Update()) — un Garde stun
        // continuait de bouger et d'attaquer. Bug latent trouvé pendant l'audit de la spec,
        // corrigé ici consciemment, pas juste un refactor.
        bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping ||
                     statusEffects.isShocked || statusEffects.isFreezed);
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = isCCd;
        if (isCCd) return;

        _combatAI.Tick();
    }
```
(The `[PNJ-DEBUG]` NavMeshAgent diagnostic is intentionally deleted here — it was already
confirmed useless during manual testing earlier in this project's history, this migration is the
natural point to remove it since this whole method is being rewritten anyway.)

- [ ] **Step 4: Add `RefreshEnemyList()` and rewrite `FindClosestEnemy()`**

Find:
```csharp
    /// <summary>
    /// Cherche l'entité ennemie la plus proche dans aggroRadius.
    /// Cibles actuelles : Mobs uniquement.
    /// À étendre pour FactionNPC vs FactionNPC (Phase 9).
    /// </summary>
    private Entity FindClosestEnemy()
    {
        if (data == null) return null;

        // Taunt (§3.1.1.1) force le ciblage sur la source du debuff, peu importe la proximité
        // normale — même schéma que Mob.GetClosestEnemy().
        if (statusEffects != null && statusEffects.isTaunted)
        {
            Entity tauntSource = statusEffects.GetDebuffSource(DebuffType.Taunt);
            if (tauntSource != null && !tauntSource.isDead) return tauntSource;
        }

        Collider[] hits    = Physics.OverlapSphere(transform.position, data.aggroRadius);
        Entity     closest = null;
        float      minDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            Mob mob = col.GetComponent<Mob>();
            if (mob == null || mob.isDead) continue;

            float dist = Vector3.Distance(transform.position, mob.transform.position);
            if (dist < minDist) { minDist = dist; closest = mob; }
        }
        return closest;
    }
```
Replace with:
```csharp
    /// <summary>Rafraîchit enemyList — même modèle que Mob.cs (spec §5bis) : Aggressive = scan
    /// de proximité (aggroRadius) ∪ aggroSet ; Passive = aggroSet seul.</summary>
    private void RefreshEnemyList()
    {
        enemyList.Clear();
        aggroSet.RemoveWhere(e => e == null || e.isDead);

        if (data.aiType == MobAIType.Aggressive)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, data.aggroRadius);
            foreach (Collider col in hits)
            {
                Mob mob = col.GetComponent<Mob>();
                if (mob == null || mob.isDead) continue;
                if (!enemyList.Contains(mob)) enemyList.Add(mob);
            }
        }

        foreach (Entity e in aggroSet)
            if (!enemyList.Contains(e)) enemyList.Add(e);
    }

    /// <summary>Cherche l'entité ennemie la plus proche dans enemyList. Cibles actuelles : Mobs
    /// uniquement. À étendre pour FactionNPC vs FactionNPC (Phase 9).</summary>
    public Entity FindClosestEnemy()
    {
        if (data == null) return null;

        // Taunt (§3.1.1.1) force le ciblage sur la source du debuff, peu importe la proximité
        // normale — même schéma que Mob.FindClosestEnemy().
        if (statusEffects != null && statusEffects.isTaunted)
        {
            Entity tauntSource = statusEffects.GetDebuffSource(DebuffType.Taunt);
            if (tauntSource != null && !tauntSource.isDead) return tauntSource;
        }

        Entity closest = null;
        float  minDist = float.MaxValue;

        foreach (Entity e in enemyList)
        {
            if (e == null || e.isDead) continue;
            float dist = Vector3.Distance(transform.position, e.transform.position);
            if (dist < minDist) { minDist = dist; closest = e; }
        }
        return closest;
    }

    // =========================================================
    // ICombatAIProfile — voir spec §5bis pour le détail de chaque membre
    // =========================================================
    public SkillData       BasicAttackSkill  => data?.basicAttackSkill;
    public List<SkillData> SecondarySkills   => data?.skills;
    public float           PatrolRadius      => data != null ? data.patrolRadius : 0f;
    public float           LeashDistance     => data != null ? data.leashRadius : 0f;
    public Vector3         LeashAnchor       => _spawnPos;
    public bool            AutoEngageOnSight => data != null && data.aiType == MobAIType.Aggressive;

    public bool HasAnyEnemyNearby() => enemyList.Count > 0;

    public void OnForcedEngage(Entity aggressor)
    {
        if (aggressor != null && !aggressor.isDead) aggroSet.Add(aggressor);
    }

    /// <summary>Ancre du leash FIXE au spawn pour PNJ (contrairement à Mob, dont l'ancre bouge à
    /// chaque nouvel engagement) — décision explicite de Florian, le Garde doit rester fidèle à
    /// son poste. Rien à faire ici.</summary>
    public void OnEngageStart() { }

    /// <summary>PAS de reset HP/Mana instantané pour PNJ (contrairement à Mob.OnReturnToPatrol())
    /// — décision explicite de Florian, baseRegenHP suffit.</summary>
    public void OnReturnToPatrol()
    {
        aggroSet.Clear();
    }
```

- [ ] **Step 5: Delete the entire combat-loop block**

Delete every method between the `// IA COMBAT` section header and the
`private void LookAt(Transform target)` utility method (exclusive of `LookAt` itself, which
stays — it's still used, now called from inside `CombatAIController`, but `PNJ.LookAt()` is no
longer called from `PNJ.cs`... actually it IS no longer called from anywhere in `PNJ.cs` after
this deletion, since `CombatAIController` has its own private `LookAt()`. Delete `PNJ.LookAt()`
too (it becomes dead code) — see the full deletion list below.):

Delete `HandleCombatAI()`, `TryUseSecondarySkill()`, `StartPendingHit()`, `OnAnimationHitEvent`
(a NEW, smaller version is re-added in Step 6, same as Task 4 Step 8), `ResolvePendingHit()`,
`StartChannelCast()`, `ResolveChannelCast()`, `InterruptChannelCast()`, `EndChannelCastState()`,
`GetMaxSkillRange()` (moved into `CombatAIController`), `ReturnToSpawn()`, and `LookAt()`. Also
delete the leftover fields `_combatTarget`, `_attackTimer`, `_skillCooldowns`, `_pendingSkill`,
`_pendingTarget`, `_pendingTimeout`, `_pendingIsMulti`, `_pendingMultiNextIndex`, `IsPendingHit`
(property — this one already deleted in Step 1's replacement, confirm it's gone),
`BasicRangeExitFactor`/`_wasInBasicRange` (already deleted in Step 1's replacement, confirm).

- [ ] **Step 6: Re-add a small `OnAnimationHitEvent` that forwards to the controller**

```csharp
    /// <summary>Reçoit l'Animation Event relayé par PNJAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0) => _combatAI?.OnAnimationHitEvent(hitIndex);
```
(`?.` here, unlike Mob's forward in Task 4 Step 8 — `_combatAI` is only ever assigned for
`canFight` PNJ, and a non-`canFight` PNJ has no `PNJAnimatorController` calling this in the first
place, but the null-conditional keeps this safe regardless.) Add it anywhere convenient, e.g.
right after the new `FindClosestEnemy()`/`ICombatAIProfile` block from Step 4.

- [ ] **Step 7: Add a `TakeDamage()` override — did not exist before this migration**

Add this new method (PNJ never overrode `TakeDamage()` before — pick any location, e.g. right
after `Awake()`):

```csharp
    // =========================================================
    // DÉGÂTS — aggro (n'existait pas avant cette migration : un PNJ frappé par un Mob ne
    // ripostait que si HandleCombatAI() retrouvait un ennemi par coïncidence via le scan de
    // proximité classique)
    // =========================================================
    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
    {
        base.TakeDamage(amount, sourceElement, source);

        if (!isDead && data != null && data.canFight)
            _combatAI.ForceEngage(source);
    }
```

- [ ] **Step 8: Update `Die()`'s cleanup lines**

Find:
```csharp
        base.Die();

        _agent?.ResetPath();
        _combatTarget = null;

        // Un PNJ (contrairement à un Mob) n'est jamais désactivé à sa mort — RespawnCoroutine()
        // repasse isDead = false après data.respawnDelay secondes. Sans ce nettoyage, un
        // pending-hit resté en vol (Mob/PNJ tué pendant l'anim de son attaque) résoudrait au
        // premier Update() après respawn — dégâts/zone/trajectoire fantômes depuis le point de
        // spawn (trouvé en vérification indépendante du plan).
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        // Même raisonnement pour la canalisation (sous-chantier 2). NE PAS appeler
        // _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-même, dans le bon
        // ordre (voir son commentaire, Step 7).
        if (_isChanneling)
            InterruptChannelCast(voluntary: false, reason: "mort");

        if (data.respawnDelay > 0f)
```
Replace with:
```csharp
        base.Die();

        // Un PNJ (contrairement à un Mob) n'est jamais désactivé à sa mort — RespawnCoroutine()
        // repasse isDead = false après data.respawnDelay secondes. Sans ce nettoyage, un
        // pending-hit resté en vol résoudrait au premier Update() après respawn — dégâts/zone/
        // trajectoire fantômes depuis le point de spawn.
        _combatAI?.NotifyDeath();

        if (data.respawnDelay > 0f)
```

- [ ] **Step 9: Compile check**

Open Unity, let it recompile.

Expected: 0 compile errors. Any error inside `PNJ.cs` about a missing field means Step 5's
deletion list missed something still referenced — check the exact field name in the error and
confirm it was on the deletion list; if it's a field this plan didn't anticipate, fix it and note
the discrepancy rather than silently improvising.

- [ ] **Step 10: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "refactor: migrate PNJ combat AI onto shared CombatAIController, add Passive/Aggressive + patrol"
```

---

### Task 6: Manual Play Mode verification (Florian)

Not automatable — no test framework in this project, and this task is entirely about observing
real NavMeshAgent/Animator/Physics behavior in the running game. Florian performs each check
below and reports back anything that doesn't match.

**Setup needed before testing:**
- One `MobData` asset with `aiType = Aggressive` (existing test mob is fine).
- One `MobData` asset with `aiType = Passive` (duplicate an existing one, flip `aiType`, or edit
  the existing test mob temporarily).
- One `PNJData` (Guard) asset with `patrolRadius > 0` and `aiType = Aggressive` (to test the new
  patrol).
- One `PNJData` (Guard) asset with `aiType = Passive` (to test the new Passive/Aggressive
  parity).
- A canFight PNJ prefab confirmed to already carry a `NavMeshAgent` component (Task 5 Step 2's
  comment flags this as a pre-existing requirement, now load-bearing for `CombatAIController`'s
  own `[RequireComponent(typeof(NavMeshAgent))]`).

**Checklist:**

1. Compile 0 errors, 0 new warnings.
2. Mob (`aiType = Aggressive`): patrols normally, engages a player at range, no Attack/Chase
   oscillation visible over a sustained fight (30s+, several attack/reposition cycles).
3. Mob (`aiType = Passive`): ignores a player passing nearby without being taunted/hit; engages
   the instant it's hit (`TakeDamage` → `ForceEngage(source)`). With two players nearby (B closer
   than A), if A lands the hit, the Mob targets A specifically, not B, even though B is closer.
   If the attacker then kites outside `detectionRange`, the Mob keeps chasing it (stays in
   `aggroSet`) instead of losing the target.
4. Mob: no more "walking in place" — `Speed` stays at 0 for the whole attack windup (can confirm
   visually; no debug logs remain to check numerically after this migration).
5. PNJ Guard (`aiType = Aggressive`, default): combat behavior unchanged from before this branch
   — secondary skill prioritized, basic attack otherwise, range respected, no regression.
6. PNJ Guard, hard CC (stun): now correctly freezes in place — this is new; previously it kept
   moving/attacking through a stun.
7. PNJ Guard with `patrolRadius > 0`: visibly roams within that radius when idle, returns to
   roaming it after a fight ends (not just back to a single fixed point).
8. PNJ Guard (`aiType = Passive`, new test asset): ignores a Mob walking by; engages the instant
   a Mob hits it (`TakeDamage` → `ForceEngage(source)`), targets that Mob specifically even if a
   different, non-attacking Mob is closer. On disengaging (leash/no target), `aggroSet` clears
   (`OnReturnToPatrol()`) — confirm NO instant HP/Mana refill happens for PNJ (unlike Mob), HP
   should recover only via `baseRegenHP` at its normal rate.
9. PNJ Guard leash: confirm it still measures from its fixed spawn point, not from wherever it
   started the fight — walk a Mob past a Guard, let the Guard chase it a good distance, confirm
   the Guard gives up based on distance-from-post, not distance-from-where-the-chase-began.
10. Channeling (`castTime > 0`) on both Mob and PNJ: still correctly interrupted by hard CC (full
    cooldown) and by target death (half cooldown) — no regression in `_channelBar`/
    `EndChannelCastState` ordering.
11. Leash (general): Mob and PNJ both return to spawn correctly beyond their respective leash
    distance, with no pending-hit/channel ghost-resolving after disengagement.
12. **New in this plan, not originally in the spec's own checklist**: confirm a Mob or PNJ no
    longer needs to get all the way to `GetMaxSkillRange() * 1.2` before re-engaging at melee —
    it should now commit to closing the gap the moment it's within `GetMaxSkillRange()` (no
    multiplier). This is an intentional simplification (Global Constraints, point 4) — the thing
    to watch for is any NEW oscillation this might introduce at that boundary; if one appears,
    report it rather than assuming it's expected, since removing that buffer was a judgment call
    made while writing this plan, not something Florian explicitly signed off on in the spec
    conversation.

If anything in this checklist fails, do not attempt a blind fix — use
`superpowers:systematic-debugging` the same way earlier bugs in this exact codebase were tracked
down this session (root-cause first, temporary diagnostic logs if needed, no guessing).
