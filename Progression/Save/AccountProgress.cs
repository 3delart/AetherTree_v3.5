using System.Collections.Generic;

// =============================================================
// ACCOUNTPROGRESS — Données de sauvegarde niveau COMPTE
// Path : Assets/_Game/Scripts/Progression/Save/AccountProgress.cs
// AetherTree GDD v31
//
// Séparé de CharacterProgress intentionnellement :
//   - CharacterProgress  → lié à UN personnage  (save_[nom].json)
//   - AccountProgress    → lié au COMPTE joueur (account.json)
//
// En v3.5 (solo, un seul perso) : account.json existe déjà et
// accumule les compteurs cross-perso pour préparation v4.
// En v4 (multijoueur) : account.json devient une lecture depuis
// le serveur — AccountProgress reste la même structure.
//
// Compteurs compte :
//   Somme des compteurs de TOUS les personnages du compte.
//   Exemple : timePlayed = heures cumulées sur perso A + perso B.
//
// Conditions compte débloquées :
//   Une condition compte débloquée sur perso A n'est PAS
//   automatiquement appliquée sur perso B — la reward va
//   uniquement au perso connecté au moment du déblocage.
//   Mais le flag "débloqué" est stocké ici pour ne pas
//   re-déclencher la condition sur un autre perso.
// =============================================================

[System.Serializable]
public class AccountProgress
{
    // ── Identité compte ───────────────────────────────────────
    // En v3.5 : correspond au nom du seul perso existant.
    // En v4   : accountID serveur.
    public string accountID = "";

    // ── Compteurs cumulés cross-perso ─────────────────────────
    // Même format que activityCountersList dans CharacterProgress —
    // clés définies dans CounterKeys.cs.
    // Mis à jour à chaque Save() en additionnant les deltas du perso actif.
    public List<StringIntPair> accountCountersList = new List<StringIntPair>();

    // ── Conditions compte débloquées ──────────────────────────
    // IDs des ConditionData avec scope = Account dont la condition
    // est remplie. Empêche le re-déclenchement sur un autre perso.
    public List<string> unlockedAccountConditionIDs = new List<string>();

    // ── Serveur (v4) ──────────────────────────────────────────
    // Réservé pour les conditions scope = Server.
    // En v3.5 : toujours vide, jamais écrit.
    public List<string> unlockedServerConditionIDs = new List<string>();
}
