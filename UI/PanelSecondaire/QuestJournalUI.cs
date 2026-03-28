using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Text;

// =============================================================
// QUESTJOURNALUI — Journal des quêtes (touche J)
// Path : Assets/Scripts/UI/Panels/QuestJournalUI.cs
// AetherTree GDD v31 — §25
//
// Prefab rewardItemEntryPrefab attendu :
//   RewardItemEntry
//     ├── Icon   (Image)             ← icône du SO
//     └── Label  (TextMeshProUGUI)   ← nom + quantité
// =============================================================

public class QuestJournalUI : MonoBehaviour
{
    public static QuestJournalUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;
    public Button     closeButton;

    [Header("Liste des quêtes — PanelLeft")]
    public Transform  questListContent;
    public GameObject questEntryPrefab;

    [Header("Détail — PanelRight")]
    public Transform  detailParent;
    public GameObject questDetailPrefab;

    [Header("Récompenses items — Prefab")]
    [Tooltip("Prefab avec Image 'Icon' + TextMeshProUGUI 'Label'.\nInstancié pour chaque item récompense.")]
    public GameObject rewardItemEntryPrefab;

    // ── État interne ──────────────────────────────────────────
    private bool      _isOpen        = false;
    private QuestData _selectedQuest = null;

    private GameObject      _currentDetailInstance = null;
    private TextMeshProUGUI _detailName;
    private TextMeshProUGUI _detailRank;
    private TextMeshProUGUI _detailDescription;
    private TextMeshProUGUI _objectifsText;
    private TextMeshProUGUI _xpRewardsText;
    private TextMeshProUGUI _aerisRewardsText;

    // Container dans lequel on instancie les rewardItemEntryPrefab
    private Transform _itemsRewardsContainer;

    // Entrées instanciées — pour les détruire au refresh
    private readonly List<GameObject>  _rewardItemEntries = new List<GameObject>();
    private readonly List<GameObject>  _entryObjects      = new List<GameObject>();

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        EnsureRaycastBlocker();
        GameEventBus.OnQuestAction += OnQuestAction;
        GameEventBus.OnMobKilled   += OnMobKilled;
    }

    private void OnDestroy()
    {
        GameEventBus.OnQuestAction -= OnQuestAction;
        GameEventBus.OnMobKilled   -= OnMobKilled;
    }

    private void EnsureRaycastBlocker()
    {
        if (panel == null) return;
        var img = panel.GetComponent<Image>();
        if (img == null) { img = panel.AddComponent<Image>(); img.color = new Color(0,0,0,0); }
        img.raycastTarget = true;
    }

    // =========================================================
    // EVENTS
    // =========================================================

    private void OnQuestAction(QuestEvent e)
    {
        if (!_isOpen) return;
        RefreshList();
        if (_selectedQuest != null && e.quest == _selectedQuest)
            ShowDetail(_selectedQuest);
    }

    private void OnMobKilled(MobKilledEvent e)
    {
        if (!_isOpen || _selectedQuest == null) return;
        RefreshObjectifsText(_selectedQuest);
    }

    // =========================================================
    // OPEN / CLOSE / TOGGLE
    // =========================================================

    public void Open()
    {
        _isOpen = true;
        if (panel != null) panel.SetActive(true);
        RefreshList();
        if (_selectedQuest != null &&
            QuestSystem.Instance?.GetQuestState(_selectedQuest) == QuestState.Active)
            ShowDetail(_selectedQuest);
        else
            ClearDetail();
    }

    public void Close()
    {
        _isOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    public void Toggle()
    {
        if (_isOpen) Close(); else Open();
    }

    // =========================================================
    // LISTE
    // =========================================================

    private void RefreshList()
    {
        foreach (var go in _entryObjects)
            if (go != null) Destroy(go);
        _entryObjects.Clear();

        if (QuestSystem.Instance == null || questListContent == null || questEntryPrefab == null)
            return;

        var quests = new List<QuestData>();
        quests.AddRange(QuestSystem.Instance.GetActiveQuests());
        quests.AddRange(QuestSystem.Instance.GetCompletedQuests());

        if (quests.Count == 0) { SpawnEmptyMessage("Aucune quête en cours."); return; }

        foreach (var quest in quests)
            SpawnQuestEntry(quest);
    }

    private void SpawnEmptyMessage(string msg)
    {
        var go  = new GameObject("EmptyMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(questListContent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = msg; tmp.fontSize = 35;
        tmp.color = new Color(0.5f, 0.5f, 0.6f);
        tmp.alignment = TextAlignmentOptions.Center;
        _entryObjects.Add(go);
    }

    private void SpawnQuestEntry(QuestData quest)
    {
        if (quest == null || questEntryPrefab == null) return;

        var go = Instantiate(questEntryPrefab, questListContent);
        _entryObjects.Add(go);

        // ── RankColor ─────────────────────────────────────────────
        var rankColorImg = go.transform.Find("RankColor")?.GetComponent<Image>();
        if (rankColorImg != null)
            rankColorImg.color = RankColor(quest.questRank, 1f);

        // ── QuestName ─────────────────────────────────────────────
        var nameText = go.transform.Find("QuestName")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
            nameText.text = quest.questName;

        // ── QuestRank ─────────────────────────────────────────────
        var rankText = go.transform.Find("QuestRank")?.GetComponent<TextMeshProUGUI>();
        if (rankText != null)
        {
            rankText.text  = RankLabel(quest.questRank);
            rankText.color = RankColor(quest.questRank, 0.85f);
        }

        // ── Fond — surlignage si sélectionnée ────────────────────
        var rootImg = go.GetComponent<Image>();
        if (rootImg != null)
            rootImg.color = (_selectedQuest == quest)
                ? new Color(0.30f, 0.20f, 0.55f, 1f)
                : new Color(0.15f, 0.15f, 0.22f, 1f);

        var captured = quest;

        // ── ButtonSuivis ──────────────────────────────────────────
        var suiviBtn = go.transform.Find("ButtonSuivis")?.GetComponent<Button>();
        if (suiviBtn != null)
        {
            bool tracked = QuestTrackerUI.Instance?.IsTracked(captured) ?? false;
            var suiviBtnImg = suiviBtn.GetComponent<Image>();
            if (suiviBtnImg != null)
                suiviBtnImg.color = tracked
                    ? new Color(0.35f, 0.75f, 0.35f)
                    : new Color(0.25f, 0.25f, 0.35f);

            suiviBtn.onClick.RemoveAllListeners();
            suiviBtn.onClick.AddListener(() => OnSuiviClicked(captured));
        }

        // ── rootBtn → affiche le détail ───────────────────────────
        var rootBtn = go.GetComponent<Button>();
        if (rootBtn != null)
        {
            rootBtn.onClick.RemoveAllListeners();
            rootBtn.onClick.AddListener(() => OnEntryClicked(captured));
        }

        // ── Indicateur complétée ──────────────────────────────────
        bool isComplete = QuestSystem.Instance?.GetQuestState(quest) == QuestState.Completed;
        if (nameText != null && isComplete)
            nameText.text = $"✓ {quest.questName}";
    }
    private void OnEntryClicked(QuestData quest) { _selectedQuest = quest; ShowDetail(quest); RefreshList(); }
    private void OnSuiviClicked(QuestData quest)
    {
        if (quest == null || QuestTrackerUI.Instance == null) return;
    
        bool isNowTracked = QuestTrackerUI.Instance.ToggleTracked(quest);
    
        // Met à jour le visuel du bouton dans la liste
        RefreshList();
    
        Debug.Log($"[QUEST] Suivi {quest.questName} : {(isNowTracked ? "activé ✓" : "désactivé")}");
    }

    // =========================================================
    // DÉTAIL
    // =========================================================

    private void ShowDetail(QuestData quest)
    {
        if (quest == null) { ClearDetail(); return; }

        if (_currentDetailInstance == null)
            InstantiateDetailPrefab();

        if (_currentDetailInstance == null) return;

        if (_detailName != null) { _detailName.text = quest.questName; _detailName.color = RankColor(quest.questRank, 1f); }
        if (_detailRank != null) { _detailRank.text = RankLabel(quest.questRank); _detailRank.color = RankColor(quest.questRank, 0.85f); }
        if (_detailDescription != null)
            _detailDescription.text = !string.IsNullOrEmpty(quest.description) ? quest.description : "Aucune description.";

        RefreshObjectifsText(quest);
        RefreshRewards(quest);
    }

    private void InstantiateDetailPrefab()
    {
        if (questDetailPrefab == null || detailParent == null) return;

        if (_currentDetailInstance != null) Destroy(_currentDetailInstance);

        _currentDetailInstance = Instantiate(questDetailPrefab, detailParent);
        var root = _currentDetailInstance.transform;

        var entete         = root.Find("QuestEntete");
        _detailName        = entete?.Find("QuestName")       ?.GetComponent<TextMeshProUGUI>();
        _detailRank        = entete?.Find("QuestRank")       ?.GetComponent<TextMeshProUGUI>();
        _detailDescription = entete?.Find("QuestDescription")?.GetComponent<TextMeshProUGUI>();

        _objectifsText = root.Find("QuestObjective")?.Find("QuestObjectifs")?.GetComponent<TextMeshProUGUI>();

        var rewards       = root.Find("QuestRewards");
        _xpRewardsText    = rewards?.Find("XPRewards")   ?.GetComponent<TextMeshProUGUI>();
        _aerisRewardsText = rewards?.Find("AerisRewards") ?.GetComponent<TextMeshProUGUI>();

        // Container des items — cherche ItemsRewards comme Transform parent
        _itemsRewardsContainer = rewards?.Find("ItemsRewards");

        // Fallback : si ItemsRewards est un TMP, on cherche son parent comme container
        if (_itemsRewardsContainer == null)
            _itemsRewardsContainer = rewards;
    }

    // =========================================================
    // OBJECTIFS
    // =========================================================

    private void RefreshObjectifsText(QuestData quest)
    {
        if (_objectifsText == null || quest?.objectives == null) return;

        var sb = new StringBuilder();
        foreach (var obj in quest.objectives)
        {
            string icon  = obj.IsComplete ? "<color=#4DE670>✓</color>" : "•";
            string color = obj.IsComplete ? "#99E699" : "#DADBEB";
            string line  = $"{icon} <color={color}>{obj.description}</color>";
            if (obj.requiredCount > 1)
            {
                string pc = obj.IsComplete ? "#4DE670" : "#AAAACC";
                line += $"  <color={pc}>{obj.ProgressLabel}</color>";
            }
            sb.AppendLine(line);
        }
        _objectifsText.text = sb.ToString().TrimEnd();
    }

    // =========================================================
    // RÉCOMPENSES
    // =========================================================

    private void RefreshRewards(QuestData quest)
    {
        // XP & Aeris — texte simple
        if (_xpRewardsText    != null) _xpRewardsText.text    = quest.xpReward    > 0 ? $"+{quest.xpReward} XP" : "—";
        if (_aerisRewardsText != null) _aerisRewardsText.text = quest.aerisReward > 0 ? $"+{quest.aerisReward} ¤" : "—";

        // Items — vide les anciennes entrées
        foreach (var go in _rewardItemEntries)
            if (go != null) Destroy(go);
        _rewardItemEntries.Clear();

        if (_itemsRewardsContainer == null) return;
        if (quest.rewardItems == null || quest.rewardItems.Count == 0) return;

        foreach (var reward in quest.rewardItems)
        {
            if (reward == null) continue;

            string displayName = reward.DisplayName;
            Sprite icon        = reward.GetIcon();

            if (string.IsNullOrEmpty(displayName)) continue;

            if (rewardItemEntryPrefab != null)
            {
                // Instancie le prefab
                var go    = Instantiate(rewardItemEntryPrefab, _itemsRewardsContainer);
                var img   = go.transform.Find("Icon") ?.GetComponent<Image>();
                var label = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();

                if (img != null)
                {
                    img.sprite  = icon;
                    img.enabled = icon != null;
                    img.color   = icon != null ? Color.white : new Color(0,0,0,0);
                }

                if (label != null)
                    label.text = $"<color=#FFD966>{displayName}</color>";

                _rewardItemEntries.Add(go);
            }
            else
            {
                // Fallback sans prefab : texte simple dans le container
                var go  = new GameObject("RewardItem", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(_itemsRewardsContainer, false);
                var tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.text     = $"<color=#FFD966>• {displayName}</color>";
                tmp.fontSize = 28;
                _rewardItemEntries.Add(go);
            }
        }
    }

    // =========================================================
    // CLEAR
    // =========================================================

    private void ClearDetail()
    {
        _selectedQuest = null;

        foreach (var go in _rewardItemEntries)
            if (go != null) Destroy(go);
        _rewardItemEntries.Clear();

        if (_currentDetailInstance != null)
        {
            Destroy(_currentDetailInstance);
            _currentDetailInstance = null;
        }

        _detailName = _detailRank = _detailDescription = _objectifsText = null;
        _xpRewardsText = _aerisRewardsText = null;
        _itemsRewardsContainer = null;
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
        QuestRank.Hidden    => "Cachée",
        _                   => ""
    };

    private static Color RankColor(QuestRank rank, float alpha) => rank switch
    {
        QuestRank.Main      => new Color(1.0f, 0.85f, 0.2f,  alpha),
        QuestRank.Secondary => new Color(0.7f, 0.85f, 1.0f,  alpha),
        QuestRank.Daily     => new Color(0.6f, 0.95f, 0.65f, alpha),
        QuestRank.Guild     => new Color(0.9f, 0.65f, 1.0f,  alpha),
        QuestRank.Event     => new Color(1.0f, 0.65f, 0.35f, alpha),
        QuestRank.Hidden    => new Color(0.7f, 0.7f,  0.75f, alpha),
        _                   => new Color(1f,   1f,    1f,    alpha)
    };
}