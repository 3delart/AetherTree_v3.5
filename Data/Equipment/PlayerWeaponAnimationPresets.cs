using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PLAYERWEAPONANIMATIONPRESETS — ScriptableObject global
// Path : Assets/Scripts/Data/Equipment/PlayerWeaponAnimationPresets.cs
//
// Mappe chaque WeaponType (famille de départ) vers son couple de clips
// locomotion armée (idle with weapon / running with weapon). Même mapping
// par famille que WeaponTypeRegistry (héritage via WeaponTypeExtensions.
// GetStartingFamily()), mais PAS de singleton Instance ici — un seul
// consommateur (PlayerAnimatorController), qui garde une référence directe
// à l'asset en Inspector plutôt que de passer par un lookup statique.
//
// Setup : créer via Assets > Create > AetherTree > Config >
//         PlayerWeaponAnimationPresets, assigner l'asset sur le champ
//         prévu dans PlayerAnimatorController (prefab Player).
// =============================================================

[CreateAssetMenu(fileName = "PlayerWeaponAnimationPresets", menuName = "AetherTree/Config/PlayerWeaponAnimationPresets")]
public class PlayerWeaponAnimationPresets : ScriptableObject
{
    [System.Serializable]
    public class WeaponAnimationEntry
    {
        [Tooltip("Type d'arme de départ (famille). Ex: ShortSword, Bow, Staff...")]
        public WeaponType weaponType;
        [Tooltip("Anim jouée à l'arrêt, arme équipée (state \"idle with weapon\").")]
        public AnimationClip idleWithWeaponClip;
        [Tooltip("Anim jouée en déplacement, arme équipée (state \"running with weapon\").")]
        public AnimationClip runningWithWeaponClip;
    }

    [Header("Mapping WeaponType (famille) → clips locomotion armée")]
    [Tooltip("N'assigner que les armes de départ (familles).\n" +
             "Les variantes (LongSword, DoubleSword...) héritent automatiquement.")]
    public List<WeaponAnimationEntry> entries = new List<WeaponAnimationEntry>();

    private Dictionary<WeaponType, WeaponAnimationEntry> _cache;

    public void RebuildCache()
    {
        _cache = new Dictionary<WeaponType, WeaponAnimationEntry>();
        foreach (var entry in entries)
        {
            if (entry.idleWithWeaponClip == null && entry.runningWithWeaponClip == null)
            {
                Debug.LogWarning($"[PlayerWeaponAnimationPresets] {entry.weaponType} : aucun clip assigné !");
                continue;
            }
            _cache[entry.weaponType] = entry;
        }
    }

    /// <summary>
    /// Retourne l'entrée idle/running armée pour un WeaponType donné.
    /// Les variantes remontent automatiquement à leur famille de départ.
    /// Retourne null si introuvable (ex: aucun preset pour ce type pour l'instant).
    /// </summary>
    public WeaponAnimationEntry GetPreset(WeaponType weaponType)
    {
        if (_cache == null) RebuildCache();

        WeaponType family = weaponType.GetStartingFamily();

        if (_cache.TryGetValue(family, out WeaponAnimationEntry entry))
            return entry;

        return null;
    }
}
