using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// PNJ — Entité PNJ (Non-Player Character)
// Path : Assets/Scripts/Core/PNJ.cs
// AetherTree GDD v3.5 — §3.4 (PNJ)
//
// Types gérés (GDD v3.5 §3.4) :
//   Merchant     → ShopUI
//   Blacksmith   → ForgeUI (TODO Phase 6)
//   Antiquarian  → RuneUI (TODO Phase 6)
//   FusionNPC    → FusionUI (TODO Phase 6)
//   Quest        → QuestUI (TODO Phase 7)
//   Mayor        → Dialogue conditionnel + création guilde
//   FactionNPC   → Services faction Solthars / Umbrans (TODO Phase 9)
//   HarborMaster → Navigation bateau (TODO Phase 8)
//   Guard        → IA combat mobs + dialogue neutre
//   Decorative   → Dialogue lore/ambiance uniquement
//
// Mémoire joueur : le PNJ se souvient des joueurs connus via SaveSystem.
// Dialogue : SO DialogueData — stages numérotés + options cliquables.
//
// Combat (SkillSystem) :
//   Tout PNJ avec data.canFight == true et data.basicAttackSkill assigné
//   peut combattre — pas seulement les Gardes.
//   HandleCombatAI() est actif pour tout PNJ canFight, quel que soit
//   son pnjType. Un marchand itinérant, un garde, un PNJ de faction
//   utilisent tous le même chemin via SkillSystem.Execute().
//   Champs PNJData requis pour le combat :
//     canFight          : active l'IA de combat
//     basicAttackSkill  : SkillData de l'attaque de base (obligatoire si canFight)
//     skills            : liste de skills secondaires (optionnel)
//     aggroRadius       : rayon de détection des mobs
//     combatMoveSpeed   : vitesse en mode combat (0 = utilise moveSpeed)
//
//   Cooldown de l'attaque de base = basicAttackSkill.cooldown (PAS un champ PNJData séparé —
//   retiré, ignoré par erreur en pratique, source de confusion trouvée en test manuel).
//   Portée d'engagement = GetMaxSkillRange() (max entre basicAttackSkill.range et data.skills —
//   PAS PNJData.attackRange, retiré, même raison : toujours vérifier sur SkillData).
//
// Die() :
//   Tout PNJ avec data.canDie == true peut mourir et respawner.
//   data.canDie == false → invulnérable (civils, décoratifs).
//   Remplace l'ancienne condition if (pnjType != Guard).
// =============================================================

[RequireComponent(typeof(SkillSystem))]
public class PNJ : Entity, ICombatAIProfile
{
    // ── Data ──────────────────────────────────────────────────
    [Header("Data PNJ (assigner ici)")]
    public PNJData data;

    // ── Interaction ───────────────────────────────────────────
    [Header("Interaction")]
    [Tooltip("Rayon dans lequel le joueur peut interagir avec ce PNJ")]
    public float interactionRadius = 3f;

    // ── Mémoire joueurs connus ────────────────────────────────
    // Persisté via SaveSystem — GDD v3.5 §3.4
    private HashSet<string> knownPlayerIDs = new HashSet<string>();

    // ── Dialogue actif ────────────────────────────────────────
    private DialogueData  activeDialogue = null;
    private DialogueStage currentStage   = null;
    private Player        talkingTo      = null;

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

    // =========================================================
    // INITIALISATION
    // =========================================================

    protected override void Awake()
    {
        if (data != null)
        {
            entityName     = data.pnjName;
            entityType     = EntityType.PNJ;
            weaponCategory = data.weaponCategory;

            // Défenses — tous les PNJ (GDD v3.5 §3.4 : tous peuvent mourir si canDie)
            SetMeleeDefense (data.meleeDefense);
            SetRangedDefense(data.rangedDefense);
            SetMagicDefense (data.magicDefense);
            SetPrecision    (data.precision);
            SetDodge        (data.dodge);

            // HP / Mana / attaque — tous les PNJ canFight, pas seulement les Gardes
            if (data.canFight)
            {
                SetMaxHP          (data.baseMaxHP);
                SetMaxMana        (data.baseMaxMana);
                SetRegenHP        (data.baseRegenHP);
                SetAttackDamageMin(data.attackDamage);
                SetAttackDamageMax(data.attackDamage);
                SetMoveSpeed      (data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed);
            }
        }

        base.Awake();

        // Fige le snapshot — RequestRecalculate() repartira de ces valeurs
        SnapshotBaseStats();

        LoadKnownPlayersFromPrefs();

        // Cache des composants combat
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<PNJAnimatorController>();
        _spawnPos    = transform.position;

        if (data != null && data.canFight)
        {
            // Ajouté dynamiquement, pas via [RequireComponent] sur la classe — un PNJ non
            // combattant (civil, décoratif) ne doit jamais se voir forcer un NavMeshAgent par la
            // chaîne de RequireComponent de CombatAIController. `_agent` est relu APRÈS
            // AddComponent, PAS avant — CombatAIController requiert NavMeshAgent
            // ([RequireComponent]), donc si le prefab n'en portait pas déjà un (il le devrait,
            // même exigence qu'avant cette migration), Unity en ajoute un par défaut à l'instant
            // de cet AddComponent ; lire _agent avant ce point l'aurait laissé null, et
            // Initialize() aurait reçu ce null au lieu du NavMeshAgent réellement présent sur le
            // GameObject — trouvé en relisant ce plan avant exécution.
            _combatAI = gameObject.AddComponent<CombatAIController>();
            _agent    = GetComponent<NavMeshAgent>();
            _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
            _combatAI.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
        }
    }

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

    // =========================================================
    // UPDATE
    // =========================================================

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

    // =========================================================
    // INTERACTION JOUEUR
    // =========================================================

    /// <summary>
    /// Appelé quand un joueur interagit avec ce PNJ (touche E ou clic).
    /// Déclenche le dialogue approprié selon le type et la mémoire.
    /// </summary>
    public void Interact(Player player)
    {
        if (player == null || data == null || isDead) return;
        if (Vector3.Distance(transform.position, player.transform.position) > interactionRadius)
            return;

        talkingTo = player;

        // ── NotifyTalkTo — AVANT le switch ────────────────────
        // Notifie QuestSystem que le joueur parle à CE PNJ précis (par référence SO).
        // Valide uniquement les objectifs TalkTo qui ciblent exactement ce PNJData.
        QuestSystem.Instance?.NotifyTalkTo(data, player);

        switch (data.pnjType)
        {
            case PNJType.Merchant:     InteractMerchant(player);     break;
            case PNJType.Forge:        InteractForge(player);        break;
#pragma warning disable CS0618 // Rarity — retiré du design (Pari est un onglet de Forge), ordinal gardé
            case PNJType.Rarity:       InteractRarity(player);       break;
#pragma warning restore CS0618
            case PNJType.Antiquarian:  InteractAntiquarian(player);  break;
            case PNJType.Cordonnier:   InteractCordonnier(player);   break;
            case PNJType.Quest:        InteractQuest(player);        break;
            case PNJType.Guard:        InteractGuard(player);        break;
            case PNJType.Decorative:   InteractDecorative(player);   break;
            case PNJType.Mayor:        InteractMayor(player);        break;
#pragma warning disable CS0618
            case PNJType.FactionNPC:   InteractFactionNPC(player);   break;
#pragma warning restore CS0618
            case PNJType.HarborMaster: InteractHarborMaster(player); break;
            case PNJType.Cook:         InteractGenericShop(player);  break;
            case PNJType.Tinkerer:     InteractGenericShop(player);  break;
            case PNJType.Jeweler:      InteractGenericShop(player);  break;
            case PNJType.Hatter:       InteractGenericShop(player);  break;
            case PNJType.CraftStation: InteractGenericShop(player);  break;
        }

        RegisterKnownPlayer(player);
    }

    // ── Marchand ──────────────────────────────────────────────
    private void InteractMerchant(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
    }

    // ── PNJ composables génériques (Cook/Tinkerer/Jeweler/Hatter/CraftStation) ──
    // Dialogue d'abord — la fenêtre (PNJWindowUI → CraftPanelUI/ShopUI selon l'onglet)
    // ne s'ouvre qu'au clic sur l'option dont Action = OpenPNJWindow, même schéma
    // que Merchant/Forge/Antiquaire ci-dessus.
    private void InteractGenericShop(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
    }

    // ── Forgeron ──────────────────────────────────────────────
    // N'ouvre PAS la fenêtre directement — le dialogue s'affiche d'abord,
    // la fenêtre Forge (Boutique/Craft Équipement/Upgrade/Pari) s'ouvre seulement
    // quand le joueur clique l'option dont l'Action = DialogueAction.OpenForge (voir
    // HandleDialogueAction ci-dessous — seul point de dispatch, DialogueUI.OnOptionClicked
    // ne fait que forwarder le clic via SelectOption()).
    private void InteractForge(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
    }

    // ── PNJ Rareté ────────────────────────────────────────────
    // Même schéma que le Forgeron : dialogue d'abord, RarityUI ne s'ouvre
    // qu'au clic sur l'option dont l'Action = DialogueAction.OpenRarity.
    private void InteractRarity(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
    }

    // ── Antiquaire ────────────────────────────────────────────
    private void InteractAntiquarian(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
        // TODO Phase 6 : RuneUI.Instance?.Open(data, player)
        Debug.Log($"[PNJ/Antiquaire] {data.pnjName} — identification:{data.canIdentifyRunes} insertion:{data.canInsertRunes} (RuneUI Phase 6)");
    }

    // ── Cordonnier ────────────────────────────────────────────
    private void InteractCordonnier(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
        // TODO Phase 6 : fenêtre Cordonnier (Boutique/Fusion) — Instance?.Open(player)
        Debug.Log("[PNJ/Cordonnier] Fenêtre Fusion Phase 6");
    }

    // ── Quête ─────────────────────────────────────────────────
    private void InteractQuest(Player player)
    {
        DialogueData dialogue = SelectDialogue(player);
        if (dialogue != null) StartDialogue(dialogue, player);
    }

    // ── Garde ─────────────────────────────────────────────────
    private void InteractGuard(Player player)
    {
        if (data.defaultDialogue != null)
            StartDialogue(data.defaultDialogue, player);
    }

    // ── Décoratif ─────────────────────────────────────────────
    private void InteractDecorative(Player player)
    {
        DialogueData dialogue = SelectDialogue(player);
        if (dialogue != null) StartDialogue(dialogue, player);
    }

    // ── Maire ─────────────────────────────────────────────────
    private void InteractMayor(Player player)
    {
        DialogueData dialogue = player.CanCreateGuild()
            ? (data.guildUnlockDialogue   ?? data.defaultDialogue)
            : (data.guildNotReadyDialogue ?? data.defaultDialogue);
        StartDialogue(dialogue, player);
    }

    // ── PNJ Faction ───────────────────────────────────────────
    private void InteractFactionNPC(Player player)
    {
        // TODO Phase 9 : vérifier player.faction vs data.faction
        StartDialogue(SelectDialogue(player), player);
        Debug.Log($"[PNJ/Faction] {data.pnjName} ({data.faction}) — FactionSystem Phase 9");
    }

    // ── Capitaine de Port ─────────────────────────────────────
    private void InteractHarborMaster(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
        // TODO Phase 8 : HarborUI.Instance?.Open(data.availableDestinations, player)
        Debug.Log("[PNJ/Capitaine] HarborUI Phase 8");
    }

    // =========================================================
    // SÉLECTION DU DIALOGUE
    // GDD v3.5 §3.4 — dialogue selon réputation + mémoire
    // =========================================================

    private DialogueData SelectDialogue(Player player)
    {
        if (data == null) return null;

        if (data.reputationDialogueThreshold > 0 &&
            data.highReputationDialogue != null &&
            player.worldReputationRank >= data.reputationDialogueThreshold)
            return data.highReputationDialogue;

        if (data.knownPlayerDialogue != null && IsKnownPlayer(player))
            return data.knownPlayerDialogue;

        return data.defaultDialogue;
    }

    // =========================================================
    // SYSTÈME DE DIALOGUE
    // =========================================================

    private void StartDialogue(DialogueData dialogue, Player player)
    {
        if (dialogue == null)
        {
            Debug.LogWarning($"[PNJ] {data.pnjName} : aucun DialogueData assigné.");
            return;
        }

        activeDialogue = dialogue;
        currentStage   = dialogue.GetFirstStage();

        if (currentStage == null)
        {
            Debug.LogWarning($"[PNJ] {data.pnjName} : DialogueData sans stage.");
            return;
        }

        if (IsKnownPlayer(player))
        {
            while (currentStage != null && currentStage.skipIfKnown)
            {
                if (currentStage.options != null && currentStage.options.Count > 0)
                {
                    int nextID = currentStage.options[0].nextStageID;
                    currentStage = nextID >= 0 ? dialogue.GetStage(nextID) : null;
                }
                else break;
            }
        }

        if (currentStage == null)
        {
            Debug.LogWarning($"[PNJ] {data.pnjName} : plus aucun stage après skip.");
            return;
        }

        DialogueUI.Instance?.OpenDialogue(this, currentStage, player);
    }

    public void SelectOption(DialogueOption option, Player player)
    {
        if (option == null || activeDialogue == null) return;

        HandleDialogueAction(option.action, player);

        if (option.nextStageID == -1) { EndDialogue(); return; }

        DialogueStage nextStage = activeDialogue.GetStage(option.nextStageID);
        if (nextStage == null)
        {
            Debug.LogWarning($"[PNJ] Stage {option.nextStageID} introuvable dans {activeDialogue.dialogueName}");
            EndDialogue();
            return;
        }

        currentStage = nextStage;
        GrantStageRewards(currentStage, player);

        if (currentStage.options == null || currentStage.options.Count == 0)
        {
            DialogueUI.Instance?.ShowStage(currentStage);
            if (!currentStage.isDynamicQuestStage) EndDialogue();
            return;
        }

        DialogueUI.Instance?.ShowStage(currentStage);
    }

    private void GrantStageRewards(DialogueStage stage, Player player)
    {
        if (stage == null || player == null) return;

        if (stage.rewardXP > 0)
            player.AddCombatXP(stage.rewardXP);

        if (stage.rewardAeris > 0)
            Debug.Log($"[PNJ] Récompense Aeris : +{stage.rewardAeris} (CurrencySystem Phase 5)");

        if (!string.IsNullOrEmpty(stage.rewardItemID))
            Debug.Log($"[PNJ] Récompense item : {stage.rewardItemID} (InventorySystem Phase 5)");

        if (stage.rewardWorldRep != 0)
            player.AddWorldReputation(stage.rewardWorldRep);
    }

    private void HandleDialogueAction(DialogueAction action, Player player)
    {
        switch (action)
        {
            case DialogueAction.OpenShop:           ShopUI.Instance?.OpenShop(data, player); break;
            case DialogueAction.OpenForge:          ForgeUI.Instance?.Open(); break;
            case DialogueAction.OpenRarity:          RarityUI.Instance?.Open(); break;
            case DialogueAction.OpenPNJWindow:       PNJWindowUI.Instance?.Open(data, player); break;
            case DialogueAction.OpenRuneUI:         Debug.Log("[PNJ] OpenRuneUI — RuneUI Phase 6");   break;
            case DialogueAction.OpenFusionUI:       FusionUI.Instance?.Open(data, player); break;
            case DialogueAction.OpenQuestLog:       Debug.Log("[PNJ] OpenQuestLog — QuestUI Phase 7");  break;
            case DialogueAction.OpenHarborUI:       Debug.Log("[PNJ] OpenHarborUI — HarborUI Phase 8"); break;
            case DialogueAction.TriggerGuildCreation: TryCreateGuild(player); break;
            case DialogueAction.CloseDialogue:      EndDialogue(); break;
        }
    }

    private void EndDialogue()
    {
        activeDialogue = null;
        currentStage   = null;
        talkingTo      = null;
        DialogueUI.Instance?.CloseDialogue();
    }

    // =========================================================
    // CRÉATION DE GUILDE — GDD v3.5 §3.4
    // =========================================================

    private void TryCreateGuild(Player player)
    {
        if (!player.CanCreateGuild())
        {
            Debug.Log("[PNJ/Maire] Condition non remplie — 20 membres uniques requis.");
            return;
        }
        // TODO: vérifier Aeris joueur >= data.guildCreationCost
        // TODO: GuildSystem.Instance?.CreateGuild(player, data.guildCreationCost)
        Debug.Log($"[PNJ/Maire] Création de guilde débloquée — Coût : {data.guildCreationCost} Aeris (Phase 9)");
    }

    // =========================================================
    // MÉMOIRE JOUEURS — GDD v3.5 §3.4
    // =========================================================

    public void RegisterKnownPlayer(Player player)
    {
        if (player == null) return;
        string id = player.entityName;
        if (knownPlayerIDs.Add(id))
        {
            SaveKnownPlayersToPrefs();
            Debug.Log($"[PNJ] {data.pnjName} mémorise le joueur : {id}");
        }
    }

    private string PrefsKey => $"PNJ_Known_{data?.name ?? gameObject.name}";

    private void SaveKnownPlayersToPrefs()
    {
        PlayerPrefs.SetString(PrefsKey, string.Join("|", knownPlayerIDs));
        PlayerPrefs.Save();
    }

    private void LoadKnownPlayersFromPrefs()
    {
        string saved = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(saved)) return;
        knownPlayerIDs.Clear();
        foreach (string id in saved.Split('|'))
            if (!string.IsNullOrEmpty(id)) knownPlayerIDs.Add(id);
    }

    public bool IsKnownPlayer(Player player)
    {
        if (player == null) return false;
        return knownPlayerIDs.Contains(player.entityName); // TODO: player.playerID
    }

    // =========================================================
    // IA COMBAT — commun à tous les PNJ canFight
    // GDD v3.5 §3.4
    //
    // Comportement :
    //   1. Cherche la cible la plus proche dans aggroRadius
    //   2. Se déplace vers elle (si NavMeshAgent présent)
    //   3. À portée : tente d'abord un skill secondaire,
    //      sinon utilise basicAttackSkill via SkillSystem.Execute()
    //   4. Sans cible : retourne au spawnPos
    //
    // Cibles : Mobs uniquement (les PNJ ne s'attaquent pas entre eux
    // sauf cas spéciaux futurs — FactionNPC vs FactionNPC en PvP).
    // =========================================================

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

    /// <summary>Reçoit l'Animation Event relayé par PNJAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0) => _combatAI?.OnAnimationHitEvent(hitIndex);

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

    // =========================================================
    // MORT & RESPAWN — GDD v3.5 §3.4
    // data.canDie contrôle qui peut mourir — plus de hardcode pnjType.
    // =========================================================

    /// <summary>Effets On-Hit infligés de ce PNJ — source unique, PNJData.</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => data?.onHitDealtEffects;

    /// <summary>Effets On-Hit reçus de ce PNJ — voir GetOnHitDealtEffects().</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => data?.onHitReceivedEffects;

    protected override void Die()
    {
        // PNJ sans canDie (civils, décoratifs) → invulnérables
        if (data == null || !data.canDie) return;

        base.Die();

        // Un PNJ (contrairement à un Mob) n'est jamais désactivé à sa mort — RespawnCoroutine()
        // repasse isDead = false après data.respawnDelay secondes. Sans ce nettoyage, un
        // pending-hit resté en vol résoudrait au premier Update() après respawn — dégâts/zone/
        // trajectoire fantômes depuis le point de spawn.
        _combatAI?.NotifyDeath();

        if (data.respawnDelay > 0f)
            StartCoroutine(RespawnCoroutine());

        Debug.Log($"[PNJ] {data.pnjName} mort — respawn dans {data.respawnDelay}s");
    }

    private IEnumerator RespawnCoroutine()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (_agent != null) _agent.enabled = false;

        yield return new WaitForSeconds(data.respawnDelay);

        isDead             = false;
        currentHP          = maxHP;
        currentMana        = maxMana;
        transform.position = _spawnPos;
        _combatAI?.ResetCooldowns();

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;
        if (_agent != null) { _agent.enabled = true; _agent.Warp(_spawnPos); }

        Debug.Log($"[PNJ] {data.pnjName} respawné.");
    }

    // =========================================================
    // ACCESSEURS
    // =========================================================

    public PNJType       PNJType      => data != null ? data.pnjType : global::PNJType.Decorative;
    public string        PNJName      => data != null ? data.pnjName : entityName;
    public bool          IsTalking    => talkingTo != null;
    public DialogueStage CurrentStage => currentStage;

    // =========================================================
    // GIZMOS
    // =========================================================

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
