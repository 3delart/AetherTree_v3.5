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

    [Header("— Filtre arme —")]
    [Tooltip("Any (défaut) = récompense universelle. Une famille précise = cette entrée n'est\n" +
             "accordée que si l'arme équipée du joueur appartient à cette famille au moment du\n" +
             "turn-in (WeaponType.GetStartingFamily(), même regroupement que la compat skill).\n" +
             "Aucun choix proposé au joueur — résolution automatique.")]
    public WeaponType requiredWeaponFamily = WeaponType.Any;

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
    public RequirementSet requirements = new RequirementSet();

    [Header("Objectifs")]
    [Tooltip("True = séquentiels (avec groupID pour le mix)\nFalse = tous actifs simultanément")]
    public bool objectivesInOrder = false;
    public List<QuestObjective> objectives = new List<QuestObjective>();

    [Header("Récompenses")]
    public int xpReward       = 100;
    public int aerisReward    = 50;
    public int prestigeReward = 50;

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

        if (requirements?.requirements != null)
        {
            foreach (var r in requirements.requirements)
            {
                if (r == null || r.field != RequirementField.ItemOwned || r.item == null) continue;
                if (!(r.item is ResourceData) && !(r.item is ConsumableData))
                    Debug.LogWarning($"[QUEST] {questName} : prérequis ItemOwned sur '{r.item.name}' — " +
                        "InventorySystem.GetItemCount ne sait compter que ResourceData/ConsumableData, " +
                        "ce prérequis sera TOUJOURS considéré non rempli pour ce type d'item.");
            }
        }

        if (objectives != null)
        {
            var seenIDs = new System.Collections.Generic.HashSet<string>();
            foreach (var o in objectives)
            {
                if (o == null) continue;
                // Vide (nouvel objectif) OU déjà vu (objectif dupliqué via "+" dans l'Inspector,
                // qui copie objectiveID avec le reste) — les deux cas doivent regénérer, sinon
                // deux objectifs partagent la même clé de sauvegarde (voir LoadSaveData).
                if (string.IsNullOrEmpty(o.objectiveID) || seenIDs.Contains(o.objectiveID))
                    o.objectiveID = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                seenIDs.Add(o.objectiveID);
            }
        }

        if (objectives != null)
        {
            foreach (var o in objectives)
                if (o != null && o.type == QuestObjectiveType.Boss && o.targetMob != null && !o.targetMob.IsBoss())
                    Debug.LogWarning($"[QUEST] {questName} : objectif Boss '{o.description}' pointe vers " +
                        $"'{o.targetMob.mobName}' qui n'est pas un boss (MobData.IsBoss() == false) — " +
                        "probable erreur de configuration.");
        }
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

    [Tooltip("Clé technique STABLE — ne change jamais, utilisée pour la sauvegarde de la\n" +
             "progression (voir QuestSystem.GetSaveData/LoadSaveData). Auto-remplie si vide,\n" +
             "ne JAMAIS afficher au joueur.")]
    public string objectiveID = "";

    [Tooltip("Kill / Boss — glisser le MobData")]
    [ShowIf(nameof(type), QuestObjectiveType.Kill, QuestObjectiveType.Boss)]
    public MobData targetMob;

    [Tooltip("TalkTo — glisser le PNJData")]
    [ShowIf(nameof(type), QuestObjectiveType.TalkTo)]
    public PNJData targetPNJ;

    [Tooltip("Gather / Craft — glisser le SO item (doit hériter de ItemData, ex: ResourceData,\n" +
             "ConsumableData, WeaponData... — un item Gem/Rune, qui n'hérite pas d'ItemData, ne\n" +
             "peut pas être ciblé par ce type d'objectif)")]
    [ShowIf(nameof(type), QuestObjectiveType.Gather, QuestObjectiveType.Craft)]
    public ScriptableObject targetItem;

    [Tooltip("Explore — glisser le prefab de la zone (composant ZoneTrigger de CE prefab, pas une\n" +
             "instance de scène). Plus de string zoneID tapée à la main (ZoneData retiré 2026-10-04).")]
    [ShowIf(nameof(type), QuestObjectiveType.Explore)]
    public ZoneTrigger targetZonePrefab;

    public int requiredCount = 1;
    public int currentCount  = 0;

    public bool   IsComplete    => currentCount >= requiredCount;
    public string ProgressLabel => $"{currentCount}/{requiredCount}";

    public string TargetName => type switch
    {
        QuestObjectiveType.Kill    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.Boss    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.TalkTo  => targetPNJ  != null ? targetPNJ.pnjName  : "",
        QuestObjectiveType.Gather  => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Craft   => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Explore => targetZonePrefab != null ? targetZonePrefab.zoneID : "",
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
// 2 retiré (2026-10-04) — Deliver fusionné dans Gather (même mécanique exacte, zéro asset
// n'utilisait cet ordinal — vérifié par grep direct sur les .asset avant suppression). Ordinal 2
// jamais réutilisé.
public enum QuestObjectiveType { Kill = 0, TalkTo = 1, Gather = 3, Explore = 4, Craft = 5, Boss = 6 }