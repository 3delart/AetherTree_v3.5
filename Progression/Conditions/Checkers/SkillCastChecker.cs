using UnityEngine;

// =============================================================
// SKILLCASTCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/SkillCastChecker.cs
// Ecoute : SkillUsedEvent
// =============================================================

[System.Serializable]
public class SkillCastChecker : ConditionCheckerBase
{
    [Tooltip("null = n'importe quel skill")]
    public SkillData   specificSkill  = null;
    [Tooltip("Any = n'importe quel élément")]
    public ElementType element        = ElementType.Any;
    public bool        mustBeCombo    = false;
    public bool        mustBeSolo     = false;
    public bool        mustBeInGroup  = false;
    [Tooltip("Any = n'importe quelle arme")]
    public WeaponType  weapon         = WeaponType.Any;
    [Tooltip("Vide = n'importe quelle zone")]
    public string      inZone         = "";

    public override System.Type RelevantEventType => typeof(SkillUsedEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not SkillUsedEvent e)                              return false;
        if (specificSkill != null && e.skill != specificSkill)              return false;
        if (element != ElementType.Any && e.primaryElement != element)     return false;
        if (mustBeCombo   && !e.isCombo)                                    return false;
        if (mustBeSolo    &&  e.isInParty)                                  return false;
        if (mustBeInGroup && !e.isInParty)                                  return false;
        if (!string.IsNullOrEmpty(inZone) && e.locationID != inZone)       return false;
        if (weapon != WeaponType.Any)
        {
            var w = player?.equippedWeaponInstance?.data?.weaponType ?? WeaponType.Any;
            if (w != weapon) return false;
        }
        return true;
    }
}
