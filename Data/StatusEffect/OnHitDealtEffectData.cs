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
    DamageAmpOnHit   = 0,
    LifestealOnHit   = 1,
    ManaOnHit        = 2,
    ApplyDebuffOnHit = 3,
    ApplyBuffOnHit   = 4,
}

[CreateAssetMenu(fileName = "NewOnHitDealt", menuName = "AetherTree/StatusEffects/OnHitDealtEffectData")]
public class OnHitDealtEffectData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"ohd_\"\n" +
             "(ex: \"ohd_lifesteal_arme\"). Ne JAMAIS afficher au joueur — voir effectName pour l'affichage.")]
    public string effectID;
    [Tooltip("Nom affiché au joueur (fr/en).")]
    public LocalizedText effectName = new LocalizedText();
    [Tooltip("Description affichée au joueur (fr/en) — tooltip équipement/permanent.")]
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    [Header("Type")]
    public OnHitDealtEffectType effectType = OnHitDealtEffectType.LifestealOnHit;

    // Pas de champ "chance" ici — la probabilité de déclenchement se règle UNIQUEMENT sur
    // l'Entry (équipement/permanent/MobData/PNJData), voir OnHitDealtEffectEntry.chance.
    // Avant, un champ existait ici EN PLUS de l'override sur l'Entry — deux endroits pour
    // régler la même chose, aucun double-jet réel (un seul Random.value via EffectiveChance),
    // mais confus pour le designer. Retiré (2026-09-06, demande Florian).

    // ── DamageAmpOnHit ────────────────────────────────────────
    [Tooltip("% de dégâts en plus sur CE coup. Ex: 0.10 = +10%. Roulé dans CombatSystem, " +
             "au même stage que le critique — voir CalculateDamage/CalculateMobDamage.")]
    [Min(0f)]
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

    /// <summary>Calcule le montant de vol de vie selon les dégâts infligés ce coup.</summary>
    public float GetLifestealAmount(float damageDealt)
        => lifestealModifier == ModifierType.Percent ? damageDealt * lifestealAmount : lifestealAmount;

    /// <summary>Calcule le montant de mana restauré selon le MaxMana de l'attaquant.</summary>
    public float GetManaAmount(float attackerMaxMana)
        => manaModifier == ModifierType.Percent ? attackerMaxMana * manaAmount : manaAmount;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(effectID))
            effectID = name;
    }
#endif
}

// =============================================================
// OnHitDealtEffectEntry — une ligne dans la liste d'un équipement/permanent/mob/pnj
// =============================================================
[System.Serializable]
public class OnHitDealtEffectEntry
{
    [Tooltip("SO de l'effet On-Hit infligé à appliquer.")]
    public OnHitDealtEffectData effect;

    [Tooltip("Probabilité de déclenchement par coup infligé [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    /// <summary>Roll si l'effet se déclenche ce coup.</summary>
    public bool Roll() => effect != null && Random.value < chance;
}
