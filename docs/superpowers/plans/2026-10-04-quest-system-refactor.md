# Refonte du système de quêtes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this
> plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer les prérequis/objectifs/récompenses rigides de `QuestData` par un système
composable (prérequis AND/OR/NOT, objectifs réellement câblés sur les events existants,
récompenses variables par famille d'arme), et combler les 3 gaps opérationnels identifiés par
l'audit (abandon de quête, précheck inventaire, sauvegarde par clé stable).

**Architecture:** Un nouvel objet `RequirementSet`/`Requirement` (Data/Requirements/) remplace
`minLevel`/`prerequisiteQuest`. `QuestObjectiveType.Deliver` est fusionné dans `Gather` (ordinal
retiré en trou commenté). Trois nouveaux handlers dans `QuestSystem.cs` (Gather/Explore/Craft)
branchent les événements déjà publiés (`ItemEvent`/`ZoneEvent`/`RecipeCraftedEvent`) sur le même
schéma que `HandleMobKilled`/`NotifyTalkTo` existants — zéro infra événementielle à inventer.
`QuestRewardItem` gagne un filtre optionnel par famille d'arme, résolu automatiquement au
turn-in, sans jamais proposer de choix au joueur.

**Tech Stack:** Unity C#, ScriptableObject, `GameEventBus` (event bus statique maison),
`JsonUtility` pour la sauvegarde (`SaveSystem.cs`/`CharacterProgress.cs`). Aucun framework de
test automatisé dans ce projet.

**Spec:** `docs/superpowers/specs/2026-10-04-quest-system-refactor-design.md`

## Global Constraints

- Aucun framework de test automatisé, aucun compilateur Unity accessible depuis cette session
  (confirmé — convention déjà établie sur tous les chantiers précédents de cette session). Le
  "test" de chaque tâche est donc toujours : (1) relecture du diff produit — accolades
  équilibrées, types cohérents avec les tâches précédentes, aucune référence à un symbole
  supprimé ; (2) un scénario de vérification manuelle Play Mode précis, à exécuter par Florian
  après compilation réelle dans Unity Editor. Aucune commande shell n'est inventée pour simuler
  un test qui n'existe pas.
- Ordinal safety sur tout enum sérialisé par Unity : jamais renuméroter/réutiliser un ordinal
  retiré — toujours un trou commenté (`// N retiré (date) — raison. Ordinal N jamais réutilisé.`),
  même convention que `PNJType`/`DialogueAction` déjà appliquée ce chantier-ci.
- Renommage de champ déjà sérialisé → `[FormerlySerializedAs("ancienNom")]` + `using
  UnityEngine.Serialization;`, jamais un renommage sec qui perdrait la valeur sur les assets
  existants.
- Grep systématique avant toute suppression de symbole (champ, valeur d'enum) pour confirmer
  l'absence d'autre usage dans le code — déjà fait pour tout ce plan lors de son écriture, mais à
  refaire si un écart est constaté en cours d'exécution.
- Commits directs sur `master` une fois chaque tâche terminée (convention du projet, confirmée
  explicitement par Florian pour ce repo) — pas de branche dédiée à créer sauf instruction
  contraire.
- `RequirementSet` est scopé au gating de quête (`QuestData.requirements`) dans ce chantier — pas
  de câblage côté sélection de dialogue PNJ (chantier séparé, en pause, repris plus tard).

## Review Focus

- **`ItemOwned` requirement toujours fausse pour tout item qui n'est ni `ResourceData` ni
  `ConsumableData`** — `InventorySystem.GetItemCount(ItemData)` ne sait compter que ces deux
  types (switch existant, confirmé en lisant `InventorySystem.cs`). Un designer qui configure un
  prérequis "posséder cette arme" verrait ce prérequis échouer silencieusement pour toujours.
  Couvert par la Tâche 2 (warning `OnValidate` explicite si `item` n'est ni Resource ni
  Consumable).
- **Récompense à 0 item si TOUTES les entrées sont filtrées par famille d'arme et qu'aucune ne
  matche le joueur** — la quête se termine (XP/Aeris/Prestige accordés, état `TurnedIn`) sans
  qu'aucun item ne soit reçu, sans que rien ne le signale. Couvert par la Tâche 8 (warning
  explicite si `eligibleRewards.Count == 0` alors que `quest.rewardItems.Count > 0`).
  Comportement accepté tel quel par ailleurs (pas de choix joueur — hors scope, confirmé par
  Florian), seul le silence est corrigé.
  <br>
- **Ancienne sauvegarde chargée après le changement de format `QuestSaveEntry`** (Tâche 12) — le
  champ `objectiveCounts: List<int>` devient `objectiveEntries: List<...>` avec un nom ET un type
  différents. `JsonUtility` ignore silencieusement les clés JSON qui n'ont plus de champ
  correspondant : une sauvegarde antérieure verra ses compteurs d'objectifs EN COURS
  (`QuestState.Active`) réinitialisés à 0 au premier chargement après la mise à jour — l'état
  (`Active`/`Completed`/`TurnedIn`) lui-même n'est pas affecté (champ `state` inchangé). Ruling
  explicite dans la Tâche 12 : accepté tel quel, contenu de test uniquement à ce stade du projet,
  effet cosmétique et ponctuel (une seule fois, à la prochaine sauvegarde le nouveau format est
  écrit).
- **`ZoneEvent` est publié à CHAQUE tick périodique dans la zone, pas seulement à l'entrée** — un
  objectif Explore pourrait sembler s'incrémenter en boucle. Déjà sans risque réel : `
  QuestObjective.Increment()` retourne immédiatement si `IsComplete` est déjà vrai (`requiredCount`
  vaut 1 pour ce type dans l'usage courant) — aucun comportement erroné, mais à documenter dans le
  handler (Tâche 6) pour qu'un futur lecteur ne s'inquiète pas à tort.
- **Objectif Gather/Craft avec `targetItem` resté vide (non assigné)** — `(ItemData)obj.targetItem`
  vaudrait `null`, et le match `null.itemID` planterait sans garde. Couvert par un null-check
  explicite dans chaque handler (Tâche 6), jamais un cast qui throw.

---

### Tâche 1 : `RequirementSet` — nouvel objet de prérequis composable

**Fichiers :**
- Créer : `Data/Requirements/RequirementSet.cs`

**Interfaces :**
- Produit : `enum RequirementField { Level, PrestigeRank, QuestState, ItemOwned }`, `class
  Requirement { RequirementField field; int minLevel; int minPrestigeRank; QuestData quest;
  QuestState requiredState; ItemData item; int itemCount; bool IsMet(Player player) }`, `class
  RequirementSet { List<Requirement> requirements; bool requireAny; bool reverseMatch; bool
  IsMet(Player player) }` — consommés par la Tâche 2 (`QuestData.requirements`) et la Tâche 3
  (`QuestSystem.CanAccept`).
- Consomme : `Player.level`, `Player.prestigeRank` (`Entities/Player.cs:199,214`, déjà publics),
  `QuestSystem.Instance.GetQuestState(QuestData)` (déjà public), `InventorySystem.Instance.
  GetItemCount(ItemData)` (déjà public, `Systems/InventorySystem.cs:284`), `ItemData` (déjà
  existant, `Data/ItemData.cs`).

- [ ] **Étape 1 : Écrire le fichier**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================
// REQUIREMENTSET.CS — Prérequis composables AND/OR/NOT, réutilisables
// Path : Assets/Scripts/Data/Requirements/RequirementSet.cs
// AetherTree GDD v31 — §25 (refonte quêtes, 2026-10-04)
//
// Remplace QuestData.minLevel/prerequisiteQuest par un objet composable.
// Nommé RequirementSet/Requirement — pas PNJCondition* (plus PNJ-spécifique
// maintenant), pas Condition* (collision avec Progression/Conditions/
// ConditionData.cs, système d'achievement événementiel sans rapport).
//
// Scopé au gating de quête (QuestData.requirements) pour ce chantier —
// réutilisation côté sélection de dialogue PNJ (chantier séparé, en pause)
// prévue mais pas branchée ici.
//
// 4 types seulement — pas de "Reputation" par faction : Player ne stocke
// aucun score par faction (seul prestigeRank existe), un tel champ serait
// non fonctionnel (vérifié avant d'écrire ce fichier, voir le spec).
// =============================================================

public enum RequirementField { Level, PrestigeRank, QuestState, ItemOwned }

[Serializable]
public class Requirement
{
    public RequirementField field = RequirementField.Level;

    [ShowIf(nameof(field), RequirementField.Level)]
    public int minLevel = 1;

    [ShowIf(nameof(field), RequirementField.PrestigeRank)]
    public int minPrestigeRank = 0;

    [ShowIf(nameof(field), RequirementField.QuestState)]
    public QuestData quest;
    [ShowIf(nameof(field), RequirementField.QuestState)]
    public QuestState requiredState = QuestState.TurnedIn;

    [Tooltip("ATTENTION : InventorySystem.GetItemCount() ne sait compter que ResourceData et\n" +
             "ConsumableData — un item d'un autre type (Weapon/Armor/...) rendra ce prérequis\n" +
             "TOUJOURS faux. Un warning Console signale ce cas depuis l'Inspector de QuestData.")]
    [ShowIf(nameof(field), RequirementField.ItemOwned)]
    public ItemData item;
    [ShowIf(nameof(field), RequirementField.ItemOwned)]
    public int itemCount = 1;

    /// <summary>True si CE prérequis seul est rempli pour ce joueur.</summary>
    public bool IsMet(Player player)
    {
        if (player == null) return false;

        switch (field)
        {
            case RequirementField.Level:
                return player.level >= minLevel;

            case RequirementField.PrestigeRank:
                return player.prestigeRank >= minPrestigeRank;

            case RequirementField.QuestState:
                if (quest == null) return true; // pas de quête assignée = condition vide, toujours vraie
                return QuestSystem.Instance != null
                    && QuestSystem.Instance.GetQuestState(quest) == requiredState;

            case RequirementField.ItemOwned:
                if (item == null) return true; // pas d'item assigné = condition vide, toujours vraie
                return InventorySystem.Instance != null
                    && InventorySystem.Instance.GetItemCount(item) >= Mathf.Max(1, itemCount);

            default:
                return true;
        }
    }
}

[Serializable]
public class RequirementSet
{
    public List<Requirement> requirements = new List<Requirement>();

    [Tooltip("false = toutes les conditions doivent être vraies (AND).\n" +
             "true = au moins une condition doit être vraie (OR).")]
    public bool requireAny = false;

    [Tooltip("Inverse le résultat final (NOT) — coché = le résultat normal doit être FAUX pour\n" +
             "que IsMet() renvoie vrai.")]
    public bool reverseMatch = false;

    /// <summary>True si l'ensemble des prérequis est rempli pour ce joueur. Une liste vide est
    /// toujours considérée remplie (aucun prérequis = toujours accessible).</summary>
    public bool IsMet(Player player)
    {
        bool result;

        if (requirements == null || requirements.Count == 0)
        {
            result = true;
        }
        else if (requireAny)
        {
            result = false;
            foreach (var r in requirements)
                if (r != null && r.IsMet(player)) { result = true; break; }
        }
        else
        {
            result = true;
            foreach (var r in requirements)
                if (r != null && !r.IsMet(player)) { result = false; break; }
        }

        return reverseMatch ? !result : result;
    }
}
```

- [ ] **Étape 2 : Relecture de sanité**

Vérifier : accolades équilibrées, `using UnityEngine;` présent (nécessaire pour `Mathf`,
`Tooltip`), aucune référence à un symbole qui n'existe pas encore (`QuestData`/`QuestSystem`/
`InventorySystem`/`Player`/`ItemData` existent tous déjà dans le projet — confirmé par lecture
directe avant d'écrire ce plan).

- [ ] **Étape 3 : Commit**

```bash
git add Data/Requirements/RequirementSet.cs
git commit -m "feat: ajoute RequirementSet — prérequis composables AND/OR/NOT pour les quêtes"
```

---

### Tâche 2 : `QuestData.cs` — intégration de `RequirementSet`, retrait de `minLevel`/`prerequisiteQuest`

**Fichiers :**
- Modifier : `Data/Quests/QuestData.cs:113-116` (bloc `[Header("Prérequis")]`), `:176-182`
  (`OnValidate`)

**Interfaces :**
- Consomme : `RequirementSet` (Tâche 1).
- Produit : `QuestData.requirements : RequirementSet` — consommé par la Tâche 3
  (`QuestSystem.CanAccept`) et la Tâche 13 (migration des assets de test).

- [ ] **Étape 1 : Remplacer le bloc Prérequis**

Remplacer (lignes 113-116) :

```csharp
    [Header("Prérequis")]
    public int       minLevel          = 1;
    [Tooltip("Quête à compléter avant celle-ci (chaîne de quêtes)")]
    public QuestData prerequisiteQuest = null;
```

par :

```csharp
    [Header("Prérequis")]
    public RequirementSet requirements = new RequirementSet();
```

- [ ] **Étape 2 : Étendre `OnValidate` avec le warning `ItemOwned`**

Remplacer (lignes 176-182) :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(questID))
            questID = name;
    }
#endif
```

par :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(questID))
            questID = name;

        if (requirements?.requirements != null)
        {
            foreach (var r in requirements.requirements)
            {
                if (r == null || r.field != RequirementField.ItemOwned || r.item == null) continue;
                if (!(r.item is ResourceData) && !(r.item is ConsumableData))
                    Debug.LogWarning($"[QUEST] {questName} : prérequis ItemOwned sur '{r.item.name}' — " +
                        "InventorySystem.GetItemCount ne sait compter que ResourceData/ConsumableData, " +
                        "ce prérequis sera TOUJOURS considéré non rempli pour ce type d'item.");
            }
        }
    }
#endif
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : plus aucune référence à `minLevel`/`prerequisiteQuest` dans ce fichier (elles seront
retirées de `QuestSystem.cs` à la Tâche 3 — jusque-là le projet ne compile pas, normal en milieu
de chantier inline, la Tâche 3 suit immédiatement).

- [ ] **Étape 4 : Commit**

```bash
git add Data/Quests/QuestData.cs
git commit -m "refactor: QuestData.requirements remplace minLevel/prerequisiteQuest"
```

---

### Tâche 3 : `QuestSystem.cs` — `CanAccept` utilise `RequirementSet`

**Fichiers :**
- Modifier : `Systems/QuestSystem.cs:87-107` (`CanAccept`)

**Interfaces :**
- Consomme : `QuestData.requirements` (Tâche 2), `RequirementSet.IsMet(Player)` (Tâche 1).

- [ ] **Étape 1 : Remplacer `CanAccept`**

Remplacer (lignes 87-107) :

```csharp
    /// <summary>True si la quête peut être acceptée.</summary>
    public bool CanAccept(QuestData quest, Player player)
    {
        if (quest == null) return false;
        if (string.IsNullOrEmpty(quest.questID)) return false;

        // Déjà acceptée ou terminée ?
        var state = GetQuestState(quest.questID);
        if (state == QuestState.Active || state == QuestState.TurnedIn) return false;

        // Niveau minimum
        if (player != null && player.level < quest.minLevel) return false;

        // Prérequis quête
        if (quest.prerequisiteQuest != null)
        {
            var preState = GetQuestState(quest.prerequisiteQuest.questID);
            if (preState != QuestState.TurnedIn) return false;
        }

        return true;
    }
```

par :

```csharp
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
```

- [ ] **Étape 2 : Relecture de sanité**

Vérifier : `quest.requirements` existe bien sur `QuestData` depuis la Tâche 2, aucune autre
occurrence de `.minLevel`/`.prerequisiteQuest` ne reste dans ce fichier (confirmées absentes par
grep avant l'écriture de ce plan — seules lignes 97/100/102, toutes dans ce bloc).

- [ ] **Étape 3 : Vérification manuelle Play Mode**

Florian recompile dans Unity Editor (0 erreur attendue). Sur `quest_braven_chicken_test.asset` ou
`quest_braven_loup_test.asset` (migrées à la Tâche 13), configurer un `Requirement` de type Level
(ex. niveau 5 minimum) et vérifier qu'elle n'est proposable qu'à partir de ce niveau — scénario 2
de la section Vérification du spec.

- [ ] **Étape 4 : Commit**

```bash
git add Systems/QuestSystem.cs
git commit -m "refactor: CanAccept utilise RequirementSet.IsMet au lieu de minLevel/prerequisiteQuest"
```

---

### Tâche 4 : `QuestObjectiveType` — retrait de `Deliver` (fusionné dans `Gather`)

**Fichiers :**
- Modifier : `Data/Quests/QuestData.cs:208-209` (`ShowIf` de `targetItem`), `:222-232`
  (`TargetName`), `:243` (enum `QuestObjectiveType`)

**Interfaces :**
- Produit : `enum QuestObjectiveType { Kill=0, TalkTo=1, /* 2 retiré */ Gather=3, Explore=4,
  Craft=5, Boss=6 }` — consommé par la Tâche 6 (handlers) et la Tâche 7 (warning Boss).

- [ ] **Étape 1 : Mettre à jour le `ShowIf` de `targetItem`**

Remplacer (ligne 208-209) :

```csharp
    [Tooltip("Deliver / Gather / Craft — glisser le SO item")]
    [ShowIf(nameof(type), QuestObjectiveType.Deliver, QuestObjectiveType.Gather, QuestObjectiveType.Craft)]
    public ScriptableObject targetItem;
```

par :

```csharp
    [Tooltip("Gather / Craft — glisser le SO item (doit hériter de ItemData, ex: ResourceData,\n" +
             "ConsumableData, WeaponData... — un item Gem/Rune, qui n'hérite pas d'ItemData, ne\n" +
             "peut pas être ciblé par ce type d'objectif)")]
    [ShowIf(nameof(type), QuestObjectiveType.Gather, QuestObjectiveType.Craft)]
    public ScriptableObject targetItem;
```

- [ ] **Étape 2 : Mettre à jour `TargetName`**

Remplacer (lignes 222-232) :

```csharp
    public string TargetName => type switch
    {
        QuestObjectiveType.Kill    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.Boss    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.TalkTo  => targetPNJ  != null ? targetPNJ.pnjName  : "",
        QuestObjectiveType.Deliver => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Gather  => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Craft   => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Explore => targetZoneID,
        _                          => ""
    };
```

par :

```csharp
    public string TargetName => type switch
    {
        QuestObjectiveType.Kill    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.Boss    => targetMob  != null ? targetMob.mobName  : "",
        QuestObjectiveType.TalkTo  => targetPNJ  != null ? targetPNJ.pnjName  : "",
        QuestObjectiveType.Gather  => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Craft   => targetItem != null ? targetItem.name    : "",
        QuestObjectiveType.Explore => targetZoneID,
        _                          => ""
    };
```

- [ ] **Étape 3 : Retirer l'ordinal `Deliver` de l'enum**

Remplacer (ligne 243) :

```csharp
public enum QuestObjectiveType { Kill = 0, TalkTo = 1, Deliver = 2, Gather = 3, Explore = 4, Craft = 5, Boss = 6 }
```

par :

```csharp
// 2 retiré (2026-10-04) — Deliver fusionné dans Gather (même mécanique exacte, zéro asset
// n'utilisait cet ordinal — vérifié par grep direct sur les .asset avant suppression). Ordinal 2
// jamais réutilisé.
public enum QuestObjectiveType { Kill = 0, TalkTo = 1, Gather = 3, Explore = 4, Craft = 5, Boss = 6 }
```

- [ ] **Étape 4 : Relecture de sanité**

Grep `QuestObjectiveType.Deliver` dans tout le projet pour confirmer zéro référence restante
(déjà confirmé par grep avant l'écriture de ce plan — seules les 2 occurrences de ce fichier,
maintenant retirées).

- [ ] **Étape 5 : Commit**

```bash
git add Data/Quests/QuestData.cs
git commit -m "refactor: retire QuestObjectiveType.Deliver (fusionné dans Gather, ordinal 2 retiré)"
```

---

### Tâche 5 : `QuestObjective` — champ `objectiveID` stable

**Fichiers :**
- Modifier : `Data/Quests/QuestData.cs:190-198` (classe `QuestObjective`), `OnValidate` de
  `QuestData` (étendu à la Tâche 2)

**Interfaces :**
- Produit : `QuestObjective.objectiveID : string` — consommé par la Tâche 12 (sauvegarde par clé
  stable) et la Tâche 13 (migration des assets de test).

- [ ] **Étape 1 : Ajouter le champ**

Dans la classe `QuestObjective`, juste après `public QuestObjectiveType type = QuestObjectiveType.Kill;`
(ligne 198), ajouter :

```csharp

    [Tooltip("Clé technique STABLE — ne change jamais, utilisée pour la sauvegarde de la\n" +
             "progression (voir QuestSystem.GetSaveData/LoadSaveData). Auto-remplie si vide,\n" +
             "ne JAMAIS afficher au joueur.")]
    public string objectiveID = "";
```

- [ ] **Étape 2 : Auto-remplissage dans `OnValidate`**

Dans le `OnValidate` de `QuestData` (étendu à la Tâche 2), ajouter après le bloc de warning
`ItemOwned` :

```csharp

        if (objectives != null)
        {
            foreach (var o in objectives)
                if (o != null && string.IsNullOrEmpty(o.objectiveID))
                    o.objectiveID = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : le bloc est bien À L'INTÉRIEUR de la méthode `OnValidate` existante (ne pas créer une
seconde méthode du même nom — erreur de compilation CS0111). `System.Guid` ne nécessite pas de
`using` supplémentaire (`System` est déjà qualifié en toutes lettres ici).

- [ ] **Étape 4 : Vérification manuelle Play Mode**

Florian recompile, ouvre `quest_braven_chicken_test.asset` dans l'Inspector (avant la migration
de la Tâche 13) — un `objectiveID` généré automatiquement doit apparaître sur chacun des 2
objectifs existants dès l'ouverture (déclenche `OnValidate`).

- [ ] **Étape 5 : Commit**

```bash
git add Data/Quests/QuestData.cs
git commit -m "feat: QuestObjective.objectiveID — clé stable auto-remplie pour la sauvegarde"
```

---

### Tâche 6 : `QuestSystem.cs` — câblage Gather/Explore/Craft

**Fichiers :**
- Modifier : `Systems/QuestSystem.cs:44-45` (`Subscribe`/`Unsubscribe`), zone après
  `HandleMobKilled` (après ligne 242)

**Interfaces :**
- Consomme : `GameEventBus.OnItemAction : Action<ItemEvent>`, `GameEventBus.OnZoneEntered :
  Action<ZoneEvent>`, `GameEventBus.OnRecipeCrafted : Action<RecipeCraftedEvent>` (tous déjà
  déclarés et publiés, `Events/GameEventBus.cs:27-28,35`, `Events/GameEvents.cs`),
  `QuestObjectiveType.Gather/Explore/Craft` (Tâche 4), `ItemData` (`Data/ItemData.cs`),
  `RecipeData.result : ItemData` (`Data/Craft/RecipeData.cs:84`, déjà typé `ItemData`
  directement — comparaison directe possible, aucun cast nécessaire côté Craft).

- [ ] **Étape 1 : Étendre `Subscribe`/`Unsubscribe`**

Remplacer (lignes 44-45) :

```csharp
    private void Subscribe()   => GameEventBus.OnMobKilled += HandleMobKilled;
    private void Unsubscribe() => GameEventBus.OnMobKilled -= HandleMobKilled;
```

par :

```csharp
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
```

- [ ] **Étape 2 : Ajouter les 3 handlers**

Juste après la fermeture de `HandleMobKilled` (après la ligne `}` qui suit la ligne 242), ajouter :

```csharp

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
    // ZoneEvent est publié à CHAQUE tick périodique dans la zone, pas seulement à l'entrée —
    // sans risque ici : Increment() retourne immédiatement si IsComplete est déjà vrai, et
    // requiredCount vaut 1 pour ce type dans l'usage courant (entrer dans la zone suffit).

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
                if (obj.targetZoneID != e.zoneID) continue;

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
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : `ItemAction`/`ItemEvent`/`ZoneEvent`/`RecipeCraftedEvent` sont bien dans le namespace
global (pas de `using` manquant — tous déjà utilisés sans qualification ailleurs dans le
projet), les 3 nouveaux handlers suivent exactement le schéma de `HandleMobKilled` (boucle
`_activeData`/check état Active/`GetActiveObjectiveIndices`/check type/Increment/Publish/
CheckCompletion).

- [ ] **Étape 4 : Vérification manuelle Play Mode**

Florian recompile. Configure temporairement un objectif Gather sur une des quêtes de test
(viser une `ResourceData` déjà présente en jeu), ramasse l'item ciblé → vérifie que la
progression avance (scénario 4 du spec). Idem pour Explore (entrer dans une zone, scénario 5) et
Craft (crafter la recette ciblée, scénario 6).

- [ ] **Étape 5 : Commit**

```bash
git add Systems/QuestSystem.cs
git commit -m "feat: câble les objectifs Gather/Explore/Craft sur ItemEvent/ZoneEvent/RecipeCraftedEvent"
```

---

### Tâche 7 : `QuestData.cs` — warning `OnValidate` pour un objectif Boss mal configuré

**Fichiers :**
- Modifier : `OnValidate` de `QuestData` (étendu aux Tâches 2 et 5)

**Interfaces :**
- Consomme : `MobData.IsBoss()` (déjà existant, `Data/Mobs/MobData.cs:231-235`).

- [ ] **Étape 1 : Ajouter le warning**

Dans le `OnValidate` de `QuestData`, ajouter après le bloc d'auto-remplissage `objectiveID`
(Tâche 5) :

```csharp

        if (objectives != null)
        {
            foreach (var o in objectives)
                if (o != null && o.type == QuestObjectiveType.Boss && o.targetMob != null && !o.targetMob.IsBoss())
                    Debug.LogWarning($"[QUEST] {questName} : objectif Boss '{o.description}' pointe vers " +
                        $"'{o.targetMob.mobName}' qui n'est pas un boss (MobData.IsBoss() == false) — " +
                        "probable erreur de configuration.");
        }
```

- [ ] **Étape 2 : Relecture de sanité**

Vérifier : ce bloc boucle sur `objectives` séparément du bloc `objectiveID` de la Tâche 5 (deux
boucles `foreach` distinctes dans la même méthode — acceptable, chacune a une responsabilité
propre, pas besoin de les fusionner).

- [ ] **Étape 3 : Vérification manuelle Play Mode**

Florian recompile. Configure un objectif Boss pointant par erreur vers un `MobData` normal
(`IsBoss() == false`) → vérifie l'apparition du warning Console dès l'ouverture de l'asset
(scénario 7, deuxième partie, du spec).

- [ ] **Étape 4 : Commit**

```bash
git add Data/Quests/QuestData.cs
git commit -m "feat: warning OnValidate si un objectif Boss cible un MobData non-boss"
```

---

### Tâche 8 : `QuestRewardItem` — filtre par famille d'arme

**Fichiers :**
- Modifier : `Data/Quests/QuestData.cs:14-29` (classe `QuestRewardItem`), `Systems/QuestSystem.cs:152-170`
  (bloc ITEMS de `TurnInQuest`)

**Interfaces :**
- Produit : `QuestRewardItem.requiredWeaponFamily : WeaponType` — consommé par la Tâche 9
  (précheck inventaire).
- Consomme : `WeaponType.Any` (déjà existant, `Data/Equipment/WeaponType.cs:56`),
  `WeaponTypeExtensions.GetStartingFamily()` (déjà existant, utilisé 10+ fois dans le projet avec
  exactement le motif `(player.equippedWeapon?.weaponType ?? WeaponType.UnArmed).GetStartingFamily()`
  — voir `Data/Skills/SkillBar.cs:402` pour un exemple identique).

- [ ] **Étape 1 : Ajouter le champ sur `QuestRewardItem`**

Dans la classe `QuestRewardItem`, juste avant la fermeture du bloc `[Header("— Gemmes & Runes —")]`
(avant la ligne `}`qui ferme la classe, ligne 97), ajouter :

```csharp

    [Header("— Filtre arme —")]
    [Tooltip("Any (défaut) = récompense universelle. Une famille précise = cette entrée n'est\n" +
             "accordée que si l'arme équipée du joueur appartient à cette famille au moment du\n" +
             "turn-in (WeaponType.GetStartingFamily(), même regroupement que la compat skill).\n" +
             "Aucun choix proposé au joueur — résolution automatique.")]
    public WeaponType requiredWeaponFamily = WeaponType.Any;
```

- [ ] **Étape 2 : Filtrer dans `TurnInQuest`**

Remplacer (lignes 152-170) :

```csharp
        // ── ITEMS ─────────────────────────────────────────────────
        if (quest.rewardItems != null && InventorySystem.Instance != null)
        {
            foreach (var reward in quest.rewardItems)
            {
                if (reward == null) continue;
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
```

par :

```csharp
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
```

- [ ] **Étape 3 : Calculer `eligibleRewards` en tête de `TurnInQuest`**

Dans `TurnInQuest`, juste après le bloc qui vérifie l'état `Completed` (donc avant la ligne
`_states[quest.questID] = QuestState.TurnedIn;`), ajouter :

```csharp

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
```

- [ ] **Étape 4 : Relecture de sanité**

Vérifier : `eligibleRewards` est déclaré avant son usage dans le bloc ITEMS (Étape 2) — les deux
insertions doivent cohabiter dans le même ordre que `TurnInQuest` les traverse (calcul en tête,
utilisation plus bas). `List<QuestRewardItem>` est déjà valide — `System.Collections.Generic`
est déjà importé en tête de `QuestSystem.cs` (ligne 2).

- [ ] **Étape 5 : Commit**

```bash
git add Data/Quests/QuestData.cs Systems/QuestSystem.cs
git commit -m "feat: QuestRewardItem.requiredWeaponFamily — récompense résolue automatiquement par famille d'arme"
```

---

### Tâche 9 : Précheck inventaire au turn-in

**Fichiers :**
- Modifier : `Systems/InventorySystem.cs:47` (près de `IsFull`), `Systems/QuestSystem.cs`
  (`TurnInQuest`, juste après le bloc ajouté à la Tâche 8 Étape 3)

**Interfaces :**
- Produit : `InventorySystem.EmptySlotCount : int` (propriété publique).
- Consomme : `eligibleRewards` (Tâche 8).

- [ ] **Étape 1 : Ajouter `EmptySlotCount`**

Dans `InventorySystem.cs`, juste après la ligne `public bool IsFull => _items.Count >= MAX_SLOTS;`
(ligne 47), ajouter :

```csharp
    public int EmptySlotCount => Mathf.Max(0, MAX_SLOTS - _items.Count);
```

- [ ] **Étape 2 : Précheck dans `TurnInQuest`**

Dans `Systems/QuestSystem.cs`, juste après le bloc de warning ajouté à la Tâche 8 Étape 3 (donc
toujours avant `_states[quest.questID] = QuestState.TurnedIn;`), ajouter :

```csharp

        if (InventorySystem.Instance != null && InventorySystem.Instance.EmptySlotCount < eligibleRewards.Count)
        {
            Debug.LogWarning($"[QUEST] {quest.questName} : inventaire insuffisant " +
                $"({InventorySystem.Instance.EmptySlotCount} emplacement(s) libre(s), " +
                $"{eligibleRewards.Count} récompense(s) à octroyer) — turn-in refusé, rien n'est accordé.");
            return false;
        }
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : ce refus intervient AVANT tout octroi (XP/Aeris/Prestige/Items) et avant le passage à
l'état `TurnedIn` — un refus ici laisse la quête dans l'état `Completed`, rejouable au prochain
essai avec plus de place. Correspond exactement au comportement demandé par le spec ("refuse
proprement... AVANT d'accorder quoi que ce soit").

- [ ] **Étape 4 : Vérification manuelle Play Mode**

Florian remplit l'inventaire jusqu'à `MAX_SLOTS - 1`, turn-in une quête à 2 items de récompense →
doit refuser proprement, aucun item perdu, log clair, la quête reste `Completed` (scénario 10 du
spec).

- [ ] **Étape 5 : Commit**

```bash
git add Systems/InventorySystem.cs Systems/QuestSystem.cs
git commit -m "feat: précheck EmptySlotCount avant tout octroi de récompense au turn-in"
```

---

### Tâche 10 : `AbandonQuest` + `QuestAction.Abandoned`

**Fichiers :**
- Modifier : `Events/GameEvents.cs:196` (enum `QuestAction`), `Systems/QuestSystem.cs` (nouvelle
  méthode, après `TurnInQuest`)

**Interfaces :**
- Produit : `QuestSystem.AbandonQuest(QuestData quest, Player player) : bool` — consommé par la
  Tâche 11 (bouton Abandonner).

- [ ] **Étape 1 : Étendre `QuestAction`**

Remplacer (ligne 196) :

```csharp
public enum QuestAction { Accepted, ObjectiveUpdated, Completed, TurnedIn, Failed }
```

par :

```csharp
public enum QuestAction { Accepted, ObjectiveUpdated, Completed, TurnedIn, Failed, Abandoned }
```

(append-only — `Abandoned` prend l'ordinal suivant disponible, 5, sans décaler aucune des valeurs
existantes.)

- [ ] **Étape 2 : Ajouter `AbandonQuest`**

Dans `Systems/QuestSystem.cs`, juste après la fermeture de `TurnInQuest` (après la ligne `}` qui
suit la ligne 181), ajouter :

```csharp

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
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : `_activeData.Remove(quest.questID)` est appelé APRÈS `quest.ResetProgress()` (qui a
besoin que `quest.objectives` reste accessible — `ResetProgress` opère sur le `QuestData` lui-même,
pas sur `_activeData`, donc l'ordre n'a pas d'impact fonctionnel ici, mais le garder dans cet
ordre reste plus lisible).

- [ ] **Étape 4 : Commit**

```bash
git add Events/GameEvents.cs Systems/QuestSystem.cs
git commit -m "feat: QuestSystem.AbandonQuest + QuestAction.Abandoned"
```

---

### Tâche 11 : `QuestJournalUI.cs` — bouton Abandonner

**Fichiers :**
- Modifier : `UI/PanelSecondaire/QuestJournalUI.cs:23-37` (section Header Inspector), `:66-72`
  (`Start`), `:246-262` (`ShowDetail`)

**Interfaces :**
- Consomme : `QuestSystem.Instance.AbandonQuest(QuestData, Player)` (Tâche 10),
  `QuestSystem.Instance.GetQuestState(QuestData)` (déjà public).

- [ ] **Étape 1 : Ajouter le champ Inspector**

Dans la section `[Header("Détail — PanelRight")]` (après la ligne 33 `public GameObject
questDetailPrefab;`), ajouter :

```csharp

    [Header("Actions")]
    [Tooltip("Visible uniquement quand la quête sélectionnée est Active (masqué sinon) —\n" +
             "assigner le GameObject Bouton dans le prefab de détail.")]
    public Button abandonButton;
```

- [ ] **Étape 2 : Wirer le clic dans `Start`**

Remplacer (ligne 68) :

```csharp
        closeButton?.onClick.AddListener(Close);
```

par :

```csharp
        closeButton?.onClick.AddListener(Close);
        abandonButton?.onClick.AddListener(OnAbandonClicked);
```

- [ ] **Étape 3 : Ajouter `OnAbandonClicked` et basculer la visibilité dans `ShowDetail`**

Après la méthode `OnSuiviClicked` (après la ligne 240 `}`), ajouter :

```csharp

    private void OnAbandonClicked()
    {
        if (_selectedQuest == null || QuestSystem.Instance == null) return;
        if (QuestSystem.Instance.GetQuestState(_selectedQuest) != QuestState.Active) return;

        var player = FindObjectOfType<Player>();
        if (!QuestSystem.Instance.AbandonQuest(_selectedQuest, player)) return;

        _selectedQuest = null;
        ClearDetail();
        RefreshList();
    }
```

Puis, dans `ShowDetail` (ligne 246-262), juste après la ligne qui remplit `_detailDescription`
(avant `RefreshObjectifsText(quest);`), ajouter :

```csharp

        if (abandonButton != null)
            abandonButton.gameObject.SetActive(QuestSystem.Instance?.GetQuestState(quest) == QuestState.Active);
```

- [ ] **Étape 4 : Relecture de sanité**

Vérifier : `ClearDetail()` (existant, ligne 379) remet déjà `_selectedQuest = null` — l'appel
redondant dans `OnAbandonClicked` est inoffensif (même valeur assignée deux fois), gardé pour la
clarté de lecture.

- [ ] **Étape 5 : Note pour Florian (hors code)**

Le `Button` réel doit être glissé dans le champ `abandonButton` sur le prefab `questDetailPrefab`
dans l'Inspector Unity — aucun GameObject n'est créé par ce plan, seul le câblage C# est en
place. Si aucun bouton n'est assigné, `abandonButton?.` ne fait simplement rien (pas d'erreur).

- [ ] **Étape 6 : Vérification manuelle Play Mode**

Florian recompile, assigne le bouton dans le prefab, accepte une quête, l'abandonne depuis le
journal → vérifie qu'elle redevient proposable (scénario 8 du spec).

- [ ] **Étape 7 : Commit**

```bash
git add UI/PanelSecondaire/QuestJournalUI.cs
git commit -m "feat: QuestJournalUI — bouton Abandonner sur une quête active"
```

---

### Tâche 12 : Sauvegarde par `objectiveID` au lieu de l'index positionnel

**Fichiers :**
- Modifier : `Systems/QuestSystem.cs:316-358` (`GetSaveData`/`LoadSaveData`), `:399-403`
  (`QuestSaveEntry`), `Progression/Save/CharacterProgress.cs:188-193` (`SavedQuest`),
  `Progression/Save/SaveSystem.cs:601-610` (`CollectQuests`), `:932-941` (chargement)

**Interfaces :**
- Consomme : `QuestObjective.objectiveID` (Tâche 5).
- Produit : `QuestSaveEntry.objectiveEntries : List<QuestObjectiveSaveEntry>` (remplace
  `objectiveCounts : List<int>`), `SavedQuest.objectiveEntries : List<SavedQuestObjective>`
  (même remplacement côté `CharacterProgress`).

**Décision actée pour ce chantier (ruling)** : ce changement renomme ET retype le champ de
sauvegarde par objectif (`objectiveCounts: List<int>` → `objectiveEntries:
List<QuestObjectiveSaveEntry>`). `JsonUtility` (confirmé utilisé par `SaveSystem.cs`, lignes
178/207/230/261) ignore silencieusement une clé JSON sans champ C# correspondant au chargement :
une sauvegarde ANTÉRIEURE à ce commit verra donc, à son premier chargement après la mise à jour,
les compteurs de progression des quêtes encore `Active` réinitialisés à 0 (le champ `state` lui
n'est pas affecté — `Active`/`Completed`/`TurnedIn` restent corrects). Accepté tel quel pour ce
chantier : contenu de test uniquement à ce stade du projet, effet cosmétique et limité à UN
SEUL chargement (la sauvegarde suivante écrit déjà le nouveau format). Aucune migration
automatique n'est construite.

- [ ] **Étape 1 : Nouvelles structures de sauvegarde dans `QuestSystem.cs`**

Remplacer (lignes 399-403) :

```csharp
[System.Serializable]
public class QuestSaveEntry
{
    public string     questID = "";
    public QuestState state   = QuestState.None;
    public List<int>  objectiveCounts = new List<int>();
}
```

par :

```csharp
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
```

- [ ] **Étape 2 : Réécrire `GetSaveData`**

Remplacer (lignes 316-331) :

```csharp
    /// <summary>Retourne les données de sauvegarde (état + compteurs).</summary>
    public List<QuestSaveEntry> GetSaveData()
    {
        var list = new List<QuestSaveEntry>();
        foreach (var kvp in _states)
        {
            var entry = new QuestSaveEntry { questID = kvp.Key, state = kvp.Value };

            if (_activeData.TryGetValue(kvp.Key, out var quest))
            {
                foreach (var obj in quest.objectives)
                    entry.objectiveCounts.Add(obj.currentCount);
            }
            list.Add(entry);
        }
        return list;
    }
```

par :

```csharp
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
```

- [ ] **Étape 3 : Réécrire `LoadSaveData`**

Remplacer (lignes 333-358) :

```csharp
    /// <summary>Restaure l'état depuis la sauvegarde.</summary>
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
                    for (int i = 0; i < entry.objectiveCounts.Count && i < quest.objectives.Count; i++)
                        quest.objectives[i].currentCount = entry.objectiveCounts[i];
                }
            }
        }
    }
```

par :

```csharp
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
```

- [ ] **Étape 4 : Même remplacement côté `CharacterProgress.cs`**

Remplacer (`Progression/Save/CharacterProgress.cs:188-193`) :

```csharp
public class SavedQuest
{
    public string     questID;
    public QuestState state;
    public List<int>  objectiveCounts = new List<int>();
}
```

par :

```csharp
[System.Serializable]
public class SavedQuestObjective
{
    public string objectiveID;
    public int    currentCount;
}

public class SavedQuest
{
    public string     questID;
    public QuestState state;
    public List<SavedQuestObjective> objectiveEntries = new List<SavedQuestObjective>();
}
```

(Garder le `[System.Serializable]` déjà présent au-dessus de l'ancienne classe `SavedQuest` dans
le fichier réel — ne pas le dupliquer, seulement l'ajouter devant la nouvelle classe
`SavedQuestObjective`.)

- [ ] **Étape 5 : Mettre à jour `CollectQuests` dans `SaveSystem.cs`**

Remplacer (lignes 601-610) :

```csharp
    private List<SavedQuest> CollectQuests()
    {
        var entries = QuestSystem.Instance.GetSaveData();
        var result  = new List<SavedQuest>();
        foreach (var e in entries)
            result.Add(new SavedQuest {
                questID = e.questID, state = e.state,
                objectiveCounts = e.objectiveCounts });
        return result;
    }
```

par :

```csharp
    private List<SavedQuest> CollectQuests()
    {
        var entries = QuestSystem.Instance.GetSaveData();
        var result  = new List<SavedQuest>();
        foreach (var e in entries)
        {
            var saved = new SavedQuest { questID = e.questID, state = e.state };
            foreach (var o in e.objectiveEntries)
                saved.objectiveEntries.Add(new SavedQuestObjective
                {
                    objectiveID  = o.objectiveID,
                    currentCount = o.currentCount
                });
            result.Add(saved);
        }
        return result;
    }
```

- [ ] **Étape 6 : Mettre à jour le chargement dans `SaveSystem.cs`**

Remplacer (lignes 932-941) :

```csharp
        if (QuestSystem.Instance != null && p.quests != null && p.quests.Count > 0)
        {
            var allQuests = FindAllQuests();
            var entries   = new List<QuestSaveEntry>();
            foreach (var q in p.quests)
                entries.Add(new QuestSaveEntry {
                    questID = q.questID, state = q.state,
                    objectiveCounts = q.objectiveCounts });
            QuestSystem.Instance.LoadSaveData(entries, allQuests);
        }
```

par :

```csharp
        if (QuestSystem.Instance != null && p.quests != null && p.quests.Count > 0)
        {
            var allQuests = FindAllQuests();
            var entries   = new List<QuestSaveEntry>();
            foreach (var q in p.quests)
            {
                var entry = new QuestSaveEntry { questID = q.questID, state = q.state };
                if (q.objectiveEntries != null)
                    foreach (var o in q.objectiveEntries)
                        entry.objectiveEntries.Add(new QuestObjectiveSaveEntry
                        {
                            objectiveID  = o.objectiveID,
                            currentCount = o.currentCount
                        });
                entries.Add(entry);
            }
            QuestSystem.Instance.LoadSaveData(entries, allQuests);
        }
```

- [ ] **Étape 7 : Relecture de sanité**

Vérifier : les 4 fichiers touchés (`QuestSystem.cs`, `CharacterProgress.cs`, `SaveSystem.cs` ×2
sites) utilisent tous le même nom de champ `objectiveEntries` et les mêmes noms de type
(`QuestObjectiveSaveEntry` côté `QuestSystem.cs`, `SavedQuestObjective` côté
`CharacterProgress.cs` — DEUX classes distinctes et c'est voulu, même motif que
`QuestSaveEntry`/`SavedQuest` déjà existant : `QuestSystem.cs` ne doit pas dépendre de
`CharacterProgress.cs`, c'est `SaveSystem.cs` qui fait le pont entre les deux).

- [ ] **Étape 8 : Vérification manuelle Play Mode**

Florian recompile. Accepte une quête, avance un objectif, sauvegarde/recharge → progression
restaurée correctement, y compris après avoir réordonné les objectifs dans l'asset entre les deux
(scénario 11 du spec — test spécifique de ce fix).

- [ ] **Étape 9 : Commit**

```bash
git add Systems/QuestSystem.cs Progression/Save/CharacterProgress.cs Progression/Save/SaveSystem.cs
git commit -m "refactor: sauvegarde de la progression de quête par objectiveID au lieu de l'index positionnel"
```

---

### Tâche 13 : Migration manuelle des 2 assets de test

**Fichiers :**
- Modifier : `Content/Entity/PNJ/PNJ_quest_test_1/quest_braven_chicken_test.asset`,
  `Content/Entity/PNJ/PNJ_quest_test_2/quest_braven_loup_test.asset`

**Interfaces :**
- Consomme : `QuestData.requirements` (Tâche 2), `QuestObjective.objectiveID` (Tâche 5).

Les deux assets actuels ont `minLevel: 1` et `prerequisiteQuest: {fileID: 0}` (vide) — aucune
vraie chaîne de prérequis n'existe entre eux aujourd'hui (vérifié en lisant les deux fichiers
avant d'écrire ce plan). La migration est donc une simple substitution structurelle, sans
prérequis réel à reconstituer.

- [ ] **Étape 1 : `quest_braven_chicken_test.asset`**

Remplacer (lignes 19-20) :

```yaml
  minLevel: 1
  prerequisiteQuest: {fileID: 0}
```

par :

```yaml
  requirements:
    requirements: []
    requireAny: 0
    reverseMatch: 0
```

Puis ajouter `objectiveID:` sur chacun des 2 objectifs (juste après la ligne `type:` de chaque
entrée) :

```yaml
  objectives:
  - description: Il faut tuer 5 poules
    groupID: 1
    type: 0
    objectiveID: obj_kill_poules
    targetMob: {fileID: 11400000, guid: 5cb71766d12df7c41949c5fcbfd502d2, type: 2}
    targetPNJ: {fileID: 0}
    targetItem: {fileID: 0}
    targetZoneID: 
    requiredCount: 5
    currentCount: 0
  - description: parler au pnj pour continuer
    groupID: 2
    type: 1
    objectiveID: obj_talkto_pnj
    targetMob: {fileID: 0}
    targetPNJ: {fileID: 11400000, guid: e82c4ac6f47e4fb4797a8bfc28e4835d, type: 2}
    targetItem: {fileID: 0}
    targetZoneID: 
    requiredCount: 1
    currentCount: 0
```

- [ ] **Étape 2 : `quest_braven_loup_test.asset`**

Remplacer (lignes 19-20) :

```yaml
  minLevel: 1
  prerequisiteQuest: {fileID: 0}
```

par :

```yaml
  requirements:
    requirements: []
    requireAny: 0
    reverseMatch: 0
```

Puis ajouter `objectiveID:` sur chacun des 2 objectifs :

```yaml
  objectives:
  - description: Il faut tuer 5 loup
    groupID: 1
    type: 0
    objectiveID: obj_kill_loups
    targetMob: {fileID: 11400000, guid: 2a48c7ac294896b418e090dd73e2df4e, type: 2}
    targetPNJ: {fileID: 0}
    targetItem: {fileID: 0}
    targetZoneID: 
    requiredCount: 5
    currentCount: 5
  - description: parler au pnj pour terminer
    groupID: 2
    type: 1
    objectiveID: obj_talkto_pnj
    targetMob: {fileID: 0}
    targetPNJ: {fileID: 11400000, guid: 604d726bad7912243b275e5976f7e6bf, type: 2}
    targetItem: {fileID: 0}
    targetZoneID: 
    requiredCount: 1
    currentCount: 1
```

- [ ] **Étape 3 : Relecture de sanité**

Vérifier : l'indentation YAML des nouvelles clés `requirements`/`objectiveID` correspond
exactement à celle des clés voisines existantes (2 espaces par niveau, cohérent avec tout le
fichier). Aucune autre ligne des 2 fichiers n'est modifiée (champs `rewardItems`, `xpReward`,
etc. inchangés).

- [ ] **Étape 4 : Vérification manuelle Play Mode**

Florian ouvre les deux assets dans l'Inspector Unity après recompilation — aucune erreur de
désérialisation, le bloc "Prérequis" affiche la nouvelle UI `RequirementSet` (liste vide), les
objectifs affichent leur `objectiveID`.

- [ ] **Étape 5 : Commit**

```bash
git add Content/Entity/PNJ/PNJ_quest_test_1/quest_braven_chicken_test.asset Content/Entity/PNJ/PNJ_quest_test_2/quest_braven_loup_test.asset
git commit -m "chore: migre les 2 quêtes de test vers RequirementSet + objectiveID"
```

---

### Tâche 14 : Vérification finale (Play Mode, Florian)

Aucune modification de code — cette tâche consiste à exécuter, dans l'ordre, les 11 scénarios de
la section Vérification du spec (déjà distribués un par un dans les tâches ci-dessus, regroupés
ici pour une passe de bout en bout après que tout le chantier est commité) :

- [ ] 1. Compiler, 0 erreur.
- [ ] 2. `Requirement` de type Level (ex. niveau 5) — non proposable avant, proposable après.
- [ ] 3. `Requirement` de type QuestState (quête B nécessite quête A `TurnedIn`) — chaîne
  identique à l'ancien `prerequisiteQuest`.
- [ ] 4. Objectif Gather — ramasser l'item ciblé, la progression avance.
- [ ] 5. Objectif Explore — entrer dans la zone ciblée, la progression avance.
- [ ] 6. Objectif Craft — crafter la recette ciblée, la progression avance.
- [ ] 7. Objectif Boss — tuer le boss ciblé compte comme Kill ; un `targetMob` non-boss déclenche
  le warning `OnValidate`.
- [ ] 8. Accepter une quête, l'abandonner depuis le journal — redevient proposable (sauf
  dépendance QuestState d'une autre quête).
- [ ] 9. Récompense à 2 variantes par famille d'arme — turn-in avec les deux familles équipées,
  seule la bonne variante est reçue.
- [ ] 10. Inventaire à `MAX_SLOTS - 1`, turn-in 2 items de récompense — refus propre, aucun item
  perdu, log clair.
- [ ] 11. Accepter une quête, avancer un objectif, sauvegarder/recharger, y compris après avoir
  réordonné les objectifs dans l'asset entre les deux — progression restaurée correctement.

Aucun commit associé à cette tâche — purement une checklist de validation manuelle.
