using UnityEngine;
using System.Collections.Generic;

// =============================================================
// JewelryData — ScriptableObject template de bijou
// Path : Assets/Scripts/Data/Equipment/JewelryData.cs
// AetherTree GDD v3.6 — §5.7 (extension ratio-roll Phase 2 sur les
// défenses, hors GDD d'origine — voir
// a implémenter/note-refonte-roll-ratio-phase2.md)
//
// Hérite de EquipmentDataBase (itemID, displayName, description,
// icon, requiredLevel, config... — voir ItemData/EquipmentDataBase).
//
// 3 slots de bijoux : 1 Anneau, 1 Collier, 1 Bracelet.
// Pas de rareté, pas d'upgrade, pas de fusion (GDD §5.0/§5.2).
//
// Pool de stats disponibles dans config.bonuses (GDD §5.7) :
//   MaxHP, MaxMana, Precision, Dodge
//   ⚠ Pas de CritChance, Points élémentaires ni Résistances élémentaires
//     en bonusLines — ces stats viennent des gemmes insérées.
//
// Gemmes (GDD §5.7) :
//   Insertion irréversible — aucun moyen d'extraire une gemme.
// =============================================================

[CreateAssetMenu(fileName = "NewJewelry", menuName = "AetherTree/Equipment/JewelryData")]
public class JewelryData : EquipmentDataBase
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Slot de bijou — détermine dans quel emplacement ce bijou peut être équipé.\n" +
             "GDD §5.7 : 1 Anneau, 1 Collier, 1 Bracelet simultanément.")]
    public JewelrySlot jewelrySlot = JewelrySlot.Ring;

    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
    [Header("Défense mêlée (rollée — contribution défensive principale du bijou, GDD §5.7)")]
    public float baseMeleeDefense = 0f;
    [Min(0f)] public float meleeDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float meleeDefenseRollGapPercent = 0.06f;
    public float MeleeDefenseLow  => baseMeleeDefense * (1f - meleeDefenseSpreadPercent);
    public float MeleeDefenseHigh => baseMeleeDefense * (1f + meleeDefenseSpreadPercent);

    // ── Défense distance — rollée ─────────────────────────────
    [Header("Défense distance (rollée)")]
    public float baseRangedDefense = 0f;
    [Min(0f)] public float rangedDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float rangedDefenseRollGapPercent = 0.06f;
    public float RangedDefenseLow  => baseRangedDefense * (1f - rangedDefenseSpreadPercent);
    public float RangedDefenseHigh => baseRangedDefense * (1f + rangedDefenseSpreadPercent);

    // ── Défense magique — rollée ───────────────────────────────
    [Header("Défense magique (rollée)")]
    public float baseMagicDefense = 0f;
    [Min(0f)] public float magicDefenseSpreadPercent  = 0.10f;
    [Min(0f)] public float magicDefenseRollGapPercent = 0.06f;
    public float MagicDefenseLow  => baseMagicDefense * (1f - magicDefenseSpreadPercent);
    public float MagicDefenseHigh => baseMagicDefense * (1f + magicDefenseSpreadPercent);

    // ── Gemmes ────────────────────────────────────────────────
    [Header("Gemmes (GDD §5.7)")]
    [Tooltip("Nombre de slots gemme disponibles sur ce bijou (1 à 4).")]
    [Range(1, 4)]
    public int gemSlots = 1;

    [Tooltip("Niveau maximum des gemmes pouvant être insérées.\n" +
             "Une gemme dont le niveau dépasse ce seuil ne peut pas être insérée.")]
    [Min(1)] public int maxGemLevel = 1;

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Crée une instance avec 3 défenses rollées (ratio, sans rareté/upgrade).</summary>
    public JewelryInstance CreateInstance()
        => new JewelryInstance(this, Random.value, Random.value, Random.value);
}

// ── Type de bijou ─────────────────────────────────────────────
public enum JewelrySlot { Ring, Necklace, Bracelet }

// =============================================================
// GemSlotInstance — état d'un slot gemme sur un bijou
// =============================================================
[System.Serializable]
public class GemSlotInstance
{
    public GemInstance gem = null;

    public bool IsEmpty  => gem == null;
    public bool IsFilled => gem != null;

    /// <summary>
    /// Tente d'insérer une gemme dans ce slot.
    /// Première insertion irréversible — GDD §5.7.
    /// </summary>
    public bool TryInsert(GemInstance newGem)
    {
        if (newGem == null)
        {
            Debug.LogWarning("[GemSlotInstance] TryInsert : gemme null.");
            return false;
        }
        if (IsFilled)
        {
            Debug.LogWarning($"[GemSlotInstance] Slot déjà occupé par {gem.Label} — insertion irréversible.");
            return false;
        }

        gem = newGem;
        gem.Reveal();
        Debug.Log($"[GemSlotInstance] Gemme insérée définitivement : {newGem.Label}");
        return true;
    }
}

// =============================================================
// JewelryInstance — données runtime d'un bijou équipé
// GDD v3.6 — §5.7
// =============================================================
[System.Serializable]
public class JewelryInstance
{
    public JewelryData data;
    public GemSlotInstance[] gemSlots;

    // Ratios rollés — fixés à la génération, position (0..1) dans la fourchette
    // DÉRIVÉE du SO. Pas de rareté/upgrade sur ce slot (GDD §5.2).
    public float rolledRatioMelee;
    public float rolledRatioRanged;
    public float rolledRatioMagic;

    public JewelryInstance(JewelryData source, float ratioMelee = 0f, float ratioRanged = 0f, float ratioMagic = 0f)
    {
        data              = source;
        rolledRatioMelee  = ratioMelee;
        rolledRatioRanged = ratioRanged;
        rolledRatioMagic  = ratioMagic;
        gemSlots          = new GemSlotInstance[source.gemSlots];
        for (int i = 0; i < gemSlots.Length; i++)
            gemSlots[i] = new GemSlotInstance();
    }

    // ── Défenses (Lerp depuis le SO — signature inchangée) ────
    public float MeleeDefense  => data != null ? Mathf.Lerp(data.MeleeDefenseLow,  data.MeleeDefenseHigh,  rolledRatioMelee)  : 0f;
    public float RangedDefense => data != null ? Mathf.Lerp(data.RangedDefenseLow, data.RangedDefenseHigh, rolledRatioRanged) : 0f;
    public float MagicDefense  => data != null ? Mathf.Lerp(data.MagicDefenseLow,  data.MagicDefenseHigh,  rolledRatioMagic)  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string      ItemId       => data != null ? data.itemID : "unknown_jewelry";
    public string      JewelryName  => data != null ? data.displayName.Get(LocalizationManager.CurrentLanguage) : "Jewelry";
    public Sprite      Icon         => data != null ? data.icon        : null;
    public JewelrySlot Slot         => data != null ? data.jewelrySlot : JewelrySlot.Ring;
    public int         MaxGemLevel  => data != null ? data.maxGemLevel : 1;

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;

    // ── Insertion gemme ───────────────────────────────────────

    /// <summary>
    /// Tente d'insérer une gemme dans le slot indiqué.
    /// Vérifie : index valide, gem.GemLevel ≤ maxGemLevel.
    /// Irréversible — GDD §5.7.
    /// </summary>
    public bool TryInsertGem(int slotIndex, GemInstance gem)
    {
        if (slotIndex < 0 || slotIndex >= gemSlots.Length)
        {
            Debug.LogWarning($"[JewelryInstance] Slot {slotIndex} invalide (max {gemSlots.Length - 1}).");
            return false;
        }
        if (gem.GemLevel > MaxGemLevel)
        {
            Debug.LogWarning($"[JewelryInstance] Gemme {gem.Label} (Lv{gem.GemLevel}) trop haute " +
                             $"pour ce bijou (max Lv{MaxGemLevel}).");
            return false;
        }
        return gemSlots[slotIndex].TryInsert(gem);
    }

    public IEnumerable<GemInstance> GetEquippedGems()
    {
        foreach (var slot in gemSlots)
            if (!slot.IsEmpty) yield return slot.gem;
    }
}
