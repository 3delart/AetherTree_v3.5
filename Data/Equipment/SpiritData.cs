using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SpiritData — ScriptableObject template d'esprit élémentaire
// Path : Assets/Scripts/Data/Equipment/SpiritData.cs
// AetherTree GDD v3.6 — §5.8
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// 8 Esprits au total :
//   7 Esprits élémentaires — Feu, Eau, Terre, Nature, Foudre,
//     Ténèbres, Lumière — source principale de points élémentaires.
//   1 Esprit Neutre — bonus phys/crit/HP au lieu de pts élémentaires.
//
// Règles générales (GDD §5.8) :
//   1 seul esprit actif à la fois — changement libre depuis inventaire.
//   XP accordée par mobs dans la plage ±15 niveaux du joueur.
//   Niveau max 50 — bonus aux paliers 10, 20, 30, 40, 50.
//   Obtention principale : conditions in-game (IConditionChecker).
//
// Esprit Neutre (GDD §5.8) :
//   Équipé à la place d'un esprit élémentaire.
//   N'apporte aucun point élémentaire.
//   Bonus via config.bonuses : BonusAttack%, CritChance, MaxHP.
//
// Stats fixes sur le SO :
//   element        — détermine quels points élémentaires sont apportés
//   elementalPoints — base de points élémentaires au niveau 1
//   maxLevel (50)  — niveau maximum de l'esprit
//
// Paliers de bonus via SpiritMilestone.config (GDD §5.8) :
//   Lv10, 20, 30, 40, 50 — valeurs à calibrer en test (§14.2)
// =============================================================

[CreateAssetMenu(fileName = "NewSpirit", menuName = "AetherTree/Equipment/SpiritData")]
public class SpiritData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject spiritPrefab;

    // ── Élément ───────────────────────────────────────────────
    [Header("Élément")]
    [Tooltip("Élément fixe de cet esprit — 1 esprit par élément non-Neutre.\n" +
             "ElementType.Neutral = Esprit Neutre (bonus phys/crit/HP — pas de pts élémentaires).")]
    public ElementType element = ElementType.Fire;

    // ── Points élémentaires ───────────────────────────────────
    [Header("Points élémentaires (ignoré si element = Neutral — GDD §5.8)")]
    [Tooltip("Points élémentaires apportés au niveau 1.")]
    public int pointsAtLevel1 = 1;

    [Tooltip("Points élémentaires apportés au niveau maximum.")]
    public int pointsAtMaxLevel = 20;

    [Tooltip("Exposant de la courbe de progression (1.0 = linéaire, 1.5 = progressive).\n" +
             "Recommandé : 1.5")]
    public float pointsCurveExponent = 1.5f;

    // ── Progression ───────────────────────────────────────────
    [Header("Progression")]
    [Tooltip("Niveau maximum de l'esprit (défaut 50 — GDD §5.8).")]
    public int maxLevel = 50;

    // ── Paliers de bonus ──────────────────────────────────────
    [Header("Paliers de bonus (Lv10, 20, 30, 40, 50 — GDD §5.8)")]
    [Tooltip("Bonus débloqués à certains niveaux.\n" +
             "⚠ Valeurs à calibrer en test — §14.2.\n" +
             "Esprits élémentaires : recommandé PointsFire/etc. ou ElementBonusFire/etc.\n" +
             "Esprit Neutre : recommandé BonusAttack, CritChance, BonusHP.")]
    public List<SpiritMilestone> milestones = new List<SpiritMilestone>();

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>
    /// Points élémentaires apportés au niveau N.
    /// Retourne 0 si element == Neutral (Esprit Neutre).
    /// </summary>
    public int GetPointsAtLevel(int level)
    {
        if (element == ElementType.Neutral) return 0;
        if (maxLevel <= 1) return pointsAtLevel1;
        float t    = Mathf.Clamp01((float)(level - 1) / (maxLevel - 1));
        float tPow = Mathf.Pow(t, pointsCurveExponent);
        return Mathf.RoundToInt(Mathf.Lerp(pointsAtLevel1, pointsAtMaxLevel, tPow));
    }

    /// <summary>XP requis pour passer du niveau N au niveau N+1.</summary>
    public int GetXPRequired(int level)
        => Mathf.RoundToInt(100 * Mathf.Pow(level, 1.3f));

    /// <summary>Points élémentaires cumulés de niveau 1 à N.</summary>
    public int GetTotalPointsAtLevel(int level)
    {
        int total = 0;
        for (int i = 1; i <= Mathf.Min(level, maxLevel); i++)
            total += GetPointsAtLevel(i);
        return total;
    }

    /// <summary>Retourne les bonus du palier atteint à ce niveau (null si aucun).</summary>
    public SpiritMilestone GetMilestone(int level)
    {
        if (milestones == null) return null;
        foreach (var m in milestones)
            if (m.level == level) return m;
        return null;
    }
}

// =============================================================
// SpiritMilestone — Bonus débloqué à un palier de niveau
// GDD v3.6 — §5.8 (valeurs à calibrer §14.2)
// =============================================================
[System.Serializable]
public class SpiritMilestone
{
    [Tooltip("Niveau auquel ce palier est débloqué (ex: 10, 20, 30, 40, 50).")]
    public int level;

    [Tooltip("Bonus débloqués à ce palier.\n" +
             "⚠ Valeurs à calibrer — §14.2.\n" +
             "Esprits élémentaires : PointsFire/etc. ou ElementBonusFire/etc.\n" +
             "Esprit Neutre : BonusAttack, CritChance, BonusHP.")]
    public List<StatBonus> bonuses = new List<StatBonus>();
}

// =============================================================
// SpiritInstance — données runtime d'un esprit équipé / en stock
// GDD v3.6 — §5.8
// =============================================================
[System.Serializable]
public class SpiritInstance
{
    public SpiritData data;

    public int level     = 1;
    public int currentXP = 0;

    public SpiritInstance(SpiritData source)
    {
        data      = source;
        level     = 1;
        currentXP = 0;
    }

    // ── Accesseurs ────────────────────────────────────────────
    public string      ItemId      => data != null ? data.itemID : "unknown_spirit";
    public string      SpiritName  => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Spirit";
    public Sprite      Icon        => data != null ? data.icon       : null;
    public ElementType Element     => data != null ? data.element    : ElementType.Neutral;
    public int         MaxLevel    => data != null ? data.maxLevel   : 50;
    public bool        IsMaxLevel  => level >= MaxLevel;

    /// <summary>Points élémentaires cumulés apportés à ce niveau (0 si Esprit Neutre).</summary>
    public int TotalElementalPoints => data != null ? data.GetTotalPointsAtLevel(level) : 0;

    /// <summary>XP requis pour le prochain niveau.</summary>
    public int XPRequired => data != null ? data.GetXPRequired(level) : 100;

    // ── Raccourcis config ─────────────────────────────────────
    public List<StatBonus> Bonuses => data?.config?.bonuses;

    // ── XP & Level Up ─────────────────────────────────────────

    /// <summary>
    /// Ajoute de l'XP à l'esprit.
    /// Retourne true si un level up s'est produit.
    /// Le mob doit être dans la plage ±15 niveaux du joueur — vérifié par SpiritSystem.
    /// </summary>
    public bool AddXP(int amount)
    {
        if (IsMaxLevel) return false;

        currentXP += amount;
        bool leveledUp = false;

        while (currentXP >= XPRequired && !IsMaxLevel)
        {
            currentXP -= XPRequired;
            level++;
            leveledUp = true;
            Debug.Log($"[Spirit] {ItemId} → Niveau {level} !");
        }

        if (IsMaxLevel) currentXP = 0;
        return leveledUp;
    }
}
