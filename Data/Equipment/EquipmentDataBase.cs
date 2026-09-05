using UnityEngine;

// =============================================================
// EquipmentDataBase — base commune aux pièces d'équipement portées
// Path : Assets/Scripts/Data/Equipment/EquipmentDataBase.cs
// AetherTree GDD v3.6 — §5.1
//
// Pour les types qui portent un EquipmentConfig (bonus, effets de
// statut, résistances, on-hit) : Weapon, Armor, Helmet, Gloves,
// Boots, Jewelry, Spirit, CosmeticHead, CosmeticBody.
//
// Talisman, Resource, Consumable et CosmeticData (legacy) héritent
// directement de ItemData — pas de config sur ces types.
// =============================================================

public abstract class EquipmentDataBase : ItemData
{
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    [Tooltip("Bonus passifs, effets appliqués à l'attaque, résistances aux debuffs\n" +
             "et effets On-Hit reçus — regroupés dans un seul champ.\n" +
             "Lus par CharacterStats.RecalculateStats() et Player.ApplyOnHitEffects().")]
    public EquipmentConfig config;
}
