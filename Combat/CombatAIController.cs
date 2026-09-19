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

/// <summary>Pont vers CombatEntityAnimatorController (composant unique partagé Mob/PNJ, voir
/// World/CombatEntityAnimatorController.cs).</summary>
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
    float           LeashDistance     { get; }   // distance MAX autorisée depuis LeashAnchor ; <= 0 = illimité (jamais de désengagement auto)
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
        // LeashDistance <= 0 = illimité, jamais de désengagement automatique — préserve le
        // comportement actuel de PNJData.leashRadius (0 documenté comme "illimité" sur le champ
        // lui-même) ; trouvé en relisant ce plan avant exécution, sans ce garde un Garde
        // configuré à 0 se désengagerait dès son premier pas hors spawn au lieu de ne jamais
        // lâcher prise.
        if (_profile.LeashDistance > 0f &&
            Vector3.Distance(_owner.transform.position, _profile.LeashAnchor) > _profile.LeashDistance)
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
            ResetCooldowns();
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

    /// <summary>Relais direct depuis CombatEntityAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;
        ResolvePendingHit(hitIndex);
    }

    /// <summary>Vide tous les cooldowns de skills secondaires — utilisé par
    /// PNJ.RespawnCoroutine() (un PNJ qui respawn après sa mort repart à zéro, contrairement à un
    /// simple désengagement via OnReturnToPatrol(), qui ne touche pas aux cooldowns). Trouvé en
    /// relisant PNJ.cs pendant la revue finale de ce plan : RespawnCoroutine() fait
    /// `_skillCooldowns.Clear()` directement aujourd'hui, un champ que cette migration retire de
    /// PNJ — sans cette méthode, Task 5 casserait la compilation.</summary>
    public void ResetCooldowns()
    {
        _skillCooldowns.Clear();
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
            // isTrajectory vérifié EN PREMIER — un skill Cone/Target/GroundTarget avec les deux flags
            // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par
            // StartTrajectory(), qui plante lui-même la zone différée en plus du dégât immédiat.
            // Priorité inversée sans risque pour tout le reste : un skill qui n'a qu'un seul des
            // deux flags actif se comporte identiquement peu importe l'ordre des checks.
            if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, _owner, target);
            else if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, _owner, target);
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

        // Ordre inversé — voir commentaire équivalent dans ResolvePendingHit().
        if (skill.isTrajectory)
            _skillSystem?.StartTrajectory(skill, _owner, target);
        else if (skill.hasDelayedImpact)
            _skillSystem?.PlantDelayedZone(skill, _owner, target);
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
        // isOnNavMesh gardé — trouvé en test manuel : ResetPath() jette
        // "can only be called on an active agent that has been placed on a NavMesh" si l'agent
        // est temporairement hors-mesh (knockback, spawn, glitch de collision) ; ni Mob.cs ni
        // PNJ.cs ne garantissaient ce cas avant la migration non plus, mais cette méthode est
        // maintenant le point de passage unique pour les deux, donc le garde-fou vit ici.
        if (_agent.isOnNavMesh) _agent.ResetPath();
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
