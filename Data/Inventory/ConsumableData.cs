using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CONSUMABLEDATA.CS — ScriptableObject template de consommable
// Path : Assets/Scripts/Data/Inventory/ConsumableData.cs
// AetherTree GDD v3.6
//
// Hérite de ItemData (itemID, displayName, description, icon,
// isStackable, stackSize, requiredLevel... — voir ItemData). Pas de
// EquipmentConfig — consommable pur, pas d'équipement.
//
// Types de consommables :
//   Potion        — restaure HP/Mana, applique un BuffData
//   Food          — nourriture (Cuisiner) — mêmes champs que Potion, catégorie distincte
//   DungeonKey    — clé d'accès à un donjon (Classique ET Déblocage, même type — renommé
//                   depuis DungeonStone pour ne plus laisser croire à un type par sous-catégorie)
//   TeleportItem  — téléportation vers une zone
//   RewardChest   — à l'usage, tire 1 item pondéré dans chestEntries et l'ajoute direct à
//                   l'inventaire (voir RollChestEntry() plus bas) — jamais un asset séparé,
//                   les entrées d'un coffre ne sont jamais partagées entre deux consommables
//   Other         — effet spécial custom
//
// RuneData/GemData ne sont PAS des ConsumableType — ce sont des ScriptableObject à
// part entière (RuneInstance/GemInstance gérés séparément, voir Data/Inventory/RuneData.cs
// et GemData.cs), jamais wrappés dans un consommable.
//
// Usage :
//   ConsumableData SO → CreateInstance() → ConsumableInstance
//   Player.UseConsumable(instance) → applique l'effet
// =============================================================

public enum ConsumableType
{
    Potion       = 0, // Restaure HP et/ou Mana, applique un BuffData
    Food         = 1, // Nourriture (Cuisiner) — mêmes champs que Potion, catégorie distincte
    DungeonKey   = 2, // Ouvre l'accès à un donjon spécifique (Classique ET Déblocage)
    TeleportItem = 3, // Téléporte vers une zone
    Other        = 4, // Effet custom
    RewardChest  = 5, // Tire 1 item pondéré dans chestEntries à l'usage — ajouté après coup,
                       // TOUJOURS en fin d'enum (ordinal safety).
    AetherEcho   = 6, // Diffuse un message dans le chat (canal Écho d'Aether), consommé par
                       // ChatSystem.TrySendPlayerMessage au moment de l'ENVOI réel — PAS un
                       // effet instantané à l'usage comme les autres types, volontairement PAS
                       // branché dans ConsoBarUI.UseConsumable (voir Systems/ChatSystem.cs).
}

[System.Serializable]
public class ChestEntry
{
    [Tooltip("Item potentiellement gagnant — glisser le SO directement ici.")]
    public ScriptableObject itemSO;

    [Tooltip("Poids RELATIF, pas une probabilité 0-1 — comparé à la somme des poids de toutes " +
             "les entrées. Ex: 3 entrées de poids 10/5/1 → 62.5% / 31.25% / 6.25%.")]
    [Min(0.01f)]
    public float weight = 1f;

    public int quantity = 1;
}

[CreateAssetMenu(fileName = "cons_", menuName = "AetherTree/Inventaire/ConsumableData")]
public class ConsumableData : ItemData
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ConsumableType  consumableType = ConsumableType.Potion;

    // ── Clé de donjon ─────────────────────────────────────────
    [Tooltip("VFX accroché au joueur pendant l'attente entre la consommation de cette clé et le " +
             "franchissement du portail gaté du donjon — voir InstanceSession.AttachLeaderVfx(), " +
             "appelé par ConsoBarUI juste après un ArmEntry() réussi. Null = rien.")]
    [ShowIf(nameof(consumableType), ConsumableType.DungeonKey, Header = "Clé de donjon (si consumableType = DungeonKey)")]
    public GameObject dungeonEntryVfx;

    // ── Potion / Food — mêmes champs, à plat, pas de sous-section ──
    [Tooltip("Cooldown avant de pouvoir réutiliser cet objet (secondes).")]
    [ShowIf(nameof(consumableType), ConsumableType.Potion, ConsumableType.Food)]
    public float cooldown = 30f;
    [Tooltip("HP restaurés. 0 = pas de soin HP.")]
    [ShowIf(nameof(consumableType), ConsumableType.Potion, ConsumableType.Food)]
    public float healHP   = 0f;
    [Tooltip("Mana restaurée. 0 = pas de soin Mana.")]
    [ShowIf(nameof(consumableType), ConsumableType.Potion, ConsumableType.Food)]
    public float healMana = 0f;
    [Tooltip("Buff appliqué à l'utilisation (optionnel) — sa propre durée est définie sur le BuffData lui-même.")]
    [ShowIf(nameof(consumableType), ConsumableType.Potion, ConsumableType.Food)]
    public BuffData buffEffect;

    // ── Téléportation ─────────────────────────────────────────
    [Tooltip("ID de la zone de destination.")]
    [ShowIf(nameof(consumableType), ConsumableType.TeleportItem, Header = "Téléportation (si consumableType = TeleportItem)")]
    public string targetZoneID = "";

    // ── Coffre ────────────────────────────────────────────────
    [Tooltip("Un seul gagnant tiré parmi ces entrées au prorata de leur poids — jamais vide, " +
             "jamais deux items en un coffre. Différent de LootTable (Data/Mobs/LootTable.cs), " +
             "qui tire CHAQUE entrée indépendamment (pensé pour les drops de mob, pas un coffre).")]
    [ShowIf(nameof(consumableType), ConsumableType.RewardChest, Header = "Coffre (si consumableType = RewardChest)")]
    public List<ChestEntry> chestEntries = new List<ChestEntry>();

    /// <summary>Tire UN gagnant pondéré et retourne l'InventoryItem correspondant — null si
    /// aucune entrée valide (liste vide, ou somme des poids nulle). rarityRank est la rareté déjà
    /// rollée à l'accord du coffre (voir ConsumableInstance.chestRarity) — appliquée à l'entrée
    /// gagnante si c'est une Arme/Armure, voir ItemDropFactory.</summary>
    public InventoryItem RollChestEntry(int rarityRank)
    {
        float totalWeight = 0f;
        foreach (var entry in chestEntries)
            if (entry != null && entry.itemSO != null) totalWeight += Mathf.Max(0f, entry.weight);

        if (totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cursor = 0f;

        foreach (var entry in chestEntries)
        {
            if (entry == null || entry.itemSO == null) continue;
            cursor += Mathf.Max(0f, entry.weight);
            if (roll > cursor) continue;

            return ItemDropFactory.CreateInventoryItem(entry.itemSO, entry.quantity, rarityOverride: rarityRank);
        }

        return null; // ne devrait arriver qu'en cas d'imprécision flottante extrême
    }

    // ── Utilitaires ───────────────────────────────────────────
    public ConsumableInstance CreateInstance(int quantity = 1)
        => new ConsumableInstance(this, Mathf.Clamp(quantity, 1, stackSize));

#if UNITY_EDITOR
    private void Reset()
    {
        isStackable = true; // toujours empilable — quantity/Add()/Remove() déjà en place sur l'instance
    }
#endif
}

// =============================================================
// CONSUMABLEINSTANCE — données runtime d'un consommable
// =============================================================
[System.Serializable]
public class ConsumableInstance
{
    public ConsumableData data;
    public int            quantity = 1;

    [Tooltip("Rareté rollée UNE FOIS à l'accord du coffre (InstanceSession.OnBossKilled(), via " +
             "WeaponData.RollRarity()) — null pour tout consommable qui n'est pas un RewardChest. " +
             "Reste figée jusqu'à l'ouverture (ConsumableData.RollChestEntry() la reçoit en " +
             "paramètre).")]
    public int?            chestRarity;

    public ConsumableInstance(ConsumableData source, int qty = 1, int? rolledChestRarity = null)
    {
        data        = source;
        quantity    = qty;
        chestRarity = rolledChestRarity;
    }

    public string ItemId    => data != null ? data.itemID : "unknown_consumable";
    public string Name      => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Consumable";
    public Sprite Icon      => data?.icon;
    public int    MaxStack  => data?.stackSize ?? 99;
    public bool   IsEmpty   => quantity <= 0;

    /// <summary>Nom coloré par rareté pour un RewardChest déjà rollé (voir chestRarity) — même
    /// patron que WeaponInstance/ArmorInstance.DisplayNameRich. Retombe sur Name pour tout le
    /// reste (pas de rareté sur un consommable normal).</summary>
    public string DisplayNameRich =>
        (data != null && data.consumableType == ConsumableType.RewardChest && chestRarity.HasValue)
            ? $"<color={RarityTier.GetColorHex(chestRarity.Value)}>{RarityTier.GetName(chestRarity.Value)} {Name}</color>"
            : Name;

    /// <summary>Ajoute une quantité au stack. Retourne le surplus si dépassement.</summary>
    public int Add(int amount)
    {
        int total   = quantity + amount;
        quantity    = Mathf.Min(total, MaxStack);
        return Mathf.Max(0, total - MaxStack);
    }

    /// <summary>Retire une quantité. Retourne false si stock insuffisant.</summary>
    public bool Remove(int amount = 1)
    {
        if (quantity < amount) return false;
        quantity -= amount;
        return true;
    }
}
