using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SKILLDATA — ScriptableObject définissant un sort
// Path : Assets/Scripts/Data/Skills/SkillData.cs
// AetherTree GDD v3.5 — Section 4
//
// Ordre Inspector :
//   ① Identité      — nom, description, tags, skillType, icon, vfx, son
//   ② Compatibilité arme
//   ③ Effet principal — effectType, damageMultiplier, cooldown, castTime
//   ④ Coût           — mana, HP, gold
//   ⑤ Ciblage & Portée
//   ⑥ Éléments & Multiplicateur élémentaire
//   ⑦ Effets secondaires (StatusEffects SO)
//   ⑨ Visuel & Son
//
// Calcul des dégâts (GDD §6.2) :
//   Physique  — baseDamage * damageMultiplier * ratio → réduit par défense
//     mRed = (dmg*meleeRatio)²  / (dmg*meleeRatio  + def * 1.5)
//     rRed = (dmg*rangedRatio)² / (dmg*rangedRatio + def * 1.5)
//     gRed = (dmg*magicRatio)²  / (dmg*magicRatio  + def * 1.5)
//     physDamage = mRed + rRed + gRed
//
//   Élémentaire — indépendant de l'arme, basé sur les elemPoints du joueur
//     elemDamage = elemPoints * elementalMultiplier
//     elemDamage *= (1 - résistance effective)
//
//   Total = physDamage + elemDamage
//
// Ratios de dégâts (damageMeleeRatio + damageRangedRatio + damageMagicRatio = 1.0) :
//   Ex: skill mêlée pur      → Melee 1.0 / Ranged 0.0 / Magic 0.0
//   Ex: skill hybride         → Melee 0.7 / Ranged 0.0 / Magic 0.3
//   Ex: projectile magique    → Melee 0.0 / Ranged 0.3 / Magic 0.7
//
// ⚠ Les skills permanents (bonus stats définitifs) utilisent PermanentSkillData,
//   un SO séparé — pas SkillData. SkillType.Permanent a été supprimé.
// =============================================================

[CreateAssetMenu(fileName = "skl_", menuName = "AetherTree/Skills/SkillData")]
public class SkillData : ScriptableObject
{
    // ── ① Identité ────────────────────────────────────────────
    [Header("① Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"skl_\"\n" +
             "(ex: \"skl_shortsword_combo3\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string           skillID;
    [Tooltip("Nom affiché au joueur (fr/en). Ne jamais utiliser dans un log/comparaison —\n" +
             "utiliser le nom d'asset Unity (this.name, déjà la clé stable utilisée par\n" +
             "SaveSystem.FindSOByName) pour ça.")]
    public LocalizedText   skillName   = new LocalizedText();
    [Tooltip("Description affichée au joueur (fr/en).")]
    public LocalizedText   description = new LocalizedText();
    public List<SkillTag> tags        = new List<SkillTag>();
    public SkillType      skillType   = SkillType.Active;

    // ── ② Compatibilité arme ──────────────────────────────────
    [Header("② Compatibilité arme")]
    [Tooltip("Types d'armes compatibles — vide = universel")]
    public List<WeaponType> compatibleWeapons = new List<WeaponType>();

    // ── ③ Effet principal ─────────────────────────────────────
    [Header("③ Effet principal")]
    [Tooltip("Effet principal du skill.\n" +
             "Damage → inflige des dégâts physiques + élémentaires\n" +
             "Buff   → applique uniquement des buffs (via StatusEffects)\n" +
             "Debuff → applique uniquement des debuffs (via StatusEffects)\n" +
             "Other  → effet spécial (drain, téléport, invocation...)")]
    public SkillEffectType effectType       = SkillEffectType.Damage;

    [Tooltip("Multiplicateur global sur les dégâts physiques.\n" +
             "Ex: 1.0 = dégâts normaux | 2.0 = double dégâts physiques")]
    public float           damageMultiplier = 1f;
    public float           cooldown         = 1f;
    public float           castTime         = 0f;

    // ── Effet spécial (si effectType == Other) ────────────────
    [Tooltip("Effet spécial appliqué par ce skill.\nActif uniquement si effectType = Other.")]
    [ShowIf(nameof(effectType), SkillEffectType.Other, Header = "③ Effet spécial (si effectType = Other)")]
    public SkillSpecialEffect specialEffect = SkillSpecialEffect.None;

    [Tooltip("Force du déplacement pour Pull/Push/PullAoE/PushAoE/GatherAoE/Vortex.\nDistance en unités world.")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Pull, SkillSpecialEffect.Push, SkillSpecialEffect.PullAoE, SkillSpecialEffect.PushAoE, SkillSpecialEffect.GatherAoE, SkillSpecialEffect.Vortex)]
    public float pullPushForce = 5f;

    [Tooltip("Ratio des dégâts restitués en soin (DrainHP). Ex: 0.5 = 50% des dégâts soignés.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.DrainHP)]
    public float drainHealRatio = 0.5f;

    [Tooltip("MobData à invoquer (Summon uniquement).")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Summon)]
    public MobData summonMobData;

    [Tooltip("Durée de vie de l'invocation en secondes. 0 = permanent jusqu'à la mort.")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Summon)]
    public float summonDuration = 30f;

    // ── ③ Ratios de dégâts physiques ──────────────────────────
    [Header("③ Ratios de dégâts physiques (somme doit = 1.0)")]
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

    [Tooltip("Qui est touché par les effets de zone (AoE/multi-cibles).\n" +
             "Enemies  → seulement les ennemis du caster (dégâts classiques)\n" +
             "Allies   → seulement les alliés du caster (soin/buff de groupe, inclut le caster\n" +
             "           lui-même s'il est dans la zone — ex: Purify de zone)\n" +
             "Everyone → tout le monde dans la zone, sans distinction")]
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Direction, TargetType.Skillshot, TargetType.LineTarget)]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;

    // ── ⑥ Éléments ────────────────────────────────────────────
    [Header("⑥ Éléments")]
    [Tooltip("Vide = Neutre pur (pas de dégâts élémentaires)\n" +
             "1 élément = skill élémentaire\n" +
             "2+ éléments = skill combo élémentaire")]
    public List<ElementType> elements = new List<ElementType>();

    [Range(0f, 5f)]
    [Tooltip("Multiplicateur élémentaire appliqué sur les elemPoints du joueur. GDD §6.2.\n" +
             "0   = pas de dégâts élémentaires (skill purement physique)\n" +
             "1.0 = elemPoints × 1.0  (ex: 300 pts → 300 dégâts elem)\n" +
             "2.5 = elemPoints × 2.5  (ex: 300 pts → 750 dégâts elem)\n" +
             "5.0 = skill burst élémentaire maximum\n" +
             "⚠ Ignoré automatiquement si elements est vide (skill Neutre)")]
    public float elementalMultiplier = 1f;

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
    [ShowIf(nameof(executionType), SkillExecutionType.MultiHit)]
    public List<HitStep>   hitSteps  = new List<HitStep>();

    [Tooltip("ComboSequence uniquement — liste des SkillData steps dans l'ordre.\n" +
             "Chaque step est un skill complet avec ses propres stats et icône.")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public List<SkillData> comboSteps = new List<SkillData>();

    [Tooltip("ComboSequence uniquement — durée en secondes pendant laquelle\n" +
             "le joueur peut appuyer pour continuer le combo après chaque step.\n" +
             "Expiration → CD déclenché + retour au step 0.")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public float comboWindowDuration = 2f;

    // ── ⑩ Visuel & Son ────────────────────────────────────────
    [Header("⑩ Visuel & Son")]
    public Sprite     icon;
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;

    [Tooltip("Animation jouée par le caster à l'exécution du skill (PlayerAnimatorController.PlayAttack).\n" +
             "Vide = pas d'animation dédiée (ex: buff pur, effet purement passif).\n" +
             "⚠ Donnée visuelle — si SpellData est un jour séparé en gameplay/visuel pour le\n" +
             "serveur-autoritaire (voir a implémenter/AetherTree_Recap_Reseau+refactor.md §Priorité 1),\n" +
             "ce champ part du côté visuel, pas gameplay.")]
    public AnimationClip attackAnimation;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>True si le skill n'a aucun élément — pas de dégâts élémentaires.</summary>
    public bool IsNeutral => elements == null || elements.Count == 0;

    /// <summary>True si le skill a 2 éléments ou plus (combo élémentaire).</summary>
    public bool IsCombo => elements != null && elements.Count >= 2;

    /// <summary>Élément principal du skill. Neutral si aucun élément défini.</summary>
    public ElementType PrimaryElement => IsNeutral ? ElementType.Neutral : elements[0];

    /// <summary>
    /// Multiplicateur élémentaire effectif.
    /// Retourne 0 si le skill est Neutre — éléments vides = pas de dégâts élémentaires.
    /// </summary>
    public float EffectiveElementalMultiplier => IsNeutral ? 0f : elementalMultiplier;

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
    /// Appelé par CombatSystem — GDD §6.2.
    /// </summary>
    public float GetWeightedDefense(float meleeDefense, float rangedDefense, float magicDefense)
        => meleeDefense  * damageMeleeRatio
         + rangedDefense * damageRangedRatio
         + magicDefense  * damageMagicRatio;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(skillID))
            skillID = name;

        // MultiHit : SkillBar.LockForMultiHit() bloque l'auto-attaque pendant
        // somme(hitSteps.delay) + 0.3s — si attackAnimation dure sensiblement plus
        // longtemps, l'auto-attaque reprend la main avant la fin de l'anim et écrase
        // le state Attack partagé en plein milieu (bug vécu — voir historique du projet).
        // Avertissement plutôt que correction automatique : c'est aux delays ou à
        // l'anim d'être ajustés, pas au lock de compenser en silence.
        if (executionType == SkillExecutionType.MultiHit && attackAnimation != null
            && hitSteps != null && hitSteps.Count > 0)
        {
            float totalDelay = 0f;
            foreach (var step in hitSteps)
                totalDelay += step.delay;
            totalDelay += 0.3f;

            float animLength = attackAnimation.length;
            if (animLength - totalDelay > 0.15f)
            {
                Debug.LogWarning($"[SkillData:{name}] attackAnimation ({animLength:F2}s) dure " +
                                  $"{(animLength - totalDelay):F2}s de plus que le lock MultiHit " +
                                  $"({totalDelay:F2}s, somme des hitSteps.delay + marge) — l'auto-attaque " +
                                  $"va reprendre la main avant la fin de l'animation. Allonge les delays " +
                                  $"des hitSteps, ou raccourcis/retime l'animation.", this);
            }
        }
    }
#endif
}

// =============================================================
// ENUMS
// =============================================================

/// <summary>
/// Type de skill — détermine l'onglet dans SkillLibraryUI et les règles d'équipement.
/// ⚠ Permanent supprimé — les passifs définitifs utilisent PermanentSkillData (SO séparé).
/// ⚠ PassiveUtility supprimé — vestige jamais fini (aucun moteur runtime), remplacé
/// intégralement par PassiveSkillData (SO séparé, slots P1/P2/P3 PassifBar, GDD §7.5).
/// </summary>
public enum SkillType
{
    BasicAttack,     // Attaque de base — slot 0 SkillBar uniquement
    Active,          // Sort actif — slots 1-8 SkillBar
    Ultimate,        // Ultime — slot 9 SkillBar
}

public enum SkillEffectType
{
    Damage,  // Inflige des dégâts physiques + élémentaires
    Buff,    // Applique uniquement des buffs (via StatusEffects SO)
    Debuff,  // Applique uniquement des debuffs (via StatusEffects SO)
    Other,   // Effet spécial (drain, téléport, invocation, dash...)
}

public enum ModifierType  { Flat, Percent }

public enum TargetType
{
    Target, Self, AoE_Self, AoE_Target, Skillshot,
    LineTarget, GroundTarget, Cone, Direction, Dash_Target, Dash_Direction
}

/// <summary>Qui est touché par un effet de zone/multi-cibles — voir SkillSystem.IsAlly.</summary>
public enum SkillAoeFaction
{
    Enemies,    // Ne touche que les ennemis du caster (comportement historique implicite)
    Allies,     // Ne touche que les alliés du caster (inclut le caster s'il est dans la zone)
    Everyone,   // Touche tout le monde dans la zone, sans distinction
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

    [Header("Dégâts physiques")]
    public float damageMultiplier  = 1f;
    [Range(0f, 1f)] public float damageMeleeRatio  = 1f;
    [Range(0f, 1f)] public float damageRangedRatio = 0f;
    [Range(0f, 1f)] public float damageMagicRatio  = 0f;

    [Header("Élémentaire")]
    [Tooltip("Élément de ce hit. Neutral = pas de dégâts élémentaires sur ce hit.")]
    public ElementType element             = ElementType.Neutral;

    [Range(0f, 5f)]
    [Tooltip("Multiplicateur élémentaire de ce hit.\n" +
             "0 = pas de dégâts élémentaires | 1.0 = elemPoints × 1.0\n" +
             "⚠ Ignoré si element = Neutral")]
    public float       elementalMultiplier = 0f;

    [Header("Effets de statut — appliqués sur ce hit uniquement")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("VFX / Son (optionnel — utilise celui du skill parent si vide)")]
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;

    /// <summary>Multiplicateur élémentaire effectif — 0 si élément Neutral.</summary>
    public float EffectiveElementalMultiplier
        => element == ElementType.Neutral ? 0f : elementalMultiplier;
}
