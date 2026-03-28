using UnityEngine;
using System.Collections.Generic;

// =============================================================
// QUESTSYSTEM — Cerveau central du système de quêtes
// Path : Assets/Scripts/Systems/QuestSystem.cs
// AetherTree GDD v31 — §25
//
// Gère :
//   AcceptQuest()    — accepte une quête si les prérequis sont remplis
//   NotifyKill()     — appelé par Mob.Die() via MobKilledEvent
//   NotifyTalkTo()   — appelé par PNJ.Interact() quand type = TalkTo
//   TurnInQuest()    — valide la quête, distribue les récompenses
//   GetQuestState()  — None / Active / Completed / TurnedIn
//
// Setup Unity :
//   Poser sur _Managers. Pas de références Inspector requises.
// =============================================================

public enum QuestState { None, Active, Completed, TurnedIn }

public class QuestSystem : MonoBehaviour
{
    public static QuestSystem Instance { get; private set; }

    // questID → état
    private readonly Dictionary<string, QuestState>   _states     = new Dictionary<string, QuestState>();
    // questID → QuestData (pour accéder aux objectifs runtime)
    private readonly Dictionary<string, QuestData>    _activeData = new Dictionary<string, QuestData>();

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()   => GameEventBus.OnMobKilled += HandleMobKilled;
    private void Unsubscribe() => GameEventBus.OnMobKilled -= HandleMobKilled;

    // =========================================================
    // ACCEPT
    // =========================================================

    /// <summary>
    /// Accepte une quête si les prérequis sont remplis.
    /// Retourne true si l'acceptation a réussi.
    /// </summary>
    public bool AcceptQuest(QuestData quest, Player player)
    {
        if (quest == null || player == null) return false;
        if (string.IsNullOrEmpty(quest.questID))
        {
            Debug.LogWarning($"[QUEST] {quest.questName} n'a pas de questID !");
            return false;
        }
        if (!CanAccept(quest, player))
        {
            Debug.Log($"[QUEST] {quest.questName} : prérequis non remplis.");
            return false;
        }

        _states[quest.questID]     = QuestState.Active;
        _activeData[quest.questID] = quest;

        // Remet les compteurs à zéro (important pour les quotidiennes)
        quest.ResetProgress();

        Debug.Log($"[QUEST] Acceptée : {quest.questName}");

        GameEventBus.Publish(new QuestEvent
        {
            quest  = quest,
            action = QuestAction.Accepted,
            player = player,
        });
        return true;
    }

    /// <summary>True si la quête peut être acceptée.</summary>
    public bool CanAccept(QuestData quest, Player player)
    {
        if (quest == null) return false;
        if (string.IsNullOrEmpty(quest.questID)) return false;

        // Déjà acceptée ou terminée ?
        var state = GetQuestState(quest.questID);
        if (state == QuestState.Active || state == QuestState.TurnedIn) return false;

        // Niveau minimum
        if (player != null && player.level < quest.minLevel) return false;

        // Prérequis quête
        if (quest.prerequisiteQuest != null)
        {
            var preState = GetQuestState(quest.prerequisiteQuest.questID);
            if (preState != QuestState.TurnedIn) return false;
        }

        return true;
    }

    // =========================================================
    // TURN IN
    // =========================================================

    /// <summary>
    /// Valide la quête et distribue les récompenses.
    /// Retourne true si la validation a réussi.
    /// </summary>
    public bool TurnInQuest(QuestData quest, Player player)
    {
        if (quest == null || player == null) return false;
        if (GetQuestState(quest.questID) != QuestState.Completed)
        {
            Debug.Log($"[QUEST] {quest.questName} : pas encore complétée.");
            return false;
        }

        _states[quest.questID] = QuestState.TurnedIn;

        // XP
        if (quest.xpReward > 0)
        {
            player.AddCombatXP(quest.xpReward);
            FloatingText.Spawn($"+{quest.xpReward} XP",
                player.transform.position + Vector3.up * 2.5f, new Color(0.4f, 0.8f, 1f));
        }

        // Aeris
        if (quest.aerisReward > 0 && AerisSystem.Instance != null)
        {
            AerisSystem.Instance.Add(quest.aerisReward);
            FloatingText.Spawn($"+{quest.aerisReward} ¤",
                player.transform.position + Vector3.up * 2f, new Color(1f, 0.85f, 0.2f));
        }

        // ── ITEMS ─────────────────────────────────────────────────
        if (quest.rewardItems != null && InventorySystem.Instance != null)
        {
            foreach (var reward in quest.rewardItems)
            {
                if (reward == null) continue;
                var item = reward.CreateItem();
                if (item == null) continue;

                bool added = InventorySystem.Instance.AddItem(item);
                if (added)
                    FloatingText.Spawn($"+{reward.DisplayName}",
                        player.transform.position + Vector3.up * 1.5f,
                        new Color(1f, 0.85f, 0.3f));
                else
                    Debug.LogWarning($"[QUEST] Inventaire plein — item {reward.DisplayName} perdu !");
            }
        }
        // ──────────────────────────────────────────────────────────

        Debug.Log($"[QUEST] Terminée : {quest.questName} (+{quest.xpReward} XP / +{quest.aerisReward} ¤)");

        GameEventBus.Publish(new QuestEvent
        {
            quest  = quest,
            action = QuestAction.TurnedIn,
            player = player,
        });
        return true;
    }

    // =========================================================
    // ÉTAT
    // =========================================================

    public QuestState GetQuestState(string questID)
    {
        if (string.IsNullOrEmpty(questID)) return QuestState.None;
        return _states.TryGetValue(questID, out var state) ? state : QuestState.None;
    }

    public QuestState GetQuestState(QuestData quest)
        => quest != null ? GetQuestState(quest.questID) : QuestState.None;

    // =========================================================
    // PROGRESSION KILL (via GameEventBus)
    // =========================================================

    private void HandleMobKilled(MobKilledEvent e)
    {
        if (e.mob == null) return;
        if (e.eligiblePlayers == null || e.eligiblePlayers.Count == 0) return;

        // Ne concerne que le joueur principal
        Player player = e.eligiblePlayers[0];

        foreach (var kvp in _activeData)
        {
            if (_states[kvp.Key] != QuestState.Active) continue;

            QuestData quest  = kvp.Value;
            var activeIndices = quest.GetActiveObjectiveIndices();

            foreach (int idx in activeIndices)
            {
                var obj = quest.objectives[idx];
                if (obj.type != QuestObjectiveType.Kill) continue;

                // Vérifie si le mob correspond (comparaison nom, insensible à la casse)
                if (!string.IsNullOrEmpty(obj.TargetName) &&
                    !e.mob.mobName.Equals(obj.TargetName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                bool wasComplete = obj.IsComplete;
                bool justDone   = obj.Increment();

                if (!wasComplete)
                {
                    Debug.Log($"[QUEST] {quest.questName} · {obj.description} : {obj.ProgressLabel}");
                    GameEventBus.Publish(new QuestEvent
                    {
                        quest           = quest,
                        action          = QuestAction.ObjectiveUpdated,
                        objectiveIndex  = idx,
                    });
                }
            }

            CheckCompletion(quest);
        }
    }

    // =========================================================
    // PROGRESSION PARLER AU PNJ
    // =========================================================

    /// <summary>
    /// À appeler depuis PNJ.Interact() quand le PNJ est la cible d'un objectif TalkTo.
    /// </summary>
    public void NotifyTalkTo(PNJData pnjData, Player player)
    {
        if (pnjData == null || player == null) return;

        foreach (var kvp in _activeData)
        {
            if (_states[kvp.Key] != QuestState.Active) continue;

            QuestData quest       = kvp.Value;
            var       activeIndices = quest.GetActiveObjectiveIndices();

            foreach (int idx in activeIndices)
            {
                var obj = quest.objectives[idx];
                if (obj.type != QuestObjectiveType.TalkTo) continue;

                // Comparaison par référence SO — pas par nom string
                if (obj.targetPNJ == null || obj.targetPNJ != pnjData) continue;

                obj.Increment();
                Debug.Log($"[QUEST] {quest.questName} · TalkTo {pnjData.pnjName} : {obj.ProgressLabel}");

                GameEventBus.Publish(new QuestEvent
                {
                    quest          = quest,
                    action         = QuestAction.ObjectiveUpdated,
                    objectiveIndex = idx,
                    player         = player,
                });
            }

            CheckCompletion(quest);
        }
    }

    // =========================================================
    // VÉRIFICATION COMPLÉTION
    // =========================================================

    private void CheckCompletion(QuestData quest)
    {
        if (_states[quest.questID] != QuestState.Active) return;
        if (!quest.AllObjectivesComplete()) return;

        _states[quest.questID] = QuestState.Completed;
        Debug.Log($"[QUEST] Complétée (à valider) : {quest.questName}");

        GameEventBus.Publish(new QuestEvent
        {
            quest  = quest,
            action = QuestAction.Completed,
        });

        // Notification visuelle
        var player = UnityEngine.Object.FindObjectOfType<Player>();
        if (player != null)
            FloatingText.Spawn($"Quête complétée !", player.transform.position + Vector3.up * 3f,
                new Color(1f, 0.85f, 0.2f), 2f);
    }

    // =========================================================
    // SAUVEGARDE / CHARGEMENT
    // =========================================================

    /// <summary>Retourne les données de sauvegarde (état + compteurs).</summary>
    public List<QuestSaveEntry> GetSaveData()
    {
        var list = new List<QuestSaveEntry>();
        foreach (var kvp in _states)
        {
            var entry = new QuestSaveEntry { questID = kvp.Key, state = kvp.Value };

            if (_activeData.TryGetValue(kvp.Key, out var quest))
            {
                foreach (var obj in quest.objectives)
                    entry.objectiveCounts.Add(obj.currentCount);
            }
            list.Add(entry);
        }
        return list;
    }

    /// <summary>Restaure l'état depuis la sauvegarde.</summary>
    public void LoadSaveData(List<QuestSaveEntry> entries, List<QuestData> allQuests)
    {
        if (entries == null || allQuests == null) return;

        // Construit un dict pour retrouver les QuestData par ID
        var questByID = new Dictionary<string, QuestData>();
        foreach (var q in allQuests)
            if (q != null && !string.IsNullOrEmpty(q.questID))
                questByID[q.questID] = q;

        foreach (var entry in entries)
        {
            _states[entry.questID] = entry.state;

            if (entry.state == QuestState.Active || entry.state == QuestState.Completed)
            {
                if (questByID.TryGetValue(entry.questID, out var quest))
                {
                    _activeData[entry.questID] = quest;
                    for (int i = 0; i < entry.objectiveCounts.Count && i < quest.objectives.Count; i++)
                        quest.objectives[i].currentCount = entry.objectiveCounts[i];
                }
            }
        }
    }

    // =========================================================
    // ACCESSEURS UI
    // =========================================================

    /// <summary>Toutes les quêtes actives.</summary>
    public List<QuestData> GetActiveQuests()
    {
        var list = new List<QuestData>();
        foreach (var kvp in _activeData)
            if (_states[kvp.Key] == QuestState.Active)
                list.Add(kvp.Value);
        return list;
    }

    /// <summary>Toutes les quêtes terminées (pas encore validées).</summary>
    public List<QuestData> GetCompletedQuests()
    {
        var list = new List<QuestData>();
        foreach (var kvp in _activeData)
            if (_states[kvp.Key] == QuestState.Completed)
                list.Add(kvp.Value);
        return list;
    }

    /// <summary>Toutes les quêtes validées (TurnedIn).</summary>
    public List<QuestData> GetTurnedInQuests()
    {
        var list = new List<QuestData>();
        foreach (var kvp in _activeData)
            if (_states[kvp.Key] == QuestState.TurnedIn)
                list.Add(kvp.Value);
        return list;
    }
}

// =============================================================
// STRUCTURE SAUVEGARDE
// =============================================================
[System.Serializable]
public class QuestSaveEntry
{
    public string     questID = "";
    public QuestState state   = QuestState.None;
    public List<int>  objectiveCounts = new List<int>();
}