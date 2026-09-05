using UnityEngine;

// =============================================================
// StatBonus — Bonus de stat générique pour tous les équipements
// Path : Assets/Scripts/Data/Inventory/Equipment/StatBonus.cs
// AetherTree GDD v3.5 — §5.1 à §5.7
//
// Utilisé via EquipmentConfig.bonuses sur tous les slots :
//   Arme, Armure, Casque, Gants, Bottes, Bijoux, Esprits, Runes
//
// Pour ajouter une nouvelle stat :
//   1. Ajouter la valeur dans StatType
//   2. Ajouter le case dans CharacterStats.AccumulateBonus()
//   C'est tout — aucun autre fichier à modifier.
//
// ┌─────────────────────────┬──────────┬────────────────────────┐
// │ StatType                │ Unité    │ Exemple Inspector      │
// ├─────────────────────────┼──────────┼────────────────────────┤
// │ MeleeDefense            │ flat     │ 30   → +30 déf mêlée   │
// │ RangedDefense           │ flat     │ 20   → +20 déf distance│
// │ MagicDefense            │ flat     │ 15   → +15 déf magique │
// │ BonusAttack             │ flat     │ 15   → +15 atk min/max │
// │ Dodge                   │ flat     │ 10   → +10 esquive     │
// │ Precision               │ flat     │ 20   → +20 précision   │
// │ MoveSpeed               │ flat     │ 0.5  → +0.5 vitesse    │
// │ CritChance              │ RATIO    │ 0.05 → +5% crit        │
// │ CritMultiplier          │ RATIO    │ 0.15 → +15% mult crit  │
// │ ResistFire/All/...      │ RATIO    │ 0.10 → +10% résistance │
// │ PointsFire/All/...      │ flat     │ 10   → +10 pts élém    │
// │ BonusHP                 │ flat     │ 200  → +200 HP max     │
// │ BonusMana               │ flat     │ 50   → +50 Mana max    │
// │ BonusRegenHP            │ flat     │ 2    → +2 HP/s         │
// │ BonusRegenMana          │ flat     │ 1    → +1 Mana/s       │
// └─────────────────────────┴──────────┴────────────────────────┘
// =============================================================

public enum StatType
{
    // ── Défense ───────────────────────────────────────────────
    [InspectorName("Melee Defense (flat)")]    MeleeDefense,
    [InspectorName("Ranged Defense (flat)")]   RangedDefense,
    [InspectorName("Magic Defense (flat)")]    MagicDefense,

    // ── Attaque ───────────────────────────────────────────────
    [InspectorName("Bonus Attack (flat)")]     BonusAttack,

    // ── Mobilité ──────────────────────────────────────────────
    [InspectorName("Dodge (flat)")]            Dodge,
    [InspectorName("Precision (flat)")]        Precision,
    [InspectorName("Move Speed (flat)")]       MoveSpeed,

    // ── Critique ──────────────────────────────────────────────
    [InspectorName("Crit Chance (ratio 0.05 = 5%)")]   CritChance,
    [InspectorName("Crit Multiplier (ratio 0.15 = +15%)")] CritMultiplier,

    [InspectorName("Reduction de dmg critique (ratio 0.10 = -10% dmg crit)")] CritDmgReduction, 

    // ── Résistances élémentaires ──────────────────────────────
    [InspectorName("Resist Fire (ratio 0.10 = 10%)")]      ResistFire,
    [InspectorName("Resist Water (ratio 0.10 = 10%)")]     ResistWater,
    [InspectorName("Resist Lightning (ratio 0.10 = 10%)")] ResistLightning,
    [InspectorName("Resist Earth (ratio 0.10 = 10%)")]     ResistEarth,
    [InspectorName("Resist Nature (ratio 0.10 = 10%)")]    ResistNature,
    [InspectorName("Resist Darkness (ratio 0.10 = 10%)")]  ResistDarkness,
    [InspectorName("Resist Light (ratio 0.10 = 10%)")]     ResistLight,
    [InspectorName("Resist ALL (ratio 0.10 = 10%)")]       ResistAll,

    // ── Points élémentaires ───────────────────────────────────
    [InspectorName("Points Fire (flat)")]      PointsFire,
    [InspectorName("Points Water (flat)")]     PointsWater,
    [InspectorName("Points Lightning (flat)")] PointsLightning,
    [InspectorName("Points Earth (flat)")]     PointsEarth,
    [InspectorName("Points Nature (flat)")]    PointsNature,
    [InspectorName("Points Darkness (flat)")]  PointsDarkness,
    [InspectorName("Points Light (flat)")]     PointsLight,
    [InspectorName("Points ALL (flat)")]       PointsAll,

    // ── Multiplicateurs de points élémentaires — RETIRÉS (2026) ────────
    // Jamais appliqués (accumulés dans CharacterStats mais jamais lus avant le
    // push sur Entity — bug silencieux, pas une vraie feature). Ordinal gardé
    // (au milieu de l'enum, BonusHP/BonusMana suivent) — jamais delete/réordonner.
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusFire,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusWater,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusLightning,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusEarth,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusNature,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusDarkness,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusLight,
    [System.Obsolete("Retiré (2026) — jamais appliqué, ordinal gardé.")] ElementBonusAll,

    // ── Vie & Mana ────────────────────────────────────────────
    // Regen ici = PERMANENT (équipement / PermanentSkillData.bonuses, jamais expire).
    // Pour une regen TEMPORAIRE (buff/debuff avec durée), voir StatModifierType.RegenHP/
    // RegenMana dans StatusEffectData.cs — les deux s'additionnent sur le même champ final
    // Entity.RegenHP, pas de conflit, juste deux durées de vie différentes.
    [InspectorName("Bonus HP (flat)")]         BonusHP,
    [InspectorName("Bonus Mana (flat)")]       BonusMana,
    [InspectorName("Regen HP naturel (flat)")]   BonusRegenHP,
    [InspectorName("Regen Mana naturel (flat)")] BonusRegenMana,

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    [InspectorName("All Defense (flat ou %, voir mode)")] AllDefense,
}

// =============================================================
// StatBonus — une ligne de bonus dans l'Inspector
// Assigné via EquipmentConfig.bonuses sur tous les équipements.
// =============================================================
[System.Serializable]
public class StatBonus
{
    [Tooltip("Type de stat à modifier — voir tableau des unités dans StatBonus.cs")]
    public StatType statType;

    [Tooltip(
        "Flat : valeur directe ajoutée à la stat.\n" +
        "Percent : % appliqué sur (Base + tous les Flat actifs, équipement + buffs), sommé\n" +
        "avec tous les autres % actifs ciblant la même stat avant d'être appliqué UNE fois.\n" +
        "Masqué pour les stats toujours additives (Crit, Résistances, Points élémentaires,\n" +
        "MoveSpeed) — pas de multiplication pour elles, juste addition directe de `value`."
    )]
    [ShowIf(nameof(statType),
        StatType.MeleeDefense, StatType.RangedDefense, StatType.MagicDefense, StatType.AllDefense,
        StatType.BonusAttack, StatType.Dodge, StatType.Precision,
        StatType.BonusHP, StatType.BonusMana, StatType.BonusRegenHP, StatType.BonusRegenMana)]
    public ModifierType mode = ModifierType.Flat;

    [Tooltip(
        "Mode Flat : valeur directe, ex: 200, 10, 0.5\n" +
        "Mode Percent : décimal, ex: 0.05 = 5% | 0.10 = 10%\n" +
        "Stats sans sélecteur mode (Crit, Résistances, Points, MoveSpeed) : toujours en\n" +
        "décimal pour Crit/Résistances (0.10 = 10%), valeur directe pour Points/MoveSpeed."
    )]
    public float value;
}
