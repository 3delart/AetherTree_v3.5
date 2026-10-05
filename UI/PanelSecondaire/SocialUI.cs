using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// SOCIALUI.CS — Panel Social Global
// AetherTree GDD v21
//
// Touches :
//   GameControls.OpenMail  → ouvre sur onglet Mail
//   GameControls.OpenGuild → ouvre sur onglet Guild
//
// GameControls.OpenChat (touche V) ouvre/cache le dock HUD permanent séparé (voir
// UI/PanelFixe/ChatUI.cs), PAS l'onglet Chat ci-dessous — les deux sont liés différemment :
// l'onglet Chat ici = conversations privées PAR JOUEUR (DM), voir DirectMessageSystem.cs. Un clic
// sur le nom d'un expéditeur dans le dock ChatUI appelle OpenDMWith(nom) pour ouvrir directement
// la bonne conversation ici (2026-10-05, Florian — re-construit après un premier retrait par
// erreur du stub vide précédent, confondu avec le dock global).
//
// Si déjà ouvert sur le bon onglet → ferme.
// Cliquer un onglet → swap sans fermer.
//
// Hiérarchie Unity :
//   SocialPanel (racine)
//     SocialTitlePanel / SocialCloseButton
//     SocialTabs : ChatTab | GuildTab | MailTab
//     ChatPanel (onglet DM)
//       ConversationListPanel
//         ConversationScroll > Viewport > Content   (un ConversationEntryPrefab par conversation :
//                                                      Avatar/Initials, Name, LastMessage, UnreadDot)
//       ThreadPanel
//         ThreadHeader : ThreadName (TMP) | DeleteConversationButton (supprime fil + entrée liste)
//         ThreadScroll > Viewport > Content          (un ThreadLinePrefab par message — TMP simple)
//         ThreadInputBar : ThreadInput (TMP_InputField) | ThreadSendButton
//     GuildPanel   ← stub pour l'instant
//     MailsPanel
//       MailsListPanel
//         FilterBar : FilterAll | FilterUnread | FilterSystem | FilterPlayer
//         MailScroll > Viewport > Content
//       MailDetailPanel
//         DetailHeader : DetailSubject | DetailFrom | DetailDate
//         DetailBody > Viewport > Content > Text (TMP)
//         RewardBox : RewardTitle | RewardItemContainer | ClaimButton
//         DetailFooter : DeleteButton | ReplyButton
// =============================================================

public class SocialUI : MonoBehaviour
{
    public static SocialUI Instance { get; private set; }

    // =========================================================
    // RÉFÉRENCES UI
    // =========================================================
    [Header("Panel racine")]
    public GameObject socialPanel;
    public Button     closeButton;

    [Header("Onglets")]
    public Button chatTab;
    public Button guildTab;
    public Button mailTab;

    [Header("Sous-panels")]
    public GameObject chatPanel;
    public GameObject guildPanel;
    public GameObject mailsPanel;

    // ── DM (Chat privé par joueur) ──────────────────────────────
    [Header("DM — Liste conversations")]
    public Transform  conversationContent;
    public GameObject conversationEntryPrefab;

    [Header("DM — Fil de conversation")]
    public TextMeshProUGUI threadHeaderName;
    public Transform        threadContent;
    public GameObject       threadLinePrefab;
    public TMP_InputField   threadInput;
    public Button            threadSendButton;
    public Button            deleteConversationButton; // supprime le fil ENTIER (thread + entrée liste)

    // ── MAILS ─────────────────────────────────────────────────
    [Header("Filtre mails")]
    public Button filterAll;
    public Button filterUnread;
    public Button filterSystem;
    public Button filterPlayer;

    [Header("Liste mails")]
    public Transform  mailContent;
    public GameObject entryMailPrefab;

    [Header("Détail — Header")]
    public TextMeshProUGUI detailSubject;
    public TextMeshProUGUI detailFrom;
    public TextMeshProUGUI detailDate;

    [Header("Détail — Corps")]
    public TextMeshProUGUI detailBodyText;

    [Header("Détail — Récompense")]
    public GameObject      rewardBox;
    public TextMeshProUGUI rewardTitle;
    public Transform       rewardItemContainer;
    public Button          claimButton;

    [Header("Détail — Footer")]
    public Button deleteButton;
    public Button replyButton;

    [Header("Prefab récompense")]
    public GameObject rewardItemPrefab;

    // =========================================================
    // ÉTAT INTERNE
    // =========================================================
    public enum SocialTab { None, Chat, Guild, Mail }

    private SocialTab   currentTab    = SocialTab.None;
    private MailFilter  currentFilter = MailFilter.All;
    private MailMessage selectedMail  = null;
    private bool        _isOpen       = false;

    private string _selectedConversation = null; // nom du joueur, "" / null = aucune sélection
    private Player _player;

    private enum MailFilter { All, Unread, System, Player }

    private static readonly Color TAB_ACTIVE      = new Color(0.35f, 0.22f, 0.65f);
    private static readonly Color TAB_INACTIVE    = new Color(0.15f, 0.15f, 0.25f);
    private static readonly Color FILTER_ACTIVE   = new Color(0.4f,  0.25f, 0.75f);
    private static readonly Color FILTER_INACTIVE = new Color(0.2f,  0.2f,  0.35f);

    // =========================================================
    // INIT
    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (socialPanel != null) socialPanel.SetActive(false);
    }

    private void Start()
    {
        // Bouton fermer
        closeButton?.onClick.AddListener(Close);

        // Onglets
        chatTab? .onClick.AddListener(() => SwitchTab(SocialTab.Chat));
        guildTab?.onClick.AddListener(() => SwitchTab(SocialTab.Guild));
        mailTab? .onClick.AddListener(() => SwitchTab(SocialTab.Mail));

        // Filtres mails
        filterAll?.onClick   .AddListener(() => SetFilter(MailFilter.All));
        filterUnread?.onClick.AddListener(() => SetFilter(MailFilter.Unread));
        filterSystem?.onClick.AddListener(() => SetFilter(MailFilter.System));
        filterPlayer?.onClick.AddListener(() => SetFilter(MailFilter.Player));

        // Actions détail
        claimButton? .onClick.AddListener(OnClaimClicked);
        deleteButton?.onClick.AddListener(OnDeleteClicked);
        replyButton? .onClick.AddListener(OnReplyClicked);

        // DM
        threadSendButton?.onClick.AddListener(OnThreadSendClicked);
        threadInput      ?.onSubmit.AddListener(_ => OnThreadSendClicked());
        deleteConversationButton?.onClick.AddListener(OnDeleteConversationClicked);

        ClearMailDetail();
    }

    // =========================================================
    // OPEN / CLOSE / TOGGLE
    // =========================================================

    /// <summary>Ouvre le panel sur un onglet spécifique.</summary>
    public void OpenOnTab(SocialTab tab)
    {
        _isOpen = true;
        if (socialPanel != null) socialPanel.SetActive(true);

        // Si même onglet Mail déjà actif → refresh quand même
        if (tab == SocialTab.Mail && currentTab == SocialTab.Mail)
        {
            RefreshMailList();
            ClearMailDetail();
            return;
        }

        SwitchTab(tab);
    }

    /// <summary>Si ouvert sur cet onglet → ferme. Sinon → ouvre sur cet onglet.</summary>
    public void ToggleTab(SocialTab tab)
    {
        if (_isOpen && currentTab == tab) Close();
        else OpenOnTab(tab);
    }

    /// <summary>Ouvre directement sur la conversation DM d'un joueur — appelé par ChatUI quand on
    /// clique le nom d'un expéditeur dans le dock global (raccourci "MP", Florian 2026-10-05).
    /// Crée la conversation si elle n'existe pas encore (toujours quelque chose à afficher).</summary>
    public void OpenDMWith(string playerName)
    {
        if (string.IsNullOrEmpty(playerName)) return;
        DirectMessageSystem.Instance?.GetOrCreateConversation(playerName);
        _selectedConversation = playerName;

        _isOpen = true;
        if (socialPanel != null) socialPanel.SetActive(true);
        SwitchTab(SocialTab.Chat);
    }

    public void Close()
    {
        _isOpen    = false;
        currentTab = SocialTab.None;
        if (socialPanel != null) socialPanel.SetActive(false);
    }

    public bool IsOpen => _isOpen;

    // =========================================================
    // SWITCH ONGLET
    // =========================================================
    private void SwitchTab(SocialTab tab)
    {
        currentTab = tab;

        // Active le bon sous-panel, désactive les autres
        if (chatPanel  != null) chatPanel .SetActive(tab == SocialTab.Chat);
        if (guildPanel != null) guildPanel.SetActive(tab == SocialTab.Guild);
        if (mailsPanel != null) mailsPanel.SetActive(tab == SocialTab.Mail);

        // Visuels onglets
        if (chatTab  != null) chatTab .image.color = tab == SocialTab.Chat  ? TAB_ACTIVE : TAB_INACTIVE;
        if (guildTab != null) guildTab.image.color = tab == SocialTab.Guild ? TAB_ACTIVE : TAB_INACTIVE;
        if (mailTab  != null) mailTab .image.color = tab == SocialTab.Mail  ? TAB_ACTIVE : TAB_INACTIVE;

        // Refresh contenu si Mail
        if (tab == SocialTab.Mail)
        {
            RefreshMailList();
            ClearMailDetail();
        }

        if (tab == SocialTab.Chat)
        {
            RefreshConversationList();
            RefreshThread();
        }
    }

    // =========================================================
    // DM (CHAT PRIVÉ PAR JOUEUR)
    // =========================================================

    /// <summary>Liste des conversations, triée alphabétiquement — pas d'ordre "dernier message"
    /// pour l'instant (pas de timestamp sur ChatLine), amélioration possible plus tard.</summary>
    private void RefreshConversationList()
    {
        if (conversationContent == null) return;

        foreach (Transform child in conversationContent)
            Destroy(child.gameObject);

        var conversations = DirectMessageSystem.Instance?.GetConversations();
        if (conversations == null || conversationEntryPrefab == null) return;

        foreach (var conv in conversations)
        {
            var go = Instantiate(conversationEntryPrefab, conversationContent);

            var nameText = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (nameText != null) nameText.text = conv.otherPlayerName;

            var lastMsgText = go.transform.Find("LastMessage")?.GetComponent<TextMeshProUGUI>();
            if (lastMsgText != null)
                lastMsgText.text = conv.messages.Count > 0 ? conv.messages[conv.messages.Count - 1].text : "";

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                string captured = conv.otherPlayerName;
                btn.onClick.AddListener(() => SelectConversation(captured));
            }
        }
    }

    private void SelectConversation(string playerName)
    {
        _selectedConversation = playerName;
        RefreshThread();
    }

    private void RefreshThread()
    {
        if (threadHeaderName != null)
            threadHeaderName.text = _selectedConversation ?? "";

        if (threadContent == null) return;

        foreach (Transform child in threadContent)
            Destroy(child.gameObject);

        if (string.IsNullOrEmpty(_selectedConversation)) return;

        var conv = DirectMessageSystem.Instance?.GetOrCreateConversation(_selectedConversation);
        if (conv == null || threadLinePrefab == null) return;

        foreach (var line in conv.messages)
        {
            var go = Instantiate(threadLinePrefab, threadContent);

            string timeStr = line.timestamp.ToString("HH:mm");

            // Même pattern que ChatUI.Refresh — champ "Time" dédié optionnel (TMP nommé "Time"),
            // sinon l'heure est préfixée dans le texte principal (Florian a réutilisé
            // ChatLinePrefab comme ThreadLinePrefab, qui a déjà ce champ, 2026-10-05).
            var timeTxt = go.transform.Find("Time")?.GetComponent<TextMeshProUGUI>();
            if (timeTxt != null) timeTxt.text = timeStr;

            TextMeshProUGUI txt = null;
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (t == timeTxt) continue;
                txt = t;
                break;
            }
            if (txt == null) continue;

            string timePrefix = timeTxt == null ? $"{timeStr} " : "";
            txt.text = $"{timePrefix}{line.sender} : {line.text}";
        }
    }

    private void OnThreadSendClicked()
    {
        if (threadInput == null || string.IsNullOrWhiteSpace(threadInput.text)) return;
        if (string.IsNullOrEmpty(_selectedConversation)) return;
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;

        string text = threadInput.text;

        // Symétrie avec le raccourci "/Nom message" du dock global (ChatUI.OnSendClicked) — un MP
        // envoyé DEPUIS SocialUI doit aussi apparaître dans le flux unifié (tagué Private,
        // filtrable), pas seulement dans la conversation DM. Avant ce fix, seul le sens
        // dock→SocialUI postait aux deux endroits ; SocialUI→dock ne postait qu'à
        // DirectMessageSystem (Florian, 2026-10-05 : "le chat ne reçoit pas les messages du
        // socialUI").
        ChatSystem.Instance?.TrySendPlayerMessage(ChatChannel.Private, text, _player, _selectedConversation);

        bool sent = DirectMessageSystem.Instance != null
            && DirectMessageSystem.Instance.SendDM(_selectedConversation, text, _player);
        if (sent) threadInput.text = "";
    }

    /// <summary>Supprime la conversation entière (fil + entrée dans la liste) — pas juste un
    /// message. Confirmation via le popup partagé ConfirmationUI (même pattern que l'abandon de
    /// quête, QuestJournalUI) avant toute suppression réelle.</summary>
    private void OnDeleteConversationClicked()
    {
        if (string.IsNullOrEmpty(_selectedConversation)) return;

        string captured = _selectedConversation;
        ConfirmationUI.Instance.OpenConfirmFlow("Supprimer",
            $"Supprimer la conversation avec {captured} ? L'historique sera perdu.",
            _ => ConfirmDeleteConversation(captured));
    }

    /// <summary>conversation passée explicitement (pas relue depuis _selectedConversation) — la
    /// popup peut rester ouverte un moment, _selectedConversation pourrait changer entre-temps si
    /// le joueur clique une autre conversation dans la liste avant de confirmer.</summary>
    private void ConfirmDeleteConversation(string playerName)
    {
        DirectMessageSystem.Instance?.DeleteConversation(playerName);
        if (_selectedConversation == playerName) _selectedConversation = null;

        RefreshConversationList();
        RefreshThread();
    }

    /// <summary>Appelé par DirectMessageSystem après un SendDM — rafraîchit la liste (dernier
    /// message) et le fil si c'est la conversation actuellement affichée. Vérifie la visibilité
    /// RÉELLE du panel (activeInHierarchy) plutôt que _isOpen seul — _isOpen ne se met à jour que
    /// via OpenOnTab/ToggleTab/Close, et peut désynchroniser si le panel est ouvert par un autre
    /// chemin (ex: un bouton HUD générique qui fait juste SetActive) — trouvé après le rapport de
    /// Florian "le socialUI ne se refresh pas lors d'ajout de message", 2026-10-05.</summary>
    public void RefreshDMIfOpen(string playerName)
    {
        bool panelVisible = socialPanel != null && socialPanel.activeInHierarchy;
        if (!panelVisible || currentTab != SocialTab.Chat) return;
        RefreshConversationList();
        if (_selectedConversation == playerName) RefreshThread();
    }

    // =========================================================
    // CALLBACKS MAILBOXSYSTEM
    // =========================================================

    /// <summary>Appelé par MailboxSystem quand un nouveau mail arrive.</summary>
    public void OnNewMail(MailMessage mail)
    {
        if (_isOpen && currentTab == SocialTab.Mail) RefreshMailList();
        Debug.Log($"[SOCIALUI] Nouveau mail : {mail.subject}");
        // TODO : badge notification sur le bouton Mail du HUD
    }

    /// <summary>Appelé après ClaimReward.</summary>
    public void RefreshMail(MailMessage mail)
    {
        if (!_isOpen || currentTab != SocialTab.Mail) return;
        RefreshMailList();
        if (selectedMail?.mailID == mail.mailID) ShowMailDetail(mail);
    }

    // =========================================================
    // FILTRE MAILS
    // =========================================================
    private void SetFilter(MailFilter filter)
    {
        currentFilter = filter;
        RefreshFilterVisuals();
        RefreshMailList();
        ClearMailDetail();
    }

    private void RefreshFilterVisuals()
    {
        if (filterAll    != null) filterAll   .image.color = currentFilter == MailFilter.All    ? FILTER_ACTIVE : FILTER_INACTIVE;
        if (filterUnread != null) filterUnread.image.color = currentFilter == MailFilter.Unread ? FILTER_ACTIVE : FILTER_INACTIVE;
        if (filterSystem != null) filterSystem.image.color = currentFilter == MailFilter.System ? FILTER_ACTIVE : FILTER_INACTIVE;
        if (filterPlayer != null) filterPlayer.image.color = currentFilter == MailFilter.Player ? FILTER_ACTIVE : FILTER_INACTIVE;
    }

    // =========================================================
    // LISTE MAILS
    // =========================================================
    private void RefreshMailList()
    {
        if (mailContent == null || entryMailPrefab == null) return;

        foreach (Transform child in mailContent)
            Destroy(child.gameObject);

        var mails = MailboxSystem.Instance?.GetAllMails();
        if (mails == null) return;

        foreach (var mail in mails)
        {
            if (!MatchesFilter(mail)) continue;
            SpawnMailEntry(mail);
        }
    }

    private bool MatchesFilter(MailMessage mail)
    {
        return currentFilter switch
        {
            MailFilter.Unread => !mail.isRead,
            MailFilter.System => mail.isFromServer,
            MailFilter.Player => !mail.isFromServer,
            _                 => true
        };
    }

    private void SpawnMailEntry(MailMessage mail)
    {
        var go = Instantiate(entryMailPrefab, mailContent);
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) { rt.anchoredPosition = Vector2.zero; rt.localScale = Vector3.one; }

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            // 2 lignes dans le même champ (pas de champ dédié — expéditeur en petit/teinté,
            // sujet en dessous) plutôt qu'une seule ligne compacte qui wrap n'importe où.
            string unreadDot = mail.isRead ? "" : "<color=#4CDB57>●</color> ";
            string sender    = mail.isFromServer ? "Système" : mail.senderName;
            string rewardTag = mail.CanClaim ? "  <color=#FFD966>[+]</color>" : "";

            tmp.richText = true;
            tmp.text  = $"{unreadDot}<size=80%><color=#A9A0C9>{sender}</color></size>\n{mail.subject}{rewardTag}";
            tmp.color = mail.isRead ? new Color(0.6f, 0.55f, 0.75f) : Color.white;
        }

        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            var captured = mail;
            btn.onClick.AddListener(() => OnMailEntryClicked(captured));
        }
    }

    // =========================================================
    // SÉLECTION MAIL
    // =========================================================
    private void OnMailEntryClicked(MailMessage mail)
    {
        selectedMail = mail;
        MailboxSystem.Instance?.MarkAsRead(mail.mailID);
        ShowMailDetail(mail);
        RefreshMailList();
    }

    // =========================================================
    // DÉTAIL MAIL
    // =========================================================
    private void ShowMailDetail(MailMessage mail)
    {
        if (mail == null) { ClearMailDetail(); return; }

        if (detailSubject  != null) detailSubject.text  = mail.subject;
        if (detailFrom     != null) detailFrom.text     = mail.isFromServer ? "Serveur AetherTree" : mail.senderName;
        if (detailDate     != null) detailDate.text     = mail.SentDateString;
        if (detailBodyText != null) detailBodyText.text = mail.body;

        bool hasReward = mail.HasReward;
        if (rewardBox != null) rewardBox.SetActive(hasReward);

        if (hasReward)
        {
            if (rewardTitle != null)
                rewardTitle.text = mail.rewardClaimed ? "Récompense récupérée" : "Pièces jointes";

            PopulateRewardItems(mail.reward);

            if (claimButton != null)
            {
                claimButton.interactable = mail.CanClaim;
                var txt = claimButton.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = mail.rewardClaimed ? ">> Recupere" : ">> Recuperer";
            }
        }

        if (replyButton  != null) replyButton.interactable  = !mail.isFromServer;
        if (deleteButton != null) deleteButton.interactable = !mail.CanClaim;
    }

    private void PopulateRewardItems(MailReward reward)
    {
        if (rewardItemContainer == null) return;
        foreach (Transform child in rewardItemContainer) Destroy(child.gameObject);
        if (reward == null) return;

        // GetDisplayName() et GetIcon() centralisent la logique d'affichage
        // pour tous les RewardType — plus besoin de switch ici.
        string desc      = reward.GetDisplayName();
        Sprite rewardIcon = reward.GetIcon();

        if (rewardItemPrefab != null)
        {
            var go  = Instantiate(rewardItemPrefab, rewardItemContainer);
            var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = desc;

            // Icône selon le type
            var icon = go.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null && rewardIcon != null)
                icon.sprite = rewardIcon;

            // Tooltip au survol — TooltipTrigger a besoin qu'on lui passe la donnée
            // (SetItem/SetSkill), sinon OnPointerEnter n'affiche jamais rien.
            var tooltip = go.GetComponentInChildren<TooltipTrigger>();
            if (tooltip != null) SetRewardTooltip(tooltip, reward);
        }
        else
        {
            // Fallback sans prefab
            var go  = new GameObject("RewardDesc", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(rewardItemContainer, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text     = desc;
            tmp.fontSize = 13;
            tmp.color    = new Color(0.85f, 0.75f, 1f);
        }
    }

    /// <summary>Alimente le TooltipTrigger de l'icône selon le rewardType — Skill a son propre
    /// tooltip dédié, tout le reste (équipement générique, ressource, consommable, recette)
    /// passe par un InventoryItem jetable construit juste pour la preview (jamais ajouté à
    /// l'inventaire). Title/Pet/Other n'ont pas de SO à prévisualiser — tooltip reste inactif.</summary>
    private void SetRewardTooltip(TooltipTrigger tooltip, MailReward reward)
    {
        switch (reward.rewardType)
        {
            case RewardType.Skill:
            case RewardType.SkillAndTitle:
                if (reward.rewardSkill != null) tooltip.SetSkill(reward.rewardSkill);
                break;

            case RewardType.Recipe:
                if (reward.rewardRecipe?.result != null)
                {
                    var previewItem = BuildPreviewItem(reward.rewardRecipe.result, reward.rewardRecipe.resultQuantity);
                    if (previewItem != null) tooltip.SetItem(previewItem);
                }
                break;

            case RewardType.Resource:
                if (reward.rewardResource != null)
                    tooltip.SetItem(BuildPreviewItem(reward.rewardResource, reward.rewardResourceQuantity));
                break;

            case RewardType.Consumable:
                if (reward.rewardConsumable != null)
                    tooltip.SetItem(BuildPreviewItem(reward.rewardConsumable, reward.rewardConsumableQuantity));
                break;

            default:
                if (reward.rewardEquipment is ItemData equip)
                {
                    var previewItem = BuildPreviewItem(equip);
                    if (previewItem != null) tooltip.SetItem(previewItem);
                }
                break;
        }
    }

    /// <summary>Instance jetable (jamais ajoutée à l'inventaire) juste pour le tooltip —
    /// même dispatch par sous-type que CraftSystem.ResolveCraft/ShopUI.ResolveItemFromEntry.</summary>
    private InventoryItem BuildPreviewItem(ItemData item, int quantity = 1) => item switch
    {
        WeaponData wd        => new InventoryItem(wd.CreateDropInstance()),
        ArmorData ad         => new InventoryItem(ad.CreateDropInstance()),
        HelmetData hd        => new InventoryItem(hd.CreateInstance()),
        GlovesData gd        => new InventoryItem(gd.CreateInstance()),
        BootsData bd         => new InventoryItem(bd.CreateInstance()),
        JewelryData jd       => new InventoryItem(jd.CreateInstance()),
        SpiritData sd        => new InventoryItem(new SpiritInstance(sd)),
        CosmeticDataHead chd => new InventoryItem(chd.CreateInstance()),
        CosmeticDataBody cbd => new InventoryItem(cbd.CreateInstance()),
        TalismanData tad     => new InventoryItem(tad.CreateInstance()),
        ResourceData rd      => new InventoryItem(rd.CreateInstance(quantity)),
        ConsumableData cd    => new InventoryItem(cd.CreateInstance(quantity)),
        _                    => null,
    };

    private void ClearMailDetail()
    {
        selectedMail = null;
        if (detailSubject  != null) detailSubject.text  = "";
        if (detailFrom     != null) detailFrom.text     = "";
        if (detailDate     != null) detailDate.text     = "";
        if (detailBodyText != null) detailBodyText.text = "";
        if (rewardBox      != null) rewardBox.SetActive(false);
        if (replyButton    != null) replyButton.interactable  = false;
        if (deleteButton   != null) deleteButton.interactable = false;
    }

    // =========================================================
    // ACTIONS
    // =========================================================
    private void OnClaimClicked()
    {
        if (selectedMail == null) return;
        MailboxSystem.Instance?.ClaimReward(selectedMail.mailID);
    }

    private void OnDeleteClicked()
    {
        if (selectedMail == null) return;
        MailboxSystem.Instance?.DeleteMail(selectedMail.mailID);
        ClearMailDetail();
        RefreshMailList();
    }

    private void OnReplyClicked()
    {
        Debug.Log($"[SOCIALUI] Répondre à {selectedMail?.senderName} — à implémenter Phase 2");
    }
}