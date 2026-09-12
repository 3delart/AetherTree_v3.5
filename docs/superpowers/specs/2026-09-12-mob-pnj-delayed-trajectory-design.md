# Zone différée & Trajectoire mobile pour Mob/PNJ — Design

## Contexte

Les chantiers C (zone à impact différé) et D (trajectoire mobile) sont Player-only : `hasDelayedImpact`/`isTrajectory` ne sont JAMAIS consultés par le chemin Mob/PNJ. Vérifié par lecture complète :

- `Entities/Mob.cs`, `TryUseSkill()` (ligne ~379-410) : `_skillSystem?.Execute(skill, this, target);` inconditionnel, sur son PROPRE composant `SkillSystem` (pas le singleton `SkillSystem.Instance` du Player).
- `Entities/PNJ.cs`, `TryUseSecondarySkill()` (ligne ~546-577, "même logique que Mob.TryUseSkill()") : identique, `_skillSystem.Execute(skill, this, target);`.
- Les deux ont aussi un appel `_skillSystem.Execute(data.basicAttackSkill, this, target);` pour l'attaque de base (Mob.cs:363, PNJ.cs:532).
- `Combat/SkillSystem.cs` — `PlantDelayedZone()`/`StartTrajectory()` sont déjà des méthodes **caster-agnostiques** dans leur corps (le seul bloc spécifique Player, `if (caster.entityType == EntityType.Player && caster is Player player) { ... }`, se saute proprement pour un Mob/PNJ) — la plomberie de résolution (position, coroutine, VFX) fonctionne déjà pour n'importe quel `Entity`.

Ce chantier couvre UNIQUEMENT le branchement `hasDelayedImpact`/`isTrajectory` dans les boucles de décision Mob/PNJ. Hors scope, explicitement déferré (voir conversation) : canalisation visible (castTime) pour Mob/PNJ, VFX de cast (`vfxCast`) pour Mob/PNJ.

## Décisions (validées avec Florian)

1. **Portée du ciblage** : `hasDelayedImpact` a un `[ShowIf]` réel de `Target`/`GroundTarget`/`AoE_Target` (vérifié dans `SkillData.cs:226-229` — **`Self`/`AoE_Self` n'en font PAS partie**, correction d'une erreur de la version précédente de cette spec, qui listait ces 4 types alors que seuls 3 existent et que `Self`/`AoE_Self` sont absents). Les skills Mob/PNJ visés par ce chantier utilisent donc `Target`/`AoE_Target` (pour `hasDelayedImpact`) ou `Direction` (pour `isTrajectory`, déjà supporté nativement). **`GroundTarget` reste techniquement sélectionnable dans l'Inspector** (il fait partie du `[ShowIf]` de `hasDelayedImpact`) mais est hors scope volontaire pour Mob/PNJ — aucun Mob ne "clique" un point au sol, `_groundTargetPoint` n'est jamais posé sur le `SkillSystem` d'un Mob, donc `PlantDelayedZone()` retomberait sur `_groundTargetPoint ?? caster.transform.position` = **la position du Mob lui-même** (branche existante, ligne 247) — pas un crash, juste un comportement "zone sous ses propres pieds" probablement pas voulu. Florian confirme ne pas configurer `GroundTarget` sur un skill Mob/PNJ ; aucun garde-fou de code n'est ajouté pour l'empêcher (cohérent avec la philosophie du fichier : avertissements à la configuration, pas de garde runtime redondante — mais ici il n'existe même pas d'avertissement dédié, à noter comme limite acceptée).
   - `hasDelayedImpact` + `Target`/`AoE_Target` : `PlantDelayedZone()` capture déjà `target.transform.position` en `Vector3` pur au moment du plantage (branche `else` du `if (skill.targetType == TargetType.GroundTarget)`, ligne 250-253) — fonctionne déjà tel quel pour un `target` Mob-fourni.
   - `isTrajectory` + `Direction` : `StartTrajectory()` retombe déjà sur `caster.transform.forward` (branche `else` de la ligne 349-363) puisque `_skillDirection` n'est jamais posé par aucun flow — et `Mob.TryUseSkill()`/`PNJ.TryUseSecondarySkill()` appellent déjà `LookAt(target.transform)` juste avant `Execute()` — donc un Mob visera automatiquement sa cible dès qu'il l'affronte, sans code supplémentaire.
2. **Survie de la coroutine si le caster meurt/disparaît en cours de route** : un Mob qui meurt fait `Destroy(gameObject, 3f)` (délai de corpse, vérifié comme le SEUL chemin de destruction d'un Mob dans le projet). Le risque réel concerne spécifiquement `DelayedZoneRoutine()` : son tout premier `yield return new WaitForSeconds(skill.impactDelay);` ne contient AUCUNE vérification `caster.isDead` — si `impactDelay` dépasse ces 3 secondes, la coroutine (tournant sur le composant `SkillSystem` du Mob lui-même) serait tuée par Unity PENDANT cette attente, avant même d'atteindre la boucle qui vérifie `caster.isDead` et nettoie le marker — fuite garantie. (Note : une fois DANS la boucle, `zoneDuration` seul ne pose pas ce risque — le premier tour de boucle vérifie `isDead` immédiatement.) Pour `TrajectoryRoutine()`, le risque est en pratique quasi nul : sa boucle vérifie `caster.isDead` À CHAQUE FRAME (`yield return null`), donc la mort du Mob est détectée en un cycle de frame (~16 ms), bien avant les 3 secondes du délai de corpse — le host y est ajouté par symétrie/défense-en-profondeur, pas parce qu'un scénario de fuite concret a été identifié pour cette méthode. Décision : **host temporaire**, créé uniquement pour un caster non-Player, qui exécute la coroutine indépendamment du caster et se détruit lui-même à la toute fin de sa propre routine — un host par cast, détruit à la fin de ce cast (une exception non gérée AVANT ce `Destroy` final laisserait exceptionnellement le host orphelin, comme n'importe quelle autre coroutine du fichier dans ce cas — limite acceptée, pas spécifique à ce chantier).
3. **Basic attack inclus** : même branchement appliqué aux 2 sites `basicAttackSkill` (Mob.cs:363, PNJ.cs:532) — mécanique identique, coût nul, évite un trou silencieux si un designer configure un jour une attaque de base avec ces flags.
4. **Sécurité du singleton `SkillSystem.Instance`** : trouvé en vérification indépendante — `Awake()` fait `if (Instance == null) Instance = this;`, SANS garantie qu'un Mob ne s'éveille pas avant le Player (Unity ne garantit aucun ordre entre `Awake()` de GameObjects différents). Si un Mob gagne cette course, `Instance` pointe sur SON `SkillSystem` ; si ce Mob meurt ensuite, `Instance` devient un faux-null Unity, et le PROCHAIN `SkillSystem` créé (mob suivant, OU un host temporaire de ce chantier) volerait la place — cassant silencieusement tous les skills du Player (`SkillBar`/`TargetingSystem`/`PassiveSkillSystem` appellent tous `SkillSystem.Instance?.Execute(...)`, le `?.` ne fait alors plus rien). Bug PRÉEXISTANT (pas causé par ce chantier), mais les hosts temporaires ajoutent des instances `SkillSystem` supplémentaires — plus d'occasions de déclencher le problème. Le commentaire du code dit déjà "le Player porte l'instance principale, Mob/PNJ ne doivent jamais l'écraser" — l'intention existe, jamais appliquée correctement. Florian valide le fix : `Awake()` ne réclame `Instance` que si `GetComponent<Player>() != null` — seul le Player peut désormais devenir le singleton, ni un Mob/PNJ ni un host temporaire. Inclus dans ce chantier (Task 1).

## `Combat/SkillSystem.cs` — host temporaire

### `PlantDelayedZone()`

Juste avant l'actuel `StartCoroutine(DelayedZoneRoutine(skill, caster, position, marker));` (ligne 259), insérer la sélection de host :

```csharp
// Mob/PNJ : Destroy(gameObject, délai de corpse) sur le caster tuerait cette coroutine avant
// qu'elle atteigne son propre nettoyage si impactDelay+zoneDuration dépasse ce délai — délègue
// à un host temporaire qui survit indépendamment du caster, détruit lui-même en fin de routine.
// Le chemin Player (jamais détruit) reste inchangé, tourne sur `this` comme aujourd'hui.
SkillSystem host = caster.entityType == EntityType.Player
    ? this
    : new GameObject($"DelayedZoneHost_{skill.name}").AddComponent<SkillSystem>();
bool destroySelfOnFinish = host != this;

host.StartCoroutine(host.DelayedZoneRoutine(skill, caster, position, marker, destroySelfOnFinish));
```

Remplace la ligne `StartCoroutine(DelayedZoneRoutine(skill, caster, position, marker));` existante.

### `DelayedZoneRoutine()`

Nouveau paramètre `bool destroySelfOnFinish` ajouté à la signature (ligne 267) :

```csharp
private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker, bool destroySelfOnFinish)
```

Corps de la méthode INCHANGÉ jusqu'à la toute fin. Remplacer la dernière ligne (`if (marker != null) Destroy(marker);`) par :

```csharp
        if (marker != null) Destroy(marker);
        if (destroySelfOnFinish) Destroy(gameObject);
    }
```

`gameObject` ici réfère à celui de l'instance sur laquelle la coroutine tourne réellement (le host temporaire, puisque `host.DelayedZoneRoutine(...)` a été appelée SUR `host`) — pas celui du caster ni de l'instance SkillSystem d'origine. Aucun risque de détruire le Player par erreur : `destroySelfOnFinish` n'est `true` que lorsque `host != this`, ce qui n'arrive jamais pour un caster Player.

### `StartTrajectory()`

Même principe. Juste avant l'actuel `StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx));` (ligne 375), insérer :

```csharp
SkillSystem host = caster.entityType == EntityType.Player
    ? this
    : new GameObject($"TrajectoryHost_{skill.name}").AddComponent<SkillSystem>();
bool destroySelfOnFinish = host != this;

host.StartCoroutine(host.TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx, destroySelfOnFinish));
```

### `TrajectoryRoutine()`

Nouveau paramètre `bool destroySelfOnFinish` ajouté à la signature (ligne 389) :

```csharp
private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx, bool destroySelfOnFinish)
```

**Deux points de sortie à traiter** (comme pour le nettoyage `trajectoryVfx` déjà en place, même symétrie) :

1. Sortie précoce `totalDistance <= 0.01f` — après le `if (trajectoryVfx != null) Destroy(trajectoryVfx);` existant sur ce chemin, ajouter `if (destroySelfOnFinish) Destroy(gameObject);` avant le `yield break`.
2. Fin de méthode (naturelle ou `casterDied`) — après le `if (trajectoryVfx != null) Destroy(trajectoryVfx);` inconditionnel déjà en fin de méthode, ajouter `if (destroySelfOnFinish) Destroy(gameObject);`.

### Sécurité du singleton — fix inclus

Contrairement à une version précédente de cette spec, ce n'est PAS déjà sûr : Unity ne garantit aucun ordre entre les `Awake()` de GameObjects différents, donc un Mob peut réclamer `Instance` avant le Player (voir Décision 4 ci-dessus). Remplacer dans `Awake()` :

```csharp
    private void Awake()
    {
        // Singleton souple — le Player porte l'instance principale.
        // Les Mob/PNJ portent leur propre composant sans écraser l'instance.
        if (Instance == null)
            Instance = this;
    }
```

par :

```csharp
    private void Awake()
    {
        // Singleton souple — le Player porte l'instance principale. Seul un SkillSystem sur un
        // GameObject avec un composant Player peut réclamer Instance — un Mob/PNJ/host
        // temporaire ne l'écrase donc jamais, peu importe l'ordre réel des Awake() (non garanti
        // par Unity entre GameObjects différents). Corrige un bug réel trouvé en vérification :
        // avant ce fix, `if (Instance == null)` seul pouvait laisser un Mob gagner la course au
        // démarrage et voler la place — cassant silencieusement tous les skills du Player
        // ensuite (SkillBar/TargetingSystem/PassiveSkillSystem appellent tous
        // SkillSystem.Instance?.Execute(...), le `?.` ne faisant plus rien sur un Instance
        // devenu un Mob mort).
        if (Instance == null && GetComponent<Player>() != null)
            Instance = this;
    }
```

## `Entities/Mob.cs` — branchement dans `TryUseSkill()`

Remplacer (ligne 403) :

```csharp
            _skillSystem?.Execute(skill, this, target);
```

par :

```csharp
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, this);
            else
                _skillSystem?.Execute(skill, this, target);
```

Et pour l'attaque de base, remplacer (ligne 363) :

```csharp
                _skillSystem?.Execute(data.basicAttackSkill, this, target);
```

par :

```csharp
                if (data.basicAttackSkill.hasDelayedImpact)
                    _skillSystem?.PlantDelayedZone(data.basicAttackSkill, this, target);
                else if (data.basicAttackSkill.isTrajectory)
                    _skillSystem?.StartTrajectory(data.basicAttackSkill, this);
                else
                    _skillSystem?.Execute(data.basicAttackSkill, this, target);
```

(Ce second remplacement reste à l'intérieur du `if (!isDead && !target.isDead && data.basicAttackSkill != null)` déjà existant — `data.basicAttackSkill` y est donc garanti non-null.)

## `Entities/PNJ.cs` — branchement dans `TryUseSecondarySkill()`

Remplacer (ligne 571) :

```csharp
            _skillSystem.Execute(skill, this, target);
```

par :

```csharp
            if (skill.hasDelayedImpact)
                _skillSystem.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem.StartTrajectory(skill, this);
            else
                _skillSystem.Execute(skill, this, target);
```

Et pour l'attaque de base, remplacer (ligne 532) :

```csharp
                    _skillSystem.Execute(data.basicAttackSkill, this, _combatTarget);
```

par :

```csharp
                    if (data.basicAttackSkill.hasDelayedImpact)
                        _skillSystem.PlantDelayedZone(data.basicAttackSkill, this, _combatTarget);
                    else if (data.basicAttackSkill.isTrajectory)
                        _skillSystem.StartTrajectory(data.basicAttackSkill, this);
                    else
                        _skillSystem.Execute(data.basicAttackSkill, this, _combatTarget);
```

(Reste dans le `if (!isDead && !_combatTarget.isDead)` déjà existant, ligne 531.)

Note : PNJ.cs n'utilise pas `?.` sur `_skillSystem` (déjà garanti non-null par le `if (_skillSystem == null) return;` de l'appelant `HandleCombatAI()`) — cohérent avec le style déjà en place, pas de changement d'idiome introduit.

## Cas limites

- **CD/mana** : déjà postés par `TryUseSkill()`/`TryUseSecondarySkill()` AVANT l'appel (`SpendMana`, puis après l'appel `_skillCooldowns[skill] = ...`) — totalement inchangé par ce chantier, cohérent avec le principe "launch vs resolution" déjà établi (CD posé au lancement, jamais à la résolution différée).
- **`RegisterLastSkill`** : `PlantDelayedZone()`/`Execute()` appellent déjà `if (target is Mob mobTarget && caster is Player attackerPlayer) mobTarget.RegisterLastSkill(...)` — condition qui exige un `caster is Player`, donc un Mob/PNJ attaquant ne déclenche jamais cette ligne (comportement identique à aujourd'hui, un Mob n'accorde pas de "killer credit" de cette façon — inchangé, hors scope).
- **`hasDelayedImpact` + `AoE_Self`/`Self`** : ces 2 `targetType` ne font PAS partie du `[ShowIf]` de `hasDelayedImpact` (voir Décision 1) — le champ est normalement invisible/non cochable dans l'Inspector pour un skill configuré ainsi. Un cas de "drift" reste possible (le champ était `true` avec un `targetType` compatible, puis le designer a changé `targetType` vers `Self`/`AoE_Self` sans décocher `hasDelayedImpact` — exactement le scénario déjà couvert par le warning `OnValidate()` existant pour ce champ). Si ça arrive quand même au runtime : `PlantDelayedZone()` retombe sur la branche `else` (ligne 252, `target != null ? target.transform.position : caster.transform.position`) — la zone se plante à la position du Mob lui-même, pas un crash. Comportement cohérent, déjà couvert par le warning existant, pas un trou spécifique à ce chantier.
- **Host temporaire et `zoneDuration`/trajet très courts** : le host est créé et détruit même pour un cast très bref (quelques dixièmes de seconde) — léger coût `Instantiate`/`Destroy` d'un GameObject vide par cast Mob à zone différée/trajectoire. Négligeable au rythme de cast d'un Mob (pas un chemin per-frame), YAGNI de pooler ça pour l'instant.
- **Plusieurs Mobs castent en même temps** : chaque cast crée SON PROPRE host (nommé `DelayedZoneHost_<skillName>`/`TrajectoryHost_<skillName>`) — aucun partage d'état, aucun risque de collision entre casts simultanés de Mobs différents (ou du même Mob, deux casts qui se chevauchent).
- **`isTrajectory` en mode `Direction` sans mouvement du Mob pendant le trajet** : `StartTrajectory()` capture `caster.transform.forward` UNE FOIS au lancement (`Vector3 dir = ... caster.transform.forward;` ligne 359) — si le Mob continue de tourner/bouger après avoir lancé (ex: son IA le fait pivoter vers autre chose), la trajectoire garde sa direction initiale, ne suit pas la nouvelle orientation du Mob. Comportement hérité, identique à ce qui existe déjà pour le Player, pas une régression de ce chantier.

## Fichiers touchés

- `Combat/SkillSystem.cs` — host temporaire dans `PlantDelayedZone()`/`StartTrajectory()`, nouveau paramètre `destroySelfOnFinish` sur `DelayedZoneRoutine()`/`TrajectoryRoutine()`.
- `Entities/Mob.cs` — branchement dans `TryUseSkill()` (skill secondaire) et le bloc `basicAttackSkill` de `HandleAttack()`.
- `Entities/PNJ.cs` — branchement dans `TryUseSecondarySkill()` et le bloc `basicAttackSkill` de `HandleCombatAI()`.

Aucun changement sur `Data/Skills/SkillData.cs` (aucun nouveau champ), aucun changement de save format.

## Vérification (manuelle, Play Mode, geste Florian)

Nécessite de configurer `hasDelayedImpact`/`isTrajectory` sur un skill secondaire d'un Mob ou PNJ de test existant (`data.skills`), ou sur son `basicAttackSkill` :

1. **Mob avec `hasDelayedImpact` + `Target`** : le Mob doit planter une zone à la position de sa cible (au moment du cast), infliger les dégâts après `impactDelay`, la cible doit pouvoir esquiver en bougeant.
2. **Mob avec `isTrajectory` + `Direction`** : le Mob doit envoyer une hitbox qui voyage tout droit dans la direction où il fait face (vers sa cible, puisqu'il vient de faire `LookAt`).
3. **Mob qui meurt PENDANT que sa zone/trajectoire est encore active** : la zone/trajectoire doit continuer de se résoudre normalement (dégâts, VFX, nettoyage) malgré la disparition du Mob — vérifier dans la Hierarchy qu'un GameObject `DelayedZoneHost_...`/`TrajectoryHost_...` existe pendant ce temps et disparaît proprement à la fin.
4. **CD posé au bon moment** : le Mob ne doit pas pouvoir relancer le même skill avant son `cooldown`, peu importe la durée de la zone/trajectoire (CD déjà posé au lancement, inchangé).
5. **PNJ combattant** : même vérification que 1-4 sur un PNJ hostile/combattant plutôt qu'un Mob, pour confirmer la parité.
6. **Basic attack avec `hasDelayedImpact`/`isTrajectory`** (cas de test, pas forcément un besoin réel) : configurer temporairement le `basicAttackSkill` d'un Mob de test avec l'un des deux flags, confirmer que ça fonctionne aussi sur ce chemin.
