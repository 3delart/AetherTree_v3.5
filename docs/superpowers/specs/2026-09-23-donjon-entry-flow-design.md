# Flux d'entrée en donjon — portails gatés, triggers, panel groupe

**Statut** : Discuté et approuvé en chat avec Florian (2026-09-23), en attente de plan
d'implémentation.
**Repo** : AetherTree v3.5 (Unity C#, solo-dev, pas de framework de test automatisé —
vérification manuelle en Play Mode uniquement).
**Dépend de** : `docs/superpowers/specs/2026-09-23-instance-system-design.md` (système
`InstanceSession`/`IInstanceConfig`/`DungeonData`, déjà implémenté et shippé — commits
`44dacfc`..`8cee222`). Ce chantier **révise** une partie de ce qui était déjà câblé (voir §2).
**Référence design** : Florian compare explicitement ce flux aux raids de NosTale — chef qui
forme le groupe, entrée gatée, salles qui débloquent en séquence, panel raid avec HP/MP des
membres. C'est le modèle à suivre.

## 1. Contexte

Le chantier précédent (Instance System) a livré `InstanceSession`/`DungeonData`/
`CombatVagueData` et câblé la consommation de `ConsumableData.DungeonStone` pour appeler
`InstanceSession.Enter()` **directement** (`UI/PanelFixe/ConsoBarUI.cs`, commit `1d318d8`).
Florian précise maintenant le vrai flux voulu, à deux temps distincts :

1. Consommer la Pierre/Clé (peu importe son nom exact, "Sceau" évoqué mais confirmé être le
   même mécanisme générique — un `ConsumableData` de type `DungeonStone`) → le joueur devient
   **chef de groupe donjon**, un panel dédié s'ouvre. **Aucun chargement de scène ici.**
2. Le chef doit ensuite marcher jusqu'au portail d'entrée et le franchir — ce portail est
   **verrouillé par défaut**, ne s'ouvre que pour un chef avec une entrée valide en attente, et
   embarque tout le groupe avec lui. **C'est à ce moment que `InstanceSession.Enter()` doit être
   appelé**, pas à l'étape 1.

Florian a aussi noté qu'un donjon peut avoir **plusieurs scènes** (ex: Map 1 → Map 2 → Map Boss)
— chacune reliée par un portail interne, verrouillé jusqu'à ce qu'une condition soit remplie
dans le donjon (tuer un mini-boss, actionner un levier). C'est exactement le champ `lockedUntil:
DungeonTrigger` déjà présent dans le schéma `DungeonMapData` du GDD (§14.2.3) mais jamais
implémenté — `DungeonTrigger` lui-même n'a jamais été défini nulle part dans le GDD (juste
nommé, jamais détaillé), ce chantier le définit.

**Décision de conception (Florian, 2026-09-23)** : plutôt que d'inventer un composant de portail
séparé pour les donjons, **étendre `Events/Portal.cs`** (le portail existant, utilisé partout
dans le monde ouvert) avec un système de verrou composable — un seul type de portail dans tout
le jeu, verrouillé ou non selon sa configuration, réutilisable pour l'entrée en donjon, les
portails internes de donjon, ET les portails de palier (actuellement non gatés du tout, alors
que le GDD §14.2.2/§10.1 dit explicitement que l'accès au palier suivant est conditionné à la
réussite du Donjon de Déblocage).

## 2. Révision du chantier précédent

**`UI/PanelFixe/ConsoBarUI.cs`** — la branche `ConsumableType.DungeonStone` (commit `1d318d8`,
puis fix `8cee222`) appelle actuellement `InstanceSession.Instance.Enter(dungeon)` directement
et ne consomme l'item que si `true` est retourné. Ce chantier change ce qu'elle appelle :
`InstanceSession.Instance.ArmEntry(dungeon)` (nouvelle méthode, voir §4.3) à la place de
`Enter()` — la consommation de l'item reste gatée sur le retour bool de cet appel, la logique de
warning/cleanup ne change pas, seul le nom de la méthode appelée change.

`InstanceSession.Enter(IInstanceConfig)` (méthode déjà existante, déjà testée/review) **n'est
plus appelée par `ConsoBarUI`** — elle est appelée uniquement par le nouveau `Portal.cs` gaté
(§4.2), au moment du franchissement. Sa signature et son comportement interne ne changent pas.

## 3. Objectifs

- `Events/Portal.cs` — nouveau champ composable `PortalGateType` (défaut `None` = comportement
  actuel inchangé pour tous les portails existants du monde ouvert). Trois valeurs gatées :
  `RequiresDungeonEntry`, `RequiresTierUnlock`, `RequiresTrigger`.
- `InstanceSession` — nouvel état "entrée en attente" (`PendingInstance`/`Participants`) posé
  par la consommation de l'item, consommé par le franchissement du portail gaté.
- `DungeonTrigger` — petit système de condition intra-donjon (mob tué / levier actionné),
  tracké par run (reset à chaque `Enter()`, jamais persisté en sauvegarde).
- Panel groupe donjon (`DungeonGroupPanelUI` ou nom équivalent) — header (nom du donjon), corps
  (liste des participants avec HP/MP, couleur distincte pour le chef), footer (vies restantes).
  Visible dès l'entrée en attente posée, pas seulement une fois l'instance active.

## 4. Non-objectifs

- **Pas de vrai système Groupe/Parties multijoueur.** `Participants` reste une `List<Player>`
  qui ne contient jamais que le joueur local en solo — même principe que le chantier précédent
  (shape prêt, aucun réseau). Pas d'invitation, pas de gestion de membres.
- **Pas de contenu de donjon réel** — aucune vraie salle Map1/Map2/MapBoss, aucun vrai
  mini-boss/levier. Ce chantier livre le système (gate de portail générique, `DungeonTrigger`
  comme mécanisme, panel), le contenu est un chantier séparé plus tard dans la roadmap démo.
- **Pas de UX de blocage soignée** — un portail gaté non franchissable se contente d'un
  `Debug.Log`/`LogWarning` pour ce premier passage, pas de message à l'écran, pas de feedback
  visuel (porte qui secoue, son "verrouillé", etc.) — poli plus tard si besoin.
- **`RequiresTierUnlock` ne construit pas le flag `unlockedTier[N]` lui-même** — ce chantier
  suppose ce flag existe quelque part sur `Player`/`PlayerData` (à vérifier/ajouter si absent,
  voir §4.1) mais ne construit pas le Donjon de Déblocage qui doit un jour l'écrire ; ce chantier
  ne fait qu'ajouter la LECTURE de ce flag comme condition de portail.
- **`DungeonTrigger` reste minimal** — seulement `MobKilled`/`LeverActivated`, pas toute la
  richesse `List<DungeonTrigger> triggers` du GDD (portes, mécanismes complexes) — juste assez
  pour qu'un portail interne puisse être gaté par une condition simple.

## 4.1 Vérification préalable — `unlockedTier[N]`

Avant d'écrire le plan, vérifier si un flag de déblocage de palier existe déjà quelque part sur
`Player.cs`/`CharacterProgress.cs` (grep `unlockedTier`/`unlockedPalier`/similaire). D'après
l'audit GDD complet du 2026-09-23, aucun système de Donjon (classique ou déblocage) n'existe
encore dans le code — ce flag n'existe probablement pas non plus. Si absent, ce chantier ajoute
le champ minimal (`List<int> unlockedTiers` ou `bool[] unlockedTiers` sur `CharacterProgress`,
persisté comme le reste de la progression) — pas la logique qui l'écrit (ça viendra avec le
Donjon de Déblocage), juste le champ + sa lecture par `Portal.cs`.

## 5. Architecture

### 5.1 `Events/Portal.cs` — extension

```csharp
public enum PortalGateType
{
    None                  = 0, // Comportement actuel — libre, aucun changement pour l'existant
    RequiresDungeonEntry  = 1, // InstanceSession.PendingInstance valide pour CE portail
    RequiresTierUnlock    = 2, // Player.unlockedTiers contient targetTier
    RequiresTrigger       = 3, // InstanceSession.IsTriggerMet(requiredTriggerID) pendant un run actif
}
```

Nouveaux champs sur `Portal` (`[Header("Verrou")]`, `[ShowIf]` gaté par `gateType`) :
- `gateType` (`PortalGateType`, défaut `None`).
- `linkedInstanceID` (string) — `ShowIf(gateType, RequiresDungeonEntry)` — doit matcher
  `IInstanceConfig.InstanceID` du donjon attendu.
- `targetTier` (int) — `ShowIf(gateType, RequiresTierUnlock)`.
- `requiredTriggerID` (string) — `ShowIf(gateType, RequiresTrigger)`.

`OnTriggerEnter` (méthode existante) gagne un check AVANT de lancer `TeleportRoutine()` :

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
            return player.HasUnlockedTier(targetTier); // voir §4.1
        case PortalGateType.RequiresTrigger:
            return InstanceSession.Instance != null
                && InstanceSession.Instance.IsTriggerMet(requiredTriggerID);
        default:
            return true;
    }
}
```

Si `gateType == RequiresDungeonEntry` et `CanCross` est vrai, le franchissement ne doit PAS
suivre le chemin normal `StartCoroutine(TeleportRoutine())` (qui charge `targetMap` via
`SceneLoader.LoadMap` — un simple aller-retour point à point). À la place, il appelle
`InstanceSession.Instance.ConsumePendingEntry()` (§5.3), qui déclenche `Enter()` et charge la
VRAIE scène du donjon (`config.SceneName`, résolu depuis la `DungeonData`, pas depuis
`targetMap` du portail — ce champ du portail devient inutilisé pour ce gateType précis). Pour
`RequiresTierUnlock` et `RequiresTrigger`, le comportement reste un `TeleportRoutine()` normal
une fois `CanCross` vrai — seul `RequiresDungeonEntry` redirige la destination.

### 5.2 `DungeonTrigger` — nouveau petit fichier

```csharp
public enum DungeonTriggerType { MobKilled, LeverActivated }

[System.Serializable]
public class DungeonTrigger
{
    public string triggerID;           // référencé par Portal.requiredTriggerID
    public DungeonTriggerType triggerType;
    public MobData requiredMob;        // MobKilled uniquement — quel Mob doit mourir
}
```

Pas de `ScriptableObject` dédié — vit comme sous-champ de `DungeonMapData` (déjà sur
`DungeonData`, chantier précédent) : `DungeonMapData.triggers: List<DungeonTrigger>` (le champ
`triggers`/`lockedUntil` du GDD §14.2.3, jamais ajouté au premier passage — Non-objectifs de
l'ancien chantier l'excluait explicitement). Le déclenchement réel (quel `Mob.Die()` notifie
quel `triggerID`) est un câblage de contenu, hors scope ici — ce chantier fournit juste
`InstanceSession.NotifyTriggerMet(string triggerID)`/`IsTriggerMet(string)` comme mécanisme.

### 5.3 `InstanceSession` — nouvel état "entrée en attente" + triggers

```csharp
public IInstanceConfig PendingInstance { get; private set; }
public List<Player>    Participants    { get; private set; } = new List<Player>();
public bool            IsLeader        { get; private set; }

private HashSet<string> _metTriggerIDs = new HashSet<string>();

/// <summary>Posé par la consommation de l'item d'entrée (ConsoBarUI) — n'appelle PAS Enter(),
/// juste prépare l'état que le portail gaté consommera. Toujours [joueur local] en solo.</summary>
public bool ArmEntry(IInstanceConfig config)
{
    if (config == null) return false;
    PendingInstance = config;
    IsLeader        = true;
    Participants    = new List<Player> { FindObjectOfType<Player>() };
    return true;
}

/// <summary>Appelé par Portal (gateType = RequiresDungeonEntry) au moment du franchissement —
/// consomme l'entrée en attente et démarre réellement l'instance.</summary>
public bool ConsumePendingEntry()
{
    if (PendingInstance == null) return false;
    IInstanceConfig config = PendingInstance;
    PendingInstance = null;
    return Enter(config); // méthode existante, inchangée
}

public void NotifyTriggerMet(string triggerID)
{
    if (string.IsNullOrEmpty(triggerID)) return;
    _metTriggerIDs.Add(triggerID);
}

public bool IsTriggerMet(string triggerID) => _metTriggerIDs.Contains(triggerID);
```

`Enter()` (méthode existante, inchangée dans son corps) doit en plus vider `_metTriggerIDs` au
début de chaque run — un trigger d'un run précédent ne doit jamais rester "acquis" au suivant.

### 5.4 Panel groupe donjon — nouveau fichier UI

Nom exact à trancher au plan (`UI/PanelFixe/DungeonGroupPanelUI.cs`, suit la convention des
panels HUD permanents déjà en place — `PlayerInfosPanel`, `StatusEffectUI`, etc.). Le panel
racine reste caché (`gameObject.SetActive(false)`, pattern `[hidden]`/`ShowOnly` déjà utilisé
ailleurs dans le projet) tant que `InstanceSession.PendingInstance == null &&
InstanceSession.CurrentInstance == null` — poll simple en `Update()` (même idiome que
`ConsoBarUI`/`PlayerInfosPanel`, pas d'event dédié pour ce premier passage) ; se réaffiche/se
recache automatiquement en suivant cet état, y compris à la fin d'un run (`ExitAfterDelay()` met
`CurrentInstance = null`, ce qui doit re-cacher le panel).

- **Header** : `(PendingInstance ?? CurrentInstance).DisplayName`.
- **Corps** : une ligne par entrée de `Participants` — nom, HP/MP (lu directement sur `Player`,
  solo = une seule ligne), couleur distincte si `IsLeader` (toujours vrai en solo aujourd'hui).
- **Footer** : `CurrentInstance != null ? LivesRemaining : PendingInstance.LivesPerPlayer` (vies
  restantes une fois dans le run, aperçu de la config avant d'y être).

## 6. Vérification (Play Mode manuel)

1. Compiler, 0 erreur.
2. Créer un `DungeonData` de test avec DEUX `DungeonMapData` (`map_test_1`, `map_test_boss`),
   la deuxième avec `isBossRoom = true` et un `triggers` contenant un `DungeonTrigger`
   (`triggerID = "test_lever"`, `LeverActivated`).
3. Poser un `Portal` en Map_Entrée-de-test, `gateType = RequiresDungeonEntry`,
   `linkedInstanceID` = celui du DungeonData de test. Vérifier qu'il est infranchissable tant
   qu'aucune entrée n'est en attente (marcher dedans sans avoir consommé la pierre → rien ne se
   passe, log de refus).
4. Consommer la Pierre de test → vérifier `InstanceSession.PendingInstance` non-null, panel
   groupe donjon apparaît (header = nom du donjon test, 1 ligne = joueur, footer =
   `livesPerPlayer` de la config). Vérifier qu'AUCUN chargement de scène n'a eu lieu.
5. Franchir le portail → vérifier `Enter()` se déclenche réellement maintenant (scène chargée),
   `PendingInstance` redevient null, `CurrentInstance` est set, panel bascule sur
   `LivesRemaining`.
6. Poser un second `Portal` DANS `map_test_1` vers `map_test_boss`, `gateType =
   RequiresTrigger`, `requiredTriggerID = "test_lever"`. Vérifier infranchissable au départ.
   Appeler manuellement `InstanceSession.Instance.NotifyTriggerMet("test_lever")` (bouton test
   temporaire ou Inspector) → vérifier le portail devient franchissable.
7. Terminer le run (échec ou victoire) → vérifier `_metTriggerIDs` est vide au run suivant
   (retenter l'étape 6 sans re-notifier → doit à nouveau être bloqué).
