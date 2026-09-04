using UnityEngine;

// =============================================================
// FUSIONSYSTEM.CS — Fusion de gants/bottes (S0 → S6)
// Path : Assets/Scripts/Systems/FusionSystem.cs
// AetherTree GDD — Fusion, Cordonnier
//
// Calque RaritySystem.cs (Pari de rareté) — jet + consommation même en cas d'échec, la
// destruction/ajout des InventoryItem est gérée par l'appelant (FusionUI), pas ici : ce
// système ne connaît que les instances GlovesInstance/BootsInstance passées en paramètre,
// jamais leur provenance dans l'inventaire.
//
// Formule de palier (voir GlovesInstance.Fuse()/BootsInstance.Fuse()) :
//   target = slot1.fusionLevel + slot2.fusionLevel + 1, refusé si > 6 (CanFuse).
// Taux de réussite : FusionTableData, indexé par palier D'ARRIVÉE (1..6).
// Échec = les DEUX instances doivent être détruites par l'appelant — ce système ne le fait
// jamais lui-même, il retourne juste FusionResult.Failure.
// =============================================================

public enum FusionResult
{
    Success,
    Failure,
    InvalidCombo,     // target > 6
    SameItem,         // slot1 == slot2 (même référence d'instance)
    MissingResource,  // Aeris insuffisant
    InvalidTarget,    // slot1/slot2/player/table null
}

public class FusionSystem : MonoBehaviour
{
    public static FusionSystem Instance { get; private set; }

    [Tooltip("Table des paliers (taux de succès + coût Aeris) — voir FusionTableData.cs.")]
    public FusionTableData table;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Calcule le palier cible et dit si la combinaison est valide (≤ S6).
    /// Utilisé par FusionUI pour activer/désactiver le bouton Fuse AVANT tout jet —
    /// une combinaison invalide n'est jamais tentée, jamais un échec qui coûterait des
    /// ressources.</summary>
    public bool CanFuse(int fusionLevel1, int fusionLevel2, out int target)
    {
        target = fusionLevel1 + fusionLevel2 + 1;
        return target <= 6;
    }

    public FusionResult TryFuseGloves(GlovesInstance slot1, GlovesInstance slot2, Player player, out GlovesInstance result)
    {
        result = null;
        if (slot1 == null || slot2 == null || player == null || table == null) return FusionResult.InvalidTarget;
        if (ReferenceEquals(slot1, slot2)) return FusionResult.SameItem;
        if (!CanFuse(slot1.fusionLevel, slot2.fusionLevel, out int target)) return FusionResult.InvalidCombo;

        var tier = table.GetTier(target);
        if (tier == null) return FusionResult.InvalidCombo;
        if (!TryConsumeCost(tier)) return FusionResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success) result = GlovesInstance.Fuse(slot1, slot2);

        Debug.Log($"[FUSION] Gants {(success ? "RÉUSSIE" : "ÉCHOUÉE")} → cible S{target} (taux {Mathf.RoundToInt(tier.successRate * 100f)}%).");
        return success ? FusionResult.Success : FusionResult.Failure;
    }

    public FusionResult TryFuseBoots(BootsInstance slot1, BootsInstance slot2, Player player, out BootsInstance result)
    {
        result = null;
        if (slot1 == null || slot2 == null || player == null || table == null) return FusionResult.InvalidTarget;
        if (ReferenceEquals(slot1, slot2)) return FusionResult.SameItem;
        if (!CanFuse(slot1.fusionLevel, slot2.fusionLevel, out int target)) return FusionResult.InvalidCombo;

        var tier = table.GetTier(target);
        if (tier == null) return FusionResult.InvalidCombo;
        if (!TryConsumeCost(tier)) return FusionResult.MissingResource;

        bool success = Random.value < tier.successRate;
        if (success) result = BootsInstance.Fuse(slot1, slot2);

        Debug.Log($"[FUSION] Bottes {(success ? "RÉUSSIE" : "ÉCHOUÉE")} → cible S{target} (taux {Mathf.RoundToInt(tier.successRate * 100f)}%).");
        return success ? FusionResult.Success : FusionResult.Failure;
    }

    /// <summary>Vérifie ET consomme l'Aeris en un seul passage — retourne false (rien
    /// consommé) si l'Aeris manque. Consommé même en cas d'échec du jet, comme
    /// Upgrade/Rarity (appelé AVANT le Random.value dans TryFuseXxx).</summary>
    private bool TryConsumeCost(FusionTableData.FusionTier tier)
    {
        if (tier.aerisCost <= 0) return true;
        if (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < tier.aerisCost) return false;
        AerisSystem.Instance.Spend(tier.aerisCost);
        return true;
    }
}
