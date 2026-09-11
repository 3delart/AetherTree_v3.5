# Trajectoire mobile (tornade ciblée) — Design

## Contexte

Chantier C ("zone à impact différé") a livré une zone qui, une fois plantée, reste **fixe** au
sol jusqu'à détonation/expiration. Florian a explicitement distingué ce cas d'une seconde
mécanique jamais implémentée : une hitbox qui **se déplace** entre le caster et un point cible,
infligeant des dégâts à tout ce qu'elle traverse sur son passage — exemple donné : "un tourbillon
d'eau sur self, puis envoie la tornade d'eau en ligne droite devant lui (ou target ground)".

Ce chantier ajoute cette seconde mécanique, appelée "trajectoire" dans le code (`isTrajectory`).
Il est volontairement indépendant du chantier C : un skill choisit l'une ou l'autre mécanique,
jamais les deux à la fois (voir Décisions).

Portée : Player uniquement (Mob/PNJ hors scope, comme A/B/C). Plomberie logique pure — aucun
VFX de déplacement n'est ajouté ici ; le rendu visuel du mouvement est un chantier séparé,
prévu après validation en jeu de celui-ci.

## Décisions (validées avec Florian)

1. **Vitesse** : vitesse réelle en unités/seconde, réutilise le champ `projectileSpeed`
   existant sur `SkillData` (déclaré mais jamais câblé à un mouvement réel jusqu'ici).
2. **Détection des cibles** : balayage continu (`SphereCastAll` entre la position du tick
   précédent et la position du tick courant), jamais un simple `OverlapSphere` sur la position
   instantanée — élimine tout risque de "sauter" une cible fine à vitesse élevée ou framerate
   bas.
3. **Multi-hit** : une entité ne peut être touchée qu'une seule fois par cast, même si elle
   reste plusieurs frames dans le rayon de la hitbox (`HashSet<Entity>` de suivi, vidé à chaque
   nouveau cast). Aligné sur le comportement déjà existant de `ExecuteLineTarget()`.
4. **Origine** : toujours la position du caster au moment de la résolution. Un départ depuis
   une position différée (ex. une comète du chantier C qui se remettrait ensuite à bouger) est
   explicitement hors scope.
5. **Direction/destination** : les deux modes sont supportés, au choix du skill designer, via
   le `targetType` déjà porté par le skill :
   - `GroundTarget` → destination = point cliqué au sol (`_groundTargetPoint`).
   - `Direction` → destination = position du caster + direction (souris/regard) × `range`.
6. **Câblage `SkillData`** : un flag booléen `isTrajectory`, combiné aux `targetType` existants
   `GroundTarget`/`Direction` — pas de nouveau `targetType` dédié. Cohérent avec le pattern déjà
   utilisé par `hasDelayedImpact` (chantier C) : un flag transversal plutôt qu'une explosion de
   l'enum `TargetType` pour chaque variante temporelle.
7. **Exclusivité avec `hasDelayedImpact`** : mutuellement exclusifs. Un skill est soit une zone
   fixe différée (chantier C), soit une trajectoire mobile (ce chantier), jamais les deux sur le
   même skill pour l'instant. Averti par `OnValidate()`, pas bloqué en dur (même philosophie que
   les autres warnings du fichier).
8. **Scope** : Player uniquement, aucun VFX de mouvement ajouté (chantier VFX séparé, à venir
   après validation en jeu de celui-ci).

## Champs ajoutés (`Data/Skills/SkillData.cs`)

Un seul nouveau champ, juste après le bloc `hasDelayedImpact`/`impactDelay`/`zoneDuration`/
`zoneTickInterval` du chantier C :

```csharp
[Tooltip("Transforme la résolution de ce skill en hitbox mobile qui voyage du caster vers " +
         "une destination (au lieu de résoudre les dégâts au point de résolution existant, " +
         "une trajectoire est parcourue et touche tout ce qui se trouve sur son passage). " +
         "Distinct de hasDelayedImpact (zone FIXE une fois plantée) — mutuellement exclusif. " +
         "GroundTarget : voyage vers le point cliqué au sol. Direction : voyage en ligne " +
         "droite sur une distance = range.")]
[ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Direction,
    Header = "⑨Ter Trajectoire mobile")]
public bool isTrajectory = false;
```

`ShowIfAttribute` prend `params object[] values` — le constructeur combine nativement plusieurs
valeurs du MÊME champ en OU (voir `Utils/ShowIfAttribute.cs`, déjà utilisé ainsi par exemple pour
`[ShowIf(nameof(action), DialogueAction.AcceptQuest, DialogueAction.TurnInQuest)]`). Pas besoin
d'`AndField`/`AndValue` (réservé à combiner deux champs DIFFÉRENTS en ET).

Aucun autre champ numérique n'est ajouté : la vitesse réutilise `projectileSpeed` (fallback
10 unités/sec si `<= 0` — arbitraire mais cohérent avec les autres fallbacks du fichier,
`projectileSpeed` n'ayant aucun fallback préexistant ailleurs), le rayon de la hitbox réutilise
`aoeRadius` (fallback 0.5, même valeur que `ExecuteDirection`), la portée max en mode
`Direction` réutilise `range` (fallback 10, même valeur que `ExecuteDirection` — `ExecuteSkillshot`
utilise des fallbacks différents, 15/0.25, non pertinents ici).

`OnValidate()` gagne trois nouveaux avertissements, suivant l'idiome déjà en place dans le
fichier (`Debug.LogWarning` préfixé du nom du skill) :
- `isTrajectory == true` et `hasDelayedImpact == true` simultanément → avertissement
  d'incompatibilité, aucune valeur n'est modifiée automatiquement (pas de writeback, cohérent
  avec le reste du fichier).
- `isTrajectory == true` et `targetType` différent de `GroundTarget`/`Direction` → avertissement
  de configuration incohérente (même idiome que le warning `targetType`-drift ajouté au
  chantier C pour `hasDelayedImpact`).
- `isTrajectory == true` et `executionType != SkillExecutionType.Normal` → avertissement,
  symétrique du warning déjà existant pour `hasDelayedImpact && executionType != Normal`. Sans
  lui, un skill `isTrajectory` configuré en MultiHit/ComboSequence n'a aucun avertissement alors
  que `StartTrajectory()` n'est jamais atteinte depuis le dispatch MultiHit/ComboSequence normal
  (silencieusement ignorée) — sauf un cas particulier déjà présent pour `hasDelayedImpact` et
  non corrigé dans ce chantier (`LaunchSkill()` teste `castTime > 0` AVANT `executionType`, donc
  un skill MultiHit avec `castTime > 0` passe par `StartChannel()`/`ResolveChannel()`, où
  `isTrajectory` EST atteint et `hitSteps` est silencieusement ignoré — comportement préexistant
  identique pour `hasDelayedImpact`, non spécifique à ce chantier, pas corrigé ici).

## `Combat/SkillSystem.cs`

### `StartTrajectory(SkillData skill, Entity caster)`

Nouvelle méthode publique, miroir de `PlantDelayedZone()` pour la partie "résolution
immédiate" (CD/mana/event/bookkeeping), mais sans paramètre `target` — `GroundTarget` et
`Direction` ne prennent jamais de cible Entity (confirmé par le commentaire existant en tête de
fichier : "target peut être null pour Self / AoE_Self / GroundTarget / Direction / Skillshot /
Cone / Dash_Direction").

```csharp
public void StartTrajectory(SkillData skill, Entity caster)
{
    if (skill == null || caster == null || caster.isDead) return;

    if (caster.entityType == EntityType.Player && caster is Player player)
    {
        player.ResolveSkillUse(skill, null);

        GameEventBus.Publish(new SkillUsedEvent
        {
            skill          = skill,
            target         = null,
            caster         = player,
            primaryElement = skill.PrimaryElement,
            isCombo        = skill.elements != null && skill.elements.Count >= 2,
            locationID     = player.currentZoneID,
            isInParty      = false,
        });
    }

    Vector3 origin = caster.transform.position;
    Vector3 destination;

    if (skill.targetType == TargetType.GroundTarget)
    {
        // Même consommation que ExecuteGroundTarget()/PlantDelayedZone() — sans ce reset,
        // un skill sans rapport lancé plus tard hériterait d'une position périmée.
        destination = _groundTargetPoint ?? origin;
        _groundTargetPoint = null;
    }
    else // TargetType.Direction
    {
        // _skillDirection n'est en réalité JAMAIS posé par le flow joueur actuel —
        // SetSkillDirection() n'a qu'un seul appelant dans tout le projet
        // (TargetingSystem.TryExecuteSkill(), lui-même sans appelant, code mort). Le fallback
        // caster.transform.forward est donc TOUJOURS celui utilisé en pratique aujourd'hui —
        // comportement déjà identique pour ExecuteDirection()/ExecuteSkillshot()/ExecuteCone(),
        // pas une régression introduite ici. La direction résolue est celle où le PERSONNAGE
        // fait face, pas la souris/le regard caméra.
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;
        float range = skill.range > 0f ? skill.range : 10f;
        destination = origin + dir * range;
    }

    StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination));
}
```

### `TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination)`

```csharp
private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination)
{
    float totalDistance = Vector3.Distance(origin, destination);
    if (totalDistance <= 0.01f) yield break; // origine == destination, rien à parcourir

    float speed  = skill.projectileSpeed > 0f ? skill.projectileSpeed : 10f;
    float radius = skill.aoeRadius       > 0f ? skill.aoeRadius       : 0.5f;
    Vector3 dir  = (destination - origin) / totalDistance;

    HashSet<Entity> alreadyHit = new HashSet<Entity>();

    // Pass initiale à l'origine — un SphereCastAll ne détecte JAMAIS un collider déjà en
    // chevauchement à son point de départ (limitation connue de la physique Unity, même raison
    // pour laquelle DashInDirection utilise OverlapSphere et non un SphereCast). Sans ce pass,
    // une entité collée au caster au moment du lancement (ex: un ennemi au corps-à-corps quand
    // le joueur lance la trajectoire) pourrait n'être JAMAIS touchée.
    foreach (Collider col in Physics.OverlapSphere(origin, radius))
    {
        Entity entity = col.GetComponentInParent<Entity>();
        if (entity == null || entity.isDead) continue;
        if (alreadyHit.Contains(entity)) continue;
        if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

        alreadyHit.Add(entity);
        ApplyEffectType(skill, caster, entity);
        ApplyStatusEffects(skill, caster, entity);
        CheckKill(entity);

        if (skill.vfxImpact != null)
            Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
        if (skill.soundEffect != null)
            AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
    }

    Vector3 previousPos = origin;
    float   traveled    = 0f;

    while (traveled < totalDistance)
    {
        // Garde caster mort en cours de trajet — même effet que le `break` de
        // DelayedZoneRoutine (rien à nettoyer après, pas de marker/VFX créé par cette coroutine).
        // DashToTarget/DashInDirection utilisent `yield break` (pas `break`) car ILS ont du
        // nettoyage post-boucle à sauter — pas le cas ici, comparaison à ces deux-là non
        // pertinente.
        if (caster == null || caster.isDead) break;

        traveled += speed * Time.deltaTime;
        Vector3 currentPos = origin + dir * Mathf.Min(traveled, totalDistance);
        float   segment    = Vector3.Distance(previousPos, currentPos);

        if (segment > 0.0001f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(previousPos, radius, (currentPos - previousPos).normalized, segment);
            foreach (RaycastHit h in hits)
            {
                Entity entity = h.collider.GetComponentInParent<Entity>();
                if (entity == null || entity.isDead) continue;
                if (alreadyHit.Contains(entity)) continue;
                if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                alreadyHit.Add(entity);
                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);

                // Un vfxImpact/soundEffect PAR entité touchée — même précédent que
                // ResolveMultiHitStep() (une trajectoire est une séquence de hits distincts,
                // potentiellement à des positions/moments différents, pas une zone unique comme
                // DelayedZoneRoutine qui joue un seul vfx/son par tick peu importe combien
                // d'entités touchées).
                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
            }
        }

        previousPos = currentPos;
        yield return null;
    }
}
```

`vfxImpact`/`soundEffect` sont donc bien joués (contrairement à une version antérieure de cette
spec qui prévoyait de ne rien jouer du tout) — seul le VFX de TRAJET (un effet qui suivrait
`currentPos` à chaque frame pour visualiser le déplacement lui-même) reste hors scope, réservé
au chantier VFX à venir.

## `Data/Skills/SkillBar.cs`

Dans `ResolveInstant()`, juste après la garde combo existante (qui reste évaluée en premier,
inchangée) et le branchement `hasDelayedImpact` existant :

```csharp
if (skill.hasDelayedImpact)
    SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
else if (skill.isTrajectory)
    SkillSystem.Instance?.StartTrajectory(skill, _player);
else
    SkillSystem.Instance?.ResolveExecute(skill, _player, target);
```

Même branchement ajouté dans `ResolveChannel()`, avec `Execute()` comme cas par défaut au lieu
de `ResolveExecute()` (délibérément préservé, comme le fait déjà le branchement
`hasDelayedImpact` du chantier C — pas une incohérence à corriger) :

```csharp
if (skill.hasDelayedImpact)
    SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
else if (skill.isTrajectory)
    SkillSystem.Instance?.StartTrajectory(skill, _player);
else
    SkillSystem.Instance?.Execute(skill, _player, target);
```

`_cooldownTimers[slot]`/`_gcdTimer`/`DelayAutoAttack` postés après le branchement dans les deux
cas, inchangés — même principe "launch vs resolution" que chantiers A/B/C : seul le dégât est
étalé/déplacé dans le temps, le CD/GCD est posé au point de résolution existant.

## Cas limites

- **Origine == destination** (`GroundTarget` cliqué sur la position du caster, ou `Direction`
  avec `range <= 0`) : la coroutine se termine immédiatement (`yield break`) sans toucher
  personne. Comportement assumé, pas un bug — au designer de configurer un `range`/une cible
  cohérente.
- **Caster meurt en cours de trajet** : la boucle s'arrête (`break`) au tick suivant, aucune
  détection supplémentaire après la mort. Pas de nettoyage à faire (pas de marker/VFX créé par
  cette coroutine dans ce chantier).
- **Deux trajectoires simultanées** (le joueur relance un 2ᵉ skill à trajectoire avant que la
  1ʳᵉ ait fini) : coexistent sans conflit, toute la coroutine capture ses données en variables
  locales (`origin`, `destination`, `alreadyHit`), aucun champ à slot unique utilisé.
- **`isTrajectory` sur un `targetType` autre que `GroundTarget`/`Direction`** : avertissement
  Console via `OnValidate()`, mais si un designer force quand même cette combinaison au runtime
  (ex. changement de `targetType` par script), `StartTrajectory()` n'est appelée que depuis les
  deux branchements `ResolveInstant()`/`ResolveChannel()` qui ne testent pas le `targetType` —
  **note d'implémentation** : le calcul de `destination` dans `StartTrajectory()` suppose
  `targetType == GroundTarget` OU `Direction` (if/else binaire, pas de `switch` avec cas
  d'erreur). Un skill mal configuré avec un `targetType` incompatible tombera silencieusement
  dans la branche `else` (traité comme `Direction`) — acceptable car déjà averti à l'édition via
  `OnValidate()`, cohérent avec la philosophie du fichier (warnings à la configuration, pas de
  garde runtime redondante).

## Fichiers touchés

- `Data/Skills/SkillData.cs` — champ `isTrajectory`, 3 nouveaux avertissements `OnValidate()`.
- `Combat/SkillSystem.cs` — `StartTrajectory()` + `TrajectoryRoutine()` (nouvelles méthodes,
  aucune méthode existante modifiée).
- `Data/Skills/SkillBar.cs` — branchement `isTrajectory` ajouté dans `ResolveInstant()` et
  `ResolveChannel()`, juste après le branchement `hasDelayedImpact` existant.

Aucun changement dans `TargetingSystem.cs`. `_groundTargetPoint` est bien posé par le flow
existant (clic au sol, voir `StartInstant()`/`StartChannel()`), consommé de la même façon que
les mécaniques statiques déjà en place. `_skillDirection`, en revanche, n'est **jamais** posé
par le flow joueur réel aujourd'hui (voir note dans `StartTrajectory()` ci-dessus) — mode
`Direction` retombe donc systématiquement sur `caster.transform.forward`, comportement déjà
partagé avec `ExecuteDirection()`/`ExecuteSkillshot()`/`ExecuteCone()` existants, pas une
régression de ce chantier.

## Vérification (manuelle, Play Mode, geste Florian)

Nécessite la création de 2 skills de test (`hasDelayedImpact = false`, `isTrajectory = true`) :
un en mode `GroundTarget`, un en mode `Direction`. Points à vérifier :

1. **GroundTarget** : cliquer un point au sol, la hitbox doit voyager du joueur vers ce point à
   la vitesse de `projectileSpeed` et infliger des dégâts à toute entité traversée en chemin
   (pas seulement à l'arrivée).
2. **Direction** : marcher d'abord dans une direction précise (le joueur ne pivote jamais vers
   la souris en mode Direction, `EngageAndFaceTarget()` l'exclut explicitement), puis lancer
   sans cible — la hitbox doit voyager en ligne droite dans la direction où le PERSONNAGE fait
   face (`caster.transform.forward` — pas la souris/caméra, voir note sur `_skillDirection` plus
   haut) sur une distance `range`, mêmes dégâts en chemin.
3. **Esquive réelle** : une cible qui se déplace hors du chemin APRÈS le lancement mais AVANT
   que la hitbox n'atteigne sa position ne doit PAS être touchée.
4. **Pas de double-hit** : une cible qui reste immobile pile sur le trajet ne doit recevoir
   qu'un seul tick de dégâts, pas un par frame.
5. **CD posé au lancement** : le cooldown de la slot doit démarrer immédiatement au clic/fin de
   canalisation, pas à l'arrivée de la hitbox à destination.
6. **Warning Console** : configurer un skill de test avec `isTrajectory` ET `hasDelayedImpact`
   cochés en même temps → vérifier l'avertissement dans la Console à la sélection de l'asset.
7. **Deux trajectoires simultanées** : relancer un 2ᵉ skill à trajectoire avant que le 1ᵉʳ
   n'arrive à destination — les deux doivent progresser indépendamment sans se perturber.
8. **VFX/son par entité** : configurer `vfxImpact`/`soundEffect` sur un skill de test, toucher
   2+ entités espacées sur le chemin — le VFX/son doit jouer à CHAQUE hit, à la position de
   l'entité touchée, pas une seule fois pour tout le cast.
9. **Cible au corps-à-corps (point blank)** : lancer une trajectoire alors qu'une entité est
   déjà collée au caster au moment du clic/de la résolution — elle DOIT être touchée dès le
   premier instant (vérifie le pass `OverlapSphere` initial, un `SphereCastAll` seul ne détecte
   pas un chevauchement déjà présent à son point de départ).
10. **Mort du caster en cours de trajet** : si possible à déclencher manuellement (dégâts reçus
    pendant que la trajectoire voyage) — la coroutine doit s'arrêter proprement, aucune erreur
    Console.
