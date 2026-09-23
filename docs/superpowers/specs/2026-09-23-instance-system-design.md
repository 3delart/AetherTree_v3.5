# Instance System — sessions privées pour Donjons, Combat à Vague, Événements

**Statut** : Discuté et approuvé en chat avec Florian (2026-09-23), en attente de plan
d'implémentation.
**Repo** : AetherTree v3.5 (Unity C#, solo-dev, pas de framework de test automatisé —
vérification manuelle en Play Mode uniquement).
**Contexte GDD** : `AetherTree_GDD_v3.6(1).md` §14.2 (Donjon Classique), §14.3 (Donjon de
Déblocage), §14.5.1 (Combat Vague) — fichier hors du repo Scripts, à
`C:\AetherTree_v3.5\AetherTree_GDD_v3.6(1).md`.

## 1. Contexte

Le projet n'a aucun système d'instanciation aujourd'hui — `Events/SceneLoader.cs` gère un
chargement additif simple, une seule map active à la fois (`_currentMap`), sans notion de
session privée, de vies, ni de condition d'échec/réussite. Le prochain chantier de la roadmap
démo (Donjon Classique, Donjon de Déblocage, Combat à Vague, et plus tard Boss/Invasion/Arène
Équipe) a besoin d'une brique commune : "un joueur entre dans une activité avec des règles de
mort propres, en ressort par échec ou par victoire, récupère des récompenses."

Le GDD a déjà un schéma détaillé pour deux de ces activités (§14.2.3 `DungeonData SO`, et un
schéma `CombatVagueData SO` sous §14.5.1) — ce chantier codifie ce schéma plutôt que d'en
inventer un nouveau, et ajoute la couche runtime qui manque.

**Décision de portée (Florian, 2026-09-23)** : ce système est construit **solo/local
uniquement** pour la démo — pas de réseau, pas de matchmaking, pas de multi-instance concurrent.
L'architecture doit rester *prête* pour un futur passage multijoueur (ne pas coder en dur
"1 seul joueur possible"), mais aucun travail réseau n'est fait maintenant. Le "nombre
d'instances nécessaire selon le nombre de participants" (Combat à Vague, §14.5.1 — 20 joueurs
max/instance, plusieurs instances simultanées par tranche) reste un problème serveur pour plus
tard : sur un client solo, il n'y a jamais qu'une seule session active à la fois de toute façon.

**Règle de mort unifiée (Florian, 2026-09-23)** : Donjon Classique a des vies (`livesPerPlayer`,
défaut 2, GDD §14.2.3), Donjon de Déblocage GDD-en-vaut-1 (§14.3.3 "Vies individuelles : 1"),
Combat à Vague n'a explicitement aucune résurrection (§14.5.1 "Chaque mort est définitive").
Plutôt que 3 comportements différents à coder, une seule règle couvre les 3 cas : `livesRemaining
= livesPerPlayer ; à chaque mort, livesRemaining-- ; si > 0 → respawn au point de contrôle ;
sinon → expulsion (fin de run)`. Classique (2 vies) est le seul cas où le "respawn" arrive
jamais en pratique — Déblocage et Vagues, avec 1 vie, collapsent naturellement en "1 mort = fin"
sans cas spécial.

## 2. Objectifs

- `IInstanceConfig` — interface minimale que n'importe quel SO d'activité instanciée implémente
  (`DungeonData` aujourd'hui, `CombatVagueData` aujourd'hui, `ArenaTeamData` plus tard).
- `DungeonData` SO — codifie le schéma GDD §14.2.3 (Classique et Déblocage via un seul champ
  `dungeonType`), squelette de contenu (une salle par défaut, pas de contenu réel — le contenu
  de chaque donjon est un chantier séparé, voir Non-objectifs).
- `CombatVagueData` SO — codifie le schéma GDD §14.5.1 (vagues, timers, tranche), squelette de
  contenu.
- `InstanceSession` — composant runtime singleton (même patron que `SkillSystem`/
  `CombatAIController` : `SkillSystem.Instance`) qui pilote UNE session active à la fois : vies
  restantes, entrée/sortie, hook mort, hook boss-kill, distribution de récompense.
- Entrée/sortie de scène — réutilise `SceneLoader.LoadMap()`/`ReloadCurrentMap()` tel quel,
  aucune nouvelle mécanique de chargement de scène.
- Consommation de l'objet d'entrée — câble enfin `ConsumableData.DungeonStone` (existe déjà,
  jamais branché — voir `ConsoBarUI.cs` commentaire "pas encore câblés").

## 3. Non-objectifs

- **Pas de réseau/multijoueur réel** — pas de matchmaking, pas de routage joueur→instance
  serveur, pas de session partagée entre plusieurs clients. `InstanceSession` track un seul
  participant (`Player` local) aujourd'hui ; le champ est en forme de liste pour ne pas devoir
  réécrire l'API plus tard, mais rien ne consomme jamais plus d'un élément pour l'instant.
- **Pas de contenu de donjon/vague réel** — aucune salle, aucun mob, aucun boss n'est créé par ce
  chantier. Ce chantier livre le SYSTÈME ; le contenu (Donjon Classique Palier 1, Donjon de
  Déblocage P1→P2, les 5 vagues de Combat à Vague) est un chantier de contenu séparé, plus tard
  dans la roadmap démo.
- **Pas de mécanique spéciale de boss** — le boss de Donjon de Déblocage (seul des 5 boss de la
  roadmap démo à avoir une vraie mécanique, confirmé par Florian) sera designé/branché dans son
  propre chantier, pas ici. Ce chantier ne fait que poser le hook `OnBossKilled()` que ce futur
  boss appellera.
- **Pas de Map_Entrée (zone tampon publique)** — le GDD décrit une zone tampon non-instanciée
  entre le monde ouvert et le donjon (§14.2.2). Pour ce premier passage, l'entrée saute
  directement du monde ouvert à l'instance (consommer l'objet → `InstanceSession.Enter()`) — la
  zone tampon peut être ajoutée plus tard sans toucher `InstanceSession` (elle ne fait que
  déplacer QUAND le joueur consomme l'objet, pas comment la session fonctionne).
- **`deathLimit` (mort collective) n'a aucun effet observable en solo** — le champ reste sur
  `DungeonData` (fidèle au GDD, prêt pour le multijoueur), mais avec un seul joueur,
  `livesPerPlayer` détermine déjà la fin de run avant que `deathLimit` ne puisse jamais être
  pertinent. Pas de code mort à retirer, juste un champ qui ne fait rien d'observable pour
  l'instant.

## 4. Architecture

### 4.1 Nouveau fichier : `Combat/InstanceSession.cs` (ou `Systems/InstanceSession.cs`)

```csharp
public interface IInstanceConfig
{
    string InstanceID   { get; }   // dungeonID / eventID — snake_case, immuable
    string DisplayName  { get; }
    string SceneName    { get; }   // nom de la scène Unity à charger via SceneLoader
    string EntryItemID  { get; }   // ID du consommable requis pour entrer (Pierre / Clé)
    int    LivesPerPlayer { get; } // voir §1 — règle de mort unifiée
}

public enum InstanceOutcome { InProgress, Success, Failure }

public class InstanceSession : MonoBehaviour
{
    public static InstanceSession Instance { get; private set; }

    public IInstanceConfig CurrentInstance { get; private set; }
    public int  LivesRemaining { get; private set; }
    public InstanceOutcome CurrentOutcome { get; private set; } = InstanceOutcome.InProgress;

    // Point de respawn courant DANS l'instance — mis à jour à chaque salle/checkpoint franchi
    // (Donjon Classique : salle 1 fixe pour ce premier passage — pas de checkpoints multiples
    // tant que le contenu réel des salles n'existe pas, voir Non-objectifs).
    private Vector3 _respawnPoint;
    private string  _returnMapName; // capturé à Enter() — SceneLoader.CurrentMap avant le chargement

    public void Enter(IInstanceConfig config, Vector3 respawnPoint)
    {
        CurrentInstance  = config;
        LivesRemaining   = config.LivesPerPlayer;
        CurrentOutcome   = InstanceOutcome.InProgress;
        _respawnPoint    = respawnPoint;
        _returnMapName   = SceneLoader.Instance?.CurrentMap;
        SceneLoader.Instance?.LoadMap(config.SceneName);
    }

    /// <summary>Appelé par Entity/Player.Die() quand isDead passe à true à l'intérieur d'une
    /// instance active — voir §4.3 pour le point d'accroche exact.</summary>
    public void OnPlayerDeath()
    {
        if (CurrentInstance == null || CurrentOutcome != InstanceOutcome.InProgress) return;

        LivesRemaining--;
        if (LivesRemaining > 0)
            RespawnAtCheckpoint();
        else
            EndRun(InstanceOutcome.Failure);
    }

    /// <summary>Appelé par le boss (via son MobData/Mob.Die() ou un hook dédié — exact
    /// accrochage laissé au chantier qui construit le premier boss réel) à sa mort.</summary>
    public void OnBossKilled()
    {
        if (CurrentInstance == null) return;
        EndRun(InstanceOutcome.Success);
    }

    private void RespawnAtCheckpoint() { /* Warp joueur à _respawnPoint, reset HP/Mana */ }

    private void EndRun(InstanceOutcome outcome)
    {
        CurrentOutcome = outcome;
        // TODO (chantier contenu) : distribuer récompenses si Success (donjonLootTable /
        // worldRepGain / rewardAerisFormula selon le type concret de CurrentInstance).
        StartCoroutine(ExitAfterDelay(15f)); // §14.2.4 — expulsion 15s après fin de combat
    }

    private IEnumerator ExitAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        SceneLoader.Instance?.LoadMapWithSpawn(_returnMapName);
        CurrentInstance = null;
    }
}
```

> **Correction (voir plan d'implémentation, Task 5)** : `Enter()` ne prend pas de paramètre
> `Vector3 respawnPoint` — `SceneLoader.LoadMapWithSpawn()` résout `PlayerSpawnPoint` lui-même
> une fois la scène chargée, et le respawn en cours de run réutilise `SceneLoader.
> ReloadCurrentMap()` plutôt qu'un warp manuel. Voir `Systems/InstanceSession.cs` pour le code
> réel.

### 4.2 Nouveau fichier : `Data/Content/DungeonData.cs`

Codifie GDD §14.2.3 tel quel :

```csharp
public enum DungeonType { Classic, Unlock, FactionPvP }

[CreateAssetMenu(fileName = "dgn_", menuName = "AetherTree/Contenu/DungeonData")]
public class DungeonData : ScriptableObject, IInstanceConfig
{
    [Header("Identité")]
    public string dungeonID;
    public string displayName;
    public DungeonType dungeonType = DungeonType.Classic;
    public int tier;
    public int levelMin, levelMax;

    [Header("Accès")]
    public int    maxPlayers   = 15;
    public string entryItemID; // ConsumableData.dungeonID correspondant (voir §4.4)

    [Header("Structure")]
    public bool hasCorridor = false;
    public List<DungeonMapData> maps = new(); // 1 salle par défaut tant que hasCorridor = false
    public List<MobData> bossData = new();

    [Header("Règles de mort — voir §1 spec")]
    public int livesPerPlayer = 2; // Classique : 2 · Déblocage : 1 (voir §14.3.3)
    public int deathLimit     = 5; // collectif — sans effet observable en solo, voir Non-objectifs

    [Header("Couloir")]
    public bool  mobRespawn   = false;
    public float respawnDelay = 30f;

    [Header("Récompenses")]
    public DonjonLootTableData donjonLootTable;
    public int worldRepGain;
    public CodexEntryData codexEntry;

    // IInstanceConfig
    public string InstanceID     => dungeonID;
    public string DisplayName    => displayName;
    public string SceneName      => maps.Count > 0 ? maps[0].mapID : null; // 1 salle pour l'instant
    public string EntryItemID    => entryItemID;
    public int    LivesPerPlayer => livesPerPlayer;
}

[System.Serializable]
public class DungeonMapData
{
    public string mapID;
    public string displayName;
    public bool   isBossRoom;
    // spawners/triggers/lockedUntil : ajoutés quand le contenu réel des salles est construit
}
```

**Donjon de Déblocage** utilise `dungeonType = Unlock` sur le même `DungeonData` — pas de
sous-classe séparée. Le flag `unlockedTier[N]` par joueur (GDD §14.3.4) et la condition "présent
dans la salle du boss à sa mort" sont un hook additionnel sur `OnBossKilled()`, ajouté quand ce
donjon précis est construit (voir Non-objectifs) — pas dans ce chantier.

### 4.3 Nouveau fichier : `Data/Content/CombatVagueData.cs`

Codifie le schéma GDD sous §14.5.1 tel quel (`eventID`, `waveCount`, `preparationDuration`,
`interWaveDuration`, `waves: List<CombatVagueWaveData>`, `timerMax`, etc.) — implémente
`IInstanceConfig` avec `LivesPerPlayer => 1` codé en dur (GDD : "aucune résurrection possible",
jamais configurable). Détail complet laissé au chantier Combat à Vague (item 5 de la roadmap
démo) — ce chantier-ci ne fait que garantir que le type implémente `IInstanceConfig` correctement
pour qu'`InstanceSession` puisse le consommer sans modification.

### 4.4 Modifications : `Combat/SkillSystem.cs` ou nouveau point d'entrée pour la consommation

`ConsumableData.DungeonStone` (déjà existant, `dungeonID` string field) doit résoudre ce
`dungeonID` vers un `DungeonData` asset (registre similaire à `ShopStockRegistry`/
`CraftSystem`'s auto-fill pattern — un `DungeonRegistry` avec `[ContextMenu] AutoFillDungeons()`
suit la même convention déjà établie dans le projet) puis appeler `InstanceSession.Instance
.Enter(dungeonData, respawnPoint)`. Câblage exact laissé au plan d'implémentation.

### 4.5 Modifications : `Entities/Player.cs` (ou `Entity.cs`)

Le point où `isDead` passe à `true` doit appeler `InstanceSession.Instance?.OnPlayerDeath()`
**avant** toute autre logique de mort (écran de mort classique) SI une instance est active —
sinon le comportement de mort hors-instance (déjà existant, `RespawnSystem.cs`/
`DeathScreenUI.cs`) reste inchangé. Exact point d'accroche (probablement dans `Entity.Die()` ou
un event `OnEntityDeath` déjà publié sur `GameEventBus`) à confirmer en lisant le code de mort
existant pendant le plan d'implémentation.

## 5. Vérification (Play Mode manuel, pas de framework de test)

1. Compiler, 0 erreur.
2. Créer un `DungeonData` de test (`dungeonType = Classic`, `livesPerPlayer = 2`, 1 salle, pas de
   vrai boss — juste un Mob de test avec beaucoup de HP).
3. Créer un `ConsumableData` (`consumableType = DungeonStone`, `dungeonID` pointant vers le
   donjon de test), l'ajouter à l'inventaire du joueur de test.
4. Consommer l'objet → vérifier que la scène de test se charge via `SceneLoader` (log
   `OnMapLoaded`), `InstanceSession.LivesRemaining == 2`.
5. Mourir une fois → vérifier respawn dans l'instance, `LivesRemaining == 1`.
6. Mourir une seconde fois → vérifier `CurrentOutcome == Failure`, expulsion après 15s vers la
   map d'origine.
7. Refaire l'essai, tuer le Mob de test → vérifier `CurrentOutcome == Success`, expulsion après
   15s.
8. Cas limite : configurer `livesPerPlayer = 1` sur une copie du donjon de test (simule Donjon de
   Déblocage/Combat à Vague) → vérifier qu'une seule mort déclenche directement `Failure`, aucun
   respawn intermédiaire.
