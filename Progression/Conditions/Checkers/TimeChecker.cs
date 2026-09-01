using UnityEngine;

// =============================================================
// TIMECHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/TimeChecker.cs
//
// Ecoute : TimeEvent
//
// Couvre :
//   - Login, Logout, AFK, jours consécutifs
//   - Temps joué total (scope Character ou Account via ConditionEntry.scope)
//
// Exemples :
//   "Se connecter 7 jours consécutifs"
//     → action=ConsecutiveDay, minDays=7
//
//   "Jouer 60 min en AFK"
//     → action=AFK, minMinutes=60
//
//   "Jouer 10h sur ce personnage"
//     → action=Any, minPlaytimeMinutes=600, scope=Character sur ConditionEntry
//
//   "Jouer 10h sur le compte"
//     → action=Any, minPlaytimeMinutes=600, scope=Account sur ConditionEntry
// =============================================================

[System.Serializable]
public class TimeChecker : ConditionCheckerBase
{
    [Header("Action")]
    [Tooltip("Any = n'importe quel event temps")]
    public TimeAction action      = TimeAction.Any;

    [Tooltip("0 = pas de minimum. En minutes. Utilisé seulement si action=AFK.")]
    [ShowIf(nameof(action), TimeAction.AFK, Header = "AFK")]
    public float minMinutes       = 0f;

    [Header("Jours consécutifs")]
    [Tooltip("0 = pas de minimum")]
    public int   minDays          = 0;

    [Header("Temps joué total (session ou cumulé)")]
    [Tooltip("0 = pas de restriction. En minutes.")]
    public float minPlaytimeMinutes = 0f;

    [Tooltip("Character = vérifie e.characterPlaytimeMinutes (par perso). " +
             "Account   = vérifie e.accountPlaytimeMinutes  (cumulé compte). " +
             "Doit correspondre au scope de la ConditionEntry pour un comportement déterministe.")]
    public CounterScope playtimeScope = CounterScope.Character;

    [Header("Nuit")]
    public bool mustBeNight = false;

    public override System.Type RelevantEventType => typeof(TimeEvent);

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not TimeEvent e) return false;

        // ── Action ────────────────────────────────────────────
        if (action != TimeAction.Any && e.action != action)                           return false;

        // ── AFK ───────────────────────────────────────────────
        if (minMinutes > 0 && e.action == TimeAction.AFK && e.afkMinutes < minMinutes) return false;

        // ── Jours consécutifs ─────────────────────────────────
        if (minDays > 0 && e.consecutiveDays < minDays)                               return false;

        // ── Temps joué total ──────────────────────────────────
        // playtimeScope sélectionne le champ à lire de façon déterministe —
        // évite l'ancien OR qui acceptait dès qu'un des deux champs dépassait le seuil.
        // Le scope ici DOIT correspondre au scope de la ConditionEntry parente.
        if (minPlaytimeMinutes > 0)
        {
            float played = playtimeScope == CounterScope.Account
                ? e.accountPlaytimeMinutes
                : e.characterPlaytimeMinutes;

            if (played < minPlaytimeMinutes) return false;
        }

        // ── Nuit ──────────────────────────────────────────────
        if (mustBeNight && !(DayNightCycle.Instance?.IsNight ?? false)) return false;

        return true;
    }
}
