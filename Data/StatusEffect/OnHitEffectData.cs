using UnityEngine;
using System.Collections.Generic;

// =============================================================
// OnHitEffectData — ScriptableObject template d'effet On-Hit
// Path : Assets/Scripts/Data/StatusEffect/OnHitEffectData.cs
// AetherTree GDD v3.5 — §5.1 à §5.6 (OnHitEffects via EquipmentConfig)
//
// Déclenché quand l'entité équipée REÇOIT un coup.
// Chaque effet a une chance d'activation [0..1].
//
// Types disponibles :
//   ReflectPercent   — renvoie X% des dégâts reçus à l'attaquant
//   Thorns           — renvoie une valeur fixe de dégâts à l'attaquant
//   CounterDebuff    — applique un debuff sur l'attaquant
//   HealOnHit        — soigne la cible sur le coup reçu
//   CounterBuff      — applique un buff sur soi-même
//
// ReflectPercent & Thorns : pierceDefense contrôle si les
//   dégâts renvoyés ignorent la défense de l'attaquant.
//
// Assets > Create > AetherTree > Equipment > OnHitEffectData
// =============================================================

public enum OnHitEffectType
{
    ReflectPercent, // Renvoie X% des dégâts reçus
    Thorns,         // Renvoie une valeur fixe
    CounterDebuff,  // Applique un debuff sur l'attaquant
    HealOnHit,      // Soigne la cible (self)
    CounterBuff,    // Applique un buff sur soi-même
}

[CreateAssetMenu(fileName = "NewOnHitEffect", menuName = "AetherTree/StatusEffects/OnHitEffectData")]
public class OnHitEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "OnHitEffect";
    public Sprite icon;

    [Header("Type")]
    public OnHitEffectType effectType = OnHitEffectType.Thorns;

    [Header("Déclenchement")]
    [Tooltip("Probabilité de déclenchement par coup reçu [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    // ── ReflectPercent ────────────────────────────────────────
    [Tooltip("Pourcentage des dégâts reçus renvoyés à l'attaquant.\nEx: 0.20 = 20% réfléchis.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), OnHitEffectType.ReflectPercent, Header = "Reflect % (ReflectPercent)")]
    public float reflectPercent = 0.20f;

    // ── Thorns ────────────────────────────────────────────────
    [Tooltip("Dégâts fixes renvoyés à l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitEffectType.Thorns, Header = "Épines fixes (Thorns)")]
    public float thornsDamage = 10f;

    // ── Commun Reflect + Thorns ───────────────────────────────
    [Tooltip("Si true : les dégâts renvoyés ignorent la défense de l'attaquant (dégâts bruts).\n" +
             "Si false : passent par CombatSystem normalement.")]
    [ShowIf(nameof(effectType), OnHitEffectType.ReflectPercent, OnHitEffectType.Thorns, Header = "Options Reflect & Thorns")]
    public bool pierceDefense = false;

    [Tooltip("Élément des dégâts renvoyés.\nNeutral = pas d'élément.")]
    [ShowIf(nameof(effectType), OnHitEffectType.ReflectPercent, OnHitEffectType.Thorns)]
    public ElementType reflectElement = ElementType.Neutral;

    // ── CounterDebuff ─────────────────────────────────────────
    [Tooltip("Debuff appliqué sur l'attaquant au déclenchement.\nGlisser un DebuffData ici.")]
    [ShowIf(nameof(effectType), OnHitEffectType.CounterDebuff, Header = "Counter-Debuff (CounterDebuff)")]
    public DebuffData counterDebuff;

    // ── HealOnHit ─────────────────────────────────────────────
    [Tooltip("Flat : valeur fixe soignée.\nPercent : % du MaxHP de la cible.")]
    [ShowIf(nameof(effectType), OnHitEffectType.HealOnHit, Header = "Soin au coup reçu (HealOnHit)")]
    public ModifierType healModifier = ModifierType.Flat;
    [Tooltip("Montant de soin.\nEx: 50 (Flat) ou 0.05 (Percent = 5% MaxHP).")]
    [ShowIf(nameof(effectType), OnHitEffectType.HealOnHit)]
    public float healAmount = 50f;

    // ── CounterBuff ───────────────────────────────────────────
    [Tooltip("Buff appliqué sur soi-même au déclenchement.\nGlisser un BuffData ici.")]
    [ShowIf(nameof(effectType), OnHitEffectType.CounterBuff, Header = "Counter-Buff sur soi (CounterBuff)")]
    public BuffData counterBuff;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Retourne true si l'effet se déclenche ce coup.</summary>
    public bool Roll() => Random.value < chance;

    /// <summary>Calcule le soin à appliquer selon le MaxHP de la cible.</summary>
    public float GetHealAmount(float targetMaxHP)
        => healModifier == ModifierType.Percent ? targetMaxHP * healAmount : healAmount;
}

// =============================================================
// OnHitEffectEntry — une ligne dans la liste d'un équipement
// Glisse le SO + possibilité d'override la chance localement.
// =============================================================
[System.Serializable]
public class OnHitEffectEntry
{
    [Tooltip("SO de l'effet On-Hit à appliquer.")]
    public OnHitEffectData effect;

    [Tooltip("Override de la chance du SO [0..1].\nSi 0, utilise la chance définie dans le SO.")]
    [Range(0f, 1f)]
    public float chanceOverride = 0f;

    /// <summary>Chance effective : override si > 0, sinon valeur du SO.</summary>
    public float EffectiveChance => (chanceOverride > 0f && effect != null)
        ? chanceOverride
        : (effect != null ? effect.chance : 0f);

    /// <summary>Roll si l'effet se déclenche ce coup.</summary>
    public bool Roll() => effect != null && Random.value < EffectiveChance;
}
