using UnityEngine;
using System.Collections.Generic;

// =============================================================
// HelmetData — ScriptableObject template de casque
// Path : Assets/Scripts/Data/Inventory/Equipment/HelmetData.cs
// AetherTree GDD v3.5 — §5.3
//
// Pas de rareté, pas d'upgrade, pas de rune (GDD §5.0).
// Pas de restriction ArmorType — tout joueur peut équiper.
// Obtenu par drop, craft ou condition in-game.
//
// Stats fixes sur le SO (GDD §5.3) :
//   meleeDefense, rangedDefense, magicDefense  — fixes, pas de roll
//   requiredLevel                              — niveau minimum requis
//   unlockCondition                            — optionnel
//
// Pool de stats disponibles dans config.bonuses (GDD §5.3) :
//   MaxHP, MaxMana, RegenHP, RegenMana, Precision, Dodge
//   Résistances élémentaires (plafond 75% par élément — GDD §3.2)
//   ⚠ Pas de CritChance, CritMultiplier ni Points élémentaires sur le casque.
//
// Effets et bonus via EquipmentConfig (champ unique) :
//   config.bonuses, config.statusEffects,
//   config.debuffResistances, config.onHitEffects
// =============================================================

[CreateAssetMenu(fileName = "NewHelmet", menuName = "AetherTree/Equipment/HelmetData")]
public class HelmetData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string     helmetName = "Helmet";
    public Sprite     icon;
    public GameObject helmetPrefab;

    // ── Niveau & Condition ────────────────────────────────────
    [Header("Niveau & Condition")]
    [Tooltip("Niveau minimum du joueur requis pour équiper ce casque.")]
    [Min(1)] public int requiredLevel = 1;

    [Tooltip("Condition de déblocage optionnelle — certains casques ne s'obtiennent\n" +
             "que via une condition in-game (IConditionChecker). Null = toujours disponible.")]
    public ConditionEntry unlockCondition;

    // ── Défenses fixes ────────────────────────────────────────
    [Header("Défenses fixes (identiques sur toutes les instances — pas de roll)")]
    [Tooltip("Défense contre les attaques de type Melee — fixe, pas de modificateur de rareté.")]
    public float meleeDefense  = 0f;

    [Tooltip("Défense contre les attaques de type Ranged — fixe.")]
    public float rangedDefense = 0f;

    [Tooltip("Défense contre les attaques de type Magic — fixe.")]
    public float magicDefense  = 0f;

    // ── Configuration — effets et bonus ───────────────────────
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    [Tooltip("Bonus passifs fixes (HP, Mana, Précision, Esquive, Résistances élémentaires),\n" +
             "effets appliqués à l'attaque, résistances aux debuffs et effets On-Hit.\n" +
             "GDD §5.3 — pas de CritChance, CritMultiplier ni Points élémentaires autorisés.")]
    public EquipmentConfig config;

    // ── Description ───────────────────────────────────────────
    [Header("Description")]
    [TextArea]
    public string description = "";

    // ── Utilitaires ───────────────────────────────────────────

    public HelmetInstance CreateInstance() => new HelmetInstance(this);
}

// =============================================================
// HelmetInstance — wrapper runtime d'un casque équipé
// GDD v3.5 — §5.3
// =============================================================
[System.Serializable]
public class HelmetInstance
{
    public HelmetData data;

    public HelmetInstance(HelmetData source) { data = source; }

    // ── Défenses (lues directement sur le SO) ─────────────────
    public float MeleeDefense  => data != null ? data.meleeDefense  : 0f;
    public float RangedDefense => data != null ? data.rangedDefense : 0f;
    public float MagicDefense  => data != null ? data.magicDefense  : 0f;

    // ── Raccourcis SO ─────────────────────────────────────────
    public string HelmetName    => data != null ? data.helmetName    : "Helmet";
    public Sprite Icon          => data != null ? data.icon          : null;
    public int    RequiredLevel => data != null ? data.requiredLevel : 1;

    // ── Raccourcis config (4 slots fusionnés) ─────────────────
    public List<StatBonus>             Bonuses           => data?.config?.bonuses;
    public List<StatusEffectEntry>     StatusEffects     => data?.config?.statusEffects;
    public List<DebuffResistanceEntry> DebuffResistances => data?.config?.debuffResistances;
    public List<OnHitEffectEntry>      OnHitEffects      => data?.config?.onHitEffects;
}
