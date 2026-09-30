# World Event Scheduler — Generic Multi-Type Events + Invasion — Design

## Contexte

`WorldEventScheduler`/`WorldBossData` (shippé 2026-09-29) gèrent aujourd'hui UN SEUL type
d'événement, câblé en dur : Boss Géant. Florian veut généraliser en vrai sélecteur d'événement —
toutes les 6-8h (intervalle aléatoire), UN type d'événement est tiré au hasard parmi plusieurs
(Boss Géant, Invasion, d'autres plus tard), chacun avec sa propre mécanique de déroulement mais
un même tronc commun (choix de palier, tracking de participation, distribution de récompense).

Ce chantier fait deux choses en un : (1) généralise le scheduler pour accueillir plusieurs types
d'événements sans dupliquer le tronc commun, (2) livre Invasion comme second type réel, avec son
système de vagues.

## Architecture

### `Systems/WorldEventScheduler.cs` — devient un pur dispatcher

Le timer (intervalle aléatoire) et les 2 offsets d'annonce (T-5min/T-1min) remontent ICI,
partagés par TOUS les types d'événements — un seul rythme global, pas un timer par type.
`WorldBossData` perd ces champs (ils vivaient dessus depuis hier, mauvais endroit maintenant
qu'un 2e type existe).

```csharp
[Header("Timer — intervalle aléatoire entre deux événements (secondes)")]
public float minInterval = 21600f; // 6h
public float maxInterval = 28800f; // 8h

[Header("Annonces — délai AVANT le déclenchement (secondes)")]
public float firstWarningOffset  = 300f; // 5 min
public float secondWarningOffset = 60f;  // 1 min

[Header("Pool d'événements possibles")]
public List<WorldEventData> eventPool = new List<WorldEventData>();

public float FirstWarningOffset  => firstWarningOffset;
public float SecondWarningOffset => secondWarningOffset;
```

Cycle principal :

```csharp
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
        yield return selected.RunEvent(this);
        // selected.RunEvent gère TOUT (annonces, spawn, résolution) en utilisant
        // FirstWarningOffset/SecondWarningOffset ci-dessus — le scheduler ne sait rien
        // de plus sur ce qui se passe à l'intérieur.
    }
}
```

Utilitaire NavMesh (déplacé depuis hier, devient public+static pour que les types d'événements
puissent l'appeler) :

```csharp
public static bool TryGetRandomNavMeshPoint(out Vector3 point)
{
    // Code inchangé depuis hier — triangulation + point barycentrique uniforme.
}
```

### `Data/Content/WorldEventData.cs` — nouvelle classe abstraite, tronc commun

`WorldEventMapEntry` **déménage** ici depuis `Data/Content/WorldBossData.cs` (où il vivait
depuis hier) — c'est maintenant une classe partagée par tous les types d'événements, plus
spécifique à Boss Géant. Le plan d'implémentation doit RETIRER cette classe de
`WorldBossData.cs` en même temps qu'il l'ajoute ici, pas la dupliquer dans les deux fichiers.

**Amendement (2026-09-30, après implémentation)** — `eligibleMaps`/`WorldEventMapEntry`/
`PickRandomMap()` ont ensuite déménagé UNE 2e FOIS, cette fois de `WorldEventData` vers
`Systems/WorldEventScheduler.cs` directement (même raisonnement que le timer/les offsets déjà
partagés : un palier éligible à un event ne dépend pas du type tiré — Florian, 2026-09-30). Le
code ci-dessous montre l'état intermédiaire (juste après ce chantier), pas l'état final — voir
commit `f454bba` pour le diff réel. `WorldBossData`/`InvasionData` lisent maintenant
`scheduler.eligibleMaps`/`scheduler.PickRandomMap()` au lieu de `this.eligibleMaps`/
`this.PickRandomMap()`, et `SyncEligibleMapsSceneNames()` (protected sur `WorldEventData`)
disparaît, remplacée par un `OnValidate` propre à `WorldEventScheduler`.

```csharp
using System.Linq;

[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;
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

    public abstract string DisplayName { get; }
    public abstract IEnumerator RunEvent(WorldEventScheduler scheduler);
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

    protected void StartTracking()
    {
        _hitCounts.Clear();
        GameEventBus.OnDamageDealt += OnDamageDealtHandler;
    }

    protected void StopTracking() => GameEventBus.OnDamageDealt -= OnDamageDealtHandler;

    /// <summary>Distribue la récompense de l'événement à tout joueur ayant atteint
    /// minHitsToBeEligible coups sur un mob possédé par CET événement (voir OwnsMob) — calculé à
    /// la volée ici, jamais maintenu comme liste incrémentale, pour rester la source de vérité
    /// unique au moment de la distribution.</summary>
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

`OwnsMob`/`TrackDamage` remplacent le mécanisme `MobData.massEventRewards`/`contributingPlayers`
d'hier pour tout ce qui passe par un événement — la participation est trackée par l'ÉVÉNEMENT
lui-même (nombre de coups portés, pas juste présence), pas par le mob individuel. **`massEventRewards`
est retiré entièrement** (voir §Suppression plus bas) : il ne sert plus à rien une fois que la
récompense vit sur l'événement et non sur le mob.

**Correction post-relecture (2026-09-30)** — bug trouvé en relisant spec+plan+code réel avant
implémentation : dans la première version de ce design, `eligiblePlayers` était une simple liste
(présence/absence). Florian a demandé un VRAI seuil de participation (nombre de coups, pas juste
"a tapé au moins 1 fois") pour empêcher un joueur de passage d'être éligible — d'où
`minHitsToBeEligible` (défaut 10, "il faut vraiment participer") et le passage à un
`Dictionary<Player,int>` interne plutôt qu'une liste. Cette correction rend aussi obsolète le
champ public `eligiblePlayers` (HideInInspector) de la première version — plus rien d'externe n'a
besoin de le lire, il devient un détail d'implémentation privé (`_hitCounts`).

### `Systems/LootManager.cs` / `Progression/XPSystem.cs` — 2 nouvelles méthodes publiques

Extraites pour être appelées directement par `WorldEventData.GrantEventRewards`, sans passer par
`GameEventBus`/`MobKilledEvent` (l'événement décide LUI-MÊME quand distribuer, pas à la mort d'un
mob précis) :

```csharp
// LootManager.cs
/// <summary>Distribue une LootTable à une liste de joueurs, hors du pipeline MobKilledEvent —
/// pour la récompense de fin d'événement (World Boss/Invasion). Chaque item va à un joueur
/// DIFFÉRENT (tirage sans remise, même règle que la fairness de masse) ; items en trop si plus
/// d'items droppés que de joueurs éligibles = non attribués, pas de bouclage.</summary>
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

```csharp
// XPSystem.cs
/// <summary>XP/Prestige de la LootTable d'un événement à TOUS les joueurs éligibles — même
/// principe que HandleMobKilled mais hors pipeline MobKilledEvent.</summary>
public void GrantEventRewards(LootTable table, List<Player> eligiblePlayers)
{
    if (table == null || eligiblePlayers == null || eligiblePlayers.Count == 0) return;
    if (table.xpReward > 0)
        foreach (Player p in eligiblePlayers) GiveCombatXP(p, table.xpReward);
    if (table.prestigeReward > 0)
        foreach (Player p in eligiblePlayers) p.AddPrestige(table.prestigeReward);
}
```

### Suppression — `MobData.massEventRewards` et son câblage (2026-09-29)

Retiré entièrement, plus utile (Florian, 2026-09-30 : "masseventreward ne sert plus si la
récompense est sur l'event et non sur le mob") :
- `MobData.massEventRewards: bool` — champ supprimé.
- `Progression/XPSystem.cs::HandleMobKilled` — revient à lire `e.eligiblePlayers` directement
  (comportement d'avant-hier, seuil ≥10% inchangé pour tout mob normal).
- `Systems/LootManager.cs::OnMobKilled` — revient à `PickRandomEligible(e.eligiblePlayers)` pour
  chaque item/Aeris indépendamment (comportement d'avant-hier), plus de branche
  `massEventRewards`/tirage sans remise ICI (cette logique est réutilisée mais déplacée dans
  `GrantEventLoot` ci-dessus, pour l'usage événementiel uniquement).

`Mob.Die()`'s calcul de `eligiblePlayers`/`contributingPlayers` (≥10%/≥1 dégât) reste
**inchangé** — toujours calculé et publié sur chaque `MobKilledEvent`, `contributingPlayers`
reste utilisé ailleurs (XP Esprit, GDD §5.8), seul son usage pour la fairness de masse est retiré.

### `Data/Mobs/MobData.cs` — nouveau `MobType.EventInvasionMob` + `InvasionRole`

Même patron que `DungeonMob`/`DungeonRole` (voir ce même fichier) :

```csharp
public enum MobType
{
    WorldMob         = 0,
    DungeonMob       = 1,
    WorldBoss        = 2,
    EventGiantBoss   = 4,
    EventInvasionMob = 6,   // NOUVEAU — tout mob de l'événement Invasion (vague/renfort/boss),
                            // voir InvasionRole
}

public enum InvasionRole
{
    Normal = 0, // Mob de vague/renfort — le trash de l'invasion.
    Boss   = 1, // LE boss de cette invasion — voir InvasionVariant.boss.
}
```

**Correction post-relecture (2026-09-30)** — première version de ce design réutilisait
`MobType.BossInvasion` (ordinal 5, déjà présent, jamais câblé) comme boss d'invasion, avec un
`MobType` dédié (ordinal 6) pour le trash — pas de nouvel enum `InvasionRole`. Florian a demandé
le même patron que `MobDungeon`/`DungeonRole` à la place : un seul `MobType` pour TOUT mob de
l'invasion, le rôle (`InvasionRole.Normal`/`Boss`) distingue trash/boss, exactement comme
`dungeonRole` le fait pour les mobs de donjon. `BossInvasion` (ordinal 5) est retiré — jamais
sérialisé sur aucun asset réel (l'Invasion vient d'être créée) — et PAS renuméroté pour combler le
trou, même discipline que l'ordinal 3 vacant (ex-`BossDungeon`).

**Deuxième correction (2026-09-30)** — Florian a ensuite demandé que TOUT `MobType` de mob
d'ÉVÉNEMENT commence par `Event` : `BossWorld` → `EventWorldBoss`, `MobInvasion` → `EventMobInvasion`
(renommage de nom d'enum uniquement, ordinaux 4/6 inchangés). `BossMap` n'était PAS renommé à cette
étape : pas un mob d'événement (aucun lien avec `WorldEventScheduler`), juste un mini-boss permanent
en zone ouverte.

**Troisième correction (2026-09-30)** — Florian a ensuite étendu le renommage à TOUT l'enum, deux
catégories explicites : `World` = mob PERMANENT (monde ouvert), `Event` = mob d'un
`WorldEventScheduler` (temporaire). `Normal` → `WorldMob`, `BossMap` → `WorldBoss`, `MobDungeon` →
`DungeonMob` (PAS `World` — un mob de donjon vit en instance, pas dans le monde ouvert, catégorie
à part), `EventWorldBoss` → `EventGiantBoss` (PAS `EventWorldBoss` — collision de nom avec le
nouveau `WorldBoss` (ex-`BossMap`), alors que ce sont deux mobs différents ; `EventGiantBoss` colle
au `DisplayName` déjà utilisé partout, "Boss Géant"), `MobInvasion`/`EventMobInvasion` →
`EventInvasionMob`. Renommage de noms d'enum uniquement, ordinaux (0/1/2/4/6) tous inchangés.

### `Data/Content/WorldBossData.cs` — perd le timer, gagne une récompense par boss

```csharp
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
    public List<WorldBossEntry> possibleBosses = new List<WorldBossEntry>();

    [Header("Résolution")]
    public float despawnTimeout = 1800f; // 30 min

    public override string DisplayName => "Boss Géant";

    private GameObject _aliveBossObj;
    private Mob         _aliveMob;
    private bool        _resolved;
    private bool        _killedFlag;

    protected override bool OwnsMob(Entity target) => target != null && ReferenceEquals(target, _aliveMob);

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

        if (SceneManager.GetActiveScene().name != targetMap.sceneName) yield break; // personne présent, event annulé

        if (entry.boss.prefab == null)
        {
            Debug.LogWarning($"[WorldBossData] {entry.boss.mobName} n'a pas de prefab — événement annulé.");
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
        // lui-même — bug trouvé en relecture (2026-09-30) : Mob.Die() (synchrone, déclenché par
        // TakeDamage) appelle onDeathCallback AVANT que SkillSystem, dans son appelant, publie le
        // DamageDealtEvent de CE MÊME coup. Résoudre directement dans le callback (StopTracking
        // immédiat) désabonnait le tracking avant que le coup fatal soit compté — le joueur qui
        // achève le boss pouvait être exclu de eligiblePlayers si ce coup le faisait franchir
        // minHitsToBeEligible. En ne faisant que positionner _resolved/_killedFlag dans le
        // callback, la résolution réelle n'arrive qu'à la reprise de la coroutine (frame
        // suivante au plus tôt) — le DamageDealtEvent du coup fatal est alors déjà traité.
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
                if (entry.boss != null && entry.boss.mobType != MobType.EventGiantBoss)
                    Debug.LogWarning($"[WorldBossData] {name} : {entry.boss.mobName} a mobType = " +
                        $"{entry.boss.mobType}, attendu EventGiantBoss pour un Boss Géant.");

        SyncEligibleMapsSceneNames();
    }
#endif
}
```

Le champ `_scheduler` de la première version disparaît — plus nécessaire : `RunEvent` a déjà
`scheduler` en paramètre pour son propre `StopCoroutine`, la résolution ne passe plus par un
callback externe qui en aurait besoin séparément.


### `Data/Content/InvasionData.cs` — nouveau, système de vagues

```csharp
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
    [Tooltip("Nom d'affichage/organisation — ex: Feu, Eau, Corruption... Sert aussi dans l'annonce.")]
    public string variantName = "Invasion";

    [Tooltip("Exactement 5 vagues attendues (pas de blocage dur si différent, juste la convention).")]
    public List<InvasionWave> waves = new List<InvasionWave>();

    [Tooltip("Repop en boucle sur le même timer tant que le boss reste vivant après son apparition.")]
    public List<InvasionMobEntry> reinforcements = new List<InvasionMobEntry>();

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
    [Tooltip("Délai entre 2 vagues ET entre 2 vagues de renfort (secondes) — 1-2 min recommandé.")]
    public float waveInterval = 90f;

    public override string DisplayName => "Invasion";

    private readonly List<Mob> _aliveMobs = new List<Mob>();
    private Mob  _bossMob;
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
        _bossMob  = null;
        _bossDead = false;

        // ── 5 vagues fixes — pop sur le timer, n'attendent PAS que la précédente soit clear ──
        foreach (InvasionWave wave in variant.waves)
        {
            SpawnWave(wave.mobs, anchor, targetMap.palier);
            yield return new WaitForSeconds(waveInterval);
        }

        // ── Boss — spawn une seule fois, après la dernière vague ──
        if (variant.boss != null && variant.boss.prefab != null)
        {
            Vector3 bossPos = anchor; // même zone que les vagues — voir GetRandomPointInRadius
            GameObject bossObj = Instantiate(variant.boss.prefab, GetRandomPointInRadius(anchor), Quaternion.identity);
            _bossMob = bossObj.GetComponent<Mob>();
            _bossMob?.InitializeSpawn(variant.boss, targetMap.palier);
            if (_bossMob != null) _aliveMobs.Add(_bossMob);
            AnnoncePanel.Instance?.Announce($"Le boss de l'invasion est apparu sur le Palier {targetMap.palier} !");
            _bossMob?.OnDeath(() => _bossDead = true);
        }
        else
        {
            _bossDead = true; // pas de boss configuré — n'attend pas indéfiniment
        }

        // ── Renforts en boucle tant que le boss est vivant ──
        while (!_bossDead)
        {
            yield return new WaitForSeconds(waveInterval);
            if (_bossDead) break;
            SpawnWave(variant.reinforcements, anchor, targetMap.palier);
        }

        // ── Boss mort — plus aucun spawn, attendre que tout le reste meure ──
        _aliveMobs.RemoveAll(m => m == null);
        yield return new WaitUntil(() =>
        {
            _aliveMobs.RemoveAll(m => m == null || m.isDead);
            return _aliveMobs.Count == 0;
        });

        StopTracking();
        AnnoncePanel.Instance?.Announce($"L'invasion ({variant.variantName}) du Palier {targetMap.palier} a été repoussée !");
        GrantEventRewards(variant.rewardTable);
    }

    private void SpawnWave(List<InvasionMobEntry> mobs, Vector3 anchor, int palier)
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
        if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, spawnRadius, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return center; // repli sur l'ancre si le sample échoue
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleVariants != null)
        {
            foreach (var variant in possibleVariants)
            {
                if (variant.boss != null &&
                    (variant.boss.mobType != MobType.EventInvasionMob || variant.boss.invasionRole != InvasionRole.Boss))
                    Debug.LogWarning($"[InvasionData] {name} : {variant.boss.mobName} a mobType = " +
                        $"{variant.boss.mobType}/invasionRole = {variant.boss.invasionRole}, attendu " +
                        "EventInvasionMob + InvasionRole.Boss pour un boss d'invasion.");

                if (variant.waves != null)
                    foreach (var wave in variant.waves)
                        WarnIfNotInvasionTrash(wave.mobs);

                WarnIfNotInvasionTrash(variant.reinforcements);
            }
        }

        SyncEligibleMapsSceneNames();
    }

    private void WarnIfNotInvasionTrash(List<InvasionMobEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
            if (entry.mob != null &&
                (entry.mob.mobType != MobType.EventInvasionMob || entry.mob.invasionRole != InvasionRole.Normal))
                Debug.LogWarning($"[InvasionData] {name} : {entry.mob.mobName} a mobType = " +
                    $"{entry.mob.mobType}/invasionRole = {entry.mob.invasionRole}, attendu " +
                    "EventInvasionMob + InvasionRole.Normal pour un mob de vague/renfort.");
    }
#endif
}
```

**Décisions prises pour combler les détails non explicitement tranchés par Florian, à valider
en relisant ce spec** :
- Pas de boss configuré sur une variante → `_bossDead = true` immédiatement (évite un `WaitUntil`
  infini) plutôt qu'un blocage — variante mal configurée, l'event se termine vite plutôt que de
  soft-lock le scheduler.
- `waveInterval` par défaut 90s (1min30, milieu de la fourchette 1-2min donnée par Florian) —
  seul champ de cadence, réutilisé pour l'intervalle entre vagues fixes ET entre vagues de
  renfort (une seule notion de rythme, pas deux valeurs séparées à retenir).
- Si le sample NavMesh échoue pour un point de spawn individuel, repli sur le point d'ancrage
  lui-même plutôt que d'annuler ce spawn précis (dégradation gracieuse, pas de perte de mob).

## Flux de données (Invasion)

```
WorldEventScheduler (timer partagé écoulé)
  → tire InvasionData dans eventPool
  → InvasionData.RunEvent :
      tire palier + variante (ex: Feu)
      annonce T-5min / T-1min (offsets du scheduler)
      scène active == palier tiré ? NON → fin, rien ne spawn
      OUI → point d'ancrage aléatoire (NavMesh)
            annonce "invasion commencée"
            StartTracking() (abonnement OnDamageDealt)
            pour chaque vague (5) : spawn mobs autour de l'ancre, attendre waveInterval (sans clear)
            spawn boss (une fois)
            tant que boss vivant : spawn renforts toutes les waveInterval
            boss mort → plus de spawn → attendre que _aliveMobs soit vide
            StopTracking()
            annonce succès
            GrantEventRewards(variante.rewardTable) → eligiblePlayers accumulés
```

## Gestion d'erreur

- `eventPool` vide → scheduler inerte, warning répété toutes les 5s, jamais de crash.
- `possibleBosses`/`possibleVariants`/`eligibleMaps` vide sur l'event tiré → ce cycle est ignoré
  (`yield break`), le scheduler reboucle sur un nouveau tirage au prochain intervalle.
- Prefab manquant sur un boss/mob → warning, ce spawn précis est sauté (mob/boss non instancié),
  jamais de crash. Pour l'Invasion, un boss manquant termine la boucle de renforts immédiatement
  plutôt que de bloquer indéfiniment.
- Aucun NavMesh baké → warning, événement annulé pour ce cycle.
- Joueur absent du palier tiré au moment T-0 → aucun spawn, pas d'erreur, juste un cycle "pour
  rien" (déjà le comportement de Boss Géant, inchangé).

## Vérification (Play Mode manuel)

1. Configurer `WorldEventScheduler` : timer réduit pour tester (`minInterval`/`maxInterval` +
   les 2 offsets), `eventPool` = [un `WorldBossData`, un `InvasionData`] tous les deux remplis.
2. Relancer plusieurs cycles rapprochés → confirmer que les DEUX types sortent au hasard (pas
   toujours le même), que chaque type formule sa propre annonce (texte différent), et que
   plusieurs bosses/variantes différents sortent du pool au fil des essais.
3. World Boss : confirmer qu'un `WorldBossEntry` spécifique amène bien SA `rewardTable` propre
   (2 boss différents avec 2 tables différentes, vérifier laquelle est distribuée selon lequel
   est tiré).
4. Invasion : rester sur le palier tiré, observer les 5 vagues pop sur le timer SANS attendre
   que la précédente soit clear (des mobs de vagues différentes doivent coexister), confirmer le
   boss spawn une seule fois après la vague 5.
5. Ne PAS tuer le boss tout de suite → confirmer que les renforts continuent de pop toutes les
   `waveInterval` tant qu'il est vivant.
6. Tuer le boss puis laisser des mobs de vague en vie → confirmer qu'aucun nouveau spawn
   n'intervient, et que la récompense n'est distribuée qu'une fois le DERNIER mob mort, pas à la
   mort du boss.
7. Confirmer qu'un joueur ayant atteint `minHitsToBeEligible` (10) coups sur des mobs de vague
   UNIQUEMENT (jamais le boss lui-même) reçoit bien la récompense finale.
8. Confirmer qu'un joueur passé rapidement (moins de 10 coups, ex: 3) sur un mob de l'événement
   n'est PAS dans la liste récompensée — le seuil filtre bien les participants insuffisants.
9. Confirmer le fix de la race condition World Boss : configurer un boss avec peu de HP, faire en
   sorte que le coup qui l'achève soit EXACTEMENT le 10e coup du joueur (celui qui le fait
   franchir `minHitsToBeEligible`) — confirmer que la récompense est bien distribuée (avant le
   fix, ce coup precis pouvait être perdu par la course entre `Mob.Die()`/`OnDeath` et la
   publication de `DamageDealtEvent`, voir §WorldBossData).
10. Confirmer la suppression de `massEventRewards` : un mob normal (non lié à un événement) avec
    plusieurs items droppés continue de tirer un gagnant indépendant par item (comportement
    d'avant-hier, pas le tirage sans remise).
