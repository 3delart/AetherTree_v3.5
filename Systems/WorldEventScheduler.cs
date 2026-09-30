using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDEVENTSCHEDULER.CS — Dispatcher générique d'événement mondial
// Path : Assets/Scripts/Systems/WorldEventScheduler.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/AerisSystem. Porte le
// timer partagé + les 2 offsets d'annonce (T-5min/T-1min) ET eligibleMaps — communs à TOUS les
// types d'événements, jamais dupliqués sur chaque asset WorldEventData (Florian, 2026-09-30 : un
// palier éligible à un event l'est pour N'IMPORTE QUEL type, pas un pool par asset). Pur
// dispatcher : ne sait RIEN de la mécanique interne de World Boss/Invasion, se contente de tirer
// un type au hasard dans eventPool et de lui déléguer tout le reste via RunEvent().
//
// Florian place ce composant à la main sous _Managers et glisse les assets dans eventPool —
// l'auto-création (Instance ci-dessous) reste un filet de sécurité comme partout ailleurs dans
// le projet, mais sortirait avec eventPool vide — système inerte tant que personne ne le
// configure (voir RunCycle, garde de config).
// =============================================================

[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — sceneName se remplit automatiquement (voir " +
             "WorldEventScheduler.OnValidate). Editor-only, n'existe pas en build : sceneName " +
             "reste le champ réellement lu au runtime, même convention que Portal.targetMapScene/" +
             "DungeonMapData.mapScene.")]
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;

    [Tooltip("Palier de CETTE scène — doit correspondre au MapInfo.palier posé dans la scène " +
             "elle-même. Pas de lecture automatique possible (une scène non chargée n'a pas de " +
             "MapInfo accessible) : à retaper ici à la main, une seule fois à la config.")]
    public int palier;
}

public class WorldEventScheduler : MonoBehaviour
{
    private static WorldEventScheduler _instance;

    public static WorldEventScheduler Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WorldEventScheduler (auto)");
                _instance = go.AddComponent<WorldEventScheduler>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    [Header("Timer — intervalle aléatoire entre deux événements (secondes)")]
    [Tooltip("Défaut ~6-8h. Réduis CES DEUX VALEURS *ET* les deux offsets d'annonce ci-dessous " +
             "ENSEMBLE pour tester en Play Mode sans attendre des heures.")]
    public float minInterval = 21600f; // 6h
    public float maxInterval = 28800f; // 8h

    [Header("Annonces — délai AVANT le déclenchement (secondes)")]
    public float firstWarningOffset  = 300f; // 5 min
    public float secondWarningOffset = 60f;  // 1 min

    [Header("Pool d'événements possibles")]
    [Tooltip("Un type est tiré au hasard à chaque cycle — glisse ici un WorldBossData, un " +
             "InvasionData, ou tout futur type héritant de WorldEventData.")]
    public List<WorldEventData> eventPool = new List<WorldEventData>();

    [Header("Paliers éligibles")]
    [Tooltip("Partagé par TOUS les types d'événements — un palier éligible l'est pour n'importe " +
             "quel type tiré, pas un pool séparé par asset.")]
    public List<WorldEventMapEntry> eligibleMaps = new List<WorldEventMapEntry>();

    public float FirstWarningOffset  => firstWarningOffset;
    public float SecondWarningOffset => secondWarningOffset;

    public WorldEventMapEntry PickRandomMap()
        => (eligibleMaps == null || eligibleMaps.Count == 0) ? null : eligibleMaps[Random.Range(0, eligibleMaps.Count)];

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(RunCycle());
    }

    // =========================================================
    // CYCLE PRINCIPAL
    // =========================================================

    private IEnumerator RunCycle()
    {
        while (true)
        {
            if (eventPool == null || eventPool.Count == 0)
            {
                Debug.LogWarning("[WorldEventScheduler] eventPool vide — système inerte. Nouvelle vérification dans 5s.");
                yield return new WaitForSeconds(5f);
                continue;
            }

            float totalDelay = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(Mathf.Max(0f, totalDelay - firstWarningOffset));

            WorldEventData selected = eventPool[Random.Range(0, eventPool.Count)];
            if (selected == null) continue; // entrée vide dans la liste — reboucle direct

            yield return selected.RunEvent(this);
            // selected.RunEvent gère TOUT (annonces, spawn, résolution, récompense) en utilisant
            // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien de
            // plus sur ce qui se passe à l'intérieur.
        }
    }

    // =========================================================
    // POSITION ALÉATOIRE SUR LE NAVMESH — utilisé par tout type d'événement
    // =========================================================

    /// <summary>Point barycentrique uniforme dans un triangle du NavMesh baké de la scène
    /// ACTIVE. Pas de biais par aire de triangle (acceptable, pas une distribution
    /// statistiquement critique). Retourne false si le NavMesh est vide/absent — un
    /// `out Vector3.zero` en cas d'échec aurait été un piège : (0,0,0) est une position VALIDE
    /// sur un vrai NavMesh (l'origine du monde), donc pas utilisable comme sentinelle d'échec —
    /// d'où le bool de retour plutôt qu'un simple Vector3.</summary>
    public static bool TryGetRandomNavMeshPoint(out Vector3 point)
    {
        var tri = NavMesh.CalculateTriangulation();
        if (tri.indices.Length < 3) { point = Vector3.zero; return false; }

        int triCount = tri.indices.Length / 3;
        int t = Random.Range(0, triCount) * 3;
        Vector3 a = tri.vertices[tri.indices[t]];
        Vector3 b = tri.vertices[tri.indices[t + 1]];
        Vector3 c = tri.vertices[tri.indices[t + 2]];

        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        point = a * (1f - r1) + b * (r1 * (1f - r2)) + c * (r1 * r2);
        return true;
    }

#if UNITY_EDITOR
    // Synchronise sceneName depuis mapScene (SceneAsset, editor-only) à chaque édition — évite un
    // sceneName tapé/copié à la main qui diverge silencieusement du vrai nom de scène après un
    // renommage/déplacement de fichier .unity (même patron que DungeonData.OnValidate).
    private void OnValidate()
    {
        if (eligibleMaps == null) return;
        foreach (var entry in eligibleMaps)
            if (entry.mapScene != null) entry.sceneName = entry.mapScene.name;
    }
#endif
}
