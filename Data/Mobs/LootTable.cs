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

    [Tooltip("Coché : cette entrée ne peut dropper QUE si au moins un joueur éligible a\n" +
             "requiredQuest Active. La quantité droppée est aussi plafonnée pour ne jamais\n" +
             "dépasser ce qu'il manque à l'objectif Gather correspondant (requiredCount -\n" +
             "currentCount) — un joueur ne peut jamais accumuler plus que le nécessaire.")]
    public bool questLootOnly = false;

    [Tooltip("Quête devant être Active pour qu'un joueur éligible compte — voir questLootOnly.\n" +
             "L'objectif Gather de CETTE quête ciblant le même item (itemSO ci-dessus) sert aussi\n" +
             "à calculer le plafond de quantité.")]
    [ShowIf(nameof(questLootOnly), true)]
    public QuestData requiredQuest;
}

// ── Un item roulé + son éventuelle restriction de gagnant ────
public class RolledItem
{
    public InventoryItem item;

    /// <summary>null = n'importe quel joueur éligible peut gagner cet item (comportement
    /// normal). Non-null = SEUL ce joueur peut le recevoir (drop questLootOnly — voir
    /// LootTable.QuestLootCap). Tant que QuestSystem reste global (aucun suivi de quête PAR
    /// JOUEUR aujourd'hui), c'est le premier joueur éligible non-null, pas forcément "celui qui
    /// a vraiment la quête" en multijoueur — limitation connue, documentée, pas un bug de ce
    /// fix : une vraie correction par-joueur demande une refonte de QuestSystem lui-même.</summary>
    public Player restrictedTo;
}

// ── Résultat d'un roll ────────────────────────────────────────
public class LootRollResult
{
    public List<RolledItem> items    = new List<RolledItem>();
    public int              aeris    = 0;
    public int              xp       = 0;
    public int              prestige = 0;
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

    [Header("Prestige")]
    [Tooltip("Prestige accordé à la mort du mob — 0 par défaut, la plupart des mobs n'en donnent " +
             "pas (voir spec Prestige/Aura §1.4). Réservé aux boss/mobs notables.")]
    public int prestigeReward = 0;

    // =========================================================
    // ROLL
    // =========================================================

    /// <summary>
    /// Roll complet — retourne items + aeris + xp. Chaque entry est tirée indépendamment.
    /// eligiblePlayers sert UNIQUEMENT au gating questLootOnly (voir LootEntry) — null/vide
    /// désactive silencieusement toute entrée questLootOnly (pas d'exception), compatible avec
    /// les appelants qui n'ont pas de contexte joueur (aucun aujourd'hui).
    /// </summary>
    public LootRollResult RollAll(List<Player> eligiblePlayers = null)
    {
        var result = new LootRollResult
        {
            aeris    = Random.Range(minAeris, maxAeris + 1),
            xp       = xpReward,
            prestige = prestigeReward,
        };

        foreach (LootEntry entry in entries)
        {
            if (entry == null) continue;

            int?   cap          = null;
            Player restrictedTo = null;
            if (entry.questLootOnly)
            {
                var questCap = QuestLootCap(entry, eligiblePlayers);
                if (questCap == null || questCap.Value.cap <= 0) continue; // pas éligible ou déjà assez
                cap          = questCap.Value.cap;
                restrictedTo = questCap.Value.holder;
            }

            if (Random.value > entry.dropChance) continue;

            int qty = RollQuantity(entry);
            if (cap != null) qty = Mathf.Min(qty, cap.Value);
            if (qty <= 0) continue;

            var item = CreateInventoryItem(entry, qty);
            if (item != null) result.items.Add(new RolledItem { item = item, restrictedTo = restrictedTo });
        }

        return result;
    }

    /// <summary>Plafond de quantité + joueur restreint pour une entry questLootOnly — null si la
    /// quête n'est pas Active (vérifié UNE fois, pas par joueur : QuestSystem est global
    /// aujourd'hui, voir RolledItem.restrictedTo) ou si aucun joueur éligible non-null n'existe.
    /// cap = requiredCount - currentCount de l'objectif Gather de cette quête ciblant le même
    /// item ; holder = premier joueur éligible non-null (voir limitation documentée ci-dessus).</summary>
    private (int cap, Player holder)? QuestLootCap(LootEntry entry, List<Player> eligiblePlayers)
    {
        if (entry.requiredQuest == null || eligiblePlayers == null || QuestSystem.Instance == null) return null;
        if (QuestSystem.Instance.GetQuestState(entry.requiredQuest) != QuestState.Active) return null;

        Player holder = null;
        foreach (var p in eligiblePlayers)
            if (p != null) { holder = p; break; }
        if (holder == null) return null;

        if (entry.requiredQuest.objectives == null) return null;
        foreach (var obj in entry.requiredQuest.objectives)
        {
            if (obj.type != QuestObjectiveType.Gather) continue;
            if ((obj.targetItem as ItemData) != entry.itemSO as ItemData) continue;
            return (Mathf.Max(0, obj.requiredCount - obj.currentCount), holder);
        }
        return null;
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

    /// <summary>Crée un InventoryItem depuis une LootEntry — voir ItemDropFactory (partagé
    /// avec ConsumableData.RollChestEntry(), qui lui force une rareté fixe pour Armes/Armures
    /// au lieu du roll aléatoire habituel).</summary>
    private InventoryItem CreateInventoryItem(LootEntry entry, int qty)
        => ItemDropFactory.CreateInventoryItem(entry.itemSO, qty);

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
