using UnityEngine;

// =============================================================
// BuffData — ScriptableObject template de buff
// Path : Assets/Scripts/Data/StatusEffect/BuffData.cs
// AetherTree GDD v3.5 — §3.1.1.2
//
// v3.5 — critBonus remplacé par critChanceBonus + critDamageBonus (deux champs séparés)
//         BuffStatType remplacé par StatModifierType (fusion avec DebuffStatType)
//
// Assets > Create > AetherTree > StatusEffects > BuffData
// =============================================================

[CreateAssetMenu(fileName = "NewBuff", menuName = "AetherTree/StatusEffects/BuffData")]
public class BuffData : StatusEffectData
{
    [Header("Type de buff")]
    public BuffType buffType;

    // ── Soin instantané (Heal) ────────────────────────────────
    [Header("Soin instantané (Heal)")]
    [Tooltip("Flat : valeur fixe (ex: 1000 HP)\nPercent : % du MaxHP de la cible (ex: 0.10 = 10%)")]
    public ModifierType healModifier = ModifierType.Flat;
    [Tooltip("Montant de soin instantané.")]
    public float healAmount = 0f;

    // ── Soin sur la durée (Regeneration) ─────────────────────
    [Header("Soin sur la durée (Regeneration)")]
    [Tooltip("Flat : valeur fixe par seconde\nPercent : % du MaxHP de la cible par seconde")]
    public ModifierType hotModifier = ModifierType.Flat;
    [Tooltip("Soin par seconde — tick géré dans BuffInstance.Tick().")]
    public float healPerSecond = 0f;

    // ── Bouclier (Shield) ─────────────────────────────────────
    [Header("Bouclier (Shield)")]
    [Tooltip("Flat : montant fixe absorbé\nPercent : % du MaxHP de la cible")]
    public ModifierType shieldModifier = ModifierType.Flat;
    [Tooltip("Montant de dégâts absorbés.")]
    public float shieldAmount = 0f;

    // ── Résistance élémentaire (Barrier — §3.1.1.2) ──────────
    [Header("Résistance élémentaire (Barrier)")]
    [Tooltip("Barrier (§3.1.1.2) : résistance élémentaire temporaire.\n0 = aucun effet | 0.20 = -20% dégâts élémentaires reçus.")]
    [Range(0f, 1f)]
    public float elementResistBonus = 0f;

    // ── Défense (DefenseUp / Fortify) ─────────────────────────
    [Header("Défense (DefenseUp)")]
    [Tooltip("Flat : valeur fixe | Percent : % de la défense actuelle")]
    public ModifierType defenseModifier = ModifierType.Flat;
    [Tooltip("Bonus de défense appliqué aux 3 types.")]
    public float defenseBonus = 0f;

    // ── Esquive (DodgeUp) ─────────────────────────────────────
    [Header("Esquive (DodgeUp)")]
    [Tooltip("Flat : valeur fixe | Percent : % de l'esquive actuelle")]
    public ModifierType dodgeModifier = ModifierType.Flat;
    [Tooltip("Bonus d'esquive.")]
    public float dodgeBonus = 0f;

    // ── Précision (PrecisionUp) ──────────────────────────────
    [Header("Précision (PrecisionUp)")]
    [Tooltip("Flat : valeur fixe | Percent : % de la précision actuelle")]
    public ModifierType precisionModifier = ModifierType.Flat;
    [Tooltip("Bonus de précision.")]
    public float precisionBonus = 0f;

    // ── Vitesse (Haste) ───────────────────────────────────────
    [Header("Vitesse (Haste)")]
    [Tooltip("Multiplicateur de vitesse.\nEx: 1.3 = +30% vitesse.")]
    public float speedMultiplier = 1f;

    // ── Attaque (AttackUp) ────────────────────────────────────
    [Header("Attaque (AttackUp)")]
    [Tooltip("Flat : valeur fixe | Percent : % de l'attaque actuelle")]
    public ModifierType attackModifier = ModifierType.Flat;
    [Tooltip("Bonus d'attaque ajouté à baseAttackMin et baseAttackMax.")]
    public float attackBonus = 0f;

    // ── Critique (CritChanceUp / CritDamageUp) ────────────────
    [Header("Critique (CritChanceUp / CritDamageUp)")]
    [Tooltip("CritChanceUp : bonus de chance de critique [0..1].\nEx: 0.10 = +10% critique.")]
    [Range(0f, 1f)]
    public float critChanceBonus = 0f;

    [Tooltip("CritDamageUp : bonus de multiplicateur de critique.\nEx: 0.25 = +0.25× (base 1.5 → 1.75).")]
    public float critDamageBonus = 0f;

    // ── Augmentation de stat (Stats) ──────────────────────────
    [Header("Augmentation de stat (Stats)")]
    [Tooltip("Stat à augmenter — utilisé uniquement si buffType = Stats.\nv3.5 : utilise StatModifierType (fusion BuffStatType + DebuffStatType).")]
    public StatModifierType buffStatType = StatModifierType.AttackDamage;
    [Tooltip("Flat : valeur directe | Percent : ratio (0.10 = +10%)")]
    public ModifierType buffModifier = ModifierType.Flat;
    [Tooltip("Valeur du bonus.")]
    public float buffStatValue = 0f;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Calcule le soin instantané selon le MaxHP de la cible.</summary>
    public float GetHealAmount(float targetMaxHP)
        => healModifier == ModifierType.Percent ? targetMaxHP * healAmount : healAmount;

    /// <summary>Calcule le soin/s selon le MaxHP de la cible.</summary>
    public float GetHealPerSecond(float targetMaxHP)
        => hotModifier == ModifierType.Percent ? targetMaxHP * healPerSecond : healPerSecond;

    /// <summary>Calcule le montant du bouclier selon le MaxHP de la cible.</summary>
    public float GetShieldAmount(float targetMaxHP)
        => shieldModifier == ModifierType.Percent ? targetMaxHP * shieldAmount : shieldAmount;

    public override StatusEffectInstance CreateInstance(Entity source)
        => new BuffInstance(this, source);
}
