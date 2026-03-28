using UnityEngine;
using System.Collections.Generic;

// =============================================================
// WEAPONTYPEREGISTRY — ScriptableObject global
// Path : Assets/Scripts/Data/Inventory/Equipment/WeaponTypeRegistry.cs
// AetherTree GDD v3.5 — §5.1 (WeaponTypeRegistry)
//
// Mappe chaque WeaponType (famille de départ) vers son skill
// d'attaque de base. Les variantes héritent automatiquement
// via WeaponTypeExtensions.GetStartingFamily().
//
// Ex: LongSword → famille ShortSword → ShortSword_Skill_01
//
// Usage : WeaponTypeRegistry.Instance.GetBasicAttackSkill(weaponType)
//
// Setup : créer via Assets > Create > AetherTree > Weapons > WeaponTypeRegistry
//         et assigner dans le champ _instance de la scène ou via Resources.
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
