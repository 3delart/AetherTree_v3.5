# Simplification TargetType — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Réduire `SkillData.TargetType` de 11 valeurs à 8 (6 actifs pour le ciblage dégâts/
debuff/buff + 2 Dash inchangés), en absorbant `Skillshot`/`LineTarget`/`Direction` dans des
modificateurs composables (`stopAtFirstHit`, `zoneFollowsAnchor`, et l'`isTrajectory`/
`hasDelayedImpact` déjà existants), et en réparant la visée souris de `Cone` (code déjà écrit,
jamais branché).

**Architecture:** Suppression pure des 3 valeurs d'enum redondantes (vérifié : zéro `.asset` ne
les référence), renumérotation séquentielle des 8 survivantes. Deux nouveaux champs `bool` sur
`SkillData`. `SkillSystem.TrajectoryRoutine`/`DelayedZoneRoutine`/`PlantDelayedZone` étendus pour
porter le nouveau comportement. `TargetingSystem.ResolveDirection()` (déjà correcte, orpheline)
branchée depuis `SkillBar.cs`.

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification par compilation +
grep + Play Mode manuel (Florian).

**Spec:** `docs/superpowers/specs/2026-09-19-targettype-simplification-design.md` — lire en
entier avant de commencer, ce plan en transcrit le contenu mais la spec porte le raisonnement
complet derrière chaque décision (notamment §6a/§6b/§6c, les trous trouvés en re-relecture).

## Global Constraints

- Aucun framework de test automatisé dans ce projet — chaque tâche se vérifie par compilation
  Unity (0 erreur) + `grep` ciblé, jamais par une suite de tests.
- Repo root réel = `c:\AetherTree_v3.5\Assets\Scripts` (le `.git` vit ici, pas à la racine
  `C:\AetherTree_v3.5`).
- **Le déplacement (Dash/Pull/Push/Teleport/regroupement d'ennemis) est HORS SCOPE** —
  `Dash_Target`/`Dash_Direction` sont renumérotés mais leur comportement ne change PAS dans ce
  chantier.
- Suppression pure de `Skillshot`/`LineTarget`/`Direction` (pas de placeholder `[Obsolete]`) —
  déviation délibérée et déjà validée de la convention "ordinal safety" habituelle du projet,
  UNIQUEMENT parce qu'aucun `.asset` ne les référence (re-vérifié en Tâche 1, ne pas faire
  confiance aveuglément au grep de la spec — un asset a pu être créé/modifié entretemps).
- Toutes les modifications de code touchant `Data/Skills/SkillData.cs` et ses 3 consommateurs
  (`Combat/SkillSystem.cs`, `Combat/TargetingSystem.cs`, `Data/Skills/SkillBar.cs`) forment un
  seul ensemble qui doit compiler ENSEMBLE — Tâche 1 seule ne compile pas tant que les Tâches
  2-4 n'ont pas aussi retiré leurs références aux 3 valeurs supprimées. C'est un vrai prérequis
  séquentiel, pas une suggestion d'ordre.

---

### Task 1: `SkillData.cs` — enum + 2 nouveaux champs + nettoyage ShowIf/OnValidate

**Files:**
- Modify: `Data/Skills/SkillData.cs`

**Interfaces:**
- Produces: `enum TargetType { Target=0, Self=1, AoE_Self=2, AoE_Target=3, GroundTarget=4,
  Cone=5, Dash_Target=6, Dash_Direction=7 }` (remplace l'enum à 11 valeurs). Nouveaux champs
  publics `bool stopAtFirstHit` et `bool zoneFollowsAnchor` sur `SkillData`. Ces 3 éléments sont
  consommés par les Tâches 2-4.
- Consumes: rien (première tâche du plan).

⚠ **Ce fichier seul ne compile pas après cette tâche** — `Combat/SkillSystem.cs`,
`Combat/TargetingSystem.cs` et `Data/Skills/SkillBar.cs` référencent encore
`TargetType.Direction`/`.Skillshot`/`.LineTarget` jusqu'aux Tâches 2-4. Ne pas essayer de
compiler seul après cette tâche — c'est attendu, pas une erreur à corriger ici.

- [ ] **Step 1: Re-vérifier qu'aucun `.asset` ne référence les 3 valeurs à supprimer**

Depuis `c:\AetherTree_v3.5` (PAS `Assets/Scripts` — les `.asset` sont sous `Assets/Content`) :

```bash
grep -rl "targetType: 4$" --include=*.asset . 2>/dev/null   # Skillshot
grep -rl "targetType: 5$" --include=*.asset . 2>/dev/null   # LineTarget
```

Expected: **aucune sortie** pour les deux commandes (déjà vérifié en écrivant la spec — si un
asset apparaît ici alors qu'il n'apparaissait pas dans la spec, STOP, ne pas continuer cette
tâche, remonter le cas à Florian d'abord — la suppression pure n'est plus sûre).

- [ ] **Step 2: Renuméroter l'enum `TargetType`**

Dans `Data/Skills/SkillData.cs`, remplacer :

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3, Skillshot = 4,
    LineTarget = 5, GroundTarget = 6, Cone = 7, Direction = 8, Dash_Target = 9, Dash_Direction = 10
}
```

par :

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3,
    GroundTarget = 4, Cone = 5, Dash_Target = 6, Dash_Direction = 7,
}
```

- [ ] **Step 3: Nettoyer le `[ShowIf]` de `aoeFaction`**

Remplacer :

```csharp
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Direction, TargetType.Skillshot, TargetType.LineTarget,
        TargetType.Target, TargetType.Dash_Target, DisplayName = "Cible")]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;
```

par :

```csharp
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Target, TargetType.Dash_Target, DisplayName = "Cible")]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;
```

- [ ] **Step 4: Nettoyer le tooltip + `[ShowIf]` de `hasDelayedImpact`, ajouter `Self`/`AoE_Self`**

Remplacer tout le bloc `hasDelayedImpact` (tooltip + attribut + champ) :

```csharp
    [Tooltip("Transforme la résolution de ce skill en zone au sol à impact différé — au lieu de\n" +
             "résoudre les dégâts immédiatement au point de résolution existant (fin d'anim / fin\n" +
             "de canalisation), une zone se plante à cet endroit et les dégâts n'appliquent qu'après\n" +
             "impactDelay secondes, à qui se trouve RÉELLEMENT dans la zone à ce moment (permet une\n" +
             "vraie fenêtre d'esquive). Non supporté avec MultiHit/ComboSequence — la vraie garde\n" +
             "contre un step de combo est côté code (SkillBar.ResolveInstant), pas ce warning seul.\n" +
             "Cone/Direction/GroundTarget + isTrajectory = seule combinaison autorisée avec\n" +
             "isTrajectory (exception délibérée, voir tooltip isTrajectory) — la zone se plante au\n" +
             "bout du cône/de la ligne/du trajet, EN PLUS du dégât immédiat du sweep/de l'expansion\n" +
             "(double dégât voulu si une cible reste dans le rayon final). Tous les autres targetType\n" +
             "restent mutuellement exclusifs.")]
    [ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget, TargetType.AoE_Target,
        TargetType.Cone, TargetType.Direction,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Zone à impact différé")]
    public bool hasDelayedImpact = false;
```

par :

```csharp
    [Tooltip("Transforme la résolution de ce skill en zone au sol à impact différé — au lieu de\n" +
             "résoudre les dégâts immédiatement au point de résolution existant (fin d'anim / fin\n" +
             "de canalisation), une zone se plante à cet endroit et les dégâts n'appliquent qu'après\n" +
             "impactDelay secondes, à qui se trouve RÉELLEMENT dans la zone à ce moment (permet une\n" +
             "vraie fenêtre d'esquive). Non supporté avec MultiHit/ComboSequence — la vraie garde\n" +
             "contre un step de combo est côté code (SkillBar.ResolveInstant), pas ce warning seul.\n" +
             "Cone/GroundTarget/Target + isTrajectory = seule combinaison autorisée avec isTrajectory\n" +
             "(exception délibérée, voir tooltip isTrajectory) — la zone se plante au bout du\n" +
             "cône/du trajet, EN PLUS du dégât immédiat du sweep/de l'expansion (double dégât voulu\n" +
             "si une cible reste dans le rayon final). Tous les autres targetType restent\n" +
             "mutuellement exclusifs.")]
    [ShowIf(nameof(targetType), TargetType.Self, TargetType.AoE_Self, TargetType.Target,
        TargetType.GroundTarget, TargetType.AoE_Target, TargetType.Cone,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Zone à impact différé")]
    public bool hasDelayedImpact = false;
```

- [ ] **Step 5: Nettoyer le tooltip + `[ShowIf]` de `isTrajectory`, remplacer Direction par Target**

Remplacer :

```csharp
    [Tooltip("Transforme la résolution de ce skill en hitbox mobile qui voyage du caster vers " +
             "une destination (au lieu de résoudre les dégâts au point de résolution existant, " +
             "une trajectoire est parcourue et touche tout ce qui se trouve sur son passage). " +
             "Distinct de hasDelayedImpact (zone FIXE une fois plantée) — mutuellement exclusif, " +
             "SAUF Cone/Direction/GroundTarget (seule combinaison autorisée, voir tooltip " +
             "hasDelayedImpact) : " +
             "la zone se plante alors au bout du trajet, EN PLUS du dégât immédiat.\n" +
             "GroundTarget : voyage vers le point cliqué au sol.\n" +
             "Direction / Skillshot : voyage en ligne droite sur une distance = range (même " +
             "géométrie que la résolution instantanée de Skillshot, juste étalée dans le temps).\n" +
             "Target / AoE_Target / LineTarget : voyage vers la position de la cible AU LANCEMENT " +
             "(figée, pas de homing — si la cible bouge après coup, la trajectoire continue vers " +
             "le point où elle était, peut la manquer).\n" +
             "Cone : PAS un point qui voyage — un cône qui s'ÉLARGIT depuis le caster (portée 0 → " +
             "range à la vitesse projectileSpeed), angle = coneHalfAngle. Chemin de résolution " +
             "séparé (TrajectoryConeRoutine), pas le sweep point-à-point des autres cas.")]
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Direction, TargetType.Target,
        TargetType.AoE_Target, TargetType.LineTarget, TargetType.Skillshot, TargetType.Cone,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Trajectoire mobile")]
    public bool isTrajectory = false;
```

par :

```csharp
    [Tooltip("Transforme la résolution de ce skill en hitbox mobile qui voyage du caster vers " +
             "une destination (au lieu de résoudre les dégâts au point de résolution existant, " +
             "une trajectoire est parcourue et touche tout ce qui se trouve sur son passage). " +
             "Distinct de hasDelayedImpact (zone FIXE une fois plantée) — mutuellement exclusif, " +
             "SAUF Cone/GroundTarget/Target (seule combinaison autorisée, voir tooltip " +
             "hasDelayedImpact) : la zone se plante alors au bout du trajet, EN PLUS du dégât " +
             "immédiat.\n" +
             "GroundTarget : voyage vers le point cliqué au sol (visé souris).\n" +
             "Target / AoE_Target : voyage vers la position de la cible AU LANCEMENT (figée, pas " +
             "de homing — si la cible bouge après coup, la trajectoire continue vers le point où " +
             "elle était, peut la manquer). Perce tout sur le trajet, sauf si stopAtFirstHit.\n" +
             "Cone : PAS un point qui voyage — un cône qui s'ÉLARGIT depuis le caster (portée 0 → " +
             "range à la vitesse projectileSpeed), angle = coneHalfAngle, visé à la souris. Chemin " +
             "de résolution séparé (TrajectoryConeRoutine), pas le sweep point-à-point des autres " +
             "cas.")]
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Target,
        TargetType.AoE_Target, TargetType.Cone,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Trajectoire mobile")]
    public bool isTrajectory = false;
```

- [ ] **Step 6: Ajouter `stopAtFirstHit` juste après `isTrajectory`**

Insérer ce nouveau champ immédiatement après le bloc `isTrajectory` de l'étape précédente
(avant le bloc `trajectoryShape`) :

```csharp
    [Tooltip("Si coché, le sweep s'arrête au PREMIER ennemi touché (comme l'ancien Skillshot —\n" +
             "tir précis). Si décoché (défaut), perce tout ce qui est sur le trajet (comme " +
             "l'ancien Direction/LineTarget). Cone non concerné — touche toujours tout l'éventail.\n" +
             "Si combiné à hasDelayedImpact, la zone différée se plante au POINT D'ARRÊT réel, pas " +
             "à la destination d'origine.")]
    [ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget,
        AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Arrêt au premier hit")]
    public bool stopAtFirstHit = false;
```

- [ ] **Step 7: Nettoyer le `[ShowIf]` de `trajectoryShape`**

Remplacer :

```csharp
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Direction, TargetType.Target,
        TargetType.AoE_Target, TargetType.LineTarget, TargetType.Skillshot,
        AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Forme de trajectoire")]
    public TrajectoryShape trajectoryShape = TrajectoryShape.Sphere;
```

par :

```csharp
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Target, TargetType.AoE_Target,
        AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Forme de trajectoire")]
    public TrajectoryShape trajectoryShape = TrajectoryShape.Sphere;
```

(`trajectoryWidth`/`trajectoryDepth`, juste en dessous, ne référencent que `trajectoryShape` —
rien à changer sur ces deux champs.)

- [ ] **Step 8: Ajouter `zoneFollowsAnchor` juste après `trajectoryDepth`, avant le header `⑧`**

```csharp
    [Tooltip("Si coché, la zone RECALCULE sa position à chaque tick sur l'entité vivante " +
             "(Self/AoE_Self : le CASTER — ex: tourbillon qui te suit si tu te déplaces en " +
             "spinnant. Target/AoE_Target : la CIBLE — ex: corbeaux qui suivent une cible " +
             "marquée). Si décoché (défaut), la zone reste figée à la position capturée au " +
             "lancement — ex: fiole de poison lancée au sol, zone qui punit une cible qui " +
             "s'enfuit. GroundTarget : toujours figé, ce champ n'apparaît pas (pas d'entité à " +
             "suivre).")]
    [ShowIf(nameof(targetType), TargetType.Self, TargetType.AoE_Self, TargetType.Target,
        TargetType.AoE_Target, AndField = nameof(hasDelayedImpact), AndValue = true,
        Header = "⑦ Zone qui suit")]
    public bool zoneFollowsAnchor = false;
```

- [ ] **Step 9: Réécrire les 4 checks `OnValidate()` qui référencent Direction/Skillshot/LineTarget**

Remplacer le bloc `hasDelayedImpactSupportedTargetType` :

```csharp
        bool hasDelayedImpactSupportedTargetType =
            targetType == TargetType.Target || targetType == TargetType.GroundTarget ||
            targetType == TargetType.AoE_Target || targetType == TargetType.Cone ||
            targetType == TargetType.Direction;
        if (hasDelayedImpact && !hasDelayedImpactSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La zone se plantera quand même, centrée sur le " +
                              "caster. Décoche hasDelayedImpact ou remets targetType sur Target/" +
                              "GroundTarget/AoE_Target/Cone/Direction.", this);
```

par :

```csharp
        bool hasDelayedImpactSupportedTargetType =
            targetType == TargetType.Self || targetType == TargetType.AoE_Self ||
            targetType == TargetType.Target || targetType == TargetType.GroundTarget ||
            targetType == TargetType.AoE_Target || targetType == TargetType.Cone;
        if (hasDelayedImpact && !hasDelayedImpactSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La zone se plantera quand même, centrée sur le " +
                              "caster. Décoche hasDelayedImpact ou remets targetType sur Self/" +
                              "AoE_Self/Target/GroundTarget/AoE_Target/Cone.", this);
```

Remplacer le bloc `trajectoryPlusZoneSupportedTargetType` :

```csharp
        bool trajectoryPlusZoneSupportedTargetType =
            targetType == TargetType.Cone || targetType == TargetType.Direction ||
            targetType == TargetType.GroundTarget;
        if (isTrajectory && hasDelayedImpact && !trajectoryPlusZoneSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true ET hasDelayedImpact = " +
                              "true simultanément avec targetType = " + targetType + " — combinaison " +
                              "non supportée pour ce targetType (seuls Cone/Direction/GroundTarget " +
                              "l'autorisent). Décoche l'un des deux.", this);
```

par :

```csharp
        bool trajectoryPlusZoneSupportedTargetType =
            targetType == TargetType.Cone || targetType == TargetType.GroundTarget ||
            targetType == TargetType.Target;
        if (isTrajectory && hasDelayedImpact && !trajectoryPlusZoneSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true ET hasDelayedImpact = " +
                              "true simultanément avec targetType = " + targetType + " — combinaison " +
                              "non supportée pour ce targetType (seuls Cone/GroundTarget/Target " +
                              "l'autorisent). Décoche l'un des deux.", this);
```

Remplacer le bloc `isTrajectorySupportedTargetType` :

```csharp
        bool isTrajectorySupportedTargetType =
            targetType == TargetType.GroundTarget || targetType == TargetType.Direction ||
            targetType == TargetType.Target       || targetType == TargetType.AoE_Target ||
            targetType == TargetType.LineTarget   || targetType == TargetType.Skillshot ||
            targetType == TargetType.Cone;
        if (isTrajectory && !isTrajectorySupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La trajectoire sera quand même lancée, traitée " +
                              "comme Direction. Décoche isTrajectory ou remets targetType sur " +
                              "GroundTarget/Direction/Target/AoE_Target/LineTarget/Skillshot/Cone.", this);
```

par :

```csharp
        bool isTrajectorySupportedTargetType =
            targetType == TargetType.GroundTarget || targetType == TargetType.Target ||
            targetType == TargetType.AoE_Target   || targetType == TargetType.Cone;
        if (isTrajectory && !isTrajectorySupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La trajectoire sera quand même lancée, traitée " +
                              "comme le trajet générique (voir StartTrajectory). Décoche isTrajectory " +
                              "ou remets targetType sur GroundTarget/Target/AoE_Target/Cone.", this);
```

- [ ] **Step 10: Mettre à jour le bloc "Ordre Inspector" en tête de fichier**

Le commentaire `⑥ Ciblage & Portée` (ligne ~17) mentionne déjà `targetType, range, aoeRadius,
coneHalfAngle, aoeFaction` de façon générique — aucun changement nécessaire (ne liste pas les
valeurs de l'enum individuellement).

- [ ] **Step 11: Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "refactor: simplify TargetType enum (11->8), add stopAtFirstHit/zoneFollowsAnchor

Removes Skillshot/LineTarget/Direction (absorbed into GroundTarget/Target
+ isTrajectory/stopAtFirstHit). Extends hasDelayedImpact to Self/AoE_Self
and its isTrajectory combo to Target. New zoneFollowsAnchor toggle for
zones that follow a moving caster/target.

NOTE: this commit alone does not compile — SkillSystem.cs/TargetingSystem.cs/
SkillBar.cs still reference the removed enum values, fixed in the next
3 commits of this plan."
```

---

### Task 2: `Combat/SkillSystem.cs` — dispatch, trajectoire, zones

**Files:**
- Modify: `Combat/SkillSystem.cs`

**Interfaces:**
- Consumes: `TargetType` renuméroté + `stopAtFirstHit`/`zoneFollowsAnchor` (Task 1).
- Produces: `PlantDelayedZone(SkillData, Entity caster, Entity target)` inchangée en signature
  publique ; `DelayedZoneRoutine` gagne un paramètre `Entity anchorEntity` (privée, seul ce
  fichier l'appelle). `StartTrajectory`/`TrajectoryRoutine`/`TrajectoryConeRoutine` inchangées en
  signature publique.

⚠ Ce fichier + Task 1 compilent ENSEMBLE seulement après les Tâches 3-4 (TargetingSystem.cs/
SkillBar.cs référencent aussi les valeurs supprimées).

- [ ] **Step 1: Mettre à jour le bloc de commentaire en tête de fichier**

Remplacer (lignes ~16-52) :

```csharp
// Target nullable par design :
//   Self / AoE_Self / GroundTarget / Direction / Skillshot / Cone → target null autorisée
//   Target / AoE_Target / Dash_Target / LineTarget                 → target requise
//
// Points d'entrée :
//   Execute(skill, caster, target)
//     → Enregistrements Player (UseSkill, SkillUsedEvent)
//     → RegisterLastSkill sur Mob cible
//     → Dispatch selon executionType (MultiHit / Standard)
//     → Dispatch selon skill.targetType
//     → ApplyEffectType → CombatSystem (calcul pur)
//     → GameEventBus.Publish(DamageDealtEvent) [Player uniquement]
//
// TargetType gérés :
//   Target        → monocible sur Entity requise
//   AoE_Target    → zone autour de la cible
//   Dash_Target   → dash vers la cible puis dégâts
//   LineTarget    → ligne entre caster et cible, hits tout sur le trajet
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint)
//   Direction     → SphereCast dans une direction (SetSkillDirection)
//   Skillshot     → projectile en ligne droite (SphereCast, no target required)
//   Cone          → cône en éventail devant le caster (SetSkillDirection ou forward)
//   Dash_Direction → dash vers un point directionnel (SetSkillDirection ou forward)
//
// MultiHit (GDD §7.1) :
//   Hit initial = skill parent, puis chaque HitStep en coroutine.
//   Proxy SkillData temporaire — détruit après usage.
//
// GroundTarget :
//   SetGroundTargetPoint(point) appelé par TargetingSystem avant Execute().
//   Remis à null après utilisation.
//
// Direction / Skillshot / Cone / Dash_Direction :
//   SetSkillDirection(dir) appelé par TargetingSystem avant Execute()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================
```

par :

```csharp
// Target nullable par design :
//   Self / AoE_Self / GroundTarget / Cone → target null autorisée
//   Target / AoE_Target / Dash_Target      → target requise
//
// Points d'entrée :
//   Execute(skill, caster, target)
//     → Enregistrements Player (UseSkill, SkillUsedEvent)
//     → RegisterLastSkill sur Mob cible
//     → Dispatch selon executionType (MultiHit / Standard)
//     → Dispatch selon skill.targetType
//     → ApplyEffectType → CombatSystem (calcul pur)
//     → GameEventBus.Publish(DamageDealtEvent) [Player uniquement]
//
// TargetType gérés :
//   Target        → monocible sur Entity requise. + isTrajectory : perce jusqu'à la cible
//                   (stopAtFirstHit pour s'arrêter au 1er hit).
//   AoE_Target    → zone autour de la cible
//   Dash_Target   → dash vers la cible puis dégâts
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint), visé souris.
//                   + isTrajectory : voyage vers ce point (stopAtFirstHit possible).
//   Cone          → cône en éventail devant le caster, visé souris (SetSkillDirection via
//                   TargetingSystem.ResolveDirection())
//   Dash_Direction → dash vers un point directionnel (SetSkillDirection ou forward)
//
// MultiHit (GDD §7.1) :
//   Hit initial = skill parent, puis chaque HitStep en coroutine.
//   Proxy SkillData temporaire — détruit après usage.
//
// GroundTarget :
//   SetGroundTargetPoint(point) appelé par TargetingSystem avant Execute().
//   Remis à null après utilisation.
//
// Cone / Dash_Direction :
//   SetSkillDirection(dir) appelé par SkillBar avant Execute()/StartTrajectory()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================
```

- [ ] **Step 2: `DispatchByTargetType()` — retirer les 3 `case` supprimés**

Remplacer (l'ordre des `case` autour de `LineTarget`/`Direction`/`Skillshot`) :

```csharp
            case TargetType.LineTarget:
                if (target != null && !target.isDead)
                    ExecuteLineTarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            // ── Pas de target Entity ──────────────────────────
            case TargetType.Self:
                ExecuteOnSelf(skill, caster);
                break;

            case TargetType.AoE_Self:
                ExecuteAoESelf(skill, caster);
                break;

            case TargetType.GroundTarget:
                ExecuteGroundTarget(skill, caster);
                break;

            case TargetType.Direction:
                ExecuteDirection(skill, caster);
                break;

            case TargetType.Skillshot:
                ExecuteSkillshot(skill, caster);
                break;

            case TargetType.Cone:
                ExecuteCone(skill, caster);
                break;
```

par :

```csharp
            // ── Pas de target Entity ──────────────────────────
            case TargetType.Self:
                ExecuteOnSelf(skill, caster);
                break;

            case TargetType.AoE_Self:
                ExecuteAoESelf(skill, caster);
                break;

            case TargetType.GroundTarget:
                ExecuteGroundTarget(skill, caster);
                break;

            case TargetType.Cone:
                ExecuteCone(skill, caster);
                break;
```

- [ ] **Step 3: Supprimer `ExecuteDirection()`, `ExecuteSkillshot()`, `ExecuteLineTarget()`**

Supprimer les 3 méthodes entières (avec leur bloc de commentaire `// ====` associé) :
`ExecuteDirection` (bloc `// DIRECTION — ...` jusqu'à l'accolade fermante), `ExecuteSkillshot`
(bloc `// SKILLSHOT — ...`), `ExecuteLineTarget` (bloc `// LINE TARGET — ...`). Ce sont les 3
méthodes lues en Step 1 de l'exploration de ce plan — repérables par leur signature
`private void ExecuteDirection(SkillData skill, Entity caster)`,
`private void ExecuteSkillshot(SkillData skill, Entity caster)`,
`private void ExecuteLineTarget(SkillData skill, Entity caster, Entity target)`. Le commentaire
`// CONE — ...` juste après `ExecuteLineTarget` reste, inchangé (Cone n'est pas supprimé).

- [ ] **Step 4: `StartTrajectory()` — simplifier la branche Target/AoE_Target (retirer LineTarget)**

Remplacer :

```csharp
        else if (skill.targetType == TargetType.Target || skill.targetType == TargetType.AoE_Target ||
                 skill.targetType == TargetType.LineTarget)
        {
            // Position figée AU LANCEMENT, pas de homing — si target meurt/sort de portée avant
            // que StartTrajectory() soit appelée (délai d'anim), on vise quand même son dernier
            // point connu plutôt que d'annuler silencieusement le skill (même philosophie que le
            // fallback origin==destination plus bas, qui gère déjà le cas target == null).
            // AoE_Target/LineTarget : TrajectoryRoutine balaie déjà TOUT ce qui est sur le trajet
            // (pas juste la cible la plus proche) — même résultat qu'ExecuteLineTarget/
            // ExecuteAoE_Target côté instantané, juste étalé dans le temps.
            destination = target != null ? target.transform.position : origin;
        }
        else // TargetType.Direction / Skillshot (même géométrie que la résolution instantanée de
             // Skillshot — ligne droite depuis le caster, voir ExecuteSkillshot) ou targetType
             // incompatible — voir OnValidate, traité comme Direction par défaut plutôt que planter
        {
```

par :

```csharp
        else if (skill.targetType == TargetType.Target || skill.targetType == TargetType.AoE_Target)
        {
            // Position figée AU LANCEMENT, pas de homing — si target meurt/sort de portée avant
            // que StartTrajectory() soit appelée (délai d'anim), on vise quand même son dernier
            // point connu plutôt que d'annuler silencieusement le skill (même philosophie que le
            // fallback origin==destination plus bas, qui gère déjà le cas target == null).
            // AoE_Target : TrajectoryRoutine balaie déjà TOUT ce qui est sur le trajet (pas juste
            // la cible la plus proche) — même résultat qu'ExecuteAoETarget côté instantané, juste
            // étalé dans le temps.
            destination = target != null ? target.transform.position : origin;
        }
        else // Fallback générique — targetType théoriquement incompatible avec isTrajectory (voir
             // OnValidate) : GroundTarget/Target/AoE_Target/Cone ont chacun leur propre branche
             // ci-dessus, donc en usage normal AUCUN targetType valide n'atteint cette branche.
             // Gardée comme filet de sécurité (facing du caster + range) pour ne jamais laisser un
             // skill qui a déjà coûté mana/CD se solder par un no-op silencieux — voir spec §6b.
        {
```

- [ ] **Step 5: Vérifier que le corps de la branche fallback (calcul `forwardHalfExtent`) est
      inchangé**

Le corps de cette branche (calcul `dir`/`range`/`forwardHalfExtent`/`effectiveRange`/
`destination`, déjà présent) **ne change pas** — seul le commentaire d'introduction ci-dessus a
changé. Ne pas toucher au code entre les accolades.

- [ ] **Step 6: `TrajectoryRoutine()` — ajouter l'arrêt anticipé `stopAtFirstHit`**

Dans la pass initiale à l'origine, remplacer :

```csharp
        Collider[] initialHits = skill.trajectoryShape == TrajectoryShape.Box
            ? Physics.OverlapBox(origin, halfExtents, Quaternion.LookRotation(dir))
            : Physics.OverlapSphere(origin, radius);
        foreach (Collider col in initialHits)
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

        Vector3 previousPos  = origin;
        float   traveled     = 0f;
        bool    casterDied   = false;
```

par :

```csharp
        Collider[] initialHits = skill.trajectoryShape == TrajectoryShape.Box
            ? Physics.OverlapBox(origin, halfExtents, Quaternion.LookRotation(dir))
            : Physics.OverlapSphere(origin, radius);
        Vector3 stopPoint = origin; // point d'arrêt réel si stopAtFirstHit coupe court — mis à
                                     // jour à chaque hit, lu après la boucle pour la combo zone.
        bool stoppedEarly = false;
        foreach (Collider col in initialHits)
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

            // stopAtFirstHit : le trajet s'arrête ICI, dès la pass initiale — une entité déjà
            // collée au caster au lancement compte comme le "premier hit".
            if (skill.stopAtFirstHit) { stopPoint = origin; stoppedEarly = true; break; }
        }

        Vector3 previousPos  = origin;
        float   traveled     = 0f;
        bool    casterDied   = false;
```

⚠ **Bug trouvé en auto-relecture de ce plan** : si `stopAtFirstHit` se déclenche dès la pass
initiale (entité déjà collée au caster au lancement), la boucle `while` juste en dessous
s'exécuterait quand même une 1ère fois avant de s'arrêter (son ancienne condition ne connaît pas
`stoppedEarly`) — un 2e sweep aurait lieu alors que le trajet est censé être déjà terminé.
Corriger la ligne juste après le bloc ci-dessus (inchangée par les deux remplacements
précédents, donc facile à manquer) :

Remplacer :

```csharp
        while (traveled < totalDistance)
```

par :

```csharp
        while (traveled < totalDistance && !stoppedEarly)
```

Dans la boucle par frame, remplacer :

```csharp
            if (segment > 0.0001f)
            {
                Vector3 segmentDir = (currentPos - previousPos).normalized;
                RaycastHit[] hits = skill.trajectoryShape == TrajectoryShape.Box
                    ? Physics.BoxCastAll(previousPos, halfExtents, segmentDir, Quaternion.LookRotation(segmentDir), segment)
                    : Physics.SphereCastAll(previousPos, radius, segmentDir, segment);
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
                    // pas une zone unique comme DelayedZoneRoutine qui joue un seul vfx/son par
                    // tick peu importe combien d'entités touchées).
                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
                }

                if (trajectoryVfx != null)
                {
                    trajectoryVfx.transform.position = currentPos;
                    trajectoryVfx.transform.rotation = Quaternion.LookRotation(segmentDir);
                }
            }

            previousPos = currentPos;
            yield return null;
        }
```

par :

```csharp
            if (segment > 0.0001f)
            {
                Vector3 segmentDir = (currentPos - previousPos).normalized;
                RaycastHit[] hits = skill.trajectoryShape == TrajectoryShape.Box
                    ? Physics.BoxCastAll(previousPos, halfExtents, segmentDir, Quaternion.LookRotation(segmentDir), segment)
                    : Physics.SphereCastAll(previousPos, radius, segmentDir, segment);
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
                    // pas une zone unique comme DelayedZoneRoutine qui joue un seul vfx/son par
                    // tick peu importe combien d'entités touchées).
                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);

                    // stopAtFirstHit : le trajet s'arrête AU POINT DE CE HIT, pas à `destination`
                    // — une flèche qui perce le premier ennemi ne continue pas au-delà.
                    if (skill.stopAtFirstHit) { stopPoint = h.point; stoppedEarly = true; break; }
                }

                if (trajectoryVfx != null)
                {
                    trajectoryVfx.transform.position = stoppedEarly ? stopPoint : currentPos;
                    trajectoryVfx.transform.rotation = Quaternion.LookRotation(segmentDir);
                }
            }

            previousPos = currentPos;
            if (stoppedEarly) break;
            yield return null;
        }
```

- [ ] **Step 7: `TrajectoryRoutine()` — étendre `plantsZoneAtEnd` à `Target`, utiliser `stopPoint`**

Remplacer :

```csharp
        // hasDelayedImpact + isTrajectory combinés — UNIQUEMENT Direction/GroundTarget (voir
        // OnValidate, exception délibérée à leur exclusion mutuelle habituelle). Le sweep a déjà
        // infligé ses dégâts immédiats aux entités traversées ; en plus, une zone classique se
        // plante au bout de la trajectoire (aoeRadius côté Sphere — jamais lu par le sweep
        // lui-même, qui a son propre radius local) et détone après impactDelay — double dégât
        // voulu si une cible reste dans le rayon final (demande explicite Florian — ex: mur de
        // feu qui voyage vers le point cliqué (GroundTarget) ET laisse une zone brûlante à
        // l'arrivée).
        bool plantsZoneAtEnd = !casterDied && skill.hasDelayedImpact &&
            (skill.targetType == TargetType.Direction || skill.targetType == TargetType.GroundTarget);

        // Whiff total (rien touché EN CHEMIN) — pas de feedback immédiat si une zone va de toute
        // façon se planter au bout (son propre marker/détonation suffit, un vfxImpact immédiat en
        // plus serait un doublon confus). Même fallback que le cas origine==destination ci-dessus
        // sinon : le skill a coûté mana/HP/or + cooldown, il doit produire un feedback même sur un
        // whiff total.
        if (!casterDied && alreadyHit.Count == 0 && !plantsZoneAtEnd)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
        }

        // Nettoyage INCONDITIONNEL — contrairement au fallback vfxImpact/soundEffect ci-dessus
        // (qui ne joue pas si casterDied), le VFX de trajet doit TOUJOURS disparaître, mort du
        // caster comprise, sinon il resterait affiché indéfiniment sur une trajectoire abandonnée.
        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);

        if (plantsZoneAtEnd)
        {
            GameObject marker = skill.vfxZoneMarker != null
                ? Instantiate(skill.vfxZoneMarker, destination, Quaternion.identity)
                : null;
            // Nested sur CE host (pas un nouveau) — destroySelfOnFinish: false ici, le Destroy du
            // host reste géré une seule fois, juste en dessous, une fois cette attente terminée.
            yield return DelayedZoneRoutine(skill, caster, destination, marker, destroySelfOnFinish: false);
        }
```

par :

```csharp
        // hasDelayedImpact + isTrajectory combinés — UNIQUEMENT Target/GroundTarget/Cone (voir
        // OnValidate, exception délibérée à leur exclusion mutuelle habituelle — Cone est géré
        // dans TrajectoryConeRoutine, pas ici). Le sweep a déjà infligé ses dégâts immédiats aux
        // entités traversées ; en plus, une zone classique se plante là où le trajet s'est
        // terminé (aoeRadius côté Sphere — jamais lu par le sweep lui-même, qui a son propre
        // radius local) et détone après impactDelay — double dégât voulu si une cible reste dans
        // le rayon final (demande explicite Florian — ex: mur de feu qui voyage vers le point
        // cliqué (GroundTarget) ET laisse une zone brûlante à l'arrivée ; lance qui transperce
        // jusqu'à la cible (Target) ET explose à l'arrivée).
        bool plantsZoneAtEnd = !casterDied && skill.hasDelayedImpact &&
            (skill.targetType == TargetType.GroundTarget || skill.targetType == TargetType.Target);

        // Point où la zone se plante — le point d'arrêt réel si stopAtFirstHit a coupé court,
        // sinon la destination d'origine (trajet allé jusqu'au bout).
        Vector3 zonePlantPoint = stoppedEarly ? stopPoint : destination;

        // Whiff total (rien touché EN CHEMIN) — pas de feedback immédiat si une zone va de toute
        // façon se planter au bout (son propre marker/détonation suffit, un vfxImpact immédiat en
        // plus serait un doublon confus). Même fallback que le cas origine==destination ci-dessus
        // sinon : le skill a coûté mana/HP/or + cooldown, il doit produire un feedback même sur un
        // whiff total.
        if (!casterDied && alreadyHit.Count == 0 && !plantsZoneAtEnd)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
        }

        // Nettoyage INCONDITIONNEL — contrairement au fallback vfxImpact/soundEffect ci-dessus
        // (qui ne joue pas si casterDied), le VFX de trajet doit TOUJOURS disparaître, mort du
        // caster comprise, sinon il resterait affiché indéfiniment sur une trajectoire abandonnée.
        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);

        if (plantsZoneAtEnd)
        {
            GameObject marker = skill.vfxZoneMarker != null
                ? Instantiate(skill.vfxZoneMarker, zonePlantPoint, Quaternion.identity)
                : null;
            // Nested sur CE host (pas un nouveau) — destroySelfOnFinish: false ici, le Destroy du
            // host reste géré une seule fois, juste en dessous, une fois cette attente terminée.
            // anchorEntity: null — le point d'arrivée/d'arrêt d'une trajectoire est déjà un point
            // fixe résolu, "suivre" une entité n'a pas de sens ici (voir zoneFollowsAnchor).
            yield return DelayedZoneRoutine(skill, caster, zonePlantPoint, null, marker, destroySelfOnFinish: false);
        }
```

- [ ] **Step 8: `TrajectoryConeRoutine()` — ajouter `anchorEntity: null` à son appel nested**

Remplacer :

```csharp
            yield return DelayedZoneRoutine(skill, caster, tipPoint, marker, destroySelfOnFinish: false);
```

par (même raison que Step 7 — Cone ne suit jamais rien, point fixe) :

```csharp
            yield return DelayedZoneRoutine(skill, caster, tipPoint, null, marker, destroySelfOnFinish: false);
```

- [ ] **Step 9: `PlantDelayedZone()` — résoudre `anchorEntity` et le passer à la coroutine**

Remplacer :

```csharp
        Vector3 position;
        if (skill.targetType == TargetType.GroundTarget)
        {
            // Même consommation que ExecuteGroundTarget() — sans ce reset, un skill sans
            // rapport lancé plus tard (ex: TeleportSelf) hériterait de cette position périmée.
            position = _groundTargetPoint ?? caster.transform.position;
            _groundTargetPoint = null;
        }
        else
        {
            position = target != null ? target.transform.position : caster.transform.position;
        }

        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        // Mob/PNJ : Destroy(gameObject, délai de corpse) sur le caster tuerait cette coroutine
        // avant qu'elle atteigne son propre nettoyage si impactDelay+zoneDuration dépasse ce
        // délai — délègue à un host temporaire qui survit indépendamment du caster, détruit
        // lui-même en fin de routine. Le chemin Player (jamais détruit) reste inchangé, tourne
        // sur `this` comme aujourd'hui.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"DelayedZoneHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.DelayedZoneRoutine(skill, caster, position, marker, destroySelfOnFinish));
    }
```

par :

```csharp
        Vector3 position;
        if (skill.targetType == TargetType.GroundTarget)
        {
            // Même consommation que ExecuteGroundTarget() — sans ce reset, un skill sans
            // rapport lancé plus tard (ex: TeleportSelf) hériterait de cette position périmée.
            position = _groundTargetPoint ?? caster.transform.position;
            _groundTargetPoint = null;
        }
        else
        {
            position = target != null ? target.transform.position : caster.transform.position;
        }

        // Entité à suivre si skill.zoneFollowsAnchor est coché (§5 de la spec) — le CASTER pour
        // Self/AoE_Self (le champ n'est même pas visible dans l'Inspector pour les autres
        // targetType, voir SkillData.zoneFollowsAnchor), la CIBLE pour Target/AoE_Target, jamais
        // pour GroundTarget (pas d'entité à suivre, un point au sol reste toujours figé).
        Entity anchorEntity = null;
        if (skill.zoneFollowsAnchor)
        {
            if (skill.targetType == TargetType.Self || skill.targetType == TargetType.AoE_Self)
                anchorEntity = caster;
            else if (skill.targetType == TargetType.Target || skill.targetType == TargetType.AoE_Target)
                anchorEntity = target;
        }

        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        // Si la zone doit suivre une entité, parente le marker à son transform — Unity gère le
        // suivi (position ET rotation) automatiquement, cohérent pour un VFX de tourbillon qui
        // doit tourner avec le joueur. Sinon (défaut), le marker reste en world space, fixe,
        // comportement inchangé.
        if (marker != null && anchorEntity != null)
            marker.transform.SetParent(anchorEntity.transform);

        // Mob/PNJ : Destroy(gameObject, délai de corpse) sur le caster tuerait cette coroutine
        // avant qu'elle atteigne son propre nettoyage si impactDelay+zoneDuration dépasse ce
        // délai — délègue à un host temporaire qui survit indépendamment du caster, détruit
        // lui-même en fin de routine. Le chemin Player (jamais détruit) reste inchangé, tourne
        // sur `this` comme aujourd'hui.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"DelayedZoneHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.DelayedZoneRoutine(skill, caster, position, anchorEntity, marker, destroySelfOnFinish));
    }
```

- [ ] **Step 10: `DelayedZoneRoutine()` — nouveau paramètre `anchorEntity`, position dynamique**

Remplacer la signature et le corps de la boucle :

```csharp
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker, bool destroySelfOnFinish)
    {
        yield return new WaitForSeconds(skill.impactDelay);

        // Clamp défensif — un zoneTickInterval <= 0 (mauvaise saisie, ou valeur posée par script/
        // API en contournant le [Min] de l'Inspector) ferait tourner cette boucle indéfiniment,
        // un OverlapSphere + dégâts + VFX à CHAQUE FRAME jusqu'à la mort du caster. 0.05s = 20
        // ticks/seconde max, largement suffisant pour tout usage gameplay réel.
        float tickInterval = Mathf.Max(0.05f, skill.zoneTickInterval);

        float remaining = skill.zoneDuration;
        while (true)
        {
            // Même garde que les autres coroutines longues du fichier (DashToTarget/
            // DashInDirection) — le caster peut mourir entre le plantage et la détonation.
            // `break` (pas `yield break`) pour que le nettoyage du marker en fin de méthode
            // s'exécute quand même.
            if (caster == null || caster.isDead) break;

            Collider[] hits = Physics.OverlapSphere(position, skill.aoeRadius);
            foreach (Collider col in hits)
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity.isDead) continue;
                if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);
            }

            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, position, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, position);

            if (remaining <= 0f) break;

            yield return new WaitForSeconds(tickInterval);
            remaining -= tickInterval;
        }

        if (marker != null) Destroy(marker);
        if (destroySelfOnFinish) Destroy(gameObject);
    }
```

par :

```csharp
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, Entity anchorEntity, GameObject marker, bool destroySelfOnFinish)
    {
        yield return new WaitForSeconds(skill.impactDelay);

        // Clamp défensif — un zoneTickInterval <= 0 (mauvaise saisie, ou valeur posée par script/
        // API en contournant le [Min] de l'Inspector) ferait tourner cette boucle indéfiniment,
        // un OverlapSphere + dégâts + VFX à CHAQUE FRAME jusqu'à la mort du caster. 0.05s = 20
        // ticks/seconde max, largement suffisant pour tout usage gameplay réel.
        float tickInterval = Mathf.Max(0.05f, skill.zoneTickInterval);

        float remaining = skill.zoneDuration;
        while (true)
        {
            // Même garde que les autres coroutines longues du fichier (DashToTarget/
            // DashInDirection) — le caster peut mourir entre le plantage et la détonation.
            // `break` (pas `yield break`) pour que le nettoyage du marker en fin de méthode
            // s'exécute quand même.
            if (caster == null || caster.isDead) break;

            // zoneFollowsAnchor : recalcule la position sur l'entité suivie SI elle est encore
            // vivante — sinon retombe sur `position` (figée), évite un NullReferenceException/
            // téléportation en (0,0,0) si l'entité suivie meurt entre deux ticks. `position`
            // elle-même n'est JAMAIS réassignée (reste le point d'origine pour ce fallback).
            Vector3 tickPosition = (anchorEntity != null && !anchorEntity.isDead)
                ? anchorEntity.transform.position
                : position;

            Collider[] hits = Physics.OverlapSphere(tickPosition, skill.aoeRadius);
            foreach (Collider col in hits)
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity.isDead) continue;
                if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);
            }

            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, tickPosition, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, tickPosition);

            if (remaining <= 0f) break;

            yield return new WaitForSeconds(tickInterval);
            remaining -= tickInterval;
        }

        if (marker != null) Destroy(marker);
        if (destroySelfOnFinish) Destroy(gameObject);
    }
```

(Le marker suit déjà tout seul via le parentage fait dans `PlantDelayedZone` — Step 9 — pas
besoin de le repositionner ici.)

- [ ] **Step 11: Mettre à jour le XML doc-comment de `TrajectoryRoutine`**

Remplacer la phrase finale du `<summary>` :

```csharp
    /// coroutine. Si `skill.hasDelayedImpact` ET `targetType = Direction` (seule combinaison
    /// autorisée avec isTrajectory, voir SkillData.isTrajectory), plante EN PLUS une zone
    /// classique (DelayedZoneRoutine) à `destination` une fois le sweep terminé — double dégât
    /// voulu, pas une alternative au sweep.</summary>
```

par :

```csharp
    /// coroutine. Si `skill.stopAtFirstHit`, le sweep s'arrête au premier hit (initial pass OU
    /// boucle par frame) au lieu d'aller jusqu'à `destination`. Si `skill.hasDelayedImpact` ET
    /// `targetType = Target/GroundTarget` (seule combinaison autorisée avec isTrajectory pour ces
    /// deux types, voir SkillData.isTrajectory), plante EN PLUS une zone classique
    /// (DelayedZoneRoutine) au point d'arrêt réel (respecte stopAtFirstHit) une fois le sweep
    /// terminé — double dégât voulu, pas une alternative au sweep.</summary>
```

- [ ] **Step 12: Compiler**

Ne compile PAS encore (Tâches 3-4 restantes) — c'est attendu. Vérifier juste qu'aucune ERREUR
n'apparaît en dehors de celles attribuables à `TargetingSystem.cs`/`SkillBar.cs` (les seuls
fichiers qui référencent encore `Direction`/`Skillshot`/`LineTarget` à ce stade).

- [ ] **Step 13: Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "refactor: SkillSystem — stopAtFirstHit, zoneFollowsAnchor, drop dead targetTypes

TrajectoryRoutine stops early on stopAtFirstHit (zone-combo plants at the
real stop point, not the original destination). DelayedZoneRoutine/
PlantDelayedZone gain an anchorEntity param for zones that follow a
moving caster/target. ExecuteDirection/ExecuteSkillshot/ExecuteLineTarget
removed along with their DispatchByTargetType cases.

Still doesn't compile alone — TargetingSystem.cs/SkillBar.cs fixed next."
```

---

### Task 3: `Combat/TargetingSystem.cs` — `ResolveDirection()` publique, nettoyage code mort

**Files:**
- Modify: `Combat/TargetingSystem.cs`

**Interfaces:**
- Consumes: `TargetType` renuméroté (Task 1).
- Produces: `public Vector3 ResolveDirection()` — méthode publique consommée par la Task 4
  (`SkillBar.cs`, appel `TargetingSystem.Instance.ResolveDirection()`).

- [ ] **Step 1: Rendre `ResolveDirection()` publique**

Remplacer :

```csharp
    private Vector3 ResolveDirection()
```

par :

```csharp
    /// <summary>Résout la direction de visée pour un skill directionnel (Cone) — priorité à la
    /// cible engagée/sélectionnée (direction caster→cible), sinon raycast souris (aplati sur XZ),
    /// sinon le facing du caster en dernier recours. Publique — appelée par SkillBar.cs juste
    /// avant StartTrajectory()/Execute() pour tout skill targetType == Cone.</summary>
    public Vector3 ResolveDirection()
```

- [ ] **Step 2: Retirer les 3 `case` morts de `TryExecuteSkill()`**

Cette méthode reste du code mort (toujours aucun appelant après ce chantier — hors scope de la
supprimer, voir spec §3) mais doit continuer à COMPILER. Remplacer :

```csharp
            case TargetType.Direction:
            case TargetType.Skillshot:
            case TargetType.Cone:
            case TargetType.Dash_Direction:
            {
                SkillSystem.Instance.SetSkillDirection(ResolveDirection());
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            case TargetType.Target:
            case TargetType.AoE_Target:
            case TargetType.Dash_Target:
            case TargetType.LineTarget:
            {
```

par :

```csharp
            case TargetType.Cone:
            case TargetType.Dash_Direction:
            {
                SkillSystem.Instance.SetSkillDirection(ResolveDirection());
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            case TargetType.Target:
            case TargetType.AoE_Target:
            case TargetType.Dash_Target:
            {
```

- [ ] **Step 3: Compiler**

Ne compile PAS encore seul (`SkillBar.cs`, Tâche 4, référence aussi les valeurs supprimées) —
attendu.

- [ ] **Step 4: Commit**

```bash
git add Combat/TargetingSystem.cs
git commit -m "refactor: TargetingSystem — ResolveDirection() public, drop dead enum cases

Publicizes ResolveDirection() so SkillBar.cs can wire it to Cone's real
cast flow (next commit). TryExecuteSkill() stays dead code (no live
caller) but must keep compiling — dropped its Direction/Skillshot/
LineTarget switch cases."
```

---

### Task 4: `Data/Skills/SkillBar.cs` — visée souris Cone + nettoyage

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `TargetingSystem.ResolveDirection()` publique (Task 3), `TargetType` renuméroté
  (Task 1).
- Produces: rien de nouveau consommé par une tâche suivante — dernier fichier de code du
  chantier, **doit compiler proprement après cette tâche** (avec Tasks 1-3).

- [ ] **Step 1: Mettre à jour le commentaire en tête de fichier**

Remplacer :

```csharp
// Calls SkillSystem.Execute(skill, caster, target) — target peut être null
//   pour Self, AoE_Self, GroundTarget, Direction, Skillshot, LineTarget, Cone.
```

par :

```csharp
// Calls SkillSystem.Execute(skill, caster, target) — target peut être null
//   pour Self, AoE_Self, GroundTarget, Cone.
```

- [ ] **Step 2: `needsTarget` — retirer `LineTarget`**

Remplacer :

```csharp
        bool needsTarget = skill.targetType == TargetType.Target
                        || skill.targetType == TargetType.AoE_Target
                        || skill.targetType == TargetType.Dash_Target
                        || skill.targetType == TargetType.LineTarget;
```

par :

```csharp
        bool needsTarget = skill.targetType == TargetType.Target
                        || skill.targetType == TargetType.AoE_Target
                        || skill.targetType == TargetType.Dash_Target;
```

- [ ] **Step 3: `EngageAndFaceTarget()` — retirer `Direction`/`Skillshot`, garder `Cone`**

Remplacer :

```csharp
        if (target != null
            && skill.targetType != TargetType.Self
            && skill.targetType != TargetType.AoE_Self
            && skill.targetType != TargetType.GroundTarget
            && skill.targetType != TargetType.Direction
            && skill.targetType != TargetType.Skillshot
            && skill.targetType != TargetType.Cone
            && skill.effectType != SkillEffectType.Buff
            && skill.effectType != SkillEffectType.Debuff)
```

par :

```csharp
        if (target != null
            && skill.targetType != TargetType.Self
            && skill.targetType != TargetType.AoE_Self
            && skill.targetType != TargetType.GroundTarget
            && skill.targetType != TargetType.Cone
            && skill.effectType != SkillEffectType.Buff
            && skill.effectType != SkillEffectType.Debuff)
```

Mettre aussi à jour le commentaire XML juste au-dessus qui liste ces types (recherche
`Buff/Debuff n'engagent JAMAIS le combat, même sur Target/AoE_Target/Dash_Target/`) : remplacer
`LineTarget — un buff` par `un buff` (retire juste la mention du type supprimé, le sens de la
phrase reste identique).

- [ ] **Step 4: Brancher `ResolveDirection()` pour `Cone` dans `ResolveInstant()`**

Remplacer :

```csharp
        // isTrajectory vérifié EN PREMIER — un skill Cone/Direction avec les deux flags cochés
        // (combo autorisé, voir SkillData.isTrajectory) doit passer par StartTrajectory(), qui
        // plante lui-même la zone différée en plus du dégât immédiat. Priorité inversée sans
        // risque pour tout le reste : un skill qui n'a qu'un seul des deux flags actif se
        // comporte identiquement peu importe l'ordre des checks.
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
```

par :

```csharp
        // Cone est visé à la souris (TargetingSystem.ResolveDirection() — priorité cible
        // engagée/sélectionnée, sinon raycast souris, sinon facing) — posé juste avant le
        // dispatch, consommé et remis à null par SkillSystem dès son premier usage plus bas
        // (StartTrajectory ou Execute selon isTrajectory).
        if (skill.targetType == TargetType.Cone)
            SkillSystem.Instance?.SetSkillDirection(TargetingSystem.Instance.ResolveDirection());

        // isTrajectory vérifié EN PREMIER — un skill Cone/Target/GroundTarget avec les deux flags
        // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par StartTrajectory(),
        // qui plante lui-même la zone différée en plus du dégât immédiat. Priorité inversée sans
        // risque pour tout le reste : un skill qui n'a qu'un seul des deux flags actif se
        // comporte identiquement peu importe l'ordre des checks.
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
```

- [ ] **Step 5: Brancher `ResolveDirection()` pour `Cone` dans `ResolveChannel()`**

Remplacer :

```csharp
        // Ordre inversé — voir commentaire équivalent dans SkillBar.ResolveInstant().
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);
```

par :

```csharp
        // Cone visé à la souris — voir commentaire équivalent dans SkillBar.ResolveInstant().
        if (skill.targetType == TargetType.Cone)
            SkillSystem.Instance?.SetSkillDirection(TargetingSystem.Instance.ResolveDirection());

        // Ordre inversé — voir commentaire équivalent dans SkillBar.ResolveInstant().
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);
```

- [ ] **Step 6: Compiler — DOIT être propre (0 erreur) à ce stade**

C'est le premier point du plan où le projet doit compiler sans erreur (Tasks 1-4 combinées).
Ouvrir Unity (ou `Console` si déjà ouvert) et vérifier 0 erreur de compilation.

- [ ] **Step 7: Grep de non-régression**

Depuis `Assets/Scripts` :

```bash
grep -rn "TargetType\.\(Direction\|Skillshot\|LineTarget\)" --include=*.cs .
```

Expected: **aucune sortie**. Si quelque chose remonte, c'est un spot manqué par ce plan —
corriger avant de continuer (probablement un fichier hors des 4 déjà couverts ; re-grep aussi
sans le filtre `TargetType\.` au cas où une référence existe sous une autre forme, ex. cast
`(TargetType)`).

- [ ] **Step 8: Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "feat: wire Cone to real mouse aim via TargetingSystem.ResolveDirection()

Cone skills now aim at the mouse (or locked target direction) instead of
always firing wherever the character happens to be facing. Also drops
LineTarget/Direction/Skillshot from needsTarget and EngageAndFaceTarget's
exclusion list (Cone stays excluded — still no locked Entity target).

This is the first commit in this plan where the project compiles clean."
```

---

### Task 5: Migration des 5 `.asset` existants

**Files:**
- Modify: `../Content/Skills/Test/DelayedZone_test/skl_test_delayed_cast.asset`
- Modify: `../Content/Skills/Test/DelayedZone_test/skl_test_delayed_channel.asset`
- Modify: `../Content/Skills/Test/Trajectory_test/skl_test_trajectory_storm.asset`
- Modify: `../Content/Entity/Mobs/Wolf_test/skl_loup_special_test.asset`
- Modify: `../Content/Entity/Mobs/MobTestAnim+vfx/Skills/skl_basic.asset`

(Chemins relatifs à `Assets/Scripts` — remontent dans `Assets/Content`, en dehors du repo git
qui vit dans `Assets/Scripts` ; ce sont quand même des fichiers texte simples, éditables
directement, PAS besoin d'ouvrir Unity pour cette tâche.)

**Interfaces:**
- Consumes: la nouvelle numérotation de l'enum `TargetType` (Task 1) — `Target=0, Self=1,
  AoE_Self=2, AoE_Target=3, GroundTarget=4, Cone=5, Dash_Target=6, Dash_Direction=7`.
- Produces: rien (dernière dépendance de code, aucune tâche suivante n'en a besoin).

- [ ] **Step 1: `skl_test_delayed_cast.asset` — GroundTarget 6→4**

Ouvrir le fichier, chercher la ligne `targetType: 6`, remplacer par `targetType: 4`.

- [ ] **Step 2: `skl_test_delayed_channel.asset` — GroundTarget 6→4**

Même changement : `targetType: 6` → `targetType: 4`.

- [ ] **Step 3: `skl_test_trajectory_storm.asset` — GroundTarget 6→4**

Même changement : `targetType: 6` → `targetType: 4`.

- [ ] **Step 4: `skl_loup_special_test.asset` — Dash_Target 9→6**

Chercher `targetType: 9`, remplacer par `targetType: 6`.

- [ ] **Step 5: `skl_basic.asset` — Direction 8→Target 0**

Chercher `targetType: 8`, remplacer par `targetType: 0` (décision documentée en spec §1bis —
`basicAttackSkill` d'un Mob test, `Target` est l'équivalent standard).

- [ ] **Step 6: Vérifier qu'aucun autre `.asset` n'a été oublié**

```bash
grep -rl "targetType: [4589]\|targetType: 10" --include=*.asset ../Content 2>/dev/null
```

Expected: **aucune sortie** (les 4 valeurs supprimées 4/5/8, plus l'ancienne valeur 9 qui vient
d'être migrée, plus 10 qui n'était de toute façon jamais utilisée — tout doit être vide).

- [ ] **Step 7: Commit**

```bash
git add "../Content/Skills/Test/DelayedZone_test/skl_test_delayed_cast.asset" \
        "../Content/Skills/Test/DelayedZone_test/skl_test_delayed_channel.asset" \
        "../Content/Skills/Test/Trajectory_test/skl_test_trajectory_storm.asset" \
        "../Content/Entity/Mobs/Wolf_test/skl_loup_special_test.asset" \
        "../Content/Entity/Mobs/MobTestAnim+vfx/Skills/skl_basic.asset"
git commit -m "chore: migrate 5 SkillData assets to renumbered TargetType ordinals

3x GroundTarget (6->4), 1x Dash_Target (9->6), 1x Direction->Target (8->0,
see spec §1bis for reasoning on skl_basic.asset)."
```

---

### Task 6: `docs/guide-utilisation/creation-skills.md` — mise à jour du guide

**Files:**
- Modify: `docs/guide-utilisation/creation-skills.md`

**Interfaces:**
- Consumes: la nouvelle liste de `targetType` (Task 1) et les 2 nouveaux champs
  `stopAtFirstHit`/`zoneFollowsAnchor`.
- Produces: rien (documentation seule, aucune dépendance de code).

Peut être fait en parallèle des Tasks 2-5 une fois la Task 1 connue (aucune dépendance de
compilation — c'est un fichier Markdown).

- [ ] **Step 1: Remplacer le tableau des 11 `targetType` par la nouvelle liste**

Dans la section `### ⑥ Ciblage & Portée`, repérer le tableau `| targetType | Cible verrouillée ? |
Portée / zone | Comportement | Cas d'usage typique |` (11 lignes : `Target`/`Self`/`AoE_Self`/
`AoE_Target`/`GroundTarget`/`Direction`/`Skillshot`/`LineTarget`/`Cone`/`Dash_Target`/
`Dash_Direction`). Remplacer les lignes `Direction`/`Skillshot`/`LineTarget` (et le paragraphe
"Distinctions à ne pas manquer" qui les compare, juste en dessous du tableau) par ce contenu :

```markdown
| `Target` | **Oui** | `range` = distance max au clic | Touche la cible verrouillée. + `isTrajectory` : perce tout jusqu'à elle (position figée au lancement, pas de homing) — décoche `stopAtFirstHit` pour percer, coche-le pour s'arrêter au 1er ennemi touché (ex: lance qui transperce vs flèche qui s'arrête). | Sort mono-cible, ou lance qui transperce jusqu'à une cible verrouillée. |
| `GroundTarget` | Non (point au sol, visé à la souris) | `aoeRadius`, centré sur le point cliqué | Sphère à un point choisi au sol. + `isTrajectory` : voyage vers ce point (mur/vague qui avance vers où tu cliques) — `stopAtFirstHit` disponible pareil que `Target`. | Météore, zone posée au sol, ou mur de feu qui voyage vers la souris. |
| `Cone` | Non, **visé à la souris** | `range` (portée), `coneHalfAngle` (demi-angle en degrés) | Éventail devant le caster, orienté vers la souris (priorité à la cible engagée/sélectionnée si il y en a une) — touche tout ce qui est dans l'angle ET la portée. | Souffle, attaque en arc dirigée par le joueur. |
```

Retirer aussi toute mention de `Skillshot`/`LineTarget`/`Direction` restée ailleurs dans le
fichier (`Ctrl+F` sur ces 3 mots — la section "Distinctions à ne pas manquer" et le tableau sont
les deux seuls endroits qui les citaient).

- [ ] **Step 2: Réécrire la section ⑦ (Zone à impact différé / Trajectoire mobile)**

Dans le tableau `| Champ | Rôle |` de cette section, remplacer la ligne `hasDelayedImpact` et la
ligne `isTrajectory` par :

```markdown
| `hasDelayedImpact` | Zone plantée au sol/sur une entité, dégâts différés de `impactDelay` secondes. `Self`/`AoE_Self`/`Target`/`GroundTarget`/`AoE_Target`/`Cone`. |
| `isTrajectory` | Hitbox mobile qui voyage/s'élargit et touche tout sur son passage (perce, sauf `stopAtFirstHit` coché). `GroundTarget`/`Target`/`AoE_Target`/`Cone`. |
```

Et remplacer le paragraphe "Mutuellement exclusifs, SAUF..." par :

```markdown
**Mutuellement exclusifs, SAUF `Cone`, `GroundTarget` et `Target`** — ces trois-là autorisent les
deux cochés en même temps : le dégât immédiat du sweep/de l'expansion s'applique normalement, ET
une zone classique (`aoeRadius` en Sphere) se plante en plus là où le trajet s'est terminé (le
point d'arrêt réel si `stopAtFirstHit` a coupé court, pas la destination d'origine), détonant
après `impactDelay` — **double dégât voulu** si une cible reste dans le rayon final. Pour tous
les autres `targetType`, toujours incompatibles (warning Console si les deux sont cochés
ensemble).
```

Ajouter 2 nouvelles lignes au tableau (juste après `isTrajectory`) :

```markdown
| `stopAtFirstHit` | `isTrajectory` + `Target`/`GroundTarget` uniquement. Coché = le trajet s'arrête au 1er ennemi touché (comme une flèche qui se plante). Décoché (défaut) = perce tout (comme un rayon/une lance). |
| `zoneFollowsAnchor` | `hasDelayedImpact` + `Self`/`AoE_Self`/`Target`/`AoE_Target` uniquement (jamais `GroundTarget`, pas d'entité à suivre). Coché = la zone recalcule sa position à chaque tick sur le CASTER (Self/AoE_Self) ou la CIBLE (Target/AoE_Target) — ex: tourbillon qui te suit, corbeaux qui suivent une cible marquée. Décoché (défaut) = zone figée au point de lancement — ex: fiole de poison posée au sol. |
```

- [ ] **Step 3: Commit**

```bash
git add docs/guide-utilisation/creation-skills.md
git commit -m "docs: update creation-skills.md for the 8-value TargetType + new toggles"
```

---

### Task 7: Vérification finale — grep global + Play Mode manuel (Florian)

**Files:** aucun (vérification pure, pas de modification).

**Interfaces:**
- Consumes: l'intégralité des Tasks 1-6.

- [ ] **Step 1: Grep final sur tout le repo**

```bash
cd Assets/Scripts
grep -rn "TargetType\.\(Direction\|Skillshot\|LineTarget\)" --include=*.cs .
```

Expected: **aucune sortie**.

- [ ] **Step 2: Compilation Unity propre**

Ouvrir Unity, attendre la recompilation, vérifier 0 erreur ET 0 nouveau warning inattendu dans
la Console au chargement de la scène (les warnings `OnValidate` de `SkillData` sont normaux si
un `.asset` a un champ mal configuré — mais aucun ne devrait apparaître sur les 5 assets migrés
en Task 5 si la migration est correcte).

- [ ] **Step 3-12 : Checklist Play Mode (geste Florian, pas automatisable)**

Reprendre l'intégralité de la section "Vérification" de la spec
(`docs/superpowers/specs/2026-09-19-targettype-simplification-design.md`, 10 points) :

1. `Target` + `isTrajectory` + `stopAtFirstHit` : la trajectoire s'arrête bien au 1er ennemi.
2. `Target` + `isTrajectory` + `hasDelayedImpact` (sans `stopAtFirstHit`) : perce tout le monde
   EN ROUTE vers la cible, ET plante une zone à l'arrivée sur la cible.
3. `Cone` : viser avec la souris dans une direction différente de où le perso regarde → le cône
   part bien vers la souris, pas vers le facing.
4. `AoE_Self` + `hasDelayedImpact`, `impactDelay=0`, `zoneDuration>0`, `zoneTickInterval` court,
   `zoneFollowsAnchor` coché : se déplacer pendant les ticks → la zone suit le joueur.
5. Même test avec `zoneFollowsAnchor` décoché : la zone reste plantée où le skill a été lancé.
6. `Target`/`AoE_Target` + `hasDelayedImpact` + `zoneFollowsAnchor` : la cible bouge/fuit pendant
   les ticks → la zone la suit (coché) ou reste sur place (décoché).
7. Les 5 `.asset` migrés (Task 5) : ouvrir chacun dans l'Inspector, vérifier que le `targetType`
   affiché correspond bien à l'intention d'origine (pas de `Debug.LogWarning` `OnValidate`
   inattendu).
8. `GroundTarget` + `isTrajectory` + `trajectoryShape = Box` (mur de feu large) : toujours
   fonctionnel après renumérotation.
9. `skl_test_trajectory_storm.asset` en particulier : recharger dans l'Inspector, vérifier
   qu'aucun champ ne s'affiche cassé/vide.
10. Un skill `Dash_Target`/`Dash_Direction` existant (s'il y en a un configuré) : toujours
    fonctionnel après renumérotation 9→6/10→7 — comportement inchangé, juste l'ordinal a bougé.

Ces 10 points nécessitent du geste manuel en Play Mode (viser à la souris, se déplacer pendant
un tick, observer visuellement où une zone se plante) — pas automatisable par un agent, à
réaliser par Florian.
