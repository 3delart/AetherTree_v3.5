using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CHARACTERDATA — ScriptableObject TEMPLATE uniquement
// Path : Assets/Scripts/Data/CharacterData.cs
// AetherTree GDD v3.5 — §3.2
//
// Définit les stats de base, l'arme de départ et les skills
// initiaux d'un personnage. Ne stocke PAS de données runtime.
// Les données runtime (XP, affinités, compteurs, titre actif)
// vivent dans Player.cs.
//
// Stats de base (GDD v3.5 §3.2) :
//   Les valeurs baseMaxHP / hpPerLevel varient par WeaponCategory.
//   CharacterStats.RecalculateStats() appelle GetBaseHP(WeaponCategory)
//   et GetHPPerLevel(WeaponCategory) pour obtenir la bonne courbe.
//   Les modificateurs (×1.20 Melee, ×0.80 Ranged...) sont encodés
//   directement dans les valeurs — pas de multiplicateur à la volée.
//
// Progression HP par WeaponCategory (GDD v3.5 §3.2) :
//   Melee  : base 600,  +67/lv  → lv100 ≈ 7 233  (cible 7 260)
//   Magic  : base 500,  +56/lv  → lv100 ≈ 6 044  (cible 6 050)
//   Ranged : base 400,  +47/lv  → lv100 ≈ 5 053  (cible 5 040)
//
// Progression Mana par WeaponCategory (GDD v3.5 §3.2) :
//   Melee  : base 100,  +20/lv  → lv100 ≈ 2 080  (cible 2 080) ✓
//   Magic  : base 140,  +40/lv  → lv100 ≈ 4 100  (cible 4 100) ✓
//   Ranged : base 120,  +30/lv  → lv100 ≈ 3 090  (cible 3 090) ✓
//
// RegenHP / RegenMana : fixes — pas de progression par niveau (GDD v3.5 §3.2).
// MoveSpeed           : fixe à 4f — modifié uniquement par équipement / monture.
//
// ⚠ Valeurs à affiner en alpha test (GDD v3.5 §3.3).
// =============================================================

[CreateAssetMenu(fileName = "NewCharacter", menuName = "AetherTree/Characters/CharacterData")]
public class CharacterData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string characterName = "Aventurier";
    public Sprite portrait;

    // ── Arme de départ — choix définitif ─────────────────────
    [Header("Arme de départ (choix irrévocable)")]
    [Tooltip("Arme sélectionnée à la création — définit WeaponCategory et ArmorType pour toute la partie")]
    public WeaponData startingWeapon;

    // ── Stats de base — communes à toutes les WeaponCategory ─
    [Header("Stats de base")]
    [Tooltip("RegenHP par seconde — fixe, pas de progression par niveau. GDD v3.5 §3.2")]
    public float baseRegenHP   = 1f;

    [Tooltip("RegenMana par seconde — fixe, pas de progression par niveau. GDD v3.5 §3.2")]
    public float baseRegenMana = 0.5f;

    [Tooltip("Vitesse de déplacement de base — modifiée uniquement par équipement / monture. GDD v3.5 §3.2")]
    public float baseMoveSpeed = 4f;

    // ── Progression HP par WeaponCategory ────────────────────
    // GDD v3.5 §3.2 — chaque catégorie a sa propre courbe HP.
    // Melee : plus exposé → plus de HP. Ranged : mobilité → moins de HP.
    // Ces valeurs sont lues par CharacterStats.RecalculateStats().
    [Header("Progression HP par WeaponCategory (GDD v3.5 §3.2)")]

    [Tooltip("HP de base à lv1 — Melee. GDD cible lv1=600")]
    public float baseMaxHP_Melee   = 600f;
    [Tooltip("+HP par niveau — Melee. GDD cible lv100≈7260")]
    public float hpPerLevel_Melee  = 67f;

    [Tooltip("HP de base à lv1 — Magic. GDD cible lv1=500")]
    public float baseMaxHP_Magic   = 500f;
    [Tooltip("+HP par niveau — Magic. GDD cible lv100≈6050")]
    public float hpPerLevel_Magic  = 56f;

    [Tooltip("HP de base à lv1 — Ranged. GDD cible lv1=400")]
    public float baseMaxHP_Ranged  = 400f;
    [Tooltip("+HP par niveau — Ranged. GDD cible lv100≈5040")]
    public float hpPerLevel_Ranged = 47f;

    // ── Progression Mana par WeaponCategory ──────────────────
    // GDD v3.5 §3.2 — Magic a plus de Mana, Melee moins.
    [Header("Progression Mana par WeaponCategory (GDD v3.5 §3.2)")]

    [Tooltip("Mana de base à lv1 — Melee. GDD cible lv1=100")]
    public float baseMaxMana_Melee   = 100f;
    [Tooltip("+Mana par niveau — Melee. GDD cible lv100≈2080")]
    public float manaPerLevel_Melee  = 20f;

    [Tooltip("Mana de base à lv1 — Magic. GDD cible lv1=140")]
    public float baseMaxMana_Magic   = 140f;
    [Tooltip("+Mana par niveau — Magic. GDD cible lv100≈4100")]
    public float manaPerLevel_Magic  = 40f;

    [Tooltip("Mana de base à lv1 — Ranged. GDD cible lv1=120")]
    public float baseMaxMana_Ranged  = 120f;
    [Tooltip("+Mana par niveau — Ranged. GDD cible lv100≈3090")]
    public float manaPerLevel_Ranged = 30f;

    // ── XP requis par niveau ──────────────────────────────────
    [Header("Courbe XP")]
    [Tooltip("XP combat requis pour passer chaque niveau (index 0 = niveau 1→2)")]
    public List<int> xpThresholds = new List<int> { 100, 250, 500, 900, 1500 };

    // ── Skills de départ ──────────────────────────────────────
    [Header("Skills de départ")]
    [Tooltip("Skills disponibles dès le début — placés automatiquement dans la SkillBar")]
    public List<SkillData> startingSkills = new List<SkillData>();

    // =========================================================
    // ACCESSEURS — lus par CharacterStats.RecalculateStats()
    // =========================================================

    /// <summary>Catégorie d'arme déduite de l'arme de départ.</summary>
    public WeaponCategory WeaponCategory =>
        startingWeapon != null ? startingWeapon.Category : WeaponCategory.Melee;

    /// <summary>ArmorType équipable par ce personnage.</summary>
    public ArmorType ArmorType =>
        startingWeapon != null ? startingWeapon.LinkedArmorType : ArmorType.Lourde;

    /// <summary>
    /// HP de base à lv1 selon la WeaponCategory du joueur.
    /// Appelé par CharacterStats.RecalculateStats().
    /// </summary>
    public float GetBaseHP(WeaponCategory category)
    {
        switch (category)
        {
            case WeaponCategory.Magic:  return baseMaxHP_Magic;
            case WeaponCategory.Ranged: return baseMaxHP_Ranged;
            default:                    return baseMaxHP_Melee;
        }
    }

    /// <summary>
    /// HP gagnés par niveau selon la WeaponCategory du joueur.
    /// Appelé par CharacterStats.RecalculateStats().
    /// </summary>
    public float GetHPPerLevel(WeaponCategory category)
    {
        switch (category)
        {
            case WeaponCategory.Magic:  return hpPerLevel_Magic;
            case WeaponCategory.Ranged: return hpPerLevel_Ranged;
            default:                    return hpPerLevel_Melee;
        }
    }

    /// <summary>
    /// Mana de base à lv1 selon la WeaponCategory du joueur.
    /// Appelé par CharacterStats.RecalculateStats().
    /// </summary>
    public float GetBaseMana(WeaponCategory category)
    {
        switch (category)
        {
            case WeaponCategory.Magic:  return baseMaxMana_Magic;
            case WeaponCategory.Ranged: return baseMaxMana_Ranged;
            default:                    return baseMaxMana_Melee;
        }
    }

    /// <summary>
    /// Mana gagnée par niveau selon la WeaponCategory du joueur.
    /// Appelé par CharacterStats.RecalculateStats().
    /// </summary>
    public float GetManaPerLevel(WeaponCategory category)
    {
        switch (category)
        {
            case WeaponCategory.Magic:  return manaPerLevel_Magic;
            case WeaponCategory.Ranged: return manaPerLevel_Ranged;
            default:                    return manaPerLevel_Melee;
        }
    }

    /// <summary>
    /// XP nécessaire pour passer au niveau suivant.
    /// Utilise la liste xpThresholds, puis une formule exponentielle si dépassée.
    /// </summary>
    public int GetXPThreshold(int currentLevel)
    {
        int idx = currentLevel - 1;
        if (idx < 0) return xpThresholds.Count > 0 ? xpThresholds[0] : 100;
        if (idx < xpThresholds.Count) return xpThresholds[idx];

        // double + clamp — 1.5^n explose largement au-delà de l'int à haut niveau
        // (ex: niveau 85 avec 5 paliers auteur → exposant 80 → dépasse int.MaxValue,
        // Mathf.RoundToInt wrap silencieusement en négatif sans le clamp).
        double extrapolated = xpThresholds[xpThresholds.Count - 1] *
            System.Math.Pow(1.5, idx - xpThresholds.Count + 1);
        return extrapolated >= int.MaxValue ? int.MaxValue : (int)System.Math.Round(extrapolated);
    }
}
