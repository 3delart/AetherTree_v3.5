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
| Cible meurt en cours de canalisation | **Interrompt aussi** (poll `_channelTarget.isDead`), classé dans le même bucket que mouvement → **CD moitié**. Ni un choix du joueur ni un CC gagné par l'adversaire — plein CD serait punitif sans raison de gameplay. Surtout pertinent pour AoE_Target (la zone perd son centre). Ne concerne que Target/AoE_Target/Dash_Target/LineTarget — sans effet sur GroundTarget (majorité des skills de canalisation prévus). |
| Taunt pendant la canalisation | **N'interrompt PAS** — Taunt force juste la cible du slot 0, ne désactive pas le joueur (différent d'un vrai hard CC). S'applique normalement une fois le lock de canalisation levé (fin normale ou interrupt par autre chose) — aucun code spécifique nécessaire, `TryUseSlot()` gère déjà Taunt pour les actions suivantes. |
| Portée (Mob/PNJ) | **Joueur uniquement** pour ce chantier — `SkillBar` est Player-only (Mob/PNJ castent via un autre chemin, pas de progress bar). Canalisation Mob/PNJ = phase future, hors scope. |
| Conflit avec Harvest/Craft (ProgressBarUI singleton) | `StartProgress` annule silencieusement la barre précédente — comportement déjà existant, cohérent (impossible de récolter/crafter ET canaliser un skill combat en même temps de toute façon). |

## Architecture

### 1. `SkillData.cs` — nouveau champ

```csharp
[Tooltip("Animation jouée PENDANT la canalisation (castTime > 0) — boucle ou étirée sur\n" +
         "castTime secondes. Distincte de attackAnimation (jouée sur les skills castTime 0).\n" +
         "Coupée net si la canalisation est interrompue (CC/Silence/mouvement).")]
[ShowIf(nameof(HasCastTime))]
public AnimationClip channelAnimation;

/// <summary>True si ce skill a un temps de canalisation — condition calculée pour ShowIf,
/// même pattern que IsNeutral/IsCombo ci-dessous. ShowIfAttribute (Utils/ShowIfAttribute.cs)
/// ne compare QUE par égalité sur une liste de valeurs discrètes (params object[] values) —
/// pas d'opérateur d'inégalité disponible sur un float, confirmé en lisant le fichier —
/// ce helper bool est donc la seule voie, pas une option parmi d'autres.</summary>
public bool HasCastTime => castTime > 0f;
```

Nouveau warning dans `OnValidate()` (même bloc `#if UNITY_EDITOR` que le warning MultiHit
existant, `SkillData.cs:259-289`) — évite qu'un designer configure un skill avec `castTime > 0`
ET `executionType != Normal` sans s'en rendre compte (combinaison non gérée, voir note dans
la section SkillBar ci-dessous — Combo prendrait la main en silence, castTime ignoré) :

```csharp
if (castTime > 0f && executionType != SkillExecutionType.Normal)
    Debug.LogWarning($"[SkillData:{name}] castTime > 0 avec executionType = {executionType} — " +
                      "combinaison non gérée, la canalisation sera ignorée (Combo/MultiHit " +
                      "prennent la main). Remets executionType à Normal si ce skill doit " +
                      "canaliser.", this);
```

### 2. `Entities/Player.cs` — accesseur public + split de `UseSkill()`

`animatorController` (`Entities/Player.cs:193`) est **privé** — `SkillBar` ne peut pas y
accéder tel quel. Ajouter une propriété en lecture seule, même convention que les autres
accesseurs publics déjà présents sur `Player` (`IsAFK`, etc.) :

```csharp
public PlayerAnimatorController AnimatorController => animatorController;
```

**Bug + question de design trouvés en relecture complète de `Player.UseSkill()`
(`Entities/Player.cs:1056-1102`)** — cette méthode est appelée en tout premier par
`SkillSystem.Execute()`, donc aujourd'hui à CHAQUE résolution de skill (instant = au clic,
canalisation = à `ResolveChannel()` d'après ce plan). Elle fait bien plus que jouer l'anim :

```
lastSkillUsed = skill
RegisterCombatAction()        → CombatActive = true (entrée en combat)
PlayAttack(attackAnimation)   → joue l'anim "Attack"
RegisterRealSkillUse()        → clear l'AFK
Stealth-break (si countsForAffinity && isStealthed)
RegisterCast() élémentaire    → fait bouger l'affinité (RÉSOLUTION uniquement, correct)
```

Pour une canalisation, si tout ça n'arrive qu'à `ResolveChannel()` (fin), deux problèmes :
1. **Bug** : `PlayAttack(skill.attackAnimation)` se redéclenche PAR-DESSUS l'anim de
   canalisation qui vient de finir, si le designer a rempli `attackAnimation` par habitude
   sur un skill castTime > 0 (rien ne l'en empêche aujourd'hui).
2. **Faille de gameplay confirmée par Florian** : entrée en combat et Stealth-break
   n'arriveraient qu'en cas de résolution réussie — un joueur furtif pourrait lancer une
   canalisation, l'annuler volontairement (mouvement, zéro risque), et rester invisible tout
   du long. **Décision : lancer une canalisation casse l'invisibilité immédiatement**, comme
   `RegisterCombatAction()`/AFK-clear — ces trois-là doivent se déclencher au LANCEMENT, pas
   à la résolution. Seul `RegisterCast()` élémentaire reste correctement gardé à la
   résolution (un skill interrompu n'a pas vraiment "joué" son élément).

Fix : split `UseSkill()` en deux méthodes. `BeginSkillUse()` porte tout ce qui doit arriver
IMMÉDIATEMENT (combat/AFK/Stealth) — appelée par `StartChannel()` (Tâche canalisation) pour
les canalisations, et par `UseSkill()` elle-même pour les skills instants (comportement
inchangé, même frame qu'aujourd'hui). `UseSkill()` garde l'anim (désormais conditionnelle) et
le bookkeeping élémentaire, qui doivent rester au moment de la résolution dans les deux cas :

```csharp
public void BeginSkillUse(SkillData skill)
{
    if (skill == null) return;

    lastSkillUsed = skill;
    RegisterCombatAction();

    bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);
    // AFK — voir RegisterRealSkillUse() : seul un VRAI skill (pas l'attaque de base) casse
    // l'AFK, volontairement (spammer juste l'attaque de base en restant immobile reste AFK).
    if (!isBasic) RegisterRealSkillUse();

    // Buff/Debuff ne comptent PAS pour la fenêtre d'affinité — un soin/buff tagué Eau ne
    // "joue" pas de l'eau au sens combat. Stealth — casse UNIQUEMENT sur dégâts infligés
    // (Damage/Other), pas sur un Buff/Debuff seul — décision explicite Florian.
    bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                           && skill.effectType != SkillEffectType.Debuff;
    if (countsForAffinity && statusEffects != null && statusEffects.isStealthed)
        statusEffects.RemoveBuff(BuffType.Stealth);
}

public void UseSkill(SkillData skill, Entity target = null)
{
    if (skill == null) return;

    // castTime 0 : comportement inchangé, tout arrive ici au même instant qu'avant.
    // castTime > 0 : BeginSkillUse() déjà appelé par SkillBar.StartChannel() au clic — combat/
    // AFK/Stealth ne doivent pas attendre la résolution. PlayAttack ignoré ici : PlayChannel()
    // a déjà joué l'anim de canalisation au lancement, la rejouer casserait le clip en cours.
    if (skill.castTime <= 0f)
    {
        BeginSkillUse(skill);
        animatorController?.PlayAttack(skill.attackAnimation);
    }

    bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);
    bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                           && skill.effectType != SkillEffectType.Debuff;

    if (countsForAffinity)
    {
        // Écart de niveau avec la cible — anti farm d'un mob hors de portée pour faire bouger
        // l'affinité gratuitement. Pas de cible/PNJ → pas de restriction, voir
        // ElementalSystem.RegisterCast.
        int? targetLevel = target is Mob targetMob ? targetMob.mobLevel : (int?)null;

        if (!skill.IsNeutral)
            foreach (var element in skill.elements)
                elementalSystem.RegisterCast(element, isBasicAttack: isBasic, targetLevel: targetLevel);
        else
            elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic, targetLevel: targetLevel);
    }

    // RequestRecalculate() (pas juste stats.RecalculateStats()) — sinon le pass équipement
    // tourne seul, SANS jamais relancer ReapplyActiveModifiers() après.
    RequestRecalculate();
    RefreshTitle();
}
```

`StartChannel()` (voir plus bas) appelle `_player.BeginSkillUse(skill)` dès le lancement,
avant même de dépenser mana/HP/gold. Pas besoin d'annuler/défaire quoi que ce soit en cas
d'interrupt — une fois cassé, le Stealth reste cassé (le joueur a été vu tenter l'action),
même convention que les autres jeux (annuler un cast ne restaure jamais la furtivité).

### 3. `SkillBar.cs` — nouvel état de canalisation

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

Le lock total (équivalent `_multiHitLockTimer`) doit aussi bloquer `TryUseSlot()` dès l'entrée
de la méthode, même bloc que le check `_multiHitLockTimer > 0f` existant (ligne 240) :

```csharp
if (_multiHitLockTimer > 0f || _isChanneling) return false;
```

⚠ **Bug évité — ne PAS brancher `castTime` uniquement dans `TryUseSlot()`.** Un skill ciblé
hors de portée passe par `StartApproach()` puis, une fois à portée, `CheckApproach()`
(`SkillBar.cs:445-482`) appelle `ExecuteSkill()` **directement** — ce second point d'appel
contournerait entièrement la canalisation (résolution instantanée dès l'arrivée à portée au
lieu de démarrer le channel). Les DEUX call sites (`TryUseSlot` après le bloc Combo, ET
`CheckApproach`) doivent passer par un dispatcher commun :

```csharp
private void LaunchSkill(SkillData skill, int slot, Entity target)
{
    if (skill.castTime > 0f) StartChannel(skill, slot, target);
    else                     ExecuteSkill(skill, slot, target);
}
```

Remplacer dans `TryUseSlot()` (fin de méthode) :
```csharp
// ── Combo séquentiel (Méthode 2) ─────────────────────
if (TryAdvanceCombo(skill, slot, target)) return true;

LaunchSkill(skill, slot, target);   // était : ExecuteSkill(skill, slot, target);
return true;
```

Et dans `CheckApproach()` :
```csharp
if (dist <= range)
{
    if (_agent != null) _agent.ResetPath();
    LaunchSkill(_pendingSkill, _pendingSlot, _pendingTarget);   // était : ExecuteSkill(...)
    CancelApproach();
}
```

Note : un skill `castTime > 0` avec `executionType = ComboSequence` fait passer
`TryAdvanceCombo()` en premier (inchangé) — dans ce cas de données mal configurées, Combo
prend la main et `castTime` est silencieusement ignoré. Voir warning `OnValidate()` proposé
plus bas pour éviter ce piège de configuration.

Nouvelles méthodes :

```csharp
private void StartChannel(SkillData skill, int slot, Entity target)
{
    // Combat/AFK/Stealth doivent réagir au LANCEMENT, pas à la résolution — voir section
    // Entities/Player.cs ci-dessus (faille Stealth trouvée en relecture, confirmée par Florian).
    _player.BeginSkillUse(skill);

    _player.SpendMana(GetEffectiveManaCost(skill));
    if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
    if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

    _isChanneling    = true;
    _channelSkill    = skill;
    _channelSlot     = slot;
    _channelTarget   = target;
    _channelStartPos = _player.transform.position;

    _player.AnimatorController?.PlayChannel(skill.channelAnimation);

    // POINT D'EXTENSION CHANTIER B (voir section dédiée plus bas) : le déclencheur de
    // ResolveChannel() est ICI, et seulement ici. Chantier B remplacera onComplete par un
    // Animation Event posé sur channelAnimation (résolution calée sur la vraie frame
    // d'impact) au lieu du timer de la bar — aucun autre code de ce fichier n'a besoin de
    // changer. Ne pas coupler ResolveChannel() à autre chose que cet unique appelant.
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

    // ⚠ Capturer target AVANT EndChannelState() — celle-ci met _channelTarget à null,
    // et Execute() a besoin de la vraie cible. Bug trouvé en relecture : appeler
    // SkillSystem.Execute APRÈS EndChannelState() résout toujours sur une cible null.
    SkillData skill  = _channelSkill;
    int       slot   = _channelSlot;
    Entity    target = _channelTarget;

    EndChannelState();

    SkillSystem.Instance?.Execute(skill, _player, target);
    _cooldownTimers[slot] = skill.cooldown;
    if (slot >= 1) _gcdTimer = GCD_DURATION;
}

/// <summary>voluntary = true (mouvement OU cible morte — ni un choix punitif du joueur ni un
/// CC gagné par l'adversaire, CD moitié) | false (CC/Silence subi, CD complet).</summary>
private void InterruptChannel(bool voluntary, string reason)
{
    if (!_isChanneling) return;

    SkillData skill = _channelSkill;
    int       slot  = _channelSlot;

    EndChannelState();

    ProgressBarUI.Instance?.Cancel();
    _player.AnimatorController?.CancelChannel();

    _cooldownTimers[slot] = voluntary ? skill.cooldown * 0.5f : skill.cooldown;
    if (slot >= 1) _gcdTimer = GCD_DURATION;

    Debug.Log($"[SKILLBAR] Canalisation interrompue ({reason}) — CD {_cooldownTimers[slot]:F2}s.");
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
        InterruptChannel(voluntary: false, reason: "CC");
    }
    else if (_channelTarget != null && _channelTarget.isDead)
    {
        // Ni un choix du joueur ni un CC gagné par l'adversaire — bucket "volontaire"
        // (CD moitié). Uniquement pertinent pour les canalisations avec cible (Target/
        // AoE_Target/Dash_Target/LineTarget) — Self/AoE_Self/GroundTarget/Direction/
        // Skillshot/Cone n'ont pas de _channelTarget, ce check ne les affecte jamais.
        // Surtout utile pour AoE_Target : la zone tape autour de la cible, plus de
        // centre = plus aucune raison d'aller au bout des secondes restantes.
        InterruptChannel(voluntary: true, reason: "cible morte");
    }
    else
    {
        float moved = Vector3.Distance(_player.transform.position, _channelStartPos);
        if (moved > CANCEL_MOVE_THRESHOLD)   // même seuil que ResourceNode (0.3f) — constante à partager ou dupliquer
            InterruptChannel(voluntary: true, reason: "mouvement");
    }
}
```

### 4. Combo — interruption CC de la fenêtre d'attente

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

### 5. `PlayerAnimatorController.cs` — coupure nette de l'anim de canalisation

Réutilise le mécanisme `AnimatorOverrideController` déjà en place pour `PlayAttack()`
(échange du clip du state "Attack" réutilisable) pour `PlayChannel()`.

Pour `CancelChannel()` — vérifié dans `PlayerAnimatorController.cs` : AUCUN nom de state de
locomotion n'existe en dur dans le code. `Speed`/`InCombat` pilotent un blend tree en continu
(`Update()` les pousse à chaque frame quel que soit le state actif) — le graphe Animator gère
lui-même les transitions vers/depuis la locomotion, jamais un `_animator.Play("NomDeState")`
explicite dans le code actuel. Deviner un nom de state serait fragile (renommage du state dans
l'Editor = code cassé silencieusement). Solution robuste, zéro nom à connaître côté code : un
**Trigger Animator**, même pattern que `IsMoving` déjà utilisé sur le Spirit Companion cette
session — transition "Any State → locomotion" dans le Controller, **Has Exit Time décoché**,
condition = ce trigger. Le code se contente de le déclencher, le graphe fait le reste.

```csharp
private const string CancelActionTrigger = "CancelAction";   // nouveau paramètre Trigger, à créer dans l'Animator Controller (Editor)

public void PlayChannel(AnimationClip clip)
{
    if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
    _overrideController[attackPlaceholderClip] = clip;
    _animator.Play(AttackState, 0, 0f);
}

/// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
/// retour à la locomotion, ne laisse jamais le clip jouer jusqu'au bout après un interrupt.</summary>
public void CancelChannel()
{
    if (_animator == null) return;
    _animator.SetTrigger(CancelActionTrigger);
}
```

**Travail Editor requis (Florian, avant/pendant l'implémentation)** : dans l'Animator
Controller du joueur, ajouter un paramètre **Trigger** nommé `CancelAction`, puis une
transition **"Any State" → (state/blend tree de locomotion)**, **Has Exit Time décoché**,
condition = `CancelAction`. Aucun nom de state à communiquer au code — le trigger fonctionne
indépendamment du state actuellement actif.

### Aeris/HP costs

`StartChannel()` dépense mana/HP/gold au clic, à l'identique de `ExecuteSkill()` aujourd'hui
— reste inchangé pour l'instant, aucune raison de le déplacer à la résolution (contrairement
au CD/GCD) : Florian a explicitement confirmé "mana dépensé au lancement du skill".

## Point d'extension — chantier B (calage sur frame d'impact)

Ce chantier A ne construit RIEN de chantier B par avance (pas de stub vide, pas de méthode
`OnHitFrame()` sans logique — on a déjà nettoyé ce type de dette ce matin avec `castTime`,
`BarType.Cast` et `SkillSpecialEffect.Interrupt`, pas de raison d'en recréer). Ce qui compte,
c'est que l'architecture d'A n'ait qu'UN SEUL endroit à modifier quand B arrivera, sans
toucher au reste (mana/CD/GCD/lock/interrupt).

**Ce point unique existe déjà** : `onComplete: ResolveChannel` dans `StartChannel()`
(section 2 ci-dessus). Aujourd'hui c'est `ProgressBarUI` (timer, bar à 100%) qui appelle
`ResolveChannel()`. Le jour où B est spécifié, il suffira de :
1. Ajouter un Animation Event sur `channelAnimation`, posé sur la vraie frame d'impact.
2. Faire appeler `ResolveChannel()` par cet event (callback sur `PlayerAnimatorController`,
   qui relaie à `SkillBar`) au lieu du `onComplete` de la bar.
3. Garder `ProgressBarUI.StartProgress` tel quel pour le visuel (la bar continue de remplir
   sur `castTime`, purement cosmétique) — SAUF si B décide que la bar doit littéralement finir
   à la frame d'impact plutôt qu'à `castTime` pile, auquel cas `duration` changerait aussi,
   décision à prendre dans la spec B, pas ici.

`ResolveChannel()`/`InterruptChannel()`/`EndChannelState()` restent inchangées dans tous les
cas — elles ne savent pas QUI les a appelées, seulement QUAND agir. C'est cette séparation qui
évite le rework : ne jamais fusionner la logique de résolution (CD/GCD/dégâts) avec le
déclencheur qui décide du "quand".

Même raisonnement s'appliquera à Normal/MultiHit/Combo quand B les couvrira — ils ne sont PAS
touchés par A (hors service pour Combo, voir section 3), donc aucun risque de rework induit
par A sur ces trois-là. Seule anticipation utile à noter : si B retarde un jour la résolution
d'un skill Normal (castTime 0) à un Animation Event plutôt qu'au clic, la règle "CD/GCD à la
résolution" déjà posée par A (et déjà généralisée à Normal dans ce document, même si
actuellement résolution = clic = même frame) s'appliquera sans changement de règle — seul le
moment de la résolution bouge, pas la logique qui en dépend.

## Annexe — mécanisme chantier B (référence, PAS une spec complète)

Capturé ici pour mémoire, à ré-explorer en vrai brainstorm quand B sera attaqué — ne pas
implémenter depuis cette annexe seule.

**Mécanisme : Unity Animation Event.** Dans la fenêtre Animation (pas Animator) d'un clip, on
pose un marqueur sur la timeline à la frame exacte d'impact visuel, avec un nom de fonction
(string). Unity appelle automatiquement cette fonction PENDANT la lecture du clip, à la frame
posée — aucun polling, l'Animator déclenche lui-même. La fonction doit être publique, sur un
component du même GameObject que l'Animator (ou un enfant) — `PlayerAnimatorController` est le
point naturel (déjà porteur de la référence Animator).

**Inversion de dépendance** — c'est le cœur du chantier B : aujourd'hui, clic → calcul immédiat
→ l'anim joue en parallèle (juste visuel). Avec B : clic → l'anim joue → c'est l'ANIM qui
déclenche le calcul (Animation Event), pas le clic. Damage devient piloté par l'animation, plus
par l'input.

**Implications par type de skill** (aucune ne doit être implémentée dans ce chantier A) :
- **Normal** (castTime 0) — 1 event sur `attackAnimation`. Nécessite de retarder l'appel à
  `SkillSystem.Execute()` (aujourd'hui synchrone dans `ExecuteSkill()`) jusqu'à ce que l'event
  tombe.
- **MultiHit** — autant d'events que de `hitSteps`, un par frame d'impact du clip ; il faut un
  index pour savoir quel `HitStep` résoudre à chaque déclenchement de l'event (remplace
  `WaitForSeconds(step.delay)` dans `ExecuteMultiHit`).
- **Combo** — chaque step a déjà son propre `SkillData`/clip → même traitement qu'un Normal,
  1 event par step.
- **Canalisation** — déjà couvert par le point d'extension ci-dessus (1 event sur
  `channelAnimation`, remplace `onComplete` de la bar par l'event).

**Garde-fou indispensable** — si un clip oublie son event, le skill ne doit jamais rester
bloqué en attente indéfiniment : prévoir un timeout de sécurité (ex: résoudre quand même après
`attackAnimation.length`/`castTime` si aucun event n'est tombé), même esprit que le warning
`OnValidate()` déjà en place pour le mismatch MultiHit/durée d'anim.

## Fichiers touchés

- `Data/Skills/SkillData.cs` — nouveau champ `channelAnimation`, nouveau helper
  `HasCastTime`, nouveau warning `OnValidate()` (castTime>0 + executionType≠Normal).
- `Entities/Player.cs` — nouvelle propriété publique `AnimatorController` (le champ existant
  `animatorController` est privé, `SkillBar` ne peut pas y accéder sans ça) ; `UseSkill()`
  splitté en `BeginSkillUse()` (combat/AFK/Stealth, immédiat) + `UseSkill()` allégé (anim
  conditionnelle + bookkeeping élémentaire, résolution) — bug PlayAttack + faille Stealth
  trouvés en relecture.
- `Data/Skills/SkillBar.cs` — nouveaux champs canalisation, nouveau dispatcher `LaunchSkill()`
  utilisé par `TryUseSlot()` ET `CheckApproach()` (remplace les 2 appels directs à
  `ExecuteSkill()`), nouvelles méthodes `StartChannel`/`ResolveChannel`/`InterruptChannel`/
  `EndChannelState`, nouveau bloc de poll dans `Update()`, ajout du check CC dans le bloc
  combo existant.
- `World/PlayerAnimatorController.cs` — nouvelles méthodes `PlayChannel`/`CancelChannel` +
  constante `CancelActionTrigger`. Nécessite aussi un ajout côté Editor (paramètre Trigger +
  transition Any State dans l'Animator Controller, voir section 5).

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
6bis. Relancer un skill `castTime > 0` avec `targetType = Target` ou `AoE_Target`, tuer la
   cible en cours de canalisation → vérifier : interrupt immédiat, anim coupée, CD à MOITIÉ
   (même bucket que le mouvement).
7. Relancer avec Taunt actif (pas de CC) → vérifier : la canalisation va au bout normalement,
   Taunt ne l'interrompt pas.
8. Vérifier qu'un Combo (ComboSequence existant) en attente d'un step suivant est bien cassé
   (CD + reset) si un hard CC atterrit pendant la fenêtre d'attente.
9. Vérifier qu'un MultiHit existant n'est PAS interrompu par un CC en plein milieu de sa
   séquence (comportement inchangé, confirmé volontairement).
10. **Régression approche** : skill de canalisation avec `targetType = Target`, lancé hors de
    portée → le joueur doit APPROCHER puis DÉMARRER la canalisation en arrivant à portée (pas
    résoudre instantanément à l'arrivée — c'était le bug de `CheckApproach()` trouvé en
    relecture).
11. Vérifier que la résolution s'applique bien sur la BONNE cible (pas null) — c'était le 2e
    bug trouvé en relecture (`EndChannelState()` appelé avant la lecture de `_channelTarget`).
12. Créer volontairement un skill `castTime > 0` avec `executionType = MultiHit` (mauvaise
    config) → vérifier que le warning `OnValidate()` apparaît dans la Console.
13. Se rendre furtif (Stealth actif), lancer une canalisation castTime > 0 dégâts → vérifier :
    Stealth cassé IMMÉDIATEMENT au clic (pas d'attendre la fin), et reste cassé même si la
    canalisation est ensuite annulée volontairement (mouvement).
14. Vérifier qu'un skill castTime 0 EXISTANT casse toujours le Stealth exactement comme avant
    ce plan (non-régression du split `BeginSkillUse`/`UseSkill`).
