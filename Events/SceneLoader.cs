using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// =============================================================
// SCENELOADER.CS — Chargement additif des maps
// Path : Assets/Scripts/Events/SceneLoader.cs
// AetherTree GDD v30 — Section 38 / 17
//
// _Persistent.unity est toujours chargée (Player, HUD, Managers)
// Les maps sont chargées/déchargées en Additive par-dessus.
//
// Usage :
//   SceneLoader.Instance.LoadMap("Map_01");
//   SceneLoader.Instance.LoadMapWithSpawn("Map_02");
//
// Setup Unity :
//   - Placer sur un GameObject dans _Persistent.unity
//   - Ajouter toutes les scènes dans File > Build Settings
// =============================================================

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Header("Scène de départ")]
    public string startMap = "Map_01";

    [Header("Transition")]
    public float fadeDuration = 0.5f;

    private string _currentMap  = "";
    private bool   _isLoading   = false;

    // Vrai uniquement pour le tout premier chargement au démarrage.
    // Empêche SaveSystem.Save() d'écraser la sauvegarde avec les
    // valeurs par défaut du joueur (Niv.1) avant que Load() soit appelé.
    private bool _isFirstLoad = true;

    /// <summary>Déclenché quand une map est entièrement chargée et prête.</summary>
    public static event System.Action<string> OnMapLoaded;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(startMap))
            StartCoroutine(LoadMapRoutine(startMap, reposition: true));
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Charge une map via portail — le portail gère le repositionnement.</summary>
    public void LoadMap(string mapName)
    {
        if (_isLoading) return;
        if (mapName == _currentMap) return;
        StartCoroutine(LoadMapRoutine(mapName, reposition: false));
    }

    /// <summary>Charge une map et repositionne sur le SpawnPoint (mort, connexion, TP base).</summary>
    public void LoadMapWithSpawn(string mapName)
    {
        if (_isLoading) return;
        StartCoroutine(LoadMapRoutine(mapName, reposition: true));
    }

    /// <summary>Replace le joueur sur le PlayerSpawnPoint de la map courante SANS la recharger —
    /// l'état de la scène (mobs morts, leviers actionnés, objectifs) reste intact. Utilisé par le
    /// respawn de donjon (InstanceSession). Retourne false si aucun PlayerSpawnPoint n'existe.</summary>
    public bool WarpToSpawnPoint() => RepositionPlayer();

    public string CurrentMap => _currentMap;
    public bool   IsLoading  => _isLoading;

    // =========================================================
    // CHARGEMENT
    // =========================================================

    private IEnumerator LoadMapRoutine(string mapName, bool reposition = true)
    {
        _isLoading = true;

        // TODO : fade out — FadeUI.Instance?.FadeOut(fadeDuration);
        // yield return new WaitForSeconds(fadeDuration);

        // ── Sauvegarde avant de changer de map ───────────────
        // Ignorée au premier démarrage (_isFirstLoad = true) pour ne pas
        // écraser la sauvegarde existante avec les valeurs par défaut du joueur.
        if (!_isFirstLoad)
        {
            var player = FindObjectOfType<Player>();
            if (player != null) SaveSystem.Instance?.Save(player);
            if (WorldStateRegistry.Exists) WorldStateRegistry.Instance.Save();
        }

        // Nettoie les abonnements GameEventBus — évite les références mortes
        GameEventBus.Reset();

        // ── Décharge l'ancienne map ───────────────────────────
        if (!string.IsNullOrEmpty(_currentMap))
        {
            Scene old = SceneManager.GetSceneByName(_currentMap);
            if (old.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_currentMap);
                while (!unload.isDone) yield return null;
            }
        }

        // ── Charge la nouvelle map en additif ─────────────────
        AsyncOperation load = SceneManager.LoadSceneAsync(mapName, LoadSceneMode.Additive);
        while (!load.isDone) yield return null;

        _currentMap  = mapName;
        _isFirstLoad = false;  // ← à partir d'ici, les sauvegardes sont autorisées

        // Définit la nouvelle scène comme scène active (lighting, navmesh...)
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(mapName));

        yield return null;

        // Repositionne uniquement si demandé — pas pour les portails
        if (reposition) RepositionPlayer();

        // TODO : fade in — FadeUI.Instance?.FadeIn(fadeDuration);

        _isLoading = false;

        OnMapLoaded?.Invoke(mapName);
    }

    // =========================================================
    // SPAWN POINT
    // =========================================================

    private bool RepositionPlayer()
    {
        // MapSpawnPoint par défaut d'abord ; le GameObject nommé "PlayerSpawnPoint" reste accepté
        // en secours pour les scènes pas encore migrées.
        Transform spawn = MapSpawnPoint.FindDefault()?.transform;
        if (spawn == null) spawn = GameObject.Find("PlayerSpawnPoint")?.transform;
        if (spawn == null)
        {
            Debug.LogWarning($"[SceneLoader] Aucun MapSpawnPoint par défaut dans '{_currentMap}' — " +
                             "le joueur n'est pas repositionné. Poser Prefab_MapSpawnPoint dans la scène.");
            return false;
        }

        Player player = FindObjectOfType<Player>();
        if (player == null) return false;

        var agent = player.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.Warp(spawn.position);
        else player.transform.position = spawn.position;
        return true;
    }
}
