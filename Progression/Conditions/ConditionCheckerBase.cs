using UnityEngine;

// =============================================================
// CONDITIONCHECKERBASE.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Core/ConditionCheckerBase.cs
//
// Classe de base abstraite pour tous les checkers inline.
// Sérialisée directement dans ConditionEntry via [SerializeReference].
// Pas de ScriptableObject séparé — tout vit dans le ConditionData.
//
// Pour créer un nouveau checker :
//   1. Créer une classe héritant de ConditionCheckerBase
//   2. Ajouter [System.Serializable]
//   3. Implémenter Evaluate()
//   → Apparaît automatiquement dans le dropdown Inspector
// =============================================================

[System.Serializable]
public abstract class ConditionCheckerBase
{
    /// <summary>
    /// Retourne true si l'event satisfait cette condition pour ce joueur.
    /// Appelé uniquement si les filtres globaux (levelMin/Max, maxWinners)
    /// de ConditionEntry sont déjà passés.
    /// </summary>
    public abstract bool Evaluate(object gameEvent, Player player);

    /// <summary>
    /// Type d'event GameEventBus auquel ce checker répond (le même type que le
    /// "is XEvent" en tête de son Evaluate()). Utilisé par UnlockManager pour
    /// indexer les ConditionData par type d'event et éviter de parcourir toutes
    /// les conditions du jeu à chaque event — voir UnlockManager._conditionsByEventType.
    /// </summary>
    public abstract System.Type RelevantEventType { get; }
}
