using UnityEngine;
using System.Collections.Generic;
using System.IO;

// =============================================================
// WORLDSTATEREGISTRY.CS — État monde persistant (timers de respawn, event global)
// Path : Assets/Scripts/Systems/WorldStateRegistry.cs
// Spec : docs/superpowers/specs/2026-10-01-world-state-save-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/WorldEventScheduler.
// Source de vérité EN MÉMOIRE des timers de respawn de zone (SpawnManager) — SpawnManager
// n'est plus propriétaire de son propre décompte, il rapporte/interroge ce registre. Les timers
// courent en PERMANENCE (Update() ici), peu importe quelle scène est chargée — un boss tué sur
// la map B revient même si le joueur passe cette heure sur la map A (Florian, 2026-10-01).
//
// Porte AUSSI la persistance disque (JSON, world_state.json) — pas de classe séparée, ce
// registre est déjà la source de vérité, même esprit que SaveSystem.cs qui fait les deux pour
// CharacterProgress. Gelé pendant que le jeu est FERMÉ : Save() capture le temps restant,
// Load() le restaure tel quel, aucun calcul de temps réel écoulé hors-jeu.
//
// Ne stocke QUE le timer, plus de position (retiré le 2026-10-01 — Florian : les zones
// SpawnManager respawnent à une position ALÉATOIRE dans la zone de toute façon, verrouiller la
// destination au moment du death n'apporte rien de perceptible ; et le futur chantier "mobs/
// ressources posés à la main" n'en aurait jamais eu besoin non plus, leur position est déjà
// fixe par nature). SpawnManager retire une position fraîche à chaque respawn réel.
// =============================================================
public class WorldStateRegistry : MonoBehaviour
{
    private static WorldStateRegistry _instance;

    public static WorldStateRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WorldStateRegistry (auto)");
                _instance = go.AddComponent<WorldStateRegistry>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    /// <summary>Même patron que InstanceSession.Exists/WorldEventScheduler.Exists — ne jamais
    /// déclencher l'auto-création juste pour vérifier qu'un registre existe.</summary>
    public static bool Exists => _instance != null;

    private readonly Dictionary<string, float> _zoneTimers = new Dictionary<string, float>();

    /// <summary>Countdown restant avant le prochain tirage WorldEventScheduler — géré ici pour
    /// que Save()/Load() aient un endroit unique où lire/écrire TOUT l'état monde. Lu/écrit par
    /// WorldEventScheduler lui-même via les méthodes ci-dessous (jamais directement par un tiers).</summary>
    private float _worldEventTimeRemaining = -1f; // -1 = pas encore initialisé par le scheduler

    /// <summary>Même dossier que SaveSystem.cs (Directory.GetParent(Application.dataPath) +
    /// "Saves", PAS Application.persistentDataPath — erreur corrigée le 2026-10-01, Florian :
    /// "pourtant mon saveslot est rangé ici : C:\AetherTree_v3.5\Saves") — world_state.json doit
    /// vivre juste à côté de save_slot0.json/account.json, pas dans un dossier différent que
    /// personne ne pense à vérifier.</summary>
    private static string WorldSaveDir
    {
        get
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Saves");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string WorldSavePath => Path.Combine(WorldSaveDir, "world_state.json");

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Load(); // tôt — avant que WorldEventScheduler.Start() ne lise son countdown restauré
    }

    private void Update()
    {
        if (_zoneTimers.Count == 0) return;

        // Copie des clés : on ne peut pas réassigner une valeur de Dictionary<string,float>
        // (type valeur) pendant qu'on itère directement dessus.
        var keys = new List<string>(_zoneTimers.Keys);
        foreach (string key in keys)
            _zoneTimers[key] = Mathf.Max(0f, _zoneTimers[key] - Time.deltaTime);
    }

    // =========================================================
    // ZONES SPAWNMANAGER
    // =========================================================
    //
    // "identifier" est un identifiant GÉNÉRIQUE composé par l'appelant (SpawnManager) — le
    // registre ne sait rien de ce qu'il représente. Pour un boss de map (1 seul par zone) :
    // juste zoneName. Pour un node de ressource (plusieurs par zone, nodeCount) : zoneName + un
    // suffixe d'index stable ("Zone#0", "Zone#1"...) — chaque slot garde son propre timer,
    // indépendant des autres slots de la même zone.

    private static string ZoneKey(string sceneName, string identifier) => $"{sceneName}|{identifier}";

    /// <summary>Appelé par SpawnManager à la mort d'un boss/épuisement d'un node ressource —
    /// démarre (ou redémarre) le décompte de cette clé.</summary>
    public void RegisterRespawn(string sceneName, string identifier, float delay)
        => _zoneTimers[ZoneKey(sceneName, identifier)] = Mathf.Max(0f, delay);

    /// <summary>Appelé par SpawnManager (à son Start() ET en polling tant que la clé est en
    /// cooldown) — true + remaining si encore trackée (en cooldown, remaining peut être 0 =
    /// prête à respawn MAIS pas encore nettoyée, voir ClearZone), false si jamais enregistrée
    /// (jamais mort/épuisée, ou déjà nettoyée — prête).</summary>
    public bool TryGetRemainingTime(string sceneName, string identifier, out float remaining)
        => _zoneTimers.TryGetValue(ZoneKey(sceneName, identifier), out remaining);

    /// <summary>Appelé par SpawnManager une fois le respawn RÉELLEMENT déclenché (boss
    /// réinstancié / node ressource recréé) — retire l'entrée, la clé est de nouveau libre.</summary>
    public void ClearZone(string sceneName, string identifier)
        => _zoneTimers.Remove(ZoneKey(sceneName, identifier));

    // =========================================================
    // WORLDEVENTSCHEDULER
    // =========================================================

    /// <summary>Appelé par WorldEventScheduler à chaque frame de son propre countdown — tient le
    /// registre à jour pour qu'un Save() déclenché à tout moment capture la vraie valeur.</summary>
    public void SetWorldEventTimeRemaining(float remaining) => _worldEventTimeRemaining = remaining;

    /// <summary>Appelé par WorldEventScheduler à son Start() — true + remaining si une save
    /// existait déjà (reprend ce countdown), false si c'est un tout premier lancement (le
    /// scheduler tire un intervalle neuf comme avant).</summary>
    public bool TryGetWorldEventTimeRemaining(out float remaining)
    {
        remaining = _worldEventTimeRemaining;
        return _worldEventTimeRemaining >= 0f;
    }

    // =========================================================
    // SAVE / LOAD
    // =========================================================

    public void Save()
    {
        var state = new WorldState { worldEventTimeRemaining = _worldEventTimeRemaining };
        foreach (var kv in _zoneTimers)
        {
            int sep = kv.Key.IndexOf('|');
            state.zoneTimers.Add(new ZoneTimerState
            {
                sceneName            = kv.Key.Substring(0, sep),
                identifier           = kv.Key.Substring(sep + 1),
                respawnTimeRemaining = kv.Value,
            });
        }

        try
        {
            string json = JsonUtility.ToJson(state, prettyPrint: true);
            File.WriteAllText(WorldSavePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de sauvegarde : {e.Message}");
        }
    }

    private void Load()
    {
        if (!File.Exists(WorldSavePath)) return; // premier lancement — tout reste à l'état neuf

        try
        {
            string json  = File.ReadAllText(WorldSavePath);
            var    state = JsonUtility.FromJson<WorldState>(json);
            if (state == null) return;

            _worldEventTimeRemaining = state.worldEventTimeRemaining;
            _zoneTimers.Clear();
            if (state.zoneTimers != null)
                foreach (var z in state.zoneTimers)
                    _zoneTimers[ZoneKey(z.sceneName, z.identifier)] = z.respawnTimeRemaining;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de chargement, repart à neuf : {e.Message}");
        }
    }
}

[System.Serializable]
public class WorldState
{
    public float worldEventTimeRemaining = -1f;
    public List<ZoneTimerState> zoneTimers = new List<ZoneTimerState>();
}

[System.Serializable]
public class ZoneTimerState
{
    public string sceneName;
    public string identifier; // zoneName (boss) ou "zoneName#index" (node de ressource)
    public float  respawnTimeRemaining;
}
