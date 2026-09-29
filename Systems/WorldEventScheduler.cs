using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;

// =============================================================
// WORLDEVENTSCHEDULER.CS — Moteur de l'événement mondial à intervalle aléatoire
// Path : Assets/Scripts/Systems/WorldEventScheduler.cs
// Spec : docs/superpowers/specs/2026-09-29-world-event-scheduler-boss-geant-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/AerisSystem. Toute la
// config (intervalle, offsets d'annonce, paliers éligibles, timeout) vit sur UN asset partagé
// (eventData, voir Data/Content/WorldBossData.cs) — ce composant est un pur moteur, pas de
// champs de config directement dessus. Florian place ce composant à la main sous _Managers et
// glisse l'asset dans eventData — l'auto-création (Instance ci-dessous) reste un filet de
// sécurité comme partout ailleurs dans le projet, mais sortirait avec eventData vide — système
// inerte tant que personne ne l'assigne (voir RunCycle, garde de config).
//
// Réutilisable plus tard pour Invasion (roadmap #3, même famille de timer aléatoire) — PAS pour
// Combat à Vague (roadmap #4, timer à HEURE FIXE, mécanisme différent, à ne jamais fusionner
// avec celui-ci).
//
// Un seul événement actif à la fois : RunCycle() bloque sur SpawnAndWaitForResolution() jusqu'à
// résolution complète (mort ou timeout) avant de reboucler — aucun chevauchement possible par
// construction, pas besoin d'un garde explicite en plus.
// =============================================================
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

    [Tooltip("Toute la config de l'événement (boss/timer/annonces/paliers/timeout) — voir " +
             "Data/Content/WorldBossData.cs. Un asset partagé glissé ici sur le GameObject " +
             "sous _Managers.")]
    public WorldBossData eventData;

    // ── État runtime ─────────────────────────────────────────────
    private GameObject _aliveBoss;
    private Coroutine  _timeoutCoroutine;
    private bool       _resolved;

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
            if (eventData == null || eventData.possibleBosses == null || eventData.possibleBosses.Count == 0 ||
                eventData.eligibleMaps == null || eventData.eligibleMaps.Count == 0)
            {
                Debug.LogWarning("[WorldEventScheduler] eventData non assigné ou possibleBosses/eligibleMaps " +
                    "vide(s) — système inerte tant que ces champs sont vides. Nouvelle vérification dans 5s.");
                yield return new WaitForSeconds(5f);
                continue; // relit la config à chaque passage — un fix live en Éditeur est pris en compte
            }

            float totalDelay = Random.Range(eventData.minInterval, eventData.maxInterval);

            // ── Jusqu'à la 1ère annonce (T-5min par défaut) ──────
            yield return new WaitForSeconds(Mathf.Max(0f, totalDelay - eventData.firstWarningOffset));

            WorldEventMapEntry targetMap = eventData.eligibleMaps[Random.Range(0, eventData.eligibleMaps.Count)];
            MobData targetBoss = eventData.possibleBosses[Random.Range(0, eventData.possibleBosses.Count)];

            AnnoncePanel.Instance?.Announce(
                $"Un Boss Géant menace le Palier {targetMap.palier} dans {eventData.firstWarningOffset / 60f:F0} minutes !");

            // ── Jusqu'à la 2e annonce (T-1min par défaut) ────────
            yield return new WaitForSeconds(Mathf.Max(0f, eventData.firstWarningOffset - eventData.secondWarningOffset));

            AnnoncePanel.Instance?.Announce(
                $"Un Boss Géant menace le Palier {targetMap.palier} dans {eventData.secondWarningOffset / 60f:F0} minute(s) !");

            // ── Jusqu'au spawn (T-0) ──────────────────────────────
            yield return new WaitForSeconds(eventData.secondWarningOffset);

            // Scène active RELUE ici, jamais capturée plus tôt — un joueur qui change de map
            // pendant la fenêtre d'annonce ne doit compter que sa position AU MOMENT du check.
            if (SceneManager.GetActiveScene().name == targetMap.sceneName)
                yield return SpawnAndWaitForResolution(targetBoss, targetMap.palier);
            // Sinon : rien ne spawn, on boucle directement sur le cycle suivant (retour Idle).
        }
    }

    // =========================================================
    // SPAWN + RÉSOLUTION (mort ou timeout)
    // =========================================================

    private IEnumerator SpawnAndWaitForResolution(MobData bossData, int palier)
    {
        if (bossData.prefab == null)
        {
            Debug.LogWarning($"[WorldEventScheduler] {bossData.mobName} n'a pas de prefab — événement annulé.");
            yield break;
        }

        if (!TryGetRandomNavMeshPoint(out Vector3 spawnPos))
        {
            Debug.LogWarning("[WorldEventScheduler] Aucun NavMesh baké dans la scène active — événement annulé.");
            yield break;
        }

        _aliveBoss = Instantiate(bossData.prefab, spawnPos, Quaternion.identity);
        Mob mob = _aliveBoss.GetComponent<Mob>();
        if (mob != null)
        {
            mob.data     = bossData;
            mob.mobLevel = palier;
        }

        AnnoncePanel.Instance?.Announce($"Le Boss Géant est apparu sur le Palier {palier} !");

        _resolved = false;
        mob?.OnDeath(() => OnBossResolved(palier, killed: true));

        _timeoutCoroutine = StartCoroutine(TimeoutAfterDelay(palier, eventData.despawnTimeout));

        // Bloque le cycle principal tant que ni la mort ni le timeout n'ont résolu l'événement.
        yield return new WaitUntil(() => _resolved);
    }

    private IEnumerator TimeoutAfterDelay(int palier, float delay)
    {
        yield return new WaitForSeconds(delay);
        OnBossResolved(palier, killed: false);
    }

    /// <summary>Point de résolution UNIQUE — mort ET timeout y passent tous les deux. Le garde
    /// _resolved empêche une double résolution si les deux se déclenchent presque en même temps
    /// (le joueur tue le boss juste avant/pendant que le timeout allait se déclencher) : sans ce
    /// garde, une double annonce ou un Destroy() sur un objet déjà détruit par Mob.Die() serait
    /// possible.</summary>
    private void OnBossResolved(int palier, bool killed)
    {
        if (_resolved) return;
        _resolved = true;

        if (_timeoutCoroutine != null) { StopCoroutine(_timeoutCoroutine); _timeoutCoroutine = null; }

        if (killed)
        {
            AnnoncePanel.Instance?.Announce($"Le Boss Géant du Palier {palier} a été vaincu !");
            // Récompenses (XP/Aeris/Prestige/loot) déjà gérées par GameEventBus.OnMobKilled dès
            // que Mob.Die() publie l'event — le boss est un Mob normal, rien à faire ici.
        }
        else
        {
            if (_aliveBoss != null) Destroy(_aliveBoss);
            AnnoncePanel.Instance?.Announce($"Le Boss Géant du Palier {palier} s'est retiré...");
        }

        _aliveBoss = null;
    }

    // =========================================================
    // POSITION ALÉATOIRE SUR LE NAVMESH
    // =========================================================

    /// <summary>Point barycentrique uniforme dans un triangle du NavMesh baké de la scène
    /// ACTIVE — voir spec §Position de spawn. Pas de biais par aire de triangle (acceptable,
    /// pas une distribution statistiquement critique). Retourne false si le NavMesh est vide/
    /// absent — un `out Vector3.zero` en cas d'échec aurait été un piège : (0,0,0) est une
    /// position VALIDE sur un vrai NavMesh (l'origine du monde), donc pas utilisable comme
    /// sentinelle d'échec — d'où le bool de retour plutôt qu'un simple Vector3.</summary>
    private static bool TryGetRandomNavMeshPoint(out Vector3 point)
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
}
