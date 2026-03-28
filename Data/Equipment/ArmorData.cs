using UnityEngine;
using System.Collections.Generic;

// =============================================================
// ArmorData — ScriptableObject template d'armure corps
// Path : Assets/Scripts/Data/Inventory/Equipment/ArmorData.cs
// AetherTree GDD v3.5 — §5.2
//
// L'armure est entièrement libre — tout joueur peut équiper
// n'importe quel type sans restriction liée à son arme.
// Le type d'armure oriente naturellement le build via ses
// bonus passifs fixes (GDD §5.2).
//
// Stats fixes sur le SO (identiques sur toutes les instances) :
//   armorType, weaponLevel, requiredLevel
//
// Stats rollées au drop / craft :
//   meleeDefense, rangedDefense, magicDefense → affectées par rareté + upgrade
//
// Bonus passifs par type d'armure (GDD §5.2) :
//   Lourde  → +Défenses + Esquive
//   Légère  → +BonusAttack + Précision
//   Robe    → +Points élémentaires + Réduction cooldown
// Ces bonus fixes sont définis via config.bonuses sur le SO.
//
// Systèmes applicables (GDD §5.2) :
//   Rareté   r-2 → r+7 — modificateur % sur les 3 défenses simultanément
//   Upgrade  +0  → +10  — bonus cumulatif sur les défenses
//   Rune     1 slot Rune Armor — rune.runeLevel ≤ weaponLevel armure
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

// ── Résistance à un debuff — partagée par tous les équipements ──
[System.Serializable]
public class DebuffResistanceEntry
{
    [Tooltip("Type de debuff résisté.")]
    public DebuffType debuffType;

    [Tooltip("Chance de résister [0..1].\nEx: 0.05 = 5% | 0.30 = 30%")]
    [Range(0f, 1f)]
    public float resistChance = 0.05f;
}

[CreateAssetMenu(fileName = "NewArmor", menuName = "AetherTree/Equipment/ArmorData")]
public class ArmorData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     armorName = "Armor";

    [Tooltip("Type d'armure — oriente le build via les bonus passifs fixes.\n" +
             "Lourde = Tank (Défenses + Esquive)\n" +
             "Légère = DPS (BonusAttack + Précision)\n" +
             "Robe   = Mage (Points élémentaires + Réduction cooldown)\n" +
             "GDD §5.2 — aucune restriction par WeaponCategory du joueur.")]
    public ArmorType  armorType = ArmorType.Melee;

    public Sprite     icon;
    public GameObject armorPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau de l'armure — détermine le niveau maximum de rune pouvant être insérée.\n" +
             "Règle GDD §5.7 : rune.runeLevel ≤ weaponLevel de l'armure.")]
    [Min(1)] public int weaponLevel = 1;

    [Tooltip("Niveau minimum du joueur requis pour équiper cette armure.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Stats rollées — fourchettes définies sur le SO ────────
    [Header("Défenses rollées au drop / craft (affectées par rareté + upgrade)")]
    [Tooltip("Borne basse du roll pour la défense mêlée.")]
    public float baseMeleeDefenseMin  = 10f;
    [Tooltip("Borne haute du roll pour la défense mêlée.")]
    public float baseMeleeDefenseMax  = 15f;

    [Tooltip("Borne basse du roll pour la défense distance.")]
    public float baseRangedDefenseMin = 10f;
    [Tooltip("Borne haute du roll pour la défense distance.")]
    public float baseRangedDefenseMax = 15f;

    [Tooltip("Borne basse du roll pour la défense magique.")]
    public float baseMagicDefenseMin  = 8f;
    [Tooltip("Borne haute du roll pour la défense magique.")]
    public float baseMagicDefenseMax  = 12f;

    // ── Esquive — rollée, NON affectée par rareté/upgrade ─────
    [Header("Esquive rollée (non affectée par rareté / upgrade)")]
    [Tooltip("Borne basse du roll pour l'esquive.\nNon modifiée par rareté ni upgrade.")]
    public float baseDodgeMin = 5f;
    [Tooltip("Borne haute du roll pour l'esquive.")]
    public float baseDodgeMax = 10f;

    // ── Configuration — effets et bonus ───────────────────────
    [Header("Configuration (bonus passifs, effets de statut, résistances, on-hit)")]
    [Tooltip("Bonus passifs fixes (défenses, esquive, élémentaires selon type d'armure),\n" +
             "effets appliqués à l'attaque, résistances aux debuffs et effets On-Hit.\n" +
             "GDD §5.2 — les bonus passifs par type d'armure sont définis ici.")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Crée une instance avec stats rollées.</summary>
    public ArmorInstance CreateDropInstance(int rarityRank = 0, int upgradeLevel = 0)
    {
        float melee  = Random.Range(baseMeleeDefenseMin,  baseMeleeDefenseMax);
        float ranged = Random.Range(baseRangedDefenseMin, baseRangedDefenseMax);
        float magic  = Random.Range(baseMagicDefenseMin,  baseMagicDefenseMax);
        float dodge  = Random.Range(baseDodgeMin,         baseDodgeMax);
        return new ArmorInstance(this, melee, ranged, magic, dodge, rarityRank, upgradeLevel);
    }

    /// <summary>
    /// Roll la rareté au drop selon la table GDD §5.8.
    /// r-2(8%) r-1(12%) r0(20.85%) r+1(18%) r+2(15.7%) r+3(11.5%)
    /// r+4(8.5%) r+5(4.1%) r+6(1%) r+7(0.35%)
    /// </summary>
    public static int RollRarity()
    {
        float roll = Random.value * 100f;
        if (roll < 8f)     return -2;
        if (roll < 20f)    return -1;
        if (roll < 40.85f) return  0;
        if (roll < 58.85f) return  1;
        if (roll < 74.55f) return  2;
        if (roll < 86.05f) return  3;
        if (roll < 94.55f) return  4;
        if (roll < 98.65f) return  5;
        if (roll < 99.65f) return  6;
        return 7;
    }
}

// =============================================================
// ArmorInstance — données runtime d'une armure droppée / craftée
// GDD v3.5 — §5.2 / §5.8 / §5.9
// =============================================================
[System.Serializable]
public class ArmorInstance
{
    public ArmorData data;

    // Stats rollées — fixées à la génération
    public float rolledMeleeDefense;
    public float rolledRangedDefense;
    public float rolledMagicDefense;
    public float rolledDodge;

    // Modificateurs appliqués après drop
    public int rarityRank   = 0;   // -2 à +7 — GDD §5.8
    public int upgradeLevel = 0;   // 0 à +10 — GDD §5.9

    // Slot rune — 1 par armure, irréversible via Antiquaire
    public RuneInstance equippedRune = null;

    public ArmorInstance(ArmorData source,
                         float meleeDefense, float rangedDefense, float magicDefense,
                         float dodge, int rarity = 0, int upgrade = 0)
    {
        data                 = source;
        rolledMeleeDefense   = meleeDefense;
        rolledRangedDefense  = rangedDefense;
        rolledMagicDefense   = magicDefense;
        rolledDodge          = dodge;
        rarityRank           = rarity;
        upgradeLevel         = upgrade;
    }

    // ── Modificateurs ─────────────────────────────────────────

    /// <summary>Modificateur de rareté. GDD §5.8.</summary>
    private float RarityBonus => rarityRank * 0.10f;

    /// <summary>Modificateur d'upgrade — croissance triangulaire. GDD §5.9.</summary>
    private float UpgradeBonus
    {
        get
        {
            int n = Mathf.Clamp(upgradeLevel, 0, 10);
            return (n * (n + 1) / 2f) * 0.01f;
        }
    }

    // ── Stats finales — affectées par rareté + upgrade ────────
    public float FinalMeleeDefense  => rolledMeleeDefense  * (1f + RarityBonus) * (1f + UpgradeBonus);
    public float FinalRangedDefense => rolledRangedDefense * (1f + RarityBonus) * (1f + UpgradeBonus);
    public float FinalMagicDefense  => rolledMagicDefense  * (1f + RarityBonus) * (1f + UpgradeBonus);

    // ── Stats finales — NON affectées par rareté / upgrade ────
    public float FinalDodge => rolledDodge;

    // ── Raccourcis SO ─────────────────────────────────────────
    public ArmorType ArmorType    => data != null ? data.armorType    : global::ArmorType.Melee;
    public string    ArmorName    => data != null ? data.armorName    : "Armor";
    public Sprite    Icon         => data != null ? data.icon         : null;
    public int       WeaponLevel  => data != null ? data.weaponLevel  : 1;
    public string    RarityLabel  => rarityRank >= 0 ? $"r+{rarityRank}" : $"r{rarityRank}";

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;

    // ── Rune ──────────────────────────────────────────────────

    /// <summary>
    /// Tente d'insérer une rune Armor.
    /// Règle GDD §5.7 : rune.Category == Armor ET rune.runeLevel ≤ weaponLevel de l'armure.
    /// L'ancienne rune est écrasée (détruite) — irréversible sans item spécial.
    /// </summary>
    public bool TryInsertRune(RuneInstance rune)
    {
        if (rune == null)
        {
            Debug.LogWarning("[ArmorInstance] TryInsertRune : rune null.");
            return false;
        }
        if (rune.Category != RuneCategory.Armor)
        {
            Debug.LogWarning($"[ArmorInstance] {rune.RuneName} est une rune Weapon — incompatible avec une armure.");
            return false;
        }
        if (!rune.CanInsertInto(WeaponLevel))
        {
            Debug.LogWarning($"[ArmorInstance] Rune {rune.RarityLabel} (lv{rune.runeLevel}) " +
                             $"trop haute pour cette armure (weaponLevel {WeaponLevel}).");
            return false;
        }
        if (equippedRune != null)
            Debug.Log($"[ArmorInstance] Rune {equippedRune.RuneName} écrasée par {rune.RuneName}.");

        equippedRune = rune;
        Debug.Log($"[ArmorInstance] Rune insérée : {rune.Label} dans {ArmorName}.");
        return true;
    }

    public RuneInstance RemoveRune()
    {
        RuneInstance removed = equippedRune;
        equippedRune = null;
        return removed;
    }
}
