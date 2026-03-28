using UnityEngine;

// =============================================================
// DebuffData — ScriptableObject template de debuff
// Path : Assets/Scripts/Data/StatusEffect/DebuffData.cs
// AetherTree GDD v3.5 — §3.1.1.1
//
// v3.5 — DebuffStatType remplacé par StatModifierType (fusion avec BuffStatType)
//
// Assets > Create > AetherTree > StatusEffects > DebuffData
// =============================================================

[CreateAssetMenu(fileName = "NewDebuff", menuName = "AetherTree/StatusEffects/DebuffData")]
public class DebuffData : StatusEffectData
{
    [Header("Type de debuff")]
    public DebuffType debuffType;

    // ── Dégâts sur la durée (Burn, Poison, Bleed) ─────────────
    [Header("Dégâts sur la durée (Burn, Poison, Bleed)")]
    [Tooltip("Dégâts par seconde.")]
    public float damagePerSecond = 0f;

    [Tooltip("Élément des dégâts.\nBurn → Fire | Poison → Nature | Bleed → Neutral")]
    public ElementType damageElement = ElementType.Neutral;

    // ── Ralentissement (Freeze, Slow) ─────────────────────────
    [Header("Ralentissement (Freeze, Slow)")]
    [Tooltip("Multiplicateur de vitesse [0..1].\n0 = immobilisé | 0.5 = 50% vitesse | 1 = aucun effet\nFreeze : toujours 0 — immobilisation totale (§3.1.1.1).")]
    [Range(0f, 1f)]
    public float slowMultiplier = 0f;

    // ── Réduction de soins (Poison) ───────────────────────────
    [Header("Réduction de soins reçus (Poison)")]
    [Tooltip("Poison (§3.1.1.1) : réduit les soins reçus par la cible.\n0 = aucune réduction | 0.3 = -30% soins reçus")]
    [Range(0f, 1f)]
    public float healReduction = 0f;

    // ── Réduction de défense (ArmorBreak, Shocked) ────────────
    [Header("Réduction de défense (ArmorBreak, Shocked)")]
    [Tooltip("ArmorBreak : réduction flat appliquée aux 3 types de défense.\nShocked : même champ — utilisé pour le mini-stun via CombatSystem.")]
    public float defenseReduction = 0f;

    // ── Drain de mana (ManaDrain) ─────────────────────────────
    [Header("Drain de mana (ManaDrain)")]
    [Tooltip("ManaDrain (§3.1.1.1) : mana drainé par seconde.\nRéutilise damagePerSecond pour le tick — ce champ est un alias lisible.")]
    public float manaDrainPerSecond = 0f;

    // ── Réduction de stat (Stats) ─────────────────────────────
    [Header("Réduction de stat (Stats)")]
    [Tooltip("Stat à réduire — utilisé uniquement si debuffType = Stats.\nv3.5 : utilise StatModifierType (fusion BuffStatType + DebuffStatType).")]
    public StatModifierType debuffStatType = StatModifierType.MoveSpeed;

    [Tooltip("Type de modificateur — Flat ou Percent.")]
    public ModifierType debuffModifier = ModifierType.Percent;

    [Tooltip("Valeur de réduction.\nFlat : valeur directe | Percent : ratio (0.10 = -10%)")]
    public float debuffValue = 0f;

    public override StatusEffectInstance CreateInstance(Entity source)
        => new DebuffInstance(this, source);
}
