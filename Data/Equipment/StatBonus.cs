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
    [InspectorName("Melee Defense (flat)")]    MeleeDefense = 0,
    [InspectorName("Ranged Defense (flat)")]   RangedDefense = 1,
    [InspectorName("Magic Defense (flat)")]    MagicDefense = 2,

    // ── Attaque ───────────────────────────────────────────────
    [InspectorName("Bonus Attack (flat)")]     BonusAttack = 3,

    // ── Mobilité ──────────────────────────────────────────────
    [InspectorName("Dodge (flat)")]            Dodge = 4,
    [InspectorName("Precision (flat)")]        Precision = 5,
    [InspectorName("Move Speed (flat)")]       MoveSpeed = 6,

    // ── Critique ──────────────────────────────────────────────
    [InspectorName("Crit Chance (ratio 0.05 = 5%)")]   CritChance = 7,
    [InspectorName("Crit Multiplier (ratio 0.15 = +15%)")] CritMultiplier = 8,

    [InspectorName("Reduction de dmg critique (ratio 0.10 = -10% dmg crit)")] CritDmgReduction = 9,

    // ── Résistances élémentaires ──────────────────────────────
    [InspectorName("Resist Fire (ratio 0.10 = 10%)")]      ResistFire = 10,
    [InspectorName("Resist Water (ratio 0.10 = 10%)")]     ResistWater = 11,
    [InspectorName("Resist Lightning (ratio 0.10 = 10%)")] ResistLightning = 12,
    [InspectorName("Resist Earth (ratio 0.10 = 10%)")]     ResistEarth = 13,
    [InspectorName("Resist Nature (ratio 0.10 = 10%)")]    ResistNature = 14,
    [InspectorName("Resist Darkness (ratio 0.10 = 10%)")]  ResistDarkness = 15,
    [InspectorName("Resist Light (ratio 0.10 = 10%)")]     ResistLight = 16,
    [InspectorName("Resist ALL (ratio 0.10 = 10%)")]       ResistAll = 17,

    // ── Points élémentaires ───────────────────────────────────
    [InspectorName("Points Fire (flat)")]      PointsFire = 18,
    [InspectorName("Points Water (flat)")]     PointsWater = 19,
    [InspectorName("Points Lightning (flat)")] PointsLightning = 20,
    [InspectorName("Points Earth (flat)")]     PointsEarth = 21,
    [InspectorName("Points Nature (flat)")]    PointsNature = 22,
    [InspectorName("Points Darkness (flat)")]  PointsDarkness = 23,
    [InspectorName("Points Light (flat)")]     PointsLight = 24,
    [InspectorName("Points ALL (flat)")]       PointsAll = 25,

    // 26-33 retirés (2026-09-07) — anciens multiplicateurs de points élémentaires
    // (ElementBonusFire..All), jamais appliqués (accumulés dans CharacterStats mais jamais lus
    // avant le push sur Entity — bug silencieux, pas une vraie feature). Sans rapport avec
    // PointsFire/../All (flat, actif) ni ElementalDamageBonusFire/../All (%, actif, alimenté
    // par SpiritData) plus bas. Zéro référence code, zéro .asset — vérifié avant suppression.
    // Ordinaux 26-33 jamais réutilisés.

    // ── Vie & Mana ────────────────────────────────────────────
    // Regen ici = PERMANENT (équipement / PermanentSkillData.bonuses, jamais expire).
    // Pour une regen TEMPORAIRE (buff/debuff avec durée), voir StatModifierType.RegenHP/
    // RegenMana dans StatusEffectData.cs — les deux s'additionnent sur le même champ final
    // Entity.RegenHP, pas de conflit, juste deux durées de vie différentes.
    [InspectorName("Bonus HP (flat)")]         BonusHP = 34,
    [InspectorName("Bonus Mana (flat)")]       BonusMana = 35,
    [InspectorName("Regen HP naturel (flat)")]   BonusRegenHP = 36,
    [InspectorName("Regen Mana naturel (flat)")] BonusRegenMana = 37,

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    [InspectorName("All Defense (flat ou %, voir mode)")] AllDefense = 38,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Accumulateurs BRUTS — PAS
    // la formule (Base+Flat)×(1+%) du reste de cet enum. Voir CharacterStats.RecalculateStats,
    // section PUSH — poussés directement depuis flatAcc/percentAcc, pas via FinalOf(). Appliqués
    // sur le NOMBRE de dégâts par CombatSystem, pas une stat persistante.
    [InspectorName("Final Damage Bonus (attaquant, flat ou %, voir mode)")]     FinalDamageBonus = 39,
    [InspectorName("Final Damage Reduction (défenseur, flat ou %, voir mode)")] FinalDamageReduction = 40,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Bonus de dégâts élémentaires
    // PAR élément — toujours additifs (même famille que ResistX/PointsX, pas de formule
    // Base+Flat×%). Source principale : paliers SpiritData (2026-09-06), mais génériques comme
    // tout StatType — n'importe quel équipement peut en donner. Appliqué dans
    // CombatSystem.CalculateDamage sur elemDamage, ciblé par skill.PrimaryElement.
    [InspectorName("Dégâts élémentaires Feu (ratio 0.05 = +5%)")]      ElementalDamageBonusFire = 41,
    [InspectorName("Dégâts élémentaires Eau (ratio 0.05 = +5%)")]      ElementalDamageBonusWater = 42,
    [InspectorName("Dégâts élémentaires Foudre (ratio 0.05 = +5%)")]   ElementalDamageBonusLightning = 43,
    [InspectorName("Dégâts élémentaires Terre (ratio 0.05 = +5%)")]    ElementalDamageBonusEarth = 44,
    [InspectorName("Dégâts élémentaires Nature (ratio 0.05 = +5%)")]   ElementalDamageBonusNature = 45,
    [InspectorName("Dégâts élémentaires Ténèbres (ratio 0.05 = +5%)")] ElementalDamageBonusDarkness = 46,
    [InspectorName("Dégâts élémentaires Lumière (ratio 0.05 = +5%)")]  ElementalDamageBonusLight = 47,
    [InspectorName("Dégâts élémentaires TOUS (ratio 0.05 = +5%)")]     ElementalDamageBonusAll = 48,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Pénétration de résistance
    // ennemie PAR élément — toujours additive, généralise l'ancien bonus rang5 Neutre/élémentaire
    // hardcodé (ElementalSystem.GetRank5ResistPenetration) qui reste une source séparée et
    // s'additionne avec celle-ci. Soustrait de la résistance cible dans CombatSystem.
    [InspectorName("Pénétration résist. Feu (ratio 0.05 = -5% résist ennemie)")]      ResistPenetrationFire = 49,
    [InspectorName("Pénétration résist. Eau (ratio 0.05 = -5% résist ennemie)")]      ResistPenetrationWater = 50,
    [InspectorName("Pénétration résist. Foudre (ratio 0.05 = -5% résist ennemie)")]   ResistPenetrationLightning = 51,
    [InspectorName("Pénétration résist. Terre (ratio 0.05 = -5% résist ennemie)")]    ResistPenetrationEarth = 52,
    [InspectorName("Pénétration résist. Nature (ratio 0.05 = -5% résist ennemie)")]   ResistPenetrationNature = 53,
    [InspectorName("Pénétration résist. Ténèbres (ratio 0.05 = -5% résist ennemie)")] ResistPenetrationDarkness = 54,
    [InspectorName("Pénétration résist. Lumière (ratio 0.05 = -5% résist ennemie)")]  ResistPenetrationLight = 55,
    [InspectorName("Pénétration résist. TOUS (ratio 0.05 = -5% résist ennemie)")]     ResistPenetrationAll = 56,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Réduction de coût en mana
    // PAR élément du skill lancé — toujours additive. Lu par SkillBar au moment de la dépense
    // (Player.SpendMana), jamais une stat de combat.
    [InspectorName("Réduction coût mana Feu (ratio 0.05 = -5%)")]      ManaCostReductionFire = 57,
    [InspectorName("Réduction coût mana Eau (ratio 0.05 = -5%)")]      ManaCostReductionWater = 58,
    [InspectorName("Réduction coût mana Foudre (ratio 0.05 = -5%)")]   ManaCostReductionLightning = 59,
    [InspectorName("Réduction coût mana Terre (ratio 0.05 = -5%)")]    ManaCostReductionEarth = 60,
    [InspectorName("Réduction coût mana Nature (ratio 0.05 = -5%)")]   ManaCostReductionNature = 61,
    [InspectorName("Réduction coût mana Ténèbres (ratio 0.05 = -5%)")] ManaCostReductionDarkness = 62,
    [InspectorName("Réduction coût mana Lumière (ratio 0.05 = -5%)")]  ManaCostReductionLight = 63,
    [InspectorName("Réduction coût mana TOUS (ratio 0.05 = -5%)")]     ManaCostReductionAll = 64,

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety). Réduction de cooldown
    // globale — toujours additive, s'ajoute à CharacterStats.cooldownReduction (déjà alimenté
    // par StatPoints élémentaire et le passif Robe). Source ajoutée : paliers SpiritData.
    [InspectorName("Réduction Cooldown (ratio 0.05 = -5%)")] CooldownReduction = 65,
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
