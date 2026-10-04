# Refonte du système de quêtes — prérequis, objectifs, récompenses

## Contexte

Un audit complet (2026-10-04, comparaison avec AnyRPG — voir
`[[project_aethertree_quest_dialogue_audit]]`) a révélé que le système de quêtes actuel a trois
faiblesses structurelles, pas de simples bugs isolés :

1. **Objectifs morts** — `QuestObjectiveType` déclare 7 valeurs (Kill/TalkTo/Deliver/Gather/
   Explore/Craft/Boss), sélectionnables dans l'Inspector, affichées correctement dans
   `QuestJournalUI` — mais `QuestSystem.cs` n'incrémente QUE Kill et TalkTo. Une quête "récolte
   5 herbes" aujourd'hui aurait une barre de progression qui ne bouge jamais.
2. **Gating minimal** — `QuestData.CanAccept` (en réalité `QuestSystem.CanAccept`) ne gère que
   `minLevel` + un seul `prerequisiteQuest` en chaîne. Pas de composition AND/OR/NOT, pas de
   condition de rang de prestige, d'item possédé, de réputation.
3. **Récompenses rigides** — `rewardItems` est une liste à octroi inconditionnel. Florian veut
   qu'une entrée de récompense puisse varier selon `player.equippedWeapon.weaponType` (donner la
   variante d'objet qui correspond à l'arme du joueur), sans jamais lui demander de choisir —
   résolution automatique.

Florian, en validant l'ampleur du chantier : *"il faut qu'on repense à tout depuis le départ :
Prérequis (min level, quête, prestige min, etc.), Objectifs (kill, talkto etc.) et les
récompenses (suivant player.weapontype ou non)"*. Ce n'est donc pas un patch des 3 bugs
identifiés par l'audit — c'est une refonte des trois sous-systèmes de `QuestData`.

**Bonne nouvelle trouvée en creusant avant d'écrire cette spec** : la plupart des événements
nécessaires pour les objectifs "morts" existent déjà, publiés mais jamais écoutés par
`QuestSystem.cs` : `ItemEvent` (`ItemAction.Pickup`, pour Gather), `ZoneEvent` (pour Explore),
`RecipeCraftedEvent` (pour Craft). Ce chantier est donc surtout du branchement, pas de l'infra à
inventer — moins risqué que l'audit ne le laissait penser.

## Design

### 1. Prérequis — `RequirementSet` (nouvel objet composable réutilisable)

Remplace `QuestData.minLevel`/`prerequisiteQuest` par un objet composable AND/OR/NOT, même
principe que la section 1 du brainstorm panel-PNJ (mis en pause pour ce chantier, repris plus
tard — cet objet lui sera directement réutilisable pour le gating de dialogue à ce moment-là,
zéro travail perdu). Nommé `RequirementSet`/`Requirement` — volontairement PAS `PNJCondition*`
(plus PNJ-spécifique maintenant) ni rien contenant `Condition*` (collision de nom avec le système
d'achievement existant `Progression/Conditions/ConditionData.cs` — différent, événementiel,
sans rapport).

**4 types, pas 5** — un "Reputation" par faction était prévu dans le brainstorm initial (section 1
du panel PNJ) mais `Player` ne stocke PAS de score par faction (vérifié dans `Player.cs` en
écrivant cette spec) : seul `prestigeRank` (int) existe, `FactionType` est un enum sans rien pour
le scorer. Reputation aurait été redondant avec PrestigeRank — retiré avant d'écrire du code non
fonctionnel.

```csharp
public enum RequirementField { Level, PrestigeRank, QuestState, ItemOwned }

[Serializable]
public class Requirement
{
    public RequirementField field;
    [ShowIf(nameof(field), RequirementField.Level)]        public int minLevel;
    [ShowIf(nameof(field), RequirementField.PrestigeRank)] public int minPrestigeRank;
    [ShowIf(nameof(field), RequirementField.QuestState)]   public QuestData quest;
    [ShowIf(nameof(field), RequirementField.QuestState)]   public QuestState requiredState; // enum déjà existant dans QuestSystem.cs
    [ShowIf(nameof(field), RequirementField.ItemOwned)]    public ItemData item;
    [ShowIf(nameof(field), RequirementField.ItemOwned)]    public int itemCount = 1;

    public bool IsMet(Player player) { /* switch sur field */ }
}

[Serializable]
public class RequirementSet
{
    public List<Requirement> requirements = new();
    public bool requireAny;   // false = AND, true = OR
    public bool reverseMatch; // NOT sur le résultat final
    public bool IsMet(Player player) { /* vide = toujours vrai, sinon all/any + NOT */ }
}
```

Fichier : `Data/Requirements/RequirementSet.cs` (nouveau dossier — ni `Progression/Conditions/`
pour éviter la confusion avec `ConditionData`, ni un dossier spécifique à Quest puisque réutilisé
par le panel PNJ plus tard).

`QuestData` gagne `public RequirementSet requirements = new();` à la place de `minLevel`/
`prerequisiteQuest`. `QuestSystem.CanAccept(QuestData quest, Player player)` devient
`quest.requirements.IsMet(player) && état actuel != Active/TurnedIn` (le check d'état reste du
ressort de `QuestSystem`, pas de `RequirementSet` — une quête ne doit jamais se re-proposer déjà
active/terminée, indépendamment de ses prérequis).

**Migration** : `minLevel`/`prerequisiteQuest` retirés de `QuestData`. Les 2 assets de test
existants (`quest_braven_chicken_test.asset`, `quest_braven_loup_test.asset`) seront reconfigurés
à la main avec un `Requirement` de type Level équivalent — contenu `_test` jetable, pas de
contenu réel à préserver (vérifié).

### 2. Objectifs — type consolidé + câblage réel

**Deliver fusionné dans Gather** — même mécanique exacte ("posséder N de l'item X"), zéro asset
existant n'utilise l'ordinal 2 (vérifié par grep direct sur le YAML avant d'écrire cette spec,
pas supposé). Ordinal 2 devient un trou commenté, même convention que les retraits précédents
dans ce fichier.

**Boss ne devient PAS un type séparé fonctionnellement** — `MobData.IsBoss()` existe déjà
(`mobType == WorldBoss/EventGiantBoss`, ou `DungeonRole.Boss`/`InvasionRole.Boss` selon le
contexte). Kill et Boss partagent le MÊME câblage (`MobKilledEvent`, match sur `targetMob`
exact) — Boss reste un type d'enum séparé pour l'affichage/la clarté de conception ("Tue CE
boss" vs "Tue 5 de ces mobs"), avec un warning `OnValidate` si un objectif Boss pointe vers un
`targetMob` dont `IsBoss() == false` (probable erreur de configuration).

```csharp
public enum QuestObjectiveType { Kill = 0, TalkTo = 1, /* 2 retiré, Deliver fusionné dans Gather */ Gather = 3, Explore = 4, Craft = 5, Boss = 6 }
```

**Câblage dans `QuestSystem.cs`** (nouveaux handlers, même schéma que `HandleMobKilled`/
`NotifyTalkTo` déjà en place) :

- **Gather** : écoute `ItemEvent` filtré sur `action == ItemAction.Pickup`. Match par
  `((ItemData)targetItem).itemID == e.itemID` — `ItemData` (`Data/ItemData.cs`) est déjà la
  classe de base commune WeaponData/ArmorData/ResourceData/ConsumableData/..., avec un `itemID`
  stable fait exactement pour ça. `targetItem` reste un champ `ScriptableObject` générique sur
  `QuestObjective` (pas besoin de le retyper en `ItemData` — juste caster au moment du match, un
  item qui ne dérive pas d'`ItemData`, comme Gem/Rune aujourd'hui, ne pourra simplement pas être
  ciblé par un objectif Gather, cohérent avec l'étendue actuelle d'`ItemData`).
- **Explore** : écoute `ZoneEvent`. Match par `e.zoneID == obj.targetZoneID`, incrémente sur
  CHAQUE event reçu pour cette zone (première entrée suffit à valider dans la plupart des cas vu
  que `requiredCount` vaudra 1 pour ce type — pas besoin de filtrer sur `isFinalExit`, qui sert à
  un autre usage côté `ZoneChecker`/achievements).
- **Craft** : écoute `RecipeCraftedEvent`. Match par comparaison entre `targetItem` et l'item de
  sortie de `e.recipe` — **exact champ de sortie de `RecipeData` à confirmer en lisant le fichier
  réel pendant le plan** (pas lu dans cette spec, à ne pas deviner).
- **Boss** : fusionné dans le handler Kill existant, voir ci-dessus — aucun nouveau handler.

### 3. Récompenses — variante par famille d'arme

Nouveau champ sur `QuestRewardItem` :

```csharp
[Tooltip("Any (défaut) = récompense universelle. Une famille précise = cette entrée n'est\n" +
         "accordée que si l'arme équipée du joueur appartient à cette famille au moment du\n" +
         "turn-in (WeaponType.GetStartingFamily(), même regroupement que la compat skill).\n" +
         "Aucun choix proposé au joueur — résolution automatique.")]
public WeaponType requiredWeaponFamily = WeaponType.Any;
```

`QuestSystem.TurnInQuest` filtre `rewardItems` : ne garde que les entrées dont
`requiredWeaponFamily == WeaponType.Any` OU dont
`requiredWeaponFamily == (player.equippedWeapon?.weaponType ?? WeaponType.UnArmed).GetStartingFamily()`
avant de les octroyer. `xpReward`/`aerisReward`/`prestigeReward` restent universels, non
concernés — seuls les items peuvent varier.

### 4. Correctifs opérationnels (déjà identifiés par l'audit, inclus dans cette refonte)

- **`AbandonQuest(QuestData, Player)`** — nouvelle méthode sur `QuestSystem`, n'existe nulle part
  aujourd'hui (vérifié par grep). Remet l'état à `None`, retire de `_activeData`, réinitialise la
  progression. `QuestAction` (`Events/GameEvents.cs`) gagne une valeur `Abandoned` en fin d'enum
  (ordinal safety — append-only, même convention que partout ailleurs dans ce projet).
  `QuestJournalUI` gagne un bouton "Abandonner" sur une quête active.
- **Précheck inventaire au turn-in** — `InventorySystem` gagne `public int EmptySlotCount` (même
  convention que `IsFull` déjà public). `TurnInQuest` vérifie `EmptySlotCount >=
  rewardItems.Count` (après filtrage par famille d'arme, voir section 3) AVANT d'accorder quoi
  que ce soit — refuse proprement (log + aucun octroi partiel) si insuffisant, comme AnyRPG.
- **Sauvegarde par clé stable, pas par index** — `QuestObjective` gagne un champ
  `objectiveID` (string, même convention que `pnjID`/`questID`/`itemID` — stable, jamais
  affiché). `QuestSaveEntry.objectiveCounts` passe de `List<int>` positionnel à une liste de
  paires `{objectiveID, currentCount}` — réordonner/insérer un objectif dans l'asset entre un
  save et un load ne corrompt plus silencieusement la progression restaurée (bug identifié par
  l'audit). Les assets de test existants devront avoir leurs objectifs remplis avec un
  `objectiveID` à la migration (vide aujourd'hui, nouveau champ).

## Comportement assumé / hors scope

- Pas de récompense au choix joueur (pick N parmi M, façon AnyRPG) — explicitement écarté par
  Florian pour ce chantier.
- Pas de vrais "steps" imbriqués façon AnyRPG — le modèle actuel (liste plate + `groupID` pour
  les objectifs simultanés) est gardé, cohérent avec le reste du projet (une classe + enum +
  ShowIf plutôt que du polymorphisme `[SerializeReference]`).
- `RequirementSet` est scopé au gating de quête (`QuestData.requirements`) dans ce chantier —
  son usage côté sélection de dialogue PNJ (section 1 du brainstorm panel-PNJ, mis en pause)
  reste à brancher lors de la reprise de ce chantier séparé, pas touché ici.
- `QuestData.ResetProgress()` continue de muter le ScriptableObject partagé plutôt qu'un état par
  joueur séparé (gap mineur de l'audit, inoffensif en solo — pas dans ce chantier).

## Fichiers touchés

- `Data/Requirements/RequirementSet.cs` — nouveau fichier, `RequirementField`/`Requirement`/
  `RequirementSet`.
- `Data/Quests/QuestData.cs` — retire `minLevel`/`prerequisiteQuest`, ajoute `requirements`
  (`RequirementSet`) ; `QuestObjectiveType` (Deliver retiré, ordinal 2 en trou commenté) ;
  `QuestObjective` gagne `objectiveID` ; `QuestRewardItem` gagne `requiredWeaponFamily`.
- `Systems/QuestSystem.cs` — `CanAccept` utilise `RequirementSet.IsMet` ; nouveaux handlers
  Gather/Explore/Craft ; `AbandonQuest` ; précheck inventaire dans `TurnInQuest` ; filtrage
  récompense par famille d'arme ; sauvegarde par `objectiveID` au lieu de l'index positionnel.
- `Events/GameEvents.cs` — `QuestAction` gagne `Abandoned`.
- `Systems/InventorySystem.cs` — nouveau `EmptySlotCount`.
- `UI/PanelSecondaire/QuestJournalUI.cs` — bouton Abandonner.
- 2 assets de test (`quest_braven_chicken_test.asset`, `quest_braven_loup_test.asset`) — migration
  manuelle `minLevel`/`prerequisiteQuest` → `Requirement`, `objectiveID` rempli.

Aucun changement dans `DialogueUI.cs`/`PNJ.cs` (le panel PNJ/sélection de dialogue reste un
chantier séparé, en pause).

## Vérification

Pas de framework de test automatisé — vérification manuelle en Play Mode par Florian :

1. Compiler, 0 erreur.
2. Créer/modifier une quête avec un `Requirement` de type Level (ex: niveau 5 minimum) — vérifier
   qu'elle n'est pas proposable avant, l'est après.
3. `Requirement` de type QuestState (quête B nécessite quête A `TurnedIn`) — vérifier le
   comportement de chaîne identique à l'ancien `prerequisiteQuest`.
4. Quête avec un objectif Gather — ramasser l'item ciblé, vérifier que la progression avance.
5. Quête avec un objectif Explore — entrer dans la zone ciblée, vérifier la progression.
6. Quête avec un objectif Craft — crafter la recette ciblée, vérifier la progression.
7. Quête avec un objectif Boss — tuer le boss ciblé, vérifier que ça compte comme Kill
   normalement ; configurer par erreur un `targetMob` non-boss sur un objectif Boss → vérifier le
   warning `OnValidate`.
8. Accepter une quête, l'abandonner depuis le journal → vérifier qu'elle redevient proposable
   (ou pas, selon ses propres `Requirement` de type QuestState si une autre quête en dépendait).
9. Configurer une récompense avec 2 variantes par famille d'arme (ex: Epée si famille Épée,
   Bâton si famille Magie) — tester le turn-in avec les deux familles équipées, vérifier que
   seule la bonne variante est reçue.
10. Remplir l'inventaire jusqu'à MAX_SLOTS - 1, turn-in une quête à 2 items de récompense → doit
    refuser proprement, aucun item perdu, log clair.
11. Accepter une quête, avancer un objectif, sauvegarder/recharger — vérifier la progression
    restaurée correctement même après avoir réordonné les objectifs dans l'asset entre les deux
    (test spécifique du fix `objectiveID`).
