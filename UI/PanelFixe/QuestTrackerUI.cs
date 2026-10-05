using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// QUESTTRACKERUI — Tracker HUD permanent
// Path : Assets/Scripts/UI/PanelFixe/QuestTrackerUI.cs
// AetherTree GDD v3.1 — §25
// =============================================================

public class QuestTrackerUI : MonoBehaviour
{
    public static QuestTrackerUI Instance { get; private set; }

    [Header("Contenu")]
    public Transform  trackerContent;
    public GameObject questBlockPrefab;
    public GameObject objectiveLinePrefab;

    [Header("Bouton réduire / agrandir")]
    [Tooltip("Bouton QuestButtonReduc dans QuestTitlePanel")]
    public Button reduceButton;
    [Tooltip("Texte du bouton (optionnel — affiche − ou +)")]
    public TextMeshProUGUI reduceButtonText;

    [Header("Paramètres")]
    public int maxDisplayed = 3;

    [Header("Message vide (optionnel)")]
    public string emptyMessage = "";

    // État réduit
    private bool _isReduced = false;

    // Suivi manuel
    private readonly HashSet<string> _trackedQuestIDs = new HashSet<string>();

    private static readonly Color BULLET_DONE    = new Color(0.3f,  0.9f,  0.4f);
    private static readonly Color BULLET_PENDING = new Color(0.65f, 0.65f, 0.75f);

    private readonly List<GameObject> _blocks = new List<GameObject>();
    private bool _dirty       = true;
    private bool _initialized = false;

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        Subscribe();

        if (reduceButton != null)
            reduceButton.onClick.AddListener(ToggleReduce);

        UpdateReduceButtonText();
        StartCoroutine(InitDelayed());
    }

    // GameEventBus.Reset() (appelé à CHAQUE changement de scène, ex: entrée/sortie de donjon)
    // désabonne tout — sans Resubscribe() listé dans Reset(), ce tracker arrêtait de recevoir
    // OnQuestAction/OnMobKilled dès le premier changement de scène de la session, pour de bon
    // (jamais réabonné, trouvé en testant une quête DungeonComplete — Florian, 2026-10-05).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()
    {
        GameEventBus.OnQuestAction += OnQuestAction;
        GameEventBus.OnMobKilled   += OnMobKilled;
    }

    private void Unsubscribe()
    {
        GameEventBus.OnQuestAction -= OnQuestAction;
        GameEventBus.OnMobKilled   -= OnMobKilled;
    }

    private IEnumerator InitDelayed()
    {
        yield return null;
        yield return null;
        _initialized = true;
        _dirty = false;
        Refresh();
    }

    private void OnDestroy() => Unsubscribe();

    private void OnQuestAction(QuestEvent e)
    {
        if (e.quest != null &&
            (e.action == QuestAction.TurnedIn || e.action == QuestAction.Failed || e.action == QuestAction.Abandoned))
            _trackedQuestIDs.Remove(e.quest.questID);
        _dirty = true;
    }

    private void OnMobKilled(MobKilledEvent e) => _dirty = true;

    private void Update()
    {
        if (!_initialized || !_dirty) return;
        _dirty = false;
        Refresh();
    }

    // =========================================================
    // TOGGLE RÉDUIRE / AGRANDIR
    // =========================================================

    public void ToggleReduce()
    {
        _isReduced = !_isReduced;

        // Cache ou affiche tous les blocs — TrackerContent reste toujours actif
        foreach (var block in _blocks)
            if (block != null) block.SetActive(!_isReduced);

        UpdateReduceButtonText();
    }

    private void UpdateReduceButtonText()
    {
        if (reduceButtonText == null) return;
        reduceButtonText.text = _isReduced ? "+" : "−";
    }

    // =========================================================
    // API SUIVI
    // =========================================================

    public bool ToggleTracked(QuestData quest)
    {
        if (quest == null || string.IsNullOrEmpty(quest.questID)) return false;

        if (_trackedQuestIDs.Contains(quest.questID))
        {
            _trackedQuestIDs.Remove(quest.questID);
            _dirty = true;
            return false;
        }

        if (_trackedQuestIDs.Count >= maxDisplayed)
        {
            Debug.Log($"[QuestTracker] Maximum {maxDisplayed} quêtes suivies.");
            return false;
        }

        _trackedQuestIDs.Add(quest.questID);
        _dirty = true;
        return true;
    }

    public bool IsTracked(QuestData quest)
        => quest != null && _trackedQuestIDs.Contains(quest.questID);

    // =========================================================
    // REFRESH
    // =========================================================

    public void Refresh()
    {
        foreach (var go in _blocks)
            if (go != null) Destroy(go);
        _blocks.Clear();

        // TrackerContent toujours visible
        if (trackerContent != null && !trackerContent.gameObject.activeSelf)
            trackerContent.gameObject.SetActive(true);

        if (QuestSystem.Instance == null || questBlockPrefab == null)
        {
            if (!string.IsNullOrEmpty(emptyMessage)) SpawnEmptyMessage();
            return;
        }

        var toDisplay = GetQuestsToDisplay();

        if (toDisplay.Count == 0)
        {
            if (!string.IsNullOrEmpty(emptyMessage)) SpawnEmptyMessage();
            return;
        }

        int count = 0;
        foreach (var quest in toDisplay)
        {
            if (count >= maxDisplayed) break;
            if (quest == null) continue;
            SpawnQuestBlock(quest);
            count++;
        }

        // Applique l'état réduit aux nouveaux blocs
        if (_isReduced)
            foreach (var block in _blocks)
                if (block != null) block.SetActive(false);
    }

    public void ForceRefresh()
    {
        if (!_initialized) return;
        _dirty = false;
        Refresh();
    }

    // =========================================================
    // QUÊTES À AFFICHER
    // =========================================================

    private List<QuestData> GetQuestsToDisplay()
    {
        var allActive = QuestSystem.Instance.GetActiveQuests();
        if (allActive == null) return new List<QuestData>();

        if (_trackedQuestIDs.Count > 0)
        {
            var tracked = new List<QuestData>();
            foreach (var q in allActive)
                if (q != null && _trackedQuestIDs.Contains(q.questID))
                    tracked.Add(q);
            return tracked;
        }

        return allActive;
    }

    // =========================================================
    // SPAWN
    // =========================================================

    private void SpawnQuestBlock(QuestData quest)
    {
        if (quest == null) return;

        var block = Instantiate(questBlockPrefab, trackerContent);
        _blocks.Add(block);

        var nameText = block.transform.Find("QuestName")?.GetComponent<TextMeshProUGUI>()
                    ?? block.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text  = quest.questName;
            nameText.color = RankColor(quest.questRank);
        }

        if (objectiveLinePrefab == null || quest.objectives == null) return;

        var activeIndices = quest.GetActiveObjectiveIndices();
        for (int i = 0; i < quest.objectives.Count; i++)
        {
            var obj = quest.objectives[i];
            if (obj == null) continue;
            if (quest.objectivesInOrder && !activeIndices.Contains(i) && !obj.IsComplete) continue;
            SpawnObjectiveLine(block.transform, obj);
        }
    }

    private void SpawnObjectiveLine(Transform parent, QuestObjective obj)
    {
        if (objectiveLinePrefab == null) return;

        var line = Instantiate(objectiveLinePrefab, parent);

        var bullet = line.transform.Find("Bullet")?.GetComponent<Image>();
        if (bullet != null)
            bullet.color = obj.IsComplete ? BULLET_DONE : BULLET_PENDING;

        var txt = line.transform.Find("ObjectiveText")?.GetComponent<TextMeshProUGUI>()
               ?? line.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            string label = obj.description;
            if (obj.requiredCount > 1) label += $"  {obj.ProgressLabel}";
            txt.text  = label;
            txt.color = obj.IsComplete
                ? new Color(0.55f, 0.85f, 0.55f)
                : new Color(0.85f, 0.85f, 0.90f);
        }
    }

    private void SpawnEmptyMessage()
    {
        if (trackerContent == null || string.IsNullOrEmpty(emptyMessage)) return;

        var go = new GameObject("EmptyMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(trackerContent, false);
        _blocks.Add(go);

        var tmp      = go.GetComponent<TextMeshProUGUI>();
        tmp.text      = emptyMessage;
        tmp.fontSize  = 12f;
        tmp.color     = new Color(0.5f, 0.5f, 0.6f, 0.8f);
        tmp.alignment = TextAlignmentOptions.MidlineRight;
    }

    // =========================================================
    private static Color RankColor(QuestRank rank) => rank switch
    {
        QuestRank.Main      => new Color(1.0f, 0.85f, 0.2f),
        QuestRank.Secondary => new Color(0.85f, 0.90f, 1.0f),
        QuestRank.Daily     => new Color(0.6f,  0.95f, 0.65f),
        QuestRank.Guild     => new Color(0.9f,  0.65f, 1.0f),
        QuestRank.Event     => new Color(1.0f,  0.65f, 0.35f),
        QuestRank.Secret    => new Color(0.75f, 0.75f, 0.8f),
        _                   => Color.white
    };
}
