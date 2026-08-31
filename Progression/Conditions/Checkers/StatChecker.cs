using UnityEngine;

// =============================================================
// STATCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/StatChecker.cs
//
// Ecoute : StatsChangedEvent
//
// Remplace : StatThresholdChecker, StatPointChecker, AffinityChecker
// Couvre en un seul checker :
//   - Stats finales (MaxHP, défenses, résistances, crit, dodge...)
//   - Level du joueur
//   - Points investis (rang Attaque, Défense, Elemental, HP)
//   - Affinité élémentaire (valeur, rang, dominant)
//
// Usage :
//   Mode = FinalStat  → vérifie player.MaxHP >= minValue, etc.
//                       → vérifie player.level >= minValue (PlayerLevel)
//   Mode = StatPoint  → vérifie player.statPoints.GetRank(stat) == exactMilestone
//   Mode = Affinity   → vérifie elemental.GetAffinity() / GetElementRank() / dominant
//
// Config pour "atteindre le niveau 5" :
//   Mode      = FinalStat
//   FinalStat = PlayerLevel
//   Min Value = 5
//   Nombre requis (ConditionEntry) = 1
// =============================================================

[System.Serializable]
public class StatChecker : ConditionCheckerBase
{
    // ── Mode ──────────────────────────────────────────────────
    public enum CheckMode { FinalStat, StatPoint, Affinity }

    public CheckMode mode = CheckMode.FinalStat;

    // ── FinalStat ─────────────────────────────────────────────
    public enum FinalStatType
    {
        MaxHP, MaxMana,
        MeleeDefense, RangedDefense, MagicDefense,
        Dodge, CritChance, CritMultiplier, Precision,
        ResistFire, ResistWater, ResistEarth, ResistNature,
        ResistLightning, ResistDarkness, ResistLight,
        PlayerLevel,    // ← level du joueur (lu sur player.level)
    }

    [Header("Mode : FinalStat")]
    public FinalStatType finalStat = FinalStatType.MaxHP;
    [Tooltip("0 = pas de minimum")]
    public float         minValue  = 0f;
    [Tooltip("0 = pas de maximum")]
    public float         maxValue  = 0f;

    // ── StatPoint ─────────────────────────────────────────────
    [Header("Mode : StatPoint")]
    public StatCategory  statCategory   = StatCategory.Attack;
    [Tooltip("0 = pas de restriction")]
    public int           minRank        = 0;
    [Tooltip("0 = pas de restriction")]
    public int           maxRank        = 0;
    [Tooltip("Si > 0 : rang exact requis (palier)")]
    public int           exactMilestone = 0;

    // ── Affinity ──────────────────────────────────────────────
    [Header("Mode : Affinity")]
    public ElementType   affinityElement    = ElementType.Fire;
    [Tooltip("0 = pas de restriction d'affinité")]
    public float         minAffinity        = 0f;
    [Tooltip("0 = pas de rang minimum")]
    public int           affinityRankMin    = 0;
    public bool          mustBeDominant     = false;

    // ─────────────────────────────────────────────────────────

    public override System.Type RelevantEventType => typeof(StatsChangedEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not StatsChangedEvent) return false;
        if (player == null)                     return false;

        return mode switch
        {
            CheckMode.FinalStat => EvaluateFinalStat(player),
            CheckMode.StatPoint => EvaluateStatPoint(player),
            CheckMode.Affinity  => EvaluateAffinity(player),
            _                   => false,
        };
    }

    private bool EvaluateFinalStat(Player p)
    {
        float value = finalStat switch
        {
            FinalStatType.MaxHP             => p.MaxHP,
            FinalStatType.MaxMana           => p.MaxMana,
            FinalStatType.MeleeDefense      => p.GetMeleeDefense(),
            FinalStatType.RangedDefense     => p.GetRangedDefense(),
            FinalStatType.MagicDefense      => p.GetMagicDefense(),
            FinalStatType.Dodge             => p.GetEffectiveDodge(),
            FinalStatType.CritChance        => p.GetEffectiveCritChance(),
            FinalStatType.CritMultiplier    => p.CritMultiplier,
            FinalStatType.Precision         => p.GetEffectivePrecision(),
            FinalStatType.ResistFire        => p.stats?.GetResistance(ElementType.Fire)      ?? 0f,
            FinalStatType.ResistWater       => p.stats?.GetResistance(ElementType.Water)     ?? 0f,
            FinalStatType.ResistEarth       => p.stats?.GetResistance(ElementType.Earth)     ?? 0f,
            FinalStatType.ResistNature      => p.stats?.GetResistance(ElementType.Nature)    ?? 0f,
            FinalStatType.ResistLightning   => p.stats?.GetResistance(ElementType.Lightning) ?? 0f,
            FinalStatType.ResistDarkness    => p.stats?.GetResistance(ElementType.Darkness)  ?? 0f,
            FinalStatType.ResistLight       => p.stats?.GetResistance(ElementType.Light)     ?? 0f,
            FinalStatType.PlayerLevel       => p.level,
            _                               => 0f,
        };

        if (minValue > 0 && value < minValue) return false;
        if (maxValue > 0 && value > maxValue) return false;
        return true;
    }

    private bool EvaluateStatPoint(Player p)
    {
        if (p.statPoints == null) return false;
        int rank = p.statPoints.GetRank(statCategory);

        if (exactMilestone > 0) return rank == exactMilestone;
        if (minRank > 0 && rank < minRank) return false;
        if (maxRank > 0 && rank > maxRank) return false;
        return true;
    }

    private bool EvaluateAffinity(Player p)
    {
        var elemental = p.GetComponent<ElementalSystem>();
        if (elemental == null) return false;

        if (mustBeDominant      && elemental.GetDominantElement()               != affinityElement) return false;
        if (minAffinity > 0     && elemental.GetAffinity(affinityElement)       < minAffinity)      return false;
        if (affinityRankMin > 0 && elemental.GetElementRank(affinityElement)    < affinityRankMin)  return false;
        return true;
    }
}
