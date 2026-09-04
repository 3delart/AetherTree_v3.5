using UnityEngine;

// =============================================================
// FUSIONTABLEDATA.CS — Table des taux de réussite de Fusion (S0 → S6)
// Path : Assets/Scripts/Data/Equipment/FusionTableData.cs
// AetherTree GDD — Fusion Gants/Bottes, Cordonnier
//
// Un seul asset pour tout le jeu, référencé par FusionSystem. Taux indexés par le
// PALIER D'ARRIVÉE (1 = S1 ... 6 = S6) — toute paire (slot1.fusionLevel, slot2.fusionLevel)
// visant le même palier utilise le même taux, peu importe la paire exacte utilisée.
// Valeurs calquées sur la référence NosTale (voir spec 2026-09-04-fusion-system-design.md).
// aerisCost à calibrer plus tard — non bloquant, comme UpgradeTableData/RarityGambleTableData.
// =============================================================

[CreateAssetMenu(fileName = "FusionTable", menuName = "AetherTree/Config/FusionTableData")]
public class FusionTableData : ScriptableObject
{
    [System.Serializable]
    public class FusionTier
    {
        [Tooltip("Palier affiché pour référence — pas utilisé en code, juste lisible dans l'Inspector.")]
        public string label;

        [Range(0f, 1f)]
        public float successRate;

        [Tooltip("Coût en Aeris — à calibrer selon économie complète, 0 par défaut.")]
        public int aerisCost = 0;
    }

    [Tooltip("Durée de la canalisation (secondes) — même barre que Forge/Rareté/Craft, résolution (jet+consommation) à la fin.")]
    public float channelDuration = 2.5f;

    [Tooltip("6 paliers, index 0 = cible S1 ... index 5 = cible S6. Taux calqués sur NosTale.")]
    public FusionTier[] tiers = new FusionTier[]
    {
        new FusionTier { label = "→ S1", successRate = 1.00f },
        new FusionTier { label = "→ S2", successRate = 1.00f },
        new FusionTier { label = "→ S3", successRate = 0.80f },
        new FusionTier { label = "→ S4", successRate = 0.70f },
        new FusionTier { label = "→ S5", successRate = 0.50f },
        new FusionTier { label = "→ S6", successRate = 0.20f },
    };

    /// <summary>Retourne le palier correspondant au niveau de fusion CIBLE (1..6).
    /// null si hors plage (0 = pas de fusion, >6 = combo invalide, jamais atteint en pratique
    /// car FusionSystem.CanFuse() refuse déjà target > 6 avant d'appeler GetTier).</summary>
    public FusionTier GetTier(int targetFusionLevel)
        => (targetFusionLevel >= 1 && targetFusionLevel <= 6) ? tiers[targetFusionLevel - 1] : null;
}
