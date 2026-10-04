using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// REQUIREMENTSET.CS — Prérequis composables AND/OR/NOT, réutilisables
// Path : Assets/Scripts/Data/Requirements/RequirementSet.cs
// AetherTree GDD v31 — §25 (refonte quêtes, 2026-10-04)
//
// Remplace QuestData.minLevel/prerequisiteQuest par un objet composable.
// Nommé RequirementSet/Requirement — pas PNJCondition* (plus PNJ-spécifique
// maintenant), pas Condition* (collision avec Progression/Conditions/
// ConditionData.cs, système d'achievement événementiel sans rapport).
//
// Scopé au gating de quête (QuestData.requirements) pour ce chantier —
// réutilisation côté sélection de dialogue PNJ (chantier séparé, en pause)
// prévue mais pas branchée ici.
//
// 4 types seulement — pas de "Reputation" par faction : Player ne stocke
// aucun score par faction (seul prestigeRank existe), un tel champ serait
// non fonctionnel (vérifié avant d'écrire ce fichier, voir le spec).
// =============================================================

public enum RequirementField { Level, PrestigeRank, QuestState, ItemOwned }

[Serializable]
public class Requirement
{
    public RequirementField field = RequirementField.Level;

    [ShowIf(nameof(field), RequirementField.Level)]
    public int minLevel = 1;

    [ShowIf(nameof(field), RequirementField.PrestigeRank)]
    public int minPrestigeRank = 0;

    [ShowIf(nameof(field), RequirementField.QuestState)]
    public QuestData quest;
    [ShowIf(nameof(field), RequirementField.QuestState)]
    public QuestState requiredState = QuestState.TurnedIn;

    [Tooltip("ATTENTION : InventorySystem.GetItemCount() ne sait compter que ResourceData et\n" +
             "ConsumableData — un item d'un autre type (Weapon/Armor/...) rendra ce prérequis\n" +
             "TOUJOURS faux. Un warning Console signale ce cas depuis l'Inspector de QuestData.")]
    [ShowIf(nameof(field), RequirementField.ItemOwned)]
    public ItemData item;
    [ShowIf(nameof(field), RequirementField.ItemOwned)]
    public int itemCount = 1;

    /// <summary>True si CE prérequis seul est rempli pour ce joueur.</summary>
    public bool IsMet(Player player)
    {
        if (player == null) return false;

        switch (field)
        {
            case RequirementField.Level:
                return player.level >= minLevel;

            case RequirementField.PrestigeRank:
                return player.prestigeRank >= minPrestigeRank;

            case RequirementField.QuestState:
                if (quest == null) return true; // pas de quête assignée = condition vide, toujours vraie
                return QuestSystem.Instance != null
                    && QuestSystem.Instance.GetQuestState(quest) == requiredState;

            case RequirementField.ItemOwned:
                if (item == null) return true; // pas d'item assigné = condition vide, toujours vraie
                return InventorySystem.Instance != null
                    && InventorySystem.Instance.GetItemCount(item) >= Mathf.Max(1, itemCount);

            default:
                return true;
        }
    }
}

[Serializable]
public class RequirementSet
{
    public List<Requirement> requirements = new List<Requirement>();

    [Tooltip("false = toutes les conditions doivent être vraies (AND).\n" +
             "true = au moins une condition doit être vraie (OR).")]
    public bool requireAny = false;

    [Tooltip("Inverse le résultat final (NOT) — coché = le résultat normal doit être FAUX pour\n" +
             "que IsMet() renvoie vrai.")]
    public bool reverseMatch = false;

    /// <summary>True si l'ensemble des prérequis est rempli pour ce joueur. Une liste vide est
    /// toujours considérée remplie (aucun prérequis = toujours accessible).</summary>
    public bool IsMet(Player player)
    {
        bool result;

        if (requirements == null || requirements.Count == 0)
        {
            result = true;
        }
        else if (requireAny)
        {
            result = false;
            foreach (var r in requirements)
                if (r != null && r.IsMet(player)) { result = true; break; }
        }
        else
        {
            result = true;
            foreach (var r in requirements)
                if (r != null && !r.IsMet(player)) { result = false; break; }
        }

        return reverseMatch ? !result : result;
    }
}
