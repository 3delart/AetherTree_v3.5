# Loot direct-en-inventaire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le loot d'un mob part directement dans l'inventaire du/des joueur(s) éligible(s) au
kill au lieu de spawner physiquement au sol.

**Architecture:** `LootManager.OnMobKilled` roll une fois (`LootTable.RollAll()`, inchangé),
puis pour chaque item/l'Aeris tire un gagnant aléatoire parmi `MobKilledEvent.eligiblePlayers`
(déjà calculé par `Mob.Die()`, ≥10% des dégâts) et livre directement dans
`InventorySystem.Instance` (singleton global — pas de sac par-joueur-distant tant que le
réseau n'existe pas, mais le tirage lui-même est déjà correct pour plus tard). Si l'inventaire
est plein, l'item part en mail de secours via `MailboxSystem` (référence SO, reroll à la
réclamation — pas de préservation du roll exact). `WorldPickupItem`/la branche pickup de
`TargetingSystem`/les prefabs de drop deviennent morts et sont supprimés.

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification par lecture de
code (grep) pendant l'implémentation, vérification finale manuelle en Play Mode par Florian.

**Spec:** `docs/superpowers/specs/2026-09-05-loot-direct-to-inventory-design.md`

## Global Constraints

- Aucun changement au comportement de `LootTable.RollAll()`/`LootEntry`/`RollQuantity` — le
  roll lui-même reste identique, seul ce qui arrive APRÈS change.
- `Mob.Die()`/`damageContributions`/`eligiblePlayers` (≥10% dégâts) — ne pas toucher.
- `WeaponData.weaponPrefab` — ne JAMAIS toucher (conservé, sert uniquement au visuel porté
  désormais, décision finale Florian).
- `ResourceNode`/récolte (arbres, minerai) — système séparé, non touché. Ne jamais retirer
  `ResourceData.nodePrefab` (harvesting) — seul `ResourceData.prefab` (drop au sol mob) part.
- **Déviation par rapport à la spec écrite** : la spec proposait d'ajouter `RewardType.Gem`/
  `RewardType.Rune` pour le mail de secours. En lisant `Data/Inventory/GemData.cs`/
  `RuneData.cs` pendant la planification : ces deux classes sont explicitement marquées
  "Hors scope actuel — refonte prévue" et leur `CreateAssetMenu` a été volontairement retiré
  (aucun asset ne peut être créé aujourd'hui). Construire un nouveau `RewardType`/`MailReward`
  pour un système qui va être refondu est du travail jetable — à la place, un Gem/Rune qui
  droppe avec un inventaire plein est juste loggé et perdu (Task 1, cas `default`). Si Gem/Rune
  redeviennent un vrai système plus tard, ce point de code est le seul endroit à revisiter.
- Ordinal safety : ce plan n'ajoute AUCUNE valeur d'enum (contrairement à la spec initiale) —
  rien à vérifier de ce côté.

---

### Task 1: MailboxSystem — mail de secours pour loot en inventaire plein

**Files:**
- Modify: `Systems/MailboxSystem.cs`

**Interfaces:**
- Consumes: `InventoryItem` (propriétés `WeaponInstance`/`ArmorInstance`/`HelmetInstance`/
  `GlovesInstance`/`BootsInstance`/`JewelryInstance`/`SpiritInstance`/`ConsumableInstance`/
  `ResourceInstance`, chacune avec un `.data` — déjà utilisées partout dans le projet),
  `RewardType`/`MailReward`/`MailMessage` (déjà définis dans ce même fichier).
- Produces: `public void SendLootOverflowMail(InventoryItem item, string mobName)` sur
  `MailboxSystem` — Task 2 l'appelle.

- [ ] **Step 1: Factoriser la construction de mail commune**

`SendRewardMail` construit un `MailMessage` avec un pattern qui va être répété par
`SendLootOverflowMail` — extraire un helper privé partagé. Remplacer dans
`Systems/MailboxSystem.cs` le corps actuel de `SendRewardMail` :

```csharp
    public void SendRewardMail(ConditionData condition, ConditionReward condReward)
    {
        string subject = $"Récompense débloquée : {condition.displayName}";
        string body    = condition.description;

        var mailReward = new MailReward
        {
            rewardType               = condReward.rewardType,
            rewardSkill              = condReward.rewardSkill,
            rewardTitle              = condReward.rewardTitle,
            rewardEquipment          = condReward.rewardEquipment,
            rewardResource           = condReward.rewardResource,
            rewardResourceQuantity   = condReward.rewardResourceQuantity,
            rewardConsumable         = condReward.rewardConsumable,
            rewardConsumableQuantity = condReward.rewardConsumableQuantity,
            rewardPetID              = condReward.rewardPetID,
            rewardRecipe             = condReward.rewardRecipe,
            rewardQuest              = condReward.rewardQuest,
        };

        var mail = new MailMessage
        {
            mailID        = $"reward_{condition.conditionID}_{System.DateTime.Now.Ticks}",
            senderName    = "Serveur AetherTree",
            isFromServer  = true,
            sentAt        = System.DateTime.Now,
            subject       = subject,
            body          = body,
            reward        = mailReward,
            rewardClaimed = false,
            isRead        = false,
        };

        messages.Add(mail);
        Debug.Log($"[MAILBOX] Mail envoyé : {subject}");

        SocialUI.Instance?.OnNewMail(mail);
    }
```

par :

```csharp
    public void SendRewardMail(ConditionData condition, ConditionReward condReward)
    {
        var mailReward = new MailReward
        {
            rewardType               = condReward.rewardType,
            rewardSkill              = condReward.rewardSkill,
            rewardTitle              = condReward.rewardTitle,
            rewardEquipment          = condReward.rewardEquipment,
            rewardResource           = condReward.rewardResource,
            rewardResourceQuantity   = condReward.rewardResourceQuantity,
            rewardConsumable         = condReward.rewardConsumable,
            rewardConsumableQuantity = condReward.rewardConsumableQuantity,
            rewardPetID              = condReward.rewardPetID,
            rewardRecipe             = condReward.rewardRecipe,
            rewardQuest              = condReward.rewardQuest,
        };

        SendMail(
            mailID:     $"reward_{condition.conditionID}_{System.DateTime.Now.Ticks}",
            senderName: "Serveur AetherTree",
            subject:    $"Récompense débloquée : {condition.displayName}",
            body:       condition.description,
            reward:     mailReward);
    }

    /// <summary>Envoie un loot de combat en mail de secours — l'inventaire du gagnant était
    /// plein au moment du kill (voir LootManager.DeliverItem). Ne stocke QU'une référence
    /// vers le SO d'origine, pas l'instance déjà rollée au kill — les stats sont rerollées à
    /// la réclamation (CreateDropInstance()/CreateInstance()), exactement comme un mail de
    /// récompense ConditionData aujourd'hui. Pas de préservation du roll exact — décision du
    /// design (voir spec §Gestion inventaire plein).</summary>
    public void SendLootOverflowMail(InventoryItem item, string mobName)
    {
        if (item == null) return;

        var reward = new MailReward { rewardDescription = item.Name };

        if      (item.WeaponInstance?.data     != null) { reward.rewardType = RewardType.Weapon;    reward.rewardEquipment = item.WeaponInstance.data; }
        else if (item.ArmorInstance?.data      != null) { reward.rewardType = RewardType.Armor;      reward.rewardEquipment = item.ArmorInstance.data; }
        else if (item.HelmetInstance?.data     != null) { reward.rewardType = RewardType.Helmet;     reward.rewardEquipment = item.HelmetInstance.data; }
        else if (item.GlovesInstance?.data     != null) { reward.rewardType = RewardType.Gloves;     reward.rewardEquipment = item.GlovesInstance.data; }
        else if (item.BootsInstance?.data      != null) { reward.rewardType = RewardType.Boots;      reward.rewardEquipment = item.BootsInstance.data; }
        else if (item.JewelryInstance?.data    != null) { reward.rewardType = RewardType.Jewelry;    reward.rewardEquipment = item.JewelryInstance.data; }
        else if (item.SpiritInstance?.data     != null) { reward.rewardType = RewardType.Spirit;     reward.rewardEquipment = item.SpiritInstance.data; }
        else if (item.ConsumableInstance?.data != null) { reward.rewardType = RewardType.Consumable; reward.rewardConsumable = item.ConsumableInstance.data; reward.rewardConsumableQuantity = item.ConsumableInstance.quantity; }
        else if (item.ResourceInstance?.data   != null) { reward.rewardType = RewardType.Resource;   reward.rewardResource   = item.ResourceInstance.data;   reward.rewardResourceQuantity   = item.ResourceInstance.quantity; }
        else
        {
            // Gem/Rune (ou tout type non géré ci-dessus) — pas de RewardType dédié, ces deux
            // types sont "hors scope, refonte prévue" (voir GemData.cs/RuneData.cs, aucun
            // CreateAssetMenu). Perdu si l'inventaire est plein — acceptable tant qu'aucun
            // contenu réel n'existe pour ces types.
            Debug.LogWarning($"[MAILBOX] Loot perdu (inventaire plein, type non géré par le mail) : {item.Name} ({mobName})");
            return;
        }

        SendMail(
            mailID:     $"loot_{mobName}_{System.DateTime.Now.Ticks}",
            senderName: "Butin de combat",
            subject:    $"Butin de {mobName} (inventaire plein)",
            body:       "Ton inventaire était plein au moment du kill — voici ton butin.",
            reward:     reward);
    }

    /// <summary>Construit et envoie un MailMessage — factorisé entre SendRewardMail et
    /// SendLootOverflowMail (même structure, seule la source de la récompense diffère).</summary>
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

- [ ] **Step 2: Vérifier qu'aucune référence à l'ancien corps de `SendRewardMail` n'a été
  cassée**

Run: `grep -n "SendRewardMail\|SendLootOverflowMail\|private void SendMail" Systems/MailboxSystem.cs`
Expected: `SendRewardMail` toujours appelée depuis `UnlockManager.cs` (pas modifié dans cette
tâche), `SendLootOverflowMail` et `SendMail` existent tous les deux dans `MailboxSystem.cs`.

- [ ] **Step 3: Commit**

```bash
git add Systems/MailboxSystem.cs
git commit -m "feat: add loot overflow mail fallback to MailboxSystem"
```

---

### Task 2: LootManager — livraison directe en inventaire

**Files:**
- Modify: `Systems/LootManager.cs`

**Interfaces:**
- Consumes: `MailboxSystem.Instance.SendLootOverflowMail(InventoryItem, string)` (Task 1),
  `InventorySystem.Instance.AddItem(InventoryItem item)` → `bool` (déjà existant,
  `Systems/InventorySystem.cs:120`), `AerisSystem.Instance.Add(int amount)` (déjà existant,
  `Systems/AerisSystem.cs:39`), `MobKilledEvent.eligiblePlayers` → `List<Player>` (déjà
  existant, `Events/GameEvents.cs:17`), `MobKilledEvent.mob` → `MobData` (déjà existant, a un
  champ `mobName` déjà utilisé ailleurs dans le projet, ex. `LootManager.cs:68` actuel).
- Produces: rien de consommé par une tâche suivante — `LootManager` reste un point terminal.

- [ ] **Step 1: Remplacer tout le fichier**

`Systems/LootManager.cs` — remplacer le contenu entier par :

```csharp
using System.Collections.Generic;
using UnityEngine;

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
// =============================================================

public class LootManager : MonoBehaviour
{
    public static LootManager Instance { get; private set; }

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => Resubscribe();
    private void OnDisable() => GameEventBus.OnMobKilled -= OnMobKilled;

    public void Resubscribe()
    {
        GameEventBus.OnMobKilled -= OnMobKilled;
        GameEventBus.OnMobKilled += OnMobKilled;
    }

    // =========================================================
    // ÉVÉNEMENT MOB TUÉ
    // =========================================================

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

    // =========================================================
    // DISTRIBUTION
    // =========================================================

    /// <summary>Tirage uniforme parmi les joueurs éligibles (≥10% des dégâts totaux). En solo,
    /// eligiblePlayers ne contient toujours que le joueur local — ce tirage est déjà correct
    /// tel quel, prêt à servir sans changement le jour où le réseau existera (aucun sac
    /// par-joueur-distant construit à ce jour — voir DeliverItem/DeliverAeris ci-dessous).</summary>
    private Player PickRandomEligible(List<Player> eligiblePlayers)
        => eligiblePlayers[Random.Range(0, eligiblePlayers.Count)];

    /// <summary>Livre un item au gagnant. InventorySystem est aujourd'hui un singleton global
    /// unique (pas de sac par-joueur-distant) — `winner` sert déjà à choisir CORRECTEMENT le
    /// gagnant parmi les éligibles, mais la livraison elle-même cible encore ce singleton tant
    /// que le multi/réseau n'existe pas. SEUL point à mettre à jour quand un vrai sac
    /// par-joueur-distant existera : remplacer InventorySystem.Instance par le sac de winner.</summary>
    private void DeliverItem(Player winner, InventoryItem item, string mobName)
    {
        if (winner == null || item == null) return;

        bool added = InventorySystem.Instance != null && InventorySystem.Instance.AddItem(item);
        if (added)
        {
            Debug.Log($"[LOOT] {winner.entityName} a reçu {item.Name} ({mobName}).");
        }
        else
        {
            MailboxSystem.Instance?.SendLootOverflowMail(item, mobName);
            Debug.Log($"[LOOT] Inventaire plein — {item.Name} envoyé par mail à {winner.entityName} ({mobName}).");
        }
    }

    /// <summary>Livre l'Aeris au gagnant. Même remarque que DeliverItem pour le singleton
    /// global — pas de plafond côté AerisSystem, donc jamais de mail de secours nécessaire.</summary>
    private void DeliverAeris(Player winner, int amount, string mobName)
    {
        if (winner == null || amount <= 0) return;

        AerisSystem.Instance?.Add(amount);
        Debug.Log($"[LOOT] {winner.entityName} a reçu {amount} Aeris ({mobName}).");
    }
}
```

- [ ] **Step 2: Vérifier qu'aucune référence à l'ancien code de spawn ne subsiste**

Run: `grep -rn "SpawnLootDelayed\|SpawnItem\|SpawnAeris\|RandomSpawnPos\|SpawnGO\|GetItemPrefab\|pickupPrefab\|aerisPrefab" Systems/LootManager.cs`
Expected: aucun résultat (fichier entièrement remplacé).

Run: `grep -rn "LootManager\." Assets/Scripts --include=*.cs` (ou équivalent sur ce projet)
pour repérer d'éventuels appelants externes des méthodes supprimées.
Expected: seul `LootManager.Instance` est référencé ailleurs si besoin (vérifier qu'aucun
appelant externe n'utilisait `SpawnItem`/`SpawnAeris`/etc. — ces méthodes étaient toutes
`private`, donc aucun appelant externe possible ; ce grep sert juste de garde-fou).

- [ ] **Step 3: Commit**

```bash
git add Systems/LootManager.cs
git commit -m "feat: deliver mob loot directly to inventory instead of ground-spawning"
```

---

### Task 3: TargetingSystem — retirer la branche pickup

**Files:**
- Modify: `Combat/TargetingSystem.cs`

**Interfaces:**
- Consumes: rien de nouveau.
- Produces: rien — `TargetingSystem` ne référence plus `WorldPickupItem` du tout après cette
  tâche, ce qui débloque Task 4 (suppression du fichier).

- [ ] **Step 1: Retirer le champ `selectedPickup`**

Dans `Combat/TargetingSystem.cs`, remplacer :

```csharp
    private Entity                selectedTarget;
    private Entity                engagedTarget;
    private ResourceNode          selectedNode;
    private WorldPickupItem       selectedPickup;
    private IInteractableBuilding selectedBuilding;
    private GameObject            selectedBuildingGO;
```

par :

```csharp
    private Entity                selectedTarget;
    private Entity                engagedTarget;
    private ResourceNode          selectedNode;
    private IInteractableBuilding selectedBuilding;
    private GameObject            selectedBuildingGO;
```

- [ ] **Step 2: Retirer la branche pickup du raycast dans `HandleInput()`**

Remplacer :

```csharp
        // ── Priorité 1 : WorldPickupItem (loot ou aeris) ──────
        WorldPickupItem pickup = hit.collider.GetComponentInParent<WorldPickupItem>();
        if (pickup != null) { HandlePickupClick(pickup); return; }

        // ── Priorité 2 : ResourceNode ─────────────────────────
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node != null) { HandleNodeClick(node); return; }

        // ── Priorité 3 : Bâtiment interactif ──────────────────
        IInteractableBuilding building = hit.collider.GetComponentInParent<IInteractableBuilding>();
        if (building != null) { HandleBuildingClick(building, hit.collider.gameObject); return; }

        // ── Priorité 4 : Entity ───────────────────────────────
```

par :

```csharp
        // ── Priorité 1 : ResourceNode ──────────────────────────
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node != null) { HandleNodeClick(node); return; }

        // ── Priorité 2 : Bâtiment interactif ───────────────────
        IInteractableBuilding building = hit.collider.GetComponentInParent<IInteractableBuilding>();
        if (building != null) { HandleBuildingClick(building, hit.collider.gameObject); return; }

        // ── Priorité 3 : Entity ────────────────────────────────
```

- [ ] **Step 3: Retirer `HandlePickupClick`**

Supprimer entièrement (section "── WorldPickupItem (item ou aeris) ───────────────────────" et
son commentaire de header) :

```csharp
    // ── WorldPickupItem (item ou aeris) ───────────────────────

    private void HandlePickupClick(WorldPickupItem pickup)
    {
        if (selectedPickup == pickup)
        {
            float dist = Vector3.Distance(player.transform.position, pickup.transform.position);
            if (dist <= pickup.pickupRange)
                pickup.TryPickUp();
            else
                StartApproach(ApproachPickupRoutine(pickup));
        }
        else
        {
            ClearAllSelection();
            selectedPickup = pickup;
        }
    }

```

- [ ] **Step 4: Retirer `ApproachPickupRoutine`**

Supprimer entièrement (section "── Routine WorldPickupItem (item ou aeris) ───────────────") :

```csharp
    // ── Routine WorldPickupItem (item ou aeris) ───────────────

    private IEnumerator ApproachPickupRoutine(WorldPickupItem pickup)
    {
        if (_agent == null) yield break;

        float elapsed = 0f;
        _agent.SetDestination(pickup.transform.position);

        while (elapsed < ApproachTimeout)
        {
            if (pickup == null || !pickup.gameObject.activeSelf) yield break;

            if (Vector3.Distance(player.transform.position, pickup.transform.position) <= pickup.pickupRange)
            {
                _agent.ResetPath();
                pickup.TryPickUp();
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.ResetPath();
        _approachCoroutine = null;
    }

```

- [ ] **Step 5: Retirer `selectedPickup = null;` de `ClearAllSelection()`**

Remplacer :

```csharp
    private void ClearAllSelection()
    {
        if (selectedOutline != null) { selectedOutline.enabled = false; selectedOutline = null; }
        if (_nodeOutline     != null) { _nodeOutline.enabled     = false; _nodeOutline     = null; }
        if (_buildingOutline != null) { _buildingOutline.enabled = false; _buildingOutline = null; }
        selectedTarget     = null;
        selectedNode       = null;
        selectedPickup     = null;
        selectedBuilding   = null;
        selectedBuildingGO = null;
    }
```

par :

```csharp
    private void ClearAllSelection()
    {
        if (selectedOutline != null) { selectedOutline.enabled = false; selectedOutline = null; }
        if (_nodeOutline     != null) { _nodeOutline.enabled     = false; _nodeOutline     = null; }
        if (_buildingOutline != null) { _buildingOutline.enabled = false; _buildingOutline = null; }
        selectedTarget     = null;
        selectedNode       = null;
        selectedBuilding   = null;
        selectedBuildingGO = null;
    }
```

- [ ] **Step 6: Retirer `GetSelectedPickup()`**

Remplacer :

```csharp
    public Entity                GetEngagedTarget()   => engagedTarget;
    public Entity                GetSelectedTarget()  => selectedTarget;
    public ResourceNode          GetSelectedNode()    => selectedNode;
    public WorldPickupItem       GetSelectedPickup()  => selectedPickup;
    public IInteractableBuilding GetSelectedBuilding() => selectedBuilding;
    public bool                  IsAutoAttacking      => autoAttacking;
```

par :

```csharp
    public Entity                GetEngagedTarget()   => engagedTarget;
    public Entity                GetSelectedTarget()  => selectedTarget;
    public ResourceNode          GetSelectedNode()    => selectedNode;
    public IInteractableBuilding GetSelectedBuilding() => selectedBuilding;
    public bool                  IsAutoAttacking      => autoAttacking;
```

- [ ] **Step 7: Mettre à jour les commentaires d'en-tête obsolètes**

Le header du fichier (lignes ~10-21) mentionne encore `WorldPickupItem` comme cible gérée —
remplacer :

```csharp
// ─── Cibles gérées ───────────────────────────────────────────
//   Entity  (Mob / PNJ / Pet) → Select → Engage (mobs/pets)
//   ResourceNode              → collecte
//   WorldPickupItem           → pickup (item ou Aeris)
//   IInteractableBuilding     → interaction bâtiment
//   Ground / vide             → Deselect
//
// ─── Auto-approche unifiée ───────────────────────────────────
//   TOUTES les approches passent par une coroutine unique.
//   StopApproach() est PUBLIC — appelé par PlayerController
//   dès que le joueur prend le contrôle manuel.
//   WorldLootItem et WorldAerisItem supprimés → WorldPickupItem.
```

par :

```csharp
// ─── Cibles gérées ───────────────────────────────────────────
//   Entity  (Mob / PNJ / Pet) → Select → Engage (mobs/pets)
//   ResourceNode              → collecte
//   IInteractableBuilding     → interaction bâtiment
//   Ground / vide             → Deselect
//
// Le loot de kill de mob part directement dans l'inventaire (LootManager) —
// plus de pickup au sol à cliquer, voir docs/superpowers/specs/
// 2026-09-05-loot-direct-to-inventory-design.md.
//
// ─── Auto-approche unifiée ───────────────────────────────────
//   TOUTES les approches passent par une coroutine unique.
//   StopApproach() est PUBLIC — appelé par PlayerController
//   dès que le joueur prend le contrôle manuel.
```

- [ ] **Step 8: Vérifier qu'aucune référence à WorldPickupItem ne subsiste**

Run: `grep -n "WorldPickupItem\|selectedPickup\|HandlePickupClick\|ApproachPickupRoutine\|GetSelectedPickup" Combat/TargetingSystem.cs`
Expected: aucun résultat.

- [ ] **Step 9: Commit**

```bash
git add Combat/TargetingSystem.cs
git commit -m "refactor: remove WorldPickupItem click/approach branch from TargetingSystem"
```

---

### Task 4: Supprimer World/WorldPickupItem.cs

**Files:**
- Delete: `World/WorldPickupItem.cs`
- Delete: `World/WorldPickupItem.cs.meta`

**Interfaces:**
- Consumes: nécessite Task 2 et Task 3 terminées (plus aucun créateur ni consommateur de
  `WorldPickupItem` dans le code avant de supprimer le fichier).
- Produces: rien.

- [ ] **Step 1: Vérifier qu'il ne reste plus aucune référence dans tout le projet**

Run: `grep -rn "WorldPickupItem" Assets/Scripts --include=*.cs`
Expected: aucun résultat (Task 2 a retiré les créations dans `LootManager.cs`, Task 3 a retiré
toutes les références dans `TargetingSystem.cs`). Si un résultat apparaît ailleurs, l'investiguer
avant de continuer — ne pas supprimer le fichier tant qu'une référence subsiste (le build
casserait).

- [ ] **Step 2: Supprimer les fichiers**

```bash
git rm World/WorldPickupItem.cs World/WorldPickupItem.cs.meta
```

- [ ] **Step 3: Commit**

```bash
git commit -m "chore: delete WorldPickupItem — mob loot no longer spawns on the ground"
```

---

### Task 5: Retirer les champs prefab de drop devenus morts

**Files:**
- Modify: `Data/Equipment/ArmorData.cs:66`
- Modify: `Data/Equipment/HelmetData.cs:30`
- Modify: `Data/Equipment/GlovesData.cs:35`
- Modify: `Data/Equipment/BootsData.cs:38`
- Modify: `Data/Inventory/ResourceData.cs:44`
- Modify: `Data/Inventory/ConsumableData.cs:43`

**Interfaces:**
- Consumes: nécessite Task 2 terminée (`LootManager.GetItemPrefab`, seul lecteur de ces
  champs, a déjà disparu).
- Produces: rien.

**Ne PAS toucher** : `Data/Equipment/WeaponData.cs:34` (`weaponPrefab`, conservé — sert au
visuel porté), `Data/Inventory/ResourceData.cs:49` (`nodePrefab`, sert à la récolte via
`Systems/SpawnManager.cs`, système séparé non touché par ce plan).

- [ ] **Step 1: `Data/Equipment/ArmorData.cs`**

Remplacer (aucun attribut dédié au-dessus de ce champ — `[Header]` partagé avec `armorType`
juste au-dessus reste en place) :
```csharp
    public ArmorType  armorType = ArmorType.Melee;

    public GameObject armorPrefab;

    // ── Niveau ────────────────────────────────────────────────
```
par :
```csharp
    public ArmorType  armorType = ArmorType.Melee;

    // ── Niveau ────────────────────────────────────────────────
```

- [ ] **Step 2: `Data/Equipment/HelmetData.cs`**

Remplacer :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject helmetPrefab;

    // ── Condition ─────────────────────────────────────────────
```
par :
```csharp
    // ── Condition ─────────────────────────────────────────────
```
(le bloc `// ── Identité ──` + `[Header("Identité")]` ne servait qu'à ce seul champ dans ce
fichier — supprimé avec lui.)

- [ ] **Step 3: `Data/Equipment/GlovesData.cs`**

Remplacer :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject glovesPrefab;

    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
```
par :
```csharp
    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
```

- [ ] **Step 4: `Data/Equipment/BootsData.cs`**

Remplacer :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public GameObject bootsPrefab;

    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
```
par :
```csharp
    // ── Défense mêlée — rollée, PAS de rareté/upgrade sur ce slot ──
```

- [ ] **Step 5: `Data/Inventory/ResourceData.cs`**

Remplacer (garder `resourceType` et surtout `nodePrefab` juste en dessous, intact — sert à la
récolte via `Systems/SpawnManager.cs`, système séparé hors périmètre) :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ResourceType resourceType = ResourceType.CraftMaterial;
    public GameObject   prefab;       // prefab objet au sol (WorldLootItem)

    // ── Node World (Collectible uniquement) ───────────────────
```
par :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ResourceType resourceType = ResourceType.CraftMaterial;

    // ── Node World (Collectible uniquement) ───────────────────
```

- [ ] **Step 6: `Data/Inventory/ConsumableData.cs`**

Remplacer :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ConsumableType  consumableType = ConsumableType.Potion;
    public GameObject      prefab;

    // ── Potion / Food — mêmes champs, à plat, pas de sous-section ──
```
par :
```csharp
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public ConsumableType  consumableType = ConsumableType.Potion;

    // ── Potion / Food — mêmes champs, à plat, pas de sous-section ──
```

- [ ] **Step 7: Vérifier qu'aucun code ne référence encore ces champs**

Run: `grep -rn "\.armorPrefab\|\.helmetPrefab\|\.glovesPrefab\|\.bootsPrefab" Assets/Scripts --include=*.cs`
Expected: aucun résultat (seul lecteur était `LootManager.GetItemPrefab`, supprimé en Task 2).

Run: `grep -n "ConsumableInstance?.data?.prefab\|ResourceInstance?.data?.prefab" Assets/Scripts --include=*.cs -r`
Expected: aucun résultat, même raison.

Run: `grep -n "weaponPrefab" Data/Equipment/WeaponData.cs`
Expected: le champ `weaponPrefab` est toujours présent (confirmation qu'il n'a pas été
accidentellement touché par cette tâche).

- [ ] **Step 8: Commit**

```bash
git add Data/Equipment/ArmorData.cs Data/Equipment/HelmetData.cs Data/Equipment/GlovesData.cs Data/Equipment/BootsData.cs Data/Inventory/ResourceData.cs Data/Inventory/ConsumableData.cs
git commit -m "chore: remove dead ground-drop prefab fields (loot no longer spawns physically)"
```

---

### Task 6: Vérification manuelle finale (Play Mode)

**Files:** aucun — tâche de vérification uniquement, pas de code.

**Interfaces:**
- Consumes: toutes les tâches précédentes doivent être terminées et le projet doit compiler
  sans erreur dans Unity avant de commencer cette tâche.

- [ ] **Step 1: Compiler dans Unity**

Ouvrir le projet dans Unity Editor, attendre la recompilation, vérifier 0 erreur dans la
Console. S'il y a des erreurs, les résoudre avant de continuer (probablement un reste de
référence à un champ/méthode supprimé — grep le message d'erreur exact).

- [ ] **Step 2: Kill solo — livraison directe**

Tuer un mob avec un seul joueur éligible (le joueur local, seul dans la zone). Vérifier dans
la Console :
- Un log `[LOOT] {joueur} a reçu {item} ({mob}).` par item droppé.
- Un log `[LOOT] {joueur} a reçu {N} Aeris ({mob}).` si de l'Aeris a droppé.
- Aucun objet visible au sol à l'emplacement de la mort.
- Les items sont bien présents dans l'inventaire (`I` ou le raccourci habituel), l'Aeris est
  bien crédité (affichage HUD).

- [ ] **Step 3: Inventaire plein — mail de secours**

Remplir l'inventaire jusqu'à `MAX_SLOTS` (80 emplacements — utiliser un item stackable en
quantité ou looter/acheter jusqu'à saturation). Tuer un mob qui droppe un item d'équipement
(pas juste une ressource stackable, pour bien tester le cas "nouveau slot refusé"). Vérifier :
- Log `[LOOT] Inventaire plein — {item} envoyé par mail à {joueur} ({mob}).`
- Un nouveau mail apparaît dans Messagerie (Panel Social), sujet "Butin de {mob} (inventaire
  plein)".
- Cliquer "Récupérer" sur ce mail (après avoir vidé de la place dans l'inventaire) — l'item
  apparaît bien dans l'inventaire, avec des stats fraîchement rollées (pas d'erreur, pas de
  crash).

- [ ] **Step 4: Récolte non affectée**

Aller sur un nœud de récolte existant (arbre, minerai). Vérifier que le clic, l'approche
automatique et la collecte fonctionnent exactement comme avant ce plan — aucune régression.

- [ ] **Step 5: Visuel arme inchangé**

Équiper différentes armes (`WeaponData` différents, même type ou non). Vérifier que chaque
arme garde bien son propre modèle visuel porté sur le personnage, comme avant ce plan (ce
plan ne touche PAS `WeaponData.weaponPrefab` ni `World/WeaponVisual.cs`).

- [ ] **Step 6: Rapport**

Si tout est correct, considérer le plan terminé — pas de commit supplémentaire nécessaire pour
cette tâche (vérification uniquement).
