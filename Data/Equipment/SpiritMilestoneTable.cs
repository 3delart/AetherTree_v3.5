using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SpiritMilestoneTable — table de paliers PARTAGÉE entre TOUS les esprits (Neutre inclus)
// Path : Assets/Scripts/Data/Equipment/SpiritMilestoneTable.cs
// AetherTree GDD v3.6 — §5.8 (table finale 2026-09-07)
//
// Fichier séparé de SpiritData.cs — Unity exige que le fichier corresponde au nom de la
// classe pour le picker "créer depuis ce champ" sur un champ d'objet ([CreateAssetMenu] seul
// tolère un mismatch via le menu Assets>Create, mais pas via ce raccourci).
//
// Un seul asset référencé par les 21 SpiritData élémentaires (7 éléments × 3 tiers) ET l'Esprit
// Neutre — éditer cet asset met à jour tout le monde d'un coup.
//
// `milestones` — 4 stats visant TOUJOURS l'élément propre de l'esprit qui référence la table
// (SpiritInstance.Element), cumulatives (chaque palier atteint s'additionne aux précédents).
//
// `elementProcs` (2026-09-07, refonte demande Florian) — remplace l'ancien procRates +
// elementDebuffs (1 seul effet/élément, chance qui scalait). Maintenant : par élément (7 +
// Neutre = 8 entrées), une LISTE de procs INDÉPENDANTS, chacun avec son propre niveau, sa
// propre direction (Dealt/Received), son propre effet et son propre taux. Plusieurs procs au
// même élément stackent (rollés indépendamment) — permet par ex. 2 entrées "brûlure 1%" à
// Lv50 et Lv100 pour ~2% cumulé, ou des effets DIFFÉRENTS à chaque palier (Lv50=heal reçu,
// Lv70=slow infligé, Lv100=freeze infligé). Neutre est juste une entrée `element` de plus —
// peut porter un buff (ApplyBuffOnHit/CounterBuff/HealOnHit) autant qu'un debuff.
// =============================================================
[CreateAssetMenu(fileName = "spr_milestones_elemental", menuName = "AetherTree/Config/SpiritMilestoneTable")]
public class SpiritMilestoneTable : ScriptableObject
{
    // ── Gain par niveau (2026-09-07, demande Florian) ──────────
    // 2 listes SÉPARÉES (pas une liste unique à 5 champs — trop confus, un esprit élémentaire
    // n'utilise jamais les 4 champs Neutre et vice-versa). Liste ÉDITABLE, un niveau = une
    // ligne — plus de formule/courbe fixe. Total à un niveau donné = somme de toutes les
    // lignes ≤ ce niveau. Un niveau sans ligne = 0 gagné.
    [Header("Gain par niveau — Esprits élémentaires (points)")]
    public List<SpiritElementalPointGain> elementalPointGains = new List<SpiritElementalPointGain>();

    [Header("Gain par niveau — Esprit Neutre (stats)")]
    public List<SpiritNeutralStatGain> neutralStatGains = new List<SpiritNeutralStatGain>();

    /// <summary>Points élémentaires cumulés jusqu'au niveau donné (esprits élémentaires).</summary>
    public float GetElementalPointsAt(int level)
    {
        float sum = 0f;
        if (elementalPointGains == null) return sum;
        foreach (var g in elementalPointGains)
            if (g.level <= level) sum += g.elementalPoints;
        return sum;
    }

    /// <summary>Attaque Neutre cumulée jusqu'au niveau donné.</summary>
    public float GetNeutralAttackAt(int level) => SumNeutral(level, g => g.neutralAttack);
    /// <summary>HP Neutre cumulés jusqu'au niveau donné.</summary>
    public float GetNeutralHPAt(int level) => SumNeutral(level, g => g.neutralHP);
    /// <summary>Crit Chance Neutre cumulée jusqu'au niveau donné.</summary>
    public float GetNeutralCritChanceAt(int level) => SumNeutral(level, g => g.neutralCritChance);
    /// <summary>Résist ALL Neutre cumulée jusqu'au niveau donné.</summary>
    public float GetNeutralResistAllAt(int level) => SumNeutral(level, g => g.neutralResistAll);

    private float SumNeutral(int level, System.Func<SpiritNeutralStatGain, float> selector)
    {
        if (neutralStatGains == null) return 0f;
        float sum = 0f;
        foreach (var g in neutralStatGains)
            if (g.level <= level) sum += selector(g);
        return sum;
    }

    [Header("Paliers — Esprits élémentaires")]
    public List<SpiritElementalMilestone> milestones = new List<SpiritElementalMilestone>();

    [Header("Paliers — Esprit Neutre")]
    [Tooltip("StatBonus (CritMultiplier/AllDefense recommandés), cumulatif comme `milestones`.\n" +
             "Procs Neutre : voir elementProcs (element=Neutral).")]
    public List<SpiritMilestone> neutralMilestones = new List<SpiritMilestone>();

    [Header("Procs — tous esprits (Neutre inclus)")]
    [Tooltip("Procs par élément (Feu, Eau, Foudre, Terre, Nature, Ténèbres, Lumière, Neutre) —\n" +
             "chaque élément a sa propre liste de procs indépendants (niveau/direction/effet/taux).")]
    public List<SpiritElementalProcSet> elementProcs = new List<SpiritElementalProcSet>();

    /// <summary>Tous les procs actifs (niveau atteint) pour un élément donné.</summary>
    public IEnumerable<SpiritElementalProcEntry> GetActiveProcs(ElementType element, int level)
    {
        if (elementProcs == null) yield break;
        foreach (var set in elementProcs)
        {
            if (set.element != element || set.procs == null) continue;
            foreach (var p in set.procs)
                if (p.level <= level)
                    yield return p;
        }
    }
}

[System.Serializable]
public class SpiritElementalPointGain
{
    [Tooltip("Niveau auquel ce gain est accordé (1-100).")]
    public int level;

    [Tooltip("Points élémentaires gagnés À CE niveau.")]
    public float elementalPoints;
}

[System.Serializable]
public class SpiritNeutralStatGain
{
    [Tooltip("Niveau auquel ce gain est accordé (1-100).")]
    public int level;

    [Tooltip("Attaque gagnée À CE niveau (flat).")]
    public float neutralAttack;

    [Tooltip("HP gagnés À CE niveau (flat).")]
    public float neutralHP;

    [Tooltip("Crit Chance gagnée À CE niveau (ratio, ex: 0.002 = 0.2%).")]
    public float neutralCritChance;

    [Tooltip("Résistance ALL gagnée À CE niveau (ratio, ex: 0.003 = 0.3%).")]
    public float neutralResistAll;
}

[System.Serializable]
public class SpiritElementalMilestone
{
    [Tooltip("Niveau auquel ce palier est débloqué (ex: 10, 20, 30, 40, 50, 60, 70, 80, 90, 100).")]
    public int level;

    [Tooltip("+% résistance à l'élément propre de l'esprit — cumulatif avec les paliers précédents.")]
    public float resistBonusPercent;

    [Tooltip("+% dégâts de l'élément propre de l'esprit — cumulatif avec les paliers précédents.")]
    public float elementalDamageBonusPercent;

    [Tooltip("-% coût mana des skills de l'élément propre de l'esprit — cumulatif.")]
    public float manaCostReductionPercent;

    [Tooltip("-% résistance ennemie de l'élément propre de l'esprit (pénétration) — cumulatif.")]
    public float resistPenetrationPercent;
}

/// <summary>Dealt = proc en infligeant un coup (cible touchée). Received = proc en recevant un
/// coup (soi-même) — réaction défensive.</summary>
public enum OnHitProcDirection { Dealt = 0, Received = 1 }

[System.Serializable]
public class SpiritElementalProcSet
{
    [Tooltip("Élément ciblé — Neutral inclus (l'Esprit Neutre lit cette même liste).")]
    public ElementType element = ElementType.Fire;

    public List<SpiritElementalProcEntry> procs = new List<SpiritElementalProcEntry>();
}

[System.Serializable]
public class SpiritElementalProcEntry
{
    [Tooltip("Niveau auquel ce proc est débloqué (ex: 50, 70, 100).")]
    public int level;

    [Tooltip("Dealt = proc en infligeant un coup (sur la cible touchée).\n" +
             "Received = proc en recevant un coup (sur/pour soi-même).")]
    public OnHitProcDirection direction = OnHitProcDirection.Dealt;

    [Tooltip("Effet infligé — buff sur soi (ApplyBuffOnHit) ou debuff sur la cible (ApplyDebuffOnHit).")]
    [ShowIf(nameof(direction), OnHitProcDirection.Dealt)]
    public OnHitDealtEffectData dealtEffect;

    [Tooltip("Effet reçu — réaction défensive (Thorns, CounterDebuff, CounterBuff, HealOnHit...).")]
    [ShowIf(nameof(direction), OnHitProcDirection.Received)]
    public OnHitReceivedEffectData receivedEffect;

    [Range(0f, 1f)]
    [Tooltip("Chance de déclenchement [0..1] pour CE proc précis, indépendante des autres.")]
    public float chance = 0.01f;
}
