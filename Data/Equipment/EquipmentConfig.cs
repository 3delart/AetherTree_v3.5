using UnityEngine;
using System.Collections.Generic;

// =============================================================
// EquipmentConfig — Configuration d'effets d'un équipement
// Path : Assets/Scripts/Data/Inventory/Equipment/EquipmentConfig.cs
// AetherTree GDD v3.5 — §5.1 à §5.6
//
// Remplace les 4 listes séparées (bonuses / statusEffects /
// debuffResistances / onHitEffects) par une seule classe
// assignable dans l'Inspector via un champ unique.
//
// Usage dans un SO :
//   public EquipmentConfig config;
//
// Lecture dans CharacterStats.RecalculateStats() :
//   weapon.config.bonuses
//   weapon.config.statusEffects
//   weapon.config.debuffResistances
//   weapon.config.onHitEffects
//
// Tous les slots d'équipement utilisent cette même structure —
// CombatSystem, StatusEffectSystem et CharacterStats ne font
// jamais la distinction selon le slot source.
// =============================================================

[System.Serializable]
public class EquipmentConfig
{
    [Header("Bonus de stats (passifs permanents)")]
    [Tooltip("Bonus fixes appliqués tant que l'équipement est porté.\n" +
             "Lus par CharacterStats.RecalculateStats() à chaque recalcul.\n\n" +
             "FLAT  : Défenses, BonusAttack, Dodge, Précision, MoveSpeed,\n" +
             "        PointsFire/All, BonusHP, BonusMana, BonusRegen\n" +
             "        → entrer la valeur directe  ex: 200, 10, 0.5\n\n" +
             "RATIO : CritChance, CritDamage, ResistFire/All\n" +
             "        → entrer en décimal  ex: 0.05 = 5% | 0.10 = 10%")]
    public List<StatBonus> bonuses = new List<StatBonus>();

    [Header("Effets de statut (appliqués à chaque attaque)")]
    [Tooltip("Effets appliqués lors d'une attaque selon leur probabilité.\n" +
             "Glisser un DebuffData ou BuffData, puis régler la chance.\n" +
             "Ex: Burn 4s à 5% | Slow 2s à 10%")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("Résistances aux debuffs (porteur)")]
    [Tooltip("Chances de résister à un debuff spécifique quand on est attaqué.\n" +
             "Chargées dans StatusEffectSystem via CharacterStats.RecalculateStats().\n" +
             "Ex: Freeze 0.05 = 5% de résistance au gel")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

    [Header("Effets On-Hit (déclenchés quand le porteur reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le porteur reçoit un coup.\n" +
             "Lus par Player.ApplyOnHitEffects() à chaque TakeDamage().\n" +
             "Ex: Thorns 10 dmg à 100% | Reflect 20% à 15% | HealOnHit 2% MaxHP")]
    public List<OnHitEffectEntry> onHitEffects = new List<OnHitEffectEntry>();
}
