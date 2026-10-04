# PNJ Ambulant (Patrol Route) — Design

## Contexte

Florian veut améliorer les PNJ avec un nouveau comportement : un PNJ qui marche d'un Point A
vers un Point B (et au-delà si plus de points), en traversant potentiellement un biome entier.
Ce PNJ peut être un marchand itinérant qu'on escorte, un garde qui patrouille un chemin fixe, ou
tout autre PNJ qu'on veut voir bouger dans le monde plutôt que rester planté à un seul endroit.

Besoins explicites (reformulés depuis la conversation) :
- Une liste de points de passage assignée en glissant des Transforms de scène (pas des assets)
  sur l'exemplaire du PNJ — chaque PNJ placé dans une map a sa propre route.
- Le PNJ avance vers son prochain point en continu, indépendamment de la position du joueur
  (pas un "escort" qui attend — il marche à son rythme, le joueur choisit de le suivre/protéger
  ou pas).
- Il peut se faire attaquer. S'il a `canFight = true`, il riposte **exactement comme un Garde
  aujourd'hui** (même `CombatAIController`, mêmes skills/cooldowns/chase) — pas de système de
  combat distinct à construire. S'il a `canFight = false`, il encaisse sans riposter et continue
  d'avancer.
- Une fois le combat terminé, il reprend sa marche là où il en était — pas de retour forcé vers
  le Point A.
- Si le joueur lui parle, il s'arrête de marcher pendant le dialogue. Un dégât reçu pendant le
  dialogue le coupe immédiatement et bascule sur le combat.
- Arrivé au bout de sa route, deux comportements possibles (configurables par PNJ) : boucle en
  marchant en va-et-vient, ou attend puis disparaît/réapparaît au Point A pour recommencer.
- L'arrivée au point final doit pouvoir déclencher une récompense de quête plus tard — exposé ici
  comme un simple point d'accroche (événement), pas une vraie intégration QuestData (hors scope,
  la revue dialogue/quest-giver/quête multi-step viendra dans une session dédiée séparée).

**Contrainte explicite de Florian** : la marche en route (« ambulant ») et la déambulation
aléatoire existante autour du spawn (« patrouille », l'actuel état `Patrol` de
`CombatAIController` pour les Gardes) doivent rester **deux systèmes séparés**. Le fichier
partagé `Combat/CombatAIController.cs` (utilisé par Mob ET PNJ) ne doit subir **aucune
modification** — toute la logique de route vit exclusivement dans `Entities/PNJ.cs`, qui se
contente d'utiliser l'API publique déjà existante de `CombatAIController` (`Initialize()`,
`CurrentState`) sans jamais y toucher.

## État actuel du code (vérifié avant ce design)

- `Data/PNJ/PNJData.cs` : asset SO partagé. Tout ce qui concerne le mouvement (NavMeshAgent,
  `walkClip`/`chaseClip`, `patrolRadius`, `leashRadius`, `aiType`) est aujourd'hui conditionné à
  `canFight = true` — un PNJ non-combattant n'a aucun moyen de se déplacer.
- `Entities/PNJ.cs` : composant scène. `Awake()` n'ajoute `CombatEntityAnimatorController` +
  `CombatAIController` + ne configure le `NavMeshAgent` que si `data.canFight`. `Interact()`
  ouvre un dialogue sans aucune condition liée au combat. `Die()`/`RespawnCoroutine()` existent
  déjà (respawn à `_spawnPos`, capturé une fois dans `Awake()`).
- `Combat/CombatAIController.cs` : machine à 3 états (`Patrol`/`Engage`/`Return`) partagée
  Mob/PNJ. L'état `Patrol` actuel = déambulation ALÉATOIRE dans `PatrolRadius` autour du spawn —
  sémantique différente d'une marche dirigée A→B. `Initialize(owner, agent, skillSystem,
  profile, animator, spawnPos)` est **publique** et ne fait que des assignations de champs (pas
  d'effet de bord) — rappelable à tout moment sans risque.
- Aucun pattern existant de liste de Transforms de scène assignée dans l'Inspector ailleurs dans
  le projet — ce sera le premier.
- `ShowIfPropertyDrawer` est un `PropertyDrawer` Unity standard — fonctionne sur n'importe quel
  champ sérialisé, MonoBehaviour compris (pas réservé aux ScriptableObject malgré son usage
  actuel uniquement sur des SO).

## Design

### Où vivent les données — `PNJ.cs`, pas `PNJData.cs`

Une route est propre à l'exemplaire placé dans une map précise — deux PNJ partageant le même
`PNJData` (ex: deux marchands du même type) peuvent avoir des trajets complètement différents
selon la map. `PNJData` (asset SO) ne change pas du tout pour cette fonctionnalité.

Nouveaux champs sur `PNJ.cs` :

```csharp
public enum PatrolEndBehavior { LoopBackAndForth, WaitThenRespawnAtStart, StopAtEnd }

[Header("Patrol Route (PNJ ambulant)")]
[Tooltip("Active la marche dirigée A→B→... — système INDÉPENDANT de la patrouille de combat\n" +
         "(CombatAIController.Patrol, réservée aux Gardes). Glisser des GameObjects de la SCÈNE\n" +
         "(pas des prefabs/assets) dans patrolPoints ci-dessous — au moins 2 points requis.")]
public bool isPatrolRoute = false;

[ShowIf(nameof(isPatrolRoute), true)]
public List<Transform> patrolPoints = new List<Transform>();

[ShowIf(nameof(isPatrolRoute), true)]
public PatrolEndBehavior endBehavior = PatrolEndBehavior.LoopBackAndForth;

[Tooltip("Attente au dernier point avant de disparaître (WaitThenRespawnAtStart uniquement).")]
[ShowIf(nameof(endBehavior), PatrolEndBehavior.WaitThenRespawnAtStart)]
public float waitAtEndSeconds = 5f;

[Tooltip("Attente au Point A, disparu, avant de réapparaître et recommencer.")]
[ShowIf(nameof(endBehavior), PatrolEndBehavior.WaitThenRespawnAtStart)]
public float waitAtStartSeconds = 5f;
```

Vitesse de marche : réutilise `data.baseMoveSpeed` (déjà présent sur `PNJData`, pas réservé au
combat) — pas de nouveau champ de vitesse.

### Attache des composants — `PNJ.Awake()`

Condition de mouvement élargie : un PNJ a besoin d'un `NavMeshAgent` + animator combat s'il peut
se battre OU s'il a une route, pas seulement `canFight` comme aujourd'hui.

```csharp
bool needsMovement = (data != null && data.canFight) || isPatrolRoute;
if (needsMovement)
{
    _animatorController = GetComponent<CombatEntityAnimatorController>();
    if (_animatorController == null)
        _animatorController = gameObject.AddComponent<CombatEntityAnimatorController>();

    _agent = GetComponent<NavMeshAgent>();   // doit déjà exister sur le prefab (même exigence qu'aujourd'hui pour canFight)
    _agent.speed = data.combatMoveSpeed > 0f ? data.combatMoveSpeed : data.baseMoveSpeed;
    SetMoveSpeed(data.baseMoveSpeed);
}

if (data != null && data.canFight)
{
    _combatAI = gameObject.AddComponent<CombatAIController>();
    _combatAI.Initialize(this, _agent, _skillSystem, this, _animatorController, _spawnPos);
}

if (isPatrolRoute && patrolPoints.Count < 2)
    Debug.LogWarning($"[PNJ] {name} : isPatrolRoute = true mais moins de 2 points assignés.");
if (isPatrolRoute && data != null && data.walkClip == null)
    Debug.LogWarning($"[PNJ] {name} : isPatrolRoute = true mais data.walkClip non assigné.");
```

**Migration requise côté Unity (pas du code)** : un PNJ non-combattant qui devient ambulant doit
avoir un `NavMeshAgent` posé manuellement sur son prefab — jusqu'ici seuls les PNJ `canFight`
en avaient besoin.

### La marche — entièrement dans `PNJ.cs`, zéro connaissance de `CombatAIController`

Nouveaux champs privés :

```csharp
private int  _routeIndex     = 0;
private int  _routeDirection = 1;     // +1 ou -1, pour LoopBackAndForth
private bool _routeWaiting   = false; // pendant WaitThenRespawnAtStart
```

Appelé depuis `Update()`, juste avant/après `_combatAI.Tick()` :

```csharp
private void TickPatrolRoute()
{
    if (!isPatrolRoute || patrolPoints.Count < 2) return;
    if (IsTalking) return;                                    // dialogue gèle la marche
    if (_combatAI != null && _combatAI.CurrentState != CombatAIState.Patrol) return; // combat/retour en cours
    if (_routeWaiting) return;                                 // attente WaitThenRespawnAtStart

    Transform current = patrolPoints[_routeIndex];
    if (current == null) return;

    // Ré-ancre le "chez-soi" du combat sur la position actuelle — PAS le Point A d'origine.
    // Initialize() est publique, ne fait que des assignations de champs : rappel sans risque.
    // CombatAIController ne sait RIEN de la route, juste "voici où revenir après un combat".
    _combatAI?.Initialize(this, _agent, _skillSystem, this, _animatorController, transform.position);

    if (_agent != null) _agent.SetDestination(current.position);

    if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
        AdvanceRoute();
}

private void AdvanceRoute()
{
    bool atEnd   = _routeDirection > 0 && _routeIndex == patrolPoints.Count - 1;
    bool atStart = _routeDirection < 0 && _routeIndex == 0;

    if (atEnd)
    {
        OnReachedRouteEnd?.Invoke(this);   // hook quest, voir plus bas

        switch (endBehavior)
        {
            case PatrolEndBehavior.LoopBackAndForth:
                _routeDirection = -1;
                _routeIndex += _routeDirection;
                break;
            case PatrolEndBehavior.StopAtEnd:
                isPatrolRoute = false;   // s'arrête définitivement, plus jamais réévalué
                break;
            case PatrolEndBehavior.WaitThenRespawnAtStart:
                StartCoroutine(WaitThenRespawnRoutine());
                break;
        }
        return;
    }

    if (atStart)
    {
        // Seulement atteignable par LoopBackAndForth (seul cas où _routeDirection devient
        // négatif) — repart vers +1. StopAtEnd/WaitThenRespawnAtStart ne reculent jamais,
        // cette branche ne les concerne pas.
        _routeDirection = 1;
        _routeIndex += _routeDirection;
        return;
    }

    _routeIndex += _routeDirection;
}
```

Trace de vérification sur une route à 3 points (A=0, B=1, C=2) : index visité après chaque
arrivée = 0(A)→1(B)→2(C, atEnd, flip)→1(B)→0(A, atStart, flip)→1(B)→2(C, atEnd, flip)→... —
va-et-vient propre, jamais d'index hors limites.

`WaitThenRespawnRoutine()` réutilise le MÊME style que `RespawnCoroutine()` existant (cacher
renderers/colliders, attendre, réapparaître) mais déclenché par l'ARRIVÉE, pas par la mort :

```csharp
private IEnumerator WaitThenRespawnRoutine()
{
    _routeWaiting = true;
    yield return new WaitForSeconds(waitAtEndSeconds);

    foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    if (_agent != null) _agent.enabled = false;

    yield return new WaitForSeconds(waitAtStartSeconds);

    _routeIndex     = 0;
    _routeDirection = 1;
    transform.position = patrolPoints[0].position;

    foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = true;
    foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;
    if (_agent != null) { _agent.enabled = true; _agent.Warp(patrolPoints[0].position); }

    _routeWaiting = false;
}
```

### Intégration dans `PNJ.Update()`

`Update()` a aujourd'hui un early-return `if (data == null || !data.canFight) return;` qui
couperait court avant d'atteindre quoi que ce soit — un PNJ ambulant NON-combattant ne doit PAS
dépendre de `canFight` pour marcher. `TickPatrolRoute()` doit s'exécuter AVANT ce garde ; tout le
reste (poll de canalisation, liste d'ennemis, freeze CC, `_combatAI.Tick()`) reste conditionné à
`canFight` exactement comme aujourd'hui, inchangé :

```csharp
protected override void Update()
{
    base.Update();
    if (isDead) return;

    TickPatrolRoute();   // indépendant de canFight — doit tourner même pour un PNJ passif

    if (data == null || !data.canFight) return;   // tout ce qui suit = combat, inchangé

    _combatAI.PollChannelInterrupt();
    RefreshEnemyList();
    if (IsDashing) return;

    bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping ||
                 statusEffects.isShocked || statusEffects.isFreezed);
    if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = isCCd;
    if (isCCd) return;

    _combatAI.Tick();
}
```

### Hook quest (déféré)

```csharp
/// <summary>Déclenché à chaque arrivée au dernier point de la route (une fois par boucle pour
/// LoopBackAndForth, une fois par cycle pour WaitThenRespawnAtStart). Pas encore consommé —
/// point d'accroche pour une future récompense de quête (QuestSystem), à brancher lors de la
/// revue dialogue/quest-giver/quête multi-step.</summary>
public event System.Action<PNJ> OnReachedRouteEnd;
```

### Combat — `CombatAIController` INTOUCHÉ

Aucune ligne modifiée dans `Combat/CombatAIController.cs`. Le flux complet (déjà validé avec
Florian via le schéma texte) :

1. **Marche** : `TickPatrolRoute()` avance l'agent tant que `CombatAIController` (s'il existe)
   est en état `Patrol`, pas en dialogue. Ré-ancre `_combatAI`'s `_spawnPos` interne sur la
   position courante à chaque tick via `Initialize()`.
2. **Combat** : un ennemi détecté (ou `TakeDamage()` → `ForceEngage()`, inchangé) fait basculer
   `CombatAIController` en `Engage` — géré 100% par le code existant, identique à un Garde.
   `TickPatrolRoute()` se tait tout seul (sa garde `CurrentState != Patrol` l'arrête).
3. **Return** : ennemi mort/parti → `CombatAIController.Return` (inchangé) marche vers son
   `_spawnPos` interne — qui est le DERNIER point ré-ancré par la marche, pas le Point A
   d'origine, grâce à l'étape 1. Arrivé → repasse en `Patrol`.
4. **Reprise** : `TickPatrolRoute()` redevient actif (condition `CurrentState == Patrol` de
   nouveau vraie) et continue vers `patrolPoints[_routeIndex]` — index inchangé pendant tout le
   combat, donc pas de régression de progression sur la route.

Pour `canFight = false` : pas de `CombatAIController` du tout (comme aujourd'hui), la condition
`_combatAI != null && ...` dans `TickPatrolRoute()` devient `_combatAI == null` → toujours vrai →
la marche ne s'arrête jamais pour cause de combat (il n'y en a pas), seulement pour le dialogue.
`TakeDamage()` continue d'appliquer les dégâts normalement (mort possible si `canDie`), sans
jamais engager de combat.

### Dialogue — pause + interruption par combat

Deux ajouts dans `PNJ.cs`, aucun dans `DialogueUI.cs` :

1. **`Interact()`** — refuse d'ouvrir un dialogue si le PNJ est en plein combat :
   ```csharp
   if (_combatAI != null && _combatAI.CurrentState == CombatAIState.Engage) return;
   ```
2. **`TakeDamage()`** — un coup reçu pendant un dialogue le ferme immédiatement, avant la logique
   `ForceEngage()` existante :
   ```csharp
   public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
   {
       base.TakeDamage(amount, sourceElement, source);
       if (IsTalking) EndDialogue();
       if (!isDead && data != null && data.canFight)
           _combatAI.ForceEngage(source);
   }
   ```
   S'applique que `canFight` soit vrai ou faux — même un PNJ passif qui encaisse sans riposter
   voit son dialogue coupé par un coup reçu.

La pause de marche pendant le dialogue est déjà couverte par la garde `if (IsTalking) return;`
en tête de `TickPatrolRoute()` — rien de plus à faire côté `DialogueUI`.

### Gizmo (confort édition, pas fonctionnel)

`OnDrawGizmosSelected()` existant gagne un tracé de la route (ligne entre points consécutifs,
sphère à chaque point) quand `isPatrolRoute` — même esprit que les cercles aggro/engage déjà
dessinés, aide Florian à poser ses points dans l'éditeur sans deviner.

## Portée volontairement exclue (pour cette session)

- Toute intégration réelle avec `QuestData`/`QuestSystem` pour la récompense à l'arrivée — seul
  l'événement `OnReachedRouteEnd` est posé, rien d'autre ne l'écoute encore.
- Dialogue multi-step, quest-giver, conditions de prérequis — hors scope, revue séparée prévue.
- Vrai mode "escort" dépendant de la distance au joueur — explicitement refusé par Florian pour
  cette itération (le PNJ marche à son rythme, indépendant du joueur).
- Patrouille existante des Gardes (`CombatAIController.Patrol`, déambulation aléatoire) —
  totalement inchangée, aucune ligne touchée.

## Vérification

Pas de framework de test automatisé — vérification manuelle en Play Mode par Florian :

1. Compiler, 0 erreur.
2. Créer 2-3 GameObjects vides dans une scène, les glisser dans `patrolPoints` d'un PNJ
   `isPatrolRoute = true`, `canFight = false` → vérifier qu'il marche de point en point sans agir
   de façon étrange au premier lancement.
3. Lui parler en cours de route → vérifier qu'il s'arrête, et reprend la marche à la fermeture du
   dialogue (sans sauter de point).
4. Passer `canFight = true` avec un `basicAttackSkill` assigné, faire approcher un mob agressif →
   vérifier qu'il se bat comme un Garde (chase/skills), puis reprend sa marche vers son point
   actuel après le combat — SANS retourner au Point A.
5. Cas limite : combat déclenché alors qu'un dialogue était ouvert → vérifier que le dialogue se
   ferme tout seul et que le PNJ passe directement au combat.
6. Tester `endBehavior = LoopBackAndForth` sur une route à 2 points → va-et-vient infini observé.
7. Tester `endBehavior = WaitThenRespawnAtStart` → attente au bout, disparition, réapparition au
   Point A après le délai, reprise de la marche depuis le début.
8. Vérifier qu'un PNJ Garde existant (`isPatrolRoute = false`) n'a AUCUN changement de
   comportement (patrouille aléatoire inchangée) — confirme que les deux systèmes sont bien
   restés séparés.
