using UnityEngine;

// =============================================================
// RARITYSYSTEM.CS — Pari de rareté (PNJ Rareté)
// Path : Assets/Scripts/Systems/RaritySystem.cs
// GDD v3.6 — §3.4.8
//
// Arme et Armure uniquement. Deux tirages indépendants (voir
// RarityGambleTableData) : issue du pari selon la rareté ACTUELLE, puis si
// Amélioration, nouvelle rareté tirée dans le pool complet r-2→r+7 (peut
// retomber plus bas que l'actuelle — s'applique quand même). Ce système ne
// fait QUE le jet + la consommation + la mutation de rarityRank — la
// destruction de l'item lui-même (retrait inventaire/déséquipement) est
// gérée par RarityUI, qui seul connaît la provenance de l'InventoryItem.
// =============================================================

public enum RarityGambleResult
{
    Improved,
    Stagnated,
    Destroyed,
    MissingResource,
    MaxRarity,
    InvalidTarget,
}

public class RaritySystem : MonoBehaviour
{
    public static RaritySystem Instance { get; private set; }

    [Tooltip("Table des paliers (issue du pari + pool de rareté) — GDD §3.4.8.")]
    public RarityGambleTableData table;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public RarityGambleResult TryGambleWeapon(WeaponInstance weapon, Player player)
    {
        if (weapon == null || player == null || table == null) return RarityGambleResult.InvalidTarget;

        var tier = table.GetTier(weapon.rarityRank);
        if (tier == null) return RarityGambleResult.MaxRarity;
        if (!TryConsumeCost()) return RarityGambleResult.MissingResource;

        var outcome = RollOutcome(tier);
        if (outcome == RarityGambleResult.Improved)
        {
            weapon.rarityRank = RollNewRarity();
            if (player.equippedWeaponInstance == weapon)
                player.stats.RecalculateStats(player);
        }

        return outcome;
    }

    public RarityGambleResult TryGambleArmor(ArmorInstance armor, Player player)
    {
        if (armor == null || player == null || table == null) return RarityGambleResult.InvalidTarget;

        var tier = table.GetTier(armor.rarityRank);
        if (tier == null) return RarityGambleResult.MaxRarity;
        if (!TryConsumeCost()) return RarityGambleResult.MissingResource;

        var outcome = RollOutcome(tier);
        if (outcome == RarityGambleResult.Improved)
        {
            armor.rarityRank = RollNewRarity();
            if (player.equippedArmorInstance == armor)
                player.stats.RecalculateStats(player);
        }

        return outcome;
    }

    private RarityGambleResult RollOutcome(RarityGambleTableData.GambleTier tier)
    {
        float roll = Random.value;
        if (roll < tier.improveChance) return RarityGambleResult.Improved;
        if (roll < tier.improveChance + tier.stagnateChance) return RarityGambleResult.Stagnated;
        return RarityGambleResult.Destroyed;
    }

    /// <summary>Tirage 2 — pool complet r-2→r+7 pondéré par table.dropRates (GDD §5.13).</summary>
    private int RollNewRarity()
    {
        float roll = Random.value;
        float cumulative = 0f;
        for (int i = 0; i < table.dropRates.Length; i++)
        {
            cumulative += table.dropRates[i];
            if (roll < cumulative) return i - 2; // index 0 = r-2
        }
        return 7; // repli si la somme n'atteint pas exactement 1 (arrondi flottant)
    }

    /// <summary>Vérifie ET consomme en un seul passage — retourne false (rien consommé)
    /// si l'une des ressources requises manque. GDD : consommation même en cas
    /// d'échec/destruction du jet, donc appelée AVANT le Random.value. Coût FIXE
    /// (table.aerisCost/requiredResource) — identique quelle que soit la rareté
    /// actuelle, seule l'issue du pari varie par tier.</summary>
    private bool TryConsumeCost()
    {
        var inv = InventorySystem.Instance;
        if (inv == null) return false;

        if (table.aerisCost > 0 && (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < table.aerisCost))
            return false;

        if (table.requiredResource != null &&
            inv.GetResourceCount(table.requiredResource) < table.requiredResourceAmount)
            return false;

        if (table.aerisCost > 0)
            AerisSystem.Instance.Spend(table.aerisCost);

        if (table.requiredResource != null)
            inv.ConsumeResource(table.requiredResource, table.requiredResourceAmount);

        return true;
    }
}
