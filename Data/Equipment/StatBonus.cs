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
//   1. Ajouter la valeur dans StatType (fin d'enum — ordinal safety)
//   2. Si elle doit rester toujours additive (jamais de %), l'ajouter à
//      CharacterStats.ExceptionStatTypes ET au ShowIf de StatBonus.mode ci-dessous
//      (sinon elle route par défaut vers AddToAcc — Flat/Percent normal)
//   3. Ajouter un appel FinalOf(StatType.<NouvelleStat>, <accumulateur_direct_existant>)
//      dans la section PUSH de CharacterStats.RecalculateStats — SANS cet appel, la
//      valeur accumulée dans flatAcc/percentAcc n'est jamais lue.
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

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Accumulateurs BRUTS — PAS
    // la formule (Base+Flat)×(1+%) du reste de cet enum. Voir CharacterStats.RecalculateStats,
    // section PUSH — poussés directement depuis flatAcc/percentAcc, pas via FinalOf(). Appliqués
    // sur le NOMBRE de dégâts par CombatSystem, pas une stat persistante.
    [InspectorName("Final Damage Bonus (attaquant, flat ou %, voir mode)")]     FinalDamageBonus,
    [InspectorName("Final Damage Reduction (défenseur, flat ou %, voir mode)")] FinalDamageReduction,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Bonus de dégâts élémentaires
    // PAR élément — toujours additifs (même famille que ResistX/PointsX, pas de formule
    // Base+Flat×%). Source principale : paliers SpiritData (2026-09-06), mais génériques comme
    // tout StatType — n'importe quel équipement peut en donner. Appliqué dans
    // CombatSystem.CalculateDamage sur elemDamage, ciblé par skill.PrimaryElement.
    [InspectorName("Dégâts élémentaires Feu (ratio 0.05 = +5%)")]      ElementalDamageBonusFire,
    [InspectorName("Dégâts élémentaires Eau (ratio 0.05 = +5%)")]      ElementalDamageBonusWater,
    [InspectorName("Dégâts élémentaires Foudre (ratio 0.05 = +5%)")]   ElementalDamageBonusLightning,
    [InspectorName("Dégâts élémentaires Terre (ratio 0.05 = +5%)")]    ElementalDamageBonusEarth,
    [InspectorName("Dégâts élémentaires Nature (ratio 0.05 = +5%)")]   ElementalDamageBonusNature,
    [InspectorName("Dégâts élémentaires Ténèbres (ratio 0.05 = +5%)")] ElementalDamageBonusDarkness,
    [InspectorName("Dégâts élémentaires Lumière (ratio 0.05 = +5%)")]  ElementalDamageBonusLight,
    [InspectorName("Dégâts élémentaires TOUS (ratio 0.05 = +5%)")]     ElementalDamageBonusAll,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Pénétration de résistance
    // ennemie PAR élément — toujours additive, généralise l'ancien bonus rang5 Neutre/élémentaire
    // hardcodé (ElementalSystem.GetRank5ResistPenetration) qui reste une source séparée et
    // s'additionne avec celle-ci. Soustrait de la résistance cible dans CombatSystem.
    [InspectorName("Pénétration résist. Feu (ratio 0.05 = -5% résist ennemie)")]      ResistPenetrationFire,
    [InspectorName("Pénétration résist. Eau (ratio 0.05 = -5% résist ennemie)")]      ResistPenetrationWater,
    [InspectorName("Pénétration résist. Foudre (ratio 0.05 = -5% résist ennemie)")]   ResistPenetrationLightning,
    [InspectorName("Pénétration résist. Terre (ratio 0.05 = -5% résist ennemie)")]    ResistPenetrationEarth,
    [InspectorName("Pénétration résist. Nature (ratio 0.05 = -5% résist ennemie)")]   ResistPenetrationNature,
    [InspectorName("Pénétration résist. Ténèbres (ratio 0.05 = -5% résist ennemie)")] ResistPenetrationDarkness,
    [InspectorName("Pénétration résist. Lumière (ratio 0.05 = -5% résist ennemie)")]  ResistPenetrationLight,
    [InspectorName("Pénétration résist. TOUS (ratio 0.05 = -5% résist ennemie)")]     ResistPenetrationAll,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Réduction de coût en mana
    // PAR élément du skill lancé — toujours additive. Lu par SkillBar au moment de la dépense
    // (Player.SpendMana), jamais une stat de combat.
    [InspectorName("Réduction coût mana Feu (ratio 0.05 = -5%)")]      ManaCostReductionFire,
    [InspectorName("Réduction coût mana Eau (ratio 0.05 = -5%)")]      ManaCostReductionWater,
    [InspectorName("Réduction coût mana Foudre (ratio 0.05 = -5%)")]   ManaCostReductionLightning,
    [InspectorName("Réduction coût mana Terre (ratio 0.05 = -5%)")]    ManaCostReductionEarth,
    [InspectorName("Réduction coût mana Nature (ratio 0.05 = -5%)")]   ManaCostReductionNature,
    [InspectorName("Réduction coût mana Ténèbres (ratio 0.05 = -5%)")] ManaCostReductionDarkness,
    [InspectorName("Réduction coût mana Lumière (ratio 0.05 = -5%)")]  ManaCostReductionLight,
    [InspectorName("Réduction coût mana TOUS (ratio 0.05 = -5%)")]     ManaCostReductionAll,

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety). Réduction de cooldown
    // globale — toujours additive, s'ajoute à CharacterStats.cooldownReduction (déjà alimenté
    // par StatPoints élémentaire et le passif Robe). Source ajoutée : paliers SpiritData.
    [InspectorName("Réduction Cooldown (ratio 0.05 = -5%)")] CooldownReduction,
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
        StatType.BonusHP, StatType.BonusMana, StatType.BonusRegenHP, StatType.BonusRegenMana,
        StatType.FinalDamageBonus, StatType.FinalDamageReduction)]
    public ModifierType mode = ModifierType.Flat;

    [Tooltip(
        "Mode Flat : valeur directe, ex: 200, 10, 0.5\n" +
        "Mode Percent : décimal, ex: 0.05 = 5% | 0.10 = 10%\n" +
        "Stats sans sélecteur mode (Crit, Résistances, Points, MoveSpeed) : toujours en\n" +
        "décimal pour Crit/Résistances (0.10 = 10%), valeur directe pour Points/MoveSpeed."
    )]
    public float value;
}
