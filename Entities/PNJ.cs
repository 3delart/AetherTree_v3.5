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
//   FactionNPC   → Services faction Solthars / Umbrans (TODO Phase 9)
//   Teleporter   → Téléportation vers une destination choisie (TODO Phase 8)
//   Guard        → IA combat mobs + dialogue neutre
//   Decorative   → Dialogue lore/ambiance uniquement
//
// Mémoire joueur : le PNJ se souvient des joueurs connus via SaveSystem.
// Dialogue : SO DialogueData — stages numérotés + options cliquables.
//
// Combat (SkillSystem) :
//   Tout PNJ avec data.canFight == true et data.basicAttackSkill assigné
//   peut combattre — pas seulement les Gardes.
//   PNJ implémente ICombatAIProfile et délègue l'IA au CombatAIController partagé
//   (Combat/CombatAIController.cs, machine à 3 états Patrol/Engage/Return) pour tout PNJ
//   canFight, quel que soit son pnjType. Un marchand itinérant, un garde, un PNJ de faction
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
//   Portée d'engagement = CombatAIController.EngageRange (max entre basicAttackSkill.range et
//   data.skills — PAS PNJData.attackRange, retiré, même raison : toujours vérifier sur SkillData).
//
// Die() :
//   Tout PNJ avec data.canDie == true peut mourir et respawner.
//   data.canDie == false → invulnérable (civils, décoratifs).
//   Remplace l'ancienne condition if (pnjType != Guard).
// =============================================================

[RequireComponent(typeof(SkillSystem))]
public class PNJ : Entity, ICombatAIProfile, ICombatAnimatorProfile
{
    // ── Data ──────────────────────────────────────────────────
    [Header("Data PNJ (assigner ici)")]
    public PNJData data;

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
             "au bouclage sur patrolPoints[0] (ignoré au DERNIER point si loopRoute = false, voir\n" +
             "waitBeforeDisappearSeconds ci-dessous).")]
    [ShowIf(nameof(isPatrolRoute), true)]
    public float waitAtPointSeconds = 3f;

    [Tooltip("Coché (défaut) = boucle sans fin sur patrolPoints[0] (comportement historique).\n" +
             "Décoché = s'arrête au DERNIER point de la liste au lieu de reboucler — voir\n" +
             "waitBeforeDisappearSeconds / hiddenDurationSeconds ci-dessous.")]
    [ShowIf(nameof(isPatrolRoute), true)]
    public bool loopRoute = true;

    [Tooltip("Idle au DERNIER point avant de disparaître (loopRoute = false uniquement) — distinct\n" +
             "de waitAtPointSeconds, pour régler ce point final indépendamment des autres.")]
    [ShowIf(nameof(isPatrolRoute), true, AndField = nameof(loopRoute), AndValue = false)]
    public float waitBeforeDisappearSeconds = 3f;

    [Tooltip("Durée pendant laquelle le PNJ est complètement absent (invisible, inattaquable,\n" +
             "non-interactible) avant de réapparaître à patrolPoints[0] et reprendre la route.")]
    [ShowIf(nameof(isPatrolRoute), true, AndField = nameof(loopRoute), AndValue = false)]
    public float hiddenDurationSeconds = 5f;

    [Tooltip("Durée d'attente à patrolPoints[0] avant le PREMIER départ de ce cycle — distincte de\n" +
             "waitAtPointSeconds (utilisé partout ailleurs sur la route). Pendant cette fenêtre,\n" +
             "IsAcceptingEscort vaut true : une quête Escort ciblant ce PNJ est \"récupérable\".\n" +
             "Dès le départ (fin de cette attente), la quête n'est plus proposable jusqu'au\n" +
             "prochain cycle (réapparition après hiddenDurationSeconds, ou respawn après mort).")]
    [ShowIf(nameof(isPatrolRoute), true, AndField = nameof(loopRoute), AndValue = false)]
    public float escortAcceptWindowSeconds = 300f;

    /// <summary>True uniquement pendant l'attente initiale à patrolPoints[0], avant le premier
    /// départ de ce cycle — voir escortAcceptWindowSeconds ci-dessus. Lu par
    /// QuestSystem.CanAccept pour un objectif Escort ciblant ce PNJ.</summary>
    public bool IsAcceptingEscort => isPatrolRoute && !loopRoute && _routeIndex == 0 && _routeWaiting;

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

    private CombatEntityAnimatorController _animatorController;

    /// <summary>Exposé pour un éventuel consommateur externe (aucun aujourd'hui).</summary>
    public CombatAIController CombatAI => _combatAI;

    /// <summary>Miroir de Mob.IsDashing — posé par les routines de verbe de SkillSystem.
    /// StartDisplacement() pendant tout déplacement animé (DashSelf/Pull/Push) où ce PNJ est
    /// caster OU victime, pour empêcher _combatAI.Tick() de reprendre le contrôle du transform
    /// (agent NavMesh désactivé) pendant que la coroutine le Lerp manuellement. Sans ce flag,
    /// un PNJ canFight se battrait avec sa propre IA pour la position pendant le trajet — même
    /// bug que Mob avant l'ajout de IsDashing.</summary>
    public bool IsDashing { get; set; } = false;

    /// <summary>Exposé pour CombatEntityAnimatorController (IsChasing) — même rôle que
    /// Mob.CurrentState. Patrol par défaut si le PNJ n'est pas canFight (pas de CombatAIController
    /// dans ce cas).</summary>
    public CombatAIState CurrentState => _combatAI != null ? _combatAI.CurrentState : CombatAIState.Patrol;

    // ── Patrol Route — état privé ──────────────────────────────
    private int       _routeIndex       = 0;
    private bool      _routeWaiting     = false;
    private float     _routeWaitTimer   = 0f;
    private Transform _lastRouteTarget  = null; // évite un SetDestination() par frame — un seul appel par point visé
    // _routeAnchor est le "chez-soi" de combat pendant la route — distinct de _spawnPos (figé au
    // Point A d'origine). Alimente LeashAnchor/PatrolRadius ci-dessous : sans lui, un PNJ ambulant
    // qui combat à plus de leashRadius de son Point A déclenche Return en boucle (CombatAIController
    // compare toujours contre LeashAnchor, que Initialize() ne touche pas) — trouvé en revue de
    // code avant exécution, pas en test manuel.
    private Vector3    _routeAnchor      = Vector3.zero;
    // _awaitingDisappear : le cycle _routeWaiting EN COURS est celui du DERNIER point en mode
    // loopRoute = false — à son expiration, TickPatrolRoute() doit disparaître au lieu de
    // reboucler normalement. Un simple bool plutôt qu'un enum d'état séparé : évite de toucher
    // à la logique _routeWaiting déjà en place (revue + corrigée), un seul if de plus à sa sortie.
    private bool       _awaitingDisappear = false;
    private bool       _routeHidden       = false;
    private float      _routeHideTimer    = 0f;
    // Un bouton de dialogue "Ouvrir boutique/forge/..." dispatche l'action PUIS ferme le dialogue
    // (nextStageID = -1) dans la foulée — IsTalking redevient donc faux pendant que le joueur est
    // encore dans la boutique, et la route repartait en plein shopping. Posé par
    // HandleDialogueAction() quand il déclenche un de ces panneaux, relu/effacé par
    // IsSecondaryPanelOpen() une fois ce panneau refermé. Trouvé en test manuel par Florian (PNJ
    // ambulant marchand).
    private bool       _awaitingSecondaryPanel = false;

    /// <summary>Déclenché à CHAQUE arrivée à un point de la route (pas seulement le dernier) —
    /// pointIndex donne l'index dans patrolPoints. Pas encore consommé — point d'accroche pour une
    /// future récompense de quête (QuestSystem), à brancher lors de la revue dialogue/quest-giver/
    /// quête multi-step (hors scope ici, voir la spec).</summary>
    public event System.Action<PNJ, int> OnReachedRoutePoint;

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
                SetRegenMana      (data.baseRegenMana);
                SetAttackDamageMin(data.attackDamage);
                SetAttackDamageMax(data.attackDamage);
                SetMoveSpeed      (data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed);
            }
        }

        base.Awake();

        // Résistances aux debuffs — innées, indépendantes de l'équipement (les PNJ n'en ont
        // pas). Même raison que Mob.ApplyData() : CharacterStats.ApplyDebuffResistances() est
        // strictement réservée au Player. APRÈS base.Awake() — statusEffects n'est initialisée
        // que là (Entity.Awake()).
        if (data != null && data.debuffResistances != null)
            foreach (var entry in data.debuffResistances)
                statusEffects.SetDebuffResistance(entry.debuffType, entry.resistChance);

        // Fige le snapshot — RequestRecalculate() repartira de ces valeurs
        SnapshotBaseStats();

        // Cache des composants combat
        _skillSystem = GetComponent<SkillSystem>();
        _spawnPos    = transform.position;
        _routeAnchor = _spawnPos;

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

    // =========================================================
    // DÉGÂTS — aggro (n'existait pas avant cette migration : un PNJ frappé par un Mob ne
    // ripostait que si HandleCombatAI() retrouvait un ennemi par coïncidence via le scan de
    // proximité classique)
    // =========================================================
    public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null, bool skipDamageReduction = false)
    {
        // _routeHidden AVANT base.TakeDamage() — le PNJ est censé être complètement absent entre
        // deux passages de route (loopRoute = false), pas juste invisible : aucun dégât ne doit
        // passer, même via une attaque de zone qui ne dépend pas du Collider désactivé.
        if (_routeHidden) return;

        base.TakeDamage(amount, sourceElement, source, skipDamageReduction);

        // Coupe un dialogue en cours AVANT ForceEngage() — s'applique même si !canFight (un PNJ
        // passif qui encaisse sans riposter voit aussi son dialogue interrompu par un coup reçu).
        if (IsTalking) EndDialogue();

        // ?. ajouté ici pour la même raison qu'au garde de Update() : un canFight PNJ sans
        // NavMeshAgent n'a plus de CombatAIController, _combatAI.ForceEngage(...) planterait sinon.
        if (!isDead && data != null && data.canFight)
            _combatAI?.ForceEngage(source);
    }

    // =========================================================
    // UPDATE
    // =========================================================

    protected override void Update()
    {
        base.Update();
        if (isDead) return;

        // TickPatrolRoute() AVANT ce early-return — un PNJ ambulant NON-combattant doit marcher
        // même si data.canFight est false, sinon ce return couperait court avant d'y arriver.
        TickPatrolRoute();

        // _combatAI == null ajouté ici : un canFight PNJ sans NavMeshAgent sur son prefab n'a plus
        // de CombatAIController depuis le garde ajouté en Task 1 (_agent != null requis avant de
        // l'instancier) — sans ce check, PollChannelInterrupt()/Tick() plus bas plantent en
        // NullReferenceException à CHAQUE frame au lieu de se contenter de l'avertissement déjà
        // loggé dans Awake(). Trouvé en revue de code avant exécution.
        if (data == null || !data.canFight || _combatAI == null) return;

        // Poll d'interrupt de canalisation — DOIT rester avant le freeze CC ci-dessous : un hard
        // CC doit interrompre la canalisation EN COURS, pas être bloqué par le early-return sur
        // isCCd qui vient juste après.
        _combatAI.PollChannelInterrupt();

        RefreshEnemyList();

        // IsDashing — voir sa doc ci-dessus. Même position relative que Mob.Update() : après
        // RefreshEnemyList(), avant le freeze CC (un déplacement en cours n'a pas besoin d'être
        // interrompu par le check CC, la coroutine de SkillSystem gère déjà sa propre
        // interruption CC pour DashSelf — voir IsHardCCd).
        if (IsDashing) return;

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

    /// <summary>true tant qu'un panneau secondaire déclenché par CE PNJ (boutique/forge/rareté/
    /// fenêtre PNJ/fusion) reste ouvert — _awaitingSecondaryPanel, posé par HandleDialogueAction(),
    /// s'efface tout seul dès que le panneau concerné se referme (peu importe comment : bouton
    /// Fermer du panneau lui-même, Escape, etc. — contrairement au talkingTo/DialogueUI.IsOpen de
    /// plus haut, pas besoin d'un self-heal séparé ici puisqu'on relit l'état du panneau à CHAQUE
    /// frame au lieu de se fier à un événement de fermeture).</summary>
    private bool IsSecondaryPanelOpen()
    {
        if (!_awaitingSecondaryPanel) return false;

        bool anyOpen =
            (ShopUI.Instance      != null && ShopUI.Instance.gameObject.activeSelf)      ||
            (ForgeUI.Instance     != null && ForgeUI.Instance.gameObject.activeSelf)     ||
            (RarityUI.Instance    != null && RarityUI.Instance.gameObject.activeSelf)    ||
            (PNJWindowUI.Instance != null && PNJWindowUI.Instance.gameObject.activeSelf) ||
            (FusionUI.Instance    != null && FusionUI.Instance.gameObject.activeSelf);

        if (!anyOpen) _awaitingSecondaryPanel = false; // panneau refermé — libère la route
        return anyOpen;
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

        // _routeHidden AVANT le guard _agent ci-dessous — Disappear() désactive volontairement
        // l'agent/renderer/collider le temps de l'absence, le guard renverrait donc toujours vrai
        // et ce timer ne décompterait jamais si on le testait après.
        if (_routeHidden)
        {
            _routeHideTimer -= Time.deltaTime;
            if (_routeHideTimer <= 0f) ReappearAtRouteStart();
            return;
        }

        if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh || IsDashing) return; // agent indisponible (Pull/Push, hors navmesh...) — ne pas spammer SetDestination/pathPending dans le vide

        // Ré-ancrage du _spawnPos INTERNE de CombatAIController — À CHAQUE frame, MÊME pendant un
        // dialogue/combat, PAS seulement pendant la marche (contrairement à _routeAnchor plus bas,
        // qui lui reste figé pendant le combat pour garder un leash qui a un sens). Initialize()
        // est une pure assignation de champs (vérifié dans CombatAIController.cs — aucun effet de
        // bord sur CurrentState/cooldowns/cible en cours), donc aucun risque à la rappeler aussi
        // souvent. Sans ce ré-ancrage continu, TickReturn() viserait encore la position d'il y a
        // plusieurs secondes (la dernière fois que la route avait tourné, AVANT le combat) au lieu
        // d'ici-et-maintenant, et ramènerait le PNJ marcher vers un point déjà quitté avant de
        // reprendre sa route — trouvé en test manuel par Florian ("repart depuis sa position de
        // fin de combat, pas de retour à l'ancien point").
        _combatAI?.Initialize(this, _agent, _skillSystem, this, _animatorController, transform.position);

        // Self-heal : si talkingTo est resté non-nul alors que la fenêtre de dialogue n'est plus
        // ouverte (fermée via le bouton "Fermer"/Escape, qui passent par DialogueUI directement
        // sans repasser par PNJ.EndDialogue()), on le détecte ici plutôt que de geler la route
        // pour toujours. Ne couvre pas le cas où un AUTRE PNJ a pris la fenêtre (DialogueUI reste
        // "ouverte", juste avec un interlocuteur différent) — ce cas-là reste le trou de cycle de
        // vie pré-existant signalé dans la spec, à valider avec Florian.
        if (IsTalking && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen))
            talkingTo = null;

        if (IsTalking)
        {
            // Le PNJ s'arrête NET à sa position actuelle pendant le dialogue au lieu de continuer
            // sa route en tâche de fond — ResetPath() coupe le chemin en cours, _lastRouteTarget =
            // null force un SetDestination() frais vers le MÊME point à la reprise (pas de saut
            // d'index). Avant ce correctif, l'early-return ci-dessous empêchait seulement de RE-
            // donner un ordre à l'agent, mais ne l'arrêtait pas : un SetDestination() déjà émis
            // continue d'être suivi par le NavMeshAgent tout seul, dialogue ouvert ou non — trouvé
            // en test manuel par Florian (le PNJ continuait de marcher pendant le dialogue).
            if (_agent.hasPath) { _agent.ResetPath(); _lastRouteTarget = null; }
            return;
        }

        if (IsSecondaryPanelOpen())
        {
            // Même traitement que IsTalking ci-dessus — un bouton "Ouvrir boutique" ferme le
            // dialogue tout en laissant le panneau ouvert, donc IsTalking seul ne suffit plus.
            if (_agent.hasPath) { _agent.ResetPath(); _lastRouteTarget = null; }
            return;
        }

        if (_combatAI != null && _combatAI.CurrentState != CombatAIState.Patrol)
        {
            // Le combat a pu repositionner l'agent n'importe où (poursuite d'une cible, retour) —
            // forcer un SetDestination() frais à la reprise plutôt que de se fier à un cache
            // _lastRouteTarget périmé qui ferait croire que l'agent vise déjà le bon point alors
            // que son chemin réel a été écrasé entre-temps par CombatAIController.
            _lastRouteTarget = null;
            return;
        }

        if (_routeWaiting)
        {
            _routeWaitTimer -= Time.deltaTime;
            if (_routeWaitTimer <= 0f)
            {
                _routeWaiting = false;
                // Ce cycle de wait était celui du dernier point en mode loopRoute = false — à son
                // expiration, on disparaît au lieu de reboucler normalement vers patrolPoints[0].
                if (_awaitingDisappear)
                {
                    _awaitingDisappear = false;
                    Disappear();
                }
            }
            return;
        }

        Transform current = patrolPoints[_routeIndex];
        if (current == null) return; // point supprimé de la scène après assignation — reste figé plutôt que planter

        // Figé ici (pas rafraîchi pendant le combat, contrairement au ré-ancrage continu plus haut)
        // — LeashAnchor ci-dessous lit cette valeur pour plafonner la distance de poursuite
        // pendant un combat ; si elle suivait aussi la position courante en temps réel, le leash
        // ne vaudrait plus jamais rien (toujours ~0 de distance par rapport à soi-même).
        _routeAnchor = transform.position;

        // SetDestination une seule fois par point visé (pas par frame) — CombatAIController.
        // TickPatrol fait pareil ; ré-émettre le même chemin à chaque Update() était le design
        // initial du plan, mais pathPending peut encore valoir true au moment où on le relit juste
        // après l'avoir ré-émis, ce qui aurait pu empêcher toute détection d'arrivée — trouvé en
        // revue de code avant exécution.
        if (current != _lastRouteTarget)
        {
            _agent.SetDestination(current.position);
            _lastRouteTarget = current;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
        {
            // Invoke AVANT l'avance d'index — le hook rapporte le point qu'on vient d'atteindre,
            // pas le prochain visé.
            OnReachedRoutePoint?.Invoke(this, _routeIndex);

            bool isLastPoint = _routeIndex == patrolPoints.Count - 1;

            // Publié à CHAQUE passage au dernier point, même en boucle (loopRoute = true) — un
            // objectif de quête Escort ne compte qu'une fois grâce au garde-fou IsComplete de
            // QuestObjective.Increment(), pas besoin de filtrer ici.
            if (isLastPoint && data != null)
                GameEventBus.Publish(new PNJRouteCompletedEvent { pnjData = data });

            if (!loopRoute && isLastPoint)
            {
                // Pas d'avance d'index ici — ReappearAtRouteStart() le remet à 0 explicitement
                // quand l'absence se termine, un peu plus loin.
                _awaitingDisappear = true;
                _routeWaiting      = true;
                _routeWaitTimer    = waitBeforeDisappearSeconds;
            }
            else
            {
                // Arrivée (donc attente) À patrolPoints[0] — avant incrément — utilise la longue
                // fenêtre "récupération d'escorte" au lieu du wait normal entre deux points.
                bool wasAtStart = _routeIndex == 0 && !loopRoute;
                _routeIndex     = (_routeIndex + 1) % patrolPoints.Count; // boucle automatique sur 0
                _routeWaiting   = true;
                _routeWaitTimer = wasAtStart ? escortAcceptWindowSeconds : waitAtPointSeconds;
            }
            _lastRouteTarget = null; // force un nouveau SetDestination() vers le point suivant
        }
    }

    /// <summary>Rend le PNJ complètement absent (renderer/collider/agent désactivés, Interact()/
    /// TakeDamage() no-op via _routeHidden) pendant hiddenDurationSeconds — appelé uniquement
    /// depuis TickPatrolRoute() quand loopRoute = false et que le dernier point vient d'idle.</summary>
    private void Disappear()
    {
        _routeHidden    = true;
        _routeHideTimer = hiddenDurationSeconds;
        SetRouteVisualAndCollision(false);
    }

    /// <summary>Fin de l'absence — réapparaît directement à patrolPoints[0] (Warp, pas de marche
    /// de retour : le PNJ était complètement absent, pas juste invisible sur place) et reprend la
    /// route depuis le début.</summary>
    private void ReappearAtRouteStart()
    {
        _routeHidden = false;
        SetRouteVisualAndCollision(true);

        _routeIndex      = 0;
        _lastRouteTarget = null;

        Transform start = patrolPoints.Count > 0 ? patrolPoints[0] : null;
        if (start != null)
        {
            transform.position = start.position;
            if (_agent != null && _agent.isOnNavMesh) _agent.Warp(start.position);
            _routeAnchor = start.position;
            _combatAI?.Initialize(this, _agent, _skillSystem, this, _animatorController, _routeAnchor);
        }
    }

    /// <summary>Bascule renderer/collider/agent — utilisé par Disappear()/ReappearAtRouteStart()
    /// uniquement. Ne touche PAS isDead/RespawnCoroutine() : ce n'est pas une mort, juste une
    /// absence scénique entre deux passages de route.</summary>
    private void SetRouteVisualAndCollision(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = visible;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = visible;
        if (_agent != null) _agent.enabled = visible;
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
        // _routeHidden : PNJ complètement absent entre deux passages de route (loopRoute = false)
        // — pas interactible, même si un joueur réussit à cliquer où son Collider désactivé était.
        if (player == null || data == null || isDead || _routeHidden) return;
        if (Vector3.Distance(transform.position, player.transform.position) > interactionRadius)
            return;
        // Pas de dialogue en plein combat — cohérent avec TakeDamage() qui ferme un dialogue déjà
        // ouvert dès qu'un coup arrive (voir plus bas) : le combat et le dialogue ne se mélangent
        // jamais, dans aucun des deux sens.
        if (_combatAI != null && _combatAI.CurrentState == CombatAIState.Engage) return;

        talkingTo = player;

        // ── NotifyTalkTo — AVANT le switch ────────────────────
        // Notifie QuestSystem que le joueur parle à CE PNJ précis (par référence SO).
        // Valide uniquement les objectifs TalkTo qui ciblent exactement ce PNJData.
        QuestSystem.Instance?.NotifyTalkTo(data, player);

        switch (data.pnjType)
        {
            case PNJType.Merchant:     InteractMerchant(player);     break;
#pragma warning disable CS0618 // Rarity — retiré du design (Pari est un onglet de shopSpecialty.Forge), ordinal gardé
            case PNJType.Rarity:       InteractRarity(player);       break;
#pragma warning restore CS0618
            case PNJType.Quest:        InteractQuest(player);        break;
            case PNJType.Guard:        InteractGuard(player);        break;
            case PNJType.Decorative:   InteractDecorative(player);   break;
#pragma warning disable CS0618
            case PNJType.FactionNPC:   InteractFactionNPC(player);   break;
#pragma warning restore CS0618
            case PNJType.Teleporter:   InteractTeleporter(player);   break;
            case PNJType.Purification: InteractPurification(player); break;
        }
    }

    // ── Marchand (toute spécialité — shopSpecialty pilote les onglets, voir
    // PNJTypeExtensions.GetTabs()) ──────────────────────────────
    // N'ouvre PAS la fenêtre directement — le dialogue s'affiche d'abord, la fenêtre
    // (PNJWindowUI, onglets résolus depuis shopSpecialty) ne s'ouvre que quand le joueur
    // clique l'option dont l'Action = DialogueAction.OpenPNJWindow (voir HandleDialogueAction
    // ci-dessous — seul point de dispatch, DialogueUI.OnOptionClicked ne fait que forwarder
    // le clic via SelectOption()). Fusionné depuis InteractForge/InteractGenericShop/
    // InteractAntiquarian/InteractCordonnier — identiques, voir commentaire PNJType.Merchant
    // dans PNJData.cs. Demande Florian 2026-10-04.
    private void InteractMerchant(Player player)
    {
        StartDialogue(SelectDialogue(player), player);

        // TODO Phase 6 : RuneUI.Instance?.Open(data, player) — seule spécialité encore un
        // stub (DialogueAction.OpenRuneUI), les autres (Forge/Cordonnier/Cook/Tinkerer/
        // Jeweler/Hatter/CraftStation) ouvrent déjà une fenêtre réelle.
        if (data.shopSpecialty == ShopSpecialty.Antiquarian)
            Debug.Log($"[PNJ/Antiquaire] {data.pnjName} — identification:{data.canIdentifyRunes} insertion:{data.canInsertRunes} (RuneUI Phase 6)");
    }

    // ── PNJ Rareté ────────────────────────────────────────────
    // Même schéma que Merchant : dialogue d'abord, RarityUI ne s'ouvre
    // qu'au clic sur l'option dont l'Action = DialogueAction.OpenRarity.
    private void InteractRarity(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
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

    // ── Purification (Aura) ──────────────────────────────────
    // Texte différent selon le palier Aura ACTUEL du joueur — voir
    // PNJData.purificationDialogueByAuraRank. Une entrée vide/absente à l'index du rang courant
    // retombe sur SelectDialogue (defaultDialogue), même filet de sécurité que tout autre PNJ.
    private void InteractPurification(Player player)
    {
        DialogueData dialogue = null;
        var byRank = data.purificationDialogueByAuraRank;
        if (byRank != null && player.auraRank < byRank.Count)
            dialogue = byRank[player.auraRank];

        dialogue ??= SelectDialogue(player);
        if (dialogue != null) StartDialogue(dialogue, player);
    }

    // ── PNJ Faction ───────────────────────────────────────────
    private void InteractFactionNPC(Player player)
    {
        // TODO Phase 9 : vérifier player.faction vs data.faction
        StartDialogue(SelectDialogue(player), player);
        Debug.Log($"[PNJ/Faction] {data.pnjName} ({data.faction}) — FactionSystem Phase 9");
    }

    // ── Téléporteur ───────────────────────────────────────────
    private void InteractTeleporter(Player player)
    {
        StartDialogue(SelectDialogue(player), player);
        // TODO Phase 8 : HarborUI.Instance?.Open(data.availableDestinations, player)
        Debug.Log("[PNJ/Téléporteur] HarborUI Phase 8");
    }

    // =========================================================
    // SÉLECTION DU DIALOGUE
    // Un seul dialogue par défaut, pas de filtre — demande Florian 2026-10-04 (retrait de
    // la ladder réputation/connu). `player` gardé en paramètre pour ne pas devoir toucher
    // chaque site d'appel si une sélection redevient nécessaire plus tard.
    // =========================================================

    private DialogueData SelectDialogue(Player player)
    {
        return data?.defaultDialogue;
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

        if (currentStage == null)
        {
            Debug.LogWarning($"[PNJ] {data.pnjName} : plus aucun stage après skip.");
            return;
        }

        DialogueUI.Instance?.OpenDialogue(this, currentStage, player);
    }

    public void SelectOption(DialogueOption option, Player player)
    {
        // option == null = DialogueUI.OnClickClose() ("Fermer"/"Partir"/Escape) — AVANT cette
        // correction, ce early-return laissait talkingTo non-nul pour toujours : inoffensif tant
        // que rien ne lisait IsTalking hors de PNJ.cs, mais TickPatrolRoute() en dépend maintenant
        // pour geler la marche pendant un dialogue — sans EndDialogue() ici, la route ne reprenait
        // plus jamais après le premier dialogue fermé par ce bouton. Trouvé en revue de code avant
        // exécution.
        if (option == null) { EndDialogue(); return; }
        if (activeDialogue == null) return;

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
            EndDialogue();
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

        if (stage.rewardPrestige != 0)
            player.AddPrestige(stage.rewardPrestige);
    }

    private void HandleDialogueAction(DialogueAction action, Player player)
    {
        switch (action)
        {
            // _awaitingSecondaryPanel = true sur les 5 actions qui ouvrent un panneau SECONDAIRE —
            // le dialogue qui les déclenche se ferme juste après (nextStageID = -1), donc IsTalking
            // seul ne suffit plus à geler la route pendant que ce panneau reste ouvert.
            case DialogueAction.OpenShop:           ShopUI.Instance?.OpenShop(data, player); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenForge:          ForgeUI.Instance?.Open(); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenRarity:          RarityUI.Instance?.Open(); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenPNJWindow:       PNJWindowUI.Instance?.Open(data, player); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenRuneUI:         Debug.Log("[PNJ] OpenRuneUI — RuneUI Phase 6");   break;
            case DialogueAction.OpenFusionUI:       FusionUI.Instance?.Open(data, player); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenQuestLog:       PNJQuestBoardUI.Instance?.Open(data, player); _awaitingSecondaryPanel = true; break;
            case DialogueAction.OpenTeleportUI:     Debug.Log("[PNJ] OpenTeleportUI — HarborUI Phase 8"); break;
            case DialogueAction.PurifyAura:         TryPurifyAura(player); break;
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
    // PURIFICATION AURA — spec 2026-09-29-prestige-aura-design.md §2.5
    // =========================================================

    /// <summary>Rachète UN palier d'Aura (rang courant → rang-1), coût lu sur
    /// player.prestigeAuraData.auraTiers[rang courant] — table PARTAGÉE (un seul asset dragué
    /// sur le prefab Player), plus sur ce PNJData individuellement. Recalcule le rang APRÈS
    /// l'achat via player.AddAura(delta) — le delta amène l'Aura exactement au plancher du
    /// palier cible (Player.GetAuraTierFloor), jamais un gain additif classique (spec §2.5 :
    /// chaque achat FIXE l'Aura au plancher du palier suivant). Rien à faire si déjà Normal
    /// (rang 0) — pas de palier au-dessus à racheter.</summary>
    private void TryPurifyAura(Player player)
    {
        if (player == null || player.prestigeAuraData == null) return;

        int rank = player.auraRank;
        if (rank <= 0)
        {
            FloatingText.Spawn("Aura déjà Normal", player.transform.position + Vector3.up * 2f, Color.gray);
            return;
        }

        var tiers = player.prestigeAuraData.auraTiers;
        if (tiers == null || rank >= tiers.Count) return;
        var tier = tiers[rank];

        int aerisCost      = tier.purificationAerisCost;
        int resourceQty    = tier.purificationResourceQty;
        bool needsResource = tier.purificationResource != null && resourceQty > 0;

        if (needsResource && (InventorySystem.Instance == null ||
            InventorySystem.Instance.GetResourceCount(tier.purificationResource) < resourceQty))
        {
            FloatingText.Spawn($"{tier.purificationResource.name} insuffisant",
                player.transform.position + Vector3.up * 2f, Color.red);
            return;
        }

        if (AerisSystem.Instance == null || !AerisSystem.Instance.Spend(aerisCost))
        {
            FloatingText.Spawn("Aeris insuffisant", player.transform.position + Vector3.up * 2f, Color.red);
            return;
        }

        if (needsResource)
            InventorySystem.Instance.ConsumeResource(tier.purificationResource, resourceQty);

        // Dernière étape (Terni → Normal) : remonte au PLAFOND (+100), pas au plancher (0) —
        // sinon +100 (le départ, voir Player.aura) devient définitivement inatteignable une fois
        // qu'on est descendu sous 0 ne serait-ce qu'une fois (aucune autre source ne peut jamais
        // dépasser le plancher d'un palier). Florian, 2026-09-29 : "on ne peut pas remonter à
        // +100" — repéré en testant ce PNJ. Toutes les autres étapes restent plancher normal.
        int targetAura = (rank - 1 == 0) ? 100 : player.GetAuraTierFloor(rank - 1);
        player.AddAura(targetAura - player.aura);

        FloatingText.Spawn("Aura purifiée", player.transform.position + Vector3.up * 2f, new Color(0.6f, 0.9f, 1f));
        Debug.Log($"[PNJ/Purification] Aura {rank} → {rank - 1} — {aerisCost} Aeris" +
            (needsResource ? $" + {resourceQty}x {tier.purificationResource.name}" : "") + ".");
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

    /// <summary>Reçoit l'Animation Event relayé par CombatEntityAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0) => _combatAI?.OnAnimationHitEvent(hitIndex);

    // =========================================================
    // ICombatAnimatorProfile — voir World/CombatEntityAnimatorController.cs
    // =========================================================
    public AnimationClip IdleClip  => data?.idleClip;
    public AnimationClip WalkClip  => data?.walkClip;
    public AnimationClip ChaseClip => data?.chaseClip;
    public AnimationClip DeathClip => data?.deathClip;
    public List<AnimationClip> IdleClipVariants => data?.idleClipVariants;

    // =========================================================
    // ICombatAIProfile — voir spec §5bis pour le détail de chaque membre
    // =========================================================
    public SkillData       BasicAttackSkill  => data?.basicAttackSkill;
    public List<SkillData> SecondarySkills   => data?.skills;
    // isPatrolRoute coupe la patrouille aléatoire du combat (PatrolRadius=0) et déplace son ancre
    // de laisse (LeashAnchor) sur _routeAnchor au lieu de _spawnPos — sinon les deux systèmes se
    // marchent dessus : TickPatrol ferait vagabonder le PNJ loin de son point d'arrêt pendant
    // waitAtPointSeconds, et LeashAnchor resterait figé sur le Point A d'origine (jamais mis à
    // jour par le ré-ancrage de TickPatrolRoute), faisant boucler Engage/Return dès que la route
    // s'éloigne de plus de leashRadius du Point A — trouvé en revue de code avant exécution.
    public float           PatrolRadius      => isPatrolRoute ? 0f : (data != null ? data.patrolRadius : 0f);
    public float           LeashDistance     => data != null ? data.leashRadius : 0f;
    public Vector3         LeashAnchor       => isPatrolRoute ? _routeAnchor : _spawnPos;
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
    /// — décision explicite de Florian, baseRegenHP/baseRegenMana suffisent.</summary>
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

        // Échoue toute quête d'escorte en cours visant ce PNJ (objectif Escort pas encore
        // complet) — AVANT le respawn, pas besoin de condition sur canFight/aiType ici, un PNJ
        // sans objectif Escort actif ne fait simplement rien dans cette méthode.
        QuestSystem.Instance?.FailEscortQuestsFor(data);

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
        // Anim de mort jouée AVANT le masquage instantané existant — AJOUTÉE par-dessus
        // respawnDelay (jamais carvée dedans, décision Florian) : le corps reste visible en
        // train de jouer l'anim pendant deathAnimLength secondes, PUIS la séquence
        // masquage/attente respawnDelay/réapparition démarre, strictement inchangée. Sans
        // deathClip assigné, PlayDeath() retourne 0f et ce bloc est un no-op total — comportement
        // actuel préservé à l'identique.
        float deathAnimLength = _animatorController?.PlayDeath(data?.deathClip) ?? 0f;
        if (deathAnimLength > 0f)
            yield return new WaitForSeconds(deathAnimLength);

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (_agent != null) _agent.enabled = false;

        yield return new WaitForSeconds(data.respawnDelay);

        isDead             = false;
        currentHP          = maxHP;
        currentMana        = maxMana;
        transform.position = _spawnPos;
        _routeIndex         = 0; // sinon le PNJ réapparaît au Point A mais vise encore un point loin dans la route
        _routeWaiting       = false;
        _awaitingDisappear  = false;
        _routeHidden        = false; // si mort pendant l'absence scénique (loopRoute = false) — le code de mort/respawn gère déjà sa propre ré-activation renderer/collider plus bas, pas besoin de SetRouteVisualAndCollision() ici
        _lastRouteTarget    = null;
        // Re-ancrage sur _spawnPos (PAS _routeAnchor) — sans ça, un PNJ mort en plein combat loin
        // sur sa route respawn au Point A mais son CombatAIController croit encore que son "chez-
        // soi" est l'ancien point de combat : NotifyDeath() ne remet pas CurrentState à Patrol, un
        // respawn en Engage/Return viserait alors ce point lointain au lieu du Point A où le PNJ
        // vient de réapparaître. Trouvé en revue de code avant exécution.
        _routeAnchor = _spawnPos;
        _combatAI?.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
        _combatAI?.ResetCooldowns();

        // Si la mort a eu lieu en Engage/Return, CurrentState reste figé là (NotifyDeath() ne le
        // remet jamais à Patrol, voir juste au-dessus) — sans ce nettoyage, FindClosestEnemy()
        // retrouve l'ancien agresseur dans aggroSet dès la prochaine frame et le PNJ repart au
        // combat instantanément au réveil au lieu de reprendre calmement sa route. CurrentState
        // lui-même n'a pas de setter public exposé (private set), donc pas de moyen direct de le
        // forcer à Patrol ici — mais vider aggroSet fait que TickEngage() ne retrouve plus aucune
        // cible dès le prochain Tick() (CombatAIController.cs, pas touché), ce qui déclenche tout
        // seul son propre retour normal vers Patrol via le même chemin Engage→Return→Patrol que la
        // fin de combat habituelle. aggroSet.Clear() est d'ailleurs exactement ce qu'OnReturnToPatrol()
        // fait déjà en temps normal (plus bas dans ce fichier) — la mort court-circuitait juste ce
        // nettoyage. Trouvé en test manuel par Florian.
        aggroSet.Clear();
        enemyList.Clear();

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = true;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;
        if (_agent != null) { _agent.enabled = true; _agent.Warp(_spawnPos); }

        // Même nécessité que Player.Revive() — l'état Animator "Death" n'a pas de transition de
        // sortie automatique, sans ce signal le PNJ réapparaît figé en pose de mort.
        _animatorController?.CancelChannel();

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

        if (isPatrolRoute && patrolPoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Count; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(patrolPoints[i].position, 0.3f);

                // Pas de ligne de fermeture dernier→premier quand loopRoute = false — le PNJ ne
                // reboucle pas en marchant (il disparaît puis réapparaît directement au premier
                // point), une ligne ici suggérerait à tort une marche de retour continue.
                bool isLastSegment = i == patrolPoints.Count - 1;
                if (isLastSegment && !loopRoute) continue;

                Transform next = patrolPoints[(i + 1) % patrolPoints.Count];
                if (next != null) Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }
    }
}
