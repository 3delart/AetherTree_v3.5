using UnityEngine;
using System.Collections.Generic;

// =============================================================
// MailboxSystem.CS
// Path : Assets/_Game/Scripts/Systems/MailboxSystem.cs
// AetherTree GDD v31
//
// Flow récompense :
//   UnlockManager.Unlock() → MailboxSystem.SendRewardMail()
//   → Panel Social onglet Messagerie affiche le mail
//   → Joueur clique "Récupérer" → MailboxSystem.ClaimReward()
//   → Reward distribuée selon RewardType
// =============================================================

[System.Serializable]
public class MailMessage
{
    // ── Identité ──────────────────────────────────────────────
    public string          mailID;
    public string          senderName;
    public bool            isFromServer;
    public System.DateTime sentAt;

    // ── Contenu ───────────────────────────────────────────────
    public string          subject;
    [TextArea] public string body;

    // ── Récompense attachée ───────────────────────────────────
    public MailReward      reward;
    public bool            rewardClaimed;

    // ── État ──────────────────────────────────────────────────
    public bool            isRead;

    // ── Helpers ───────────────────────────────────────────────
    public bool HasReward        => reward != null && reward.rewardType != RewardType.None;
    public bool CanClaim         => HasReward && !rewardClaimed;
    public string SentDateString => sentAt.ToString("dd/MM/yyyy HH:mm");
}

// =============================================================
// MailReward — Récompense attachée à un mail
// Miroir de ConditionReward — stocké en runtime dans le mail.
// =============================================================
[System.Serializable]
public class MailReward
{
    public RewardType rewardType = RewardType.None;

    // ── Skill / Titre ─────────────────────────────────────────
    public SkillData         rewardSkill;
    public string            rewardTitle;

    // ── Équipement générique (SO) ─────────────────────────────
    // Contient WeaponData, ArmorData, HelmetData, GlovesData,
    // BootsData, JewelryData, SpiritData, CosmeticDataHead,
    // CosmeticDataBody ou TalismanData selon rewardType.
    public ScriptableObject  rewardEquipment;

    // ── Ressource / Consommable ───────────────────────────────
    public ResourceData      rewardResource;
    public int               rewardResourceQuantity = 1;
    public ConsumableData    rewardConsumable;
    public int               rewardConsumableQuantity = 1;

    // ── Pet (string ID — SO à venir) ──────────────────────────
    public string            rewardPetID;
    // ── Recette ────────────────────────────────────────────────
    public RecipeData        rewardRecipe;

    // ── Quête ──────────────────────────────────────────────────
    public QuestData         rewardQuest;

    // ── Description ───────────────────────────────────────────
    public string            rewardDescription;

    // ── Helpers affichage ─────────────────────────────────────

    /// <summary>Nom lisible de la récompense pour l'UI.</summary>
    public string GetDisplayName()
    {
        Language lang = LocalizationManager.CurrentLanguage;
        string equipName(string fallback) => (rewardEquipment as ItemData)?.displayName.Get(lang) ?? fallback;

        return rewardType switch
        {
            RewardType.Skill
            or RewardType.SkillAndTitle  => rewardSkill != null ? rewardSkill.skillName.Get(lang) : "Compétence",
            RewardType.Title             => rewardTitle ?? "Titre",
            RewardType.Weapon            => equipName("Arme"),
            RewardType.Armor             => equipName("Armure"),
            RewardType.Helmet            => equipName("Casque"),
            RewardType.Gloves            => equipName("Gants"),
            RewardType.Boots             => equipName("Bottes"),
            RewardType.Jewelry           => equipName("Bijou"),
            RewardType.Spirit            => equipName("Esprit"),
            RewardType.CosmeticHead      => equipName("Cosmétique Tête"),
            RewardType.CosmeticBody      => equipName("Cosmétique Corps"),
            RewardType.Talisman          => equipName("Talisman"),
            RewardType.Resource          => rewardResource   != null ? $"{rewardResource.displayName.Get(lang)} ×{rewardResourceQuantity}"     : "Ressource",
            RewardType.Consumable        => rewardConsumable != null ? $"{rewardConsumable.displayName.Get(lang)} ×{rewardConsumableQuantity}" : "Consommable",
            RewardType.Recipe            => rewardRecipe?.result != null ? $"Recette : {rewardRecipe.result.displayName.Get(lang)}" : "Recette",
            RewardType.Pet               => !string.IsNullOrEmpty(rewardPetID)    ? $"Pet : {rewardPetID}"        : "Pet",
            RewardType.Quest             => rewardQuest != null ? $"Quête : {rewardQuest.questName}" : "Quête",
            _                            => rewardDescription ?? "Récompense",
        };
    }

    /// <summary>Icône de la récompense pour l'UI (null si non applicable).</summary>
    public Sprite GetIcon()
    {
        return rewardType switch
        {
            RewardType.Skill
            or RewardType.SkillAndTitle  => rewardSkill?.icon,
            RewardType.Weapon            => (rewardEquipment as WeaponData)?.icon,
            RewardType.Armor             => (rewardEquipment as ArmorData)?.icon,
            RewardType.Helmet            => (rewardEquipment as HelmetData)?.icon,
            RewardType.Gloves            => (rewardEquipment as GlovesData)?.icon,
            RewardType.Boots             => (rewardEquipment as BootsData)?.icon,
            RewardType.Jewelry           => (rewardEquipment as JewelryData)?.icon,
            RewardType.Spirit            => (rewardEquipment as SpiritData)?.icon,
            RewardType.CosmeticHead      => (rewardEquipment as CosmeticDataHead)?.icon,
            RewardType.CosmeticBody      => (rewardEquipment as CosmeticDataBody)?.icon,
            RewardType.Talisman          => (rewardEquipment as TalismanData)?.icon,
            RewardType.Resource          => rewardResource?.icon,
            RewardType.Consumable        => rewardConsumable?.icon,
            RewardType.Recipe            => rewardRecipe?.result?.icon,
            _                            => null,
        };
    }
}

// =============================================================
// MailboxSystem — Singleton gérant la boîte mail du joueur
// =============================================================
public class MailboxSystem : MonoBehaviour
{
    public static MailboxSystem Instance { get; private set; }

    private List<MailMessage> messages = new List<MailMessage>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // =========================================================
    // ENVOI
    // =========================================================

    /// <summary>
    /// Envoie un mail de récompense serveur suite au déblocage d'une condition.
    /// Appelé par UnlockManager.Unlock().
    /// </summary>
    public void SendRewardMail(ConditionData condition, ConditionReward condReward)
    {
        var mailReward = new MailReward
        {
            rewardType               = condReward.rewardType,
            rewardSkill              = condReward.rewardSkill,
            rewardTitle              = condReward.rewardTitle,
            rewardEquipment          = condReward.rewardEquipment,
            rewardResource           = condReward.rewardResource,
            rewardResourceQuantity   = condReward.rewardResourceQuantity,
            rewardConsumable         = condReward.rewardConsumable,
            rewardConsumableQuantity = condReward.rewardConsumableQuantity,
            rewardPetID              = condReward.rewardPetID,
            rewardRecipe             = condReward.rewardRecipe,
            rewardQuest              = condReward.rewardQuest,
        };

        SendMail(
            mailID:     $"reward_{condition.conditionID}_{System.DateTime.Now.Ticks}",
            senderName: "Serveur AetherTree",
            subject:    $"Récompense débloquée : {condition.displayName}",
            body:       condition.description,
            reward:     mailReward);
    }

    /// <summary>Envoie un loot de combat en mail de secours — l'inventaire du gagnant était
    /// plein au moment du kill (voir LootManager.DeliverItem). Ne stocke QU'une référence
    /// vers le SO d'origine, pas l'instance déjà rollée au kill — les stats sont rerollées à
    /// la réclamation (CreateDropInstance()/CreateInstance()), exactement comme un mail de
    /// récompense ConditionData aujourd'hui. Pas de préservation du roll exact — décision du
    /// design (voir spec §Gestion inventaire plein).</summary>
    public void SendLootOverflowMail(InventoryItem item, string mobName)
    {
        if (item == null) return;

        var reward = new MailReward { rewardDescription = item.Name };

        if      (item.WeaponInstance?.data     != null) { reward.rewardType = RewardType.Weapon;    reward.rewardEquipment = item.WeaponInstance.data; }
        else if (item.ArmorInstance?.data      != null) { reward.rewardType = RewardType.Armor;      reward.rewardEquipment = item.ArmorInstance.data; }
        else if (item.HelmetInstance?.data     != null) { reward.rewardType = RewardType.Helmet;     reward.rewardEquipment = item.HelmetInstance.data; }
        else if (item.GlovesInstance?.data     != null) { reward.rewardType = RewardType.Gloves;     reward.rewardEquipment = item.GlovesInstance.data; }
        else if (item.BootsInstance?.data      != null) { reward.rewardType = RewardType.Boots;      reward.rewardEquipment = item.BootsInstance.data; }
        else if (item.JewelryInstance?.data    != null) { reward.rewardType = RewardType.Jewelry;    reward.rewardEquipment = item.JewelryInstance.data; }
        else if (item.SpiritInstance?.data     != null) { reward.rewardType = RewardType.Spirit;     reward.rewardEquipment = item.SpiritInstance.data; }
        else if (item.ConsumableInstance?.data != null) { reward.rewardType = RewardType.Consumable; reward.rewardConsumable = item.ConsumableInstance.data; reward.rewardConsumableQuantity = item.ConsumableInstance.quantity; }
        else if (item.ResourceInstance?.data   != null) { reward.rewardType = RewardType.Resource;   reward.rewardResource   = item.ResourceInstance.data;   reward.rewardResourceQuantity   = item.ResourceInstance.quantity; }
        else
        {
            // Gem/Rune (ou tout type non géré ci-dessus) — pas de RewardType dédié, ces deux
            // types sont "hors scope, refonte prévue" (voir GemData.cs/RuneData.cs, aucun
            // CreateAssetMenu). Perdu si l'inventaire est plein — acceptable tant qu'aucun
            // contenu réel n'existe pour ces types.
            Debug.LogWarning($"[MAILBOX] Loot perdu (inventaire plein, type non géré par le mail) : {item.Name} ({mobName})");
            return;
        }

        SendMail(
            mailID:     $"loot_{mobName}_{System.DateTime.Now.Ticks}",
            senderName: "Butin de combat",
            subject:    $"Butin de {mobName} (inventaire plein)",
            body:       "Ton inventaire était plein au moment du kill — voici ton butin.",
            reward:     reward);
    }

    /// <summary>Construit et envoie un MailMessage — factorisé entre SendRewardMail et
    /// SendLootOverflowMail (même structure, seule la source de la récompense diffère).</summary>
    private void SendMail(string mailID, string senderName, string subject, string body, MailReward reward)
    {
        var mail = new MailMessage
        {
            mailID        = mailID,
            senderName    = senderName,
            isFromServer  = true,
            sentAt        = System.DateTime.Now,
            subject       = subject,
            body          = body,
            reward        = reward,
            rewardClaimed = false,
            isRead        = false,
        };

        messages.Add(mail);
        Debug.Log($"[MAILBOX] Mail envoyé : {subject}");

        SocialUI.Instance?.OnNewMail(mail);
    }

    // =========================================================
    // RÉCUPÉRATION DE RÉCOMPENSE
    // =========================================================

    public bool ClaimReward(string mailID)
    {
        var mail = GetMail(mailID);
        if (mail == null)
        {
            Debug.LogWarning($"[MAILBOX] Mail introuvable : {mailID}");
            return false;
        }
        if (!mail.CanClaim)
        {
            Debug.LogWarning($"[MAILBOX] Récompense déjà récupérée : {mailID}");
            return false;
        }

        var player = FindObjectOfType<Player>();
        if (player == null)
        {
            Debug.LogWarning("[MAILBOX] Player introuvable — impossible de distribuer la récompense.");
            return false;
        }

        DistributeReward(mail.reward, player);
        mail.rewardClaimed = true;
        mail.isRead        = true;

        Debug.Log($"[MAILBOX] Récompense récupérée : {mail.subject}");
        SocialUI.Instance?.RefreshMail(mail);
        return true;
    }

    private void DistributeReward(MailReward reward, Player player)
    {
        if (reward == null) return;

        switch (reward.rewardType)
        {
            // ── Skill ──────────────────────────────────────────
            case RewardType.Skill:
                if (reward.rewardSkill != null)
                {
                    player.UnlockSkill(reward.rewardSkill);
                    SkillLibraryUI.Instance?.RefreshIfOpen();
                    Debug.Log($"[MAILBOX] Skill débloqué : {reward.rewardSkill.name}");
                }
                break;

            // ── Skill + Titre ──────────────────────────────────
            case RewardType.SkillAndTitle:
                if (reward.rewardSkill != null)
                {
                    player.UnlockSkill(reward.rewardSkill);
                    SkillLibraryUI.Instance?.RefreshIfOpen();
                }
                if (!string.IsNullOrEmpty(reward.rewardTitle))
                    Debug.Log($"[MAILBOX] Titre débloqué : {reward.rewardTitle}");
                    // TODO: TitleSystem.Instance?.UnlockTitle(reward.rewardTitle, player)
                break;

            // ── Titre ──────────────────────────────────────────
            case RewardType.Title:
                if (!string.IsNullOrEmpty(reward.rewardTitle))
                    Debug.Log($"[MAILBOX] Titre débloqué : {reward.rewardTitle}");
                    // TODO: TitleSystem.Instance?.UnlockTitle(reward.rewardTitle, player)
                break;

            // ── Équipements ────────────────────────────────────
            case RewardType.Weapon:
                var weapon = reward.rewardEquipment as WeaponData;
                if (weapon != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(weapon.CreateDropInstance(0, 0)));
                    Debug.Log($"[MAILBOX] Arme ajoutée : {weapon.itemID}");
                }
                break;

            case RewardType.Armor:
                var armor = reward.rewardEquipment as ArmorData;
                if (armor != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(armor.CreateDropInstance(0, 0)));
                    Debug.Log($"[MAILBOX] Armure ajoutée : {armor.itemID}");
                }
                break;

            case RewardType.Helmet:
                var helmet = reward.rewardEquipment as HelmetData;
                if (helmet != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(helmet.CreateInstance()));
                    Debug.Log($"[MAILBOX] Casque ajouté : {helmet.itemID}");
                }
                break;

            case RewardType.Gloves:
                var gloves = reward.rewardEquipment as GlovesData;
                if (gloves != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(gloves.CreateInstance()));
                    Debug.Log($"[MAILBOX] Gants ajoutés : {gloves.itemID}");
                }
                break;

            case RewardType.Boots:
                var boots = reward.rewardEquipment as BootsData;
                if (boots != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(boots.CreateInstance()));
                    Debug.Log($"[MAILBOX] Bottes ajoutées : {boots.itemID}");
                }
                break;

            case RewardType.Jewelry:
                var jewelry = reward.rewardEquipment as JewelryData;
                if (jewelry != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(jewelry.CreateInstance()));
                    Debug.Log($"[MAILBOX] Bijou ajouté : {jewelry.itemID}");
                }
                break;

            case RewardType.Spirit:
                var spirit = reward.rewardEquipment as SpiritData;
                if (spirit != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(new SpiritInstance(spirit)));
                    Debug.Log($"[MAILBOX] Esprit ajouté : {spirit.itemID}");
                }
                break;

            case RewardType.CosmeticHead:
                var cosHead = reward.rewardEquipment as CosmeticDataHead;
                if (cosHead != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(cosHead.CreateInstance()));
                    Debug.Log($"[MAILBOX] Cosmétique tête ajouté : {cosHead.itemID}");
                }
                break;

            case RewardType.CosmeticBody:
                var cosBody = reward.rewardEquipment as CosmeticDataBody;
                if (cosBody != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(cosBody.CreateInstance()));
                    Debug.Log($"[MAILBOX] Cosmétique corps ajouté : {cosBody.itemID}");
                }
                break;

            case RewardType.Talisman:
                var talisman = reward.rewardEquipment as TalismanData;
                if (talisman != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(talisman.CreateInstance()));
                    Debug.Log($"[MAILBOX] Talisman ajouté : {talisman.itemID}");
                }
                break;

            // ── Ressource ──────────────────────────────────────
            case RewardType.Resource:
                if (reward.rewardResource != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(reward.rewardResource.CreateInstance(reward.rewardResourceQuantity)));
                    Debug.Log($"[MAILBOX] Ressource ×{reward.rewardResourceQuantity} : {reward.rewardResource.itemID}");
                }
                break;

            // ── Consommable ────────────────────────────────────
            case RewardType.Consumable:
                if (reward.rewardConsumable != null)
                {
                    InventorySystem.Instance?.AddItem(new InventoryItem(reward.rewardConsumable.CreateInstance(reward.rewardConsumableQuantity)));
                    Debug.Log($"[MAILBOX] Consommable ×{reward.rewardConsumableQuantity} : {reward.rewardConsumable.itemID}");
                }
                break;

            // ── Recette ───────────────────────────────────────
            case RewardType.Recipe:
                if (reward.rewardRecipe != null)
                {
                    player.UnlockRecipe(reward.rewardRecipe);
                    Debug.Log($"[MAILBOX] Recette débloquée : {reward.rewardRecipe.name}");
                }
                break;

            // ── Pet ───────────────────────────────────────────
            case RewardType.Pet:
                if (!string.IsNullOrEmpty(reward.rewardPetID))
                {
                    Debug.Log($"[MAILBOX] Pet débloqué : {reward.rewardPetID}");
                    // TODO: PetSystem.Instance?.UnlockPet(reward.rewardPetID, player)
                }
                break;

            // ── Quête ───────────────────────────────────────────
            case RewardType.Quest:
                if (reward.rewardQuest != null)
                {
                    bool accepted = QuestSystem.Instance != null
                        && QuestSystem.Instance.AcceptQuest(reward.rewardQuest, player);
                    Debug.Log(accepted
                        ? $"[MAILBOX] Quête ajoutée au journal : {reward.rewardQuest.questName}"
                        : $"[MAILBOX] Échec ajout quête (prérequis/niveau non remplis ou déjà active) : {reward.rewardQuest.questName}");
                }
                break;

            case RewardType.None:
            default:
                Debug.Log($"[MAILBOX] Récompense sans distribution : {reward.rewardDescription}");
                break;
        }
    }

    // =========================================================
    // ACCESSEURS
    // =========================================================

    public void RestoreMail(MailMessage mail)
    {
        if (mail == null || string.IsNullOrEmpty(mail.mailID)) return;
        if (messages.Exists(m => m.mailID == mail.mailID)) return;
        messages.Add(mail);
    }

    public List<MailMessage> GetAllMails()    => messages;
    public int               UnreadCount()    => messages.FindAll(m => !m.isRead).Count;
    public int               UnclaimedCount() => messages.FindAll(m => m.CanClaim).Count;

    public MailMessage GetMail(string mailID)
        => messages.Find(m => m.mailID == mailID);

    public void MarkAsRead(string mailID)
    {
        var mail = GetMail(mailID);
        if (mail != null) mail.isRead = true;
    }

    public void DeleteMail(string mailID)
    {
        var mail = GetMail(mailID);
        if (mail != null && !mail.CanClaim)
            messages.Remove(mail);
        else if (mail?.CanClaim == true)
            Debug.LogWarning("[MAILBOX] Impossible de supprimer un mail avec récompense non récupérée.");
    }
}
