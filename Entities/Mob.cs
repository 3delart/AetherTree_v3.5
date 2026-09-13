using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Linq;

// =============================================================
// MOB — Entité ennemie avec IA NavMesh 4 états
// Path : Assets/Scripts/Core/Mob.cs
// AetherTree GDD v3.5 — §3.3 (Mobs)
//
// IA : Patrol → Chase → Attack → Return (avec Leash) — GDD v3.5 §3.3
//
// Points clés :
// — enemyList<Entity> : joueurs + pets à portée de détection — §3.3
// — damageContributions<Player, float> : éligibilité loot ≥10% — §3.3
// — Cible = entité la plus proche dans enemyList (réévaluée à chaque tick) — §3.3
// — Die() calcule eligiblePlayers avant de publier MobKilledEvent — §3.3
// — Pet : dégâts attribués à son owner dans ResolveAttacker() — §3.5
// — Pas de regen HP/Mana (GDD §3.1 — joueur uniquement)
// =============================================================

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(StatusEffectSystem))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(SkillSystem))]
public class Mob : Entity
{
    [Header("Data")]
    public MobData data;
    public int     mobLevel = 1;

    protected NavMeshAgent agent;
    protected Vector3      spawnPos;
    protected MobState     currentState = MobState.Patrol;

    // Exposé pour MobAnimatorController — distingue walk (Patrol) de chase (Chase), impossible
    // via le seul paramètre Speed puisque les deux utilisent la même vitesse aujourd'hui.
    public MobState CurrentState => currentState;

    // ── enemyList — GDD v3.5 §3.3 ────────────────────────────
    // Contient joueurs ET pets à portée
    protected List<Entity> enemyList = new List<Entity>();

    // ── Contributions aux dégâts — GDD v3.5 §3.3 ────────────
    // Clé = Player, Valeur = dégâts totaux infligés
    // Les dégâts du Pet sont attribués à son owner
    protected Dictionary<Player, float>    damageContributions = new Dictionary<Player, float>();

    // Total RÉEL de tous les dégâts subis (Player + PNJ + tout le reste) — sert de dénominateur
    // pour le seuil d'éligibilité ≥10% (damageContributions ne trace QUE les Player, donc un
    // PNJ allié qui inflige la majorité des dégâts gonflait artificiellement le % du Player :
    // trouvé en test manuel, un Garde tuant un Mob à 95% laissait le Player "éligible" à 100%
    // sur les 5% qu'il avait réellement infligés).
    protected float totalDamageTaken = 0f;

    // ── Dernier skill utilisé par chaque attaquant ────────────
    // Mis à jour par SkillSystem via RegisterLastSkill() avant TakeDamage().
    // Null pour les kills DoT (le skill n'est pas connu au moment du tick).
    private Dictionary<Player, SkillData>   lastSkillByAttacker = new Dictionary<Player, SkillData>();

    private float attackTimer = 0f;
    public bool IsDashing { get; set; } = false;
    // Cooldown individuel par skill — clé = SkillData, valeur = temps restant
    private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();

    // ── Attente de résolution (hit-frame-sync — un seul hit en vol à la fois, un Mob n'agit
    // jamais en parallèle sur deux skills) — sous-chantier 1, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-animator-foundations-design.md ─────────────────────────────────
    private SkillData _pendingSkill   = null;
    private Entity    _pendingTarget  = null;
    private float     _pendingTimeout = 0f;
    private bool      _pendingIsMulti = false;
    private int       _pendingMultiNextIndex = 0;

    public bool IsPendingHit => _pendingSkill != null;

    private MobAnimatorController _animatorController;

    // ── Canalisation visible (castTime > 0) — sous-chantier 2, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-channel-vfxcast-design.md — mutuellement exclusif du mécanisme
    // _pendingSkill ci-dessus : un skill castTime > 0 ne passe JAMAIS par StartPendingHit() ────
    private bool           _isChanneling   = false;
    private SkillData      _channelSkill   = null;
    private Entity         _channelTarget  = null;
    private GameObject     _channelVfxCast = null;
    private CastBarSpawner _channelBar     = null;

    private System.Action onDeathCallback;

    // Patrouille
    private bool  patrolPointsSet = false;
    private bool  isWaiting       = false;
    private float waitTimer       = 0f;

    // Skills — cache du SkillSystem porté par ce GameObject
    private SkillSystem _skillSystem;

    // Aggro
    private Vector3     aggroPos;

    // =========================================================
    // INITIALISATION
    // =========================================================

    protected override void Awake()
    {
        base.Awake();
        agent               = GetComponent<NavMeshAgent>();
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<MobAnimatorController>();
        spawnPos = transform.position;
        ApplyData();
    }

       private void ApplyData()
    {
        if (data == null) return;
 
        entityName     = data.mobName;
        entityType     = EntityType.Mob;
        weaponCategory = data.weaponCategory;
 
        // ── Calcul des stats finales via MobStatCalculator ────
        // mobLevel est assigné par SpawnManager avant Awake().
        // Si le mob est placé directement en scène sans SpawnManager,
        // mobLevel vaut 1 (valeur par défaut du champ public).
        MobComputedStats s = MobStatCalculator.Calculate(data, mobLevel);
 
        // ── Push sur Entity via les setters ──────────────────
        SetMaxHP          (s.maxHP);
        SetMaxMana        (s.maxMana);
        SetAttackDamageMin(s.atkMin);
        SetAttackDamageMax(s.atkMax);
        SetMeleeDefense   (s.meleeDef);
        SetRangedDefense  (s.rangedDef);
        SetMagicDefense   (s.magicDef);
        SetPrecision      (s.precision);
        SetDodge          (s.dodge);
        SetCritChance     (s.critChance);
        SetCritMultiplier (s.critMultiplier);
        SetMoveSpeed      (data.moveSpeed);

        // ── Points élémentaires ───────────────────────────────
        // Poussés sur Entity pour que CombatSystem les lise via
        // GetElementalPoints() — même pipeline que le joueur. GDD §6.2.
        SetElementalPoints(data.elementType, s.elemPoints);
 
        if (s.regenHP   > 0f) SetRegenHP  (s.regenHP);
        if (s.regenMana > 0f) SetRegenMana(s.regenMana);
 
        currentHP   = maxHP;
        currentMana = maxMana;
        agent.speed = data.moveSpeed;
 
        // ── Résistances élémentaires — profil fixe du SO ─────
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            SetElementalResistance(e, data.GetElementalResistance(e));
 
        // ── Snapshot — base pour buffs/debuffs ────────────────
        SnapshotBaseStats();
    }

    // =========================================================
    // UPDATE — machine à états
    // =========================================================

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
        // morte (volontaire, CD demi). Les dégâts simples n'interrompent PAS, même règle que
        // SkillBar côté Player (vérifiée dans le code réel, pas une nouveauté). PAS de terme
        // isDead ici — Update() a déjà un early-return dessus juste au-dessus (ligne
        // `if (isDead || data == null) return;`), donc cette branche ne serait jamais atteinte ;
        // la mort est couverte séparément par le nettoyage explicite dans Die() (Step 7bis).
        // InterruptChannelCast() gère elle-même l'annulation de _channelBar (voir Step 6) — ne
        // JAMAIS appeler _channelBar?.Cancel() ici (trouvé en vérification indépendante du
        // plan : appeler Cancel() avant InterruptChannelCast ferait exécuter le callback
        // onCancel réentrant — () => InterruptChannelCast(voluntary: true, ...) — PENDANT que
        // _isChanneling est encore vrai, posant silencieusement le CD demi avant même que cet
        // appel explicite, potentiellement voluntary:false, n'atteigne son propre garde et
        // no-op — un hard CC finirait TOUJOURS avec le CD demi au lieu du CD complet).
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

        // Stun/Sleep — CC dur, GDD §3.1.1.1 : "bloque TOUTES les actions". Avant ce fix, seul
        // HandleAttack() vérifiait isStunned (l'attaque était bloquée mais le mob continuait
        // de patrouiller/chasser normalement) et isSleeping n'était vérifié NULLE PART pour
        // bloquer une action (seulement utilisé pour le réveil au premier dégât reçu) — gel
        // complet ici, un seul endroit, plutôt que dans chaque Handle* séparément.
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

    // =========================================================
    // ENEMYLIST — détection & nettoyage
    // GDD v3.5 §3.3 — enemyList<Entity> joueurs + pets
    // =========================================================

    /// <summary>
    /// Rafraîchit la liste des ennemis à portée de détection.
    /// Contient joueurs ET pets dans le rayon de détection.
    /// </summary>
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

    /// <summary>
    /// Retourne l'entité la plus proche dans enemyList — sauf si CE MOB LUI-MÊME subit Taunt
    /// (le debuff cible "Enemies" = le mob, pas le lanceur), auquel cas il cible systématiquement
    /// la SOURCE du debuff (GDD §3.1.1.1 : "force les ennemis à cibler cette entité" = celle qui
    /// a taunté), peu importe la proximité normale. Réévaluée à chaque tick — GDD v3.5 §3.3.
    /// </summary>
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

    // =========================================================
    // PATROL
    // =========================================================

    private void HandlePatrol()
    {
        // Taunt (GDD §3.1.1.1) force l'engagement même sur un mob Passif — le debuff cible
        // "Enemies" = CE mob (pas le lanceur), donc c'est statusEffects.isTaunted DE CE MOB
        // qu'il faut checker, pas scanner enemyList. Sans ce check, un mob Passif tauntée
        // ignorait purement et simplement l'appel au combat tant qu'il n'avait pas déjà pris
        // de dégâts (seul déclencheur normal de son aggro).
        bool isTaunted = statusEffects != null && statusEffects.isTaunted;

        if ((data.aiType == MobAIType.Aggressive || isTaunted) && enemyList.Count > 0)
        {
            BeginChase();
            return;
        }

        if (!patrolPointsSet)
        {
            agent.SetDestination(GetNavMeshPoint(spawnPos, data.patrolRadius));
            patrolPointsSet = true;
            return;
        }

        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaiting = false;
                agent.SetDestination(GetNavMeshPoint(spawnPos, data.patrolRadius));
            }
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f)
        {
            isWaiting = true;
            waitTimer = Random.Range(2f, 5f);
        }
    }

    // =========================================================
    // CHASE
    // =========================================================

    private void HandleChase()
    {
        if (IsBeyondLeash()) { GoReturn(); return; }

        if (statusEffects != null && statusEffects.isRooted)
        {
            agent.ResetPath();
            return;
        }

        if (statusEffects != null && statusEffects.isFeared)
        {
            Entity threat = GetClosestEnemy();
            if (threat != null)
            {
                Vector3 fleeDir = (transform.position - threat.transform.position).normalized;
                agent.SetDestination(transform.position + fleeDir * 5f);
            }
            return;
        }

        Entity target = GetClosestEnemy();
        if (target == null) { GoReturn(); return; }

        // Skill secondaire depuis Chase — dash, projectile, etc. Bloqué si Taunt actif : la
        // cible est déjà forcée sur la source du taunt via GetClosestEnemy(), Taunt force
        // AUSSI l'attaque de base uniquement, pas de skill spécial (§3.1.1.1).
        bool tauntedInChase = statusEffects != null && statusEffects.isTaunted;
        if (!tauntedInChase && TryUseSkill(target)) return;

        if (IsInRange(target, data.attackRange))
        {
            currentState = MobState.Attack;
            agent.ResetPath();
            return;
        }

        agent.SetDestination(target.transform.position);
    }

    // =========================================================
    // ATTACK
    // =========================================================

    private void HandleAttack()
    {
        if (isDead) return;

        if (IsBeyondLeash()) { GoReturn(); return; }

        if (statusEffects != null && (statusEffects.isStunned || statusEffects.isShocked || statusEffects.isFreezed)) return;

        Entity target = GetClosestEnemy();
        if (target == null || target.isDead) { GoReturn(); return; }

        if (!IsInRange(target, data.attackRange * 1.2f))
        {
            currentState = MobState.Chase;
            return;
        }

        LookAt(target.transform);

        // Skill secondaire prioritaire sur l'attaque de base — bloqué si Taunt actif, force
        // l'attaque de base uniquement sur la source du taunt (§3.1.1.1).
        bool tauntedInAttack = statusEffects != null && statusEffects.isTaunted;
        if (!tauntedInAttack && TryUseSkill(target)) return;

        // Attaque de base — bloquée tant qu'un pending-hit est en vol (StartPendingHit ci-
        // dessous) : sans cette garde, une fois le CD déplacé à la résolution (Step 7), plus
        // rien n'empêcherait ce bloc de se redéclencher à CHAQUE frame pendant l'anim (trouvé
        // en vérification indépendante du plan — voir Global Constraints).
        if (attackTimer <= 0f && !IsPendingHit && !_isChanneling)
        {
            attackTimer = data.attackCooldown;

            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                if (data.basicAttackSkill.castTime > 0f)
                    StartChannelCast(data.basicAttackSkill, target);
                else
                    StartPendingHit(data.basicAttackSkill, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
        }
    }

    // =========================================================
    // SKILL — tick CD + exécution (Chase ET Attack)
    // =========================================================

    /// <summary>
    /// Tick les cooldowns et tente d'exécuter le premier skill secondaire disponible.
    /// Appelé depuis HandleChase ET HandleAttack.
    /// Retourne true si un skill a été lancé.
    /// </summary>
    private bool TryUseSkill(Entity target)
    {
        // Un pending-hit OU une canalisation est déjà en vol — ne rien redéclencher tant que
        // l'un des deux n'est pas résolu. Retourne true pour que HandleAttack()/HandleChase()
        // traitent ce tick comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit || _isChanneling) return true;
        if (data.skills == null || data.skills.Count == 0) return false;

        // Tick des cooldowns
        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            if (_skillCooldowns.ContainsKey(skill))
                _skillCooldowns[skill] -= Time.deltaTime;
        }

        // Premier skill prêt et à portée
        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
            if (cd > 0f) continue;
            if (!IsInRange(target, skill.range)) continue;
            if (skill.manaCost > 0f && !HasMana(skill.manaCost)) continue;

            if (skill.manaCost > 0f) SpendMana(skill.manaCost);

            LookAt(target.transform);
            if (skill.castTime > 0f)
                StartChannelCast(skill, target);
            else
                StartPendingHit(skill, target);
            attackTimer = data.attackCooldown;
            return true;
        }

        return false;
    }

    /// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
    /// (dégâts/effets/zone/trajectoire) n'arrive qu'à l'event d'impact ou au timeout de
    /// secours, jamais ici. Sans attackAnimation assignée, résout immédiatement (comportement
    /// identique à avant ce sous-chantier). Un MultiHit SANS attackAnimation reste sur l'ancien
    /// chemin Execute()/ExecuteMultiHit (respecte HitStep.delay via coroutine) — il n'y a pas de
    /// frame d'impact à attendre, entrer dans le pending-hit ferait perdre ce délai entre coups
    /// (trouvé en vérification indépendante du plan).</summary>
    private void StartPendingHit(SkillData skill, Entity target)
    {
        // Fire-and-forget, comme SkillBar.StartInstant()/StartMultiHit()/LaunchComboHit() côté
        // Player — pas de handle stocké/nettoyé ici, contrairement à _channelVfxCast : un skill
        // instantané ne s'interrompt jamais avant résolution, le prefab gère sa propre durée de
        // vie (trouvé manquant lors d'une relecture du statut VFX Mob/PNJ).
        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, transform.position, Quaternion.identity);

        bool isMulti = skill.executionType == SkillExecutionType.MultiHit
                       && skill.hitSteps != null && skill.hitSteps.Count > 0;

        if (isMulti && skill.attackAnimation == null)
        {
            _skillSystem?.Execute(skill, this, target);
            if (data.skills != null && data.skills.Contains(skill))
                _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            return;
        }

        _animatorController?.PlayAttack(skill.attackAnimation);

        _pendingSkill   = skill;
        _pendingTarget  = target;
        _pendingIsMulti = isMulti;
        _pendingMultiNextIndex = 0;
        _pendingTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingTimeout <= 0f)
            ResolvePendingHit(0);
    }

    /// <summary>Reçoit l'Animation Event relayé par MobAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;   // event hors contexte, ignoré silencieusement
        ResolvePendingHit(hitIndex);
    }

    /// <summary>Résout le hit en attente — branchement à 3 voies identique à avant ce
    /// sous-chantier (hasDelayedImpact / isTrajectory / dispatch standard), sauf routage
    /// MultiHit par index. Pose le cooldown du skill secondaire APRÈS résolution (pas au
    /// déclenchement). Rejette un hitIndex hors séquence (event mal numéroté sur le clip —
    /// piège trouvé en task-review : Unity met souvent l'argument int par défaut à 0 sur
    /// CHAQUE event d'un clip MultiHit si on oublie de le changer, ce qui résoudrait le coup
    /// de base plusieurs fois au lieu des hitSteps distincts, silencieusement).</summary>
    private void ResolvePendingHit(int hitIndex)
    {
        if (_pendingSkill == null) return;

        SkillData skill   = _pendingSkill;
        Entity    target  = _pendingTarget;
        bool      isMulti = _pendingIsMulti;

        if (isMulti && hitIndex != _pendingMultiNextIndex)
        {
            Debug.LogWarning($"[MOB] Animation Event MultiHit reçu avec hitIndex={hitIndex}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        if (isMulti)
        {
            _skillSystem?.ResolveMultiHitStep(skill, this, target, hitIndex);
            _pendingMultiNextIndex++;
            int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
            if (_pendingMultiNextIndex < totalHits) return;   // encore des hits à venir
        }
        else
        {
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, this);
            else
                _skillSystem?.ResolveExecute(skill, this, target);
        }

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        // CD posé ici (résolution), pas au déclenchement — même principe que le chantier B côté
        // Player. Ne concerne que les skills secondaires (data.skills) — l'attaque de base
        // utilise attackTimer, déjà reposé au déclenchement dans HandleAttack().
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    // =========================================================
    // CANALISATION (castTime > 0) — sous-chantier 2
    // =========================================================

    private void StartChannelCast(SkillData skill, Entity target)
    {
        _isChanneling  = true;
        _channelSkill  = skill;
        _channelTarget = target;

        _animatorController?.PlayChannel(skill.channelAnimation);

        _channelVfxCast = skill.vfxCast != null
            ? Instantiate(skill.vfxCast, transform.position, Quaternion.identity)
            : null;

        _channelBar = CastBarSpawner.Show(
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            followTarget: transform,
            onComplete:   ResolveChannelCast,
            onCancel:     () => InterruptChannelCast(voluntary: true, reason: "bar volée")
        );
    }

    private void ResolveChannelCast()
    {
        if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

        SkillData skill  = _channelSkill;
        Entity    target = _channelTarget;

        EndChannelCastState();

        if (skill.hasDelayedImpact)
            _skillSystem?.PlantDelayedZone(skill, this, target);
        else if (skill.isTrajectory)
            _skillSystem?.StartTrajectory(skill, this);
        else
            _skillSystem?.Execute(skill, this, target);

        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    /// <summary>voluntary = true (cible morte, bar volée en interne — CD moitié) | false
    /// (CC/mort subie — CD complet). Même règle que SkillBar.InterruptChannel() côté Player.
    /// Capture _channelBar AVANT EndChannelCastState() puis annule la bar APRÈS — ORDRE
    /// CRITIQUE (trouvé en vérification indépendante du plan) : si la bar était annulée AVANT,
    /// son callback onCancel réentrant (() => InterruptChannelCast(voluntary: true, ...))
    /// s'exécuterait pendant que _isChanneling est encore vrai et poserait le CD demi avant que
    /// CET appel n'atteigne son propre garde — un hard CC finirait TOUJOURS avec le CD demi au
    /// lieu du CD complet, silencieusement. Seule cette méthode a le droit d'appeler
    /// _channelBar.Cancel() — tous les autres appelants (poll Update(), GoReturn(), Die())
    /// appellent seulement InterruptChannelCast(...) et laissent CETTE méthode gérer la bar.</summary>
    private void InterruptChannelCast(bool voluntary, string reason)
    {
        if (!_isChanneling) return;

        SkillData      skill = _channelSkill;
        CastBarSpawner bar   = _channelBar;

        EndChannelCastState();   // _isChanneling = false AVANT bar.Cancel() — voir résumé ci-dessus

        bar?.Cancel();           // callback onCancel réentrant tombe sur !_isChanneling, no-op
        _animatorController?.CancelChannel();

        if (data.skills != null && data.skills.Contains(skill))
        {
            // Même fallback 6f que ResolveChannelCast()/ResolvePendingHit() — sans lui, un
            // skill castTime>0 avec cooldown=0f interrompu par un hard CC repostait un CD de
            // 0f, et comme le garde de ré-entrée (_isChanneling) se referme dans la MÊME frame
            // que ce posage, rien n'empêchait un redéclenchement immédiat — boucle
            // instanciation/destruction par frame tant que le CC dure (trouvé en review finale,
            // exposition PNJ encore plus large : HandleCombatAI() n'a aucun garde-fou CC générique).
            float baseCd = skill.cooldown > 0f ? skill.cooldown : 6f;
            _skillCooldowns[skill] = voluntary ? baseCd * 0.5f : baseCd;
        }

        Debug.Log($"[MOB] Canalisation interrompue ({reason}) — {(voluntary ? "CD demi" : "CD complet")}.");
    }

    private void EndChannelCastState()
    {
        _isChanneling  = false;
        _channelSkill  = null;
        _channelTarget = null;

        if (_channelVfxCast != null) { Destroy(_channelVfxCast); _channelVfxCast = null; }
        // _channelBar n'est PAS annulé ici — InterruptChannelCast() s'en charge APRÈS cet appel
        // (voir son commentaire ci-dessus). ResolveChannelCast() (fin normale, la bar a déjà
        // terminé d'elle-même) n'a rien à annuler non plus. Juste null la référence.
        _channelBar = null;
    }

    // =========================================================
    // RETURN
    // =========================================================

    private void HandleReturn()
    {
        if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            agent.SetDestination(spawnPos);

        if (Vector3.Distance(transform.position, spawnPos) <= 2f)
        {
            agent.ResetPath();
            agent.Warp(spawnPos);
            FullReset();
        }
    }

    private void GoReturn()
    {
        currentState = MobState.Return;
        agent.SetDestination(spawnPos);

        // Un pending-hit en vol ne doit pas résoudre plus tard sur une cible désormais hors
        // combat — FullReset() (appelé seulement à l'ARRIVÉE au spawn, secondes plus tard) est
        // trop tardif pour ça, le timeout du pending a déjà quasi toujours résolu entre-temps.
        // GoReturn() est appelé au moment RÉEL du désengagement (leash dépassé, cible perdue),
        // donc c'est ici que le nettoyage doit avoir lieu (trouvé en review finale — parité
        // avec le nettoyage déjà posé au même moment côté PNJ.HandleCombatAI()).
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        // Même raisonnement pour la canalisation (sous-chantier 2) — un Mob qui se désengage en
        // pleine canalisation ne doit pas la voir résoudre plus tard sur une cible hors combat.
        // NE PAS appeler _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-
        // même, dans le bon ordre (voir son commentaire, Step 6).
        if (_isChanneling)
            InterruptChannelCast(voluntary: true, reason: "désengagement");
    }

    private void BeginChase()
    {
        isWaiting    = false;
        aggroPos     = transform.position;
        currentState = MobState.Chase;
    }

    private void FullReset()
    {
        currentHP         = maxHP;
        currentMana       = maxMana;
        isWaiting         = false;
        patrolPointsSet   = false;
        currentState      = MobState.Patrol;
        _skillCooldowns.Clear();

        // Un Mob qui rentre au spawn (leash) en plein milieu de l'anim de son attaque ne doit
        // pas voir ce coup résoudre plus tard sur une cible désormais hors combat — trouvé en
        // task-review.
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;
        enemyList.Clear();
        damageContributions.Clear();
        totalDamageTaken = 0f;
        lastSkillByAttacker.Clear();

        // Nettoie tous les effets actifs — un mob qui rentre au spawn repart sans debuffs. GDD v3.5 §3.3.
        statusEffects?.ResetDebuffResistances();
        RequestRecalculate();
    }

    // =========================================================
    // DÉGÂTS — aggro + contributions
    // GDD v3.5 §3.3
    // =========================================================

    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
    {
        if (isDead) return;

        // ── LOG : mob reçoit des dégâts ───────────────────────
        string sourceName = source != null ? source.entityName : "inconnu";


        // ── Attribution des contributions AVANT base.TakeDamage ──
        // Ordre critique : Die() est appelé dans base.TakeDamage si HP <= 0
        // Les contributions doivent être enregistrées avant pour que
        // eligiblePlayers soit correct dans Die() — même pour les kills DoT

        // totalDamageTaken compte TOUTE source (Player, PNJ, DoT...) — c'est le vrai
        // dénominateur du seuil ≥10%, contrairement à damageContributions qui ne trace que
        // les Player et gonflerait sinon leur % si un PNJ allié inflige le plus gros des dégâts.
        totalDamageTaken += amount;

        Player attacker = ResolveAttacker(source);
        if (attacker != null)
        {
            if (!damageContributions.ContainsKey(attacker))
                damageContributions[attacker] = 0f;
            damageContributions[attacker] += amount;
        }

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

    /// <summary>
    /// Résout le Player responsable des dégâts.
    /// Si source est un Pet, retourne pet.owner.
    /// </summary>
    private Player ResolveAttacker(Entity source)
    {
        if (source == null) return null;

        // Source directe = Player
        if (source is Player player) return player;

        // TODO: Pet.cs — décommenter quand Pet sera implémenté
        // if (source is Pet pet) return pet.owner;

        return null;
    }

    // =========================================================
    // MORT — éligibilité loot ≥10%
    // GDD v3.5 §3.3
    // =========================================================

    /// <summary>Effets On-Hit infligés de ce mob — source unique, MobData, pas d'agrégation
    /// (contrairement à Player qui combine plusieurs pièces d'équipement).</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => data?.onHitDealtEffects;

    /// <summary>Effets On-Hit reçus de ce mob — voir GetOnHitDealtEffects().</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => data?.onHitReceivedEffects;

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


        // ── Calcul dégâts totaux ──────────────────────────────
        // totalDamageTaken (toute source) — PAS la somme de damageContributions (Player
        // uniquement), sinon un PNJ allié qui inflige le gros des dégâts gonfle artificiellement
        // le % du Player (trouvé en test manuel).
        float totalDamage = totalDamageTaken;


        // ── Joueurs éligibles — ≥10% des dégâts totaux (XP joueur / loot) ────
        var eligiblePlayers = new List<Player>();
        if (totalDamage > 0f)
        {
            foreach (KeyValuePair<Player, float> entry in damageContributions)
            {
                if (entry.Value / totalDamage >= 0.10f)
                    eligiblePlayers.Add(entry.Key);
            }
        }

        // ── Joueurs contributeurs — ≥1 dégât (XP Esprit, GDD §5.8) ───────────
        var contributingPlayers = new List<Player>();
        foreach (KeyValuePair<Player, float> entry in damageContributions)
            if (entry.Value > 0f)
                contributingPlayers.Add(entry.Key);

        // ── Résolution du killerSkill ─────────────────────────
        // On prend le skill du joueur qui a infligé le plus de dégâts.
        // Null si le mob est mort d'un DoT pur sans skill direct enregistré.
        Player topContributor = null;
        float  topDamage      = 0f;
        foreach (KeyValuePair<Player, float> entry in damageContributions)
        {
            if (entry.Value > topDamage)
            {
                topDamage      = entry.Value;
                topContributor = entry.Key;
            }
        }
        SkillData killerSkill = null;
        if (topContributor != null)
            lastSkillByAttacker.TryGetValue(topContributor, out killerSkill);

        // ── LOG : résumé du kill ──────────────────────────────
        string killerSkillName = killerSkill != null ? killerSkill.name : "DoT / inconnu";
        string killerName      = topContributor != null ? topContributor.entityName : "inconnu";

        // ── UN SEUL publish — tout le reste s'abonne ─────────
        // XPSystem, LootManager, UnlockManager réagissent indépendamment.
        // Mob.Die() ne connaît plus ni Player, ni XPSystem, ni LootManager.
        GameEventBus.Publish(new MobKilledEvent
        {
            mob                 = data,
            mobLevel            = mobLevel,
            killerSkill         = killerSkill,
            killerWeapon        = topContributor?.equippedWeapon?.weaponType ?? WeaponType.Any,
            eligiblePlayers     = eligiblePlayers,
            contributingPlayers = contributingPlayers,
            deathPosition   = transform.position,
            wasStealth      = topContributor?.statusEffects?.isStealthed ?? false,
            wasUnarmed      = topContributor?.equippedWeapon == null,
            wasBoss         = data != null && data.aiType == MobAIType.Boss,
            locationID      = "",   // TODO : ZoneSystem
            isInParty       = eligiblePlayers.Count > 1,
        });

        onDeathCallback?.Invoke();
        Destroy(gameObject, 3f);
    }

    // =========================================================
    // UTILITAIRES PUBLICS
    // =========================================================

    public void OnDeath(System.Action callback) => onDeathCallback = callback;

    /// <summary>
    /// Enregistre le dernier skill utilisé par un attaquant.
    /// Appelé par SkillSystem.Execute() juste avant TakeDamage().
    /// Permet à Die() de remplir killerSkill dans MobKilledEvent.
    /// Pour les kills DoT, cette méthode n'est pas appelée — killerSkill sera null.
    /// </summary>
    public void RegisterLastSkill(Player attacker, SkillData skill)
    {
        if (attacker == null || skill == null) return;
        lastSkillByAttacker[attacker] = skill;
    }

    /// <summary>
    /// Force l'aggro depuis une source externe (ex: pet attaqué).
    /// Ajoute la source à enemyList si pas déjà présente.
    /// </summary>
    public void AggroFrom(Entity attacker)
    {
        if (isDead || attacker == null) return;
        if (!enemyList.Contains(attacker))
            enemyList.Add(attacker);
        aggroPos     = transform.position;
        isWaiting    = false;
        currentState = MobState.Chase;
    }

    /// <summary>True si le mob peut être capturé (HP sous le seuil).</summary>
    public bool IsCaptureable()
    {
        if (data == null || !data.isCapturable) return false;
        return HPPercent <= data.captureHPThreshold;
    }

    /// <summary>Retourne la contribution en % d'un joueur donné.</summary>
    public float GetDamageContribution(Player player)
    {
        if (!damageContributions.ContainsKey(player)) return 0f;
        // totalDamageTaken (toute source), même correction que Die() — voir son commentaire.
        return totalDamageTaken > 0f ? damageContributions[player] / totalDamageTaken : 0f;
    }

    // =========================================================
    // UTILITAIRES PRIVÉS
    // =========================================================

    private bool IsInRange(Entity target, float range)
    {
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.transform.position) <= range;
    }

    private bool IsBeyondLeash()
    {
        // ⚠ Ne pas court-circuiter sur enemyList vide :
        // si tous les ennemis sortent de la detectionRange, RefreshEnemyList vide la liste
        // mais le mob doit quand même rentrer s'il s'est éloigné de son aggroPos.
        float leash = data.detectionRange * data.leashMultiplier;
        return Vector3.Distance(transform.position, aggroPos) > leash;
    }

    private void LookAt(Transform target)
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
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

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Vector3 origin = Application.isPlaying ? spawnPos : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, data.detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, data.attackRange);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(origin, data.detectionRange * data.leashMultiplier);
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Gizmos.DrawWireSphere(origin, data.patrolRadius);
    }
}

public enum MobState { Patrol, Chase, Attack, Return }