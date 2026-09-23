# Instance System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a solo-scoped instance/session system (private run, lives, death handling, entry/exit) reusable by Donjon Classique, Donjon de Déblocage, and Combat à Vague, with zero dungeon/wave content — system only.

**Architecture:** One thin interface (`IInstanceConfig`) two concrete ScriptableObject configs implement (`DungeonData`, `CombatVagueData`); one runtime singleton (`InstanceSession`) that tracks the single active run and applies one unified death rule (lives-- ; >0 = reload-and-respawn, else = end run); reuses the existing `SceneLoader` for all actual scene loading (`LoadMapWithSpawn`/`ReloadCurrentMap`, both already written, `ReloadCurrentMap`'s own comment already says "reset donjons").

**Tech Stack:** Unity C#, no automated test framework — manual Play Mode verification only (Florian tests himself). CAVEMAN MODE is active for chat with Florian; it does not apply to code, comments, commit messages, or this plan — all stay normal complete prose.

**Spec:** `docs/superpowers/specs/2026-09-23-instance-system-design.md`

## Global Constraints

- Solo/local only — no networking, no matchmaking. Data shapes stay forward-compatible (nothing hardcodes "exactly one player" at the type level) but zero network code is written.
- One unified death rule across all instance types: `livesRemaining-- ; if (livesRemaining > 0) → reload+respawn in place ; else → end run (Failure) and expel to the open-world map the player entered from`. No per-activity-type special-casing in `InstanceSession`.
- No real dungeon/wave/boss content — this plan delivers the system (SO skeletons, runtime, wiring), never actual rooms, waves, or a real boss.
- `DungeonData` covers Donjon Classique AND Donjon de Déblocage via one `dungeonType` field — not two SO classes.
- `DungeonData`/`CombatVagueData` stay separate SO types (not one generic `InstanceData`) — confirmed with Florian, the two activities share almost no fields beyond what `IInstanceConfig` already captures.
- **Correction vs. the committed spec**: `InstanceSession.Enter()` does NOT take a `Vector3 respawnPoint` parameter — the dungeon scene's own `PlayerSpawnPoint` GameObject only exists after that scene loads, so a caller can never have a valid position to pass in beforehand. Entry uses `SceneLoader.LoadMapWithSpawn()` (already resolves `PlayerSpawnPoint` post-load), and in-run respawn uses `SceneLoader.ReloadCurrentMap()` (reloads the instance scene fresh — resetting mob spawns too — and repositions to `PlayerSpawnPoint`, exactly matching that method's own pre-existing code comment "respawn, reset donjons..."). Task 5 also updates the spec file to match.

---

### Task 1: `IInstanceConfig` interface

**Files:**
- Create: `Data/Content/IInstanceConfig.cs`

**Interfaces:**
- Produces: `IInstanceConfig` with properties `InstanceID` (string), `DisplayName` (string), `SceneName` (string), `EntryItemID` (string), `LivesPerPlayer` (int) — every later task consumes this exact shape, do not rename any member.

- [ ] **Step 1: Create the interface file**

```csharp
// =============================================================
// IINSTANCECONFIG.CS — Contrat commun à toute activité instanciée
// Path : Assets/Scripts/Data/Content/IInstanceConfig.cs
// AetherTree GDD v3.6 — §14.2/§14.3/§14.5.1
//
// Implémenté par DungeonData (Donjon Classique/Déblocage) et
// CombatVagueData (Combat à Vague) — voir docs/superpowers/specs/
// 2026-09-23-instance-system-design.md pour le design complet.
// InstanceSession ne connaît QUE cette interface, jamais un type concret.
// =============================================================

public interface IInstanceConfig
{
    /// <summary>Identifiant unique — snake_case, immuable (dungeonID/eventID sur le SO concret).</summary>
    string InstanceID { get; }

    /// <summary>Nom affiché dans l'UI.</summary>
    string DisplayName { get; }

    /// <summary>Nom de la scène Unity à charger via SceneLoader pour cette activité.</summary>
    string SceneName { get; }

    /// <summary>ID du ConsumableData (DungeonStone) requis pour entrer.</summary>
    string EntryItemID { get; }

    /// <summary>Vies individuelles avant fin de run — voir la règle de mort unifiée dans
    /// InstanceSession.OnPlayerDeath(). Donjon Classique : 2 (défaut GDD §14.2.3). Donjon de
    /// Déblocage : 1 (GDD §14.3.3). Combat à Vague : toujours 1, codé en dur sur
    /// CombatVagueData (GDD §14.5.1 : "aucune résurrection possible").</summary>
    int LivesPerPlayer { get; }
}
```

- [ ] **Step 2: Compile check**

Unity doit compiler sans erreur (interface seule, aucun consommateur encore). Ouvrir la Console Unity, vérifier 0 erreur.

- [ ] **Step 3: Commit**

```bash
git add Data/Content/IInstanceConfig.cs
git commit -m "feat: add IInstanceConfig — shared contract for instanced activities"
```

---

### Task 2: `DungeonData` ScriptableObject

**Files:**
- Create: `Data/Content/DungeonData.cs`

**Interfaces:**
- Consumes: `IInstanceConfig` (Task 1).
- Produces: `DungeonData` (class), `DungeonType` (enum: `Classic`, `Unlock`, `FactionPvP`), `DungeonMapData` (serializable class with `mapID`/`displayName`/`isBossRoom`) — later tasks reference `DungeonData.dungeonID`/`.livesPerPlayer`/`.maps` by these exact names.

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DUNGEONDATA.CS — ScriptableObject template de donjon
// Path : Assets/Scripts/Data/Content/DungeonData.cs
// AetherTree GDD v3.6 — §14.2.3
//
// Couvre Donjon Classique ET Donjon de Déblocage via dungeonType — pas deux
// classes séparées (les deux partagent toute la structure GDD, seules les
// valeurs par défaut de livesPerPlayer/deathLimit/bossType diffèrent, voir
// §14.3.3 "Règles Spécifiques" du GDD). Pas de contenu réel de salle dans
// ce chantier (spawners/triggers/lockedUntil pas encore ajoutés à
// DungeonMapData) — voir docs/superpowers/specs/2026-09-23-instance-
// system-design.md §3 Non-objectifs.
// =============================================================

public enum DungeonType
{
    Classic    = 0,
    Unlock     = 1,
    FactionPvP = 2,
}

[System.Serializable]
public class DungeonMapData
{
    [Tooltip("Identifiant de la salle — doit correspondre au nom de la scène Unity chargée.")]
    public string mapID;
    [Tooltip("Nom affiché à l'entrée de la salle.")]
    public string displayName;
    [Tooltip("true = salle de boss.")]
    public bool   isBossRoom;
}

[CreateAssetMenu(fileName = "dgn_", menuName = "AetherTree/Contenu/DungeonData")]
public class DungeonData : ScriptableObject, IInstanceConfig
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Identifiant unique — snake_case, immuable.")]
    public string dungeonID = "";
    public string displayName = "";
    [Tooltip("Classic = Donjon Classique · Unlock = Donjon de Déblocage · FactionPvP = plus tard.")]
    public DungeonType dungeonType = DungeonType.Classic;
    public int tier = 1;
    public int levelMin = 1;
    public int levelMax = 10;

    // ── Accès ─────────────────────────────────────────────────
    [Header("Accès")]
    [Tooltip("Capacité max — Classique : 15 (voir GDD §14.2.3). Sans effet observable en solo.")]
    public int maxPlayers = 15;
    [Tooltip("dungeonID du ConsumableData (DungeonStone) requis pour entrer.")]
    public string entryItemID = "";

    // ── Structure ─────────────────────────────────────────────
    [Header("Structure")]
    [Tooltip("false = donjon direct boss (une seule salle).")]
    public bool hasCorridor = false;
    [Tooltip("Salles dans l'ordre — 1 seule si hasCorridor = false.")]
    public List<DungeonMapData> maps = new List<DungeonMapData>();
    [Tooltip("Boss dans l'ordre.")]
    public List<MobData> bossData = new List<MobData>();

    // ── Règles de mort — voir InstanceSession.OnPlayerDeath() ──
    [Header("Règles de mort")]
    [Tooltip("Vies individuelles par joueur. Classique : 2 (défaut). Déblocage : 1.")]
    public int livesPerPlayer = 2;
    [Tooltip("Morts collectives avant échec — 0 = illimité. Sans effet observable en solo (un " +
             "seul joueur, livesPerPlayer détermine déjà la fin de run avant que ce champ ne " +
             "soit jamais pertinent), champ gardé fidèle au GDD pour le futur multijoueur.")]
    public int deathLimit = 5;

    // ── Couloir ───────────────────────────────────────────────
    [Header("Couloir")]
    public bool  mobRespawn   = false;
    public float respawnDelay = 30f;

    // ── Récompenses ───────────────────────────────────────────
    [Header("Récompenses")]
    public DonjonLootTableData donjonLootTable;
    public int worldRepGain = 0;
    public CodexEntryData codexEntry;

    // ── IInstanceConfig ───────────────────────────────────────
    public string InstanceID     => dungeonID;
    public string DisplayName    => displayName;
    public string SceneName      => maps.Count > 0 ? maps[0].mapID : null;
    public string EntryItemID    => entryItemID;
    public int    LivesPerPlayer => livesPerPlayer;
}
```

**Si `DonjonLootTableData` ou `CodexEntryData` n'existent pas encore dans le projet** : grep
`class DonjonLootTableData` et `class CodexEntryData` sous `Data/`. S'ils n'existent pas, déclarer
ces deux champs comme `ScriptableObject` (type générique) au lieu du type spécifique — ne bloque
pas ce chantier, ce sont des types de contenu hors scope (voir spec §3 Non-objectifs), le champ
existe juste pour matcher le schéma GDD.

- [ ] **Step 2: Compile check + manual asset creation**

Unity Console : 0 erreur. Clic droit dans le Project (dans `Assets/Content/`, hors du repo git
comme tous les autres assets de contenu) → `Create > AetherTree > Contenu > DungeonData` → vérifier
que l'asset se crée et que l'Inspector affiche tous les champs déclarés ci-dessus.

- [ ] **Step 3: Commit**

```bash
git add Data/Content/DungeonData.cs
git commit -m "feat: add DungeonData SO — codifies GDD §14.2.3 for Classique/Déblocage"
```

---

### Task 3: `CombatVagueData` ScriptableObject

**Files:**
- Create: `Data/Content/CombatVagueData.cs`

**Interfaces:**
- Consumes: `IInstanceConfig` (Task 1).
- Produces: `CombatVagueData` (class), `CombatVagueWaveData` (serializable class), `WaveMobEntry` (serializable class) — field names below are copied verbatim from the GDD's own table (`C:\AetherTree_v3.5\AetherTree_GDD_v3.6(1).md`, lines ~6945-6982) — do not rename.

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using System.Collections.Generic;

// =============================================================
// COMBATVAGUEDATA.CS — ScriptableObject template d'événement Combat à Vague
// Path : Assets/Scripts/Data/Content/CombatVagueData.cs
// AetherTree GDD v3.6 — §14.5.1
//
// Type SO dédié — le GDD lui-même avait abandonné un "EventData SO"
// générique pour ce contenu (structure vagues/timers/tranches trop
// différente d'un donjon salles/couloir, voir DungeonData). Pas de
// contenu réel (waves reste vide) dans ce chantier — voir
// docs/superpowers/specs/2026-09-23-instance-system-design.md §3.
// =============================================================

public enum ExitPortalTrigger
{
    OnBossDeath = 0,
}

public enum BossType
{
    A = 0,
    B = 1,
}

[System.Serializable]
public class WaveMobEntry
{
    [Tooltip("Le mob à spawner.")]
    public MobData mobData;
    [Tooltip("Nombre d'exemplaires.")]
    public int count = 1;
    [Tooltip("Fourchette de niveau — calculée dynamiquement selon la tranche du joueur.")]
    public int levelMin = 1;
    public int levelMax = 1;
}

[System.Serializable]
public class CombatVagueWaveData
{
    [Tooltip("Index de la vague (1 à waveCount).")]
    public int waveIndex = 1;
    [Tooltip("Timestamp depuis le début, en secondes.")]
    public float spawnTimestamp = 0f;
    [Tooltip("Mobs à spawner pour cette vague.")]
    public List<WaveMobEntry> mobEntries = new List<WaveMobEntry>();
    [Tooltip("true uniquement sur la dernière vague.")]
    public bool hasBoss = false;
    [Tooltip("Boss de la dernière vague — null si hasBoss = false.")]
    public MobData bossData;
}

[CreateAssetMenu(fileName = "vague_", menuName = "AetherTree/Contenu/CombatVagueData")]
public class CombatVagueData : ScriptableObject, IInstanceConfig
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Identifiant unique — snake_case.")]
    public string eventID = "";
    public string displayName = "";
    [Tooltip("Nom de la scène Unity de cet événement.")]
    public string sceneName = "";
    [Tooltip("dungeonID du ConsumableData requis pour entrer — même mécanisme que DungeonStone.")]
    public string entryItemID = "";

    // ── Affectation par tranche (multijoueur — sans effet en solo) ──
    [Header("Tranches (multijoueur — sans effet observable en solo)")]
    public int  maxPlayersPerInstance = 20;
    public bool instancePerTranche    = true;
    public int  trancheSize           = 10;

    // ── Structure ─────────────────────────────────────────────
    [Header("Structure")]
    public int   waveCount            = 5;
    public float preparationDuration  = 30f;
    public float interWaveDuration    = 30f;
    public BossType bossType          = BossType.A;
    public ExitPortalTrigger exitPortalAppears = ExitPortalTrigger.OnBossDeath;
    public float expulsionDelay       = 900f;
    public float timerMax             = 900f;
    public List<CombatVagueWaveData> waves = new List<CombatVagueWaveData>();

    // ── Récompenses ───────────────────────────────────────────
    [Header("Récompenses")]
    [Tooltip("Formule descriptive — voir GDD §14.5.1. Calcul réel implémenté au chantier contenu.")]
    public string rewardAerisFormula    = "1000 * niveau du joueur";
    public string rewardWorldRepFormula = "50 * tranche du joueur";

    // ── IInstanceConfig ───────────────────────────────────────
    public string InstanceID     => eventID;
    public string DisplayName    => displayName;
    public string SceneName      => sceneName;
    public string EntryItemID    => entryItemID;
    // Toujours 1 — GDD §14.5.1 : "aucune résurrection possible... chaque mort est définitive
    // pour ce run", jamais configurable, contrairement à DungeonData.livesPerPlayer.
    public int LivesPerPlayer => 1;
}
```

- [ ] **Step 2: Compile check + manual asset creation**

Unity Console : 0 erreur. `Create > AetherTree > Contenu > CombatVagueData` → vérifier tous les
champs dans l'Inspector, en particulier que `LivesPerPlayer` n'apparaît PAS comme un champ éditable
(c'est une propriété calculée, jamais un `public int` — vérifier qu'il n'y a pas de faute de
frappe qui l'aurait rendu éditable par erreur).

- [ ] **Step 3: Commit**

```bash
git add Data/Content/CombatVagueData.cs
git commit -m "feat: add CombatVagueData SO — codifies GDD §14.5.1 for Combat à Vague"
```

---

### Task 4: `InstanceSession` runtime

**Files:**
- Create: `Systems/InstanceSession.cs`

**Interfaces:**
- Consumes: `IInstanceConfig` (Task 1), `SceneLoader.Instance.LoadMapWithSpawn(string)`/`.ReloadCurrentMap()`/`.CurrentMap` (existing, `Events/SceneLoader.cs`), `Player.Revive(float, float)` (existing, `Entities/Player.cs:1332`).
- Produces: `InstanceSession` (singleton, `InstanceSession.Instance`), `InstanceOutcome` (enum: `InProgress`, `Success`, `Failure`), public API `Enter(IInstanceConfig)`, `OnPlayerDeath()`, `OnBossKilled()` — Task 5 calls `Enter()`, Task 6 calls `OnPlayerDeath()`.

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;
using System.Collections;

// =============================================================
// INSTANCESESSION.CS — Session privée pour Donjon/Combat à Vague/Événements
// Path : Assets/Scripts/Systems/InstanceSession.cs
// AetherTree GDD v3.6 — §14.2/§14.3/§14.5.1
//
// Track UNE session active à la fois (solo aujourd'hui — voir docs/
// superpowers/specs/2026-09-23-instance-system-design.md pour la décision
// de portée). Ne connaît JAMAIS DungeonData/CombatVagueData par leur type
// concret — uniquement via IInstanceConfig, pour rester réutilisable par
// n'importe quelle future activité instanciée (Arène Équipe, etc.) sans
// modification de ce fichier.
//
// Règle de mort UNIFIÉE (spec §1) : vies-- ; si > 0 → recharge la scène
// courante et respawn (SceneLoader.ReloadCurrentMap(), déjà écrit pour ça
// — voir son propre commentaire "respawn, reset donjons...") ; sinon → fin
// de run, expulsion vers la map d'où le joueur est entré.
// =============================================================

public class InstanceSession : MonoBehaviour
{
    public static InstanceSession Instance { get; private set; }

    public IInstanceConfig  CurrentInstance { get; private set; }
    public int              LivesRemaining  { get; private set; }
    public InstanceOutcome  CurrentOutcome  { get; private set; } = InstanceOutcome.InProgress;

    [Tooltip("Délai avant expulsion après la fin d'un run (succès ou échec) — GDD §14.2.4 : " +
             "\"expulsion automatique après 15 secondes\".")]
    public float exitDelay = 15f;

    // Capturé à Enter() — la map d'où le joueur vient, pour y revenir à la sortie.
    private string _returnMapName;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Point d'entrée unique — consommé par le câblage de ConsumableData.DungeonStone
    /// (voir Task 5). Charge la scène de l'activité via SceneLoader (qui résout lui-même
    /// PlayerSpawnPoint au chargement — voir SceneLoader.LoadMapWithSpawn) et initialise les
    /// vies depuis la config.</summary>
    public void Enter(IInstanceConfig config)
    {
        if (config == null || SceneLoader.Instance == null) return;

        CurrentInstance = config;
        LivesRemaining  = config.LivesPerPlayer;
        CurrentOutcome  = InstanceOutcome.InProgress;
        _returnMapName  = SceneLoader.Instance.CurrentMap;

        SceneLoader.Instance.LoadMapWithSpawn(config.SceneName);
    }

    /// <summary>Appelé par Player.Die() (voir Task 6) quand une instance est active — remplace
    /// entièrement le flux de mort normal (RespawnSystem/DeathScreenUI) tant qu'une session est
    /// en cours.</summary>
    public void OnPlayerDeath()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;

        LivesRemaining--;
        if (LivesRemaining > 0)
            RespawnAtCheckpoint();
        else
            EndRun(InstanceOutcome.Failure);
    }

    /// <summary>Appelé quand le boss de l'activité meurt — câblage exact (quel Mob.Die() precis
    /// déclenche ceci) laissé au chantier qui construit le premier boss réel, voir spec §3
    /// Non-objectifs. Ce chantier ne fait que garantir que le hook existe et fonctionne.</summary>
    public void OnBossKilled()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;
        EndRun(InstanceOutcome.Success);
    }

    // =========================================================
    // INTERNE
    // =========================================================

    private void RespawnAtCheckpoint()
    {
        var player = FindObjectOfType<Player>();
        SceneLoader.Instance?.ReloadCurrentMap(); // recharge la scène — reset mobs, PlayerSpawnPoint
        player?.Revive(1f, 1f); // pleine vie — la perte d'une vie EST la pénalité, pas le %HP
    }

    private void EndRun(InstanceOutcome outcome)
    {
        CurrentOutcome = outcome;
        Debug.Log($"[INSTANCE] {CurrentInstance.DisplayName} — {outcome}. Expulsion dans {exitDelay}s.");
        StartCoroutine(ExitAfterDelay());
    }

    private IEnumerator ExitAfterDelay()
    {
        yield return new WaitForSeconds(exitDelay);
        SceneLoader.Instance?.LoadMapWithSpawn(_returnMapName);
        CurrentInstance = null;
    }
}

public enum InstanceOutcome { InProgress, Success, Failure }
```

**Sur `player?.Revive(1f, 1f)` juste après `ReloadCurrentMap()`** : `ReloadCurrentMap()` est une
coroutine asynchrone (`StartCoroutine(ReloadRoutine())` dans `SceneLoader.cs`) — l'appeler puis
immédiatement `Revive()` dans la même frame fonctionne car `Player` vit dans `_Persistent.unity`
(jamais déchargée, voir le header comment de `SceneLoader.cs`) donc `FindObjectOfType<Player>()`
le trouve tout de suite ; `Revive()` restaure HP/Mana/isDead indépendamment du chargement de la
nouvelle salle en cours. Si le Play Mode test (Task 7) montre un problème de timing (le joueur
visuellement mort une frame avant que la salle rechargée n'apparaisse), passer `RespawnAtCheckpoint`
en coroutine attendant `!SceneLoader.Instance.IsLoading` avant `Revive()` — noté ici pour ne pas
être surpris, pas implémenté par anticipation (YAGNI tant que le test ne montre pas le problème).

- [ ] **Step 2: Compile check**

Unity Console : 0 erreur. Ajouter un `GameObject` vide nommé `InstanceSession` dans la scène
`_Persistent.unity` (là où vivent `SkillSystem`/`SceneLoader`/`RespawnSystem`/`SaveSystem` — vérifier
en ouvrant cette scène quel GameObject porte déjà ces composants et ajouter `InstanceSession`
dessus ou à côté, suivant la convention existante), lui attacher le composant.

- [ ] **Step 3: Commit**

```bash
git add Systems/InstanceSession.cs
git commit -m "feat: add InstanceSession — solo-scoped run tracker with unified death rule"
```

---

### Task 5: `DungeonRegistry` + consumption wiring + spec correction

**Files:**
- Create: `Systems/DungeonRegistry.cs`
- Modify: `UI/PanelFixe/ConsoBarUI.cs` (`TryUseSlot`, currently lines 82-121 — the `DungeonStone`
  branch replaces the early-return log at lines 95-99)
- Modify: `docs/superpowers/specs/2026-09-23-instance-system-design.md` (correct the
  `Enter(IInstanceConfig, Vector3)` signature shown in §4.1 to match Task 4's actual
  `Enter(IInstanceConfig)` — see Global Constraints above for the reasoning)

**Interfaces:**
- Consumes: `DungeonData` (Task 2), `InstanceSession.Instance.Enter(IInstanceConfig)` (Task 4).
- Produces: `DungeonRegistry.Instance.Resolve(string dungeonID) → DungeonData`.

- [ ] **Step 1: Create the registry, mirroring `CraftSystem.cs`'s exact auto-fill pattern**

```csharp
using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DUNGEONREGISTRY.CS — Registre de tous les DungeonData du projet
// Path : Assets/Scripts/Systems/DungeonRegistry.cs
// AetherTree GDD v3.6
//
// Même patron que CraftSystem.allRecipes / UnlockManager.allConditions —
// auto-fill éditeur, résolution par ID à l'exécution.
// =============================================================

public class DungeonRegistry : MonoBehaviour
{
    public static DungeonRegistry Instance { get; private set; }

    [Tooltip("Clic droit → 'Auto-remplir allDungeons' pour scanner le projet.")]
    public List<DungeonData> allDungeons = new List<DungeonData>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
#if UNITY_EDITOR
        EditorAutoFill();
#endif
    }

    public DungeonData Resolve(string dungeonID)
    {
        return allDungeons.Find(d => d != null && d.dungeonID == dungeonID);
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-remplir allDungeons")]
    private void EditorAutoFill()
    {
        allDungeons.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:DungeonData");
        foreach (string guid in guids)
        {
            var d = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (d != null) allDungeons.Add(d);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
```

- [ ] **Step 2: Wire `ConsumableData.DungeonStone` in `ConsoBarUI.TryUseSlot()`**

Read the current `TryUseSlot` body first (`UI/PanelFixe/ConsoBarUI.cs`, the early-return block is
at lines 94-99):

```csharp
        var data = instance.data;
        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }
```

Replace that exact block with:

```csharp
        var data = instance.data;

        if (data.consumableType == ConsumableType.DungeonStone)
        {
            var dungeon = DungeonRegistry.Instance?.Resolve(data.dungeonID);
            if (dungeon == null)
            {
                Debug.LogWarning($"[ConsoBarUI] Pierre de donjon '{data.dungeonID}' introuvable dans DungeonRegistry.");
                return;
            }
            InstanceSession.Instance?.Enter(dungeon);
            instance.Remove(1);
            if (instance.IsEmpty)
            {
                var wrapper = InventorySystem.Instance?.GetAllItems().Find(i => i.ConsumableInstance == instance);
                if (wrapper != null) InventorySystem.Instance.RemoveItem(wrapper);
                slot.SetConsoInstance(null);
            }
            else
            {
                slot.UpdateQuantity(instance.quantity);
            }
            InventorySystem.Instance?.OnInventoryChanged?.Invoke();
            InventoryUI.Instance?.RefreshGrid();
            return;
        }

        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }
```

**Pourquoi la consommation est dupliquée ici plutôt que de tomber dans le code Potion/Food
existant en dessous** : ce code existant fait aussi `Heal`/`RecoverMana`/`ApplyBuff` puis
consomme — la Pierre de Donjon n'a besoin QUE de la consommation (`instance.Remove(1)` + cleanup
slot), pas des effets Potion. Dupliquer ces ~10 lignes de cleanup est plus clair qu'un `if`
supplémentaire au milieu du chemin Potion/Food — même appel `return` immédiat après, pas de
`data.cooldown` appliqué (une Pierre de donjon n'a pas de cooldown, `ShowIf` ne l'affiche même
pas pour ce type sur `ConsumableData`).

- [ ] **Step 3: Update `ConsoBarUI.cs`'s stale doc comment**

The method's XML doc comment (line 80-81) currently says: `Potion (heal HP/Mana + buff) seule
implémentée pour l'instant ; DungeonStone/TeleportItem/Other pas encore câblés.` Update to:

```csharp
    /// <summary>Utilise le consommable du slot — Potion/Food (heal HP/Mana + buff) et
    /// DungeonStone (entrée en instance) implémentés ; TeleportItem/Other pas encore câblés.</summary>
```

- [ ] **Step 4: Correct the spec file's `Enter()` signature**

In `docs/superpowers/specs/2026-09-23-instance-system-design.md`, find the `Enter` method in the
§4.1 code block (currently `public void Enter(IInstanceConfig config, Vector3 respawnPoint)`) and
the matching `_respawnPoint` field + its use in `RespawnAtCheckpoint()`'s comment. Replace with a
short note pointing at this plan:

```markdown
> **Correction (voir plan d'implémentation, Task 5)** : `Enter()` ne prend pas de paramètre
> `Vector3 respawnPoint` — `SceneLoader.LoadMapWithSpawn()` résout `PlayerSpawnPoint` lui-même
> une fois la scène chargée, et le respawn en cours de run réutilise `SceneLoader.
> ReloadCurrentMap()` plutôt qu'un warp manuel. Voir `Systems/InstanceSession.cs` pour le code
> réel.
```

- [ ] **Step 5: Compile check + manual Inspector wiring**

Unity Console : 0 erreur. Sur le même GameObject `_Persistent` que `InstanceSession` (Task 4),
ajouter le composant `DungeonRegistry`. Clic droit dessus dans l'Inspector → "Auto-remplir
allDungeons" → vérifier que le `DungeonData` créé au Task 2 apparaît dans la liste.

- [ ] **Step 6: Commit**

```bash
git add Systems/DungeonRegistry.cs UI/PanelFixe/ConsoBarUI.cs docs/superpowers/specs/2026-09-23-instance-system-design.md
git commit -m "feat: wire ConsumableData.DungeonStone to InstanceSession.Enter()"
```

---

### Task 6: Death hook — `Player.Die()` → `InstanceSession.OnPlayerDeath()`

**Files:**
- Modify: `Entities/Player.cs:1009-1030` (`Die()` override)

**Interfaces:**
- Consumes: `InstanceSession.Instance.OnPlayerDeath()` (Task 4), `InstanceSession.Instance.CurrentInstance` (Task 4).

- [ ] **Step 1: Read the current method**

```csharp
    protected override void Die()
    {
        // Consommé AVANT base.Die() — celui-ci wipe tous les effets actifs via
        // ClearAllEffects(), Revive y compris s'il n'est pas déjà retiré ici.
        float reviveDelay = 0f, reviveHP = 0f, reviveMana = 0f;
        bool hasRevive = statusEffects != null &&
            statusEffects.TryConsumeRevive(out reviveDelay, out reviveHP, out reviveMana);

        base.Die();
        GameEventBus.Publish(new PlayerDeathEvent
        {
            cause     = ElementType.Neutral,
            killer    = null,
            hpAtDeath = currentHP,
            context   = DeathContext.OpenWorld,
        });

        if (hasRevive)
            RespawnSystem.Instance?.TriggerDelayedRevive(reviveDelay, reviveHP, reviveMana);
        else
            RespawnSystem.Instance?.TriggerDeath();
    }
```

- [ ] **Step 2: Replace with the instance-aware version**

```csharp
    protected override void Die()
    {
        // Une instance active court-circuite TOUT le flux de mort normal, Revive inclus — le
        // GDD est explicite sur au moins 2 des 3 activités (Déblocage/Vagues : "aucune
        // résurrection possible"), et la cohérence entre les 3 types passe par une seule règle
        // (voir InstanceSession.OnPlayerDeath()) plutôt qu'une exception par activité.
        if (InstanceSession.Instance != null && InstanceSession.Instance.CurrentInstance != null)
        {
            statusEffects?.ClearAllEffects();
            base.Die();
            GameEventBus.Publish(new PlayerDeathEvent
            {
                cause     = ElementType.Neutral,
                killer    = null,
                hpAtDeath = currentHP,
                context   = DeathContext.OpenWorld,
            });
            InstanceSession.Instance.OnPlayerDeath();
            return;
        }

        // Consommé AVANT base.Die() — celui-ci wipe tous les effets actifs via
        // ClearAllEffects(), Revive y compris s'il n'est pas déjà retiré ici.
        float reviveDelay = 0f, reviveHP = 0f, reviveMana = 0f;
        bool hasRevive = statusEffects != null &&
            statusEffects.TryConsumeRevive(out reviveDelay, out reviveHP, out reviveMana);

        base.Die();
        GameEventBus.Publish(new PlayerDeathEvent
        {
            cause     = ElementType.Neutral,
            killer    = null,
            hpAtDeath = currentHP,
            context   = DeathContext.OpenWorld,
        });

        if (hasRevive)
            RespawnSystem.Instance?.TriggerDelayedRevive(reviveDelay, reviveHP, reviveMana);
        else
            RespawnSystem.Instance?.TriggerDeath();
    }
```

**Sur `statusEffects?.ClearAllEffects()` dans la nouvelle branche** : `base.Die()` fait déjà ce
nettoyage dans le chemin normal (voir le commentaire existant "celui-ci wipe tous les effets
actifs via ClearAllEffects()") — vérifier en lisant `Entity.Die()` (`Entities/Entity.cs:704`) que
`base.Die()` appelle bien `ClearAllEffects()` lui-même. Si c'est le cas, retirer l'appel
`statusEffects?.ClearAllEffects()` explicite ajouté ci-dessus (redondant) — gardé ici uniquement
si `base.Die()` ne le fait PAS déjà pour ce chemin. Vérifier avant de committer, ne pas dupliquer
un nettoyage qui a déjà lieu.

- [ ] **Step 3: Compile check**

Unity Console : 0 erreur.

- [ ] **Step 4: Commit**

```bash
git add Entities/Player.cs
git commit -m "feat: route player death through InstanceSession when an instance is active"
```

---

### Task 7: Final verification (Florian, manual Play Mode)

**Files:** none (verification only).

**Interfaces:** none — this task exercises the full chain built by Tasks 1-6.

- [ ] **Step 1: Grep sanity check**

```bash
grep -rn "pas encore câblés" UI/PanelFixe/ConsoBarUI.cs
```

Expected: no match (the stale doc comment was updated in Task 5 Step 3). If it still matches,
Task 5 Step 3 wasn't applied — fix before continuing.

- [ ] **Step 2: Full compile check**

Open Unity, wait for full recompile, Console: 0 error, 0 exception.

- [ ] **Step 3: Prepare test assets** (Florian, in-editor)

1. Reuse the `DungeonData` test asset from Task 2 (`dungeonType = Classic`, `livesPerPlayer = 2`).
   Set `maps` to one `DungeonMapData` entry whose `mapID` matches a real, existing test scene name
   already in Build Settings (or create a throwaway empty scene, add it to Build Settings, and use
   its name).
2. Place a `PlayerSpawnPoint`-named empty GameObject in that test scene (same convention every
   other map already uses — `SceneLoader.RepositionPlayer()`/`RespawnSystem.OnMapLoaded()` both
   look it up by this exact name).
3. Create a `ConsumableData` asset (`consumableType = DungeonStone`, `dungeonID` = the test
   dungeon's `dungeonID`), add it to the test Player's inventory, equip it to a ConsoBar slot
   (F1-F3).

- [ ] **Step 4: Entry test**

Play Mode → press the equipped slot's hotkey. Expected: Console log from `SceneLoader`
(`OnMapLoaded`), player teleported to the test dungeon scene's `PlayerSpawnPoint`,
`InstanceSession.Instance.LivesRemaining == 2` (inspect via a temporary `Debug.Log` or the
Inspector on the `InstanceSession` component in Play Mode).

- [ ] **Step 5: First death — respawn**

Let the test player die once (any means — self-damage script, a weak test Mob, etc.). Expected:
scene reloads (mobs/state reset), player repositioned at `PlayerSpawnPoint`, full HP/Mana,
`LivesRemaining == 1`. The normal open-world `DeathScreenUI` must NOT appear.

- [ ] **Step 6: Second death — failure**

Die again. Expected: `InstanceSession.Instance.CurrentOutcome == InstanceOutcome.Failure`, a
`[INSTANCE] ... Failure. Expulsion dans 15s.` log, and after 15 seconds the player is back on the
original open-world map (`_returnMapName`), `CurrentInstance == null`.

- [ ] **Step 7: Success path**

Re-enter the instance (repeat Step 3-4 with a fresh Pierre). This time call
`InstanceSession.Instance.OnBossKilled()` manually (temporary keybind, or the Inspector's
right-click "Invoke Method" if exposed, or a one-line debug script) instead of dying. Expected:
`CurrentOutcome == InstanceOutcome.Success`, same 15s expulsion log and return-to-open-world flow.

- [ ] **Step 8: 1-life edge case**

Duplicate the test `DungeonData`, set `livesPerPlayer = 1` (simulates Donjon de Déblocage /
Combat à Vague). Enter, die once. Expected: `LivesRemaining == 0`, `CurrentOutcome ==
InstanceOutcome.Failure` immediately — no intermediate respawn at any point.

- [ ] **Step 9: Report to Florian**

Summarize pass/fail for each of Steps 4-8 in chat. Any failure goes through
`superpowers:systematic-debugging` before any fix — no guessing.

---

## Self-Review Notes (completed during plan authoring, not a step to re-run)

- **Spec coverage**: §4.1 (InstanceSession) → Task 4. §4.2 (DungeonData) → Task 2. §4.3
  (CombatVagueData) → Task 3. §4.4 (consumption wiring) → Task 5. §4.5 (death hook) → Task 6.
  §5 (verification) → Task 7. All spec sections have a task.
- **Placeholder scan**: no TBD/TODO in any code block above; the one deferred item
  (`OnBossKilled()`'s real trigger wiring) is explicitly out of THIS chantier's scope per spec §3
  Non-objectifs, not a placeholder within scope.
- **Type consistency**: `IInstanceConfig.SceneName`/`.EntryItemID`/`.LivesPerPlayer` used
  identically by `DungeonData` (Task 2), `CombatVagueData` (Task 3), and `InstanceSession` (Task
  4) — verified no renames across tasks. `InstanceSession.Enter(IInstanceConfig)` (Task 4)
  matches its only caller (Task 5) exactly.
- **Spec deviation flagged and fixed**: the committed spec's `Enter(IInstanceConfig,
  Vector3 respawnPoint)` signature was wrong (respawn point can't exist before the scene loads) —
  corrected in Task 4's actual code and the spec file itself patched in Task 5 Step 4, rather than
  silently diverging from the committed doc.
