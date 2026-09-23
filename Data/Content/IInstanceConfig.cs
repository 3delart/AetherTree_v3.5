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

    /// <summary>ID du ConsumableData (DungeonStone) requis pour entrer.</summary>
    string EntryItemID { get; }

    /// <summary>Vies individuelles avant fin de run — voir la règle de mort unifiée dans
    /// InstanceSession.OnPlayerDeath(). Donjon Classique : 2 (défaut GDD §14.2.3). Donjon de
    /// Déblocage : 1 (GDD §14.3.3). Combat à Vague : toujours 1, codé en dur sur
    /// CombatVagueData (GDD §14.5.1 : "aucune résurrection possible").</summary>
    int LivesPerPlayer { get; }
}
