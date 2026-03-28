using UnityEngine;
using System.Collections.Generic;

// =============================================================
// JewelryData — ScriptableObject template de bijou
// Path : Assets/Scripts/Data/Inventory/Equipment/JewelryData.cs
// AetherTree GDD v3.5 — §5.5
//
// 3 slots de bijoux : 1 Anneau, 1 Collier, 1 Bracelet.
// Pas de rareté, pas d'upgrade, pas de fusion (GDD §5.0).
//
// Stats fixes sur le SO (GDD §5.5) :
//   jewelrySlot                — Ring / Necklace / Bracelet
//   meleeDefense, rangedDefense, magicDefense — contribution défensive principale
//   gemSlots                   — nombre de slots gemme (1 à 4)
//   maxGemLevel                — niveau maximum de gemme insérable
//   requiredLevel
//
// Pool de stats disponibles dans config.bonuses (GDD §5.5) :
//   MaxHP, MaxMana, Precision, Dodge
//   ⚠ Pas de CritChance, Points élémentaires ni Résistances élémentaires
//     en bonusLines — ces stats viennent des gemmes insérées.
//
// Gemmes (GDD §5.5) :
//   Insertion irréversible — aucun moyen d'extraire une gemme.
//   Pool : HP/Mana, Défenses, CritChance, CritMultiplier,
//          Précision/Esquive, Résistances élémentaires, Points élémentaires.
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

[CreateAssetMenu(fileName = "NewJewelry", menuName = "AetherTree/Equipment/JewelryData")]
public class JewelryData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string      jewelryName = "Jewelry";

    [Tooltip("Slot de bijou — détermine dans quel emplacement ce bijou peut être équipé.\n" +
             "GDD §5.5 : 1 Anneau, 1 Collier, 1 Bracelet simultanément.")]
    public JewelrySlot jewelrySlot = JewelrySlot.Ring;
    public Sprite      icon;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau minimum du joueur requis pour équiper ce bijou.")]
    [Min(1)] public int requiredLevel = 1;

    // ── Défenses fixes ────────────────────────────────────────
    [Header("Défenses fixes (contribution défensive principale du bijou — GDD §5.5)")]
    public float meleeDefense  = 0f;
    public float rangedDefense = 0f;
    public float magicDefense  = 0f;

    // ── Gemmes ────────────────────────────────────────────────
    [Header("Gemmes (GDD §5.5)")]
    [Tooltip("Nombre de slots gemme disponibles sur ce bijou (1 à 4).")]
    [Range(1, 4)]
    public int gemSlots = 1;

    [Tooltip("Niveau maximum des gemmes pouvant être insérées.\n" +
             "Une gemme dont le niveau dépasse ce seuil ne peut pas être insérée.")]
    [Min(1)] public int maxGemLevel = 1;

    // ── Configuration — effets et bonus ───────────────────────
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    [Tooltip("Bonus passifs fixes (HP, Mana, Précision, Esquive uniquement — GDD §5.5),\n" +
             "effets appliqués à l'attaque, résistances aux debuffs et effets On-Hit.\n" +
             "⚠ Pas de CritChance, Points élémentaires ni Résistances en bonusLines —\n" +
             "   ces stats viennent des gemmes insérées.")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public JewelryInstance CreateInstance() => new JewelryInstance(this);
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
    /// Première insertion irréversible — GDD §5.5.
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
// GDD v3.5 — §5.5
// =============================================================
[System.Serializable]
public class JewelryInstance
{
    public JewelryData data;
    public GemSlotInstance[] gemSlots;

    public JewelryInstance(JewelryData source)
    {
        data     = source;
        gemSlots = new GemSlotInstance[source.gemSlots];
        for (int i = 0; i < gemSlots.Length; i++)
            gemSlots[i] = new GemSlotInstance();
    }

    // ── Défenses (lues sur le SO) ─────────────────────────────
    public float MeleeDefense  => data != null ? data.meleeDefense  : 0f;
    public float RangedDefense => data != null ? data.rangedDefense : 0f;
    public float MagicDefense  => data != null ? data.magicDefense  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string      JewelryName  => data != null ? data.jewelryName : "Jewelry";
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
    /// Irréversible — GDD §5.5.
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
