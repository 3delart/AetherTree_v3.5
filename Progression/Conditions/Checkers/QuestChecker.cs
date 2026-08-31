using UnityEngine;

// =============================================================
// QUESTCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/QuestChecker.cs
// Ecoute : QuestEvent
// =============================================================

[System.Serializable]
public class QuestChecker : ConditionCheckerBase
{
    [Tooltip("null = n'importe quelle quête")]
    public QuestData   specificQuest  = null;
    [Tooltip("L'action qui valide la condition")]
    public QuestAction requiredAction = QuestAction.Completed;

    public override System.Type RelevantEventType => typeof(QuestEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not QuestEvent e)                      return false;
        if (e.action != requiredAction)                         return false;
        if (specificQuest != null && e.quest != specificQuest)  return false;
        return true;
    }
}
