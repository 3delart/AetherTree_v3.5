// =============================================================
// IINSTANCECONFIG.CS — Contrat commun à toute activité instanciée
// Path : Assets/Scripts/Data/Content/IInstanceConfig.cs
// AetherTree GDD v3.6 — §14.2/§14.3/§14.5.1
//
// Implémenté par DungeonData (Donjon Classique/Déblocage) et
// CombatVagueData (Combat à Vague) — voir docs/superpowers/specs/
// 2026-09-23-instance-system-design.md pour le design complet.
// InstanceSession ne connaît QUE cette interface, jamais un type concret.
// =============================================================

public interface IInstanceConfig
{
    /// <summary>Identifiant unique — snake_case, immuable (dungeonID/eventID sur le SO concret).</summary>
    string InstanceID { get; }

    /// <summary>Nom affiché dans l'UI.</summary>
    string DisplayName { get; }

    /// <summary>Nom de la scène Unity à charger via SceneLoader pour cette activité.</summary>
    string SceneName { get; }

    /// <summary>ID du ConsumableData (consumableType = DungeonKey) requis pour entrer.</summary>
    string EntryItemID { get; }

    /// <summary>Vies individuelles avant fin de run — voir la règle de mort unifiée dans
    /// InstanceSession.OnPlayerDeath(). Donjon Classique : 2 (défaut GDD §14.2.3). Donjon de
    /// Déblocage : 1 (GDD §14.3.3). Combat à Vague : toujours 1, codé en dur sur
    /// CombatVagueData (GDD §14.5.1 : "aucune résurrection possible").</summary>
    int LivesPerPlayer { get; }

    /// <summary>Vies communes de l'activité : nombre total de morts (tous joueurs confondus)
    /// tolérées avant l'échec pour tout le monde. 0 = illimité. Chaque mort retire une vie au
    /// joueur ET une vie commune — voir InstanceSession.OnPlayerDeath().</summary>
    int DeathLimit { get; }

    /// <summary>Délai (secondes) entre une mort avec vie(s) restante(s) et le retour au point
    /// d'entrée de la salle — voir InstanceSession.RespawnRoutine(). Volontairement plus long que
    /// DeathExpulsionDelay : punit la mort et pénalise le groupe qui joue sans ce joueur pendant
    /// ce temps.</summary>
    float RespawnDelay { get; }

    /// <summary>Délai (secondes) de la fenêtre de grâce après la DERNIÈRE vie perdue — voir
    /// InstanceSession.FailureGraceRoutine(). Plus court que RespawnDelay : ce joueur ne participe
    /// plus au run. Si le boss meurt PENDANT cette fenêtre, le run bascule en Succès au lieu d'une
    /// expulsion — nom distinct du champ CombatVagueData.expulsionDelay existant (durée max de
    /// l'événement, concept totalement différent), pour ne jamais les confondre.</summary>
    float DeathExpulsionDelay { get; }

    /// <summary>Délai (secondes) entre un Succès (boss tué) et l'expulsion vers la map de retour —
    /// voir InstanceSession.SuccessExitRoutine(). GDD §14.2.4 : "expulsion automatique après
    /// 15 secondes" pour un donjon.</summary>
    float SuccessExitDelay { get; }
}
