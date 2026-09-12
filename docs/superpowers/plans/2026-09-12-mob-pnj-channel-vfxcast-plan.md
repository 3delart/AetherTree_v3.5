# Canalisation visible + vfxCast Mob/PNJ (sous-chantier 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner à Mob et PNJ une canalisation visible (`castTime > 0` enfin consultée), avec
barre de cast world-space suivant le caster, VFX de lancement (`vfxCast`), et interruption sur
hard CC/mort — mirroring exact du système déjà en production côté Player
(`SkillBar.StartChannel()`/`ResolveChannel()`/`InterruptChannel()`).

**Architecture:** Un nouveau composant partagé `World/CastBarSpawner.cs` (une instance par
canalisation active, pas un singleton comme `ProgressBarUI`) fournit la barre visuelle. Les
Animator Controllers du sous-chantier 1 (`MobAnimatorController`/`PNJAnimatorController`)
gagnent `PlayChannel()`/`CancelChannel()` (réutilisent le state `Attack` existant, pas de
nouveau state). `Entities/Mob.cs`/`Entities/PNJ.cs` gagnent un flux de canalisation
mutuellement exclusif du mécanisme `_pendingSkill` déjà existant.

**Tech Stack:** Unity 2022+ (C#), `Animator`/`AnimatorOverrideController` (déjà en place),
`UI/PanelSecondaire/ProgressBarUI.cs` (lu, jamais modifié), pas de framework de test automatisé.

**Spec:** `docs/superpowers/specs/2026-09-12-mob-pnj-channel-vfxcast-design.md`

## Global Constraints

- Pas de nouveau state Animator "Channel" — le Player réutilise le MÊME state "Attack" pour
  attaque instantanée ET canalisation. `PlayChannel()` est un simple alias de `PlayAttack()`
  (même méthode privée `PlayOverrideClip()`).
- `World/CastBarSpawner.cs` n'est PAS un singleton — une instance PAR canalisation active,
  créée/détruite dynamiquement. Ne touche jamais à `UI/PanelSecondaire/ProgressBarUI.cs` (lu
  seulement, via `ProgressBarUI.Instance.progressBarPrefab`/`.colorCast`/`.heightOffset`).
- Un skill `castTime > 0` ne passe JAMAIS par `StartPendingHit()`/le mécanisme `_pendingSkill`
  du sous-chantier 1 — les deux flux sont mutuellement exclusifs, branchement AVANT l'appel à
  `StartPendingHit()` dans `HandleAttack()`/`TryUseSkill()` (Mob) et
  `HandleCombatAI()`/`TryUseSecondarySkill()` (PNJ).
- Résolution via `SkillSystem.Execute()` (PAS `ResolveExecute()`) — comme
  `SkillBar.ResolveChannel()` côté Player, pour couvrir nativement un skill MultiHit+castTime
  via la coroutine `ExecuteMultiHit` déjà existante dans `Execute()`.
- Poll d'interrupt dans `Update()` : hard CC (`isStunned`/`isShocked`/`isFreezed`/
  `isKnockedBack`/`isFeared`/`isSilenced`) OU mort du caster → interrupt NON-volontaire (CD
  complet) ; cible morte → interrupt volontaire (CD demi). **Les dégâts simples n'interrompent
  PAS** — règle déjà réelle côté Player aujourd'hui, pas une nouveauté introduite ici.
- Idiome à préserver strictement : `Mob.cs` = `_skillSystem?.` partout (null-conditional).
  `PNJ.cs` = `_skillSystem.` SANS `?.` (garanti non-null par le guard déjà en tête de
  `HandleCombatAI()`). Ne jamais uniformiser les deux fichiers l'un sur l'autre.
- Nettoyage de canalisation ajouté aux MÊMES endroits que le nettoyage du pending-hit
  (sous-chantier 1) : `Mob.GoReturn()` ; côté PNJ le bloc leash de `HandleCombatAI()` et
  `PNJ.Die()` (PNJ ne désactive jamais son composant à la mort, contrairement à Mob).
- Pas d'immobilisation explicite / seuil de mouvement ajouté — décision actée dans le spec, ne
  pas ajouter ce code dans ce plan.
- Aucun changement `Data/Skills/SkillData.cs` (`castTime`/`channelAnimation`/`vfxCast` existent
  déjà), aucun changement `Combat/SkillSystem.cs` (`Execute`/`PlantDelayedZone`/`StartTrajectory`
  déjà caster-agnostiques), aucun changement `UI/PanelSecondaire/ProgressBarUI.cs`.

---

## Task 1: `World/CastBarSpawner.cs` (nouveau, partagé)

**Files:**
- Create: `World/CastBarSpawner.cs`

**Interfaces:**
- Consumes: `ProgressBarUI.Instance` (`progressBarPrefab`, `colorCast`, `heightOffset` — tous
  déjà des champs publics existants sur `UI/PanelSecondaire/ProgressBarUI.cs`), `TMPro.TextMeshProUGUI`,
  `UnityEngine.UI.Image`.
- Produces: `public static CastBarSpawner Show(string label, float duration, Transform
  followTarget, System.Action onComplete, System.Action onCancel = null)`, `public void
  Cancel()` — consommés par `Entities/Mob.cs` (Task 3) et `Entities/PNJ.cs` (Task 4).

Aucune dépendance sur les autres tâches — ne connaît ni `Mob` ni `PNJ`, juste un `Transform` à
suivre.

- [ ] **Step 1: Créer le fichier avec le code complet**

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

- [ ] **Step 2: Vérifier la compilation**

Ce fichier ne dépend d'aucune autre tâche de ce plan — il doit compiler seul. Vérifier que
`ProgressBarUI` (classe déjà existante dans `UI/PanelSecondaire/ProgressBarUI.cs`) expose bien
publiquement `progressBarPrefab` (`GameObject`), `colorCast` (`Color`), `heightOffset` (`float`)
et `Instance` (`static ProgressBarUI`) — lire ce fichier pour confirmer avant de committer.

- [ ] **Step 3: Commit**

```bash
git add World/CastBarSpawner.cs
git commit -m "feat: add CastBarSpawner, a per-cast world-space channel bar for Mob/PNJ"
```

---

## Task 2: Extension des Animator Controllers (`MobAnimatorController.cs` + `PNJAnimatorController.cs`)

**Files:**
- Modify: `World/MobAnimatorController.cs`
- Modify: `World/PNJAnimatorController.cs`

**Interfaces:**
- Produces (sur les deux classes) : `public void PlayChannel(AnimationClip clip)`, `public void
  CancelChannel()` — consommés par `Entities/Mob.cs` (Task 3) et `Entities/PNJ.cs` (Task 4).

Les deux fichiers reçoivent EXACTEMENT le même changement (mécanique, même forme) — un seul
dispatch peut couvrir les deux si le mode d'exécution choisi le permet (batch), sinon deux tâches
identiques indépendantes. Aucune dépendance sur la Task 1.

⚠ **Avant d'éditer** : lire les deux fichiers réels en entier — ils ont été créés au
sous-chantier 1, forme actuelle confirmée ci-dessous, mais toujours vérifier avant d'écraser.

### `World/MobAnimatorController.cs`

- [ ] **Step 0: Corriger le commentaire d'en-tête périmé**

Trouver :
```csharp
// PAS de paramètre InCombat (aucune mécanique d'équipement visible sur Mob),
// PAS de CancelActionTrigger/state Channel (sous-chantier 2, futur).
// =============================================================
```
Remplacer par :
```csharp
// PAS de paramètre InCombat (aucune mécanique d'équipement visible sur Mob).
// CancelActionTrigger ajouté au sous-chantier 2 (canalisation) — pas de state Channel séparé,
// réutilise le state "Attack" existant.
// =============================================================
```

- [ ] **Step 1: Ajouter la constante `CancelActionTrigger`**

Trouver :
```csharp
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";
```
Remplacer par :
```csharp
    private const string SpeedParam          = "Speed";
    private const string AttackState         = "Attack";
    private const string CancelActionTrigger = "CancelAction";
```

- [ ] **Step 2: Protéger `PlayOverrideClip()` contre un trigger orphelin**

Trouver :
```csharp
    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;

        // Indexation par référence au clip D'ORIGINE (attackPlaceholderClip), pas par nom de
        // state — voir le commentaire sur le champ ci-dessus.
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }
```
Remplacer par :
```csharp
    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;

        // Indexation par référence au clip D'ORIGINE (attackPlaceholderClip), pas par nom de
        // state — voir le commentaire sur le champ ci-dessus.
        _overrideController[attackPlaceholderClip] = clip;

        // Reset du trigger CancelAction avant de rejouer le state Attack — sinon un trigger posé
        // par CancelChannel() qui n'a jamais trouvé de transition à consommer (ex: l'Animator
        // était déjà revenu en locomotion) resterait en attente et se déclencherait au prochain
        // re-entré dans Attack, coupant net un skill qui n'a rien à voir avec l'interrupt
        // précédent — même protection que PlayerAnimatorController.cs.
        _animator.ResetTrigger(CancelActionTrigger);
        _animator.Play(AttackState, 0, 0f);
    }
```

- [ ] **Step 3: Ajouter `PlayChannel()`/`CancelChannel()`, juste après `PlayAttack()`**

Trouver :
```csharp
    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);
```
Remplacer par :
```csharp
    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (réutilise le state "Attack", pas de state séparé). Appelé par
    /// Mob.StartChannelCast().</summary>
    public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion. Appelé par Mob.InterruptChannelCast().</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }
```

- [ ] **Step 4: Commit**

```bash
git add World/MobAnimatorController.cs
git commit -m "feat: add PlayChannel/CancelChannel to MobAnimatorController"
```

### `World/PNJAnimatorController.cs`

- [ ] **Step 5: Ajouter la constante `CancelActionTrigger`**

Trouver :
```csharp
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";
```
Remplacer par :
```csharp
    private const string SpeedParam          = "Speed";
    private const string AttackState         = "Attack";
    private const string CancelActionTrigger = "CancelAction";
```

- [ ] **Step 6: Protéger `PlayOverrideClip()` contre un trigger orphelin**

Trouver :
```csharp
    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }
```
Remplacer par :
```csharp
    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
        _overrideController[attackPlaceholderClip] = clip;

        // Reset du trigger CancelAction avant de rejouer le state Attack — même protection que
        // MobAnimatorController.cs/PlayerAnimatorController.cs.
        _animator.ResetTrigger(CancelActionTrigger);
        _animator.Play(AttackState, 0, 0f);
    }
```

- [ ] **Step 7: Ajouter `PlayChannel()`/`CancelChannel()`, juste après `PlayAttack()`**

Trouver :
```csharp
    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par PNJ.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);
```
Remplacer par :
```csharp
    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par PNJ.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (réutilise le state "Attack", pas de state séparé). Appelé par
    /// PNJ.StartChannelCast().</summary>
    public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion. Appelé par PNJ.InterruptChannelCast().</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }
```

- [ ] **Step 8: Vérifier la compilation des deux fichiers**

Lire les deux fichiers modifiés en entier — confirmer accolades équilibrées, `CancelActionTrigger`
déclaré une seule fois par fichier, `PlayChannel`/`CancelChannel` bien à l'intérieur de leur
classe respective.

- [ ] **Step 9: Commit**

```bash
git add World/PNJAnimatorController.cs
git commit -m "feat: add PlayChannel/CancelChannel to PNJAnimatorController"
```

---

## Task 3: `Entities/Mob.cs` — flux de canalisation

**Files:**
- Modify: `Entities/Mob.cs`

**Interfaces:**
- Consumes: `MobAnimatorController.PlayChannel(AnimationClip)`/`CancelChannel()` (Task 2),
  `CastBarSpawner.Show(string, float, Transform, System.Action, System.Action)`/`Cancel()`
  (Task 1), `SkillSystem.Execute(SkillData, Entity, Entity)`/`PlantDelayedZone(SkillData, Entity,
  Entity)`/`StartTrajectory(SkillData, Entity)` (déjà existants, `Combat/SkillSystem.cs`).
- Produces: rien de nouveau consommé par d'autres tâches (flux interne à `Mob.cs`).

Dépend des Tasks 1 et 2 (côté Mob). Indépendante de la Task 4 (aucun fichier ni interface
partagée avec `PNJ.cs`).

⚠ **Avant d'éditer** : le fichier a pu changer de forme depuis l'écriture de ce plan (numéros de
ligne indicatifs, état confirmé au moment de l'écriture ci-dessous). Toujours lire
`Entities/Mob.cs` en entier — au minimum `Awake()`, `HandleAttack()`, `TryUseSkill()`,
`GoReturn()` — dans leur état RÉEL actuel avant de modifier.

- [ ] **Step 1: Lire le fichier réel en entier**

Confirmer que les champs `_pendingSkill`/`IsPendingHit`/`_animatorController` (sous-chantier 1),
`HandleAttack()`, `TryUseSkill()`, `StartPendingHit()`, `GoReturn()` existent toujours sous ces
noms avec un contenu proche de celui décrit ci-dessous.

- [ ] **Step 2: Ajouter les nouveaux champs de canalisation**

Trouver (juste après le champ `_animatorController` déjà posé au sous-chantier 1) :
```csharp
    private MobAnimatorController _animatorController;
    private System.Action onDeathCallback;
```
Remplacer par :
```csharp
    private MobAnimatorController _animatorController;

    // ── Canalisation visible (castTime > 0) — sous-chantier 2, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-channel-vfxcast-design.md — mutuellement exclusif du mécanisme
    // _pendingSkill ci-dessus : un skill castTime > 0 ne passe JAMAIS par StartPendingHit() ────
    private bool           _isChanneling   = false;
    private SkillData      _channelSkill   = null;
    private Entity         _channelTarget  = null;
    private GameObject     _channelVfxCast = null;
    private CastBarSpawner _channelBar     = null;

    private System.Action onDeathCallback;
```

- [ ] **Step 3: Ajouter le poll d'interrupt dans `Update()`**

Trouver :
```csharp
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
Remplacer par :
```csharp
        // Timeout de secours (event d'impact jamais reçu) — tick AVANT tout early-return CC :
        // un coup déjà lancé va au bout, non-interruptible, même principe que le Player
        // (chantier B hit-frame-sync).
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        // Poll d'interrupt de la canalisation — hard CC (non-volontaire, CD complet) ou cible
        // morte (volontaire, CD demi). Les dégâts simples n'interrompent PAS, même règle que
        // SkillBar côté Player (vérifiée dans le code réel, pas une nouveauté). PAS de terme
        // isDead ici — Update() a déjà un early-return dessus juste au-dessus (ligne
        // `if (isDead || data == null) return;`), donc cette branche ne serait jamais atteinte ;
        // la mort est couverte séparément par le nettoyage explicite dans Die() (Step 7bis).
        // InterruptChannelCast() gère elle-même l'annulation de _channelBar (voir Step 6) — ne
        // JAMAIS appeler _channelBar?.Cancel() ici (trouvé en vérification indépendante du
        // plan : appeler Cancel() avant InterruptChannelCast ferait exécuter le callback
        // onCancel réentrant — () => InterruptChannelCast(voluntary: true, ...) — PENDANT que
        // _isChanneling est encore vrai, posant silencieusement le CD demi avant même que cet
        // appel explicite, potentiellement voluntary:false, n'atteigne son propre garde et
        // no-op — un hard CC finirait TOUJOURS avec le CD demi au lieu du CD complet).
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

- [ ] **Step 3bis: Ajouter la garde de ré-entrée `_isChanneling`**

**Bug trouvé en vérification indépendante du plan** : `IsPendingHit` (sous-chantier 1) ne couvre
PAS `_isChanneling`. Sans garde dédiée, `TryUseSkill()` et le bloc d'attaque de base de
`HandleAttack()` redéclencheraient `StartChannelCast()` À CHAQUE frame tant que la canalisation
est en vol — mana vidée en boucle, un nouveau `vfxCast`/`CastBarSpawner` instancié chaque frame
sans jamais détruire le précédent (fuite), anim relancée sur sa frame 0 sans arrêt.

Trouver, en tête de `TryUseSkill()` (posé au sous-chantier 1) :
```csharp
        // Un pending-hit est déjà en vol (secondaire OU attaque de base) — ne rien redéclencher
        // tant qu'il n'est pas résolu (event ou timeout). Retourne true pour que HandleAttack()/
        // HandleChase() traitent ce tick comme "occupé" (mêmes early-return qu'un vrai skill
        // lancé) plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit) return true;
```
Remplacer par :
```csharp
        // Un pending-hit OU une canalisation est déjà en vol — ne rien redéclencher tant que
        // l'un des deux n'est pas résolu. Retourne true pour que HandleAttack()/HandleChase()
        // traitent ce tick comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit || _isChanneling) return true;
```

Trouver, dans `HandleAttack()` (posé au sous-chantier 1) :
```csharp
        if (attackTimer <= 0f && !IsPendingHit)
```
Remplacer par :
```csharp
        if (attackTimer <= 0f && !IsPendingHit && !_isChanneling)
```

- [ ] **Step 4: Brancher `castTime > 0` dans `HandleAttack()`**

Trouver :
```csharp
            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                StartPendingHit(data.basicAttackSkill, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
```
Remplacer par :
```csharp
            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                if (data.basicAttackSkill.castTime > 0f)
                    StartChannelCast(data.basicAttackSkill, target);
                else
                    StartPendingHit(data.basicAttackSkill, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
```

- [ ] **Step 5: Brancher `castTime > 0` dans `TryUseSkill()`**

Trouver :
```csharp
            LookAt(target.transform);
            StartPendingHit(skill, target);
            attackTimer = data.attackCooldown;
            return true;
```
Remplacer par :
```csharp
            LookAt(target.transform);
            if (skill.castTime > 0f)
                StartChannelCast(skill, target);
            else
                StartPendingHit(skill, target);
            attackTimer = data.attackCooldown;
            return true;
```

- [ ] **Step 6: Ajouter les 4 nouvelles méthodes de canalisation, juste après `ResolvePendingHit()`**

Trouver la fin de `ResolvePendingHit()` (dernière ligne de la méthode, juste avant le commentaire
de section `// RETURN`) :
```csharp
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    // =========================================================
    // RETURN
    // =========================================================
```
Remplacer par :
```csharp
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    // =========================================================
    // CANALISATION (castTime > 0) — sous-chantier 2
    // =========================================================

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
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            followTarget: transform,
            onComplete:   ResolveChannelCast,
            onCancel:     () => InterruptChannelCast(voluntary: true, reason: "bar volée")
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

    /// <summary>voluntary = true (cible morte, bar volée en interne — CD moitié) | false
    /// (CC/mort subie — CD complet). Même règle que SkillBar.InterruptChannel() côté Player.
    /// Capture _channelBar AVANT EndChannelCastState() puis annule la bar APRÈS — ORDRE
    /// CRITIQUE (trouvé en vérification indépendante du plan) : si la bar était annulée AVANT,
    /// son callback onCancel réentrant (() => InterruptChannelCast(voluntary: true, ...))
    /// s'exécuterait pendant que _isChanneling est encore vrai et poserait le CD demi avant que
    /// CET appel n'atteigne son propre garde — un hard CC finirait TOUJOURS avec le CD demi au
    /// lieu du CD complet, silencieusement. Seule cette méthode a le droit d'appeler
    /// _channelBar.Cancel() — tous les autres appelants (poll Update(), GoReturn(), Die())
    /// appellent seulement InterruptChannelCast(...) et laissent CETTE méthode gérer la bar.</summary>
    private void InterruptChannelCast(bool voluntary, string reason)
    {
        if (!_isChanneling) return;

        SkillData      skill = _channelSkill;
        CastBarSpawner bar   = _channelBar;

        EndChannelCastState();   // _isChanneling = false AVANT bar.Cancel() — voir résumé ci-dessus

        bar?.Cancel();           // callback onCancel réentrant tombe sur !_isChanneling, no-op
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
        // (voir son commentaire ci-dessus). ResolveChannelCast() (fin normale, la bar a déjà
        // terminé d'elle-même) n'a rien à annuler non plus. Juste null la référence.
        _channelBar = null;
    }

    // =========================================================
    // RETURN
    // =========================================================
```

- [ ] **Step 7: Nettoyer la canalisation dans `GoReturn()`**

Trouver :
```csharp
    private void GoReturn()
    {
        currentState = MobState.Return;
        agent.SetDestination(spawnPos);

        // Un pending-hit en vol ne doit pas résoudre plus tard sur une cible désormais hors
        // combat — FullReset() (appelé seulement à l'ARRIVÉE au spawn, secondes plus tard) est
        // trop tardif pour ça, le timeout du pending a déjà quasi toujours résolu entre-temps.
        // GoReturn() est appelé au moment RÉEL du désengagement (leash dépassé, cible perdue),
        // donc c'est ici que le nettoyage doit avoir lieu (trouvé en review finale — parité
        // avec le nettoyage déjà posé au même moment côté PNJ.HandleCombatAI()).
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;
    }
```
Remplacer par :
```csharp
    private void GoReturn()
    {
        currentState = MobState.Return;
        agent.SetDestination(spawnPos);

        // Un pending-hit en vol ne doit pas résoudre plus tard sur une cible désormais hors
        // combat — FullReset() (appelé seulement à l'ARRIVÉE au spawn, secondes plus tard) est
        // trop tardif pour ça, le timeout du pending a déjà quasi toujours résolu entre-temps.
        // GoReturn() est appelé au moment RÉEL du désengagement (leash dépassé, cible perdue),
        // donc c'est ici que le nettoyage doit avoir lieu (trouvé en review finale — parité
        // avec le nettoyage déjà posé au même moment côté PNJ.HandleCombatAI()).
        _pendingSkill   = null;
        _pendingTarget  = null;
        _pendingTimeout = 0f;
        _pendingIsMulti = false;

        // Même raisonnement pour la canalisation (sous-chantier 2) — un Mob qui se désengage en
        // pleine canalisation ne doit pas la voir résoudre plus tard sur une cible hors combat.
        // NE PAS appeler _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-
        // même, dans le bon ordre (voir son commentaire, Step 6).
        if (_isChanneling)
            InterruptChannelCast(voluntary: true, reason: "désengagement");
    }
```

- [ ] **Step 7bis: Nettoyer la canalisation dans `Die()`**

**Requis pour Mob (trouvé en vérification indépendante du plan)** : le poll `hardCC` de la
Step 3 ne peut jamais voir une mort (`Update()` a déjà un early-return `if (isDead || data ==
null) return;` AVANT ce poll) — sans nettoyage explicite dans `Die()`, un Mob tué en pleine
canalisation garde sa barre/son `vfxCast`/son anim affichés pendant tout le reste de `castTime`.

Trouver, dans `Die()` :
```csharp
    protected override void Die()
    {
        base.Die();
        agent.ResetPath();
        agent.enabled = false;
        this.enabled = false;
```
Remplacer par :
```csharp
    protected override void Die()
    {
        base.Die();
        agent.ResetPath();
        agent.enabled = false;

        // Canalisation en vol au moment de la mort (sous-chantier 2) — nettoyée AVANT de
        // désactiver ce composant, sinon la barre/le vfxCast/l'anim resteraient affichés
        // jusqu'à la fin naturelle du timer sur un Mob déjà mort (trouvé en vérification
        // indépendante du plan). NE PAS appeler _channelBar?.Cancel() directement —
        // InterruptChannelCast() s'en charge dans le bon ordre (voir Step 6).
        if (_isChanneling)
            InterruptChannelCast(voluntary: false, reason: "mort");

        this.enabled = false;
```

- [ ] **Step 8: Vérifier la compilation**

Lire le fichier modifié en entier — confirmer accolades équilibrées, les 4 nouvelles méthodes de
canalisation bien à l'intérieur de la classe `Mob`, `?.` préservé sur tous les appels
`_skillSystem`/`_animatorController`/`_channelBar`. Confirmer que `LocalizationManager` (classe
statique globale, `Data/Localization/LocalizationManager.cs`) est accessible sans nouveau
`using` (pas de namespace dans ce projet). Confirmer spécifiquement (points ajoutés après
vérification indépendante du plan) :
- `TryUseSkill()` retourne `true` immédiatement via `if (IsPendingHit || _isChanneling) return true;` (Step 3bis).
- `HandleAttack()`'s bloc d'attaque de base porte `&& !IsPendingHit && !_isChanneling` (Step 3bis).
- `InterruptChannelCast()` capture `_channelBar` dans une variable locale, appelle
  `EndChannelCastState()` AVANT `bar?.Cancel()` (Step 6) — jamais l'inverse.
- Aucun appel externe à `_channelBar?.Cancel()` nulle part dans le fichier (poll `Update()`,
  `GoReturn()`, `Die()`) — seule `InterruptChannelCast()` y touche.
- `Die()` nettoie bien la canalisation AVANT `this.enabled = false;` (Step 7bis).

- [ ] **Step 9: Commit**

```bash
git add Entities/Mob.cs
git commit -m "feat: add visible channeling + vfxCast to Mob"
```

---

## Task 4: `Entities/PNJ.cs` — même flux, idiome sans `?.`

**Files:**
- Modify: `Entities/PNJ.cs`

**Interfaces:**
- Consumes: `PNJAnimatorController.PlayChannel(AnimationClip)`/`CancelChannel()` (Task 2), les
  mêmes méthodes `CastBarSpawner`/`SkillSystem` que la Task 3.
- Produces: rien de nouveau consommé par d'autres tâches.

Dépend des Tasks 1 et 2 (côté PNJ). Indépendante de la Task 3.

**Rappel critique** : `PNJ.cs` n'utilise JAMAIS `?.` sur `_skillSystem` (garanti non-null). Tous
les appels `_skillSystem.` dans les steps ci-dessous sont SANS `?.`. `_animatorController` et
`_channelBar`, eux, restent avec `?.` (nullables légitimement — pas de `[RequireComponent]`
dessus).

⚠ **Avant d'éditer** : lire `Entities/PNJ.cs` en entier — état RÉEL actuel confirmé ci-dessous
au moment de l'écriture, mais toujours revérifier.

- [ ] **Step 1: Lire le fichier réel en entier**

- [ ] **Step 2: Ajouter les nouveaux champs de canalisation**

Trouver :
```csharp
    public bool IsPendingHit => _pendingSkill != null;

    private PNJAnimatorController _animatorController;
```
Remplacer par :
```csharp
    public bool IsPendingHit => _pendingSkill != null;

    private PNJAnimatorController _animatorController;

    // ── Canalisation visible (castTime > 0) — sous-chantier 2, voir docs/superpowers/specs/
    // 2026-09-12-mob-pnj-channel-vfxcast-design.md — mutuellement exclusif du mécanisme
    // _pendingSkill ci-dessus : un skill castTime > 0 ne passe JAMAIS par StartPendingHit() ────
    private bool           _isChanneling   = false;
    private SkillData      _channelSkill   = null;
    private Entity         _channelTarget  = null;
    private GameObject     _channelVfxCast = null;
    private CastBarSpawner _channelBar     = null;
```

- [ ] **Step 3: Ajouter le poll d'interrupt dans `Update()`**

Trouver :
```csharp
        // Timeout de secours (event d'impact jamais reçu) — même principe que Mob.cs.
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }
```
Remplacer par :
```csharp
        // Timeout de secours (event d'impact jamais reçu) — même principe que Mob.cs.
        if (_pendingSkill != null)
        {
            _pendingTimeout -= Time.deltaTime;
            if (_pendingTimeout <= 0f)
                ResolvePendingHit(_pendingIsMulti ? _pendingMultiNextIndex : 0);
        }

        // Poll d'interrupt de la canalisation — même règle que Mob.cs (hard CC = CD complet,
        // cible morte = CD demi, dégâts simples n'interrompent PAS). PAS de terme isDead ici —
        // même raison que Mob.cs (Update() a déjà un early-return dessus juste au-dessus) ; la
        // mort est couverte séparément par Die() (Step 8). InterruptChannelCast() gère elle-
        // même l'annulation de _channelBar — ne JAMAIS appeler _channelBar?.Cancel() ici (voir
        // le commentaire détaillé sur InterruptChannelCast(), Step 7 — appeler Cancel() avant
        // ferait exécuter le callback onCancel réentrant pendant que _isChanneling est encore
        // vrai, posant silencieusement le CD demi même sur un hard CC).
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

- [ ] **Step 3bis: Ajouter la garde de ré-entrée `_isChanneling`**

**Bug trouvé en vérification indépendante du plan** — même raison que Mob.cs Step 3bis :
`IsPendingHit` ne couvre pas `_isChanneling`, sans garde dédiée `TryUseSecondarySkill()` et le
bloc d'attaque de base de `HandleCombatAI()` redéclencheraient `StartChannelCast()` à chaque
frame pendant toute la canalisation.

Trouver, en tête de `TryUseSecondarySkill()` (posé au sous-chantier 1) :
```csharp
        // Un pending-hit est déjà en vol (secondaire OU attaque de base) — ne rien redéclencher
        // tant qu'il n'est pas résolu. Retourne true pour que HandleCombatAI() traite ce tick
        // comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit) return true;
```
Remplacer par :
```csharp
        // Un pending-hit OU une canalisation est déjà en vol — ne rien redéclencher tant que
        // l'un des deux n'est pas résolu. Retourne true pour que HandleCombatAI() traite ce tick
        // comme "occupé" plutôt que de tomber sur l'attaque de base.
        if (IsPendingHit || _isChanneling) return true;
```

- [ ] **Step 4: Nettoyer la canalisation dans le bloc leash de `HandleCombatAI()`**

Trouver :
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

            // Même raisonnement pour la canalisation (sous-chantier 2). NE PAS appeler
            // _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-même, dans le
            // bon ordre (voir son commentaire, Step 7).
            if (_isChanneling)
                InterruptChannelCast(voluntary: true, reason: "désengagement");

            ReturnToSpawn();
            return;
        }
```

- [ ] **Step 5: Brancher `castTime > 0` dans le bloc d'attaque de base de `HandleCombatAI()`**

Trouver :
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
Remplacer par :
```csharp
            // Bloqué tant qu'un pending-hit OU une canalisation est en vol — même raison que
            // Mob.HandleAttack() (sans cette garde, plus rien n'empêche un redéclenchement à
            // chaque frame une fois le CD déplacé à la résolution ; le taunt court-circuite
            // TryUseSecondarySkill() donc sa propre garde ne couvre pas ce chemin, voir
            // Step 3bis).
            if (_attackTimer <= 0f && !IsPendingHit && !_isChanneling && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                {
                    if (data.basicAttackSkill.castTime > 0f)
                        StartChannelCast(data.basicAttackSkill, _combatTarget);
                    else
                        StartPendingHit(data.basicAttackSkill, _combatTarget);
                }
                _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            }
```

- [ ] **Step 6: Brancher `castTime > 0` dans `TryUseSecondarySkill()`**

Trouver :
```csharp
            LookAt(target.transform);
            StartPendingHit(skill, target);
            _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            return true;
```
Remplacer par :
```csharp
            LookAt(target.transform);
            if (skill.castTime > 0f)
                StartChannelCast(skill, target);
            else
                StartPendingHit(skill, target);
            _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            return true;
```

- [ ] **Step 7: Ajouter les 4 nouvelles méthodes de canalisation, juste après `ResolvePendingHit()`**

Trouver la fin de `ResolvePendingHit()` (dernière ligne, juste avant le commentaire de la
méthode suivante `FindClosestEnemy()`) :
```csharp
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    /// <summary>
    /// Cherche l'entité ennemie la plus proche dans aggroRadius.
```
Remplacer par :
```csharp
        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    // =========================================================
    // CANALISATION (castTime > 0) — sous-chantier 2
    // =========================================================

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
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            followTarget: transform,
            onComplete:   ResolveChannelCast,
            onCancel:     () => InterruptChannelCast(voluntary: true, reason: "bar volée")
        );
    }

    private void ResolveChannelCast()
    {
        if (!_isChanneling) return;

        SkillData skill  = _channelSkill;
        Entity    target = _channelTarget;

        EndChannelCastState();

        if (skill.hasDelayedImpact)
            _skillSystem.PlantDelayedZone(skill, this, target);
        else if (skill.isTrajectory)
            _skillSystem.StartTrajectory(skill, this);
        else
            _skillSystem.Execute(skill, this, target);

        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }

    /// <summary>voluntary = true (cible morte, bar volée en interne — CD moitié) | false
    /// (CC/mort subie — CD complet). Même règle que SkillBar.InterruptChannel() côté Player.
    /// Capture _channelBar AVANT EndChannelCastState() puis annule la bar APRÈS — ORDRE
    /// CRITIQUE (trouvé en vérification indépendante du plan) : si la bar était annulée AVANT,
    /// son callback onCancel réentrant (() => InterruptChannelCast(voluntary: true, ...))
    /// s'exécuterait pendant que _isChanneling est encore vrai et poserait le CD demi avant que
    /// CET appel n'atteigne son propre garde — un hard CC finirait TOUJOURS avec le CD demi au
    /// lieu du CD complet, silencieusement. Seule cette méthode a le droit d'appeler
    /// _channelBar.Cancel() — tous les autres appelants (poll Update(), leash, Die())
    /// appellent seulement InterruptChannelCast(...) et laissent CETTE méthode gérer la bar.</summary>
    private void InterruptChannelCast(bool voluntary, string reason)
    {
        if (!_isChanneling) return;

        SkillData      skill = _channelSkill;
        CastBarSpawner bar   = _channelBar;

        EndChannelCastState();   // _isChanneling = false AVANT bar.Cancel() — voir résumé ci-dessus

        bar?.Cancel();           // callback onCancel réentrant tombe sur !_isChanneling, no-op
        _animatorController?.CancelChannel();

        if (data.skills != null && data.skills.Contains(skill))
            _skillCooldowns[skill] = voluntary ? skill.cooldown * 0.5f : skill.cooldown;

        Debug.Log($"[PNJ] Canalisation interrompue ({reason}) — {(voluntary ? "CD demi" : "CD complet")}.");
    }

    private void EndChannelCastState()
    {
        _isChanneling  = false;
        _channelSkill  = null;
        _channelTarget = null;

        if (_channelVfxCast != null) { Destroy(_channelVfxCast); _channelVfxCast = null; }
        // _channelBar n'est PAS annulé ici — InterruptChannelCast() s'en charge APRÈS cet appel
        // (voir son commentaire ci-dessus). ResolveChannelCast() (fin normale) n'a rien à
        // annuler non plus. Juste null la référence.
        _channelBar = null;
    }

    /// <summary>
    /// Cherche l'entité ennemie la plus proche dans aggroRadius.
```

- [ ] **Step 8: Nettoyer la canalisation dans `Die()`**

Trouver :
```csharp
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
Remplacer par :
```csharp
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

        // Même raisonnement pour la canalisation (sous-chantier 2). NE PAS appeler
        // _channelBar?.Cancel() ici — InterruptChannelCast() s'en charge elle-même, dans le bon
        // ordre (voir son commentaire, Step 7).
        if (_isChanneling)
            InterruptChannelCast(voluntary: false, reason: "mort");
```

- [ ] **Step 9: Vérifier la compilation**

Lire le fichier modifié en entier — confirmer accolades équilibrées, ZÉRO `?.` introduit sur
`_skillSystem` dans le nouveau code (`_animatorController`/`_channelBar` restent avec `?.`, c'est
correct), les 4 nouvelles méthodes bien à l'intérieur de la classe `PNJ`. Confirmer
spécifiquement (points ajoutés après vérification indépendante du plan) :
- `TryUseSecondarySkill()` retourne `true` immédiatement via
  `if (IsPendingHit || _isChanneling) return true;` (Step 3bis).
- Le bloc d'attaque de base de `HandleCombatAI()` porte
  `&& !IsPendingHit && !_isChanneling` (Step 5).
- `InterruptChannelCast()` capture `_channelBar` dans une variable locale, appelle
  `EndChannelCastState()` AVANT `bar?.Cancel()` (Step 7) — jamais l'inverse.
- Aucun appel externe à `_channelBar?.Cancel()` nulle part dans le fichier (poll `Update()`,
  leash de `HandleCombatAI()`, `Die()`) — seule `InterruptChannelCast()` y touche.

- [ ] **Step 10: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: add visible channeling + vfxCast to PNJ"
```

---

## Task 5: Vérification manuelle Play Mode (Florian — non automatisable)

**Files:** aucun fichier de code — setup Editor + test en jeu.

**Interfaces:** consomme l'ensemble des Tasks 1-4.

- [ ] **Step 1: Setup Editor (Florian)**

Au-delà du setup déjà requis pour le sous-chantier 1 (Animator Controller + placeholder + au
moins un clip d'attaque avec Animation Event) :
1. Assigner `channelAnimation` sur au moins un `SkillData` de test avec `castTime > 0`.
2. Assigner `vfxCast` (un prefab VFX quelconque) sur ce même skill.
3. Confirmer que `ProgressBarUI` a bien un `progressBarPrefab` assigné dans la scène (déjà
   requis pour la canalisation Player/harvest/craft existante — si ça fonctionne déjà pour le
   Player, rien à faire ici).
4. Assigner ce skill de test comme `basicAttackSkill` ou dans `data.skills` d'un Mob ET d'un PNJ
   `canFight` de test.
5. **Ajouter le paramètre Trigger `CancelAction` à l'Animator Controller du Mob ET du PNJ de
   test, avec une transition Any-State → locomotion dessus (Has Exit Time décoché).** Trouvé en
   review finale : `PlayOverrideClip()` appelle `ResetTrigger("CancelAction")` sur CHAQUE
   attaque/canalisation (pas seulement la canalisation) depuis le sous-chantier 1 — sans ce
   paramètre sur le Controller, Unity logue `Parameter 'CancelAction' does not exist.` à CHAQUE
   attaque de test, et une interruption de canalisation ne coupera jamais visuellement l'anim.
   Sans lien avec ce plan-ci en particulier — un oubli de setup du sous-chantier 1 rendu visible
   seulement maintenant que du code appelle réellement le trigger.

- [ ] **Step 2: Exécuter la checklist de vérification du spec**

Reprendre les 11 points de la section « Vérification » de
`docs/superpowers/specs/2026-09-12-mob-pnj-channel-vfxcast-design.md` :
1. Compiler, 0 erreur.
2. Mob avec skill `castTime > 0` simple : anim de canalisation joue, `vfxCast` apparaît au
   lancement, barre world-space suit le Mob et se remplit sur `castTime` secondes, dégâts/effets
   partent à la FIN du timer.
3. Deux Mob canalisent en même temps (ou un Mob pendant que le Player harvest/craft/canalise) →
   chaque barre indépendante, aucune ne s'écrase.
4. Hard CC (Stun/Freeze/etc.) pendant la canalisation → interrompue net (anim coupée, barre
   disparaît, `vfxCast` détruit), CD complet.
5. Dégâts simples (pas de CC) pendant la canalisation → n'interrompt PAS.
6. Cible meurt pendant la canalisation → interrompue (CD moitié), pas de crash.
7. Tuer le Mob/PNJ pendant sa canalisation → tout se nettoie proprement.
8. Skill à canalisation avec `hasDelayedImpact`/`isTrajectory` → zone/trajectoire démarre à la
   fin du timer.
9. PNJ `canFight` — mêmes vérifications 2-8.
10. PNJ non-`canFight` — aucune régression dialogue/boutique.
11. Aucune régression sur la canalisation Player, le harvest, ou le craft.
12. Garde de ré-entrée : sur un `castTime` long (>2s), l'anim/le `vfxCast`/la barre restent
    stables tout du long — pas de redémarrage/instanciation répétée, pas de drain de mana en
    boucle.
13. Hard CC sur un Mob/PNJ en canalisation → vérifier dans la Console que le log affiche bien
    « CD complet » (pas « CD demi ») ; interrompre volontairement via une cible tuée → « CD
    demi ».

- [ ] **Step 3: Rapporter les résultats**

Si tout passe, signaler que le sous-chantier 2 est prêt à être poussé (avec le sous-chantier 1).
Si un point échoue, signaler précisément lequel avec le comportement observé.

---

## Self-Review (effectué à l'écriture de ce plan)

**1. Spec coverage** : les 4 sections d'architecture du spec (extension Animator, CastBarSpawner,
flux Mob, flux PNJ) correspondent respectivement aux Tasks 2, 1, 3, 4. La section
« Vérification » du spec est intégralement reprise dans la Task 5. Aucune section sans tâche
correspondante.

**2. Placeholder scan** : aucun TBD/TODO — tout le code des 4 tâches est repris tel quel du spec
(déjà transcrit fidèlement depuis les blocs de code complets du spec), pas des esquisses.

**3. Type consistency** : `StartChannelCast(SkillData, Entity)`, `ResolveChannelCast()`,
`InterruptChannelCast(bool, string)`, `EndChannelCastState()` ont la même signature dans `Mob.cs`
(Task 3) et `PNJ.cs` (Task 4), seule différence l'idiome `?.`/sans-`?.` sur `_skillSystem`.
`CastBarSpawner.Show(string, float, Transform, System.Action, System.Action)`/`Cancel()` (Task 1)
consommées identiquement par les deux fichiers. `PlayChannel(AnimationClip)`/`CancelChannel()`
(Task 2) ont la même signature sur `MobAnimatorController`/`PNJAnimatorController`, consommées
identiquement. Tous les appels `SkillSystem.Execute`/`PlantDelayedZone`/`StartTrajectory`
utilisent les signatures exactes déjà vérifiées dans le code réel au moment de l'écriture de ce
plan.

**4. Vérification indépendante plan-vs-code (post-écriture) — 2 blockers + 1 medium corrigés
avant exécution** :
- **Blocker (garde de ré-entrée manquante)** : `_isChanneling` n'était consultée nulle part dans
  `TryUseSkill()`/`HandleAttack()`/`TryUseSecondarySkill()`/`HandleCombatAI()` — une canalisation
  en vol se serait redéclenchée à chaque frame (mana vidée, `vfxCast`/barres instanciés sans
  fin). Fixé : Step 3bis ajouté aux Tasks 3 et 4, guard étendu sur les blocs d'attaque de base.
- **Blocker (ordre `EndChannelCastState()`/`bar.Cancel()` inversé)** : la version initiale
  appelait `_channelBar?.Cancel()` AVANT `InterruptChannelCast()` dans 7 sites — le callback
  `onCancel` réentrant posait alors TOUJOURS le CD demi (même sur un hard CC), silencieusement.
  Fixé : `InterruptChannelCast()` capture `_channelBar`, appelle `EndChannelCastState()` d'abord,
  puis `bar?.Cancel()` — tous les appels externes à `_channelBar?.Cancel()` supprimés (Steps 6-7
  Mob, 7-8 PNJ).
- **Medium (pas de nettoyage canalisation dans `Mob.Die()`)** : le poll `hardCC` ne peut jamais
  voir `isDead` (early-return de `Update()` déjà dessus) — Mob n'avait donc aucun filet pour ce
  cas, contrairement à PNJ. Fixé : Step 7bis ajouté à la Task 3.

Spec (`2026-09-12-mob-pnj-channel-vfxcast-design.md`) corrigée en parallèle pour rester
cohérente avec ces 3 fixes.
