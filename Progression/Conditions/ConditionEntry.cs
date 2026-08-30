using UnityEngine;

// =============================================================
// CONDITIONENTRY.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Core/ConditionEntry.cs
//
// Une sous-condition d'un ConditionData.
// Contient :
//   - Le checker inline (logique de vérification)
//   - Les filtres globaux (level, maxWinners) appliqués AVANT le checker
//   - Le compteur (combien de fois le checker doit retourner true)
//   - Le scope (Character / Account / Server)
// =============================================================

[System.Serializable]
public class ConditionEntry
{
    // ── Checker — toute la logique est ici ───────────────────
    [SerializeReference]
    public ConditionCheckerBase checker = null;

    // ── Compteur ──────────────────────────────────────────────
    [Tooltip("Nombre de fois que le checker doit retourner true.")]
    public int countRequired = 1;

    // ── Filtres globaux — vérifiés AVANT le checker ───────────
    [Tooltip("Niveau minimum du joueur requis pour que cette entry compte. 0 = pas de restriction.")]
    public int playerLevelMin = 0;

    [Tooltip("Niveau maximum du joueur pour que cette entry compte. 0 = pas de restriction.")]
    public int playerLevelMax = 0;

    [Tooltip("Nombre maximum de joueurs pouvant valider cette entry. 0 = illimité.\n" +
             "Scope Server requis pour une vraie race mondiale (v4).\n" +
             "En v3.5 : ignoré si scope = Server, appliqué localement sinon.")]
    public int maxWinners = 0;

    // ── Scope ─────────────────────────────────────────────────
    [Tooltip(
        "Character = compteur propre au personnage (défaut).\n" +
        "Account   = cumulé sur tous les persos du compte.\n" +
        "Server    = race mondiale — v4 uniquement, ignoré en v3.5.")]
    public CounterScope scope = CounterScope.Character;
}
