using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SKILLDATA — ScriptableObject définissant un sort
// Path : Assets/Scripts/Data/Skills/SkillData.cs
// AetherTree GDD v30 — Section 4
//
// Ordre Inspector :
//   ① Identité      — nom, description, tags, skillType, icon, vfx, son
//   ② Compatibilité arme
//   ③ Effet principal — effectType, damageMultiplier, cooldown, castTime
//   ④ Coût           — mana, HP, gold
//   ⑤ Ciblage & Portée
//   ⑥ Éléments & Ratios de dégâts
//   ⑦ Effets secondaires (StatusEffects SO)
//   ⑨ Visuel & Son
//
// Ratios de dégâts (damageMeleeRatio + damageRangedRatio + damageMagicRatio = 1.0) :
//   Ex: skill mêlée pur      → Melee 1.0 / Ranged 0.0 / Magic 0.0
//   Ex: skill hybride         → Melee 0.7 / Ranged 0.0 / Magic 0.3
//   Ex: projectile magique    → Melee 0.0 / Ranged 0.3 / Magic 0.7
// CombatSystem lit ces ratios pour appliquer la défense pondérée de la cible.
//
// ⚠ Les skills permanents (bonus stats définitifs) utilisent PermanentSkillData,
//   un SO séparé — pas SkillData. SkillType.Permanent a été supprimé.
// =============================================================

[CreateAssetMenu(fileName = "NewSkill", menuName = "AetherTree/Skills/SkillData")]
public class SkillData : ScriptableObject
{
    // ── ① Identité ────────────────────────────────────────────
    [Header("① Identité")]
    public string         skillName   = "SkillName";
    [TextArea]
    public string         description = "";
    public List<SkillTag> tags        = new List<SkillTag>();
    public SkillType      skillType   = SkillType.Active;

    // ── ② Compatibilité arme ──────────────────────────────────
    [Header("② Compatibilité arme")]
    [Tooltip("Types d'armes compatibles — vide = universel")]
    public List<WeaponType> compatibleWeapons = new List<WeaponType>();

    // ── ③ Effet principal ─────────────────────────────────────
    [Header("③ Effet principal")]
    [Tooltip("Effet principal du skill.\n" +
             "Damage → inflige des dégâts (damageMultiplier × arme)\n" +
             "Buff   → applique uniquement des buffs (via StatusEffects)\n" +
             "Debuff → applique uniquement des debuffs (via StatusEffects)\n" +
             "Other  → effet spécial (drain, téléport, invocation...)")]
    public SkillEffectType effectType       = SkillEffectType.Damage;
    public float           damageMultiplier = 1f;
    public float           cooldown         = 1f;
    public float           castTime         = 0f;

    // ── Effet spécial (si effectType == Other) ────────────────
    [Header("③ Effet spécial (si effectType = Other)")]
    [Tooltip("Effet spécial appliqué par ce skill.\nActif uniquement si effectType = Other.")]
    public SkillSpecialEffect specialEffect = SkillSpecialEffect.None;

    [Tooltip("Force du déplacement pour Pull/Push/PullAoE/PushAoE/GatherAoE/Vortex.\nDistance en unités world.")]
    public float pullPushForce = 5f;

    [Tooltip("Ratio des dégâts restitués en soin (DrainHP). Ex: 0.5 = 50% des dégâts soignés.")]
    [Range(0f, 1f)]
    public float drainHealRatio = 0.5f;

    [Tooltip("MobData à invoquer (Summon uniquement).")]
    public MobData summonMobData;

    [Tooltip("Durée de vie de l'invocation en secondes. 0 = permanent jusqu'à la mort.")]
    public float summonDuration = 30f;

    // ── Ratios de dégâts — GDD v30 §4.2 ──────────────────────
    [Header("③ Ratios de dégâts (somme doit = 1.0)")]
    [Tooltip("Part des dégâts réduite par la défense Mêlée de la cible.\n" +
             "Ex: skill mêlée pur → 1.0 | skill hybride → 0.7")]
    [Range(0f, 1f)]
    public float damageMeleeRatio  = 1f;

    [Tooltip("Part des dégâts réduite par la défense Distance de la cible.\n" +
             "Ex: projectile → 1.0 | lancer de lame → 0.5")]
    [Range(0f, 1f)]
    public float damageRangedRatio = 0f;

    [Tooltip("Part des dégâts réduite par la défense Magique de la cible.\n" +
             "Ex: sort pur → 1.0 | skill hybride → 0.3")]
    [Range(0f, 1f)]
    public float damageMagicRatio  = 0f;

    // ── ④ Coût ────────────────────────────────────────────────
    [Header("④ Coût")]
    [Tooltip("Coût en mana.")]
    public float manaCost = 0f;
    [Tooltip("Coût en HP.")]
    public float hpCost   = 0f;
    [Tooltip("Coût en gold.")]
    public int   goldCost = 0;

    // ── ⑤ Ciblage & Portée ────────────────────────────────────
    [Header("⑤ Ciblage & Portée")]
    public TargetType targetType      = TargetType.Target;
    public float      range           = 2.5f;
    public float      aoeRadius       = 0f;
    public float      projectileSpeed = 15f;
    public GameObject projectilePrefab;

    // ── ⑥ Éléments ────────────────────────────────────────────
    [Header("⑥ Éléments")]
    [Tooltip("Vide = Neutre pur | 1 = élémentaire | 2+ = combo élémentaire")]
    public List<ElementType> elements            = new List<ElementType>();
    public float             elementalMultiplier = 1f;
    [Range(0f, 1f)]
    [Tooltip("Part élémentaire des dégâts (0–1). Ex: 0.3 = 30% élém + 70% physique.\n" +
             "Utilisé par CombatSystem pipeline §6.1.")]
    public float             elementalRatio      = 0.3f;

    // ── ⑦ Effets secondaires (StatusEffects SO) ───────────────
    [Header("⑦ Effets secondaires (BuffData / DebuffData + chance)")]
    [Tooltip("Effets déclenchés à l'utilisation du skill.\n" +
             "Glisse un BuffData ou DebuffData + règle la chance.\n\n" +
             "Ex: Skill Damage + Burn 30% → dégâts + chance de brûlure\n" +
             "Ex: Skill Buff pur → glisse un BuffData Haste à 100%\n" +
             "Ex: Skill Debuff pur → glisse un DebuffData Poison à 100%")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    // ── ⑨ Exécution avancée ───────────────────────────────────
    [Header("⑨ Exécution avancée")]
    [Tooltip("Normal        → exécution standard\n" +
             "MultiHit      → une activation, N hits en séquence (hitSteps)\n" +
             "ComboSequence → N appuis successifs sur le même slot (comboSteps)")]
    public SkillExecutionType executionType = SkillExecutionType.Normal;

    [Tooltip("MultiHit uniquement — liste des hits avec leurs stats propres.\n" +
             "Chaque HitStep définit : délai, multiplicateur, élément, effets, VFX.")]
    public List<HitStep>   hitSteps  = new List<HitStep>();

    [Tooltip("ComboSequence uniquement — liste des SkillData steps dans l'ordre.\n" +
             "Chaque step est un skill complet avec ses propres stats et icône.")]
    public List<SkillData> comboSteps = new List<SkillData>();

    [Tooltip("ComboSequence uniquement — durée en secondes pendant laquelle\n" +
             "le joueur peut appuyer pour continuer le combo après chaque step.\n" +
             "Expiration → CD déclenché + retour au step 0.")]
    public float comboWindowDuration = 2f;

    // ── ⑩ Visuel & Son ────────────────────────────────────────
    [Header("⑩ Visuel & Son")]
    public Sprite     icon;
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;

    // ── Helpers ───────────────────────────────────────────────
    public bool        IsNeutral      => elements == null || elements.Count == 0;
    public bool        IsCombo        => elements != null && elements.Count >= 2;
    public ElementType PrimaryElement => IsNeutral ? ElementType.Neutral : elements[0];

    public bool HasElement(ElementType e) => !IsNeutral && elements.Contains(e);
    public bool HasTag(SkillTag t)        => tags != null && tags.Contains(t);

    public string GetElementsLabel()
    {
        if (IsNeutral) return "Neutre";
        return string.Join(" + ", elements);
    }

    public bool IsCompatibleWith(WeaponType weaponType)
    {
        return compatibleWeapons == null || compatibleWeapons.Count == 0
               || compatibleWeapons.Contains(weaponType);
    }

    /// <summary>
    /// Défense pondérée selon les ratios du skill.
    /// Ex: skill (meleeRatio=0.7, magicRatio=0.3) → meleeDefense×0.7 + magicDefense×0.3
    /// Appelé par PlayerStats.GetWeightedDefense — GDD v30 §6.1.
    /// </summary>
    public float GetWeightedDefense(float meleeDefense, float rangedDefense, float magicDefense)
        => meleeDefense * damageMeleeRatio
         + rangedDefense * damageRangedRatio
         + magicDefense * damageMagicRatio;
}

// =============================================================
// ENUMS
// =============================================================

/// <summary>
/// Type de skill — détermine l'onglet dans SkillLibraryUI et les règles d'équipement.
/// ⚠ Permanent supprimé — les passifs définitifs utilisent PermanentSkillData (SO séparé).
/// </summary>
public enum SkillType
{
    BasicAttack,     // Attaque de base — slot 0 SkillBar uniquement
    Active,          // Sort actif — slots 1-8 SkillBar
    Ultimate,        // Ultime — slot 9 SkillBar
    PassiveUtility,  // Passif utilitaire — slots P1/P2/P3 PassifBar
}

public enum SkillEffectType
{
    Damage,  // Inflige des dégâts (damageMultiplier × arme)
    Buff,    // Applique uniquement des buffs (via StatusEffects SO)
    Debuff,  // Applique uniquement des debuffs (via StatusEffects SO)
    Other,   // Effet spécial (drain, téléport, invocation, dash...)
}

public enum ModifierType  { Flat, Percent }
public enum AnimationType { Melee, Ranged, Magic, Special }

public enum TargetType
{
    Target, Self, AoE_Self, AoE_Target, Skillshot,
    LineTarget, GroundTarget, Cone, Direction, Dash_Target, Dash_Direction
}

// =============================================================
// SKILL SPECIAL EFFECTS
// =============================================================
public enum SkillSpecialEffect
{
    None,           // Pas d'effet spécial (valeur par défaut)

    // ── Déplacement cible unique ──────────────────────────────
    Pull,           // Attire la cible vers le caster
    Push,           // Repousse la cible loin du caster
    SwapPosition,   // Échange la position caster ↔ cible

    // ── Déplacement zone ──────────────────────────────────────
    PullAoE,        // Attire toutes les entités de la zone vers le caster
    PushAoE,        // Repousse toutes les entités de la zone
    GatherAoE,      // Regroupe toutes les entités vers le centre de la zone
    Vortex,         // Attire en spirale vers un point (= GatherAoE + Slow)

    // ── Téléportation ─────────────────────────────────────────
    TeleportSelf,   // Téléporte le caster vers la cible / point au sol
    TeleportTarget, // Téléporte la cible vers le caster

    // ── Drain / Transfert ─────────────────────────────────────
    DrainHP,        // Vol de HP : dégâts sur cible → soin caster (drainHealRatio)
    DrainMana,      // Vol de Mana : vide la cible, rend le caster

    // ── Invocation ────────────────────────────────────────────
    Summon,         // Invoque un mob allié (summonMobData) — TODO phase suivante

    // ── Divers ────────────────────────────────────────────────
    Interrupt,      // Annule le cast en cours de la cible — TODO phase suivante
}

public enum SkillTag
{
    Illusion, Invocateur, Berserker, Necromancien, Furtif, Duelliste,
    Soutien, Mobilite, Zone,
    Buff, Debuff, DoT, Bleed, Stun, Root, Knockback, Shield, Drain, Combo,
    BasicAttack, HeavyAttack, RangedAttack, MagicAttack,
}

// =============================================================
// SKILL EXECUTION TYPE
// =============================================================
public enum SkillExecutionType
{
    Normal,         // Comportement standard — aucun changement
    MultiHit,       // Une activation, N hits en séquence (SkillData.hitSteps)
    ComboSequence,  // N appuis successifs sur le même slot (SkillData.comboSteps)
}

// =============================================================
// HIT STEP — un hit dans un MultiHit
// Chaque step a ses propres stats indépendantes du SkillData parent.
// =============================================================
[System.Serializable]
public class HitStep
{
    [Header("Identification")]
    public string stepName = "Hit";

    [Tooltip("Délai avant ce hit en secondes (depuis le hit précédent ou l'activation).")]
    [Min(0f)]
    public float delay = 0.3f;

    [Header("Dégâts")]
    public float damageMultiplier  = 1f;
    [Range(0f, 1f)] public float damageMeleeRatio  = 1f;
    [Range(0f, 1f)] public float damageRangedRatio = 0f;
    [Range(0f, 1f)] public float damageMagicRatio  = 0f;

    [Header("Élémentaire")]
    public ElementType element        = ElementType.Neutral;
    [Range(0f, 1f)]
    public float       elementalRatio = 0f;

    [Header("Effets de statut — appliqués sur ce hit uniquement")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("VFX / Son (optionnel — utilise celui du skill parent si vide)")]
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;
}