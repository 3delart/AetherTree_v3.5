using UnityEngine;
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
// Règle de mort UNIFIÉE (spec §1) : vies-- ; si > 0 → recharge la scène
// courante et respawn (SceneLoader.ReloadCurrentMap(), déjà écrit pour ça
// — voir son propre commentaire "respawn, reset donjons...") ; sinon → fin
// de run, expulsion vers la map d'où le joueur est entré.
// =============================================================

public enum InstanceOutcome { InProgress, Success, Failure }

public class InstanceSession : MonoBehaviour
{
    public static InstanceSession Instance { get; private set; }

    public IInstanceConfig  CurrentInstance { get; private set; }
    public int              LivesRemaining  { get; private set; }
    public InstanceOutcome  CurrentOutcome  { get; private set; } = InstanceOutcome.InProgress;

    [Tooltip("Délai avant expulsion après la fin d'un run (succès ou échec) — GDD §14.2.4 : " +
             "\"expulsion automatique après 15 secondes\".")]
    public float exitDelay = 15f;

    // Capturé à Enter() — la map d'où le joueur vient, pour y revenir à la sortie.
    private string _returnMapName;

    // ── Entrée en attente — voir docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md ──
    public IInstanceConfig PendingInstance { get; private set; }
    public List<Player>    Participants    { get; private set; } = new List<Player>();
    public bool            IsLeader        { get; private set; }

    private HashSet<string> _metTriggerIDs = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Point d'entrée unique — appelé par ConsumePendingEntry() quand un Portal gaté
    /// (gateType = RequiresDungeonEntry) est franchi, pas directement par la consommation de
    /// l'item. Charge la scène de l'activité via SceneLoader (qui résout lui-même
    /// PlayerSpawnPoint au chargement — voir SceneLoader.LoadMapWithSpawn) et initialise les
    /// vies depuis la config. Retourne false sans rien modifier (CurrentInstance/LivesRemaining
    /// non touchés) si la config ou la scène est invalide, ou si SceneLoader est absent/déjà en
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
        _metTriggerIDs.Clear();
        LivesRemaining  = config.LivesPerPlayer;
        CurrentOutcome  = InstanceOutcome.InProgress;
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
        IsLeader        = true;
        Participants    = new List<Player> { FindObjectOfType<Player>() };
        return true;
    }

    /// <summary>Annule l'entrée en attente — l'item d'entrée n'est PAS rendu (pas d'UX de groupe
    /// soignée pour ce passage). Appelable depuis un futur bouton "quitter le groupe".</summary>
    public void CancelPendingEntry()
    {
        PendingInstance = null;
        IsLeader        = false;
        Participants.Clear();
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
        return true;
    }

    /// <summary>Marque un trigger de donjon (mini-boss tué, levier actionné) comme acquis pour
    /// LE RUN EN COURS UNIQUEMENT — jamais persisté, remis à zéro à chaque Enter(). Câblage réel
    /// (quel Mob.Die() appelle ceci) hors scope de ce chantier, voir spec §4 Non-objectifs —
    /// cette méthode existe pour que Portal.CanCross() ait quelque chose à lire, le contenu qui
    /// l'appellera vraiment viendra avec un futur donjon réel.</summary>
    public void NotifyTriggerMet(string triggerID)
    {
        if (string.IsNullOrEmpty(triggerID)) return;
        _metTriggerIDs.Add(triggerID);
    }

    public bool IsTriggerMet(string triggerID) => _metTriggerIDs.Contains(triggerID);

    /// <summary>Appelé par Player.Die() dans Entities/Player.cs quand une instance est active —
    /// remplace entièrement le flux de mort normal (RespawnSystem/DeathScreenUI) tant qu'une
    /// session est en cours.</summary>
    public void OnPlayerDeath()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;

        LivesRemaining--;
        if (LivesRemaining > 0)
            RespawnAtCheckpoint();
        else
            EndRun(InstanceOutcome.Failure);
    }

    /// <summary>Appelé quand le boss de l'activité meurt — câblage exact (quel Mob.Die() precis
    /// déclenche ceci) laissé au chantier qui construit le premier boss réel, voir spec §3
    /// Non-objectifs. Ce chantier ne fait que garantir que le hook existe et fonctionne.</summary>
    public void OnBossKilled()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;
        EndRun(InstanceOutcome.Success);
    }

    // =========================================================
    // INTERNE
    // =========================================================

    private void RespawnAtCheckpoint()
    {
        var player = FindObjectOfType<Player>();
        SceneLoader.Instance?.ReloadCurrentMap(); // recharge la scène — reset mobs, PlayerSpawnPoint
        player?.Revive(1f, 1f); // pleine vie — la perte d'une vie EST la pénalité, pas le %HP
    }

    private void EndRun(InstanceOutcome outcome)
    {
        CurrentOutcome = outcome;
        Debug.Log($"[INSTANCE] {CurrentInstance.DisplayName} — {outcome}. Expulsion dans {exitDelay}s.");
        StartCoroutine(ExitAfterDelay());
    }

    private IEnumerator ExitAfterDelay()
    {
        yield return new WaitForSeconds(exitDelay);

        if (!string.IsNullOrEmpty(_returnMapName))
            SceneLoader.Instance?.LoadMapWithSpawn(_returnMapName);
        else
            Debug.LogWarning("[INSTANCE] _returnMapName vide — expulsion sans rechargement de map.");

        // Une sortie sur Failure laisse le joueur isDead (Player.Die() a court-circuité
        // RespawnSystem.TriggerDeath() — voir OnPlayerDeath()) : il faut le ranimer ici,
        // sinon il reste bloqué mort en permanence. Une sortie sur Success ne doit PAS
        // toucher un joueur vivant.
        var player = FindObjectOfType<Player>();
        if (player != null && player.isDead)
            RespawnSystem.Instance?.Revive(1f, 1f);

        CurrentInstance = null;
        IsLeader        = false;
        Participants.Clear();
    }
}
