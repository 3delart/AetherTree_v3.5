using UnityEngine;
using System.Collections.Generic;

// =============================================================
// BootsData — ScriptableObject template de bottes
// Path : Assets/Scripts/Data/Inventory/Equipment/BootsData.cs
// AetherTree GDD v3.5 — §5.4
//
// Même structure que GlovesData — mêmes règles de fusion S0→S6.
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0).
//
// Stats fixes sur le SO :
//   meleeDefense, rangedDefense, magicDefense — fixes sur le SO
//   elementalResist[7]                        — fixes sur le SO
//   requiredLevel
//
// Bottes uniquement (GDD §5.4) :
//   Certaines bottes apportent un bonus de MoveSpeed fixe défini
//   via config.bonuses (StatType.MoveSpeed) — contribue à moveSpeed
//   via CharacterStats.RecalculateStats().
//
// Fusion (GDD §5.4) — identique aux gants :
//   fusionLevel = max(slot1, slot2) + 1 — plafonné à S6
//   Résistances plafonnées à 75% par élément — GDD §3.2
//   Irréversible — Slot 1 détruite définitivement
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

[CreateAssetMenu(fileName = "NewBoots", menuName = "AetherTree/Equipment/BootsData")]
public class BootsData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     bootsName = "Boots";
    public Sprite     icon;
    public GameObject bootsPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau minimum du joueur requis pour équiper ces bottes.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Défenses fixes ────────────────────────────────────────
    [Header("Défenses fixes (identiques sur toutes les instances)")]
    public float meleeDefense  = 0f;
    public float rangedDefense = 0f;
    public float magicDefense  = 0f;

    // ── Résistances élémentaires de base ─────────────────────
    [Header("Résistances élémentaires de base (ratio 0.01 = 1%)")]
    [Tooltip("Résistances de départ de ces bottes.\n" +
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
    [Tooltip("Bonus passifs fixes (dont MoveSpeed flat si applicable),\n" +
             "effets appliqués à l'attaque, résistances aux debuffs et effets On-Hit.\n" +
             "GDD §5.4 — MoveSpeed via StatType.MoveSpeed dans config.bonuses.")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public BootsInstance CreateInstance() => new BootsInstance(this)
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
// GDD v3.5 — §5.4
// =============================================================
[System.Serializable]
public class BootsInstance
{
    public BootsData data;

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

    public BootsInstance(BootsData source) { data = source; }

    // ── Défenses (lues sur le SO) ─────────────────────────────
    public float MeleeDefense  => data != null ? data.meleeDefense  : 0f;
    public float RangedDefense => data != null ? data.rangedDefense : 0f;
    public float MagicDefense  => data != null ? data.magicDefense  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string BootsName     => data != null ? data.bootsName     : "Boots";
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
    /// Fusionne deux paires de bottes. GDD §5.4.
    /// Slot1 (sacrifiée) est détruite — ses résistances s'additionnent à Slot2.
    /// fusionLevel = max(slot1, slot2) + 1 — plafonné à S6.
    /// Résistances sans plafond — un joueur peut dépasser 100%.
    /// </summary>
    public static BootsInstance Fuse(BootsInstance slot1, BootsInstance slot2)
    {
        if (slot1 == null || slot2 == null)
        {
            Debug.LogWarning("[BootsInstance] Fuse : une des deux instances est null.");
            return slot1 ?? slot2;
        }

BootsInstance result = new BootsInstance(slot2.data)
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
