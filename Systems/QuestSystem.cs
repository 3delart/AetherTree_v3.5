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

    private void Subscribe()
    {
        GameEventBus.OnMobKilled     += HandleMobKilled;
        GameEventBus.OnItemAction    += HandleItemAction;
        GameEventBus.OnZoneEntered   += HandleZoneEntered;
        GameEventBus.OnRecipeCrafted += HandleRecipeCrafted;
    }

    private void Unsubscribe()
    {
        GameEventBus.OnMobKilled     -= HandleMobKilled;
        GameEventBus.OnItemAction    -= HandleItemAction;
        GameEventBus.OnZoneEntered   -= HandleZoneEntered;
        GameEventBus.OnRecipeCrafted -= HandleRecipeCrafted;
    }

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

        // Déjà acceptée ou terminée ? — reste du ressort de QuestSystem, pas de RequirementSet :
        // une quête ne doit jamais se re-proposer déjà active/terminée, indépendamment de ses
        // propres prérequis.
        var state = GetQuestState(quest.questID);
        if (state == QuestState.Active || state == QuestState.TurnedIn) return false;

        return quest.requirements == null || quest.requirements.IsMet(player);
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

        WeaponType playerFamily = (player.equippedWeapon?.weaponType ?? WeaponType.UnArmed).GetStartingFamily();
        var eligibleRewards = new List<QuestRewardItem>();
        if (quest.rewardItems != null)
            foreach (var r in quest.rewardItems)
                if (r != null && (r.requiredWeaponFamily == WeaponType.Any || r.requiredWeaponFamily == playerFamily))
                    eligibleRewards.Add(r);

        if (quest.rewardItems != null && quest.rewardItems.Count > 0 && eligibleRewards.Count == 0)
            Debug.LogWarning($"[QUEST] {quest.questName} : aucune récompense item éligible pour la " +
                $"famille d'arme {playerFamily} — toutes les entrées étaient filtrées, vérifier la " +
                "configuration si ce n'est pas voulu.");

        if (InventorySystem.Instance != null && InventorySystem.Instance.EmptySlotCount < eligibleRewards.Count)
        {
            Debug.LogWarning($"[QUEST] {quest.questName} : inventaire insuffisant " +
                $"({InventorySystem.Instance.EmptySlotCount} emplacement(s) libre(s), " +
                $"{eligibleRewards.Count} récompense(s) à octroyer) — turn-in refusé, rien n'est accordé.");
            FloatingText.Spawn("Inventaire plein !",
                player.transform.position + Vector3.up * 2f, Color.red);
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

        // Prestige
        if (quest.prestigeReward > 0)
        {
            player.AddPrestige(quest.prestigeReward);
            FloatingText.Spawn($"+{quest.prestigeReward} Prestige",
                player.transform.position + Vector3.up * 3f, new Color(0.85f, 0.65f, 1f));
        }

        // ── ITEMS ─────────────────────────────────────────────────
        if (eligibleRewards.Count > 0 && InventorySystem.Instance != null)
        {
            foreach (var reward in eligibleRewards)
            {
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
    // ABANDON
    // =========================================================

    /// <summary>Abandonne une quête Active ou Completed — remet sa progression à zéro et son
    /// état à None. Retourne true si l'abandon a réussi.</summary>
    public bool AbandonQuest(QuestData quest, Player player)
    {
        if (quest == null) return false;

        var state = GetQuestState(quest.questID);
        if (state != QuestState.Active && state != QuestState.Completed) return false;

        quest.ResetProgress();
        _states[quest.questID] = QuestState.None;
        _activeData.Remove(quest.questID);

        Debug.Log($"[QUEST] Abandonnée : {quest.questName}");

        GameEventBus.Publish(new QuestEvent
        {
            quest  = quest,
            action = QuestAction.Abandoned,
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
                if (obj.type != QuestObjectiveType.Kill && obj.type != QuestObjectiveType.Boss) continue;

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
    // PROGRESSION RÉCOLTE (Gather) — via ItemEvent.Pickup
    // =========================================================

    private void HandleItemAction(ItemEvent e)
    {
        if (e.action != ItemAction.Pickup) return;
        if (string.IsNullOrEmpty(e.itemID)) return;

        foreach (var kvp in _activeData)
        {
            if (_states[kvp.Key] != QuestState.Active) continue;

            QuestData quest       = kvp.Value;
            var       activeIndices = quest.GetActiveObjectiveIndices();

            foreach (int idx in activeIndices)
            {
                var obj = quest.objectives[idx];
                if (obj.type != QuestObjectiveType.Gather) continue;

                var targetData = obj.targetItem as ItemData;
                if (targetData == null || targetData.itemID != e.itemID) continue;

                bool wasComplete = obj.IsComplete;
                obj.Increment(e.quantity > 0 ? e.quantity : 1);

                if (!wasComplete)
                {
                    Debug.Log($"[QUEST] {quest.questName} · {obj.description} : {obj.ProgressLabel}");
                    GameEventBus.Publish(new QuestEvent
                    {
                        quest          = quest,
                        action         = QuestAction.ObjectiveUpdated,
                        objectiveIndex = idx,
                    });
                }
            }

            CheckCompletion(quest);
        }
    }

    // =========================================================
    // PROGRESSION EXPLORATION (Explore) — via ZoneEvent
    // =========================================================
    // ZoneEvent (ZoneTrigger.cs, système partagé pré-existant, pas touché par ce chantier) n'est
    // publié qu'au premier tick périodique (tickIntervalSeconds, 60s par défaut) ou à la sortie
    // de la zone — PAS immédiatement à l'entrée. Un objectif Explore peut donc valider avec un
    // délai allant jusqu'à tickIntervalSeconds si le joueur reste dans la zone, ou seulement à la
    // sortie s'il tick est désactivé (tickIntervalSeconds = 0). Réduire tickIntervalSeconds sur le
    // ZoneTrigger concerné (champ Inspector déjà public) atténue le délai sans toucher au code.
    // Sans risque de sur-comptage : Increment() retourne immédiatement si IsComplete est déjà
    // vrai, et requiredCount vaut 1 pour ce type dans l'usage courant.

    private void HandleZoneEntered(ZoneEvent e)
    {
        if (string.IsNullOrEmpty(e.zoneID)) return;

        foreach (var kvp in _activeData)
        {
            if (_states[kvp.Key] != QuestState.Active) continue;

            QuestData quest       = kvp.Value;
            var       activeIndices = quest.GetActiveObjectiveIndices();

            foreach (int idx in activeIndices)
            {
                var obj = quest.objectives[idx];
                if (obj.type != QuestObjectiveType.Explore) continue;
                if (obj.targetZonePrefab == null || obj.targetZonePrefab.zoneID != e.zoneID) continue;

                bool wasComplete = obj.IsComplete;
                obj.Increment();

                if (!wasComplete)
                {
                    Debug.Log($"[QUEST] {quest.questName} · {obj.description} : {obj.ProgressLabel}");
                    GameEventBus.Publish(new QuestEvent
                    {
                        quest          = quest,
                        action         = QuestAction.ObjectiveUpdated,
                        objectiveIndex = idx,
                    });
                }
            }

            CheckCompletion(quest);
        }
    }

    // =========================================================
    // PROGRESSION CRAFT (Craft) — via RecipeCraftedEvent
    // =========================================================

    private void HandleRecipeCrafted(RecipeCraftedEvent e)
    {
        if (e.recipe == null || e.recipe.result == null) return;

        foreach (var kvp in _activeData)
        {
            if (_states[kvp.Key] != QuestState.Active) continue;

            QuestData quest       = kvp.Value;
            var       activeIndices = quest.GetActiveObjectiveIndices();

            foreach (int idx in activeIndices)
            {
                var obj = quest.objectives[idx];
                if (obj.type != QuestObjectiveType.Craft) continue;
                if ((obj.targetItem as ItemData) != e.recipe.result) continue;

                bool wasComplete = obj.IsComplete;
                obj.Increment(e.quantity > 0 ? e.quantity : 1);

                if (!wasComplete)
                {
                    Debug.Log($"[QUEST] {quest.questName} · {obj.description} : {obj.ProgressLabel}");
                    GameEventBus.Publish(new QuestEvent
                    {
                        quest          = quest,
                        action         = QuestAction.ObjectiveUpdated,
                        objectiveIndex = idx,
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

    /// <summary>Retourne les données de sauvegarde (état + compteurs par objectiveID).</summary>
    public List<QuestSaveEntry> GetSaveData()
    {
        var list = new List<QuestSaveEntry>();
        foreach (var kvp in _states)
        {
            var entry = new QuestSaveEntry { questID = kvp.Key, state = kvp.Value };

            if (_activeData.TryGetValue(kvp.Key, out var quest))
            {
                foreach (var obj in quest.objectives)
                    entry.objectiveEntries.Add(new QuestObjectiveSaveEntry
                    {
                        objectiveID  = obj.objectiveID,
                        currentCount = obj.currentCount
                    });
            }
            list.Add(entry);
        }
        return list;
    }

    /// <summary>Restaure l'état depuis la sauvegarde — match par objectiveID, pas par index
    /// positionnel (réordonner/insérer un objectif entre un save et un load ne corrompt plus la
    /// progression restaurée).</summary>
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

                    // ResetProgress AVANT d'appliquer les entrées sauvegardées : currentCount vit
                    // sur le ScriptableObject partagé (peut déjà porter une valeur périmée — un
                    // objectif sans entrée correspondante, ex. objectiveID vide/dupliqué ou ancien
                    // format de save, doit retomber à 0, pas garder une valeur fortuite de l'asset).
                    quest.ResetProgress();

                    if (entry.objectiveEntries != null)
                    {
                        foreach (var savedObj in entry.objectiveEntries)
                        {
                            if (string.IsNullOrEmpty(savedObj.objectiveID)) continue;
                            var target = quest.objectives.Find(o => o.objectiveID == savedObj.objectiveID);
                            if (target != null) target.currentCount = savedObj.currentCount;
                        }
                    }
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
public class QuestObjectiveSaveEntry
{
    public string objectiveID  = "";
    public int    currentCount = 0;
}

[System.Serializable]
public class QuestSaveEntry
{
    public string     questID = "";
    public QuestState state   = QuestState.None;
    public List<QuestObjectiveSaveEntry> objectiveEntries = new List<QuestObjectiveSaveEntry>();
}