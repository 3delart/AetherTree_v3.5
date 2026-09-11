# Zone à impact différé (chantier C) — Design

## Contexte et motivation

Chantier A (Canalisation) et B (hit-frame sync) ont établi que les dégâts d'un skill résolvent
à un point précis — fin d'animation pour un skill instant, fin de canalisation pour un skill à
`castTime > 0`. Ce modèle marche bien pour des attaques directes (l'archer qui tire, le mage
qui lance une boule de feu rapide) où le temps de vol du projectile est trop court pour être
perçu par le joueur : les dégâts sont déjà décidés, le VFX du projectile n'est qu'un habillage
visuel qui peut légèrement "traîner" derrière la résolution mécanique sans que ça pose
problème.

Ce modèle casse pour des effets **télégraphiés/lents** — l'exemple qui a fait émerger ce
chantier : un mage de feu canalise 2 secondes pour invoquer une comète, qui met ensuite 2
secondes supplémentaires à tomber du ciel. Si les dégâts résolvent à la fin de la canalisation
(comme le ferait un skill normal), la cible encaisse le coup au moment où la comète *apparaît*,
alors qu'elle a encore 2 secondes, visuellement, pour s'écarter — une fenêtre d'esquive qui
existe à l'écran mais pas en mécanique. Le délai n'est plus un détail visuel ici : il est le
gameplay (un effet de zone télégraphié qu'on peut éviter en bougeant).

Ce chantier ajoute un mécanisme de **résolution différée par zone fixe au sol**, orthogonal à
la Canalisation (qui gère déjà le délai *avant* le lancement) — celui-ci gère le délai *après*
la résolution existante, entre le moment où l'effet est "décidé" visuellement (la zone se
plante au sol) et le moment où il touche réellement.

Pose également en même temps trois champs VFX sur `SkillData` (`vfxCast`, `vfxZoneMarker`,
`vfxImpact`) en prévision du prochain chantier (système VFX complet, non traité ici) — la zone
différée a de toute façon besoin d'un minimum de retour visuel pour être un mécanisme jouable
(le joueur doit voir où esquiver), donc ces champs sont introduits maintenant plutôt que d'être
retravaillés deux fois.

## Non-objectifs (explicitement hors scope)

- **Trajectoire mobile** (ex: une tornade envoyée en ligne droite qui balaie une zone entre le
  caster et un point, touchant tout sur son passage) — mécanisme différent (zone qui se
  déplace dans le temps, pas une zone fixe), pas traité ici.
- **Mob/PNJ** — n'utilisent pas encore chantier A/B (toujours sur l'ancien chemin synchrone
  `SkillSystem.Execute()`), donc ce mécanisme reste joueur uniquement, cohérent avec A/B.
- **Système VFX complet** (chantier D) — seuls les 3 champs de référence prefab sont posés ici ;
  le comportement de spawn/orientation/composition riche (projectile qui voyage, marqueur
  composite, etc. — voir brainstorm chantier D) sera traité dans son propre chantier. Ici, les
  3 champs sont utilisés de façon minimale (spawn simple à une position, pas de logique
  d'orientation/trajectoire).
- **MultiHit / ComboSequence** — ce mécanisme ne s'applique qu'aux skills `executionType =
  Normal` (instant ou canalisé). Combiner avec MultiHit/Combo n'est pas géré.

## Champs SkillData

Nouveaux champs, section "⑨ Exécution avancée" ou nouvelle sous-section dédiée :

```csharp
[Tooltip("Transforme la résolution de ce skill en zone au sol à impact différé — au lieu de\n" +
         "résoudre les dégâts immédiatement au point de résolution existant (fin d'anim / fin\n" +
         "de canalisation), une zone se plante à cet endroit et les dégâts n'appliquent qu'après\n" +
         "impactDelay secondes, à qui se trouve RÉELLEMENT dans la zone à ce moment (permet une\n" +
         "vraie fenêtre d'esquive). Non supporté avec MultiHit/ComboSequence.")]
[ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget, TargetType.AoE_Target,
    AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
    Header = "⑨Bis Zone à impact différé")]
public bool hasDelayedImpact = false;

[Tooltip("Délai en secondes entre le plantage de la zone (résolution existante) et le premier\n" +
         "tick de dégâts.")]
[ShowIf(nameof(hasDelayedImpact), true)]
public float impactDelay = 1f;

[Tooltip("Durée pendant laquelle la zone reste active APRÈS le premier tick (impactDelay).\n" +
         "0 = un seul tick, la zone disparaît ensuite (impact différé simple — ex: comète).\n" +
         "> 0 = zone persistante qui retick toutes les zoneTickInterval secondes pendant cette\n" +
         "durée (ex: zone de lave, pluie de glace).")]
[ShowIf(nameof(hasDelayedImpact), true)]
public float zoneDuration = 0f;

[Tooltip("Fréquence des ticks de dégâts pendant zoneDuration. Ignoré si zoneDuration = 0.")]
[ShowIf(nameof(hasDelayedImpact), true)]
public float zoneTickInterval = 1f;
```

Section "⑩ Visuel & Son", en plus de `attackAnimation`/`channelAnimation` déjà existants :

```csharp
[Tooltip("VFX qui reste sur le caster, spawné au LANCEMENT (pentacle aux pieds, glow aux\n" +
         "mains...). Optionnel — vide = pas de VFX de cast.")]
public GameObject vfxCast;

[Tooltip("VFX de zone/avertissement — spawné quand la zone se plante au sol (point de\n" +
         "résolution existant), reste affiché jusqu'à la détonation. Actif uniquement si\n" +
         "hasDelayedImpact = true.")]
[ShowIf(nameof(hasDelayedImpact), true)]
public GameObject vfxZoneMarker;

[Tooltip("VFX joué exactement au moment où les dégâts s'appliquent réellement — immédiat pour\n" +
         "un skill sans délai (comportement historique de vfxPrefab), ou à chaque détonation\n" +
         "pour une zone à impact différé.")]
[FormerlySerializedAs("vfxPrefab")]
public GameObject vfxImpact;
```

`[FormerlySerializedAs("vfxPrefab")]` préserve la valeur déjà assignée sur tous les assets
`SkillData` existants au moment du renommage — sans cet attribut, chaque skill perdrait
silencieusement sa référence VFX actuelle. Tous les usages de `skill.vfxPrefab` dans le code
(`SkillSystem.cs`, `HitStep.vfxPrefab` reste inchangé — c'est un champ séparé sur `HitStep`, pas
concerné par ce renommage) deviennent `skill.vfxImpact`.

`OnValidate()` gagne un avertissement supplémentaire (même famille que celui déjà existant pour
`castTime` + MultiHit/ComboSequence) :

```csharp
if (hasDelayedImpact && executionType != SkillExecutionType.Normal)
    Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec executionType = " +
                      $"{executionType} — combinaison non gérée, la zone différée ne fonctionne " +
                      "qu'avec executionType = Normal (instant ou canalisé).", this);
```

## Flux de résolution

### Sans `hasDelayedImpact` (comportement actuel, inchangé)

Résolution existante (chantier A/B) : `SkillSystem.ResolveExecute()` (skill instant, via
`SkillBar.ResolveInstant()`) ou `SkillSystem.Execute()` (canalisation, via
`SkillBar.ResolveChannel()`) — dégâts calculés immédiatement, `vfxImpact` (ex-`vfxPrefab`)
spawné à la cible/au point résolu, exactement comme aujourd'hui. `vfxCast`, s'il est défini,
spawne au lancement (`StartInstant()`/`StartChannel()`), indépendamment de tout ça.

### Avec `hasDelayedImpact = true`

1. **Lancement** (`StartInstant()` ou `StartChannel()`, inchangé) : mana/HP/gold dépensés,
   `vfxCast` spawné si défini (à la position du caster). Pour une canalisation, le joueur reste
   immobile pendant `castTime` comme aujourd'hui.
2. **Point de résolution existant** (fin d'anim pour instant, fin de canalisation) : au lieu
   d'appeler `ResolveExecute()`/`Execute()` pour calculer les dégâts, `SkillBar` appelle une
   nouvelle méthode `SkillSystem.PlantDelayedZone(skill, caster, target)`. CD/GCD sont posés
   **ici, à ce moment précis**, exactement comme le ferait la résolution normale — ce chantier
   ne touche PAS au timing CD/GCD/coûts, seulement à quand les DÉGÂTS s'appliquent.
3. **`PlantDelayedZone()`** : résout la position fixe de la zone —
   - `targetType = GroundTarget` → `_groundTargetPoint` (déjà existant, utilisé tel quel).
   - `targetType = Target`/`AoE_Target` → position ACTUELLE de `target.transform.position` à
     cet instant, capturée en `Vector3` (pas une référence à l'entité — si la cible bouge
     ensuite, la zone ne la suit pas, c'est ce qui permet l'esquive).
   Spawne `vfxZoneMarker` à cette position (si défini), puis démarre une coroutine
   (`StartCoroutine`, même pattern que `ExecuteMultiHit` existant dans le même fichier).
4. **Après `impactDelay` secondes** : premier tick — `DetonateDelayedZone()` fait
   `Physics.OverlapSphere(position, skill.aoeRadius)`, filtre chaque collider trouvé via
   `PassesAoeFilter(skill.aoeFaction, caster, entity)` (méthode déjà existante, réutilisée telle
   quelle — même logique que `ExecuteGroundTarget`/`ExecuteAoETarget`), puis pour chaque entité
   qui passe le filtre : `ApplyEffectType()`, `ApplyStatusEffects()`, `CheckKill()` — les mêmes
   appels que le reste du fichier fait déjà pour toute résolution AoE. Spawne `vfxImpact` à la
   position de la zone.
5. **Si `zoneDuration > 0`** : au lieu de s'arrêter après le premier tick, la coroutine continue
   — attend `zoneTickInterval` secondes, refait un tick identique (nouveau
   `Physics.OverlapSphere` + dégâts à qui est présent À CE MOMENT), répète jusqu'à ce que
   `zoneDuration` total se soit écoulé depuis le premier tick. `vfxZoneMarker` reste affiché
   pendant toute la durée ; `vfxImpact` rejoue à chaque tick. Si `zoneDuration = 0`,
   `vfxZoneMarker` est détruit après le tick unique et la zone disparaît.

Chaque tick est un check INDÉPENDANT — une entité qui entre dans la zone entre deux ticks se
fait toucher au tick suivant ; une entité qui sort n'est plus touchée au tick d'après. Aucun
état de "déjà touché" n'est conservé entre les ticks (contrairement à un DoT classique qui, une
fois appliqué, tourne indépendamment de la position de la cible).

## Emplacement du code

- **`Data/Skills/SkillBar.cs`** — `ResolveInstant()` et `ResolveChannel()` : ajout d'un
  branchement `if (skill.hasDelayedImpact) SkillSystem.Instance?.PlantDelayedZone(skill,
  _player, target); else <comportement existant inchangé>` juste avant l'appel actuel à
  `ResolveExecute()`/`Execute()`. CD/GCD/`DelayAutoAttack` restent posés dans le même bloc,
  inchangés, indépendamment de cette branche.
- **`Combat/SkillSystem.cs`** — nouvelles méthodes `PlantDelayedZone(SkillData, Entity, Entity)`
  et une coroutine privée (ex: `DelayedZoneRoutine`) qui fait le(s) tick(s) de détonation en
  réutilisant `PassesAoeFilter`/`ApplyEffectType`/`ApplyStatusEffects`/`CheckKill` déjà présents
  dans ce fichier. Renommage `vfxPrefab` → `vfxImpact` partout où le champ est lu.
- **`Data/Skills/SkillData.cs`** — nouveaux champs ci-dessus + avertissement `OnValidate()`.

## Vérification

Pas de framework de test automatisé sur ce projet — vérification manuelle en Play Mode :

1. Skill `hasDelayedImpact = true`, `zoneDuration = 0`, `targetType = Target` — cibler une
   entité, vérifier qu'une zone se plante à sa position ACTUELLE, que la cible peut s'écarter
   pendant `impactDelay` et éviter les dégâts si elle bouge à temps, et les subir si elle reste.
2. Même test avec `targetType = GroundTarget` — vérifier que la zone se plante au point cliqué,
   pas sur une entité.
3. Skill `zoneDuration > 0` (ex: 5s, `zoneTickInterval = 1`) — vérifier plusieurs ticks de
   dégâts espacés, qu'une entité qui entre en cours de route se fait toucher au tick suivant, et
   qu'une entité qui sort n'est plus touchée.
4. Vérifier `CD`/`GCD`/mana posés au lancement de la zone (résolution existante), pas à la
   détonation — le joueur peut relancer un autre skill normalement pendant que la zone attend
   son détonation.
5. Vérifier que les assets `SkillData` existants ayant déjà un `vfxPrefab` assigné conservent
   bien leur VFX après le renommage en `vfxImpact` (grâce à `[FormerlySerializedAs]`).
6. `hasDelayedImpact = true` + `executionType = MultiHit` (ou `ComboSequence`) — vérifier le
   warning `OnValidate()` en Console, et que rien n'essaie de câbler ce mécanisme dans ce cas
   (reste sur le comportement MultiHit/Combo standard).
