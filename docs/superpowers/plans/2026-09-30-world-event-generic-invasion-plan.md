# World Event Generic Dispatcher + Invasion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generalize yesterday's Boss-Géant-only `WorldEventScheduler` into a real multi-type event dispatcher, and ship Invasion as the second event type.

**Architecture:** `WorldEventScheduler` becomes a pure dispatcher (shared timer + announce cadence, picks a random `WorldEventData` from a pool). A new abstract `WorldEventData` ScriptableObject base class owns everything common to every event type (eligible-map roll, live participation tracking via `GameEventBus.OnDamageDealt`, reward granting). `WorldBossData` is restructured on top of it (each boss entry carries its own reward table); `InvasionData` is new (5 fixed waves + endless reinforcements while its boss survives). Yesterday's `MobData.massEventRewards` mechanism is fully reverted — superseded by the event-level reward mechanism.

**Tech Stack:** Unity C#, no automated test framework — every task ends with a manual Play Mode verification step Florian runs himself.

**Spec:** `docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md`

## Global Constraints

- One active event at a time — the scheduler's cycle only proceeds to the next random draw once the running event's `RunEvent` coroutine fully completes. No overlapping events.
- Timer and both announce offsets (`minInterval`/`maxInterval`/`firstWarningOffset`/`secondWarningOffset`) live ONLY on `WorldEventScheduler` — never duplicated onto `WorldBossData`/`InvasionData`/future event types.
- Event-wide participation is tracked by the EVENT itself via `GameEventBus.OnDamageDealt` (any damage ≥0, from a `Player`, to a mob the event owns) as a per-player HIT COUNT, not a per-mob `Mob.Die()` computation — a player only becomes reward-eligible once their count reaches `WorldEventData.minHitsToBeEligible` (default 10; a single drive-by hit is not enough).
- `WorldBossData` must never call `StopTracking()`/`GrantEventRewards()` from inside a `Mob.OnDeath` callback directly — `Mob.Die()` runs synchronously inside `Entity.TakeDamage()`, BEFORE the caller (`SkillSystem`) publishes that same hit's `DamageDealtEvent`; resolving synchronously in the callback would silently drop the killing blow's player from the hit count. Resolution must happen after the `RunEvent` coroutine's `WaitUntil` unblocks (a later frame), never inside the callback itself.
- `MobData.massEventRewards` and its special-casing in `LootManager`/`XPSystem` are removed entirely — restore those two files' reward logic to read `eligiblePlayers` directly, exactly as before it was introduced.
- The event-level reward distribution (items no-repeat draw, extras unawarded if more items than players, no wrap-around; XP/Prestige to every eligible player) is a NEW public code path (`LootManager.GrantEventLoot`/`XPSystem.GrantEventRewards`), separate from the normal per-mob `MobKilledEvent` pipeline.
- `WorldEventMapEntry` lives in exactly one file (`Data/Content/WorldEventData.cs`) — never duplicated into `WorldBossData.cs`.
- No wave in Invasion waits for the previous wave to be cleared before spawning — waves and reinforcements pop strictly on the shared `waveInterval` timer, mobs accumulate.

## Review Focus

- **A player who only ever hits wave-1/wave-2 trash mobs, never the Invasion boss itself, but reaches `minHitsToBeEligible` (10) total hits on those trash mobs** — must still be counted eligible and receive the event's final reward. This is the entire reason `OwnsMob`/`OnDamageDealt`-based tracking exists instead of reusing `Mob.Die()`'s per-mob computation — a task's verification must exercise this specifically, not just "someone who tanked everything."
- **A player who hits an event mob fewer than `minHitsToBeEligible` times (e.g. a drive-by 1-3 hits) then leaves** — must NOT receive the reward. Confirms the threshold actually filters, not just that tracking exists.
- **A player's hit that BOTH crosses `minHitsToBeEligible` AND kills the World Boss in the same blow** (their 10th hit is also the fatal one) — must still count toward eligibility and receive the reward. `Mob.Die()` runs synchronously inside `Entity.TakeDamage()`, before `SkillSystem` publishes that hit's `DamageDealtEvent` — resolving the event (stopping tracking / granting rewards) directly from the `Mob.OnDeath` callback would race ahead of that event and silently drop this exact player. This is the specific case the deferred-resolution fix (Task 5) targets — a test that only checks "the boss dies → someone gets rewarded" would miss it.
- **Invasion boss dies while wave-1 mobs (or reinforcements) are still alive** — spawning must stop immediately, but the reward must NOT be granted until every remaining invasion mob is also dead. A test that only checks "boss dies → reward" would miss a premature-reward bug here.
- **Two different `WorldBossEntry` (or `InvasionVariant`) rolled on different cycles** — each must grant its OWN `rewardTable`, not always the first one in the list or a shared/stale one. An easy copy-paste bug (closing over the wrong loop variable, or reading `possibleBosses[0]` instead of the rolled entry) would silently always reward the same table.
- **`eventPool` contains a `WorldBossData` with an empty `possibleBosses`/`eligibleMaps` (or an `InvasionData` with no `possibleVariants`)** — that specific cycle must be skipped (warn, no crash, no infinite hang), and the SCHEDULER must still move on to roll again next interval, not get stuck retrying the same broken asset forever.
- **The old `massEventRewards` revert leaves no trace** — a normal (non-event) mob with 2+ loot entries must go back to fully independent per-item draws (a single player CAN win more than one item), matching pre-2026-09-29 behavior exactly. Confirms the revert didn't leave a half-migrated state.

---

### Task 1: Revert `MobData.massEventRewards` and its wiring

**Files:**
- Modify: `Data/Mobs/MobData.cs`
- Modify: `Systems/LootManager.cs`
- Modify: `Progression/XPSystem.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MobKilledEvent.eligiblePlayers`/`contributingPlayers` (already existing, untouched) become the ONLY inputs `LootManager`/`XPSystem` read again — later tasks' `GrantEventLoot`/`GrantEventRewards` (Task 3) are separate, unrelated methods, not a continuation of this removed mechanism.

- [ ] **Step 1: Remove `MobData.massEventRewards`**

In `Data/Mobs/MobData.cs`, find:

```csharp
    // ── Loot ──────────────────────────────────────────────────
    [Header("Loot")]
    public LootTable lootTable;

    [Tooltip("Coché : XP/Prestige/loot vont à TOUT joueur ayant infligé ≥1 dégât " +
             "(MobKilledEvent.contributingPlayers), pas seulement ceux ≥10% des dégâts totaux " +
             "(eligiblePlayers, seuil normal). Pensé pour les boss à grande échelle (World Boss, " +
             "Invasion) où atteindre 10% devient irréaliste avec des dizaines/centaines de " +
             "participants simultanés — un seuil pensé pour un donjon 5 joueurs, pas un event " +
             "monde. Laisser décoché pour tout le reste (dungeon, mobs normaux).")]
    public bool massEventRewards = false;

    // ── Capture / Pet ─────────────────────────────────────────
```

Replace with:

```csharp
    // ── Loot ──────────────────────────────────────────────────
    [Header("Loot")]
    public LootTable lootTable;

    // ── Capture / Pet ─────────────────────────────────────────
```

Leave every other field on `MobData.cs` (including `hardCCResistance`/`softDebuffResistance`, `debuffResistances`) exactly as-is — this step touches only this one block.

- [ ] **Step 2: Revert `LootManager.cs`**

Replace the whole file header comment AND `OnMobKilled` body. Find:

```csharp
// =============================================================
// LOOTMANAGER — Livre le loot d'un mob directement dans
// l'inventaire du/des joueur(s) éligible(s), plus de spawn au sol.
// Path : Assets/Scripts/Systems/LootManager.cs
// AetherTree GDD v3.5
//
// S'abonne à GameEventBus.OnMobKilled. Un seul roll partagé
// (LootTable.RollAll()) ; chaque item ET l'Aeris tirent
// indépendamment un gagnant aléatoire parmi eligiblePlayers
// (≥10% des dégâts totaux, calculé par Mob.Die()).
// Si l'inventaire du gagnant est plein, l'item part en mail de
// secours (MailboxSystem.SendLootOverflowMail) plutôt que d'être
// perdu. L'Aeris n'a jamais ce problème (AerisSystem sans plafond).
//
// MobData.massEventRewards (World Boss/Invasion) change la règle ITEMS uniquement (Florian,
// 2026-09-29) : chaque item droppé (LootEntry.dropChance déjà tiré indépendamment dans
// RollAll(), inchangé) va à un joueur DIFFÉRENT parmi le pool restant — un joueur déjà gagnant
// est retiré du tirage pour les items suivants, jamais 2 items au même joueur. Si le pool
// s'épuise avant la fin des items droppés, les items en trop ne sont PAS attribués (pas de
// bouclage/répétition — "tant pis"). L'Aeris n'est PAS concerné par cette règle (indépendant,
// inchangé) — de toute façon un World Boss n'est pas censé en donner (aerisDropChance = 0 sur
// son LootTable), donc la question ne se pose pas en pratique.
// =============================================================
```

Replace with:

```csharp
// =============================================================
// LOOTMANAGER — Livre le loot d'un mob directement dans
// l'inventaire du/des joueur(s) éligible(s), plus de spawn au sol.
// Path : Assets/Scripts/Systems/LootManager.cs
// AetherTree GDD v3.5
//
// S'abonne à GameEventBus.OnMobKilled. Un seul roll partagé
// (LootTable.RollAll()) ; chaque item ET l'Aeris tirent
// indépendamment un gagnant aléatoire parmi eligiblePlayers
// (≥10% des dégâts totaux, calculé par Mob.Die()).
// Si l'inventaire du gagnant est plein, l'item part en mail de
// secours (MailboxSystem.SendLootOverflowMail) plutôt que d'être
// perdu. L'Aeris n'a jamais ce problème (AerisSystem sans plafond).
//
// GrantEventLoot (voir plus bas) est un chemin SÉPARÉ, pour la récompense de fin d'événement
// (World Boss/Invasion, voir Data/Content/WorldEventData.cs) — jamais déclenché par
// GameEventBus.OnMobKilled, jamais mélangé avec la logique ci-dessous.
// =============================================================
```

Then find:

```csharp
    private void OnMobKilled(MobKilledEvent e)
    {
        // massEventRewards (MobData) : ≥1 dégât suffit (contributingPlayers) au lieu du seuil
        // ≥10% (eligiblePlayers) — même raison que XPSystem.HandleMobKilled, voir Mob.Die().
        var rewardPool = e.mob != null && e.mob.massEventRewards ? e.contributingPlayers : e.eligiblePlayers;
        if (rewardPool == null || rewardPool.Count == 0) return;

        if (e.mob?.lootTable == null)
        {
            Debug.LogWarning($"[LOOTMANAGER] Pas de LootTable sur {e.mob?.mobName}.");
            return;
        }

        LootRollResult roll = e.mob.lootTable.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        string mobName = e.mob.mobName;

        if (e.mob.massEventRewards)
        {
            // Un item par joueur max, tirage SANS remise — un gagnant est retiré du pool pour
            // les items suivants. Pool épuisé avant la fin des items droppés → items restants
            // non attribués, pas de bouclage (Florian, 2026-09-29 : "tant pis").
            var remainingPool = new List<Player>(rewardPool);
            foreach (InventoryItem item in roll.items)
            {
                if (remainingPool.Count == 0) break;
                int index = Random.Range(0, remainingPool.Count);
                Player winner = remainingPool[index];
                remainingPool.RemoveAt(index);
                DeliverItem(winner, item, mobName);
            }
        }
        else
        {
            foreach (InventoryItem item in roll.items)
            {
                Player winner = PickRandomEligible(rewardPool);
                DeliverItem(winner, item, mobName);
            }
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(rewardPool);
            DeliverAeris(winner, roll.aeris, mobName);
        }
    }
```

Replace with:

```csharp
    private void OnMobKilled(MobKilledEvent e)
    {
        if (e.eligiblePlayers == null || e.eligiblePlayers.Count == 0) return;

        if (e.mob?.lootTable == null)
        {
            Debug.LogWarning($"[LOOTMANAGER] Pas de LootTable sur {e.mob?.mobName}.");
            return;
        }

        LootRollResult roll = e.mob.lootTable.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        string mobName = e.mob.mobName;

        foreach (InventoryItem item in roll.items)
        {
            Player winner = PickRandomEligible(e.eligiblePlayers);
            DeliverItem(winner, item, mobName);
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(e.eligiblePlayers);
            DeliverAeris(winner, roll.aeris, mobName);
        }
    }
```

- [ ] **Step 3: Revert `XPSystem.cs`**

Find:

```csharp
    private void HandleMobKilled(MobKilledEvent e)
    {
        if (e.mob == null) return;

        // massEventRewards (MobData) : ≥1 dégât suffit (contributingPlayers) au lieu du seuil
        // ≥10% (eligiblePlayers) — un World Boss/Invasion à des dizaines de participants rendrait
        // 10% des dégâts totaux irréaliste pour presque tout le monde, voir Mob.Die().
        var rewardPool = e.mob.massEventRewards ? e.contributingPlayers : e.eligiblePlayers;

        if (rewardPool != null && rewardPool.Count > 0)
        {
            // XP depuis LootTable — source de vérité centralisée
            int xp = e.mob.lootTable?.xpReward ?? 0;
            if (xp > 0)
                foreach (Player p in rewardPool)
                    GiveCombatXP(p, xp);

            // Prestige depuis LootTable — 0 par défaut, réservé aux boss/mobs notables
            // (voir spec Prestige/Aura §1.4).
            int prestige = e.mob.lootTable?.prestigeReward ?? 0;
            if (prestige > 0)
                foreach (Player p in rewardPool)
                    p.AddPrestige(prestige);
        }

        GiveSpiritXP(e);
    }
```

Replace with:

```csharp
    private void HandleMobKilled(MobKilledEvent e)
    {
        if (e.mob == null) return;

        if (e.eligiblePlayers != null && e.eligiblePlayers.Count > 0)
        {
            // XP depuis LootTable — source de vérité centralisée
            int xp = e.mob.lootTable?.xpReward ?? 0;
            if (xp > 0)
                foreach (Player p in e.eligiblePlayers)
                    GiveCombatXP(p, xp);

            // Prestige depuis LootTable — 0 par défaut, réservé aux boss/mobs notables
            // (voir spec Prestige/Aura §1.4).
            int prestige = e.mob.lootTable?.prestigeReward ?? 0;
            if (prestige > 0)
                foreach (Player p in e.eligiblePlayers)
                    p.AddPrestige(prestige);
        }

        GiveSpiritXP(e);
    }
```

- [ ] **Step 4: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors in the Console (there must be no remaining reference to `massEventRewards` anywhere in the project after this step; a leftover reference would be a compile error, not a silent bug, so a clean compile IS the proof).

- [ ] **Step 5: Verify the revert behaviorally (Review Focus item 5)**

Find (or temporarily configure) a normal, non-event `MobData` whose `LootTable` has 2+ item entries with high `dropChance` (near 1.0, so both reliably drop). Kill it in Play Mode — confirm the existing behavior from before 2026-09-29 is back: nothing prevents the SAME player (you, in solo) from being picked as the winner for both items — there is no "one item max" restriction anymore for a normal mob kill.

- [ ] **Step 6: Commit**

```bash
git add Data/Mobs/MobData.cs Systems/LootManager.cs Progression/XPSystem.cs
git commit -m "revert: remove MobData.massEventRewards, superseded by event-level rewards"
```

---

### Task 2: `MobData.cs` — add `MobType.MobInvasion`

**Files:**
- Modify: `Data/Mobs/MobData.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MobType.MobInvasion` (ordinal 6) — Task 6's `InvasionData` documents that wave/reinforcement `MobData` entries should use this type (not enforced in code, matches the project's existing soft-validation convention for `MobType`/`DungeonRole` combinations).

- [ ] **Step 1: Add the new enum member**

In `Data/Mobs/MobData.cs`, find:

```csharp
public enum MobType
{
    Normal       = 0,  // Mob standard, monde ouvert
    MobDungeon   = 1,  // Tout mob de donjon (normal/objectif/spécial/boss) — voir DungeonRole
    BossMap      = 2,  // ex-BossZone — erre en zone ouverte (Palier 1, mini-boss)
    BossWorld    = 4,  // Boss Géant (événement, spawn sur un palier random)
    BossInvasion = 5,  // Boss de l'événement Invasion
}
```

Replace with:

```csharp
public enum MobType
{
    Normal       = 0,  // Mob standard, monde ouvert
    MobDungeon   = 1,  // Tout mob de donjon (normal/objectif/spécial/boss) — voir DungeonRole
    BossMap      = 2,  // ex-BossZone — erre en zone ouverte (Palier 1, mini-boss)
    BossWorld    = 4,  // Boss Géant (événement, spawn sur un palier random)
    BossInvasion = 5,  // Boss de l'événement Invasion — voir InvasionVariant.boss
    MobInvasion  = 6,  // Mob de vague/renfort d'une Invasion (le trash, pas le boss) — voir
                       // InvasionWave/InvasionVariant.reinforcements (Data/Content/InvasionData.cs)
}
```

- [ ] **Step 2: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors.

- [ ] **Step 3: Verify the new value is selectable**

Open any `MobData` asset in the Inspector, open the `Mob Type` dropdown — confirm `Mob Invasion` now appears as an option alongside the existing values.

**Amendement post-exécution (2026-09-30)** — après exécution de cette tâche telle quelle,
Florian a demandé le même patron que `MobDungeon`/`DungeonRole` plutôt qu'un `MobType` séparé pour
le boss : `BossInvasion` (ordinal 5) est retiré, `MobInvasion` (ordinal 6, inchangé) couvre
maintenant TOUT mob d'invasion (trash ET boss), distingués par un nouveau champ `invasionRole:
InvasionRole` (`Normal`/`Boss`, `ShowIf(mobType, MobInvasion)`) sur `MobData`, et `IsBoss()` est
mis à jour en conséquence. Voir commit `79e2952` (après le commit initial de cette tâche) pour le
diff réel — non reflété dans le before/after ci-dessus, qui documente l'état intermédiaire tel
qu'exécuté avant cet amendement.

- [ ] **Step 4: Commit**

```bash
git add Data/Mobs/MobData.cs
git commit -m "feat: add MobType.MobInvasion for Invasion wave/reinforcement mobs"
```

---

### Task 3: `LootManager`/`XPSystem` — event-level reward granting methods

**Files:**
- Modify: `Systems/LootManager.cs`
- Modify: `Progression/XPSystem.cs`

**Interfaces:**
- Consumes: `LootTable.RollAll()`/`.xpReward`/`.prestigeReward` (existing, unchanged). `Player` (existing).
- Produces: `LootManager.GrantEventLoot(LootTable table, List<Player> eligiblePlayers)` and `XPSystem.GrantEventRewards(LootTable table, List<Player> eligiblePlayers)` — both public, instance methods, called via `.Instance?.` — Task 4's `WorldEventData.GrantEventRewards` calls both directly.

- [ ] **Step 1: Add `LootManager.GrantEventLoot`**

In `Systems/LootManager.cs`, immediately after the closing brace of `OnMobKilled` (the version restored in Task 1) and before the `// DISTRIBUTION` region comment, insert:

```csharp
    // =========================================================
    // RÉCOMPENSE D'ÉVÉNEMENT (World Boss / Invasion)
    // =========================================================

    /// <summary>Distribue une LootTable à une liste de joueurs, HORS du pipeline
    /// GameEventBus.OnMobKilled — pour la récompense de fin d'événement (World Boss/Invasion,
    /// voir Data/Content/WorldEventData.cs), jamais liée à la mort d'un mob précis. Chaque item
    /// va à un joueur DIFFÉRENT (tirage sans remise) ; si plus d'items droppent que de joueurs
    /// éligibles, les items en trop ne sont PAS attribués (pas de bouclage/répétition).</summary>
    public void GrantEventLoot(LootTable table, List<Player> eligiblePlayers)
    {
        if (table == null || eligiblePlayers == null || eligiblePlayers.Count == 0) return;

        LootRollResult roll = table.RollAll();
        if (roll.items.Count == 0 && roll.aeris == 0) return;

        var remainingPool = new List<Player>(eligiblePlayers);
        foreach (InventoryItem item in roll.items)
        {
            if (remainingPool.Count == 0) break;
            int index = Random.Range(0, remainingPool.Count);
            Player winner = remainingPool[index];
            remainingPool.RemoveAt(index);
            DeliverItem(winner, item, "Événement");
        }

        if (roll.aeris > 0)
        {
            Player winner = PickRandomEligible(eligiblePlayers);
            DeliverAeris(winner, roll.aeris, "Événement");
        }
    }
```

- [ ] **Step 2: Add `XPSystem.GrantEventRewards`**

In `Progression/XPSystem.cs`, immediately after the closing brace of `HandleMobKilled` (the version restored in Task 1) and before `GiveSpiritXP`, insert:

```csharp
    /// <summary>XP/Prestige d'une LootTable d'événement à TOUS les joueurs éligibles, HORS du
    /// pipeline GameEventBus.OnMobKilled — même principe que HandleMobKilled mais pour la
    /// récompense de fin d'événement (World Boss/Invasion, voir Data/Content/WorldEventData.cs),
    /// jamais liée à la mort d'un mob précis.</summary>
    public void GrantEventRewards(LootTable table, List<Player> eligiblePlayers)
    {
        if (table == null || eligiblePlayers == null || eligiblePlayers.Count == 0) return;

        if (table.xpReward > 0)
            foreach (Player p in eligiblePlayers)
                GiveCombatXP(p, table.xpReward);

        if (table.prestigeReward > 0)
            foreach (Player p in eligiblePlayers)
                p.AddPrestige(table.prestigeReward);
    }
```

- [ ] **Step 3: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors.

- [ ] **Step 4: Verify by direct call (temporary test hook)**

These two methods have no caller yet (Task 4-7 add the real caller) — to verify them in isolation before that exists, temporarily add a single debug line anywhere convenient (e.g. in `AerisSystem.Awake()` or any `Start()` you can trigger), call:
`LootManager.Instance?.GrantEventLoot(someRealLootTable, new List<Player> { FindObjectOfType<Player>() });`
and `XPSystem.Instance?.GrantEventRewards(someRealLootTable, new List<Player> { FindObjectOfType<Player>() });`
with any existing `LootTable` asset that has `xpReward`/`prestigeReward` > 0 and at least one item. Enter Play Mode, confirm you receive the XP/Prestige/item exactly once. Remove the temporary debug line afterward — it was only to prove the method works before Task 4-7 exist.

- [ ] **Step 5: Commit**

```bash
git add Systems/LootManager.cs Progression/XPSystem.cs
git commit -m "feat: add GrantEventLoot/GrantEventRewards for event-level reward distribution"
```

---

### Task 4: `WorldEventData.cs` — abstract base class + relocate `WorldEventMapEntry`

**Files:**
- Create: `Data/Content/WorldEventData.cs`
- Modify: `Data/Content/WorldBossData.cs` (remove `WorldEventMapEntry` only — full restructure is Task 5)

**Interfaces:**
- Consumes: `GameEventBus.OnDamageDealt` / `DamageDealtEvent { amount, element, source: Entity, target: Entity, isCrit, isOneHit }` (existing, `Events/GameEvents.cs`). `LootManager.Instance?.GrantEventLoot(...)` / `XPSystem.Instance?.GrantEventRewards(...)` (Task 3).
- Produces: `WorldEventMapEntry { mapScene (editor-only), sceneName, palier }`, abstract class `WorldEventData` with `eligibleMaps: List<WorldEventMapEntry>`, `minHitsToBeEligible: int` (default 10), `abstract string DisplayName`, `abstract IEnumerator RunEvent(WorldEventScheduler scheduler)`, `protected abstract bool OwnsMob(Entity target)`, `protected WorldEventMapEntry PickRandomMap()`, `protected void StartTracking()`, `protected void StopTracking()`, `protected void GrantEventRewards(LootTable table)`, `protected void SyncEligibleMapsSceneNames()` (editor-only) — Task 5 references the class name, Task 6 inherits from it and implements every abstract member. Participation is tracked internally as a private `Dictionary<Player,int>` (hit counts), never exposed as a public field — `GrantEventRewards` computes the eligible list on demand from it.

- [ ] **Step 1: Read the current `WorldBossData.cs` to confirm exact `WorldEventMapEntry` boundaries**

Read `Data/Content/WorldBossData.cs` in full before editing — confirm the `WorldEventMapEntry` class still starts right after the file's header comment block and ends right before the `[CreateAssetMenu]` line, exactly as it was left in yesterday's commits. If anything differs, adjust the removal in Step 3 to match the real boundaries instead of assuming.

- [ ] **Step 2: Write `Data/Content/WorldEventData.cs`**

```csharp
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDEVENTDATA.CS — Tronc commun de tout type d'événement mondial
// Path : Assets/Scripts/Data/Content/WorldEventData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Base abstraite de WorldBossData/InvasionData (et de tout futur type d'événement). Porte ce qui
// est IDENTIQUE entre tous les types : tirage du palier, tracking de participation en temps réel
// (GameEventBus.OnDamageDealt — tout dégât ≥0 d'un Player sur un mob que CET événement possède,
// aucun seuil), et distribution de la récompense finale. Chaque type concret n'écrit QUE sa
// propre mécanique de spawn/résolution (RunEvent) et répond à "ce mob m'appartient-il ?" (OwnsMob).
//
// Timer/offsets d'annonce ne vivent PAS ici — ils sont partagés par TOUS les types d'événements
// et vivent sur Systems/WorldEventScheduler.cs (le dispatcher), pas sur chaque asset.
// =============================================================

[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — sceneName se remplit automatiquement (voir OnValidate des " +
             "classes concrètes). Editor-only, n'existe pas en build : sceneName reste le champ " +
             "réellement lu au runtime, même convention que Portal.targetMapScene/" +
             "DungeonMapData.mapScene.")]
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;

    [Tooltip("Palier de CETTE scène — doit correspondre au MapInfo.palier posé dans la scène " +
             "elle-même. Pas de lecture automatique possible (une scène non chargée n'a pas de " +
             "MapInfo accessible) : à retaper ici à la main, une seule fois à la config.")]
    public int palier;
}

public abstract class WorldEventData : ScriptableObject
{
    [Header("Paliers éligibles")]
    public List<WorldEventMapEntry> eligibleMaps = new List<WorldEventMapEntry>();

    [Header("Éligibilité")]
    [Tooltip("Coups minimum portés sur un mob de CET événement pour être éligible à la " +
             "récompense finale — empêche un joueur de passage (1 coup, repart) d'être " +
             "récompensé comme un vrai participant (Florian, 2026-09-30 : \"il faut vraiment " +
             "participer\").")]
    public int minHitsToBeEligible = 10;

    private readonly Dictionary<Player, int> _hitCounts = new Dictionary<Player, int>();

    /// <summary>Nom d'affichage utilisé dans les annonces partagées ("Un {DisplayName} menace...").</summary>
    public abstract string DisplayName { get; }

    /// <summary>Point d'entrée unique, appelé par WorldEventScheduler quand ce type est tiré au
    /// hasard — gère TOUTE la mécanique (décision, annonces, spawn, résolution, récompense).</summary>
    public abstract IEnumerator RunEvent(WorldEventScheduler scheduler);

    /// <summary>"Ce mob appartient-il à l'instance actuellement en cours de CET événement ?" —
    /// utilisé par le tracking de dégâts pour savoir si taper ce mob compte pour la
    /// participation. Chaque type concret répond selon sa propre notion de "mes mobs" (un seul
    /// boss pour World Boss, une liste de mobs de vague/renfort pour Invasion).</summary>
    protected abstract bool OwnsMob(Entity target);

    protected WorldEventMapEntry PickRandomMap()
        => (eligibleMaps == null || eligibleMaps.Count == 0) ? null : eligibleMaps[Random.Range(0, eligibleMaps.Count)];

    private void OnDamageDealtHandler(DamageDealtEvent e)
    {
        if (!(e.source is Player player)) return;
        if (!OwnsMob(e.target)) return;
        _hitCounts.TryGetValue(player, out int count);
        _hitCounts[player] = count + 1;
    }

    /// <summary>À appeler AU DÉBUT de la fenêtre où les mobs de cet événement peuvent être
    /// tapés (juste après le premier spawn) — vide les compteurs d'une éventuelle exécution
    /// précédente et s'abonne au tracking de dégâts.</summary>
    protected void StartTracking()
    {
        _hitCounts.Clear();
        GameEventBus.OnDamageDealt += OnDamageDealtHandler;
    }

    /// <summary>À appeler dès que l'événement est résolu (succès ou échec) — coupe
    /// l'abonnement, plus aucun dégât ne doit être compté après ce point. Ne doit JAMAIS être
    /// appelé directement depuis un callback Mob.OnDeath (voir WorldBossData, Task 5) : Mob.Die()
    /// est synchrone et s'exécute AVANT que l'appelant (SkillSystem) publie le DamageDealtEvent
    /// de ce même coup — couper le tracking à cet instant précis perdrait ce dernier coup.</summary>
    protected void StopTracking() => GameEventBus.OnDamageDealt -= OnDamageDealtHandler;

    /// <summary>Distribue la LootTable de l'événement (XP/Prestige/items) à tout joueur ayant
    /// atteint minHitsToBeEligible coups — calculé à la volée depuis _hitCounts (jamais maintenu
    /// comme liste incrémentale), délègue à LootManager.GrantEventLoot/XPSystem.GrantEventRewards,
    /// hors pipeline MobKilledEvent normal.</summary>
    protected void GrantEventRewards(LootTable table)
    {
        List<Player> eligiblePlayers = _hitCounts
            .Where(kv => kv.Value >= minHitsToBeEligible)
            .Select(kv => kv.Key)
            .ToList();

        LootManager.Instance?.GrantEventLoot(table, eligiblePlayers);
        XPSystem.Instance?.GrantEventRewards(table, eligiblePlayers);
    }

#if UNITY_EDITOR
    /// <summary>Partagé par tout type concret (WorldBossData/InvasionData) — synchronise
    /// sceneName depuis mapScene pour chaque entrée de eligibleMaps. Factorisé ici pour ne pas
    /// dupliquer cette boucle dans le OnValidate de chaque sous-classe.</summary>
    protected void SyncEligibleMapsSceneNames()
    {
        if (eligibleMaps == null) return;
        foreach (var entry in eligibleMaps)
            if (entry.mapScene != null) entry.sceneName = entry.mapScene.name;
    }
#endif
}
```

- [ ] **Step 3: Remove `WorldEventMapEntry` from `WorldBossData.cs`**

In `Data/Content/WorldBossData.cs`, delete the entire `WorldEventMapEntry` class block (confirmed in Step 1) — from its `[System.Serializable]` attribute down to its closing `}`, leaving the file's header comment and the `[CreateAssetMenu]`/`public class WorldBossData` declaration adjacent to each other (with the rest of `WorldBossData`'s own body untouched for now — Task 5 handles the rest of this file).

- [ ] **Step 4: Verify it compiles**

Wait for Unity to finish compiling. Expect an error at this point: `WorldBossData` still references `WorldEventMapEntry` (now a different class in a different file, but same name/namespace — this should actually still resolve fine since C# doesn't care which file a type is declared in within the same assembly). If Unity reports a **duplicate class** error instead, it means Step 3's removal was incomplete (two copies still exist) — go back and remove the leftover copy from `WorldBossData.cs` before continuing. `WorldBossData` itself will keep compiling using its old (pre-Task-6) shape until Task 6 — this task does not yet make it inherit from `WorldEventData`.

- [ ] **Step 5: Commit**

```bash
git add Data/Content/WorldEventData.cs Data/Content/WorldBossData.cs
git commit -m "feat: add WorldEventData abstract base, relocate WorldEventMapEntry"
```

---

### Task 5: `WorldEventScheduler.cs` rewrite + `WorldBossData.cs` restructure

Done as one task, not two — `WorldBossData` cannot compile against the old `WorldEventScheduler`
shape, and the new `WorldEventScheduler` has no real consumer until `WorldBossData` is updated
to match. Splitting them would mean committing a deliberately broken build in between; folding
them into one task keeps every commit green.

**Files:**
- Modify: `Systems/WorldEventScheduler.cs` (full rewrite)
- Modify: `Data/Content/WorldBossData.cs` (full rewrite of the class body — `WorldEventMapEntry` already removed in Task 4)

**Interfaces:**
- Consumes: `WorldEventData.RunEvent(WorldEventScheduler)`, `.eligibleMaps`, `PickRandomMap()`, `StartTracking()`/`StopTracking()`, `GrantEventRewards(LootTable)`, `SyncEligibleMapsSceneNames()` (Task 4). `Mob.InitializeSpawn(MobData, int)`/`Mob.OnDeath(Action)` (existing, unchanged since yesterday).
- Produces: `WorldEventScheduler.Instance` (unchanged lazy singleton), `public float FirstWarningOffset`/`SecondWarningOffset` (read-only properties), `public List<WorldEventData> eventPool`, `public static bool TryGetRandomNavMeshPoint(out Vector3 point)` (was private instance method, now public static — Task 6 calls it as `WorldEventScheduler.TryGetRandomNavMeshPoint(out pos)`). `WorldBossEntry { boss: MobData, rewardTable: LootTable }`, `WorldBossData : WorldEventData` with `possibleBosses: List<WorldBossEntry>`, `despawnTimeout: float` — the reference shape Task 6's `InvasionData` follows for its own `RunEvent`/`OwnsMob`. Resolution (`StopTracking`/`GrantEventRewards`) happens only after `RunEvent`'s own `WaitUntil` unblocks, never inside the `Mob.OnDeath` callback directly — see the Global Constraints note on the last-hit race.

- [ ] **Step 1: Read the current `WorldEventScheduler.cs`**

Read `Systems/WorldEventScheduler.cs` in full — confirm it still matches yesterday's shipped shape (lazy singleton `Awake`/`Instance`, `eventData: WorldBossData` field, `RunCycle`/`SpawnAndWaitForResolution`/`TimeoutAfterDelay`/`OnBossResolved`/`TryGetRandomNavMeshPoint`). This task replaces the ENTIRE file content — if the real file has diverged from this description, favor a full replacement using the code below over trying to preserve any Boss-Géant-specific pieces (those move into `WorldBossData.cs`, Step 3 below).

- [ ] **Step 2: Replace the whole `WorldEventScheduler.cs` file**

```csharp
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// WORLDEVENTSCHEDULER.CS — Dispatcher générique d'événement mondial
// Path : Assets/Scripts/Systems/WorldEventScheduler.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/AerisSystem. Porte le
// timer partagé + les 2 offsets d'annonce (T-5min/T-1min) — communs à TOUS les types
// d'événements, jamais dupliqués sur chaque asset WorldEventData. Pur dispatcher : ne sait RIEN
// de la mécanique interne de World Boss/Invasion, se contente de tirer un type au hasard dans
// eventPool et de lui déléguer tout le reste via RunEvent().
//
// Florian place ce composant à la main sous _Managers et glisse les assets dans eventPool —
// l'auto-création (Instance ci-dessous) reste un filet de sécurité comme partout ailleurs dans
// le projet, mais sortirait avec eventPool vide — système inerte tant que personne ne le
// configure (voir RunCycle, garde de config).
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

    [Header("Timer — intervalle aléatoire entre deux événements (secondes)")]
    [Tooltip("Défaut ~6-8h. Réduis CES DEUX VALEURS *ET* les deux offsets d'annonce ci-dessous " +
             "ENSEMBLE pour tester en Play Mode sans attendre des heures.")]
    public float minInterval = 21600f; // 6h
    public float maxInterval = 28800f; // 8h

    [Header("Annonces — délai AVANT le déclenchement (secondes)")]
    public float firstWarningOffset  = 300f; // 5 min
    public float secondWarningOffset = 60f;  // 1 min

    [Header("Pool d'événements possibles")]
    [Tooltip("Un type est tiré au hasard à chaque cycle — glisse ici un WorldBossData, un " +
             "InvasionData, ou tout futur type héritant de WorldEventData.")]
    public List<WorldEventData> eventPool = new List<WorldEventData>();

    public float FirstWarningOffset  => firstWarningOffset;
    public float SecondWarningOffset => secondWarningOffset;

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

            yield return selected.RunEvent(this);
            // selected.RunEvent gère TOUT (annonces, spawn, résolution, récompense) en utilisant
            // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien de
            // plus sur ce qui se passe à l'intérieur.
        }
    }

    // =========================================================
    // POSITION ALÉATOIRE SUR LE NAVMESH — utilisé par tout type d'événement
    // =========================================================

    /// <summary>Point barycentrique uniforme dans un triangle du NavMesh baké de la scène
    /// ACTIVE. Pas de biais par aire de triangle (acceptable, pas une distribution
    /// statistiquement critique). Retourne false si le NavMesh est vide/absent — un
    /// `out Vector3.zero` en cas d'échec aurait été un piège : (0,0,0) est une position VALIDE
    /// sur un vrai NavMesh (l'origine du monde), donc pas utilisable comme sentinelle d'échec —
    /// d'où le bool de retour plutôt qu'un simple Vector3.</summary>
    public static bool TryGetRandomNavMeshPoint(out Vector3 point)
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

- [ ] **Step 3: Read the current `WorldBossData.cs`**

Read `Data/Content/WorldBossData.cs` in full (post-Task-4, `WorldEventMapEntry` already removed) — confirm the remaining `WorldBossData` class body still matches yesterday's shipped shape (`possibleBosses: List<MobData>`, `minInterval`/`maxInterval`/`firstWarningOffset`/`secondWarningOffset`/`despawnTimeout`, the `OnValidate` with the `MobType.BossWorld` warning + scene-name sync). This step replaces the entire remaining content.

- [ ] **Step 4: Replace the whole `WorldBossData.cs` file**

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDBOSSDATA.CS — Configuration de l'événement Boss Géant
// Path : Assets/Scripts/Data/Content/WorldBossData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Hérite de WorldEventData (Data/Content/WorldEventData.cs) — le timer/les offsets d'annonce
// vivent sur WorldEventScheduler (partagés par tous les types), pas ici. Chaque WorldBossEntry
// porte SA PROPRE récompense (rewardTable) : deux boss différents dans possibleBosses n'ont
// jamais la même récompense d'événement (Florian, 2026-09-30).
// =============================================================

[System.Serializable]
public class WorldBossEntry
{
    public MobData   boss;
    public LootTable rewardTable;
}

[CreateAssetMenu(fileName = "wboss_", menuName = "AetherTree/Contenu/WorldBossData")]
public class WorldBossData : WorldEventData
{
    [Header("Boss possibles")]
    [Tooltip("Un boss est tiré au hasard parmi ceux-ci à chaque événement Boss Géant. Chaque " +
             "entrée porte sa propre récompense d'événement (rewardTable) — jamais partagée " +
             "entre plusieurs boss. Devrait avoir MobType = BossWorld (juste un avertissement " +
             "si un autre type est glissé ici, pas un blocage).")]
    public List<WorldBossEntry> possibleBosses = new List<WorldBossEntry>();

    [Header("Résolution")]
    [Tooltip("Si le boss n'est pas tué dans ce délai après son spawn, il despawn.")]
    public float despawnTimeout = 1800f; // 30 min

    public override string DisplayName => "Boss Géant";

    private GameObject _aliveBossObj;
    private Mob         _aliveMob;
    private bool        _resolved;
    private bool        _killedFlag;

    protected override bool OwnsMob(Entity target)
        => target != null && ReferenceEquals(target, _aliveMob);

    public override IEnumerator RunEvent(WorldEventScheduler scheduler)
    {
        if (possibleBosses == null || possibleBosses.Count == 0 || eligibleMaps == null || eligibleMaps.Count == 0)
        {
            Debug.LogWarning("[WorldBossData] possibleBosses/eligibleMaps vide — cycle ignoré.");
            yield break;
        }

        WorldEventMapEntry targetMap = PickRandomMap();
        WorldBossEntry     entry     = possibleBosses[Random.Range(0, possibleBosses.Count)];

        AnnoncePanel.Instance?.Announce(
            $"Un {DisplayName} menace le Palier {targetMap.palier} dans {scheduler.FirstWarningOffset / 60f:F0} minutes !");
        yield return new WaitForSeconds(Mathf.Max(0f, scheduler.FirstWarningOffset - scheduler.SecondWarningOffset));

        AnnoncePanel.Instance?.Announce(
            $"Un {DisplayName} menace le Palier {targetMap.palier} dans {scheduler.SecondWarningOffset / 60f:F0} minute(s) !");
        yield return new WaitForSeconds(scheduler.SecondWarningOffset);

        if (SceneManager.GetActiveScene().name != targetMap.sceneName) yield break; // personne présent

        if (entry.boss == null || entry.boss.prefab == null)
        {
            Debug.LogWarning($"[WorldBossData] Boss sans prefab (entrée tirée) — événement annulé.");
            yield break;
        }
        if (!WorldEventScheduler.TryGetRandomNavMeshPoint(out Vector3 spawnPos))
        {
            Debug.LogWarning("[WorldBossData] Aucun NavMesh baké — événement annulé.");
            yield break;
        }

        _aliveBossObj = Instantiate(entry.boss.prefab, spawnPos, Quaternion.identity);
        _aliveMob     = _aliveBossObj.GetComponent<Mob>();
        _aliveMob?.InitializeSpawn(entry.boss, targetMap.palier);

        AnnoncePanel.Instance?.Announce($"Le {DisplayName} est apparu sur le Palier {targetMap.palier} !");

        StartTracking();
        _resolved   = false;
        _killedFlag = false;
        _aliveMob?.OnDeath(() => MarkResolved(killed: true));
        Coroutine timeoutCoroutine = scheduler.StartCoroutine(TimeoutAfterDelay(despawnTimeout));

        yield return new WaitUntil(() => _resolved);

        // StopTracking()/GrantEventRewards() APRÈS le WaitUntil, jamais dans le callback de mort
        // lui-même — Mob.Die() (synchrone, déclenché par TakeDamage) appelle onDeathCallback AVANT
        // que SkillSystem, dans son appelant, publie le DamageDealtEvent de CE MÊME coup. Résoudre
        // directement dans le callback désabonnait le tracking avant que le coup fatal soit
        // compté — le joueur qui achève le boss pouvait être exclu si ce coup le faisait franchir
        // minHitsToBeEligible. En ne faisant que positionner _resolved/_killedFlag dans le
        // callback, la résolution réelle n'arrive qu'à la reprise de la coroutine (frame suivante
        // au plus tôt) — le DamageDealtEvent du coup fatal est alors déjà traité.
        StopTracking();
        if (timeoutCoroutine != null) scheduler.StopCoroutine(timeoutCoroutine);

        if (_killedFlag)
        {
            AnnoncePanel.Instance?.Announce($"Le {DisplayName} du Palier {targetMap.palier} a été vaincu !");
            GrantEventRewards(entry.rewardTable);
        }
        else
        {
            if (_aliveBossObj != null) Destroy(_aliveBossObj);
            AnnoncePanel.Instance?.Announce($"Le {DisplayName} du Palier {targetMap.palier} s'est retiré...");
        }

        _aliveBossObj = null;
        _aliveMob     = null;
    }

    private IEnumerator TimeoutAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        MarkResolved(killed: false);
    }

    /// <summary>Positionne le résultat — mort ET timeout y passent tous les deux. Le garde
    /// _resolved empêche une double résolution si les deux se déclenchent presque en même temps.
    /// Ne fait QUE marquer l'état : la résolution réelle (StopTracking/GrantEventRewards) vit dans
    /// RunEvent, après le WaitUntil — jamais ici (voir le commentaire dans RunEvent).</summary>
    private void MarkResolved(bool killed)
    {
        if (_resolved) return;
        _resolved   = true;
        _killedFlag = killed;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleBosses != null)
            foreach (var entry in possibleBosses)
                if (entry.boss != null && entry.boss.mobType != MobType.BossWorld)
                    Debug.LogWarning($"[WorldBossData] {name} : {entry.boss.mobName} a mobType = " +
                        $"{entry.boss.mobType}, attendu BossWorld pour un Boss Géant.");

        SyncEligibleMapsSceneNames();
    }
#endif
}
```

- [ ] **Step 5: Verify both files compile together**

Wait for Unity to finish compiling — confirm NO errors anywhere in the project (both files were rewritten in this same task, so this is the first and only compile checkpoint for this pair — never commit if this step shows errors).

- [ ] **Step 6: Verify the asset migrates or needs re-creation**

Open Florian's existing `WorldBossData` asset in the Inspector. Since `possibleBosses` changed type from `List<MobData>` to `List<WorldBossEntry>`, Unity will NOT preserve the old list's content automatically (different serialized shape) — confirm the list appears EMPTY now, and re-add each boss as a `WorldBossEntry` (drag the `MobData`, then also drag/create a `LootTable` for `rewardTable`). This is expected data loss for this one field, not a bug — tell Florian to re-populate it. Also confirm the `WorldEventScheduler` GameObject in the scene needs its old `Event Data` single-asset field re-assigned into the new `Event Pool` list (same expected one-time migration).

- [ ] **Step 7: Commit**

```bash
git add Systems/WorldEventScheduler.cs Data/Content/WorldBossData.cs
git commit -m "feat: generic WorldEventScheduler dispatcher + WorldBossData restructure"
```

---

### Task 6: `InvasionData.cs` — new wave-based event type

**Files:**
- Create: `Data/Content/InvasionData.cs`

**Interfaces:**
- Consumes: `WorldEventData` (Task 4). `WorldEventScheduler.FirstWarningOffset`/`SecondWarningOffset`/`TryGetRandomNavMeshPoint` (Task 5). `Mob.InitializeSpawn`/`OnDeath`/`Entity.isDead` (existing).
- Produces: `InvasionMobEntry { mob: MobData, count: int }`, `InvasionWave { mobs: List<InvasionMobEntry> }`, `InvasionVariant { variantName, waves: List<InvasionWave>, reinforcements: List<InvasionMobEntry>, boss: MobData, rewardTable: LootTable }`, `InvasionData : WorldEventData` — nothing later in this plan consumes this type further (it is the second, independent leaf of the `WorldEventData` hierarchy alongside `WorldBossData`).

- [ ] **Step 1: Write `Data/Content/InvasionData.cs`**

```csharp
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// INVASIONDATA.CS — Configuration de l'événement Invasion (vagues + renforts)
// Path : Assets/Scripts/Data/Content/InvasionData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Hérite de WorldEventData — même tronc commun que WorldBossData (tirage de palier, tracking de
// participation, récompense finale). possibleVariants permet plusieurs thèmes (ex: élémentaires)
// avec chacun ses propres vagues/renforts/boss/récompense — jamais partagés entre variantes.
//
// Déroulement : point d'ancrage aléatoire sur le NavMesh du palier tiré, 5 vagues fixes qui
// popent sur un timer SANS attendre que la précédente soit clear (les mobs s'accumulent), le
// boss spawn une seule fois après la dernière vague, puis des renforts repopent en boucle sur le
// même timer tant que le boss reste vivant. Boss mort → plus aucun spawn → l'événement n'est
// résolu (et ne récompense) qu'une fois TOUS les mobs d'invasion restants également morts.
// =============================================================

[System.Serializable]
public class InvasionMobEntry
{
    public MobData mob;
    public int     count = 1;
}

[System.Serializable]
public class InvasionWave
{
    public List<InvasionMobEntry> mobs = new List<InvasionMobEntry>();
}

[System.Serializable]
public class InvasionVariant
{
    [Tooltip("Nom d'affichage — ex: Feu, Eau, Corruption... Utilisé dans les annonces.")]
    public string variantName = "Invasion";

    [Tooltip("Exactement 5 vagues attendues (pas de blocage dur si différent).")]
    public List<InvasionWave> waves = new List<InvasionWave>();

    [Tooltip("Repop en boucle sur le même timer tant que le boss reste vivant après son apparition.")]
    public List<InvasionMobEntry> reinforcements = new List<InvasionMobEntry>();

    [Tooltip("Devrait avoir MobType = BossInvasion.")]
    public MobData   boss;
    public LootTable rewardTable;
}

[CreateAssetMenu(fileName = "invasion_", menuName = "AetherTree/Contenu/InvasionData")]
public class InvasionData : WorldEventData
{
    [Header("Variantes possibles (ex: élémentaires)")]
    public List<InvasionVariant> possibleVariants = new List<InvasionVariant>();

    [Header("Zone de spawn autour du point d'ancrage")]
    [Tooltip("Rayon (mètres) autour du point d'ancrage tiré au hasard — jamais toute la map.")]
    public float spawnRadius = 20f;

    [Header("Cadence")]
    [Tooltip("Délai entre 2 vagues ET entre 2 vagues de renfort (secondes).")]
    public float waveInterval = 90f;

    public override string DisplayName => "Invasion";

    private readonly List<Mob> _aliveMobs = new List<Mob>();
    private bool _bossDead;

    protected override bool OwnsMob(Entity target) => target is Mob m && _aliveMobs.Contains(m);

    public override IEnumerator RunEvent(WorldEventScheduler scheduler)
    {
        if (possibleVariants == null || possibleVariants.Count == 0 || eligibleMaps == null || eligibleMaps.Count == 0)
        {
            Debug.LogWarning("[InvasionData] possibleVariants/eligibleMaps vide — cycle ignoré.");
            yield break;
        }

        WorldEventMapEntry targetMap = PickRandomMap();
        InvasionVariant    variant   = possibleVariants[Random.Range(0, possibleVariants.Count)];

        AnnoncePanel.Instance?.Announce(
            $"Une invasion ({variant.variantName}) menace le Palier {targetMap.palier} dans {scheduler.FirstWarningOffset / 60f:F0} minutes !");
        yield return new WaitForSeconds(Mathf.Max(0f, scheduler.FirstWarningOffset - scheduler.SecondWarningOffset));

        AnnoncePanel.Instance?.Announce(
            $"Une invasion ({variant.variantName}) menace le Palier {targetMap.palier} dans {scheduler.SecondWarningOffset / 60f:F0} minute(s) !");
        yield return new WaitForSeconds(scheduler.SecondWarningOffset);

        if (SceneManager.GetActiveScene().name != targetMap.sceneName) yield break;

        if (!WorldEventScheduler.TryGetRandomNavMeshPoint(out Vector3 anchor))
        {
            Debug.LogWarning("[InvasionData] Aucun NavMesh baké — événement annulé.");
            yield break;
        }

        AnnoncePanel.Instance?.Announce($"L'invasion ({variant.variantName}) a commencé sur le Palier {targetMap.palier} !");

        StartTracking();
        _aliveMobs.Clear();
        _bossDead = false;

        // ── 5 vagues fixes — pop sur le timer, n'attendent PAS que la précédente soit clear ──
        foreach (InvasionWave wave in variant.waves)
        {
            SpawnGroup(wave.mobs, anchor, targetMap.palier);
            yield return new WaitForSeconds(waveInterval);
        }

        // ── Boss — spawn une seule fois, après la dernière vague ──
        if (variant.boss != null && variant.boss.prefab != null)
        {
            GameObject bossObj = Instantiate(variant.boss.prefab, GetRandomPointInRadius(anchor), Quaternion.identity);
            Mob bossMob = bossObj.GetComponent<Mob>();
            bossMob?.InitializeSpawn(variant.boss, targetMap.palier);
            if (bossMob != null) _aliveMobs.Add(bossMob);
            AnnoncePanel.Instance?.Announce($"Le boss de l'invasion est apparu sur le Palier {targetMap.palier} !");
            bossMob?.OnDeath(() => _bossDead = true);
        }
        else
        {
            Debug.LogWarning("[InvasionData] Variante sans boss valide — pas de renforts, résolution immédiate dès que le reste est mort.");
            _bossDead = true; // évite un WaitUntil infini plus bas
        }

        // ── Renforts en boucle tant que le boss est vivant ──
        while (!_bossDead)
        {
            yield return new WaitForSeconds(waveInterval);
            if (_bossDead) break;
            SpawnGroup(variant.reinforcements, anchor, targetMap.palier);
        }

        // ── Boss mort — plus aucun spawn, attendre que tout le reste meure ──
        yield return new WaitUntil(() =>
        {
            _aliveMobs.RemoveAll(m => m == null || m.isDead);
            return _aliveMobs.Count == 0;
        });

        StopTracking();
        AnnoncePanel.Instance?.Announce($"L'invasion ({variant.variantName}) du Palier {targetMap.palier} a été repoussée !");
        GrantEventRewards(variant.rewardTable);
    }

    private void SpawnGroup(List<InvasionMobEntry> mobs, Vector3 anchor, int palier)
    {
        if (mobs == null) return;
        foreach (var entry in mobs)
        {
            if (entry.mob == null || entry.mob.prefab == null) continue;
            for (int i = 0; i < entry.count; i++)
            {
                GameObject obj = Instantiate(entry.mob.prefab, GetRandomPointInRadius(anchor), Quaternion.identity);
                Mob mob = obj.GetComponent<Mob>();
                mob?.InitializeSpawn(entry.mob, palier);
                if (mob != null) _aliveMobs.Add(mob);
            }
        }
    }

    private Vector3 GetRandomPointInRadius(Vector3 center)
    {
        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
        if (NavMesh.SamplePosition(candidate, out var hit, spawnRadius, NavMesh.AllAreas))
            return hit.position;
        return center; // repli sur l'ancre si le sample échoue — dégradation gracieuse
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleVariants != null)
        {
            foreach (var variant in possibleVariants)
            {
                if (variant.boss != null && variant.boss.mobType != MobType.BossInvasion)
                    Debug.LogWarning($"[InvasionData] {name} : {variant.boss.mobName} a mobType = " +
                        $"{variant.boss.mobType}, attendu BossInvasion pour un boss d'invasion.");

                if (variant.waves != null)
                    foreach (var wave in variant.waves)
                        WarnIfNotMobInvasion(wave.mobs);

                WarnIfNotMobInvasion(variant.reinforcements);
            }
        }

        SyncEligibleMapsSceneNames();
    }

    private void WarnIfNotMobInvasion(List<InvasionMobEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
            if (entry.mob != null && entry.mob.mobType != MobType.MobInvasion)
                Debug.LogWarning($"[InvasionData] {name} : {entry.mob.mobName} a mobType = " +
                    $"{entry.mob.mobType}, attendu MobInvasion pour un mob de vague/renfort.");
    }
#endif
}
```

**Amendement post-exécution (2026-09-30)** — suite à l'amendement de Task 2 (`BossInvasion`
retiré, `InvasionRole` ajouté), les checks `OnValidate` ci-dessus ont été mis à jour : `variant.boss`
doit avoir `mobType == MobInvasion && invasionRole == InvasionRole.Boss` (au lieu de `mobType ==
BossInvasion`), et les mobs de vague/renfort doivent avoir `mobType == MobInvasion && invasionRole
== InvasionRole.Normal` (au lieu de juste `mobType == MobInvasion`) — `WarnIfNotMobInvasion` est
devenu `WarnIfNotInvasionTrash` en conséquence. Voir commit `79e2952` pour le diff réel.

- [ ] **Step 2: Verify it compiles**

Wait for Unity to finish compiling — confirm no errors.

- [ ] **Step 3: Verify the asset is creatable**

`Assets > Create > AetherTree > Contenu > InvasionData` — confirm the menu item exists and creates an asset with empty `Possible Variants`/`Eligible Maps` and default `spawnRadius`/`waveInterval`/`minHitsToBeEligible` (10) values.

- [ ] **Step 4: Verify the `OnValidate` mobType warnings**

On the new asset, add one `InvasionVariant`, set its `boss` to any `MobData` whose `mobType`/`invasionRole` is NOT `MobInvasion`+`Boss` (e.g. leave it `Normal` mobType) — confirm a `[InvasionData] ... attendu MobInvasion + InvasionRole.Boss` warning appears in the Console immediately. Add one wave with one `InvasionMobEntry` whose `mob` isn't `MobInvasion`+`InvasionRole.Normal` — confirm the matching `attendu MobInvasion + InvasionRole.Normal` warning also appears. Fix both (or leave as a deliberate test artifact if you'll reconfigure real content in Task 7) — the point is confirming the warnings actually fire, not that the asset ends up correctly configured yet.

- [ ] **Step 5: Commit**

```bash
git add Data/Content/InvasionData.cs
git commit -m "feat: add InvasionData — 5-wave + endless-reinforcement Invasion event"
```

---

### Task 7: Final configuration + verification

**Files:** none (Editor configuration + verification-only task).

**Interfaces:** none.

- [ ] **Step 1: Configure `WorldEventScheduler` under `_Managers`**

On the existing `WorldEventScheduler` GameObject (placed yesterday): the old `Event Data` field is gone (replaced by `Event Pool`) — drag the existing `WorldBossData` asset into `Event Pool` (element 0). Create a new `InvasionData` asset (Task 6 Step 3), fill in at least one `InvasionVariant` with 5 waves (any test `MobData` with `MobType = MobInvasion` / `InvasionRole = Normal`, count 1-2 each), a small `reinforcements` list, a `boss` (`MobType = MobInvasion` / `InvasionRole = Boss`, a real `prefab`), and a `rewardTable` (`LootTable` with `xpReward`/`prestigeReward` > 0 and at least one item). Add this `InvasionData` to `Event Pool` (element 1). Fill `Eligible Maps` on the `InvasionData` the same way as `WorldBossData`'s (drag `Map_01`, set its real palier).

- [ ] **Step 2: Re-populate `WorldBossData.possibleBosses`**

Per Task 5 Step 6 — re-add each boss as a `WorldBossEntry` with its own `rewardTable`. Create (or reuse) a SECOND `LootTable` with different `prestigeReward`/items from the first, and — if you only have one boss `MobData` configured — temporarily duplicate the `WorldBossEntry` with the same boss but the second `rewardTable`, purely to test Review Focus item 3 (different entries grant different tables) without needing two full boss prefabs ready yet.

- [ ] **Step 3: Verify both event types fire (reduced timer)**

Set `Min Interval`/`Max Interval` to `30`/`60`, `First Warning Offset` to `10`, `Second Warning Offset` to `3` on `WorldEventScheduler`. Run several cycles in a row (stay on `Map_01`) — confirm BOTH `WorldBossData` and `InvasionData` come up across enough tries (not always the same one), and each formats its own distinct announcement text ("Un Boss Géant menace..." vs "Une invasion (...) menace...").

- [ ] **Step 4: Verify per-entry rewards differ (Review Focus item 3)**

Force enough World Boss cycles to see both `WorldBossEntry` rolled at least once each (small pool makes this quick) — confirm the Prestige/items you receive match the SPECIFIC entry's `rewardTable`, not always the same one regardless of which boss/entry was picked.

- [ ] **Step 5: Verify Invasion wave accumulation**

Let an Invasion cycle reach spawn — confirm wave 2's mobs appear ~90s after wave 1's WITHOUT wave 1 needing to be cleared first (kill nothing, just watch — by wave 3 there should be visibly more alive mobs than any single wave's own count). Confirm the boss spawns once, only after wave 5.

- [ ] **Step 6: Verify Invasion reinforcements + late reward (Review Focus items 1 and 2)**

Do NOT kill the boss immediately — confirm reinforcements keep spawning every `waveInterval` while it's alive. As you fight, make sure you personally land at least `minHitsToBeEligible` (10) hits on wave-1/wave-2 trash mobs and NEVER hit the boss itself at all — the point is to confirm a player who exclusively hit trash, never the boss, still ends up rewarded once they cross the threshold. Kill the boss (with a different attack/character if testing solo isn't possible, otherwise just land the final blow yourself in addition to your trash hits), then confirm: no new spawns occur, but the "invasion repoussée" announcement and reward do NOT fire until every remaining alive invasion mob is also dead — leave a couple of trash mobs alive on purpose after the boss dies and confirm the reward waits for them.

- [ ] **Step 7: Verify the `minHitsToBeEligible` threshold actually filters (Review Focus item 2)**

During a World Boss or Invasion cycle, land only 2-3 hits on an event mob then stop entirely (don't finish the fight, let someone/something else — or just let the timeout/other mobs — resolve the event). Confirm you do NOT appear in the reward distribution once the event resolves — a drive-by handful of hits below `minHitsToBeEligible` must not count as real participation.

- [ ] **Step 8: Verify the World Boss last-hit race fix (Review Focus item 3)**

Configure a throwaway low-HP test boss (or lower a real one's `baseHP`/`hpPerLevel` temporarily) so you can land EXACTLY your 10th hit as the killing blow — i.e. your `minHitsToBeEligible`-th hit and the fatal hit are the same single hit. Confirm you still receive the event reward. Before the Task 5 fix (`StopTracking`/`GrantEventRewards` deferred until after `RunEvent`'s `WaitUntil`, never inside the `Mob.OnDeath` callback), this exact hit could be silently dropped from the hit count because `Mob.Die()` runs synchronously before `SkillSystem` publishes that hit's `DamageDealtEvent`. Restore the test boss's real stats afterward.

- [ ] **Step 9: Verify the empty-pool-entry guard**

Temporarily clear `Eligible Maps` on the `InvasionData` asset specifically (leave `WorldBossData`'s intact) and force enough cycles to roll `InvasionData` — confirm a `[InvasionData] possibleVariants/eligibleMaps vide` warning appears, no crash, and the scheduler moves on to the next cycle (which might roll `WorldBossData` instead, or `InvasionData` again with the same warning). Restore `Eligible Maps` afterward.

- [ ] **Step 10: Restore production values**

Set `Min Interval`/`Max Interval`/`First Warning Offset`/`Second Warning Offset` back on `WorldEventScheduler` to real values (`21600`/`28800`/`300`/`60`, or whatever Florian decides for actual demo pacing), and confirm both event data assets' own content (`possibleBosses`/`possibleVariants`) are back to their real intended entries (no leftover duplicate-entry test hack from Step 2, and no leftover low-HP test hack from Step 8).

- [ ] **Step 11: Report back**

Tell me what worked and what didn't.
