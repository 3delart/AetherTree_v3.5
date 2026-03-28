using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

// =============================================================
// SAVESYSTEM — Sauvegarde complète sur fichier JSON
// Path : Assets/Scripts/Progression/Save/SaveSystem.cs
// AetherTree GDD v31
//
// Fichier : [ProjetUnity]/Saves/save_slot0.json
// =============================================================

public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    private const string SAVE_FILENAME = "save_slot0.json";
    private const float  LOAD_DELAY    = 0.15f;

    [Header("Autosave")]
    [Tooltip("Intervalle de sauvegarde automatique en secondes. 0 = désactivé.")]
    public float autosaveInterval = 30f;

    private float _autosaveTimer = 0f;
    private bool  _isFirstLoad   = true; // évite de sauvegarder avant le premier chargement

    private string SavePath
    {
        get
        {
            string dir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Saves");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, SAVE_FILENAME);
        }
    }

    private Vector3 _pendingPosition;
    private Player  _pendingPlayer;
    private bool    _hasPendingPosition;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (HasSave())
            Invoke(nameof(LoadAfterInit), LOAD_DELAY);
        else
            _isFirstLoad = false; // nouveau personnage, autosave autorisé immédiatement
    }

    private void LoadAfterInit()
    {
        var player = FindObjectOfType<Player>();
        if (player != null) Load(player);
        else Debug.LogWarning("[SAVE] LoadAfterInit : aucun Player trouvé.");

        _isFirstLoad = false; // autorise l'autosave à partir de maintenant
    }


    private void Update()
    {
        // Pas d'autosave avant que le chargement initial soit terminé
        if (_isFirstLoad) return;

        _autosaveTimer += Time.deltaTime;
        if (_autosaveTimer >= autosaveInterval && autosaveInterval > 0f)
        {
            _autosaveTimer = 0f;
            var player = FindObjectOfType<Player>();
            if (player != null)
            {
                Save(player);
                Debug.Log("[SAVE] 💾 Autosave");
            }
        }
    }

    private void OnApplicationQuit()
    {
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player);
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player);
    }
#endif

    // =========================================================
    // SAVE
    // =========================================================

    public void Save(Player player)
    {
        if (player == null) return;

        var    progress = CollectProgress(player);
        string json     = JsonUtility.ToJson(progress, prettyPrint: true);

        try
        {
            File.WriteAllText(SavePath, json);
            Debug.Log($"[SAVE] ✅ Sauvegardé → {SavePath}\n" +
                      $"Niv.{progress.level} | XP:{progress.xpCombat} | " +
                      $"Items:{progress.items.Count} | Quêtes:{progress.quests.Count} | " +
                      $"Mails:{progress.mails.Count} | Aeris:{progress.aeris}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SAVE] ❌ Erreur écriture : {e.Message}");
        }
    }

    // =========================================================
    // LOAD
    // =========================================================

    public void Load(Player player)
    {
        if (player == null || !HasSave()) return;

        try
        {
            string json     = File.ReadAllText(SavePath);
            var    progress = JsonUtility.FromJson<CharacterProgress>(json);

            if (progress == null)
            {
                Debug.LogWarning("[LOAD] JSON invalide — sauvegarde ignorée.");
                return;
            }

            ApplyProgress(player, progress);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOAD] ❌ Erreur lecture : {e.Message}");
        }
    }

    public bool HasSave() => File.Exists(SavePath);

    public void DeleteSave()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log("[SAVE] 🗑 Sauvegarde supprimée.");
        }
    }

    // =========================================================
    // COLLECTE — Player → CharacterProgress
    // =========================================================

    private CharacterProgress CollectProgress(Player player)
    {
        var progress = new CharacterProgress
        {
            characterName   = player.entityName,
            level           = player.level,
            xpCombat        = player.xpCombat,
            activeTitle     = player.activeTitle,
            worldReputation = player.worldReputation,
            pvpReputation   = player.pvpReputation,
            lastMap         = SceneLoader.Instance?.CurrentMap ?? "Map_01",
            posX            = player.transform.position.x,
            posY            = player.transform.position.y,
            posZ            = player.transform.position.z,
            aeris           = AerisSystem.Instance?.Aeris ?? 0,
        };

        // ⑧ Conditions débloquées
        if (UnlockManager.Instance != null)
            progress.unlockedConditionIDs = UnlockManager.Instance.GetUnlocked();

        // ⑫ Progression conditions en cours
        if (UnlockManager.Instance != null)
            progress.conditionProgresses = UnlockManager.Instance.GetConditionProgresses();

        // ⑨ Compteurs activité
        var counter = player.GetActivityCounter();
        if (counter != null)
            foreach (var kv in counter.GetAll())
                progress.activityCountersList.Add(new StringIntPair(kv.Key, kv.Value));

        // ⑥ Skills débloqués
        foreach (var skill in player.unlockedSkills)
            if (skill != null) progress.unlockedSkillNames.Add(skill.name);

        // ⑥ Permanents débloqués
        if (player.unlockedPermanents != null)
            foreach (var perm in player.unlockedPermanents)
                if (perm != null) progress.unlockedPermanentNames.Add(perm.name);

        // ⑥ Passifs débloqués
        if (player.unlockedPassives != null)
            foreach (var passive in player.unlockedPassives)
                if (passive != null) progress.unlockedPassiveNames.Add(passive.name);

        // ⑦ SkillBar
        if (SkillBar.Instance != null)
            for (int i = 0; i < 10; i++)
            {
                var skill = SkillBar.Instance.GetSkillAtSlot(i);
                if (skill != null)
                    progress.skillBarSlots.Add(new SavedSkillSlot { slotIndex = i, skillName = skill.name });
            }

        // ⑤ Équipements portés
        CollectEquipped(progress, player);

        // ⑤ Inventaire
        if (InventorySystem.Instance != null)
            foreach (var item in InventorySystem.Instance.GetAllItems())
                CollectInventoryItem(progress, item);

        // ⑩ Quêtes
        if (QuestSystem.Instance != null)
            progress.quests = CollectQuests();

        // ⑪ Jauge élémentaire
        var elemental = player.GetElementalSystem();
        if (elemental != null)
            CollectElementalAffinities(progress, elemental);

        // ⑬ Mails
        if (MailboxSystem.Instance != null)
            CollectMails(progress);

        return progress;
    }

    // ── Équipements portés ────────────────────────────────────

    private void CollectEquipped(CharacterProgress p, Player player)
    {
        if (player.equippedWeaponInstance?.data != null)
            p.items.Add(new SavedItem {
                category = "Weapon", soName = player.equippedWeaponInstance.data.name,
                rarityRank = player.equippedWeaponInstance.rarityRank,
                upgradeLevel = player.equippedWeaponInstance.upgradeLevel,
                isEquipped = true, slot = "Weapon" });

        if (player.equippedArmorInstance?.data != null)
            p.items.Add(new SavedItem {
                category = "Armor", soName = player.equippedArmorInstance.data.name,
                rarityRank = player.equippedArmorInstance.rarityRank,
                upgradeLevel = player.equippedArmorInstance.upgradeLevel,
                isEquipped = true, slot = "Armor" });

        if (player.equippedHelmetInstance?.data != null)
            p.items.Add(new SavedItem {
                category = "Helmet", soName = player.equippedHelmetInstance.data.name,
                isEquipped = true, slot = "Helmet" });

        if (player.equippedGlovesInstance?.data != null)
            p.items.Add(new SavedItem {
                category = "Gloves", soName = player.equippedGlovesInstance.data.name,
                isEquipped = true, slot = "Gloves" });

        if (player.equippedBootsInstance?.data != null)
            p.items.Add(new SavedItem {
                category = "Boots", soName = player.equippedBootsInstance.data.name,
                isEquipped = true, slot = "Boots" });

        if (player.equippedJewelryInstances != null)
            foreach (var j in player.equippedJewelryInstances)
                if (j?.data != null)
                    p.items.Add(new SavedItem {
                        category = "Jewelry", soName = j.data.name,
                        jewelrySlot = j.Slot.ToString(),
                        isEquipped = true, slot = j.Slot.ToString() });

        if (player.equippedSpiritInstances != null)
            foreach (var s in player.equippedSpiritInstances)
                if (s?.data != null)
                    p.items.Add(new SavedItem {
                        category = "Spirit", soName = s.data.name,
                        spiritLevel = s.level, spiritXP = s.currentXP,
                        isEquipped = true, slot = "Spirit" });
    }

    // ── Item inventaire ───────────────────────────────────────

    private void CollectInventoryItem(CharacterProgress p, InventoryItem item)
    {
        if (item == null) return;
        SavedItem saved = null;

        if      (item.WeaponInstance?.data     != null)
            saved = new SavedItem { category = "Weapon",     soName = item.WeaponInstance.data.name,
                                    rarityRank = item.WeaponInstance.rarityRank,
                                    upgradeLevel = item.WeaponInstance.upgradeLevel };
        else if (item.ArmorInstance?.data      != null)
            saved = new SavedItem { category = "Armor",      soName = item.ArmorInstance.data.name,
                                    rarityRank = item.ArmorInstance.rarityRank,
                                    upgradeLevel = item.ArmorInstance.upgradeLevel };
        else if (item.HelmetInstance?.data     != null)
            saved = new SavedItem { category = "Helmet",     soName = item.HelmetInstance.data.name };
        else if (item.GlovesInstance?.data     != null)
            saved = new SavedItem { category = "Gloves",     soName = item.GlovesInstance.data.name };
        else if (item.BootsInstance?.data      != null)
            saved = new SavedItem { category = "Boots",      soName = item.BootsInstance.data.name };
        else if (item.JewelryInstance?.data    != null)
            saved = new SavedItem { category = "Jewelry",    soName = item.JewelryInstance.data.name,
                                    jewelrySlot = item.JewelryInstance.Slot.ToString() };
        else if (item.SpiritInstance?.data     != null)
            saved = new SavedItem { category = "Spirit",     soName = item.SpiritInstance.data.name,
                                    spiritLevel = item.SpiritInstance.level,
                                    spiritXP    = item.SpiritInstance.currentXP };
        else if (item.ConsumableInstance?.data != null)
            saved = new SavedItem { category = "Consumable", soName = item.ConsumableInstance.data.name,
                                    quantity = item.ConsumableInstance.quantity };
        else if (item.ResourceInstance?.data   != null)
            saved = new SavedItem { category = "Resource",   soName = item.ResourceInstance.data.name,
                                    quantity = item.ResourceInstance.quantity };
        else if (item.GemInstance?.data        != null)
            saved = new SavedItem { category = "Gem",        soName = item.GemInstance.data.name };
        else if (item.RuneInstance?.data       != null)
            saved = new SavedItem { category = "Rune",       soName = item.RuneInstance.data.name };
        else if (item.CosmeticInstanceHead?.data != null)
            saved = new SavedItem { category = "CosmeticHead", soName = item.CosmeticInstanceHead.data.name };
        else if (item.CosmeticInstanceBody?.data != null)
            saved = new SavedItem { category = "CosmeticBody", soName = item.CosmeticInstanceBody.data.name };
        else if (item.CardInstance?.data         != null)
            saved = new SavedItem { category = "Card",          soName = item.CardInstance.data.name };

        if (saved != null) { saved.isEquipped = false; p.items.Add(saved); }
    }

    // ── Quêtes ────────────────────────────────────────────────

    private List<SavedQuest> CollectQuests()
    {
        var entries = QuestSystem.Instance.GetSaveData();
        var result  = new List<SavedQuest>();
        foreach (var e in entries)
            result.Add(new SavedQuest {
                questID = e.questID, state = e.state,
                objectiveCounts = e.objectiveCounts });
        return result;
    }

    // ── Jauge élémentaire ─────────────────────────────────────

    private void CollectElementalAffinities(CharacterProgress p, ElementalSystem elemental)
    {
        foreach (ElementType t in System.Enum.GetValues(typeof(ElementType)))
        {
            if (t == ElementType.Any) continue;
            float aff = elemental.GetAffinity(t);
            if (aff > 0.0001f)
                p.elementAffinities.Add(new SavedElementAffinity {
                    element = t.ToString(),
                    weight  = aff * elemental.GetWindowSize() });
        }
    }

    // ── Mails ─────────────────────────────────────────────────

    private void CollectMails(CharacterProgress p)
    {
        var mails = MailboxSystem.Instance.GetAllMails();
        Debug.Log($"[SAVE] CollectMails — {mails?.Count ?? 0} mails trouvés");
        if (mails == null) return;

        foreach (var mail in mails)
        {
            if (mail == null) continue;

            var saved = new SavedMail
            {
                mailID        = mail.mailID,
                senderName    = mail.senderName,
                isFromServer  = mail.isFromServer,
                sentAt        = mail.sentAt.ToString("o"), // format ISO 8601
                subject       = mail.subject,
                body          = mail.body,
                isRead        = mail.isRead,
                rewardClaimed = mail.rewardClaimed,
                hasReward     = mail.HasReward,
            };

            if (mail.reward != null)
            {
                saved.rewardType         = (int)mail.reward.rewardType;
                saved.rewardSkillName    = mail.reward.rewardSkill?.name ?? "";
                saved.rewardTitle        = mail.reward.rewardTitle       ?? "";
                saved.rewardItemID       = mail.reward.rewardItemID      ?? "";
                saved.rewardItemQuantity = mail.reward.rewardItemQuantity;
                saved.rewardRecipeID     = mail.reward.rewardRecipeID    ?? "";
                saved.rewardDescription  = mail.reward.rewardDescription ?? "";
            }

            p.mails.Add(saved);
        }
    }

    // =========================================================
    // APPLICATION — CharacterProgress → Player
    // =========================================================

    private void ApplyProgress(Player player, CharacterProgress p)
    {
        // ① Niveau & XP
        if (p.level > 1) player.OnLevelUp(p.level);
        player.xpCombat    = p.xpCombat;
        player.activeTitle = p.activeTitle;

        // ② Réputation
        player.AddWorldReputation(p.worldReputation - player.worldReputation);
        player.AddPvPReputation(p.pvpReputation   - player.pvpReputation);

        // ④ Aeris
        if (AerisSystem.Instance != null)
        {
            int delta = p.aeris - AerisSystem.Instance.Aeris;
            if (delta > 0) AerisSystem.Instance.Add(delta);
        }

        // ⑧ Conditions débloquées
        UnlockManager.Instance?.LoadUnlocked(p.unlockedConditionIDs);

        // ⑫ Progression conditions en cours
        if (p.conditionProgresses != null)
            UnlockManager.Instance?.LoadConditionProgresses(p.conditionProgresses);

        // ⑨ Compteurs activité
        var counter = player.GetActivityCounter();
        if (counter != null)
        {
            var dict = new Dictionary<string, int>();
            foreach (var pair in p.activityCountersList)
                dict[pair.key] = pair.value;
            counter.LoadFromSave(dict);
        }

        // ⑥ Skills débloqués
        foreach (var skillName in p.unlockedSkillNames)
        {
            var skill = FindSOByName<SkillData>(skillName);
            if (skill != null) player.UnlockSkill(skill);
        }

        // ⑥ Permanents débloqués
        if (p.unlockedPermanentNames != null)
            foreach (var name in p.unlockedPermanentNames)
            {
                var perm = FindSOByName<PermanentSkillData>(name);
                if (perm != null) player.UnlockPermanent(perm);
            }

        // ⑥ Passifs débloqués
        if (p.unlockedPassiveNames != null)
            foreach (var name in p.unlockedPassiveNames)
            {
                var passive = FindSOByName<PassiveSkillData>(name);
                if (passive != null) player.UnlockPassive(passive);
            }

        // ⑦ SkillBar
        if (SkillBar.Instance != null)
            foreach (var savedSlot in p.skillBarSlots)
            {
                var skill = FindSOByName<SkillData>(savedSlot.skillName);
                if (skill != null) SkillBar.Instance.SetSkillAtSlot(savedSlot.slotIndex, skill);
            }

        // ⑬ Mails — restaurés avant les items pour que les skills soient
        // disponibles si le joueur clique "Récupérer" immédiatement
        RestoreMails(p.mails);

        // ⑤ Items — chargés en coroutine
        StartCoroutine(LoadItemsDelayed(player, p));
    }

    // ── Restauration mails ────────────────────────────────────

    private void RestoreMails(List<SavedMail> savedMails)
    {
        if (savedMails == null || savedMails.Count == 0) return;
        if (MailboxSystem.Instance == null) return;

        foreach (var saved in savedMails)
        {
            if (saved == null || string.IsNullOrEmpty(saved.mailID)) continue;

            // Reconstruit la récompense
            MailReward reward = null;
            if (saved.hasReward)
            {
                reward = new MailReward
                {
                    rewardType         = (RewardType)saved.rewardType,
                    rewardTitle        = saved.rewardTitle,
                    rewardItemID       = saved.rewardItemID,
                    rewardItemQuantity = saved.rewardItemQuantity,
                    rewardRecipeID     = saved.rewardRecipeID,
                    rewardDescription  = saved.rewardDescription,
                };

                // Retrouve le SkillData par nom si nécessaire
                if (!string.IsNullOrEmpty(saved.rewardSkillName))
                    reward.rewardSkill = FindSOByName<SkillData>(saved.rewardSkillName);
            }

            // Reconstruit le DateTime
            DateTime sentAt = DateTime.Now;
            if (!string.IsNullOrEmpty(saved.sentAt))
                DateTime.TryParse(saved.sentAt, out sentAt);

            var mail = new MailMessage
            {
                mailID        = saved.mailID,
                senderName    = saved.senderName,
                isFromServer  = saved.isFromServer,
                sentAt        = sentAt,
                subject       = saved.subject,
                body          = saved.body,
                isRead        = saved.isRead,
                rewardClaimed = saved.rewardClaimed,
                reward        = reward,
            };

            MailboxSystem.Instance.RestoreMail(mail);
        }

        Debug.Log($"[LOAD] ✅ {savedMails.Count} mail(s) restauré(s).");
    }

    // ── Restauration items ────────────────────────────────────

    private IEnumerator LoadItemsDelayed(Player player, CharacterProgress p)
    {
        yield return null;

        InventorySystem.Instance?.UnequipAll(player);
        InventorySystem.Instance?.ClearAll();

        foreach (var saved in p.items)
            RestoreItem(player, saved);

        // ⑩ Quêtes
        if (QuestSystem.Instance != null && p.quests != null && p.quests.Count > 0)
        {
            var allQuests = FindAllQuests();
            var entries   = new List<QuestSaveEntry>();
            foreach (var q in p.quests)
                entries.Add(new QuestSaveEntry {
                    questID = q.questID, state = q.state,
                    objectiveCounts = q.objectiveCounts });
            QuestSystem.Instance.LoadSaveData(entries, allQuests);
        }

        // ⑪ Jauge élémentaire
        var elemental = player.GetElementalSystem();
        if (elemental != null && p.elementAffinities != null && p.elementAffinities.Count > 0)
            elemental.LoadAffinities(p.elementAffinities);

        // Position
        if (SceneLoader.Instance != null && !string.IsNullOrEmpty(p.lastMap)
            && p.lastMap != SceneLoader.Instance.CurrentMap)
        {
            _pendingPosition    = new Vector3(p.posX, p.posY, p.posZ);
            _pendingPlayer      = player;
            _hasPendingPosition = true;
            SceneLoader.OnMapLoaded += OnTargetMapLoaded;
            SceneLoader.Instance.LoadMap(p.lastMap);
        }
        else
        {
            RepositionPlayer(player, p);
        }

        // Refresh UI
        GameEventBus.Publish(new StatsChangedEvent { player = player });
        InventoryUI.Instance?.RefreshGrid();
        CharacterPanelUI.Instance?.Refresh();

        Debug.Log($"[LOAD] ✅ Items:{p.items.Count} | Quêtes:{p.quests?.Count ?? 0} | " +
                  $"Mails:{p.mails?.Count ?? 0}");
    }

    private void RestoreItem(Player player, SavedItem saved)
    {
        var item = CreateItemFromSave(saved);
        if (item == null)
        {
            Debug.LogWarning($"[LOAD] ⚠ Item introuvable : {saved.category} '{saved.soName}'");
            return;
        }
        if (saved.isEquipped) InventorySystem.Instance?.EquipItem(item, player);
        else                  InventorySystem.Instance?.AddItem(item);
    }

    private InventoryItem CreateItemFromSave(SavedItem saved)
    {
        switch (saved.category)
        {
            case "Weapon":
                var wd = FindSOByName<WeaponData>(saved.soName);
                return wd != null ? new InventoryItem(wd.CreateDropInstance(saved.rarityRank, saved.upgradeLevel)) : null;
            case "Armor":
                var ad = FindSOByName<ArmorData>(saved.soName);
                return ad != null ? new InventoryItem(ad.CreateDropInstance(saved.rarityRank, saved.upgradeLevel)) : null;
            case "Helmet":
                var hd = FindSOByName<HelmetData>(saved.soName);
                return hd != null ? new InventoryItem(hd.CreateInstance()) : null;
            case "Gloves":
                var gd = FindSOByName<GlovesData>(saved.soName);
                return gd != null ? new InventoryItem(gd.CreateInstance()) : null;
            case "Boots":
                var bd = FindSOByName<BootsData>(saved.soName);
                return bd != null ? new InventoryItem(bd.CreateInstance()) : null;
            case "Jewelry":
                var jd = FindSOByName<JewelryData>(saved.soName);
                return jd != null ? new InventoryItem(jd.CreateInstance()) : null;
            case "Spirit":
                var sd = FindSOByName<SpiritData>(saved.soName);
                if (sd == null) return null;
                var si = new SpiritInstance(sd);
                si.level = Mathf.Max(1, saved.spiritLevel);
                si.currentXP = Mathf.Max(0, saved.spiritXP);
                return new InventoryItem(si);
            case "Consumable":
                var cd = FindSOByName<ConsumableData>(saved.soName);
                return cd != null ? new InventoryItem(cd.CreateInstance(Mathf.Max(1, saved.quantity))) : null;
            case "Resource":
                var rd = FindSOByName<ResourceData>(saved.soName);
                return rd != null ? new InventoryItem(rd.CreateInstance(Mathf.Max(1, saved.quantity))) : null;
            case "Gem":
                var gemD = FindSOByName<GemData>(saved.soName);
                return gemD != null ? new InventoryItem(gemD.CreateDropInstance()) : null;
            case "Rune":
                var runeD = FindSOByName<RuneData>(saved.soName);
                return runeD != null ? new InventoryItem(runeD.CreateDropInstance()) : null;
            case "CosmeticHead":
                var cosH = FindSOByName<CosmeticDataHead>(saved.soName);
                return cosH != null ? new InventoryItem(new CosmeticInstanceHead(cosH)) : null;
            case "CosmeticBody":
                var cosB = FindSOByName<CosmeticDataBody>(saved.soName);
                return cosB != null ? new InventoryItem(new CosmeticInstanceBody(cosB)) : null;
            case "Card":
                var card = FindSOByName<CardData>(saved.soName);
                return card != null ? new InventoryItem(new CardInstance(card)) : null;
            default:
                Debug.LogWarning($"[LOAD] Catégorie inconnue : '{saved.category}'");
                return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private List<QuestData> FindAllQuests()
    {
        var result = new List<QuestData>();
        foreach (var q in Resources.FindObjectsOfTypeAll<QuestData>())
            if (q != null) result.Add(q);
        return result;
    }

    private void RepositionPlayer(Player player, CharacterProgress p)
    {
        var pos   = new Vector3(p.posX, p.posY, p.posZ);
        var agent = player.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.Warp(pos);
        else player.transform.position = pos;
    }

    private void OnTargetMapLoaded(string mapName)
    {
        SceneLoader.OnMapLoaded -= OnTargetMapLoaded;
        if (!_hasPendingPosition || _pendingPlayer == null) return;
        var agent = _pendingPlayer.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.Warp(_pendingPosition);
        else _pendingPlayer.transform.position = _pendingPosition;
        _hasPendingPosition = false;
        _pendingPlayer      = null;
    }

    private T FindSOByName<T>(string soName) where T : ScriptableObject
    {
        if (string.IsNullOrEmpty(soName)) return null;
        foreach (var so in Resources.FindObjectsOfTypeAll<T>())
            if (so.name == soName) return so;
        Debug.LogWarning($"[LOAD] SO introuvable : {typeof(T).Name} '{soName}'");
        return null;
    }
}