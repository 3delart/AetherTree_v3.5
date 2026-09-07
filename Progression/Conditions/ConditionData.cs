using System.Collections.Generic;
using UnityEngine;

// =============================================================
// CONDITIONDATA.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Core/ConditionData.cs
// AetherTree GDD v31
//
// ScriptableObject définissant une condition de déblocage.
// Une condition = N sous-conditions (ConditionEntry) + un mode d'évaluation.
//
// Modes :
//   Parallel    — toutes les entries s'accumulent simultanément.
//   Ordered     — Entry[N] ne commence à compter qu'une fois Entry[N-1] validée.
//   AllRequired — alias sémantique de Parallel. Toutes requises, ordre libre.
//
// Rewards :
//   Glisser directement les SOs depuis le projet.
//   Equipment → un seul champ ScriptableObject générique (WeaponData, ArmorData, etc.)
//               le RewardType indique ce que c'est pour le MailboxSystem.
//   Pet / Recipe → string ID en attendant les SOs dédiés.
// =============================================================

public enum CounterScope
{
    Character = 0,
    Account   = 1,
    Server    = 2,  // v4 uniquement, ignoré en v3.5
}

public enum ConditionMode
{
    Parallel    = 0,
    Ordered     = 1,
    AllRequired = 2,
}

public enum RewardType
{
    None          = 0,

    // ── Skills & Titres ───────────────────────────────────────
    Skill         = 1,
    SkillAndTitle = 2,
    Title         = 3,

    // ── Équipements (glisser dans rewardEquipment) ────────────
    Weapon        = 4,
    Armor         = 5,
    Helmet        = 6,
    Gloves        = 7,
    Boots         = 8,
    Jewelry       = 9,
    Spirit        = 10,
    CosmeticHead  = 11,
    CosmeticBody  = 12,
    Talisman      = 13,

    // ── Items ─────────────────────────────────────────────────
    Resource      = 14,
    Consumable    = 15,

    // ── Futurs SOs (string ID pour l'instant) ─────────────────
    Recipe        = 16,
    Pet           = 17,

    Other         = 18,

    Quest         = 19,
}

[System.Serializable]
public class ConditionReward
{
    [Header("Filtre arme")]
    [Tooltip("Any = tout le monde reçoit ce reward")]
    public WeaponType weaponType = WeaponType.Any;

    [Header("Type de récompense")]
    public RewardType rewardType = RewardType.None;

    // ── Skill / Passif ────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Skill, RewardType.SkillAndTitle, Header = "Skill / Passif")]
    public SkillData rewardSkill;

    // ── Titre ─────────────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Title, RewardType.SkillAndTitle, Header = "Titre")]
    public string rewardTitle;

    // ── Équipement (générique) ────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Weapon, RewardType.Armor, RewardType.Helmet, RewardType.Gloves,
        RewardType.Boots, RewardType.Jewelry, RewardType.Spirit, RewardType.CosmeticHead, RewardType.CosmeticBody,
        RewardType.Talisman, Header = "Équipement")]
    [Tooltip(
        "Glisser ici le SO d'équipement correspondant au rewardType :\n" +
        "  Weapon      → WeaponData\n" +
        "  Armor       → ArmorData\n" +
        "  Helmet      → HelmetData\n" +
        "  Gloves      → GlovesData\n" +
        "  Boots       → BootsData\n" +
        "  Jewelry     → JewelryData\n" +
        "  Spirit      → SpiritData\n" +
        "  CosmeticHead→ CosmeticDataHead\n" +
        "  CosmeticBody→ CosmeticDataBody\n" +
        "  Talisman    → TalismanData")]
    public ScriptableObject rewardEquipment;

    // ── Ressource ─────────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Resource, Header = "Ressource")]
    public ResourceData    rewardResource;
    [ShowIf(nameof(rewardType), RewardType.Resource)]
    public int             rewardResourceQuantity = 1;

    // ── Consommable ───────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Consumable, Header = "Consommable")]
    public ConsumableData  rewardConsumable;
    [ShowIf(nameof(rewardType), RewardType.Consumable)]
    public int             rewardConsumableQuantity = 1;

    // ── Pet (string ID — SO à venir) ──────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Pet, Header = "Pet (string ID — SO à venir)")]
    public string rewardPetID;

    // ── Recette ────────────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Recipe, Header = "Recette")]
    public RecipeData rewardRecipe;

    // ── Quête ─────────────────────────────────────────────────
    [ShowIf(nameof(rewardType), RewardType.Quest, Header = "Quête")]
    public QuestData rewardQuest;

    // ── Utilitaires ───────────────────────────────────────────

    /// <summary>Vérifie que le reward est cohérent (SO assigné pour son type).</summary>
    public bool IsValid()
    {
        switch (rewardType)
        {
            case RewardType.None:    return false;
            case RewardType.Skill:
            case RewardType.SkillAndTitle: return rewardSkill != null;
            case RewardType.Title:         return !string.IsNullOrEmpty(rewardTitle);
            case RewardType.Weapon:
            case RewardType.Armor:
            case RewardType.Helmet:
            case RewardType.Gloves:
            case RewardType.Boots:
            case RewardType.Jewelry:
            case RewardType.Spirit:
            case RewardType.CosmeticHead:
            case RewardType.CosmeticBody:
            case RewardType.Talisman:      return rewardEquipment != null;
            case RewardType.Resource:      return rewardResource   != null;
            case RewardType.Consumable:    return rewardConsumable != null;
            case RewardType.Pet:           return !string.IsNullOrEmpty(rewardPetID);
            case RewardType.Recipe:        return rewardRecipe != null;
            case RewardType.Quest:         return rewardQuest != null;
            default:                       return true;
        }
    }

    /// <summary>
    /// Retourne le SO d'équipement casté dans le bon type.
    /// Retourne null si le cast échoue (mauvais type assigné).
    /// </summary>
    public T GetEquipment<T>() where T : ScriptableObject => rewardEquipment as T;
}

[CreateAssetMenu(fileName = "cond_", menuName = "AetherTree/Progression/ConditionData")]
public class ConditionData : ScriptableObject
{
    [Header("Identifiant unique")]
    public string conditionID;

    [Header("Mode d'évaluation")]
    [Tooltip(
        "Parallel    = toutes les entries s'accumulent en même temps.\n" +
        "Ordered     = chaque entry attend que la précédente soit validée.\n" +
        "AllRequired = toutes requises, ordre libre.")]
    public ConditionMode mode = ConditionMode.Parallel;

    [Header("Sous-conditions")]
    public List<ConditionEntry> conditions = new List<ConditionEntry>();

    [Header("Récompenses")]
    public List<ConditionReward> rewards = new List<ConditionReward>();

    // ── Mail (sujet + corps envoyés au déblocage — voir MailboxSystem.SendRewardMail) ──
    // Secret : pas de champ dédié — le designer choisit lui-même un nom/description
    // qui ne spoil pas la condition plutôt qu'un mode "caché" séparé.
    [Header("Mail")]
    [Tooltip("Nom utilisé dans le sujet du mail.")]
    public string displayName;
    [TextArea]
    [Tooltip("Corps du mail.")]
    public string description;

    // ── Utilitaires ───────────────────────────────────────────

    public CounterScope GetDominantScope()
    {
        var dominant = CounterScope.Character;
        foreach (var entry in conditions)
        {
            if (entry == null) continue;
            if (entry.scope > dominant) dominant = entry.scope;
        }
        return dominant;
    }

    public List<ConditionReward> GetEligibleRewards(WeaponType equippedWeapon)
    {
        var result = new List<ConditionReward>();
        if (rewards == null) return result;
        foreach (var reward in rewards)
        {
            if (reward == null || !reward.IsValid())                           continue;
            if (reward.weaponType == WeaponType.Any || reward.weaponType == equippedWeapon)
                result.Add(reward);
        }
        return result;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Validation editor : avertit si un reward est invalide (SO manquant).
    /// Appelé automatiquement par Unity à chaque modification dans l'Inspector.
    /// </summary>
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(conditionID))
            conditionID = name;

        if (rewards == null) return;
        for (int i = 0; i < rewards.Count; i++)
        {
            var r = rewards[i];
            if (r == null || r.rewardType == RewardType.None) continue;
            if (!r.IsValid())
                UnityEditor.EditorUtility.SetDirty(this); // force refresh Inspector
        }
    }
#endif
}
