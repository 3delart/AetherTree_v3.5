using UnityEngine;
using System.Collections.Generic;

// =============================================================
// GlovesData — ScriptableObject template de gants
// Path : Assets/Scripts/Data/Inventory/Equipment/GlovesData.cs
// AetherTree GDD v3.5 — §5.4
//
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0).
// Fusion S0 → S6 via PNJ de Fusion — cumulant les résistances
// élémentaires des deux pièces combinées (GDD §5.4).
//
// Stats fixes sur le SO :
//   meleeDefense, rangedDefense, magicDefense — fixes sur le SO
//   elementalResist[7]                        — fixes sur le SO
//   requiredLevel
//
// Fusion (GDD §5.4) :
//   Slot 1 (sacrifiée) + Slot 2 (conservée) → Slot 2 + résists Slot 1
//   fusionLevel = max(slot1, slot2) + 1 — plafonné à S6
//   Résistances plafonnées à 75% par élément — GDD §3.2
//   Irréversible — Slot 1 détruite définitivement
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

[CreateAssetMenu(fileName = "NewGloves", menuName = "AetherTree/Equipment/GlovesData")]
public class GlovesData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     glovesName = "Gloves";
    public Sprite     icon;
    public GameObject glovesPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau minimum du joueur requis pour équiper ces gants.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Défenses fixes ────────────────────────────────────────
    [Header("Défenses fixes (identiques sur toutes les instances)")]
    public float meleeDefense  = 0f;
    public float rangedDefense = 0f;
    public float magicDefense  = 0f;

    // ── Résistances élémentaires de base ─────────────────────
    [Header("Résistances élémentaires de base (ratio 0.01 = 1%)")]
    [Tooltip("Résistances de départ de ces gants.\n" +
             "Additionnées à la fusion — plafonnées à 0.75 (75%) par élément.\n" +
             "GDD §5.4 / §3.2.")]
    public float baseResistFire      = 0f;
    public float baseResistWater     = 0f;
    public float baseResistLightning = 0f;
    public float baseResistEarth     = 0f;
    public float baseResistNature    = 0f;
    public float baseResistDarkness  = 0f;
    public float baseResistLight     = 0f;

    // ── Configuration — effets et bonus ───────────────────────
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    [Tooltip("Bonus passifs fixes, effets appliqués à l'attaque,\n" +
             "résistances aux debuffs et effets On-Hit.\n" +
             "Lus par CharacterStats.RecalculateStats() et Player.ApplyOnHitEffects().")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public GlovesInstance CreateInstance() => new GlovesInstance(this)
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
// GlovesInstance — wrapper runtime d'une paire de gants équipée
// GDD v3.5 — §5.4
// =============================================================
[System.Serializable]
public class GlovesInstance
{
    public GlovesData data;

    // ── Fusion ────────────────────────────────────────────────
    [Tooltip("Palier de fusion : 0 = S0 (base) … 6 = S6 (max). GDD §5.4.")]
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

    public GlovesInstance(GlovesData source) { data = source; }

    // ── Défenses (lues sur le SO) ─────────────────────────────
    public float MeleeDefense  => data != null ? data.meleeDefense  : 0f;
    public float RangedDefense => data != null ? data.rangedDefense : 0f;
    public float MagicDefense  => data != null ? data.magicDefense  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string GlovesName    => data != null ? data.glovesName    : "Gloves";
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
    /// Fusionne deux paires de gants. GDD §5.4.
    /// Slot1 (sacrifiée) est détruite — ses résistances s'additionnent à Slot2.
    /// fusionLevel = max(slot1, slot2) + 1 — plafonné à S6.
    /// Résistances sans plafond — un joueur peut dépasser 100%.
    /// </summary>
    public static GlovesInstance Fuse(GlovesInstance slot1, GlovesInstance slot2)
    {
        if (slot1 == null || slot2 == null)
        {
            Debug.LogWarning("[GlovesInstance] Fuse : une des deux instances est null.");
            return slot1 ?? slot2;
        }

GlovesInstance result = new GlovesInstance(slot2.data)
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

        Debug.Log($"[GlovesInstance] Fusion → {result.FusionLabel} | " +
                  $"Fire {result.resistFire:P0} | Water {result.resistWater:P0} | " +
                  $"Lightning {result.resistLightning:P0}");
        return result;
    }
}
