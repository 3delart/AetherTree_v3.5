# Trajectoire mobile (tornade ciblée) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter une mécanique de hitbox mobile ("tornade") qui voyage du caster vers un point
cible (GroundTarget) ou une direction (Direction), infligeant des dégâts à toute entité
traversée en chemin — distincte de la zone fixe du chantier C (`hasDelayedImpact`).

**Architecture:** Un nouveau flag `isTrajectory` sur `SkillData` (aucun nouveau `targetType`).
`SkillSystem` gagne `StartTrajectory()` (résolution CD/mana, calcule origine/destination) +
`TrajectoryRoutine()` (coroutine qui avance un point virtuel à `projectileSpeed` unités/sec,
`SphereCastAll` du point précédent au point courant à chaque frame, anti-double-hit via
`HashSet<Entity>`). `SkillBar` branche `isTrajectory` dans `ResolveInstant()`/`ResolveChannel()`,
juste après le branchement `hasDelayedImpact` existant — même principe "launch vs resolution"
que les chantiers A/B/C.

**Tech Stack:** Unity C#, ScriptableObject (`SkillData`), coroutines (`IEnumerator`), pas de
framework de test automatisé — vérification manuelle Play Mode uniquement.

**Spec:** `docs/superpowers/specs/2026-09-11-trajectory-tornado-design.md`

## Global Constraints

- `isTrajectory` est un flag booléen combiné aux `targetType` existants `GroundTarget`/
  `Direction` — jamais un nouveau `targetType`.
- Aucun nouveau champ numérique : vitesse = `projectileSpeed` (fallback 10 si `<= 0` — arbitraire,
  `projectileSpeed` n'a aucun fallback préexistant ailleurs), rayon hitbox = `aoeRadius`
  (fallback 0.5 si `<= 0`, même valeur que `ExecuteDirection()`), portée max mode Direction =
  `range` (fallback 10 si `<= 0`, même valeur que `ExecuteDirection()` — `ExecuteSkillshot()`
  utilise des fallbacks différents, 15/0.25, non pertinents ici).
- `isTrajectory` et `hasDelayedImpact` (chantier C) sont mutuellement exclusifs — averti par
  `OnValidate()`, jamais bloqué en dur (idiome du fichier : warnings à la config, pas de garde
  runtime redondante).
- `StartTrajectory(SkillData skill, Entity caster)` ne prend PAS de paramètre `target` —
  `GroundTarget`/`Direction` ne prennent jamais de cible Entity.
- `_skillDirection` n'est JAMAIS posé par le flow joueur réel aujourd'hui (`SetSkillDirection()`
  n'a qu'un seul appelant dans tout le projet, `TargetingSystem.TryExecuteSkill()`, lui-même
  sans appelant — code mort, vérifié par grep global). Le mode `Direction` retombe donc
  systématiquement sur `caster.transform.forward` (façade du personnage, PAS la souris/caméra) —
  comportement déjà identique pour `ExecuteDirection()`/`ExecuteSkillshot()`/`ExecuteCone()`
  existants, pas une régression de ce chantier.
- Détection des cibles : `SphereCastAll` entre position précédente et position courante à
  CHAQUE FRAME (`yield return null`), jamais un simple `OverlapSphere` sur la position
  instantanée — ne doit jamais sauter une cible fine à vitesse élevée.
- Une entité ne peut être touchée qu'une seule fois par cast (`HashSet<Entity>`).
- CD/GCD/`DelayAutoAttack` postés au moment de la résolution (lancement normal / fin de
  canalisation), jamais à l'arrivée de la hitbox.
- `vfxImpact`/`soundEffect` sont joués à la position de CHAQUE entité nouvellement touchée
  (même précédent que `ResolveMultiHitStep()` — une trajectoire est une séquence de hits
  distincts, pas une zone unique comme `DelayedZoneRoutine`). Seul le VFX de TRAJET (effet qui
  suivrait le déplacement lui-même) reste hors scope, chantier VFX séparé à venir.
- Player uniquement — Mob/PNJ hors scope.

---

### Task 1: Champ `isTrajectory` + warnings `OnValidate()` sur `SkillData`

**Files:**
- Modify: `Data/Skills/SkillData.cs:243-249` (ajout du champ juste après `zoneTickInterval`),
  `Data/Skills/SkillData.cs:379-408` (ajout de 3 warnings dans `OnValidate()`, après le warning
  `aoeRadius <= 0` existant du chantier C, avant la fermeture `}` de la méthode ligne 409)

**Interfaces:**
- Consumes: rien (nouveau champ indépendant)
- Produces: `public bool isTrajectory` — lu par `SkillBar.ResolveInstant()`/`ResolveChannel()`
  (Task 3) et `SkillSystem.StartTrajectory()`/`TrajectoryRoutine()` (Task 2)

- [ ] **Step 1: Ajouter le champ `isTrajectory`**

Dans `Data/Skills/SkillData.cs`, juste après le champ `zoneTickInterval` (ligne 249) et AVANT le
commentaire `// ── ⑩ Visuel & Son ────` (ligne 251), insérer :

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

Le champ réutilise `projectileSpeed` (vitesse), `aoeRadius` (rayon de la hitbox) et `range`
(portée max en mode `Direction`) déjà déclarés plus haut dans le fichier — aucun de ces 3 champs
n'a besoin d'un `[ShowIf]` supplémentaire pour `isTrajectory` (ils sont déjà visibles en
permanence, section ⑤ Ciblage & Portée).

- [ ] **Step 2: Ajouter les 3 warnings `OnValidate()`**

Dans `Data/Skills/SkillData.cs`, à l'intérieur de la méthode `OnValidate()`, juste après le bloc
`if (hasDelayedImpact && aoeRadius <= 0f) { ... }` (se termine ligne 408) et AVANT la fermeture
`}` de `OnValidate()` (ligne 409), insérer :

```csharp
        // isTrajectory et hasDelayedImpact sont mutuellement exclusifs — un skill est soit une
        // zone fixe différée (chantier C), soit une trajectoire mobile (ce chantier), jamais les
        // deux. Avertissement seulement, pas de correction automatique (idiome du fichier).
        if (isTrajectory && hasDelayedImpact)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true ET hasDelayedImpact = " +
                              "true simultanément — combinaison non supportée, mutuellement " +
                              "exclusifs. Décoche l'un des deux.", this);

        // [ShowIf] masque isTrajectory hors GroundTarget/Direction dans l'Inspector, mais ne le
        // RESET jamais si targetType change après coup (ShowIf n'a aucun writeback) — le champ
        // reste true, invisible, et StartTrajectory() tourne quand même au runtime, traitant
        // silencieusement le skill comme targetType = Direction (voir SkillSystem.StartTrajectory).
        if (isTrajectory && targetType != TargetType.GroundTarget && targetType != TargetType.Direction)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La trajectoire sera quand même lancée, traitée " +
                              "comme Direction. Décoche isTrajectory ou remets targetType sur " +
                              "GroundTarget/Direction.", this);

        // Symétrique du warning existant hasDelayedImpact && executionType != Normal. Sans lui,
        // un skill isTrajectory configuré en MultiHit/ComboSequence n'a aucun avertissement alors
        // que StartTrajectory() n'est jamais atteinte depuis le dispatch MultiHit/ComboSequence
        // normal (silencieusement ignorée) — sauf le cas particulier castTime > 0 + MultiHit, où
        // LaunchSkill() teste castTime AVANT executionType et route vers StartChannel()/
        // ResolveChannel() (isTrajectory y EST atteint, hitSteps silencieusement ignoré) —
        // comportement préexistant identique pour hasDelayedImpact, non corrigé ici.
        if (isTrajectory && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la trajectoire ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);
```

- [ ] **Step 3: Vérifier la compilation dans Unity**

Ouvrir Unity Editor (ou lancer une compilation batch si disponible), vérifier 0 erreur dans la
Console. Sélectionner n'importe quel `SkillData` asset existant dans le Project window pour
déclencher `OnValidate()` une fois et confirmer qu'aucun warning inattendu n'apparaît sur les
skills existants (tous ont `isTrajectory = false` par défaut, donc les 3 nouveaux warnings ne
doivent jamais se déclencher sur les assets déjà en place).

- [ ] **Step 4: Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "feat: add isTrajectory field + OnValidate warnings to SkillData"
```

---

### Task 2: `StartTrajectory()` + `TrajectoryRoutine()` sur `SkillSystem`

**Files:**
- Modify: `Combat/SkillSystem.cs` — nouvelles méthodes insérées juste après `DelayedZoneRoutine()`
  (fin de méthode ligne 289, avant le commentaire `// ResolveMultiHitStep` ligne 291)

**Interfaces:**
- Consumes: `SkillData.isTrajectory` (Task 1), `SkillData.projectileSpeed`/`aoeRadius`/`range`
  (champs existants), `_groundTargetPoint` (`Vector3?`, champ privé existant sur `SkillSystem`),
  `_skillDirection` (`Vector3?`, champ privé existant sur `SkillSystem`), `PassesAoeFilter()`/
  `ApplyEffectType()`/`ApplyStatusEffects()`/`CheckKill()` (méthodes privées/statiques existantes,
  déjà utilisées par `DelayedZoneRoutine`/`ExecuteGroundTarget`/`ExecuteDirection`)
- Produces: `public void StartTrajectory(SkillData skill, Entity caster)` — appelée par
  `SkillBar.ResolveInstant()`/`ResolveChannel()` (Task 3)

- [ ] **Step 1: Ajouter `StartTrajectory()`**

Dans `Combat/SkillSystem.cs`, juste après la fin de `DelayedZoneRoutine()` (après la ligne
`if (marker != null) Destroy(marker);` et l'accolade fermante, ligne 289), insérer :

```csharp
    /// <summary>Résout un skill `isTrajectory` DÉJÀ lancé par SkillBar (mana/anim/BeginSkillUse
    /// déjà faits au lancement) — fait le bookkeeping de résolution immédiatement (comme
    /// ResolveExecute/PlantDelayedZone), mais lance une coroutine qui déplace une hitbox du
    /// caster vers une destination, infligeant des dégâts à tout ce qu'elle traverse. Distinct de
    /// PlantDelayedZone (zone FIXE une fois plantée) — mutuellement exclusif, voir
    /// SkillData.OnValidate(). GroundTarget et Direction ne prennent jamais de cible Entity (voir
    /// commentaire en tête de fichier), donc pas de paramètre `target` ici.</summary>
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
        else // TargetType.Direction (ou targetType incompatible — voir OnValidate, traité
             // comme Direction par défaut plutôt que planter)
        {
            // _skillDirection n'est en réalité JAMAIS posé par le flow joueur actuel —
            // SetSkillDirection() n'a qu'un seul appelant dans tout le projet
            // (TargetingSystem.TryExecuteSkill(), lui-même sans appelant, code mort). Le
            // fallback caster.transform.forward est donc TOUJOURS celui utilisé en pratique
            // aujourd'hui — comportement déjà identique pour ExecuteDirection()/
            // ExecuteSkillshot()/ExecuteCone(), pas une régression introduite ici. La direction
            // résolue est celle où le PERSONNAGE fait face, pas la souris/le regard caméra.
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            float range = skill.range > 0f ? skill.range : 10f;
            destination = origin + dir * range;
        }

        StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination));
    }

    /// <summary>Déplace un point virtuel de `origin` à `destination` à la vitesse
    /// `skill.projectileSpeed` (fallback 10), balaie un SphereCastAll (rayon `skill.aoeRadius`,
    /// fallback 0.5) entre la position du tick précédent et la position du tick courant à CHAQUE
    /// FRAME — ne peut jamais sauter une cible même à vitesse élevée. Un OverlapSphere initial à
    /// `origin` précède la boucle (un SphereCastAll ne détecte pas un chevauchement déjà présent
    /// à son point de départ — sinon une entité collée au caster au lancement ne serait jamais
    /// touchée). Une entité ne peut être touchée qu'une seule fois par cast (HashSet).
    /// vfxImpact/soundEffect joués par entité touchée (même précédent que ResolveMultiHitStep) —
    /// seul le VFX de TRAJET (effet qui suivrait le déplacement lui-même) reste hors scope,
    /// chantier VFX séparé à venir.</summary>
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination)
    {
        float totalDistance = Vector3.Distance(origin, destination);
        if (totalDistance <= 0.01f) yield break; // origine == destination, rien à parcourir

        float speed  = skill.projectileSpeed > 0f ? skill.projectileSpeed : 10f;
        float radius = skill.aoeRadius       > 0f ? skill.aoeRadius       : 0.5f;
        Vector3 dir  = (destination - origin) / totalDistance;

        HashSet<Entity> alreadyHit = new HashSet<Entity>();

        // Pass initiale à l'origine — un SphereCastAll ne détecte JAMAIS un collider déjà en
        // chevauchement à son point de départ (limitation connue de la physique Unity, même
        // raison pour laquelle DashInDirection utilise OverlapSphere et non un SphereCast). Sans
        // ce pass, une entité collée au caster au moment du lancement (ex: un ennemi au
        // corps-à-corps quand le joueur lance la trajectoire) pourrait n'être JAMAIS touchée.
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
            // DelayedZoneRoutine (rien à nettoyer après, pas de marker/VFX créé par cette
            // coroutine). DashToTarget/DashInDirection utilisent `yield break` (pas `break`) car
            // ILS ont du nettoyage post-boucle à sauter — pas le cas ici, comparaison à ces
            // deux-là non pertinente.
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
                    // pas une zone unique comme DelayedZoneRoutine qui joue un seul vfx/son par
                    // tick peu importe combien d'entités touchées).
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

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console (`HashSet<Entity>` nécessite
`System.Collections.Generic`, déjà importé en tête de `SkillSystem.cs` — pas de nouvel `using` à
ajouter).

- [ ] **Step 3: Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "feat: add StartTrajectory + TrajectoryRoutine for mobile hitbox skills"
```

---

### Task 3: Branchement `isTrajectory` dans `SkillBar`

**Files:**
- Modify: `Data/Skills/SkillBar.cs:539-542` (`ResolveInstant()`), `Data/Skills/SkillBar.cs:721-724`
  (`ResolveChannel()`)

**Interfaces:**
- Consumes: `SkillSystem.Instance.StartTrajectory(SkillData, Entity)` (Task 2),
  `SkillData.isTrajectory` (Task 1)
- Produces: rien (point d'entrée terminal du flow de résolution)

- [ ] **Step 1: Brancher `isTrajectory` dans `ResolveInstant()`**

Dans `Data/Skills/SkillBar.cs`, la méthode `ResolveInstant()` contient actuellement (lignes
539-542) :

```csharp
        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
```

Remplacer par :

```csharp
        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
```

La garde combo existante (`if (IsComboActive && slot == _comboSlot) { ... return; }`, lignes
532-537, juste AVANT ce bloc) reste inchangée et continue d'être évaluée en premier — un step de
combo ne passe jamais par `StartTrajectory()`, exactement comme il ne passe jamais par
`PlantDelayedZone()`.

- [ ] **Step 2: Brancher `isTrajectory` dans `ResolveChannel()`**

Dans `Data/Skills/SkillBar.cs`, la méthode `ResolveChannel()` contient actuellement (lignes
721-724) :

```csharp
        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);
```

Remplacer par :

```csharp
        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);
```

Le cas par défaut reste `Execute()` (pas `ResolveExecute()`) — délibérément préservé, comme le
fait déjà le branchement `hasDelayedImpact` du chantier C (pas une incohérence à corriger dans
ce chantier).

`_cooldownTimers[slot]`/`_gcdTimer`/`DelayAutoAttack` (lignes suivantes dans les deux méthodes)
restent inchangés — postés après le branchement dans les deux cas, peu importe la branche prise.

- [ ] **Step 3: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 4: Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "feat: wire isTrajectory branching into ResolveInstant/ResolveChannel"
```

---

### Task 4: Vérification manuelle Play Mode (geste Florian)

**Files:** aucun fichier de code — création de 2 assets `SkillData` de test + vérification en
jeu, pas automatisable par un agent.

**Interfaces:**
- Consumes: `SkillData.isTrajectory`/`targetType` (Task 1), branchement `SkillBar` (Task 3)
- Produces: rien — tâche de validation finale

- [ ] **Step 1: Créer 2 skills de test**

Un skill `isTrajectory = true` + `targetType = GroundTarget`, un skill `isTrajectory = true` +
`targetType = Direction`. `hasDelayedImpact = false` sur les deux (sinon warning + comportement
non défini). `projectileSpeed` réglé à une valeur lente pour bien observer le déplacement en jeu
(ex: 3-5 unités/sec plutôt que la valeur par défaut, le temps de voir la hitbox progresser).

- [ ] **Step 2: Tester GroundTarget**

Cliquer un point au sol avec le skill GroundTarget+isTrajectory. Vérifier que la hitbox met un
temps visible/mesurable à atteindre ce point (cohérent avec `projectileSpeed`), et qu'une entité
placée SUR le chemin (pas seulement à l'arrivée) prend des dégâts.

- [ ] **Step 3: Tester Direction**

Marcher d'abord dans une direction précise et bien visible (le joueur ne pivote JAMAIS vers la
souris en mode Direction — `EngageAndFaceTarget()` l'exclut explicitement, voir
`SkillBar.cs:980-990` — donc `caster.transform.forward` = dernière direction de marche, pas le
regard caméra). Puis lancer le skill Direction+isTrajectory sans cible. Vérifier que la hitbox
voyage en ligne droite dans CETTE direction (pas la souris/caméra — `_skillDirection` n'est
jamais posé par le flow joueur actuel, voir Global Constraints) sur une distance cohérente avec
`range`, et touche les entités sur le chemin.

- [ ] **Step 4: Vérifier l'esquive réelle**

Lancer une trajectoire vers/à travers une entité, puis faire bouger cette entité HORS du chemin
avant que la hitbox n'atteigne sa position — elle ne doit PAS être touchée.

- [ ] **Step 5: Vérifier l'absence de double-hit**

Lancer une trajectoire à travers une entité qui reste immobile pile sur le chemin — elle ne doit
recevoir qu'un seul tick de dégâts, pas un par frame passée dans son rayon.

- [ ] **Step 6: Vérifier le CD posé au lancement**

Observer que le cooldown de la slot démarre immédiatement au clic (GroundTarget) ou au lancement
(Direction) — pas à l'arrivée de la hitbox à destination.

- [ ] **Step 7: Vérifier le warning de cumul**

Sur un des 2 skills de test, cocher temporairement `hasDelayedImpact = true` EN PLUS de
`isTrajectory = true` — vérifier que le warning Console attendu (Task 1, Step 2) apparaît à la
sélection de l'asset dans le Project window. Décocher `hasDelayedImpact` ensuite pour revenir à
l'état de test normal.

- [ ] **Step 8: Vérifier deux trajectoires simultanées**

Relancer un 2ᵉ skill à trajectoire avant que le 1ᵉʳ n'ait atteint sa destination (CD permettant,
ou sur 2 slots différentes) — les deux doivent progresser indépendamment sans se perturber ni
partager leur `HashSet` anti-double-hit.

- [ ] **Step 9: Vérifier le vfxImpact/soundEffect par entité touchée**

Configurer `vfxImpact`/`soundEffect` sur un des 2 skills de test. Lancer une trajectoire qui
touche 2+ entités espacées sur le chemin — vérifier que le VFX/son joue à CHAQUE hit, à la
position de l'entité touchée (pas une seule fois pour tout le cast, pas au point de départ/
d'arrivée).

- [ ] **Step 10: Vérifier une cible au corps-à-corps (point blank)**

Lancer une trajectoire alors qu'une entité est déjà collée au caster au moment du lancement
(ex: un ennemi au corps-à-corps) — elle DOIT être touchée dès le premier instant. Vérifie le
pass `OverlapSphere` initial de `TrajectoryRoutine()` (Task 2, Step 1) : un `SphereCastAll` seul
ne détecte pas un chevauchement déjà présent à son point de départ, cette entité serait
autrement ratée en permanence.

- [ ] **Step 11: Vérifier la mort du caster en cours de trajet**

Si possible à déclencher manuellement (prendre des dégâts pendant qu'une trajectoire lente
voyage) — la coroutine doit s'arrêter proprement (`break` sur `caster.isDead`), aucune erreur
Console.

- [ ] **Step 12: Rapporter les résultats**

Florian confirme si tous les points ci-dessus passent, ou signale les écarts observés pour
investigation.
