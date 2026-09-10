# Cast/Canalisation avec interruption CC — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter une vraie mécanique de canalisation (`SkillData.castTime > 0`) aux skills
combat du joueur — verrou total de la skillbar, animation dédiée coupée net, résolution
(dégâts/effet) uniquement à la fin, interruptible par hard CC/Silence/mouvement/mort de la
cible, avec CD/GCD posés à la résolution (pas au clic).

**Architecture:** Nouvel état runtime `_isChanneling` dans `SkillBar` piloté par un poll
`Update()` (même pattern que `ResourceNode` pour l'annulation par mouvement — pas d'event
CC disponible dans `StatusEffectSystem`). Réutilise `ProgressBarUI` (déjà générique, déjà
`BarType.Cast` prévu mais jamais branché) pour le visuel de la bar, et
`AnimatorOverrideController` (déjà en place pour `PlayAttack`) pour l'anim de canalisation.
Un dispatcher `LaunchSkill()` unique route vers `StartChannel()` (castTime > 0) ou
`ExecuteSkill()` existant (castTime 0, inchangé) aux DEUX points d'appel qui lancent
effectivement un skill (`TryUseSlot` et `CheckApproach` — le second avait été oublié dans
une première passe, voir spec).

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification manuelle
Play Mode uniquement (dernière tâche de ce plan).

**Spec:** `docs/superpowers/specs/2026-09-09-cast-canalisation-design.md`

## Global Constraints

- Seulement 4 fichiers touchés dans tout ce plan : `Data/Skills/SkillData.cs`,
  `Entities/Player.cs`, `World/PlayerAnimatorController.cs`, `Data/Skills/SkillBar.cs`.
  Aucun autre fichier du projet ne change (`SkillSystem.cs`, `StatusEffectSystem.cs`,
  `ProgressBarUI.cs`, `ResourceNode.cs` réutilisés tels quels).
- Chantier B (calage dégâts sur frame d'impact via Animation Event) est HORS SCOPE — ne pas
  l'implémenter, ne pas ajouter de stub/méthode vide en prévision (voir spec §"Point
  d'extension" — le seul point de branchement futur, `onComplete: ResolveChannel` dans
  `StartChannel()`, existe déjà par construction de ce plan, rien à ajouter exprès).
- Aucun nouvel enum ni modification d'enum existant — uniquement un nouveau champ
  `AnimationClip`, une nouvelle propriété publique, et de la logique runtime. Zéro risque de
  casse de save/asset (pas d'ordinal touché).
- Pas de tests automatisés dans ce projet — chaque tâche se termine par "compiler, 0 erreur",
  pas par une suite de tests. La vérification fonctionnelle complète est la Tâche 7
  (checklist manuelle Play Mode), à cocher par Florian.
- CAVEMAN MODE actif côté communication avec Florian pendant l'exécution de ce plan —
  n'affecte JAMAIS le code produit ni les commentaires/docs, qui restent rédigés normalement.
- Un point de ce plan nécessite un geste Editor Unity de la part de Florian (Tâche 3 —
  ajouter un paramètre Trigger + une transition dans l'Animator Controller du joueur) : ne
  jamais deviner un nom de state à sa place, le trigger existe précisément pour éviter ça.

---

### Task 1: `SkillData.cs` — champ `channelAnimation` + helper + warning

**Files:**
- Modify: `Data/Skills/SkillData.cs`

**Interfaces:**
- Consumes: rien de nouveau — s'appuie sur `castTime` (existe déjà, ligne 72) et
  `executionType`/`SkillExecutionType` (existent déjà).
- Produces : `public AnimationClip channelAnimation` (champ, pas de `[ShowIf]` dessus — voir
  Step 1) — consommé par la Tâche 4 (`SkillBar.StartChannel`).

- [ ] **Step 1 : Ajouter le champ `channelAnimation`**

Dans `Data/Skills/SkillData.cs`, le champ `attackAnimation` se trouve dans la section
`⑩ Visuel & Son` :

```csharp
    public AnimationClip attackAnimation;
```

Juste après cette ligne, ajouter :

```csharp

    [Tooltip("Animation jouée PENDANT la canalisation (castTime > 0) — boucle ou étirée sur\n" +
             "castTime secondes. Distincte de attackAnimation (jouée sur les skills castTime 0).\n" +
             "Coupée net si la canalisation est interrompue (CC/Silence/mouvement).")]
    public AnimationClip channelAnimation;
```

Pas de `[ShowIf]` sur ce champ — `Editor/ShowIfPropertyDrawer.cs:130-131` ne résout que des
champs réellement sérialisés (`FindProperty`), jamais une propriété C# calculée comme
`castTime > 0f` aurait été ; ça fail-open silencieusement (le champ resterait affiché tout le
temps, `ShowIf` sans effet). Le tooltip seul suffit — `channelAnimation` reste visible en
permanence dans l'Inspector.

- [ ] **Step 2 : Ajouter le warning `OnValidate()`**

Dans le bloc `#if UNITY_EDITOR` / `OnValidate()` existant (fin du fichier, après le warning
MultiHit/attackAnimation déjà en place), ajouter avant la fermeture de la méthode :

```csharp

        // Une canalisation (castTime > 0) combinée à MultiHit/ComboSequence n'est pas gérée —
        // TryAdvanceCombo() (SkillBar) prend la main avant le dispatch castTime, castTime est
        // silencieusement ignoré. Avertit plutôt que de laisser un designer se demander
        // pourquoi son skill ne canalise pas.
        if (castTime > 0f && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] castTime > 0 avec executionType = {executionType} — " +
                              "combinaison non gérée, la canalisation sera ignorée (Combo/MultiHit " +
                              "prennent la main). Remets executionType à Normal si ce skill doit " +
                              "canaliser.", this);
```

- [ ] **Step 3 : Compiler, vérifier 0 erreur**

Ouvrir Unity, attendre la recompilation, vérifier la Console : 0 erreur, 0 warning inattendu.

- [ ] **Step 4 : Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "$(cat <<'EOF'
feat: add channelAnimation field + castTime validation to SkillData

New field for the upcoming channel/cast system (castTime > 0), plus an
OnValidate warning when castTime and a Combo/MultiHit executionType
are combined -- that combination is not handled, Combo/MultiHit wins
silently, better to flag it than let it confuse content authoring.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `Entities/Player.cs` — accesseur public + split `UseSkill()`/`BeginSkillUse()`

**Files:**
- Modify: `Entities/Player.cs:193`, `Entities/Player.cs:1056-1102`

**Interfaces:**
- Consumes: le champ privé existant `animatorController` (`Entities/Player.cs:193`, type
  `PlayerAnimatorController`, déjà assigné dans `Awake()` ligne 213) ; `lastSkillUsed`,
  `RegisterCombatAction()`, `RegisterRealSkillUse()`, `statusEffects`, `elementalSystem`,
  `RequestRecalculate()`, `RefreshTitle()` (tous déjà existants dans ce fichier, réutilisés
  tels quels).
- Produces: `public PlayerAnimatorController AnimatorController => animatorController;`
  (consommé par la Tâche 4) ; `public void BeginSkillUse(SkillData skill)` (consommé par la
  Tâche 4 — `StartChannel()` l'appelle au lancement).

⚠ Trouvé en relecture complète du fichier (pas dans la version initiale du spec) : `UseSkill()`
est appelée par `SkillSystem.Execute()`, donc aujourd'hui au moment de la RÉSOLUTION pour une
canalisation (`ResolveChannel`, Tâche 4) — pas au clic. Or `UseSkill()` fait aussi
`RegisterCombatAction()` (entrée en combat) et casse le Stealth. Sans cette tâche, un joueur
furtif pourrait lancer une canalisation, l'annuler volontairement, et rester invisible tout du
long — confirmé par Florian que lancer une canalisation doit casser l'invisibilité
immédiatement. `PlayAttack()` doit aussi être ignorée pour une canalisation (l'anim est déjà
gérée par `PlayChannel`, la rejouer à la résolution casserait le clip par-dessus).

- [ ] **Step 1 : Ajouter la propriété publique `AnimatorController`**

Dans `Entities/Player.cs`, trouver la déclaration du champ privé :

```csharp
    private PlayerAnimatorController animatorController;
```

Juste après cette ligne, ajouter :

```csharp
    public PlayerAnimatorController AnimatorController => animatorController;
```

- [ ] **Step 2 : Splitter `UseSkill()` en `BeginSkillUse()` + `UseSkill()` allégée**

Trouver la méthode complète actuelle :

```csharp
    public void UseSkill(SkillData skill, Entity target = null)
    {
        if (skill == null) return;

        lastSkillUsed = skill;
        RegisterCombatAction();
        animatorController?.PlayAttack(skill.attackAnimation);

        bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);

        // AFK — voir RegisterRealSkillUse() : seul un VRAI skill (pas l'attaque de base) casse
        // l'AFK, volontairement (spammer juste l'attaque de base en restant immobile reste AFK).
        if (!isBasic) RegisterRealSkillUse();

        // Buff/Debuff ne comptent PAS pour la fenêtre d'affinité — un soin/buff tagué Eau ne
        // "joue" pas de l'eau au sens combat, seuls les skills qui infligent réellement des
        // dégâts (Damage/Other, ex: DrainHP) font bouger le rang élémentaire.
        bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                               && skill.effectType != SkillEffectType.Debuff;

        // Stealth — casser UNIQUEMENT sur dégâts infligés (Damage/Other), pas sur un
        // Buff/Debuff seul (soigner/buffer un allié, ou même debuff un ennemi sans dégâts, ne
        // révèle pas) — décision explicite Florian, même distinction que countsForAffinity.
        if (countsForAffinity && statusEffects != null && statusEffects.isStealthed)
            statusEffects.RemoveBuff(BuffType.Stealth);

        if (countsForAffinity)
        {
            // Écart de niveau avec la cible — anti farm d'un mob hors de portée (trop faible ou
            // trop fort) pour faire bouger l'affinité gratuitement. Pas de cible/PNJ (pas de
            // niveau comparable) → pas de restriction, voir ElementalSystem.RegisterCast.
            int? targetLevel = target is Mob targetMob ? targetMob.mobLevel : (int?)null;

            if (!skill.IsNeutral)
                foreach (var element in skill.elements)
                    elementalSystem.RegisterCast(element, isBasicAttack: isBasic, targetLevel: targetLevel);
            else
                elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic, targetLevel: targetLevel);
        }

        // RequestRecalculate() (pas juste stats.RecalculateStats()) — sinon le pass équipement
        // tourne seul, SANS jamais relancer ReapplyActiveModifiers() après : un buff actif sur
        // n'importe quelle stat se faisait effacer dès le skill suivant (attaque de base
        // incluse), car son contenu n'était jamais réappliqué par-dessus le recalcul équipement.
        RequestRecalculate();
        RefreshTitle();
    }
```

Remplacer intégralement par ces DEUX méthodes :

```csharp
    /// <summary>Effets qui doivent réagir au LANCEMENT d'un skill, pas à sa résolution —
    /// entrée en combat, clear AFK, Stealth-break. Appelée directement par UseSkill() pour les
    /// skills instants (castTime 0, même frame qu'avant), et par SkillBar.StartChannel() pour
    /// une canalisation (castTime > 0) — sinon un joueur furtif pourrait canaliser, annuler
    /// volontairement, et rester invisible tout du long. Une fois cassé, le Stealth reste cassé
    /// même si la canalisation est ensuite interrompue — pas de logique d'annulation ici.</summary>
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
        // "joue" pas de l'eau au sens combat, seuls les skills qui infligent réellement des
        // dégâts (Damage/Other, ex: DrainHP) font bouger le rang élémentaire.
        bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                               && skill.effectType != SkillEffectType.Debuff;

        // Stealth — casser UNIQUEMENT sur dégâts infligés (Damage/Other), pas sur un
        // Buff/Debuff seul (soigner/buffer un allié, ou même debuff un ennemi sans dégâts, ne
        // révèle pas) — décision explicite Florian, même distinction que countsForAffinity.
        if (countsForAffinity && statusEffects != null && statusEffects.isStealthed)
            statusEffects.RemoveBuff(BuffType.Stealth);
    }

    public void UseSkill(SkillData skill, Entity target = null)
    {
        if (skill == null) return;

        // castTime 0 : comportement inchangé, tout arrive ici au même instant qu'avant.
        // castTime > 0 : BeginSkillUse() déjà appelé par SkillBar.StartChannel() au clic —
        // combat/AFK/Stealth ne doivent pas attendre la résolution. PlayAttack ignorée ici :
        // PlayChannel() a déjà joué l'anim de canalisation au lancement, la rejouer
        // écraserait le clip en cours (ou son retour à la locomotion) à la résolution.
        if (skill.castTime <= 0f)
        {
            BeginSkillUse(skill);
            animatorController?.PlayAttack(skill.attackAnimation);
        }

        bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);

        // Buff/Debuff ne comptent PAS pour la fenêtre d'affinité — voir BeginSkillUse.
        bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                               && skill.effectType != SkillEffectType.Debuff;

        if (countsForAffinity)
        {
            // Écart de niveau avec la cible — anti farm d'un mob hors de portée (trop faible ou
            // trop fort) pour faire bouger l'affinité gratuitement. Pas de cible/PNJ (pas de
            // niveau comparable) → pas de restriction, voir ElementalSystem.RegisterCast.
            int? targetLevel = target is Mob targetMob ? targetMob.mobLevel : (int?)null;

            if (!skill.IsNeutral)
                foreach (var element in skill.elements)
                    elementalSystem.RegisterCast(element, isBasicAttack: isBasic, targetLevel: targetLevel);
            else
                elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic, targetLevel: targetLevel);
        }

        // RequestRecalculate() (pas juste stats.RecalculateStats()) — sinon le pass équipement
        // tourne seul, SANS jamais relancer ReapplyActiveModifiers() après : un buff actif sur
        // n'importe quelle stat se faisait effacer dès le skill suivant (attaque de base
        // incluse), car son contenu n'était jamais réappliqué par-dessus le recalcul équipement.
        RequestRecalculate();
        RefreshTitle();
    }
```

- [ ] **Step 3 : Compiler, vérifier 0 erreur**

- [ ] **Step 4 : Commit**

```bash
git add Entities/Player.cs
git commit -m "$(cat <<'EOF'
feat: split Player.UseSkill into BeginSkillUse + UseSkill

BeginSkillUse (combat entry, AFK clear, Stealth-break) now fires at
skill LAUNCH; UseSkill keeps the animation trigger (now skipped for
castTime>0, since PlayChannel already handled it) and the elemental
affinity bookkeeping, which correctly stays gated to resolution. Also
exposes AnimatorController as a public read-only property -- SkillBar
needs to reach it for the upcoming channel system.

Instant skills (castTime 0) are unaffected: UseSkill still calls
BeginSkillUse synchronously at the same frame as before. Only a
channel (castTime > 0, wired in a later task) will call BeginSkillUse
early, at cast start, via SkillBar.StartChannel().

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `PlayerAnimatorController.cs` — `PlayChannel`/`CancelChannel`

**Files:**
- Modify: `World/PlayerAnimatorController.cs`
- Editor (Florian, hors code) : Animator Controller du joueur

**Interfaces:**
- Consumes: `_overrideController`/`_animator`/`attackPlaceholderClip`/`AttackState` (champs
  privés déjà existants dans ce fichier, réutilisés tels quels).
- Produces: `public void PlayChannel(AnimationClip clip)`, `public void CancelChannel()` —
  consommés par la Tâche 4 via `Player.AnimatorController` (Tâche 2).

- [ ] **Step 1 : Ajouter la constante `CancelActionTrigger`**

En haut de la classe, à côté des constantes existantes :

```csharp
    private const string SpeedParam    = "Speed";
    private const string InCombatParam = "InCombat";
    private const string AttackState   = "Attack";
```

Ajouter une 4e ligne :

```csharp
    private const string CancelActionTrigger = "CancelAction";
```

- [ ] **Step 2 : Ajouter `PlayChannel` et `CancelChannel`**

Après la méthode `PlayAttack` existante, ajouter :

```csharp

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (override du state "Attack" réutilisable). Appelé par
    /// SkillBar.StartChannel().</summary>
    public void PlayChannel(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion, ne laisse jamais le clip jouer jusqu'au bout après un interrupt.
    /// Appelé par SkillBar.InterruptChannel().</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }
```

- [ ] **Step 3 : Compiler, vérifier 0 erreur**

- [ ] **Step 4 : Geste Editor Unity (Florian) — paramètre Trigger + transition**

Ce step n'est PAS du code — sans lui, `CancelChannel()` compile mais ne fait rien de visible
en jeu (le trigger n'a nulle part où aller).

1. Ouvrir l'Animator Controller du joueur (celui assigné sur le prefab Player).
2. Onglet **Parameters** → **+** → **Trigger** → nommer exactement `CancelAction` (doit
   matcher `CancelActionTrigger` ci-dessus, sensible à la casse).
3. Clic droit sur **Any State** dans le graphe → **Make Transition** → cible = le state (ou
   blend tree) de locomotion de base (celui vers lequel "Attack" retourne normalement une
   fois son clip terminé).
4. Sélectionner cette nouvelle transition → dans l'Inspector, décocher **Has Exit Time**,
   ajouter une condition : `CancelAction`.
5. Sauvegarder la scène/le Controller.

Aucun nom de state à communiquer au code — le trigger fonctionne indépendamment du state
actuellement actif au moment où il se déclenche.

- [ ] **Step 5 : Commit**

```bash
git add World/PlayerAnimatorController.cs
git commit -m "$(cat <<'EOF'
feat: add PlayChannel/CancelChannel to PlayerAnimatorController

PlayChannel mirrors PlayAttack's override-clip mechanism for
castTime>0 skills. CancelChannel uses a Trigger (CancelAction) rather
than a hardcoded Play() target -- no locomotion state name exists as
a constant anywhere in this file (Speed/InCombat drive a continuous
blend tree), so guessing one would be fragile. Requires a matching
Trigger parameter + Any State transition (Has Exit Time unchecked) in
the player's Animator Controller, added manually in the Editor.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `SkillBar.cs` — état de canalisation (`StartChannel`/`ResolveChannel`/`InterruptChannel`)

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `skill.castTime`/`skill.channelAnimation` (Tâche 1),
  `_player.AnimatorController`/`_player.BeginSkillUse(SkillData)` (Tâche 2),
  `.PlayChannel()`/`.CancelChannel()` (Tâche 3),
  `_player.statusEffects` (`isStunned`/`isShocked`/`isFreezed`/`isKnockedBack`/`isFeared`/
  `isSilenced`, déjà existants — voir `TryUseSlot()` lignes 211-235), `ProgressBarUI.Instance`
  (`StartProgress`/`Cancel`, déjà existant), `GetEffectiveManaCost()` (méthode privée déjà
  existante dans ce fichier), `_cooldownTimers[]`/`_gcdTimer`/`GCD_DURATION` (champs déjà
  existants).
- Produces: `_isChanneling` (bool), `public bool IsChanneling` (propriété),
  `StartChannel(SkillData skill, int slot, Entity target)`, `ResolveChannel()`,
  `InterruptChannel(bool voluntary, string reason)`, `EndChannelState()` — consommés par la
  Tâche 5 (`LaunchSkill`).

Cette tâche ajoute le state machine complet mais NE LE BRANCHE PAS encore à `TryUseSlot()`/
`CheckApproach()` (ça, c'est la Tâche 5) — `StartChannel` sera temporairement inatteignable
depuis le jeu, c'est normal, le fichier compile quand même.

- [ ] **Step 1 : Ajouter les champs runtime**

Dans `Data/Skills/SkillBar.cs`, trouver le bloc de champs existant pour le combo séquentiel :

```csharp
    private int       _comboStep    = 0;
    private int       _comboSlot    = -1;
    private float     _comboTimer   = 0f;
    private SkillData _comboSkill   = null;
```

Juste après, ajouter :

```csharp

    // ── Canalisation (castTime > 0) ────────────────────────────
    private const float CHANNEL_CANCEL_MOVE_THRESHOLD = 0.3f;   // même seuil que ResourceNode

    private bool      _isChanneling      = false;
    private SkillData _channelSkill      = null;
    private int       _channelSlot       = -1;
    private Entity    _channelTarget     = null;
    private Vector3   _channelStartPos   = Vector3.zero;

    public bool IsChanneling => _isChanneling;
```

- [ ] **Step 2 : Ajouter les 4 méthodes**

Ajouter ces 4 méthodes dans la classe `SkillBar` (par exemple juste après `ResetCombo()`) :

```csharp

    // ── Canalisation ──────────────────────────────────────────

    private void StartChannel(SkillData skill, int slot, Entity target)
    {
        // Combat/AFK/Stealth doivent réagir au LANCEMENT, pas à la résolution — voir Tâche 2
        // (faille Stealth trouvée en relecture, confirmée par Florian).
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

        // POINT D'EXTENSION CHANTIER B (calage sur frame d'impact, hors scope de ce plan) :
        // le déclencheur de ResolveChannel() est ICI, et seulement ici. Le jour où B est
        // spécifié, onComplete sera remplacé par un Animation Event posé sur channelAnimation
        // au lieu du timer de la bar — aucun autre code de cette méthode/classe n'aura besoin
        // de changer. Ne jamais coupler ResolveChannel() à autre chose que cet appelant.
        ProgressBarUI.Instance?.StartProgress(
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            onComplete:   ResolveChannel,
            onCancel:     null,   // annulation gérée explicitement par InterruptChannel
            type:         ProgressBarUI.BarType.Cast,
            followTarget: _player.transform
        );
    }

    private void ResolveChannel()
    {
        if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

        // Capturer AVANT EndChannelState() — celle-ci met _channelTarget à null, et Execute()
        // a besoin de la vraie cible.
        SkillData skill  = _channelSkill;
        int       slot   = _channelSlot;
        Entity    target = _channelTarget;

        EndChannelState();

        SkillSystem.Instance?.Execute(skill, _player, target);
        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1) _gcdTimer = GCD_DURATION;
    }

    /// <summary>voluntary = true (mouvement OU cible morte — ni un choix punitif du joueur ni
    /// un CC gagné par l'adversaire, CD moitié) | false (CC/Silence subi, CD complet).</summary>
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

- [ ] **Step 3 : Ajouter le poll dans `Update()`**

Dans `Update()`, trouver le bloc existant du timer combo :

```csharp
        // ── Timer combo séquentiel ────────────────────────────
        if (_comboSlot >= 0 && _comboTimer > 0f)
        {
```

Juste AVANT ce bloc (ou juste après — l'ordre entre les deux blocs n'a pas d'importance, ils
sont indépendants), ajouter :

```csharp
        // ── Poll canalisation (CC / Silence / mouvement / cible morte) ──
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
                InterruptChannel(voluntary: true, reason: "cible morte");
            }
            else
            {
                float moved = Vector3.Distance(_player.transform.position, _channelStartPos);
                if (moved > CHANNEL_CANCEL_MOVE_THRESHOLD)
                    InterruptChannel(voluntary: true, reason: "mouvement");
            }
        }

```

- [ ] **Step 4 : Bloquer `TryUseSlot()` pendant une canalisation**

Trouver dans `TryUseSlot()` :

```csharp
        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit en cours → tous les slots bloqués sans exception
        if (_multiHitLockTimer > 0f)
        {
            return false;
        }
```

Remplacer par :

```csharp
        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit ou canalisation en cours → tous les slots bloqués sans exception
        if (_multiHitLockTimer > 0f || _isChanneling)
        {
            return false;
        }
```

- [ ] **Step 5 : Compiler, vérifier 0 erreur**

À ce stade `StartChannel` n'est appelée par rien — c'est normal, la Tâche 5 la branche.

- [ ] **Step 6 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: add channel state machine to SkillBar (not yet wired to input)

StartChannel/ResolveChannel/InterruptChannel/EndChannelState plus a
per-frame poll in Update() for hard CC, Silence, voluntary movement,
and target death -- movement and target death land in the same
"voluntary" bucket (half CD), CC/Silence in the "involuntary" bucket
(full CD). CD and GCD are posted at resolution (success or interrupt),
never at cast start, mirroring the existing MultiHit deferred-cooldown
precedent. Not yet reachable from TryUseSlot/CheckApproach -- next task.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `SkillBar.cs` — dispatcher `LaunchSkill` + branchement

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `StartChannel()` (Tâche 4), `ExecuteSkill()` (méthode privée déjà existante dans
  ce fichier), `skill.castTime` (Tâche 1).
- Produces: `LaunchSkill(SkillData skill, int slot, Entity target)` — remplace les 2 anciens
  appels directs à `ExecuteSkill()` dans `TryUseSlot()` et `CheckApproach()`.

⚠ Sans cette tâche, un skill de canalisation ciblé hors de portée résoudrait instantanément
dès l'arrivée à portée au lieu de canaliser — bug trouvé en relecture du spec, voir
`docs/superpowers/specs/2026-09-09-cast-canalisation-design.md` section "Bug évité".

- [ ] **Step 1 : Ajouter `LaunchSkill`**

Ajouter cette méthode dans la classe `SkillBar` (par exemple juste avant `ExecuteSkill`) :

```csharp

    // ── Dispatch castTime 0 vs canalisation — UNIQUE point d'entrée pour lancer un skill ──
    // Utilisé par TryUseSlot() ET CheckApproach() — ne jamais appeler ExecuteSkill()
    // directement depuis un autre endroit, sinon castTime > 0 serait contourné.
    private void LaunchSkill(SkillData skill, int slot, Entity target)
    {
        if (skill.castTime > 0f) StartChannel(skill, slot, target);
        else                     ExecuteSkill(skill, slot, target);
    }
```

- [ ] **Step 2 : Brancher dans `TryUseSlot()`**

Trouver la toute fin de `TryUseSlot()` :

```csharp
        // ── Combo séquentiel (Méthode 2) ─────────────────────
        if (TryAdvanceCombo(skill, slot, target)) return true;

        ExecuteSkill(skill, slot, target);
        return true;
```

Remplacer la dernière ligne d'exécution :

```csharp
        // ── Combo séquentiel (Méthode 2) ─────────────────────
        if (TryAdvanceCombo(skill, slot, target)) return true;

        LaunchSkill(skill, slot, target);
        return true;
```

- [ ] **Step 3 : Brancher dans `CheckApproach()`**

Trouver dans `CheckApproach()` :

```csharp
        if (dist <= range)
        {
            if (_agent != null) _agent.ResetPath();
            ExecuteSkill(_pendingSkill, _pendingSlot, _pendingTarget);
            CancelApproach();
        }
```

Remplacer :

```csharp
        if (dist <= range)
        {
            if (_agent != null) _agent.ResetPath();
            LaunchSkill(_pendingSkill, _pendingSlot, _pendingTarget);
            CancelApproach();
        }
```

- [ ] **Step 4 : Compiler, vérifier 0 erreur**

- [ ] **Step 5 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: wire channel system into skill launch (TryUseSlot + CheckApproach)

Both call sites that actually launch a skill now go through a single
LaunchSkill() dispatcher instead of calling ExecuteSkill() directly.
CheckApproach() was the one call site the earlier design pass missed --
without this, a channeled skill needing to approach its target would
have resolved instantly on arrival instead of channeling.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `SkillBar.cs` — interruption CC de la fenêtre combo

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `_player.statusEffects` (déjà existant), `_comboSlot`/`_comboTimer`/`_comboSkill`/
  `_cooldownTimers[]`/`ResetCombo()` (déjà existants).
- Produces: rien de nouveau exposé — modifie uniquement le comportement interne du bloc timer
  combo existant.

- [ ] **Step 1 : Ajouter le check CC dans le bloc timer combo**

Dans `Update()`, trouver :

```csharp
        // ── Timer combo séquentiel ────────────────────────────
        if (_comboSlot >= 0 && _comboTimer > 0f)
        {
            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f)
            {
                // Fenêtre expirée — CD déclenché + reset
                Debug.Log($"[SKILLBAR] Combo expiré sur slot {_comboSlot} — CD déclenché.");
                _cooldownTimers[_comboSlot] = _comboSkill != null ? _comboSkill.cooldown : 1f;
                ResetCombo();
            }
        }
```

Remplacer par :

```csharp
        // ── Timer combo séquentiel ────────────────────────────
        if (_comboSlot >= 0 && _comboTimer > 0f)
        {
            var fx = _player.statusEffects;
            bool hardCC = fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed
                                       || fx.isKnockedBack || fx.isFeared);
            // Silence volontairement EXCLU ici — un combo castTime 0 n'est pas une
            // canalisation ; Silence bloque déjà les NOUVEAUX lancements via TryUseSlot,
            // mais n'a jamais interrompu une fenêtre d'attente ouverte avant ce plan.

            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f || hardCC)
            {
                Debug.Log($"[SKILLBAR] Combo {(hardCC ? "interrompu (CC)" : "expiré")} sur slot {_comboSlot} — CD déclenché.");
                _cooldownTimers[_comboSlot] = _comboSkill != null ? _comboSkill.cooldown : 1f;
                ResetCombo();
            }
        }
```

- [ ] **Step 2 : Compiler, vérifier 0 erreur**

- [ ] **Step 3 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: interrupt combo window on hard CC

A hard CC landing during the wait window between two ComboSequence
inputs now breaks the combo (CD + reset), reusing the exact same
consequence path as the existing timer-expiry case -- just a second
trigger condition. Individual combo steps stay uninterruptible once
launched, only the waiting window between them is vulnerable. Silence
intentionally excluded here (blocks new casts already, was never asked
to interrupt an open window).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Vérification manuelle Play Mode

**Files:** aucun — tâche de vérification uniquement, pas de code.

**Interfaces:**
- Consumes : l'intégralité du système construit par les Tâches 1-6.
- Produces : rien — confirme que le système fonctionne, ou remonte les écarts à corriger.

Pas de framework de test automatisé sur ce projet — cette checklist remplace la suite de
tests, à dérouler par Florian en Play Mode.

- [ ] **Step 1 : Créer un `SkillData` de test castTime > 0**

Dans Unity : `Create → AetherTree → Skills → SkillData` (ou dupliquer un skill actif
existant). Configurer :
- `skillType = Active`, un slot 1-8 de la skillbar de test.
- `castTime = 3` (secondes).
- `channelAnimation` = n'importe quel clip existant assignable temporairement (même un clip
  d'attaque déjà présent dans le projet, juste pour valider le flow — le vrai clip dédié
  viendra plus tard).
- `targetType = Target` (pour pouvoir tester le cas cible-morte et le cas approche/portée).
- `cooldown = 6` (valeur facilement lisible pour vérifier "moitié" vs "complet" au chrono).

- [ ] **Step 2 : Lancer le skill à portée, laisser aller au bout**

Vérifier :
- La bar `Cast` (couleur violette, `colorCast`) apparaît au-dessus du joueur et se remplit
  sur 3 secondes.
- L'anim de canalisation joue.
- Tous les autres slots (0 compris — tenter l'auto-attaque, tenter un autre skill) sont
  bloqués pendant ces 3 secondes.
- Le mana est déjà déduit dès le clic (pas à la fin).
- À la fin : l'effet du skill s'applique (dégâts sur la cible), le CD (6s) ET le GCD se
  posent SEULEMENT à cet instant (pas avant), la skillbar se débloque immédiatement après.

- [ ] **Step 3 : Interrupt par CC**

Relancer le skill, se faire infliger Stun (ou Freeze/Fear/Shocked/Knockback/Silence — tester
au moins Stun et Silence) pendant la canalisation. Vérifier :
- L'anim est coupée NET (retour instantané à la locomotion normale, pas de clip qui continue
  à jouer en arrière-plan).
- La bar disparaît immédiatement.
- Aucun effet ne s'applique sur la cible.
- Le CD posé est COMPLET (6s), pas la moitié.

- [ ] **Step 4 : Annulation par mouvement**

Relancer le skill, bouger (déplacement clavier/clic) pendant la canalisation, sans subir de
CC. Vérifier :
- Même annulation visuelle que l'interrupt CC (anim coupée, bar disparaît).
- Le CD posé est à MOITIÉ (3s), pas complet.

- [ ] **Step 5 : Cible qui meurt en cours de canalisation**

Relancer le skill sur une cible avec peu de PV, la faire tuer par autre chose (une autre
attaque, un autre joueur/mob) pendant que la canalisation tourne. Vérifier :
- Interrupt immédiat dès la mort de la cible (pas besoin d'attendre la fin des 3s).
- Anim coupée net, comme les autres interrupts.
- CD posé à MOITIÉ (même bucket que le mouvement).

- [ ] **Step 6 : Taunt n'interrompt pas**

Se faire Taunt (sans autre CC) pendant une canalisation en cours sur une cible différente de
la source du Taunt. Vérifier : la canalisation va au bout normalement, aucune interruption.

- [ ] **Step 7 : Régression approche**

Lancer le skill de test sur une cible HORS de portée. Vérifier :
- Le joueur se déplace d'abord vers la cible (comportement d'approche déjà existant).
- Une fois à portée, la CANALISATION démarre (bar apparaît, anim joue) — PAS de résolution
  instantanée à l'arrivée. C'était le bug de `CheckApproach()` trouvé en relecture du spec.

- [ ] **Step 8 : Régression MultiHit**

Sur un skill MultiHit EXISTANT (pas le skill de test castTime>0), se faire infliger un hard
CC entre deux hits de la séquence. Vérifier : la séquence continue jusqu'au bout, PAS
interrompue — comportement volontairement inchangé.

- [ ] **Step 9 : Interruption CC de la fenêtre combo**

Sur un skill ComboSequence EXISTANT, déclencher le step 1 puis se faire infliger un hard CC
PENDANT la fenêtre d'attente (avant d'appuyer pour le step suivant). Vérifier : le combo est
cassé (CD posé, retour au step 0) au moment du CC, pas seulement à l'expiration normale du
timer.

- [ ] **Step 10 : Warning de configuration**

Créer volontairement un `SkillData` avec `castTime = 2` ET `executionType = MultiHit`
(mauvaise config intentionnelle). Vérifier que le warning apparaît dans la Console Unity au
moment de la modification/sélection de l'asset.

- [ ] **Step 11 : Aucune régression sur les skills castTime 0 existants**

Utiliser 2-3 skills existants (Normal, différents `targetType`) déjà en jeu. Vérifier :
comportement strictement identique à avant ce plan — résolution instantanée, pas de bar Cast,
pas de blocage supplémentaire, Stealth toujours cassé exactement comme avant (même frame).

- [ ] **Step 12 : Stealth cassé au LANCEMENT d'une canalisation, pas à la résolution**

Se rendre furtif (buff Stealth actif), lancer le skill de test castTime > 0 sur une cible.
Vérifier : le Stealth se casse IMMÉDIATEMENT au clic (pas d'attendre la fin des 3 secondes).

- [ ] **Step 13 : Stealth reste cassé même si la canalisation est annulée**

Refaire le Step 12, puis bouger pour annuler volontairement la canalisation en cours.
Vérifier : le Stealth reste cassé (pas restauré par l'annulation) — cohérent avec Step 4.

Si tous les points ci-dessus passent, le chantier A est complet et fonctionnel.
