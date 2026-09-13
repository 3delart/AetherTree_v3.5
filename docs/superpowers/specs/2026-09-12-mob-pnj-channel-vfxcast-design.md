# Canalisation visible + vfxCast Mob/PNJ (sous-chantier 2) — Design

## Contexte

Suite du chantier "animation de combat Mob/PNJ" — le sous-chantier 1 (Fondations Animator +
hit-frame-sync, commits `62fcf23..c4dea34`, code complet et review finale clean, pas encore
testé en jeu par Florian) a donné à Mob/PNJ un state `Attack` réutilisable + une synchro
dégâts-sur-frame-d'impact. Explicitement laissé de côté pour ce sous-chantier 2 : la
canalisation visible (`castTime > 0`, aujourd'hui totalement ignorée côté Mob/PNJ — `TryUseSkill`/
`HandleAttack`/`TryUseSecondarySkill`/`HandleCombatAI` ne lisent jamais `skill.castTime`) et le
spawn de `vfxCast` (existe déjà comme champ `SkillData`, câblé uniquement côté `SkillBar`
= Player).

Décidé avec Florian : enchaîner l'implémentation directement, sans attendre son test manuel du
sous-chantier 1 (risque accepté explicitement).

## Référence — le système Player existant (transcrire fidèlement, pas réinventer)

Lu en entier dans le code réel avant d'écrire ce spec :

- **`World/PlayerAnimatorController.cs`** : contrairement à une hypothèse initiale corrigée
  pendant ce brainstorm, il n'y a **PAS de state Animator séparé pour la canalisation**. Le
  Player réutilise le **MÊME state `Attack`** pour l'attaque instantanée ET la canalisation —
  `PlayChannel(AnimationClip clip) => PlayOverrideClip(clip);` est un simple alias de
  `PlayAttack()`, cohérent puisque les deux ne jouent jamais en même temps. Seul ajout
  nécessaire pour la canalisation : le trigger `CancelAction` (déjà présent, déjà utilisé par
  `PlayOverrideClip()` pour se protéger d'un trigger orphelin) + `CancelChannel()` qui le
  déclenche pour couper l'anim net.
- **`Data/Skills/SkillBar.cs`** — `StartChannel()` (ligne 680) : `BeginSkillUse` → engage/face
  cible → dépense mana/HP/or → pose l'état (`_isChanneling`, `_channelSkill`, `_channelTarget`,
  etc.) → `PlayChannel(skill.channelAnimation)` → spawn `vfxCast`
  (`Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity)`, gardé dans
  `_channelVfxCast`) → `ProgressBarUI.Instance?.StartProgress(duration: skill.castTime,
  onComplete: ResolveChannel, onCancel: () => InterruptChannel(voluntary: true, ...), type:
  Cast, followTarget: _player.transform)`.
- **`ResolveChannel()`** (ligne 736) : capture skill/target AVANT `EndChannelState()` (qui les
  null), puis branchement à 3 voies **avec `Execute()`** pour le cas standard (PAS
  `ResolveExecute()`) — couvre nativement un skill MultiHit+castTime via la coroutine
  `ExecuteMultiHit` déjà existante dans `Execute()`, sans dupliquer de mécanisme d'index.
- **`InterruptChannel(bool voluntary, string reason)`** (ligne 765) : `EndChannelState()` (null
  l'état + détruit `_channelVfxCast`) → `ProgressBarUI.Instance?.Cancel()` →
  `AnimatorController?.CancelChannel()` → CD = `voluntary ? cooldown*0.5f : cooldown` (CD demi
  si interrompu par un motif "non punitif" — mouvement, cible morte ; CD complet si CC/mort
  subie).
- **Poll d'interrupt** (`Update()`, ligne ~175) : `hardCC = isDead || isStunned || isShocked ||
  isFreezed || isKnockedBack || isFeared || isSilenced` → interrupt non-volontaire ; cible morte
  → interrupt volontaire ; mouvement au-delà d'un seuil → interrupt volontaire. **Confirmé avec
  Florian : les dégâts simples n'interrompent PAS une canalisation, seul un hard CC le fait — et
  c'est déjà la règle réelle du Player aujourd'hui** (vérifié dans le code, pas une nouveauté
  introduite ici).
- **`UI/PanelSecondaire/ProgressBarUI.cs`** — barre world-space déjà existante : Canvas World
  Space instancié dynamiquement, suit une `Transform` (`followTarget`), toujours face caméra,
  `BarType.Cast` déjà réservé (couleur violette `colorCast`). **Limite trouvée en exploration** :
  singleton à UN SEUL slot actif (`_instance`/`_activeCoroutine` uniques) — utilisé aujourd'hui
  par canalisation Player + harvest + craft. Plusieurs Mob/PNJ canalisant en même temps (ou un
  Mob canalisant pendant que le Player harvest/craft/canalise) s'écraseraient mutuellement.
  **Décidé avec Florian : nouveau spawner dédié, une instance par canalisation active, pas de
  refactor du singleton existant** (zéro risque de régression sur harvest/craft/canalisation
  Player qui marchent déjà).

## Décisions de design (issues du brainstorm)

| Question | Décision |
|---|---|
| Enchaîner l'implémentation sans attendre le test du sous-chantier 1 ? | **Oui**, décidé explicitement par Florian, risque accepté. |
| Cast-bar : refactor `ProgressBarUI` en multi-instance, ou nouveau spawner dédié ? | **Nouveau spawner dédié** (`World/CastBarSpawner.cs`) — une instance par canalisation active, pas un singleton. Réutilise le MÊME prefab visuel que `ProgressBarUI` (lu via `ProgressBarUI.Instance.progressBarPrefab`/`colorCast`/`heightOffset` — pas de nouvelle assignation Inspector requise) sans toucher au système Player/harvest/craft existant. |
| Script Animator partagé Mob/PNJ pour la partie Channel, ou étendre les 2 scripts séparés du sous-chantier 1 ? | **Étendre les 2 scripts séparés** (`MobAnimatorController.cs`/`PNJAnimatorController.cs`) — cohérent avec la décision déjà prise au sous-chantier 1 de les garder séparés. |
| `CastBarSpawner` partagé Mob/PNJ ou dupliqué ? | **Partagé** — contrairement aux Animator Controllers (qui doivent `GetComponent<Mob>()`/`GetComponent<PNJ>()` pour relayer l'event d'impact), une barre de cast n'a besoin de rien de spécifique à Mob ou PNJ : juste un `Transform` à suivre, une durée, deux callbacks. Aucune raison de dupliquer. |
| Interruption sur dégâts simples ou seulement hard CC ? | **Seulement hard CC** (+ mort du caster, + cible morte) — même règle que le Player aujourd'hui, confirmée par Florian pour les 3 types de caster uniformément. |
| Résolution via `Execute()` ou `ResolveExecute()` ? | **`Execute()`**, comme le Player — couvre nativement MultiHit+castTime sans nouveau mécanisme d'index, et Mob/PNJ n'ont pas besoin du split Execute/ResolveExecute (ce split existe côté Player uniquement pour éviter de relancer `PlayAttack` par-dessus l'anim déjà jouée — non pertinent ici puisque `Execute()` ne touche jamais l'Animator). |
| Immobilisation explicite pendant la canalisation ? | **RENVERSÉ après test manuel — code ajouté.** Décision d'origine ("pas de code ajouté") reposait sur une justification déjà trouvée fausse en review finale (2 cas où le Mob/PNJ continuait à bouger en pleine canalisation). Florian a ensuite explicitement demandé le blocage après l'avoir vu en jeu. Fix : `HandleChase()`/`HandleAttack()` (Mob) et `HandleCombatAI()` (PNJ) gèlent tout mouvement/transition d'état dès que `_isChanneling` est vrai (juste après le check de leash, qui continue lui de tourner pour pouvoir interrompre proprement un désengagement) — le poll d'interrupt CC/cible-morte dans `Update()` reste indépendant de ce gel. |
| Un skill à canalisation peut-il aussi avoir `hasDelayedImpact`/`isTrajectory` ? | **Oui, même branchement 3 voies que l'existant** (résolution après le timer de canalisation plutôt qu'après une frame d'anim) — aucun changement de logique de zone/trajectoire elles-mêmes, seulement le déclencheur. |
| Garde de ré-entrée pendant qu'une canalisation est en vol ? | **Obligatoire — trouvé en vérification indépendante du plan, absent du premier jet.** `IsPendingHit` (sous-chantier 1) ne couvre PAS `_isChanneling` — sans un garde dédié, `TryUseSkill()`/`TryUseSecondarySkill()` et le bloc d'attaque de base redéclencheraient `StartChannelCast()` À CHAQUE frame pendant toute la canalisation (mana vidée en boucle, `vfxCast`/`CastBarSpawner` instanciés en boucle sans jamais détruire les précédents, anim relancée sur sa frame 0 sans arrêt). Fix : `if (IsPendingHit || _isChanneling) return true;` en tête de `TryUseSkill()`/`TryUseSecondarySkill()`, et `&& !_isChanneling` ajouté (en plus de `!IsPendingHit`) à la condition du bloc d'attaque de base. |
| Ordre `EndChannelCastState()` / `CastBarSpawner.Cancel()` dans `InterruptChannelCast()` ? | **`EndChannelCastState()` D'ABORD, puis `bar.Cancel()`** — trouvé en vérification indépendante du plan : l'ordre inverse (bar annulée avant que l'état soit nettoyé) fait que le callback `onCancel` de la bar (`() => InterruptChannelCast(voluntary: true, ...)`) s'exécute pendant que `_isChanneling` est encore vrai, donc CE callback réentrant fait tout le travail (CD demi posé) avant que l'appel externe (`voluntary: false` sur un hard CC) n'atteigne son propre `if (!_isChanneling) return;` et ne no-op — **un hard CC finirait TOUJOURS avec le CD demi au lieu du CD complet, silencieusement**. `InterruptChannelCast()` doit capturer `_channelBar` dans une variable locale, appeler `EndChannelCastState()` (qui met `_isChanneling = false`), PUIS appeler `bar?.Cancel()` — le callback `onCancel` réentrant tombe alors sur le garde `!_isChanneling` et no-op proprement, exactement comme `SkillBar.InterruptChannel()` le fait déjà côté Player (`EndChannelState()` avant `ProgressBarUI.Instance?.Cancel()`). **Conséquence : aucun appel externe à `_channelBar?.Cancel()` ne doit exister ailleurs** (pas dans les polls `Update()`, pas dans `GoReturn()`/le leash PNJ/`Die()`) — seul `InterruptChannelCast()` possède ce droit, tous les autres appelants appellent simplement `InterruptChannelCast(...)` et laissent CETTE méthode gérer la bar. |
| `isSleeping` inclus dans le poll hard-CC ? | **Non — parité volontaire avec le Player.** `SkillBar.cs` ne l'inclut pas non plus dans son propre poll de canalisation, malgré la note ailleurs dans `Mob.cs` (GDD §3.1.1.1, "Sleep bloque TOUTES les actions") qui l'inclut dans le gel générique `isCCd` de `Update()`. Un Mob endormi pendant sa canalisation la termine donc normalement (l'early-return CC générique de `Update()` intervient APRÈS le poll de canalisation, pas avant). Accepté comme transcription fidèle du système Player existant, pas une omission — à corriger dans les deux systèmes ensemble si jamais jugé nécessaire, pas seulement côté Mob/PNJ. |
| Nettoyage de canalisation dans `Mob.Die()` ? | **Requis — trouvé en vérification indépendante du plan.** Le poll `hardCC` dans `Update()` inclut `isDead`, mais `Update()` a déjà un early-return `if (isDead || data == null) return;` AVANT ce poll — donc la branche `isDead` du poll est du code mort, jamais atteinte. Contrairement à PNJ (qui a déjà un nettoyage explicite dans `Die()`), Mob n'en avait aucun prévu. Sans lui, un Mob tué en pleine canalisation garde sa barre/son `vfxCast`/son anim affichés pendant tout le reste de `castTime`, et `ResolveChannelCast()` peut s'exécuter sur un Mob mort (sans effet grâce aux gardes `caster.isDead` déjà dans `SkillSystem`, mais visuellement incorrect). `Mob.Die()` doit appeler `InterruptChannelCast(voluntary: false, reason: "mort")` si `_isChanneling`, juste avant `this.enabled = false;`. |

## Architecture

### 1. `World/MobAnimatorController.cs` / `World/PNJAnimatorController.cs` — extension

Ajouter le trigger et les 2 méthodes, exactement comme le Player :

```csharp
private const string CancelActionTrigger = "CancelAction";
```

Dans `PlayOverrideClip(AnimationClip clip)`, ajouter la même protection que le Player (évite un
trigger `CancelAction` orphelin de coincer une future anim) :

```csharp
_overrideController[attackPlaceholderClip] = clip;
_animator.ResetTrigger(CancelActionTrigger);   // ← nouvelle ligne
_animator.Play(AttackState, 0, 0f);
```

Nouvelles méthodes publiques (le state réutilisé reste `Attack` — pas de nouveau state Animator
à créer, confirmé par la lecture du Player) :

```csharp
/// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
/// d'échange que PlayAttack (réutilise le state "Attack"). Appelé par Mob.StartChannelCast()
/// (resp. PNJ).</summary>
public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip);

/// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
/// retour à la locomotion. Appelé par Mob.InterruptChannelCast() (resp. PNJ).</summary>
public void CancelChannel()
{
    if (_animator == null) return;
    _animator.SetTrigger(CancelActionTrigger);
}
```

### 2. `World/CastBarSpawner.cs` (nouveau, partagé)

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// CASTBARSPAWNER.CS — Barre de canalisation world-space, une instance par
// canalisation active (Mob/PNJ). Path : Assets/Scripts/World/CastBarSpawner.cs
//
// Contrairement à ProgressBarUI (singleton à 1 slot, utilisé par la
// canalisation Player + harvest + craft), plusieurs Mob/PNJ peuvent canaliser
// simultanément — chaque canalisation obtient sa propre instance de ce
// composant, sur un GameObject dédié qui se détruit lui-même en fin de vie.
// Réutilise le MÊME prefab visuel que ProgressBarUI (lu dynamiquement via
// ProgressBarUI.Instance) — aucune nouvelle assignation Inspector requise.
// =============================================================

public class CastBarSpawner : MonoBehaviour
{
    private GameObject      _visual;
    private Image           _fill;
    private Transform       _followTarget;
    private float           _duration;
    private float           _elapsed;
    private System.Action   _onComplete;
    private System.Action   _onCancel;
    private bool            _finished;

    /// <summary>Crée une nouvelle barre de canalisation world-space et la lance. Retourne
    /// l'instance pour permettre un Cancel() externe (interrupt). Ne fait rien de visible si
    /// ProgressBarUI.Instance ou son progressBarPrefab ne sont pas encore assignés — dégrade en
    /// simple minuteur invisible (même comportement que ProgressBarUI.RunProgressNoVisual).</summary>
    public static CastBarSpawner Show(string label, float duration, Transform followTarget,
        System.Action onComplete, System.Action onCancel = null)
    {
        var go      = new GameObject($"CastBar_{followTarget.name}");
        var spawner = go.AddComponent<CastBarSpawner>();
        spawner.Init(label, duration, followTarget, onComplete, onCancel);
        return spawner;
    }

    private void Init(string label, float duration, Transform followTarget,
        System.Action onComplete, System.Action onCancel)
    {
        _followTarget = followTarget;
        _duration     = duration;
        _onComplete   = onComplete;
        _onCancel     = onCancel;

        var progressBarUI = ProgressBarUI.Instance;
        if (progressBarUI == null || progressBarUI.progressBarPrefab == null)
        {
            Debug.LogWarning("[CastBarSpawner] ProgressBarUI.Instance ou son progressBarPrefab " +
                              "non assigné — canalisation sans visuel.", this);
            return;
        }

        Vector3 spawnPos = followTarget.position + Vector3.up * progressBarUI.heightOffset;
        _visual = Instantiate(progressBarUI.progressBarPrefab, spawnPos, Quaternion.identity);

        Transform fillT  = FindChildByName(_visual, "Fill");
        Transform labelT = FindChildByName(_visual, "Label");
        _fill = fillT != null ? fillT.GetComponent<Image>() : null;
        var labelComp    = labelT != null ? labelT.GetComponent<TextMeshProUGUI>() : null;

        if (_fill != null) { _fill.fillAmount = 0f; _fill.color = progressBarUI.colorCast; }
        if (labelComp != null) labelComp.text = label;
    }

    private void Update()
    {
        if (_finished) return;

        if (_followTarget == null) { Cancel(); return; }   // caster détruit sous nos pieds

        if (_visual != null)
        {
            _visual.transform.position = _followTarget.position +
                Vector3.up * (ProgressBarUI.Instance != null ? ProgressBarUI.Instance.heightOffset : 2.5f);
            if (Camera.main != null)
                _visual.transform.forward = Camera.main.transform.forward;
        }

        _elapsed += Time.deltaTime;
        if (_fill != null) _fill.fillAmount = Mathf.Clamp01(_elapsed / _duration);

        if (_elapsed >= _duration)
        {
            _finished = true;
            System.Action complete = _onComplete;
            DestroySelf();
            complete?.Invoke();
        }
    }

    /// <summary>Annule la canalisation avant son terme (interrupt) — appelé par
    /// Mob.InterruptChannelCast()/PNJ.InterruptChannelCast(), jamais par ce composant lui-même
    /// sauf si le followTarget disparaît.</summary>
    public void Cancel()
    {
        if (_finished) return;
        _finished = true;
        System.Action cancel = _onCancel;
        DestroySelf();
        cancel?.Invoke();
    }

    private void DestroySelf()
    {
        if (_visual != null) Destroy(_visual);
        Destroy(gameObject);
    }

    private Transform FindChildByName(GameObject parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }
}
```

### 3. `Entities/Mob.cs` — flux de canalisation

**Nouveaux champs** :

```csharp
private bool           _isChanneling  = false;
private SkillData      _channelSkill  = null;
private Entity         _channelTarget = null;
private GameObject     _channelVfxCast = null;
private CastBarSpawner _channelBar    = null;
```

**Branchement AVANT le pending-hit existant** — dans `HandleAttack()` et `TryUseSkill()`, le
point où `StartPendingHit(skill, target)` est actuellement appelé devient :

```csharp
if (skill.castTime > 0f)
    StartChannelCast(skill, target);
else
    StartPendingHit(skill, target);
```

(Un skill à canalisation ne passe JAMAIS par le mécanisme `_pendingSkill`/hit-frame-sync — les
deux flux sont mutuellement exclusifs, exactement comme côté Player où `StartChannel()` et
`StartInstant()`/`StartMultiHit()` sont des branches séparées de `LaunchSkill()`.)

**Garde de ré-entrée — requis en plus du branchement ci-dessus (trouvé en vérification
indépendante du plan, absent du premier jet) :** `IsPendingHit` (sous-chantier 1) ne couvre pas
`_isChanneling` — sans garde dédié, `TryUseSkill()` et le bloc d'attaque de base de
`HandleAttack()` redéclencheraient `StartChannelCast()` À CHAQUE frame tant que la canalisation
est en vol (rien ne bloque leur ré-entrée, exactement le même bug de classe que le pending-hit
avant sa propre garde au sous-chantier 1). En tête de `TryUseSkill()` :

```csharp
if (IsPendingHit || _isChanneling) return true;
```

Et sur la condition du bloc d'attaque de base dans `HandleAttack()` :

```csharp
if (attackTimer <= 0f && !IsPendingHit && !_isChanneling)
```

**Nouvelles méthodes** :

```csharp
private void StartChannelCast(SkillData skill, Entity target)
{
    _isChanneling  = true;
    _channelSkill  = skill;
    _channelTarget = target;

    _animatorController?.PlayChannel(skill.channelAnimation);

    _channelVfxCast = skill.vfxCast != null
        ? Instantiate(skill.vfxCast, transform.position, Quaternion.identity)
        : null;

    _channelBar = CastBarSpawner.Show(
        label:       skill.skillName.Get(LocalizationManager.CurrentLanguage),
        duration:    skill.castTime,
        followTarget: transform,
        onComplete:  ResolveChannelCast,
        onCancel:    () => InterruptChannelCast(voluntary: true, reason: "bar volée")
    );
}

private void ResolveChannelCast()
{
    if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

    SkillData skill  = _channelSkill;
    Entity    target = _channelTarget;

    EndChannelCastState();

    if (skill.hasDelayedImpact)
        _skillSystem?.PlantDelayedZone(skill, this, target);
    else if (skill.isTrajectory)
        _skillSystem?.StartTrajectory(skill, this);
    else
        _skillSystem?.Execute(skill, this, target);

    if (data.skills != null && data.skills.Contains(skill))
        _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
}

/// <summary>voluntary = true (cible morte, bar volée en interne — CD moitié) | false (CC/mort
/// subie — CD complet). Même règle que SkillBar.InterruptChannel() côté Player. Capture
/// _channelBar AVANT EndChannelCastState() puis annule la bar APRÈS (pas avant) — sinon le
/// callback onCancel de la bar (réentrant sur cette même méthode) s'exécuterait avec
/// _isChanneling encore vrai et poserait le CD demi avant que CET appel (potentiellement
/// voluntary:false) n'atteigne son propre garde et no-op — un hard CC finirait TOUJOURS avec le
/// CD demi au lieu du CD complet, silencieusement (trouvé en vérification indépendante du
/// plan). Seule cette méthode a le droit d'appeler _channelBar.Cancel() — tous les autres
/// appelants (poll Update(), GoReturn(), Die()) appellent seulement InterruptChannelCast(...)
/// et laissent CETTE méthode gérer la bar.</summary>
private void InterruptChannelCast(bool voluntary, string reason)
{
    if (!_isChanneling) return;

    SkillData      skill = _channelSkill;
    CastBarSpawner bar   = _channelBar;

    EndChannelCastState();   // _isChanneling = false AVANT bar.Cancel() — voir résumé ci-dessus

    bar?.Cancel();           // onCancel réentrant tombe sur le garde !_isChanneling, no-op propre
    _animatorController?.CancelChannel();

    if (data.skills != null && data.skills.Contains(skill))
        _skillCooldowns[skill] = voluntary ? skill.cooldown * 0.5f : skill.cooldown;

    Debug.Log($"[MOB] Canalisation interrompue ({reason}) — {(voluntary ? "CD demi" : "CD complet")}.");
}

private void EndChannelCastState()
{
    _isChanneling  = false;
    _channelSkill  = null;
    _channelTarget = null;

    if (_channelVfxCast != null) { Destroy(_channelVfxCast); _channelVfxCast = null; }
    // _channelBar n'est PAS annulé ici — InterruptChannelCast() s'en charge APRÈS cet appel
    // (voir son commentaire). ResolveChannelCast() (fin normale, bar déjà terminée d'elle-même)
    // n'a rien à annuler non plus. Juste null la référence dans les deux cas.
    _channelBar = null;
}
```

**Poll d'interrupt dans `Update()`** — ajouter, à côté du tick du pending-hit déjà présent
(avant les early-return CC existants, puisque ce poll DOIT justement réagir aux CC). **Ne PAS**
appeler `_channelBar?.Cancel()` ici — `InterruptChannelCast()` s'en charge lui-même, dans le bon
ordre (voir ci-dessus) :

```csharp
if (_isChanneling)
{
    bool hardCC = statusEffects != null && (statusEffects.isStunned ||
                  statusEffects.isShocked || statusEffects.isFreezed ||
                  statusEffects.isKnockedBack || statusEffects.isFeared ||
                  statusEffects.isSilenced);
    if (hardCC)
        InterruptChannelCast(voluntary: false, reason: "CC");
    else if (_channelTarget != null && _channelTarget.isDead)
        InterruptChannelCast(voluntary: true, reason: "cible morte");
}
```

(Le terme `isDead ||` de la première version a été retiré — `Update()` a déjà un early-return
`if (isDead || data == null) return;` AVANT ce poll, donc cette branche n'était jamais atteinte ;
la mort est couverte séparément par le nettoyage explicite dans `Die()`, voir plus bas.)

**`GoReturn()`** (déjà modifié au sous-chantier 1 pour nettoyer le pending-hit) — ajouter le
nettoyage de canalisation au même endroit, même raisonnement (désengagement mi-canalisation) :

```csharp
if (_isChanneling)
    InterruptChannelCast(voluntary: true, reason: "désengagement");
```

**`Die()`** — **requis pour Mob aussi** (trouvé en vérification indépendante du plan — Mob
n'avait aucun nettoyage de canalisation prévu, contrairement au pending-hit du sous-chantier 1
qui, lui, s'arrête naturellement via `this.enabled = false`). Ajouter, juste avant cette ligne :

```csharp
if (_isChanneling)
    InterruptChannelCast(voluntary: false, reason: "mort");
```

### 4. `Entities/PNJ.cs` — même flux, idiome sans `?.`

Code identique à `Mob.cs` ci-dessus (y compris l'ordre `EndChannelCastState()` puis
`bar.Cancel()` dans `InterruptChannelCast()`), avec les différences déjà établies aux
sous-chantiers précédents : `_skillSystem.` sans `?.` partout (`_animatorController`/
`_channelBar` restent avec `?.` — nullables légitimement), nettoyage de canalisation ajouté au
même endroit que le nettoyage de pending-hit déjà posé (leash-disengage dans
`HandleCombatAI()`, et `Die()`).

**`Die()`** — ajouter le nettoyage de canalisation à côté du nettoyage de pending-hit déjà
présent (même raison : `PNJ.Die()` ne désactive jamais le composant, un `_isChanneling` resté
vrai survivrait au respawn) :

```csharp
if (_isChanneling)
    InterruptChannelCast(voluntary: false, reason: "mort");
```

## Cas limites vérifiés

- **Skill à canalisation avec `hasDelayedImpact`/`isTrajectory`** : `ResolveChannelCast()`
  reproduit le même branchement 3 voies que `ResolvePendingHit()` — comportement de zone/
  trajectoire inchangé, seul le déclencheur (fin du timer de cast vs frame d'impact d'anim)
  diffère.
- **Skill à canalisation MultiHit** : `Execute()` (pas `ResolveExecute()`) gère nativement sa
  propre coroutine `ExecuteMultiHit` — aucun nouveau mécanisme d'index nécessaire, cohérent avec
  le choix du Player.
- **`_channelBar` détruit sous nos pieds** (ex: scène déchargée) : `Update()` de
  `CastBarSpawner` vérifie `_followTarget == null` à chaque frame et s'auto-annule proprement.
- **Interrupt pendant que `_channelBar` a DÉJÀ atteint sa durée au même frame** (event et poll
  se chevauchent) : `ResolveChannelCast()`/`InterruptChannelCast()` gardent tous deux
  `if (!_isChanneling) return;` en tête — le premier arrivé gagne, le second est un no-op.
- **Un Mob/PNJ tué pendant sa canalisation** : le poll `hardCC` ne peut PAS voir `isDead`
  (`Update()` a déjà un early-return dessus avant ce poll) — c'est le nettoyage explicite dans
  `Die()` (Mob ET PNJ, voir ci-dessus) qui couvre ce cas, pas une redondance avec le poll.
- **Ré-déclenchement pendant qu'une canalisation est en vol** : voir Décisions ci-dessus ("Garde
  de ré-entrée") — un vrai bug trouvé en vérification indépendante du plan (mana vidée en
  boucle, `vfxCast`/barres instanciés sans fin), corrigé par `_isChanneling` consommé
  explicitement dans `TryUseSkill()`/`TryUseSecondarySkill()` ET dans le bloc d'attaque de base.
- **PNJ non-`canFight`** : `HandleCombatAI()` n'est jamais appelée, `_isChanneling` reste
  toujours `false` — aucun changement de comportement pour les PNJ dialogue/boutique.

## Fichiers touchés

- `World/MobAnimatorController.cs` — `CancelActionTrigger`, `ResetTrigger` dans
  `PlayOverrideClip`, `PlayChannel()`, `CancelChannel()`.
- `World/PNJAnimatorController.cs` — mêmes ajouts.
- `World/CastBarSpawner.cs` (nouveau, partagé Mob/PNJ).
- `Entities/Mob.cs` — nouveaux champs canalisation, branchement `castTime > 0` dans
  `HandleAttack()`/`TryUseSkill()`, nouvelles méthodes `StartChannelCast`/`ResolveChannelCast`/
  `InterruptChannelCast`/`EndChannelCastState`, poll dans `Update()`, nettoyage dans
  `GoReturn()`.
- `Entities/PNJ.cs` — mêmes catégories de changement, adaptées à l'idiome sans `?.`, nettoyage
  dans le leash-disengage de `HandleCombatAI()` et dans `Die()`.

Aucun changement `Combat/SkillSystem.cs` (`Execute`/`PlantDelayedZone`/`StartTrajectory` déjà
prêts, caster-agnostiques), aucun changement `Data/Skills/SkillData.cs` (`castTime`/
`channelAnimation`/`vfxCast` existent déjà). Aucun changement `UI/PanelSecondaire/
ProgressBarUI.cs` (lu seulement, jamais modifié — zéro risque de régression sur harvest/craft/
canalisation Player).

## Hors scope (rappel)

- Immobilisation explicite / seuil de mouvement pendant la canalisation Mob/PNJ (voir Décisions
  — pas de symptôme identifié justifiant ce code aujourd'hui).
- Toute modification du système `ProgressBarUI`/canalisation Player existant.

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian, une fois le
setup Editor du sous-chantier 1 fait (Animator Controller + placeholder + Animation Events) ET
au moins un `SkillData` de test avec `castTime > 0` + `channelAnimation` + `vfxCast` assignés
sur un Mob et un PNJ `canFight` :

1. Compiler, 0 erreur.
2. Mob avec skill `castTime > 0` simple : l'anim de canalisation joue, `vfxCast` apparaît au
   lancement, une barre de cast world-space suit le Mob et se remplit sur `castTime` secondes,
   les dégâts/effets partent à la fin du timer (pas au déclenchement).
3. Deux Mob canalisent en même temps (ou un Mob canalise pendant que le Player harvest/craft/
   canalise) → chaque barre reste indépendante, aucune ne s'écrase/disparaît prématurément.
4. Infliger un hard CC (Stun/Freeze/etc.) au Mob pendant sa canalisation → interrompue net
   (anim coupée, barre disparaît, `vfxCast` détruit), CD complet posé.
5. Infliger de simples dégâts (pas de CC) pendant la canalisation → NE l'interrompt PAS,
   continue normalement jusqu'à résolution.
6. La cible du skill meurt pendant la canalisation → interrompue (CD moitié), pas de crash.
7. Tuer le Mob/PNJ pendant sa canalisation → tout se nettoie proprement (barre, vfxCast,
   anim), pas d'effet appliqué après la mort.
8. Skill à canalisation avec `hasDelayedImpact`/`isTrajectory` → la zone/trajectoire démarre à
   la fin du timer de canalisation, comportement de zone/trajectoire inchangé par ailleurs.
9. PNJ `canFight` — mêmes vérifications 2-8.
10. PNJ non-`canFight` — aucune régression dialogue/boutique.
11. Vérifier qu'aucune régression n'apparaît sur la canalisation Player, le harvest, ou le
    craft (ProgressBarUI non modifié, mais bon sens de le confirmer après avoir ajouté un
    lecteur externe de `ProgressBarUI.Instance.progressBarPrefab`/`colorCast`/`heightOffset`).
