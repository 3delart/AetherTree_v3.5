using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// INSTANCESESSION.CS — Session privée pour Donjon/Combat à Vague/Événements
// Path : Assets/Scripts/Systems/InstanceSession.cs
// AetherTree GDD v3.6 — §14.2/§14.3/§14.5.1
//
// Track UNE session active à la fois (solo aujourd'hui — voir docs/
// superpowers/specs/2026-09-23-instance-system-design.md pour la décision
// de portée). Ne connaît JAMAIS DungeonData/CombatVagueData par leur type
// concret — uniquement via IInstanceConfig, pour rester réutilisable par
// n'importe quelle future activité instanciée (Arène Équipe, etc.) sans
// modification de ce fichier.
//
// Règle de mort UNIFIÉE (spec §1, étendue en 2026-09 aux vies communes) :
// chaque mort retire une vie au joueur ET une vie commune (DeathLimit) ; si le
// joueur a encore des vies et que la limite commune n'est pas atteinte →
// écran de mort (IInstanceConfig.RespawnDelay) puis retour au MapSpawnPoint de
// la salle SANS recharger la scène (mobs/leviers/objectifs conservés) ; sinon →
// fenêtre de grâce (DeathExpulsionDelay, voir FailureGraceRoutine) puis
// expulsion vers la map d'où le joueur est entré, sauf si le boss meurt entre-
// temps (le run bascule alors en Succès).
// =============================================================

public enum InstanceOutcome { InProgress, Success, Failure, Left }

public class InstanceSession : MonoBehaviour
{
    private static InstanceSession _instance;

    /// <summary>Auto-crée une instance si aucune n'a été placée en scène — évite qu'ArmEntry()/
    /// Enter() échouent silencieusement (le seul symptôme visible est un log d'appelant qui dit
    /// "refusé", sans aucun log [INSTANCE] puisque cette classe n'est jamais atteinte). Sans
    /// donnée d'Éditeur à charger (contrairement à DungeonRegistry) — tous les délais viennent de
    /// CurrentInstance (IInstanceConfig), rien à configurer sur cet objet auto-créé.</summary>
    public static InstanceSession Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("InstanceSession (auto)");
                _instance = go.AddComponent<InstanceSession>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    /// <summary>true si une instance a déjà été créée (placée en scène OU auto-créée par un
    /// appel précédent à Instance) — ne DÉCLENCHE jamais la création, contrairement à lire
    /// Instance directement. Pour les appelants qui veulent juste savoir "y a-t-il une session
    /// en cours quelque part" sans en provoquer une par erreur (ex: SaveSystem.CollectProgress,
    /// appelé à CHAQUE sauvegarde, y compris quand aucun run n'a jamais commencé).</summary>
    public static bool Exists => _instance != null;

    public IInstanceConfig  CurrentInstance { get; private set; }
    public InstanceOutcome  CurrentOutcome  { get; private set; } = InstanceOutcome.InProgress;

    // ── Vies — deux compteurs (voir OnPlayerDeath) ──────────────
    /// <summary>Morts cumulées de tous les joueurs depuis Enter() — à comparer à
    /// IInstanceConfig.DeathLimit (vies communes de l'activité).</summary>
    public int TotalDeaths { get; private set; }

    private readonly Dictionary<Player, int> _playerLives = new Dictionary<Player, int>();

    /// <summary>Vies restantes d'un joueur. Avant Enter() (entrée en attente), retombe sur le
    /// plein de vies de l'activité en attente — jamais d'état "inconnu" à afficher.</summary>
    public int GetPlayerLives(Player player)
    {
        if (player != null && _playerLives.TryGetValue(player, out int lives)) return lives;
        IInstanceConfig config = CurrentInstance ?? PendingInstance;
        return config != null ? config.LivesPerPlayer : 0;
    }

    /// <summary>Vies communes restantes (DeathLimit - TotalDeaths). -1 si l'activité n'a pas de
    /// limite commune (DeathLimit = 0) — l'appelant n'affiche alors rien.</summary>
    public int TeamLivesRemaining
    {
        get
        {
            IInstanceConfig config = CurrentInstance ?? PendingInstance;
            if (config == null || config.DeathLimit <= 0) return -1;
            return Mathf.Max(0, config.DeathLimit - TotalDeaths);
        }
    }

    // Les 3 délais (respawn/expulsion de grâce/sortie succès) viennent de CurrentInstance
    // (IInstanceConfig.RespawnDelay/DeathExpulsionDelay/SuccessExitDelay), PAS de champs ici —
    // InstanceSession reste générique (Donjon, Combat à Vague, futures activités), chaque config
    // règle son propre rythme (ex: LivesPerPlayer=1 sur Combat à Vague rend RespawnDelay inerte).

    // Capturé à Enter() — la map d'où le joueur vient, pour y revenir à la sortie.
    private string _returnMapName;

    /// <summary>Exposé pour SaveSystem.CollectProgress — un vrai quit (isQuitting) en plein donjon
    /// doit rediriger lastMap vers CETTE map (la waiting room), pas vers SceneLoader.startMap.</summary>
    public string ReturnMapName => _returnMapName;

    // ── Entrée en attente — voir docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md ──
    public IInstanceConfig PendingInstance { get; private set; }
    public List<Player>    Participants    { get; private set; } = new List<Player>();

    /// <summary>Le participant chef du groupe — PAR PERSONNE, pas "le joueur local est-il chef"
    /// (l'ancien bool IsLeader ne pouvait pas distinguer les deux, invisible tant qu'il n'y avait
    /// jamais qu'un seul participant). Sert à colorer la bonne ligne dans DungeonGroupPanelUI une
    /// fois la liste triée par niveau — comparer Leader à CHAQUE participant, pas un flag global.
    /// En solo, toujours le joueur qui a armé l'entrée (voir ArmEntry).</summary>
    public Player Leader { get; private set; }

    // VFX "chef de groupe" — voir AttachLeaderVfx()/ClearLeaderVfx().
    private GameObject _leaderVfxInstance;

    private void Awake()
    {
        // Compare au champ, jamais à la propriété Instance — la lire déclencherait sa propre
        // auto-création si _instance est encore null, créant un second GameObject en parallèle
        // de celui-ci pendant qu'il s'enregistre lui-même.
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        // Protège aussi un InstanceSession placé à la main dans une scène (pas seulement
        // l'auto-création, voir Instance ci-dessus) — un donjon multi-salles changerait de scène
        // en cours de run sans ça, détruisant la session au passage.
        DontDestroyOnLoad(gameObject);
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Point d'entrée unique — appelé par ConsumePendingEntry() quand un Portal gaté
    /// (gateType = RequiresDungeonEntry) est franchi, pas directement par la consommation de
    /// l'item. Charge la scène de l'activité via SceneLoader (qui résout lui-même
    /// PlayerSpawnPoint au chargement — voir SceneLoader.LoadMapWithSpawn) et initialise les
    /// vies (par joueur + compteur de morts communes) depuis la config. Retourne false sans rien
    /// modifier (CurrentInstance non touché) si la config ou la scène est invalide, ou si SceneLoader est absent/déjà en
    /// train de charger.</summary>
    public bool Enter(IInstanceConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.SceneName))
        {
            Debug.LogWarning("[INSTANCE] Enter() refusé — config nulle ou SceneName vide.");
            return false;
        }
        if (SceneLoader.Instance == null || SceneLoader.Instance.IsLoading)
        {
            Debug.LogWarning("[INSTANCE] Enter() refusé — SceneLoader indisponible ou chargement en cours.");
            return false;
        }

        CurrentInstance = config;
        CurrentOutcome  = InstanceOutcome.InProgress;
        TotalDeaths     = 0;
        _playerLives.Clear();
        foreach (var participant in Participants)
            if (participant != null) _playerLives[participant] = config.LivesPerPlayer;
        _returnMapName  = SceneLoader.Instance.CurrentMap;

        SceneLoader.Instance.LoadMapWithSpawn(config.SceneName);
        return true;
    }

    /// <summary>Posé par la consommation de l'item d'entrée (voir ConsoBarUI.TryUseSlot) —
    /// n'appelle PAS Enter(), prépare seulement l'état qu'un portail gaté (RequiresDungeonEntry)
    /// consommera au franchissement. Refuse si une instance est déjà en cours
    /// (CurrentInstance != null) pour éviter de corrompre l'état actif avec une nouvelle entrée
    /// en attente par-dessus, si la config est invalide (SceneName vide — mirror de la
    /// validation d'Enter(), pour ne pas consommer l'item pour une config qui échouera de toute
    /// façon), ou si une entrée est DÉJÀ en attente (sinon un second arme silencieusement écrase
    /// le premier — 2 items consommés pour une seule entrée utilisable). Voir CancelPendingEntry()
    /// pour sortir de cet état sans être bloqué.</summary>
    public bool ArmEntry(IInstanceConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.SceneName) || CurrentInstance != null) return false;
        if (PendingInstance != null)
        {
            Debug.LogWarning($"[INSTANCE] ArmEntry refusé — une entrée est déjà en attente ({PendingInstance.InstanceID}). Annuler d'abord via CancelPendingEntry().");
            return false;
        }
        PendingInstance = config;
        Participants    = new List<Player> { FindObjectOfType<Player>() };
        Leader          = Participants[0];
        return true;
    }

    /// <summary>Attache un VFX au joueur le temps de l'ATTENTE uniquement — de la consommation
    /// de la clé jusqu'au franchissement réel du portail. Détruit par ClearLeaderVfx(), appelé
    /// soit par ConsumePendingEntry() (entrée réussie — le VFX marquait l'attente, pas le run),
    /// soit par CancelPendingEntry() (annulation). Appelé par ConsoBarUI juste après un
    /// ArmEntry() réussi (pas depuis ArmEntry() elle-même — le VFX est un détail de présentation
    /// du consommable, ArmEntry() n'a pas à connaître ConsumableData). Remplace tout VFX
    /// précédent au lieu de les empiler si appelé plusieurs fois.</summary>
    public void AttachLeaderVfx(GameObject vfxPrefab, Player player)
    {
        ClearLeaderVfx();
        if (vfxPrefab == null || player == null) return;
        // Pas de parentage — hériterait aussi de la rotation du joueur, pas juste sa position.
        // FollowPositionOnly ne copie que la position chaque frame.
        _leaderVfxInstance = Instantiate(vfxPrefab, player.transform.position, Quaternion.identity);
        var follow = _leaderVfxInstance.AddComponent<FollowPositionOnly>();
        follow.target = player.transform;

        // Doit survivre aux changements de map le temps de l'attente (ex: waiting_room →
        // portail du donjon) — sans parentage au joueur (lui-même DontDestroyOnLoad), plus rien
        // ne le protège du nettoyage automatique de la scène déchargée.
        DontDestroyOnLoad(_leaderVfxInstance);
    }

    private void ClearLeaderVfx()
    {
        if (_leaderVfxInstance != null) Destroy(_leaderVfxInstance);
        _leaderVfxInstance = null;
    }

    /// <summary>Annule l'entrée en attente — l'item d'entrée n'est PAS rendu (pas d'UX de groupe
    /// soignée pour ce passage). Appelable depuis un futur bouton "quitter le groupe".</summary>
    public void CancelPendingEntry()
    {
        PendingInstance = null;
        Leader          = null;
        Participants.Clear();
        ClearLeaderVfx();
    }

    /// <summary>Appelé par Portal.CanCross() / OnTriggerEnter (gateType = RequiresDungeonEntry) au
    /// moment du franchissement du portail gaté — consomme l'entrée en attente et démarre
    /// réellement l'instance via Enter() (méthode existante, inchangée). PendingInstance n'est
    /// nullifié qu'APRÈS un Enter() réussi : si Enter() échoue (SceneName invalide, SceneLoader
    /// occupé), l'entrée reste en attente et peut être retentée au lieu d'être perdue
    /// définitivement. Retourne false si aucune entrée n'était en attente (le portail ne devrait
    /// normalement jamais appeler ceci sans avoir déjà vérifié PendingInstance != null via
    /// CanCross(), mais reste défensif) ou si Enter() échoue.</summary>
    public bool ConsumePendingEntry()
    {
        if (PendingInstance == null) return false;
        IInstanceConfig config = PendingInstance;
        if (!Enter(config)) return false;
        PendingInstance = null;
        ClearLeaderVfx(); // le VFX marque l'attente, pas tout le run — disparaît à l'entrée réelle
        return true;
    }

    /// <summary>Appelé par Player.Die() dans Entities/Player.cs quand une instance est active —
    /// remplace entièrement le flux de mort normal (RespawnSystem/DeathScreenUI) tant qu'une
    /// session est en cours.
    ///
    /// Chaque mort compte pour UNE vie commune (TotalDeaths, à comparer à DeathLimit). LivesPerPlayer
    /// est un nombre de RESPAWNS, pas un total de morts tolérées : avec 2, la 1re et la 2e mort
    /// respawnent (2 cœurs qui se grisent l'un après l'autre), la 3e est fatale — on ne consomme
    /// jamais la dernière vie sans respawn, la mort SUIVANTE échoue directement.</summary>
    public void OnPlayerDeath(Player player)
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;

        // LivesPerPlayer = nombre de RESPAWNS disponibles, pas le nombre total de morts tolérées
        // avant échec. Avec 2 : mort 1 → 1er respawn (1er cœur grisé), mort 2 → 2e respawn (2e
        // cœur grisé), mort 3 → plus de respawn, échec direct. On regarde donc les vies AVANT de
        // décrémenter : si ce joueur n'en avait déjà plus, CETTE mort est fatale, on ne respawn
        // jamais sur le coup qui vide le dernier cœur.
        int livesBeforeThisDeath = GetPlayerLives(player);
        TotalDeaths++;

        // Annonce de groupe — TOUTE mort, pas seulement la fatale (voir Florian, spec annonces
        // 2026-09-29 : "Mort d'un allié", scope donjon uniquement).
        if (player != null)
            AnnoncePanel.Instance?.Announce($"L'allié {player.entityName} a été tué");

        bool teamOut = CurrentInstance.DeathLimit > 0 && TotalDeaths >= CurrentInstance.DeathLimit;

        if (livesBeforeThisDeath <= 0 || teamOut)
        {
            // Solo uniquement pour l'instant (voir roadmap) — un futur multijoueur devra
            // distinguer "CE joueur est éliminé" de "le run entier échoue" via Participants,
            // pas nécessaire tant qu'il n'y a jamais qu'un seul joueur.
            StartCoroutine(FailureGraceRoutine());
            return;
        }

        if (player != null) _playerLives[player] = livesBeforeThisDeath - 1;
        RespawnAtCheckpoint(player);
    }

    /// <summary>Appelé quand le boss de l'activité meurt — voir Mob.isDungeonBoss/Mob.Die().
    /// Sur un Donjon de Déblocage (dungeonType = Unlock), accorde en plus le palier au joueur
    /// AVANT l'expulsion (GDD §14.3.4) — un Donjon Classique n'accorde jamais de palier.
    /// boss : le Mob tué, uniquement pour nommer l'annonce globale de réussite (voir Florian,
    /// spec annonces 2026-09-29) — aucune autre logique ici ne dépend de sa valeur.</summary>
    public void OnBossKilled(Mob boss)
    {
        Debug.Log($"[DIAG-DONJON] OnBossKilled appelé — CurrentInstance=" +
            $"{(CurrentInstance != null ? CurrentInstance.InstanceID : "NULL")}, CurrentOutcome={CurrentOutcome}");
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;

        // Annonce globale — réussite du donjon, visible de tous (voir AnnoncePanel, aucune
        // distinction de portée côté client tant que le jeu est solo).
        AnnoncePanel.Instance?.Announce(
            $"L'équipe de {Leader?.entityName} a vaincu {boss?.entityName}");

        if (CurrentInstance is DungeonData dungeon)
        {
            if (dungeon.dungeonType == DungeonType.Unlock)
            {
                var player = FindObjectOfType<Player>();
                if (player != null && !player.unlockedTiers.Contains(dungeon.palier))
                    player.unlockedTiers.Add(dungeon.palier);
            }

            if (dungeon.bossChest != null)
            {
                // Rareté rollée UNE FOIS ici (WeaponData.RollRarity(), même table que les drops
                // d'équipement normaux) et figée sur l'instance — RollChestEntry() la réutilise à
                // l'ouverture, voir ConsumableData.cs/ConsumableInstance.chestRarity.
                //
                // Solo-scoped (voir roadmap) — Participants ne contient que le joueur local,
                // InventorySystem.Instance est donc toujours le bon inventaire cible. Le futur
                // multijoueur réel devra distribuer par joueur, pas via ce singleton unique.
                foreach (var participant in Participants)
                {
                    if (participant == null) continue;
                    int rolledRarity = WeaponData.RollRarity();
                    var chestInstance = dungeon.bossChest.CreateInstance(1);
                    chestInstance.chestRarity = rolledRarity;
                    InventorySystem.Instance?.AddItem(new InventoryItem(chestInstance));
                }
            }

            // Prestige — une fois par donjon par jour réel (voir Player.TryClaimDailyDungeonPrestige,
            // spec Prestige/Aura §1.4 : "daily, pas 1ère fois à vie"). Solo-scoped comme le coffre
            // de boss ci-dessus — chaque participant réclame indépendamment.
            if (dungeon.prestigeReward > 0)
            {
                foreach (var participant in Participants)
                {
                    if (participant == null) continue;
                    if (participant.TryClaimDailyDungeonPrestige(dungeon.dungeonID))
                        participant.AddPrestige(dungeon.prestigeReward);
                }
            }
        }

        EndRun(InstanceOutcome.Success);
    }

    /// <summary>Abandon volontaire d'un run en cours — le joueur fuit plutôt que de "mourir"
    /// (voir Florian, 2026-09-29 : évite une éventuelle pénalité de réputation liée à la mort,
    /// PAS ENCORE implémentée dans le projet — rien à appliquer ici tant qu'elle n'existe pas).
    /// Aucune vie perdue, TotalDeaths pas incrémenté. `InstanceOutcome.Left` — délibérément PAS
    /// `Failure` : ExecuteExit() route Failure vers le checkpoint du palier (punitif), alors
    /// qu'une fuite volontaire doit renvoyer à la waiting room (_returnMapName, même destination
    /// qu'un Success) — juste sans récompense. Left tombe dans la branche "else" de
    /// ExecuteExit() (identique à Success pour la destination), sans déclencher le bonus de
    /// coffre de boss. Pas de fenêtre de grâce (contrairement à FailureGraceRoutine) : c'est un
    /// choix volontaire, pas une mort, aucune raison d'attendre — expulsion immédiate. Appelé
    /// depuis DungeonGroupPanelUI APRÈS confirmation (ConfirmationUI.OpenConfirmFlow), jamais
    /// directement au clic.</summary>
    public void LeaveDungeon()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;
        CurrentOutcome = InstanceOutcome.Left;
        ExecuteExit();
    }

    // =========================================================
    // INTERNE
    // =========================================================

    private void RespawnAtCheckpoint(Player player)
    {
        StartCoroutine(RespawnRoutine(player));
    }

    /// <summary>Écran de mort avec compte à rebours (CurrentInstance.RespawnDelay), puis retour au
    /// MapSpawnPoint par défaut de la salle — le point d'entrée — SANS recharger la scène : mobs tués,
    /// leviers actionnés et objectifs restent tels quels. Même mécanique de blocage que
    /// RespawnSystem.TriggerDelayedRevive (agent + contrôleur coupés pendant l'attente).
    /// La pleine vie au retour : perdre une vie EST la pénalité, pas le %HP.</summary>
    private IEnumerator RespawnRoutine(Player player)
    {
        if (player == null) yield break;

        TargetingSystem.Instance?.ClearEverything();

        var agent = player.GetComponent<NavMeshAgent>();
        if (agent != null) { agent.ResetPath(); agent.enabled = false; }
        var controller = player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        DeathScreenUI.Show();
        float remaining = CurrentInstance.RespawnDelay;
        while (remaining > 0f)
        {
            DeathScreenUI.UpdateTimer(Mathf.CeilToInt(remaining));
            remaining -= Time.deltaTime;
            yield return null;
        }

        // Run terminé pendant l'attente (ex: boss tué par un autre joueur) : ExitAfterDelay()
        // ranime et déplace déjà le joueur, ne rien faire ici.
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress)
        {
            DeathScreenUI.Hide();
            yield break;
        }

        if (agent != null) agent.enabled = true;
        if (controller != null) controller.enabled = true;

        if (SceneLoader.Instance == null || !SceneLoader.Instance.WarpToSpawnPoint())
            Debug.LogWarning("[INSTANCE] Aucun MapSpawnPoint par défaut dans la scène du donjon — respawn sur place.");

        player.Revive(1f, 1f);
        DeathScreenUI.Hide();
    }

    /// <summary>Fenêtre de grâce après la dernière vie perdue — voir IInstanceConfig.DeathExpulsionDelay.
    /// Ne fixe PAS CurrentOutcome tout de suite : il reste InProgress pendant toute la fenêtre,
    /// donc OnBossKilled() (son garde vérifie justement InProgress) continue de fonctionner
    /// normalement si le boss meurt pendant ce temps — c'est ce qui permet au run de basculer en
    /// Success avant que ce joueur ne soit expulsé. Si la fenêtre s'écoule sans ça, Failure est
    /// déclenché ici et exécuté IMMÉDIATEMENT (pas de second délai empilé par-dessus).</summary>
    private IEnumerator FailureGraceRoutine()
    {
        // Même blocage que RespawnRoutine (le joueur est déjà isDead, mais on coupe quand même
        // agent/contrôleur par sécurité/cohérence) — réactivés par RespawnSystem.Revive(), appelé
        // depuis ExecuteExit() dans les deux issues (Success si le boss meurt entre-temps, Failure
        // sinon).
        var player = FindObjectOfType<Player>();
        var agent = player?.GetComponent<NavMeshAgent>();
        if (agent != null) { agent.ResetPath(); agent.enabled = false; }
        var controller = player?.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        DeathScreenUI.Show();
        float remaining = CurrentInstance.DeathExpulsionDelay;
        while (remaining > 0f)
        {
            if (CurrentOutcome != InstanceOutcome.InProgress)
            {
                // Boss tué pendant la fenêtre — OnBossKilled() a déjà démarré EndRun(Success) et
                // gère tout lui-même (récompense incluse, ce joueur reste dans Participants).
                DeathScreenUI.Hide();
                yield break;
            }
            DeathScreenUI.UpdateTimer(Mathf.CeilToInt(remaining));
            remaining -= Time.deltaTime;
            yield return null;
        }

        CurrentOutcome = InstanceOutcome.Failure;
        Debug.Log($"[INSTANCE] {CurrentInstance.DisplayName} — Failure. Expulsion immédiate.");
        DeathScreenUI.Hide();
        ExecuteExit();
    }

    private void EndRun(InstanceOutcome outcome)
    {
        CurrentOutcome = outcome;
        Debug.Log($"[INSTANCE] {CurrentInstance.DisplayName} — {outcome}. Expulsion dans {CurrentInstance.SuccessExitDelay}s.");

        // EndRun n'est appelé qu'avec Success (vérifié — Failure passe par FailureGraceRoutine
        // directement, sans EndRun) — publier ici suffit pour un objectif de quête
        // DungeonComplete, zéro risque de compter une Failure/Left comme une victoire.
        Debug.Log($"[DIAG-DONJON] EndRun publie InstanceCompletedEvent instanceID={CurrentInstance.InstanceID}");
        GameEventBus.Publish(new InstanceCompletedEvent { instanceID = CurrentInstance.InstanceID });

        StartCoroutine(SuccessExitRoutine());
    }

    private IEnumerator SuccessExitRoutine()
    {
        yield return new WaitForSeconds(CurrentInstance.SuccessExitDelay);
        ExecuteExit();
    }

    /// <summary>Téléportation + nettoyage partagés par les deux issues — appelé soit après
    /// SuccessExitDelay (Success), soit immédiatement à la fin de FailureGraceRoutine (Failure,
    /// déjà entièrement écoulé son propre délai).</summary>
    private void ExecuteExit()
    {
        // Succès → retour à la map d'où le joueur est entré (ex: une salle d'attente devant le
        // donjon — franchissable à nouveau tout de suite pour un re-run). Échec → pas de "petit
        // aller-retour", direct au checkpoint du palier DE CE DONJON (DungeonData.palier) — un
        // Classique/Déblocage de palier 2 renvoie à la ville du palier 2 si atteinte, sinon
        // remonte la chaîne (Player.ResolveCheckpoint), "Map_01" en tout dernier recours.
        string destinationMap;
        if (CurrentOutcome == InstanceOutcome.Failure)
        {
            int dungeonPalier = (CurrentInstance as DungeonData)?.palier ?? 1;
            var failedPlayer = FindObjectOfType<Player>();
            destinationMap = failedPlayer?.ResolveCheckpoint(dungeonPalier)
                ?? (SceneLoader.Instance?.startMap ?? "Map_01");
        }
        else
        {
            destinationMap = _returnMapName;
        }

        if (!string.IsNullOrEmpty(destinationMap))
            SceneLoader.Instance?.LoadMapWithSpawn(destinationMap);
        else
            Debug.LogWarning("[INSTANCE] Aucune map de destination valide — expulsion sans rechargement de map.");

        // Une sortie sur Failure laisse le joueur isDead (Player.Die() a court-circuité
        // RespawnSystem.TriggerDeath() — voir OnPlayerDeath()) : il faut le ranimer ici,
        // sinon il reste bloqué mort en permanence. Une sortie sur Success ne doit PAS
        // toucher un joueur vivant.
        var player = FindObjectOfType<Player>();
        if (player != null && player.isDead)
            RespawnSystem.Instance?.Revive(1f, 1f);

        CurrentInstance = null;
        Leader          = null;
        Participants.Clear();
        _playerLives.Clear();
        TotalDeaths     = 0;
    }
}
