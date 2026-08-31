using UnityEngine;

// =============================================================
// ZONECHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/ZoneChecker.cs
//
// Ecoute : ZoneEvent (publié par ZoneTrigger)
//
// Deux modes de déclenchement :
//   - Tick périodique : ZoneTrigger publie toutes les X secondes
//     → utile pour compter des présences répétées (countRequired > 1)
//   - Sortie de zone  : ZoneTrigger publie avec isFinalExit=true
//     → utile pour vérifier une durée totale (minTotalSeconds)
//
// Exemples :
//   "Rester 30s AFK sous l'arbre"
//     → zone=Zone_Sous_Arbre, mustBeAFK=true, minTotalSeconds=30
//
//   "Entrer 10 fois dans la fontaine"
//     → zone=Zone_Fontaine, countRequired=10 sur ConditionEntry
// =============================================================

[System.Serializable]
public class ZoneChecker : ConditionCheckerBase
{
    [Header("Zone")]
    [Tooltip("null = n'importe quelle zone")]
    public ZoneData zone = null;

    [Header("Durée")]
    [Tooltip("Durée minimum en secondes pour que l'event compte. 0 = pas de restriction.")]
    public float minTotalSeconds = 0f;
    [Tooltip("Si true, ne compte que l'event de sortie (durée totale). " +
             "Si false, compte aussi les ticks périodiques.")]
    public bool  onlyFinalExit  = false;

    [Header("Conditions de présence")]
    public bool mustBeAFK = false;
    public bool atNight   = false;

    [Header("Donjon")]
    public bool  isDungeon    = false;
    public bool  dungeonSolo  = false;
    public bool  dungeonNoHit = false;
    [Tooltip("0 = pas de limite de temps (speedrun). En secondes.")]
    public float speedRunMax  = 0f;

    public override System.Type RelevantEventType => typeof(ZoneEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not ZoneEvent e) return false;
        if (UnlockManager.Instance != null && UnlockManager.Instance.verboseLogs)
            Debug.Log($"[ZONECHECKER] zoneID={e.zoneID} isFinalExit={e.isFinalExit} isAFK={e.isAFK} duration={e.timeSpentSeconds}");

        // ── Zone ─────────────────────────────────────────────
        if (zone != null && e.zoneID != zone.zoneID)           return false;

        // ── Type d'event ──────────────────────────────────────
        if (onlyFinalExit && !e.isFinalExit)                   return false;

        // ── Durée ─────────────────────────────────────────────
        float duration = e.isFinalExit ? e.totalTimeSeconds : e.timeSpentSeconds;
        if (minTotalSeconds > 0 && duration < minTotalSeconds) return false;

        // ── Conditions de présence ────────────────────────────
        if (mustBeAFK && !e.isAFK)                                           return false;
        if (atNight   && !(DayNightCycle.Instance?.IsNight ?? false))        return false;

        // ── Donjon ────────────────────────────────────────────
        if (isDungeon    && !e.isDungeon)                                    return false;
        if (dungeonSolo  && !e.dungeonSolo)                                  return false;
        if (dungeonNoHit && !e.dungeonNoHit)                                 return false;
        if (speedRunMax > 0 && e.dungeonTimeSeconds > speedRunMax)           return false;

        return true;
    }
}
