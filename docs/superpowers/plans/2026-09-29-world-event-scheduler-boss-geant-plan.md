# World Event Scheduler + Boss Géant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first timed random-interval world event (a reusable scheduler mechanism) with Boss Géant as its first real content.

**Architecture:** A new global `WorldEventScheduler` singleton (`DontDestroyOnLoad`, same family as `InstanceSession`/`AerisSystem`) runs a single coroutine cycling Idle → Decision (T-5min) → Second warning (T-1min) → Spawn-check (T-0) → Active (boss alive, resolved by death or a 30-minute timeout) → back to Idle. A new `WorldBossData` ScriptableObject holds **all** of the event's configuration (boss pool, timer interval, announcement offsets, eligible maps/paliers, despawn timeout) as a single shared asset — `WorldEventScheduler` is a pure engine with no config fields of its own, reading everything from the one `eventData` asset reference (same shape as `Player.prestigeAuraData`, revised mid-implementation per Florian's explicit request — see Ruling below). The boss spawns at a random point on the current scene's baked NavMesh (no per-map configuration) and is a completely ordinary `Mob` — the existing `GameEventBus.OnMobKilled` → `LootManager`/`XPSystem` pipeline (including `LootTable.prestigeReward`) already grants rewards with zero new code.

**Tech Stack:** Unity C#, no automated test framework — every task ends with a manual Play Mode verification step Florian runs himself.

**Spec:** `docs/superpowers/specs/2026-09-29-world-event-scheduler-boss-geant-design.md`

**Ruling (2026-09-29, mid-implementation):** the first version of this plan split config between `WorldBossData` (boss pool only) and Inspector fields directly on `WorldEventScheduler` (interval/offsets/eligibleMaps/timeout). Florian reviewed the Task 1 output and asked for all of it to live on `WorldBossData` instead — "le world boss data devrait être les paramètres de tout l'événement" — matching the `PrestigeAuraData` precedent (one shared config asset, not scattered MonoBehaviour fields). This plan reflects that corrected shape throughout; `WorldEventMapEntry` also moved into `Data/Content/WorldBossData.cs` alongside `WorldBossData` itself.

## Global Constraints

- One active event at a time — the next cycle never starts until the previous one is fully resolved (killed or timed out). No overlapping events.
- Solo-scoped spawn check: the boss only spawns if the player's currently active scene matches the randomly rolled palier's scene at the T-0 moment — read fresh at T-0, never cached from decision time.
- Boss level = the rolled palier number directly (no formula, no per-palier table — stat/level balancing isn't calibrated anywhere in the project yet).
- No fixed spawn point of any kind — the spawn position comes from `NavMesh.CalculateTriangulation()` on the currently active scene, zero per-map configuration.
- Despawn timeout: 30 minutes (1800s) after spawn if the boss isn't killed — value lives on `WorldBossData.despawnTimeout`, not hardcoded.
- The boss must be an ordinary `Mob` — do not add any bespoke reward-granting code; `Mob.Die()` already publishes `MobKilledEvent`, and `LootManager`/`Progression/XPSystem.cs` already consume it for items/Aeris/XP/Prestige.
- `WorldEventScheduler` is a `DontDestroyOnLoad` singleton — copy the exact lazy-singleton + `Awake()` guard pattern from `Systems/InstanceSession.cs:39-50` and `:116-126`, do not reinvent it.
- **All event configuration lives on `WorldBossData` (the shared asset), never as Inspector fields directly on `WorldEventScheduler`.**

## Review Focus

- **`eventData` unassigned, or its `possibleBosses`/`eligibleMaps` empty** (fresh `WorldEventScheduler` just placed in the scene) — the coroutine must not crash and must not permanently give up; it should log a warning and keep rechecking periodically so a live in-Editor fix (assigning the asset/lists while Play Mode is running) is picked up without needing a restart.
- **Player changes scene during the announcement window** (between the T-5min decision and the T-0 spawn check) — the T-0 comparison must read `SceneManager.GetActiveScene().name` fresh at T-0, never a scene name captured earlier at decision time.
- **No baked NavMesh in the active scene at spawn time** (`NavMesh.CalculateTriangulation()` returns an empty/near-empty result) — must not crash, must cancel just this occurrence (log + return to Idle), not throw or hang the whole scheduler.
- **Chosen boss `MobData.prefab` is null** (a content-authoring mistake, e.g. an empty test `MobData` dropped into `possibleBosses`) — must not crash, must cancel just this occurrence, matching the exact guard style already in `Systems/SpawnManager.cs`'s `SpawnBoss()`.
- **Boss killed at (almost) the same moment the 30-minute timeout coroutine also fires** — both paths call the same resolution point; a re-entrancy guard must ensure the event resolves exactly once (no double announcement, no double reward side-effect, no dangling coroutine still running after the event already ended).

---

### Task 1: `WorldBossData` ScriptableObject — full event configuration

**Files:**
- Create: `Data/Content/WorldBossData.cs`

**Interfaces:**
- Consumes: `MobData` (existing, `Data/Mobs/MobData.cs`) — reads `.mobType`, `.mobName`, `.prefab`. `SceneAsset` (Unity, editor-only).
- Produces: `WorldEventMapEntry` class (`mapScene`/`sceneName`/`palier`) and `WorldBossData` class with `possibleBosses: List<MobData>`, `minInterval`/`maxInterval`/`firstWarningOffset`/`secondWarningOffset`/`despawnTimeout: float`, `eligibleMaps: List<WorldEventMapEntry>` — Task 2 references this type and every one of these field names directly on a `WorldEventScheduler.eventData` reference.

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDBOSSDATA.CS — Configuration complète de l'événement Boss Géant
// Path : Assets/Scripts/Data/Content/WorldBossData.cs
// Spec : docs/superpowers/specs/2026-09-29-world-event-scheduler-boss-geant-design.md
//
// Un seul asset, glissé sur WorldEventScheduler.eventData — même patron que
// Player.prestigeAuraData (Data/Progression/PrestigeAuraData.cs) : toute la config d'un système
// dans UN asset partagé, éditable sans recompiler, plutôt que dispersée sur les champs Inspector
// d'un composant attaché à une scène précise (Florian, 2026-09-29 : "le world boss data devrait
// être les paramètres de tout l'événement").
//
// Le niveau du boss n'est PAS stocké ici : il est fixé au palier de la map tirée au moment de
// l'événement (mobLevel = palier, voir WorldEventScheduler), pas une propriété de l'asset.
// =============================================================

[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — sceneName se remplit automatiquement (voir " +
             "WorldBossData.OnValidate). Editor-only, n'existe pas en build : sceneName reste le " +
             "champ réellement lu au runtime, même convention que Portal.targetMapScene/" +
             "DungeonMapData.mapScene.")]
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;

    [Tooltip("Palier de CETTE scène — doit correspondre au MapInfo.palier posé dans la scène " +
             "elle-même. Pas de lecture automatique possible (une scène non chargée n'a pas de " +
             "MapInfo accessible) : à retaper ici à la main, une seule fois à la config.")]
    public int palier;
}

[CreateAssetMenu(fileName = "wboss_", menuName = "AetherTree/Contenu/WorldBossData")]
public class WorldBossData : ScriptableObject
{
    [Header("Boss")]
    [Tooltip("Un mob est tiré au hasard parmi ceux-ci à chaque événement Boss Géant. Devrait " +
             "avoir MobType = BossWorld (juste un avertissement si un autre type est glissé ici, " +
             "pas un blocage — même discipline que le reste du projet).")]
    public List<MobData> possibleBosses = new List<MobData>();

    [Header("Timer — intervalle aléatoire entre deux événements (secondes)")]
    [Tooltip("Défaut proche de SpawnManager (boss de map, 3600-7200s). Pour tester en Play Mode " +
             "sans attendre une heure, réduis CES DEUX VALEURS *ET* les deux offsets d'annonce " +
             "ci-dessous ENSEMBLE (ex: min/max=30/60, offsets=10/3) — les offsets sont un délai " +
             "AVANT le spawn, ils doivent rester plus petits que l'intervalle pour garder un sens.")]
    public float minInterval = 3600f;
    public float maxInterval = 7200f;

    [Header("Annonces — délai AVANT le spawn (secondes)")]
    public float firstWarningOffset  = 300f; // 5 min
    public float secondWarningOffset = 60f;  // 1 min

    [Header("Paliers éligibles")]
    public List<WorldEventMapEntry> eligibleMaps = new List<WorldEventMapEntry>();

    [Header("Résolution")]
    [Tooltip("Si le boss n'est pas tué dans ce délai après son spawn, il despawn.")]
    public float despawnTimeout = 1800f; // 30 min

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleBosses != null)
            foreach (var boss in possibleBosses)
                if (boss != null && boss.mobType != MobType.BossWorld)
                    Debug.LogWarning($"[WorldBossData] {name} : {boss.mobName} a mobType = " +
                        $"{boss.mobType}, attendu BossWorld pour un Boss Géant.");

        if (eligibleMaps != null)
            foreach (var entry in eligibleMaps)
                if (entry.mapScene != null) entry.sceneName = entry.mapScene.name;
    }
#endif
}
```

- [ ] **Step 2: Verify it compiles and the asset is creatable**

In Unity: wait for the compile to finish (no errors in the Console), then `Assets > Create > AetherTree > Contenu > WorldBossData` — confirm the menu item exists and creates an asset with an empty `Possible Bosses` list, an empty `Eligible Maps` list, and the default timer/offset/timeout values (`3600`/`7200`/`300`/`60`/`1800`) visible in the Inspector.

- [ ] **Step 3: Verify the boss-type warning**

Drag any existing `MobData` asset whose `mobType` is NOT `BossWorld` (e.g. a `Normal` mob) into `possibleBosses` — confirm a `[WorldBossData]` warning appears in the Console naming that mob and its actual type. Remove it again (or leave it, it's just a warning, not a block) before moving on.

- [ ] **Step 4: Verify the scene-name sync**

Add an entry to `Eligible Maps`, drag `Map_01`'s scene asset into `Map Scene` — confirm (via a temporary un-hide of `sceneName`, or a temporary `Debug.Log` in `OnValidate`) that `sceneName` fills in as `"Map_01"` automatically, not empty or stale.

- [ ] **Step 5: Commit**

```bash
git add Data/Content/WorldBossData.cs
git commit -m "feat: add WorldBossData asset holding all Boss Géant event configuration"
```

---

### Task 2: `WorldEventScheduler` — engine, spawn, and resolution

**Files:**
- Create: `Systems/WorldEventScheduler.cs`

**Interfaces:**
- Consumes: `WorldBossData` and `WorldEventMapEntry` (Task 1, `Data/Content/WorldBossData.cs`) — reads `eventData.possibleBosses`/`.minInterval`/`.maxInterval`/`.firstWarningOffset`/`.secondWarningOffset`/`.eligibleMaps`/`.despawnTimeout` and, per entry, `.sceneName`/`.palier`. `AnnoncePanel.Instance?.Announce(string message)` (existing, `UI/PanelFixe/AnnoncePanel.cs:53`). `Mob.OnDeath(System.Action callback)` (existing, `Entities/Mob.cs:596`). `Mob.data`/`Mob.mobLevel` (existing public fields, same ones `Systems/SpawnManager.cs`'s `SpawnBoss()` already assigns). `MobData.prefab`/`MobData.mobName` (existing).
- Produces: `WorldEventScheduler.Instance` (static, lazy singleton) and a public `WorldBossData eventData` field — nothing later in this plan consumes it further, but future Invasion work (not part of this plan) is expected to reuse this same class's timer pattern as a reference.

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;

// =============================================================
// WORLDEVENTSCHEDULER.CS — Moteur de l'événement mondial à intervalle aléatoire
// Path : Assets/Scripts/Systems/WorldEventScheduler.cs
// Spec : docs/superpowers/specs/2026-09-29-world-event-scheduler-boss-geant-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/AerisSystem. Toute la
// config (intervalle, offsets d'annonce, paliers éligibles, timeout) vit sur UN asset partagé
// (eventData, voir Data/Content/WorldBossData.cs) — ce composant est un pur moteur, pas de
// champs de config directement dessus. Florian place ce composant à la main sous _Managers et
// glisse l'asset dans eventData — l'auto-création (Instance ci-dessous) reste un filet de
// sécurité comme partout ailleurs dans le projet, mais sortirait avec eventData vide — système
// inerte tant que personne ne l'assigne (voir RunCycle, garde de config).
//
// Réutilisable plus tard pour Invasion (roadmap #3, même famille de timer aléatoire) — PAS pour
// Combat à Vague (roadmap #4, timer à HEURE FIXE, mécanisme différent, à ne jamais fusionner
// avec celui-ci).
//
// Un seul événement actif à la fois : RunCycle() bloque sur SpawnAndWaitForResolution() jusqu'à
// résolution complète (mort ou timeout) avant de reboucler — aucun chevauchement possible par
// construction, pas besoin d'un garde explicite en plus.
// =============================================================
public class WorldEventScheduler : MonoBehaviour
{
    private static WorldEventScheduler _instance;

    public static WorldEventScheduler Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WorldEventScheduler (auto)");
                _instance = go.AddComponent<WorldEventScheduler>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    [Tooltip("Toute la config de l'événement (boss/timer/annonces/paliers/timeout) — voir " +
             "Data/Content/WorldBossData.cs. Un asset partagé glissé ici sur le GameObject " +
             "sous _Managers.")]
    public WorldBossData eventData;

    // ── État runtime ─────────────────────────────────────────────
    private GameObject _aliveBoss;
    private Coroutine  _timeoutCoroutine;
    private bool       _resolved;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(RunCycle());
    }

    // =========================================================
    // CYCLE PRINCIPAL
    // =========================================================

    private IEnumerator RunCycle()
    {
        while (true)
        {
            if (eventData == null || eventData.possibleBosses == null || eventData.possibleBosses.Count == 0 ||
                eventData.eligibleMaps == null || eventData.eligibleMaps.Count == 0)
            {
                Debug.LogWarning("[WorldEventScheduler] eventData non assigné ou possibleBosses/eligibleMaps " +
                    "vide(s) — système inerte tant que ces champs sont vides. Nouvelle vérification dans 5s.");
                yield return new WaitForSeconds(5f);
                continue; // relit la config à chaque passage — un fix live en Éditeur est pris en compte
            }

            float totalDelay = Random.Range(eventData.minInterval, eventData.maxInterval);

            // ── Jusqu'à la 1ère annonce (T-5min par défaut) ──────
            yield return new WaitForSeconds(Mathf.Max(0f, totalDelay - eventData.firstWarningOffset));

            WorldEventMapEntry targetMap = eventData.eligibleMaps[Random.Range(0, eventData.eligibleMaps.Count)];
            MobData targetBoss = eventData.possibleBosses[Random.Range(0, eventData.possibleBosses.Count)];

            AnnoncePanel.Instance?.Announce(
                $"Un Boss Géant menace le Palier {targetMap.palier} dans {eventData.firstWarningOffset / 60f:F0} minutes !");

            // ── Jusqu'à la 2e annonce (T-1min par défaut) ────────
            yield return new WaitForSeconds(Mathf.Max(0f, eventData.firstWarningOffset - eventData.secondWarningOffset));

            AnnoncePanel.Instance?.Announce(
                $"Un Boss Géant menace le Palier {targetMap.palier} dans {eventData.secondWarningOffset / 60f:F0} minute(s) !");

            // ── Jusqu'au spawn (T-0) ──────────────────────────────
            yield return new WaitForSeconds(eventData.secondWarningOffset);

            // Scène active RELUE ici, jamais capturée plus tôt — un joueur qui change de map
            // pendant la fenêtre d'annonce ne doit compter que sa position AU MOMENT du check.
            if (SceneManager.GetActiveScene().name == targetMap.sceneName)
                yield return SpawnAndWaitForResolution(targetBoss, targetMap.palier);
            // Sinon : rien ne spawn, on boucle directement sur le cycle suivant (retour Idle).
        }
    }

    // =========================================================
    // SPAWN + RÉSOLUTION (mort ou timeout)
    // =========================================================

    private IEnumerator SpawnAndWaitForResolution(MobData bossData, int palier)
    {
        if (bossData.prefab == null)
        {
            Debug.LogWarning($"[WorldEventScheduler] {bossData.mobName} n'a pas de prefab — événement annulé.");
            yield break;
        }

        if (!TryGetRandomNavMeshPoint(out Vector3 spawnPos))
        {
            Debug.LogWarning("[WorldEventScheduler] Aucun NavMesh baké dans la scène active — événement annulé.");
            yield break;
        }

        _aliveBoss = Instantiate(bossData.prefab, spawnPos, Quaternion.identity);
        Mob mob = _aliveBoss.GetComponent<Mob>();
        mob?.InitializeSpawn(bossData, palier);

        AnnoncePanel.Instance?.Announce($"Le Boss Géant est apparu sur le Palier {palier} !");

        _resolved = false;
        mob?.OnDeath(() => OnBossResolved(palier, killed: true));

        _timeoutCoroutine = StartCoroutine(TimeoutAfterDelay(palier, eventData.despawnTimeout));

        // Bloque le cycle principal tant que ni la mort ni le timeout n'ont résolu l'événement.
        yield return new WaitUntil(() => _resolved);
    }

    private IEnumerator TimeoutAfterDelay(int palier, float delay)
    {
        yield return new WaitForSeconds(delay);
        OnBossResolved(palier, killed: false);
    }

    /// <summary>Point de résolution UNIQUE — mort ET timeout y passent tous les deux. Le garde
    /// _resolved empêche une double résolution si les deux se déclenchent presque en même temps
    /// (le joueur tue le boss juste avant/pendant que le timeout allait se déclencher) : sans ce
    /// garde, une double annonce ou un Destroy() sur un objet déjà détruit par Mob.Die() serait
    /// possible.</summary>
    private void OnBossResolved(int palier, bool killed)
    {
        if (_resolved) return;
        _resolved = true;

        if (_timeoutCoroutine != null) { StopCoroutine(_timeoutCoroutine); _timeoutCoroutine = null; }

        if (killed)
        {
            AnnoncePanel.Instance?.Announce($"Le Boss Géant du Palier {palier} a été vaincu !");
            // Récompenses (XP/Aeris/Prestige/loot) déjà gérées par GameEventBus.OnMobKilled dès
            // que Mob.Die() publie l'event — le boss est un Mob normal, rien à faire ici.
        }
        else
        {
            if (_aliveBoss != null) Destroy(_aliveBoss);
            AnnoncePanel.Instance?.Announce($"Le Boss Géant du Palier {palier} s'est retiré...");
        }

        _aliveBoss = null;
    }

    // =========================================================
    // POSITION ALÉATOIRE SUR LE NAVMESH
    // =========================================================

    /// <summary>Point barycentrique uniforme dans un triangle du NavMesh baké de la scène
    /// ACTIVE — voir spec §Position de spawn. Pas de biais par aire de triangle (acceptable,
    /// pas une distribution statistiquement critique). Retourne false si le NavMesh est vide/
    /// absent — un `out Vector3.zero` en cas d'échec aurait été un piège : (0,0,0) est une
    /// position VALIDE sur un vrai NavMesh (l'origine du monde), donc pas utilisable comme
    /// sentinelle d'échec — d'où le bool de retour plutôt qu'un simple Vector3.</summary>
    private static bool TryGetRandomNavMeshPoint(out Vector3 point)
    {
        var tri = NavMesh.CalculateTriangulation();
        if (tri.indices.Length < 3) { point = Vector3.zero; return false; }

        int triCount = tri.indices.Length / 3;
        int t = Random.Range(0, triCount) * 3;
        Vector3 a = tri.vertices[tri.indices[t]];
        Vector3 b = tri.vertices[tri.indices[t + 1]];
        Vector3 c = tri.vertices[tri.indices[t + 2]];

        float r1 = Mathf.Sqrt(Random.value);
        float r2 = Random.value;
        point = a * (1f - r1) + b * (r1 * (1f - r2)) + c * (r1 * r2);
        return true;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors in the Console.

- [ ] **Step 3: Place and configure in a scene**

In `Map_01` (or wherever `_Managers` lives), create an empty GameObject named `WorldEventScheduler` under `_Managers`, add the `WorldEventScheduler` component. In the Inspector:
- Assign the `WorldBossData` asset from Task 1 to `Event Data`.
- On that asset: add at least one `MobData` with `MobType = BossWorld` and a valid `prefab` to `Possible Bosses`; confirm `Eligible Maps` has the `Map_01` entry from Task 1 Step 4, with `Palier` set to match `Map_01`'s real `MapInfo.palier` value.

- [ ] **Step 4: Verify the empty-config guard**

Temporarily clear `Event Data` on the `WorldEventScheduler` GameObject (drag it out) and enter Play Mode — confirm a `[WorldEventScheduler] eventData non assigné...` warning repeats in the Console roughly every 5 seconds and the game does NOT freeze or throw. Re-assign `Event Data` while still in Play Mode — confirm the warning stops within ~5 seconds (proves the live-recheck works, no restart needed).

- [ ] **Step 5: Verify a full fast cycle (reduced timers)**

On the `WorldBossData` asset, temporarily set `Min Interval`/`Max Interval` to `30`/`60`, `First Warning Offset` to `10`, `Second Warning Offset` to `3`. Enter Play Mode on `Map_01`, stay there, and confirm:
- An `AnnoncePanel` message naming Palier 1 and "10 minutes" appears (the label is generic — it always says "minutes" regardless of the actual offset unit — see note below) after roughly `totalDelay - 10` seconds.
- A second announcement ("3 minute(s)") appears 7 seconds later.
- A boss matching one of `Possible Bosses`, at `mobLevel = 1`, spawns somewhere on the map's NavMesh 3 seconds after that.

*(Known cosmetic detail, not a bug: with `firstWarningOffset` set to 10 seconds for this fast test, the announcement text will literally say "dans 10 minutes" — the message format divides by 60 assuming offsets are always production-scale minutes. Fine for this verification pass; if Florian wants a smarter unit-aware label later, that's a follow-up polish item, not part of this plan.)*

- [ ] **Step 6: Verify the "player elsewhere" no-spawn path**

Repeat Step 5 but leave `Map_01` (load `Map_02`) before the T-0 mark, if the rolled palier was Map_01's — or simply re-run the cycle a few times (palier is re-rolled each cycle) until you observe a cycle where the announced palier does NOT match your current scene. Confirm: no boss appears, no error, and a new cycle starts immediately (another announcement eventually fires again).

- [ ] **Step 7: Verify a LIVE scene switch mid-wait**

This is different from Step 6 (which tests never having been on the right map at all). Here: start a cycle while on `Map_01`. As soon as the first (T-5min-equivalent) announcement fires, immediately travel to `Map_02` before the T-0 mark — confirm the T-0 check reads your NEW active scene (`Map_02`) at that moment, not the one you were on when the announcement fired. If the rolled palier was Map_01's, this should now produce a no-spawn (since you left); if it was Map_02's, it should now spawn once you arrive there. Either outcome is correct as long as it matches whichever scene you were ACTUALLY on at T-0, not at decision time.

- [ ] **Step 8: Verify the no-NavMesh guard**

Only if `Map_02` (or any other eligible map) genuinely has no baked NavMesh — otherwise skip this step, the guard is a defensive code path for a scenario that may not currently exist in the project's actual scenes. If it does apply: force a few cycles to roll that map's palier (or temporarily set `Eligible Maps` to only that one entry) and confirm: `[WorldEventScheduler] Aucun NavMesh baké...` warning appears, no crash, no spawn, and the scheduler immediately moves on to the next cycle instead of getting stuck.

- [ ] **Step 9: Verify the missing-prefab guard**

Temporarily add a `MobData` asset with `prefab` left empty to `Possible Bosses` (or clear the `prefab` field on an existing test entry, then restore it after). Force enough cycles for it to get rolled (small `Possible Bosses` list makes this quick) — confirm a `[WorldEventScheduler] ... n'a pas de prefab` warning appears, no crash, no spawn, and the scheduler moves on to the next cycle. Restore the `prefab` field afterward.

- [ ] **Step 10: Verify death resolution (and the no-double-announcement signal)**

Let a boss spawn (Step 5), kill it. Confirm: `AnnoncePanel` shows "a été vaincu" **exactly once** (not twice — a second, unexpected "s'est retiré" appearing right after would mean the `_resolved` re-entrancy guard failed to stop the timeout coroutine from also firing), the boss's normal XP/Aeris/loot (and Prestige, if its `LootTable.prestigeReward > 0`) are received exactly as any other mob kill, and a new cycle begins (another pair of announcements eventually fires).

- [ ] **Step 11: Verify timeout resolution**

Temporarily set `Despawn Timeout` to `10` for this check. Let a boss spawn and do NOT kill it — confirm after ~10 seconds it disappears, `AnnoncePanel` shows "s'est retiré" exactly once, and a new cycle begins. Restore `Despawn Timeout` to `1800` afterward.

- [ ] **Step 12: Restore production values**

On the `WorldBossData` asset, set `Min Interval`/`Max Interval`/`First Warning Offset`/`Second Warning Offset`/`Despawn Timeout` back to their real values (`3600`/`7200`/`300`/`60`/`1800`, or whatever Florian decides for the actual demo pacing), and confirm `Possible Bosses`/`Eligible Maps` are back to their real intended content (no leftover no-prefab test entry from Step 9).

- [ ] **Step 13: Commit**

```bash
git add Systems/WorldEventScheduler.cs
git commit -m "feat: add WorldEventScheduler — random-interval world event timer + Boss Géant spawn/resolution"
```

---

### Task 3: Final verification pass

**Files:** none (verification-only task).

**Interfaces:** none.

- [ ] **Step 1: Full-length real-pacing smoke test**

With production values restored (Task 2 Step 12), leave the game running on `Map_01` for one full real cycle without touching the reduced-timer shortcuts — confirm the two announcements and (if the palier matches) the spawn all still happen correctly at real speed, not just the accelerated test values. This is the one check that can only catch timing math mistakes that a fast test with tiny numbers could mask (e.g. an off-by-one in which offset subtracts from which).

- [ ] **Step 2: Confirm no regression on existing systems**

Kill a few ordinary (non-boss) mobs in the same session — confirm normal XP/Aeris/loot still work exactly as before this plan (the boss shares the same `Mob`/`MobKilledEvent` pipeline; this plan added no new hooks into it, so this should be a non-event, but worth one quick confirmation).

- [ ] **Step 3: Report back**

Tell me what worked and what didn't — this plan's remaining known follow-ups (not part of this plan, flag only if you want them scheduled next): the "always says minutes" cosmetic label detail from Task 2 Step 5, and reusing this same `WorldEventScheduler` pattern for Invasion (roadmap #3).
