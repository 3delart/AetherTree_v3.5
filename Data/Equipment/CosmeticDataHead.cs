using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CosmeticDataHead — ScriptableObject template de cosmétique tête
// Path : Assets/Scripts/Data/Equipment/CosmeticDataHead.cs
// AetherTree GDD v3.6 — §5.x (Cosmétiques)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// Cosmétique purement visuel — n'apporte aucune stat directement.
// Peut avoir un EquipmentConfig pour des effets passifs cosmétiques
// ou des bonus spéciaux (à définir).
//
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0/§5.2).
// Aucune restriction par WeaponCategory ni ArmorType.
// =============================================================

[CreateAssetMenu(fileName = "cos_head_", menuName = "AetherTree/Inventaire/Equipement/CosmeticDataHead")]
public class CosmeticDataHead : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject cosmeticPrefab;

    // ── Utilitaires ───────────────────────────────────────────

    public CosmeticInstanceHead CreateInstance() => new CosmeticInstanceHead(this);
}

// =============================================================
// CosmeticInstanceHead — wrapper runtime d'un cosmétique tête équipé
// GDD v3.6 — §5.x (Cosmétiques)
// =============================================================
[System.Serializable]
public class CosmeticInstanceHead
{
    public CosmeticDataHead data;

    public CosmeticInstanceHead(CosmeticDataHead source) { data = source; }

    // ── Raccourcis SO ─────────────────────────────────────────
    public string ItemId       => data != null ? data.itemID : "unknown_cosmetic_head";
    public string CosmeticName  => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Cosmetic Head";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // ── Raccourcis config ─────────────────────────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<OnHitReceivedEffectEntry> OnHitReceivedEffects => data?.config?.onHitReceivedEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitDealtEffectEntry>    OnHitDealtEffects    => data?.config?.onHitDealtEffects;
}
