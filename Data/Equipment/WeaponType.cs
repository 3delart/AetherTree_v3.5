using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// WEAPONTYPE.CS — Data-driven, zéro switch à maintenir
// Path : Assets/Scripts/Data/Inventory/Equipment/WeaponType.cs
// AetherTree GDD v3.5 — §5.1 (WeaponType / WeaponCategory / WeaponTypeRegistry)
//
// Pour ajouter une nouvelle arme :
//   1. Ajouter la valeur dans WeaponType
//   2. Lui mettre l'attribut [WeaponInfo(category)] (départ)
//      ou [WeaponInfo(category, family)] (variante)
//   C'est tout. Catégorie, famille, isStarting → auto-détectés.
//   Le basicAttackSkill est assigné dans WeaponTypeRegistry (Inspector).
//
// Armes de départ (démo) : ShortSword, Bow, Staff
// Armes de départ (final) : ShortSword, GreatAxe, Scythe,
//   Mace, Dagger, Shield, Bow, Pistol, Whip, Staff, Orb,
//   Tome, Wand
// =============================================================


// =============================================================
// WEAPONINFОATTRIBUTE
// =============================================================
[AttributeUsage(AttributeTargets.Field)]
public class WeaponInfoAttribute : Attribute
{
    public WeaponCategory Category   { get; }
    public bool           IsStarting { get; }
    public WeaponType     Family     { get; }

    /// <summary>Arme de départ.</summary>
    public WeaponInfoAttribute(WeaponCategory category)
    {
        Category   = category;
        IsStarting = true;
        Family     = WeaponType.Any;
    }

    /// <summary>Variante in-game d'une famille de départ.</summary>
    public WeaponInfoAttribute(WeaponCategory category, WeaponType family)
    {
        Category   = category;
        IsStarting = false;
        Family     = family;
    }
}


// =============================================================
// ENUM WEAPONTYPE
// =============================================================
public enum WeaponType
{
    Any,    // Joker — aucune restriction


    // ── Unarmed ─────────────────────────────────────────────────
    [WeaponInfo(WeaponCategory.Unarmed)]
    UnArmed,


    // ── Mêlée ───────────────────────────────────────────────────

    // Épée courte — départ démo + final
    [WeaponInfo(WeaponCategory.Melee)]
    ShortSword,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.ShortSword)]
    LongSword,          // Épée longue

    [WeaponInfo(WeaponCategory.Melee, WeaponType.ShortSword)]
    DoubleSword,        // Double épée

    // Hache — départ final uniquement
    [WeaponInfo(WeaponCategory.Melee)]
    GreatAxe,           // Pas de variante pour l'instant

    // Faux — départ final uniquement
    [WeaponInfo(WeaponCategory.Melee)]
    Scythe,             // Pas de variante pour l'instant

    // Massue — départ final uniquement
    [WeaponInfo(WeaponCategory.Melee)]
    Mace,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.Mace)]
    Hammer,             // Marteau (variante de la massue)

    // Dague — départ final uniquement
    [WeaponInfo(WeaponCategory.Melee)]
    Dagger,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.Dagger)]
    DoubleDagger,       // Double dague

    // Bouclier — départ final uniquement (pas de variante)
    [WeaponInfo(WeaponCategory.Melee)]
    Shield,


    // ── Distance ────────────────────────────────────────────────

    // Arc — départ démo + final
    [WeaponInfo(WeaponCategory.Ranged)]
    Bow,

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Bow)]
    Crossbow,           // Arbalète

    // Pistolet — départ final uniquement
    [WeaponInfo(WeaponCategory.Ranged)]
    Pistol,

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Pistol)]
    Shotgun,            // Pompe

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Pistol)]
    Sniper,             // Sniper

    // Fouet — départ final uniquement (pas de variante)
    [WeaponInfo(WeaponCategory.Ranged)]
    Whip,


    // ── Magique ─────────────────────────────────────────────────

    // Bâton — départ démo + final
    [WeaponInfo(WeaponCategory.Magic)]
    Staff,

    [WeaponInfo(WeaponCategory.Magic, WeaponType.Staff)]
    Scepter,            // Sceptre

    // Orbe — départ final uniquement (pas de variante)
    [WeaponInfo(WeaponCategory.Magic)]
    Orb,

    // Tome — départ final uniquement (pas de variante)
    [WeaponInfo(WeaponCategory.Magic)]
    Tome,

    // Baguette — départ final uniquement (pas de variante)
    [WeaponInfo(WeaponCategory.Magic)]
    Wand,
}


// =============================================================
// EXTENSIONS — lit les attributs, cache les résultats
// =============================================================
public static class WeaponTypeExtensions
{
    private static readonly Dictionary<WeaponType, WeaponInfoAttribute> _cache
        = new Dictionary<WeaponType, WeaponInfoAttribute>();

    private static WeaponInfoAttribute GetInfo(WeaponType type)
    {
        if (_cache.TryGetValue(type, out var cached)) return cached;
        var field = typeof(WeaponType).GetField(type.ToString());
        var attr  = field?.GetCustomAttributes(typeof(WeaponInfoAttribute), false);
        var info  = (attr != null && attr.Length > 0) ? (WeaponInfoAttribute)attr[0] : null;
        _cache[type] = info;
        return info;
    }

    /// <summary>Catégorie de l'arme (Melee/Ranged/Magic).</summary>
    public static WeaponCategory GetCategory(this WeaponType type)
        => GetInfo(type)?.Category ?? WeaponCategory.Melee;

    /// <summary>ArmorType lié à cette catégorie d'arme.</summary>
    public static ArmorType GetArmorType(this WeaponType type)
    {
        switch (type.GetCategory())
        {
            case WeaponCategory.Ranged: return ArmorType.Ranged;
            case WeaponCategory.Magic:  return ArmorType.Magic;
            default:                    return ArmorType.Melee;
        }
    }

    /// <summary>True si arme sélectionnable à la création (version finale).</summary>
    public static bool IsStartingWeapon(this WeaponType type)
        => GetInfo(type)?.IsStarting ?? false;

    /// <summary>True si arme disponible dans la démo (3 armes de départ).</summary>
    public static bool IsDemoWeapon(this WeaponType type)
        => type == WeaponType.ShortSword
        || type == WeaponType.Bow
        || type == WeaponType.Staff;

    /// <summary>
    /// Famille de départ de cette arme.
    /// Ex: DoubleSword → ShortSword | ShortSword → ShortSword
    /// </summary>
    public static WeaponType GetStartingFamily(this WeaponType type)
    {
        var info = GetInfo(type);
        if (info == null || info.IsStarting) return type;
        return info.Family;
    }

    /// <summary>Toutes les armes d'une catégorie donnée.</summary>
    public static List<WeaponType> GetAllOfCategory(WeaponCategory category)
    {
        var result = new List<WeaponType>();
        foreach (WeaponType t in Enum.GetValues(typeof(WeaponType)))
        {
            if (t == WeaponType.Any) continue;
            if (t.GetCategory() == category) result.Add(t);
        }
        return result;
    }

    /// <summary>Toutes les armes de départ sélectionnables à la création (13 familles — version finale).
    /// Exclut WeaponType.UnArmed (Unarmed) qui n'est pas un choix de départ.</summary>
    public static List<WeaponType> GetAllStartingWeapons()
    {
        var result = new List<WeaponType>();
        foreach (WeaponType t in Enum.GetValues(typeof(WeaponType)))
            if (t != WeaponType.Any && t != WeaponType.UnArmed && t.IsStartingWeapon())
                result.Add(t);
        return result;
    }

    /// <summary>Toutes les variantes d'une famille de départ.</summary>
    public static List<WeaponType> GetVariantsOf(WeaponType startingWeapon)
    {
        var result = new List<WeaponType>();
        foreach (WeaponType t in Enum.GetValues(typeof(WeaponType)))
        {
            if (t == WeaponType.Any || t == startingWeapon) continue;
            if (!t.IsStartingWeapon() && t.GetStartingFamily() == startingWeapon)
                result.Add(t);
        }
        return result;
    }

    /// <summary>True si cette arme est une variante débloquable (pas une arme de départ).</summary>
    public static bool IsVariant(this WeaponType type)
        => type != WeaponType.Any && !type.IsStartingWeapon();
}


// =============================================================
// WEAPONTYPEREGISTRY — ScriptableObject global
// Mappe chaque WeaponType (famille de départ) vers son skill
// d'attaque de base. Les variantes héritent automatiquement
// via WeaponTypeExtensions.GetStartingFamily().
//
// Ex: LongSword → famille ShortSword → ShortSword_Skill_01
//
// Usage : WeaponTypeRegistry.Instance.GetBasicAttackSkill(weaponType)
//
// Setup : créer via Assets > Create > AetherTree > Weapons > WeaponTypeRegistry
//         et assigner dans GameDataRegistry sur le GameObject _Managers.
// =============================================================
[CreateAssetMenu(fileName = "WeaponTypeRegistry", menuName = "AetherTree/Weapons/WeaponTypeRegistry")]
public class WeaponTypeRegistry : ScriptableObject
{
    public static WeaponTypeRegistry Instance { get; internal set; }

    [System.Serializable]
    public class WeaponSkillEntry
    {
        [Tooltip("Type d'arme de départ (famille). Ex: ShortSword, Bow, Staff...")]
        public WeaponType weaponType;
        [Tooltip("Skill d'attaque de base assigné à cette famille.")]
        public SkillData  basicAttackSkill;
    }

    [Header("Mapping WeaponType (famille) → Skill d'attaque de base")]
    [Tooltip("N'assigner que les armes de départ (familles).\n" +
             "Les variantes (LongSword, DoubleSword...) héritent automatiquement.")]
    public List<WeaponSkillEntry> entries = new List<WeaponSkillEntry>();

    // Cache pour éviter une recherche linéaire à chaque appel
    private Dictionary<WeaponType, SkillData> _cache;

    private void OnEnable()
    {
        Instance = this;
        BuildCache();
    }

    private void BuildCache()
    {
        _cache = new Dictionary<WeaponType, SkillData>();
        foreach (var entry in entries)
        {
            if (entry.basicAttackSkill == null)
            {
                Debug.LogWarning($"[WeaponTypeRegistry] {entry.weaponType} : basicAttackSkill non assigné !");
                continue;
            }
            _cache[entry.weaponType] = entry.basicAttackSkill;
        }
    }

    /// <summary>
    /// Retourne le skill d'attaque de base pour un WeaponType donné.
    /// Les variantes remontent automatiquement à leur famille de départ.
    /// Retourne null si introuvable — l'appelant doit logger l'erreur.
    /// </summary>
    public SkillData GetBasicAttackSkill(WeaponType weaponType)
    {
        if (_cache == null) BuildCache();

        // Remonte à la famille de départ si c'est une variante
        WeaponType family = weaponType.GetStartingFamily();

        if (_cache.TryGetValue(family, out SkillData skill))
            return skill;

        return null;
    }
}


// =============================================================
// ENUMS LIÉS
// =============================================================
public enum WeaponCategory { Unarmed, Melee, Ranged, Magic }
public enum ArmorType      { Melee, Ranged, Magic }
