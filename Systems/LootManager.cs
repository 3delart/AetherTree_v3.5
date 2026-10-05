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
//
// GrantEventLoot (voir plus bas) est un chemin SÉPARÉ, pour la récompense de fin d'événement
// (World Boss/Invasion, voir Data/Content/WorldEventData.cs) — jamais déclenché par
// GameEventBus.OnMobKilled, jamais mélangé avec la logique ci-dessous.
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

        LootRollResult roll = e.mob.lootTable.RollAll(e.eligiblePlayers);
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        string mobName = e.mob.mobName;

        foreach (RolledItem rolled in roll.items)
        {
            Player winner = rolled.restrictedTo ?? PickRandomEligible(e.eligiblePlayers);
            DeliverItem(winner, rolled.item, mobName);
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(e.eligiblePlayers);
            DeliverAeris(winner, roll.aeris, mobName);
        }
    }

    // =========================================================
    // RÉCOMPENSE D'ÉVÉNEMENT (World Boss / Invasion)
    // =========================================================

    /// <summary>Distribue une LootTable à une liste de joueurs, HORS du pipeline
    /// GameEventBus.OnMobKilled — pour la récompense de fin d'événement (World Boss/Invasion,
    /// voir Data/Content/WorldEventData.cs), jamais liée à la mort d'un mob précis. Chaque item
    /// va à un joueur DIFFÉRENT (tirage sans remise) ; si plus d'items droppent que de joueurs
    /// éligibles, les items en trop ne sont PAS attribués (pas de bouclage/répétition).</summary>
    public void GrantEventLoot(LootTable table, List<Player> eligiblePlayers)
    {
        if (table == null || eligiblePlayers == null || eligiblePlayers.Count == 0) return;

        LootRollResult roll = table.RollAll(eligiblePlayers);
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        var remainingPool = new List<Player>(eligiblePlayers);
        foreach (RolledItem rolled in roll.items)
        {
            if (rolled.restrictedTo != null)
            {
                // Hors pool/tirage sans remise — un item questLootOnly va TOUJOURS à son joueur
                // restreint, jamais à un autre, même si le pool général est épuisé.
                DeliverItem(rolled.restrictedTo, rolled.item, "Événement");
                continue;
            }

            if (remainingPool.Count == 0) break;
            int index = Random.Range(0, remainingPool.Count);
            Player winner = remainingPool[index];
            remainingPool.RemoveAt(index);
            DeliverItem(winner, rolled.item, "Événement");
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(eligiblePlayers);
            DeliverAeris(winner, roll.aeris, "Événement");
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

        if (InventorySystem.Instance == null)
        {
            Debug.LogWarning($"[LOOT] InventorySystem introuvable — impossible de livrer {item.Name} ({mobName}), envoi par mail en secours.");
            MailboxSystem.Instance?.SendLootOverflowMail(item, mobName);
            return;
        }

        if (InventorySystem.Instance.AddItem(item))
        {
            Debug.Log($"[LOOT] {winner.entityName} a reçu {item.Name} ({mobName}).");

            var itemData = item.GetItemData();
            if (itemData != null)
                GameEventBus.Publish(new ItemEvent
                {
                    itemID   = itemData.itemID,
                    itemName = item.Name,
                    action   = ItemAction.Pickup,
                    quantity = item.GetQuantity(),
                });
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

        if (AerisSystem.Instance == null)
        {
            Debug.LogWarning($"[LOOT] AerisSystem introuvable — {amount} Aeris perdus ({mobName}).");
            return;
        }

        int boosted = Mathf.RoundToInt(amount * (1f + winner.GoldBonusPercent));
        int bonus   = boosted - amount;
        AerisSystem.Instance.Add(boosted);

        string label = bonus > 0 ? $"{boosted} Aeris (+{bonus} Aeris bonus)" : $"{boosted} Aeris";
        Debug.Log($"[LOOT] {winner.entityName} a reçu {label} ({mobName}) — {amount} brut, bonus {winner.GoldBonusPercent:P0}.");
    }
}
