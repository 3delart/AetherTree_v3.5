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
    // Reste en clé string (pas ValueTuple) : persisté via StringIntPair
    // (CharacterProgress.cs) qui attend une string — changer le type ici
    // casserait le format de save. Voir localWinnerCounts ci-dessous pour
    // l'optimisation sans-allocation (non persisté, donc libre de le faire).
    private Dictionary<string, int> accountCounters
        = new Dictionary<string, int>();

    // ── Winners locaux ────────────────────────────────────────
    // Jamais persisté (nom "locaux" — reset à chaque session) — libre d'utiliser
    // une clé ValueTuple (struct, pas d'allocation heap par hit).
    private Dictionary<(string, int), int> localWinnerCounts
        = new Dictionary<(string, int), int>();

    // ── Records de déblocage ──────────────────────────────────
    private Dictionary<string, UnlockRecord> records
        = new Dictionary<string, UnlockRecord>();

    // ── Index par type d'event — construit une fois dans Init(), consulté
    // à chaque EvaluateAll() au lieu de reparcourir allConditions en entier.
    // Catalogue PARTAGÉ (données de référence) — ne jamais retirer une
    // condition ici parce qu'UN joueur l'a complétée. Seul retrait légitime :
    // maxWinners globalement épuisé, via _pendingIndexRemoval (voir EvaluateAll).
    private Dictionary<System.Type, List<ConditionData>> _conditionsByEventType
        = new Dictionary<System.Type, List<ConditionData>>();

    // validEntries ne change jamais après l'Init d'un ConditionData — calculé
    // une fois ici plutôt que reparcouru à chaque FinalizeCondition().
    private Dictionary<string, int> _validEntriesCache
        = new Dictionary<string, int>();

    // Déblocages détectés (compteur/seuil déjà vérifiés, temps réel) mais dont
    // l'octroi (mail, save) est différé hors du chemin chaud — voir Update().
    private Queue<ConditionData> _pendingUnlocks = new Queue<ConditionData>();

    // Conditions à retirer de _conditionsByEventType (maxWinners épuisé) — traité
    // après le foreach de EvaluateAll pour ne jamais muter la liste en cours d'itération.
    private List<ConditionData> _pendingIndexRemoval = new List<ConditionData>();

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

    // Vide _pendingUnlocks une fois par frame — hors du chemin chaud des events
    // de combat. La détection (compteur + seuil, dans EvaluateCondition) reste
    // en temps réel ; seul l'octroi (mail, save) est lissé ici.
    private void Update()
    {
        while (_pendingUnlocks.Count > 0)
            Unlock(_pendingUnlocks.Dequeue());
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

        // Type d'arme du personnage — fixe pour toute la partie (voir Unlock()).
        // Une condition dont AUCUNE récompense ne matche ce type ne sera JAMAIS
        // débloquable pour ce joueur (le type ne change jamais en cours de partie)
        // — pas la peine de la compter du tout, ça allège le suivi (moins de
        // conditions trackées, moins d'events à évaluer).
        WeaponType characterWeaponType = player?.characterData?.startingWeapon?.weaponType ?? WeaponType.Any;

        foreach (var condition in allConditions)
        {
            if (condition == null || string.IsNullOrEmpty(condition.conditionID)) continue;
            if (privateCounters.ContainsKey(condition.conditionID)) continue;

            bool hasAnyRewardDefined = condition.rewards != null && condition.rewards.Count > 0;
            if (hasAnyRewardDefined && condition.GetEligibleRewards(characterWeaponType).Count == 0)
            {
                if (verboseLogs)
                    Debug.Log($"[UNLOCK] {condition.conditionID} — aucune récompense éligible pour {characterWeaponType}, jamais comptée.");
                continue;
            }

            var entryCounters = new Dictionary<int, int>();
            int validEntries  = 0;
            for (int i = 0; i < condition.conditions.Count; i++)
            {
                entryCounters[i] = 0;

                var entry = condition.conditions[i];
                if (entry?.checker == null) continue;
                if (entry.scope != CounterScope.Server) validEntries++;

                // Index par type d'event — une condition à entries multi-type
                // apparaît dans plusieurs buckets, n'importe lequel doit la
                // re-déclencher (voir plan §2).
                var eventType = entry.checker.RelevantEventType;
                if (eventType == null) continue;
                if (!_conditionsByEventType.TryGetValue(eventType, out var bucket))
                {
                    bucket = new List<ConditionData>();
                    _conditionsByEventType[eventType] = bucket;
                }
                if (!bucket.Contains(condition)) bucket.Add(condition);
            }

            privateCounters[condition.conditionID]  = entryCounters;
            completedEntries[condition.conditionID] = new HashSet<int>();
            _validEntriesCache[condition.conditionID] = validEntries;
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

        // Filtrage par type d'event — remplace le parcours de allConditions en
        // entier. Un event dont aucun checker du jeu ne s'occupe (ex: aucun
        // ItemEvent/PetEvent utilisé actuellement) ressort en O(1) ici.
        if (!_conditionsByEventType.TryGetValue(gameEvent.GetType(), out var relevant)) return;

        foreach (var condition in relevant)
        {
            if (condition == null || string.IsNullOrEmpty(condition.conditionID)) continue;
            if (records.ContainsKey(condition.conditionID))                       continue;
            if (!privateCounters.ContainsKey(condition.conditionID))              continue;

            EvaluateCondition(condition, gameEvent);
        }

        // Retraits différés jusqu'ici — jamais pendant le foreach ci-dessus,
        // qui itère potentiellement l'une des listes concernées.
        if (_pendingIndexRemoval.Count > 0)
        {
            foreach (var condition in _pendingIndexRemoval)
                foreach (var bucket in _conditionsByEventType.Values)
                    bucket.Remove(condition);
            _pendingIndexRemoval.Clear();
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
                var winKey  = WinnerKey(condition.conditionID, i);
                int winners = localWinnerCounts.TryGetValue(winKey, out int w) ? w : 0;
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
                    var winKey = WinnerKey(condition.conditionID, i);
                    localWinnerCounts[winKey] = (localWinnerCounts.TryGetValue(winKey, out int w) ? w : 0) + 1;

                    // Pool de gagnants épuisé pour TOUTES les entries à maxWinners de cette
                    // condition → plus jamais débloquable par personne, seul cas où on retire
                    // du catalogue partagé (voir plan §4 — jamais sur simple complétion joueur).
                    if (AllWinnerSlotsExhausted(condition) && !_pendingIndexRemoval.Contains(condition))
                        _pendingIndexRemoval.Add(condition);
                }
            }
        }

        FinalizeCondition(condition);
    }

    /// <summary>
    /// True si toutes les entries à maxWinners>0 de la condition ont atteint leur
    /// plafond (et qu'il y en a au moins une) — condition définitivement et
    /// globalement indébloquable, peu importe le joueur.
    /// </summary>
    private bool AllWinnerSlotsExhausted(ConditionData condition)
    {
        bool hasWinnerLimit = false;
        for (int i = 0; i < condition.conditions.Count; i++)
        {
            var entry = condition.conditions[i];
            if (entry?.checker == null || entry.maxWinners <= 0) continue;
            hasWinnerLimit = true;
            int winners = localWinnerCounts.TryGetValue(WinnerKey(condition.conditionID, i), out int w) ? w : 0;
            if (winners < entry.maxWinners) return false;
        }
        return hasWinnerLimit;
    }

    private void FinalizeCondition(ConditionData condition)
    {
        if (records.ContainsKey(condition.conditionID)) return;

        // validEntries précalculé une fois dans Init() — ne change jamais après,
        // plus besoin de reparcourir les entries à chaque évaluation.
        if (!_validEntriesCache.TryGetValue(condition.conditionID, out int validEntries) || validEntries == 0)
            return;

        var completed = completedEntries[condition.conditionID];
        if (completed.Count == validEntries && !_pendingUnlocks.Contains(condition))
            _pendingUnlocks.Enqueue(condition);
    }

    // =========================================================
    // COMPTEURS
    // =========================================================

    private int GetCounter(ConditionData condition, int entryIndex)
    {
        var entry = condition.conditions[entryIndex];
        if (entry.scope == CounterScope.Account)
        {
            var key = AccountKey(condition.conditionID, entryIndex);
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
            var key = AccountKey(condition.conditionID, entryIndex);
            accountCounters[key] = GetCounter(condition, entryIndex) + 1;
            return;
        }
        if (privateCounters.TryGetValue(condition.conditionID, out var d))
            d[entryIndex] = d.ContainsKey(entryIndex) ? d[entryIndex] + 1 : 1;
    }

    // AccountKey reste une string — voir commentaire sur accountCounters (persisté
    // via StringIntPair, ne pas changer le type sans migrer le format de save).
    private static string AccountKey(string conditionID, int entryIndex)
        => $"{conditionID}__{entryIndex}";

    // WinnerKey en ValueTuple (struct) — jamais persisté, pas d'allocation heap
    // par hit contrairement à l'ancienne interpolation de string.
    private static (string, int) WinnerKey(string conditionID, int entryIndex)
        => (conditionID, entryIndex);

    // =========================================================
    // DÉBLOCAGE
    // =========================================================

    private void Unlock(ConditionData condition)
    {
        if (records.ContainsKey(condition.conditionID)) return;

        // ⚠ Rien n'est enregistré tant qu'on n'est pas sûr qu'une récompense (si
        // prévue) partira réellement — sinon la condition est "gâchée" pour de bon
        // sans jamais avoir livré son mail, sans retry possible. Voir plus bas :
        // seul le cas "des rewards existent mais aucun n'est éligible" bloque —
        // une condition sans AUCUNE récompense définie (marqueur pur) s'enregistre
        // normalement.
        if (MailboxSystem.Instance == null)
        {
            Debug.LogError("[UNLOCK] MailboxSystem introuvable ! Retry au prochain déclenchement.");
            return;
        }

        // Type d'arme du PERSONNAGE (fixe, défini à la création via startingWeapon)
        // — pas l'arme momentanément équipée, qui peut être retirée à tout moment
        // sans changer l'identité du personnage. GDD §5.1 : weaponCategory est
        // immuable ; ce filtre de reward va plus loin, au WeaponType précis.
        WeaponType characterWeaponType = player?.characterData?.startingWeapon?.weaponType ?? WeaponType.Any;
        var        eligibleRewards     = condition.GetEligibleRewards(characterWeaponType);

        bool hasAnyRewardDefined = condition.rewards != null && condition.rewards.Count > 0;
        if (hasAnyRewardDefined && eligibleRewards.Count == 0)
        {
            Debug.LogWarning($"[UNLOCK] {condition.conditionID} — aucune récompense éligible pour {characterWeaponType}, retry plus tard.");
            return;
        }

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
