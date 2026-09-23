using UnityEngine;
using System.Collections;

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

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Point d'entrée unique — consommé par le câblage de ConsumableData.DungeonStone
    /// (voir Task 5). Charge la scène de l'activité via SceneLoader (qui résout lui-même
    /// PlayerSpawnPoint au chargement — voir SceneLoader.LoadMapWithSpawn) et initialise les
    /// vies depuis la config. Retourne false sans rien modifier (CurrentInstance/LivesRemaining
    /// non touchés) si la config ou la scène est invalide, ou si SceneLoader est absent/déjà en
    /// train de charger — l'appelant (ConsoBarUI) ne doit consommer l'item que sur true.</summary>
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
        LivesRemaining  = config.LivesPerPlayer;
        CurrentOutcome  = InstanceOutcome.InProgress;
        _returnMapName  = SceneLoader.Instance.CurrentMap;

        SceneLoader.Instance.LoadMapWithSpawn(config.SceneName);
        return true;
    }

    /// <summary>Appelé par Player.Die() (voir Task 6) quand une instance est active — remplace
    /// entièrement le flux de mort normal (RespawnSystem/DeathScreenUI) tant qu'une session est
    /// en cours.</summary>
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
    }
}
