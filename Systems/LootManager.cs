using System.Collections.Generic;
using UnityEngine;

// =============================================================
// LOOTMANAGER — Livre le loot d'un mob directement dans
// l'inventaire du/des joueur(s) éligible(s), plus de spawn au sol.
// Path : Assets/Scripts/Systems/LootManager.cs
// AetherTree GDD v3.5
//
// S'abonne à GameEventBus.OnMobKilled. Un seul roll partagé
// (LootTable.RollAll()) ; chaque item ET l'Aeris tirent
// indépendamment un gagnant aléatoire parmi eligiblePlayers
// (≥10% des dégâts totaux, calculé par Mob.Die()).
// Si l'inventaire du gagnant est plein, l'item part en mail de
// secours (MailboxSystem.SendLootOverflowMail) plutôt que d'être
// perdu. L'Aeris n'a jamais ce problème (AerisSystem sans plafond).
// =============================================================

public class LootManager : MonoBehaviour
{
    public static LootManager Instance { get; private set; }

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => Resubscribe();
    private void OnDisable() => GameEventBus.OnMobKilled -= OnMobKilled;

    public void Resubscribe()
    {
        GameEventBus.OnMobKilled -= OnMobKilled;
        GameEventBus.OnMobKilled += OnMobKilled;
    }

    // =========================================================
    // ÉVÉNEMENT MOB TUÉ
    // =========================================================

    private void OnMobKilled(MobKilledEvent e)
    {
        if (e.eligiblePlayers == null || e.eligiblePlayers.Count == 0) return;

        if (e.mob?.lootTable == null)
        {
            Debug.LogWarning($"[LOOTMANAGER] Pas de LootTable sur {e.mob?.mobName}.");
            return;
        }

        LootRollResult roll = e.mob.lootTable.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        string mobName = e.mob.mobName;

        foreach (InventoryItem item in roll.items)
        {
            Player winner = PickRandomEligible(e.eligiblePlayers);
            DeliverItem(winner, item, mobName);
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(e.eligiblePlayers);
            DeliverAeris(winner, roll.aeris, mobName);
        }
    }

    // =========================================================
    // DISTRIBUTION
    // =========================================================

    /// <summary>Tirage uniforme parmi les joueurs éligibles (≥10% des dégâts totaux). En solo,
    /// eligiblePlayers ne contient toujours que le joueur local — ce tirage est déjà correct
    /// tel quel, prêt à servir sans changement le jour où le réseau existera (aucun sac
    /// par-joueur-distant construit à ce jour — voir DeliverItem/DeliverAeris ci-dessous).</summary>
    private Player PickRandomEligible(List<Player> eligiblePlayers)
        => eligiblePlayers[Random.Range(0, eligiblePlayers.Count)];

    /// <summary>Livre un item au gagnant. InventorySystem est aujourd'hui un singleton global
    /// unique (pas de sac par-joueur-distant) — `winner` sert déjà à choisir CORRECTEMENT le
    /// gagnant parmi les éligibles, mais la livraison elle-même cible encore ce singleton tant
    /// que le multi/réseau n'existe pas. SEUL point à mettre à jour quand un vrai sac
    /// par-joueur-distant existera : remplacer InventorySystem.Instance par le sac de winner.</summary>
    private void DeliverItem(Player winner, InventoryItem item, string mobName)
    {
        if (winner == null || item == null) return;

        bool added = InventorySystem.Instance != null && InventorySystem.Instance.AddItem(item);
        if (added)
        {
            Debug.Log($"[LOOT] {winner.entityName} a reçu {item.Name} ({mobName}).");
        }
        else
        {
            MailboxSystem.Instance?.SendLootOverflowMail(item, mobName);
            Debug.Log($"[LOOT] Inventaire plein — {item.Name} envoyé par mail à {winner.entityName} ({mobName}).");
        }
    }

    /// <summary>Livre l'Aeris au gagnant. Même remarque que DeliverItem pour le singleton
    /// global — pas de plafond côté AerisSystem, donc jamais de mail de secours nécessaire.
    /// Bonus talisman (GoldBonus, ex: +15%) appliqué ici — repris de l'ancien
    /// WorldPickupItem.PickUpAeris() (supprimé), qui l'appliquait via FindObjectOfType&lt;Player&gt;() ;
    /// ici `winner` est déjà la bonne référence, plus besoin de la relookup.</summary>
    private void DeliverAeris(Player winner, int amount, string mobName)
    {
        if (winner == null || amount <= 0) return;

        int boosted = Mathf.RoundToInt(amount * (1f + winner.GoldBonusPercent));
        AerisSystem.Instance?.Add(boosted);
        Debug.Log($"[LOOT] {winner.entityName} a reçu {boosted} Aeris ({mobName}).");
    }
}
