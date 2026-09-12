# Système VFX — Trajet & Statut — Design

## Contexte

Les chantiers A à D ont livré 3 champs VFX sur `SkillData` (`vfxCast`, `vfxZoneMarker`,
`vfxImpact`) + `soundEffect`, tous des ponctuels statiques : `Instantiate(prefab, position,
Quaternion.identity)` à un point de résolution déjà câblé (lancement, plantage de zone, moment
réel des dégâts). Vérifié par grep exhaustif sur `SkillSystem.cs`/`SkillBar.cs` : les 14 sites
d'`Instantiate` de VFX du projet utilisent tous `Quaternion.identity` sans exception — aucune
rotation nulle part, peu importe le `targetType`.

Deux trous identifiés et documentés (mais explicitement hors scope) lors des chantiers
précédents :
1. **VFX de trajet** — chantier D (trajectoire mobile) a une hitbox qui voyage frame par frame
   (`TrajectoryRoutine()`), mais aucun VFX ne suit ce déplacement. Le trou est documenté dans le
   doc-comment de la méthode : "seul le VFX de TRAJET... reste hors scope, chantier VFX séparé à
   venir."
2. **VFX de statut** — un effet visuel persistant tant qu'un buff/debuff est actif sur une
   entité (ex: brûlure qui recouvre le joueur pendant un DoT Feu) n'existe pas du tout. Vérifié
   par lecture complète : `Entities/StatusEffectSystem.cs` ne contient aucune ligne VFX/GameObject
   liée à l'affichage — seulement de la logique gameplay (dégâts, flags, recalcul de stats).

Ce chantier couvre les deux d'un coup (décision Florian — les deux besoins ont des déclencheurs
différents, `SkillSystem` pour le trajet vs `StatusEffectSystem` pour le statut, mais partagent
une nature commune : instancier un prefab VFX, le faire suivre quelque chose, le détruire au bon
moment).

## Décisions (validées avec Florian)

1. **VFX de trajet — orientation** : le VFX suit la position ET s'oriente selon la direction de
   déplacement (`Quaternion.LookRotation(dir)`) — jamais fait nulle part dans le projet
   jusqu'ici, mais coût quasi nul (la direction est déjà calculée dans `TrajectoryRoutine()`).
2. **VFX de statut — attache** : enfant direct du transform de l'entité
   (`transform.SetParent`/paramètre `parent` d'`Instantiate`) — suit position, rotation,
   mouvement et animations automatiquement, sans code de suivi à écrire. Correspond à l'exemple
   donné ("brûlure qui recouvre le joueur").
3. **Champ VFX de statut** : sur `StatusEffectData` (classe de base partagée par `BuffData` ET
   `DebuffData`), miroir exact du champ `icon` déjà présent à cet endroit — un seul champ pour
   les deux types.
4. **Portée** : Player uniquement pour le VFX de trajet (comme A/B/C/D, la trajectoire elle-même
   est Player-only). Le VFX de statut, lui, s'applique à TOUTE entité ayant un
   `StatusEffectSystem` (Player, Mob, PNJ) — cohérent avec le fait que `ApplyBuff`/
   `TryApplyDebuff` sont déjà génériques à toute `Entity`, aucune restriction Player à introduire
   artificiellement.

## VFX de trajet (`Combat/SkillSystem.cs`)

### Nouveau champ (`Data/Skills/SkillData.cs`)

Ajouté juste après `vfxCast` (section ⑩ Visuel & Son) :

```csharp
[Tooltip("VFX qui suit la hitbox pendant tout le déplacement d'un skill isTrajectory — suit " +
         "la position ET s'oriente selon la direction de déplacement. Actif uniquement si " +
         "isTrajectory = true. Distinct de vfxCast (spawné au lancement, ne suit pas) et de " +
         "vfxImpact (joué au moment du hit, ponctuel).")]
[ShowIf(nameof(isTrajectory), true)]
public GameObject vfxTrajectory;
```

### `StartTrajectory()` — spawn initial

Après le calcul de `origin`/`destination`, juste avant `StartCoroutine(TrajectoryRoutine(...))` :

```csharp
// Quaternion.LookRotation logue un warning Console sur un vecteur nul — cas dégénéré
// origin == destination (raycast manqué), déjà géré par le yield break précoce de
// TrajectoryRoutine juste après. Le VFX sera de toute façon détruit à la frame suivante par ce
// même chemin de sortie — Quaternion.identity en attendant évite juste le warning inutile.
Vector3 initialDir = destination - origin;
GameObject trajectoryVfx = skill.vfxTrajectory != null
    ? Instantiate(skill.vfxTrajectory, origin,
        initialDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(initialDir) : Quaternion.identity)
    : null;

StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx));
```

`TrajectoryRoutine()` gagne un 5ᵉ paramètre `GameObject trajectoryVfx`.

### `TrajectoryRoutine()` — suivi + nettoyage sur TOUS les chemins de sortie

Le VFX doit être détruit peu importe comment la coroutine se termine — même exigence que le
`marker` de `DelayedZoneRoutine()` (chantier C), qui a le même besoin (`if (marker != null)
Destroy(marker);` après la boucle, quel que soit le chemin de sortie).

Chemins de sortie actuels de `TrajectoryRoutine()` (vérifiés dans le code réel) :
- Le `yield break` précoce (origine == destination, ligne ~369).
- Le `break` sur mort du caster à l'intérieur du `while` (ligne ~412).
- La fin naturelle du `while` (traveled >= totalDistance).

Trois modifications :

1. **Sortie précoce** (`totalDistance <= 0.01f`) — ajouter `if (trajectoryVfx != null)
   Destroy(trajectoryVfx);` juste avant le `yield break` existant.
2. **Boucle principale** — à L'INTÉRIEUR du bloc `if (segment > 0.0001f) { ... }` existant (celui
   qui fait déjà le `SphereCastAll`), AVANT la ligne `previousPos = currentPos;` qui suit ce
   bloc — **piège** : si placé APRÈS `previousPos = currentPos;`, `currentPos - previousPos`
   vaudrait toujours zéro (les deux variables seraient déjà égales), cassant la rotation à
   chaque frame. Réutilise la même direction déjà calculée pour le `SphereCastAll` (extraite en
   variable locale pour éviter de la normaliser deux fois) :
   ```csharp
   if (segment > 0.0001f)
   {
       Vector3 segmentDir = (currentPos - previousPos).normalized;
       RaycastHit[] hits = Physics.SphereCastAll(previousPos, radius, segmentDir, segment);
       foreach (RaycastHit h in hits) { /* ... inchangé ... */ }

       if (trajectoryVfx != null)
       {
           trajectoryVfx.transform.position = currentPos;
           trajectoryVfx.transform.rotation = Quaternion.LookRotation(segmentDir);
       }
   }

   previousPos = currentPos;
   ```
   Si `segment <= 0.0001f` (frame quasi-nulle), le VFX garde sa dernière position/rotation —
   différence invisible à l'écran (le déplacement réel de cette frame est négligeable), et évite
   le même piège `LookRotation(vecteur nul)` qu'à l'initialisation.
3. **Fin de méthode** (après le bloc de fallback miss-feedback existant) — `if (trajectoryVfx !=
   null) Destroy(trajectoryVfx);` inconditionnel, couvre à la fois la fin naturelle du `while` ET
   le cas `casterDied` (contrairement au fallback vfxImpact/soundEffect qui, lui, ne joue PAS si
   `casterDied` — le nettoyage du VFX de trajet doit se faire dans TOUS les cas, mort du caster
   comprise, sinon le VFX resterait affiché indéfiniment sur un cadavre de trajectoire abandonnée).

## VFX de statut (`Entities/StatusEffectSystem.cs`, `Data/StatusEffect/*.cs`)

### Nouveaux champs

`Data/StatusEffect/StatusEffectData.cs` — juste après le champ `icon` existant (ligne ~246) :

```csharp
[Tooltip("VFX persistant tant que ce buff/debuff est actif sur une entité — attaché en enfant " +
         "du transform de l'entité (suit position/rotation/animations automatiquement). " +
         "Optionnel — vide = pas de VFX de statut.")]
public GameObject statusVfx;
```

`Data/StatusEffect/StatusEffectInstance.cs` — sur la classe de base abstraite `StatusEffectInstance`
(hérité par `BuffInstance` ET `DebuffInstance`), juste après `remainingTime` :

```csharp
/// <summary>Référence à l'instance VFX spawnée pour CET effet précis (une par instance, propre
/// au stacking : chaque instance stackée a la sienne). Null si data.statusVfx est vide ou pas
/// encore spawnée.</summary>
public GameObject spawnedVfx;
```

### Spawn — au moment de la création d'une NOUVELLE instance uniquement

5 points de création identifiés dans `StatusEffectSystem.cs` (vérifiés par lecture complète),
chacun immédiatement suivi d'un appel à `OnApplyBuff(instance)`/`OnApplyDebuff(instance)` :
- `ApplyBuff()` — cas non-stackable (ligne ~344) et cas stackable (ligne ~369).
- `TryApplyDebuff()` — cas non-stackable (ligne ~289) et cas stackable (ligne ~315).
- `ApplyBuffWithDuration()` — cas talisman (ligne ~404).

AUCUN de ces points ne doit spawner sur un simple `Refresh()` d'une instance déjà active (les
branches `existing.Refresh(); return;` / `sameAsset.Refresh(); return;` juste avant chaque
création, respectivement) — sinon une même instance verrait son VFX dupliqué à chaque recast.

Nouvelle méthode privée, appelée une fois par les 5 sites, juste après chaque
`OnApplyBuff`/`OnApplyDebuff` :

```csharp
/// <summary>Spawn le VFX persistant d'un effet nouvellement créé (jamais sur un simple
/// Refresh() — voir les 5 call sites). Enfant du transform de l'entité : suit position/
/// rotation/animations automatiquement, détruit avec le GameObject parent si l'entité est
/// détruite (comportement Unity par défaut, rien à coder).</summary>
private void SpawnStatusVfx(StatusEffectInstance instance)
{
    if (instance.data.statusVfx == null) return;
    instance.spawnedVfx = Instantiate(instance.data.statusVfx, _entity.transform.position,
        Quaternion.identity, _entity.transform);
}
```

### Despawn — à l'expiration RÉELLE d'une instance

`ExpireBuffInstance()`/`ExpireDebuffInstance()` ont chacune une garde de sortie précoce (`if
(!_activeBuffs.TryGetValue(...) || !list.Remove(instance)) return;`) suivie ensuite de plusieurs
branches avec leurs propres `return` internes (types `Stats` notamment, qui font `return` après
`RecalculateAndReapply()`). Le nettoyage VFX doit donc se faire IMMÉDIATEMENT après la garde de
sortie et AVANT tout branchement type-spécifique, sinon un `return` interne le sauterait :

```csharp
private void ExpireBuffInstance(BuffType type, BuffInstance instance)
{
    if (!_activeBuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
    if (list.Count == 0) _activeBuffs.Remove(type);

    if (instance.spawnedVfx != null) Destroy(instance.spawnedVfx);

    // ... reste de la méthode inchangé (flags, recalcul de stats) ...
}
```

Même insertion, même position relative (juste après le retrait de la liste, avant tout switch),
dans `ExpireDebuffInstance()`.

## Cas limites

- **Refresh d'une instance déjà active** : ne spawn jamais un second VFX (voir "Spawn"
  ci-dessus) — le VFX déjà en place continue d'exister sans interruption visuelle, cohérent avec
  le fait que `Refresh()` ne fait que remettre `remainingTime` à zéro, la durée continue depuis
  le même point de vue visuel.
- **Stacking** (types `Stats`/`Dot`, plusieurs assets différents actifs simultanément pour le
  même `BuffType`/`DebuffType`) : chaque instance stackée a sa propre entrée `spawnedVfx` —
  plusieurs VFX peuvent coexister sur la même entité si plusieurs stacks actives ont chacune un
  `statusVfx` configuré. Comportement assumé, pas un bug — au designer de configurer des VFX
  cohérents entre eux si plusieurs stacks du même type sont censées être actives ensemble.
- **Mort de l'entité pendant qu'un statusVfx est actif** : le GameObject VFX, étant un enfant du
  transform de l'entité, est détruit automatiquement par Unity quand le parent est détruit —
  aucun code de nettoyage supplémentaire nécessaire. `ExpireBuffInstance`/`ExpireDebuffInstance`
  ne sont d'ailleurs probablement jamais appelées dans ce cas précis (l'entité disparaît avant
  qu'un effet actif expire naturellement) — le VFX suit son parent dans la destruction, pas de
  fuite.
- **Caster mort en cours de trajectoire (VFX de trajet)** : contrairement au fallback
  vfxImpact/soundEffect (qui ne joue PAS dans ce cas, "le caster est mort avant qu'on puisse
  savoir" n'est pas un vrai miss), le nettoyage du `trajectoryVfx` doit TOUJOURS se faire,
  `casterDied` ou non — un VFX de trajet abandonné visible indéfiniment serait un vrai bug
  visuel, contrairement à l'absence de feedback sonore/visuel de fin qui est un choix délibéré.
- **`vfxTrajectory` vide** : toutes les nouvelles lignes sont gardées par `if (trajectoryVfx !=
  null)` — un skill `isTrajectory` sans `vfxTrajectory` configuré se comporte exactement comme
  avant ce chantier (aucune régression pour les skills de test déjà créés lors du chantier D,
  ex: `skl_test_trajectory_storm`).
- **`statusVfx` vide** : `SpawnStatusVfx()` retourne immédiatement (`if (instance.data.statusVfx
  == null) return;`), `instance.spawnedVfx` reste `null`, `Destroy(null)` ne serait de toute
  façon jamais appelé sur un `null` par la garde `if (instance.spawnedVfx != null)` à
  l'expiration — aucune régression pour tous les `BuffData`/`DebuffData` existants (aucun n'a ce
  champ configuré, ajout d'un nouveau champ optionnel avec valeur par défaut `null`).

## Fichiers touchés

- `Data/Skills/SkillData.cs` — champ `vfxTrajectory`.
- `Combat/SkillSystem.cs` — `StartTrajectory()` (spawn initial, signature de
  `TrajectoryRoutine()` modifiée), `TrajectoryRoutine()` (suivi + nettoyage sur 3 chemins de
  sortie).
- `Data/StatusEffect/StatusEffectData.cs` — champ `statusVfx`.
- `Data/StatusEffect/StatusEffectInstance.cs` — champ `spawnedVfx` sur la classe de base.
- `Entities/StatusEffectSystem.cs` — nouvelle méthode privée `SpawnStatusVfx()`, appelée depuis
  5 sites de création d'instance (`ApplyBuff()` ×2, `TryApplyDebuff()` ×2,
  `ApplyBuffWithDuration()` ×1) ; nettoyage ajouté dans `ExpireBuffInstance()` et
  `ExpireDebuffInstance()`.

## Vérification (manuelle, Play Mode, geste Florian)

Nécessite la création/configuration d'assets de test :
1. **VFX de trajet — GroundTarget** : configurer `vfxTrajectory` sur `skl_test_trajectory_storm`
   (déjà existant, chantier D) — le VFX doit voyager du joueur vers le point cliqué, orienté
   dans le sens du déplacement, disparaître à l'arrivée.
2. **VFX de trajet — mort du caster en cours de route** : le VFX doit disparaître immédiatement
   si le caster meurt pendant que la trajectoire voyage (pas de VFX fantôme abandonné).
3. **VFX de trajet — origine == destination** (GroundTarget avec raycast manqué, cas rare mais
   déjà géré dans le code) : le VFX ne doit pas rester affiché indéfiniment si spawné puis
   immédiatement détruit sur ce chemin de sortie précoce.
4. **VFX de statut — buff** : configurer `statusVfx` sur un `BuffData` de test (ex: Regeneration)
   — le VFX doit apparaître attaché au joueur à l'application, suivre ses déplacements/
   animations, disparaître exactement à l'expiration de la durée.
5. **VFX de statut — debuff DoT** : configurer `statusVfx` sur un `DebuffData` de type Dot/Burn
   existant — même vérification, sur une cible Mob si possible (confirme que ça marche sur une
   Entity non-Player, pas seulement Player).
6. **Refresh sans duplication** : relancer le même buff/debuff (même asset) pendant qu'il est
   déjà actif — un seul VFX doit rester visible, pas un second qui se superpose.
7. **Stacking** : appliquer 2 buffs `Stats` différents du même `BuffType` avec chacun un
   `statusVfx` configuré — les deux VFX doivent coexister, chacun suivant l'entité
   indépendamment.
8. **Mort de l'entité avec VFX de statut actif** : tuer une entité pendant qu'un `statusVfx` est
   affiché — le VFX doit disparaître avec le corps (pas de VFX orphelin flottant après
   destruction).
