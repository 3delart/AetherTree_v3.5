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
// Esprit Neutre (GDD §5.8, table finale 2026-09-07) :
//   Équipé à la place d'un esprit élémentaire — se spécialise pas, mix de plusieurs stats
//   génériques au lieu de dégâts d'un seul élément. Justification lore : pas une force
//   élémentaire externe comme les 7 autres, mais l'esprit du joueur lui-même — modeste mais
//   jamais contré par une résistance ennemie (contrairement aux dégâts élémentaires).
//   Gain par NIVEAU (GetNeutralXAtLevel) — liste éditable ligne par ligne (level → valeur),
//   voir sharedElementalMilestones.neutralStatGains (2026-09-07, plus de courbe/formule
//   automatique, plus de constantes codées en dur) : Attaque / HP / CritChance / Resist ALL,
//   n'importe quel niveau, n'importe quelle valeur.
//   Paliers (SpiritMilestone.bonuses, DISTINCTS du continu) : CritMultiplier, AllDefense.
//   Procs : voir SpiritMilestoneTable.elementProcs (element=Neutral), buff ou debuff au choix.
//
// Points élémentaires (esprits non-Neutre uniquement) — voir GetTotalPointsAtLevel, liste
// éditable ligne par ligne sur sharedElementalMilestones.elementalPointGains (2026-09-07).
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
// Esprit Neutre (2026-09-07) : paliers dans la MÊME table partagée, liste séparée
// (SpiritMilestoneTable.neutralMilestones, StatBonus CritMultiplier/AllDefense + proc
// dealt/received) — voir SpiritMilestoneTable.cs pour la classe SpiritMilestone.
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

    [Tooltip("Niveau de départ d'un NOUVEL esprit de ce tier (défaut 1, tier Common). Permet à\n" +
             "un tier supérieur de démarrer avec une avance au lieu de repartir de zéro — ex:\n" +
             "l'Esprit Intermédiaire (maxLevel 70) peut commencer à 40 plutôt qu'à 1. Différent\n" +
             "de la 'Spirit Fusion' (transfert du niveau EXACT d'un esprit existant, pas encore\n" +
             "implémenté) — ici c'est un point de départ FIXE par asset, pas dynamique.")]
    public int startingLevel = 1;

    // ── Paliers de bonus — TOUS les esprits (Neutre + élémentaires) ─────
    // Une seule table partagée (2026-09-07, demande Florian) — toute la config Spirit au même
    // endroit. Élémentaires : lit `milestones`/`procRates`/`elementDebuffs` (ciblés sur
    // data.element). Neutre : lit `neutralMilestones` (StatBonus + proc dealt/received,
    // structure différente — pas d'élément à cibler).
    [Tooltip("Table PARTAGÉE entre TOUS les esprits — 1 seul asset SpiritMilestoneTable à\n" +
             "créer/éditer pour affecter les 21 SpiritData élémentaires ET l'Esprit Neutre d'un coup.")]
    public SpiritMilestoneTable sharedElementalMilestones;

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>XP requis pour passer du niveau N au niveau N+1 — formule GDD §5.8 :
    /// 100 + (palier×20)×N, où palier = floor((N-1)/10)+1 (1 pour N=1-10, 2 pour N=11-20, etc.).
    /// Remplace l'ancienne formule 100×N^1.3 (bien plus dure tôt, plus douce en fin — écart
    /// vérifié par comparaison chiffrée avec Florian). Décision explicite Florian (2026-09-06).</summary>
    public int GetXPRequired(int level)
    {
        int palier = (level - 1) / 10 + 1;
        return 100 + (palier * 20) * level;
    }

    /// <summary>Points élémentaires cumulés de niveau 1 à N — somme des lignes
    /// SpiritMilestoneTable.elementalPointGains (liste éditable, 2026-09-07). 0 si
    /// element == Neutral ou si sharedElementalMilestones n'est pas assignée.</summary>
    public int GetTotalPointsAtLevel(int level)
    {
        if (element == ElementType.Neutral || sharedElementalMilestones == null) return 0;
        return Mathf.RoundToInt(sharedElementalMilestones.GetElementalPointsAt(Mathf.Min(level, maxLevel)));
    }

    // ── Esprit Neutre — gain par niveau (2026-09-07) ───────────
    // Réparti sur 4 stats génériques (le Neutre ne se spécialise pas), distinct des paliers
    // (CritMultiplier/AllDefense/proc, voir SpiritMilestone) pour ne jamais donner la même
    // chose. Valeurs éditées ligne par ligne sur sharedElementalMilestones.neutralStatGains.
    public float GetNeutralBonusAttackAtLevel(int level)
        => element == ElementType.Neutral && sharedElementalMilestones != null
            ? sharedElementalMilestones.GetNeutralAttackAt(Mathf.Min(level, maxLevel)) : 0f;

    public float GetNeutralBonusHPAtLevel(int level)
        => element == ElementType.Neutral && sharedElementalMilestones != null
            ? sharedElementalMilestones.GetNeutralHPAt(Mathf.Min(level, maxLevel)) : 0f;

    public float GetNeutralCritChanceAtLevel(int level)
        => element == ElementType.Neutral && sharedElementalMilestones != null
            ? sharedElementalMilestones.GetNeutralCritChanceAt(Mathf.Min(level, maxLevel)) : 0f;

    public float GetNeutralResistAllAtLevel(int level)
        => element == ElementType.Neutral && sharedElementalMilestones != null
            ? sharedElementalMilestones.GetNeutralResistAllAt(Mathf.Min(level, maxLevel)) : 0f;
}

// =============================================================
// SpiritMilestone — Bonus débloqué à un palier de niveau, ESPRIT NEUTRE UNIQUEMENT
// Utilisé par SpiritMilestoneTable.neutralMilestones (Data/Equipment/SpiritMilestoneTable.cs)
// — reste défini ici pour rester proche de GetNeutral*AtLevel ci-dessus. Esprits élémentaires :
// voir SpiritElementalMilestone dans SpiritMilestoneTable.cs. Procs (Neutre ET élémentaires) :
// voir SpiritMilestoneTable.elementProcs (mécanisme unifié, 2026-09-07) — plus de proc ici.
// =============================================================
[System.Serializable]
public class SpiritMilestone
{
    [Tooltip("Niveau auquel ce palier est débloqué (ex: 10, 20, 30, 40, 50, 60, 70, 80, 90, 100).")]
    public int level;

    [Tooltip("Bonus débloqués à ce palier — cumulatifs avec tous les paliers déjà atteints.\n" +
             "Distinct du gain continu (Attaque/HP/CritChance/ResistAll) — recommandé ici :\n" +
             "CritMultiplier, AllDefense (table finale 2026-09-07).")]
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
        level     = source != null ? Mathf.Clamp(source.startingLevel, 1, source.maxLevel) : 1;
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
