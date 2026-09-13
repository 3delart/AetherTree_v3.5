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
public class PNJ : Entity
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
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
                _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
        }
    }

    // =========================================================
    // UPDATE
    // =========================================================

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

        // Poll d'interrupt de la canalisation — même règle que Mob.cs (hard CC = CD complet,
        // cible morte = CD demi, dégâts simples n'interrompent PAS). PAS de terme isDead ici —
        // même raison que Mob.cs (Update() a déjà un early-return dessus juste au-dessus) ; la
        // mort est couverte séparément par Die() (Step 8). InterruptChannelCast() gère elle-
        // même l'annulation de _channelBar — ne JAMAIS appeler _channelBar?.Cancel() ici (voir
        // le commentaire détaillé sur InterruptChannelCast(), Step 7 — appeler Cancel() avant
        // ferait exécuter le callback onCancel réentrant pendant que _isChanneling est encore
        // vrai, posant silencieusement le CD demi même sur un hard CC).
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

    private void HandleCombatAI()
    {
        if (_skillSystem == null) return;

        _attackTimer -= Time.deltaTime;

        // ── Vérification leash ────────────────────────────────
        if (data.leashRadius > 0f &&
            Vector3.Distance(transform.position, _spawnPos) > data.leashRadius)
        {
            // Trop loin du spawn — lâche la cible et rentre. Un pending-hit en vol ne doit pas
            // résoudre plus tard sur une cible désormais hors combat — trouvé en task-review
            // (même bug que Mob.FullReset()).
            _combatTarget   = null;
            _pendingSkill   = null;
            _pendingTarget  = null;
            _pendingTimeout = 0f;
            _pendingIsMulti = false;

            // Même raisonnement pour la canalisation (sous-chantier 2). NE PAS appeler
            // _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-même, dans le
            // bon ordre (voir son commentaire, Step 7).
            if (_isChanneling)
                InterruptChannelCast(voluntary: true, reason: "désengagement");

            ReturnToSpawn();
            return;
        }

        // Actualise la cible si nécessaire
        if (_combatTarget == null || _combatTarget.isDead)
            _combatTarget = FindClosestEnemy();

        if (_combatTarget == null || _combatTarget.isDead)
        {
            ReturnToSpawn();
            return;
        }

        // Gèle mouvement/décision tant qu'une canalisation est en vol — sans ce garde, rien
        // n'empêchait le PNJ de continuer à s'approcher (agent.SetDestination plus bas) pendant
        // son propre cast, ce qui n'a pas de sens visuellement (trouvé sur demande de Florian).
        // Le poll d'interrupt (hard CC/cible morte) continue de tourner dans Update(),
        // indépendant de ce gel ; le leash ci-dessus continue aussi de s'appliquer.
        if (_isChanneling) return;

        float dist        = Vector3.Distance(transform.position, _combatTarget.transform.position);
        float attackRange = GetMaxSkillRange();

        if (dist <= attackRange)
        {
            LookAt(_combatTarget.transform);
            // Skill secondaire bloqué si Taunt actif — force l'attaque de base uniquement sur
            // la source du taunt (§3.1.1.1), même schéma que Mob.HandleAttack. TryUseSecondarySkill()
            // vérifie déjà sa propre range par skill (skill.range) — rien à ajouter ici.
            bool tauntedNow = statusEffects != null && statusEffects.isTaunted;
            if (!tauntedNow && TryUseSecondarySkill(_combatTarget))
            {
                _agent?.ResetPath();
                return;
            }

            // Range de l'attaque de base spécifiquement — PAS `attackRange` (= GetMaxSkillRange(),
            // potentiellement la portée d'un skill secondaire bien plus longue). Trouvé en test
            // manuel : sans ce check séparé, le PNJ tirait son attaque de base depuis la portée du
            // spécial (ex: 10) au lieu de s'approcher jusqu'à SA propre portée (ex: 2.5).
            float basicRange = data.basicAttackSkill != null && data.basicAttackSkill.range > 0f
                ? data.basicAttackSkill.range
                : 2f;

            if (dist > basicRange)
            {
                _agent?.SetDestination(_combatTarget.transform.position);
                return;
            }

            _agent?.ResetPath();

            // Bloqué tant qu'un pending-hit OU une canalisation est en vol — même raison que
            // Mob.HandleAttack() (sans cette garde, plus rien n'empêche un redéclenchement à
            // chaque frame une fois le CD déplacé à la résolution ; le taunt court-circuite
            // TryUseSecondarySkill() donc sa propre garde ne couvre pas ce chemin, voir
            // Step 3bis).
            if (_attackTimer <= 0f && !IsPendingHit && !_isChanneling && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                {
                    if (data.basicAttackSkill.castTime > 0f)
                        StartChannelCast(data.basicAttackSkill, _combatTarget);
                    else
                        StartPendingHit(data.basicAttackSkill, _combatTarget);
                }
                // Cooldown de l'attaque de base = celui du SkillData lui-même (pas
                // PNJData.attackCooldown, retiré — trouvé en test manuel : ignoré, seule la
                // durée d'anim comptait, confusion pour Florian).
                _attackTimer = data.basicAttackSkill.cooldown > 0f ? data.basicAttackSkill.cooldown : 2f;

                // DEBUG TEMPORAIRE — diagnostic pause avant relais de l'attaque de base.
                Debug.Log($"[PNJ-DEBUG] t={Time.time:F2} BASIC '{data.basicAttackSkill.name}' déclenché, " +
                          $"_attackTimer posé à {_attackTimer:F2}s");
            }
        }
        else
        {
            _agent?.SetDestination(_combatTarget.transform.position);
        }
    }

    /// <summary>
    /// Tente d'utiliser le premier skill secondaire disponible (cooldown + portée + mana).
    /// Même logique que Mob.TryUseSkill().
    /// </summary>
    private bool TryUseSecondarySkill(Entity target)
    {
        // Tick des cooldowns — TOUJOURS, même pendant un pending-hit/canalisation en vol (basic
        // OU secondaire). Trouvé en test manuel : ce tick était placé APRÈS le early-return
        // ci-dessous, donc gelé chaque fois qu'une attaque de base était en vol (quasi en
        // permanence) — le CD du spécial ne progressait quasiment jamais en temps réel, ne
        // redevenait jamais disponible.
        if (data.skills != null)
        {
            foreach (var skill in data.skills)
            {
                if (skill == null) continue;
                if (_skillCooldowns.ContainsKey(skill))
                    _skillCooldowns[skill] -= Time.deltaTime;
            }
        }

        // Un pending-hit OU une canalisation est déjà en vol — ne rien redéclencher tant que
        // l'un des deux n'est pas résolu. Retourne true pour que HandleCombatAI() traite ce tick
        // comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit || _isChanneling) return true;
        if (data.skills == null || data.skills.Count == 0) return false;

        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
            if (cd > 0f) continue;

            float range = skill.range > 0f ? skill.range : 2f;
            if (Vector3.Distance(transform.position, target.transform.position) > range) continue;
            if (skill.manaCost > 0f && !HasMana(skill.manaCost)) continue;

            if (skill.manaCost > 0f) SpendMana(skill.manaCost);

            LookAt(target.transform);
            if (skill.castTime > 0f)
                StartChannelCast(skill, target);
            else
                StartPendingHit(skill, target);
            // _attackTimer NE DOIT PAS être touché ici — c'est le timer de l'attaque de base
            // uniquement (trouvé en test manuel : le partager avec les skills secondaires
            // bloquait le basic pour tout le CD du spécial, ex: un spécial à CD 10s empêchait
            // le basic de retirer pendant 10s au lieu de reprendre dès la fin de l'anim du
            // spécial). Le skill secondaire est déjà gardé indépendamment par
            // _skillCooldowns[skill] (posé dans ResolvePendingHit()/ResolveChannelCast()/
            // InterruptChannelCast()) — rien d'autre à faire ici.

            // DEBUG TEMPORAIRE — diagnostic pause avant relais de l'attaque de base.
            Debug.Log($"[PNJ-DEBUG] t={Time.time:F2} SPECIAL '{skill.name}' déclenché " +
                      $"(skill.cooldown={skill.cooldown:F2}, _attackTimer inchangé={_attackTimer:F2})");
            return true;
        }

        return false;
    }

    /// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
    /// n'arrive qu'à l'event d'impact ou au timeout de secours. Sans attackAnimation assignée,
    /// résout immédiatement (comportement identique à avant ce sous-chantier). Un MultiHit SANS
    /// attackAnimation reste sur l'ancien chemin Execute()/ExecuteMultiHit (respecte
    /// HitStep.delay via coroutine) plutôt que d'entrer dans le pending-hit.</summary>
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
            _skillSystem.Execute(skill, this, target);
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

    /// <summary>Reçoit l'Animation Event relayé par PNJAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;
        ResolvePendingHit(hitIndex);
    }

    /// <summary>Résout le hit en attente — même branchement à 3 voies qu'avant ce
    /// sous-chantier, sauf routage MultiHit par index. Pose le cooldown du skill secondaire
    /// APRÈS résolution. Rejette un hitIndex hors séquence (event mal numéroté sur le clip —
    /// piège trouvé en task-review sur Mob.cs : Unity met souvent l'argument int par défaut à
    /// 0 sur chaque event d'un clip MultiHit si on oublie de le changer).</summary>
    private void ResolvePendingHit(int hitIndex)
    {
        if (_pendingSkill == null) return;

        SkillData skill   = _pendingSkill;
        Entity    target  = _pendingTarget;
        bool      isMulti = _pendingIsMulti;

        if (isMulti && hitIndex != _pendingMultiNextIndex)
        {
            Debug.LogWarning($"[PNJ] Animation Event MultiHit reçu avec hitIndex={hitIndex}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        if (isMulti)
        {
            _skillSystem.ResolveMultiHitStep(skill, this, target, hitIndex);
            _pendingMultiNextIndex++;
            int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
            if (_pendingMultiNextIndex < totalHits) return;
        }
        else
        {
            if (skill.hasDelayedImpact)
                _skillSystem.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem.StartTrajectory(skill, this);
            else
                _skillSystem.ResolveExecute(skill, this, target);
        }

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

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
        if (!_isChanneling) return;

        SkillData skill  = _channelSkill;
        Entity    target = _channelTarget;

        EndChannelCastState();

        if (skill.hasDelayedImpact)
            _skillSystem.PlantDelayedZone(skill, this, target);
        else if (skill.isTrajectory)
            _skillSystem.StartTrajectory(skill, this);
        else
            _skillSystem.Execute(skill, this, target);

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
    /// _channelBar.Cancel() — tous les autres appelants (poll Update(), leash, Die())
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
            // 0f, et le garde de ré-entrée (_isChanneling) se referme dans la MÊME frame que ce
            // posage — boucle instanciation/destruction par frame tant que le CC dure. PNJ y est
            // encore plus exposé que Mob : HandleCombatAI() n'a aucun garde-fou CC générique
            // (trouvé en review finale).
            float baseCd = skill.cooldown > 0f ? skill.cooldown : 6f;
            _skillCooldowns[skill] = voluntary ? baseCd * 0.5f : baseCd;
        }

        Debug.Log($"[PNJ] Canalisation interrompue ({reason}) — {(voluntary ? "CD demi" : "CD complet")}.");
    }

    private void EndChannelCastState()
    {
        _isChanneling  = false;
        _channelSkill  = null;
        _channelTarget = null;

        if (_channelVfxCast != null) { Destroy(_channelVfxCast); _channelVfxCast = null; }
        // _channelBar n'est PAS annulé ici — InterruptChannelCast() s'en charge APRÈS cet appel
        // (voir son commentaire ci-dessus). ResolveChannelCast() (fin normale) n'a rien à
        // annuler non plus. Juste null la référence.
        _channelBar = null;
    }

    /// <summary>Plus grande range configurée parmi basicAttackSkill + data.skills — décide
    /// quand ce PNJ arrête de s'approcher pour engager (jamais PNJData.attackRange, retiré :
    /// toujours vérifier sur SkillData, trouvé en test manuel — un skill secondaire à longue
    /// portée obligeait sinon le PNJ à rentrer en mêlée avant de pouvoir l'utiliser).</summary>
    private float GetMaxSkillRange()
    {
        float max = data.basicAttackSkill != null && data.basicAttackSkill.range > 0f
            ? data.basicAttackSkill.range
            : 2f;

        if (data.skills != null)
        {
            foreach (var skill in data.skills)
            {
                if (skill == null) continue;
                if (skill.range > max) max = skill.range;
            }
        }

        return max;
    }

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

    private void ReturnToSpawn()
    {
        if (_agent == null) return;
        if (Vector3.Distance(transform.position, _spawnPos) > 2f)
            _agent.SetDestination(_spawnPos);
        else
            _agent.ResetPath();
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
        _skillCooldowns.Clear();

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;
        if (_agent != null) { _agent.enabled = true; _agent.Warp(_spawnPos); }

        Debug.Log($"[PNJ] {data.pnjName} respawné.");
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private void LookAt(Transform target)
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
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
            Gizmos.DrawWireSphere(transform.position, GetMaxSkillRange());
        }
    }
}
