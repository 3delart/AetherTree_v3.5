using UnityEngine;

// =============================================================
// UPGRADETABLEDATA.CS — Table des paliers d'Upgrade (+0 → +10)
// Path : Assets/Scripts/Data/Equipment/UpgradeTableData.cs
// GDD v3.6 — §5.14
//
// Un seul asset pour tout le jeu, référencé par UpgradeSystem. Les taux de
// succès sont déjà remplis (rééquilibrés, pas les valeurs GDD d'origine) —
// reste à assigner les ResourceData
// dans l'Inspector une fois ces items créés (Pierre de renforcement I-V,
// Cristal de stabilité, Larme d'Aether, Cœur d'Aether — pas encore en jeu).
// tiers[i] = palier (+i → +i+1). Le bonus de stats lui-même (%) N'EST PAS ici
// — déjà calculé par la formule triangulaire sur WeaponInstance/ArmorInstance
// (UpgradeBonus), ce SO ne porte que ce qui manquait : succès + ressources.
// =============================================================

[CreateAssetMenu(fileName = "UpgradeTable", menuName = "AetherTree/Config/UpgradeTableData")]
public class UpgradeTableData : ScriptableObject
{
    [System.Serializable]
    public class UpgradeTier
    {
        [Tooltip("Palier affiché pour référence — pas utilisé en code, juste lisible dans l'Inspector.")]
        public string label;

        [Range(0f, 1f)]
        public float successRate;

        [Tooltip("Coût en Aeris — GDD §5.14 : \"à calibrer selon économie complète\", 0 par défaut.")]
        public int aerisCost = 0;

        [Tooltip("null = aucune ressource requise à ce palier.")]
        public ResourceData requiredResource;
        public int requiredResourceAmount = 1;

        [Tooltip("Item spécial requis en plus (Cristal de stabilité, Larme/Cœur d'Aether...). null = aucun.")]
        public ResourceData requiredSpecialItem;
        public int requiredSpecialItemAmount = 1;
    }

    [Tooltip("Durée de la canalisation (secondes) — même barre que la récolte de ressource, résolution (jet+consommation) à la fin.")]
    public float channelDuration = 2.5f;

    [Tooltip("10 paliers, index 0 = +0→+1 ... index 9 = +9→+10. Taux rééquilibrés (valeurs GDD d'origine remplacées).")]
    public UpgradeTier[] tiers = new UpgradeTier[]
    {
        new UpgradeTier { label = "+0 → +1",  successRate = 1.00f  },
        new UpgradeTier { label = "+1 → +2",  successRate = 0.90f  },
        new UpgradeTier { label = "+2 → +3",  successRate = 0.75f  },
        new UpgradeTier { label = "+3 → +4",  successRate = 0.50f  },
        new UpgradeTier { label = "+4 → +5",  successRate = 0.33f  },
        new UpgradeTier { label = "+5 → +6",  successRate = 0.20f  },
        new UpgradeTier { label = "+6 → +7",  successRate = 0.10f  },
        new UpgradeTier { label = "+7 → +8",  successRate = 0.05f  },
        new UpgradeTier { label = "+8 → +9",  successRate = 0.01f  },
        new UpgradeTier { label = "+9 → +10", successRate = 0.005f },
    };

    public UpgradeTier GetTier(int currentUpgradeLevel)
        => (currentUpgradeLevel >= 0 && currentUpgradeLevel < tiers.Length) ? tiers[currentUpgradeLevel] : null;
}
