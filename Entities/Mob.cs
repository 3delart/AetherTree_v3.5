using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

// =============================================================
// MOB — Entité ennemie, IA déléguée à CombatAIController
// Path : Assets/Scripts/Core/Mob.cs
// AetherTree GDD v3.5 — §3.3 (Mobs)
//
// IA : Mob se contente d'implémenter ICombatAIProfile et de piloter le CombatAIController
// partagé (Combat/CombatAIController.cs), qui gère lui-même la machine à 3 états
// Patrol → Engage → Return (avec Leash) — GDD v3.5 §3.3
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
[RequireComponent(typeof(CombatAIController))]
[RequireComponent(typeof(CombatEntityAnimatorController))]
public class Mob : Entity, ICombatAIProfile, ICombatAnimatorProfile
{
    [Header("Data")]
    public MobData data;
    public int     mobLevel = 1;

    protected NavMeshAgent agent;
    protected Vector3      spawnPos;
    private   CombatAIController _combatAI;
    private   Vector3            aggroPos;

    // Exposé pour CombatEntityAnimatorController — voir CombatAIController pour le sens de chaque état.
    public CombatAIState CurrentState => _combatAI.CurrentState;

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

    public bool IsDashing { get; set; } = false;

    private CombatEntityAnimatorController _animatorController;

    private System.Action onDeathCallback;

    // ── Aggro (§5bis de la spec) — Passive ne cible QUE ce qui l'a frappé ; Aggressive garde le
    // scan de proximité ET mémorise en plus quiconque le frappe (utile si l'attaquant sort
    // ensuite de detectionRange en kitant). Vidé uniquement dans OnReturnToPatrol().
    private HashSet<Entity> aggroSet = new HashSet<Entity>();

    // Skills — cache du SkillSystem porté par ce GameObject
    private SkillSystem _skillSystem;

    // =========================================================
    // INITIALISATION
    // =========================================================

    protected override void Awake()
    {
        base.Awake();
        agent               = GetComponent<NavMeshAgent>();
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<CombatEntityAnimatorController>();
        _combatAI           = GetComponent<CombatAIController>();
        spawnPos = transform.position;
        aggroPos = spawnPos;
        ApplyData();
        _combatAI.Initialize(this, agent, _skillSystem, this, _animatorController, spawnPos);
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

        // ── Résistances aux debuffs — innées, indépendantes de l'équipement (les Mobs n'en ont
        // pas). CharacterStats.ApplyDebuffResistances() est strictement réservée au Player, donc
        // sans ce câblage statusEffects._debuffResistances resterait TOUJOURS vide pour un Mob.
        ApplyInnateDebuffResistances();

        // ── Snapshot — base pour buffs/debuffs ────────────────
        SnapshotBaseStats();
    }

    /// <summary>Pousse data.debuffResistances dans statusEffects — appelée depuis ApplyData()
    /// (spawn) ET OnReturnToPatrol() (celui-ci fait un ResetDebuffResistances() qui viderait
    /// définitivement les résistances innées sans cette ré-application — bug trouvé en review
    /// finale : un boss configuré immunisé au déplacement/CC redevenait vulnérable après son
    /// premier leash).</summary>
    private void ApplyInnateDebuffResistances()
    {
        if (data == null || data.debuffResistances == null) return;
        foreach (var entry in data.debuffResistances)
            statusEffects.SetDebuffResistance(entry.debuffType, entry.resistChance);
    }

    // =========================================================
    // UPDATE — machine à états
    // =========================================================

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

    /// <summary>
    /// Retourne l'entité la plus proche dans enemyList — sauf si CE MOB LUI-MÊME subit Taunt
    /// (le debuff cible "Enemies" = le mob, pas le lanceur), auquel cas il cible systématiquement
    /// la SOURCE du debuff (GDD §3.1.1.1 : "force les ennemis à cibler cette entité" = celle qui
    /// a taunté), peu importe la proximité normale. Réévaluée à chaque tick — GDD v3.5 §3.3.
    /// </summary>
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

    /// <summary>Ancien corps de FullReset() — HP/Mana à 100%. Les cooldowns de skills
    /// secondaires sont vidés par CombatAIController.TickReturn() via ResetCooldowns(), appelé
    /// juste avant ce hook ; le pending-hit/la canalisation en cours ont déjà été nettoyés plus
    /// tôt, dès l'entrée en Return (GoReturn()). Il ne reste ici que ce qui est propre à Mob
    /// (contributions, aggro, debuffs).</summary>
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
        ApplyInnateDebuffResistances();
        RequestRecalculate();
    }

    /// <summary>Reçoit l'Animation Event relayé par CombatEntityAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0) => _combatAI.OnAnimationHitEvent(hitIndex);

    // =========================================================
    // ICombatAnimatorProfile — voir World/CombatEntityAnimatorController.cs
    // =========================================================
    public AnimationClip IdleClip  => data?.idleClip;
    public AnimationClip WalkClip  => data?.walkClip;
    public AnimationClip ChaseClip => data?.chaseClip;

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
        // Tout mob agressé entre en combat même s'il est Passif. `source` BRUT, pas `attacker`
        // résolu ci-dessus (qui vaut null pour tout ce qui n'est pas un Player) — un Mob frappé
        // par un PNJ Garde doit quand même le cibler en retour (trouvé en écrivant le plan).
        if (!isDead)
            _combatAI.ForceEngage(source);
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

        // Nettoyage pending-hit/canalisation AVANT de désactiver quoi que ce soit — sinon la
        // barre/le vfxCast/l'anim resteraient affichés sur un Mob déjà mort.
        _combatAI.NotifyDeath();
        agent.enabled = false;

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
        _combatAI.ForceEngage(attacker);
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
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Vector3 origin = Application.isPlaying ? spawnPos : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, data.detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _combatAI != null ? _combatAI.EngageRange : 0f);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(origin, data.detectionRange * data.leashMultiplier);
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Gizmos.DrawWireSphere(origin, data.patrolRadius);
    }
}