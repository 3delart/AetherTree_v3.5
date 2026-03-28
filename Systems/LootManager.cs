using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// LOOTMANAGER — Spawne les items au sol à la mort des mobs
// Path : Assets/Scripts/Systems/LootManager.cs
// AetherTree GDD v3.5
//
// S'abonne à GameEventBus.OnMobKilled.
// Utilise WorldPickupItem (fusion de WorldLootItem + WorldAerisItem).
// Un seul prefab générique suffit — InitItem() ou InitAeris()
// configure le comportement au runtime.
// =============================================================

public class LootManager : MonoBehaviour
{
    public static LootManager Instance { get; private set; }

    [Header("Prefabs au sol")]
    [Tooltip("Prefab WorldPickupItem pour les items lootés.")]
    public GameObject pickupPrefab;
 
    [Tooltip("Prefab WorldPickupItem pour les Aeris — visuel pièce d'or. " +
             "Si non assigné, utilise pickupPrefab.")]
    public GameObject aerisPrefab;
 

    [Header("Paramètres spawn")]
    [Tooltip("Rayon de dispersion autour de la position de mort.")]
    public float spawnRadius  = 1.5f;

    [Tooltip("Durée de vie des items au sol en secondes.")]
    public float itemLifetime = 30f;

    [Tooltip("Délai avant apparition (laisse le mob mourir visuellement).")]
    public float spawnDelay   = 0.5f;

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => Resubscribe();
    private void OnDisable() => GameEventBus.OnMobKilled -= OnMobKilled;

    public void Resubscribe()
    {
        GameEventBus.OnMobKilled -= OnMobKilled;
        GameEventBus.OnMobKilled += OnMobKilled;
    }

    // =========================================================
    // ÉVÉNEMENT MOB TUÉ
    // =========================================================

    private void OnMobKilled(MobKilledEvent e)
    {
        if (e.eligiblePlayers == null || e.eligiblePlayers.Count == 0) return;

        if (e.mob?.lootTable == null)
        {
            Debug.LogWarning($"[LOOTMANAGER] Pas de LootTable sur {e.mob?.mobName}.");
            return;
        }

        LootRollResult roll = e.mob.lootTable.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        StartCoroutine(SpawnLootDelayed(roll, e.deathPosition));
    }

    // =========================================================
    // SPAWN
    // =========================================================

    private IEnumerator SpawnLootDelayed(LootRollResult roll, Vector3 origin)
    {
        yield return new WaitForSeconds(spawnDelay);

        foreach (InventoryItem item in roll.items)
            SpawnItem(item, origin);

        if (roll.aeris > 0)
            SpawnAeris(roll.aeris, origin);
    }

    private void SpawnItem(InventoryItem item, Vector3 origin)
    {
        if (item == null) return;

        Vector3 spawnPos = RandomSpawnPos(origin);

        // Préfère le prefab 3D du SO si disponible, sinon prefab générique
        GameObject sourcePrefab = GetItemPrefab(item) ?? pickupPrefab;
        GameObject go           = SpawnGO(sourcePrefab, spawnPos);

        var pickup = go.GetComponent<WorldPickupItem>()
                  ?? go.AddComponent<WorldPickupItem>();
        pickup.InitItem(item, itemLifetime);
    }

    private void SpawnAeris(int amount, Vector3 origin)
    {
        if (amount <= 0) return;
 
        Vector3    spawnPos    = RandomSpawnPos(origin);
        GameObject sourcePrefab = aerisPrefab != null ? aerisPrefab : pickupPrefab;
        GameObject go           = SpawnGO(sourcePrefab, spawnPos);
 
        var pickup = go.GetComponent<WorldPickupItem>()
                  ?? go.AddComponent<WorldPickupItem>();
        pickup.InitAeris(amount, itemLifetime * 2f);
    }
 

    // =========================================================
    // HELPERS
    // =========================================================

    /// <summary>Calcule une position aléatoire dans le rayon, collée au terrain.</summary>
    private Vector3 RandomSpawnPos(Vector3 origin)
    {
        Vector2 rand     = Random.insideUnitCircle * spawnRadius;
        Vector3 spawnPos = origin + new Vector3(rand.x, 2f, rand.y);

        if (Physics.Raycast(spawnPos, Vector3.down, out RaycastHit hit, 10f))
            spawnPos = hit.point + Vector3.up * 0.3f;

        return spawnPos;
    }

    /// <summary>Instancie un prefab ou crée une primitive de fallback.</summary>
    private GameObject SpawnGO(GameObject prefab, Vector3 position)
    {
        if (prefab != null)
            return Instantiate(prefab, position, Quaternion.identity);

        // Fallback procédural si aucun prefab assigné
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position   = position;
        go.transform.localScale = Vector3.one * 0.3f;
        return go;
    }

    /// <summary>Récupère le prefab 3D du SO de l'item si disponible.</summary>
    private GameObject GetItemPrefab(InventoryItem item)
    {
        if (item.WeaponInstance?.data?.weaponPrefab  != null) return item.WeaponInstance.data.weaponPrefab;
        if (item.ArmorInstance?.data?.armorPrefab    != null) return item.ArmorInstance.data.armorPrefab;
        if (item.HelmetInstance?.data?.helmetPrefab  != null) return item.HelmetInstance.data.helmetPrefab;
        if (item.GlovesInstance?.data?.glovesPrefab  != null) return item.GlovesInstance.data.glovesPrefab;
        if (item.BootsInstance?.data?.bootsPrefab    != null) return item.BootsInstance.data.bootsPrefab;
        if (item.ConsumableInstance?.data?.prefab    != null) return item.ConsumableInstance.data.prefab;
        if (item.ResourceInstance?.data?.prefab      != null) return item.ResourceInstance.data.prefab;
        return null;
    }
}
