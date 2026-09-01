using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PassiveSkillData — ScriptableObject de passive procédurale
// Path : Assets/Scripts/Data/Skills/PassiveSkillData.cs
// AetherTree GDD v30
//
// Passives conditionnelles — s'activent sur des événements de combat.
// Différent de PermanentSkillData (bonus stats statiques) :
//   PermanentSkillData → appliqué une fois dans RecalculateStats()
//   PassiveSkillData   → proc à runtime sur TriggerType + conditions
//
// Architecture :
//   1 PassiveSkillData = 1 trigger + List<PassiveEffect>
//   Plusieurs effets se déclenchent simultanément.
//
// Exemples de configuration (Buff/Debuff/Skill, voir PassiveEffectType) :
//   "Dernier Rempart"  OnFatalHit (100%)     → Buff(Invincible 5s) + Skill(push AoE_Self)
//   "Résurrection"     OnFatalHit (10%)      → Buff(Revive 50% HP)        (oncePerCombat)
//   "Onde de Choc"     OnTakeDamage% ≥ 20%  → Skill(push AoE_Self) + Debuff(Slow)
//   "Rage"             OnLowHP ≤ 20%        → Buff(Stats AttackDamage)
//   "Vengeur"          OnKill               → Buff(Stats MoveSpeed 3s)    (cooldown 5s)
//   "Oeil de Faucon"   OnCritical           → Buff(Heal 5% MaxHP)
//   "Résonance Combo"  OnCombo              → Buff(Shield 200)
//
// Assets > Create > AetherTree > Skills > PassiveSkillData
// =============================================================

// ── Conditions de déclenchement ───────────────────────────────
public enum PassiveTriggerType
{
    // ── Dégâts reçus ──────────────────────────────────────────
    [InspectorName("OnFatalHit — Coup qui aurait tué le joueur")]
    OnFatalHit,

    [InspectorName("OnLowHP — HP joueur ≤ seuil % (réévalué après chaque coup)")]
    OnLowHP,

    [InspectorName("OnTakeDamagePercent — Reçoit ≥ X% de son MaxHP en un seul coup")]
    OnTakeDamagePercent,

    // ── Dégâts infligés ───────────────────────────────────────
    [InspectorName("OnKill — Tuer un ennemi")]
    OnKill,

    [InspectorName("OnCritical — Infliger un coup critique")]
    OnCritical,

    // ── Skills ────────────────────────────────────────────────
    [InspectorName("OnCast — Utiliser un skill (spécifique ou n'importe lequel)")]
    OnCast,

    [InspectorName("OnCombo — Utiliser un skill avec 2+ éléments (combo élémentaire)")]
    OnCombo,

    // ── Temps ─────────────────────────────────────────────────
    [InspectorName("OnInterval — Se redéclenche automatiquement toutes les `cooldown` secondes")]
    OnInterval,
}

// ── Types d'effets disponibles ────────────────────────────────
// Réduit à 3 catégories (2026) — Heal/Shield/Invincible/Revive sont déjà
// des BuffType (Buff), Push/Damage sont déjà des mécaniques de SkillData
// (Skill) — pas besoin de les réimplémenter à la main ici. Buff/Debuff/Skill
// couvrent tout ce qu'une passive peut faire.
public enum PassiveEffectType
{
    [InspectorName("Buff — Applique un BuffData sur soi (Heal, Shield, Revive, Invincible...)")]
    Buff,

    [InspectorName("Debuff — Applique un DebuffData aux ennemis proches")]
    Debuff,

    [InspectorName("Skill — Lance un SkillData (dégâts, push, zone... tout ce qu'un skill fait déjà)")]
    Skill,
}

// =============================================================
// PASSIVE EFFECT — un effet sérialisable (ligne dans la liste)
// Seuls les champs pertinents au effectType choisi sont utilisés.
// =============================================================
[System.Serializable]
public class PassiveEffect
{
    [Tooltip("Type d'effet à appliquer quand la passive se déclenche.")]
    public PassiveEffectType effectType = PassiveEffectType.Buff;

    // ── Buff ──────────────────────────────────────────────────
    [Tooltip("Buff appliqué sur soi — le BuffData choisit lui-même son comportement\n" +
             "(buffType = Heal, Shield, Revive, Invincible, Stats...).\n" +
             "Utilisé par : Buff.")]
    [ShowIf(nameof(effectType), PassiveEffectType.Buff, Header = "Buff")]
    public BuffData buffToApply;

    // ── Debuff ────────────────────────────────────────────────
    [Tooltip("Debuff à appliquer aux ennemis proches.\n" +
             "Utilisé par : Debuff.")]
    [ShowIf(nameof(effectType), PassiveEffectType.Debuff, Header = "Debuff")]
    public DebuffData debuffToApply;

    [Tooltip("Rayon en unités world autour du joueur.\n" +
             "Utilisé par : Debuff.")]
    [Min(0f)]
    [ShowIf(nameof(effectType), PassiveEffectType.Debuff)]
    public float aoeRadius = 5f;

    // ── Skill ─────────────────────────────────────────────────
    [Tooltip("Skill lancé par le joueur au déclenchement — porte-toi-même vers\n" +
             "targetType = AoE_Self pour un skill centré sur le joueur (dégâts,\n" +
             "push...) ou tout autre type déjà supporté par le système de skill.\n" +
             "Utilisé par : Skill.")]
    [ShowIf(nameof(effectType), PassiveEffectType.Skill, Header = "Skill")]
    public SkillData skillToCast;
}

// =============================================================
// PASSIVE SKILL DATA — le ScriptableObject complet
// =============================================================
[CreateAssetMenu(fileName = "Passive_", menuName = "AetherTree/Skills/PassiveSkillData")]
public class PassiveSkillData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Nom affiché au joueur (fr/en). Ne jamais utiliser dans un log/comparaison —\n" +
             "utiliser this.name (nom d'asset Unity, déjà la clé stable) pour ça.")]
    public LocalizedText skillName  = new LocalizedText();
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    // ── Déclencheur ───────────────────────────────────────────
    [Header("Déclencheur")]
    [Tooltip("Événement de combat qui active cette passive.")]
    public PassiveTriggerType triggerType = PassiveTriggerType.OnFatalHit;

    [Tooltip("Seuil [0..1] pour OnLowHP et OnTakeDamagePercent.\n" +
             "OnLowHP        : se déclenche si HPPercent ≤ seuil.\n" +
             "OnTakeDamage%  : se déclenche si le coup ≥ seuil × MaxHP.\n" +
             "Ignoré pour les autres triggers.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(triggerType), PassiveTriggerType.OnLowHP, PassiveTriggerType.OnTakeDamagePercent)]
    public float triggerThreshold = 0.20f;

    [Tooltip("Skill spécifique requis pour OnCast.\n" +
             "Laisser vide = se déclenche sur n'importe quel skill.")]
    [ShowIf(nameof(triggerType), PassiveTriggerType.OnCast)]
    public SkillData triggerSkill;

    [Tooltip("Chance de déclenchement [0..1] quand la condition est remplie.\n" +
             "1.0 = toujours | 0.10 = 10% de chance.")]
    [Range(0f, 1f)]
    public float procChance = 1f;

    [Tooltip("Cooldown en secondes avant que cette passive puisse se déclencher à nouveau.\n" +
             "0 = pas de cooldown.\n\n" +
             "Pour OnInterval : c'est CE champ qui sert d'intervalle (\"toutes les X sec\") —\n" +
             "0 n'a pas de sens ici (redéclencherait chaque frame), garder > 0.")]
    [Min(0f)]
    public float cooldown = 60f;

    [Tooltip("Si true : ne peut se déclencher qu'une seule fois par combat.\n" +
             "Réinitialisé automatiquement à chaque Revive() du joueur.")]
    public bool oncePerCombat = false;

    // ── Effets ────────────────────────────────────────────────
    [Header("Effets (tous appliqués simultanément au déclenchement)")]
    [Tooltip("Tous les effets de cette liste se déclenchent en même temps.\n\n" +
             "Ex: [InvincibleSelf 3s] + [PushEnemiesAround r=5]\n" +
             "→ les deux s'appliquent d'un coup au déclenchement.")]
    public List<PassiveEffect> effects = new List<PassiveEffect>();

    // ── Helper ────────────────────────────────────────────────
    /// <summary>True si le roll de chance réussit.</summary>
    public bool RollProc() => Random.value <= procChance;
}
