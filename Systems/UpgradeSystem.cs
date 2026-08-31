using UnityEngine;

// =============================================================
// UPGRADESYSTEM.CS — Amélioration d'équipement (+0 → +10)
// Path : Assets/Scripts/Systems/UpgradeSystem.cs
// GDD v3.6 — §5.14
//
// Arme et Armure uniquement. En cas d'échec, ressources et item spécial
// SONT consommés — l'item reste à son niveau actuel, aucune régression.
// Le bonus de stats (%) est déjà géré par WeaponInstance/ArmorInstance
// (UpgradeBonus, formule triangulaire) — ce système ne fait que le jet de
// succès + la consommation, appelé depuis l'UI Forge (PNJ Blacksmith).
// =============================================================

public enum UpgradeResult
{
    Success,
    Failure,
    MissingResource,
    MaxLevel,
    InvalidTarget,
}

public class UpgradeSystem : MonoBehaviour
{
    public static UpgradeSystem Instance { get; private set; }

    [Tooltip("Table des paliers (taux de succès + ressources requises) — GDD §5.14.")]
    public UpgradeTableData table;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public UpgradeResult TryUpgradeWeapon(WeaponInstance weapon, Player player)
    {
        if (weapon == null || player == null || table == null) return UpgradeResult.InvalidTarget;
        if (weapon.upgradeLevel >= table.tiers.Length)            return UpgradeResult.MaxLevel;

        var tier = table.GetTier(weapon.upgradeLevel);
        if (tier == null) return UpgradeResult.InvalidTarget;

        if (!TryConsumeCost(tier)) return UpgradeResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success)
        {
            weapon.upgradeLevel++;
            if (player.equippedWeaponInstance == weapon)
                player.stats.RecalculateStats(player);
        }

        return success ? UpgradeResult.Success : UpgradeResult.Failure;
    }

    public UpgradeResult TryUpgradeArmor(ArmorInstance armor, Player player)
    {
        if (armor == null || player == null || table == null) return UpgradeResult.InvalidTarget;
        if (armor.upgradeLevel >= table.tiers.Length)           return UpgradeResult.MaxLevel;

        var tier = table.GetTier(armor.upgradeLevel);
        if (tier == null) return UpgradeResult.InvalidTarget;

        if (!TryConsumeCost(tier)) return UpgradeResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success)
        {
            armor.upgradeLevel++;
            if (player.equippedArmorInstance == armor)
                player.stats.RecalculateStats(player);
        }

        return success ? UpgradeResult.Success : UpgradeResult.Failure;
    }

    /// <summary>Vérifie ET consomme en un seul passage — retourne false (rien consommé)
    /// si l'une des ressources requises manque. GDD : consommation même en cas
    /// d'échec du jet, donc appelée AVANT le Random.value dans TryUpgradeXxx.</summary>
    private bool TryConsumeCost(UpgradeTableData.UpgradeTier tier)
    {
        var inv = InventorySystem.Instance;
        if (inv == null) return false;

        if (tier.aerisCost > 0 && (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < tier.aerisCost))
            return false;

        if (tier.requiredResource != null &&
            inv.GetResourceCount(tier.requiredResource) < tier.requiredResourceAmount)
            return false;

        if (tier.requiredSpecialItem != null &&
            inv.GetResourceCount(tier.requiredSpecialItem) < tier.requiredSpecialItemAmount)
            return false;

        if (tier.aerisCost > 0)
            AerisSystem.Instance.Spend(tier.aerisCost);

        if (tier.requiredResource != null)
            inv.ConsumeResource(tier.requiredResource, tier.requiredResourceAmount);

        if (tier.requiredSpecialItem != null)
            inv.ConsumeResource(tier.requiredSpecialItem, tier.requiredSpecialItemAmount);

        return true;
    }
}
