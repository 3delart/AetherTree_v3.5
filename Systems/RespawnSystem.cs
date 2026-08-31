using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// RESPAWNSYSTEM.CS — Gestion mort et résurrection
// AetherTree GDD v18
//
// Base v1 + CORRECTION : spawnPoint trouvé automatiquement
// via SceneLoader.OnMapLoaded (cross-scène additive).
// DeathScreenUI appelé en statique.
// =============================================================

public class RespawnSystem : MonoBehaviour
{
    public static RespawnSystem Instance { get; private set; }

    [Header("Configuration")]
    public float respawnDelay = 5f;
    [Range(0f, 1f)] public float respawnHPPercent   = 0.5f;
    [Range(0f, 1f)] public float respawnManaPercent = 0.5f;

    // Trouvé automatiquement à chaque chargement de map
    private Transform _spawnPoint;
    private Player    _player;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        SceneLoader.OnMapLoaded += OnMapLoaded;
    }

    private void OnDestroy()
    {
        SceneLoader.OnMapLoaded -= OnMapLoaded;
    }

    private void OnMapLoaded(string mapName)
    {
        // Cherche le PlayerSpawnPoint dans la map qui vient de charger
        GameObject spawnObj = GameObject.Find("PlayerSpawnPoint");
        if (spawnObj != null)
        {
            _spawnPoint = spawnObj.transform;
        }
        else
        {
            Debug.LogWarning($"[RESPAWN] PlayerSpawnPoint introuvable dans {mapName} !");
        }
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
    // RESPAWN
    // =========================================================
    private void Respawn()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;

        // Téléporte via Warp (compatible NavMesh)
        NavMeshAgent agent = _player.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            if (_spawnPoint != null) _player.transform.position = _spawnPoint.position;
            agent.enabled = true;
            if (_spawnPoint != null) agent.Warp(_spawnPoint.position);
            agent.ResetPath();
        }
        else if (_spawnPoint != null)
        {
            _player.transform.position = _spawnPoint.position;
        }

        // Réactive le PlayerController
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