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
// Fusion (révisé 2026 — voir docs/superpowers/specs/2026-09-04-fusion-system-design.md) :
//   Slot 1 + Slot 2 → nouvelle 3e instance (identité/def de Slot 2 + résists Slot1+Slot2)
//   fusionLevel = slot1.fusionLevel + slot2.fusionLevel + 1 — refusé si > S6 (FusionSystem.CanFuse)
//   Résistances SANS plafond — un joueur peut dépasser 100% (voir CombatSystem.cs:153)
//   Irréversible — Slot 1 ET Slot 2 détruites, même en cas d'ÉCHEC (voir FusionSystem)
// =============================================================

[CreateAssetMenu(fileName = "bts_", menuName = "AetherTree/Inventaire/Equipement/BootsData")]
public class BootsData : EquipmentDataBase
{
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
             "Additionnées à la fusion — sans plafond par élément.\n" +
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
    public List<OnHitReceivedEffectEntry> OnHitReceivedEffects => data?.config?.onHitReceivedEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitDealtEffectEntry>    OnHitDealtEffects    => data?.config?.onHitDealtEffects;

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
    /// Fusionne deux paires de bottes. GDD §5.6 (révisé 2026 — voir
    /// docs/superpowers/specs/2026-09-04-fusion-system-design.md).
    /// Slot1 ET Slot2 sont détruites par l'appelant (FusionUI) — cette méthode calcule
    /// seulement le résultat, ne touche jamais l'inventaire ni les instances passées en
    /// paramètre (jamais de mutation en place, évite l'aliasing si slot2 est référencé
    /// ailleurs, ex: équipé).
    /// fusionLevel = slot1.fusionLevel + slot2.fusionLevel + 1 — PAS de clamp ici, c'est
    /// FusionSystem.CanFuse() qui refuse l'action en amont si le résultat dépasserait S6.
    /// Résistances sans plafond — un joueur peut dépasser 100% (voir CombatSystem.cs:153
    /// pour le clamp appliqué uniquement au calcul de dégâts, pas au stat lui-même).
    /// Identité + défenses rollées héritées de Slot2, résistances additionnées Slot1+Slot2.
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
            fusionLevel     = slot1.fusionLevel + slot2.fusionLevel + 1,
            resistFire      = slot1.resistFire      + slot2.resistFire,
            resistWater     = slot1.resistWater     + slot2.resistWater,
            resistLightning = slot1.resistLightning + slot2.resistLightning,
            resistEarth     = slot1.resistEarth     + slot2.resistEarth,
            resistNature    = slot1.resistNature    + slot2.resistNature,
            resistDarkness  = slot1.resistDarkness  + slot2.resistDarkness,
            resistLight     = slot1.resistLight     + slot2.resistLight,
        };

        Debug.Log($"[BootsInstance] Fusion → {result.FusionLabel} | " +
                  $"Fire {Mathf.RoundToInt(result.resistFire * 100f)}% | Water {Mathf.RoundToInt(result.resistWater * 100f)}% | " +
                  $"Lightning {Mathf.RoundToInt(result.resistLightning * 100f)}%");
        return result;
    }
}
