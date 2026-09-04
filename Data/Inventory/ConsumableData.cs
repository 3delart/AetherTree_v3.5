using UnityEngine;

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
//   DungeonStone  — pierre d'accès à un donjon
//   TeleportItem  — téléportation vers une zone
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
    Potion,       // Restaure HP et/ou Mana, applique un BuffData
    Food,         // Nourriture (Cuisiner) — mêmes champs que Potion, catégorie distincte
    DungeonStone, // Ouvre l'accès à un donjon spécifique
    TeleportItem, // Téléporte vers une zone
    Other,        // Effet custom
}

[CreateAssetMenu(fileName = "cons_", menuName = "AetherTree/Inventaire/ConsumableData")]
public class ConsumableData : ItemData
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ConsumableType  consumableType = ConsumableType.Potion;
    public GameObject      prefab;

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

    // ── Pierre de donjon ──────────────────────────────────────
    [Tooltip("ID du donjon accessible avec cette pierre.")]
    [ShowIf(nameof(consumableType), ConsumableType.DungeonStone, Header = "Pierre de donjon (si consumableType = DungeonStone)")]
    public string dungeonID = "";

    // ── Téléportation ─────────────────────────────────────────
    [Tooltip("ID de la zone de destination.")]
    [ShowIf(nameof(consumableType), ConsumableType.TeleportItem, Header = "Téléportation (si consumableType = TeleportItem)")]
    public string targetZoneID = "";

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

    public ConsumableInstance(ConsumableData source, int qty = 1)
    {
        data     = source;
        quantity = qty;
    }

    public string ItemId    => data != null ? data.itemID : "unknown_consumable";
    public string Name      => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Consumable";
    public Sprite Icon      => data?.icon;
    public int    MaxStack  => data?.stackSize ?? 99;
    public bool   IsEmpty   => quantity <= 0;

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
