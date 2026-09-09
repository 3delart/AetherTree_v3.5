# Cast/Canalisation avec interruption CC — Design

## Contexte

Florian veut une vraie mécanique de canalisation (skills `castTime > 0`) interruptible par
hard CC, avec effet appliqué seulement à la fin (façon barre de récolte). Discussion complète
en brainstorm (2026-09-09) — ce document couvre uniquement le **chantier A : Cast/Canalisation
+ interruption CC**. Le calage des dégâts sur la frame d'impact réelle de l'animation (via
Animation Event, applicable à TOUS les types de skill — Normal/MultiHit/Combo/Canalisation)
est un **chantier B séparé**, hors scope ici, à traiter dans une spec dédiée une fois A posé.

Trois stubs existaient déjà dans le code, jamais branchés — confirmés par audit avant ce
design, aucun n'a de logique associée :
- `SkillData.castTime` (`Data/Skills/SkillData.cs:72`) — champ déclaré, zéro lecture ailleurs.
- `ProgressBarUI.BarType.Cast` + `colorCast` (`UI/PanelSecondaire/ProgressBarUI.cs:37,39`) —
  jamais passés par aucun appelant (`ResourceNode`→Harvest, Craft/Forge/Fusion/Rarity→Craft).
- `SkillSpecialEffect.Interrupt = 13` (`Data/Skills/SkillData.cs:364`) — commentaire
  "TODO phase suivante", mécanique d'interrupt-un-skill-adverse — **hors scope aussi**, ne
  peut de toute façon pas exister avant qu'il y ait un vrai état "en cours de cast" à
  interrompre (ce que ce chantier A construit).

Aujourd'hui, `SkillBar.ExecuteSkill()` (`Data/Skills/SkillBar.cs:494-574`) dépense
mana/HP/gold, pose le cooldown, ET appelle `SkillSystem.Instance.Execute(...)` — tout dans le
même call synchrone. Les dégâts partent littéralement à la frame du clic. Seule exception
partielle : MultiHit (`SkillBar.LockForMultiHit()` verrouille toute la barre le temps de la
séquence, CD différé à la fin via `_multiHitCooldownSlot`/`_multiHitCooldownSkill`) — mais
sans aucun check CC pendant cette séquence.

`StatusEffectSystem` n'a aucun event/callback quand un CC atterrit — juste des flags posés
directement (`isStunned`, `isShocked`, `isFreezed`, `isKnockedBack`, `isFeared`, `isSilenced`
— déjà lus tels quels dans `SkillBar.TryUseSlot()` lignes 211-235). Donc pas de push possible
pour détecter un interrupt — uniquement du poll frame par frame, exactement comme
`ResourceNode.Update()` (`Systems/ResourceNode.cs:53-68`) le fait déjà pour détecter
l'annulation par mouvement pendant une récolte. Ce chantier réutilise ce même pattern de poll.

`ProgressBarUI` (`UI/PanelSecondaire/ProgressBarUI.cs`) est un système générique déjà mature
et réutilisé 5x (ResourceNode, CraftSystem, ForgeUI, FusionUI, RarityUI) : singleton,
coroutine, barre world-space qui suit une cible (`followTarget`), `onComplete`/`onCancel`,
`Cancel()` externe, couleur par `BarType`. Réutilisé tel quel ici avec `BarType.Cast`.

## Décisions de design (issues du brainstorm)

| Question | Décision |
|---|---|
| Cast (court délai) vs Canalisation (façon récolte) — 1 ou 2 mécaniques ? | **Une seule.** La ligne de partage est `castTime == 0` (jamais interruptible, résolution déjà actée à la frame du clic — comme dans tous les MMO/action games : instant = résolu en logique au clic, l'anim qui joue après est cosmétique) vs `castTime > 0` (toujours interruptible, peu importe la durée — 0.3s ou 5s, même système). |
| Skill "Normal" (castTime 0) | Inchangé. Non-interruptible une fois lancé. |
| MultiHit (castTime 0) | **Déjà conforme au comportement voulu** — lancé = tous les hits vont au bout même si CC entre deux (`ExecuteMultiHit` n'a et n'aura aucun check CC). Zéro changement de code. |
| Combo (ComboSequence, chaque step castTime 0) | Chaque step individuel non-interruptible une fois lancé. La **fenêtre d'attente entre deux appuis** (`_comboTimer`) doit être interrompue par un hard CC — réutilise EXACTEMENT le code d'expiration de fenêtre déjà existant (CD posé + `ResetCombo()`), juste déclenché par CC en plus du timer à 0. |
| Canalisation (castTime > 0) | Nouveau système complet — voir Architecture ci-dessous. |
| Anim de canalisation | **1 seul clip** (pas de clip "canalisation" + clip "résolution" séparés). Coupé net (Animator sort du state) si interrompu — ne doit jamais jouer jusqu'au bout pendant qu'une résolution est annulée en silence derrière. |
| Lock des autres slots pendant la canalisation | **Total** — comme MultiHit (`_multiHitLockTimer`), impossible de lancer quoi que ce soit d'autre (slot 0 compris) pendant la canalisation. |
| Mana | Dépensé **au clic** (immédiatement, comme aujourd'hui). |
| CD | Posé **à la résolution** (succès OU interrupt), jamais au clic — sinon un `castTime` égal au `cooldown` laisserait 0 vrai temps mort (même raisonnement déjà en commentaire dans le code pour MultiHit : "un CD de 6s sur un skill qui dure 5s ne laisse qu'1s de vrai temps mort"). |
| GCD | **Même règle que le CD — posé à la résolution, pas au clic.** Confirmé applicable à TOUS les types de skill, pas spécifique à la Canalisation : c'est déjà le comportement actuel de Combo (GCD posé seulement à la fin de la séquence complète, jamais à chaque step) et de MultiHit (`_gcdTimer` étendu par `LockForMultiHit`). Sur les skills "Normal" (castTime 0), résolution = clic = même frame, donc aucun changement visible. |
| Interrupt CC — quels flags ? | `isStunned`, `isShocked`, `isFreezed`, `isKnockedBack`, `isFeared`, `isSilenced` — le même set déjà utilisé par `TryUseSlot()` pour bloquer un NOUVEAU lancement, plus Silence explicitement (contrairement à `TryUseSlot` où Silence ne bloque pas le slot 0, ici Silence interrompt toujours une canalisation déjà en cours, quel que soit le slot). |
| Interrupt CC → pénalité | **CD complet.** Le joueur ne l'a pas choisi. |
| Mouvement pendant la canalisation | **Annule** (comme `ResourceNode` — poll de la distance parcourue depuis le début, `Update()`), PAS un root dur. Donne au joueur une porte de sortie défensive volontaire (bouger pour esquiver plutôt que d'être forcé à tout encaisser). |
| Mouvement → pénalité | **CD à moitié** (`skill.cooldown * 0.5f`) — distinction volontaire vs subie : t'as choisi de fuir (pénalité allégée, encourage à s'en servir), vs tu t'es fait CC (pénalité pleine, punition du CC adverse). |
| Taunt pendant la canalisation | **N'interrompt PAS** — Taunt force juste la cible du slot 0, ne désactive pas le joueur (différent d'un vrai hard CC). S'applique normalement une fois le lock de canalisation levé (fin normale ou interrupt par autre chose) — aucun code spécifique nécessaire, `TryUseSlot()` gère déjà Taunt pour les actions suivantes. |
| Portée (Mob/PNJ) | **Joueur uniquement** pour ce chantier — `SkillBar` est Player-only (Mob/PNJ castent via un autre chemin, pas de progress bar). Canalisation Mob/PNJ = phase future, hors scope. |
| Conflit avec Harvest/Craft (ProgressBarUI singleton) | `StartProgress` annule silencieusement la barre précédente — comportement déjà existant, cohérent (impossible de récolter/crafter ET canaliser un skill combat en même temps de toute façon). |

## Architecture

### 1. `SkillData.cs` — nouveau champ

```csharp
[Tooltip("Animation jouée PENDANT la canalisation (castTime > 0) — boucle ou étirée sur\n" +
         "castTime secondes. Distincte de attackAnimation (jouée sur les skills castTime 0).\n" +
         "Coupée net si la canalisation est interrompue (CC/Silence/mouvement).")]
[ShowIf(nameof(castTime), 0f, Invert = true)]  // voir note ShowIf ci-dessous
public AnimationClip channelAnimation;
```

Note : `ShowIfAttribute` actuel compare par égalité à des valeurs discrètes (enum), pas par
inégalité sur un float. Si l'attribut ne supporte pas ce cas, condition de visibilité la plus
simple à implémenter : `[ShowIf(nameof(HasCastTime))]` avec un helper
`public bool HasCastTime => castTime > 0f;` — pattern déjà utilisé ailleurs dans le codebase
pour des conditions calculées (voir `IsNeutral`/`IsCombo` juste en dessous dans le même
fichier). Vérifier `Utils/ShowIfAttribute.cs` au moment de l'implémentation pour confirmer la
signature exacte disponible.

### 2. `SkillBar.cs` — nouvel état de canalisation

Nouveaux champs runtime (même zone que les champs MultiHit/Combo existants) :

```csharp
// ── Canalisation (castTime > 0) ────────────────────────────
private bool      _isChanneling      = false;
private SkillData _channelSkill      = null;
private int       _channelSlot       = -1;
private Entity    _channelTarget     = null;
private Vector3   _channelStartPos   = Vector3.zero;

public bool IsChanneling => _isChanneling;
```

`TryUseSlot()` — après la vérification de portée (ligne ~344, avant le bloc Combo existant),
nouveau branchement :

```csharp
if (skill.castTime > 0f)
{
    StartChannel(skill, slot, target);
    return true;
}
```

Le lock total (équivalent `_multiHitLockTimer`) doit aussi bloquer `TryUseSlot()` dès l'entrée
de la méthode, même bloc que le check `_multiHitLockTimer > 0f` existant (ligne 240) :

```csharp
if (_multiHitLockTimer > 0f || _isChanneling) return false;
```

Nouvelles méthodes :

```csharp
private void StartChannel(SkillData skill, int slot, Entity target)
{
    _player.SpendMana(GetEffectiveManaCost(skill));
    if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
    if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

    _isChanneling    = true;
    _channelSkill    = skill;
    _channelSlot     = slot;
    _channelTarget   = target;
    _channelStartPos = _player.transform.position;

    _player.animatorController?.PlayChannel(skill.channelAnimation);

    ProgressBarUI.Instance?.StartProgress(
        label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
        duration:     skill.castTime,
        onComplete:   ResolveChannel,
        onCancel:     null,   // annulation gérée explicitement par InterruptChannel, pas par ce callback
        type:         ProgressBarUI.BarType.Cast,
        followTarget: _player.transform
    );
}

private void ResolveChannel()
{
    if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

    SkillData skill = _channelSkill;
    int       slot  = _channelSlot;

    EndChannelState();

    SkillSystem.Instance?.Execute(skill, _player, _channelTarget);
    _cooldownTimers[slot] = skill.cooldown;
    if (slot >= 1) _gcdTimer = GCD_DURATION;
}

/// <summary>voluntary = true (mouvement, CD moitié) | false (CC/Silence subi, CD complet).</summary>
private void InterruptChannel(bool voluntary)
{
    if (!_isChanneling) return;

    SkillData skill = _channelSkill;
    int       slot  = _channelSlot;

    EndChannelState();

    ProgressBarUI.Instance?.Cancel();
    _player.animatorController?.CancelChannel();

    _cooldownTimers[slot] = voluntary ? skill.cooldown * 0.5f : skill.cooldown;
    if (slot >= 1) _gcdTimer = GCD_DURATION;

    Debug.Log($"[SKILLBAR] Canalisation interrompue ({(voluntary ? "mouvement" : "CC")}) — CD {_cooldownTimers[slot]:F2}s.");
}

private void EndChannelState()
{
    _isChanneling  = false;
    _channelSkill  = null;
    _channelSlot   = -1;
    _channelTarget = null;
}
```

`Update()` — nouveau bloc de poll, même style que le poll mouvement de `ResourceNode` :

```csharp
if (_isChanneling)
{
    var fx = _player.statusEffects;
    bool hardCC = fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed
                               || fx.isKnockedBack || fx.isFeared || fx.isSilenced);
    if (hardCC)
    {
        InterruptChannel(voluntary: false);
    }
    else
    {
        float moved = Vector3.Distance(_player.transform.position, _channelStartPos);
        if (moved > CANCEL_MOVE_THRESHOLD)   // même seuil que ResourceNode (0.3f) — constante à partager ou dupliquer
            InterruptChannel(voluntary: true);
    }
}
```

### 3. Combo — interruption CC de la fenêtre d'attente

Dans le bloc `_comboSlot >= 0 && _comboTimer > 0f` existant (`SkillBar.Update()`, ligne
131-141), ajouter le check CC AVANT le check d'expiration du timer — même conséquence
(CD + `ResetCombo()`), juste un second déclencheur :

```csharp
if (_comboSlot >= 0 && _comboTimer > 0f)
{
    var fx = _player.statusEffects;
    bool hardCC = fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed
                               || fx.isKnockedBack || fx.isFeared);
    // Silence volontairement EXCLU ici — un combo castTime 0 n'est pas une canalisation,
    // Silence bloque les NOUVEAUX lancements (déjà géré par TryUseSlot) mais n'a jamais
    // interrompu une fenêtre d'attente ouverte avant ce chantier — pas demandé par Florian
    // pour ce cas précis, à confirmer si besoin d'étendre plus tard.

    _comboTimer -= Time.deltaTime;
    if (_comboTimer <= 0f || hardCC)
    {
        Debug.Log($"[SKILLBAR] Combo {(hardCC ? "interrompu (CC)" : "expiré")} sur slot {_comboSlot} — CD déclenché.");
        _cooldownTimers[_comboSlot] = _comboSkill != null ? _comboSkill.cooldown : 1f;
        ResetCombo();
    }
}
```

### 4. `PlayerAnimatorController.cs` — coupure nette de l'anim de canalisation

Réutilise le mécanisme `AnimatorOverrideController` déjà en place pour `PlayAttack()`
(échange du clip du state "Attack" réutilisable). Deux nouvelles méthodes :

```csharp
public void PlayChannel(AnimationClip clip)
{
    if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
    _overrideController[attackPlaceholderClip] = clip;
    _animator.Play(AttackState, 0, 0f);
}

/// <summary>Coupe net l'anim de canalisation en cours — force un retour immédiat à la
/// locomotion normale, ne laisse jamais le clip jouer jusqu'au bout après un interrupt.</summary>
public void CancelChannel()
{
    if (_animator == null) return;
    _animator.Play(LocomotionState, 0, 0f);   // ⚠ nom exact du state à confirmer dans l'Animator Controller — voir note
}
```

⚠ **Point à vérifier à l'implémentation** : le nom exact du state de locomotion de base
(Idle/Move piloté par `SpeedParam`+`InCombatParam`) n'a pas été confirmé dans ce document —
`PlayerAnimatorController.cs` ne l'expose pas sous forme de constante nommée comme
`AttackState`. Florian doit confirmer le nom du state dans l'Animator Controller (Editor)
avant d'écrire `CancelChannel()`, ou ajouter une constante `LocomotionState` si elle n'existe
pas déjà à cet endroit du fichier.

### Aeris/HP costs

`StartChannel()` dépense mana/HP/gold au clic, à l'identique de `ExecuteSkill()` aujourd'hui
— reste inchangé pour l'instant, aucune raison de le déplacer à la résolution (contrairement
au CD/GCD) : Florian a explicitement confirmé "mana dépensé au lancement du skill".

## Fichiers touchés

- `Data/Skills/SkillData.cs` — nouveau champ `channelAnimation` (+ éventuel helper
  `HasCastTime` selon les capacités de `ShowIfAttribute`).
- `Data/Skills/SkillBar.cs` — nouveaux champs canalisation, branchement dans `TryUseSlot()`,
  nouvelles méthodes `StartChannel`/`ResolveChannel`/`InterruptChannel`/`EndChannelState`,
  nouveau bloc de poll dans `Update()`, ajout du check CC dans le bloc combo existant.
- `World/PlayerAnimatorController.cs` — nouvelles méthodes `PlayChannel`/`CancelChannel`.

Aucun changement dans `SkillSystem.cs`, `StatusEffectSystem.cs`, `ProgressBarUI.cs`,
`ResourceNode.cs` — tous réutilisés tels quels.

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian :

1. Compiler, 0 erreur.
2. Créer/éditer un `SkillData` de test, `castTime = 3f`, assigner un `channelAnimation`.
3. Lancer le skill → vérifier : bar `Cast` (couleur violette) apparaît au-dessus du joueur,
   anim de canalisation joue, tous les autres slots (0 compris) sont bloqués, mana déjà
   déduit immédiatement.
4. Laisser la canalisation aller au bout → vérifier : effet du skill s'applique, CD ET GCD se
   posent SEULEMENT à ce moment (pas avant), skillbar se débloque.
5. Relancer, se faire Stun/Freeze/Fear/Shock/Knockback/Silence pendant la canalisation →
   vérifier : anim coupée net (retour locomotion immédiat, pas de clip qui continue), bar
   disparaît, aucun effet appliqué, CD complet posé.
6. Relancer, bouger pendant la canalisation → vérifier : même annulation, mais CD à MOITIÉ
   du CD normal.
7. Relancer avec Taunt actif (pas de CC) → vérifier : la canalisation va au bout normalement,
   Taunt ne l'interrompt pas.
8. Vérifier qu'un Combo (ComboSequence existant) en attente d'un step suivant est bien cassé
   (CD + reset) si un hard CC atterrit pendant la fenêtre d'attente.
9. Vérifier qu'un MultiHit existant n'est PAS interrompu par un CC en plein milieu de sa
   séquence (comportement inchangé, confirmé volontairement).
