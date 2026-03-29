using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// ELEMENTALSYSTEM.CS — Jauge d'affinité élémentaire
// Path : Assets/Scripts/Data/Elements/ElementalSystem.cs
// AetherTree GDD v3.5 — §6.1 / §6.2 / §6.3 / §6.4
//
// Logique : dilution ÉGALE
//   Quand on ajoute du poids à un élément A, l'excédent est retiré
//   ÉQUITABLEMENT sur tous les autres éléments présents (> 0).
//
// Modes élémentaires (GDD §6.3) :
//   Mono        — 1 élément ≥25% (Neutre pur inclus — affinité Neutral ≥99%)
//   Dual        — 2 éléments ≥20%, écart <10%, sort combo équipé
//   Équilibriste — aucun élément ≥25% mais plusieurs actifs
//   Neutral     — état de départ pur, aucune affinité non-neutre active
//
// Rangs Neutre (GDD §6.3) — cumulatifs, basés sur affinité Neutral :
//   1 ≥25% : +10% dégâts base
//   2 ≥50% : + +5% CritChance + 10% dégâts base
//   3 ≥75% : + +10% CritMult + 10% dégâts base + 5% CritChance
//   4 ≥90% : + −10% dégâts reçus + 10% dégâts base + 5% CritChance + 10% CritMult
//   5 100% : dégâts base →+20% · CritMult →+30% · −15% déf ennemie · −10% dégâts reçus + 5% CritChance
//
// Rangs d'affinité élémentaire (GDD §6.2) :
//
// =============================================================

public enum TitleMode { Neutral, Mono, Dual, Equilibriste }

public class ElementalSystem : MonoBehaviour
{
    [Header("Fenêtre glissante")]
    [Tooltip("Taille de la fenêtre en cast-équivalents (basic = 0.25, sort = 1.0)")]
    [SerializeField] private float _windowSize = 200f;

    public const float BASIC_ATTACK_WEIGHT = 0.25f;
    public const float SKILL_WEIGHT        = 1.0f;

    private Dictionary<ElementType, float> _weightCounts
        = new Dictionary<ElementType, float>();
    private int _totalCasts = 0;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        InitNeutralWindow();
    }

    /// <summary>
    /// Garantit que _weightCounts contient une entrée pour chaque ElementType.
    /// Sûr à appeler avant Awake() — utilisé par GetAffinity() si appelé tôt
    /// (ex: CharacterStats.RecalculateStats depuis Player.Awake). GDD §6.1.
    /// </summary>
    private void EnsureInitialized()
    {
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            if (!_weightCounts.ContainsKey(t))
                _weightCounts[t] = 0f;
        }
    }

    // =========================================================
    // INITIALISATION
    // =========================================================

    public void InitNeutralWindow()
    {
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            _weightCounts[t] = 0f;
        }
        _weightCounts[ElementType.Neutral] = _windowSize;
        _totalCasts = 0;
    }

    // =========================================================
    // CHARGEMENT SAUVEGARDE
    // =========================================================

    /// <summary>
    /// Restaure les affinités depuis la sauvegarde.
    /// Appelé par SaveSystem après un chargement.
    /// </summary>
    public void LoadAffinities(List<SavedElementAffinity> affinities)
    {
        if (affinities == null || affinities.Count == 0)
        {
            InitNeutralWindow();
            return;
        }

        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            _weightCounts[t] = 0f;
        }

        foreach (var entry in affinities)
        {
            if (Enum.TryParse(entry.element, out ElementType type)
                && type != ElementType.Any)
                _weightCounts[type] = Mathf.Max(0f, entry.weight);
        }

        Renormalize();
    }

    // =========================================================
    // ENREGISTREMENT D'UN CAST — dilution égale
    // =========================================================

    public void RegisterCast(ElementType element, bool isBasicAttack = false)
    {
        if (element == ElementType.Any) return;

        float weight = isBasicAttack ? BASIC_ATTACK_WEIGHT : SKILL_WEIGHT;
        _totalCasts++;

        // 1. Ajoute le poids à l'élément entrant
        _weightCounts[element] += weight;

        // 2. Retire équitablement sur les autres éléments présents
        float toDistribute = weight;

        while (toDistribute > 0.0001f)
        {
            var others = new List<ElementType>();
            foreach (var kvp in _weightCounts)
                if (kvp.Key != element && kvp.Value > 0f)
                    others.Add(kvp.Key);

            if (others.Count == 0) break;

            float shareEach = toDistribute / others.Count;
            float leftover  = 0f;

            foreach (ElementType key in others)
            {
                float available = _weightCounts[key];
                if (available >= shareEach)
                {
                    _weightCounts[key] -= shareEach;
                }
                else
                {
                    leftover += shareEach - available;
                    _weightCounts[key] = 0f;
                }
            }

            toDistribute = leftover;
        }

        // 3. Renormalise
        Renormalize();
    }

    // =========================================================
    // RENORMALISATION
    // =========================================================

    private void Renormalize()
    {
        float total = 0f;
        foreach (var kvp in _weightCounts)
            total += kvp.Value;

        if (total <= 0f)
        {
            InitNeutralWindow();
            return;
        }

        float factor = _windowSize / total;
        var keys = new List<ElementType>(_weightCounts.Keys);
        foreach (ElementType key in keys)
            _weightCounts[key] *= factor;
    }

    // =========================================================
    // AFFINITÉS
    // =========================================================

    /// <summary>Affinité [0..1] pour un élément. GDD §6.2.</summary>
    public float GetAffinity(ElementType element)
    {
        if (element == ElementType.Any) return 0f;
        EnsureInitialized();
        float w = 0f;
        _weightCounts.TryGetValue(element, out w);
        return Mathf.Clamp01(w / _windowSize);
    }

    /// <summary>Liste triée des affinités actives (> 0.1%).</summary>
    public List<ElementAffinityPair> GetActiveAffinities()
    {
        var result = new List<ElementAffinityPair>();
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            float aff = GetAffinity(t);
            if (aff > 0.001f)
                result.Add(new ElementAffinityPair(t, aff));
        }
        result.Sort((a, b) => b.affinity.CompareTo(a.affinity));
        return result;
    }

    /// <summary>
    /// Élément dominant (hors Neutral si un élément non-neutre domine).
    /// Retourne Neutral si aucun élément non-neutre ne dépasse l'affinité Neutral.
    /// </summary>
    public ElementType GetDominantElement()
    {
        ElementType dominant = ElementType.Neutral;
        float       maxAff   = GetAffinity(ElementType.Neutral);

        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any || t == ElementType.Neutral) continue;
            float aff = GetAffinity(t);
            if (aff > maxAff) { maxAff = aff; dominant = t; }
        }

        return dominant;
    }

    // =========================================================
    // RANG D'AFFINITÉ — GDD §6.2
    // Rang 0 : 0%     | Rang 1 : >25%  | Rang 2 : ≥50%
    // Rang 3 : ≥75%   | Rang 4 : ≥90% | Rang 5 : ≥99%
    // =========================================================

    public int GetElementRank(ElementType element)
    {
        float aff = GetAffinity(element);
        if (aff >= 0.999f) return 5;
        if (aff >= 0.899f) return 4;
        if (aff >= 0.749f) return 3;
        if (aff >= 0.499f) return 2;
        if (aff >= 0.249f) return 1;
        return 0;       
    }


    /// <summary>
    /// True si l'élément a au moins un peu d'affinité (>0%) — débloque les sorts rang 1.
    /// Distinct de GetElementRank() qui commence à ≥25%.
    /// </summary>
    public bool HasAnyAffinity(ElementType element)
        => GetAffinity(element) > 0f;

    // =========================================================
    // TITRE MODE — GDD §6.3
    // =========================================================

    /// <summary>
    /// Détermine le mode élémentaire actif. GDD §6.3.
    /// hasDualSkillEquipped : true si un sort combo Dual est placé dans la SkillBar.
    /// </summary>
    public TitleMode GetTitleMode(bool hasDualSkillEquipped = false)
    {
        // Cas Neutre pur — affinité Neutral ≥99% (fenêtre quasi-intacte)
        if (GetAffinity(ElementType.Neutral) >= 0.99f)
            return TitleMode.Mono;   // Mono Neutre — bonus Neutre actif §6.3

        ElementType dominant = GetDominantElement();
        float domAff = GetAffinity(dominant);

        // Aucune affinité non-neutre significative
        if (dominant == ElementType.Neutral || domAff < 0.01f)
            return TitleMode.Neutral;

        // Dual — 2 éléments ≥20%, écart <10%, sort combo équipé
        List<ElementType> dualCandidates = GetDualCandidates();
        if (dualCandidates.Count >= 2 && hasDualSkillEquipped)
        {
            float aff1 = GetAffinity(dualCandidates[0]);
            float aff2 = GetAffinity(dualCandidates[1]);
            if (Mathf.Abs(aff1 - aff2) < 0.10f)
                return TitleMode.Dual;
        }

        // Mono — élément dominant ≥25%
        if (domAff >= 0.25f)
            return TitleMode.Mono;

        // Équilibriste — plusieurs éléments actifs mais aucun ≥25%
        return TitleMode.Equilibriste;
    }

    /// <summary>
    /// Candidats Dual : éléments non-neutres avec affinité ≥20%.
    /// Triés par affinité décroissante. GDD §6.3.
    /// </summary>
    public List<ElementType> GetDualCandidates()
    {
        var result = new List<ElementType>();
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any || t == ElementType.Neutral) continue;
            if (GetAffinity(t) >= 0.20f) result.Add(t);
        }
        result.Sort((a, b) => GetAffinity(b).CompareTo(GetAffinity(a)));
        return result;
    }

    // =========================================================
    // BONUS DÉGÂTS ÉLÉMENTAIRES — GDD §6.2 / §6.3
    // Retourne le multiplicateur total (1.0 = pas de bonus).
    // =========================================================


    /// <summary>
    /// Bonus flat cumulé jusqu'au rang actuel. GDD §6.2.
    /// Rang 1: +10 | Rang 2: +30 | Rang 3: +60 | Rang 4: +100 | Rang 5: +200
    /// </summary>
    public float GetRankFlatBonus(ElementType element)
    {
        return GetElementRank(element) switch
        {
            1 => 10f,
            2 => 30f,
            3 => 60f,
            4 => 100f,
            5 => 200f,
            _ => 0f
        };
    }

    /// <summary>
    /// Multiplicateur % cumulé jusqu'au rang actuel. GDD §6.2.
    /// Rangs 2 et 5 donnent chacun +10% — donc rang 5 = ×1.20, rangs 2-4 = ×1.10.
    /// S'applique sur (pointsBase + flatBonus).
    /// </summary>
    public float GetRankPercentMultiplier(ElementType element)
    {
        int rank = GetElementRank(element);
        if (rank >= 5) return 1.20f;
        if (rank >= 2) return 1.10f;
        return 1.00f;
    }


    /// <summary>
    /// Points élémentaires effectifs = brut (Entity) + bonus de rang flat+% (GDD §6.2).
    /// Source unique partagée entre CombatSystem et l'UI (RefreshCardElemental).
    /// Le rang s'applique uniquement en Mono (non-Neutre) ou Dual sur les candidats.
    /// </summary>
    public float GetEffectiveElementPoints(ElementType element)
    {
        // Récupère le joueur depuis ce GameObject
        Player player = GetComponent<Player>();
        if (player == null) return 0f;

        float basePoints = player.GetElementPoints(element);

        // Le rang ne s'applique qu'en Mono (élément non-Neutre) ou Dual (candidat)
        TitleMode mode = GetTitleMode();
        bool rankApplies = (mode == TitleMode.Mono && element != ElementType.Neutral)
                        || (mode == TitleMode.Dual && GetDualCandidates().Contains(element));

        if (!rankApplies) return basePoints;

        float rankFlat = GetRankFlatBonus(element);
        float rankMult = GetRankPercentMultiplier(element);
        return (basePoints + rankFlat) * rankMult;
    }

    /// <summary>
    /// Rang 5 uniquement — réduction locale de résistance ennemie sur les dégâts élémentaires.
    /// À appeler dans CombatSystem au moment du calcul : dégâts * (1 - (resistCible - 0.1)).
    /// Retourne 0.1f si rang 5, 0f sinon.
    /// </summary>
    public float GetRank5ResistPenetration(ElementType element)
        => GetElementRank(element) == 5 ? 0.1f : 0f;

    // =========================================================
    // RANGS NEUTRE — GDD §6.3
    // Progression cumulative, s'applique uniquement si Neutral pur (≥99%).
    //
    // Rang 1 ≥25% : +10% dégâts base
    // Rang 2 ≥50% : +10% dégâts base · +5% CritChance
    // Rang 3 ≥75% : +10% dégâts base · +5% CritChance · +10% CritMult
    // Rang 4 ≥90% : +10% dégâts base · +5% CritChance · +10% CritMult · −10% dégâts reçus
    // Rang 5 100% : +20% dégâts base · +5% CritChance · +30% CritMult · −10% dégâts reçus · −15% déf ennemie
    // =========================================================

    /// <summary>
    /// Rang Neutre actif [0..5]. Basé sur l'affinité Neutral.
    /// Distinct de GetElementRank() qui s'applique aux éléments non-neutres.
    /// </summary>
    public int GetNeutralRank()
    {
        float aff = GetAffinity(ElementType.Neutral);
        if (aff >= 0.999f) return 5;
        if (aff >= 0.899f) return 4;
        if (aff >= 0.749f) return 3;
        if (aff >= 0.499f) return 2;
        if (aff >= 0.249f) return 1;
        return 0;
    }

    /// <summary>
    /// Multiplicateur de dégâts base Neutre (appliqué sur baseDamage avant réduction défense).
    /// Rang 1-4 : ×1,10 | Rang 5 : ×1,20 | Sinon : ×1,00.
    /// </summary>
    public float GetNeutralDamageMultiplier()
    {
        int rank = GetNeutralRank();
        if (rank >= 5) return 1.20f;
        if (rank >= 1) return 1.10f;
        return 1.00f;
    }

    /// <summary>
    /// Bonus de CritChance Neutre (additif). Rang 2+ : +0,07. Sinon : 0.
    /// </summary>
    public float GetNeutralCritChanceBonus()
        => GetNeutralRank() >= 2 ? 0.07f : 0f;

    /// <summary>
    /// Bonus de CritMultiplier Neutre (additif). Rang 3 : +0,10 | Rang 5 : +0,20 | Sinon : 0.
    /// </summary>
    public float GetNeutralCritMultBonus()
    {
        int rank = GetNeutralRank();
        if (rank >= 5) return 0.20f;
        if (rank >= 3) return 0.10f;
        return 0f;
    }

    /// <summary>
    /// Réduction des dégâts reçus Neutre. Rang 4+ : 0,10 (10%). Sinon : 0.
    /// À appliquer côté réception des dégâts : dmg * (1 - GetNeutralDamageReduction()).
    /// </summary>
    public float GetNeutralDamageReduction()
        => GetNeutralRank() >= 4 ? 0.10f : 0f;

    /// <summary>
    /// Pénétration d'armure Neutre — réduction % des défenses ennemies. Rang 5 : 0,07. Sinon : 0.
    /// À appliquer dans CombatSystem : def * (1 - GetNeutralArmorPenetration()).
    /// </summary>
    public float GetNeutralArmorPenetration()
        => GetNeutralRank() >= 5 ? 0.07f : 0f;

    // =========================================================
    // VULNÉRABILITÉ — GDD §6.3
    // Retourne le multiplicateur de dégâts reçus depuis le contre-élément.
    // =========================================================

    /// <summary>
    /// Multiplicateur de dégâts reçus si incomingElement est le contre-élément du joueur.
    /// Neutre : aucune vulnérabilité. Équilibriste : aucune vulnérabilité. GDD §6.3.
    /// </summary>
    public float GetVulnerability(ElementType incomingElement)
    {
        TitleMode mode = GetTitleMode();

        if (mode == TitleMode.Mono)
        {
            ElementType dominant = GetDominantElement();
            // Neutre pur — pas de vulnérabilité. GDD §6.3.
            if (dominant == ElementType.Neutral) return 1f;
            if (incomingElement != dominant.GetCounter()) return 1f;
            return GetMonoVulnerability(GetAffinity(dominant));
        }

        if (mode == TitleMode.Dual)
        {
            List<ElementType> candidates = GetDualCandidates();
            float maxBonus = 0f;
            foreach (ElementType elem in candidates)
            {
                if (incomingElement == elem.GetCounter())
                {
                    float vuln = GetDualVulnerability(GetAffinity(elem));
                    if (vuln - 1f > maxBonus) maxBonus = vuln - 1f;
                }
            }
            return 1f + maxBonus;
        }

        // Neutral / Équilibriste : pas de vulnérabilité
        return 1f;
    }

    private float GetMonoVulnerability(float aff)
    {
        if (aff >= 1.00f) return 1.25f;
        if (aff >= 0.95f) return 1.23f;
        if (aff >= 0.90f) return 1.20f;
        if (aff >= 0.80f) return 1.15f;
        if (aff >= 0.65f) return 1.10f;
        if (aff >= 0.50f) return 1.05f;
        return 1f;
    }

    private float GetDualVulnerability(float aff)
    {
        if (aff >= 0.40f) return 1.12f;
        if (aff >= 0.30f) return 1.08f;
        if (aff >= 0.20f) return 1.05f;
        return 1f;
    }

    // =========================================================
    // TITRE — GDD §6.4
    // =========================================================

    /// <summary>
    /// Construit le titre actif complet du joueur selon le mode et l'arme équipée.
    /// Format GDD §6.4 : "[Épithète]" (l'appelant préfixe le nom de classe si besoin).
    /// Retourne "Équilibriste" si mode Équilibriste (nom temp — §14.1).
    /// </summary>
    public string BuildTitle(WeaponType equippedWeapon, bool hasDualSkillEquipped = false)
    {
        TitleMode mode = GetTitleMode(hasDualSkillEquipped);

        switch (mode)
        {
            case TitleMode.Mono:
            {
                ElementType dominant = GetDominantElement();
                string ep = dominant.GetEpithet(equippedWeapon);
                Debug.Log($"Mono mode : dominant = {dominant}, épithète = '{ep}'");
                return string.IsNullOrEmpty(ep) ? dominant.GetLabel() : ep;
            }

            case TitleMode.Dual:
            {
                List<ElementType> candidates = GetDualCandidates();
                if (candidates.Count >= 2)
                {
                    string ep1 = candidates[0].GetEpithet(equippedWeapon);
                    string ep2 = candidates[1].GetEpithet(equippedWeapon);
                    if (string.IsNullOrEmpty(ep1)) ep1 = candidates[0].GetLabel();
                    if (string.IsNullOrEmpty(ep2)) ep2 = candidates[1].GetLabel();
                    return $"{ep1} et {ep2}";
                }
                goto case TitleMode.Mono;
            }

            case TitleMode.Equilibriste:
                return "Équilibriste";   // Nom temp — §14.1

            default:  // Neutral
            {
                // Mode Neutre pur → épithète Neutre de l'arme. GDD §6.4.
                string ep = ElementType.Neutral.GetEpithet(equippedWeapon);
                return string.IsNullOrEmpty(ep) ? "Aventurier" : ep;
            }
        }
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    public int   GetTotalCasts()   => _totalCasts;
    public float GetTotalWeight()  => _windowSize;
    public float GetWindowSize()   => _windowSize;

    /// <summary>
    /// Snapshot des affinités courantes pour la sauvegarde.
    /// Appelé par SaveSystem avant une sérialisation.
    /// </summary>
    public List<SavedElementAffinity> GetAffinitySnapshot()
    {
        var result = new List<SavedElementAffinity>();
        foreach (var kvp in _weightCounts)
        {
            if (kvp.Value > 0f)
                result.Add(new SavedElementAffinity { element = kvp.Key.ToString(), weight = kvp.Value });
        }
        return result;
    }
}

// =============================================================
// STRUCTS UTILITAIRES
// =============================================================

public struct ElementAffinityPair
{
    public ElementType element;
    public float       affinity;
    public ElementAffinityPair(ElementType e, float a) { element = e; affinity = a; }
}

[System.Serializable]
public class SavedElementAffinity
{
    public string element;
    public float  weight;
}


