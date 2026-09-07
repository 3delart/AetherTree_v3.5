using System;
using System.Collections.Generic;
using UnityEngine;

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
//   Le basicAttackSkill est assigné dans WeaponTypeRegistry (Inspector).
//
// Titre d'arme (GetWeaponLabel) :
//   Chaque WeaponType a un titre lisible utilisé dans le titre élémentaire.
//   Ex: ShortSword → "Lame" | GreatAxe → "Berserker" | Staff → "Mage"
//   Les variantes ont leur propre titre (LongSword → "Chevalier").
//   Utilisé par Player.RefreshTitle() pour construire "Lame Embrasée".
// =============================================================


// =============================================================
// WEAPONINFOATTRIBUTE
// =============================================================
[AttributeUsage(AttributeTargets.Field)]
public class WeaponInfoAttribute : Attribute
{
    public WeaponCategory Category   { get; }
    public bool           IsStarting { get; }
    public WeaponType     Family     { get; }

    public WeaponInfoAttribute(WeaponCategory category)
    {
        Category   = category;
        IsStarting = true;
        Family     = WeaponType.Any;
    }

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
    Any = 0,

    [WeaponInfo(WeaponCategory.Unarmed)]
    UnArmed = 1,

    // ── Mêlée ───────────────────────────────────────────────────
    [WeaponInfo(WeaponCategory.Melee)]
    ShortSword = 2,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.ShortSword)]
    LongSword = 3,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.ShortSword)]
    DoubleSword = 4,

    [WeaponInfo(WeaponCategory.Melee)]
    GreatAxe = 5,

    [WeaponInfo(WeaponCategory.Melee)]
    Scythe = 6,

    [WeaponInfo(WeaponCategory.Melee)]
    Mace = 7,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.Mace)]
    Hammer = 8,

    [WeaponInfo(WeaponCategory.Melee)]
    Dagger = 9,

    [WeaponInfo(WeaponCategory.Melee, WeaponType.Dagger)]
    DoubleDagger = 10,

    [WeaponInfo(WeaponCategory.Melee)]
    Shield = 11,

    // ── Distance ────────────────────────────────────────────────
    [WeaponInfo(WeaponCategory.Ranged)]
    Bow = 12,

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Bow)]
    Crossbow = 13,

    [WeaponInfo(WeaponCategory.Ranged)]
    Pistol = 14,

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Pistol)]
    Shotgun = 15,

    [WeaponInfo(WeaponCategory.Ranged, WeaponType.Pistol)]
    Sniper = 16,


    // ── Magique ─────────────────────────────────────────────────
    [WeaponInfo(WeaponCategory.Magic)]
    Staff = 17,

    [WeaponInfo(WeaponCategory.Magic, WeaponType.Staff)]
    Scepter = 18,

    [WeaponInfo(WeaponCategory.Magic)]
    Orb = 19,

    [WeaponInfo(WeaponCategory.Magic)]
    Tome = 20,

    [WeaponInfo(WeaponCategory.Magic)]
    Wand = 21,
}


// =============================================================
// ENUMS LIÉS
// =============================================================
public enum WeaponCategory { Unarmed = 0, Melee = 1, Ranged = 2, Magic = 3 }
public enum ArmorType      { Lourde = 0, Legere = 1, Robe = 2 }


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

    /// <summary>Catégorie de l'arme (Unarmed/Melee/Ranged/Magic).</summary>
    public static WeaponCategory GetCategory(this WeaponType type)
        => GetInfo(type)?.Category ?? WeaponCategory.Melee;

    /// <summary>ArmorType lié à cette catégorie d'arme.</summary>
    public static ArmorType GetArmorType(this WeaponType type)
    {
        switch (type.GetCategory())
        {
            case WeaponCategory.Ranged: return ArmorType.Legere;
            case WeaponCategory.Magic:  return ArmorType.Robe;
            default:                    return ArmorType.Lourde;
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

    /// <summary>Toutes les armes de départ sélectionnables à la création.
    /// Exclut WeaponType.Any et WeaponType.UnArmed.</summary>
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

    // =============================================================
    // TITRE D'ARME — utilisé dans Player.RefreshTitle()
    // Indépendant du weaponName SO (ex: "Épée du Dragon").
    // Chaque WeaponType a son propre titre de classe.
    // Ex: ShortSword → "Lame" | LongSword → "Chevalier" | Staff → "Mage"
    // =============================================================

    /// <summary>
    /// Titre de classe de l'arme — utilisé pour construire le titre élémentaire complet.
    /// Ex: "Lame Embrasée", "Chevalier Radieux", "Berserker Volcanique".
    /// Retourne "" pour Any / UnArmed.
    /// </summary>
    public static string GetWeaponLabel(this WeaponType wt)
    {
        return wt switch
        {
            // ── Mêlée ──────────────────────────────────────────
            WeaponType.ShortSword   => "Lame",
            WeaponType.LongSword    => "Chevalier",
            WeaponType.DoubleSword  => "Duelliste",
            WeaponType.GreatAxe     => "Berserker",
            WeaponType.Scythe       => "Faucheur",
            WeaponType.Mace         => "Briseur",
            WeaponType.Hammer       => "Écraseur",
            WeaponType.Dagger       => "Assassin",
            WeaponType.DoubleDagger => "Traqueur",
            WeaponType.Shield       => "Sentinelle",
            // ── Distance ───────────────────────────────────────
            WeaponType.Bow          => "Archer",
            WeaponType.Crossbow     => "Arbalétrier",
            WeaponType.Pistol       => "Tireur",
            WeaponType.Shotgun      => "Gunner",
            WeaponType.Sniper       => "Précisionniste",
            // ── Magique ────────────────────────────────────────
            WeaponType.Staff        => "Mage",
            WeaponType.Scepter      => "Arcaniste",
            WeaponType.Orb          => "Gardien de l'Âme",
            WeaponType.Tome         => "Bibliomancien",
            WeaponType.Wand         => "Enchanteur",
            // ── Fallback ───────────────────────────────────────
            _                       => ""
        };
    }
}
