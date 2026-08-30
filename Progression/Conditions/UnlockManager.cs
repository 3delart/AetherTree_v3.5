using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// =============================================================
// UNLOCKMANAGER.CS
// Path : Assets/_Game/Scripts/Progression/Unlock/UnlockManager.cs
// AetherTree GDD v31
//
// Unique responsabilité : recevoir les events du GameEventBus,
// passer chaque event à chaque ConditionEntry de chaque ConditionData,
// incrémenter les compteurs et déclencher Unlock() quand tout est validé.
//
// Ne contient AUCUNE logique de condition — tout est dans les Checkers.
//
// Modes d'évaluation (ConditionMode) :
//   Parallel    — toutes les entries évaluées à chaque event.
//   Ordered     — Entry[N] bloquée jusqu'à ce que Entry[N-1] soit complète.
//   AllRequired — identique à Parallel (alias sémantique).
//
// Filtres globaux appliqués AVANT le checker (sur ConditionEntry) :
//   playerLevelMin / playerLevelMax / maxWinners
//
// Scopes :
//   Character → privateCounters  (par perso, sauvé dans character.json)
//   Account   → accountCounters  (cumulé, sauvé dans account.json)
//   Server    → ignoré v3.5, stub prêt pour v4
// =============================================================

public class UnlockManager : MonoBehaviour
{
    public static UnlockManager Instance { get; private set; }

    [Header("Toutes les conditions du jeu")]
    [Tooltip("Clic droit → 'Auto-remplir allConditions' pour scanner le projet.")]
    public List<ConditionData> allConditions = new List<ConditionData>();

    [Header("Debug")]
    public bool verboseLogs = false;

    // ── Compteurs Character ───────────────────────────────────
    private Dictionary<string, Dictionary<int, int>> privateCounters
        = new Dictionary<string, Dictionary<int, int>>();

    private Dictionary<string, HashSet<int>> completedEntries
        = new Dictionary<string, HashSet<int>>();

    // ── Compteurs Account ─────────────────────────────────────
    private Dictionary<string, int> accountCounters
        = new Dictionary<string, int>();

    // ── Winners locaux ────────────────────────────────────────
    private Dictionary<string, int> localWinnerCounts
        = new Dictionary<string, int>();

    // ── Records de déblocage ──────────────────────────────────
    private Dictionary<string, UnlockRecord> records
        = new Dictionary<string, UnlockRecord>();

    private ActivityCounter activityCounter;
    private Player          player;
    private bool            _initialized = false;
    private bool            _subscribed  = false;

    // Delegates stockés pour désabonnement correct
    private System.Action<MobKilledEvent>      _onMobKilled;
    private System.Action<DamageDealtEvent>    _onDamageDealt;
    private System.Action<SkillUsedEvent>      _onSkillUsed;
    private System.Action<PlayerDeathEvent>    _onPlayerDeath;
    private System.Action<PlayerLevelUpEvent>  _onPlayerLevelUp;
    private System.Action<DebuffReceivedEvent> _onDebuffReceived;
    private System.Action<NpcInteractEvent>    _onNpcInteract;
    private System.Action<ZoneEvent>           _onZoneEntered;
    private System.Action<ItemEvent>           _onItemAction;
    private System.Action<SocialEvent>         _onSocialAction;
    private System.Action<PetEvent>            _onPetAction;
    private System.Action<TimeEvent>           _onTimeAction;
    private System.Action<MetierEvent>         _onMetierAction;
    private System.Action<ServerEvent>         _onServerEvent;
    private System.Action<StatsChangedEvent>   _onStatsChanged;
    private System.Action<QuestEvent>          _onQuestAction;

    // =========================================================
    // LIFECYCLE
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        #if UNITY_EDITOR
            EditorAutoFill();
        #endif
        DontDestroyOnLoad(gameObject);

        _onMobKilled      = e => EvaluateAll(e);
        _onDamageDealt    = e => EvaluateAll(e);
        _onSkillUsed      = e => EvaluateAll(e);
        _onPlayerDeath    = e => EvaluateAll(e);
        _onPlayerLevelUp  = e => EvaluateAll(e);
        _onDebuffReceived = e => EvaluateAll(e);
        _onNpcInteract    = e => EvaluateAll(e);
        _onZoneEntered    = e => EvaluateAll(e);
        _onItemAction     = e => EvaluateAll(e);
        _onSocialAction   = e => EvaluateAll(e);
        _onPetAction      = e => EvaluateAll(e);
        _onTimeAction     = e => EvaluateAll(e);
        _onMetierAction   = e => EvaluateAll(e);
        _onServerEvent    = e => EvaluateAll(e);
        _onStatsChanged   = e => EvaluateAll(e);
        _onQuestAction    = e => EvaluateAll(e);
    }

    private void Start()
    {
        if (!_initialized)
        {
            var counter = FindObjectOfType<ActivityCounter>();
            if (counter != null) Init(counter);
            else Debug.LogError("[UNLOCK] ActivityCounter introuvable — Init() impossible !");
        }
    }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    // =========================================================
    // ABONNEMENTS
    // =========================================================

    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        GameEventBus.OnMobKilled      += _onMobKilled;
        GameEventBus.OnDamageDealt    += _onDamageDealt;
        GameEventBus.OnSkillUsed      += _onSkillUsed;
        GameEventBus.OnPlayerDeath    += _onPlayerDeath;
        GameEventBus.OnPlayerLevelUp  += _onPlayerLevelUp;
        GameEventBus.OnDebuffReceived += _onDebuffReceived;
        GameEventBus.OnNpcInteract    += _onNpcInteract;
        GameEventBus.OnZoneEntered    += _onZoneEntered;
        GameEventBus.OnItemAction     += _onItemAction;
        GameEventBus.OnSocialAction   += _onSocialAction;
        GameEventBus.OnPetAction      += _onPetAction;
        GameEventBus.OnTimeAction     += _onTimeAction;
        GameEventBus.OnMetierAction   += _onMetierAction;
        GameEventBus.OnServerEvent    += _onServerEvent;
        GameEventBus.OnStatsChanged   += _onStatsChanged;
        GameEventBus.OnQuestAction    += _onQuestAction;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;

        GameEventBus.OnMobKilled      -= _onMobKilled;
        GameEventBus.OnDamageDealt    -= _onDamageDealt;
        GameEventBus.OnSkillUsed      -= _onSkillUsed;
        GameEventBus.OnPlayerDeath    -= _onPlayerDeath;
        GameEventBus.OnPlayerLevelUp  -= _onPlayerLevelUp;
        GameEventBus.OnDebuffReceived -= _onDebuffReceived;
        GameEventBus.OnNpcInteract    -= _onNpcInteract;
        GameEventBus.OnZoneEntered    -= _onZoneEntered;
        GameEventBus.OnItemAction     -= _onItemAction;
        GameEventBus.OnSocialAction   -= _onSocialAction;
        GameEventBus.OnPetAction      -= _onPetAction;
        GameEventBus.OnTimeAction     -= _onTimeAction;
        GameEventBus.OnMetierAction   -= _onMetierAction;
        GameEventBus.OnServerEvent    -= _onServerEvent;
        GameEventBus.OnStatsChanged   -= _onStatsChanged;
        GameEventBus.OnQuestAction    -= _onQuestAction;
    }

    // =========================================================
    // INIT
    // =========================================================

    public void Init(ActivityCounter counter)
    {
        if (_initialized) return;
        _initialized = true;

        activityCounter = counter;
        player          = FindObjectOfType<Player>();

        if (allConditions.Count == 0)
            Debug.LogWarning("[UNLOCK] allConditions vide ! Clic droit → 'Auto-remplir allConditions'.");

        foreach (var condition in allConditions)
        {
            if (condition == null || string.IsNullOrEmpty(condition.conditionID)) continue;
            if (privateCounters.ContainsKey(condition.conditionID)) continue;

            var entryCounters = new Dictionary<int, int>();
            for (int i = 0; i < condition.conditions.Count; i++)
                entryCounters[i] = 0;

            privateCounters[condition.conditionID]  = entryCounters;
            completedEntries[condition.conditionID] = new HashSet<int>();
        }
    }

    // =========================================================
    // ÉVALUATION
    // =========================================================

    private void EvaluateAll(object gameEvent)
    {
        if (!_initialized)
        {
            var counter = FindObjectOfType<ActivityCounter>();
            if (counter != null) Init(counter);
            else return;
        }

        foreach (var condition in allConditions)
        {
            if (condition == null || string.IsNullOrEmpty(condition.conditionID)) continue;
            if (records.ContainsKey(condition.conditionID))                       continue;
            if (!privateCounters.ContainsKey(condition.conditionID))              continue;

            EvaluateCondition(condition, gameEvent);
        }
    }

    private void EvaluateCondition(ConditionData condition, object gameEvent)
    {
        var completed = completedEntries[condition.conditionID];

        for (int i = 0; i < condition.conditions.Count; i++)
        {
            var entry = condition.conditions[i];
            if (entry?.checker == null) continue;
            if (completed.Contains(i))  continue;

            // Mode Ordered
            if (condition.mode == ConditionMode.Ordered && i > 0 && !completed.Contains(i - 1))
                continue;

            // Scope Server ignoré en v3.5
            if (entry.scope == CounterScope.Server)
            {
                if (verboseLogs)
                    Debug.Log($"[UNLOCK] {condition.conditionID}[{i}] scope Server — ignoré en v3.5");
                continue;
            }

            // Filtres globaux
            if (entry.playerLevelMin > 0 && player != null && player.level < entry.playerLevelMin) continue;
            if (entry.playerLevelMax > 0 && player != null && player.level > entry.playerLevelMax) continue;

            if (entry.maxWinners > 0)
            {
                string winKey  = WinnerKey(condition.conditionID, i);
                int    winners = localWinnerCounts.TryGetValue(winKey, out int w) ? w : 0;
                if (winners >= entry.maxWinners) continue;
            }

            // Évaluation
            bool hit = false;
            try   { hit = entry.checker.Evaluate(gameEvent, player); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UNLOCK] EXCEPTION — {condition.conditionID}[{i}] " +
                               $"({entry.checker.GetType().Name}) : {ex.Message}\n{ex.StackTrace}");
                continue;
            }

            if (!hit) continue;

            IncrementCounter(condition, i);
            int current = GetCounter(condition, i);

            if (verboseLogs)
                Debug.Log($"[UNLOCK] {condition.conditionID}[{i}] " +
                          $"{entry.checker.GetType().Name} " +
                          $"scope={entry.scope} {current}/{entry.countRequired}");

            if (current >= entry.countRequired)
            {
                completed.Add(i);

                if (entry.maxWinners > 0)
                {
                    string winKey = WinnerKey(condition.conditionID, i);
                    localWinnerCounts[winKey] = (localWinnerCounts.TryGetValue(winKey, out int w) ? w : 0) + 1;
                }
            }
        }

        FinalizeCondition(condition);
    }

    private void FinalizeCondition(ConditionData condition)
    {
        if (records.ContainsKey(condition.conditionID)) return;

        int validEntries = 0;
        for (int i = 0; i < condition.conditions.Count; i++)
        {
            var entry = condition.conditions[i];
            if (entry?.checker != null && entry.scope != CounterScope.Server)
                validEntries++;
        }

        if (validEntries == 0) return;

        var completed = completedEntries[condition.conditionID];
        if (completed.Count == validEntries)
            Unlock(condition);
    }

    // =========================================================
    // COMPTEURS
    // =========================================================

    private int GetCounter(ConditionData condition, int entryIndex)
    {
        var entry = condition.conditions[entryIndex];
        if (entry.scope == CounterScope.Account)
        {
            string key = AccountKey(condition.conditionID, entryIndex);
            return accountCounters.TryGetValue(key, out int v) ? v : 0;
        }
        return privateCounters.TryGetValue(condition.conditionID, out var d)
               && d.TryGetValue(entryIndex, out int cv) ? cv : 0;
    }

    private void IncrementCounter(ConditionData condition, int entryIndex)
    {
        var entry = condition.conditions[entryIndex];
        if (entry.scope == CounterScope.Account)
        {
            string key = AccountKey(condition.conditionID, entryIndex);
            accountCounters[key] = GetCounter(condition, entryIndex) + 1;
            return;
        }
        if (privateCounters.TryGetValue(condition.conditionID, out var d))
            d[entryIndex] = d.ContainsKey(entryIndex) ? d[entryIndex] + 1 : 1;
    }

    private static string AccountKey(string conditionID, int entryIndex)
        => $"{conditionID}__{entryIndex}";

    private static string WinnerKey(string conditionID, int entryIndex)
        => $"win__{conditionID}__{entryIndex}";

    // =========================================================
    // DÉBLOCAGE
    // =========================================================

    private void Unlock(ConditionData condition)
    {
        if (records.ContainsKey(condition.conditionID)) return;

        var record = new UnlockRecord
        {
            conditionID = condition.conditionID,
            unlockedAt  = System.DateTime.Now,
            playerLevel = activityCounter?.Get(CounterKeys.PLAYER_LEVEL)        ?? 0,
            killsTotal  = activityCounter?.Get(CounterKeys.KILLS_TOTAL)         ?? 0,
            timePlayed  = activityCounter?.Get(CounterKeys.TIME_PLAYED_MINUTES) ?? 0,
        };
        records[condition.conditionID] = record;

        Debug.Log(condition.isHidden
            ? "[UNLOCK SECRET] Condition mystère débloquée !"
            : $"[UNLOCK] {condition.displayName} — scope:{condition.GetDominantScope()}");

        if (MailboxSystem.Instance == null)
        {
            Debug.LogError("[UNLOCK] MailboxSystem introuvable !");
            return;
        }

        WeaponType equippedWeapon  = player?.equippedWeaponInstance?.data?.weaponType ?? WeaponType.Any;
        var        eligibleRewards = condition.GetEligibleRewards(equippedWeapon);

        if (eligibleRewards.Count == 0)
            Debug.LogWarning($"[UNLOCK] {condition.conditionID} — aucune récompense éligible pour {equippedWeapon}.");

        foreach (var reward in eligibleRewards)
            MailboxSystem.Instance.SendRewardMail(condition, reward);
    }

    // =========================================================
    // SAVE / LOAD — Character
    // =========================================================

    public List<SavedConditionProgress> GetConditionProgresses()
    {
        var result = new List<SavedConditionProgress>();
        foreach (var condition in allConditions)
        {
            if (condition == null || string.IsNullOrEmpty(condition.conditionID)) continue;
            if (records.ContainsKey(condition.conditionID))                       continue;
            if (!privateCounters.ContainsKey(condition.conditionID))              continue;

            var counters = privateCounters[condition.conditionID];
            if (!completedEntries.TryGetValue(condition.conditionID, out var completed)) continue;

            bool hasProgress = counters.Any(kvp => kvp.Value > 0);
            if (!hasProgress) continue;

            var saved = new SavedConditionProgress { conditionID = condition.conditionID };
            for (int i = 0; i < condition.conditions.Count; i++)
            {
                saved.entryCounters .Add(counters.ContainsKey(i)  ? counters[i] : 0);
                saved.entryCompleted.Add(completed.Contains(i));
            }
            result.Add(saved);
        }
        return result;
    }

    public void LoadConditionProgresses(List<SavedConditionProgress> progresses)
    {
        if (progresses == null) return;
        foreach (var saved in progresses)
        {
            if (string.IsNullOrEmpty(saved.conditionID))         continue;
            if (records.ContainsKey(saved.conditionID))          continue;
            if (!privateCounters.ContainsKey(saved.conditionID)) continue;

            var counters  = privateCounters[saved.conditionID];
            var completed = completedEntries[saved.conditionID];

            for (int i = 0; i < saved.entryCounters.Count; i++)
            {
                if (counters.ContainsKey(i)) counters[i] = saved.entryCounters[i];
                if (i < saved.entryCompleted.Count && saved.entryCompleted[i]) completed.Add(i);
            }
        }
    }

    // =========================================================
    // SAVE / LOAD — Account
    // =========================================================

    public void CollectAccountProgress(AccountProgress account)
    {
        account.accountCountersList.Clear();
        foreach (var kv in accountCounters)
            account.accountCountersList.Add(new StringIntPair(kv.Key, kv.Value));

        account.unlockedAccountConditionIDs.Clear();
        foreach (var condition in allConditions)
        {
            if (condition == null) continue;
            if (condition.GetDominantScope() != CounterScope.Account) continue;
            if (records.ContainsKey(condition.conditionID))
                account.unlockedAccountConditionIDs.Add(condition.conditionID);
        }
    }

    public void LoadAccountProgress(AccountProgress account)
    {
        if (account == null) return;

        accountCounters.Clear();
        if (account.accountCountersList != null)
            foreach (var pair in account.accountCountersList)
                accountCounters[pair.key] = pair.value;

        if (account.unlockedAccountConditionIDs != null)
            foreach (var id in account.unlockedAccountConditionIDs)
                if (!records.ContainsKey(id))
                    records[id] = new UnlockRecord { conditionID = id };

        if (verboseLogs)
            Debug.Log($"[UNLOCK] LoadAccountProgress — {accountCounters.Count} compteurs.");
    }

    // =========================================================
    // UTILITAIRES PUBLICS
    // =========================================================

    public bool               IsUnlocked(string id) => records.ContainsKey(id);
    public UnlockRecord       GetRecord(string id)  => records.TryGetValue(id, out var r) ? r : null;
    public List<UnlockRecord> GetAllRecords()       => records.Values.OrderBy(r => r.unlockedAt).ToList();
    public List<string>       GetUnlocked()         => records.Keys.ToList();

    public int GetEntryProgress(string conditionID, int entryIndex)
    {
        if (!privateCounters.TryGetValue(conditionID, out var d)) return 0;
        return d.TryGetValue(entryIndex, out int v) ? v : 0;
    }

    public UnlockRecord GetRecordForSkill(SkillData skill)
    {
        if (skill == null) return null;
        foreach (var condition in allConditions)
        {
            if (condition == null) continue;
            foreach (var reward in condition.GetEligibleRewards(WeaponType.Any))
                if (reward?.rewardSkill == skill)
                    return GetRecord(condition.conditionID);
        }
        return null;
    }

    public void LoadUnlocked(List<string> saved)
    {
        foreach (var id in saved)
            if (!records.ContainsKey(id))
                records[id] = new UnlockRecord { conditionID = id };
    }

    // =========================================================
    // EDITOR
    // =========================================================

#if UNITY_EDITOR
    [ContextMenu("Auto-remplir allConditions")]
    private void EditorAutoFill()
    {
        allConditions.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ConditionData");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var c = UnityEditor.AssetDatabase.LoadAssetAtPath<ConditionData>(path);
            if (c != null) allConditions.Add(c);
        }
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[UNLOCK] Auto-fill : {allConditions.Count} conditions trouvées.");
    }
#endif
}
