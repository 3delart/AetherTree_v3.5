using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// =============================================================
// WORLDEVENTDATA.CS — Tronc commun de tout type d'événement mondial
// Path : Assets/Scripts/Data/Content/WorldEventData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Base abstraite de WorldBossData/InvasionData (et de tout futur type d'événement). Porte ce qui
// est IDENTIQUE entre tous les types : tracking de participation en temps réel
// (GameEventBus.OnDamageDealt — tout dégât ≥0 d'un Player sur un mob que CET événement possède,
// aucun seuil), et distribution de la récompense finale. Chaque type concret n'écrit QUE sa
// propre mécanique de spawn/résolution (RunEvent) et répond à "ce mob m'appartient-il ?" (OwnsMob).
//
// Timer/offsets d'annonce ET eligibleMaps/WorldEventMapEntry/PickRandomMap ne vivent PAS ici — ils
// sont partagés par TOUS les types d'événements et vivent sur Systems/WorldEventScheduler.cs (le
// dispatcher), pas sur chaque asset (Florian, 2026-09-30 : un palier éligible à un event l'est pour
// N'IMPORTE QUEL type, pas seulement celui d'un asset précis).
// =============================================================

public abstract class WorldEventData : ScriptableObject
{
    [Header("Éligibilité")]
    [Tooltip("Coups minimum portés sur un mob de CET événement pour être éligible à la " +
             "récompense finale — empêche un joueur de passage (1 coup, repart) d'être " +
             "récompensé comme un vrai participant (Florian, 2026-09-30 : \"il faut vraiment " +
             "participer\").")]
    public int minHitsToBeEligible = 10;

    private readonly Dictionary<Player, int> _hitCounts = new Dictionary<Player, int>();

    /// <summary>Nom d'affichage utilisé dans les annonces partagées ("Un {DisplayName} menace...").</summary>
    public abstract string DisplayName { get; }

    /// <summary>Point d'entrée unique, appelé par WorldEventScheduler quand ce type est tiré au
    /// hasard — gère TOUTE la mécanique (décision, annonces, spawn, résolution, récompense).</summary>
    public abstract IEnumerator RunEvent(WorldEventScheduler scheduler);

    /// <summary>"Ce mob appartient-il à l'instance actuellement en cours de CET événement ?" —
    /// utilisé par le tracking de dégâts pour savoir si taper ce mob compte pour la
    /// participation. Chaque type concret répond selon sa propre notion de "mes mobs" (un seul
    /// boss pour World Boss, une liste de mobs de vague/renfort pour Invasion).</summary>
    protected abstract bool OwnsMob(Entity target);

    private void OnDamageDealtHandler(DamageDealtEvent e)
    {
        if (!(e.source is Player player)) return;
        if (!OwnsMob(e.target)) return;
        _hitCounts.TryGetValue(player, out int count);
        _hitCounts[player] = count + 1;
    }

    /// <summary>À appeler AU DÉBUT de la fenêtre où les mobs de cet événement peuvent être
    /// tapés (juste après le premier spawn) — vide les compteurs d'une éventuelle exécution
    /// précédente et s'abonne au tracking de dégâts.</summary>
    protected void StartTracking()
    {
        _hitCounts.Clear();
        GameEventBus.OnDamageDealt += OnDamageDealtHandler;
    }

    /// <summary>À appeler dès que l'événement est résolu (succès ou échec) — coupe
    /// l'abonnement, plus aucun dégât ne doit être compté après ce point. Ne doit JAMAIS être
    /// appelé directement depuis un callback Mob.OnDeath (voir WorldBossData) : Mob.Die() est
    /// synchrone et s'exécute AVANT que l'appelant (SkillSystem) publie le DamageDealtEvent de ce
    /// même coup — couper le tracking à cet instant précis perdrait ce dernier coup.</summary>
    protected void StopTracking() => GameEventBus.OnDamageDealt -= OnDamageDealtHandler;

    /// <summary>Distribue la LootTable de l'événement (XP/Prestige/items) à tout joueur ayant
    /// atteint minHitsToBeEligible coups — calculé à la volée depuis _hitCounts (jamais maintenu
    /// comme liste incrémentale), délègue à LootManager.GrantEventLoot/XPSystem.GrantEventRewards,
    /// hors pipeline MobKilledEvent normal.</summary>
    protected void GrantEventRewards(LootTable table)
    {
        List<Player> eligiblePlayers = _hitCounts
            .Where(kv => kv.Value >= minHitsToBeEligible)
            .Select(kv => kv.Key)
            .ToList();

        LootManager.Instance?.GrantEventLoot(table, eligiblePlayers);
        XPSystem.Instance?.GrantEventRewards(table, eligiblePlayers);
    }
}
