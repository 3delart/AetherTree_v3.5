using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

// =============================================================
// SPAWNMANAGER.CS — Zones de spawn | respawn | boss | ressources
// AetherTree GDD v3.1 — Section World
//
// Nouveauté v3.1 :
//   ResourceSpawnZone — spawn de ResourceData (Collectible)
//   dans une zone définie. Le nodePrefab est instancié et
//   un ResourceNode est ajouté automatiquement.
//   Respawn automatique après collecte via callback.
// =============================================================

public enum SpawnType
{
    ClassicMob,
    MapBoss,
    DungeonMob,
    DungeonBoss
}

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    // =========================================================
    // STRUCTURES — MOBS (inchangé)
    // =========================================================

    [System.Serializable]
    public class MobSpawnEntry
    {
        public MobData mobData;
        public float   weight = 1f;
    }

    [System.Serializable]
    public class SpawnZone
    {
        [Header("Zone")]
        public string    zoneName  = "Zone";
        public SpawnType spawnType = SpawnType.ClassicMob;
        public Vector3   center;
        public Vector3   size      = new Vector3(20f, 0f, 20f);

        [Header("Mobs")]
        public List<MobSpawnEntry> mobEntries;
        public int minLevel = 1;
        public int maxLevel = 5;

        [Header("Quantité & Timers")]
        public int   mobCount         = 5;
        public float respawnDelay     = 30f;
        public float bossRespawnDelay = 7200f;

        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public List<GameObject> aliveMobs      = new List<GameObject>();
        [HideInInspector] public int              pendingRespawns = 0;
    }

    // =========================================================
    // STRUCTURES — RESSOURCES (nouveau v3.1)
    // =========================================================

    [System.Serializable]
    public class ResourceSpawnEntry
    {
        [Tooltip("ResourceData avec resourceType = Collectible.")]
        public ResourceData resourceData;
        [Tooltip("Poids de probabilité (plus élevé = plus fréquent).")]
        public float weight = 1f;
    }

    [System.Serializable]
    public class ResourceSpawnZone
    {
        [Header("Zone")]
        public string  zoneName = "ResourceZone";
        public Vector3 center;
        public Vector3 size     = new Vector3(20f, 0f, 20f);

        [Header("Ressources")]
        [Tooltip("ResourceData (Collectible) à spawner dans cette zone.")]
        public List<ResourceSpawnEntry> resourceEntries;

        [Tooltip("Nombre de nodes simultanés dans la zone.")]
        public int nodeCount = 5;

        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public List<GameObject> aliveNodes     = new List<GameObject>();
        [HideInInspector] public int              pendingRespawns = 0;
    }

    // =========================================================
    // INSPECTOR
    // =========================================================

    [Header("Zones de spawn — Mobs")]
    public List<SpawnZone> zones = new List<SpawnZone>();

    [Header("Zones de spawn — Ressources collectibles")]
    public List<ResourceSpawnZone> resourceZones = new List<ResourceSpawnZone>();

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
            SpawnAllMobsInZone(zone);

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

        // ── Mobs ──────────────────────────────────────────────
        foreach (SpawnZone zone in zones)
        {
            if (zone.spawnType == SpawnType.DungeonMob ||
                zone.spawnType == SpawnType.DungeonBoss) continue;

            zone.aliveMobs.RemoveAll(m => m == null);

            int maxCount = zone.spawnType == SpawnType.MapBoss ? 1 : zone.mobCount;
            int missing  = maxCount - zone.aliveMobs.Count - zone.pendingRespawns;

            for (int i = 0; i < missing; i++)
            {
                zone.pendingRespawns++;
                float delay = zone.spawnType == SpawnType.MapBoss
                    ? zone.bossRespawnDelay
                    : zone.respawnDelay;
                StartCoroutine(RespawnMobAfterDelay(zone, delay));
            }
        }

        // ── Ressources — nettoyage null ───────────────────────
        foreach (ResourceSpawnZone rZone in resourceZones)
            rZone.aliveNodes.RemoveAll(n => n == null);
    }

    // =========================================================
    // SPAWN MOBS (inchangé)
    // =========================================================

    private void SpawnAllMobsInZone(SpawnZone zone)
    {
        if (zone.spawnType == SpawnType.DungeonMob ||
            zone.spawnType == SpawnType.DungeonBoss) return;

        int count = zone.spawnType == SpawnType.MapBoss ? 1 : zone.mobCount;
        for (int i = 0; i < count; i++)
            SpawnOneMob(zone);
    }

    private void SpawnOneMob(SpawnZone zone)
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
            mob.data     = chosenData;
            mob.mobLevel = level;
            var capturedObj  = mobObj;
            var capturedZone = zone;
            mob.OnDeath(() => capturedZone.aliveMobs.Remove(capturedObj));
        }
        zone.aliveMobs.Add(mobObj);
    }

    private IEnumerator RespawnMobAfterDelay(SpawnZone zone, float delay)
    {
        yield return new WaitForSeconds(delay);
        zone.pendingRespawns--;
        SpawnOneMob(zone);
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
            StartCoroutine(RespawnNodeAfterDelay(capturedZone, data));
        });

        rZone.aliveNodes.Add(nodeObj);
    }

    private IEnumerator RespawnNodeAfterDelay(ResourceSpawnZone rZone, ResourceData data)
    {
        yield return new WaitForSeconds(data.respawnDelay);
        rZone.pendingRespawns--;
        SpawnOneNode(rZone);
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private MobData RollMobData(SpawnZone zone)
    {
        if (zone.mobEntries == null || zone.mobEntries.Count == 0) return null;
        float total = 0f;
        foreach (var e in zone.mobEntries) total += e.weight;
        float roll = Random.Range(0f, total), cumul = 0f;
        foreach (var e in zone.mobEntries)
        {
            cumul += e.weight;
            if (roll <= cumul) return e.mobData;
        }
        return zone.mobEntries[0].mobData;
    }

    private ResourceData RollResourceData(ResourceSpawnZone rZone)
    {
        if (rZone.resourceEntries == null || rZone.resourceEntries.Count == 0) return null;
        float total = 0f;
        foreach (var e in rZone.resourceEntries) total += e.weight;
        float roll = Random.Range(0f, total), cumul = 0f;
        foreach (var e in rZone.resourceEntries)
        {
            cumul += e.weight;
            if (roll <= cumul) return e.resourceData;
        }
        return rZone.resourceEntries[0].resourceData;
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
                Color c = zone.spawnType == SpawnType.MapBoss
                    ? new Color(1f, 0f, 0f, 0.2f)
                    : new Color(0f, 1f, 0.5f, 0.2f);
                Gizmos.color = c;
                Gizmos.DrawCube(zone.center, zone.size);
                Gizmos.color = new Color(c.r, c.g, c.b, 0.8f);
                Gizmos.DrawWireCube(zone.center, zone.size);
#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(zone.center + Vector3.up * 2f,
                    $"{zone.zoneName}\n{zone.aliveMobs.Count}/{zone.mobCount} mobs");
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
