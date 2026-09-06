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
//   XP accordée par mobs dans la plage ±15 niveaux du joueur (≥1 hit, seuil bas — voir
//   XPSystem.GiveSpiritXP), seul l'esprit ÉQUIPÉ xp (pas ceux en inventaire).
//   Obtention principale : conditions in-game (IConditionChecker).
//
// Système de tiers (2026-09-06, décision Florian) — 3 SpiritData SÉPARÉES par élément,
// pas un tier enum sur un seul asset :
//   Common       — requiredLevel 1,  maxLevel 50
//   Intermediate — requiredLevel 40, maxLevel 70
//   Advanced     — requiredLevel 80, maxLevel 100
//   Tier supérieur = repart à level 1 (pas de transfert — futur "Spirit Fusion" éventuel,
//   voir mémoire project_aethertree_spirit_system).
//
// Esprit Neutre (GDD §5.8) :
//   Équipé à la place d'un esprit élémentaire.
//   N'apporte aucun point élémentaire.
//   Bonus via config.bonuses : BonusAttack%, CritChance, MaxHP.
//
// Points élémentaires (esprits non-Neutre uniquement) — formule universelle, voir
// GetPointsAtLevel : 1 à 10 pts/niveau par bande de 10 niveaux, 550 points cumulés à 100.
//
// Paliers élémentaires (2026-09-06, table finale Florian) — TOUJOURS liés au SEUL élément
// de l'esprit, jamais les autres. TOUT (4 stats + proc) vit dans UNE SEULE SpiritMilestoneTable
// partagée entre les 7 éléments — le proc de chaque palier porte une entrée par élément
// (SpiritElementalMilestone.procs), l'esprit équipé ne lisant que celle qui le concerne :
//   Lv10  : +2% résistance propre à l'élément
//   Lv20  : +2% dégâts de l'élément
//   Lv30  : -2% coût mana des skills de l'élément
//   Lv40  : +4% résistance propre (cumulé, pas remplacé)
//   Lv50  : -4% pénétration résist. ennemie de l'élément + 1% proc debuff au hit
//   Lv60  : +4% dégâts de l'élément
//   Lv70  : -6% pénétration + 3% proc debuff
//   Lv80  : +6% résistance propre + -4% coût mana
//   Lv90  : +6% dégâts de l'élément
//   Lv100 : -8% pénétration + 5% proc debuff
//   (Common s'arrête à Lv50, Intermediate à Lv70 — seuls les paliers ≤ maxLevel comptent.)
// Esprit Neutre : paliers séparés via SpiritMilestone.bonuses (StatBonus, BonusAttack/
// CritChance/BonusHP), voir plus bas dans ce fichier.
// =============================================================

[CreateAssetMenu(fileName = "spr_", menuName = "AetherTree/Inventaire/Equipement/SpiritData")]
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
    // Formule universelle (2026-09-06, décision Florian) — même "palier" que GetXPRequired :
    // palier = floor((niveau-1)/10)+1, gain = palier points CE niveau (1 pt/niveau en 1-10,
    // 2 pts/niveau en 11-20, ..., 10 pts/niveau en 91-100). Total cumulé à 100 = 550 points
    // (10 paliers × 10 niveaux × palier = 10×55). Pas de champ à calibrer par asset — la
    // courbe est fixe pour tous les esprits élémentaires, seul maxLevel (tier) change combien
    // de paliers sont atteignables. Remplace l'ancienne courbe Lerp/exponent (jamais calibrée,
    // pointsAtLevel1/pointsAtMaxLevel/pointsCurveExponent retirés).

    // ── Progression ───────────────────────────────────────────
    [Header("Progression")]
    [Tooltip("Niveau maximum de cet esprit — dépend du tier de l'asset : 50 (Common), " +
             "70 (Intermediate), 100 (Advanced).")]
    public int maxLevel = 50;

    // ── Paliers de bonus — Esprit NEUTRE uniquement ────────────
    // Esprits élémentaires : voir sharedElementalMilestones ci-dessous — les 4 stats ET le
    // proc sont TOUS dans cette table partagée (2026-09-06, demande Florian) plutôt que
    // dupliqués sur 21 assets ; seul l'élément CIBLE lu diffère par esprit.
    [ShowIf(nameof(element), ElementType.Neutral, Header = "Paliers de bonus (Esprit Neutre — BonusAttack/CritChance/BonusHP)")]
    [Tooltip("Bonus débloqués à certains niveaux, cumulatifs (chaque palier atteint s'additionne\n" +
             "aux précédents). Esprit Neutre uniquement — recommandé : BonusAttack, CritChance, BonusHP.")]
    public List<SpiritMilestone> milestones = new List<SpiritMilestone>();

    // ── Paliers de bonus — Esprits ÉLÉMENTAIRES uniquement ─────
    [ShowIf(nameof(element), ElementType.Fire, ElementType.Water, ElementType.Lightning, ElementType.Earth, ElementType.Nature, ElementType.Darkness, ElementType.Light, Header = "Paliers élémentaires (table finale 2026-09-06)")]
    [Tooltip("Table PARTAGÉE entre les 7 esprits élémentaires — TOUT y est (4 stats + proc par\n" +
             "élément), un seul asset SpiritMilestoneTable à créer/éditer pour affecter les 21\n" +
             "SpiritData élémentaires (7 éléments × 3 tiers) d'un coup. Les 4 stats visent\n" +
             "toujours l'élément CIBLE (= data.element de cet asset) ; le proc va chercher dans\n" +
             "SpiritElementalMilestone.procs l'entrée dont l'élément correspond à data.element.")]
    public SpiritMilestoneTable sharedElementalMilestones;

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Points élémentaires apportés AU niveau N (pas cumulé — voir
    /// GetTotalPointsAtLevel pour le cumul). Retourne 0 si element == Neutral (Esprit Neutre).
    /// Formule universelle : palier = floor((N-1)/10)+1, gain = palier points (2026-09-06).</summary>
    public int GetPointsAtLevel(int level)
    {
        if (element == ElementType.Neutral) return 0;
        return (level - 1) / 10 + 1;
    }

    /// <summary>XP requis pour passer du niveau N au niveau N+1 — formule GDD §5.8 :
    /// 100 + (palier×20)×N, où palier = floor((N-1)/10)+1 (1 pour N=1-10, 2 pour N=11-20, etc.).
    /// Remplace l'ancienne formule 100×N^1.3 (bien plus dure tôt, plus douce en fin — écart
    /// vérifié par comparaison chiffrée avec Florian). Décision explicite Florian (2026-09-06).</summary>
    public int GetXPRequired(int level)
    {
        int palier = (level - 1) / 10 + 1;
        return 100 + (palier * 20) * level;
    }

    /// <summary>Points élémentaires cumulés de niveau 1 à N.</summary>
    public int GetTotalPointsAtLevel(int level)
    {
        int total = 0;
        for (int i = 1; i <= Mathf.Min(level, maxLevel); i++)
            total += GetPointsAtLevel(i);
        return total;
    }

    /// <summary>Retourne les bonus du palier Neutre atteint à ce niveau (null si aucun).
    /// Esprit Neutre uniquement — voir sharedElementalMilestones pour les esprits élémentaires.</summary>
    public SpiritMilestone GetMilestone(int level)
    {
        if (milestones == null) return null;
        foreach (var m in milestones)
            if (m.level == level) return m;
        return null;
    }
}

// =============================================================
// SpiritMilestone — Bonus débloqué à un palier de niveau, ESPRIT NEUTRE UNIQUEMENT
// (esprits élémentaires : voir SpiritMilestoneTable/SpiritElementalMilestone plus bas)
// =============================================================
[System.Serializable]
public class SpiritMilestone
{
    [Tooltip("Niveau auquel ce palier est débloqué (ex: 10, 20, 30, 40, 50).")]
    public int level;

    [Tooltip("Bonus débloqués à ce palier — cumulatifs avec tous les paliers déjà atteints.\n" +
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
    public int         RequiredLevel => data != null ? data.requiredLevel : 1;
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
