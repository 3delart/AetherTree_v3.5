using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CHARACTERPROGRESS — Données complètes de sauvegarde
// Path : Assets/Scripts/Progression/Save/CharacterProgress.cs
// AetherTree GDD v31
// =============================================================

// ── Item sauvegardé ───────────────────────────────────────────
[System.Serializable]
public class SavedItem
{
    public string category;
    public string soName;
    public int    rarityRank;
    public int    upgradeLevel;
    public int    quantity;
    public bool   isEquipped;
    public string slot;
    public int    spiritLevel;
    public int    spiritXP;
    public string jewelrySlot;
}

// ── Slot SkillBar ─────────────────────────────────────────────
[System.Serializable]
public class SavedSkillSlot
{
    public int    slotIndex;
    public string skillName;
}

// ── Quête sauvegardée ─────────────────────────────────────────
[System.Serializable]
public class SavedQuest
{
    public string     questID;
    public QuestState state;
    public List<int>  objectiveCounts = new List<int>();
}


// ── Progression d'une condition en cours ─────────────────────
[System.Serializable]
public class SavedConditionProgress
{
    public string     conditionID;
    public List<int>  entryCounters  = new List<int>();
    public List<bool> entryCompleted = new List<bool>();
}

// ── Mail sauvegardé ───────────────────────────────────────────
[System.Serializable]
public class SavedMail
{
    public string mailID;
    public string senderName;
    public bool   isFromServer;
    public string sentAt;          // DateTime sérialisé en string ISO
    public string subject;
    public string body;
    public bool   isRead;
    public bool   rewardClaimed;

    // Récompense
    public bool   hasReward;
    public int    rewardType;      // cast de RewardType en int
    public string rewardSkillName; // nom du SO SkillData
    public string rewardTitle;
    public string rewardItemID;
    public int    rewardItemQuantity;
    public string rewardRecipeID;
    public string rewardDescription;
}

// ── Paires clé/valeur ─────────────────────────────────────────
[System.Serializable]
public class StringIntPair
{
    public string key;
    public int    value;
    public StringIntPair(string k, int v) { key = k; value = v; }
}

[System.Serializable]
public class StringFloatPair
{
    public string key;
    public float  value;
    public StringFloatPair(string k, float v) { key = k; value = v; }
}

// =============================================================
// CHARACTERPROGRESS
// =============================================================
[System.Serializable]
public class CharacterProgress
{
    // ① Identité & Progression
    public string characterName = "";
    public int    level         = 1;
    public int    xpCombat      = 0;
    public string activeTitle   = "";

    // ② Réputation
    public int worldReputation = 0;
    public int pvpReputation   = 0;

    // ③ Position & Map
    public string lastMap = "Map_01";
    public float  posX    = 0f;
    public float  posY    = 0f;
    public float  posZ    = 0f;

    // ④ Aeris
    public int aeris = 0;

    // ⑤ Équipements + Inventaire
    public List<SavedItem> items = new List<SavedItem>();

    // ⑥ Skills débloqués
    public List<string> unlockedSkillNames      = new List<string>();
    public List<string> unlockedPermanentNames  = new List<string>(); // PermanentSkillData
    public List<string> unlockedPassiveNames    = new List<string>(); // PassiveSkillData

    // ⑦ Slots SkillBar (0–9)
    public List<SavedSkillSlot> skillBarSlots = new List<SavedSkillSlot>();

    // ⑧ Conditions débloquées
    public List<string> unlockedConditionIDs = new List<string>();

    // ⑨ Compteurs activité
    public List<StringIntPair> activityCountersList = new List<StringIntPair>();

    // ⑩ Quêtes
    public List<SavedQuest> quests = new List<SavedQuest>();

    // ⑪ Jauge élémentaire
    public List<SavedElementAffinity> elementAffinities = new List<SavedElementAffinity>();

    // ⑫ Progression conditions en cours
    public List<SavedConditionProgress> conditionProgresses = new List<SavedConditionProgress>();

    // ⑬ Mails (Mailbox)
    public List<SavedMail> mails = new List<SavedMail>();
}