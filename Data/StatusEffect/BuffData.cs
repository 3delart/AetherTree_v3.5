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

[CreateAssetMenu(fileName = "buff_", menuName = "AetherTree/StatusEffects/BuffData")]
public class BuffData : StatusEffectData
{
    [Header("Type de buff")]
    public BuffType buffType;

    // ── Soin instantané (Heal) ────────────────────────────────
    [Tooltip("Flat : valeur fixe (ex: 1000 HP)\nPercent : % du MaxHP de la cible (ex: 0.10 = 10%)")]
    [ShowIf(nameof(buffType), BuffType.Heal, Header = "Soin instantané (Heal)")]
    public ModifierType healModifier = ModifierType.Flat;
    [Tooltip("Montant de soin instantané.")]
    [ShowIf(nameof(buffType), BuffType.Heal)]
    public float healAmount = 0f;

    // ── Soin sur la durée (Regeneration) ─────────────────────
    [Tooltip("Flat : valeur fixe par seconde\nPercent : % du MaxHP de la cible par seconde")]
    [ShowIf(nameof(buffType), BuffType.Regeneration, Header = "Soin sur la durée (Regeneration)")]
    public ModifierType hotModifier = ModifierType.Flat;
    [Tooltip("Soin par seconde — tick géré dans BuffInstance.Tick().")]
    [ShowIf(nameof(buffType), BuffType.Regeneration)]
    public float healPerSecond = 0f;

    // ── Bouclier (Shield) ─────────────────────────────────────
    [Tooltip("Flat : montant fixe absorbé\nPercent : % du MaxHP de la cible")]
    [ShowIf(nameof(buffType), BuffType.Shield, Header = "Bouclier (Shield)")]
    public ModifierType shieldModifier = ModifierType.Flat;
    [Tooltip("Montant de dégâts absorbés.")]
    [ShowIf(nameof(buffType), BuffType.Shield)]
    public float shieldAmount = 0f;

    // ── Augmentation de stat (Stats) ──────────────────────────
    // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp
    // retirés (2026) — redondants avec Stats, voir StatusEffectData.BuffType.
    [Tooltip("Stat à augmenter — utilisé uniquement si buffType = Stats.\nv3.5 : utilise StatModifierType (fusion BuffStatType + DebuffStatType).")]
    [ShowIf(nameof(buffType), BuffType.Stats, Header = "Augmentation de stat (Stats)")]
    public StatModifierType buffStatType = StatModifierType.AttackDamage;
    [Tooltip("Flat : valeur directe | Percent : ratio (0.10 = +10%)")]
    [ShowIf(nameof(buffType), BuffType.Stats)]
    public ModifierType buffModifier = ModifierType.Flat;
    [Tooltip("Valeur du bonus.")]
    [ShowIf(nameof(buffType), BuffType.Stats)]
    public float buffStatValue = 0f;

    // ── Résurrection (Revive — Player uniquement) ─────────────
    [Tooltip("HP restaurés à la résurrection (ratio du MaxHP). Ex: 0.50 = 50% HP.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(buffType), BuffType.Revive, Header = "Résurrection (Revive)")]
    public float reviveHPPercent = 0.50f;

    [Tooltip("Mana restaurée à la résurrection (ratio du MaxMana).")]
    [Range(0f, 1f)]
    [ShowIf(nameof(buffType), BuffType.Revive)]
    public float reviveManaPercent = 0.30f;

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
