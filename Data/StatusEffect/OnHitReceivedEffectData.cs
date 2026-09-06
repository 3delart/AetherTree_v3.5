using UnityEngine;

// =============================================================
// OnHitReceivedEffectData — ScriptableObject template d'effet On-Hit REÇU
// Path : Assets/Scripts/Data/StatusEffect/OnHitReceivedEffectData.cs
// AetherTree GDD v3.5 §5.1 à §5.6 — refonte OnHitDealt/OnHitReceived (2026-09-06)
//
// Déclenché quand l'entité PORTEUSE (celle qui a cet effet dans sa liste
// onHitReceivedEffects) REÇOIT un coup. Chaque effet a une chance [0..1].
//
// Types disponibles :
//   DamageReductionOnHit — MODIFICATEUR, réduit CE coup en cours de calcul
//                          (roulé/appliqué dans CombatSystem.CalculateDamage/
//                          CalculateMobDamage, PAS ici comme réaction)
//   ReflectPercent   — renvoie X% des dégâts reçus à l'attaquant
//   Thorns           — renvoie une valeur fixe de dégâts à l'attaquant
//   CounterDebuff    — applique un debuff sur l'attaquant
//   HealOnHit        — soigne la cible (self)
//   CounterBuff      — applique un buff sur soi-même
//
// ReflectPercent & Thorns : pierceDefense contrôle si les dégâts renvoyés
//   ignorent la défense/résistance de l'attaquant (true = brut).
//
// Voir OnHitDealtEffectData.cs pour le pendant côté "coup infligé".
//
// Assets > Create > AetherTree > StatusEffects > OnHitReceivedEffectData
// =============================================================

public enum OnHitReceivedEffectType
{
    DamageReductionOnHit,
    ReflectPercent,
    Thorns,
    CounterDebuff,
    HealOnHit,
    CounterBuff,
}

[CreateAssetMenu(fileName = "NewOnHitReceived", menuName = "AetherTree/StatusEffects/OnHitReceivedEffectData")]
public class OnHitReceivedEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "OnHitReceivedEffect";
    public Sprite icon;

    [Header("Type")]
    public OnHitReceivedEffectType effectType = OnHitReceivedEffectType.Thorns;

    // Pas de champ "chance" ici — la probabilité de déclenchement se règle UNIQUEMENT sur
    // l'Entry (équipement/permanent/MobData/PNJData), voir OnHitReceivedEffectEntry.chance.
    // Avant, un champ existait ici EN PLUS de l'override sur l'Entry — deux endroits pour
    // régler la même chose, aucun double-jet réel (un seul Random.value via EffectiveChance),
    // mais confus pour le designer. Retiré (2026-09-06, demande Florian).

    // ── DamageReductionOnHit ──────────────────────────────────
    [Tooltip("% de dégâts en moins sur CE coup reçu. Ex: 0.50 = -50%. Roulé dans CombatSystem, " +
             "au même stage que le critique — voir CalculateDamage/CalculateMobDamage.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.DamageReductionOnHit, Header = "Réduction (DamageReductionOnHit)")]
    public float reductionPercent = 0.30f;

    // ── ReflectPercent ────────────────────────────────────────
    [Tooltip("Pourcentage des dégâts reçus renvoyés à l'attaquant.\nEx: 0.20 = 20% réfléchis.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, Header = "Reflect % (ReflectPercent)")]
    public float reflectPercent = 0.20f;

    // ── Thorns ────────────────────────────────────────────────
    [Tooltip("Dégâts fixes renvoyés à l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.Thorns, Header = "Épines fixes (Thorns)")]
    public float thornsDamage = 10f;

    // ── Commun Reflect + Thorns ───────────────────────────────
    [Tooltip("True : l'attaquant reçoit le montant fixé, tel quel (ignore défense/résistance).\n" +
             "False : réduit comme un dégât normal — défense (Neutral) ou résistance élémentaire\n" +
             "(autre élément) de l'attaquant, selon reflectElement ci-dessous.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, OnHitReceivedEffectType.Thorns, Header = "Options Reflect & Thorns", DisplayName = "Dégâts renvoyés bruts")]
    public bool pierceDefense = false;

    [Tooltip("Élément des dégâts renvoyés.\nNeutral = pas d'élément.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, OnHitReceivedEffectType.Thorns)]
    public ElementType reflectElement = ElementType.Neutral;

    // ── CounterDebuff ─────────────────────────────────────────
    [Tooltip("Debuff appliqué sur l'attaquant au déclenchement.\nGlisser un DebuffData ici.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.CounterDebuff, Header = "Counter-Debuff (CounterDebuff)")]
    public DebuffData counterDebuff;

    // ── HealOnHit ─────────────────────────────────────────────
    [Tooltip("Flat : valeur fixe soignée.\nPercent : % du MaxHP de la cible.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.HealOnHit, Header = "Soin au coup reçu (HealOnHit)")]
    public ModifierType healModifier = ModifierType.Flat;
    [Tooltip("Montant de soin.\nEx: 50 (Flat) ou 0.05 (Percent = 5% MaxHP).")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.HealOnHit)]
    public float healAmount = 50f;

    // ── CounterBuff ───────────────────────────────────────────
    [Tooltip("Buff appliqué sur soi-même au déclenchement.\nGlisser un BuffData ici.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.CounterBuff, Header = "Counter-Buff sur soi (CounterBuff)")]
    public BuffData counterBuff;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Calcule le soin à appliquer selon le MaxHP de la cible.</summary>
    public float GetHealAmount(float targetMaxHP)
        => healModifier == ModifierType.Percent ? targetMaxHP * healAmount : healAmount;
}

// =============================================================
// OnHitReceivedEffectEntry — une ligne dans la liste d'un équipement/permanent/mob/pnj
// =============================================================
[System.Serializable]
public class OnHitReceivedEffectEntry
{
    [Tooltip("SO de l'effet On-Hit reçu à appliquer.")]
    public OnHitReceivedEffectData effect;

    [Tooltip("Probabilité de déclenchement par coup reçu [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    /// <summary>Roll si l'effet se déclenche ce coup.</summary>
    public bool Roll() => effect != null && Random.value < chance;
}
