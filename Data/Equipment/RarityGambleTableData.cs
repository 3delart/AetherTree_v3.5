using UnityEngine;

// =============================================================
// RARITYGAMBLETABLEDATA.CS — Table du pari de rareté (PNJ Rareté)
// Path : Assets/Scripts/Data/Equipment/RarityGambleTableData.cs
// GDD v3.6 — §3.4.8 / §5.13
//
// Un seul asset pour tout le jeu, référencé par RaritySystem. Deux tirages
// indépendants :
//  1. Issue du pari (Amélioration/Stagnation/Destruction), selon la rareté
//     ACTUELLE de l'item — tiers[] ci-dessous, indexé rarityRank+2 (r-2=0
//     ... r+6=8). r+7 n'a pas d'entrée : pari indisponible (déjà rareté max).
//  2. Si Amélioration : nouvelle rareté tirée dans le pool complet r-2→r+7
//     avec les probabilités de drop du §5.13 (dropRates ci-dessous) — le
//     résultat s'applique toujours, même s'il est inférieur à l'actuelle.
// =============================================================

[CreateAssetMenu(fileName = "RarityGambleTable", menuName = "AetherTree/Config/RarityGambleTableData")]
public class RarityGambleTableData : ScriptableObject
{
    [System.Serializable]
    public class GambleTier
    {
        [Tooltip("Palier affiché pour référence — pas utilisé en code, juste lisible dans l'Inspector.")]
        public string label;

        [Range(0f, 1f)] public float improveChance;
        [Range(0f, 1f)] public float stagnateChance;
        [Range(0f, 1f)] public float destroyChance;
    }

    [Tooltip("Durée de la canalisation (secondes) — même barre que Forge/récolte.")]
    public float channelDuration = 2.5f;

    [Header("Coût — FIXE, identique quelle que soit la rareté actuelle (c'est un pari, pas un palier progressif)")]
    [Tooltip("Coût en Aeris — GDD §3.4.8 : \"à calibrer selon économie complète\", 0 par défaut.")]
    public int aerisCost = 0;

    [Tooltip("null = aucune ressource requise.")]
    public ResourceData requiredResource;
    public int requiredResourceAmount = 1;

    [Tooltip("9 paliers, index 0 = r-2 ... index 8 = r+6. r+7 exclu (rareté max, pari impossible). Probabilités GDD §3.4.8.")]
    public GambleTier[] tiers = new GambleTier[]
    {
        new GambleTier { label = "r-2", improveChance = 0.70f, stagnateChance = 0.20f, destroyChance = 0.10f },
        new GambleTier { label = "r-1", improveChance = 0.60f, stagnateChance = 0.25f, destroyChance = 0.15f },
        new GambleTier { label = "r0",  improveChance = 0.50f, stagnateChance = 0.30f, destroyChance = 0.20f },
        new GambleTier { label = "r+1", improveChance = 0.40f, stagnateChance = 0.30f, destroyChance = 0.30f },
        new GambleTier { label = "r+2", improveChance = 0.35f, stagnateChance = 0.25f, destroyChance = 0.40f },
        new GambleTier { label = "r+3", improveChance = 0.25f, stagnateChance = 0.25f, destroyChance = 0.50f },
        new GambleTier { label = "r+4", improveChance = 0.20f, stagnateChance = 0.20f, destroyChance = 0.60f },
        new GambleTier { label = "r+5", improveChance = 0.15f, stagnateChance = 0.15f, destroyChance = 0.70f },
        new GambleTier { label = "r+6", improveChance = 0.10f, stagnateChance = 0.10f, destroyChance = 0.80f },
    };

    [Tooltip("Tirage 2 (pool complet, si Amélioration) — probabilités de drop GDD §5.13, même ordre que RarityTier (r-2 → r+7). Somme = 1.")]
    public float[] dropRates = new float[]
    {
        0.0800f, // r-2
        0.1200f, // r-1
        0.2085f, // r0
        0.1800f, // r+1
        0.1570f, // r+2
        0.1150f, // r+3
        0.0850f, // r+4
        0.0410f, // r+5
        0.0100f, // r+6
        0.0035f, // r+7
    };

    private const int MinRank = -2;

    /// <summary>null si la rareté est déjà au maximum (r+7) — pari indisponible.</summary>
    public GambleTier GetTier(int currentRarityRank)
    {
        int i = currentRarityRank - MinRank;
        return (i >= 0 && i < tiers.Length) ? tiers[i] : null;
    }
}
