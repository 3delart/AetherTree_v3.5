using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

// =============================================================
// SAVESYSTEM — Sauvegarde complète sur fichier JSON
// Path : Assets/_Game/Scripts/Progression/Save/SaveSystem.cs
// AetherTree GDD v31
//
// Deux fichiers distincts :
//   character_[nom].json  → données du personnage actif
//   account.json          → données cumulées du compte
//
// En v3.5 (solo) : account.json est écrit en même temps que
// character.json. En v4, account.json deviendra une sync serveur.
//
// Flux Save  : Save(player) → CollectProgress() + SaveAccount()
// Flux Load  : Load(player) → ApplyProgress()  + LoadAccount()
// =============================================================

public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    // Nom du fichier perso — en v4 : "character_[characterName].json"
    private const string SAVE_FILENAME    = "save_slot0.json";
    private const string ACCOUNT_FILENAME = "account.json";
    private const float  LOAD_DELAY       = 0.15f;

    [Header("Autosave")]
    [Tooltip("Intervalle de sauvegarde automatique en secondes. 0 = désactivé.")]
    public float autosaveInterval = 30f;

    private float _autosaveTimer = 0f;
    private bool  _isFirstLoad   = true;

    // ── Chemins ───────────────────────────────────────────────

    private string SaveDir
    {
        get
        {
            string dir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Saves");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private string CharacterSavePath => Path.Combine(SaveDir, SAVE_FILENAME);
    private string AccountSavePath   => Path.Combine(SaveDir, ACCOUNT_FILENAME);

    private Vector3 _pendingPosition;
    private Player  _pendingPlayer;
    private bool    _hasPendingPosition;

    // =========================================================
    // LIFECYCLE
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Charge le compte en premier (compteurs cross-perso disponibles
        // avant que UnlockManager commence à évaluer les conditions)
        LoadAccount();

        if (HasSave())
            Invoke(nameof(LoadAfterInit), LOAD_DELAY);
        else
            _isFirstLoad = false;
    }

    private void LoadAfterInit()
    {
        var player = FindObjectOfType<Player>();
        if (player != null) Load(player);
        else Debug.LogWarning("[SAVE] LoadAfterInit : aucun Player trouvé.");

        _isFirstLoad = false;
    }

    private void Update()
    {
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
        // ⚠ Même garde que Update() — si le chargement initial (Invoke + coroutine
        // LoadItemsDelayed) n'a pas fini, le Player est encore quasi vide (défauts
        // d'Awake). Sauvegarder à ce moment-là écraserait la vraie save avec un perso
        // vide. Skip plutôt que de risquer une perte de progression.
        if (_isFirstLoad)
        {
            Debug.LogWarning("[SAVE] Quit avant la fin du chargement initial — sauvegarde ignorée pour ne pas écraser la save.");
            return;
        }

        // Flush explicite AVANT Save() — l'ordre d'OnApplicationQuit entre composants n'est
        // pas garanti par Unity, donc on ne peut pas compter sur le OnApplicationQuit propre
        // de ZoneTrigger pour avoir déjà tourné à ce stade.
        foreach (var zt in FindObjectsOfType<ZoneTrigger>())
            zt.FlushOnQuit();

        var player = FindObjectOfType<Player>();
        if (player != null) Save(player);
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
        // ⚠ Même garde — OnDisable() se déclenche à CHAQUE arrêt du Play Mode ET à
        // chaque recompilation de script pendant le Play Mode (domain reload). Sans ce
        // garde, un arrêt/recompile survenant avant la fin du chargement initial écrase
        // la vraie save avec un Player encore aux valeurs par défaut — c'est la cause
        // du bug "la save repart à 0" régulièrement pendant une session de dev/test.
        if (_isFirstLoad) return;

        var player = FindObjectOfType<Player>();
        if (player != null) Save(player);
    }
#endif

    // =========================================================
    // SAVE PERSONNAGE
    // =========================================================

    public void Save(Player player)
    {
        if (player == null) return;

        var    progress = CollectProgress(player);
        string json     = JsonUtility.ToJson(progress, prettyPrint: true);

        try
        {
            File.WriteAllText(CharacterSavePath, json);
            int itemCount = progress.weapons.Count + progress.armors.Count + progress.helmets.Count +
                            progress.gloves.Count + progress.boots.Count + progress.jewelry.Count +
                            progress.spirits.Count + progress.consumables.Count + progress.resources.Count +
                            progress.gems.Count + progress.runes.Count + progress.cosmeticHeads.Count +
                            progress.cosmeticBodies.Count + progress.talismans.Count;
            Debug.Log($"[SAVE] ✅ Personnage → {CharacterSavePath}\n" +
                      $"Niv.{progress.level} | XP:{progress.xpCombat} | " +
                      $"Items:{itemCount} | Quêtes:{progress.quests.Count} | " +
                      $"Mails:{progress.mails.Count} | Aeris:{progress.aeris}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SAVE] ❌ Erreur écriture personnage : {e.Message}");
        }

        // Sauvegarde compte en même temps
        SaveAccount();
    }

    // =========================================================
    // SAVE COMPTE
    // =========================================================

    public void SaveAccount()
    {
        var account = new AccountProgress
        {
            accountID = FindObjectOfType<Player>()?.entityName ?? "default"
        };

        // Demande à UnlockManager de remplir les données compte
        UnlockManager.Instance?.CollectAccountProgress(account);

        string json = JsonUtility.ToJson(account, prettyPrint: true);

        try
        {
            File.WriteAllText(AccountSavePath, json);
            Debug.Log($"[SAVE] ✅ Compte → {AccountSavePath} " +
                      $"({account.accountCountersList.Count} compteurs, " +
                      $"{account.unlockedAccountConditionIDs.Count} conditions compte)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SAVE] ❌ Erreur écriture compte : {e.Message}");
        }
    }

    // =========================================================
    // LOAD PERSONNAGE
    // =========================================================

    public void Load(Player player)
    {
        if (player == null || !HasSave()) return;

        try
        {
            string json     = File.ReadAllText(CharacterSavePath);
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
            Debug.LogError($"[LOAD] ❌ Erreur lecture personnage : {e.Message}");
        }
    }

    // =========================================================
    // LOAD COMPTE
    // =========================================================

    public void LoadAccount()
    {
        if (!File.Exists(AccountSavePath))
        {
            Debug.Log("[LOAD] account.json introuvable — nouveau compte, compteurs à zéro.");
            return;
        }

        try
        {
            string json    = File.ReadAllText(AccountSavePath);
            var    account = JsonUtility.FromJson<AccountProgress>(json);

            if (account == null)
            {
                Debug.LogWarning("[LOAD] account.json invalide — ignoré.");
                return;
            }

            // Restaure compteurs et conditions compte dans UnlockManager
            UnlockManager.Instance?.LoadAccountProgress(account);

            Debug.Log($"[LOAD] ✅ Compte chargé — " +
                      $"{account.accountCountersList?.Count ?? 0} compteurs, " +
                      $"{account.unlockedAccountConditionIDs?.Count ?? 0} conditions compte.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOAD] ❌ Erreur lecture compte : {e.Message}");
        }
    }

    public bool HasSave()        => File.Exists(CharacterSavePath);
    public bool HasAccountSave() => File.Exists(AccountSavePath);

    public void DeleteSave()
    {
        if (File.Exists(CharacterSavePath))
        {
            File.Delete(CharacterSavePath);
            Debug.Log("[SAVE] 🗑 Sauvegarde personnage supprimée.");
        }
    }

    public void DeleteAccount()
    {
        if (File.Exists(AccountSavePath))
        {
            File.Delete(AccountSavePath);
            Debug.Log("[SAVE] 🗑 Sauvegarde compte supprimée.");
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

            // StatPoints (§3.2.1) — rangs investis + pool
            spRankAttack    = player.statPoints?.rankAttack        ?? 0,
            spRankDefense   = player.statPoints?.rankDefense       ?? 0,
            spRankElemental = player.statPoints?.rankElemental     ?? 0,
            spRankHP        = player.statPoints?.rankHP            ?? 0,
            spAvailable     = player.statPoints?.availablePoints   ?? 0,
            spTotalEarned   = player.statPoints?.totalPointsEarned ?? 0,
        };

        // ⑧ Conditions débloquées (scope Character uniquement)
        if (UnlockManager.Instance != null)
            progress.unlockedConditionIDs = UnlockManager.Instance.GetUnlocked()
                .Where(id =>
                {
                    var cond = allConditions?.FirstOrDefault(c => c?.conditionID == id);
                    return cond == null || cond.GetDominantScope() == CounterScope.Character;
                }).ToList();

        // ⑫ Progression conditions en cours (scope Character)
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

        if (player.unlockedPermanents != null)
            foreach (var perm in player.unlockedPermanents)
                if (perm != null) progress.unlockedPermanentNames.Add(perm.name);

        if (player.unlockedPassives != null)
            foreach (var passive in player.unlockedPassives)
                if (passive != null) progress.unlockedPassiveNames.Add(passive.name);

        if (player.unlockedRecipes != null)
            foreach (var recipe in player.unlockedRecipes)
                if (recipe != null) progress.unlockedRecipeNames.Add(recipe.name);

        // ⑦ SkillBar
        if (SkillBar.Instance != null)
            for (int i = 0; i < 10; i++)
            {
                var skill = SkillBar.Instance.GetSkillAtSlot(i);
                if (skill != null)
                    progress.skillBarSlots.Add(new SavedSkillSlot { slotIndex = i, skillName = skill.name });
            }

        // ⑦Bis PassifBar (3 slots équipés)
        if (player.equippedPassives != null)
            for (int i = 0; i < player.equippedPassives.Length; i++)
            {
                var passive = player.equippedPassives[i];
                if (passive != null)
                    progress.passifBarSlots.Add(new SavedSkillSlot { slotIndex = i, skillName = passive.name });
            }

        // ⑦Ter ConsoBar (3 slots)
        if (ConsoBarUI.Instance != null)
            for (int i = 0; i < 3; i++)
            {
                var conso = ConsoBarUI.Instance.GetSlotData(i);
                if (conso != null)
                    progress.consoBarSlots.Add(new SavedSkillSlot { slotIndex = i, skillName = conso.name });
            }

        // ⑤ Équipements + Inventaire
        CollectEquipped(progress, player);
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

    // Référence aux ConditionData pour filtrage scope dans CollectProgress
    // — récupérées via UnlockManager pour éviter un FindObjectsOfTypeAll ici.
    private List<ConditionData> allConditions =>
        UnlockManager.Instance?.allConditions;

    // ── Équipements portés ────────────────────────────────────

    private void CollectEquipped(CharacterProgress p, Player player)
    {
        if (player.equippedWeaponInstance?.data != null)
        {
            var w = player.equippedWeaponInstance;
            p.weapons.Add(new SavedWeapon {
                soName = w.data.name, isEquipped = true,
                rarityRank = w.rarityRank, upgradeLevel = w.upgradeLevel,
                hasAethernelleSeal = w.hasAethernelleSeal,
                ratioMin = w.rolledRatioMin, ratioMax = w.rolledRatioMax, ratioPrecision = w.rolledRatioPrecision });
        }

        if (player.equippedArmorInstance?.data != null)
        {
            var a = player.equippedArmorInstance;
            p.armors.Add(new SavedArmor {
                soName = a.data.name, isEquipped = true,
                rarityRank = a.rarityRank, upgradeLevel = a.upgradeLevel,
                hasAethernelleSeal = a.hasAethernelleSeal,
                ratioMelee = a.rolledRatioMelee, ratioRanged = a.rolledRatioRanged,
                ratioMagic = a.rolledRatioMagic, ratioDodge = a.rolledRatioDodge });
        }

        if (player.equippedHelmetInstance?.data != null)
        {
            var h = player.equippedHelmetInstance;
            p.helmets.Add(new SavedHelmet {
                soName = h.data.name, isEquipped = true,
                ratioMelee = h.rolledRatioMelee, ratioRanged = h.rolledRatioRanged, ratioMagic = h.rolledRatioMagic });
        }

        if (player.equippedGlovesInstance?.data != null)
        {
            var g = player.equippedGlovesInstance;
            p.gloves.Add(new SavedGloves {
                soName = g.data.name, isEquipped = true,
                ratioMelee = g.rolledRatioMelee, ratioRanged = g.rolledRatioRanged, ratioMagic = g.rolledRatioMagic,
                fusionLevel = g.fusionLevel,
                resistFire = g.resistFire, resistWater = g.resistWater, resistLightning = g.resistLightning,
                resistEarth = g.resistEarth, resistNature = g.resistNature, resistDarkness = g.resistDarkness, resistLight = g.resistLight });
        }

        if (player.equippedBootsInstance?.data != null)
        {
            var b = player.equippedBootsInstance;
            p.boots.Add(new SavedBoots {
                soName = b.data.name, isEquipped = true,
                ratioMelee = b.rolledRatioMelee, ratioRanged = b.rolledRatioRanged, ratioMagic = b.rolledRatioMagic,
                fusionLevel = b.fusionLevel,
                resistFire = b.resistFire, resistWater = b.resistWater, resistLightning = b.resistLightning,
                resistEarth = b.resistEarth, resistNature = b.resistNature, resistDarkness = b.resistDarkness, resistLight = b.resistLight });
        }

        if (player.equippedJewelryInstances != null)
            foreach (var j in player.equippedJewelryInstances)
                if (j?.data != null)
                    p.jewelry.Add(new SavedJewelry {
                        soName = j.data.name, isEquipped = true,
                        jewelrySlot = j.Slot.ToString(),
                        ratioMelee = j.rolledRatioMelee, ratioRanged = j.rolledRatioRanged, ratioMagic = j.rolledRatioMagic });

        if (player.equippedSpiritInstances != null)
            foreach (var s in player.equippedSpiritInstances)
                if (s?.data != null)
                    p.spirits.Add(new SavedSpirit {
                        soName = s.data.name, isEquipped = true,
                        spiritLevel = s.level, spiritXP = s.currentXP });

        if (player.equippedTalismanInstance?.data != null)
        {
            var t = player.equippedTalismanInstance;
            p.talismans.Add(new SavedTalisman {
                soName = t.data.name, isEquipped = true,
                activatedAt = t.activatedAt?.ToString("o") ?? "" });
        }
    }

    // ── Item inventaire ───────────────────────────────────────

    private void CollectInventoryItem(CharacterProgress p, InventoryItem item)
    {
        if (item == null) return;

        if      (item.WeaponInstance?.data     != null)
            p.weapons.Add(new SavedWeapon { soName = item.WeaponInstance.data.name,
                                    rarityRank = item.WeaponInstance.rarityRank,
                                    upgradeLevel = item.WeaponInstance.upgradeLevel,
                                    hasAethernelleSeal = item.WeaponInstance.hasAethernelleSeal,
                                    ratioMin = item.WeaponInstance.rolledRatioMin,
                                    ratioMax = item.WeaponInstance.rolledRatioMax,
                                    ratioPrecision = item.WeaponInstance.rolledRatioPrecision });
        else if (item.ArmorInstance?.data      != null)
            p.armors.Add(new SavedArmor { soName = item.ArmorInstance.data.name,
                                    rarityRank = item.ArmorInstance.rarityRank,
                                    upgradeLevel = item.ArmorInstance.upgradeLevel,
                                    hasAethernelleSeal = item.ArmorInstance.hasAethernelleSeal,
                                    ratioMelee = item.ArmorInstance.rolledRatioMelee,
                                    ratioRanged = item.ArmorInstance.rolledRatioRanged,
                                    ratioMagic = item.ArmorInstance.rolledRatioMagic,
                                    ratioDodge = item.ArmorInstance.rolledRatioDodge });
        else if (item.HelmetInstance?.data     != null)
            p.helmets.Add(new SavedHelmet { soName = item.HelmetInstance.data.name,
                                    ratioMelee = item.HelmetInstance.rolledRatioMelee,
                                    ratioRanged = item.HelmetInstance.rolledRatioRanged,
                                    ratioMagic = item.HelmetInstance.rolledRatioMagic });
        else if (item.GlovesInstance?.data     != null)
            p.gloves.Add(new SavedGloves { soName = item.GlovesInstance.data.name,
                                    ratioMelee = item.GlovesInstance.rolledRatioMelee,
                                    ratioRanged = item.GlovesInstance.rolledRatioRanged,
                                    ratioMagic = item.GlovesInstance.rolledRatioMagic,
                                    fusionLevel = item.GlovesInstance.fusionLevel,
                                    resistFire = item.GlovesInstance.resistFire, resistWater = item.GlovesInstance.resistWater,
                                    resistLightning = item.GlovesInstance.resistLightning, resistEarth = item.GlovesInstance.resistEarth,
                                    resistNature = item.GlovesInstance.resistNature, resistDarkness = item.GlovesInstance.resistDarkness,
                                    resistLight = item.GlovesInstance.resistLight });
        else if (item.BootsInstance?.data      != null)
            p.boots.Add(new SavedBoots { soName = item.BootsInstance.data.name,
                                    ratioMelee = item.BootsInstance.rolledRatioMelee,
                                    ratioRanged = item.BootsInstance.rolledRatioRanged,
                                    ratioMagic = item.BootsInstance.rolledRatioMagic,
                                    fusionLevel = item.BootsInstance.fusionLevel,
                                    resistFire = item.BootsInstance.resistFire, resistWater = item.BootsInstance.resistWater,
                                    resistLightning = item.BootsInstance.resistLightning, resistEarth = item.BootsInstance.resistEarth,
                                    resistNature = item.BootsInstance.resistNature, resistDarkness = item.BootsInstance.resistDarkness,
                                    resistLight = item.BootsInstance.resistLight });
        else if (item.JewelryInstance?.data    != null)
            p.jewelry.Add(new SavedJewelry { soName = item.JewelryInstance.data.name,
                                    jewelrySlot = item.JewelryInstance.Slot.ToString(),
                                    ratioMelee = item.JewelryInstance.rolledRatioMelee,
                                    ratioRanged = item.JewelryInstance.rolledRatioRanged,
                                    ratioMagic = item.JewelryInstance.rolledRatioMagic });
        else if (item.SpiritInstance?.data     != null)
            p.spirits.Add(new SavedSpirit { soName = item.SpiritInstance.data.name,
                                    spiritLevel = item.SpiritInstance.level,
                                    spiritXP    = item.SpiritInstance.currentXP });
        else if (item.ConsumableInstance?.data != null)
            p.consumables.Add(new SavedConsumable { soName = item.ConsumableInstance.data.name,
                                    quantity = item.ConsumableInstance.quantity });
        else if (item.ResourceInstance?.data   != null)
            p.resources.Add(new SavedResource { soName = item.ResourceInstance.data.name,
                                    quantity = item.ResourceInstance.quantity });
        else if (item.GemInstance?.data        != null)
            p.gems.Add(new SavedGem { soName = item.GemInstance.data.name });
        else if (item.RuneInstance?.data       != null)
            p.runes.Add(new SavedRune { soName = item.RuneInstance.data.name });
        else if (item.CosmeticInstanceHead?.data != null)
            p.cosmeticHeads.Add(new SavedCosmeticHead { soName = item.CosmeticInstanceHead.data.name });
        else if (item.CosmeticInstanceBody?.data != null)
            p.cosmeticBodies.Add(new SavedCosmeticBody { soName = item.CosmeticInstanceBody.data.name });
        else if (item.TalismanInstance?.data     != null)
            p.talismans.Add(new SavedTalisman { soName = item.TalismanInstance.data.name,
                                    activatedAt = item.TalismanInstance.activatedAt?.ToString("o") ?? "" });
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
        if (mails == null) return;

        foreach (var mail in mails)
        {
            if (mail == null) continue;

            var saved = new SavedMail
            {
                mailID        = mail.mailID,
                senderName    = mail.senderName,
                isFromServer  = mail.isFromServer,
                sentAt        = mail.sentAt.ToString("o"),
                subject       = mail.subject,
                body          = mail.body,
                isRead        = mail.isRead,
                rewardClaimed = mail.rewardClaimed,
                hasReward     = mail.HasReward,
            };

            if (mail.reward != null)
            {
                saved.rewardType         = (int)mail.reward.rewardType;
                saved.rewardSkillName    = mail.reward.rewardSkill?.name      ?? "";
                saved.rewardTitle        = mail.reward.rewardTitle             ?? "";
                saved.rewardRecipeID     = mail.reward.rewardRecipe?.name       ?? "";
                saved.rewardPetID        = mail.reward.rewardPetID             ?? "";
                saved.rewardDescription  = mail.reward.rewardDescription       ?? "";

                // Équipement générique — on sauvegarde le nom du SO pour le retrouver au chargement
                saved.rewardEquipmentName    = mail.reward.rewardEquipment?.name        ?? "";
                // Ressource / Consommable
                saved.rewardResourceName     = mail.reward.rewardResource?.name         ?? "";
                saved.rewardResourceQuantity = mail.reward.rewardResourceQuantity;
                saved.rewardConsumableName   = mail.reward.rewardConsumable?.name       ?? "";
                saved.rewardConsumableQuantity = mail.reward.rewardConsumableQuantity;
            }

            p.mails.Add(saved);
        }
    }

    // =========================================================
    // APPLICATION — CharacterProgress → Player
    // =========================================================

    private void ApplyProgress(Player player, CharacterProgress p)
    {
        // ⑧ Conditions débloquées — chargées EN PREMIER, avant tout ce qui
        // peut publier un StatsChangedEvent (OnLevelUp, RecalculateStats...).
        // Sans ça, EvaluateAll() se déclenche sur des conditions dont les
        // compteurs sont déjà à countRequired mais pas encore dans records{},
        // ce qui re-trigger les mails de récompense à chaque chargement.
        UnlockManager.Instance?.LoadUnlocked(p.unlockedConditionIDs);

        // ⑫ Progression conditions en cours — chargée juste après pour que
        // les compteurs soient cohérents avec les flags débloqués.
        if (p.conditionProgresses != null)
            UnlockManager.Instance?.LoadConditionProgresses(p.conditionProgresses);

        // ① Niveau & XP — après les conditions pour que le StatsChangedEvent
        // publié par RecalculateStats trouve les records{} déjà remplis.
        if (p.level > 1) player.OnLevelUp(p.level);
        player.xpCombat    = p.xpCombat;
        player.activeTitle = p.activeTitle;

        // xpToNextLevel n'est pas persisté (dérivé du niveau) — jamais recalculé au chargement
        // sinon, reste à sa valeur par défaut (100, niveau 1) jusqu'au premier AddCombatXP().
        // Même formule que AddCombatXP() — voir Player.cs.
        player.xpToNextLevel = player.characterData != null
            ? player.characterData.GetXPThreshold(player.level)
            : XPSystem.CalculateXPForLevel(player.level);

        // StatPoints — APRÈS OnLevelUp (qui distribue les points du niveau) :
        // on écrase le pool avec l'état réel sauvegardé (rangs investis + points restants).
        // Anciennes saves sans ces champs → tout à 0 = comportement actuel, pas de régression.
        player.statPoints?.LoadFromSave(
            p.spRankAttack, p.spRankDefense, p.spRankElemental, p.spRankHP,
            p.spTotalEarned, p.spAvailable);

        // ② Réputation
        player.AddWorldReputation(p.worldReputation - player.worldReputation);
        player.AddPvPReputation(p.pvpReputation   - player.pvpReputation);

        // ④ Aeris — SetAeris() fait autorité (pas Add() : la save doit pouvoir aussi
        // redescendre l'Aeris, ex. si le joueur en a dépensé après la dernière sauvegarde
        // dans une session précédente qui a mal fermé).
        AerisSystem.Instance?.SetAeris(p.aeris);

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

        if (p.unlockedPermanentNames != null)
            foreach (var name in p.unlockedPermanentNames)
            {
                var perm = FindSOByName<PermanentSkillData>(name);
                if (perm != null) player.UnlockPermanent(perm);
            }

        if (p.unlockedPassiveNames != null)
            foreach (var name in p.unlockedPassiveNames)
            {
                var passive = FindSOByName<PassiveSkillData>(name);
                if (passive != null) player.UnlockPassive(passive);
            }

        if (p.unlockedRecipeNames != null)
            foreach (var name in p.unlockedRecipeNames)
            {
                var recipe = FindSOByName<RecipeData>(name);
                if (recipe != null) player.UnlockRecipe(recipe);
            }

        // ⑦ SkillBar
        if (SkillBar.Instance != null)
        {
            foreach (var savedSlot in p.skillBarSlots)
            {
                var skill = FindSOByName<SkillData>(savedSlot.skillName);
                if (skill != null)
                {
                    // Garantit que le skill est débloqué même s'il manque dans unlockedSkillNames
                    // (ex : BasicAttack débloquée via condition mais save écrite avant autosave)
                    player.UnlockSkill(skill);
                    SkillBar.Instance.SetSkillAtSlot(savedSlot.slotIndex, skill);
                }
            }
            // Signale à SetStartingSkillBar de ne pas écraser les slots restaurés
            if (p.skillBarSlots != null && p.skillBarSlots.Count > 0)
            {
                var playerRef = FindObjectOfType<Player>();
                if (playerRef != null) playerRef.skillBarRestoredFromSave = true;
            }
        }

        // ⑦Bis PassifBar (3 slots équipés)
        if (p.passifBarSlots != null && player.equippedPassives != null)
        {
            foreach (var savedSlot in p.passifBarSlots)
            {
                var passive = FindSOByName<PassiveSkillData>(savedSlot.skillName);
                if (passive != null && savedSlot.slotIndex >= 0 && savedSlot.slotIndex < player.equippedPassives.Length)
                {
                    // Ignore une entrée dupliquée (même passif déjà écrit dans un autre slot par
                    // une entrée précédente) — évite un double-proc, voir PassiveSkillSystem.
                    bool alreadyEquipped = false;
                    for (int i = 0; i < player.equippedPassives.Length; i++)
                    {
                        if (i != savedSlot.slotIndex && player.equippedPassives[i] == passive)
                        {
                            alreadyEquipped = true;
                            break;
                        }
                    }
                    if (alreadyEquipped)
                    {
                        Debug.LogWarning($"[SAVE] Passif '{passive.name}' dupliqué dans passifBarSlots — entrée slot {savedSlot.slotIndex} ignorée.");
                        continue;
                    }

                    // Garantit que le passif est débloqué même s'il manque dans unlockedPassiveNames
                    player.UnlockPassive(passive);
                    player.equippedPassives[savedSlot.slotIndex] = passive;
                }
            }
            PassifBarUI.Instance?.RefreshAll();
        }

        // ⑬ Mails
        RestoreMails(p.mails);

        // ⑤ Items — en coroutine
        StartCoroutine(LoadItemsDelayed(player, p));
        GameEventBus.PublishSaveLoaded();
    }

    // ── Restauration mails ────────────────────────────────────

    private void RestoreMails(List<SavedMail> savedMails)
    {
        if (savedMails == null || savedMails.Count == 0) return;
        if (MailboxSystem.Instance == null) return;

        foreach (var saved in savedMails)
        {
            if (saved == null || string.IsNullOrEmpty(saved.mailID)) continue;

            MailReward reward = null;
            if (saved.hasReward)
            {
                reward = new MailReward
                {
                    rewardType               = (RewardType)saved.rewardType,
                    rewardTitle              = saved.rewardTitle,
                    rewardPetID              = saved.rewardPetID,
                    rewardDescription        = saved.rewardDescription,
                    rewardResourceQuantity   = saved.rewardResourceQuantity,
                    rewardConsumableQuantity = saved.rewardConsumableQuantity,
                };

                // Skill
                if (!string.IsNullOrEmpty(saved.rewardSkillName))
                    reward.rewardSkill = FindSOByName<SkillData>(saved.rewardSkillName);

                // Équipement générique — retrouvé par nom de SO
                if (!string.IsNullOrEmpty(saved.rewardEquipmentName))
                    reward.rewardEquipment = FindSOByName<ScriptableObject>(saved.rewardEquipmentName);

                // Ressource
                if (!string.IsNullOrEmpty(saved.rewardResourceName))
                    reward.rewardResource = FindSOByName<ResourceData>(saved.rewardResourceName);

                // Consommable
                if (!string.IsNullOrEmpty(saved.rewardConsumableName))
                    reward.rewardConsumable = FindSOByName<ConsumableData>(saved.rewardConsumableName);

                // Recette
                if (!string.IsNullOrEmpty(saved.rewardRecipeID))
                    reward.rewardRecipe = FindSOByName<RecipeData>(saved.rewardRecipeID);
            }

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

        // Fenêtre courte — UnequipAll()/RestoreItems() appellent EquipWeapon()/UnequipWeapon(),
        // qui ne doivent pas écraser le slot 0 déjà posé depuis skillBarSlots pendant cette
        // restauration. Remis à false juste après, contrairement à skillBarRestoredFromSave
        // (permanent) — les équip/déséquip normaux du reste de la partie restent fonctionnels.
        player.isRestoringEquipment = true;

        InventorySystem.Instance?.UnequipAll(player);
        InventorySystem.Instance?.ClearAll();

        RestoreItems(player, p);

        player.isRestoringEquipment = false;

        // ⑦Ter ConsoBar (3 slots) — après RestoreItems : a besoin de l'inventaire déjà
        // peuplé pour retrouver la ConsumableInstance vivante (même référence que celle
        // manipulée par ConsoBarUI.TryUseSlot(), pas une instance détachée).
        if (ConsoBarUI.Instance != null && p.consoBarSlots != null)
            foreach (var savedSlot in p.consoBarSlots)
            {
                var data = FindSOByName<ConsumableData>(savedSlot.skillName);
                if (data == null) continue;
                var item = InventorySystem.Instance?.GetAllItems().Find(i => i.ConsumableInstance?.data == data);
                if (item?.ConsumableInstance != null)
                    ConsoBarUI.Instance.AssignConsoInstance(savedSlot.slotIndex, item.ConsumableInstance);
            }

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

        var elemental = player.GetElementalSystem();
        if (elemental != null && p.elementAffinities != null && p.elementAffinities.Count > 0)
            elemental.LoadAffinities(p.elementAffinities);

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

        GameEventBus.Publish(new StatsChangedEvent { player = player });
        InventoryUI.Instance?.RefreshGrid();
        CharacterPanelUI.Instance?.Refresh();

        int itemCount = p.weapons.Count + p.armors.Count + p.helmets.Count + p.gloves.Count + p.boots.Count +
                        p.jewelry.Count + p.spirits.Count + p.consumables.Count + p.resources.Count +
                        p.gems.Count + p.runes.Count + p.cosmeticHeads.Count + p.cosmeticBodies.Count + p.talismans.Count;
        Debug.Log($"[LOAD] ✅ Items:{itemCount} | Quêtes:{p.quests?.Count ?? 0} | " +
                  $"Mails:{p.mails?.Count ?? 0}");
    }

    /// <summary>Restaure les 14 catégories d'items — chacune via son propre chemin,
    /// vu que chaque SavedXxx ne porte que ses champs propres (pas de dispatch générique
    /// possible sans polymorphisme, non supporté par JsonUtility).</summary>
    private void RestoreItems(Player player, CharacterProgress p)
    {
        // NB : on reconstruit chaque instance directement avec les ratios sauvegardés
        // (constructeur explicite, ou CreateInstance() + écrasement des champs rollés
        // quand l'état par défaut du SO doit d'abord être posé — Gloves/Boots) plutôt
        // que via CreateDropInstance()/CreateInstance() seul, qui rollent un NOUVEAU
        // ratio aléatoire à chaque appel — sinon l'item change de stats à chaque
        // chargement de sauvegarde.

        foreach (var s in p.weapons)
        {
            var wd = FindSOByName<WeaponData>(s.soName);
            if (wd == null) { Debug.LogWarning($"[LOAD] ⚠ Weapon introuvable : '{s.soName}'"); continue; }
            var inst = new WeaponInstance(wd, s.ratioMin, s.ratioMax, s.ratioPrecision, s.rarityRank, s.upgradeLevel);
            inst.hasAethernelleSeal = s.hasAethernelleSeal;
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.armors)
        {
            var ad = FindSOByName<ArmorData>(s.soName);
            if (ad == null) { Debug.LogWarning($"[LOAD] ⚠ Armor introuvable : '{s.soName}'"); continue; }
            var inst = new ArmorInstance(ad, s.ratioMelee, s.ratioRanged, s.ratioMagic, s.ratioDodge, s.rarityRank, s.upgradeLevel);
            inst.hasAethernelleSeal = s.hasAethernelleSeal;
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.helmets)
        {
            var hd = FindSOByName<HelmetData>(s.soName);
            if (hd == null) { Debug.LogWarning($"[LOAD] ⚠ Helmet introuvable : '{s.soName}'"); continue; }
            var inst = new HelmetInstance(hd, s.ratioMelee, s.ratioRanged, s.ratioMagic);
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.gloves)
        {
            var gd = FindSOByName<GlovesData>(s.soName);
            if (gd == null) { Debug.LogWarning($"[LOAD] ⚠ Gloves introuvable : '{s.soName}'"); continue; }
            // Passe par CreateInstance() (pas un constructeur direct) pour poser d'abord
            // les résistances de base depuis baseResistXxx, puis écrase avec l'état
            // sauvegardé (ratios + Fusion) — fonctionne pareil, fusionnée ou non.
            var inst = gd.CreateInstance();
            inst.rolledRatioMelee  = s.ratioMelee;
            inst.rolledRatioRanged = s.ratioRanged;
            inst.rolledRatioMagic  = s.ratioMagic;
            inst.fusionLevel       = s.fusionLevel;
            inst.resistFire        = s.resistFire;
            inst.resistWater       = s.resistWater;
            inst.resistLightning   = s.resistLightning;
            inst.resistEarth       = s.resistEarth;
            inst.resistNature      = s.resistNature;
            inst.resistDarkness    = s.resistDarkness;
            inst.resistLight       = s.resistLight;
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.boots)
        {
            var bd = FindSOByName<BootsData>(s.soName);
            if (bd == null) { Debug.LogWarning($"[LOAD] ⚠ Boots introuvable : '{s.soName}'"); continue; }
            var inst = bd.CreateInstance();
            inst.rolledRatioMelee  = s.ratioMelee;
            inst.rolledRatioRanged = s.ratioRanged;
            inst.rolledRatioMagic  = s.ratioMagic;
            inst.fusionLevel       = s.fusionLevel;
            inst.resistFire        = s.resistFire;
            inst.resistWater       = s.resistWater;
            inst.resistLightning   = s.resistLightning;
            inst.resistEarth       = s.resistEarth;
            inst.resistNature      = s.resistNature;
            inst.resistDarkness    = s.resistDarkness;
            inst.resistLight       = s.resistLight;
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.jewelry)
        {
            var jd = FindSOByName<JewelryData>(s.soName);
            if (jd == null) { Debug.LogWarning($"[LOAD] ⚠ Jewelry introuvable : '{s.soName}'"); continue; }
            var inst = new JewelryInstance(jd, s.ratioMelee, s.ratioRanged, s.ratioMagic);
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.spirits)
        {
            var sd = FindSOByName<SpiritData>(s.soName);
            if (sd == null) { Debug.LogWarning($"[LOAD] ⚠ Spirit introuvable : '{s.soName}'"); continue; }
            var inst = new SpiritInstance(sd) { level = Mathf.Max(1, s.spiritLevel), currentXP = Mathf.Max(0, s.spiritXP) };
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.talismans)
        {
            var td = FindSOByName<TalismanData>(s.soName);
            if (td == null) { Debug.LogWarning($"[LOAD] ⚠ Talisman introuvable : '{s.soName}'"); continue; }
            var inst = new TalismanInstance(td);
            if (!string.IsNullOrEmpty(s.activatedAt) && DateTime.TryParse(s.activatedAt, out DateTime activated))
                inst.activatedAt = activated;
            if (inst.IsExpired)
            {
                Debug.Log($"[LOAD] Talisman expiré détruit au chargement : '{s.soName}'");
                continue; // objet détruit — jamais restauré, même s'il était équipé
            }
            AddOrEquip(new InventoryItem(inst), s.isEquipped, player);
        }

        foreach (var s in p.consumables)
        {
            var cd = FindSOByName<ConsumableData>(s.soName);
            if (cd == null) { Debug.LogWarning($"[LOAD] ⚠ Consumable introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(cd.CreateInstance(Mathf.Max(1, s.quantity))));
        }

        foreach (var s in p.resources)
        {
            var rd = FindSOByName<ResourceData>(s.soName);
            if (rd == null) { Debug.LogWarning($"[LOAD] ⚠ Resource introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(rd.CreateInstance(Mathf.Max(1, s.quantity))));
        }

        foreach (var s in p.gems)
        {
            var gemD = FindSOByName<GemData>(s.soName);
            if (gemD == null) { Debug.LogWarning($"[LOAD] ⚠ Gem introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(gemD.CreateDropInstance()));
        }

        foreach (var s in p.runes)
        {
            var runeD = FindSOByName<RuneData>(s.soName);
            if (runeD == null) { Debug.LogWarning($"[LOAD] ⚠ Rune introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(runeD.CreateDropInstance()));
        }

        foreach (var s in p.cosmeticHeads)
        {
            var cosH = FindSOByName<CosmeticDataHead>(s.soName);
            if (cosH == null) { Debug.LogWarning($"[LOAD] ⚠ CosmeticHead introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(new CosmeticInstanceHead(cosH)));
        }

        foreach (var s in p.cosmeticBodies)
        {
            var cosB = FindSOByName<CosmeticDataBody>(s.soName);
            if (cosB == null) { Debug.LogWarning($"[LOAD] ⚠ CosmeticBody introuvable : '{s.soName}'"); continue; }
            InventorySystem.Instance?.AddItem(new InventoryItem(new CosmeticInstanceBody(cosB)));
        }
    }

    private void AddOrEquip(InventoryItem item, bool isEquipped, Player player)
    {
        if (isEquipped) InventorySystem.Instance?.EquipItem(item, player);
        else            InventorySystem.Instance?.AddItem(item);
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
