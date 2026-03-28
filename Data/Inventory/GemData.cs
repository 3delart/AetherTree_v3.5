using UnityEngine;
using System.Collections.Generic;

// =============================================================
// GemData — ScriptableObject template de gemme
// Path : Assets/Scripts/Data/Inventory/Equipment/GemData.cs
// AetherTree GDD v3.5 — §5.5
//
// Règles GDD §5.5 :
//   - gemLevel fixé sur le SO (identique sur toutes les instances)
//   - Chaque stat autorisée a sa propre range min/max
//   - Au drop, un GemStatEntry est tiré aléatoirement parmi allowedStats
//   - La valeur est rollée entre entry.valueMin et entry.valueMax
//   - Insertion irréversible dans un slot de bijou — aucun moyen d'extraire
//   - gemLevel plafonné par maxGemLevel du bijou cible
//
// Pool de stats gemmes (§5.5) :
//   HP / Mana / RegenHP / RegenMana
//   Défenses (Melee/Ranged/Magic)
//   CritChance / CritMultiplier
//   Précision / Esquive
//   Résistances élémentaires / Points élémentaires
//
// Assets > Create > AetherTree > Equipment > GemData
// =============================================================

// =============================================================
// GemStatEntry — une stat autorisée avec sa propre range de valeur
// =============================================================
[System.Serializable]
public class GemStatEntry
{
    [Tooltip("Type de stat apportée par cette gemme.")]
    public StatType statType;

    [Tooltip("Valeur minimale rollée pour cette stat.")]
    public float valueMin = 5f;

    [Tooltip("Valeur maximale rollée pour cette stat.")]
    public float valueMax = 15f;
}

// =============================================================
// GemData SO
// =============================================================
[CreateAssetMenu(fileName = "NewGem", menuName = "AetherTree/Consumable/GemData")]
public class GemData : ScriptableObject
{
    [Header("Identité")]
    public string gemName = "Gem";
    public Sprite icon;

    [Header("Niveau")]
    [Tooltip("Niveau de la gemme — fixe, identique sur toutes les instances de ce SO.")]
    public int gemLevel = 1;

    [Header("Stats autorisées (une sera choisie aléatoirement au drop)")]
    [Tooltip("Chaque entrée définit un StatType possible avec sa propre range de valeur.\n" +
             "Un GemStatEntry est tiré aléatoirement au drop, puis la valeur est rollée entre valueMin et valueMax.")]

    [Header("Description")]
    [TextArea]
    public string       description  = "";
    public List<GemStatEntry> allowedStats = new List<GemStatEntry>();

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>
    /// Crée une instance droppée : tire un GemStatEntry aléatoire
    /// parmi allowedStats et rolle la valeur entre entry.valueMin et entry.valueMax.
    /// </summary>
    public GemInstance CreateDropInstance()
    {
        if (allowedStats == null || allowedStats.Count == 0)
        {
            Debug.LogWarning($"[GemData] {gemName} : aucun StatType autorisé défini !");
            return null;
        }

        GemStatEntry entry       = allowedStats[Random.Range(0, allowedStats.Count)];
        float        rolledValue = Random.Range(entry.valueMin, entry.valueMax);

        return new GemInstance(this, entry.statType, rolledValue);
    }
}

// =============================================================
// GemInstance — données runtime d'une gemme droppée
//
// StatType et valeur sont rollés au drop et immuables ensuite.
// gemLevel est lu directement sur le SO.
// =============================================================
[System.Serializable]
public class GemInstance
{
    public GemData data;

    // ── Stat rollée au drop (immuable) ────────────────────────
    public StatType rolledStat;
    public float    rolledValue;

    // ── Révélation ────────────────────────────────────────────
    /// <summary>
    /// False tant que la gemme n'est pas insérée dans un bijou.
    /// L'UI affiche "???" si false — la stat est inconnue jusqu'à l'insertion.
    /// Passe à true via Reveal(), appelé par GemSlotInstance.TryInsert().
    /// </summary>
    public bool isRevealed = false;

    // ── Constructeur ─────────────────────────────────────────
    public GemInstance(GemData source, StatType stat, float value)
    {
        data        = source;
        rolledStat  = stat;
        rolledValue = value;
    }

    // ── Révélation ───────────────────────────────────────────
    /// <summary>Révèle la stat de la gemme — appelé à l'insertion dans un bijou.</summary>
    public void Reveal() => isRevealed = true;

    // ── Accesseurs ────────────────────────────────────────────
    public string GemName  => data?.gemName  ?? "Gem";
    public Sprite Icon     => data?.icon;
    public int    GemLevel => data?.gemLevel ?? 1;

    /// <summary>
    /// StatBonus généré depuis les données rollées — lu par PlayerStats.
    /// Ne doit être appelé que si isRevealed est true (garanti par GemSlotInstance).
    /// </summary>
    public StatBonus ToStatBonus() => new StatBonus { statType = rolledStat, value = rolledValue };

    /// <summary>
    /// Affiche la gemme sous forme lisible.
    /// Si non révélée : "Gemme Lv3 — ???"
    /// Si révélée     : "Gemme Lv3 — ResistFire +0.10"
    /// </summary>
    public string Label => isRevealed
        ? $"{GemName} Lv{GemLevel} — {rolledStat} +{rolledValue:F2}"
        : $"{GemName} Lv{GemLevel} — ???";
}
