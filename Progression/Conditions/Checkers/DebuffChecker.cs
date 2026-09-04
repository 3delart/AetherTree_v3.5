using UnityEngine;

// =============================================================
// DEBUFFCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/DebuffChecker.cs
// Ecoute : DebuffReceivedEvent
// =============================================================

[System.Serializable]
public class DebuffChecker : ConditionCheckerBase
{
    [Tooltip("True = n'importe quel debuff reçu")]
    public bool       anyDebuff  = false;
    [Tooltip("Ignoré si anyDebuff est coché")]
    public DebuffType debuffType = DebuffType.Dot;

    public override System.Type RelevantEventType => typeof(DebuffReceivedEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not DebuffReceivedEvent e) return false;
        if (e.target != player)                     return false;
        if (!anyDebuff && e.debuffType != debuffType) return false;
        return true;
    }
}
