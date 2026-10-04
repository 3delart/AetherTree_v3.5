using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// PNJQUESTBOARDUI — Panel de quêtes d'un PNJ (grille de cartes)
// Path : Assets/Scripts/UI/PanelSecondaire/PNJQuestBoardUI.cs
// AetherTree GDD v31 — §25
//
// Ouvert via DialogueAction.OpenQuestLog (PNJType.Quest — voir PNJ.cs
// HandleDialogueAction) — panel INDÉPENDANT de PNJWindowUI (qui ne gère que
// les PNJ Merchant/shopSpecialty, voir PNJTypeExtensions.GetTabs()).
//
// Une carte par quête de pnjData.availableQuests :
//   - TurnedIn déjà rendue → pas affichée (plus rien à offrir ce PNJ dessus).
//   - questRank == Secret ET pas encore accessible (CanAccept faux, état
//     None) → carte verrouillée, texte "Secret" (ne révèle PAS le vrai
//     prérequis). Dès que CanAccept devient vrai, redevient une carte
//     normale comme les autres (aucune marque distinctive après déblocage).
//   - Autre quête verrouillée (état None, CanAccept faux) → carte grisée +
//     cadenas + RequirementSet.DescribeFirstUnmet() (ex: "Niveau 15 requis").
//   - Sinon → carte normale, cliquable, badge selon l'état (Disponible/
//     En cours/À rendre).
//
// Construction de la grille/détail laissée à Florian (Hierarchy + prefabs
// dans l'Editor) — ce script ne fait que les Find() sur les enfants nommés
// du prefab de carte (voir noms exacts ci-dessous) et peupler les champs du
// détail déjà câblés en Inspector.
//
// Prefab de carte (questCardPrefab) attendu :
//   QuestCard (racine, avec un Button)
//     ├── RankLabel        (TextMeshProUGUI)
//     ├── QuestName        (TextMeshProUGUI)
//     ├── StatusBadge      (GameObject — Image pill)
//     │     └── BadgeText  (TextMeshProUGUI, enfant de StatusBadge)
//     ├── LockIcon         (GameObject — Image cadenas)
//     └── RequirementText  (TextMeshProUGUI)
// =============================================================

public class PNJQuestBoardUI : MonoBehaviour
{
    public static PNJQuestBoardUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public Image            iconImage;
    public TextMeshProUGUI  initialsText;
    public TextMeshProUGUI  titleText;
    public TextMeshProUGUI  subtitleText;
    public Button           closeButton;

    [Header("Grille")]
    public Transform  questGrid;
    public GameObject questCardPrefab;

    [Header("Couleurs — badge statut")]
    public Color colorDisponible = new Color(0.35f, 0.75f, 0.65f);
    public Color colorEnCours    = new Color(0.75f, 0.6f,  0.35f);
    public Color colorARendre    = new Color(0.45f, 0.8f,  0.45f);
    public Color colorVerrouille = new Color(0.4f,  0.4f,  0.45f);

    [Header("Détail")]
    public GameObject       detailModal;
    public TextMeshProUGUI  detailQuestName;
    public TextMeshProUGUI  detailRank;
    public TextMeshProUGUI  detailDescription;
    public TextMeshProUGUI  detailObjectives;
    public TextMeshProUGUI  detailXpReward;
    public TextMeshProUGUI  detailAerisReward;
    public Transform        detailRewardsContainer;
    public GameObject       detailRewardItemEntryPrefab; // Icon + Label, même patron que QuestJournalUI
    public Button            detailActionButton;
    public TextMeshProUGUI   detailActionButtonText;
    public Button            detailCloseButton;

    // ── État interne ──────────────────────────────────────────
    private PNJData  _pnjData;
    private Player   _player;
    private QuestData _selectedQuest;

    private readonly List<GameObject> _cardObjects   = new List<GameObject>();
    private readonly List<GameObject> _rewardEntries = new List<GameObject>();

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        detailCloseButton?.onClick.AddListener(CloseDetail);
        Close();
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(PNJData pnjData, Player player)
    {
        if (pnjData == null || player == null) return;
        _pnjData = pnjData;
        _player  = player;

        if (panel != null) panel.SetActive(true);

        if (titleText != null) titleText.text = $"Quêtes de {pnjData.pnjName}";

        if (iconImage != null)
        {
            iconImage.sprite  = pnjData.portrait;
            iconImage.enabled = pnjData.portrait != null;
        }
        if (initialsText != null)
            initialsText.text = pnjData.portrait == null ? Initials(pnjData.pnjName) : "";

        CloseDetail();
        RefreshGrid();
    }

    public void Close()
    {
        CloseDetail();
        if (panel != null) panel.SetActive(false);
        _pnjData = null;
        _player  = null;
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, Mathf.Min(2, parts[0].Length)).ToUpper();
        return (parts[0][0].ToString() + parts[1][0]).ToUpper();
    }

    // =========================================================
    // GRILLE
    // =========================================================

    private void RefreshGrid()
    {
        foreach (var go in _cardObjects)
            if (go != null) Destroy(go);
        _cardObjects.Clear();

        if (_pnjData?.availableQuests == null || questGrid == null || questCardPrefab == null) return;

        int disponible = 0, enCours = 0, aRendre = 0;

        foreach (var quest in _pnjData.availableQuests)
        {
            if (quest == null || QuestSystem.Instance == null) continue;

            QuestState state      = QuestSystem.Instance.GetQuestState(quest);
            if (state == QuestState.TurnedIn) continue; // plus rien à offrir sur ce PNJ

            bool canAccept = QuestSystem.Instance.CanAccept(quest, _player);
            bool isLocked  = state == QuestState.None && !canAccept;

            if (state == QuestState.Active)     enCours++;
            else if (state == QuestState.Completed) aRendre++;
            else if (!isLocked)                 disponible++;

            SpawnCard(quest, state, isLocked);
        }

        if (subtitleText != null)
            subtitleText.text = $"{disponible} disponible(s) · {enCours} en cours · {aRendre} à rendre";
    }

    private void SpawnCard(QuestData quest, QuestState state, bool isLocked)
    {
        var go = Instantiate(questCardPrefab, questGrid);
        _cardObjects.Add(go);

        bool isSecretLocked = isLocked && quest.questRank == QuestRank.Secret;

        var rankLabel = go.transform.Find("RankLabel")?.GetComponent<TextMeshProUGUI>();
        if (rankLabel != null) rankLabel.text = RankLabel(quest.questRank);

        // Secret verrouillée : ni le nom ni le vrai rang ne doivent spoiler la quête — seul
        // "Secrète"/"???" apparaît, tant que CanAccept est faux.
        var nameText = go.transform.Find("QuestName")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null) nameText.text = isSecretLocked ? "???" : quest.questName;

        var statusBadge = go.transform.Find("StatusBadge")?.gameObject;
        var badgeText    = statusBadge?.transform.Find("BadgeText")?.GetComponent<TextMeshProUGUI>();
        var lockIcon     = go.transform.Find("LockIcon")?.gameObject;
        var reqText      = go.transform.Find("RequirementText")?.GetComponent<TextMeshProUGUI>();

        if (isLocked)
        {
            statusBadge?.SetActive(false);
            lockIcon?.SetActive(true);
            if (reqText != null)
            {
                reqText.gameObject.SetActive(true);
                reqText.text = isSecretLocked ? "Secret" : quest.requirements.DescribeFirstUnmet(_player);
            }
        }
        else
        {
            lockIcon?.SetActive(false);
            reqText?.gameObject.SetActive(false);
            statusBadge?.SetActive(true);
            if (badgeText != null)
            {
                badgeText.text = state switch
                {
                    QuestState.Active    => "En cours",
                    QuestState.Completed => "À rendre",
                    _                    => "Disponible",
                };
            }
            var badgeImg = statusBadge?.GetComponent<Image>();
            if (badgeImg != null)
                badgeImg.color = state switch
                {
                    QuestState.Active    => colorEnCours,
                    QuestState.Completed => colorARendre,
                    _                    => colorDisponible,
                };
        }

        var button = go.GetComponent<Button>();
        if (button != null)
        {
            button.interactable = !isLocked;
            var captured = quest;
            button.onClick.RemoveAllListeners();
            if (!isLocked) button.onClick.AddListener(() => OpenDetail(captured));
        }
    }

    // =========================================================
    // DÉTAIL
    // =========================================================

    private void OpenDetail(QuestData quest)
    {
        if (quest == null || QuestSystem.Instance == null) return;
        _selectedQuest = quest;

        if (detailModal != null) detailModal.SetActive(true);
        if (detailQuestName  != null) detailQuestName.text  = quest.questName;
        if (detailRank       != null) detailRank.text       = RankLabel(quest.questRank);
        if (detailDescription != null)
            detailDescription.text = !string.IsNullOrEmpty(quest.description) ? quest.description : "Aucune description.";

        if (detailObjectives != null)
        {
            var sb = new System.Text.StringBuilder();
            if (quest.objectives != null)
                foreach (var obj in quest.objectives)
                    sb.AppendLine($"• {obj.description}");
            detailObjectives.text = sb.ToString().TrimEnd();
        }

        if (detailXpReward    != null) detailXpReward.text    = quest.xpReward    > 0 ? $"+{quest.xpReward} XP" : "—";
        if (detailAerisReward != null) detailAerisReward.text = quest.aerisReward > 0 ? $"+{quest.aerisReward} ¤" : "—";

        RefreshDetailRewards(quest);
        RefreshDetailAction(quest);
    }

    private void RefreshDetailRewards(QuestData quest)
    {
        foreach (var go in _rewardEntries)
            if (go != null) Destroy(go);
        _rewardEntries.Clear();

        if (detailRewardsContainer == null || quest.rewardItems == null) return;

        foreach (var reward in quest.rewardItems)
        {
            if (reward == null) continue;
            string displayName = reward.DisplayName;
            if (string.IsNullOrEmpty(displayName)) continue;

            if (detailRewardItemEntryPrefab != null)
            {
                var go    = Instantiate(detailRewardItemEntryPrefab, detailRewardsContainer);
                var img   = go.transform.Find("Icon") ?.GetComponent<Image>();
                var label = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();

                if (img != null)
                {
                    img.sprite  = reward.GetIcon();
                    img.enabled = img.sprite != null;
                }
                if (label != null) label.text = displayName;

                _rewardEntries.Add(go);
            }
        }
    }

    private void RefreshDetailAction(QuestData quest)
    {
        if (detailActionButton == null) return;

        QuestState state = QuestSystem.Instance.GetQuestState(quest);
        detailActionButton.onClick.RemoveAllListeners();

        switch (state)
        {
            case QuestState.None:
                detailActionButton.interactable = true;
                if (detailActionButtonText != null) detailActionButtonText.text = "Accepter";
                detailActionButton.onClick.AddListener(OnAcceptClicked);
                break;

            case QuestState.Completed:
                detailActionButton.interactable = true;
                if (detailActionButtonText != null) detailActionButtonText.text = "Récupérer";
                detailActionButton.onClick.AddListener(OnTurnInClicked);
                break;

            default: // Active — rien à faire depuis ce panel, le joueur doit avancer en jeu
                detailActionButton.interactable = false;
                if (detailActionButtonText != null) detailActionButtonText.text = "En cours";
                break;
        }
    }

    private void OnAcceptClicked()
    {
        if (_selectedQuest == null || _player == null) return;
        if (QuestSystem.Instance.AcceptQuest(_selectedQuest, _player))
        {
            CloseDetail();
            RefreshGrid();
        }
    }

    private void OnTurnInClicked()
    {
        if (_selectedQuest == null || _player == null) return;
        if (QuestSystem.Instance.TurnInQuest(_selectedQuest, _player))
        {
            CloseDetail();
            RefreshGrid();
        }
    }

    private void CloseDetail()
    {
        _selectedQuest = null;
        if (detailModal != null) detailModal.SetActive(false);

        foreach (var go in _rewardEntries)
            if (go != null) Destroy(go);
        _rewardEntries.Clear();
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private static string RankLabel(QuestRank rank) => rank switch
    {
        QuestRank.Main      => "Principale",
        QuestRank.Secondary => "Secondaire",
        QuestRank.Daily     => "Quotidienne",
        QuestRank.Guild     => "Guilde",
        QuestRank.Event     => "Événement",
        QuestRank.Secret    => "Secrète",
        _                   => ""
    };
}
