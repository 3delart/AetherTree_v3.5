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

    // ── enemyList — GDD v3.5 §3.3 ────────────────────────────
    // Contient joueurs ET pets à portée
    protected List<Entity> enemyList = new List<Entity>();

    // ── Contributions aux dégâts — GDD v3.5 §3.3 ────────────
    // Clé = Player, Valeur = dégâts totaux infligés
    // Les dégâts du Pet sont attribués à son owner
    protected Dictionary<Player, float>    damageContributions = new Dictionary<Player, float>();

    // ── Dernier skill utilisé par chaque attaquant ────────────
    // Mis à jour par SkillSystem via RegisterLastSkill() avant TakeDamage().
    // Null pour les kills DoT (le skill n'est pas connu au moment du tick).
    private Dictionary<Player, SkillData>   lastSkillByAttacker = new Dictionary<Player, SkillData>();

    private float attackTimer = 0f;
    public bool IsDashing { get; set; } = false;
    // Cooldown individuel par skill — clé = SkillData, valeur = temps restant
    private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();
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
        agent           = GetComponent<NavMeshAgent>();
        _skillSystem    = GetComponent<SkillSystem>();
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
        bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping || statusEffects.isShocked);
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
            // Joueur
            Player player = col.GetComponent<Player>();
            if (player != null && !player.isDead)
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

        // Skill secondaire depuis Chase — dash, projectile, etc.
        if (TryUseSkill(target)) return;

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

        if (statusEffects != null && (statusEffects.isStunned || statusEffects.isShocked)) return;

        Entity target = GetClosestEnemy();
        if (target == null || target.isDead) { GoReturn(); return; }

        if (!IsInRange(target, data.attackRange * 1.2f))
        {
            currentState = MobState.Chase;
            return;
        }

        LookAt(target.transform);

        // Skill secondaire prioritaire sur l'attaque de base
        if (TryUseSkill(target)) return;

        // Attaque de base
        if (attackTimer <= 0f)
        {
            attackTimer = data.attackCooldown;

            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                _skillSystem?.Execute(data.basicAttackSkill, this, target);
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
            _skillSystem?.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            attackTimer = data.attackCooldown;
            return true;
        }

        return false;
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
        enemyList.Clear();
        damageContributions.Clear();
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
        this.enabled = false;


        // ── Calcul dégâts totaux ──────────────────────────────
        float totalDamage = 0f;
        foreach (float d in damageContributions.Values)
            totalDamage += d;


        // ── Joueurs éligibles — ≥10% des dégâts totaux ───────
        var eligiblePlayers = new List<Player>();
        if (totalDamage > 0f)
        {
            foreach (KeyValuePair<Player, float> entry in damageContributions)
            {
                if (entry.Value / totalDamage >= 0.10f)
                    eligiblePlayers.Add(entry.Key);
            }
        }

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
            mob             = data,
            killerSkill     = killerSkill,
            killerWeapon    = topContributor?.equippedWeapon?.weaponType ?? WeaponType.Any,
            eligiblePlayers = eligiblePlayers,
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
        float total = damageContributions.Values.Sum();
        return total > 0f ? damageContributions[player] / total : 0f;
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