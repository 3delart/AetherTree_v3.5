using System;
using System.Collections.Generic;

// =============================================================
// WEAPONTYPE.CS — Data-driven, zéro switch à maintenir
// Path : Assets/Scripts/Data/Inventory/Equipment/WeaponType.cs
// AetherTree GDD v3.5 — §5.1 (WeaponType / WeaponCategory)
//
// Pour ajouter une nouvelle arme :
//   1. Ajouter la valeur dans WeaponType
//   2. Lui mettre l'attribut [WeaponInfo(category)] (départ)
//      ou [WeaponInfo(category, family)] (variante)
//   C'est tout. Catégorie, famille, isStarting → auto-détectés.
//
// Armes de départ (démo) : ShortSword, Bow, Staff
// Armes de départ (final) : ShortSword, GreatAxe, Scythe,
//   Mace, Dagger, Shield, Bow, Pistol, Whip, Staff, Orb,
//   Tome, Wand
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
    Hands,


    // ── Mêlée ─────────────────────────────────────────────────

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

    // ── Distance ──────────────────────────────────────────────

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

    // ── Magique ───────────────────────────────────────────────

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
    /// Exclut WeaponType.Hands (Unarmed) qui n'est pas un choix de départ.</summary>
    public static List<WeaponType> GetAllStartingWeapons()
    {
        var result = new List<WeaponType>();
        foreach (WeaponType t in Enum.GetValues(typeof(WeaponType)))
            if (t != WeaponType.Any && t != WeaponType.Hands && t.IsStartingWeapon())
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
// ENUMS LIÉS
// =============================================================
public enum WeaponCategory { Unarmed, Melee, Ranged, Magic }
public enum ArmorType      { Melee, Ranged, Magic }