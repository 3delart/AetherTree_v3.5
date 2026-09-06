using UnityEngine;

// =============================================================
// OnHitDealtEffectData — ScriptableObject template d'effet On-Hit DONNÉ
// Path : Assets/Scripts/Data/StatusEffect/OnHitDealtEffectData.cs
// AetherTree GDD v3.5 — refonte OnHitDealt/OnHitReceived (2026-09-06)
//
// Déclenché quand l'entité PORTEUSE (celle qui a cet effet dans sa liste
// onHitDealtEffects — équipement/permanent/MobData/PNJData) TOUCHE une cible.
// Chaque effet a une chance d'activation [0..1].
//
// Types disponibles :
//   DamageAmpOnHit   — MODIFICATEUR, amplifie CE coup en cours de calcul
//                       (roulé/appliqué dans CombatSystem.CalculateDamage/
//                       CalculateMobDamage, PAS ici comme réaction)
//   LifestealOnHit   — réaction, soigne l'attaquant (Flat ou % des dégâts infligés)
//   ManaOnHit        — réaction, restaure du mana à l'attaquant (Flat ou % MaxMana)
//   ApplyDebuffOnHit — réaction, applique un DebuffData sur LA CIBLE touchée
//   ApplyBuffOnHit   — réaction, applique un BuffData sur L'ATTAQUANT (soi)
//
// Voir OnHitReceivedEffectData.cs pour le pendant côté "coup reçu".
//
// Assets > Create > AetherTree > StatusEffects > OnHitDealtEffectData
// =============================================================

public enum OnHitDealtEffectType
{
    DamageAmpOnHit,
    LifestealOnHit,
    ManaOnHit,
    ApplyDebuffOnHit,
    ApplyBuffOnHit,
}

[CreateAssetMenu(fileName = "NewOnHitDealt", menuName = "AetherTree/StatusEffects/OnHitDealtEffectData")]
public class OnHitDealtEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "OnHitDealtEffect";
    public Sprite icon;

    [Header("Type")]
    public OnHitDealtEffectType effectType = OnHitDealtEffectType.LifestealOnHit;

    [Header("Déclenchement")]
    [Tooltip("Probabilité de déclenchement par coup infligé [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    // ── DamageAmpOnHit ────────────────────────────────────────
    [Tooltip("% de dégâts en plus sur CE coup. Ex: 0.10 = +10%. Roulé dans CombatSystem, " +
             "au même stage que le critique — voir CalculateDamage/CalculateMobDamage.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.DamageAmpOnHit, Header = "Amplification (DamageAmpOnHit)")]
    public float ampPercent = 0.10f;

    // ── LifestealOnHit ────────────────────────────────────────
    [Tooltip("Flat : montant fixe soigné.\nPercent : % des dégâts infligés CE coup.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit, Header = "Vol de vie (LifestealOnHit)")]
    public ModifierType lifestealModifier = ModifierType.Percent;
    [Tooltip("Montant du vol de vie.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit)]
    public float lifestealAmount = 0.10f;

    // ── ManaOnHit ─────────────────────────────────────────────
    [Tooltip("Flat : montant fixe.\nPercent : % du MaxMana de l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit, Header = "Restauration de mana (ManaOnHit)")]
    public ModifierType manaModifier = ModifierType.Flat;
    [Tooltip("Montant de mana restauré.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit)]
    public float manaAmount = 5f;

    // ── ApplyDebuffOnHit ──────────────────────────────────────
    [Tooltip("Debuff appliqué sur LA CIBLE touchée au déclenchement.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyDebuffOnHit, Header = "Debuff sur la cible (ApplyDebuffOnHit)")]
    public DebuffData debuffToApply;

    // ── ApplyBuffOnHit ────────────────────────────────────────
    [Tooltip("Buff appliqué sur L'ATTAQUANT (soi-même) au déclenchement.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyBuffOnHit, Header = "Buff sur soi (ApplyBuffOnHit)")]
    public BuffData buffToApply;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Retourne true si l'effet se déclenche ce coup.</summary>
    public bool Roll() => Random.value < chance;

    /// <summary>Calcule le montant de vol de vie selon les dégâts infligés ce coup.</summary>
    public float GetLifestealAmount(float damageDealt)
        => lifestealModifier == ModifierType.Percent ? damageDealt * lifestealAmount : lifestealAmount;

    /// <summary>Calcule le montant de mana restauré selon le MaxMana de l'attaquant.</summary>
    public float GetManaAmount(float attackerMaxMana)
        => manaModifier == ModifierType.Percent ? attackerMaxMana * manaAmount : manaAmount;
}

// =============================================================
// OnHitDealtEffectEntry — une ligne dans la liste d'un équipement/permanent/mob/pnj
// =============================================================
[System.Serializable]
public class OnHitDealtEffectEntry
{
    [Tooltip("SO de l'effet On-Hit infligé à appliquer.")]
    public OnHitDealtEffectData effect;

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
