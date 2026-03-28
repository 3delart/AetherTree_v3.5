using UnityEngine;
using System.Collections.Generic;

// =============================================================
// StatusEffectData — SO de base pour tous les buffs et debuffs
// Path : Assets/Scripts/Data/StatusEffect/StatusEffectData.cs
// AetherTree GDD v3.5 — §3.1.1
//
// Historique :
//   v3.0 — Silence réattribué à Ténèbres (Wind supprimé)
//           Glace supprimée → Freeze reste, réattribué à Eau
//           Shock renommé ArmorBreak (réduction défense physique)
//           Shocked ajouté séparément (interrupt + mini-stun Foudre)
//           ManaBreak renommé ManaDrain (§3.1.1.1)
//           HealOnTime renommé Regeneration (§3.1.1.2)
//           AttackSpeedUp / MoveSpeedUp fusionnés dans Haste (§3.1.1.2)
//           RangeDefense corrigé en RangedDefense (§3.1)
//           Doublons BuffStatType / DebuffStatType supprimés
//   v3.5 — BuffStatType + DebuffStatType fusionnés en StatModifierType
//           BuffType.RegenHP supprimé (doublon avec Regeneration)
//           BuffType.Taunt supprimé (c'est un DebuffType)
//           CritChanceUp / CritDamageUp séparés dans BuffType
// =============================================================

// ── Enums debuff ──────────────────────────────────────────────
public enum DebuffType
{
    // DoT
    Burn,       // Brûlure    — Feu        — dégâts sur la durée, tick/s (§3.1.1.1)
    Poison,     // Poison     — Nature     — DoT + réduction soins reçus % (§3.1.1.1)
    Bleed,      // Saignement — Neutre     — dégâts sur la durée

    // Ralentissement & Immobilisation
    Freeze,     // Gel        — Eau        — immobilisation totale (réattribué Eau v3.0 — §3.1.1.1)
    Slow,       // Ralenti    — Eau        — réduit la vitesse de déplacement % (§3.1.1.1)
    Root,       // Enraciné   — Nature     — bloque le mouvement, peut toujours attaquer (§3.1.1.1)

    // Contrôle de foule dur
    Stun,       // Étourdi    — Terre      — bloque toutes les actions (CC dur §3.1.1.1)
    Fear,       // Peur       — Ténèbres   — fuite incontrôlée (CC dur §3.1.1.1)
    Sleep,      // Sommeil    — réveil au premier dégât reçu (§3.1.1.1)

    // Déplacement & Interrupt
    Knockback,  // Recul      — Eau        — déplace la cible à l'impact, ponctuel (§3.1.1.1)
    Shocked,    // Choc       — Foudre     — interruption du cast + mini-stun 0.5s (§3.1.1.1)

    // Précision & Ressource
    Blind,      // Aveugle    — Lumière    — réduction précision drastique (§3.1.1.1)
    ManaDrain,  // Drain mana — Ténèbres   — drain progressif sur la durée (§3.1.1.1)

    // Défense
    ArmorBreak, // Armure brisée — Terre  — réduction défense physique % temporaire (§3.1.1.1)

    // Utilitaire
    Silence,    // Silence    — Ténèbres   — bloque les skills (§3.1.1.1)
    Taunt,      // Taunt      — force les ennemis à cibler cette entité (§3.1.1.1)

    // Stats & Spéciaux
    Stats,      // Réduction de stat spécifique (utilise StatModifierType)
    Mark,       // Marque pour bonus dégâts (design decision)
    Other,      // Effet spécial custom
}

// ── Enums buff ────────────────────────────────────────────────
public enum BuffType
{
    // Soins
    Heal,           // Soin instantané (§3.1.1.2)
    Regeneration,   // Soin sur la durée — HoT (§3.1.1.2)

    // Défense
    Shield,         // Bouclier — absorbe les dégâts en priorité avant les HP (§3.1.1.2)
    Barrier,        // Bouclier HP + résistance élémentaire — Nature (§3.1.1.2)
    DefenseUp,      // Augmentation de défense — Fortify — Terre (§3.1.1.2)
    DodgeUp,        // Augmentation d'esquive (§3.1.1.2)
    PrecisionUp,    // Augmentation de précision (§


    // Offensif
    AttackUp,       // Augmentation d'attaque (§3.1.1.2)
    Haste,          // Augmentation vitesse déplacement + attackSpeed — Foudre (§3.1.1.2)
    CritChanceUp,   // Augmentation de chance de critique (§3.1.1.2)
    CritDamageUp,   // Augmentation de dégâts critiques (§3.1.1.2)

    // Spéciaux
    Purified,       // Suppression de tous les debuffs actifs — Lumière (§3.1.1.2)
    Invincible,     // Invincibilité temporaire — post-respawn 3s (§3.1.1.3)
    Stealth,        // Furtivité — interrompue par attaque/dégât reçu (§3.1.1.3)
    Dispel,         // Supprime un buff spécifique sur la cible ennemie (§3.1.1.3)
    Stats,          // Augmentation de stat spécifique (utilise StatModifierType)
    Other,          // Effet spécial custom
}

// ── Stat ciblée par un modificateur de buff ou debuff ─────────
// v3.5 — Fusion de BuffStatType + DebuffStatType en un seul enum.
// Utilisé par BuffData (buffType = Stats) et DebuffData (debuffType = Stats).
public enum StatModifierType
{
    MaxHP, MaxMana, RegenHP, RegenMana,
    AttackDamage, AttackSpeed, MoveSpeed,
    MeleeDefense, RangedDefense, MagicDefense,
    CritChance, CritDamage,
    ElementalPoint, Dodge, Precision,
    FireResistance, WaterResistance, EarthResistance,
    NatureResistance, LightningResistance,
    DarknessResistance, LightResistance,
    AllResistances,
}

// =============================================================
// StatusEffectData — base abstraite
// =============================================================
public abstract class StatusEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "Effect";
    public Sprite icon;

    [Header("Durée")]
    [Tooltip("Durée de base de l'effet en secondes.\n§3.1.1.4 : durée et chance d'application définitives sur SkillData.")]
    public float duration = 3f;

    /// <summary>Crée une instance runtime de cet effet.</summary>
    public abstract StatusEffectInstance CreateInstance(Entity source);
}

// =============================================================
// StatusEffectEntry — une ligne sur arme / rune / skill
// =============================================================
[System.Serializable]
public class StatusEffectEntry
{
    [Tooltip("SO de l'effet à appliquer (DebuffData ou BuffData).")]
    public StatusEffectData effect;

    [Tooltip("Probabilité d'application par attaque/utilisation [0..1].\nEx: 0.03 = 3% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.05f;

    /// <summary>Roll si l'effet se déclenche. True = appliquer l'effet.</summary>
    public bool Roll() => effect != null && Random.value < chance;
}
