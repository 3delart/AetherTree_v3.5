using System.Collections.Generic;
using UnityEngine;

// =============================================================
// LOOTTABLE.CS — Table de loot configurable par SO
// Path : Assets/Scripts/Data/Loot/LootTable.cs
// AetherTree GDD v30 — Section 33.2
//
// Glisser directement les SO dans LootEntry :
//   WeaponData, ArmorData, HelmetData, GlovesData, BootsData,
//   JewelryData, SpiritData, ConsumableData, ResourceData,
//   GemData, RuneData
//
// RollAll() → LootRollResult contenant des InventoryItem prêts
// à être ajoutés dans InventorySystem.
// =============================================================

// ── LootEntry — une ligne de drop ────────────────────────────
[System.Serializable]
public class LootEntry
{
    [Tooltip("Item droppé — glisser le SO directement ici")]
    public ScriptableObject itemSO;

    [Range(0f, 1f)]
    [Tooltip("Probabilité de drop — 1.0 = toujours droppé")]
    public float dropChance = 0.1f;

    [Tooltip("Quantité min droppée")]
    public int minQuantity = 1;

    [Tooltip("Quantité max droppée")]
    public int maxQuantity = 1;

    [Range(0f, 1f)]
    [Tooltip("Biais vers la quantité max — 0 = toujours min | 0.5 = uniforme | 1 = toujours max")]
    public float quantityBias = 0.5f;
}

// ── Résultat d'un roll ────────────────────────────────────────
public class LootRollResult
{
    public List<InventoryItem> items = new List<InventoryItem>();
    public int                 aeris = 0;
    public int                 xp    = 0;
}

// ── LootResult legacy ────────────────────────────────────────
[System.Serializable]
public class LootResult
{
    public string itemID;
    public int    quantity;
    public LootResult(string id, int qty) { itemID = id; quantity = qty; }
}

[CreateAssetMenu(fileName = "loot_", menuName = "AetherTree/Mob/LootTable")]
public class LootTable : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"loot_\"\n" +
             "(ex: \"loot_loup\"). Purement pour l'uniformité du Project window — jamais lu par\n" +
             "string en code, LootTable est toujours référencé par lien direct.")]
    public string lootID;

    [Header("Drops d'items")]
    public List<LootEntry> entries = new List<LootEntry>();

    [Header("Aeris (monnaie)")]
    [Range(0f, 1f)]
    [Tooltip("Probabilité de dropper des Aeris — 0 = jamais | 1 = toujours")]
    public float aerisDropChance = 0.5f;
    [Tooltip("Drop Aeris minimum")]
    public int minAeris = 0;
    [Tooltip("Drop Aeris maximum")]
    public int maxAeris = 0;

    [Header("XP")]
    [Tooltip("XP accordé aux joueurs éligibles à la mort du mob.")]
    public int xpReward = 0;

    // =========================================================
    // ROLL
    // =========================================================

    /// <summary>
    /// Roll complet — retourne items + aeris + xp.
    /// Chaque entry est tirée indépendamment.
    /// </summary>
    public LootRollResult RollAll()
    {
        var result = new LootRollResult
        {
            aeris = Random.Range(minAeris, maxAeris + 1),
            xp    = xpReward,
        };

        foreach (LootEntry entry in entries)
        {
            if (entry == null) continue;
            if (Random.value > entry.dropChance) continue;

            int qty = RollQuantity(entry);
            if (qty <= 0) continue;

            var item = CreateInventoryItem(entry, qty);
            if (item != null) result.items.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Calcule la quantité droppée selon quantityBias.
    /// quantityBias = 0   → toujours minQuantity
    /// quantityBias = 0.5 → uniforme entre min et max
    /// quantityBias = 1   → toujours maxQuantity
    /// Entre 0.5 et 1 : favorise le haut de la fourchette.
    /// Entre 0 et 0.5 : favorise le bas.
    /// </summary>
    private int RollQuantity(LootEntry entry)
    {
        if (entry.minQuantity >= entry.maxQuantity) return entry.minQuantity;

        // Random dans [0..1] pondéré par bias
        // On tire deux fois et on prend le max (bias > 0.5) ou le min (bias < 0.5)
        float r1 = Random.value;
        float r2 = Random.value;
        float t;
        if (entry.quantityBias >= 0.5f)
        {
            // Favorise le haut — plus le bias est proche de 1, plus on prend le max des deux rolls
            float blend = (entry.quantityBias - 0.5f) * 2f; // [0..1]
            t = Mathf.Lerp(Mathf.Min(r1, r2), Mathf.Max(r1, r2), blend);
        }
        else
        {
            // Favorise le bas — plus le bias est proche de 0, plus on prend le min des deux rolls
            float blend = entry.quantityBias * 2f; // [0..1]
            t = Mathf.Lerp(0f, Mathf.Min(r1, r2), blend);
        }

        return Mathf.RoundToInt(Mathf.Lerp(entry.minQuantity, entry.maxQuantity, t));
    }

    /// <summary>Crée un InventoryItem depuis une LootEntry.</summary>
    private InventoryItem CreateInventoryItem(LootEntry entry, int qty)
    {
        if (entry.itemSO == null) return null;

        switch (entry.itemSO)
        {
            case WeaponData wd:
                return new InventoryItem(wd.CreateDropInstance(WeaponData.RollRarity()));

            case ArmorData ad:
                return new InventoryItem(ad.CreateDropInstance(ArmorData.RollRarity()));

            case HelmetData hd:
                return new InventoryItem(hd.CreateInstance());

            case GlovesData gd:
                return new InventoryItem(gd.CreateInstance());

            case BootsData bd:
                return new InventoryItem(bd.CreateInstance());

            case JewelryData jd:
                return new InventoryItem(jd.CreateInstance());

            case SpiritData sd:
                return new InventoryItem(new SpiritInstance(sd));

            case ConsumableData cd:
                return new InventoryItem(cd.CreateInstance(qty));

            case ResourceData rd:
                return new InventoryItem(rd.CreateInstance(qty));

            case GemData gemD:
                return new InventoryItem(gemD.CreateDropInstance());

            case RuneData runeD:
                return new InventoryItem(runeD.CreateDropInstance());

            default:
                Debug.LogWarning($"[LootTable] Type SO non reconnu : {entry.itemSO.GetType().Name}");
                return null;
        }
    }

    /// <summary>Legacy — retourne les itemIDs string pour compatibilité.</summary>
    public List<LootResult> RollLoot()
    {
        var result = new List<LootResult>();
        foreach (LootEntry entry in entries)
        {
            if (entry == null) continue;
            if (Random.value > entry.dropChance) continue;
            int qty = RollQuantity(entry);
            if (qty <= 0) continue;
            string id = entry.itemSO != null ? entry.itemSO.name : "";
            if (!string.IsNullOrEmpty(id))
                result.Add(new LootResult(id, qty));
        }
        return result;
    }

    public int RollAeris() => Random.value <= aerisDropChance ? Random.Range(minAeris, maxAeris + 1) : 0;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(lootID))
            lootID = name;
    }
#endif
}
