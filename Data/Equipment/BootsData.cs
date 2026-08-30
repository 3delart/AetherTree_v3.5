using UnityEngine;
using System.Collections.Generic;

// =============================================================
// BootsData — ScriptableObject template de bottes
// Path : Assets/Scripts/Data/Equipment/BootsData.cs
// AetherTree GDD v3.6 — §5.6 (extension ratio-roll Phase 2 sur les
// défenses, hors GDD d'origine — voir
// a implémenter/note-refonte-roll-ratio-phase2.md)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// Même structure que GlovesData — mêmes règles de fusion S0→S6.
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0/§5.2).
//
// Résistances élémentaires : restent des valeurs FIXES sur le SO —
// pas de roll ajouté dessus (déjà couvertes par la variance de la
// Fusion — décision Phase 2).
//
// Bottes uniquement (GDD §5.6) :
//   Certaines bottes apportent un bonus de MoveSpeed fixe défini
//   via config.bonuses (StatType.MoveSpeed) — contribue à moveSpeed
//   via CharacterStats.RecalculateStats().
//
// Fusion (GDD §5.6) — identique aux gants :
//   fusionLevel = max(slot1, slot2) + 1 — plafonné à S6
//   Résistances plafonnées à 75% par élément — GDD §3.2
//   Irréversible — Slot 1 détruite définitivement
// =============================================================

[CreateAssetMenu(fileName = "NewBoots", menuName = "AetherTree/Equipment/BootsData")]
public class BootsData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject bootsPrefab;

    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
    [Header("Défense mêlée (rollée — pas de rareté/upgrade sur ce slot)")]
    public float baseMeleeDefense = 0f;
    [Min(0f)] public float meleeDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float meleeDefenseRollGapPercent = 0.06f;
    public float MeleeDefenseLow  => baseMeleeDefense * (1f - meleeDefenseSpreadPercent);
    public float MeleeDefenseHigh => baseMeleeDefense * (1f + meleeDefenseSpreadPercent);

    // ── Défense distance — rollée ─────────────────────────────
    [Header("Défense distance (rollée — pas de rareté/upgrade sur ce slot)")]
    public float baseRangedDefense = 0f;
    [Min(0f)] public float rangedDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float rangedDefenseRollGapPercent = 0.06f;
    public float RangedDefenseLow  => baseRangedDefense * (1f - rangedDefenseSpreadPercent);
    public float RangedDefenseHigh => baseRangedDefense * (1f + rangedDefenseSpreadPercent);

    // ── Défense magique — rollée ───────────────────────────────
    [Header("Défense magique (rollée — pas de rareté/upgrade sur ce slot)")]
    public float baseMagicDefense = 0f;
    [Min(0f)] public float magicDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float magicDefenseRollGapPercent = 0.06f;
    public float MagicDefenseLow  => baseMagicDefense * (1f - magicDefenseSpreadPercent);
    public float MagicDefenseHigh => baseMagicDefense * (1f + magicDefenseSpreadPercent);

    // ── Résistances élémentaires de base (fixes — non rollées) ─
    [Header("Résistances élémentaires de base (ratio 0.01 = 1% — fixes, non rollées)")]
    [Tooltip("Résistances de départ de ces bottes.\n" +
             "Additionnées à la fusion — plafonnées à 0.75 (75%) par élément.\n" +
             "GDD §5.6 / §3.2.")]
    public float baseResistFire      = 0f;
    public float baseResistWater     = 0f;
    public float baseResistLightning = 0f;
    public float baseResistEarth     = 0f;
    public float baseResistNature    = 0f;
    public float baseResistDarkness  = 0f;
    public float baseResistLight     = 0f;

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Crée une instance : 3 défenses rollées (ratio) + résistances fixes recopiées.</summary>
    public BootsInstance CreateInstance() => new BootsInstance(this, Random.value, Random.value, Random.value)
    {
        resistFire      = baseResistFire,
        resistWater     = baseResistWater,
        resistLightning = baseResistLightning,
        resistEarth     = baseResistEarth,
        resistNature    = baseResistNature,
        resistDarkness  = baseResistDarkness,
        resistLight     = baseResistLight,
    };
}

// =============================================================
// BootsInstance — wrapper runtime d'une paire de bottes équipée
// GDD v3.6 — §5.6
// =============================================================
[System.Serializable]
public class BootsInstance
{
    public BootsData data;

    // Ratios rollés — fixés à la génération, position (0..1) dans la fourchette
    // DÉRIVÉE du SO. Pas de rareté/upgrade sur ce slot (GDD §5.2).
    public float rolledRatioMelee;
    public float rolledRatioRanged;
    public float rolledRatioMagic;

    // ── Fusion ────────────────────────────────────────────────
    [Tooltip("Palier de fusion : 0 = S0 (base) … 6 = S6 (max). GDD §5.6.")]
    public int fusionLevel = 0;

    // ── Résistances élémentaires (cumulées à la fusion) ───────
    [Tooltip("Résistances élémentaires accumulées via la fusion — sans plafond.")]
    public float resistFire      = 0f;
    public float resistWater     = 0f;
    public float resistLightning = 0f;
    public float resistEarth     = 0f;
    public float resistNature    = 0f;
    public float resistDarkness  = 0f;
    public float resistLight     = 0f;

    public BootsInstance(BootsData source, float ratioMelee = 0f, float ratioRanged = 0f, float ratioMagic = 0f)
    {
        data              = source;
        rolledRatioMelee  = ratioMelee;
        rolledRatioRanged = ratioRanged;
        rolledRatioMagic  = ratioMagic;
    }

    // ── Défenses (Lerp depuis le SO — signature inchangée) ────
    public float MeleeDefense  => data != null ? Mathf.Lerp(data.MeleeDefenseLow,  data.MeleeDefenseHigh,  rolledRatioMelee)  : 0f;
    public float RangedDefense => data != null ? Mathf.Lerp(data.RangedDefenseLow, data.RangedDefenseHigh, rolledRatioRanged) : 0f;
    public float MagicDefense  => data != null ? Mathf.Lerp(data.MagicDefenseLow,  data.MagicDefenseHigh,  rolledRatioMagic)  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string ItemId       => data != null ? data.itemID : "unknown_boots";
    public string BootsName    => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Boots";
    public Sprite Icon          => data != null ? data.icon          : null;
    public string FusionLabel   => $"S{fusionLevel}";

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;

    // ── Résistance par ElementType ────────────────────────────
    public float GetResistance(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire:      return resistFire;
            case ElementType.Water:     return resistWater;
            case ElementType.Lightning: return resistLightning;
            case ElementType.Earth:     return resistEarth;
            case ElementType.Nature:    return resistNature;
            case ElementType.Darkness:  return resistDarkness;
            case ElementType.Light:     return resistLight;
            default:                    return 0f;
        }
    }

    // ── Fusion ────────────────────────────────────────────────

    /// <summary>
    /// Fusionne deux paires de bottes. GDD §5.6.
    /// Slot1 (sacrifiée) est détruite — ses résistances s'additionnent à Slot2.
    /// fusionLevel = max(slot1, slot2) + 1 — plafonné à S6.
    /// Résistances sans plafond — un joueur peut dépasser 100%.
    /// Les défenses rollées (ratio) de Slot2 sont conservées telles quelles —
    /// la Fusion ne touche que les résistances élémentaires.
    /// </summary>
    public static BootsInstance Fuse(BootsInstance slot1, BootsInstance slot2)
    {
        if (slot1 == null || slot2 == null)
        {
            Debug.LogWarning("[BootsInstance] Fuse : une des deux instances est null.");
            return slot1 ?? slot2;
        }

        BootsInstance result = new BootsInstance(slot2.data, slot2.rolledRatioMelee, slot2.rolledRatioRanged, slot2.rolledRatioMagic)
        {
            fusionLevel     = Mathf.Min(6, Mathf.Max(slot1.fusionLevel, slot2.fusionLevel) + 1),
            resistFire      = slot1.resistFire      + slot2.resistFire,
            resistWater     = slot1.resistWater     + slot2.resistWater,
            resistLightning = slot1.resistLightning + slot2.resistLightning,
            resistEarth     = slot1.resistEarth     + slot2.resistEarth,
            resistNature    = slot1.resistNature    + slot2.resistNature,
            resistDarkness  = slot1.resistDarkness  + slot2.resistDarkness,
            resistLight     = slot1.resistLight     + slot2.resistLight,
        };

        Debug.Log($"[BootsInstance] Fusion → {result.FusionLabel} | " +
                  $"Fire {result.resistFire:P0} | Water {result.resistWater:P0} | " +
                  $"Lightning {result.resistLightning:P0}");
        return result;
    }
}
