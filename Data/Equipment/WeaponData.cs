using UnityEngine;
using System.Collections.Generic;

// =============================================================
// WeaponData — ScriptableObject template d'arme
// Path : Assets/Scripts/Data/Inventory/Equipment/WeaponData.cs
// AetherTree GDD v3.5 — §5.1
//
// Stats fixes sur le SO (identiques sur toutes les instances) :
//   weaponType, attackSpeed, weaponLevel, requiredLevel
//   critChance             → fixe sur le SO — absent (0) sur les armes Magic
//   critMultiplier         → fixe sur le SO — s'additionne à la base 1.5
//
// Stats rollées au drop / craft (fixées sur l'instance) :
//   damageMin, damageMax   → affectées par rareté + upgrade
//   precision              → affectée par rareté + upgrade
//
// Systèmes applicables (GDD §5.1) :
//   Rareté   r-2 → r+7 — modificateur % sur dmgMin/dmgMax
//   Upgrade  +0  → +10  — bonus cumulatif sur les dégâts
//   Rune     1 slot Rune Weapon — rune.runeLevel ≤ weaponLevel
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

[CreateAssetMenu(fileName = "NewWeapon", menuName = "AetherTree/Equipment/WeaponData")]
public class WeaponData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     weaponName = "Weapon";
    public WeaponType weaponType = WeaponType.ShortSword;
    public Sprite     icon;
    public GameObject weaponPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau de l'arme — détermine le niveau maximum de rune pouvant être insérée.\n" +
             "Règle GDD §5.7 : rune.runeLevel ≤ weaponLevel")]
    [Min(1)] public int weaponLevel = 1;

    [Tooltip("Niveau minimum du joueur requis pour équiper cette arme.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Stats fixes (identiques sur toutes les instances) ─────
    [Header("Stats fixes (identiques sur toutes les instances)")]
    [Tooltip("Cadence d'attaque de base — attaques par seconde.\n" +
             "Gouverne uniquement le Slot 0 (BasicAttack) — jamais bloqué par le GCD.")]
    public float attackSpeed = 1f;

    // ── Stats rollées — fourchettes définies sur le SO ────────
    [Header("Stats rollées au drop / craft")]
    [Tooltip("Borne basse du roll pour les dégâts minimum (avant rareté + upgrade).")]
    public float baseDamageMinLow  = 8f;
    [Tooltip("Borne haute du roll pour les dégâts minimum.")]
    public float baseDamageMinHigh = 12f;

    [Tooltip("Borne basse du roll pour les dégâts maximum (avant rareté + upgrade).")]
    public float baseDamageMaxLow  = 13f;
    [Tooltip("Borne haute du roll pour les dégâts maximum.")]
    public float baseDamageMaxHigh = 18f;

    [Tooltip("Borne basse du roll pour la précision (avant rareté + upgrade).")]
    public float basePrecisionMin = 85f;
    [Tooltip("Borne haute du roll pour la précision.")]
    public float basePrecisionMax = 95f;

    [Range(0f, 1f)]
    public float critChance = 0.1f; // 10% de chance de critique par défaut — GDD §5.1.
    [Tooltip("Forcé à 0 si weaponCategory == Magic — GDD §5.1.")]
    
    [Range(0f, 5f)]
    public float critMultiplier = 0.20f; // +20% de dégâts critiques par défaut — s'additionne à la base 1 du joueur. GDD §5.1.
    [Tooltip("S'additionne à la base 1 du joueur. Ex: 0.20 → mult effectif = 1.20")]
    
    // ── Configuration — effets et bonus ───────────────────────
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    public EquipmentConfig config;
    [Tooltip("Bonus passifs, effets appliqués à l'attaque, résistances aux debuffs\n" +
             "et effets On-Hit reçus — regroupés dans un seul champ.\n" +
             "Lus par CharacterStats.RecalculateStats() et Player.ApplyOnHitEffects().")]
    

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Catégorie déduite du WeaponType (Melee / Ranged / Magic).</summary>
    public WeaponCategory Category => weaponType.GetCategory();

    /// <summary>ArmorType lié à cette catégorie d'arme.</summary>
    public ArmorType LinkedArmorType => weaponType.GetArmorType();

    /// <summary>
    /// Crée une instance avec stats rollées.
    /// rarityRank : -2 à +7 | upgradeLevel : 0 à +10.
    /// </summary>
    public WeaponInstance CreateDropInstance(int rarityRank = 0, int upgradeLevel = 0)
    {
        float dmgMin = Random.Range(baseDamageMinLow,  baseDamageMinHigh);
        float dmgMax = Random.Range(baseDamageMaxLow,  baseDamageMaxHigh);
        float prec   = Random.Range(basePrecisionMin,  basePrecisionMax);

        // Garantit dmgMin ≤ dmgMax
        if (dmgMin > dmgMax) (dmgMin, dmgMax) = (dmgMax, dmgMin);

        // critChance et critMultiplier sont fixes sur le SO — pas de roll. GDD §5.1.
        return new WeaponInstance(this, dmgMin, dmgMax, prec, rarityRank, upgradeLevel);
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
// WeaponInstance — données runtime d'une arme droppée / craftée
// GDD v3.5 — §5.1 / §5.8 / §5.9
// =============================================================
[System.Serializable]
public class WeaponInstance
{
    public WeaponData data;

    // Stats rollées — fixées à la génération
    public float rolledDamageMin;
    public float rolledDamageMax;
    public float rolledPrecision;

    // Modificateurs appliqués après drop
    public int rarityRank   = 0;   // -2 à +7 — GDD §5.8
    public int upgradeLevel = 0;   // 0 à +10 — GDD §5.9

    // Slot rune — 1 par arme, irréversible via Antiquaire
    public RuneInstance equippedRune = null;

    public WeaponInstance(WeaponData source,
                          float dmgMin, float dmgMax, float precision,
                          int rarity = 0, int upgrade = 0)
    {
        data            = source;
        rolledDamageMin = dmgMin;
        rolledDamageMax = dmgMax;
        rolledPrecision = precision;
        rarityRank      = rarity;
        upgradeLevel    = upgrade;
    }

    // ── Modificateurs ─────────────────────────────────────────

    /// <summary>Modificateur de rareté (+10% par rang, négatif si rang < 0). GDD §5.8.</summary>
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
    public float FinalDamageMin => rolledDamageMin * (1f + RarityBonus) * (1f + UpgradeBonus);
    public float FinalDamageMax => rolledDamageMax * (1f + RarityBonus) * (1f + UpgradeBonus);
    public float FinalPrecision => rolledPrecision * (1f + RarityBonus) * (1f + UpgradeBonus);

    // ── Stats fixes — lues directement sur le SO ──────────────
    // critChance et critMultiplier sont définis une fois sur WeaponData. GDD §5.1.
    public float CritChance     => data != null ? data.critChance    : 0f;
    public float CritMultiplier => data != null ? data.critMultiplier : 0f;
    public float AttackSpeed    => data != null ? data.attackSpeed    : 1f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public WeaponType     WeaponType   => data != null ? data.weaponType : global::WeaponType.ShortSword;
    public WeaponCategory Category     => data != null ? data.Category   : WeaponCategory.Melee;
    public string         WeaponName   => data != null ? data.weaponName : "Weapon";
    public Sprite         Icon         => data != null ? data.icon       : null;
    public int            WeaponLevel  => data != null ? data.weaponLevel  : 1;
    public int            RequiredLevel => data != null ? data.requiredLevel : 1;
    public string         RarityLabel  => rarityRank >= 0 ? $"r+{rarityRank}" : $"r{rarityRank}";

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;

    // ── Rune ──────────────────────────────────────────────────

    /// <summary>
    /// Tente d'insérer une rune Weapon.
    /// Règle GDD §5.7 : rune.Category == Weapon ET rune.runeLevel ≤ weaponLevel.
    /// L'ancienne rune est écrasée (détruite) — irréversible sans item spécial.
    /// </summary>
    public bool TryInsertRune(RuneInstance rune)
    {
        if (rune == null)
        {
            Debug.LogWarning("[WeaponInstance] TryInsertRune : rune null.");
            return false;
        }
        if (rune.Category != RuneCategory.Weapon)
        {
            Debug.LogWarning($"[WeaponInstance] {rune.RuneName} est une rune Armor — incompatible avec une arme.");
            return false;
        }
        if (!rune.CanInsertInto(WeaponLevel))
        {
            Debug.LogWarning($"[WeaponInstance] Rune {rune.RarityLabel} (lv{rune.runeLevel}) " +
                             $"trop haute pour cette arme (weaponLevel {WeaponLevel}).");
            return false;
        }
        if (equippedRune != null)
            Debug.Log($"[WeaponInstance] Rune {equippedRune.RuneName} écrasée par {rune.RuneName}.");

        equippedRune = rune;
        Debug.Log($"[WeaponInstance] Rune insérée : {rune.Label} dans {WeaponName}.");
        return true;
    }

    public RuneInstance RemoveRune()
    {
        RuneInstance removed = equippedRune;
        equippedRune = null;
        return removed;
    }
}
