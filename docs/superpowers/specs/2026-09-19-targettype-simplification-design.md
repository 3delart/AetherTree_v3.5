# Simplification du système TargetType des skills — Design

> Chantier de ciblage dégâts/debuff/buff (Damage/Buff/Debuff/Other, via `SkillData`). Le
> déplacement (Dash, Pull, Push, Teleport, regroupement d'ennemis...) est explicitement **hors
> scope** — sujet à part, à brainstormer séparément une fois ce chantier-ci digéré.

## Contexte

`SkillData.targetType` compte aujourd'hui 11 valeurs (`Target/Self/AoE_Self/AoE_Target/
Skillshot/LineTarget/GroundTarget/Cone/Direction/Dash_Target/Dash_Direction`), dont plusieurs se
recoupent fortement une fois qu'on regarde leur géométrie réelle :

- `Skillshot` = même géométrie que `Direction` (sweep depuis le caster, direction de face),
  seule différence : s'arrête au 1er hit au lieu de tout percer.
- `LineTarget` = exactement ce que fait `Target` + `isTrajectory` (voyage vers la position de la
  cible verrouillée, perce tout sur le trajet) — pas un type à part, un COMBO de deux champs déjà
  existants.
- `Direction` = ce que fait `GroundTarget` + `isTrajectory` (voyage en ligne, perce tout), sauf
  que `Direction` ne prend AUCUNE visée aujourd'hui (`_skillDirection` mort, retombe toujours sur
  `caster.transform.forward` — voir §5).

Décision (validée avec Florian) : réduire à **6 targetType actifs** (`Target/Self/AoE_Self/
AoE_Target/GroundTarget/Cone`), absorber le comportement des 3 types redondants dans des
modificateurs composables sur les 6 restants (`isTrajectory`, `hasDelayedImpact`, + 2 nouveaux
champs introduits ici : `stopAtFirstHit`, `zoneFollowsAnchor`). `Dash_Target`/`Dash_Direction`
restent des `targetType` séparés, INCHANGÉS fonctionnellement (juste renumérotés) — ils seront
retravaillés en profondeur dans le futur chantier déplacement, pas ici.

## §1 — Enum TargetType : suppression + renumérotation

Vérifié contre **tous** les `.asset` existants du projet (aucun contenu de production — tout est
`_test`/prototype) :

| Ordinal actuel | Valeur | Assets l'utilisant |
|---|---|---|
| 0 | Target | 15 assets |
| 1 | Self | 0 |
| 2 | AoE_Self | 2 assets |
| 3 | AoE_Target | 0 |
| 4 | Skillshot | **0 — suppression sûre** |
| 5 | LineTarget | **0 — suppression sûre** |
| 6 | GroundTarget | 3 assets — **à migrer** |
| 7 | Cone | 0 |
| 8 | Direction | 1 asset — **à migrer, voir §1bis** |
| 9 | Dash_Target | 1 asset — **à migrer** |
| 10 | Dash_Direction | 0 |

Nouvelle énumération, séquentielle, `Skillshot`/`LineTarget`/`Direction` **supprimés purement**
(pas de placeholder `[Obsolete]` — décision explicite Florian, contrairement à la convention
"ordinal safety" habituelle du projet ailleurs : ici aucun asset ne référence ces 3 valeurs, donc
rien à casser) :

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3,
    GroundTarget = 4, Cone = 5, Dash_Target = 6, Dash_Direction = 7,
}
```

### Migration des assets (Task dédiée du plan)

- `Target`(0), `Self`(1), `AoE_Self`(2), `AoE_Target`(3) : **ordinal inchangé**, aucune migration.
- `GroundTarget` 6→4 : `skl_test_delayed_cast.asset`, `skl_test_delayed_channel.asset`,
  `skl_test_trajectory_storm.asset` — changer `targetType: 6` → `targetType: 4`.
- `Dash_Target` 9→6 : `skl_loup_special_test.asset` — changer `targetType: 9` → `targetType: 6`.
- `Cone` 7→5, `Dash_Direction` 10→7 : aucun asset actuel, rien à migrer, juste la nouvelle valeur
  pour toute création future.

### §1bis — `skl_basic.asset` (Direction, ordinal 8, supprimé)

C'est le `basicAttackSkill` d'un Mob test (`MobTestAnim+vfx`). Direction n'existant plus,
**décision : basculer sur `Target`(0)** — comportement standard pour un basicAttackSkill de Mob
(tous les autres mobs du projet utilisent déjà `Target` pour leur attaque de base ; un Mob
n'ayant de toute façon jamais de visée souris/direction propre, `Target` = hit direct sur sa
cible verrouillée est l'équivalent le plus proche et le plus cohérent avec le reste du contenu).

## §2 — Nouveau champ `stopAtFirstHit`

Remplace le rôle de l'ancien `Skillshot`. Un `bool`, visible uniquement quand `isTrajectory` est
coché ET `targetType` est `Target` ou `GroundTarget` (pas `Cone` — une forme angulaire qui
s'élargit n'a pas de notion de "1er ennemi transpercé" propre, elle touche tout ce qui entre
dans l'éventail par construction).

```csharp
[Tooltip("Si coché, le sweep s'arrête au PREMIER ennemi touché (comme l'ancien Skillshot — tir\n" +
         "précis). Si décoché (défaut), perce tout ce qui est sur le trajet (comme l'ancien\n" +
         "Direction/LineTarget). Cone non concerné — touche toujours tout l'éventail.")]
[ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget,
    AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Arrêt au premier hit")]
public bool stopAtFirstHit = false;
```

**Implémentation (`SkillSystem.TrajectoryRoutine`)** : dès qu'UN hit est enregistré (pass
initiale à l'origine OU boucle par frame), si `skill.stopAtFirstHit` est vrai, la coroutine
s'arrête **immédiatement** — le trajet ne continue pas jusqu'à `destination`. Conséquence sur la
combo `hasDelayedImpact` (§4) : si le sweep s'arrête tôt sur un hit, la zone différée (si
activée) se plante **au point d'arrêt réel**, pas à la `destination` d'origine — une flèche qui
s'arrête dans le premier ennemi touché plante son "explosion différée" là où elle s'est
plantée, pas plus loin. Le VFX de trajet (`trajectoryVfx`) est détruit à ce même point d'arrêt,
cohérent avec l'arrêt visuel.

## §3 — Cone visé à la souris

Aujourd'hui, tout skill `Direction/Skillshot/Cone` retombe sur `caster.transform.forward`
(facing du perso) car `SkillSystem.SetSkillDirection()` n'a qu'un seul appelant vivant dans tout
le projet : **`TargetingSystem.TryExecuteSkill()`, lui-même jamais appelé nulle part** — code
mort depuis le début. Le vrai flow de cast passe par `SkillBar.cs` (`TryLaunchSkill`/
`ResolveInstant`/`ResolveChannel`), qui ne connaît PAS ce mécanisme.

`TargetingSystem.ResolveDirection()` (privée, ligne ~474) est en revanche déjà bien pensée :
priorité à la cible engagée/sélectionnée (direction caster→cible), sinon raycast souris
(aplati sur XZ), sinon `player.transform.forward` en dernier recours. **Elle marche déjà**, juste
jamais appelée par le vrai jeu.

**Plan** : rendre `ResolveDirection()` publique sur `TargetingSystem`, et l'appeler depuis
`SkillBar.cs` juste avant `StartTrajectory()`/`Execute()`/`PlantDelayedZone()` pour tout skill
`targetType == Cone` (aux 2 sites d'appel existants, `ResolveInstant`/`ResolveChannel`, même
pattern que `SetGroundTargetPoint` déjà fait pour `GroundTarget`) :

```csharp
if (skill.targetType == TargetType.Cone)
    SkillSystem.Instance?.SetSkillDirection(TargetingSystem.Instance.ResolveDirection());
```

`TargetingSystem.TryExecuteSkill()` reste du code mort après ce chantier (toujours aucun
appelant) — hors scope de le supprimer ici, laissé tel quel (pas de régression, juste continue
d'être inutilisé).

## §4 — `hasDelayedImpact` : extension complète

### Standalone (sans `isTrajectory`)

Déjà supporté aujourd'hui pour `Target`/`GroundTarget`/`AoE_Target`. **Étendu à `Self`/
`AoE_Self`** (validé Florian — ex: fiole de poison qui explose à tes pieds après délai,
tourbillon-zone qui démarre après un court délai). **PAS pour `Cone`** — une zone a besoin d'une
position, `Cone` sans `isTrajectory` n'a jamais de destination/tip résolu, ça n'a pas de sens
géométrique (redondant avec `AoE_Self`, qui couvre déjà "zone instantanée centrée sur moi").

Ajouter `TargetType.Self`, `TargetType.AoE_Self` à la liste `[ShowIf]` de `hasDelayedImpact` et
au check `hasDelayedImpactSupportedTargetType` dans `OnValidate()`.

### Combo avec `isTrajectory`

Déjà câblé pour `Cone`/`Direction`(→devient obsolète, remplacé par `GroundTarget`)/`GroundTarget`
(chantier précédent). **Ajouter `Target`** à la liste des combos autorisés — c'est le
remplacement direct de l'ancien `LineTarget` + zone à l'arrivée (ex: lance qui transperce
jusqu'à la cible ET explose/stun à l'arrivée). Mettre à jour :
- `TrajectoryRoutine`'s `plantsZoneAtEnd` : ajouter `|| skill.targetType == TargetType.Target`.
- `SkillData.OnValidate()` : `trajectoryPlusZoneSupportedTargetType` inclut désormais `Target`.
- Tooltips `isTrajectory`/`hasDelayedImpact` mis à jour (liste des 4 types : Cone/GroundTarget/
  Target, `Direction` retiré des mentions puisqu'il n'existe plus).

Résumé final des types supportant le combo `isTrajectory` + `hasDelayedImpact` simultané :
**Cone, GroundTarget, Target** (3, pas 4 — `Direction` disparaît du vocabulaire).

⚠ `TargetType.Direction` n'existera PLUS après §1 — toute comparaison `skill.targetType ==
TargetType.Direction` restante dans le code (dont `plantsZoneAtEnd` dans `TrajectoryRoutine`,
et les tableaux `[ShowIf]`/checks `OnValidate` de `isTrajectory`/`hasDelayedImpact`) est une
erreur de compilation à ce stade — il ne s'agit pas d'AJOUTER `Target` à côté de `Direction`,
mais de REMPLACER chaque occurrence de `Direction` par `Target` (le nouveau porteur du même
comportement).

## §5 — Nouveau champ `zoneFollowsAnchor`

Un `bool`, visible uniquement si `hasDelayedImpact` est coché ET `targetType` est `Self`/
`AoE_Self`/`Target`/`AoE_Target` (jamais `GroundTarget` — un point au sol n'a pas d'entité à
suivre, ni `Cone` — pas de standalone `hasDelayedImpact` pour Cone, voir §4).

```csharp
[Tooltip("Si coché, la zone RECALCULE sa position à chaque tick sur l'entité vivante " +
         "(Self/AoE_Self : le CASTER — ex: tourbillon qui te suit si tu te déplaces en spinnant. " +
         "Target/AoE_Target : la CIBLE — ex: corbeaux qui suivent une cible marquée). " +
         "Si décoché (défaut), la zone reste figée à la position capturée au lancement — ex: " +
         "fiole de poison lancée au sol, zone qui punit une cible qui s'enfuit. " +
         "GroundTarget : toujours figé, ce champ n'apparaît pas (pas d'entité à suivre).")]
[ShowIf(nameof(targetType), TargetType.Self, TargetType.AoE_Self, TargetType.Target,
    TargetType.AoE_Target, AndField = nameof(hasDelayedImpact), AndValue = true,
    Header = "⑦ Zone qui suit")]
public bool zoneFollowsAnchor = false;
```

**Implémentation (`SkillSystem.DelayedZoneRoutine`)** : la coroutine prend aujourd'hui un
`Vector3 position` figé. Il lui faut en plus un paramètre `Entity anchorEntity` (nullable — le
caster pour Self/AoE_Self, la cible pour Target/AoE_Target, `null` sinon/pour GroundTarget). À
CHAQUE tick, si `skill.zoneFollowsAnchor` est vrai ET `anchorEntity` est non-null et vivant, la
position effective du tick = `anchorEntity.transform.position` (pas le `position` figé d'origine)
— sinon retombe sur `position` (figé) : soit parce que `zoneFollowsAnchor` est faux, soit parce
que l'entité suivie est morte entre-temps (évite un `NullReferenceException`/téléportation en
`(0,0,0)`). Le marker visuel (`vfxZoneMarker`), s'il existe, doit suivre pareil — le plus simple
est de le **parenter** au transform de `anchorEntity` quand `zoneFollowsAnchor` est actif (Unity
gère le suivi automatiquement, rotation incluse — cohérent pour un VFX de tourbillon qui doit
tourner avec le joueur), et le laisser en `world space` sinon (comportement actuel, inchangé).

`PlantDelayedZone()` (point d'entrée public) doit désormais résoudre et passer `anchorEntity` à
`DelayedZoneRoutine` : `caster` si `targetType` est `Self`/`AoE_Self`, `target` si `Target`/
`AoE_Target`, `null` sinon (`GroundTarget`). Les sites internes qui invoquent
`DelayedZoneRoutine` directement (combo `isTrajectory`+`hasDelayedImpact` dans
`TrajectoryRoutine`/`TrajectoryConeRoutine`) passent `null` — le point d'arrivée d'une
trajectoire n'a pas de sens à "suivre" une entité, c'est déjà un point fixe résolu au bout du
trajet.

## §6 — Trous trouvés en re-relisant (champs déjà codés depuis, jamais reliés à cette spec)

Deux mécanismes existent déjà dans `SkillSystem.cs`/`SkillData.cs` (ajoutés APRÈS la première
version de cette spec) qui référencent encore `Direction`/`LineTarget`/`Skillshot` — sans les
corriger ici, le projet ne compile plus une fois §1 appliqué.

### §6a — `trajectoryShape`/`trajectoryWidth`/`trajectoryDepth` (forme Sphere/Box)

Ces 3 champs (déjà en prod, servent à un mur de feu large type `Box` vs une flèche fine type
`Sphere`) ont un `[ShowIf]` qui liste EXPLICITEMENT `GroundTarget, Direction, Target, AoE_Target,
LineTarget, Skillshot` comme valeurs autorisées. Après §1, retirer `Direction`/`LineTarget`/
`Skillshot` de cette liste (compileraient plus sinon) — il reste `GroundTarget, Target,
AoE_Target`, inchangés fonctionnellement, rien d'autre à toucher (la logique `TrajectoryRoutine`
qui lit `trajectoryShape` ne référence déjà aucun de ces 3 types supprimés).

### §6b — Branche fallback de `StartTrajectory()` (ex-branche Direction/Skillshot)

`StartTrajectory()` a 3 branches selon `targetType` pour calculer `destination` : `GroundTarget`,
`Target`/`AoE_Target`/`LineTarget`, et un `else` catch-all — c'est CETTE branche `else` qui
portait tout le comportement `Direction`/`Skillshot` (facing du caster + `range`), **y compris le
fix de dépassement de portée** (`forwardHalfExtent` — le bord de la sphère/boîte dépasse son
centre de son propre rayon/profondeur, compensé en raccourcissant la distance de voyage, voir
historique du chantier trajectoire).

Une fois `Direction`/`Skillshot` supprimés, **aucun `targetType` valide n'atteint plus jamais
cette branche** en usage normal — `GroundTarget`/`Target`/`AoE_Target` ont chacun leur propre
branche dédiée, `Cone` sort tôt de la méthode (branche séparée). Cette branche `else` ne
s'exécute donc plus que pour un `targetType` réellement incompatible avec `isTrajectory` (cas
déjà couvert par le warning `OnValidate` existant — "non supporté... traité comme Direction par
défaut"). **Décision : la garder telle quelle, comme filet de sécurité générique** (jamais un
skill qui a déjà coûté mana/CD ne doit se solder par un no-op silencieux — même philosophie que
partout ailleurs dans ce fichier), juste renommer son commentaire ("ex-Direction/Skillshot" →
"fallback générique, targetType non supporté par isTrajectory") et retirer les mentions de
`Direction`/`Skillshot` dans les commentaires alentour (`ExecuteDirection()`/`ExecuteSkillshot()`
n'existeront plus pour la référence croisée). Le calcul `forwardHalfExtent`/`effectiveRange`
reste identique, toujours correct pour ce cas résiduel.

### §6c — Autres références trouvées en grep exhaustif

Un grep `TargetType\.(Direction|Skillshot|LineTarget)` sur tout `Assets/Scripts/*.cs` remonte
encore 4 fichiers (au-delà de ce que §1-§5 couvrent déjà) :

- **`Data/Skills/SkillData.cs`, champ `aoeFaction`** — son `[ShowIf]` liste aussi `Direction`/
  `Skillshot`/`LineTarget` parmi les `targetType` valides. Retirer les 3, garder le reste
  (`AoE_Self`/`AoE_Target`/`GroundTarget`/`Cone`/`Target`/`Dash_Target`) inchangé.
- **`Data/Skills/SkillBar.cs`, `needsTarget` (~ligne 432)** — check qui décide si une `Entity`
  cible est obligatoire avant le cast. Retirer `|| skill.targetType == TargetType.LineTarget` —
  `Target`/`AoE_Target`/`Dash_Target` suffisent déjà (LineTarget devient `Target`, déjà dans la
  liste).
- **`Data/Skills/SkillBar.cs`, `EngageAndFaceTarget()` (~ligne 1056)** — exclut aujourd'hui
  `Self`/`AoE_Self`/`GroundTarget`/`Direction`/`Skillshot`/`Cone` du snap-vers-la-cible-et-engage
  (skills sans cible verrouillée, aim libre). Retirer `Direction`/`Skillshot` de cette liste de
  garde, **garder `Cone`** (reste mouse-aimed, jamais un vrai lock sur une Entity — même après
  §3). `Target` (absorbe LineTarget) n'a jamais été dans cette liste d'exclusion et n'a pas à y
  être — un skill `Target`-based DOIT engager/faire face à sa cible, comportement inchangé.
- **`Combat/TargetingSystem.cs`, `TryExecuteSkill()`** — méthode déjà signalée comme code mort
  (§3, aucun appelant, reste hors scope de suppression). Mais son `switch` contient encore
  `case TargetType.Direction:`/`case TargetType.Skillshot:`/`case TargetType.LineTarget:` —
  ça ne COMPILE plus une fois ces valeurs supprimées de l'enum, même si la méthode n'est jamais
  appelée. Retirer ces 3 `case` (les requêtes correspondantes, si un jour cette méthode était
  rebranchée, tomberaient sur `default` — pas un souci puisqu'elle est morte de toute façon).

## Fichiers touchés

- `Data/Skills/SkillData.cs` — enum `TargetType` renuméroté, `stopAtFirstHit` +
  `zoneFollowsAnchor` ajoutés, `ShowIf`/tooltips/`OnValidate()` mis à jour partout où `Skillshot`/
  `LineTarget`/`Direction` étaient mentionnés (dont `trajectoryShape`/`trajectoryWidth`/
  `trajectoryDepth` §6a, et `aoeFaction` §6c).
- `Combat/SkillSystem.cs` — `TrajectoryRoutine` (arrêt anticipé `stopAtFirstHit`, zone à
  `Target` en plus de `GroundTarget`/`Cone`, branche fallback renommée §6b), `TrajectoryConeRoutine`
  (inchangé, déjà bon), `DelayedZoneRoutine`+`PlantDelayedZone` (paramètre `anchorEntity`),
  `ExecuteDirection`/`ExecuteSkillshot` (méthodes mortes après suppression de ces 2 `targetType` —
  à supprimer), `DispatchByTargetType` (retire les `case Direction/Skillshot/LineTarget`), bloc de
  commentaire en tête de fichier ("TargetType gérés", lignes ~29-40) mis à jour pour la nouvelle
  liste.
- `Combat/TargetingSystem.cs` — `ResolveDirection()` passe de `private` à `public` ; retire les 3
  `case Direction/Skillshot/LineTarget` du `switch` mort de `TryExecuteSkill()` (§6c, sinon ne
  compile plus).
- `Data/Skills/SkillBar.cs` — les 2 sites de résolution (`ResolveInstant`/`ResolveChannel`)
  appellent `SetSkillDirection(TargetingSystem.Instance.ResolveDirection())` pour `Cone` ; retire
  `LineTarget` de `needsTarget` et `Direction`/`Skillshot` de la liste d'exclusion
  `EngageAndFaceTarget()` (§6c).
- 5 `.asset` existants renumérotés/migrés (§1, §1bis).
- `docs/guide-utilisation/creation-skills.md` — tableau des targetType et sections ⑥/⑦ à
  réécrire pour la nouvelle liste à 8 valeurs (6 actifs + 2 Dash inchangés) et les 2 nouveaux
  champs.

## Vérification (Play Mode manuel, pas de framework de test auto)

1. Compiler, 0 erreur (`Skillshot`/`LineTarget`/`Direction` ne doivent plus apparaître nulle
   part dans le code — un grep post-implémentation doit être vide).
2. `Target` + `isTrajectory` + `stopAtFirstHit` : la trajectoire s'arrête bien au 1er ennemi,
   pas au-delà.
3. `Target` + `isTrajectory` + `hasDelayedImpact` (sans `stopAtFirstHit`) : perce tout le monde
   EN ROUTE vers la cible, ET plante une zone à l'arrivée sur la cible.
4. `Cone` : viser avec la souris dans une direction différente de où le perso regarde → le cône
   part bien vers la souris, pas vers le facing.
5. `AoE_Self` + `hasDelayedImpact`, `impactDelay=0`, `zoneDuration>0`, `zoneTickInterval` court,
   `zoneFollowsAnchor` coché : se déplacer pendant les ticks → la zone suit le joueur.
6. Même test avec `zoneFollowsAnchor` décoché : la zone reste plantée où le skill a été lancé.
7. `Target`/`AoE_Target` + `hasDelayedImpact` + `zoneFollowsAnchor` : la cible bouge/fuit pendant
   les ticks → la zone la suit (coché) ou reste sur place (décoché).
8. Les 5 `.asset` migrés (§1) : ouvrir chacun dans l'Inspector, vérifier que le `targetType`
   affiché correspond bien à l'intention d'origine (pas de `Debug.LogWarning` `OnValidate`
   inattendu).
9. `GroundTarget` + `isTrajectory` + `trajectoryShape = Box` (mur de feu large) : toujours
   fonctionnel après renumérotation (§6a) — largeur/profondeur respectées.
10. `skl_test_trajectory_storm.asset` en particulier (déjà `isTrajectory=1`, `targetType`
    migré 6→4) : recharger dans l'Inspector, vérifier qu'aucun champ ne s'affiche cassé/vide
    (les nouveaux champs `stopAtFirstHit`/`zoneFollowsAnchor`/`trajectoryShape` retombent sur
    leurs valeurs par défaut, Unity gère les champs absents du YAML existant sans erreur).
