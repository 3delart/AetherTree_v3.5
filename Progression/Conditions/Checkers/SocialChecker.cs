using UnityEngine;

// =============================================================
// SOCIALCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/SocialChecker.cs
// Ecoute : SocialEvent
// =============================================================

[System.Serializable]
public class SocialChecker : ConditionCheckerBase
{
    public SocialAction action        = SocialAction.Any;
    public bool         mustBeInGroup = false;

    public override System.Type RelevantEventType => typeof(SocialEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not SocialEvent e)                    return false;
        if (action != SocialAction.Any && e.action != action)  return false;
        if (mustBeInGroup && !e.isInParty)                     return false;
        return true;
    }
}
