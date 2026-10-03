using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// RESPAWNSYSTEM.CS — Gestion mort et résurrection
// AetherTree GDD v18
//
// Respawn au checkpoint confirmé du palier courant (voir Player.ResolveCheckpoint/MapInfo),
// résolu à l'instant du respawn — recharge une autre map si besoin (cross-scène additive).
// DeathScreenUI appelé en statique.
// =============================================================

public class RespawnSystem : MonoBehaviour
{
    public static RespawnSystem Instance { get; private set; }

    [Header("Configuration")]
    public float respawnDelay = 5f;
    [Range(0f, 1f)] public float respawnHPPercent   = 0.5f;
    [Range(0f, 1f)] public float respawnManaPercent = 0.5f;

    private Player _player;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
    }

    private void OnDestroy()
    {
        SceneLoader.OnMapLoaded -= OnCheckpointMapLoaded;
    }

    // Résolu au moment du respawn (pas mis en cache au chargement de la map) : MapSpawnPoint par
    // défaut de la map courante, avec le GameObject nommé "PlayerSpawnPoint" en secours pour les
    // scènes pas encore migrées.
    private Transform ResolveSpawnPoint()
    {
        Transform spawn = MapSpawnPoint.FindDefault()?.transform;
        if (spawn == null) spawn = GameObject.Find("PlayerSpawnPoint")?.transform;
        if (spawn == null)
            Debug.LogWarning("[RESPAWN] Aucun MapSpawnPoint par défaut dans la map courante — respawn sur place.");
        return spawn;
    }

    // =========================================================
    // MORT
    // =========================================================
    public void TriggerDeath()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) { Debug.LogError("[RESPAWN] TriggerDeath — Player introuvable !"); return; }


        TargetingSystem.Instance?.ClearEverything();

        // Bloque le NavMeshAgent
        NavMeshAgent agent = _player.GetComponent<NavMeshAgent>();
        if (agent != null) { agent.ResetPath(); agent.enabled = false; }

        // Bloque le PlayerController
        PlayerController controller = _player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        DeathScreenUI.Show();
        StartCoroutine(RespawnCoroutine());
    }

    private IEnumerator RespawnCoroutine()
    {
        int seconds = Mathf.RoundToInt(respawnDelay);
        while (seconds > 0)
        {
            DeathScreenUI.UpdateTimer(seconds);
            yield return new WaitForSeconds(1f);
            seconds--;
        }
        Respawn();
    }

    // =========================================================
    // RÉSURRECTION DIFFÉRÉE (buff Revive) — sur place, pas de téléport
    // =========================================================

    /// <summary>Appelé par Player.Die() quand un buff Revive était actif au moment de la mort
    /// — résurrection après `delay` secondes (0 = instantané), SUR PLACE (position de mort),
    /// contrairement au respawn normal qui téléporte au PlayerSpawnPoint.</summary>
    public void TriggerDelayedRevive(float delay, float hpPercent, float manaPercent)
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) { Debug.LogError("[RESPAWN] TriggerDelayedRevive — Player introuvable !"); return; }

        TargetingSystem.Instance?.ClearEverything();

        NavMeshAgent agent = _player.GetComponent<NavMeshAgent>();
        if (agent != null) { agent.ResetPath(); agent.enabled = false; }

        PlayerController controller = _player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        DeathScreenUI.Show();
        StartCoroutine(DelayedReviveCoroutine(delay, hpPercent, manaPercent));
    }

    private IEnumerator DelayedReviveCoroutine(float delay, float hpPercent, float manaPercent)
    {
        int seconds = Mathf.RoundToInt(delay);
        while (seconds > 0)
        {
            DeathScreenUI.UpdateTimer(seconds);
            yield return new WaitForSeconds(1f);
            seconds--;
        }

        // Pas de téléport — le joueur ressuscite là où il est tombé.
        NavMeshAgent agent = _player.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = true;

        PlayerController controller = _player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = true;

        _player.Revive(hpPercent, manaPercent);
        DeathScreenUI.Hide();
    }

    // =========================================================
    // RESPAWN
    // =========================================================
    /// <summary>Choisit la destination : le checkpoint confirmé du palier courant (MapInfo de la
    /// scène active, 1 par défaut), en remontant vers les paliers inférieurs si celui-ci n'a
    /// jamais été atteint (Player.ResolveCheckpoint) — "Map_01" en tout dernier recours, nouveau
    /// personnage. Si la destination EST déjà la map courante, respawn immédiat sans rechargement
    /// ; sinon rechargement complet — on ne respawn jamais dans une zone de nature où le joueur
    /// n'a pas encore atteint la ville. Voir MapSpawnPoint (type = Palier) pour la confirmation.</summary>
    private void Respawn()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;

        int currentPalier = MapInfo.Current?.palier ?? 1;
        string targetMap = _player.ResolveCheckpoint(currentPalier)
            ?? (SceneLoader.Instance?.startMap ?? "Map_01");

        if (SceneLoader.Instance != null && targetMap != SceneLoader.Instance.CurrentMap)
        {
            SceneLoader.OnMapLoaded += OnCheckpointMapLoaded;
            SceneLoader.Instance.LoadMapWithSpawn(targetMap);
            return; // WarpAndRevive() se fait dans OnCheckpointMapLoaded, une fois la map prête
        }

        WarpAndRevive();
    }

    private void OnCheckpointMapLoaded(string mapName)
    {
        SceneLoader.OnMapLoaded -= OnCheckpointMapLoaded;
        WarpAndRevive();
    }

    /// <summary>Position + réactivation partagées entre le respawn sur place (map inchangée) et
    /// le respawn après rechargement (map de checkpoint différente) — même geste, seule la
    /// scène active au moment de l'appel diffère.</summary>
    private void WarpAndRevive()
    {
        Transform spawnPoint = ResolveSpawnPoint();
        NavMeshAgent agent = _player.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            if (spawnPoint != null) _player.transform.position = spawnPoint.position;
            agent.enabled = true;
            if (spawnPoint != null) agent.Warp(spawnPoint.position);
            agent.ResetPath();
        }
        else if (spawnPoint != null)
        {
            _player.transform.position = spawnPoint.position;
        }

        PlayerController controller = _player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = true;

        _player.Revive(respawnHPPercent, respawnManaPercent);
        DeathScreenUI.Hide();
    }

    // =========================================================
    // RÉSURRECTION PAR UN AUTRE JOUEUR
    // =========================================================
    public void Revive(float hpPercent = 1f, float manaPercent = 1f)
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        StopAllCoroutines();

        NavMeshAgent agent = _player?.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = true;

        PlayerController controller = _player?.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = true;

        _player?.Revive(hpPercent, manaPercent);
        DeathScreenUI.Hide();
    }
}