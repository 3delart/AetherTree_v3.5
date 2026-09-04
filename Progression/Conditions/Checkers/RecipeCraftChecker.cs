using UnityEngine;

// =============================================================
// RECIPECRAFTCHECKER.CS
// Path : Assets/Scripts/Progression/Conditions/Checkers/RecipeCraftChecker.cs
//
// Ecoute : RecipeCraftedEvent
//
// Couvre :
//   - Craft de n'importe quelle recette, ou d'une recette précise
//     (ex: "craft 100 petites potions de soin" → débloque la recette suivante)
// =============================================================

[System.Serializable]
public class RecipeCraftChecker : ConditionCheckerBase
{
    [Tooltip("null = n'importe quelle recette")]
    public RecipeData specificRecipe = null;

    public override System.Type RelevantEventType => typeof(RecipeCraftedEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not RecipeCraftedEvent e) return false;
        if (specificRecipe != null && e.recipe != specificRecipe) return false;
        return true;
    }
}
