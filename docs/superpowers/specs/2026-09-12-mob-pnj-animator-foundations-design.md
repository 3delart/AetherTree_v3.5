# Fondations Animator Mob/PNJ + calage sur l'anim (sous-chantier 1) — Design

## Contexte

Suite à la question de Florian "est-ce que tout est implémenté côté Mob/PNJ (skill, animation,
VFX) ?" : le chantier qui vient de se terminer (zone à impact différé + trajectoire mobile pour
Mob/PNJ) ne couvre que la logique de skill. Les VFX d'impact/zone/trajectoire (`vfxImpact`,
`vfxZoneMarker`, VFX de trajectoire qui suit) sont déjà opérationnels pour Mob/PNJ — ils sont
câblés dans `SkillSystem` lui-même, indépendamment du type de caster. Mais Mob/PNJ n'ont
**aucune animation** : vérifié dans le code réel, `Entities/Mob.cs` et `Entities/PNJ.cs` ne
contiennent aucune référence à `Animator`, et les prefabs de test actuels (`Wolf.prefab`,
`MannequinMoyen.prefab`, etc.) n'ont aucun composant `Animator`. Toute la mécanique d'animation
(idle, walk, patrol, attaque, canalisation) est à construire depuis zéro (confirmé par Florian).

Vu l'ampleur, le travail est découpé en deux sous-chantiers séquentiels (décidé avec Florian) :

1. **Ce sous-chantier — Fondations Animator Mob/PNJ** : locomotion (`Speed`), state `Attack`
   réutilisable avec `AnimatorOverrideController` (même pattern que le Player), ET la synchro
   dégâts-sur-frame-d'impact (« hit-frame-sync », équivalent du chantier B déjà livré pour le
   Player) — Florian a confirmé vouloir cette synchro dès cette fondation, pas en différé.
2. **Sous-chantier 2 (futur, pas dans ce spec)** : canalisation visible (state `Channel`,
   `CancelActionTrigger`, immobilisation/cast-bar) + `vfxCast` pour Mob/PNJ.

## Référence — le système Player existant (ne pas réinventer, transcrire fidèlement)

- `World/PlayerAnimatorController.cs` : `[RequireComponent(typeof(Animator))]`, enveloppe le
  `runtimeAnimatorController` dans un `AnimatorOverrideController` à l'`Awake()`, un seul state
  `Attack` réutilisable dont le clip est substitué à la volée via `_overrideController[attackPlaceholderClip] = clip` puis `_animator.Play(AttackState, 0, 0f)`. `Update()` pousse
  `Speed` (float, `agent.velocity.magnitude`) et `InCombat` (bool, spécifique Player — pas
  repris ici, voir Décisions). `OnSkillHitFrame(int hitIndex = 0)` est le point d'entrée de
  l'Animation Event, posé sur les clips d'attaque, qui relaie vers `SkillBar.Instance?.OnAnimationHitEvent(hitIndex)`.
- `Combat/SkillSystem.cs` expose déjà, depuis le chantier B (hit-frame-sync Player), deux
  méthodes **caster-agnostiques** (vérifié dans le code réel — elles ne branchent du code
  spécifique Player que dans un `if (caster.entityType == EntityType.Player)` interne, le reste
  est générique) :
  - `ResolveExecute(SkillData skill, Entity caster, Entity target)` (ligne ~175) — dispatch +
    VFX/son pour un hit simple, sans relancer d'anim ni gérer le MultiHit.
  - `ResolveMultiHitStep(SkillData skill, Entity caster, Entity target, int hitIndex)`
    (ligne ~541) — résout UN hit précis d'une séquence MultiHit (`hitIndex` 0 = coup de base,
    1..N = `hitSteps[hitIndex-1]`).
  - Les deux commencent par `if (skill == null || caster == null || caster.isDead) return;` —
    un appel tardif sur un caster déjà mort no-op proprement, sans état à nettoyer explicitement.
  - **Conséquence directe : aucune modification de `SkillSystem.cs` n'est nécessaire pour ce
    sous-chantier.** Mob/PNJ peuvent appeler `ResolveExecute`/`ResolveMultiHitStep` directement,
    exactement comme `SkillBar` le fait déjà pour le Player.
- `Data/Skills/SkillBar.cs` (chantier B, déjà en prod) : pattern `_pendingHitSlot`/
  `_pendingHitSkill`/`_pendingHitTarget`/`_pendingHitTimeout` (+ équivalent `_pendingMulti*`
  pour le MultiHit par index) — posé au lancement, résolu par `OnAnimationHitEvent` (event) ou
  par un tick de timeout dans `Update()` (garde-fou anti-blocage si l'event a été oublié sur le
  clip). `timeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f` — pas
  d'anim = résolution immédiate, comme avant le chantier B.

## Décisions de design (issues du brainstorm)

| Question | Décision |
|---|---|
| Script partagé Mob/PNJ ou deux scripts séparés ? | **Deux scripts séparés** (`MobAnimatorController.cs`, `PNJAnimatorController.cs`) — Florian : "je pense deux séparer... mais oui, ils sont aussi quasi identique au player". Mob.cs/PNJ.cs sont déjà deux classes séparées sans base de comportement commune ; cohérent de garder la même séparation ici plutôt que d'introduire un composant partagé nouveau. |
| Hit-frame-sync dès cette fondation, ou différé ? | **Dès maintenant.** Florian : "Hit-frame sync dès maintenant". |
| MultiHit inclus dans la synchro ? | **Oui, comme le Player, sauf le Combo** (Mob/PNJ n'ont pas de mécanique de combo IA). Florian : "pareil que le player sauf le combo". |
| Paramètre `InCombat` (variante armée/désarmée) comme le Player ? | **Non — YAGNI.** Aucune mécanique d'équipement/dégainage visible sur Mob/PNJ aujourd'hui ; ajouter ce paramètre sans state Animator qui l'utilise serait du code mort. |
| `CancelActionTrigger`/state `Channel` dans ce sous-chantier ? | **Non — sous-chantier 2.** Même séquencement que le Player (chantier A a ajouté Channel/le trigger, avant même le hit-frame-sync du chantier B) — ici on ne touche que la fondation Attack. |
| CD/GCD posé au déclenchement de l'anim ou à la résolution ? | **À la résolution**, même principe déjà appliqué au Player (chantier B) : sans ça, un skill à cooldown court + anim longue verrait son CD quasi épuisé avant même que le coup parte. Trouvé en relisant le code réel de `Mob.HandleAttack()`/`TryUseSkill()`/`PNJ.HandleCombatAI()`/`TryUseSecondarySkill()` : aujourd'hui `_skillCooldowns[skill] = ...`/`attackTimer = data.attackCooldown` sont posés **immédiatement** au déclenchement — ce sous-chantier déplace ce posage vers le callback de résolution. |
| Garde de ré-entrée pendant qu'un pending-hit est en vol ? | **Obligatoire — trouvé en vérification indépendante du plan, absent du premier jet.** Déplacer le CD à la résolution retire la SEULE protection qui empêchait aujourd'hui `TryUseSkill()`/`TryUseSecondarySkill()`/le bloc d'attaque de base de se redéclencher à CHAQUE frame tant que l'anim joue (`_skillCooldowns`/`attackTimer` ne sont plus réarmés avant la résolution). Sans garde, ceci provoque un vrai bug : mana vidée frame par frame, `Animator.Play(AttackState, 0, 0f)` relancé sur sa frame 0 en boucle, l'event d'impact jamais atteint. Fix : `IsPendingHit` (déjà prévu comme champ public) doit être consommé — `if (IsPendingHit) return true;` en tête de `TryUseSkill()`/`TryUseSecondarySkill()`, et `&& !IsPendingHit` ajouté à la condition du bloc d'attaque de base dans `HandleAttack()`/`HandleCombatAI()` (ce dernier point est nécessaire séparément car le chemin Taunt court-circuite l'appel à `TryUseSkill()`/`TryUseSecondarySkill()`, contournant sinon la garde). |
| MultiHit sans `attackAnimation` assignée — reste sur l'ancien chemin ou entre dans le pending-hit ? | **Reste sur l'ancien chemin `SkillSystem.Execute()`/`ExecuteMultiHit()` (coroutine).** Trouvé en vérification indépendante : sans anim, il n'y a pas de frame d'impact à attendre — faire entrer ce cas dans le pending-hit ferait perdre le respect de `HitStep.delay` entre les coups (la coroutine `ExecuteMultiHit` honore ce délai via `WaitForSeconds`, le pending-hit n'a rien d'équivalent). `StartPendingHit()` détecte ce cas (`isMulti && skill.attackAnimation == null`) et route directement vers `Execute()` avant même de poser l'état pending. |
| Mort en vol (caster tué pendant que son anim d'attaque joue) | **Aucune annulation explicite nécessaire pour `Mob`** — `ResolveExecute`/`PlantDelayedZone`/`StartTrajectory`/`ResolveMultiHitStep` gardent tous déjà `caster.isDead` en tête de méthode, et `Mob.Update()` s'arrête via `this.enabled = false` posé par `Die()` (le pending abandonné n'est jamais relu). **Faux pour `PNJ` — trouvé en vérification indépendante du plan.** `PNJ.Die()` NE désactive PAS le composant (`isDead = true` seulement) et `PNJ.RespawnCoroutine()` repasse `isDead = false` après `data.respawnDelay` secondes sans jamais nettoyer un pending-hit resté en vol — un PNJ tué pendant l'anim de son attaque respawnerait avec un pending-hit fantôme, résolu au premier `Update()` après respawn (dégâts/zone/trajectoire déclenchés depuis le point de spawn). **Fix : `PNJ.Die()` doit nettoyer `_pendingSkill`/`_pendingTarget`/`_pendingTimeout`/`_pendingIsMulti`** (juste après `_combatTarget = null;`, avant le `if (data.respawnDelay > 0f)`). |

## Architecture

### 1. `World/MobAnimatorController.cs` (nouveau) et `World/PNJAnimatorController.cs` (nouveau)

Même mécanisme, dupliqué volontairement dans les deux fichiers (décision ci-dessus) :

```csharp
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Animator))]
public class MobAnimatorController : MonoBehaviour
{
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans l'Animator " +
             "Controller (un placeholder dédié, jamais réutilisé) — AnimatorOverrideController " +
             "indexe par référence au clip D'ORIGINE, pas par nom de state.")]
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
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.TryUseSkill()/HandleAttack().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (hit simple ou coup de base MultiHit), 1..N
    /// pour les hitSteps. Relais pur vers l'instance locale (pas de singleton comme SkillBar —
    /// chaque Mob résout son propre pending-hit).</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _mob?.OnAnimationHitEvent(hitIndex);
    }
}
```

`PNJAnimatorController.cs` : code identique, `Mob _mob` → `PNJ _pnj`, `GetComponent<Mob>()` →
`GetComponent<PNJ>()`, classe renommée `PNJAnimatorController`. Pas de
`[RequireComponent(typeof(NavMeshAgent))]` dans aucun des deux scripts — un PNJ non-`canFight`
n'a pas de `NavMeshAgent` (confirmé dans `PNJ.Awake()`, ligne ~122 : `_agent` n'est assigné que
`if (data != null && data.canFight)`), donc `_agent` doit rester nullable et géré comme tel
(déjà le cas dans le code ci-dessus, même garde que `PlayerAnimatorController`).

Pas de `ResetTrigger(CancelActionTrigger)` dans `PlayOverrideClip` ici (contrairement au
Player) — ce trigger n'existe pas encore côté Mob/PNJ, il arrive avec le state `Channel` au
sous-chantier 2.

### 2. `Entities/Mob.cs` — pending-hit + branchement 3 voies déplacé à la résolution

**Nouveaux champs** (à côté des champs existants `attackTimer`/`_skillCooldowns`) :

```csharp
// ── Attente de résolution (hit-frame-sync — un seul hit en vol à la fois, Mob n'agit
// jamais en parallèle sur deux skills) ──────────────────────────────────────────────
private SkillData _pendingSkill   = null;
private Entity    _pendingTarget  = null;
private float     _pendingTimeout = 0f;
private bool      _pendingIsMulti = false;
private int       _pendingMultiNextIndex = 0;

private MobAnimatorController _animatorController;

public bool IsPendingHit => _pendingSkill != null;
```

`Awake()` (ligne 71-78) : ajouter `_animatorController = GetComponent<MobAnimatorController>();`
(nullable — un prefab de test sans Animator encore configuré ne doit pas planter, même
tolérance que le Player avec `AnimatorController?.`).

**`HandleAttack()`** (ligne 331-373) — remplacer le bloc d'attaque de base :

```csharp
// Bloqué tant qu'un pending-hit est en vol — voir Décisions ci-dessus ("Garde de ré-entrée").
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

(`attackTimer` continue d'être reposé immédiatement, il joue le rôle de "délai avant de
retenter une attaque", pas un vrai cooldown de skill — pas concerné par la règle "CD à la
résolution" qui vise `_skillCooldowns` pour les skills secondaires ci-dessous. Le `&&
!IsPendingHit` ajouté à la condition englobante EST le fix de la garde de ré-entrée : sans lui,
une fois `_skillCooldowns`/le CD déplacés à la résolution, plus rien n'empêche ce bloc de se
redéclencher à chaque frame pendant l'anim.)

**`TryUseSkill()`** (ligne 384-420) — ajouter la garde de ré-entrée en tête de méthode, puis
remplacer le corps de la boucle de déclenchement :

```csharp
private void TryUseSkill(Entity target)   // signature réelle : private bool TryUseSkill(...)
{
    // Un pending-hit est déjà en vol — ne rien redéclencher tant qu'il n'est pas résolu.
    // Retourne true pour que HandleAttack()/HandleChase() traitent ce tick comme "occupé".
    if (IsPendingHit) return true;
    if (data.skills == null || data.skills.Count == 0) return false;

    // ... tick des cooldowns inchangé ...

    foreach (var skill in data.skills)
    {
        if (skill == null) continue;
        float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
        if (cd > 0f) continue;
        if (!IsInRange(target, skill.range)) continue;
        if (skill.manaCost > 0f && !HasMana(skill.manaCost)) continue;

        if (skill.manaCost > 0f) SpendMana(skill.manaCost);

        LookAt(target.transform);
        StartPendingHit(skill, target);
        attackTimer = data.attackCooldown;
        return true;
    }

    return false;
}
```

Le posage de `_skillCooldowns[skill] = ...` disparaît d'ici — déplacé dans `ResolvePendingHit()`
ci-dessous (règle "CD à la résolution"). `attackTimer` reste posé ici, inchangé.

**Nouvelles méthodes** (section "SKILL", après `TryUseSkill()`) :

```csharp
/// <summary>Déclenche l'anim d'attaque et pose l'état pending — la résolution réelle
/// (dégâts/effets/zone/trajectoire) n'arrive qu'à l'event d'impact ou au timeout de secours,
/// jamais ici. Sans attackAnimation assignée, résout immédiatement (comportement identique
/// à avant ce sous-chantier). Un MultiHit SANS attackAnimation reste sur l'ancien chemin
/// Execute()/ExecuteMultiHit (coroutine, respecte HitStep.delay) — il n'y a pas de frame
/// d'impact à attendre, entrer dans le pending-hit ferait perdre ce délai entre coups.</summary>
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
/// sous-chantier (hasDelayedImpact / isTrajectory / dispatch standard), sauf routage MultiHit
/// par index. Pose le cooldown du skill secondaire APRÈS résolution (pas au déclenchement).</summary>
private void ResolvePendingHit(int hitIndex)
{
    if (_pendingSkill == null) return;

    SkillData skill  = _pendingSkill;
    Entity    target = _pendingTarget;
    bool      isMulti = _pendingIsMulti;

    if (isMulti)
    {
        _skillSystem?.ResolveMultiHitStep(skill, this, target, hitIndex);
        _pendingMultiNextIndex++;
        int totalHits = 1 + (skill.hitSteps?.Count ?? 0);
        if (_pendingMultiNextIndex < totalHits) return;   // encore des hits à venir, ne pas clore le pending
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
    // Player. Ne concerne que les skills secondaires (data.skills) — l'attaque de base utilise
    // attackTimer, déjà reposé au déclenchement dans HandleAttack() (voir commentaire là-bas).
    if (data.skills != null && data.skills.Contains(skill))
        _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
}
```

**`Update()`** (ligne 132) — ajouter le tick de timeout, AVANT les early-return de CC (même
principe que le Player : un coup déjà lancé va au bout, non-interruptible) :

```csharp
protected override void Update()
{
    base.Update();
    if (isDead || data == null) return;

    attackTimer -= Time.deltaTime;

    if (_pendingSkill != null)
    {
        _pendingTimeout -= Time.deltaTime;
        if (_pendingTimeout <= 0f)
            ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
    }

    // ... reste de la méthode inchangé
```

### 3. `Entities/PNJ.cs` — même pattern, adapté au style existant du fichier (pas de `?.` sur `_skillSystem`, guard déjà en tête de `HandleCombatAI()`)

**Nouveaux champs**, `Awake()` : `_animatorController = GetComponent<PNJAnimatorController>();`
(nullable, même tolérance).

**`HandleCombatAI()`** (ligne 490-547) — remplacer le bloc d'attaque de base (ligne 529-541) :

```csharp
// Bloqué tant qu'un pending-hit est en vol — même raison que Mob.HandleAttack().
if (_attackTimer <= 0f && !IsPendingHit && data.basicAttackSkill != null)
{
    if (!isDead && !_combatTarget.isDead)
        StartPendingHit(data.basicAttackSkill, _combatTarget);
    _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
}
```

**`TryUseSecondarySkill()`** (ligne 553+) — ajouter la garde de ré-entrée en tête de méthode
(`if (IsPendingHit) return true;`, même pattern que `Mob.TryUseSkill()`), puis remplacer le
déclenchement de la même façon (retirer le posage de `_skillCooldowns[skill]` du point de
déclenchement, appeler `StartPendingHit(skill, target)` — **`_attackTimer` reste posé après**,
ne pas le supprimer par erreur en même temps que `_skillCooldowns[skill]`).

**`Die()`** — la seule différence structurelle avec Mob (voir Décisions, ligne "Mort en vol") :
`PNJ.Die()` ne désactive jamais le composant, donc un pending-hit doit être explicitement
nettoyé, juste après `_combatTarget = null;` :

```csharp
_pendingSkill   = null;
_pendingTarget  = null;
_pendingTimeout = 0f;
_pendingIsMulti = false;
```

**Nouvelles méthodes** `StartPendingHit`/`OnAnimationHitEvent`/`ResolvePendingHit` — code
identique à Mob.cs (y compris le détour MultiHit-sans-anim vers `Execute()`), sans `?.` sur
`_skillSystem` (idiome déjà établi dans ce fichier, garanti non-null par `HandleCombatAI()`).

**`Update()`** (ligne 134-141) — tick du timeout AVANT le `if (isDead) return;` n'a pas de sens
ici puisque `HandleCombatAI()` lui-même est déjà gardé par `isDead`/`canFight` — le tick de
pending-hit s'insère simplement dans `Update()`, avant l'appel à `HandleCombatAI()` :

```csharp
protected override void Update()
{
    base.Update();
    if (isDead) return;

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

## Cas limites vérifiés

- **Skill sans `attackAnimation` assignée** : `_pendingTimeout <= 0f` immédiatement →
  `ResolvePendingHit()` appelée sur-le-champ dans `StartPendingHit()` — comportement
  strictement identique à avant ce sous-chantier (résolution instantanée).
- **Event Animation posé mais jamais reçu** (oubli d'authoring) : le timeout `Update()` résout
  quand même à la fin du clip — même garde-fou que le Player, pas de blocage permanent de l'IA.
- **MultiHit — event mal numéroté** : `ResolvePendingHit(hitIndex)` ne vérifie pas que
  `hitIndex` correspond exactement à `_pendingMultiNextIndex` avant de résoudre (contrairement à
  `SkillBar.ResolveMultiHitIndex` qui rejette un index hors séquence) — accepté comme
  simplification volontaire pour ce sous-chantier : Mob/PNJ n'ont qu'un seul clip d'attaque par
  skill, et la garde de ré-entrée (`IsPendingHit`, voir Décisions) empêche désormais tout
  second appel à `StartPendingHit` tant qu'un pending est en vol — un vrai verrou anti-index
  hors séquence (équivalent `SkillBar.ResolveMultiHitIndex`) serait sur-ingénierie tant qu'aucun
  symptôme réel n'a été observé sur un simple oubli/décalage d'authoring d'events.
- **Re-déclenchement pendant qu'un pending-hit est en vol** : voir Décisions ci-dessus ("Garde
  de ré-entrée") — un vrai bug trouvé en vérification indépendante du plan (mana vidée, anim
  jamais avancée), corrigé par `IsPendingHit` consommé explicitement dans `TryUseSkill()`/
  `TryUseSecondarySkill()` ET dans le bloc d'attaque de base.
- **MultiHit sans `attackAnimation`** : voir Décisions ci-dessus — route directement vers
  `SkillSystem.Execute()` (préserve `HitStep.delay`), n'entre jamais dans le pending-hit.
- **Mort pendant l'anim d'attaque** : voir table des décisions ci-dessus — no-op propre pour
  `Mob` via les gardes déjà existantes de `SkillSystem` + `this.enabled = false`. Pour `PNJ`,
  nettoyage EXPLICITE requis dans `Die()` (voir ci-dessus et Décisions) — sans lui, un pending
  survit au respawn et se résout en pending fantôme.
- **PNJ non-`canFight`** : `_animatorController`/`_agent` restent nullables, jamais appelés
  puisque `HandleCombatAI()` n'est jamais invoquée (`data.canFight` false) — aucun changement de
  comportement pour les PNJ purement dialogue/boutique.

## Fichiers touchés

- `World/MobAnimatorController.cs` (nouveau)
- `World/PNJAnimatorController.cs` (nouveau)
- `Entities/Mob.cs` — nouveaux champs pending-hit, `Awake()` (cache `_animatorController`),
  `HandleAttack()`, `TryUseSkill()`, nouvelles méthodes `StartPendingHit`/`OnAnimationHitEvent`/
  `ResolvePendingHit`, `Update()` (tick timeout).
- `Entities/PNJ.cs` — mêmes catégories de changement, adaptées au style sans `?.`.

Aucun changement `Combat/SkillSystem.cs` (déjà prêt, `ResolveExecute`/`ResolveMultiHitStep`
existants et caster-agnostiques), aucun changement `Data/Skills/SkillData.cs` (les champs
`attackAnimation`/`hasDelayedImpact`/`isTrajectory`/`executionType`/`hitSteps` existent déjà,
juste jamais consommés côté Mob/PNJ jusqu'ici). Aucun nouvel ordinal, aucun risque de casse de
save.

## Hors scope (rappel)

- Canalisation visible (state `Channel`, `CancelActionTrigger`, cast-bar/immobilisation) —
  sous-chantier 2.
- `vfxCast` Mob/PNJ — sous-chantier 2.
- Locomotion au-delà du seul paramètre `Speed` (states Patrol/Chase/Idle/Walk distincts dans le
  graphe Animator — pur travail Editor de Florian, aucune logique C# supplémentaire requise
  pour un blend continu piloté par `Speed`).
- Interruption d'une attaque Mob/PNJ en cours (pas de mécanique d'interrupt IA aujourd'hui).
- Combo pour Mob/PNJ (n'existe pas, hors scope permanent sauf demande future explicite).

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian, une fois les
Animator Controllers configurés et au moins un clip d'attaque avec Animation Event posé sur un
Mob et un PNJ `canFight` :

1. Compiler, 0 erreur.
2. Mob avec `basicAttackSkill` simple (pas de `hasDelayedImpact`/`isTrajectory`/MultiHit) :
   l'anim d'attaque joue, les dégâts partent pile à la frame de l'event, pas au moment où le Mob
   entre en portée.
3. Même skill mais SANS `attackAnimation` assignée sur le `SkillData` → résolution immédiate,
   comme avant ce sous-chantier (régression zéro).
4. Skill AVEC anim mais SANS event posé dessus (test volontaire) → résout quand même à la fin du
   clip (garde-fou), le Mob ne reste jamais bloqué.
5. Skill secondaire (`data.skills`) avec `hasDelayedImpact = true` → la zone se pose à la frame
   d'impact de l'anim, pas au moment du clic/déclenchement IA ; le cooldown du skill démarre à
   ce moment-là, pas avant.
6. Skill `isTrajectory = true` → même vérification, la trajectoire démarre à la frame d'impact.
7. Skill MultiHit (`executionType = MultiHit`, `hitSteps` non vide) sur un Mob de test — chaque
   hit (coup de base + chaque hitStep) résout à SA PROPRE frame d'event, dans l'ordre.
8. PNJ `canFight` (ex: Garde) — même vérifications 2-7 côté `TryUseSecondarySkill`/
   `HandleCombatAI`.
9. Tuer le Mob/PNJ PENDANT que son anim d'attaque joue (avant l'event) → pas de crash, pas
   d'effet appliqué après la mort (`caster.isDead` guard côté `SkillSystem`).
10. PNJ non-`canFight` (Marchand, Forgeron) — aucune régression, comportement dialogue/boutique
    inchangé.
11. **Garde de ré-entrée** (trouvé en vérification indépendante du plan) : sur un Mob/PNJ avec un
    `attackAnimation` assez longue (>1s), observer que le clip joue jusqu'au bout sans redémarrer
    en boucle sur sa frame 0, que la mana n'est PAS drainée à chaque frame pendant l'anim, et
    qu'un seul hit part au total (pas un par frame).
12. **PNJ tué pendant l'anim d'attaque, avec `respawnDelay` court** : attendre le respawn →
    vérifier qu'AUCUN coup fantôme ne part au moment du respawn (pas de dégâts/zone/trajectoire
    surgissant depuis le point de spawn sans action du joueur).
13. **MultiHit sans `attackAnimation` assignée** : vérifier que `HitStep.delay` est toujours
    respecté entre les coups (comportement du coroutine `ExecuteMultiHit` d'avant ce
    sous-chantier, inchangé pour ce cas précis).
