using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PRESTIGEAURADATA — ScriptableObject unique de configuration Prestige & Aura
// Path : Assets/Scripts/Data/Progression/PrestigeAuraData.cs
// Spec : docs/superpowers/specs/2026-09-29-prestige-aura-design.md
//
// Un seul asset, glissé sur Player.prestigeAuraData — remplace les seuils
// hardcodés (Player.cs), les icônes (autrefois sur CharacterPanelUI) et le
// coût de Purification (autrefois sur PNJData) par UNE liste par palier,
// éditable sans recompiler.
//
// prestigeTiers : 10 entrées (Novice → Légende, Héroïque inséré entre Élite et Légende
//   2026-09-29 pour combler le trou x5 Élite→Légende — voir Editor/PrestigeAuraDataBuilder.cs).
// auraTiers     : 6 entrées (Normal, Terni, Voilé, Corrompu, Maudit, Déchu — Éteint retiré,
//   Florian 2026-09-29, calé sur les 5 malus réels de NosTale) — index 0 (Normal) n'a ni
//   debuff ni coût de Purification (rien à racheter depuis ce palier). Aura DÉMARRE à +100
//   (pas 0), voir Player.aura/CharacterProgress.aura — évite un Terni quasi instantané au
//   premier death, même convention NosTale.
// =============================================================

[System.Serializable]
public class PrestigeTierData
{
    [Tooltip("Nom affiché du palier — ex: Novice, Apprenti, Éveillé...")]
    public string label = "";
    [Tooltip("Seuil de Prestige brut pour atteindre CE palier.")]
    public int threshold = 0;
    public Sprite icon;
}

[System.Serializable]
public class AuraTierData
{
    [Tooltip("Nom affiché du palier — ex: Normal, Terni, Voilé, Corrompu, Maudit, Déchu.")]
    public string label = "";
    [Tooltip("Valeur PLANCHER de ce palier (aura >= floor). Dernier palier (Déchu) : laisser un " +
             "très grand négatif (ex: -999999) — Aura n'a pas de plancher dur, ce palier reste le " +
             "pire peu importe jusqu'où le nombre brut continue de couler en dessous.")]
    public int floor = 0;
    [Tooltip("Malus de prix PNJ — achat (majoration) ET vente (réduction), même %.")]
    [Range(0f, 1f)]
    public float priceMalus = 0f;
    public Sprite icon;
    [Tooltip("Debuff actif tant que l'Aura reste dans ce palier. Vide sur Normal/Terni (aucun " +
             "debuff avant Voilé) — voir Player.RefreshAuraDebuff.")]
    public DebuffData debuff;

    [Header("Purification — coût pour RACHETER ce palier (monter au palier au-dessus)")]
    [Tooltip("Coût Aeris pour passer de CE palier au palier juste au-dessus. Ignoré sur Normal " +
             "(rien à racheter).")]
    public int purificationAerisCost = 0;
    [Tooltip("Ressource additionnelle optionnelle. Null = Aeris seul.")]
    public ResourceData purificationResource;
    public int purificationResourceQty = 0;
}

[CreateAssetMenu(fileName = "PrestigeAuraData", menuName = "AetherTree/Progression/PrestigeAuraData")]
public class PrestigeAuraData : ScriptableObject
{
    [Header("Prestige — 10 paliers, Novice → Légende")]
    public List<PrestigeTierData> prestigeTiers = new List<PrestigeTierData>();

    [Header("Aura — 6 paliers, Normal → Déchu")]
    public List<AuraTierData> auraTiers = new List<AuraTierData>();
}
