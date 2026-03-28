using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CosmeticDataHead — ScriptableObject template de cosmétique tête
// Path : Assets/Scripts/Data/Inventory/Equipment/CosmeticDataHead.cs
// AetherTree GDD v3.5 — §5.x (Cosmétiques)
//
// Cosmétique purement visuel — n'apporte aucune stat directement.
// Peut avoir un EquipmentConfig pour des effets passifs cosmétiques
// ou des bonus spéciaux (à définir).
//
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0).
// Aucune restriction par WeaponCategory ni ArmorType.
// =============================================================

[CreateAssetMenu(fileName = "NewCosmeticHead", menuName = "AetherTree/Cosmetics/CosmeticDataHead")]
public class CosmeticDataHead : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     cosmeticName = "Cosmetic Head";
    public Sprite     icon;
    public GameObject cosmeticPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau minimum du joueur requis pour équiper ce cosmétique.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Configuration — effets et bonus optionnels ────────────
    [Header("Configuration (optionnel — bonus, effets de statut, résistances, on-hit)")]
    [Tooltip("Effets passifs optionnels portés par ce cosmétique.\n" +
             "Un cosmétique n'apporte pas de stats directement — ce champ\n" +
             "est réservé à des effets spéciaux définis dans le GDD.")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public CosmeticInstanceHead CreateInstance() => new CosmeticInstanceHead(this);
}

// =============================================================
// CosmeticInstanceHead — wrapper runtime d'un cosmétique tête équipé
// GDD v3.5 — §5.x (Cosmétiques)
// =============================================================
[System.Serializable]
public class CosmeticInstanceHead
{
    public CosmeticDataHead data;

    public CosmeticInstanceHead(CosmeticDataHead source) { data = source; }

    // ── Raccourcis SO ─────────────────────────────────────────
    public string CosmeticName  => data != null ? data.cosmeticName  : "Cosmetic Head";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // ── Raccourcis config ─────────────────────────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;
}
