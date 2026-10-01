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
             "quel type tiré, pas un pool séparé par asset. NE JAMAIS inclure le Palier 1 " +
             "(Florian, 2026-09-30) : on laisse les nouveaux joueurs apprendre à jouer sans " +
             "interruption d'event mondial — les events commencent au Palier 2+.")]
    public List<WorldEventMapEntry> eligibleMaps = new List<WorldEventMapEntry>();

    public float FirstWarningOffset  => firstWarningOffset;
    public float SecondWarningOffset => secondWarningOffset;

    /// <summary>True sans jamais déclencher l'auto-création du singleton (contrairement à
    /// .Instance) — même patron que InstanceSession.Exists, pour un appelant qui veut juste
    /// savoir "y a-t-il un scheduler déjà en vie quelque part" sans en provoquer un par erreur
    /// (voir GameEventBus.Reset()).</summary>
    public static bool Exists => _instance != null;

    private WorldEventData _activeEvent;

    /// <summary>Ré-abonne le tracking de dégâts de l'event EN COURS après un GameEventBus.Reset()
    /// (changement de map) — no-op si aucun event n'est actuellement en train de tracker
    /// (_activeEvent null). Appelé par GameEventBus.Reset() via Exists (jamais .Instance, pour
    /// ne pas auto-créer un scheduler juste pour ce check). Florian, 2026-10-01 : sans ça, un
    /// joueur qui quitte puis revient sur la map d'un event EN PLEIN COMBAT voyait son tracking
    /// de participation s'arrêter silencieusement — GameEventBus.Reset() vide OnDamageDealt
    /// globalement, et rien ne réabonnait l'event en cours, contrairement à XPSystem/LootManager
    /// qui ont déjà ce réflexe.</summary>
    public void Resubscribe() => _activeEvent?.Resubscribe();

    public WorldEventMapEntry PickRandomMap()
        => (eligibleMaps == null || eligibleMaps.Count == 0) ? null : eligibleMaps[Random.Range(0, eligibleMaps.Count)];

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private float _timeUntilNextRoll = -1f; // -1 = pas encore armé

    private void Start()
    {
        // .Instance ICI, pas .Exists — contrairement à GameEventBus.Reset() (qui veut juste
        // savoir "y a-t-il déjà un registre" sans en créer un pour rien), le scheduler a BESOIN
        // que le registre existe pour fonctionner : s'il n'a jamais été touché avant ce point,
        // .Instance le crée et déclenche son Load() (dans son propre Awake()) — sans quoi une
        // sauvegarde pourrait rester silencieusement jamais chargée si rien d'autre n'accède au
        // registre en premier.
        if (WorldStateRegistry.Instance.TryGetWorldEventTimeRemaining(out float saved))
            _timeUntilNextRoll = saved;
        // sinon : RunCycle() tire un intervalle neuf au premier passage (comportement actuel)

        StartCoroutine(RunCycle());
    }

    private void Update()
    {
        if (_timeUntilNextRoll < 0f) return; // pas encore armé, ou event en cours (voir RunCycle)
        _timeUntilNextRoll = Mathf.Max(0f, _timeUntilNextRoll - Time.deltaTime);
        WorldStateRegistry.Instance?.SetWorldEventTimeRemaining(_timeUntilNextRoll);
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

            if (_timeUntilNextRoll < 0f)
                _timeUntilNextRoll = Random.Range(minInterval, maxInterval);

            // Attend que le countdown descende jusqu'à l'offset de 1ère annonce — équivalent au
            // WaitForSeconds(totalDelay - firstWarningOffset) d'avant, mais interrogeable/
            // sauvegardable à tout moment via _timeUntilNextRoll (décompté dans Update()) au
            // lieu d'un délai opaque figé dans la coroutine.
            yield return new WaitUntil(() => _timeUntilNextRoll <= firstWarningOffset);

            WorldEventData selected = eventPool[Random.Range(0, eventPool.Count)];
            if (selected == null) { _timeUntilNextRoll = -1f; continue; }

            _timeUntilNextRoll = -1f; // event en cours — countdown "gelé/inactif" jusqu'à
                                       // résolution (l'event en cours n'est pas sauvegardé en
                                       // détail ; si le jeu ferme ici, à la réouverture ce cycle
                                       // est abandonné et un intervalle NEUF est tiré, pas de
                                       // countdown à reprendre pour un event qui n'a jamais fini)
            _activeEvent = selected; // voir Resubscribe() — permet un re-abonnement propre si
                                      // GameEventBus.Reset() tombe pendant que cet event tourne
            yield return selected.RunEvent(this);
            _activeEvent = null;
            // selected.RunEvent gère TOUT (annonces, spawn, résolution, récompense) en utilisant
            // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien de
            // plus sur ce qui se passe à l'intérieur.
        }
    }

    // =========================================================
    // POSITION ALÉATOIRE SUR LE NAVMESH — utilisé par tout type d'événement
    // =========================================================

    /// <summary>Point barycentrique uniforme dans un triangle du NavMesh baké de la scène
    /// ACTIVE, en excluant toute WorldEventNoSpawnZone active (villes — Florian, 2026-09-30 :
    /// évite qu'un World Boss/une Invasion spawn au milieu des PNJ). Reroll jusqu'à
    /// maxAttempts avant d'abandonner — pas de biais par aire de triangle (acceptable, pas une
    /// distribution statistiquement critique). Retourne false si le NavMesh est vide/absent OU
    /// si aucun point hors zone interdite n'a été trouvé en maxAttempts essais — un
    /// `out Vector3.zero` en cas d'échec aurait été un piège : (0,0,0) est une position VALIDE
    /// sur un vrai NavMesh (l'origine du monde), donc pas utilisable comme sentinelle d'échec —
    /// d'où le bool de retour plutôt qu'un simple Vector3.</summary>
    public static bool TryGetRandomNavMeshPoint(out Vector3 point)
    {
        var tri = NavMesh.CalculateTriangulation();
        if (tri.indices.Length < 3) { point = Vector3.zero; return false; }

        WorldEventNoSpawnZone[] noSpawnZones = Object.FindObjectsOfType<WorldEventNoSpawnZone>();

        const int maxAttempts = 20;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            point = SampleRandomTrianglePoint(tri);
            if (!IsInsideAnyNoSpawnZone(point, noSpawnZones)) return true;
        }

        Debug.LogWarning($"[WorldEventScheduler] {maxAttempts} tirages de point sont tous tombés dans une " +
            "WorldEventNoSpawnZone — événement annulé pour ce cycle (réessaiera au prochain).");
        point = Vector3.zero;
        return false;
    }

    private static Vector3 SampleRandomTrianglePoint(NavMeshTriangulation tri)
    {
        int triCount = tri.indices.Length / 3;
        int t = Random.Range(0, triCount) * 3;
        Vector3 a = tri.vertices[tri.indices[t]];
        Vector3 b = tri.vertices[tri.indices[t + 1]];
        Vector3 c = tri.vertices[tri.indices[t + 2]];

        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        return a * (1f - r1) + b * (r1 * (1f - r2)) + c * (r1 * r2);
    }

    private static bool IsInsideAnyNoSpawnZone(Vector3 point, WorldEventNoSpawnZone[] zones)
    {
        foreach (var zone in zones)
            if (zone != null && Vector3.Distance(point, zone.transform.position) <= zone.radius)
                return true;
        return false;
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
