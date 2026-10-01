using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

// =============================================================
// SPAWNMANAGER.CS — Zones de spawn aléatoire
// AetherTree GDD v3.1 — Section World
//
// Réservé aux deux cas qui ont besoin de position aléatoire sur la
// map : boss de map (spawn errant, pas de placement fixe) et
// ressources rares (doivent être dures à trouver). Tout le reste
// (mobs classiques, mobs/boss de donjon, ressources courantes) est
// placé à la main dans la scène (voir Mob.cs / ResourceNode.cs).
// =============================================================

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    // =========================================================
    // STRUCTURES — BOSS DE MAP
    // =========================================================

    [System.Serializable]
    public class MobSpawnEntry
    {
        public MobData mobData;
    }

    [System.Serializable]
    public class SpawnZone
    {
        [Header("Zone")]
        public string  zoneName = "Zone";
        public Vector3 center;
        public Vector3 size     = new Vector3(20f, 0f, 20f);

        [Header("Boss")]
        [Tooltip("Un seul boss actif à la fois. Plusieurs entrées = tirage uniforme entre elles.")]
        public List<MobSpawnEntry> mobEntries;
        public int minLevel = 1;
        public int maxLevel = 5;

        [Header("Respawn")]
        [Tooltip("Fourchette de délai avant réapparition, en secondes — long car boss rare.")]
        public float minRespawnDelay = 3600f;
        public float maxRespawnDelay = 7200f;

        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public GameObject aliveBoss;
    }

    // =========================================================
    // STRUCTURES — RESSOURCES RARES
    // =========================================================

    [System.Serializable]
    public class ResourceSpawnEntry
    {
        [Tooltip("ResourceData avec resourceType = Collectible.")]
        public ResourceData resourceData;
    }

    [System.Serializable]
    public class ResourceSpawnZone
    {
        [Header("Zone")]
        public string  zoneName = "ResourceZone";
        public Vector3 center;
        public Vector3 size     = new Vector3(20f, 0f, 20f);

        [Header("Ressources")]
        [Tooltip("ResourceData (Collectible) à spawner dans cette zone. Plusieurs entrées = " +
                 "tirage uniforme entre elles.")]
        public List<ResourceSpawnEntry> resourceEntries;

        [Tooltip("Nombre de nodes simultanés dans la zone.")]
        public int nodeCount = 1;

        [Header("Respawn")]
        [Tooltip("Fourchette de délai avant réapparition, en secondes — long car ressource rare.")]
        public float minRespawnDelay = 600f;
        public float maxRespawnDelay = 1800f;

        [Header("Runtime — ne pas modifier")]
        [Tooltip("Taille nodeCount — un slot null est soit pas encore spawn, soit en cooldown " +
                 "(voir WorldStateRegistry, identifier = \"zoneName#index\"). Remplace " +
                 "l'ancien aliveNodes/pendingRespawns, qui ne portaient aucune identité par node.")]
        [HideInInspector] public GameObject[] nodeSlots;
    }

    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Zones de spawn — Boss de map")]
    public List<SpawnZone> zones = new List<SpawnZone>();

    [Header("Zones de spawn — Ressources rares")]
    public List<ResourceSpawnZone> resourceZones = new List<ResourceSpawnZone>();

    private string _sceneName;

#if UNITY_EDITOR
    private void OnValidate()
    {
        foreach (SpawnZone zone in zones)
        {
            if (zone.mobEntries == null) continue;
            foreach (MobSpawnEntry entry in zone.mobEntries)
                if (entry.mobData != null && entry.mobData.mobType != MobType.WorldBoss)
                    Debug.LogWarning($"[SpawnManager] {zone.zoneName} : {entry.mobData.mobName} a mobType = " +
                        $"{entry.mobData.mobType}, attendu WorldBoss pour un boss de map errant.");
        }

        // zoneName est la base de l'identifiant WorldStateRegistry ("scene|zoneName" pour un
        // boss, "scene|zoneName#index" pour un node) — un doublon entre deux zones (même boss/
        // boss+ressource) ferait collisionner leurs timers dans le registre.
        var seenNames = new HashSet<string>();
        foreach (SpawnZone zone in zones)
            if (!string.IsNullOrEmpty(zone.zoneName) && !seenNames.Add(zone.zoneName))
                Debug.LogWarning($"[SpawnManager] zoneName \"{zone.zoneName}\" en double — collision " +
                    "possible dans WorldStateRegistry, renomme l'une des deux zones.");
        foreach (ResourceSpawnZone rZone in resourceZones)
            if (!string.IsNullOrEmpty(rZone.zoneName) && !seenNames.Add(rZone.zoneName))
                Debug.LogWarning($"[SpawnManager] zoneName \"{rZone.zoneName}\" en double — collision " +
                    "possible dans WorldStateRegistry, renomme l'une des deux zones.");
    }
#endif

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => SceneLoader.OnMapLoaded += OnMapReady;
    private void OnDisable() => SceneLoader.OnMapLoaded -= OnMapReady;

    private bool _initialSpawnDone = false;

    private void Start()
    {
        StartCoroutine(SpawnNextFrame());
    }

    private void OnMapReady(string mapName) { }

    private IEnumerator SpawnNextFrame()
    {
        yield return null;

        _sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        NavMeshSurface surface = FindObjectOfType<NavMeshSurface>();
        if (surface != null)
            surface.BuildNavMesh();

        yield return null;
        yield return null;

        foreach (SpawnZone zone in zones)
        {
            // Zone en cooldown restaurée (save existante) — ne spawn pas tout de suite,
            // Update() la surveillera. Zone jamais enregistrée (premier lancement de session) —
            // spawn immédiat, comportement d'avant ce chantier.
            if (!WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, zone.zoneName, out _, out _))
                SpawnBoss(zone);
        }

        foreach (ResourceSpawnZone rZone in resourceZones)
            SpawnAllNodesInZone(rZone);

        _initialSpawnDone = true;
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (!_initialSpawnDone) return;

        // ── Boss de map ──────────────────────────────────────
        foreach (SpawnZone zone in zones)
        {
            if (zone.aliveBoss != null) continue; // boss vivant, rien à faire

            if (WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, zone.zoneName, out float remaining, out Vector3 pos)
                && remaining <= 0f)
            {
                WorldStateRegistry.Instance.ClearZone(_sceneName, zone.zoneName);
                SpawnBoss(zone, pos);
            }
        }

        // ── Ressources ────────────────────────────────────────
        foreach (ResourceSpawnZone rZone in resourceZones)
        {
            if (rZone.nodeSlots == null) continue;
            for (int i = 0; i < rZone.nodeSlots.Length; i++)
            {
                if (rZone.nodeSlots[i] != null) continue; // node vivant, rien à faire

                string identifier = $"{rZone.zoneName}#{i}";
                if (WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, identifier, out float remaining, out Vector3 pos)
                    && remaining <= 0f)
                {
                    WorldStateRegistry.Instance.ClearZone(_sceneName, identifier);
                    SpawnOneNode(rZone, i, pos);
                }
            }
        }
    }

    // =========================================================
    // SPAWN BOSS DE MAP
    // =========================================================

    /// <summary>spawnPos : null = premier spawn de session (tire une position fraîche dans la
    /// zone) ; une valeur fournie = respawn restauré depuis WorldStateRegistry, à LA POSITION
    /// DÉJÀ DÉCIDÉE à la mort précédente (jamais re-tirée ici).</summary>
    private void SpawnBoss(SpawnZone zone, Vector3? spawnPos = null)
    {
        MobData chosenData = RollMobData(zone);
        if (chosenData == null) { Debug.LogWarning($"[SPAWN] {zone.zoneName} — RollMobData retourne null"); return; }
        if (chosenData.prefab == null) { Debug.LogWarning($"[SpawnManager] {chosenData.mobName} n'a pas de prefab !"); return; }

        int     level = Random.Range(zone.minLevel, zone.maxLevel + 1);
        Vector3 pos   = spawnPos ?? GetRandomPosition(zone.center, zone.size);

        GameObject mobObj = Instantiate(chosenData.prefab, pos, Quaternion.identity);
        Mob mob = mobObj.GetComponent<Mob>();
        if (mob != null)
        {
            mob.InitializeSpawn(chosenData, level);
            var capturedZone = zone;
            mob.OnDeath(() =>
            {
                capturedZone.aliveBoss = null;
                Vector3 nextPos = GetRandomPosition(capturedZone.center, capturedZone.size);
                float   delay   = Random.Range(capturedZone.minRespawnDelay, capturedZone.maxRespawnDelay);
                WorldStateRegistry.Instance.RegisterRespawn(_sceneName, capturedZone.zoneName, delay, nextPos);
            });
        }
        zone.aliveBoss = mobObj;
    }

    // =========================================================
    // SPAWN RESSOURCES
    // =========================================================

    private void SpawnAllNodesInZone(ResourceSpawnZone rZone)
    {
        rZone.nodeSlots = new GameObject[rZone.nodeCount];
        for (int i = 0; i < rZone.nodeCount; i++)
        {
            // Slot en cooldown restauré (save existante) — ne spawn pas tout de suite, Update()
            // le surveillera. Slot jamais enregistré (premier lancement) — spawn immédiat.
            if (!WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, $"{rZone.zoneName}#{i}", out _, out _))
                SpawnOneNode(rZone, i, null);
        }
    }

    /// <summary>spawnPos : null = premier spawn de session (tire une position fraîche) ; une
    /// valeur fournie = respawn restauré depuis WorldStateRegistry, à LA POSITION DÉJÀ DÉCIDÉE à
    /// l'épuisement précédent (jamais re-tirée ici).</summary>
    private void SpawnOneNode(ResourceSpawnZone rZone, int slotIndex, Vector3? spawnPos)
    {
        ResourceData data = RollResourceData(rZone);
        if (data == null)
        {
            Debug.LogWarning($"[SPAWN] {rZone.zoneName} — aucune ResourceData valide.");
            return;
        }
        if (data.nodePrefab == null)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — nodePrefab non assigné !");
            return;
        }
        if (data.resourceType != ResourceType.Collectible)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — resourceType doit être Collectible !");
            return;
        }

        Vector3    pos     = spawnPos ?? GetRandomPosition(rZone.center, rZone.size);
        GameObject nodeObj = Instantiate(data.nodePrefab, pos, Quaternion.identity);

        // Ajoute ResourceNode si absent sur le prefab
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null) node = nodeObj.AddComponent<ResourceNode>();

        var capturedZone = rZone;
        var capturedSlot = slotIndex;

        node.InitFromSpawner(data, () =>
        {
            capturedZone.nodeSlots[capturedSlot] = null;
            Vector3 nextPos = GetRandomPosition(capturedZone.center, capturedZone.size);
            float   delay   = Random.Range(capturedZone.minRespawnDelay, capturedZone.maxRespawnDelay);
            WorldStateRegistry.Instance.RegisterRespawn(_sceneName, $"{capturedZone.zoneName}#{capturedSlot}", delay, nextPos);
        });

        rZone.nodeSlots[slotIndex] = nodeObj;
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private MobData RollMobData(SpawnZone zone)
    {
        if (zone.mobEntries == null || zone.mobEntries.Count == 0) return null;
        return zone.mobEntries[Random.Range(0, zone.mobEntries.Count)].mobData;
    }

    private ResourceData RollResourceData(ResourceSpawnZone rZone)
    {
        if (rZone.resourceEntries == null || rZone.resourceEntries.Count == 0) return null;
        return rZone.resourceEntries[Random.Range(0, rZone.resourceEntries.Count)].resourceData;
    }

    private Vector3 GetRandomPosition(Vector3 center, Vector3 size)
    {
        Vector3 randomPos = center + new Vector3(
            Random.Range(-size.x / 2f, size.x / 2f),
            0f,
            Random.Range(-size.z / 2f, size.z / 2f));

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(randomPos, out hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;

        return center;
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (zones != null)
            foreach (SpawnZone zone in zones)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
                Gizmos.DrawCube(zone.center, zone.size);
                Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
                Gizmos.DrawWireCube(zone.center, zone.size);
#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(zone.center + Vector3.up * 2f,
                    $"[BOSS] {zone.zoneName}\n{(zone.aliveBoss != null ? "actif" : "en attente")}");
#endif
            }

        if (resourceZones != null)
            foreach (ResourceSpawnZone rZone in resourceZones)
            {
                Gizmos.color = new Color(0.8f, 0.65f, 0.2f, 0.2f);
                Gizmos.DrawCube(rZone.center, rZone.size);
                Gizmos.color = new Color(0.8f, 0.65f, 0.2f, 0.8f);
                Gizmos.DrawWireCube(rZone.center, rZone.size);
#if UNITY_EDITOR
                UnityEditor.Handles.color = new Color(0.8f, 0.65f, 0.2f);
                int aliveCount = 0;
                if (rZone.nodeSlots != null)
                    foreach (var slot in rZone.nodeSlots)
                        if (slot != null) aliveCount++;
                UnityEditor.Handles.Label(rZone.center + Vector3.up * 2f,
                    $"[RES] {rZone.zoneName}\n{aliveCount}/{rZone.nodeCount}");
#endif
            }
    }
}
