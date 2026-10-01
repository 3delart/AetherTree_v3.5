# World State Save — Design

## Contexte

Florian, en testant le système World Event (World Boss + Invasion, construit la veille) :
*"il faut aussi commencer une save de 'serveur' (mob spawn et timer en tout genre y compris
event et autre)"*.

Aujourd'hui, TOUT l'état "monde" vit en mémoire pure, perdu à chaque fermeture du jeu :
- `WorldEventScheduler` (singleton global) — countdown aléatoire avant le prochain event, dans
  une simple coroutine `WaitForSeconds` opaque.
- `SpawnManager` (par scène) — zones de boss de map errant + ressources rares, chacune avec son
  propre `minRespawnDelay`/`maxRespawnDelay` compté par sa propre coroutine.
- Mobs/ressources placés à la main en scène (`Mob.cs`/`ResourceNode.cs`) — respawn sur place,
  compté localement, par instance.

`SaveSystem.cs` existant ne gère QUE la save par-joueur (position, inventaire, XP...) — jamais
d'état monde partagé.

## Décisions de périmètre (validées en brainstorming, 2026-10-01)

1. **But** : persistance de session (le monde doit reprendre où il en était après une
   fermeture/réouverture du jeu) — pas encore une prépa pour un vrai serveur réseau
   multi-joueur (ce chantier existe séparément, hors scope ici).
2. **Timers pendant que le jeu est FERMÉ** : gelés. On sauvegarde le temps restant, il reprend
   pile à la réouverture — le monde ne vit QUE pendant que le jeu tourne, jamais hors-jeu.
3. **Event/combat EN COURS au moment de la fermeture** : abandonné, pas repris en détail. Un
   World Boss à moitié mort ou une Invasion en vague 3 ne persiste PAS son état exact (vie des
   mobs, vagues spawn, progression) — à la réouverture, l'event en cours est simplement effacé,
   le `WorldEventScheduler` repart sur son countdown normal (gelé ou neuf selon ce qui a pu être
   sauvegardé avant l'event, voir §Gestion d'erreur). Simplifie énormément le scope : pas besoin
   de persister l'état détaillé d'un combat, seulement les timers "idle" (en attente).
4. **Périmètre v1** : `WorldEventScheduler` (1 countdown global) + zones `SpawnManager` (boss de
   map + ressources rares — peu nombreuses, identifiables par nom de zone). **PAS** les mobs/
   ressources placés à la main individuellement en scène (`Mob.cs`/`ResourceNode.cs`) — demande
   un système d'identité stable par instance qui n'existe pas aujourd'hui (un ID Unity runtime
   change à chaque relance). **Noté pour plus tard, chantier séparé** (voir §Hors scope).
5. **Timers pendant que le jeu TOURNE, sur une AUTRE map** : continuent de courir en
   permanence, peu importe la scène chargée — cohérent avec `WorldEventScheduler` qui fonctionne
   déjà ainsi (singleton global, jamais lié à une scène précise). Implique que `SpawnManager`
   n'est plus le propriétaire du décompte de ses zones — un registre central l'est, et
   `SpawnManager` lui demande "prêt ?" au lieu de compter lui-même.

## Architecture

### `Systems/WorldStateRegistry.cs` (nouveau) — source de vérité en mémoire + save/load

Singleton global, `DontDestroyOnLoad`, même famille que `WorldEventScheduler`/`InstanceSession`.
Porte DEUX responsabilités fusionnées (pas de classe séparée pour le JSON — le registre est déjà
la source de vérité, autant qu'il porte aussi sa persistance, même esprit que `SaveSystem.cs`
qui fait les deux pour `CharacterProgress`) :

1. **Tracking en mémoire** des timers de respawn de zone (`SpawnManager`), clé composite
   `"sceneName|zoneName"` — tick dans son propre `Update()`, indépendamment de la scène chargée.
2. **Save/load JSON** (`world_state.json`, même dossier/patron que `CharacterSavePath` via
   `Application.persistentDataPath`), écrit aux mêmes déclencheurs que le save joueur
   (`OnApplicationQuit` + `SceneLoader` à chaque changement de map) — pas de nouveau rythme.

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.IO;

// =============================================================
// WORLDSTATEREGISTRY.CS — État monde persistant (timers de respawn, event global)
// Path : Assets/Scripts/Systems/WorldStateRegistry.cs
// Spec : docs/superpowers/specs/2026-10-01-world-state-save-design.md
//
// Singleton global (DontDestroyOnLoad), même famille qu'InstanceSession/WorldEventScheduler.
// Source de vérité EN MÉMOIRE des timers de respawn de zone (SpawnManager) — SpawnManager
// n'est plus propriétaire de son propre décompte, il rapporte/interroge ce registre. Les timers
// courent en PERMANENCE (Update() ici), peu importe quelle scène est chargée — un boss tué sur
// la map B revient même si le joueur passe cette heure sur la map A (Florian, 2026-10-01).
//
// Porte AUSSI la persistance disque (JSON, world_state.json) — pas de classe séparée, ce
// registre est déjà la source de vérité, même esprit que SaveSystem.cs qui fait les deux pour
// CharacterProgress. Gelé pendant que le jeu est FERMÉ : Save() capture le temps restant,
// Load() le restaure tel quel, aucun calcul de temps réel écoulé hors-jeu.
// =============================================================
public class WorldStateRegistry : MonoBehaviour
{
    private static WorldStateRegistry _instance;

    public static WorldStateRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WorldStateRegistry (auto)");
                _instance = go.AddComponent<WorldStateRegistry>(); // Awake() protège en DontDestroyOnLoad
            }
            return _instance;
        }
    }

    /// <summary>Même patron que InstanceSession.Exists/WorldEventScheduler.Exists — ne jamais
    /// déclencher l'auto-création juste pour vérifier qu'un registre existe.</summary>
    public static bool Exists => _instance != null;

    private readonly Dictionary<string, float> _zoneTimers = new Dictionary<string, float>();

    /// <summary>Countdown restant avant le prochain tirage WorldEventScheduler — géré ici pour
    /// que Save()/Load() aient un endroit unique où lire/écrire TOUT l'état monde. Lu/écrit par
    /// WorldEventScheduler lui-même via les méthodes ci-dessous (jamais directement par un tiers).</summary>
    private float _worldEventTimeRemaining = -1f; // -1 = pas encore initialisé par le scheduler

    private static string WorldSavePath => Path.Combine(Application.persistentDataPath, "world_state.json");

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Load(); // tôt — avant que WorldEventScheduler.Start() ne lise son countdown restauré
    }

    private void Update()
    {
        if (_zoneTimers.Count == 0) return;

        // Copie des clés : on ne peut pas modifier un Dictionary pendant qu'on l'itère.
        var keys = new List<string>(_zoneTimers.Keys);
        foreach (string key in keys)
        {
            float remaining = _zoneTimers[key] - Time.deltaTime;
            _zoneTimers[key] = Mathf.Max(0f, remaining);
        }
    }

    // =========================================================
    // ZONES SPAWNMANAGER
    // =========================================================

    private static string ZoneKey(string sceneName, string zoneName) => $"{sceneName}|{zoneName}";

    /// <summary>Appelé par SpawnManager à la mort d'un boss/épuisement d'une ressource rare —
    /// démarre (ou redémarre) le décompte de cette zone.</summary>
    public void RegisterRespawn(string sceneName, string zoneName, float delay)
        => _zoneTimers[ZoneKey(sceneName, zoneName)] = Mathf.Max(0f, delay);

    /// <summary>Appelé par SpawnManager (à son Start() ET en polling tant que la zone est en
    /// cooldown) — true + remaining si la zone est encore trackée (en cooldown, remaining peut
    /// être 0 = prête à respawn MAIS pas encore nettoyée, voir ClearZone), false si la zone n'a
    /// jamais été enregistrée (jamais spawn/mort, ou déjà nettoyée — prête).</summary>
    public bool TryGetRemainingTime(string sceneName, string zoneName, out float remaining)
        => _zoneTimers.TryGetValue(ZoneKey(sceneName, zoneName), out remaining);

    /// <summary>Appelé par SpawnManager une fois le respawn RÉELLEMENT déclenché (boss
    /// réinstancié / node ressource recréé) — retire l'entrée, la zone est de nouveau libre.</summary>
    public void ClearZone(string sceneName, string zoneName)
        => _zoneTimers.Remove(ZoneKey(sceneName, zoneName));

    // =========================================================
    // WORLDEVENTSCHEDULER
    // =========================================================

    /// <summary>Appelé par WorldEventScheduler à chaque frame de son propre countdown — tient le
    /// registre à jour pour qu'un Save() déclenché à tout moment capture la vraie valeur.</summary>
    public void SetWorldEventTimeRemaining(float remaining) => _worldEventTimeRemaining = remaining;

    /// <summary>Appelé par WorldEventScheduler à son Start() — true + remaining si une save
    /// existait déjà (reprend ce countdown), false si c'est un tout premier lancement (le
    /// scheduler tire un intervalle neuf comme avant).</summary>
    public bool TryGetWorldEventTimeRemaining(out float remaining)
    {
        remaining = _worldEventTimeRemaining;
        return _worldEventTimeRemaining >= 0f;
    }

    // =========================================================
    // SAVE / LOAD
    // =========================================================

    public void Save()
    {
        var state = new WorldState { worldEventTimeRemaining = _worldEventTimeRemaining };
        foreach (var kv in _zoneTimers)
        {
            int sep = kv.Key.IndexOf('|');
            state.zoneTimers.Add(new ZoneTimerState
            {
                sceneName            = kv.Key.Substring(0, sep),
                zoneName             = kv.Key.Substring(sep + 1),
                respawnTimeRemaining = kv.Value,
            });
        }

        try
        {
            string json = JsonUtility.ToJson(state, prettyPrint: true);
            File.WriteAllText(WorldSavePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de sauvegarde : {e.Message}");
        }
    }

    private void Load()
    {
        if (!File.Exists(WorldSavePath)) return; // premier lancement — tout reste à l'état neuf

        try
        {
            string json  = File.ReadAllText(WorldSavePath);
            var    state = JsonUtility.FromJson<WorldState>(json);
            if (state == null) return;

            _worldEventTimeRemaining = state.worldEventTimeRemaining;
            _zoneTimers.Clear();
            if (state.zoneTimers != null)
                foreach (var z in state.zoneTimers)
                    _zoneTimers[ZoneKey(z.sceneName, z.zoneName)] = z.respawnTimeRemaining;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldStateRegistry] Échec de chargement, repart à neuf : {e.Message}");
        }
    }
}

[System.Serializable]
public class WorldState
{
    public float worldEventTimeRemaining = -1f;
    public List<ZoneTimerState> zoneTimers = new List<ZoneTimerState>();
}

[System.Serializable]
public class ZoneTimerState
{
    public string sceneName;
    public string zoneName;
    public float  respawnTimeRemaining;
}
```

### `Systems/WorldEventScheduler.cs` — timer restructuré en champ interrogeable

`RunCycle()` passe d'un `WaitForSeconds` opaque à un countdown explicite (`_timeUntilNextRoll`,
décompté dans `Update()`), pour que `WorldStateRegistry` puisse le lire/écrire. Au `Start()`, si
le registre a un countdown sauvegardé, on le reprend tel quel ; sinon comportement actuel
(tirage neuf `Random.Range(minInterval, maxInterval)`).

```csharp
private float _timeUntilNextRoll = -1f; // -1 = pas encore armé

private void Start()
{
    // .Instance ICI, pas .Exists — contrairement à GameEventBus.Reset() (qui veut juste savoir
    // "y a-t-il déjà un registre" sans en créer un pour rien), le scheduler a BESOIN que le
    // registre existe pour fonctionner : s'il n'a jamais été touché avant ce point, .Instance le
    // crée et déclenche son Load() (dans son propre Awake()) — sans quoi une sauvegarde pourrait
    // rester silencieusement jamais chargée si rien d'autre n'accède au registre en premier.
    if (WorldStateRegistry.Instance.TryGetWorldEventTimeRemaining(out float saved))
        _timeUntilNextRoll = saved;
    // sinon : RunCycle() tire un intervalle neuf au premier passage (comportement actuel)

    StartCoroutine(RunCycle());
}

private void Update()
{
    if (_timeUntilNextRoll < 0f) return; // pas encore armé, ou event en cours (voir RunCycle)
    _timeUntilNextRoll = Mathf.Max(0f, _timeUntilNextRoll - Time.deltaTime);
    WorldStateRegistry.Instance?.SetWorldEventTimeRemaining(_timeUntilNextRoll);
}

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

        if (_timeUntilNextRoll < 0f)
            _timeUntilNextRoll = Random.Range(minInterval, maxInterval);

        // Attend que le countdown descende jusqu'à l'offset de 1ère annonce — équivalent au
        // WaitForSeconds(totalDelay - firstWarningOffset) d'avant, mais interrogeable/
        // sauvegardable à tout moment via _timeUntilNextRoll (décompté dans Update()) au lieu
        // d'un délai opaque figé dans la coroutine.
        yield return new WaitUntil(() => _timeUntilNextRoll <= firstWarningOffset);

        WorldEventData selected = eventPool[Random.Range(0, eventPool.Count)];
        if (selected == null) { _timeUntilNextRoll = -1f; continue; }

        _timeUntilNextRoll = -1f; // event en cours — countdown "gelé/inactif" jusqu'à résolution
                                   // (§Décisions #3 : l'event en cours n'est pas sauvegardé en
                                   // détail ; si le jeu ferme ici, à la réouverture ce cycle est
                                   // abandonné et un intervalle NEUF est tiré, pas de countdown à
                                   // reprendre pour un event qui n'a jamais fini)
        _activeEvent = selected;
        yield return selected.RunEvent(this);
        _activeEvent = null;
    }
}
```

### `Systems/SpawnManager.cs` — délègue le décompte au registre

Changement de rôle : `SpawnManager` ne compte plus ses zones lui-même. À la place :

- **Mort du boss / épuisement ressource** → `WorldStateRegistry.Instance.RegisterRespawn(sceneName, zone.zoneName, Random.Range(zone.minRespawnDelay, zone.maxRespawnDelay))` (le tirage aléatoire du délai reste ICI, SpawnManager garde la config min/max — seul le DÉCOMPTE part au registre).
- **`Start()`** : pour chaque zone, interroge `WorldStateRegistry.Instance.TryGetRemainingTime(sceneName, zone.zoneName, out remaining)`. Si `true` → zone en cooldown, ne spawn pas, démarre le polling. Si `false` → zone libre, spawn immédiat (comportement actuel inchangé).
- **Polling** (coroutine légère, ex: vérifie toutes les 1s tant qu'en cooldown) : dès que `TryGetRemainingTime` retourne `remaining <= 0`, déclenche le respawn réel (instancie boss/node) PUIS appelle `WorldStateRegistry.Instance.ClearZone(sceneName, zone.zoneName)`.

`sceneName` = `SceneManager.GetActiveScene().name`, lu une fois au `Start()` (SpawnManager est
toujours instancié dans la scène qu'il concerne, jamais DontDestroyOnLoad).

**Avertissement Editor** (nouveau, `OnValidate` sur `SpawnManager`) : warn si deux `SpawnZone`/
`ResourceSpawnZone` du MÊME `SpawnManager` partagent le même `zoneName` — la clé composite
`"scene|zone"` doit être unique par scène, sinon deux zones distinctes s'écraseraient dans le
registre.

## Hors scope (noté pour plus tard, demande explicite Florian)

**Persistance des mobs/ressources placés à la main** (`Mob.cs`/`ResourceNode.cs`, potentiellement
des centaines d'instances par map) — nécessite un système d'identité stable par instance qui
n'existe pas aujourd'hui (un ID Unity runtime change à chaque relance). Deux pistes possibles
pour ce futur chantier, à creuser le moment venu :
- ID auto-généré (GUID) stocké dans les données sérialisées du prefab/instance.
- ID assigné à la main par Florian sur chaque instance — fiable mais fastidieux pour un grand
  nombre de mobs/ressources.

Ce chantier dépend du design final de `WorldStateRegistry` (même clé composite scène+identifiant,
juste un espace de noms séparé) — pas une réécriture, une extension une fois l'identité résolue.

## Gestion d'erreur

- `world_state.json` absent (premier lancement) → tout reste à l'état neuf (`_zoneTimers` vide,
  `_worldEventTimeRemaining = -1`), comportement identique à avant ce chantier.
- `world_state.json` corrompu/illisible → log d'erreur, repart à neuf (même filet que
  `CharacterProgress`/`AccountProgress` dans `SaveSystem.cs`).
- Écriture disque échoue (permissions, disque plein...) → log d'erreur, ne bloque jamais le jeu
  (try/catch autour de `File.WriteAllText`, même patron que `SaveSystem.cs`).
- Zone jamais enregistrée interrogée (`TryGetRemainingTime` → false) → traitée comme "prête",
  comportement actuel (spawn immédiat) — pas une erreur, le cas normal pour une zone qui n'a
  jamais encore été vidée.

## Vérification (Play Mode manuel)

1. Lancer le jeu (premier lancement, pas de `world_state.json`) — confirmer que `SpawnManager`
   spawn ses zones normalement et que `WorldEventScheduler` tire un intervalle neuf (comme avant
   ce chantier).
2. Tuer un boss de map (`SpawnManager`) → confirmer qu'il ne respawn pas immédiatement, que le
   compte à rebours tourne (vérifiable via un log/Inspector debug).
3. Changer de map PENDANT que ce boss est en cooldown → revenir quelques minutes après →
   confirmer que le compte à rebours a continué de tourner MÊME absent (§Décisions #5).
4. Fermer le jeu (vrai Quit, pas juste arrêter le Play Mode) pendant que le boss est encore en
   cooldown → relancer → confirmer que le compte à rebours reprend PILE où il était (pas de temps
   réel écoulé compté, §Décisions #2).
5. Réduire `minInterval`/`maxInterval` du `WorldEventScheduler` pour tester vite → laisser tourner
   jusqu'à un event (sans le résoudre) → fermer le jeu EN PLEIN EVENT → relancer → confirmer que
   l'event en cours a été abandonné et qu'un NOUVEAU countdown neuf a été tiré (§Décisions #3).
6. Fermer le jeu PENDANT la phase d'attente idle (pas d'event en cours) → relancer → confirmer
   que le countdown jusqu'au prochain event reprend pile où il était.
7. Configurer deux zones avec le MÊME `zoneName` sur un même `SpawnManager` → confirmer que le
   warning `OnValidate` apparaît en Console.
8. Supprimer/corrompre `world_state.json` à la main → relancer → confirmer qu'aucune erreur ne
   bloque le jeu, tout repart à neuf.
