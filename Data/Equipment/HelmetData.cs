using UnityEngine;
using System.Collections.Generic;

// =============================================================
// HelmetData — ScriptableObject template de casque
// Path : Assets/Scripts/Data/Equipment/HelmetData.cs
// AetherTree GDD v3.6 — §5.5 (extension ratio-roll Phase 2, hors GDD
// d'origine — voir a implémenter/note-refonte-roll-ratio-phase2.md)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0/§5.2) — les 3
// défenses roulent quand même dans une fourchette (variance de loot),
// juste sans multiplicateur rareté/upgrade, contrairement à Armor.
// Pas de restriction ArmorType — tout joueur peut équiper.
// Obtenu par drop, craft ou condition in-game.
//
// Pool de stats disponibles dans config.bonuses (GDD §5.5) :
//   MaxHP, MaxMana, RegenHP, RegenMana, Precision, Dodge
//   Résistances élémentaires (plafond 75% par élément — GDD §3.2)
//   ⚠ Pas de CritChance, CritMultiplier ni Points élémentaires sur le casque.
// =============================================================

[CreateAssetMenu(fileName = "hlm_", menuName = "AetherTree/Inventaire/Equipement/HelmetData")]
public class HelmetData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject helmetPrefab;

    // ── Condition ─────────────────────────────────────────────
    [Header("Condition")]
    [Tooltip("Condition de déblocage optionnelle — certains casques ne s'obtiennent\n" +
             "que via une condition in-game (IConditionChecker). Null = toujours disponible.")]
    public ConditionEntry unlockCondition;

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

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Crée une instance avec 3 défenses rollées (ratio, sans rareté/upgrade).</summary>
    public HelmetInstance CreateInstance()
        => new HelmetInstance(this, Random.value, Random.value, Random.value);
}

// =============================================================
// HelmetInstance — wrapper runtime d'un casque équipé
// GDD v3.6 — §5.5
// =============================================================
[System.Serializable]
public class HelmetInstance
{
    public HelmetData data;

    // Ratios rollés — fixés à la génération, position (0..1) dans la fourchette
    // DÉRIVÉE du SO. Pas de rareté/upgrade sur ce slot (GDD §5.2) — seul le Lerp
    // s'applique, mais le rééquilibrage rétroactif du SO fonctionne quand même.
    public float rolledRatioMelee;
    public float rolledRatioRanged;
    public float rolledRatioMagic;

    public HelmetInstance(HelmetData source, float ratioMelee = 0f, float ratioRanged = 0f, float ratioMagic = 0f)
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
    public string ItemId       => data != null ? data.itemID : "unknown_helmet";
    public string HelmetName   => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Helmet";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;
}
