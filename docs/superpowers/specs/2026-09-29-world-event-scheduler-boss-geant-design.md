# World Event Scheduler + Boss Géant — Design

## Contexte

Premier événement du jeu (roadmap 2026-09-29, point #2). Le jeu n'a aujourd'hui aucun mécanisme
de déclenchement temporisé aléatoire — `SpawnManager` gère bien un respawn min/max, mais il est
scène-scopée (une `SpawnZone` ne connaît que sa propre scène) et son cycle de vie est "toujours un
up, respawn en boucle", pas "événement ponctuel avec fenêtre de résolution". `MobType.BossWorld`
existe déjà dans l'enum (commenté "Boss Géant, spawn sur un palier random") mais n'est câblé nulle
part.

Ce chantier construit le mécanisme générique de **timer d'événement à intervalle aléatoire**
(réutilisable par Invasion, roadmap #3 — même famille de timer) avec Boss Géant comme premier
contenu réel. Combat à Vague (roadmap #4, timer à HEURE FIXE) est un mécanisme différent,
explicitement hors scope ici.

`AnnoncePanel` (5min + 1min avant l'événement) est déjà construit et fonctionnel cette session —
réutilisé tel quel, aucun travail UI nécessaire.

**Limitation solo assumée** : en multijoueur, le Boss Géant apparaîtrait sur le palier tiré que
des joueurs y soient ou non. En solo (portée actuelle du projet), il n'existe qu'une seule scène
chargée à la fois — le spawn réel n'a donc lieu QUE si le joueur se trouve physiquement sur le
palier tiré au moment T-0. S'il est ailleurs, l'événement s'éteint silencieusement et le prochain
timer redémarre. Décidé avec Florian — cohérent avec le traitement du multijoueur partout ailleurs
dans le projet (explicitement parqué, jamais simulé côté solo).

## Architecture

### `Systems/WorldEventScheduler.cs` — nouveau singleton global

Même famille que `InstanceSession`/`AerisSystem` : `DontDestroyOnLoad`, `Instance` en lazy
singleton (auto-créé si absent, comme `InstanceSession.Instance`), place-t-on à la main sous
`_Managers` ou laissé se créer tout seul, peu importe (`AddComponent` appelle `Awake()`
synchrone).

**State machine** (un seul événement actif à la fois — le prochain timer ne redémarre qu'une fois
celui-ci pleinement résolu, aucun chevauchement possible) :

1. **Idle** — `Random.Range(minInterval, maxInterval)` tourne (secondes, Inspector-configurable ;
   défaut production ~3600-7200s comme `SpawnManager`'s boss de map, mais Florian réduira
   drastiquement pour tester en Play Mode — pas de valeur fixée en dur dans le code).
2. **Décision** (à "T-5min avant spawn", càd le timer déclenche cette étape 5 minutes avant le
   spawn réel) — tire un palier au hasard dans `eligibleMaps`, tire un boss au hasard dans
   `worldBossData.possibleBosses`, niveau = palier tiré (identité directe, pas de formule — voir
   §Niveau plus bas). `AnnoncePanel.Instance?.Announce(...)` avec le nom du palier.
3. **Attente** — deuxième annonce à T-1min. À T-0 : compare `SceneManager.GetActiveScene().name`
   au `sceneName` de l'entrée `eligibleMaps` tirée.
   - Match → instancie le boss à une position aléatoire sur le NavMesh de la scène courante (voir
     §Position de spawn plus bas), passe en **Actif**.
   - Pas de match → rien ne spawn, retour direct à **Idle**, timer relancé immédiatement.
4. **Actif** — boss vivant, référence gardée (`GameObject _aliveBoss`). Deux sorties :
   - **Mort** (`Mob.OnDeath(...)`, même patron que `SpawnManager.SpawnBoss` — callback posé au
     spawn) → `AnnoncePanel.Announce("Le Boss Géant du Palier X a été vaincu !")`, retour **Idle**,
     timer relancé. Récompenses (XP/Aeris/Prestige/loot) : **déjà gérées** par le pipeline
     `GameEventBus.OnMobKilled` existant (`LootManager`/`XPSystem`, `LootTable.prestigeReward`
     inclus) — rien de neuf à câbler ici, le boss est un `Mob` normal comme les autres.
   - **Timeout 30 minutes** sans mort → despawn (`Destroy`), `AnnoncePanel.Announce("Le Boss
     Géant du Palier X s'est retiré...")`, retour **Idle**, timer relancé.

### `Data/Content/WorldBossData.cs` — nouveau ScriptableObject

```csharp
[CreateAssetMenu(fileName = "wboss_", menuName = "AetherTree/Contenu/WorldBossData")]
public class WorldBossData : ScriptableObject
{
    [Tooltip("Un des mobs ci-dessous est tiré au hasard à chaque événement. Doit avoir " +
             "MobType = BossWorld (juste un warning OnValidate si non respecté, pas un blocage " +
             "dur — même discipline que le reste du projet).")]
    public List<MobData> possibleBosses = new List<MobData>();
}
```

Asset unique référencé par `WorldEventScheduler.worldBossData` (même patron de référence qu'un
seul `PrestigeAuraData` partagé sur `Player`).

### Niveau du boss = palier tiré, sans formule

Florian : "palier 2 = niveau 2, palier 7 = niveau 7". Pas de table `levelByPalier` à part —
l'équilibrage stat/niveau (`CharacterStats`/`MobStatCalculator`) n'est de toute façon pas encore
calibré dans le projet, une formule savante ici serait prématurée. `mob.mobLevel = targetPalier`
directement, recalibrable plus tard sans toucher à ce système (juste retaper les stats
par-niveau ailleurs).

### `eligibleMaps` — liste de scènes éligibles

```csharp
[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — sceneName se remplit automatiquement (voir OnValidate). " +
             "Editor-only, n'existe pas en build : sceneName reste le champ réellement lu au " +
             "runtime (comparaison de string, même convention que SceneLoader/Portal partout " +
             "ailleurs dans le projet).")]
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;

    [Tooltip("Palier de CETTE scène — doit correspondre à MapInfo.palier posé dans la scène " +
             "elle-même. Pas de lecture automatique possible : une scène non chargée n'a pas de " +
             "MapInfo accessible, Florian le retape ici à la main (une seule fois, à la config).")]
    public int palier;
}
```

`WorldEventScheduler.eligibleMaps: List<WorldEventMapEntry>` + un `OnValidate()` qui synchronise
`sceneName` depuis `mapScene`, exactement le patron déjà utilisé par `DungeonData.OnValidate()`
pour `DungeonMapData.mapID`. Aujourd'hui : 2 entrées possibles (Map_01/palier 1, Map_02/palier 2
— Map_02 est un plan vide, techniquement éligible mais sans contenu réel autour, tel quel).

### Position de spawn — n'importe où sur le NavMesh de la scène

Florian : le Boss Géant doit pouvoir apparaître n'importe où sur la map, pas à un point fixe —
et zéro configuration par map (pas de zone centre+taille à positionner à la main). Technique
standard pour un point aléatoire sur un NavMesh entier :

```csharp
private static Vector3 GetRandomNavMeshPoint()
{
    var tri = NavMesh.CalculateTriangulation();
    if (tri.indices.Length < 3) return Vector3.zero; // NavMesh vide/absent — voir §Gestion d'erreur

    int triCount = tri.indices.Length / 3;
    int t = Random.Range(0, triCount) * 3;
    Vector3 a = tri.vertices[tri.indices[t]];
    Vector3 b = tri.vertices[tri.indices[t + 1]];
    Vector3 c = tri.vertices[tri.indices[t + 2]];

    // Point barycentrique uniforme dans le triangle (méthode racine carrée — évite le biais
    // vers un coin que donnerait une combinaison linéaire naïve de 2 valeurs aléatoires).
    float r1 = Mathf.Sqrt(Random.value);
    float r2 = Random.value;
    return a * (1f - r1) + b * (r1 * (1f - r2)) + c * (r1 * r2);
}
```

Pas de biais par aire de triangle pris en compte (un triangle a autant de chance d'être choisi
qu'un autre, indépendamment de sa taille) — acceptable ici, ce n'est pas une distribution
statistiquement critique, juste "un endroit surprise sur la map". `NavMesh.CalculateTriangulation()`
reflète le NavMesh déjà baké de la scène ACTIVE au moment de l'appel (T-0, scène confirmée
correspondre au palier tiré) — aucun marker/component à poser dans les scènes.

## Flux de données

```
WorldEventScheduler (Idle)
  → timer aléatoire écoulé
  → tire palier (eligibleMaps) + boss (worldBossData.possibleBosses) + niveau = palier
  → AnnoncePanel.Announce (T-5min)
  → attend 4 min
  → AnnoncePanel.Announce (T-1min)
  → attend 1 min (T-0)
  → scène active == palier tiré ?
      NON → Idle (timer relancé)
      OUI → Instantiate(boss.prefab) à un point aléatoire du NavMesh, mob.mobLevel = palier,
            mob.OnDeath(...) posé → Actif
  → [Actif] mort OU 30min écoulées → Idle (timer relancé), annonce de sortie
```

## Gestion d'erreur

- `eligibleMaps` vide ou `worldBossData` non assigné → log warning, timer ne se relance jamais
  (pas de crash, juste un système inerte tant que Florian ne configure pas).
- `possibleBosses` vide → même traitement, warning au moment du tirage.
- `NavMesh.CalculateTriangulation()` vide (scène sans NavMesh baké) au moment du spawn → warning,
  event annulé pour cette occurrence, retour Idle, timer relancé (ne bloque pas le système
  entier).
- Boss sans `prefab` assigné sur son `MobData` → même garde déjà existante dans
  `SpawnManager.SpawnBoss` (`Debug.LogWarning`), même comportement ici.

## Vérification (Play Mode manuel, pas de framework de test auto)

1. Configurer `WorldEventScheduler` : `eligibleMaps` (Map_01+Map_02), `worldBossData` (au moins 1
   `MobData` avec `MobType = BossWorld`), `minInterval`/`maxInterval` réduits à quelques dizaines
   de secondes pour tester sans attendre.
2. Rester sur Map_01, attendre le cycle complet → vérifier annonce T-5/T-1, vérifier spawn UNIQUEMENT
   si le palier tiré était celui de Map_01, vérifier absence de spawn + relance immédiate du timer
   sinon.
3. Tuer le boss → vérifier annonce succès, XP/Aeris/Prestige/loot reçus normalement, timer relancé.
4. Laisser un boss spawné sans le tuer 30 min (ou réduire temporairement le timeout pour tester) →
   vérifier despawn + annonce de sortie + timer relancé.
5. Changer de scène (Map_01 → Map_02) pendant qu'un événement est en Attente (T-5 à T-0) → vérifier
   que le check T-0 utilise bien la scène active AU MOMENT DU CHECK, pas celle du moment de la
   décision.
