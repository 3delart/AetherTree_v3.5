using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SpiritMilestoneTable — table de paliers PARTAGÉE entre esprits élémentaires
// Path : Assets/Scripts/Data/Equipment/SpiritMilestoneTable.cs
// AetherTree GDD v3.6 — §5.8 (table finale 2026-09-06)
//
// Fichier séparé de SpiritData.cs — Unity exige que le fichier corresponde au nom de la
// classe pour le picker "créer depuis ce champ" sur un champ d'objet ([CreateAssetMenu] seul
// tolère un mismatch via le menu Assets>Create, mais pas via ce raccourci).
//
// Un seul asset référencé par les 21 SpiritData élémentaires (7 éléments × 3 tiers) — éditer
// cet asset met à jour les 21 d'un coup. Les 4 stats de `milestones` visent TOUJOURS l'élément
// propre de l'esprit qui référence la table (SpiritInstance.Element), jamais un élément fixe.
//
// Proc debuff — séparé en 2 listes plutôt que dupliqué par élément à chaque palier
// (2026-09-06, demande Florian) :
//   procRates      — SEUL le taux [0..1] par palier (Lv50=1%, Lv70=3%, Lv100=5%), pas
//                    cumulatif (le dernier palier atteint REMPLACE, contrairement aux 4
//                    stats de `milestones` qui s'additionnent).
//   elementDebuffs — SEUL le debuff par élément (Feu→brûlure, Eau→gel, etc.), le MÊME quel
//                    que soit le palier — juste la chance change via procRates. Chaque entrée
//                    peut porter un effet OnHitDealt (proc offensif, sur la cible touchée) et/ou
//                    un effet OnHitReceived (réaction défensive, ex: CounterDebuff).
// =============================================================
[CreateAssetMenu(fileName = "spr_milestones_elemental", menuName = "AetherTree/Config/SpiritMilestoneTable")]
public class SpiritMilestoneTable : ScriptableObject
{
    public List<SpiritElementalMilestone> milestones = new List<SpiritElementalMilestone>();

    [Tooltip("Taux de proc par palier [0..1] — REMPLACE (pas cumulatif), le dernier palier\n" +
             "atteint fait foi. Ex: Lv50=0.01, Lv70=0.03, Lv100=0.05.")]
    public List<SpiritProcRate> procRates = new List<SpiritProcRate>();

    [Tooltip("Debuff appliqué par élément — 1 entrée par élément (Feu, Eau, Foudre, Terre,\n" +
             "Nature, Ténèbres, Lumière), toujours le même quel que soit le palier atteint.")]
    public List<SpiritElementalProcEffect> elementDebuffs = new List<SpiritElementalProcEffect>();

    /// <summary>Taux de proc actif au niveau donné — dernier palier atteint (0 si aucun).</summary>
    public float GetActiveProcChance(int level)
    {
        float best = 0f;
        int bestLevel = -1;
        if (procRates == null) return 0f;
        foreach (var r in procRates)
            if (r.level <= level && r.level > bestLevel) { bestLevel = r.level; best = r.chance; }
        return best;
    }

    /// <summary>Effet on-hit DEALT (debuff infligé) configuré pour un élément donné (null si aucun).</summary>
    public OnHitDealtEffectData GetDebuffEffectForElement(ElementType element)
    {
        if (elementDebuffs == null) return null;
        foreach (var e in elementDebuffs)
            if (e.element == element) return e.effect;
        return null;
    }

    /// <summary>Effet on-hit RECEIVED (réaction défensive) configuré pour un élément donné
    /// (null si aucun) — indépendant de GetDebuffEffectForElement, voir SpiritElementalProcEffect.</summary>
    public OnHitReceivedEffectData GetReceivedEffectForElement(ElementType element)
    {
        if (elementDebuffs == null) return null;
        foreach (var e in elementDebuffs)
            if (e.element == element) return e.receivedEffect;
        return null;
    }
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

[System.Serializable]
public class SpiritProcRate
{
    [Tooltip("Niveau auquel ce taux est débloqué (ex: 50, 70, 100).")]
    public int level;

    [Range(0f, 1f)]
    [Tooltip("Chance de proc par coup infligé [0..1] — remplace le taux précédent, pas cumulatif.")]
    public float chance;
}

[System.Serializable]
public class SpiritElementalProcEffect
{
    public ElementType element = ElementType.Fire;

    [Tooltip("OnHitDealtEffectData (type ApplyDebuffOnHit) — proc quand l'esprit ATTAQUE, applique\n" +
             "le debuff sur la cible touchée. Le même quel que soit le palier, seule la chance\n" +
             "change (voir procRates). Laisser vide si cet élément utilise plutôt 'received'.")]
    public OnHitDealtEffectData effect;

    [Tooltip("OnHitReceivedEffectData (ex: CounterDebuff) — proc quand le porteur REÇOIT un coup,\n" +
             "réaction défensive au lieu d'offensive (ex: renvoyer un debuff sur l'attaquant).\n" +
             "Indépendant de 'effect' — les deux peuvent être remplis en même temps si voulu.")]
    public OnHitReceivedEffectData receivedEffect;
}
