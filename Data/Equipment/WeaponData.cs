using UnityEngine;
using System.Collections.Generic;

// =============================================================
// WeaponData — ScriptableObject template d'arme
// Path : Assets/Scripts/Data/Equipment/WeaponData.cs
// AetherTree GDD v3.6 — §5.3 / §5.2Bis (ratio-roll)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// Stats fixes sur le SO (identiques sur toutes les instances) :
//   weaponType, attackSpeed, weaponLevel
//   critChance             → fixe sur le SO — absent (0) sur les armes Magic
//   critMultiplier         → fixe sur le SO — s'additionne à la base 1.5
//
// Stats rollées au drop / craft (ratio 0..1 stocké sur l'instance — voir
// a implémenter/note-systeme-ratio-degats.md pour le détail du raisonnement) :
//   damageMin, damageMax   → affectées par rareté + upgrade
//   precision              → affectée par rareté + upgrade
//
// Systèmes applicables (GDD §5.3) :
//   Rareté   r-2 → r+7 — modificateur % sur dmgMin/dmgMax
//   Upgrade  +0  → +10  — bonus cumulatif sur les dégâts
//   Rune     1 slot Rune Weapon — rune.runeLevel ≤ weaponLevel
// =============================================================

[CreateAssetMenu(fileName = "wpn_", menuName = "AetherTree/Inventaire/Equipement/WeaponData")]
public class WeaponData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public WeaponType weaponType = WeaponType.ShortSword;
    public GameObject weaponPrefab;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau de l'arme — détermine le niveau maximum de rune pouvant être insérée.\n" +
             "Règle GDD §5.7 : rune.runeLevel ≤ weaponLevel")]
    [Min(1)] public int weaponLevel = 1;

    // ── Stats fixes (identiques sur toutes les instances) ─────
    [Header("Stats fixes (identiques sur toutes les instances)")]
    [Tooltip("Cadence d'attaque de base — attaques par seconde.\n" +
             "Gouverne uniquement le Slot 0 (BasicAttack) — jamais bloqué par le GCD.")]
    public float attackSpeed = 1f;

    // ── Stats rollées — fourchettes dérivées d'une valeur de référence ────────
    // Voir note-systeme-ratio-degats.md pour le détail du raisonnement et des
    // itérations écartées.
    [Header("Stats rollées au drop / craft")]
    [Tooltip("Dégât de référence central de l'arme (avant rareté + upgrade).\n" +
             "MinLow/MinHigh et MaxLow/MaxHigh (fourchettes réelles des rolls dmgMin/dmgMax)\n" +
             "sont dérivées automatiquement de cette seule valeur via spreadBasePercent et\n" +
             "rollGapPercent — voir plus bas.")]
    public float baseDamage = 10f;

    [Tooltip("Écart entre le profil \"faible\" et le profil \"fort\" de l'arme, en % de baseDamage.\n" +
             "MinLow = baseDamage × (1 − spreadBasePercent)\n" +
             "MaxLow = baseDamage × (1 + spreadBasePercent)\n" +
             "Plus cette valeur est grande, plus l'arme peut taper bas ET fort (arme \"swingy\").\n" +
             "Ajuste-la à la baisse pour les armes à très gros dégâts si l'écart en valeur\n" +
             "absolue devient trop grand (ex: 3% sur une base à 12000+ plutôt que 10%).")]
    [Min(0f)] public float spreadBasePercent = 0.10f; // 10% par défaut

    [Tooltip("Écart fin appliqué DANS chaque fourchette (MinLow→MinHigh, MaxLow→MaxHigh),\n" +
             "en % de la borne basse de cette fourchette.\n" +
             "MinHigh = MinLow × (1 + rollGapPercent)   MaxHigh = MaxLow × (1 + rollGapPercent)\n" +
             "Contrôle la variance du roll À L'INTÉRIEUR d'une même arme (qualité du loot).\n" +
             "Ajuste-la à la baisse pour les armes à très gros dégâts, comme spreadBasePercent.")]
    [Min(0f)] public float rollGapPercent = 0.06f; // 6% par défaut

    [Tooltip("Précision de référence centrale de l'arme (avant rareté + upgrade).\n" +
             "PrecisionLow/PrecisionHigh (fourchette réelle du roll) sont dérivées\n" +
             "automatiquement de cette seule valeur via precisionSpreadPercent — voir plus bas.\n" +
             "Contrairement à baseDamage, une seule fourchette suffit ici (pas de couple\n" +
             "min/max séparé à isoler l'un de l'autre), donc un seul % de spread est nécessaire.")]
    public float basePrecision = 90f;

    [Tooltip("Écart entre le profil \"faible\" et le profil \"fort\" de précision, en % de\n" +
             "basePrecision.\n" +
             "PrecisionLow  = basePrecision × (1 − precisionSpreadPercent)\n" +
             "PrecisionHigh = basePrecision × (1 + precisionSpreadPercent)")]
    [Min(0f)] public float precisionSpreadPercent = 0.10f; // 10% par défaut

    /// <summary>Fourchette réelle du roll dmgMin, dérivée de baseDamage.</summary>
    public float MinLow  => baseDamage * (1f - spreadBasePercent);
    public float MinHigh => MinLow * (1f + rollGapPercent);

    /// <summary>Fourchette réelle du roll dmgMax, dérivée de baseDamage.</summary>
    public float MaxLow  => baseDamage * (1f + spreadBasePercent);
    public float MaxHigh => MaxLow * (1f + rollGapPercent);

    /// <summary>Fourchette réelle du roll de précision, dérivée de basePrecision.</summary>
    public float PrecisionLow  => basePrecision * (1f - precisionSpreadPercent);
    public float PrecisionHigh => basePrecision * (1f + precisionSpreadPercent);

    [Range(0f, 1f)]
    public float critChance = 0.1f; // 10% de chance de critique par défaut — GDD §5.3.
    [Tooltip("Forcé à 0 si weaponCategory == Magic — GDD §5.3.")]

    [Range(0f, 5f)]
    public float critMultiplier = 0.20f; // +20% de dégâts critiques par défaut — s'additionne à la base 1 du joueur. GDD §5.3.
    [Tooltip("S'additionne à la base 1 du joueur. Ex: 0.20 → mult effectif = 1.20")]

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        // Avertissement de configuration plutôt qu'un clamp silencieux à l'exécution :
        // si les fourchettes dérivées se chevauchent, un roll dmgMin peut dépasser un
        // roll dmgMax. On préfère prévenir le designer que corriger en cachette.
        // Avec ce modèle (MinLow/MaxLow dérivés du même baseDamage), le chevauchement
        // n'arrive que si spreadBasePercent est trop faible par rapport à rollGapPercent.
        if (MinHigh > MaxLow)
        {
            Debug.LogWarning($"[WeaponData:{itemID}] MinHigh ({MinHigh:F1}) > MaxLow ({MaxLow:F1}) — " +
                              $"les fourchettes de dmgMin/dmgMax se chevauchent, dmgMin pourrait dépasser " +
                              $"dmgMax sur certains rolls. Augmente spreadBasePercent ou réduis " +
                              $"rollGapPercent.", this);
        }
    }
#endif

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
        // Deux ratios indépendants (0..1) : dmgMin roule dans [MinLow, MinHigh], dmgMax
        // roule dans [MaxLow, MaxHigh] — ces 4 bornes sont dérivées de baseDamage via
        // spreadBasePercent et rollGapPercent (voir plus haut). Comme MinLow/MinHigh et
        // MaxLow/MaxHigh ne se chevauchent pas tant que spreadBasePercent reste raisonnable
        // par rapport à rollGapPercent (averti par OnValidate sinon), dmgMin ≤ dmgMax est
        // garanti par construction, sans clamp ni collision de rolls.
        float ratioMin  = Random.value;
        float ratioMax  = Random.value;
        float ratioPrec = Random.value;

        // critChance et critMultiplier sont fixes sur le SO — pas de roll. GDD §5.3.
        return new WeaponInstance(this, ratioMin, ratioMax, ratioPrec, rarityRank, upgradeLevel);
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
// WeaponInstance — données runtime d'une arme droppée / craftée
// GDD v3.6 — §5.3 / §5.13 / §5.14
// =============================================================
[System.Serializable]
public class WeaponInstance
{
    public WeaponData data;

    // Ratios rollés — fixés à la génération, position (0..1) dans la fourchette DÉRIVÉE
    // du SO (MinLow/MinHigh pour rolledRatioMin, MaxLow/MaxHigh pour rolledRatioMax — ces
    // fourchettes sont elles-mêmes calculées depuis baseDamage + spreadBasePercent +
    // rollGapPercent, voir WeaponData). Indépendants l'un de l'autre : un item peut rouler
    // bas sur dmgMin et haut sur dmgMax (variance de loot), sans risque de collision
    // puisque les deux fourchettes dérivées ne se chevauchent pas par construction.
    // En stockant la position relative plutôt qu'un résultat absolu, un rééquilibrage des
    // valeurs de référence sur le WeaponData (nerf/buff) se répercute automatiquement sur
    // toutes les instances déjà droppées, sans script de migration.
    public float rolledRatioMin;
    public float rolledRatioMax;
    public float rolledRatioPrecision;

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

    // Slot rune — 1 par arme, irréversible via Antiquaire
    [Header("Rune")]
    public RuneInstance equippedRune = null;

    public WeaponInstance(WeaponData source,
                          float ratioMin, float ratioMax, float ratioPrecision,
                          int rarity = 0, int upgrade = 0)
    {
        data                 = source;
        rolledRatioMin       = ratioMin;
        rolledRatioMax       = ratioMax;
        rolledRatioPrecision = ratioPrecision;
        rarityRank           = rarity;
        upgradeLevel         = upgrade;
    }

    // ── Modificateurs ─────────────────────────────────────────

    /// <summary>Modificateur de rareté (+10% par rang, négatif si rang < 0). GDD §5.13.</summary>
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
    // Chaque Lerp lit les bornes DÉRIVÉES ACTUELLES du WeaponData (MinLow/MinHigh,
    // MaxLow/MaxHigh — recalculées depuis baseDamage + spreadBasePercent + rollGapPercent
    // à chaque accès) : si ces valeurs de référence changent pour un rééquilibrage, chaque
    // instance existante se recalcule automatiquement en conservant sa position relative.
    public float FinalDamageMin =>
        (data != null ? Mathf.Lerp(data.MinLow, data.MinHigh, rolledRatioMin) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    public float FinalDamageMax =>
        (data != null ? Mathf.Lerp(data.MaxLow, data.MaxHigh, rolledRatioMax) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    public float FinalPrecision =>
        (data != null ? Mathf.Lerp(data.PrecisionLow, data.PrecisionHigh, rolledRatioPrecision) : 0f)
        * (1f + RarityBonus) * (1f + UpgradeBonus);

    // ── Stats fixes — lues directement sur le SO ──────────────
    // critChance et critMultiplier sont définis une fois sur WeaponData. GDD §5.3.
    public float CritChance     => data != null ? data.critChance    : 0f;
    public float CritMultiplier => data != null ? data.critMultiplier : 0f;
    public float AttackSpeed    => data != null ? data.attackSpeed    : 1f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public WeaponType     WeaponType    => data != null ? data.weaponType : global::WeaponType.ShortSword;
    public WeaponCategory Category      => data != null ? data.Category   : WeaponCategory.Melee;

    /// <summary>Clé technique STABLE — logs, saves, comparaisons. Jamais affichée au joueur.</summary>
    public string          ItemId       => data != null ? data.itemID : "unknown_weapon";

    /// <summary>Nom affiché au joueur, dans la langue courante. Ne jamais utiliser dans un log.</summary>
    public string          WeaponName   => data != null
        ? data.displayName.Get(LocalizationManager.CurrentLanguage)
        : "Weapon";

    public Sprite          Icon         => data != null ? data.icon       : null;
    public int             WeaponLevel  => data != null ? data.weaponLevel  : 1;
    public int             RequiredLevel => data != null ? data.requiredLevel : 1;
    public string          RarityLabel  => rarityRank >= 0 ? $"r+{rarityRank}" : $"r{rarityRank}";

    /// <summary>Nom de rareté affiché au joueur — "Aethernelle" si scellé, sinon le nom du rang. GDD §5.13/§5.13Bis.</summary>
    public string          RarityDisplayName => hasAethernelleSeal ? RarityTier.AethernelleName : RarityTier.GetName(rarityRank);

    /// <summary>Couleur affichée au joueur — GDD §5.13/§5.13Bis.</summary>
    public string          RarityDisplayColorHex => hasAethernelleSeal ? RarityTier.AethernelleColorHex : RarityTier.GetColorHex(rarityRank);

    /// <summary>Nom d'affichage complet — format canonique utilisé partout où le nom
    /// d'une arme est montré au joueur : "{RaritéNom} {Nom}" coloré, suivi de
    /// "(+N)" seulement si upgradeLevel > 0 (rien si +0).</summary>
    public string          DisplayNameRich =>
        $"<color={RarityDisplayColorHex}>{RarityDisplayName} {WeaponName}</color>"
        + (upgradeLevel > 0 ? $" (+{upgradeLevel})" : "");

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
        // ItemId (clé stable) dans le log — jamais WeaponName (texte localisé, change avec la langue).
        Debug.Log($"[WeaponInstance] Rune insérée : {rune.Label} dans {ItemId}.");
        return true;
    }

    public RuneInstance RemoveRune()
    {
        RuneInstance removed = equippedRune;
        equippedRune = null;
        return removed;
    }
}
