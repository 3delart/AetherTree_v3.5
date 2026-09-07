using UnityEngine;
using System.Collections.Generic;

// =============================================================
// QUESTDATA — ScriptableObject de définition de quête
// Path : Assets/Scripts/Data/Quests/QuestData.cs
// AetherTree GDD v31 — §25
// =============================================================

// =============================================================
// QUESTREWARDITEM — une ligne de récompense item
// =============================================================
[System.Serializable]
public class QuestRewardItem
{
    [Header("— Équipements —")]
    public WeaponData   weapon;
    public ArmorData    armor;
    public HelmetData   helmet;
    public GlovesData   gloves;
    public BootsData    boots;
    public JewelryData  jewelry;
    public SpiritData   spirit;

    [Header("— Consommables & Ressources —")]
    public ConsumableData consumable;
    public ResourceData   resource;
    [Min(1)]
    public int quantity = 1;

    [Header("— Gemmes & Runes —")]
    public GemData  gem;
    public RuneData rune;

    // ── Nom affiché ───────────────────────────────────────────

    public string DisplayName
    {
        get
        {
            string lang(LocalizedText t) => t.Get(LocalizationManager.CurrentLanguage);

            if (weapon     != null) return lang(weapon.displayName);
            if (armor      != null) return lang(armor.displayName);
            if (helmet     != null) return lang(helmet.displayName);
            if (gloves     != null) return lang(gloves.displayName);
            if (boots      != null) return lang(boots.displayName);
            if (jewelry    != null) return lang(jewelry.displayName);
            if (spirit     != null) return lang(spirit.displayName);
            if (consumable != null) return quantity > 1 ? $"{lang(consumable.displayName)} ×{quantity}" : lang(consumable.displayName);
            if (resource   != null) return quantity > 1 ? $"{lang(resource.displayName)} ×{quantity}"  : lang(resource.displayName);
            if (gem        != null) return gem.gemName;   // RuneData/GemData hors scope — reste string brut
            if (rune       != null) return rune.runeName; // idem
            return "";
        }
    }

    // ── Icône ─────────────────────────────────────────────────

    public Sprite GetIcon()
    {
        if (weapon     != null) return weapon.icon;
        if (armor      != null) return armor.icon;
        if (helmet     != null) return helmet.icon;
        if (gloves     != null) return gloves.icon;
        if (boots      != null) return boots.icon;
        if (jewelry    != null) return jewelry.icon;
        if (spirit     != null) return spirit.icon;
        if (consumable != null) return consumable.icon;
        if (resource   != null) return resource.icon;
        if (gem        != null) return gem.icon;
        if (rune       != null) return rune.icon;
        return null;
    }

    // ── Création InventoryItem ────────────────────────────────

    public InventoryItem CreateItem()
    {
        int qty = Mathf.Max(1, quantity);

        if (weapon     != null) return new InventoryItem(weapon.CreateDropInstance(WeaponData.RollRarity()));
        if (armor      != null) return new InventoryItem(armor.CreateDropInstance(ArmorData.RollRarity()));
        if (helmet     != null) return new InventoryItem(helmet.CreateInstance());
        if (gloves     != null) return new InventoryItem(gloves.CreateInstance());
        if (boots      != null) return new InventoryItem(boots.CreateInstance());
        if (jewelry    != null) return new InventoryItem(jewelry.CreateInstance());
        if (spirit     != null) return new InventoryItem(new SpiritInstance(spirit));
        if (consumable != null) return new InventoryItem(consumable.CreateInstance(qty));
        if (resource   != null) return new InventoryItem(resource.CreateInstance(qty));
        if (gem        != null) return new InventoryItem(gem.CreateDropInstance());
        if (rune       != null) return new InventoryItem(rune.CreateDropInstance());

        Debug.LogWarning("[QuestRewardItem] Aucun SO assigné dans cette entrée.");
        return null;
    }
}

// =============================================================
// QUESTDATA
// =============================================================
[CreateAssetMenu(fileName = "quest_", menuName = "AetherTree/Quetes/QuestData")]
public class QuestData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("ID unique — ex: quest_braven_01. Doit être unique dans le projet.")]
    public string    questID   = "";
    public string    questName = "Quest";
    public QuestRank questRank = QuestRank.Secondary;
    [TextArea(2, 5)]
    public string    description = "";

    [Header("Prérequis")]
    public int       minLevel          = 1;
    [Tooltip("Quête à compléter avant celle-ci (chaîne de quêtes)")]
    public QuestData prerequisiteQuest = null;

    [Header("Objectifs")]
    [Tooltip("True = séquentiels (avec groupID pour le mix)\nFalse = tous actifs simultanément")]
    public bool objectivesInOrder = false;
    public List<QuestObjective> objectives = new List<QuestObjective>();

    [Header("Récompenses")]
    public int xpReward    = 100;
    public int aerisReward = 50;

    [Header("Récompenses items")]
    [Tooltip("Ajouter autant d'entrées que voulu.\nRemplir UN SEUL champ par entrée.")]
    public List<QuestRewardItem> rewardItems = new List<QuestRewardItem>();

    // ── Helpers ───────────────────────────────────────────────

    public bool AllObjectivesComplete()
    {
        if (objectives == null || objectives.Count == 0) return true;
        foreach (var o in objectives)
            if (!o.IsComplete) return false;
        return true;
    }

    public List<int> GetActiveObjectiveIndices()
    {
        var result = new List<int>();
        if (objectives == null) return result;

        if (!objectivesInOrder)
        {
            for (int i = 0; i < objectives.Count; i++)
                if (!objectives[i].IsComplete) result.Add(i);
            return result;
        }

        for (int i = 0; i < objectives.Count; i++)
        {
            if (objectives[i].IsComplete) continue;
            string activeGroup = objectives[i].groupID;
            for (int j = i; j < objectives.Count; j++)
            {
                if (objectives[j].groupID == activeGroup && !objectives[j].IsComplete)
                    result.Add(j);
                else if (objectives[j].groupID != activeGroup)
                    break;
            }
            return result;
        }
        return result;
    }

    public void ResetProgress()
    {
        if (objectives == null) return;
        foreach (var o in objectives) o.currentCount = 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(questID))
            questID = name;
    }
#endif
}

// =============================================================
public enum QuestRank { Main = 0, Secondary = 1, Daily = 2, Guild = 3, Event = 4, Hidden = 5 }

// =============================================================
[System.Serializable]
public class QuestObjective
{
    [Tooltip("Description affichée. Ex: Tuer 10 poulets")]
    public string description = "";

    [Tooltip("Objectifs avec le même groupID sont actifs simultanément en mode séquentiel.")]
    public string groupID = "";

    public QuestObjectiveType type = QuestObjectiveType.Kill;

    [Tooltip("Kill / Boss — glisser le MobData")]
    [ShowIf(nameof(type), QuestObjectiveType.Kill, QuestObjectiveType.Boss)]
    public MobData targetMob;

    [Tooltip("TalkTo — glisser le PNJData")]
    [ShowIf(nameof(type), QuestObjectiveType.TalkTo)]
    public PNJData targetPNJ;

    [Tooltip("Deliver / Gather / Craft — glisser le SO item")]
    [ShowIf(nameof(type), QuestObjectiveType.Deliver, QuestObjectiveType.Gather, QuestObjectiveType.Craft)]
    public ScriptableObject targetItem;

    [Tooltip("Explore — ID de zone (string)")]
    [ShowIf(nameof(type), QuestObjectiveType.Explore)]
    public string targetZoneID = "";

    public int requiredCount = 1;
    public int currentCount  = 0;

    public bool   IsComplete    => currentCount >= requiredCount;
    public string ProgressLabel => $"{currentCount}/{requiredCount}";

    public string TargetName => type switch
    {
        QuestObjectiveType.Kill    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.Boss    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.TalkTo  => targetPNJ  != null ? targetPNJ.pnjName  : "",
        QuestObjectiveType.Deliver => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Gather  => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Craft   => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Explore => targetZoneID,
        _                          => ""
    };

    public bool Increment(int amount = 1)
    {
        if (IsComplete) return false;
        currentCount = Mathf.Min(currentCount + amount, requiredCount);
        return IsComplete;
    }
}

// =============================================================
public enum QuestObjectiveType { Kill = 0, TalkTo = 1, Deliver = 2, Gather = 3, Explore = 4, Craft = 5, Boss = 6 }