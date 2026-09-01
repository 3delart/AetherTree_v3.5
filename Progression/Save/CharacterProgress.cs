using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CHARACTERPROGRESS — Données complètes de sauvegarde
// Path : Assets/Scripts/Progression/Save/CharacterProgress.cs
// AetherTree GDD v31
// =============================================================

// =============================================================
// Items sauvegardés — une classe par catégorie, chacune ne portant
// QUE ses propres champs (JsonUtility ne fait pas de polymorphisme :
// une List<SavedItem> unique aurait forcé chaque entrée à porter les
// champs de TOUTES les catégories, même sans rapport — remplacé par
// une liste typée par catégorie sur CharacterProgress, voir plus bas).
//
// Les ratios rollés (ratio-roll — voir a implémenter/note-systeme-ratio-degats.md)
// sont indispensables : sans eux, recréer l'instance au chargement
// re-rollerait des valeurs aléatoires différentes à chaque partie
// (CreateDropInstance()/CreateInstance() appellent Random.value en
// interne) — rarityRank/upgradeLevel seuls ne suffisent pas à
// reproduire les mêmes stats finales.
// =============================================================

[System.Serializable]
public class SavedWeapon
{
    public string soName;
    public bool   isEquipped;
    public int    rarityRank;
    public int    upgradeLevel;
    public bool   hasAethernelleSeal; // GDD §5.13Bis — cosmétique, aucun impact sur les stats
    public float  ratioMin;
    public float  ratioMax;
    public float  ratioPrecision;
    // TODO : rune insérée — RuneData hors scope du ratio-roll pour l'instant,
    // voir a implémenter/note-rune-gem-hors-scope.md.
}

[System.Serializable]
public class SavedArmor
{
    public string soName;
    public bool   isEquipped;
    public int    rarityRank;
    public int    upgradeLevel;
    public bool   hasAethernelleSeal; // GDD §5.13Bis — cosmétique, aucun impact sur les stats
    public float  ratioMelee;
    public float  ratioRanged;
    public float  ratioMagic;
    public float  ratioDodge;
    // TODO : rune insérée — idem Weapon.
}

[System.Serializable]
public class SavedHelmet
{
    public string soName;
    public bool   isEquipped;
    public float  ratioMelee;
    public float  ratioRanged;
    public float  ratioMagic;
}

[System.Serializable]
public class SavedGloves
{
    public string soName;
    public bool   isEquipped;
    public float  ratioMelee;
    public float  ratioRanged;
    public float  ratioMagic;

    // Fusion (GDD §5.6) — irréversible en jeu, perdue au reload sans ces champs.
    public int    fusionLevel;
    public float  resistFire;
    public float  resistWater;
    public float  resistLightning;
    public float  resistEarth;
    public float  resistNature;
    public float  resistDarkness;
    public float  resistLight;
}

[System.Serializable]
public class SavedBoots
{
    public string soName;
    public bool   isEquipped;
    public float  ratioMelee;
    public float  ratioRanged;
    public float  ratioMagic;

    // Fusion (GDD §5.6) — irréversible en jeu, perdue au reload sans ces champs.
    public int    fusionLevel;
    public float  resistFire;
    public float  resistWater;
    public float  resistLightning;
    public float  resistEarth;
    public float  resistNature;
    public float  resistDarkness;
    public float  resistLight;
}

[System.Serializable]
public class SavedJewelry
{
    public string soName;
    public bool   isEquipped;
    public string jewelrySlot;
    public float  ratioMelee;
    public float  ratioRanged;
    public float  ratioMagic;
    // TODO : gemmes insérées — pas encore persistées (gap connu, hors scope de ce fix).
}

[System.Serializable]
public class SavedSpirit
{
    public string soName;
    public bool   isEquipped;
    public int    spiritLevel;
    public int    spiritXP;
}

[System.Serializable]
public class SavedConsumable
{
    public string soName;
    public int    quantity;
}

[System.Serializable]
public class SavedResource
{
    public string soName;
    public int    quantity;
}

[System.Serializable]
public class SavedGem
{
    public string soName;
    // TODO : stat révélée — pas encore persistée (Rune/Gem hors scope, roll déjà
    // re-tiré à chaque chargement, voir a implémenter/note-rune-gem-hors-scope.md).
}

[System.Serializable]
public class SavedRune
{
    public string soName;
    // TODO : idem Gem.
}

[System.Serializable]
public class SavedCosmeticHead
{
    public string soName;
}

[System.Serializable]
public class SavedCosmeticBody
{
    public string soName;
}

[System.Serializable]
public class SavedCard
{
    public string soName;
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
    public string rewardEquipmentName;      // nom du SO générique (WeaponData, ArmorData...)
    public string rewardResourceName;       // nom du SO ResourceData
    public int    rewardResourceQuantity;
    public string rewardConsumableName;     // nom du SO ConsumableData
    public int    rewardConsumableQuantity;
    public string rewardPetID;              // string ID — SO à venir
    public string rewardRecipeID;           // string ID — SO à venir
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

    // ④bis StatPoints (§3.2.1) — rangs investis + pool de points
    public int spRankAttack    = 0;
    public int spRankDefense   = 0;
    public int spRankElemental = 0;
    public int spRankHP        = 0;
    public int spAvailable     = 0;
    public int spTotalEarned   = 0;

    // ⑤ Équipements + Inventaire — une liste par catégorie (voir note en tête de
    // fichier sur les SavedXxx : JsonUtility ne permet pas de liste polymorphe).
    public List<SavedWeapon>       weapons        = new List<SavedWeapon>();
    public List<SavedArmor>        armors         = new List<SavedArmor>();
    public List<SavedHelmet>       helmets        = new List<SavedHelmet>();
    public List<SavedGloves>       gloves         = new List<SavedGloves>();
    public List<SavedBoots>        boots          = new List<SavedBoots>();
    public List<SavedJewelry>      jewelry        = new List<SavedJewelry>();
    public List<SavedSpirit>       spirits        = new List<SavedSpirit>();
    public List<SavedConsumable>   consumables    = new List<SavedConsumable>();
    public List<SavedResource>     resources      = new List<SavedResource>();
    public List<SavedGem>          gems           = new List<SavedGem>();
    public List<SavedRune>         runes          = new List<SavedRune>();
    public List<SavedCosmeticHead> cosmeticHeads  = new List<SavedCosmeticHead>();
    public List<SavedCosmeticBody> cosmeticBodies = new List<SavedCosmeticBody>();
    public List<SavedCard>         cards          = new List<SavedCard>();

    // ⑥ Skills débloqués
    public List<string> unlockedSkillNames      = new List<string>();
    public List<string> unlockedPermanentNames  = new List<string>(); // PermanentSkillData
    public List<string> unlockedPassiveNames    = new List<string>(); // PassiveSkillData

    // ⑦ Slots SkillBar (0–9)
    public List<SavedSkillSlot> skillBarSlots = new List<SavedSkillSlot>();

    // ⑦Bis Slots PassifBar (P1-P3) — les 3 PassiveSkillData réellement équipés,
    // distinct de unlockedPassiveNames (le pool possédé, voir ⑥)
    public List<SavedSkillSlot> passifBarSlots = new List<SavedSkillSlot>();

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