# Donjon Entry Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the raid-style two-step donjon entry flow (consume item → arm entry + show group panel → cross a gated portal → real instance start), a composable portal-lock system reused for dungeon entry/internal rooms/palier transitions, a minimal in-run trigger mechanism, and a group panel showing participants/HP-MP/lives.

**Architecture:** Extends the already-shipped Instance System (`InstanceSession`/`IInstanceConfig`/`DungeonData`) with a "pending entry" state consumed by a new composable lock on the existing `Events/Portal.cs`, plus a small `DungeonTrigger` mechanism tracked per-run on `InstanceSession`, plus one new HUD panel.

**Tech Stack:** Unity C#, no automated test framework — manual Play Mode verification only (Florian tests himself). CAVEMAN MODE is active for chat with Florian; it does not apply to code, comments, commit messages, or this plan — all stay normal complete prose.

**Spec:** `docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md` (depends on `docs/superpowers/specs/2026-09-23-instance-system-design.md`, already implemented — commits `44dacfc`..`8cee222`)

## Global Constraints

- Solo/local only — `Participants` is always `[local Player]`, no networking work.
- No real dungeon content (no real rooms, mini-bosses, or levers) — this plan delivers the system only.
- `Portal.cs`'s default behavior (`gateType = None`) must remain byte-identical to today for every existing portal in the game — this is a composable addition, never a rewrite of the free-crossing path.
- No polished blocked-crossing UX this pass — `Debug.Log`/`Debug.LogWarning` is the complete feedback for a blocked portal.
- `InstanceSession.Enter(IInstanceConfig)` keeps its existing signature and body (only gets one new line, `_metTriggerIDs.Clear();`) — its only caller changes from `ConsoBarUI` to the new gated `Portal`.

---

### Task 1: `InstanceSession` — pending entry + triggers

**Files:**
- Modify: `Systems/InstanceSession.cs`

**Interfaces:**
- Consumes: `IInstanceConfig` (existing, `Data/Content/IInstanceConfig.cs`).
- Produces: `InstanceSession.PendingInstance` (`IInstanceConfig`, get-only), `.Participants` (`List<Player>`, get-only), `.IsLeader` (`bool`, get-only), `ArmEntry(IInstanceConfig config) : bool`, `ConsumePendingEntry() : bool`, `NotifyTriggerMet(string triggerID)`, `IsTriggerMet(string triggerID) : bool` — Task 2 calls `ArmEntry`, Task 5 (`Portal.cs`) calls `ConsumePendingEntry`/`IsTriggerMet`/`NotifyTriggerMet` (the last one only from future content, not this plan), Task 6 (panel) reads `PendingInstance`/`Participants`/`IsLeader`.

- [ ] **Step 1: Read the current file**

The current `Systems/InstanceSession.cs` (post the previous chantier's fix wave, commit `8cee222`) looks like this — read it yourself to confirm before editing, since this step's code blocks below assume this exact starting point:

```csharp
using UnityEngine;
using System.Collections;

// =============================================================
// INSTANCESESSION.CS — Session privée pour Donjon/Combat à Vague/Événements
// Path : Assets/Scripts/Systems/InstanceSession.cs
// AetherTree GDD v3.6 — §14.2/§14.3/§14.5.1
// ... (header comment unchanged)
// =============================================================

public enum InstanceOutcome { InProgress, Success, Failure }

public class InstanceSession : MonoBehaviour
{
    public static InstanceSession Instance { get; private set; }

    public IInstanceConfig  CurrentInstance { get; private set; }
    public int              LivesRemaining  { get; private set; }
    public InstanceOutcome  CurrentOutcome  { get; private set; } = InstanceOutcome.InProgress;

    [Tooltip("...")]
    public float exitDelay = 15f;

    private string _returnMapName;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool Enter(IInstanceConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.SceneName))
        {
            Debug.LogWarning("[INSTANCE] Enter() refusé — config nulle ou SceneName vide.");
            return false;
        }
        if (SceneLoader.Instance == null || SceneLoader.Instance.IsLoading)
        {
            Debug.LogWarning("[INSTANCE] Enter() refusé — SceneLoader indisponible ou chargement en cours.");
            return false;
        }

        CurrentInstance = config;
        LivesRemaining  = config.LivesPerPlayer;
        CurrentOutcome  = InstanceOutcome.InProgress;
        _returnMapName  = SceneLoader.Instance.CurrentMap;

        SceneLoader.Instance.LoadMapWithSpawn(config.SceneName);
        return true;
    }

    // ... OnPlayerDeath()/OnBossKilled()/RespawnAtCheckpoint()/EndRun()/ExitAfterDelay() unchanged
}
```

- [ ] **Step 2: Add the pending-entry state and new methods**

Add these new members inside the `InstanceSession` class, right after the existing `_returnMapName` field declaration and before `Awake()`:

```csharp
    // ── Entrée en attente — voir docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md ──
    public IInstanceConfig PendingInstance { get; private set; }
    public List<Player>    Participants    { get; private set; } = new List<Player>();
    public bool            IsLeader        { get; private set; }

    private HashSet<string> _metTriggerIDs = new HashSet<string>();
```

This requires two new `using` directives at the top of the file (`System.Collections.Generic` for `List`/`HashSet`) — check the file doesn't already have them (it currently only has `UnityEngine`/`System.Collections`) and add:

```csharp
using System.Collections.Generic;
```

- [ ] **Step 3: Add `ArmEntry`/`ConsumePendingEntry`/`NotifyTriggerMet`/`IsTriggerMet`**

Add these public methods in the `// API PUBLIQUE` region, right after the existing `Enter(IInstanceConfig config)` method (keep `Enter()`'s own body untouched except for the one-line addition in Step 4 below — these are NEW methods added after it, not a replacement):

```csharp
    /// <summary>Posé par la consommation de l'item d'entrée (ConsoBarUI, voir Task 2) — n'appelle
    /// PAS Enter(), prépare seulement l'état qu'un portail gaté (RequiresDungeonEntry) consommera
    /// au franchissement. Refuse si une instance est déjà en cours (CurrentInstance != null) pour
    /// éviter de corrompre l'état actif avec une nouvelle entrée en attente par-dessus.</summary>
    public bool ArmEntry(IInstanceConfig config)
    {
        if (config == null || CurrentInstance != null) return false;
        PendingInstance = config;
        IsLeader        = true;
        Participants    = new List<Player> { FindObjectOfType<Player>() };
        return true;
    }

    /// <summary>Appelé par Portal (gateType = RequiresDungeonEntry) au moment du franchissement
    /// du portail gaté — consomme l'entrée en attente et démarre réellement l'instance via
    /// Enter() (méthode existante, inchangée). Retourne false si aucune entrée n'était en
    /// attente (le portail ne devrait normalement jamais appeler ceci sans avoir déjà vérifié
    /// PendingInstance != null via CanCross(), mais reste défensif).</summary>
    public bool ConsumePendingEntry()
    {
        if (PendingInstance == null) return false;
        IInstanceConfig config = PendingInstance;
        PendingInstance = null;
        return Enter(config);
    }

    /// <summary>Marque un trigger de donjon (mini-boss tué, levier actionné) comme acquis pour
    /// LE RUN EN COURS UNIQUEMENT — jamais persisté, remis à zéro à chaque Enter() (voir Step 4).
    /// Câblage réel (quel Mob.Die() appelle ceci) hors scope de ce chantier, voir spec §4
    /// Non-objectifs — cette méthode existe pour que Portal.CanCross() (Task 5) ait quelque chose
    /// à lire, le contenu qui l'appellera vraiment viendra avec un futur donjon réel.</summary>
    public void NotifyTriggerMet(string triggerID)
    {
        if (string.IsNullOrEmpty(triggerID)) return;
        _metTriggerIDs.Add(triggerID);
    }

    public bool IsTriggerMet(string triggerID) => _metTriggerIDs.Contains(triggerID);
```

- [ ] **Step 4: Reset triggers on Enter()**

Find the existing `Enter(IInstanceConfig config)` method's body. Right after this existing line:

```csharp
        CurrentInstance = config;
```

add exactly one new line so it reads:

```csharp
        CurrentInstance = config;
        _metTriggerIDs.Clear();
```

Do not change anything else in `Enter()` — same validation, same `LivesRemaining`/`CurrentOutcome`/`_returnMapName` assignments, same `SceneLoader.Instance.LoadMapWithSpawn(config.SceneName)` call, same `return true`.

- [ ] **Step 5: Compile check**

Unity Console: 0 errors. Confirm `using System.Collections.Generic;` was added (needed for `List<Player>`/`HashSet<string>`) and that no other file's `Enter(` call site broke (there should be exactly one, in `ConsoBarUI.cs`, which Task 2 updates next — a compile error there right now, before Task 2, is expected and fine, not a Task 1 problem).

- [ ] **Step 6: Commit**

```bash
git add Systems/InstanceSession.cs
git commit -m "feat: add pending-entry state and per-run triggers to InstanceSession"
```

---

### Task 2: `ConsoBarUI` — arm entry instead of entering directly

**Files:**
- Modify: `UI/PanelFixe/ConsoBarUI.cs:96-124` (the `ConsumableType.DungeonStone` branch inside `TryUseSlot`)

**Interfaces:**
- Consumes: `InstanceSession.Instance.ArmEntry(IInstanceConfig) : bool` (Task 1).

- [ ] **Step 1: Read the current branch**

The current code (as of commit `8cee222`) is:

```csharp
        if (data.consumableType == ConsumableType.DungeonStone)
        {
            var dungeon = DungeonRegistry.Instance?.Resolve(data.dungeonID);
            if (dungeon == null)
            {
                Debug.LogWarning($"[ConsoBarUI] Pierre de donjon '{data.dungeonID}' introuvable dans DungeonRegistry.");
                return;
            }
            bool entered = InstanceSession.Instance != null && InstanceSession.Instance.Enter(dungeon);
            if (!entered)
            {
                Debug.LogWarning($"[ConsoBarUI] Entrée en instance échouée pour '{data.dungeonID}' — item non consommé.");
                return;
            }
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
```

Read the live file first to confirm this matches exactly (line numbers may have drifted slightly, content should match).

- [ ] **Step 2: Change the entry call from `Enter` to `ArmEntry`**

Replace only these two lines:

```csharp
            bool entered = InstanceSession.Instance != null && InstanceSession.Instance.Enter(dungeon);
            if (!entered)
            {
                Debug.LogWarning($"[ConsoBarUI] Entrée en instance échouée pour '{data.dungeonID}' — item non consommé.");
                return;
            }
```

with:

```csharp
            bool armed = InstanceSession.Instance != null && InstanceSession.Instance.ArmEntry(dungeon);
            if (!armed)
            {
                Debug.LogWarning($"[ConsoBarUI] Entrée en attente refusée pour '{data.dungeonID}' — item non consommé.");
                return;
            }
```

Everything else in the branch (the `instance.Remove(1)` block through the trailing `return;`) stays exactly as it is — the item is still consumed only on success, cleanup/UI-refresh logic is unchanged.

- [ ] **Step 3: Update the method's XML doc comment**

The comment right above `TryUseSlot` currently reads (in part) "DungeonStone (entrée en instance) implémentés". Update to:

```csharp
    /// <summary>Utilise le consommable du slot — Potion/Food (heal HP/Mana + buff) et
    /// DungeonStone (arme une entrée en attente, voir InstanceSession.ArmEntry — le vrai
    /// chargement de l'instance a lieu au franchissement d'un portail gaté, pas ici)
    /// implémentés ; TeleportItem/Other pas encore câblés.</summary>
```

- [ ] **Step 4: Compile check**

Unity Console: 0 errors — this should now resolve the expected Task 1 compile gap (the one `Enter(` call site is now `ArmEntry(`, both exist on `InstanceSession`).

- [ ] **Step 5: Commit**

```bash
git add UI/PanelFixe/ConsoBarUI.cs
git commit -m "feat: DungeonStone consumption now arms a pending entry instead of entering directly"
```

---

### Task 3: `DungeonTrigger` + `DungeonMapData.triggers`

**Files:**
- Create: `Data/Content/DungeonTrigger.cs`
- Modify: `Data/Content/DungeonData.cs:25-34` (the `DungeonMapData` class)

**Interfaces:**
- Produces: `DungeonTriggerType` (enum), `DungeonTrigger` (serializable class) — no other task in this plan consumes `DungeonTrigger` directly (Task 5's `Portal.cs` only uses a `string requiredTriggerID`, never the `DungeonTrigger` type itself — this field stays declarative for this chantier, see spec §3 Non-objectifs).

- [ ] **Step 1: Create `DungeonTrigger.cs`**

```csharp
// =============================================================
// DUNGEONTRIGGER.CS — Condition de déverrouillage intra-donjon
// Path : Assets/Scripts/Data/Content/DungeonTrigger.cs
// AetherTree GDD v3.6 — §14.2.3 (champ triggers/lockedUntil sur DungeonMapData)
//
// Mécanisme minimal : un triggerID (string) que InstanceSession.NotifyTriggerMet()/
// IsTriggerMet() track pour LE RUN EN COURS (jamais persisté). Un Portal (voir
// Events/Portal.cs, gateType = RequiresTrigger) référence ce même triggerID pour
// se verrouiller/déverrouiller. Ce fichier reste purement déclaratif pour ce
// chantier — aucun système ne lit encore DungeonMapData.triggers, le vrai verrou
// vit sur Portal.requiredTriggerID (une simple string, pas ce type) — voir
// docs/superpowers/specs/2026-09-23-donjon-entry-flow-design.md §3 Non-objectifs.
// =============================================================

public enum DungeonTriggerType
{
    MobKilled      = 0,
    LeverActivated = 1,
}

[System.Serializable]
public class DungeonTrigger
{
    [Tooltip("Identifiant référencé par Portal.requiredTriggerID.")]
    public string triggerID;
    public DungeonTriggerType triggerType;
    [Tooltip("MobKilled uniquement — quel Mob doit mourir pour satisfaire ce trigger.")]
    public MobData requiredMob;
}
```

- [ ] **Step 2: Add `triggers` to `DungeonMapData`**

The current `DungeonMapData` class in `Data/Content/DungeonData.cs` is:

```csharp
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
```

Add one new field so it reads:

```csharp
[System.Serializable]
public class DungeonMapData
{
    [Tooltip("Identifiant de la salle — doit correspondre au nom de la scène Unity chargée.")]
    public string mapID;
    [Tooltip("Nom affiché à l'entrée de la salle.")]
    public string displayName;
    [Tooltip("true = salle de boss.")]
    public bool   isBossRoom;
    [Tooltip("Conditions de déverrouillage disponibles dans cette salle — déclaratif pour " +
             "l'instant, le verrou réel d'un portail se configure sur Portal.requiredTriggerID " +
             "(une string), pas lu automatiquement depuis cette liste.")]
    public List<DungeonTrigger> triggers = new List<DungeonTrigger>();
}
```

Also update the file's header comment (`Data/Content/DungeonData.cs` lines 9-15, the note that says "Pas de contenu réel de salle dans ce chantier (spawners/triggers/lockedUntil pas encore ajoutés à DungeonMapData)") to reflect that `triggers` now exists:

```csharp
// Couvre Donjon Classique ET Donjon de Déblocage via dungeonType — pas deux
// classes séparées (les deux partagent toute la structure GDD, seules les
// valeurs par défaut de livesPerPlayer/deathLimit/bossType diffèrent, voir
// §14.3.3 "Règles Spécifiques" du GDD). Pas de contenu réel de salle dans
// ce chantier (spawners/lockedUntil pas encore ajoutés à DungeonMapData —
// triggers ajouté par le chantier donjon-entry-flow, reste déclaratif, voir
// DungeonTrigger.cs) — voir docs/superpowers/specs/2026-09-23-instance-
// system-design.md §3 Non-objectifs.
```

- [ ] **Step 3: Compile check**

Unity Console: 0 errors. Confirm `Data/Content/DungeonData.cs` already has `using System.Collections.Generic;` at its top (it does, from the previous chantier — `List<DungeonMapData>`/`List<MobData>` already use it) so `List<DungeonTrigger>` resolves without a new using directive.

- [ ] **Step 4: Commit**

```bash
git add Data/Content/DungeonTrigger.cs Data/Content/DungeonData.cs
git commit -m "feat: add DungeonTrigger and DungeonMapData.triggers (declarative)"
```

---

### Task 4: `unlockedTiers` on Player

**Files:**
- Modify: `Entities/Player.cs` (new field + method)
- Modify: `Progression/Save/CharacterProgress.cs` (new persisted field)
- Modify: `Progression/Save/SaveSystem.cs` (save + load wiring)

**Interfaces:**
- Produces: `Player.HasUnlockedTier(int tier) : bool` — Task 5 (`Portal.cs`, `RequiresTierUnlock` gate) consumes this.

- [ ] **Step 1: Verify the field doesn't already exist under a different name**

```bash
grep -rn "unlockedTier\|unlockedPalier" Entities/Player.cs Progression/Save/CharacterProgress.cs
```

Expected: no match (confirmed absent by the 2026-09-23 GDD-vs-code audit — no Donjon system exists in the code yet). If this grep DOES find something, use that existing name instead of adding a new field, and adapt the rest of this task's steps to match it rather than creating a duplicate.

- [ ] **Step 2: Add the field and method to `Player.cs`**

Near the other simple `[HideInInspector]` progression list fields (e.g. right after `unlockedRecipes` at line 118), add:

```csharp
    [HideInInspector] public List<int> unlockedTiers = new List<int>();
```

Near where `unlockedRecipes` is null-guarded in initialization (around line 208, the block that does `if (unlockedRecipes == null) unlockedRecipes = new List<RecipeData>();`), add the same guard:

```csharp
        if (unlockedTiers == null) unlockedTiers = new List<int>();
```

Add a new public method anywhere among the other simple accessor methods (e.g. near `UnlockRecipe`, around line 784):

```csharp
    /// <summary>Un palier est débloqué par la réussite de son Donjon de Déblocage — voir GDD
    /// §14.3.4. Cette méthode ne fait QUE lire le flag ; rien n'écrit encore dedans (ça viendra
    /// avec le futur chantier Donjon de Déblocage), donc HasUnlockedTier renvoie toujours false
    /// pour l'instant tant qu'aucun contenu réel n'existe — c'est le comportement attendu, pas
    /// un bug de ce chantier.</summary>
    public bool HasUnlockedTier(int tier) => unlockedTiers.Contains(tier);
```

- [ ] **Step 3: Add the field to `CharacterProgress.cs`**

Near `elementAffinities`/other simple `List<int>` fields (e.g. `SavedQuestProgress.objectiveCounts` at line 192, or directly near `elementAffinities` at line 320), add:

```csharp
    // Paliers débloqués via Donjon de Déblocage — voir Player.HasUnlockedTier.
    public List<int> unlockedTiers = new List<int>();
```

- [ ] **Step 4: Wire save/load in `SaveSystem.cs`**

Find where `player.unlockedRecipes` is saved (around line 336-338):

```csharp
        if (player.unlockedRecipes != null)
            foreach (var recipe in player.unlockedRecipes)
                if (recipe != null) progress.unlockedRecipeNames.Add(recipe.name);
```

Add, right after it:

```csharp
        if (player.unlockedTiers != null)
            progress.unlockedTiers = new List<int>(player.unlockedTiers);
```

Find the corresponding LOAD-side restoration — search for where `progress.unlockedRecipeNames` or a similarly simple field gets copied back onto `player` during a character load (grep `unlockedRecipeNames` in `SaveSystem.cs` to find the restore method, then add the mirrored line right there):

```csharp
        if (p.unlockedTiers != null)
            player.unlockedTiers = new List<int>(p.unlockedTiers);
```

(`p`/`progress` naming: match whatever the surrounding restore method actually calls its `CharacterProgress` parameter — read the method signature first rather than assuming `p`.)

- [ ] **Step 5: Compile check**

Unity Console: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Entities/Player.cs Progression/Save/CharacterProgress.cs Progression/Save/SaveSystem.cs
git commit -m "feat: add unlockedTiers to Player/CharacterProgress (field + read-only accessor)"
```

---

### Task 5: `Portal.cs` — composable gate

**Files:**
- Modify: `Events/Portal.cs`

**Interfaces:**
- Consumes: `InstanceSession.Instance.PendingInstance`/`.IsTriggerMet(string)`/`.ConsumePendingEntry()` (Task 1), `Player.HasUnlockedTier(int)` (Task 4), `IInstanceConfig.InstanceID` (existing).
- Produces: `PortalGateType` (enum) — no later task in this plan consumes it, it's the terminal piece content authors will configure directly in the Unity Inspector.

- [ ] **Step 1: Read the current file**

Read `Events/Portal.cs` in full (it's short, ~123 lines) before editing — the exact current `OnTriggerEnter` is:

```csharp
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<Player>() == null) return;
        if (_cooldownTimer > 0f) return;
        if (_teleporting) return;
        StartCoroutine(TeleportRoutine());
    }
```

- [ ] **Step 2: Add the `PortalGateType` enum**

Add this new enum right before the `public class Portal` declaration:

```csharp
public enum PortalGateType
{
    None                 = 0, // Comportement actuel — libre, aucun changement pour l'existant
    RequiresDungeonEntry = 1, // InstanceSession.PendingInstance valide pour ce portail précis
    RequiresTierUnlock   = 2, // Player.HasUnlockedTier(targetTier)
    RequiresTrigger      = 3, // InstanceSession.IsTriggerMet(requiredTriggerID)
}
```

- [ ] **Step 3: Add the gate fields**

Inside the `Portal` class, add a new header section — place it after the existing `[Header("Paramètres")]` block (after `teleportDelay`) and before the private fields:

```csharp
    [Header("Verrou")]
    [Tooltip("None = libre (comportement actuel, inchangé). Les 3 autres valeurs bloquent le " +
             "franchissement tant que leur condition n'est pas remplie.")]
    public PortalGateType gateType = PortalGateType.None;

    [Tooltip("InstanceID du donjon attendu — doit matcher IInstanceConfig.InstanceID de la " +
             "config actuellement en attente (InstanceSession.PendingInstance).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresDungeonEntry)]
    public string linkedInstanceID = "";

    [Tooltip("Palier requis débloqué (Player.HasUnlockedTier).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresTierUnlock)]
    public int targetTier = 2;

    [Tooltip("triggerID requis acquis pendant le run en cours (InstanceSession.IsTriggerMet).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresTrigger)]
    public string requiredTriggerID = "";
```

- [ ] **Step 4: Add `CanCross`**

Add this new private method anywhere in the class (e.g. right above `OnTriggerEnter`):

```csharp
    private bool CanCross(Player player)
    {
        switch (gateType)
        {
            case PortalGateType.None:
                return true;
            case PortalGateType.RequiresDungeonEntry:
                return InstanceSession.Instance != null
                    && InstanceSession.Instance.PendingInstance != null
                    && InstanceSession.Instance.PendingInstance.InstanceID == linkedInstanceID;
            case PortalGateType.RequiresTierUnlock:
                return player != null && player.HasUnlockedTier(targetTier);
            case PortalGateType.RequiresTrigger:
                return InstanceSession.Instance != null
                    && InstanceSession.Instance.IsTriggerMet(requiredTriggerID);
            default:
                return true;
        }
    }
```

- [ ] **Step 5: Wire `CanCross` into `OnTriggerEnter`, and redirect `RequiresDungeonEntry`**

Replace the current `OnTriggerEnter`:

```csharp
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<Player>() == null) return;
        if (_cooldownTimer > 0f) return;
        if (_teleporting) return;
        StartCoroutine(TeleportRoutine());
    }
```

with:

```csharp
    private void OnTriggerEnter(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player == null) return;
        if (_cooldownTimer > 0f) return;
        if (_teleporting) return;

        if (!CanCross(player))
        {
            Debug.Log($"[PORTAL] {portalID} — franchissement bloqué (gateType = {gateType}).");
            return;
        }

        if (gateType == PortalGateType.RequiresDungeonEntry)
        {
            // L'entrée en donjon ne suit pas le chemin normal targetMap/targetPortalID — la
            // vraie destination vient de la config résolue par InstanceSession, pas de ce champ.
            bool entered = InstanceSession.Instance != null && InstanceSession.Instance.ConsumePendingEntry();
            if (!entered)
                Debug.LogWarning($"[PORTAL] {portalID} — ConsumePendingEntry() a échoué malgré CanCross() vrai.");
            return;
        }

        StartCoroutine(TeleportRoutine());
    }
```

- [ ] **Step 6: Compile check + manual Inspector verification**

Unity Console: 0 errors. Open an EXISTING portal prefab/instance already in a scene (any world-open-map portal) — confirm `gateType` defaults to `None` in the Inspector and none of the new gated fields show (matches the `[ShowIf]` behavior established elsewhere in this project, e.g. `SkillData`/`DungeonData`). Confirm that portal still crosses freely in Play Mode (a quick manual walk-through, not the full Task 7 test suite) — this is the regression check that the default path is untouched.

- [ ] **Step 7: Commit**

```bash
git add Events/Portal.cs
git commit -m "feat: add composable gate to Portal (dungeon entry / tier unlock / trigger)"
```

---

### Task 6: Group-donjon panel

**Files:**
- Create: `UI/PanelFixe/DungeonGroupPanelUI.cs`

**Interfaces:**
- Consumes: `InstanceSession.Instance.PendingInstance`/`.CurrentInstance`/`.Participants`/`.IsLeader`/`.LivesRemaining` (Task 1), `Player.CurrentHP`/`.MaxHP`/`.CurrentMana`/`.MaxMana`/`.entityName` (existing, `Entities/Entity.cs`), `IInstanceConfig.DisplayName`/`.LivesPerPlayer` (existing).

- [ ] **Step 1: Read the reference pattern**

Read `UI/PanelFixe/PlayerInfosPanel.cs` in full before writing this task — it establishes the HUD panel convention this new file must follow: a `public static X Instance` singleton set in `Awake()`, `_player = FindObjectOfType<Player>()` resolved in `Start()`, all data refreshed in `Update()` (no event-driven refresh for this first pass, matching that file's own documented convention "Toutes les données sont lues en Update() — pas d'events requis").

- [ ] **Step 2: Create the file**

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// DUNGEONGROUPPANELUI.CS — Panel groupe donjon (raid-style)
// Path : Assets/Scripts/UI/PanelFixe/DungeonGroupPanelUI.cs
// AetherTree GDD v3.6 — voir docs/superpowers/specs/2026-09-23-
// donjon-entry-flow-design.md §5.4
//
// Visible dès qu'une entrée est en attente (InstanceSession.PendingInstance)
// OU qu'une instance est active (CurrentInstance) — caché sinon. Comme
// PlayerInfosPanel, tout est relu en Update(), pas d'event dédié pour ce
// premier passage. En solo, Participants ne contient jamais que le joueur
// local — la structure (une ligne par participant) reste prête pour un
// futur groupe réel sans réécriture.
//
// SETUP HIERARCHY (exemple) :
//   DungeonGroupPanelUI (racine — SetActive piloté par ce script)
//     ├── HeaderText            (TMP — nom du donjon)
//     ├── ParticipantRowsParent (Transform — 1 ligne instanciée par participant)
//     └── FooterText            (TMP — vies restantes)
// =============================================================

public class DungeonGroupPanelUI : MonoBehaviour
{
    public static DungeonGroupPanelUI Instance { get; private set; }

    [Header("Racine")]
    [Tooltip("GameObject affiché/caché selon qu'une entrée est en attente ou active.")]
    public GameObject panelRoot;

    [Header("Header")]
    public TextMeshProUGUI headerText;

    [Header("Corps")]
    [Tooltip("Préfab d'une ligne participant (nom + HP/MP) — instancié dynamiquement.")]
    public GameObject participantRowPrefab;
    [Tooltip("Parent où les lignes participant sont instanciées/détruites.")]
    public Transform participantRowsParent;

    [Header("Footer")]
    public TextMeshProUGUI footerText;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        var session = InstanceSession.Instance;
        IInstanceConfig active = session != null
            ? session.CurrentInstance ?? session.PendingInstance
            : null;

        if (active == null)
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            return;
        }

        if (panelRoot != null) panelRoot.SetActive(true);

        if (headerText != null) headerText.text = active.DisplayName;

        RefreshParticipants(session);

        if (footerText != null)
        {
            int lives = session.CurrentInstance != null ? session.LivesRemaining : active.LivesPerPlayer;
            footerText.text = $"Vies restantes : {lives}";
        }
    }

    private void RefreshParticipants(InstanceSession session)
    {
        if (participantRowsParent == null || participantRowPrefab == null) return;

        for (int i = participantRowsParent.childCount - 1; i >= 0; i--)
            Destroy(participantRowsParent.GetChild(i).gameObject);

        foreach (var participant in session.Participants)
        {
            if (participant == null) continue;

            GameObject row = Instantiate(participantRowPrefab, participantRowsParent);
            TextMeshProUGUI[] texts = row.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                texts[0].text = participant.entityName;
                texts[0].color = session.IsLeader ? Color.yellow : Color.white;
            }
            if (texts.Length > 1)
                texts[1].text = $"{participant.CurrentHP:0}/{participant.MaxHP:0} HP — " +
                                 $"{participant.CurrentMana:0}/{participant.MaxMana:0} MP";
        }
    }
}
```

**Sur la structure `participantRowPrefab`/`GetComponentsInChildren<TextMeshProUGUI>()`** : ce
pattern suppose un préfab simple avec 2 `TextMeshProUGUI` enfants (nom, puis HP/MP) — c'est une
implémentation minimale pour ce premier passage (un seul participant en solo, donc une seule
ligne visible), pas une UI finale. Florian ajustera le préfab exact dans l'éditeur ; le script
reste correct tant que le préfab a au moins un `TextMeshProUGUI` enfant.

- [ ] **Step 3: Compile check**

Unity Console: 0 errors. In the Unity Editor, create the GameObject hierarchy described in the header comment's "SETUP HIERARCHY" block, wire the Inspector references, and confirm the panel starts hidden (no active instance/pending entry at scene start).

- [ ] **Step 4: Commit**

```bash
git add UI/PanelFixe/DungeonGroupPanelUI.cs
git commit -m "feat: add DungeonGroupPanelUI — header/participants/lives display"
```

---

### Task 7: Final verification (Florian, manual Play Mode)

**Files:** none (verification only).

**Interfaces:** none — this task exercises the full chain built by Tasks 1-6, on top of the previous chantier's Instance System (already verified separately).

- [ ] **Step 1: Full compile check**

Open Unity, wait for full recompile, Console: 0 error, 0 exception.

- [ ] **Step 2: Prepare test assets** (Florian, in-editor)

1. Create/reuse a test `DungeonData` (`dungeonType = Classic`) with **two** `DungeonMapData` entries: `map_test_1` (`isBossRoom = false`, one `DungeonTrigger` in its `triggers` list with `triggerID = "test_lever"`, `triggerType = LeverActivated`) and `map_test_boss` (`isBossRoom = true`). Both `mapID`s must match real Unity scenes in Build Settings (create throwaway empty scenes if needed).
2. Place a `PlayerSpawnPoint`-named empty GameObject in each test scene.
3. In the open-world test map, place a `Portal` with `gateType = RequiresDungeonEntry`, `linkedInstanceID` = the test dungeon's `dungeonID`.
4. In `map_test_1`, place a second `Portal` with `gateType = RequiresTrigger`, `requiredTriggerID = "test_lever"`, `targetMap = "map_test_boss"`.
5. Create a `ConsumableData` (`consumableType = DungeonStone`, `dungeonID` = the test dungeon's `dungeonID`), add it to the test Player's inventory, equip it to a ConsoBar slot.
6. Set up `DungeonGroupPanelUI` in the scene per Task 6's hierarchy.

- [ ] **Step 3: Arm entry, verify panel appears without loading anything**

Use the equipped Pierre. Expected: `InstanceSession.Instance.PendingInstance` is set (inspect via the component in Play Mode), the group panel appears (header = test dungeon name, one participant row = the local player, footer shows `LivesPerPlayer`), and **no scene load happens** — the player is still standing in the open world.

- [ ] **Step 4: Cross the gated entry portal**

Walk into the `RequiresDungeonEntry` portal. Expected: scene loads to `map_test_1` (`SceneLoader` `OnMapLoaded` log), `PendingInstance` is now null, `CurrentInstance` is set, panel footer switches to `LivesRemaining`.

- [ ] **Step 5: Confirm the internal portal is locked, then unlock it**

Walk into the `RequiresTrigger` portal inside `map_test_1` — expected: blocked (`Debug.Log` "franchissement bloqué"). Manually call `InstanceSession.Instance.NotifyTriggerMet("test_lever")` (temporary test button, or via the Inspector if a public method is invokable, or a one-line debug script). Walk into the portal again — expected: now crosses normally to `map_test_boss` via the regular `TeleportRoutine()` path (not `ConsumePendingEntry()` — that's only for `RequiresDungeonEntry`).

- [ ] **Step 6: Full death/failure/success cycle**

Repeat the previous chantier's Task 7 checks (die once → respawn in place via `ReloadCurrentMap`; die on the last life → `Failure`, expulsion after `exitDelay`, player alive and controllable on the open-world map — not the pre-fix-wave softlock; or call `InstanceSession.Instance.OnBossKilled()` manually → `Success`, same expulsion flow). Confirm the group panel hides itself once `CurrentInstance` returns to null after expulsion.

- [ ] **Step 7: Trigger reset across runs**

Re-arm and re-enter the test dungeon a second time (fresh Pierre). Expected: `map_test_1`'s `RequiresTrigger` portal is blocked again at the start of this new run — confirms `_metTriggerIDs.Clear()` in `Enter()` actually reset between runs, not just within the first one.

- [ ] **Step 8: Regression check — an existing, unrelated portal**

Walk through any pre-existing open-world portal (`gateType = None` by default, untouched by this chantier). Expected: crosses exactly as it always has, no behavior change, no new log lines.

- [ ] **Step 9: Report to Florian**

Summarize pass/fail for Steps 3-8 in chat. Any failure goes through `superpowers:systematic-debugging` before any fix — no guessing.

---

## Self-Review Notes (completed during plan authoring, not a step to re-run)

- **Spec coverage**: §5.1 (Portal) → Task 5. §5.2 (DungeonTrigger) → Task 3. §5.3 (InstanceSession) → Task 1. §5.4 (panel) → Task 6. §2 (ConsoBarUI revision) → Task 2. §4.1 (unlockedTier verification) → Task 4. §6 (verification) → Task 7. All spec sections have a task.
- **Placeholder scan**: no TBD/TODO in any in-scope code block. `DungeonTrigger`'s real content-wiring (which Mob calls `NotifyTriggerMet`) is explicitly out of scope per spec §4 Non-objectifs, not a placeholder within scope — same discipline as the previous chantier's `OnBossKilled()` hook.
- **Type consistency**: `InstanceSession.ArmEntry`/`ConsumePendingEntry`/`NotifyTriggerMet`/`IsTriggerMet` (Task 1) match their call sites exactly in Task 2 (`ArmEntry`) and Task 5 (`ConsumePendingEntry`/`IsTriggerMet`/read of `PendingInstance`). `Player.HasUnlockedTier(int)` (Task 4) matches its Task 5 call site. `DungeonMapData.triggers` (Task 3) uses the `DungeonTrigger` type also defined in Task 3, no forward reference to an undefined type.
