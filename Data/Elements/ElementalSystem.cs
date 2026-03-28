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
// Rangs d'affinité (GDD §6.2) :
//   0 — 0%      : aucun bonus
//   1 — >0%     : +5%
//   2 — ≥25%    : +10%  (seuil Mono)
//   3 — ≥50%    : +15%
//   4 — ≥75%    : +20%
//   5 — ≥90%    : +25%
// =============================================================

public enum TitleMode { Neutral, Mono, Dual, Equilibriste }

public class ElementalSystem : MonoBehaviour
{
    [Header("Fenêtre glissante")]
    [Tooltip("Taille de la fenêtre en cast-équivalents (basic = 0.25, sort = 1.0)")]
    [SerializeField] private float _windowSize = 200f;

    public const float BASIC_ATTACK_WEIGHT = 0.25f;
    public const float SKILL_WEIGHT        = 1.0f;

    private Dictionary<ElementType, float> _weightCounts;
    private int _totalCasts = 0;

    private void Awake()
    {
        _weightCounts = new Dictionary<ElementType, float>();
        foreach (ElementType t in Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            _weightCounts[t] = 0f;
        }
    }

    private void Start()
    {
        InitNeutralWindow();
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
    // Rang 0 : 0%     | Rang 1 : >0%  | Rang 2 : ≥25%
    // Rang 3 : ≥50%   | Rang 4 : ≥75% | Rang 5 : ≥90%
    // =========================================================

    public int GetElementRank(ElementType element)
    {
        float aff = GetAffinity(element);
        if (aff <= 0f)    return 0;
        if (aff < 0.25f)  return 1;
        if (aff < 0.50f)  return 2;
        if (aff < 0.75f)  return 3;
        if (aff < 0.90f)  return 4;
        return 5;
    }

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
    /// Multiplicateur de dégâts élémentaires pour un élément donné.
    /// Prend en compte le rang d'affinité et le mode actif (§6.2 / §6.3).
    /// </summary>
    public float GetElementalDamageBonus(ElementType element)
    {
        int rank = GetElementRank(element);
        TitleMode mode = GetTitleMode();

        float bonus;
        switch (rank)
        {
            case 1:  bonus = 0.05f; break;  // +5%
            case 2:  bonus = 0.10f; break;  // +10%
            case 3:  bonus = 0.15f; break;  // +15%
            case 4:  bonus = 0.20f; break;  // +20%
            case 5:  bonus = 0.25f; break;  // +25%
            default: bonus = 0.00f; break;
        }

        // Dual : 75% du bonus Mono sur chaque élément. GDD §6.3.
        if (mode == TitleMode.Dual)
            bonus *= 0.75f;

        // Équilibriste : +5% supplémentaires sur tous les éléments actifs. GDD §6.3.
        if (mode == TitleMode.Equilibriste && rank > 0)
            bonus += 0.05f;

        return 1f + bonus;
    }

    /// <summary>
    /// Bonus Neutre pur — +20% dégâts si l'affinité Neutral ≥99%. GDD §6.3.
    /// Lire avec GetTitleMode() == Mono ET GetDominantElement() == Neutral.
    /// </summary>
    public float GetNeutralBonus()
        => GetAffinity(ElementType.Neutral) >= 0.99f ? 1.20f : 1.00f;

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
