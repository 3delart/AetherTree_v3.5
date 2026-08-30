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
// Exemples de configuration :
//   "Dernier Rempart"  OnFatalHit (100%)     → InvincibleSelf 5s + PushEnemiesAround r=5
//   "Résurrection"     OnFatalHit (10%)      → ReviveSelf 50% HP          (oncePerCombat)
//   "Onde de Choc"     OnTakeDamage% ≥ 20%  → PushEnemiesAround + DebuffEnemiesAround Slow
//   "Rage"             OnLowHP ≤ 20%        → BuffSelf(Haste) + BuffSelf(AttackUp)
//   "Vengeur"          OnKill               → BuffSelf(Haste 3s)          (cooldown 5s)
//   "Oeil de Faucon"   OnCritical           → HealSelf 5% MaxHP
//   "Résonance Combo"  OnCombo              → ShieldSelf 200
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
}

// ── Types d'effets disponibles ────────────────────────────────
public enum PassiveEffectType
{
    // ── Sur soi ───────────────────────────────────────────────
    [InspectorName("BuffSelf — Applique un BuffData sur soi (Haste, AttackUp...)")]
    BuffSelf,

    [InspectorName("HealSelf — Soin instantané (flat ou % MaxHP)")]
    HealSelf,

    [InspectorName("ShieldSelf — Bouclier absorbant (flat ou % MaxHP)")]
    ShieldSelf,

    [InspectorName("InvincibleSelf — Immunité totale X secondes")]
    InvincibleSelf,

    [InspectorName("ReviveSelf — Résurrection instantanée à X% HP")]
    ReviveSelf,

    // ── Sur ennemis proches ───────────────────────────────────
    [InspectorName("PushEnemiesAround — Repousse les ennemis dans un rayon")]
    PushEnemiesAround,

    [InspectorName("DebuffEnemiesAround — Applique un DebuffData aux ennemis proches")]
    DebuffEnemiesAround,

    [InspectorName("DamageEnemiesAround — Inflige des dégâts aux ennemis proches")]
    DamageEnemiesAround,
}

// =============================================================
// PASSIVE EFFECT — un effet sérialisable (ligne dans la liste)
// Seuls les champs pertinents au effectType choisi sont utilisés.
// =============================================================
[System.Serializable]
public class PassiveEffect
{
    [Tooltip("Type d'effet à appliquer quand la passive se déclenche.")]
    public PassiveEffectType effectType = PassiveEffectType.BuffSelf;

    // ── BuffSelf ──────────────────────────────────────────────
    [Header("BuffSelf")]
    [Tooltip("Buff appliqué sur soi.\n" +
             "Utilisé par : BuffSelf.")]
    public BuffData buffToApply;

    // ── HealSelf / ShieldSelf ─────────────────────────────────
    [Header("HealSelf / ShieldSelf")]
    [Tooltip("Flat : valeur fixe | Percent : ratio du MaxHP.\n" +
             "Utilisé par : HealSelf, ShieldSelf.")]
    public ModifierType valueModifier = ModifierType.Percent;

    [Tooltip("Montant de soin ou de shield.\n" +
             "Percent : 0.20 = 20% MaxHP | Flat : 500 = 500 pts fixes.\n" +
             "Utilisé par : HealSelf, ShieldSelf.")]
    [Min(0f)]
    public float value = 0.20f;

    // ── InvincibleSelf ────────────────────────────────────────
    [Header("InvincibleSelf")]
    [Tooltip("Durée de l'invincibilité en secondes.\n" +
             "Utilisé par : InvincibleSelf.")]
    [Min(0f)]
    public float invincibleDuration = 3f;

    // ── ReviveSelf ────────────────────────────────────────────
    [Header("ReviveSelf")]
    [Tooltip("HP restaurés à la résurrection (ratio du MaxHP).\n" +
             "Ex: 0.50 = 50% HP.\nUtilisé par : ReviveSelf.")]
    [Range(0f, 1f)]
    public float reviveHPPercent = 0.50f;

    // ── Zone — Push / Debuff / Damage ─────────────────────────
    [Header("Zone (Push / Debuff / Damage)")]
    [Tooltip("Rayon en unités world autour du joueur.\n" +
             "Utilisé par : PushEnemiesAround, DebuffEnemiesAround, DamageEnemiesAround.")]
    [Min(0f)]
    public float aoeRadius = 5f;

    [Tooltip("Distance de recul en unités world.\n" +
             "Utilisé par : PushEnemiesAround.")]
    [Min(0f)]
    public float pushForce = 4f;

    [Tooltip("Debuff à appliquer aux ennemis proches.\n" +
             "Utilisé par : DebuffEnemiesAround.")]
    public DebuffData debuffToApply;

    [Tooltip("Multiplicateur de dégâts (basé sur l'attaque de base du joueur).\n" +
             "Ex: 0.5 = 50% de l'attaque de base.\n" +
             "Utilisé par : DamageEnemiesAround.")]
    [Min(0f)]
    public float damageMultiplier = 0.5f;

    [Tooltip("Élément des dégâts AoE.\n" +
             "Utilisé par : DamageEnemiesAround.")]
    public ElementType damageElement = ElementType.Neutral;

    // ── Helper ────────────────────────────────────────────────
    /// <summary>Valeur finale pour HealSelf / ShieldSelf selon le MaxHP.</summary>
    public float GetFinalValue(float maxHP)
        => valueModifier == ModifierType.Percent ? maxHP * value : value;
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
    public float triggerThreshold = 0.20f;

    [Tooltip("Skill spécifique requis pour OnCast.\n" +
             "Laisser vide = se déclenche sur n'importe quel skill.")]
    public SkillData triggerSkill;

    [Tooltip("Chance de déclenchement [0..1] quand la condition est remplie.\n" +
             "1.0 = toujours | 0.10 = 10% de chance.")]
    [Range(0f, 1f)]
    public float procChance = 1f;

    [Tooltip("Cooldown en secondes avant que cette passive puisse se déclencher à nouveau.\n" +
             "0 = pas de cooldown.")]
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
