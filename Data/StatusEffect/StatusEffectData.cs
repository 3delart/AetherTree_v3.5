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
    // DoT — retirés du design, utiliser Dot (fin d'enum) avec damageElement à la place.
    // Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser ces 3 positions.
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Fire à la place.")]
    Burn,
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Nature à la place.")]
    Poison,
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Neutral à la place.")]
    Bleed,

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

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    Dot,        // DoT générique réutilisable pour tout élément (voir damageElement) —
                // même formule que Burn/Poison/Bleed, juste pas un nom/élément figé.
                // Futurs debuffs par élément (noyade=Eau, etc.) : soit ce type générique
                // avec damageElement + effectName/icon dédiés, soit un DebuffType propre —
                // au choix du designer au moment de les créer.
}

// ── Enums buff ────────────────────────────────────────────────
public enum BuffType
{
    // Soins
    Heal,           // Soin instantané (§3.1.1.2)
    Regeneration,   // Soin sur la durée — HoT (§3.1.1.2)

    // Défense
    Shield,         // Bouclier — absorbe les dégâts en priorité avant les HP (§3.1.1.2)

    // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp retirés
    // (2026) — redondants avec Stats (StatModifierType couvre déjà chaque stat individuellement :
    // AllResistances, MeleeDefense/RangedDefense/MagicDefense, Dodge, Precision, AttackDamage,
    // MoveSpeed, CritChance, CritDamage). Gardés ici comme placeholders [Obsolete] pour ne pas
    // décaler les ordinaux sérialisés de Purified/Invincible/Stealth/Dispel/Stats/Other qui
    // suivent — Unity sérialise un enum par sa position int, pas son nom. Ne jamais réutiliser
    // ces slots pour une nouvelle valeur ; ajouter en fin d'enum à la place (voir Revive).
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.AllResistances.")]
    Removed_Barrier,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.MeleeDefense/RangedDefense/MagicDefense.")]
    Removed_DefenseUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.Dodge.")]
    Removed_DodgeUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.Precision.")]
    Removed_PrecisionUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.AttackDamage.")]
    Removed_AttackUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.MoveSpeed.")]
    Removed_Haste,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.CritChance.")]
    Removed_CritChanceUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.CritDamage.")]
    Removed_CritDamageUp,

    // Spéciaux
    Purified,       // Suppression de tous les debuffs actifs — Lumière (§3.1.1.2)
    Invincible,     // Invincibilité temporaire — post-respawn 3s (§3.1.1.3)
    Stealth,        // Furtivité — interrompue par attaque/dégât reçu (§3.1.1.3)
    Dispel,         // Supprime un buff spécifique sur la cible ennemie (§3.1.1.3)
    Stats,          // Augmentation de stat spécifique (utilise StatModifierType)
    Other,          // Effet spécial custom
    Revive,         // Résurrection instantanée (Player uniquement) — ajouté en fin d'enum
                     // volontairement, pour ne jamais décaler les valeurs déjà sérialisées.
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
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"buff_\"\n" +
             "ou \"dbf_\" selon le type (ex: \"buff_shield\", \"dbf_poison\"). Ne JAMAIS afficher au\n" +
             "joueur — voir effectName pour l'affichage.")]
    public string effectID;
    [Tooltip("Nom affiché au joueur (fr/en).")]
    public LocalizedText effectName = new LocalizedText();
    [Tooltip("Description affichée au joueur (fr/en) — tooltip consommable, icône de statut actif.")]
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    [Header("Durée")]
    [Tooltip("Durée de base de l'effet en secondes.\n§3.1.1.4 : durée et chance d'application définitives sur SkillData.")]
    public float duration = 3f;

    /// <summary>Crée une instance runtime de cet effet.</summary>
    public abstract StatusEffectInstance CreateInstance(Entity source);

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(effectID))
            effectID = name;
    }
#endif
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
