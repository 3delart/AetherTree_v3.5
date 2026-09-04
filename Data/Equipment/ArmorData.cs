using UnityEngine;
using System.Collections.Generic;

// =============================================================
// ArmorData — ScriptableObject template d'armure corps
// Path : Assets/Scripts/Data/Equipment/ArmorData.cs
// AetherTree GDD v3.6 — §5.4 / §5.2Bis (ratio-roll)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// L'armure est entièrement libre — tout joueur peut équiper
// n'importe quel type sans restriction liée à son arme.
// Le type d'armure oriente naturellement le build via ses
// bonus passifs fixes (GDD §5.4).
//
// Stats fixes sur le SO (identiques sur toutes les instances) :
//   armorType, weaponLevel
//
// Stats rollées au drop / craft (ratio 0..1 stocké sur l'instance — voir
// a implémenter/note-systeme-ratio-degats.md) — chaque défense a son propre
// triplet base/spreadPercent/rollGapPercent indépendant (pas de contrainte
// d'ordre entre les 3 défenses contrairement à dmgMin/dmgMax de l'arme, donc
// une seule fourchette par défense suffit, comme basePrecision de WeaponData) :
//   meleeDefense, rangedDefense, magicDefense → affectées par rareté + upgrade
//   dodge                                     → PAS affectée par rareté/upgrade
//                                                (ratio quand même, pour cohérence
//                                                de stockage — voir FinalDodge)
//
// Bonus passifs par type d'armure (GDD §5.4) :
//   Lourde  → +Défenses + Esquive
//   Légère  → +BonusAttack + Précision
//   Robe    → +Points élémentaires + Réduction cooldown
// Ces bonus fixes sont définis via config.bonuses sur le SO.
//
// Systèmes applicables (GDD §5.4) :
//   Rareté   r-2 → r+7 — modificateur % sur les 3 défenses simultanément
//   Upgrade  +0  → +10  — bonus cumulatif sur les défenses
//   Rune     1 slot Rune Armor — rune.runeLevel ≤ weaponLevel armure
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

[CreateAssetMenu(fileName = "arm_", menuName = "AetherTree/Inventaire/Equipement/ArmorData")]
public class ArmorData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Type d'armure — oriente le build via les bonus passifs fixes.\n" +
             "Lourde = Tank (Défenses + Esquive)\n" +
             "Légère = DPS (BonusAttack + Précision)\n" +
             "Robe   = Mage (Points élémentaires + Réduction cooldown)\n" +
             "GDD §5.4 — aucune restriction par WeaponCategory du joueur.")]
    public ArmorType  armorType = ArmorType.Melee;

    public GameObject armorPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau de l'armure — détermine le niveau maximum de rune pouvant être insérée.\n" +
             "Règle GDD §5.7 : rune.runeLevel ≤ weaponLevel de l'armure.")]
    [Min(1)] public int weaponLevel = 1;

    // ── Défense mêlée — rollée, affectée par rareté + upgrade ──
    [Header("Défense mêlée (rollée, affectée par rareté + upgrade)")]
    public float baseMeleeDefense = 12.5f;
    [Min(0f)] public float meleeDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float meleeDefenseRollGapPercent = 0.06f;
    public float MeleeDefenseLow  => baseMeleeDefense * (1f - meleeDefenseSpreadPercent);
    public float MeleeDefenseHigh => baseMeleeDefense * (1f + meleeDefenseSpreadPercent);

    // ── Défense distance — rollée, affectée par rareté + upgrade ──
    [Header("Défense distance (rollée, affectée par rareté + upgrade)")]
    public float baseRangedDefense = 12.5f;
    [Min(0f)] public float rangedDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float rangedDefenseRollGapPercent = 0.06f;
    public float RangedDefenseLow  => baseRangedDefense * (1f - rangedDefenseSpreadPercent);
    public float RangedDefenseHigh => baseRangedDefense * (1f + rangedDefenseSpreadPercent);

    // ── Défense magique — rollée, affectée par rareté + upgrade ──
    [Header("Défense magique (rollée, affectée par rareté + upgrade)")]
    public float baseMagicDefense = 10f;
    [Min(0f)] public float magicDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float magicDefenseRollGapPercent = 0.06f;
    public float MagicDefenseLow  => baseMagicDefense * (1f - magicDefenseSpreadPercent);
    public float MagicDefenseHigh => baseMagicDefense * (1f + magicDefenseSpreadPercent);

    // ── Esquive — rollée, NON affectée par rareté/upgrade ─────
    [Header("Esquive (rollée, NON affectée par rareté / upgrade — GDD §5.4)")]
    public float baseDodge = 7.5f;
    [Min(0f)] public float dodgeSpreadPercent  = 0.10f;
    [Min(0f)] public float dodgeRollGapPercent = 0.06f;
    public float DodgeLow  => baseDodge * (1f - dodgeSpreadPercent);
    public float DodgeHigh => baseDodge * (1f + dodgeSpreadPercent);

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Crée une instance avec stats rollées (4 ratios indépendants).</summary>
    public ArmorInstance CreateDropInstance(int rarityRank = 0, int upgradeLevel = 0)
    {
        float ratioMelee  = Random.value;
        float ratioRanged = Random.value;
        float ratioMagic  = Random.value;
        float ratioDodge  = Random.value;
        return new ArmorInstance(this, ratioMelee, ratioRanged, ratioMagic, ratioDodge, rarityRank, upgradeLevel);
    }

    /// <summary>
    /// Roll la rareté au drop selon la table GDD §5.13.
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
// GDD v3.6 — §5.4 / §5.13 / §5.14
// =============================================================
[System.Serializable]
public class ArmorInstance
{
    public ArmorData data;

    // Ratios rollés — fixés à la génération, position (0..1) dans la fourchette
    // DÉRIVÉE du SO (voir ArmorData.MeleeDefenseLow/High etc.). Indépendants les uns
    // des autres — un item peut rouler bas en mêlée et haut en magique.
    public float rolledRatioMelee;
    public float rolledRatioRanged;
    public float rolledRatioMagic;
    public float rolledRatioDodge;

    [Header("Modificateurs appliqués après drop")]
    public int rarityRank   = 0;   // -2 à +7 — GDD §5.13
    public int upgradeLevel = 0;   // 0 à +10 — GDD §5.14

    // Sceau Aethernelle — GDD §5.13Bis. Posé sur une pièce déjà r+7 via une Pierre
    // dédiée (condition, pas de RNG) — AUCUN bonus de stats, purement prestige/cosmétique.
    // Indépendant du slot rune ci-dessous — n'affecte ni ne remplace equippedRune.
    // Mécanique d'obtention (Pierre, condition, UI) pas encore implémentée — ce champ
    // existe pour que l'affichage soit prêt et que la save round-trip dès maintenant.
    [Header("Sceau spécial (cosmétique)")]
    public bool hasAethernelleSeal = false;

    // Slot rune — 1 par armure, irréversible via Antiquaire
    [Header("Rune")]
    public RuneInstance equippedRune = null;

    public ArmorInstance(ArmorData source,
                         float ratioMelee, float ratioRanged, float ratioMagic,
                         float ratioDodge, int rarity = 0, int upgrade = 0)
    {
        data              = source;
        rolledRatioMelee  = ratioMelee;
        rolledRatioRanged = ratioRanged;
        rolledRatioMagic  = ratioMagic;
        rolledRatioDodge  = ratioDodge;
        rarityRank        = rarity;
        upgradeLevel      = upgrade;
    }

    // ── Modificateurs ─────────────────────────────────────────

    /// <summary>Modificateur de rareté. GDD §5.13.</summary>
    private float RarityBonus => rarityRank * 0.10f;

    /// <summary>Modificateur d'upgrade — croissance triangulaire. GDD §5.14.</summary>
    private float UpgradeBonus
    {
        get
        {
            int n = Mathf.Clamp(upgradeLevel, 0, 10);
            return (n * (n + 1) / 2f) * 0.01f;
        }
    }

    // ── Stats finales — affectées par rareté + upgrade ────────
    // Chaque Lerp lit les bornes DÉRIVÉES ACTUELLES du ArmorData : un rééquilibrage
    // des valeurs de référence se répercute automatiquement sur toutes les instances
    // déjà droppées, sans script de migration (GDD §5.2Bis).
    public float FinalMeleeDefense =>
        (data != null ? Mathf.Lerp(data.MeleeDefenseLow, data.MeleeDefenseHigh, rolledRatioMelee) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    public float FinalRangedDefense =>
        (data != null ? Mathf.Lerp(data.RangedDefenseLow, data.RangedDefenseHigh, rolledRatioRanged) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    public float FinalMagicDefense =>
        (data != null ? Mathf.Lerp(data.MagicDefenseLow, data.MagicDefenseHigh, rolledRatioMagic) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    // ── Stats finales — NON affectées par rareté / upgrade ────
    // Stockée en ratio comme les 3 défenses (cohérence de stockage — un rééquilibrage
    // de baseDodge se répercute aussi automatiquement), mais sans multiplicateur
    // rareté/upgrade appliqué — GDD §5.4 exempte explicitement l'esquive.
    public float FinalDodge =>
        data != null ? Mathf.Lerp(data.DodgeLow, data.DodgeHigh, rolledRatioDodge) : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public ArmorType ArmorType    => data != null ? data.armorType    : global::ArmorType.Melee;

    /// <summary>Clé technique STABLE — logs, saves, comparaisons. Jamais affichée au joueur.</summary>
    public string    ItemId       => data != null ? data.itemID : "unknown_armor";

    /// <summary>Nom affiché au joueur, dans la langue courante. Ne jamais utiliser dans un log.</summary>
    public string    ArmorName    => data != null
        ? data.displayName.Get(LocalizationManager.CurrentLanguage)
        : "Armor";

    public Sprite    Icon         => data != null ? data.icon         : null;
    public int       WeaponLevel  => data != null ? data.weaponLevel  : 1;
    public string    RarityLabel  => rarityRank >= 0 ? $"r+{rarityRank}" : $"r{rarityRank}";

    /// <summary>Nom de rareté affiché au joueur — "Aethernelle" si scellé, sinon le nom du rang. GDD §5.13/§5.13Bis.</summary>
    public string    RarityDisplayName => hasAethernelleSeal ? RarityTier.AethernelleName : RarityTier.GetName(rarityRank);

    /// <summary>Couleur affichée au joueur — GDD §5.13/§5.13Bis.</summary>
    public string    RarityDisplayColorHex => hasAethernelleSeal ? RarityTier.AethernelleColorHex : RarityTier.GetColorHex(rarityRank);

    /// <summary>Nom d'affichage complet — format canonique utilisé partout où le nom
    /// d'une armure est montré au joueur : "{RaritéNom} {Nom}" coloré, suivi de
    /// "(+N)" seulement si upgradeLevel > 0 (rien si +0).</summary>
    public string    DisplayNameRich =>
        $"<color={RarityDisplayColorHex}>{RarityDisplayName} {ArmorName}</color>"
        + (upgradeLevel > 0 ? $" (+{upgradeLevel})" : "");

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
        Debug.Log($"[ArmorInstance] Rune insérée : {rune.Label} dans {ItemId}.");
        return true;
    }

    public RuneInstance RemoveRune()
    {
        RuneInstance removed = equippedRune;
        equippedRune = null;
        return removed;
    }
}
