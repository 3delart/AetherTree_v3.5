using UnityEngine;

// =============================================================
// NPCINTERACTCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/NpcInteractChecker.cs
// Ecoute : NpcInteractEvent
// =============================================================

[System.Serializable]
public class NpcInteractChecker : ConditionCheckerBase
{
    [Tooltip("Vide = n'importe quel NPC")]
    public string       npcID  = "";
    [Tooltip("Any = n'importe quel type d'interaction")]
    public InteractType action = InteractType.Any;

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not NpcInteractEvent e)                return false;
        if (!string.IsNullOrEmpty(npcID) && e.npcID != npcID)  return false;
        if (action != InteractType.Any && e.action != action)   return false;
        return true;
    }
}
