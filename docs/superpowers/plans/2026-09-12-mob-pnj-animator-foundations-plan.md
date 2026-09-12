# Fondations Animator Mob/PNJ + hit-frame-sync (sous-chantier 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner à Mob et PNJ un vrai système d'animation de combat (locomotion `Speed` + state
`Attack` réutilisable) et synchroniser la résolution de leurs skills (dégâts/zone différée/
trajectoire/MultiHit) sur la frame d'impact réelle de l'animation, au lieu d'une résolution
instantanée au déclenchement — même mécanisme que celui déjà en production pour le Player
(chantiers A « Canalisation » et B « hit-frame-sync »).

**Architecture:** Deux nouveaux composants `MobAnimatorController`/`PNJAnimatorController`
(copie réduite de `PlayerAnimatorController` — un seul state `Attack` réutilisable, clip
substitué via `AnimatorOverrideController`) relaient un Animation Event vers un mécanisme
« pending-hit » ajouté dans `Mob.cs`/`PNJ.cs` : le skill se déclenche (anim jouée, état posé)
mais ne s'exécute réellement (`PlantDelayedZone`/`StartTrajectory`/`ResolveExecute`/
`ResolveMultiHitStep`, déjà existants et caster-agnostiques dans `SkillSystem`) qu'à l'event
d'impact ou à un timeout de secours.

**Tech Stack:** Unity 2022+ (C#), `Animator`/`AnimatorOverrideController`/`NavMeshAgent`
(UnityEngine.AI), pas de framework de test automatisé — vérification manuelle Play Mode.

**Spec:** `docs/superpowers/specs/2026-09-12-mob-pnj-animator-foundations-design.md`

## Global Constraints

- Deux scripts séparés pour Mob et PNJ (pas de composant Animator partagé) — décision actée avec
  Florian, ne pas fusionner en un seul script « générique ».
- `Combat/SkillSystem.cs` n'est PAS modifié dans ce plan — `ResolveExecute(SkillData, Entity,
  Entity)` (ligne ~175) et `ResolveMultiHitStep(SkillData, Entity, Entity, int)` (ligne ~541)
  existent déjà et sont déjà caster-agnostiques. Ne pas les toucher, ne pas les dupliquer.
- Le branchement à 3 voies déjà en place dans `Mob.cs`/`PNJ.cs`
  (`hasDelayedImpact`→`PlantDelayedZone`, `isTrajectory`→`StartTrajectory`, sinon
  `Execute()`/`ResolveExecute()`) est CONSERVÉ — il est seulement déplacé du point de
  déclenchement au point de résolution (l'Animation Event ou le timeout), jamais réécrit.
- CD des skills secondaires (`_skillCooldowns[skill] = ...`) posé à la RÉSOLUTION, jamais au
  déclenchement. `attackTimer`/`_attackTimer` (délai générique avant retentative d'attaque, PAS
  un cooldown de skill) restent posés au déclenchement, inchangés — ne pas les confondre.
- **Garde de ré-entrée obligatoire** (trouvé en vérification indépendante du plan, absent du
  premier jet) : déplacer le CD à la résolution retire la SEULE protection qui empêchait
  aujourd'hui `TryUseSkill()`/`TryUseSecondarySkill()`/le bloc d'attaque de base de se
  redéclencher à CHAQUE frame tant qu'un pending-hit est en vol (`_skillCooldowns`/`attackTimer`
  ne sont plus réarmés avant la résolution). Sans garde explicite via `IsPendingHit`, ceci
  provoque un vrai bug : mana vidée frame par frame, anim relancée sur sa frame 0 en boucle,
  l'Animation Event d'impact jamais atteint. `IsPendingHit` (déjà introduit comme champ public
  dans ce plan) DOIT être consommé — voir Tasks 3/4, Steps 5-6 — pour bloquer tout nouveau
  déclenchement tant qu'un pending-hit existe déjà.
- MultiHit synchronisé par event (`hitIndex`), pas de Combo côté Mob/PNJ (n'existe pas). Un
  skill MultiHit SANS `attackAnimation` assignée reste sur l'ancien chemin
  `SkillSystem.Execute()`/`ExecuteMultiHit()` (coroutine, respecte `HitStep.delay`) plutôt que
  d'entrer dans le mécanisme pending-hit — trouvé en vérification indépendante : sans ce
  détour, un MultiHit sans anim perdrait le respect de `HitStep.delay` entre les coups (voir
  Tasks 3/4, Step 7, bloc `StartPendingHit`).
- Idiome à préserver strictement : `Mob.cs` utilise `_skillSystem?.` partout (null-conditional).
  `PNJ.cs` utilise `_skillSystem.` SANS `?.` (garanti non-null par le guard déjà en tête de
  `HandleCombatAI()` : `if (_skillSystem == null) return;`). Ne jamais uniformiser les deux
  fichiers l'un sur l'autre.
- Pas de vérification stricte que l'`hitIndex` reçu correspond à `_pendingMultiNextIndex` avant
  résolution (contrairement à `SkillBar.ResolveMultiHitIndex` côté Player) — simplification
  volontaire actée dans le spec, ne PAS l'ajouter dans ce plan.
- Aucune annulation explicite du pending-hit dans `Mob.Die()` — `Mob.Update()` s'arrête net via
  `this.enabled = false` posé par `Die()`, donc le pending abandonné n'est jamais relu.
  **Exception pour `PNJ` (trouvé en vérification indépendante du plan)** : `PNJ.Die()` ne
  désactive PAS le composant (le PNJ reste actif, juste `isDead = true`), et
  `PNJ.RespawnCoroutine()` repasse `isDead = false` après `data.respawnDelay` secondes SANS
  jamais nettoyer un pending-hit resté en vol — un PNJ tué pendant l'anim de son attaque
  respawnerait avec un pending-hit fantôme, résolu au premier `Update()` après respawn
  (dégâts/zone/trajectoire déclenchés depuis le point de spawn, sur une cible potentiellement
  hors de portée). `PNJ.Die()` DOIT nettoyer `_pendingSkill`/`_pendingTarget`/`_pendingTimeout`/
  `_pendingIsMulti` — voir Task 4, Step 5bis.
- Hors scope permanent de ce plan : state `Channel`/`CancelActionTrigger`/cast-bar/`vfxCast`
  pour Mob/PNJ (sous-chantier 2, futur, pas ce plan-ci).

---

## Task 1: `World/MobAnimatorController.cs` (nouveau)

**Files:**
- Create: `World/MobAnimatorController.cs`

**Interfaces:**
- Consumes: `UnityEngine.Animator`, `UnityEngine.AI.NavMeshAgent`, `Mob` (via `GetComponent<Mob>()`
  — seulement pour appeler `Mob.OnAnimationHitEvent(int)`, produit par la Task 3 — voir note de
  compilation ci-dessous, ce fichier ne compile PAS tant que la Task 3 n'existe pas).
- Produces: `public void PlayAttack(AnimationClip clip)`, `public void OnSkillHitFrame(int
  hitIndex = 0)` — consommés respectivement par `Mob.cs` (Task 3, pour déclencher l'anim) et par
  un Animation Event Unity posé sur un clip (geste Editor de Florian, hors plan).

**Note de compilation** : ce fichier référence `_mob.OnAnimationHitEvent(hitIndex)` — cette
méthode n'existe sur `Mob` qu'après la Task 3. Si ce plan est exécuté dans l'ordre (1 puis 3),
aucun souci. Si Task 1 est validée/compilée seule AVANT que Task 3 existe, le projet ne
compilera pas tant que Task 3 n'est pas faite — c'est attendu et sans risque en développement
solo (pas de CI qui bloquerait sur un état intermédiaire), mais NE PAS committer Task 1 seule
sur une branche partagée sans Task 3 dans la foulée.

- [ ] **Step 1: Créer le fichier avec le code complet**

```csharp
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// MOBANIMATORCONTROLLER.CS — Pilotage de l'Animator d'un Mob
// Path : Assets/Scripts/World/MobAnimatorController.cs
//
// Fondations (sous-chantier 1, voir docs/superpowers/specs/
// 2026-09-12-mob-pnj-animator-foundations-design.md) — locomotion (Speed) +
// state "Attack" réutilisable, même pattern que PlayerAnimatorController.cs.
// PAS de paramètre InCombat (aucune mécanique d'équipement visible sur Mob),
// PAS de CancelActionTrigger/state Channel (sous-chantier 2, futur).
// =============================================================

[RequireComponent(typeof(Animator))]
public class MobAnimatorController : MonoBehaviour
{
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans l'Animator\n" +
             "Controller (un placeholder dédié, jamais réutilisé) — AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE, pas par nom de state — sans cette référence\n" +
             "exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private Mob                         _mob;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _mob      = GetComponent<Mob>();

        // Enveloppe le Controller de base dans un Override — permet d'échanger le clip du state
        // "Attack" à la volée sans toucher au graphe. Si aucun Controller n'est encore assigné
        // (mannequin pas encore équipé de son Animator Controller), on n'enveloppe rien —
        // PlayAttack() ne fera rien tant que ce sera le cas.
        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[MobAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);
    }

    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;

        // Indexation par référence au clip D'ORIGINE (attackPlaceholderClip), pas par nom de
        // state — voir le commentaire sur le champ ci-dessus.
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (hit simple ou coup de base d'un MultiHit),
    /// 1..N pour les hitSteps. Relais pur vers l'instance locale — pas de singleton comme
    /// SkillBar côté Player, chaque Mob résout son propre pending-hit.</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _mob?.OnAnimationHitEvent(hitIndex);
    }
}
```

- [ ] **Step 2: Vérifier la compilation**

Ce fichier seul ne compile pas tant que `Mob.OnAnimationHitEvent(int)` n'existe pas (Task 3).
Si Task 3 n'est pas encore faite, ignorer toute erreur de compilation Unity mentionnant
`OnAnimationHitEvent` à ce stade — normal, transitoire. Vérifier en revanche qu'il n'y a AUCUNE
autre erreur (syntaxe, using manquant, etc.) en lisant le fichier une dernière fois avant de
committer.

- [ ] **Step 3: Commit**

```bash
git add World/MobAnimatorController.cs
git commit -m "feat: add MobAnimatorController (locomotion + reusable Attack state)"
```

---

## Task 2: `World/PNJAnimatorController.cs` (nouveau)

**Files:**
- Create: `World/PNJAnimatorController.cs`

**Interfaces:**
- Consumes: `UnityEngine.Animator`, `UnityEngine.AI.NavMeshAgent`, `PNJ` (via `GetComponent<PNJ>()`
  — pour appeler `PNJ.OnAnimationHitEvent(int)`, produit par la Task 4 — ce fichier ne compile
  PAS tant que la Task 4 n'existe pas, même remarque que la Task 1).
- Produces: `public void PlayAttack(AnimationClip clip)`, `public void OnSkillHitFrame(int
  hitIndex = 0)` — consommés par `PNJ.cs` (Task 4) et par un Animation Event Unity (geste
  Editor, hors plan).

Indépendante de la Task 1 (fichier différent, aucune interface partagée entre
`MobAnimatorController` et `PNJAnimatorController`).

- [ ] **Step 1: Créer le fichier avec le code complet**

```csharp
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// PNJANIMATORCONTROLLER.CS — Pilotage de l'Animator d'un PNJ
// Path : Assets/Scripts/World/PNJAnimatorController.cs
//
// Fondations (sous-chantier 1, voir docs/superpowers/specs/
// 2026-09-12-mob-pnj-animator-foundations-design.md) — locomotion (Speed) +
// state "Attack" réutilisable, même pattern que MobAnimatorController.cs.
// Pas de RequireComponent(NavMeshAgent) : un PNJ non-canFight n'en a pas
// (voir PNJ.Awake() — _agent n'est assigné que si data.canFight est vrai).
// =============================================================

[RequireComponent(typeof(Animator))]
public class PNJAnimatorController : MonoBehaviour
{
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans l'Animator\n" +
             "Controller (un placeholder dédié, jamais réutilisé) — AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE, pas par nom de state — sans cette référence\n" +
             "exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private PNJ                         _pnj;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _pnj      = GetComponent<PNJ>();

        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[PNJAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);
    }

    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par PNJ.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (hit simple ou coup de base d'un MultiHit),
    /// 1..N pour les hitSteps. Relais pur vers l'instance locale.</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _pnj?.OnAnimationHitEvent(hitIndex);
    }
}
```

- [ ] **Step 2: Vérifier la compilation**

Même remarque transitoire que la Task 1 (`PNJ.OnAnimationHitEvent` arrive en Task 4). Vérifier
l'absence de toute autre erreur.

- [ ] **Step 3: Commit**

```bash
git add World/PNJAnimatorController.cs
git commit -m "feat: add PNJAnimatorController (locomotion + reusable Attack state)"
```

---

## Task 3: `Entities/Mob.cs` — pending-hit + branchement déplacé à la résolution

**Files:**
- Modify: `Entities/Mob.cs`

**Interfaces:**
- Consumes: `MobAnimatorController.PlayAttack(AnimationClip)` (Task 1) ;
  `SkillSystem.ResolveExecute(SkillData, Entity, Entity)`, `SkillSystem.ResolveMultiHitStep(SkillData, Entity, Entity, int)`, `SkillSystem.PlantDelayedZone(SkillData, Entity, Entity)`,
  `SkillSystem.StartTrajectory(SkillData, Entity)` (toutes déjà existantes dans
  `Combat/SkillSystem.cs`, non modifiées par ce plan).
- Produces: `public void OnAnimationHitEvent(int hitIndex = 0)` — consommé par
  `MobAnimatorController.OnSkillHitFrame()` (Task 1, via `GetComponent<Mob>()`).

Dépend de la Task 1 (le champ `_animatorController` a le type `MobAnimatorController`).
Indépendante de la Task 2/4 (aucun fichier ni interface partagée avec PNJ).

⚠ **Avant d'éditer** : le fichier a pu changer de forme depuis l'écriture de ce plan (numéros de
ligne indicatifs). Toujours lire `Entities/Mob.cs` en entier — au minimum `Awake()`,
`Update()`, `HandleAttack()`, `TryUseSkill()` — dans leur état RÉEL actuel avant de modifier quoi
que ce soit, pour repérer tout écart avec les extraits ci-dessous.

- [ ] **Step 1: Lire le fichier réel en entier**

Confirmer que `Awake()` (autour de la ligne 71), `Update()` (autour de la ligne 132),
`HandleAttack()` (autour de la ligne 331) et `TryUseSkill()` (autour de la ligne 384) existent
toujours sous ces noms avec un contenu proche de celui décrit ci-dessous. Si un écart existe,
adapter les steps suivants à la forme réelle plutôt que d'écraser du code qui aurait changé
entre-temps.

- [ ] **Step 2: Ajouter les nouveaux champs privés**

Ajouter, juste après le champ existant `private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();` (autour de la ligne 53) :

```csharp

    // ── Attente de résolution (hit-frame-sync — un seul hit en vol à la fois, un Mob n'agit
    // jamais en parallèle sur deux skills) — sous-chantier 1, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-animator-foundations-design.md ─────────────────────────────────
    private SkillData _pendingSkill   = null;
    private Entity    _pendingTarget  = null;
    private float     _pendingTimeout = 0f;
    private bool      _pendingIsMulti = false;
    private int       _pendingMultiNextIndex = 0;

    public bool IsPendingHit => _pendingSkill != null;

    private MobAnimatorController _animatorController;
```

- [ ] **Step 3: Cacher le composant dans `Awake()`**

Trouver :
```csharp
    protected override void Awake()
    {
        base.Awake();
        agent           = GetComponent<NavMeshAgent>();
        _skillSystem    = GetComponent<SkillSystem>();
        spawnPos = transform.position;
        ApplyData();
    }
```
Remplacer par :
```csharp
    protected override void Awake()
    {
        base.Awake();
        agent               = GetComponent<NavMeshAgent>();
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<MobAnimatorController>();
        spawnPos = transform.position;
        ApplyData();
    }
```

- [ ] **Step 4: Ajouter le tick du pending-hit dans `Update()`**

Trouver :
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead || data == null) return;

        attackTimer -= Time.deltaTime;
```
Remplacer par :
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead || data == null) return;

        attackTimer -= Time.deltaTime;

        // Timeout de secours (event d'impact jamais reçu) — tick AVANT tout early-return CC :
        // un coup déjà lancé va au bout, non-interruptible, même principe que le Player
        // (chantier B hit-frame-sync).
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }
```
(le reste de la méthode — Slow×Haste, `RefreshEnemyList()`, `IsDashing`, le bloc CC, le
`switch (currentState)` — reste identique, inchangé.)

- [ ] **Step 5: Remplacer le déclenchement de l'attaque de base dans `HandleAttack()`**

Trouver :
```csharp
        // Attaque de base
        if (attackTimer <= 0f)
        {
            attackTimer = data.attackCooldown;

            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                if (data.basicAttackSkill.hasDelayedImpact)
                    _skillSystem?.PlantDelayedZone(data.basicAttackSkill, this, target);
                else if (data.basicAttackSkill.isTrajectory)
                    _skillSystem?.StartTrajectory(data.basicAttackSkill, this);
                else
                    _skillSystem?.Execute(data.basicAttackSkill, this, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
        }
```
Remplacer par :
```csharp
        // Attaque de base — bloquée tant qu'un pending-hit est en vol (StartPendingHit ci-
        // dessous) : sans cette garde, une fois le CD déplacé à la résolution (Step 7), plus
        // rien n'empêcherait ce bloc de se redéclencher à CHAQUE frame pendant l'anim (trouvé
        // en vérification indépendante du plan — voir Global Constraints).
        if (attackTimer <= 0f && !IsPendingHit)
        {
            attackTimer = data.attackCooldown;

            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                StartPendingHit(data.basicAttackSkill, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
        }
```

- [ ] **Step 6: Ajouter la garde de ré-entrée en tête de `TryUseSkill()`**

Trouver :
```csharp
    private bool TryUseSkill(Entity target)
    {
        if (data.skills == null || data.skills.Count == 0) return false;
```
Remplacer par :
```csharp
    private bool TryUseSkill(Entity target)
    {
        // Un pending-hit est déjà en vol (secondaire OU attaque de base) — ne rien redéclencher
        // tant qu'il n'est pas résolu (event ou timeout). Retourne true pour que HandleAttack()/
        // HandleChase() traitent ce tick comme "occupé" (mêmes early-return qu'un vrai skill
        // lancé) plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit) return true;
        if (data.skills == null || data.skills.Count == 0) return false;
```

- [ ] **Step 7: Remplacer le déclenchement dans `TryUseSkill()`**

Trouver :
```csharp
            LookAt(target.transform);
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, this);
            else
                _skillSystem?.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            attackTimer = data.attackCooldown;
            return true;
```
Remplacer par :
```csharp
            LookAt(target.transform);
            StartPendingHit(skill, target);
            attackTimer = data.attackCooldown;
            return true;
```
(`_skillCooldowns[skill] = ...` disparaît d'ici — posé désormais dans `ResolvePendingHit()`,
voir Step 8. `attackTimer` reste posé ici, inchangé.)

- [ ] **Step 8: Ajouter les 3 nouvelles méthodes, juste après `TryUseSkill()`**

```csharp

    /// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
    /// (dégâts/effets/zone/trajectoire) n'arrive qu'à l'event d'impact ou au timeout de
    /// secours, jamais ici. Sans attackAnimation assignée, résout immédiatement (comportement
    /// identique à avant ce sous-chantier). Un MultiHit SANS attackAnimation reste sur l'ancien
    /// chemin Execute()/ExecuteMultiHit (respecte HitStep.delay via coroutine) — il n'y a pas de
    /// frame d'impact à attendre, entrer dans le pending-hit ferait perdre ce délai entre coups
    /// (trouvé en vérification indépendante du plan).</summary>
    private void StartPendingHit(SkillData skill, Entity target)
    {
        bool isMulti = skill.executionType == SkillExecutionType.MultiHit
                       && skill.hitSteps != null && skill.hitSteps.Count > 0;

        if (isMulti && skill.attackAnimation == null)
        {
            _skillSystem?.Execute(skill, this, target);
            if (data.skills != null && data.skills.Contains(skill))
                _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            return;
        }

        _animatorController?.PlayAttack(skill.attackAnimation);

        _pendingSkill   = skill;
        _pendingTarget  = target;
        _pendingIsMulti = isMulti;
        _pendingMultiNextIndex = 0;
        _pendingTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingTimeout <= 0f)
            ResolvePendingHit(0);
    }

    /// <summary>Reçoit l'Animation Event relayé par MobAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;   // event hors contexte, ignoré silencieusement
        ResolvePendingHit(hitIndex);
    }

    /// <summary>Résout le hit en attente — branchement à 3 voies identique à avant ce
    /// sous-chantier (hasDelayedImpact / isTrajectory / dispatch standard), sauf routage
    /// MultiHit par index. Pose le cooldown du skill secondaire APRÈS résolution (pas au
    /// déclenchement). Rejette un hitIndex hors séquence (event mal numéroté sur le clip —
    /// piège trouvé en task-review : Unity met souvent l'argument int par défaut à 0 sur
    /// CHAQUE event d'un clip MultiHit si on oublie de le changer, ce qui résoudrait le coup
    /// de base plusieurs fois au lieu des hitSteps distincts, silencieusement).</summary>
    private void ResolvePendingHit(int hitIndex)
    {
        if (_pendingSkill == null) return;

        SkillData skill   = _pendingSkill;
        Entity    target  = _pendingTarget;
        bool      isMulti = _pendingIsMulti;

        if (isMulti && hitIndex != _pendingMultiNextIndex)
        {
            Debug.LogWarning($"[MOB] Animation Event MultiHit reçu avec hitIndex={hitIndex}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        if (isMulti)
        {
            _skillSystem?.ResolveMultiHitStep(skill, this, target, hitIndex);
            _pendingMultiNextIndex++;
            int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
            if (_pendingMultiNextIndex < totalHits) return;   // encore des hits à venir
        }
        else
        {
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, this);
            else
                _skillSystem?.ResolveExecute(skill, this, target);
        }

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        // CD posé ici (résolution), pas au déclenchement — même principe que le chantier B côté
        // Player. Ne concerne que les skills secondaires (data.skills) — l'attaque de base
        // utilise attackTimer, déjà reposé au déclenchement dans HandleAttack().
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }
```

- [ ] **Step 8bis: Nettoyer le pending-hit dans `FullReset()`**

Trouver (dans `FullReset()`) :
```csharp
        currentState      = MobState.Patrol;
        _skillCooldowns.Clear();
```
Remplacer par :
```csharp
        currentState      = MobState.Patrol;
        _skillCooldowns.Clear();

        // Un Mob qui rentre au spawn (leash) en plein milieu de l'anim de son attaque ne doit
        // pas voir ce coup résoudre plus tard sur une cible désormais hors combat — trouvé en
        // task-review.
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;
```

- [ ] **Step 9: Vérifier la compilation**

Lire le fichier modifié en entier une dernière fois — confirmer qu'`Awake()`, `Update()`,
`HandleAttack()`, `TryUseSkill()` sont syntaxiquement corrects (accolades équilibrées) et que
les 3 nouvelles méthodes sont bien à l'intérieur de la classe `Mob`. Confirmer que
`SkillExecutionType` est un type déjà connu du fichier (utilisé ailleurs dans le projet,
`Data/Skills/SkillData.cs`) — aucun nouveau `using` requis. Confirmer que `TryUseSkill()`
retourne bien `true` immédiatement quand `IsPendingHit` est vrai (Step 6), et que le bloc
d'attaque de base dans `HandleAttack()` porte bien la condition `&& !IsPendingHit` (Step 5).

- [ ] **Step 10: Commit**

```bash
git add Entities/Mob.cs
git commit -m "feat: sync Mob skill resolution on attack animation hit-frame"
```

---

## Task 4: `Entities/PNJ.cs` — même pattern, idiome sans `?.`

**Files:**
- Modify: `Entities/PNJ.cs`

**Interfaces:**
- Consumes: `PNJAnimatorController.PlayAttack(AnimationClip)` (Task 2) ; les mêmes 4 méthodes
  `SkillSystem` que la Task 3.
- Produces: `public void OnAnimationHitEvent(int hitIndex = 0)` — consommé par
  `PNJAnimatorController.OnSkillHitFrame()` (Task 2).

Dépend de la Task 2. Indépendante de la Task 3 (aucun fichier ni interface partagée avec Mob).

⚠ **Avant d'éditer** : lire `Entities/PNJ.cs` en entier — au minimum `Awake()` (autour de la
ligne 84), `Update()` (autour de la ligne 134), `HandleCombatAI()` (autour de la ligne 490),
`TryUseSecondarySkill()` (autour de la ligne 553) — dans leur état RÉEL actuel avant de modifier.

**Rappel critique** : `PNJ.cs` n'utilise JAMAIS `?.` sur `_skillSystem` (garanti non-null par le
guard `if (_skillSystem == null) return;` en tête de `HandleCombatAI()`). Tous les appels
`_skillSystem.` dans les steps ci-dessous sont SANS `?.`, volontairement — ne pas copier
l'idiome `?.` de Mob.cs.

- [ ] **Step 1: Lire le fichier réel en entier**

Confirmer la forme actuelle de `Awake()`, `Update()`, `HandleCombatAI()`,
`TryUseSecondarySkill()`.

- [ ] **Step 2: Ajouter les nouveaux champs privés**

Ajouter, juste après le champ existant `private Dictionary<SkillData, float> _skillCooldowns = new Dictionary<SkillData, float>();` (autour de la ligne 78) :

```csharp

    // ── Attente de résolution (hit-frame-sync — un seul hit en vol à la fois) —
    // sous-chantier 1, voir docs/superpowers/specs/2026-09-12-mob-pnj-animator-foundations-
    // design.md ──────────────────────────────────────────────────────────────────────────
    private SkillData _pendingSkill   = null;
    private Entity    _pendingTarget  = null;
    private float     _pendingTimeout = 0f;
    private bool      _pendingIsMulti = false;
    private int       _pendingMultiNextIndex = 0;

    public bool IsPendingHit => _pendingSkill != null;

    private PNJAnimatorController _animatorController;
```

- [ ] **Step 3: Cacher le composant dans `Awake()`**

Trouver, dans `protected override void Awake()` :
```csharp
        // Cache des composants combat
        _skillSystem = GetComponent<SkillSystem>();
        _spawnPos    = transform.position;
```
Remplacer par :
```csharp
        // Cache des composants combat
        _skillSystem        = GetComponent<SkillSystem>();
        _animatorController = GetComponent<PNJAnimatorController>();
        _spawnPos    = transform.position;
```

- [ ] **Step 4: Ajouter le tick du pending-hit dans `Update()`**

Trouver :
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;

        if (data != null && data.canFight)
            HandleCombatAI();
    }
```
Remplacer par :
```csharp
    protected override void Update()
    {
        base.Update();
        if (isDead) return;

        // Timeout de secours (event d'impact jamais reçu) — même principe que Mob.cs.
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        if (data != null && data.canFight)
            HandleCombatAI();
    }
```

- [ ] **Step 5: Remplacer le déclenchement de l'attaque de base dans `HandleCombatAI()`**

Trouver :
```csharp
            if (_attackTimer <= 0f && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                {
                    if (data.basicAttackSkill.hasDelayedImpact)
                        _skillSystem.PlantDelayedZone(data.basicAttackSkill, this, _combatTarget);
                    else if (data.basicAttackSkill.isTrajectory)
                        _skillSystem.StartTrajectory(data.basicAttackSkill, this);
                    else
                        _skillSystem.Execute(data.basicAttackSkill, this, _combatTarget);
                }
                _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            }
```
Remplacer par :
```csharp
            // Bloqué tant qu'un pending-hit est en vol — même raison que Mob.HandleAttack()
            // (voir Global Constraints : sans cette garde, plus rien n'empêche un
            // redéclenchement à chaque frame une fois le CD déplacé à la résolution).
            if (_attackTimer <= 0f && !IsPendingHit && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                    StartPendingHit(data.basicAttackSkill, _combatTarget);
                _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            }
```

- [ ] **Step 5bis: Nettoyer le pending-hit quand le leash se déclenche dans `HandleCombatAI()`**

Trouver, tout en haut de `HandleCombatAI()` (avant le bloc de la Step 5) :
```csharp
        if (data.leashRadius > 0f &&
            Vector3.Distance(transform.position, _spawnPos) > data.leashRadius)
        {
            // Trop loin du spawn — lâche la cible et rentre
            _combatTarget = null;
            ReturnToSpawn();
            return;
        }
```
Remplacer par :
```csharp
        if (data.leashRadius > 0f &&
            Vector3.Distance(transform.position, _spawnPos) > data.leashRadius)
        {
            // Trop loin du spawn — lâche la cible et rentre. Un pending-hit en vol ne doit pas
            // résoudre plus tard sur une cible désormais hors combat — trouvé en task-review
            // (même bug que Mob.FullReset()).
            _combatTarget   = null;
            _pendingSkill   = null;
            _pendingTarget  = null;
            _pendingTimeout = 0f;
            _pendingIsMulti = false;
            ReturnToSpawn();
            return;
        }
```

- [ ] **Step 6: Nettoyer le pending-hit dans `Die()`**

Trouver :
```csharp
        base.Die();

        _agent?.ResetPath();
        _combatTarget = null;
```
Remplacer par :
```csharp
        base.Die();

        _agent?.ResetPath();
        _combatTarget = null;

        // Un PNJ (contrairement à un Mob) n'est jamais désactivé à sa mort — RespawnCoroutine()
        // repasse isDead = false après data.respawnDelay secondes. Sans ce nettoyage, un
        // pending-hit resté en vol (Mob/PNJ tué pendant l'anim de son attaque) résoudrait au
        // premier Update() après respawn — dégâts/zone/trajectoire fantômes depuis le point de
        // spawn (trouvé en vérification indépendante du plan).
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;
```

- [ ] **Step 7: Ajouter la garde de ré-entrée en tête de `TryUseSecondarySkill()`**

Trouver :
```csharp
    private bool TryUseSecondarySkill(Entity target)
    {
        if (data.skills == null || data.skills.Count == 0) return false;
```
Remplacer par :
```csharp
    private bool TryUseSecondarySkill(Entity target)
    {
        // Un pending-hit est déjà en vol (secondaire OU attaque de base) — ne rien redéclencher
        // tant qu'il n'est pas résolu. Retourne true pour que HandleCombatAI() traite ce tick
        // comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit) return true;
        if (data.skills == null || data.skills.Count == 0) return false;
```

- [ ] **Step 8: Remplacer le déclenchement dans `TryUseSecondarySkill()`**

Trouver (le corps de la boucle de déclenchement, jusqu'au `return true;` final de cette
itération — vérifier dans le fichier réel que `_attackTimer` est bien posé ici, immédiatement
après `_skillCooldowns[skill] = ...`, même structure que le bloc `Mob.TryUseSkill()` déjà migré
en Task 3 Step 7) :
```csharp
            LookAt(target.transform);
            if (skill.hasDelayedImpact)
                _skillSystem.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem.StartTrajectory(skill, this);
            else
                _skillSystem.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            return true;
```
Remplacer par :
```csharp
            LookAt(target.transform);
            StartPendingHit(skill, target);
            _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            return true;
```
(`_skillCooldowns[skill] = ...` disparaît d'ici — posé désormais dans `ResolvePendingHit()`.
`_attackTimer` reste posé ici, inchangé — NE PAS le supprimer, contrairement à
`_skillCooldowns[skill]`. Si le fichier réel a une forme légèrement différente de ce bloc,
conserver très précisément toute ligne qui n'est ni l'appel `PlantDelayedZone`/`StartTrajectory`/
`Execute` ni le posage `_skillCooldowns[skill] = ...` — en particulier `_attackTimer`.)

- [ ] **Step 9: Ajouter les 3 nouvelles méthodes, juste après `TryUseSecondarySkill()`**

```csharp

    /// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
    /// n'arrive qu'à l'event d'impact ou au timeout de secours. Sans attackAnimation assignée,
    /// résout immédiatement (comportement identique à avant ce sous-chantier). Un MultiHit SANS
    /// attackAnimation reste sur l'ancien chemin Execute()/ExecuteMultiHit (respecte
    /// HitStep.delay via coroutine) plutôt que d'entrer dans le pending-hit.</summary>
    private void StartPendingHit(SkillData skill, Entity target)
    {
        bool isMulti = skill.executionType == SkillExecutionType.MultiHit
                       && skill.hitSteps != null && skill.hitSteps.Count > 0;

        if (isMulti && skill.attackAnimation == null)
        {
            _skillSystem.Execute(skill, this, target);
            if (data.skills != null && data.skills.Contains(skill))
                _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            return;
        }

        _animatorController?.PlayAttack(skill.attackAnimation);

        _pendingSkill   = skill;
        _pendingTarget  = target;
        _pendingIsMulti = isMulti;
        _pendingMultiNextIndex = 0;
        _pendingTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingTimeout <= 0f)
            ResolvePendingHit(0);
    }

    /// <summary>Reçoit l'Animation Event relayé par PNJAnimatorController.OnSkillHitFrame.</summary>
    public void OnAnimationHitEvent(int hitIndex = 0)
    {
        if (_pendingSkill == null) return;
        ResolvePendingHit(hitIndex);
    }

    /// <summary>Résout le hit en attente — même branchement à 3 voies qu'avant ce
    /// sous-chantier, sauf routage MultiHit par index. Pose le cooldown du skill secondaire
    /// APRÈS résolution. Rejette un hitIndex hors séquence (event mal numéroté sur le clip —
    /// piège trouvé en task-review sur Mob.cs : Unity met souvent l'argument int par défaut à
    /// 0 sur chaque event d'un clip MultiHit si on oublie de le changer).</summary>
    private void ResolvePendingHit(int hitIndex)
    {
        if (_pendingSkill == null) return;

        SkillData skill   = _pendingSkill;
        Entity    target  = _pendingTarget;
        bool      isMulti = _pendingIsMulti;

        if (isMulti && hitIndex != _pendingMultiNextIndex)
        {
            Debug.LogWarning($"[PNJ] Animation Event MultiHit reçu avec hitIndex={hitIndex}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        if (isMulti)
        {
            _skillSystem.ResolveMultiHitStep(skill, this, target, hitIndex);
            _pendingMultiNextIndex++;
            int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
            if (_pendingMultiNextIndex < totalHits) return;
        }
        else
        {
            if (skill.hasDelayedImpact)
                _skillSystem.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem.StartTrajectory(skill, this);
            else
                _skillSystem.ResolveExecute(skill, this, target);
        }

        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }
```

- [ ] **Step 10: Vérifier la compilation**

Lire le fichier modifié en entier — confirmer accolades équilibrées, aucun `?.` introduit sur
`_skillSystem` dans le nouveau code, et que `SkillExecutionType`/`SkillData` sont déjà
importés/connus (déjà utilisés ailleurs dans ce fichier). Confirmer que `Die()` nettoie bien les
4 champs pending (Step 6), que `TryUseSecondarySkill()` retourne `true` immédiatement quand
`IsPendingHit` est vrai (Step 7), et que `_attackTimer` est toujours posé après
`StartPendingHit(skill, target)` dans `TryUseSecondarySkill()` (Step 8 — c'est la correction la
plus facile à rater, vérifier ligne par ligne).

- [ ] **Step 11: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: sync PNJ skill resolution on attack animation hit-frame"
```

---

## Task 5: Vérification manuelle Play Mode (Florian — non automatisable)

**Files:** aucun fichier de code — setup Editor + test en jeu.

**Interfaces:** consomme l'ensemble des Tasks 1-4 (fonctionnalité complète du sous-chantier).

Cette tâche n'est PAS dispatchable à un agent — elle nécessite l'Éditeur Unity ouvert et un
jugement visuel humain. Pré-requis avant de pouvoir tester quoi que ce soit :

- [ ] **Step 1: Setup Editor minimal (Florian)**

Sur un prefab Mob de test (ex: `Wolf.prefab`) ET un prefab PNJ `canFight` de test (ex: un Garde) :
1. Ajouter un composant `Animator` avec un `Animator Controller` assigné (au moins un state
   `Attack` dont la Motion est un clip placeholder dédié, distinct de tout autre clip).
2. Ajouter le composant `MobAnimatorController` (ou `PNJAnimatorController` pour le PNJ) sur le
   même GameObject, assigner `attackPlaceholderClip` = exactement le même clip que la Motion du
   state `Attack`.
3. Assigner un clip d'attaque réel sur au moins un `SkillData` (`basicAttackSkill` du Mob/PNJ de
   test, champ `attackAnimation` déjà existant) et poser un Animation Event sur ce clip, method
   `OnSkillHitFrame`, avec un `int` paramètre = 0 (hit simple) à la frame d'impact visuelle
   souhaitée.
4. Pour un test MultiHit : un `SkillData` avec `executionType = MultiHit` et au moins 1
   `hitSteps`, `attackAnimation` assignée, un Animation Event par hit (`int` = 0, 1, 2...) posé
   aux frames voulues sur le même clip.

- [ ] **Step 2: Exécuter la checklist de vérification du spec**

Reprendre les 10 points de la section « Vérification » de
`docs/superpowers/specs/2026-09-12-mob-pnj-animator-foundations-design.md` :
1. Compiler, 0 erreur.
2. Mob avec skill simple → dégâts pile à la frame d'event, pas à l'entrée en portée.
3. Même skill SANS `attackAnimation` → résolution immédiate (régression zéro).
4. Skill AVEC anim mais SANS event posé → résout à la fin du clip (garde-fou), pas de blocage.
5. Skill secondaire `hasDelayedImpact` → zone posée à la frame d'impact, CD démarre à ce
   moment-là.
6. Skill `isTrajectory` → trajectoire démarre à la frame d'impact.
7. Skill MultiHit → chaque hit résout à sa propre frame d'event, dans l'ordre.
8. PNJ `canFight` — mêmes vérifications 2-7 côté `TryUseSecondarySkill`/`HandleCombatAI`.
9. Tuer le Mob/PNJ pendant l'anim d'attaque (avant l'event) → pas de crash, pas d'effet
   après la mort.
10. PNJ non-`canFight` (Marchand, Forgeron) → aucune régression dialogue/boutique.
11. Garde de ré-entrée : sur une `attackAnimation` >1s, le clip joue jusqu'au bout sans
    redémarrer en boucle sur sa frame 0, la mana n'est pas drainée à chaque frame, un seul hit
    part au total.
12. PNJ tué pendant l'anim d'attaque, `respawnDelay` court → au respawn, aucun coup fantôme ne
    part depuis le point de spawn.
13. MultiHit SANS `attackAnimation` assignée → `HitStep.delay` toujours respecté entre les coups
    (comportement `ExecuteMultiHit` inchangé pour ce cas).

- [ ] **Step 3: Rapporter les résultats**

Si tout passe : signaler que le sous-chantier est prêt à être poussé. Si un point échoue,
signaler précisément lequel avec le comportement observé — pas de fix à l'aveugle, retour au
diagnostic sur le point précis en échec.

---

## Self-Review (effectué à l'écriture de ce plan)

**1. Spec coverage** : les 3 sections d'architecture du spec (Animator controllers, pending-hit
Mob, pending-hit PNJ) correspondent respectivement aux Tasks 1, 3, 4 ; Task 2 couvre le
symétrique PNJ de la Task 1 (le spec les présente ensemble en section 1, séparés ici pour
respecter l'indépendance de fichier). La section « Vérification » du spec est intégralement
reprise dans la Task 5. Aucune section du spec sans tâche correspondante.

**2. Placeholder scan** : aucun TBD/TODO — tout le code des 4 tâches est le code complet et
final (repris tel quel du spec), pas des esquisses.

**3. Type consistency** : `StartPendingHit(SkillData, Entity)`, `OnAnimationHitEvent(int
hitIndex = 0)`, `ResolvePendingHit(int)` ont la même signature dans Mob.cs (Task 3) et PNJ.cs
(Task 4). `MobAnimatorController.PlayAttack(AnimationClip)`/`PNJAnimatorController.PlayAttack(AnimationClip)` ont la même signature (Tasks 1-2), consommées identiquement dans
`StartPendingHit()` des deux entités. `SkillSystem.ResolveExecute`/`ResolveMultiHitStep`/
`PlantDelayedZone`/`StartTrajectory` sont utilisées avec les signatures exactes déjà vérifiées
dans le code réel (`Combat/SkillSystem.cs`, lignes ~175/541/265/336 au moment de l'écriture de
ce plan).
