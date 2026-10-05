# Chat System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a chat system (ChatSystem + ChatUI) with 6 channels (World/Guild/Private/Nearby/System/AetherEcho), a unified message window with per-channel view filters, and a new "Écho d'Aether" consumable that broadcasts a message bypassing those filters — usable either from `ChatUI`'s own send picker or, more naturally, by using the item itself from the ConsoBar/inventory, which opens a small dedicated prompt panel.

**Architecture:** `Systems/ChatSystem.cs` is a new `DontDestroyOnLoad` singleton (same pattern as `QuestSystem`) holding a capped in-memory history and routing player-typed messages per channel; it also subscribes to existing `GameEventBus` events (loot, quest, level-up, mail) to auto-populate the read-only System channel. `UI/PanelFixe/ChatUI.cs` is a new scene-local HUD panel (same pattern as `QuestTrackerUI`) that renders the unified history live via a new `GameEventBus.OnChatMessage` event. Both singletons need `Resubscribe()` wired into `GameEventBus.Reset()` — this project's single most common chat-adjacent bug this session (fixed 3 times already on other UIs), so every task touching a subscription double-checks this explicitly rather than assuming it.

**Tech Stack:** Unity C#, no automated test framework in this project — verification is: (a) brace-balance sanity check after every file edit (`grep -o '{' file | wc -l` vs `}`), since no Unity compiler is reachable from this session, and (b) Florian's manual Play Mode testing, concentrated in the final task.

**Spec:** `docs/superpowers/specs/2026-10-05-chat-system-design.md`

## Global Constraints

- Repo root is `C:\AetherTree_v3.5\Assets\Scripts` exactly — this is a real git repo, branch `master`, commits go directly to master (established convention this session, confirmed repeatedly).
- `Assets/Content` (ScriptableObject game-content assets, e.g. a future Écho d'Aether item asset) is OUTSIDE this git repo — never attempt to `git add` anything under `Content/`.
- No automated test framework — do not write pytest/NUnit-style test files. Verification is brace-balance + Florian's manual Play Mode pass.
- Every new/modified singleton that subscribes to `GameEventBus` events MUST have a public `Resubscribe()` method registered in `GameEventBus.Reset()` — this is the single most-repeated bug this session (QuestTrackerUI, QuestJournalUI, PNJQuestBoardUI all hit it). Never skip this for ChatSystem or ChatUI.
- `ItemEvent`, `QuestEvent`, and other existing event structs are consumed by `QuestSystem`, `UnlockManager`, and others — adding a field to `ItemEvent` is safe (additive, existing consumers ignore unknown fields), but never remove or rename an existing field.
- `ConsumableType` is append-only — new values go at the END with the next ordinal, never inserted/reordered (existing saved data and asset YAML reference ordinals).
- Écho d'Aether is consumed ONLY at the moment a message is actually sent through `ChatSystem.TrySendPlayerMessage` — never at item "use" time (unlike every other `ConsumableType`, which triggers an immediate effect via `ConsoBarUI.UseConsumable`). `ConsoBarUI.UseConsumable` DOES get an `AetherEcho` branch (Task 11), but that branch only OPENS a prompt panel (Task 10) and returns — it must never call `ConsumeAndRefresh`/consume anything itself. Canceling the prompt must leave the item completely untouched.

## Review Focus

- **Double Resubscribe gap**: both `ChatSystem` AND `ChatUI` are new singletons that subscribe to events — a plan or implementer who wires only one of the two (or forgets the `GameEventBus.Reset()` registration for either) reproduces a bug already fixed 3 times this session. Task 5 registers `ChatSystem.Instance?.Resubscribe()`, Task 8 registers `ChatUI.Instance?.Resubscribe()` — both must land, neither is optional.
- **AetherEcho double-consumption**: if `TrySendPlayerMessage(AetherEcho, ...)` is called twice in rapid succession (e.g. a double-click on Send) before the UI disables the button/refreshes the picker, a player with exactly 1 Écho d'Aether could have it checked-then-consumed twice, or the second call could silently consume a DIFFERENT item than expected. Task 4's implementation re-checks `HasAetherEcho` defensively inside `TrySendPlayerMessage` itself (not only at picker-open time), so a stale UI state can never bypass the check.
- **Picker staleness after consumption**: the channel-send picker must not keep showing "Écho d'Aether" as a sendable option after the last one was just consumed by the previous message — Task 9's `Update()` re-checks `HasAetherEcho` every frame (cheap boolean) and rebuilds the picker's options the moment it changes, instead of only once at `Start()` or only when the dropdown happens to be clicked.
- **`ItemEvent.itemName` backward compatibility**: adding a field to a struct already consumed by `QuestSystem.HandleItemAction` must not change that method's behavior — confirmed by reading `Systems/QuestSystem.cs:436-439` before writing Task 1: that method only ever reads `e.action`/`e.itemID`, never a field that doesn't exist yet, so adding `itemName` is purely additive. No task needs to re-verify this; it's a fact about already-shipped code, not something Task 1 changes.
- **Two entry points, one consumption path**: `AetherEchoPromptUI` (Task 10, opened from `ConsoBarUI`) and `ChatUI`'s own send picker (Task 9) both end up calling the exact same `ChatSystem.TrySendPlayerMessage(AetherEcho, ...)` — neither UI implements its own consumption logic. This is deliberate: the double-consumption defense already built into Task 4 (re-check `HasAetherEcho` inside `TrySendPlayerMessage` itself) automatically covers BOTH entry points without needing a second guard in `AetherEchoPromptUI`.
- **Mail/loot messages firing before ChatSystem exists**: `LootManager`/`ResourceNode`/`MailboxSystem` already run every Play session (they're pre-existing systems) — if `ChatSystem.Subscribe()` isn't wired before Task 7 ships, those publishes are harmless no-ops (nothing is subscribed yet), not a crash. No special ordering guard is needed, but Task 6 is explicitly sequenced AFTER Task 5 (Subscribe/Unsubscribe exist) so the handlers have something to attach to when written.

---

### Task 1: `ItemEvent.itemName` + `ChatMessageEvent` + `MailReceivedEvent`

**Files:**
- Modify: `Events/GameEvents.cs:129-135` (add `itemName` field to `ItemEvent`), add 2 new structs at the end of the file (after `PNJRouteCompletedEvent`, line 230).

**Interfaces:**
- Produces: `ChatChannel` is NOT defined here (it lives in `Systems/ChatSystem.cs`, Task 4) — `ChatMessageEvent.channel` is typed `ChatChannel` but this task only declares the struct; the project will not compile between this task and Task 4 (unresolved type). That's expected in a sequential task order — Unity won't actually recompile mid-plan in this workflow (no compiler reachable from this session either way, see Tech Stack), and Task 4 lands immediately after Tasks 2-3.
- Produces: `ItemEvent.itemName` (string) — consumed by Task 6's loot handler.
- Produces: `MailReceivedEvent.subject` (string) — consumed by Task 6's mail handler and published by Task 7.

- [ ] **Step 1: Add `itemName` to `ItemEvent`**

In `Events/GameEvents.cs`, the current struct (lines 129-135) is:

```csharp
public struct ItemEvent
{
    public string     itemID;
    public ItemAction action;
    public int        quantity;
    public int        aerisAmount;
}
```

Change it to:

```csharp
public struct ItemEvent
{
    public string     itemID;
    public string     itemName;   // nom affiché, ajouté pour ChatSystem (loot en chat Système) —
                                   // vide pour les publishers existants qui ne le remplissent pas
                                   // encore (champ additif, ne casse rien).
    public ItemAction action;
    public int        quantity;
    public int        aerisAmount;
}
```

- [ ] **Step 2: Add `ChatMessageEvent` and `MailReceivedEvent`**

At the end of `Events/GameEvents.cs`, after the `PNJRouteCompletedEvent` struct (line 230), add:

```csharp

// ── Chat ─────────────────────────────────────────────────────
// Publié par : ChatSystem.AddLine() — à CHAQUE nouvelle ligne (joueur ou Système).
public struct ChatMessageEvent
{
    public ChatChannel channel;
    public string      sender; // "" pour une ligne Système
    public string      text;
}

// ── Mail reçu ────────────────────────────────────────────────
// Publié par : MailboxSystem.SendMail() — point unique où un mail est réellement ajouté
// (SendRewardMail/SendLootOverflowMail passent toutes les deux par là).
public struct MailReceivedEvent
{
    public string subject;
}
```

- [ ] **Step 3: Brace-balance check**

Run: `grep -o '{' "Events/GameEvents.cs" | wc -l` and `grep -o '}' "Events/GameEvents.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 4: Commit**

```bash
git add Events/GameEvents.cs
git commit -m "feat: ajoute ItemEvent.itemName + ChatMessageEvent/MailReceivedEvent (chat system)"
```

---

### Task 2: `GameEventBus` — déclare `OnChatMessage`/`OnMailReceived`

**Files:**
- Modify: `Events/GameEventBus.cs` (declarations ~line 37, Publish methods ~line 59, Reset() nulling ~line 83). Resubscribe() calls for ChatSystem/ChatUI are NOT added yet — those singletons don't exist until Task 4/8.

**Interfaces:**
- Consumes: `ChatMessageEvent`, `MailReceivedEvent` from Task 1.
- Produces: `GameEventBus.OnChatMessage : Action<ChatMessageEvent>`, `GameEventBus.OnMailReceived : Action<MailReceivedEvent>`, `GameEventBus.Publish(ChatMessageEvent)`, `GameEventBus.Publish(MailReceivedEvent)` — all consumed by Tasks 4-5 (ChatSystem publishing/subscribing), Task 7 (MailboxSystem publishing), and Task 8 (ChatUI subscribing).

- [ ] **Step 1: Add the 2 event declarations**

In `Events/GameEventBus.cs`, after line 37 (`public static event Action<PNJRouteCompletedEvent>   OnPNJRouteCompleted;`), add:

```csharp
    public static event Action<ChatMessageEvent>    OnChatMessage;
    public static event Action<MailReceivedEvent>   OnMailReceived;
```

- [ ] **Step 2: Add the 2 Publish overloads**

After line 59 (`public static void Publish(PNJRouteCompletedEvent e) => OnPNJRouteCompleted?.Invoke(e);`), add:

```csharp
    public static void Publish(ChatMessageEvent e)    => OnChatMessage?.Invoke(e);
    public static void Publish(MailReceivedEvent e)   => OnMailReceived?.Invoke(e);
```

- [ ] **Step 3: Null them in `Reset()`**

After line 83 (`OnPNJRouteCompleted = null;`), add:

```csharp
        OnChatMessage    = null;
        OnMailReceived   = null;
```

- [ ] **Step 4: Brace-balance check**

Run: `grep -o '{' "Events/GameEventBus.cs" | wc -l` and `grep -o '}' "Events/GameEventBus.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 5: Commit**

```bash
git add Events/GameEventBus.cs
git commit -m "feat: déclare OnChatMessage/OnMailReceived sur GameEventBus"
```

---

### Task 3: `ConsumableType.AetherEcho`

**Files:**
- Modify: `Data/Inventory/ConsumableData.cs:33-42`.

**Interfaces:**
- Produces: `ConsumableType.AetherEcho` (ordinal 6) — consumed by Task 4 (`ChatSystem.HasAetherEcho`/`TrySendPlayerMessage`).

- [ ] **Step 1: Append the new value**

Current enum (lines 33-42):

```csharp
public enum ConsumableType
{
    Potion       = 0, // Restaure HP et/ou Mana, applique un BuffData
    Food         = 1, // Nourriture (Cuisiner) — mêmes champs que Potion, catégorie distincte
    DungeonKey   = 2, // Ouvre l'accès à un donjon spécifique (Classique ET Déblocage)
    TeleportItem = 3, // Téléporte vers une zone
    Other        = 4, // Effet custom
    RewardChest  = 5, // Tire 1 item pondéré dans chestEntries à l'usage — ajouté après coup,
                       // TOUJOURS en fin d'enum (ordinal safety).
}
```

Change to:

```csharp
public enum ConsumableType
{
    Potion       = 0, // Restaure HP et/ou Mana, applique un BuffData
    Food         = 1, // Nourriture (Cuisiner) — mêmes champs que Potion, catégorie distincte
    DungeonKey   = 2, // Ouvre l'accès à un donjon spécifique (Classique ET Déblocage)
    TeleportItem = 3, // Téléporte vers une zone
    Other        = 4, // Effet custom
    RewardChest  = 5, // Tire 1 item pondéré dans chestEntries à l'usage — ajouté après coup,
                       // TOUJOURS en fin d'enum (ordinal safety).
    AetherEcho   = 6, // Diffuse un message dans le chat (canal Écho d'Aether), consommé par
                       // ChatSystem.TrySendPlayerMessage au moment de l'ENVOI réel — PAS un
                       // effet instantané à l'usage comme les autres types, volontairement PAS
                       // branché dans ConsoBarUI.UseConsumable (voir Systems/ChatSystem.cs).
}
```

- [ ] **Step 2: Brace-balance check**

Run: `grep -o '{' "Data/Inventory/ConsumableData.cs" | wc -l` and `grep -o '}' "Data/Inventory/ConsumableData.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 3: Commit**

```bash
git add Data/Inventory/ConsumableData.cs
git commit -m "feat: ajoute ConsumableType.AetherEcho"
```

---

### Task 4: `Systems/ChatSystem.cs` — structure, historique, `TrySendPlayerMessage`

**Files:**
- Create: `Systems/ChatSystem.cs`.

**Interfaces:**
- Consumes: `InventorySystem.Instance.GetAllItems() : List<InventoryItem>`, `InventorySystem.Instance.ConsumeItem(ItemData, int) : bool` (both already exist — see `Systems/InventorySystem.cs:93,292`). `Player.entityName : string` (public, inherited from `Entities/Entity.cs:50`). `InventoryItem.ConsumableInstance?.data : ConsumableData` (already used this exact way in `UI/PanelFixe/ConsoBarUI.cs:340`).
- Produces: `ChatChannel` enum (`World, Guild, Private, Nearby, System, AetherEcho`), `ChatLine` class (`channel`, `sender`, `text`), `ChatSystem.Instance`, `ChatSystem.GetHistory() : IReadOnlyList<ChatLine>`, `ChatSystem.TrySendPlayerMessage(ChatChannel, string, Player) : bool`, `ChatSystem.HasAetherEcho(Player) : bool` — all consumed by Task 8/9 (ChatUI).

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CHATSYSTEM — Historique de chat + routage des messages par canal
// Path : Assets/Scripts/Systems/ChatSystem.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// Solo aujourd'hui (pas de réseau, voir a implémenter/AetherTree_Recap_Reseau+refactor.md) —
// World/Private/Nearby/AetherEcho postent en ÉCHO LOCAL direct (TrySendPlayerMessage ajoute la
// ligne immédiatement, comme si envoyée-et-reçue). Le jour où Mirror existe, TrySendPlayerMessage
// est le point d'ancrage à transformer en [Command] vers le serveur — ne PAS appeler AddLine()
// directement depuis un futur code réseau sans passer par ce point.
//
// Guild : toujours refusé, aucun GuildSystem n'existe (voir Data/PNJ/PNJData.cs, commentaire sur
// le retrait de Mayor/TryCreateGuild le 2026-10-04).
// =============================================================

public enum ChatChannel { World, Guild, Private, Nearby, System, AetherEcho }

public class ChatLine
{
    public ChatChannel channel;
    public string      sender; // "" pour une ligne Système
    public string      text;
}

public class ChatSystem : MonoBehaviour
{
    public static ChatSystem Instance { get; private set; }

    private const int MAX_HISTORY = 200;

    private readonly List<ChatLine> _history = new List<ChatLine>();

    public IReadOnlyList<ChatLine> GetHistory() => _history;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // =========================================================
    // ENVOI JOUEUR
    // =========================================================

    /// <summary>Appelé par ChatUI quand le joueur valide sa saisie. Renvoie false si refusé
    /// (Guild sans GuildSystem, AetherEcho sans item en stock OU disparu entre la sélection du
    /// canal et l'envoi). System n'est JAMAIS un canal d'envoi valide ici — lecture seule, voir
    /// PostSystemMessage.</summary>
    public bool TrySendPlayerMessage(ChatChannel channel, string text, Player player)
    {
        if (string.IsNullOrWhiteSpace(text) || player == null) return false;

        switch (channel)
        {
            case ChatChannel.System:
                return false; // jamais un canal d'envoi joueur

            case ChatChannel.Guild:
                PostSystemMessage("Pas de guilde.");
                return false;

            case ChatChannel.AetherEcho:
            {
                // Re-check défensif ICI (pas seulement à l'ouverture du picker) — évite une
                // double-consommation si le joueur double-clique Envoyer avant que l'UI ne se
                // rafraîchisse (Review Focus de la spec, 2026-10-05).
                var echoItem = FindAetherEchoData();
                if (echoItem == null) return false;
                if (InventorySystem.Instance == null || !InventorySystem.Instance.ConsumeItem(echoItem, 1))
                    return false;

                AddLine(ChatChannel.AetherEcho, player.entityName, text);
                return true;
            }

            default: // World, Private, Nearby — écho local direct (voir commentaire d'en-tête)
                AddLine(channel, player.entityName, text);
                return true;
        }
    }

    /// <summary>True si le joueur possède au moins 1 item ConsumableType.AetherEcho — utilisé par
    /// ChatUI pour savoir si "Écho d'Aether" doit apparaître dans le picker de canal d'envoi.
    /// Paramètre player non utilisé aujourd'hui (InventorySystem est un singleton global, pas par
    /// joueur, voir limitation solo documentée ailleurs ce chantier) — gardé pour cohérence de
    /// signature avec le reste de l'API et la lisibilité d'un futur passage multijoueur.</summary>
    public bool HasAetherEcho(Player player) => FindAetherEchoData() != null;

    /// <summary>Premier ConsumableData possédé avec consumableType == AetherEcho — scan
    /// dynamique, pas de référence SO fixe (supporte plusieurs variantes futures sans
    /// reconfigurer ChatSystem, voir spec).</summary>
    private ConsumableData FindAetherEchoData()
    {
        if (InventorySystem.Instance == null) return null;
        foreach (var item in InventorySystem.Instance.GetAllItems())
        {
            var data = item.ConsumableInstance?.data;
            if (data != null && data.consumableType == ConsumableType.AetherEcho)
                return data;
        }
        return null;
    }

    // =========================================================
    // HISTORIQUE
    // =========================================================

    private void AddLine(ChatChannel channel, string sender, string text)
    {
        _history.Add(new ChatLine { channel = channel, sender = sender, text = text });
        if (_history.Count > MAX_HISTORY) _history.RemoveAt(0);

        GameEventBus.Publish(new ChatMessageEvent { channel = channel, sender = sender, text = text });
    }

    private void PostSystemMessage(string text) => AddLine(ChatChannel.System, "", text);
}
```

- [ ] **Step 2: Brace-balance check**

Run: `grep -o '{' "Systems/ChatSystem.cs" | wc -l` and `grep -o '}' "Systems/ChatSystem.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 3: Commit**

```bash
git add Systems/ChatSystem.cs
git commit -m "feat: ChatSystem — historique, TrySendPlayerMessage, HasAetherEcho"
```

---

### Task 5: `ChatSystem` — Subscribe/Unsubscribe/Resubscribe + enregistrement `GameEventBus.Reset()`

**Files:**
- Modify: `Systems/ChatSystem.cs` (add Subscribe/Unsubscribe/Resubscribe + `OnEnable`/`OnDisable`).
- Modify: `Events/GameEventBus.cs:93` area (add `ChatSystem.Instance?.Resubscribe();` to the `Reset()` list).

**Interfaces:**
- Consumes: `GameEventBus.OnMobKilled`, `OnItemAction`, `OnQuestAction`, `OnPlayerLevelUp`, `OnMailReceived` (all already declared — the last one by Task 2).
- Produces: `ChatSystem.Resubscribe()` — called by `GameEventBus.Reset()`.

- [ ] **Step 1: Add the subscription scaffolding to `ChatSystem.cs`**

In `Systems/ChatSystem.cs`, right after the `Awake()` method written in Task 4, add (handlers are empty stubs here — Task 6 fills them in, this task only wires the plumbing so the Reset() registration is correct from the start):

```csharp
    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    // GameEventBus.Reset() (changement de scène) désabonne tout — sans Resubscribe() listé dans
    // Reset(), ChatSystem perdrait ses abonnements Système au premier changement de scène de la
    // session, même bug que QuestSystem/QuestTrackerUI/QuestJournalUI/PNJQuestBoardUI déjà corrigé
    // 4 fois cette session (ne pas le refaire une 5e).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()
    {
        GameEventBus.OnMobKilled      += HandleMobKilled;
        GameEventBus.OnItemAction     += HandleItemAction;
        GameEventBus.OnQuestAction    += HandleQuestAction;
        GameEventBus.OnPlayerLevelUp  += HandlePlayerLevelUp;
        GameEventBus.OnMailReceived   += HandleMailReceived;
    }

    private void Unsubscribe()
    {
        GameEventBus.OnMobKilled      -= HandleMobKilled;
        GameEventBus.OnItemAction     -= HandleItemAction;
        GameEventBus.OnQuestAction    -= HandleQuestAction;
        GameEventBus.OnPlayerLevelUp  -= HandlePlayerLevelUp;
        GameEventBus.OnMailReceived   -= HandleMailReceived;
    }

    private void HandleMobKilled(MobKilledEvent e) { }
    private void HandleItemAction(ItemEvent e) { }
    private void HandleQuestAction(QuestEvent e) { }
    private void HandlePlayerLevelUp(PlayerLevelUpEvent e) { }
    private void HandleMailReceived(MailReceivedEvent e) { }
```

- [ ] **Step 2: Register in `GameEventBus.Reset()`**

In `Events/GameEventBus.cs`, after line `PNJQuestBoardUI.Instance?.Resubscribe();` (which is itself after `QuestJournalUI.Instance?.Resubscribe();`), add:

```csharp
        ChatSystem.Instance?.Resubscribe();
```

(Placed in the same list, any position among the other `Resubscribe()` calls is fine — they're independent of each other and of ordering.)

- [ ] **Step 3: Brace-balance check on both files**

Run: `grep -o '{' "Systems/ChatSystem.cs" | wc -l` vs `}` count, and same for `Events/GameEventBus.cs`.
Expected: both counts equal in each file.

- [ ] **Step 4: Commit**

```bash
git add Systems/ChatSystem.cs Events/GameEventBus.cs
git commit -m "feat: ChatSystem Subscribe/Unsubscribe/Resubscribe, enregistré dans GameEventBus.Reset()"
```

---

### Task 6: `ChatSystem` — handlers Système (loot/quête/levelup/mail) + `ItemEvent.itemName` aux 2 publishers

**Files:**
- Modify: `Systems/ChatSystem.cs` (fill in the 5 stub handlers from Task 5 — `HandleMobKilled` stays empty, see below).
- Modify: `Systems/LootManager.cs:152-157` (populate `itemName`).
- Modify: `Systems/ResourceNode.cs:165-170` (populate `itemName`).

**Interfaces:**
- Consumes: `ItemEvent.itemName` (Task 1), `QuestEvent.action/quest` (existing, `Events/GameEvents.cs:196-203`), `PlayerLevelUpEvent.newLevel` (existing, `Events/GameEvents.cs:71-75`), `MailReceivedEvent.subject` (Task 1).
- Produces: nothing new — this task only fills in behavior.

**Design decision made while reading the real code (not guessed):** `MobKilledEvent` (`Events/GameEvents.cs:12-29`) carries NO per-item loot info — only mob/killer/player-list metadata. The actual loot-granted hook is `ItemEvent` with `action == ItemAction.Pickup`, published once per item by BOTH `LootManager.DeliverItem()` (`Systems/LootManager.cs:152-157`, combat loot) and `ResourceNode` (`Systems/ResourceNode.cs:165-170`, gathering). `HandleMobKilled` is therefore left as a permanent no-op (not wired to anything Système-visible) — loot messages come from `HandleItemAction` instead, which is the correct, already-used pattern (`QuestSystem.HandleItemAction` already filters the exact same event the exact same way for Gather objectives, `Systems/QuestSystem.cs:438`).

- [ ] **Step 1: Populate `itemName` in `LootManager.DeliverItem`**

Current code (`Systems/LootManager.cs:146-157`):

```csharp
        if (InventorySystem.Instance.AddItem(item))
        {
            Debug.Log($"[LOOT] {winner.entityName} a reçu {item.Name} ({mobName}).");

            var itemData = item.GetItemData();
            if (itemData != null)
                GameEventBus.Publish(new ItemEvent
                {
                    itemID   = itemData.itemID,
                    action   = ItemAction.Pickup,
                    quantity = item.GetQuantity(),
                });
        }
```

Change the `ItemEvent` literal to add `itemName`:

```csharp
        if (InventorySystem.Instance.AddItem(item))
        {
            Debug.Log($"[LOOT] {winner.entityName} a reçu {item.Name} ({mobName}).");

            var itemData = item.GetItemData();
            if (itemData != null)
                GameEventBus.Publish(new ItemEvent
                {
                    itemID   = itemData.itemID,
                    itemName = item.Name,
                    action   = ItemAction.Pickup,
                    quantity = item.GetQuantity(),
                });
        }
```

- [ ] **Step 2: Populate `itemName` in `ResourceNode`**

Current code (`Systems/ResourceNode.cs:165-170`):

```csharp
            GameEventBus.Publish(new ItemEvent
            {
                itemID   = data.itemID,
                action   = ItemAction.Pickup,
                quantity = qty,
            });
```

Change to:

```csharp
            GameEventBus.Publish(new ItemEvent
            {
                itemID   = data.itemID,
                itemName = data.displayName.Get(LocalizationManager.CurrentLanguage),
                action   = ItemAction.Pickup,
                quantity = qty,
            });
```

- [ ] **Step 3: Fill in the ChatSystem handlers**

In `Systems/ChatSystem.cs`, replace the 5 empty-body stub handlers written in Task 5 with:

```csharp
    // HandleMobKilled reste un no-op volontaire — MobKilledEvent ne porte aucune info de loot
    // par item, voir HandleItemAction ci-dessous pour le vrai point d'accroche loot (décision
    // prise en lisant Events/GameEvents.cs + Systems/LootManager.cs, pas devinée).
    private void HandleMobKilled(MobKilledEvent e) { }

    private void HandleItemAction(ItemEvent e)
    {
        if (e.action != ItemAction.Pickup) return;
        string name = !string.IsNullOrEmpty(e.itemName) ? e.itemName : e.itemID;
        PostSystemMessage($"Loot : {name} ×{e.quantity}");
    }

    private void HandleQuestAction(QuestEvent e)
    {
        if (e.action != QuestAction.TurnedIn || e.quest == null) return;
        PostSystemMessage($"Quête terminée : {e.quest.questName}");
    }

    private void HandlePlayerLevelUp(PlayerLevelUpEvent e)
    {
        PostSystemMessage($"Niveau {e.newLevel} atteint !");
    }

    private void HandleMailReceived(MailReceivedEvent e)
    {
        PostSystemMessage($"Nouveau mail : {e.subject}");
    }
```

- [ ] **Step 4: Brace-balance check on all 3 modified files**

Run `grep -o '{' <file> | wc -l` vs `}` for `Systems/ChatSystem.cs`, `Systems/LootManager.cs`, `Systems/ResourceNode.cs`.
Expected: all counts equal in each file.

- [ ] **Step 5: Commit**

```bash
git add Systems/ChatSystem.cs Systems/LootManager.cs Systems/ResourceNode.cs
git commit -m "feat: handlers Système ChatSystem (loot/quête/levelup/mail) + ItemEvent.itemName"
```

---

### Task 7: `MailboxSystem` — publier `MailReceivedEvent`

**Files:**
- Modify: `Systems/MailboxSystem.cs:225-244`.

**Interfaces:**
- Consumes: `MailReceivedEvent` (Task 1), `GameEventBus.Publish(MailReceivedEvent)` (Task 2).
- Produces: nothing new — wires an existing data path to chat.

- [ ] **Step 1: Publish in the real single mail-add point**

`SendMail` (private, `Systems/MailboxSystem.cs:225-244`) is the ONLY place a mail is actually added — both `SendRewardMail` and `SendLootOverflowMail` call it, confirmed by reading the file. Current code:

```csharp
    private void SendMail(string mailID, string senderName, string subject, string body, MailReward reward)
    {
        var mail = new MailMessage
        {
            mailID        = mailID,
            senderName    = senderName,
            isFromServer  = true,
            sentAt        = System.DateTime.Now,
            subject       = subject,
            body          = body,
            reward        = reward,
            rewardClaimed = false,
            isRead        = false,
        };

        messages.Add(mail);
        Debug.Log($"[MAILBOX] Mail envoyé : {subject}");

        SocialUI.Instance?.OnNewMail(mail);
    }
```

Add the publish right after `messages.Add(mail);`:

```csharp
        messages.Add(mail);
        Debug.Log($"[MAILBOX] Mail envoyé : {subject}");
        GameEventBus.Publish(new MailReceivedEvent { subject = subject });

        SocialUI.Instance?.OnNewMail(mail);
```

- [ ] **Step 2: Brace-balance check**

Run: `grep -o '{' "Systems/MailboxSystem.cs" | wc -l` and `grep -o '}' "Systems/MailboxSystem.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 3: Commit**

```bash
git add Systems/MailboxSystem.cs
git commit -m "feat: MailboxSystem publie MailReceivedEvent (pour ChatSystem)"
```

---

### Task 8: `UI/PanelFixe/ChatUI.cs` — structure, affichage de l'historique, filtres

**Files:**
- Create: `UI/PanelFixe/ChatUI.cs`.
- Modify: `Events/GameEventBus.cs:93` area (add `ChatUI.Instance?.Resubscribe();` to `Reset()` — same list as Task 5's `ChatSystem` registration).

**Interfaces:**
- Consumes: `ChatSystem.Instance.GetHistory() : IReadOnlyList<ChatLine>` (Task 4), `GameEventBus.OnChatMessage : Action<ChatMessageEvent>` (Task 2), `ChatChannel` enum (Task 4).
- Produces: `ChatUI.Instance`, `ChatUI.Resubscribe()`, a `_dirty`-driven live-refresh loop — consumed by nothing yet (Task 9 adds the input side on the same file).

**Expected Hierarchy (Florian builds this in the Editor, same convention as `PNJQuestBoardUI` this session — this script only does `Find()` on named children):**

```
ChatPanel (racine, toujours active — HUD permanent)
  ├── MessageScrollView
  │     └── Content              (Transform — parent des lignes instanciées)
  ├── FilterBar
  │     ├── FilterWorld           (Toggle)
  │     ├── FilterGuild           (Toggle)
  │     ├── FilterPrivate         (Toggle)
  │     ├── FilterNearby          (Toggle)
  │     └── FilterSystem          (Toggle)
  │     (pas de FilterAetherEcho — ignore toujours le filtre, voir spec)
  └── ChatLinePrefab (prefab séparé, pas un enfant actif — glissé en Inspector)
        └── (TextMeshProUGUI à la racine du prefab)
```

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CHATUI — Fenêtre de chat unifiée (HUD permanent)
// Path : Assets/Scripts/UI/PanelFixe/ChatUI.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// UN SEUL flux (toutes les lignes mélangées, préfixées/colorées par canal) — PAS des onglets qui
// cachent les autres canaux (design initial faux, corrigé après inspiration Nostale réelle :
// Nostale a une fenêtre unique avec un filtre de catégorie, pas des onglets séparés).
//
// Écho d'Aether IGNORE TOUJOURS le filtre (pas de toggle dédié) — c'est sa raison d'être, voir
// spec section "Nostale : le haut-parleur ignore le filtre de vue temporaire".
//
// Construction de la Hierarchy laissée à Florian — voir le commentaire d'en-tête du plan pour la
// structure attendue exacte (noms de GameObjects que Find() cherche ci-dessous).
// =============================================================

public class ChatUI : MonoBehaviour
{
    public static ChatUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Historique")]
    public Transform  messageContent;
    public GameObject chatLinePrefab;

    [Header("Filtres (un Toggle par canal, PAS AetherEcho — toujours affiché)")]
    public Toggle filterWorld;
    public Toggle filterGuild;
    public Toggle filterPrivate;
    public Toggle filterNearby;
    public Toggle filterSystem;

    [Header("Couleurs par canal")]
    public Color colorWorld      = Color.white;
    public Color colorGuild      = new Color(0.4f, 0.85f, 0.4f);
    public Color colorPrivate    = new Color(0.9f, 0.5f,  0.8f);
    public Color colorNearby     = new Color(0.8f, 0.8f,  0.8f);
    public Color colorSystem     = new Color(0.6f, 0.6f,  0.65f);
    public Color colorAetherEcho = new Color(1f,   0.75f, 0.2f);

    private readonly List<GameObject> _lineObjects = new List<GameObject>();
    private bool _dirty = true;

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        Subscribe();

        filterWorld  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterGuild  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterPrivate?.onValueChanged.AddListener(_ => _dirty = true);
        filterNearby ?.onValueChanged.AddListener(_ => _dirty = true);
        filterSystem ?.onValueChanged.AddListener(_ => _dirty = true);
    }

    private void OnDestroy() => Unsubscribe();

    // Même bug que QuestTrackerUI/QuestJournalUI/PNJQuestBoardUI cette session —
    // GameEventBus.Reset() désabonne tout à chaque changement de scène, sans Resubscribe()
    // enregistré ce panel arrêterait de recevoir les nouveaux messages au premier changement de
    // map (ne pas répéter ce bug une 5e fois).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()   => GameEventBus.OnChatMessage += OnChatMessage;
    private void Unsubscribe() => GameEventBus.OnChatMessage -= OnChatMessage;

    private void OnChatMessage(ChatMessageEvent e) => _dirty = true;

    private void Update()
    {
        if (!_dirty) return;
        _dirty = false;
        Refresh();
    }

    // =========================================================
    // AFFICHAGE
    // =========================================================

    private bool IsChannelVisible(ChatChannel channel) => channel switch
    {
        ChatChannel.AetherEcho => true, // ignore toujours le filtre
        ChatChannel.World      => filterWorld   == null || filterWorld.isOn,
        ChatChannel.Guild      => filterGuild   == null || filterGuild.isOn,
        ChatChannel.Private    => filterPrivate == null || filterPrivate.isOn,
        ChatChannel.Nearby     => filterNearby  == null || filterNearby.isOn,
        ChatChannel.System     => filterSystem  == null || filterSystem.isOn,
        _                      => true,
    };

    private Color ChannelColor(ChatChannel channel) => channel switch
    {
        ChatChannel.World      => colorWorld,
        ChatChannel.Guild      => colorGuild,
        ChatChannel.Private    => colorPrivate,
        ChatChannel.Nearby     => colorNearby,
        ChatChannel.System     => colorSystem,
        ChatChannel.AetherEcho => colorAetherEcho,
        _                      => Color.white,
    };

    private string ChannelPrefix(ChatChannel channel) => channel switch
    {
        ChatChannel.World      => "[Monde]",
        ChatChannel.Guild      => "[Famille]",
        ChatChannel.Private    => "[Chuchotement]",
        ChatChannel.Nearby     => "[Alentour]",
        ChatChannel.System     => "[Système]",
        ChatChannel.AetherEcho => "[Écho d'Aether]",
        _                      => "",
    };

    private void Refresh()
    {
        foreach (var go in _lineObjects)
            if (go != null) Destroy(go);
        _lineObjects.Clear();

        if (ChatSystem.Instance == null || messageContent == null || chatLinePrefab == null) return;

        foreach (var line in ChatSystem.Instance.GetHistory())
        {
            if (!IsChannelVisible(line.channel)) continue;

            var go = Instantiate(chatLinePrefab, messageContent);
            _lineObjects.Add(go);

            var txt = go.GetComponentInChildren<TextMeshProUGUI>();
            if (txt == null) continue;

            string sender = string.IsNullOrEmpty(line.sender) ? "" : $"{line.sender} : ";
            txt.text  = $"{ChannelPrefix(line.channel)} {sender}{line.text}";
            txt.color = ChannelColor(line.channel);
        }
    }
}
```

- [ ] **Step 2: Register `ChatUI.Resubscribe()` in `GameEventBus.Reset()`**

In `Events/GameEventBus.cs`, right next to the `ChatSystem.Instance?.Resubscribe();` line added in Task 5, add:

```csharp
        ChatUI.Instance?.Resubscribe();
```

- [ ] **Step 3: Brace-balance check on both files**

Run `grep -o '{' <file> | wc -l` vs `}` for `UI/PanelFixe/ChatUI.cs` and `Events/GameEventBus.cs`.
Expected: both counts equal in each file.

- [ ] **Step 4: Commit**

```bash
git add UI/PanelFixe/ChatUI.cs Events/GameEventBus.cs
git commit -m "feat: ChatUI — affichage historique unifié + filtres par canal"
```

---

### Task 9: `ChatUI` — saisie + picker de canal d'envoi

**Files:**
- Modify: `UI/PanelFixe/ChatUI.cs` (add input field, send button, channel picker).

**Interfaces:**
- Consumes: `ChatSystem.Instance.TrySendPlayerMessage(ChatChannel, string, Player)` (Task 4), `ChatSystem.Instance.HasAetherEcho(Player)` (Task 4).
- Produces: nothing new for later tasks — this is the last functional piece.

**Expected additional Hierarchy (same panel as Task 8):**

```
ChatPanel
  └── InputBar
        ├── ChannelPicker        (Dropdown TMP — ou un simple Button qui cycle, au choix de
        │                          Florian en Editor ; le script lit/écrit une valeur d'index,
        │                          compatible avec les deux)
        ├── InputField            (TMP_InputField)
        └── SendButton            (Button)
```

- [ ] **Step 1: Add the fields and wiring**

In `UI/PanelFixe/ChatUI.cs`, add to the `[Header("Historique")]` block's neighboring fields (new header):

```csharp
    [Header("Saisie")]
    public TMP_Dropdown    channelPicker;
    public TMP_InputField  inputField;
    public Button          sendButton;

    private Player _player;

    // Canaux TOUJOURS listés dans le picker, dans cet ordre — AetherEcho est ajouté/retiré
    // dynamiquement en plus de ceux-ci (voir RefreshChannelPicker).
    private static readonly ChatChannel[] BaseSendableChannels =
        { ChatChannel.World, ChatChannel.Guild, ChatChannel.Private, ChatChannel.Nearby };

    private List<ChatChannel> _pickerChannels = new List<ChatChannel>();
    private bool _lastHasAetherEcho = false;
```

- [ ] **Step 2: Wire `Start()` and add the picker/send logic**

Replace the `Start()` method written in Task 8 with (adds the new wiring on top of the existing filter listeners):

```csharp
    private void Start()
    {
        Subscribe();

        filterWorld  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterGuild  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterPrivate?.onValueChanged.AddListener(_ => _dirty = true);
        filterNearby ?.onValueChanged.AddListener(_ => _dirty = true);
        filterSystem ?.onValueChanged.AddListener(_ => _dirty = true);

        sendButton   ?.onClick.AddListener(OnSendClicked);

        _lastHasAetherEcho = false; // force le premier RefreshChannelPicker() du prochain Update()
        RefreshChannelPicker();
    }
```

**Pourquoi pas un event "dropdown sur le point de s'ouvrir" :** `TMP_Dropdown` n'expose nativement que `onValueChanged` (après sélection), pas de hook "avant ouverture" simple sans ajouter un `EventTrigger` dédié côté Editor. Plutôt que de dépendre de ça, `Update()` compare `HasAetherEcho` à chaque frame (lecture booléenne, peu coûteuse) et ne reconstruit la liste QUE si elle a changé depuis la frame précédente — le picker est donc toujours à jour dès que l'item apparaît/disparaît de l'inventaire, sans jamais rester figé sur un `Start()` ancien ni dépendre d'un event incertain.

- [ ] **Step 3: Étendre `Update()` pour détecter un changement d'Écho d'Aether disponible**

Dans `UI/PanelFixe/ChatUI.cs`, remplace le `Update()` écrit en Task 8 :

```csharp
    private void Update()
    {
        if (_dirty)
        {
            _dirty = false;
            Refresh();
        }

        bool hasEcho = ChatSystem.Instance != null && ChatSystem.Instance.HasAetherEcho(_player);
        if (hasEcho != _lastHasAetherEcho)
        {
            _lastHasAetherEcho = hasEcho;
            RefreshChannelPicker();
        }
    }
```

- [ ] **Step 4: Add `RefreshChannelPicker` and `OnSendClicked`**

```csharp
    /// <summary>Reconstruit la liste des canaux proposés — appelée dès qu'Update() détecte un
    /// changement de HasAetherEcho (apparition/disparition), PAS une seule fois à Start(), pour
    /// qu'Écho d'Aether disparaisse immédiatement s'il vient d'être consommé.</summary>
    private void RefreshChannelPicker()
    {
        if (channelPicker == null) return;
        if (_player == null) _player = FindObjectOfType<Player>();

        _pickerChannels = new List<ChatChannel>(BaseSendableChannels);
        if (ChatSystem.Instance != null && ChatSystem.Instance.HasAetherEcho(_player))
            _pickerChannels.Add(ChatChannel.AetherEcho);

        var labels = new List<string>();
        foreach (var c in _pickerChannels) labels.Add(ChannelPrefix(c));

        int previousIndex = Mathf.Clamp(channelPicker.value, 0, labels.Count - 1);
        channelPicker.ClearOptions();
        channelPicker.AddOptions(labels);
        channelPicker.value = previousIndex;
    }

    private void OnSendClicked()
    {
        if (inputField == null || string.IsNullOrWhiteSpace(inputField.text)) return;
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;

        ChatChannel channel = ChatChannel.World;
        if (channelPicker != null && _pickerChannels.Count > 0)
        {
            int index = Mathf.Clamp(channelPicker.value, 0, _pickerChannels.Count - 1);
            channel = _pickerChannels[index];
        }

        bool sent = ChatSystem.Instance != null && ChatSystem.Instance.TrySendPlayerMessage(channel, inputField.text, _player);
        if (sent) inputField.text = "";

        // Le dernier Écho d'Aether vient peut-être d'être consommé — re-synchronise le picker
        // tout de suite plutôt que d'attendre la prochaine ouverture.
        RefreshChannelPicker();
    }
```

- [ ] **Step 5: Brace-balance check**

Run: `grep -o '{' "UI/PanelFixe/ChatUI.cs" | wc -l` and `grep -o '}' "UI/PanelFixe/ChatUI.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 6: Commit**

```bash
git add UI/PanelFixe/ChatUI.cs
git commit -m "feat: ChatUI — saisie + picker de canal d'envoi (Écho d'Aether dynamique)"
```

---

### Task 10: `UI/Shared/AetherEchoPromptUI.cs` — panel de saisie dédié

**Files:**
- Create: `UI/Shared/AetherEchoPromptUI.cs`.

**Interfaces:**
- Consumes: `ChatSystem.Instance.TrySendPlayerMessage(ChatChannel, string, Player) : bool` (Task 4), `ChatChannel.AetherEcho` (Task 4).
- Produces: `AetherEchoPromptUI.Instance`, `AetherEchoPromptUI.Open(Player) : void` — consumed by Task 11 (`ConsoBarUI`'s new `AetherEcho` branch).

**Design decision (Florian, 2026-10-05):** using an Écho d'Aether from the ConsoBar or by
double-clicking it in the inventory must open a small dedicated panel (text field + Valider/
Annuler), not silently consume the item nor require `ChatUI` to be open. This panel does NOT
track which specific `ConsumableInstance` to consume — it delegates entirely to `ChatSystem
.TrySendPlayerMessage`, which already re-scans the inventory itself at send time (`FindAetherEchoData`,
Task 4) — avoids ever consuming a stale/wrong instance if the inventory changed between opening
the panel and clicking Valider.

**Expected Hierarchy (Florian builds this, same convention as `ConfirmationUI` — `UI/Shared/ConfirmationUI.cs`):**

```
AetherEchoPromptPanel (racine, inactive par défaut)
  ├── MessageInput        (TMP_InputField)
  ├── ValidateButton      (Button)
  └── CancelButton        (Button)
```

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// AETHERECHOPROMPTUI — Panel de saisie pour l'Écho d'Aether
// Path : Assets/Scripts/UI/Shared/AetherEchoPromptUI.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// Ouvert par ConsoBarUI.UseConsumable (branche ConsumableType.AetherEcho) — PAS lié à l'ouverture
// de ChatUI, fonctionne même si la fenêtre de chat n'est pas affichée. Annuler ne consomme RIEN —
// seul Valider appelle ChatSystem.TrySendPlayerMessage, qui consomme l'item lui-même au moment de
// l'envoi réel (voir Systems/ChatSystem.cs — ne PAS dupliquer de logique de consommation ici).
// =============================================================

public class AetherEchoPromptUI : MonoBehaviour
{
    public static AetherEchoPromptUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject    panel;
    public TMP_InputField messageInput;
    public Button          validateButton;
    public Button          cancelButton;

    private Player _player;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        validateButton?.onClick.AddListener(OnValidateClicked);
        cancelButton  ?.onClick.AddListener(Close);
        Close();
    }

    public void Open(Player player)
    {
        _player = player;
        if (messageInput != null) messageInput.text = "";
        if (panel != null) panel.SetActive(true);
    }

    private void OnValidateClicked()
    {
        if (messageInput == null || string.IsNullOrWhiteSpace(messageInput.text)) return;
        if (_player == null || ChatSystem.Instance == null) return;

        bool sent = ChatSystem.Instance.TrySendPlayerMessage(ChatChannel.AetherEcho, messageInput.text, _player);
        if (sent) Close();
        // Refusé (item disparu entre l'ouverture et le clic, ex: vendu entre-temps) : panel reste
        // ouvert, le joueur garde son texte tapé, peut réessayer ou Annuler.
    }

    private void Close()
    {
        if (panel != null) panel.SetActive(false);
    }
}
```

- [ ] **Step 2: Brace-balance check**

Run: `grep -o '{' "UI/Shared/AetherEchoPromptUI.cs" | wc -l` and `grep -o '}' "UI/Shared/AetherEchoPromptUI.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 3: Commit**

```bash
git add UI/Shared/AetherEchoPromptUI.cs
git commit -m "feat: AetherEchoPromptUI — panel de saisie dédié à l'Écho d'Aether"
```

---

### Task 11: `ConsoBarUI.UseConsumable` — branche `AetherEcho`

**Files:**
- Modify: `UI/PanelFixe/ConsoBarUI.cs:150-200`.

**Interfaces:**
- Consumes: `AetherEchoPromptUI.Instance.Open(Player)` (Task 10).
- Produces: nothing new.

- [ ] **Step 1: Add the branch BEFORE the generic "not implemented" fallback**

Current code (`UI/PanelFixe/ConsoBarUI.cs:177-189`):

```csharp
        if (data.consumableType == ConsumableType.RewardChest)
        {
            var won = data.RollChestEntry(instance.chestRarity ?? WeaponData.RollRarity());
            if (won != null) InventorySystem.Instance?.AddItem(won);
            ConsumeAndRefresh(instance, slot);
            return;
        }

        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }
```

Change to (new block inserted between the two existing ones):

```csharp
        if (data.consumableType == ConsumableType.RewardChest)
        {
            var won = data.RollChestEntry(instance.chestRarity ?? WeaponData.RollRarity());
            if (won != null) InventorySystem.Instance?.AddItem(won);
            ConsumeAndRefresh(instance, slot);
            return;
        }

        if (data.consumableType == ConsumableType.AetherEcho)
        {
            // PAS de ConsumeAndRefresh ici — l'item n'est consommé qu'au clic Valider du panel,
            // voir AetherEchoPromptUI/ChatSystem.TrySendPlayerMessage. Annuler le panel laisse
            // l'item totalement intact (voir spec, Florian 2026-10-05).
            AetherEchoPromptUI.Instance?.Open(_player);
            return;
        }

        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }
```

- [ ] **Step 2: Brace-balance check**

Run: `grep -o '{' "UI/PanelFixe/ConsoBarUI.cs" | wc -l` and `grep -o '}' "UI/PanelFixe/ConsoBarUI.cs" | wc -l`
Expected: both counts equal.

- [ ] **Step 3: Commit**

```bash
git add UI/PanelFixe/ConsoBarUI.cs
git commit -m "feat: ConsoBarUI ouvre AetherEchoPromptUI à l'usage d'un Écho d'Aether"
```

---

### Task 12: Vérification finale (Florian, Play Mode)

**Files:** aucun — geste manuel, non-automatisable (pas de framework de test dans ce projet).

**Interfaces:**
- Consumes: tout ce qui précède (Tasks 1-11).
- Produces: rien — tâche de validation terminale.

- [ ] **Step 1: Construire la Hierarchy/prefabs**

Florian construit `ChatPanel` selon la structure documentée dans les en-têtes de Task 8 (affichage) et Task 9 (saisie) de ce plan, assigne tous les champs Inspector de `ChatUI`, crée un `ChatLinePrefab` simple (un `TextMeshProUGUI` suffit). Construit aussi `AetherEchoPromptPanel` selon la structure documentée dans l'en-tête de Task 10, assigne les champs Inspector d'`AetherEchoPromptUI`. Pose `ChatSystem`, `ChatUI` et `AetherEchoPromptUI` sur les bons GameObjects (`ChatSystem` sur `_Managers` comme les autres singletons `DontDestroyOnLoad` ; `ChatUI`/`AetherEchoPromptUI` dans le Canvas HUD permanent comme `QuestTrackerUI`/`ConfirmationUI`). Crée (ou demande à Claude de créer) un `ConsumableData` de test avec `consumableType = AetherEcho` sous `Content/` pour pouvoir tester.

- [ ] **Step 2: Les 8 scénarios de la spec**

Reproduit les 8 scénarios de la section "Tests" de `docs/superpowers/specs/2026-10-05-chat-system-design.md` :
1. Taper dans World/Private/Alentour → apparaît immédiatement, préfixe correct.
2. Taper dans Guild → refusé, ligne "Pas de guilde." en Système.
3. Sans Écho d'Aether en inventaire → absent du picker.
4. Avec 1 Écho d'Aether → apparaît dans le picker, l'envoi le consomme (vérifier disparition de l'inventaire), ligne stylée différemment.
5. Masquer le filtre World puis envoyer un Écho d'Aether → reste visible malgré le filtre.
6. Tuer un mob avec loot / terminer une quête / monter de niveau / recevoir un mail → ligne Système automatique.
7. Changer de map puis retester 1-6 → tout continue à fonctionner (vérifie `Resubscribe()`).
8. Utiliser un Écho d'Aether depuis la ConsoBar/inventaire → panel de saisie, Annuler ne consomme rien, Valider consomme et poste la ligne — même avec `ChatUI` fermé/hors écran.

- [ ] **Step 3: Rapporter à Claude**

Florian indique, pour chacun des 8 scénarios : fonctionnel / pas fonctionnel / pas testé — même format que la checklist utilisée cette session pour la validation du système de quêtes.
