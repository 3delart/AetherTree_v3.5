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
// MobData.massEventRewards (World Boss/Invasion) change la règle ITEMS uniquement (Florian,
// 2026-09-29) : chaque item droppé (LootEntry.dropChance déjà tiré indépendamment dans
// RollAll(), inchangé) va à un joueur DIFFÉRENT parmi le pool restant — un joueur déjà gagnant
// est retiré du tirage pour les items suivants, jamais 2 items au même joueur. Si le pool
// s'épuise avant la fin des items droppés, les items en trop ne sont PAS attribués (pas de
// bouclage/répétition — "tant pis"). L'Aeris n'est PAS concerné par cette règle (indépendant,
// inchangé) — de toute façon un World Boss n'est pas censé en donner (aerisDropChance = 0 sur
// son LootTable), donc la question ne se pose pas en pratique.
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
        // massEventRewards (MobData) : ≥1 dégât suffit (contributingPlayers) au lieu du seuil
        // ≥10% (eligiblePlayers) — même raison que XPSystem.HandleMobKilled, voir Mob.Die().
        var rewardPool = e.mob != null && e.mob.massEventRewards ? e.contributingPlayers : e.eligiblePlayers;
        if (rewardPool == null || rewardPool.Count == 0) return;

        if (e.mob?.lootTable == null)
        {
            Debug.LogWarning($"[LOOTMANAGER] Pas de LootTable sur {e.mob?.mobName}.");
            return;
        }

        LootRollResult roll = e.mob.lootTable.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        string mobName = e.mob.mobName;

        if (e.mob.massEventRewards)
        {
            // Un item par joueur max, tirage SANS remise — un gagnant est retiré du pool pour
            // les items suivants. Pool épuisé avant la fin des items droppés → items restants
            // non attribués, pas de bouclage (Florian, 2026-09-29 : "tant pis").
            var remainingPool = new List<Player>(rewardPool);
            foreach (InventoryItem item in roll.items)
            {
                if (remainingPool.Count == 0) break;
                int index = Random.Range(0, remainingPool.Count);
                Player winner = remainingPool[index];
                remainingPool.RemoveAt(index);
                DeliverItem(winner, item, mobName);
            }
        }
        else
        {
            foreach (InventoryItem item in roll.items)
            {
                Player winner = PickRandomEligible(rewardPool);
                DeliverItem(winner, item, mobName);
            }
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(rewardPool);
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

        if (InventorySystem.Instance == null)
        {
            Debug.LogWarning($"[LOOT] InventorySystem introuvable — impossible de livrer {item.Name} ({mobName}), envoi par mail en secours.");
            MailboxSystem.Instance?.SendLootOverflowMail(item, mobName);
            return;
        }

        if (InventorySystem.Instance.AddItem(item))
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
