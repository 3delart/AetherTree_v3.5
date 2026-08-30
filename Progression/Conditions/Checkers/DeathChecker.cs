using UnityEngine;

// =============================================================
// DEATHCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/DeathChecker.cs
//
// Ecoute : PlayerDeathEvent
//
// Couvre :
//   - Cause élémentaire de la mort
//   - Contexte (OpenWorld, Dungeon, PvP)
//   - Killer : mob spécifique ou joueur
//
// Exemples :
//   "Mourir tué par un joueur en PvP"
//     → context=PvP, killedByPlayer=true
//
//   "Mourir d'un dégât Feu en donjon"
//     → deathElement=Fire, context=Dungeon
// =============================================================

[System.Serializable]
public class DeathChecker : ConditionCheckerBase
{
    [Header("Cause")]
    [Tooltip("Any = n'importe quel élément mortel")]
    public ElementType deathElement = ElementType.Any;

    [Header("Contexte")]
    [Tooltip("Laisser à -1/Any si le contexte n'a pas d'importance")]
    public DeathContext context     = (DeathContext)(-1); 

    [Header("Killer")]
    [Tooltip("null = n'importe quel mob tueur")]
    public MobData  killerMob      = null;
    [Tooltip("True = tué par un joueur (PvP)")]
    public bool     killedByPlayer = false;

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not PlayerDeathEvent e) return false;

        // ── Cause élémentaire ─────────────────────────────────
        if (deathElement != ElementType.Any && e.cause != deathElement) return false;

        // ── Contexte ──────────────────────────────────────────
        if ((int)context != -1 && e.context != context)                return false;

        // ── Killer ────────────────────────────────────────────
        if (killedByPlayer)
        {
            if (e.killer is not Player) return false;
        }
        else if (killerMob != null)
        {
            // e.killer is an Entity — cast to Mob to access its MobData
            var mob = e.killer as Mob;
            if (mob == null || mob.data != killerMob) return false;
        }

        return true;
    }
}
