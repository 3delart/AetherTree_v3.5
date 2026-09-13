# CombatAIController — IA de combat partagée Mob/PNJ

**Statut** : Approuvé par Florian (chat), en attente de plan d'implémentation.
**Repo** : AetherTree v3.5 (Unity C#, solo-dev, pas de framework de test automatisé —
vérification manuelle en Play Mode uniquement).

## 1. Contexte

`Mob.cs` utilise une machine à états à 4 valeurs (`Patrol`/`Chase`/`Attack`/`Return`) où
`HandleChase()` et `HandleAttack()` sont deux méthodes séparées qui calculent CHACUNE leurs
propres seuils de distance pour décider des transitions. Cette duplication a produit, au cours
d'une seule session de debug manuel, une chaîne de bugs de la même famille :

- oscillation Attack↔Chase à chaque frame (deux méthodes utilisant deux seuils différents pour
  la même transition)
- `agent.ResetPath()` oublié à un point de sortie sur 5+ (Mob se retrouve bloqué en "running")
- vélocité résiduelle du NavMeshAgent (ResetPath ne suffit pas, il faut aussi forcer
  `agent.velocity = Vector3.zero`) — oublié au même genre d'endroits
- hystérésis sur `basicRange` nécessaire pour éviter un flapping SetDestination/ResetPath en
  continu à la frontière exacte du seuil — dupliquée intégralement entre Mob.cs et PNJ.cs

`PNJ.cs` (`HandleCombatAI()`) résout le même problème fonctionnel (patrouille/idle → chasse →
engage au corps à corps ou à distance) en UNE seule méthode de décision linéaire par frame, sans
état persisté séparé pour "chasse" vs "attaque" — structurellement incapable de produire la
classe de bug ci-dessus. Les deux fichiers dupliquent par ailleurs presque à l'identique (~250
lignes quasi copiées-collées) : le pending-hit (hit-frame-sync), la canalisation
(castTime > 0), le tick des cooldowns de skills secondaires, et `GetMaxSkillRange()`.

Décision de Florian : le schéma de comportement doit être IDENTIQUE entre Mob et PNJ (Garde) —
idle/déplacement en zone, ou chasse vers la cible / engage avec skill spécial en priorité sinon
attaque de base. Le Garde doit en plus gagner une patrouille (actuellement il reste immobile à
son spawn hors combat).

## 2. Objectifs

- Extraire un composant unique `CombatAIController` portant la boucle IA de combat complète
  (Patrol → Engage → Return) et toute la machinerie skill (pending-hit, canalisation, cooldowns,
  `GetMaxSkillRange`), utilisé identiquement par `Mob` et `PNJ`.
- Éliminer structurellement la classe de bug "deux seuils désynchronisés" en fusionnant
  Chase+Attack en un seul état `Engage`, décision reprise à chaque frame (pas de sous-état
  persisté).
- Centraliser le pattern `agent.ResetPath() + agent.velocity = Vector3.zero` dans une seule
  méthode (`StopAgent()`), pour ne plus jamais l'oublier à un nouveau point de sortie.
- Ajouter la patrouille au PNJ Garde (roam dans un rayon autour du spawn, `patrolRadius = 0` =
  reste immobile — comportement actuel préservé par défaut).
- Ajouter la distinction Passive/Aggressive au PNJ Garde, identique à Mob (`aiType`, réutilise
  `MobAIType`, défaut `Aggressive` = comportement actuel préservé) — un Garde Passive n'engage un
  mob que s'il se fait taper par lui.

## 3. Non-objectifs

- Ne touche PAS à la résolution des skills elle-même (`SkillSystem.Execute`/`ResolveExecute`/
  `PlantDelayedZone`/`StartTrajectory`/`ResolveMultiHitStep`) — la machinerie pending-hit/
  canalisation est RAPATRIÉE telle quelle dans le composant partagé, pas réécrite.
- Ne touche PAS au loot (`Mob.Die()` — `damageContributions`/`totalDamageTaken`/
  `MobKilledEvent`), ni au respawn PNJ (`PNJ.Die()`/`RespawnCoroutine()`), ni au système de
  dialogue PNJ.
- Ne touche PAS aux Animator Controllers (graphe, transitions, Exit Time) — chantier séparé en
  cours. Seule une interface commune minimale est ajoutée sur `MobAnimatorController`/
  `PNJAnimatorController` (signatures déjà identiques aujourd'hui, aucun changement de
  comportement).
- Ne renomme aucune API publique consommée ailleurs dans le projet
  (`Mob.CurrentState`/`MobState` restent, voir §7) sauf mention explicite.
- Ne touche pas au ciblage PAR ESPÈCE (qui est ennemi de qui) — Mob cible Players+PNJ, PNJ cible
  Mobs, cette différence reste encapsulée dans un delegate fourni par chaque owner (§4).
  **Exception explicite** : la composition du pool de cibles pour un Mob Passif CHANGE de
  comportement par rapport au GDD écrit — voir §5bis, décision assumée de Florian.

## 4. Architecture

### 4.1 Nouveau fichier : `Combat/CombatAIController.cs`

```csharp
public enum CombatAIState { Patrol, Engage, Return }

public interface ICombatAnimator
{
    void PlayAttack(AnimationClip clip);
    void PlayChannel(AnimationClip clip);
    void CancelChannel();
}

public interface ICombatAIProfile
{
    SkillData       BasicAttackSkill  { get; }
    List<SkillData> SecondarySkills   { get; }
    float           PatrolRadius      { get; }   // 0 = reste au spawn (pas de roam)
    float           LeashDistance     { get; }   // distance absolue depuis l'ancre d'aggro
    bool            AutoEngageOnSight { get; }   // true = passe en Engage dès qu'un ennemi est détecté — Mob/PNJ : aiType == Aggressive (§5bis)
    Entity          FindClosestEnemy();          // taunt-aware, retourne null si aucun candidat — pool dépendant de l'owner, voir §5bis
    bool            HasAnyEnemyNearby();         // check bon marché pour Patrol (seulement appelé si AutoEngageOnSight)
    void            OnForcedEngage(Entity aggressor); // hook owner — voir §5bis (Mob/PNJ y ajoutent aggressor à leur aggroSet)
    void            OnReturnToPatrol();          // hook owner appelé UNE FOIS à l'arrivée au spawn (Return → Patrol) — voir §5bis
}

[RequireComponent(typeof(NavMeshAgent))]
public class CombatAIController : MonoBehaviour
{
    public CombatAIState CurrentState { get; private set; } = CombatAIState.Patrol;
    public bool IsPendingHit  => _pendingSkill != null;
    public bool IsChanneling  => _isChanneling;

    public void Initialize(Entity owner, NavMeshAgent agent, SkillSystem skillSystem,
                            ICombatAIProfile profile, ICombatAnimator animator, Vector3 spawnPos);

    // Appelé depuis Update() de l'owner (Mob/PNJ), APRÈS que l'owner ait géré ses propres
    // early-returns globaux (mort, IsDashing pour Mob, etc.) et APRÈS avoir tické le poll
    // d'interrupt de canalisation (§4.3 — reste piloté par l'owner, PAS par ce composant, car il
    // doit tourner même si un CC dur gèle Tick() lui-même, voir §6.3).
    public void Tick();

    // Force la transition Patrol → Engage indépendamment de AutoEngageOnSight — hook pour
    // Mob.TakeDamage() (aggro sur dégâts, mob Passif) et Mob/PNJ.AggroFrom() (pet attaqué).
    // aggressor peut être null (ex: dégât de zone sans source directe) — dans ce cas la
    // transition a lieu mais rien n'est ajouté à un éventuel aggroSet côté owner (voir §5bis).
    public void ForceEngage(Entity aggressor);

    // Nettoyage pending-hit/canalisation — appelé par Die() de l'owner AVANT de désactiver quoi
    // que ce soit (même ordre que le code actuel : la bar/le vfxCast ne doivent pas rester
    // affichés sur une entité déjà marquée morte).
    public void NotifyDeath();

    // Relais direct depuis MobAnimatorController.OnSkillHitFrame / PNJAnimatorController.OnSkillHitFrame.
    public void OnAnimationHitEvent(int hitIndex = 0);
}
```

`CombatAIController` ne connaît PAS `Mob` ni `PNJ` — seulement `Entity` (classe de base, déjà
publique pour tout ce dont il a besoin : `statusEffects`, `isDead`, `HasMana()`, `SpendMana()`,
`transform`) et `ICombatAIProfile`/`ICombatAnimator` fournis par l'owner à `Initialize()`.

### 4.2 Contenu rapatrié tel quel (aucune logique changée, juste déplacée)

Depuis `Mob.cs`/`PNJ.cs` vers `CombatAIController` :

- Champs : `_pendingSkill`, `_pendingTarget`, `_pendingTimeout`, `_pendingIsMulti`,
  `_pendingMultiNextIndex`, `_isChanneling`, `_channelSkill`, `_channelTarget`,
  `_channelVfxCast`, `_channelBar`, `_skillCooldowns`, `_wasInBasicRange`, et **`attackTimer`/
  `_attackTimer` (Mob/PNJ) → renommé `_basicAttackTimer`** — trou trouvé en écrivant le plan : le
  §5 (Engage, étape 10) en dépend directement pour déclencher l'attaque de base, oublié de cette
  liste dans une version précédente de cette section. **Décision de placement** : `Tick()` tique
  `_basicAttackTimer -= Time.deltaTime` tout en haut, avant le `switch` Patrol/Engage/Return —
  donc il ne décompte QUE quand `Tick()` tourne (jamais pendant un CC dur, puisque l'owner ne
  l'appelle pas dans ce cas, voir §6.3). Différence mineure assumée avec le `Mob.cs` actuel (où
  `attackTimer` décompte dans `Update()` même pendant un CC, contrairement à `_skillCooldowns` qui
  lui ne décompte déjà QUE hors CC) — la migration rend `_basicAttackTimer` cohérent avec
  `_skillCooldowns` au lieu de l'inverse. Effet quasi nul en jeu (décalage d'au plus quelques
  frames sur un CD), ne nécessite pas de validation Play Mode dédiée.
- Méthodes : `StartPendingHit()`, `ResolvePendingHit()`, `StartChannelCast()`,
  `ResolveChannelCast()`, `InterruptChannelCast()`, `EndChannelCastState()`, `TryUseSkill()`
  (renommé `TryUseSecondarySkill()` — nom déjà utilisé côté PNJ, plus clair), `GetMaxSkillRange()`
- Le poll d'interrupt de canalisation (hard CC / cible morte, actuellement dupliqué en tête
  d'`Update()` des deux fichiers) devient une méthode publique `PollChannelInterrupt()` sur
  `CombatAIController`, appelée par l'owner — voir §6.3 pour pourquoi elle reste appelée
  séparément de `Tick()`.

Ces méthodes utilisent aujourd'hui `_skillSystem`/`_animatorController`/`data.skills`/
`data.basicAttackSkill` — remplacés respectivement par le `SkillSystem` passé à `Initialize()`,
`ICombatAnimator`, `profile.SecondarySkills`, `profile.BasicAttackSkill`.

### 4.3 Nouveau helper

```csharp
private void StopAgent()
{
    _agent.ResetPath();
    _agent.velocity = Vector3.zero;
}
```
Remplace les 5 sites `agent.ResetPath(); agent.velocity = Vector3.zero;` actuellement dupliqués
dans `Mob.cs` (3 sites) et `PNJ.cs` (2 sites).

## 5. Machine à états unifiée

```
Patrol ──(AutoEngageOnSight && HasAnyEnemyNearby()) || IsTauntedWithValidSource() || ForceEngage(aggressor)──▶ Engage
Patrol : PatrolRadius == 0 ? reste immobile au spawn : roam (GetNavMeshPoint + wait 2-5s), identique à Mob.HandlePatrol() actuel.

Engage ──(FindClosestEnemy() == null)──▶ Return
Engage ──(distance(spawn-anchor) > LeashDistance)──▶ Return
Engage : décision RE-CALCULÉE À CHAQUE FRAME, aucun sous-état persisté — reprend exactement
         PNJ.HandleCombatAI() actuel :
  1. si _isChanneling : freeze complet (aucun mouvement/décision), StopAgent() une fois à l'entrée
  2. target = FindClosestEnemy() (délégué, taunt-aware)
  3. dist = distance(this, target)
  4. si dist > GetMaxSkillRange() : SetDestination(target), _wasInBasicRange = false, return
  5. LookAt(target)
  6. skill secondaire (sauf Taunt actif) : TryUseSecondarySkill(target) — si lancé, StopAgent(), return
  7. basicRange = BasicAttackSkill.range (fallback 2f) ; seuil de SORTIE = basicRange × 1.15 si
     _wasInBasicRange (hystérésis, voir Global Constraints)
  8. si dist > seuil : SetDestination(target), _wasInBasicRange = false, return
  9. _wasInBasicRange = true, StopAgent()
  10. si attackTimer <= 0 && !IsPendingHit && !_isChanneling : déclenche l'attaque de base
      (StartChannelCast si castTime > 0, sinon StartPendingHit), attackTimer = cooldown

Return ──(distance(spawn) <= 2f)──▶ Patrol (agent.Warp(spawn), profile.OnReturnToPatrol(), reset patrouille)
Return : SetDestination(spawn) tant que non arrivé.
```
`OnReturnToPatrol()` est appelé une seule fois, juste après `agent.Warp(spawn)`, avant de
repasser en `Patrol` — Mob y fait tout ce que faisait `FullReset()` (HP/Mana 100%, `enemyList`
ET `aggroSet` vidés, debuffs nettoyés, etc., voir §6.1) ; PNJ n'y fait QUE vider `aggroSet` et
`_skillCooldowns` (§6.5bis — pas de reset HP/Mana instantané pour un PNJ, décision Florian :
`baseRegenHP` suffit, aucun code actuel ne fait de "FullReset PNJ").

Statuts spéciaux gérés AVANT l'entrée dans cette machine (par l'owner, voir §6.3 — comportement
Mob actuel généralisé aux deux) :
- `isRooted` : `StopAgent()`, aucune décision (equivalent HandleChase actuel)
- `isFeared` : fuite (direction opposée à la cible la plus proche), pas de décision skill
- CC dur (`isStunned`/`isSleeping`/`isShocked`/`isFreezed`) : gel complet, `agent.isStopped = true`

## 5bis. Modèle de ciblage Passif / Agressif — DÉVIATION ASSUMÉE vs GDD §3.7 écrit

**Le GDD (§3.7 "Règles de Perception") décrit aujourd'hui** : `enemyList` = scan de proximité
(`detectionRange`, Joueurs+Pets+PNJ) pour TOUS les mobs sans distinction Passif/Agressif ; la
SEULE différence entre les deux types est le déclencheur d'entrée en Chase (spontané dès qu'un
ennemi est détecté pour Aggressive ; uniquement sur `TakeDamage` pour Passive) — une fois en
Chase, les deux ciblent "l'entité hostile la plus proche dans enemyList" indifféremment.

**Florian a explicitement demandé un comportement différent** (validé en session, conflit avec
le GDD écrit signalé et confirmé) : un mob **Passif** ne doit cibler QUE les entités qui l'ont
frappé — jamais un bystander non-hostile même plus proche. Un mob **Agressif** garde le scan de
proximité ET mémorise en plus quiconque le frappe (utile si un attaquant sort ensuite de
`detectionRange` en kitant — il reste ciblé). Cette spec implémente la version de Florian ;
**le document GDD devra être mis à jour séparément pour refléter §3.7 correctement** (hors
scope de ce chantier de code).

**Implémentation** — nouveau `HashSet<Entity> aggroSet` sur `Mob` (persistant entre frames,
contrairement à `enemyList` qui est vidée à chaque `RefreshEnemyList()`) :

```csharp
private HashSet<Entity> aggroSet = new HashSet<Entity>();

// Implémente ICombatAIProfile.OnForcedEngage — Mob.cs
public void OnForcedEngage(Entity aggressor)
{
    if (aggressor != null && !aggressor.isDead) aggroSet.Add(aggressor);
}

private void RefreshEnemyList()
{
    enemyList.Clear();
    aggroSet.RemoveWhere(e => e == null || e.isDead);

    if (data.aiType == MobAIType.Aggressive)
    {
        // Scan de proximité existant (Physics.OverlapSphere sur detectionRange, filtre
        // Player/PNJ, exclusion Stealth) — inchangé, alimente enemyList comme aujourd'hui.
        ...
    }

    // Passive : enemyList reste VIDE après le scan (skippé ci-dessus) — seul aggroSet peuple
    // la liste finale, ci-dessous, pour les DEUX types (Aggressive y ajoute ses attaquants
    // sortis de detectionRange en plus de son scan de proximité).
    foreach (Entity e in aggroSet)
        if (!enemyList.Contains(e)) enemyList.Add(e);
}
```

`GetClosestEnemy()` (taunt-aware, choix du plus proche) n'a besoin d'AUCUN changement — il scanne
déjà `enemyList`, qui contient maintenant le bon pool selon `aiType` grâce au changement
ci-dessus. `Mob.TakeDamage()` appelle `_combatAI.ForceEngage(attacker)` (au lieu de
`ForceEngage()` sans argument) pour que l'attaquant soit ajouté à `aggroSet` AVANT la première
évaluation de cible. `FullReset()` doit vider `aggroSet` en plus de `enemyList` (même moment,
retour au spawn — sinon un Mob Passif garderait en mémoire un agresseur d'un combat précédent).

**PNJ Garde — extension demandée par Florian, IDENTIQUE au Mob** : `PNJData` gagne un champ
`aiType` (type `MobAIType`, réutilisé tel quel — Passive/Aggressive/Boss, `Boss` simplement
jamais lu côté PNJ, aucun risque ordinal à partager l'enum). `AutoEngageOnSight` = `data.aiType
== MobAIType.Aggressive` (au lieu du `true` inconditionnel actuel). **Défaut = `Aggressive`** —
critique pour la compatibilité : tout `PNJData` existant doit continuer à se comporter EXACTEMENT
comme avant (scan de proximité + engage spontané), donc le défaut du nouveau champ ne doit
jamais être `Passive`.

PNJ gagne le même `HashSet<Entity> aggroSet` + `OnForcedEngage(Entity aggressor)` que Mob (§5bis
ci-dessus), et `FindClosestEnemy()` devient aiType-aware avec la même règle : Aggressive = scan
de proximité (`Physics.OverlapSphere(aggroRadius)`, code actuel inchangé) ∪ `aggroSet` ; Passive
= `aggroSet` seul. `HasAnyEnemyNearby()` : Aggressive = scan de proximité non vide ; jamais
appelé pour Passive (gated par `AutoEngageOnSight`, comme Mob).

**Nouveau hook nécessaire — `PNJ.TakeDamage()`** : PNJ n'a AUCUN override de `TakeDamage()`
aujourd'hui (contrairement à Mob) — un PNJ frappé par un Mob ne fait rien de spécial, il ne
riposte que si `HandleCombatAI()` retrouve un ennemi par le scan de proximité classique
(fonctionnait par coïncidence tant que l'attaquant était dans `aggroRadius`, comme l'ancien
comportement Mob avant `aggroSet`). Nouvel override, même schéma que `Mob.TakeDamage()` :
```csharp
public override void TakeDamage(float amount, ElementType sourceElement = ElementType.Neutral, Entity source = null)
{
    base.TakeDamage(amount, sourceElement, source);
    if (!isDead && data != null && data.canFight)
        _combatAI.ForceEngage(source);
}
```
Pas d'attribution de contributions ici (PNJ n'a pas de loot/XP à distribuer sur sa propre mort
aux dépens d'un Mob — hors scope, non demandé).

**Taunt — trou trouvé en auto-relecture, corrigé ici** : le code actuel de `HandlePatrol()`
exige `enemyList.Count > 0` MÊME pour la branche taunt (`(aiType == Aggressive || isTaunted) &&
enemyList.Count > 0`) — ça ne posait jamais problème avant `aggroSet`, puisque le scan de
proximité tournait pour TOUS les mobs et un lanceur de Taunt est presque toujours dans
`detectionRange` au moment du cast, donc déjà présent dans `enemyList`. Avec le nouveau modèle
Passif (§5bis, `enemyList` = `aggroSet` uniquement, jamais peuplée par la proximité), un mob
Passif tauntée-mais-jamais-frappé aurait un `aggroSet` vide et resterait bloqué en Patrol — RÉGRESSION
du comportement Taunt actuel. **`IsTauntedWithValidSource()` ci-dessus est donc un check
indépendant d'`ICombatAIProfile`/`enemyList`**, réalisé directement par `CombatAIController` sur
`owner.statusEffects` (`isTaunted` + `GetDebuffSource(DebuffType.Taunt)` non-null et vivant) —
même logique déjà dupliquée aujourd'hui en tête de `GetClosestEnemy()`/`FindClosestEnemy()` côté
Mob/PNJ, réutilisable telle quelle sans dépendre du pool de ciblage.

## 6. Migration Mob.cs / PNJ.cs

### 6.1 `Mob.cs` — ce qui RESTE dans le fichier

- `ApplyData()`, tout `Awake()` (stats, `MobStatCalculator`)
- `RefreshEnemyList()` (aiType-aware, voir §5bis), `GetClosestEnemy()`, `OnForcedEngage()` —
  implémentent `ICombatAIProfile.FindClosestEnemy()`/`HasAnyEnemyNearby()`
  (`enemyList.Count > 0`)/`OnForcedEngage()`
- nouveau champ `aggroSet` (§5bis) — `OnReturnToPatrol()` = corps actuel de `FullReset()` +
  `aggroSet.Clear()` aux côtés de `enemyList.Clear()` (appelé par le composant, plus par un
  `switch` interne)
- `TakeDamage()` — attribution des contributions, PUIS `if (!isDead) _combatAI.ForceEngage(attacker);`
  (remplace l'actuel `currentState = MobState.Chase` conditionnel — `attacker` déjà résolu par
  `ResolveAttacker(source)` juste au-dessus dans le code actuel)
- `Die()` — calcul loot/`MobKilledEvent`, appelle `_combatAI.NotifyDeath()` avant de se désactiver
- `AggroFrom()` — appelle `_combatAI.ForceEngage(attacker)`
- `GetDamageContribution()`, `RegisterLastSkill()`, `IsCaptureable()`, gizmos

### 6.2 `Mob.cs` — ce qui DISPARAÎT (déplacé dans `CombatAIController`)

`HandlePatrol()`, `HandleChase()`, `HandleAttack()`, `HandleReturn()`, `GoReturn()`,
`BeginChase()`, `TryUseSkill()`, `GetMaxSkillRange()`, tout le bloc pending-hit/canalisation
(§4.2), `currentState`/`MobState` en tant que champ de décision (remplacé par
`_combatAI.CurrentState`, de type `CombatAIState`).

**`MobState` et `Mob.CurrentState` publics — décision ordinal-safety** : `MobAnimatorController`
lit aujourd'hui `Mob.CurrentState` (type `MobState`) pour piloter `IsChasing`. On garde
`public CombatAIState CurrentState => _combatAI.CurrentState;` sur `Mob` (même nom, nouveau
type) et on met à jour `MobAnimatorController` pour comparer à `CombatAIState.Engage` au lieu de
`MobState.Chase` (voir §6.4 — la distinction walk/run change de sens, précisé là).
L'enum `MobState` (`Patrol/Chase/Attack/Return`) est supprimée — plus aucune référence après
migration (vérifié par grep avant suppression, voir plan).

### 6.3 Ordre exact dans `Mob.Update()` après migration

```csharp
protected override void Update()
{
    base.Update();
    if (isDead || data == null) return;

    // Poll d'interrupt de canalisation — DOIT rester ici, avant le freeze CC ci-dessous : un
    // hard CC doit interrompre la canalisation EN COURS (comportement actuel), pas être bloqué
    // par le early-return sur isCCd qui vient juste après.
    _combatAI.PollChannelInterrupt();

    if (statusEffects != null)
        agent.speed = data.moveSpeed * statusEffects.slowMultiplier * statusEffects.buffSpeedMultiplier;

    RefreshEnemyList();

    if (IsDashing) return;

    bool isCCd = statusEffects != null && (statusEffects.isStunned || statusEffects.isSleeping ||
                 statusEffects.isShocked || statusEffects.isFreezed);
    if (agent != null && agent.isOnNavMesh) agent.isStopped = isCCd;
    if (isCCd) return;

    _combatAI.Tick();
}
```
Identique à l'ordre actuel — seul `switch (currentState) { ... }` est remplacé par
`_combatAI.Tick()`.

### 6.4 `MobAnimatorController` — mise à jour minimale

```csharp
_animator.SetBool(IsChasingParam, _combatAI != null && _combatAI.CurrentState == CombatAIState.Engage);
```
**Changement de sens assumé** : avant, `IsChasing` était vrai UNIQUEMENT pendant `MobState.Chase`
(pas pendant `Attack`, où le Mob peut aussi avancer vers la cible — voir le commentaire de
`HandleAttack()` "on avance juste vers la cible directement"). Avec la fusion Chase+Attack en un
seul état `Engage`, `IsChasing` devient vrai pendant TOUT `Engage` (approche ET combat rapproché
immobile). Comme `Speed` (déjà câblé sur `agent.velocity.magnitude`) distingue déjà immobile
(Speed≈0, `StopAgent()` appelé) de en mouvement (Speed>0), le blend Idle/Walk/Run de l'Animator
reste correct : `IsChasing=true` + `Speed=0` = combat rapproché immobile (Attack), `IsChasing=true`
+ `Speed>0` = approche (Chase). C'est exactement le même signal combiné qu'aujourd'hui, recombiné
différemment — à valider en Play Mode (voir §9).

### 6.5 `PNJ.cs` — ce qui RESTE

`Awake()` (stats/dialogue), tout le système de dialogue (`Interact`/`SelectOption`/
`HandleDialogueAction`/...), `Die()`/`RespawnCoroutine()`, mémoire joueurs connus,
`FindClosestEnemy()` devenu aiType-aware + nouveau `aggroSet`/`OnForcedEngage()`/
`OnReturnToPatrol()` (implémentent `ICombatAIProfile`, voir §5bis), nouveau `TakeDamage()`
override (§5bis — n'existait pas avant), gizmos.

### 6.6 `PNJ.cs` — ce qui DISPARAÎT

`HandleCombatAI()`, `TryUseSecondarySkill()`, `GetMaxSkillRange()`, `ReturnToSpawn()`, tout le
bloc pending-hit/canalisation (§4.2), `_wasInBasicRange`/`BasicRangeExitFactor`.

**Bug latent trouvé pendant l'audit, corrigé par la migration** : `PNJ.Update()` actuel n'a
AUCUN freeze sur CC dur (`isStunned`/`isShocked`/`isFreezed`) avant d'appeler
`HandleCombatAI()` — contrairement à `Mob.Update()`. Un Garde stun continuait donc de bouger et
d'attaquer. La migration applique le MÊME bloc CC-freeze que Mob (§6.3) à `PNJ.Update()` — c'est
un changement de comportement volontaire (bug réel, pas juste un refactor), signalé ici
explicitement plutôt que corrigé silencieusement.

### 6.7 `PNJ.cs` — nouvelle patrouille

`PNJ.Awake()` calcule `spawnPos` (déjà fait) — le composant partagé gère le roam lui-même via
`ICombatAIProfile.PatrolRadius`. `PNJData.patrolRadius` (nouveau champ, §7) à `0f` par défaut ⇒
`CombatAIController.Patrol` détecte `PatrolRadius <= 0f` et reste immobile au spawn (pas d'appel
`GetNavMeshPoint`/`SetDestination` tant qu'aucun ennemi n'est engagé) — comportement identique à
l'actuel `ReturnToSpawn()`-puis-immobile pour un Garde non configuré.

## 7. Données — `PNJData.cs`

Deux nouveaux champs, sous le header `Combat (canFight)` existant (celui qui porte déjà
`aggroRadius`/`leashRadius`) :

```csharp
[Tooltip("Rayon de patrouille autour du spawn quand aucune cible n'est engagée. 0 = reste immobile\n" +
         "au spawn (comportement actuel). Équivalent de MobData.patrolRadius.")]
[ShowIf(nameof(canFight), true)]
public float patrolRadius = 0f;

[Tooltip("Passive : n'engage que s'il est attaqué directement (TakeDamage). Aggressive : engage\n" +
         "dès qu'un ennemi entre dans aggroRadius — comportement actuel, DÉFAUT à ne jamais changer\n" +
         "pour ne pas casser les PNJData existants. Boss non utilisé côté PNJ (réutilise MobAIType\n" +
         "tel quel — même enum que MobData, aucun risque ordinal).")]
[ShowIf(nameof(canFight), true)]
public MobAIType aiType = MobAIType.Aggressive;
```
`ShowIf(canFight)` : cohérent avec tous les autres champs combat déjà `ShowIf(canFight)` dans ce
fichier. Défauts choisis pour ZÉRO changement de comportement sur les `PNJData` existants tant
que Florian ne les modifie pas explicitement dans l'Inspector : `patrolRadius = 0f` (immobile,
comme avant), `aiType = Aggressive` (scan de proximité + engage spontané, comme avant — un défaut
`Passive` aurait cassé tous les Gardes existants silencieusement).

## 8. Cas limites déjà couverts par le rapatriement (aucun changement de comportement attendu)

- Ordre critique `InterruptChannelCast` (capturer `_channelBar` AVANT `EndChannelCastState()`,
  l'annuler APRÈS) — copié tel quel, commentaire conservé.
- `hitIndex` MultiHit rejeté si désynchronisé de `_pendingMultiNextIndex` — copié tel quel.
- CD du skill secondaire posé à la RÉSOLUTION, jamais au déclenchement — copié tel quel.
- Fallback CD `6f` si `skill.cooldown <= 0f` sur interruption/résolution — copié tel quel.
- `_pendingSkill`/`_pendingTarget`/canalisation nettoyés sur désengagement (leash dépassé) ET sur
  mort — remplacé par `CombatAIController.NotifyDeath()` (mort) et la transition interne
  Engage→Return (désengagement, déjà dans `Tick()`).

## 9. Vérification (manuel Play Mode — pas de framework de test)

1. Compile 0 erreur, 0 warning nouveau.
2. Mob (Aggressive) : patrouille normalement, engage un joueur à portée, plus d'oscillation
   Attack/Chase visible dans une session de combat prolongée (>30s, plusieurs cycles
   attaque/déplacement).
3. Mob (Passive) : ignore un joueur qui passe à portée sans le taunter/toucher ; engage dès le
   premier coup reçu (`TakeDamage` → `ForceEngage(attacker)`). Cas limite §5bis : 2 joueurs A et
   B dans `detectionRange` (B plus proche que A) — A tape le mob → le mob cible A (l'attaquant),
   PAS B même si B est plus proche. Si un attaquant kite hors `detectionRange`, le mob Passif le
   suit quand même (reste dans `aggroSet`) au lieu de perdre sa cible.
4. Mob : plus de "marche sur place" (Speed reste à 0 pendant toute l'attaque, `hasPath=False`).
5. PNJ Garde : comportement combat identique à avant (skill spécial prioritaire, attaque de base
   sinon, portée respectée) — aucune régression perceptible.
6. PNJ Garde stun (hard CC) : reste bien figé (nouveau comportement — vérifier qu'il ne bouge/
   n'attaque plus pendant le CC, contrairement à avant).
7. PNJ Garde avec `patrolRadius > 0` configuré : patrouille visiblement dans le rayon, revient y
   patrouiller après un combat (retour au spawn, `patrolRadius = 0` déjà validé en (1)/(5) via un
   Garde existant non modifié).
7bis. PNJ Garde `aiType = Aggressive` (défaut) : comportement inchangé vs avant (régression
   check — engage tout mob à portée spontanément).
7ter. PNJ Garde `aiType = Passive` (nouveau, à créer pour le test) : ignore un mob qui passe à
   portée ; engage dès qu'un mob le frappe en premier (`TakeDamage` → `ForceEngage(source)`),
   cible ce mob spécifiquement même si un autre mob non-agresseur est plus proche. Au retour au
   spawn (leash/désengagement), `aggroSet` se vide (`OnReturnToPatrol()`) — pas de reset HP/Mana
   instantané, contrairement à Mob.
8. Canalisation (castTime > 0) sur Mob ET PNJ : toujours interrompue correctement par hard CC
   (CD complet) et cible morte (CD demi) — aucune régression de l'ordre `_channelBar`/
   `EndChannelCastState`.
9. Leash : Mob et PNJ rentrent bien au spawn au-delà de leur distance respective, sans pending-
   hit/canalisation fantôme résolvant après désengagement.
