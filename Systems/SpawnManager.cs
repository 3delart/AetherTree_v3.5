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
        [HideInInspector] public bool       pendingRespawn;
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
        [HideInInspector] public List<GameObject> aliveNodes     = new List<GameObject>();
        [HideInInspector] public int              pendingRespawns = 0;
    }

    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Zones de spawn — Boss de map")]
    public List<SpawnZone> zones = new List<SpawnZone>();

    [Header("Zones de spawn — Ressources rares")]
    public List<ResourceSpawnZone> resourceZones = new List<ResourceSpawnZone>();

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

        NavMeshSurface surface = FindObjectOfType<NavMeshSurface>();
        if (surface != null)
            surface.BuildNavMesh();

        yield return null;
        yield return null;

        foreach (SpawnZone zone in zones)
            SpawnBoss(zone);

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
            if (zone.aliveBoss == null && !zone.pendingRespawn)
            {
                zone.pendingRespawn = true;
                float delay = Random.Range(zone.minRespawnDelay, zone.maxRespawnDelay);
                StartCoroutine(RespawnBossAfterDelay(zone, delay));
            }
        }

        // ── Ressources — nettoyage null ───────────────────────
        foreach (ResourceSpawnZone rZone in resourceZones)
            rZone.aliveNodes.RemoveAll(n => n == null);
    }

    // =========================================================
    // SPAWN BOSS DE MAP
    // =========================================================

    private void SpawnBoss(SpawnZone zone)
    {
        MobData chosenData = RollMobData(zone);
        if (chosenData == null) { Debug.LogWarning($"[SPAWN] {zone.zoneName} — RollMobData retourne null"); return; }
        if (chosenData.prefab == null) { Debug.LogWarning($"[SpawnManager] {chosenData.mobName} n'a pas de prefab !"); return; }

        int     level    = Random.Range(zone.minLevel, zone.maxLevel + 1);
        Vector3 spawnPos = GetRandomPosition(zone.center, zone.size);

        GameObject mobObj = Instantiate(chosenData.prefab, spawnPos, Quaternion.identity);
        Mob mob = mobObj.GetComponent<Mob>();
        if (mob != null)
        {
            mob.InitializeSpawn(chosenData, level);
            var capturedZone = zone;
            mob.OnDeath(() => capturedZone.aliveBoss = null);
        }
        zone.aliveBoss = mobObj;
    }

    private IEnumerator RespawnBossAfterDelay(SpawnZone zone, float delay)
    {
        yield return new WaitForSeconds(delay);
        zone.pendingRespawn = false;
        SpawnBoss(zone);
    }

    // =========================================================
    // SPAWN RESSOURCES
    // =========================================================

    private void SpawnAllNodesInZone(ResourceSpawnZone rZone)
    {
        for (int i = 0; i < rZone.nodeCount; i++)
            SpawnOneNode(rZone);
    }

    private void SpawnOneNode(ResourceSpawnZone rZone)
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

        Vector3    spawnPos = GetRandomPosition(rZone.center, rZone.size);
        GameObject nodeObj  = Instantiate(data.nodePrefab, spawnPos, Quaternion.identity);

        // Ajoute ResourceNode si absent sur le prefab
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null) node = nodeObj.AddComponent<ResourceNode>();

        var capturedZone = rZone;

        node.InitFromSpawner(data, () =>
        {
            capturedZone.aliveNodes.Remove(nodeObj);
            capturedZone.pendingRespawns++;
            StartCoroutine(RespawnNodeAfterDelay(capturedZone));
        });

        rZone.aliveNodes.Add(nodeObj);
    }

    private IEnumerator RespawnNodeAfterDelay(ResourceSpawnZone rZone)
    {
        yield return new WaitForSeconds(Random.Range(rZone.minRespawnDelay, rZone.maxRespawnDelay));
        rZone.pendingRespawns--;
        SpawnOneNode(rZone);
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
                UnityEditor.Handles.Label(rZone.center + Vector3.up * 2f,
                    $"[RES] {rZone.zoneName}\n{rZone.aliveNodes.Count}/{rZone.nodeCount}");
#endif
            }
    }
}
