# World State Save Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Amendments post-implementation (2026-10-01)** — after all 4 tasks shipped and Florian tested
in Play Mode: (1) the save path was wrong (`Application.persistentDataPath` assumed without
checking `SaveSystem.cs`'s real convention, `Directory.GetParent(Application.dataPath) +
"Saves"` — fixed, commit `29e6751`); (2) position tracking was dropped from the registry entirely
— confirmed working, but added no practical value for SpawnManager's always-random-position
zones (commit `46d89e5`). The task bodies below still show the originally-approved design
(position parameters throughout) — not reflected, see the two commits above for the real diff.

**Goal:** Persist World Event scheduler countdown and SpawnManager zone respawn timers (map bosses + rare resource nodes) across a game restart, via a new central `WorldStateRegistry`.

**Architecture:** A new `WorldStateRegistry` singleton (`DontDestroyOnLoad`) owns, in memory, every tracked respawn timer (scene+identifier → remaining time + destined respawn position) and ticks them continuously regardless of which scene is loaded. It also owns JSON save/load (`world_state.json`, same `Application.persistentDataPath`/trigger conventions as `SaveSystem.cs`). `WorldEventScheduler`'s timer is restructured from an opaque `WaitForSeconds` into a queryable field so the registry can read/write it. `SpawnManager` stops owning its own respawn coroutines — it registers a timer+position at death/depletion and polls the registry instead.

**Tech Stack:** Unity C#, no automated test framework — every task ends with a manual Play Mode verification step Florian runs himself.

**Spec:** `docs/superpowers/specs/2026-10-01-world-state-save-design.md`

## Global Constraints

- Timers are FROZEN while the game is closed — `Save()` captures remaining time as-is, `Load()` restores it as-is, no real-time elapsed-time catch-up is ever computed.
- An event or SpawnManager respawn-in-progress at shutdown is NOT persisted in detail — only idle/cooldown timers are saved. `WorldEventScheduler`'s `_timeUntilNextRoll` is explicitly set to `-1f` (unarmed) the moment an event starts, so a shutdown mid-event always resumes with a fresh roll, never a stuck countdown.
- v1 scope is `WorldEventScheduler`'s global countdown + `SpawnManager` zones (map bosses, rare resource nodes) ONLY — individually hand-placed `Mob.cs`/`ResourceNode.cs` instances are explicitly out of scope (no stable per-instance ID exists yet; noted in the spec for a future chantier).
- Zone timers tick continuously in `WorldStateRegistry.Update()` regardless of which scene is currently loaded — `SpawnManager` is never the timer owner anymore, it only registers and polls.
- A respawn position is rolled ONCE, at the moment of death/depletion, and stored alongside its timer — never re-rolled at the moment the actual respawn fires, so a save/reload mid-cooldown never changes the destined spot.

## Review Focus

- **A `ResourceSpawnZone` with `nodeCount > 1` has nodes deplete at different times** — each slot's timer/position must stay independent; a bug that shares one timer across the whole zone (collapsing multiple slots into one key) would silently break multi-node zones. Task 3's own verification must spawn a zone with 2+ nodes and deplete only one.
- **`WorldStateRegistry.Instance` must exist by the time `WorldEventScheduler.Start()` queries it**, even on a from-scratch scene where nothing else has touched the registry yet — using `.Exists` there instead of `.Instance` would silently skip loading a real save. Task 2 must use `.Instance` (forces creation + `Load()`), never `.Exists`, at that specific call site.
- **Two zones (or a boss zone and a resource zone) sharing the same `zoneName` on one `SpawnManager`** silently collide in the registry (one's timer/position overwrites the other's) — must warn in the Editor, not silently misbehave at runtime. Task 3 must add and verify the `OnValidate` warning.
- **A player saves the instant after a boss/node is registered as dead** (before any `Update()` tick has run) — `Save()` must still capture the FULL rolled delay, not a value that's already started decaying incorrectly or is missing because the tick hasn't "seen" it yet. Since `RegisterRespawn` writes directly into the dictionary and `Save()` reads straight from it, this should already work by construction — Task 1's verification should still exercise a save immediately after a death, not only after the cooldown has been running a while.
- **Shutdown while an event is actively running (spawned, not yet resolved)** must resume with a brand-new random interval on restart, never a `-1` (unarmed-forever) or a negative/stuck countdown. Task 2's own verification must close the game mid-event, not just mid-idle-countdown.

---

### Task 1: `Systems/WorldStateRegistry.cs` — new file, in-memory tracking + save/load

**Files:**
- Create: `Systems/WorldStateRegistry.cs`

**Interfaces:**
- Consumes: nothing new (`UnityEngine`, `System.Collections.Generic`, `System.IO` only).
- Produces: `WorldStateRegistry` singleton with `Instance`/`Exists` (same pattern as `WorldEventScheduler`/`InstanceSession`), `RegisterRespawn(string sceneName, string identifier, float delay, Vector3 position)`, `TryGetRemainingTime(string sceneName, string identifier, out float remaining, out Vector3 position)`, `ClearZone(string sceneName, string identifier)`, `SetWorldEventTimeRemaining(float remaining)`, `TryGetWorldEventTimeRemaining(out float remaining)`, `Save()`. `WorldState`/`ZoneTimerState` serializable classes. Task 2 consumes the WorldEventScheduler-facing methods; Task 3 consumes the zone-facing methods.

- [ ] **Step 1: Write `Systems/WorldStateRegistry.cs`**

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.IO;

// =============================================================
// WORLDSTATEREGISTRY.CS — État monde persistant (timers de respawn, event global)
// Path : Assets/Scripts/Systems/WorldStateRegistry.cs
// Spec : docs/superpowers/specs/2026-10-01-world-state-save-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/WorldEventScheduler.
// Source de vérité EN MÉMOIRE des timers de respawn de zone (SpawnManager) — SpawnManager
// n'est plus propriétaire de son propre décompte, il rapporte/interroge ce registre. Les timers
// courent en PERMANENCE (Update() ici), peu importe quelle scène est chargée — un boss tué sur
// la map B revient même si le joueur passe cette heure sur la map A (Florian, 2026-10-01).
//
// Porte AUSSI la persistance disque (JSON, world_state.json) — pas de classe séparée, ce
// registre est déjà la source de vérité, même esprit que SaveSystem.cs qui fait les deux pour
// CharacterProgress. Gelé pendant que le jeu est FERMÉ : Save() capture le temps restant,
// Load() le restaure tel quel, aucun calcul de temps réel écoulé hors-jeu.
// =============================================================
public class WorldStateRegistry : MonoBehaviour
{
    private static WorldStateRegistry _instance;

    public static WorldStateRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WorldStateRegistry (auto)");
                _instance = go.AddComponent<WorldStateRegistry>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    /// <summary>Même patron que InstanceSession.Exists/WorldEventScheduler.Exists — ne jamais
    /// déclencher l'auto-création juste pour vérifier qu'un registre existe.</summary>
    public static bool Exists => _instance != null;

    /// <summary>Un timer de respawn ET la position OÙ il devra réapparaître — décidée UNE FOIS
    /// à l'enregistrement (mort/épuisement), jamais re-tirée au respawn réel. Garantit qu'un
    /// save/reload en plein cooldown ne change pas la destination déjà "actée" (Florian,
    /// 2026-10-01 : "chaque clé a son timer et sa position").</summary>
    private class ZoneTimer
    {
        public float   remaining;
        public Vector3 position;
    }

    private readonly Dictionary<string, ZoneTimer> _zoneTimers = new Dictionary<string, ZoneTimer>();

    /// <summary>Countdown restant avant le prochain tirage WorldEventScheduler — géré ici pour
    /// que Save()/Load() aient un endroit unique où lire/écrire TOUT l'état monde. Lu/écrit par
    /// WorldEventScheduler lui-même via les méthodes ci-dessous (jamais directement par un tiers).</summary>
    private float _worldEventTimeRemaining = -1f; // -1 = pas encore initialisé par le scheduler

    private static string WorldSavePath => Path.Combine(Application.persistentDataPath, "world_state.json");

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Load(); // tôt — avant que WorldEventScheduler.Start() ne lise son countdown restauré
    }

    private void Update()
    {
        // Mutation en place (ZoneTimer est une classe, pas une struct) — pas besoin de copier
        // les clés, on ne touche jamais la structure du Dictionary ici, juste ses valeurs.
        foreach (var timer in _zoneTimers.Values)
            timer.remaining = Mathf.Max(0f, timer.remaining - Time.deltaTime);
    }

    // =========================================================
    // ZONES SPAWNMANAGER
    // =========================================================
    //
    // "identifier" est un identifiant GÉNÉRIQUE composé par l'appelant (SpawnManager) — le
    // registre ne sait rien de ce qu'il représente. Pour un boss de map (1 seul par zone) :
    // juste zoneName. Pour un node de ressource (plusieurs par zone, nodeCount) : zoneName + un
    // suffixe d'index stable ("Zone#0", "Zone#1"...) — chaque slot garde son propre timer/sa
    // propre position, indépendant des autres slots de la même zone.

    private static string ZoneKey(string sceneName, string identifier) => $"{sceneName}|{identifier}";

    /// <summary>Appelé par SpawnManager à la mort d'un boss/épuisement d'un node ressource —
    /// démarre (ou redémarre) le décompte de cette clé. `position` est déjà tirée par
    /// l'appelant (dans les bornes de la zone) — stockée telle quelle, jamais retirée ici.</summary>
    public void RegisterRespawn(string sceneName, string identifier, float delay, Vector3 position)
        => _zoneTimers[ZoneKey(sceneName, identifier)] = new ZoneTimer { remaining = Mathf.Max(0f, delay), position = position };

    /// <summary>Appelé par SpawnManager (à son Start() ET en polling tant que la clé est en
    /// cooldown) — true + remaining/position si encore trackée (en cooldown, remaining peut être
    /// 0 = prête à respawn MAIS pas encore nettoyée, voir ClearZone), false si jamais enregistrée
    /// (jamais mort/épuisée, ou déjà nettoyée — prête).</summary>
    public bool TryGetRemainingTime(string sceneName, string identifier, out float remaining, out Vector3 position)
    {
        if (_zoneTimers.TryGetValue(ZoneKey(sceneName, identifier), out ZoneTimer timer))
        {
            remaining = timer.remaining;
            position  = timer.position;
            return true;
        }
        remaining = 0f;
        position  = Vector3.zero;
        return false;
    }

    /// <summary>Appelé par SpawnManager une fois le respawn RÉELLEMENT déclenché (boss
    /// réinstancié / node ressource recréé) — retire l'entrée, la clé est de nouveau libre.</summary>
    public void ClearZone(string sceneName, string identifier)
        => _zoneTimers.Remove(ZoneKey(sceneName, identifier));

    // =========================================================
    // WORLDEVENTSCHEDULER
    // =========================================================

    /// <summary>Appelé par WorldEventScheduler à chaque frame de son propre countdown — tient le
    /// registre à jour pour qu'un Save() déclenché à tout moment capture la vraie valeur.</summary>
    public void SetWorldEventTimeRemaining(float remaining) => _worldEventTimeRemaining = remaining;

    /// <summary>Appelé par WorldEventScheduler à son Start() — true + remaining si une save
    /// existait déjà (reprend ce countdown), false si c'est un tout premier lancement (le
    /// scheduler tire un intervalle neuf comme avant).</summary>
    public bool TryGetWorldEventTimeRemaining(out float remaining)
    {
        remaining = _worldEventTimeRemaining;
        return _worldEventTimeRemaining >= 0f;
    }

    // =========================================================
    // SAVE / LOAD
    // =========================================================

    public void Save()
    {
        var state = new WorldState { worldEventTimeRemaining = _worldEventTimeRemaining };
        foreach (var kv in _zoneTimers)
        {
            int sep = kv.Key.IndexOf('|');
            state.zoneTimers.Add(new ZoneTimerState
            {
                sceneName            = kv.Key.Substring(0, sep),
                identifier           = kv.Key.Substring(sep + 1),
                respawnTimeRemaining = kv.Value.remaining,
                position             = kv.Value.position,
            });
        }

        try
        {
            string json = JsonUtility.ToJson(state, prettyPrint: true);
            File.WriteAllText(WorldSavePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de sauvegarde : {e.Message}");
        }
    }

    private void Load()
    {
        if (!File.Exists(WorldSavePath)) return; // premier lancement — tout reste à l'état neuf

        try
        {
            string json  = File.ReadAllText(WorldSavePath);
            var    state = JsonUtility.FromJson<WorldState>(json);
            if (state == null) return;

            _worldEventTimeRemaining = state.worldEventTimeRemaining;
            _zoneTimers.Clear();
            if (state.zoneTimers != null)
                foreach (var z in state.zoneTimers)
                    _zoneTimers[ZoneKey(z.sceneName, z.identifier)] =
                        new ZoneTimer { remaining = z.respawnTimeRemaining, position = z.position };
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de chargement, repart à neuf : {e.Message}");
        }
    }
}

[System.Serializable]
public class WorldState
{
    public float worldEventTimeRemaining = -1f;
    public List<ZoneTimerState> zoneTimers = new List<ZoneTimerState>();
}

[System.Serializable]
public class ZoneTimerState
{
    public string  sceneName;
    public string  identifier; // zoneName (boss) ou "zoneName#index" (node de ressource)
    public float   respawnTimeRemaining;
    public Vector3 position;
}
```

- [ ] **Step 2: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors. This file has no caller yet (Tasks 2/3 wire it up), so a clean compile with an unused-but-valid class is the only thing to check here.

- [ ] **Step 3: Verify Save()/Load() round-trip manually (temporary test hook)**

Before Task 2/3 exist to exercise this for real, prove the round-trip in isolation: temporarily add a debug call somewhere reachable in Play Mode (e.g. a key-bound `if (Input.GetKeyDown(KeyCode.F9))` block in any existing `Update()`), call:
`WorldStateRegistry.Instance.RegisterRespawn("TestScene", "TestZone", 42f, new Vector3(1, 2, 3));`
`WorldStateRegistry.Instance.SetWorldEventTimeRemaining(999f);`
`WorldStateRegistry.Instance.Save();`
Enter Play Mode, trigger the hook, confirm `world_state.json` appears in `Application.persistentDataPath` (log the path via `Debug.Log(Application.persistentDataPath)` if needed to find it) with the expected values. Stop Play Mode, restart it — confirm `WorldStateRegistry.Instance.TryGetRemainingTime("TestScene", "TestZone", out float r, out Vector3 p)` returns `true`, `r` ≈ 42, `p` = (1,2,3), and `TryGetWorldEventTimeRemaining(out float w)` returns `true`, `w` ≈ 999. Remove the temporary debug hook afterward — it was only to prove the round-trip before Task 2/3 exist.

- [ ] **Step 4: Commit**

```bash
git add Systems/WorldStateRegistry.cs
git commit -m "feat: add WorldStateRegistry — in-memory zone timer tracking + world_state.json save/load"
```

---

### Task 2: `Systems/WorldEventScheduler.cs` — countdown restructured to be queryable

**Files:**
- Modify: `Systems/WorldEventScheduler.cs`

**Interfaces:**
- Consumes: `WorldStateRegistry.Instance.TryGetWorldEventTimeRemaining(out float)`, `.SetWorldEventTimeRemaining(float)` (Task 1).
- Produces: nothing new for later tasks — `WorldEventScheduler`'s public surface (`eventPool`, `eligibleMaps`, `PickRandomMap()`, `TryGetRandomNavMeshPoint`, `FirstWarningOffset`/`SecondWarningOffset`, `Exists`/`Resubscribe()`) is UNCHANGED, only the private timer internals change.

- [ ] **Step 1: Read the current `Systems/WorldEventScheduler.cs`**

Read it in full — confirm it still matches the shape read while writing this plan: `Start()` just does `StartCoroutine(RunCycle())`, `RunCycle()` does `float totalDelay = Random.Range(minInterval, maxInterval); yield return new WaitForSeconds(Mathf.Max(0f, totalDelay - firstWarningOffset));` then picks/runs an event. If it has diverged, favor the real file's surrounding content (the `_activeEvent`/`Resubscribe()`/`TryGetRandomNavMeshPoint`/`OnValidate` members below `RunCycle()` must NOT be touched by this task) over this description.

- [ ] **Step 2: Replace `Start()`**

Find:

```csharp
    private void Start()
    {
        StartCoroutine(RunCycle());
    }
```

Replace with:

```csharp
    private float _timeUntilNextRoll = -1f; // -1 = pas encore armé

    private void Start()
    {
        // .Instance ICI, pas .Exists — contrairement à GameEventBus.Reset() (qui veut juste
        // savoir "y a-t-il déjà un registre" sans en créer un pour rien), le scheduler a BESOIN
        // que le registre existe pour fonctionner : s'il n'a jamais été touché avant ce point,
        // .Instance le crée et déclenche son Load() (dans son propre Awake()) — sans quoi une
        // sauvegarde pourrait rester silencieusement jamais chargée si rien d'autre n'accède au
        // registre en premier.
        if (WorldStateRegistry.Instance.TryGetWorldEventTimeRemaining(out float saved))
            _timeUntilNextRoll = saved;
        // sinon : RunCycle() tire un intervalle neuf au premier passage (comportement actuel)

        StartCoroutine(RunCycle());
    }

    private void Update()
    {
        if (_timeUntilNextRoll < 0f) return; // pas encore armé, ou event en cours (voir RunCycle)
        _timeUntilNextRoll = Mathf.Max(0f, _timeUntilNextRoll - Time.deltaTime);
        WorldStateRegistry.Instance?.SetWorldEventTimeRemaining(_timeUntilNextRoll);
    }
```

- [ ] **Step 3: Replace `RunCycle()`**

Find:

```csharp
    private IEnumerator RunCycle()
    {
        while (true)
        {
            if (eventPool == null || eventPool.Count == 0)
            {
                Debug.LogWarning("[WorldEventScheduler] eventPool vide — système inerte. Nouvelle vérification dans 5s.");
                yield return new WaitForSeconds(5f);
                continue;
            }

            float totalDelay = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(Mathf.Max(0f, totalDelay - firstWarningOffset));

            WorldEventData selected = eventPool[Random.Range(0, eventPool.Count)];
            if (selected == null) continue; // entrée vide dans la liste — reboucle direct

            _activeEvent = selected; // voir Resubscribe() — permet un re-abonnement propre si
                                      // GameEventBus.Reset() tombe pendant que cet event tourne
            yield return selected.RunEvent(this);
            _activeEvent = null;
            // selected.RunEvent gère TOUT (annonces, spawn, résolution, récompense) en utilisant
            // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien de
            // plus sur ce qui se passe à l'intérieur.
        }
    }
```

Replace with:

```csharp
    private IEnumerator RunCycle()
    {
        while (true)
        {
            if (eventPool == null || eventPool.Count == 0)
            {
                Debug.LogWarning("[WorldEventScheduler] eventPool vide — système inerte. Nouvelle vérification dans 5s.");
                yield return new WaitForSeconds(5f);
                continue;
            }

            if (_timeUntilNextRoll < 0f)
                _timeUntilNextRoll = Random.Range(minInterval, maxInterval);

            // Attend que le countdown descende jusqu'à l'offset de 1ère annonce — équivalent au
            // WaitForSeconds(totalDelay - firstWarningOffset) d'avant, mais interrogeable/
            // sauvegardable à tout moment via _timeUntilNextRoll (décompté dans Update()) au
            // lieu d'un délai opaque figé dans la coroutine.
            yield return new WaitUntil(() => _timeUntilNextRoll <= firstWarningOffset);

            WorldEventData selected = eventPool[Random.Range(0, eventPool.Count)];
            if (selected == null) { _timeUntilNextRoll = -1f; continue; }

            _timeUntilNextRoll = -1f; // event en cours — countdown "gelé/inactif" jusqu'à
                                       // résolution (l'event en cours n'est pas sauvegardé en
                                       // détail ; si le jeu ferme ici, à la réouverture ce cycle
                                       // est abandonné et un intervalle NEUF est tiré, pas de
                                       // countdown à reprendre pour un event qui n'a jamais fini)
            _activeEvent = selected; // voir Resubscribe() — permet un re-abonnement propre si
                                      // GameEventBus.Reset() tombe pendant que cet event tourne
            yield return selected.RunEvent(this);
            _activeEvent = null;
            // selected.RunEvent gère TOUT (annonces, spawn, résolution, récompense) en utilisant
            // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien de
            // plus sur ce qui se passe à l'intérieur.
        }
    }
```

- [ ] **Step 4: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors. Confirm nothing else in the file (`eventPool`, `eligibleMaps`, `PickRandomMap()`, `TryGetRandomNavMeshPoint`, `_activeEvent`/`Resubscribe()`, `OnValidate`) was touched — a diff of this file should show changes ONLY inside `Start()`/the new `Update()`/`RunCycle()`.

- [ ] **Step 5: Verify idle countdown persists (Review Focus item 4, part 1)**

Reduce `minInterval`/`maxInterval` for fast testing (e.g. 60/120). Enter Play Mode, let the countdown run partway (well before the first-warning threshold), stop Play Mode (or do a real Quit if testing a build), restart — confirm (via a temporary debug log of `_timeUntilNextRoll`, or just by timing) that the countdown resumed close to where it left off, not from a fresh `Random.Range` roll.

- [ ] **Step 6: Verify mid-event shutdown resumes fresh (Review Focus item 5)**

With the same reduced interval, let a cycle run all the way to an event actually starting (`_activeEvent` non-null, `RunEvent` coroutine running — confirm via the announcement firing). Stop Play Mode / Quit while the event is still active (not yet resolved). Restart — confirm the event from before is NOT resumed (no leftover announcement, no spawned mobs) and a brand-new countdown is rolled (not stuck at `-1` forever, not negative).

- [ ] **Step 7: Commit**

```bash
git add Systems/WorldEventScheduler.cs
git commit -m "feat: restructure WorldEventScheduler timer to be queryable via WorldStateRegistry"
```

---

### Task 3: `Systems/SpawnManager.cs` — delegate respawn timers to the registry

**Files:**
- Modify: `Systems/SpawnManager.cs`

**Interfaces:**
- Consumes: `WorldStateRegistry.Instance.RegisterRespawn(string, string, float, Vector3)`, `.TryGetRemainingTime(string, string, out float, out Vector3)`, `.ClearZone(string, string)` (Task 1).
- Produces: nothing later in this plan consumes — `SpawnManager`'s public surface (`Instance`, `zones`, `resourceZones`) is unchanged; `ResourceSpawnZone.aliveNodes: List<GameObject>`/`.pendingRespawns: int` are REMOVED (replaced by `nodeSlots: GameObject[]`) — if any other file reads those two fields, this task's Step 1 read must catch it (grep before editing).

- [ ] **Step 1: Read the current `Systems/SpawnManager.cs`, and grep for external readers of the fields being removed**

Read the file in full — confirm it still matches the shape read while writing this plan (`SpawnZone.aliveBoss`/`.pendingRespawn`, `ResourceSpawnZone.aliveNodes`/`.pendingRespawns`, `Update()`'s boss-respawn trigger block, `SpawnBoss`/`RespawnBossAfterDelay`/`SpawnOneNode`/`RespawnNodeAfterDelay`, `OnDrawGizmos`'s use of `aliveNodes.Count`). Then search the rest of the codebase for any other reader of `ResourceSpawnZone.aliveNodes` or `.pendingRespawns` (`grep -rn "aliveNodes\|pendingRespawns" --include=*.cs`) — if anything outside `SpawnManager.cs` itself reads these, note it and adjust Step 3's gizmo code (and any such external reader) to use `nodeSlots` instead before replacing the file.

- [ ] **Step 2: Replace `SpawnZone`'s runtime fields**

Find:

```csharp
        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public GameObject aliveBoss;
        [HideInInspector] public bool       pendingRespawn;
    }
```

(this is the end of the `SpawnZone` class — the one with `minRespawnDelay`/`maxRespawnDelay` for boss de map, NOT the `ResourceSpawnZone` one a few lines below). Replace with:

```csharp
        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public GameObject aliveBoss;
    }
```

(`pendingRespawn` is removed — `WorldStateRegistry` is now the sole source of truth for "is this zone in cooldown," no local mirror needed.)

- [ ] **Step 3: Replace `ResourceSpawnZone`'s runtime fields**

Find:

```csharp
        [Header("Runtime — ne pas modifier")]
        [HideInInspector] public List<GameObject> aliveNodes     = new List<GameObject>();
        [HideInInspector] public int              pendingRespawns = 0;
    }
```

Replace with:

```csharp
        [Header("Runtime — ne pas modifier")]
        [Tooltip("Taille nodeCount — un slot null est soit pas encore spawn, soit en cooldown " +
                 "(voir WorldStateRegistry, identifier = \"zoneName#index\"). Remplace " +
                 "l'ancien aliveNodes/pendingRespawns, qui ne portaient aucune identité par node.")]
        [HideInInspector] public GameObject[] nodeSlots;
    }
```

- [ ] **Step 4: Add the scene-name field and `OnValidate` duplicate-zoneName warning**

Find:

```csharp
    [Header("Zones de spawn — Ressources rares")]
    public List<ResourceSpawnZone> resourceZones = new List<ResourceSpawnZone>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        foreach (SpawnZone zone in zones)
        {
            if (zone.mobEntries == null) continue;
            foreach (MobSpawnEntry entry in zone.mobEntries)
                if (entry.mobData != null && entry.mobData.mobType != MobType.WorldBoss)
                    Debug.LogWarning($"[SpawnManager] {zone.zoneName} : {entry.mobData.mobName} a mobType = " +
                        $"{entry.mobData.mobType}, attendu WorldBoss pour un boss de map errant.");
        }
    }
#endif
```

Replace with:

```csharp
    [Header("Zones de spawn — Ressources rares")]
    public List<ResourceSpawnZone> resourceZones = new List<ResourceSpawnZone>();

    private string _sceneName;

#if UNITY_EDITOR
    private void OnValidate()
    {
        foreach (SpawnZone zone in zones)
        {
            if (zone.mobEntries == null) continue;
            foreach (MobSpawnEntry entry in zone.mobEntries)
                if (entry.mobData != null && entry.mobData.mobType != MobType.WorldBoss)
                    Debug.LogWarning($"[SpawnManager] {zone.zoneName} : {entry.mobData.mobName} a mobType = " +
                        $"{entry.mobData.mobType}, attendu WorldBoss pour un boss de map errant.");
        }

        // zoneName est la base de l'identifiant WorldStateRegistry ("scene|zoneName" pour un
        // boss, "scene|zoneName#index" pour un node) — un doublon entre deux zones (même boss/
        // boss+ressource) ferait collisionner leurs timers dans le registre.
        var seenNames = new HashSet<string>();
        foreach (SpawnZone zone in zones)
            if (!string.IsNullOrEmpty(zone.zoneName) && !seenNames.Add(zone.zoneName))
                Debug.LogWarning($"[SpawnManager] zoneName \"{zone.zoneName}\" en double — collision " +
                    "possible dans WorldStateRegistry, renomme l'une des deux zones.");
        foreach (ResourceSpawnZone rZone in resourceZones)
            if (!string.IsNullOrEmpty(rZone.zoneName) && !seenNames.Add(rZone.zoneName))
                Debug.LogWarning($"[SpawnManager] zoneName \"{rZone.zoneName}\" en double — collision " +
                    "possible dans WorldStateRegistry, renomme l'une des deux zones.");
    }
#endif
```

(`seenNames` is shared across BOTH `zones` and `resourceZones` — a boss zone and a resource zone sharing the same `zoneName` on the same `SpawnManager` is just as much a collision as two zones of the same kind.)

- [ ] **Step 5: Capture `_sceneName` and rewrite `SpawnNextFrame()` to check the registry before spawning**

Find:

```csharp
    private IEnumerator SpawnNextFrame()
    {
        yield return null;

        NavMeshSurface surface = FindObjectOfType<NavMeshSurface>();
        if (surface != null)
            surface.BuildNavMesh();

        yield return null;
        yield return null;

        foreach (SpawnZone zone in zones)
            SpawnBoss(zone);

        foreach (ResourceSpawnZone rZone in resourceZones)
            SpawnAllNodesInZone(rZone);

        _initialSpawnDone = true;
    }
```

Replace with:

```csharp
    private IEnumerator SpawnNextFrame()
    {
        yield return null;

        _sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        NavMeshSurface surface = FindObjectOfType<NavMeshSurface>();
        if (surface != null)
            surface.BuildNavMesh();

        yield return null;
        yield return null;

        foreach (SpawnZone zone in zones)
        {
            // Zone en cooldown restaurée (save existante) — ne spawn pas tout de suite,
            // Update() la surveillera. Zone jamais enregistrée (premier lancement de session) —
            // spawn immédiat, comportement d'avant ce chantier.
            if (!WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, zone.zoneName, out _, out _))
                SpawnBoss(zone);
        }

        foreach (ResourceSpawnZone rZone in resourceZones)
            SpawnAllNodesInZone(rZone);

        _initialSpawnDone = true;
    }
```

- [ ] **Step 6: Rewrite `Update()`'s boss-respawn block to poll the registry**

Find:

```csharp
        // ── Boss de map ──────────────────────────────────────
        foreach (SpawnZone zone in zones)
        {
            if (zone.aliveBoss == null && !zone.pendingRespawn)
            {
                zone.pendingRespawn = true;
                float delay = Random.Range(zone.minRespawnDelay, zone.maxRespawnDelay);
                StartCoroutine(RespawnBossAfterDelay(zone, delay));
            }
        }

        // ── Ressources — nettoyage null ───────────────────────
        foreach (ResourceSpawnZone rZone in resourceZones)
            rZone.aliveNodes.RemoveAll(n => n == null);
```

Replace with:

```csharp
        // ── Boss de map ──────────────────────────────────────
        foreach (SpawnZone zone in zones)
        {
            if (zone.aliveBoss != null) continue; // boss vivant, rien à faire

            if (WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, zone.zoneName, out float remaining, out Vector3 pos)
                && remaining <= 0f)
            {
                WorldStateRegistry.Instance.ClearZone(_sceneName, zone.zoneName);
                SpawnBoss(zone, pos);
            }
        }

        // ── Ressources ────────────────────────────────────────
        foreach (ResourceSpawnZone rZone in resourceZones)
        {
            if (rZone.nodeSlots == null) continue;
            for (int i = 0; i < rZone.nodeSlots.Length; i++)
            {
                if (rZone.nodeSlots[i] != null) continue; // node vivant, rien à faire

                string identifier = $"{rZone.zoneName}#{i}";
                if (WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, identifier, out float remaining, out Vector3 pos)
                    && remaining <= 0f)
                {
                    WorldStateRegistry.Instance.ClearZone(_sceneName, identifier);
                    SpawnOneNode(rZone, i, pos);
                }
            }
        }
```

- [ ] **Step 7: Rewrite `SpawnBoss`/remove `RespawnBossAfterDelay`, register on death**

Find:

```csharp
    private void SpawnBoss(SpawnZone zone)
    {
        MobData chosenData = RollMobData(zone);
        if (chosenData == null) { Debug.LogWarning($"[SPAWN] {zone.zoneName} — RollMobData retourne null"); return; }
        if (chosenData.prefab == null) { Debug.LogWarning($"[SpawnManager] {chosenData.mobName} n'a pas de prefab !"); return; }

        int     level    = Random.Range(zone.minLevel, zone.maxLevel + 1);
        Vector3 spawnPos = GetRandomPosition(zone.center, zone.size);

        GameObject mobObj = Instantiate(chosenData.prefab, spawnPos, Quaternion.identity);
        Mob mob = mobObj.GetComponent<Mob>();
        if (mob != null)
        {
            mob.InitializeSpawn(chosenData, level);
            var capturedZone = zone;
            mob.OnDeath(() => capturedZone.aliveBoss = null);
        }
        zone.aliveBoss = mobObj;
    }

    private IEnumerator RespawnBossAfterDelay(SpawnZone zone, float delay)
    {
        yield return new WaitForSeconds(delay);
        zone.pendingRespawn = false;
        SpawnBoss(zone);
    }
```

Replace with:

```csharp
    /// <summary>spawnPos : null = premier spawn de session (tire une position fraîche dans la
    /// zone) ; une valeur fournie = respawn restauré depuis WorldStateRegistry, à LA POSITION
    /// DÉJÀ DÉCIDÉE à la mort précédente (jamais re-tirée ici).</summary>
    private void SpawnBoss(SpawnZone zone, Vector3? spawnPos = null)
    {
        MobData chosenData = RollMobData(zone);
        if (chosenData == null) { Debug.LogWarning($"[SPAWN] {zone.zoneName} — RollMobData retourne null"); return; }
        if (chosenData.prefab == null) { Debug.LogWarning($"[SpawnManager] {chosenData.mobName} n'a pas de prefab !"); return; }

        int     level = Random.Range(zone.minLevel, zone.maxLevel + 1);
        Vector3 pos   = spawnPos ?? GetRandomPosition(zone.center, zone.size);

        GameObject mobObj = Instantiate(chosenData.prefab, pos, Quaternion.identity);
        Mob mob = mobObj.GetComponent<Mob>();
        if (mob != null)
        {
            mob.InitializeSpawn(chosenData, level);
            var capturedZone = zone;
            mob.OnDeath(() =>
            {
                capturedZone.aliveBoss = null;
                Vector3 nextPos = GetRandomPosition(capturedZone.center, capturedZone.size);
                float   delay   = Random.Range(capturedZone.minRespawnDelay, capturedZone.maxRespawnDelay);
                WorldStateRegistry.Instance.RegisterRespawn(_sceneName, capturedZone.zoneName, delay, nextPos);
            });
        }
        zone.aliveBoss = mobObj;
    }
```

(`RespawnBossAfterDelay` is deleted entirely — `Update()`'s polling, Step 6, replaces it.)

- [ ] **Step 8: Rewrite `SpawnAllNodesInZone`/`SpawnOneNode`/remove `RespawnNodeAfterDelay`, register on depletion**

Find:

```csharp
    private void SpawnAllNodesInZone(ResourceSpawnZone rZone)
    {
        for (int i = 0; i < rZone.nodeCount; i++)
            SpawnOneNode(rZone);
    }

    private void SpawnOneNode(ResourceSpawnZone rZone)
    {
        ResourceData data = RollResourceData(rZone);
        if (data == null)
        {
            Debug.LogWarning($"[SPAWN] {rZone.zoneName} — aucune ResourceData valide.");
            return;
        }
        if (data.nodePrefab == null)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — nodePrefab non assigné !");
            return;
        }
        if (data.resourceType != ResourceType.Collectible)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — resourceType doit être Collectible !");
            return;
        }

        Vector3    spawnPos = GetRandomPosition(rZone.center, rZone.size);
        GameObject nodeObj  = Instantiate(data.nodePrefab, spawnPos, Quaternion.identity);

        // Ajoute ResourceNode si absent sur le prefab
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null) node = nodeObj.AddComponent<ResourceNode>();

        var capturedZone = rZone;

        node.InitFromSpawner(data, () =>
        {
            capturedZone.aliveNodes.Remove(nodeObj);
            capturedZone.pendingRespawns++;
            StartCoroutine(RespawnNodeAfterDelay(capturedZone));
        });

        rZone.aliveNodes.Add(nodeObj);
    }

    private IEnumerator RespawnNodeAfterDelay(ResourceSpawnZone rZone)
    {
        yield return new WaitForSeconds(Random.Range(rZone.minRespawnDelay, rZone.maxRespawnDelay));
        rZone.pendingRespawns--;
        SpawnOneNode(rZone);
    }
```

Replace with:

```csharp
    private void SpawnAllNodesInZone(ResourceSpawnZone rZone)
    {
        rZone.nodeSlots = new GameObject[rZone.nodeCount];
        for (int i = 0; i < rZone.nodeCount; i++)
        {
            // Slot en cooldown restauré (save existante) — ne spawn pas tout de suite, Update()
            // le surveillera. Slot jamais enregistré (premier lancement) — spawn immédiat.
            if (!WorldStateRegistry.Instance.TryGetRemainingTime(_sceneName, $"{rZone.zoneName}#{i}", out _, out _))
                SpawnOneNode(rZone, i, null);
        }
    }

    /// <summary>spawnPos : null = premier spawn de session (tire une position fraîche) ; une
    /// valeur fournie = respawn restauré depuis WorldStateRegistry, à LA POSITION DÉJÀ DÉCIDÉE à
    /// l'épuisement précédent (jamais re-tirée ici).</summary>
    private void SpawnOneNode(ResourceSpawnZone rZone, int slotIndex, Vector3? spawnPos)
    {
        ResourceData data = RollResourceData(rZone);
        if (data == null)
        {
            Debug.LogWarning($"[SPAWN] {rZone.zoneName} — aucune ResourceData valide.");
            return;
        }
        if (data.nodePrefab == null)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — nodePrefab non assigné !");
            return;
        }
        if (data.resourceType != ResourceType.Collectible)
        {
            Debug.LogWarning($"[SpawnManager] {data.itemID} — resourceType doit être Collectible !");
            return;
        }

        Vector3    pos     = spawnPos ?? GetRandomPosition(rZone.center, rZone.size);
        GameObject nodeObj = Instantiate(data.nodePrefab, pos, Quaternion.identity);

        // Ajoute ResourceNode si absent sur le prefab
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null) node = nodeObj.AddComponent<ResourceNode>();

        var capturedZone = rZone;
        var capturedSlot = slotIndex;

        node.InitFromSpawner(data, () =>
        {
            capturedZone.nodeSlots[capturedSlot] = null;
            Vector3 nextPos = GetRandomPosition(capturedZone.center, capturedZone.size);
            float   delay   = Random.Range(capturedZone.minRespawnDelay, capturedZone.maxRespawnDelay);
            WorldStateRegistry.Instance.RegisterRespawn(_sceneName, $"{capturedZone.zoneName}#{capturedSlot}", delay, nextPos);
        });

        rZone.nodeSlots[slotIndex] = nodeObj;
    }
```

(`RespawnNodeAfterDelay` is deleted entirely — `Update()`'s polling, Step 6, replaces it.)

- [ ] **Step 9: Fix `OnDrawGizmos`' reference to the removed `aliveNodes.Count`**

Find:

```csharp
                UnityEditor.Handles.Label(rZone.center + Vector3.up * 2f,
                    $"[RES] {rZone.zoneName}\n{rZone.aliveNodes.Count}/{rZone.nodeCount}");
```

Replace with:

```csharp
                int aliveCount = 0;
                if (rZone.nodeSlots != null)
                    foreach (var slot in rZone.nodeSlots)
                        if (slot != null) aliveCount++;
                UnityEditor.Handles.Label(rZone.center + Vector3.up * 2f,
                    $"[RES] {rZone.zoneName}\n{aliveCount}/{rZone.nodeCount}");
```

- [ ] **Step 10: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors. In particular confirm no leftover reference to `aliveNodes`/`pendingRespawns`/`pendingRespawn`/`RespawnBossAfterDelay`/`RespawnNodeAfterDelay` anywhere in the file (all should be gone).

- [ ] **Step 11: Verify first-session spawn is unaffected (baseline)**

Enter Play Mode on a scene with a `SpawnManager` (no prior `world_state.json`, or delete it first) — confirm boss zones and resource zones spawn immediately exactly as before this task (no behavior change for the "nothing saved yet" case).

- [ ] **Step 12: Verify boss respawn timer + position persist (Review Focus item 2, Global Constraint on position)**

Kill a map boss. Note its zone's last position just before death isn't relevant — note instead that a respawn position gets picked at THIS moment. Confirm it doesn't respawn immediately (`Update()`'s poll sees `remaining > 0`). Stop Play Mode (or Quit for real), restart — confirm the boss is still on cooldown (not respawned instantly) and, once the remaining time elapses, spawns at the SAME position recorded in `world_state.json` (open the file and compare the `position` field for that zone's entry against where the boss actually appears).

- [ ] **Step 13: Verify multi-node resource zones track independently (Review Focus item 1)**

Configure a `ResourceSpawnZone` with `nodeCount = 3`. Deplete exactly ONE node. Confirm the other two remain alive and unaffected (their `nodeSlots` entries still non-null). Confirm `world_state.json` after a save shows exactly one `ZoneTimerState` entry for that zone (identifier ending in the depleted slot's index, e.g. `"...#1"`), not three, and not one shared entry for the whole zone.

- [ ] **Step 14: Verify the duplicate-`zoneName` Editor warning**

In the Inspector, set two zones (either two `SpawnZone`, two `ResourceSpawnZone`, or one of each) on the same `SpawnManager` to the same `zoneName`. Confirm the `[SpawnManager] zoneName "..." en double` warning appears in the Console. Rename one back to something unique, confirm the warning stops appearing on the next edit.

- [ ] **Step 15: Verify a cooldown keeps ticking while away on another scene (Global Constraint — timers tick regardless of loaded scene)**

Kill a map boss on Map A (note the remaining time in `world_state.json`, or via a temporary debug log of the registry's value). Travel to Map B (a normal map transition, not a quit). Wait a couple of real minutes while exploring Map B. Travel back to Map A — confirm the remaining time has dropped by roughly the real time spent away, NOT frozen at the value it had when you left (that would mean `SpawnManager`'s own `Update()` was still somehow gating the countdown instead of `WorldStateRegistry`'s own `Update()` ticking it independently of which scene is loaded).

- [ ] **Step 16: Commit**

```bash
git add Systems/SpawnManager.cs
git commit -m "feat: SpawnManager delegates respawn timers/positions to WorldStateRegistry"
```

---

### Task 4: Final save trigger wiring + end-to-end verification

**Files:**
- Modify: `Events/SceneLoader.cs` (add the `WorldStateRegistry.Save()` call alongside the existing player save)
- Modify: `Progression/Save/SaveSystem.cs` (add the `WorldStateRegistry.Save()` call alongside `OnApplicationQuit`'s AND the Editor-only `OnDisable`'s existing player save — `OnDisable` fires on every Play Mode stop, which is the trigger Florian will actually hit while testing in-editor, not a real standalone quit)

**Interfaces:**
- Consumes: `WorldStateRegistry.Instance.Save()`, `WorldStateRegistry.Exists` (Task 1).
- Produces: nothing later in this plan consumes.

- [ ] **Step 1: Read `Events/SceneLoader.cs` around its existing save call**

Read the file, find the exact line already identified while writing this plan: `if (player != null) SaveSystem.Instance?.Save(player);` inside the map-change flow. Confirm the surrounding context still matches (this line is the ONLY save trigger in this file).

- [ ] **Step 2: Add the world-state save alongside it**

Find:

```csharp
            if (player != null) SaveSystem.Instance?.Save(player);
```

Replace with:

```csharp
            if (player != null) SaveSystem.Instance?.Save(player);
            if (WorldStateRegistry.Exists) WorldStateRegistry.Instance.Save();
```

(`.Exists` here, not `.Instance` — unlike `WorldEventScheduler.Start()` in Task 2, there is no reason to force-create a registry on every map change if nothing has ever registered a timer; if it doesn't exist yet there is nothing to save.)

- [ ] **Step 3: Add the world-state save inside `OnApplicationQuit`**

In `Progression/Save/SaveSystem.cs`, find:

```csharp
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player, isQuitting: true);
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
```

Replace with:

```csharp
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player, isQuitting: true);
        if (WorldStateRegistry.Exists) WorldStateRegistry.Instance.Save();
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
```

(This find string is anchored on the boundary between `OnApplicationQuit()`'s closing brace and the following `OnDisable()` — both methods end with the identical `var player = FindObjectOfType<Player>(); if (player != null) Save(player, isQuitting: true);` pair, so anchoring on this one includes the following method's opening to disambiguate which occurrence this is.)

- [ ] **Step 4: Add the world-state save inside the Editor-only `OnDisable`**

Immediately below (still in `SaveSystem.cs`), find:

```csharp
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player, isQuitting: true);
    }
#endif
```

Replace with:

```csharp
        var player = FindObjectOfType<Player>();
        if (player != null) Save(player, isQuitting: true);
        if (WorldStateRegistry.Exists) WorldStateRegistry.Instance.Save();
    }
#endif
```

(This is now the only remaining occurrence of this exact pair after Step 3's edit — the `#endif` immediately after disambiguates it as `OnDisable`'s closing, not `OnApplicationQuit`'s.)

- [ ] **Step 5: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors.

- [ ] **Step 6: Verify save fires on map change (Global Constraint, end-to-end)**

With a boss on cooldown, change map via a portal/normal map transition (not a manual `WorldStateRegistry.Instance.Save()` test hook this time) — confirm `world_state.json` updates (check its last-modified timestamp or diff its content) without needing to quit the whole application.

- [ ] **Step 7: Verify save fires on real application quit (Global Constraint, end-to-end)**

With a boss on cooldown, use a REAL Quit (if testing a built executable) or confirm `OnApplicationQuit` actually fires in your test setup (Unity Editor Play Mode stop does NOT reliably call `OnApplicationQuit` the same way a real quit does — if only testing in-Editor, rely on Step 6's map-change save as the practical verification path, and note to Florian that a real standalone build quit should also be spot-checked once one exists).

- [ ] **Step 8: Verify corrupt/missing save file doesn't block the game (Global Constraint, error handling)**

Delete `world_state.json`, start the game — confirm no errors, everything spawns fresh (already covered in Task 3 Step 11, re-confirm here end-to-end). Then corrupt it (open in a text editor, delete a brace or add garbage text), start the game — confirm a `[WorldStateRegistry] Échec de chargement` error logs but the game continues normally, zones spawn as if fresh.

- [ ] **Step 9: Report back**

Tell me what worked and what didn't.
