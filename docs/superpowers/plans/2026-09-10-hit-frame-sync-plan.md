# Calage des dégâts sur la frame d'impact (chantier B) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Faire résoudre les dégâts/effets des skills du joueur (Normal, MultiHit, Combo) à la
vraie frame d'impact de leur animation (Unity Animation Event), au lieu d'instantanément au
clic ou sur un timer — sans toucher aux données de skill, seulement au moment de résolution.

**Architecture:** `SkillBar` gagne un état "hit en attente de résolution" (par slot, comme la
Canalisation du chantier A) — lance l'anim au clic, verrouille la skillbar, résout quand
`PlayerAnimatorController.OnSkillHitFrame()` relaie l'event (ou après un timeout basé sur la
durée du clip si l'event n'est jamais posé). `Player.UseSkill()`/`SkillSystem.Execute()` se
splittent chacun en "lancement" (déjà fait au clic) vs "résolution" (ne rejoue jamais l'anim),
même principe déjà validé pour la Canalisation.

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification manuelle Play
Mode par Florian (dernière tâche), après qu'il ait posé au moins un Animation Event de test.

**Spec:** `docs/superpowers/specs/2026-09-10-hit-frame-sync-design.md`

## Global Constraints

- Seulement 4 fichiers touchés dans tout ce plan : `World/PlayerAnimatorController.cs`,
  `Entities/Player.cs`, `Combat/SkillSystem.cs`, `Data/Skills/SkillBar.cs`. `Data/Skills/
  SkillData.cs` INCHANGÉ — aucun nouveau champ (les Animation Events vivent sur les clips,
  pas dans les assets de skill).
- Joueur uniquement. Mob/PNJ et les skills déclenchés par un passif
  (`PassiveSkillSystem.skillToCast` → `SkillSystem.Execute()` direct, jamais via `SkillBar`)
  restent sur l'ancien chemin synchrone, INCHANGÉ (`Execute()`/`ExecuteMultiHit()` coroutine
  ne sont PAS supprimées, toujours utilisées par ces chemins).
- La Canalisation (`castTime > 0`) N'EST PAS touchée par ce plan — `StartChannel()` reste sur
  le timer `ProgressBarUI`. Ne pas la basculer sur un Animation Event sans demande explicite.
- **Aucun poll CC pendant l'attente d'un hit** (`_pendingHitSlot`/`_pendingMultiSlot`) —
  Normal/MultiHit/Combo restent non-interruptibles une fois lancés, décision déjà actée avant
  même le chantier A. N'ajouter aucune logique d'interrupt sur ces nouveaux champs — seule la
  Canalisation reste interruptible, poll déjà existant, inchangé.
- Formule de timeout unique, dans TOUS les cas : `skill.attackAnimation != null ?
  skill.attackAnimation.length : 0f` (0 = résolution immédiate si pas d'anim, sinon durée du
  clip comme garde-fou si l'event n'arrive jamais).
- Pas de tests automatisés — chaque tâche se termine par "compiler, 0 erreur". La
  vérification fonctionnelle (Tâche 7) nécessite qu'au moins un Animation Event de test soit
  posé par Florian dans l'Editor Unity au préalable (pas du code, hors de portée d'un agent).
- Toujours vérifier les noms/signatures exacts dans le code réel avant transcription —
  spécialement `SkillSystem.ExecuteMultiHit()` (Tâche 3), dont le corps exact est reproduit
  ci-dessous, déjà vérifié contre le fichier réel — ne pas réinventer cette logique.

---

### Task 1: `World/PlayerAnimatorController.cs` — point d'entrée de l'Animation Event

**Files:**
- Modify: `World/PlayerAnimatorController.cs`

**Interfaces:**
- Consumes: rien de nouveau.
- Produces: `public void OnSkillHitFrame(int hitIndex = 0)` — appelée par Unity depuis un
  Animation Event posé sur un clip ; consommée par la Tâche 6 (`SkillBar.OnAnimationHitEvent`,
  qui doit exister avant que cette méthode ait un effet réel — compilera avant, mais
  n'appellera concrètement rien tant que la Tâche 6 n'est pas faite : acceptable, `?.` protège).

- [ ] **Step 1 : Ajouter `OnSkillHitFrame`**

Dans `World/PlayerAnimatorController.cs`, à la fin de la classe (juste avant l'accolade
fermante, après `CancelChannel()`), ajouter :

```csharp

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de
    /// lecture (state "Attack"). hitIndex : 0 par défaut (skills à un seul coup — Normal,
    /// chaque step de Combo), ou l'index du hit pour un MultiHit (0 = coup de base, 1..N =
    /// hitSteps). Relais pur — toute la logique de résolution vit dans SkillBar, qui possède
    /// déjà tout l'état de lancement (slot, skill, target, verrous).</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        SkillBar.Instance?.OnAnimationHitEvent(hitIndex);
    }
```

- [ ] **Step 2 : Vérification syntaxique (PAS de build complet à ce stade)**

`SkillBar.OnAnimationHitEvent` n'existe pas encore — il est ajouté seulement à la Tâche 6.
Unity compile par assembly, pas fichier par fichier : un build complet lancé maintenant
échouerait sur cette référence, sans que ce soit un vrai bug — attendu dans ce plan
séquentiel où les tâches s'accumulent (Task 1 ne compile proprement, avec le reste du
projet, qu'une fois la Tâche 6 faite). Pour CETTE tâche seule : vérifier uniquement que
`PlayerAnimatorController.cs` est syntaxiquement correct (accolades équilibrées, méthode bien
placée dans la classe) — ne PAS lancer de build complet du projet avant la Tâche 6, et ne pas
traiter une éventuelle erreur de compilation à ce stade comme un signal d'échec de la tâche.

- [ ] **Step 3 : Commit**

```bash
git add World/PlayerAnimatorController.cs
git commit -m "$(cat <<'EOF'
feat: add OnSkillHitFrame animation event entry point

Pure relay to SkillBar.OnAnimationHitEvent(hitIndex) -- Unity calls
this directly from an Animation Event marker placed on a clip's
timeline. hitIndex defaults to 0 (single-hit skills); MultiHit clips
carry one event per hit with an explicit index parameter.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `Entities/Player.cs` — extraction de `ResolveSkillUse()`

**Files:**
- Modify: `Entities/Player.cs:1089-1133` (méthode `UseSkill`)

**Interfaces:**
- Consumes: `elementalSystem`, `RequestRecalculate()`, `RefreshTitle()` (déjà existants sur
  cette classe, déjà utilisés par `UseSkill()` aujourd'hui).
- Produces: `public void ResolveSkillUse(SkillData skill, Entity target)` — consommée par la
  Tâche 3 (`SkillSystem.ResolveExecute`).

- [ ] **Step 1 : Extraire `ResolveSkillUse()` et alléger `UseSkill()`**

Trouver la méthode actuelle complète :

```csharp
    public void UseSkill(SkillData skill, Entity target = null)
    {
        if (skill == null) return;

        // BeginSkillUse() toujours appelée ici, inconditionnellement — idempotente (revérifie
        // juste un état déjà posé, sans effet si déjà fait). Nécessaire : SkillBar.StartChannel()
        // l'appelle déjà au lancement pour le chemin normal d'une canalisation, MAIS d'autres
        // chemins existants (steps de combo dans SkillBar.TryAdvanceCombo, skillToCast d'un
        // effet passif dans PassiveSkillSystem) appellent SkillSystem.Execute() directement
        // sans jamais passer par StartChannel — un skill castTime>0 lancé par l'un de ces
        // chemins perdrait silencieusement combat-entry/AFK-clear/Stealth-break sans cet appel
        // inconditionnel. PlayAttack reste conditionnelle : pour une canalisation, PlayChannel()
        // a déjà joué l'anim au lancement, la rejouer ici écraserait le clip en cours (ou son
        // retour à la locomotion) à la résolution.
        BeginSkillUse(skill);
        if (skill.castTime <= 0f)
            animatorController?.PlayAttack(skill.attackAnimation);

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

Remplacer intégralement par ces DEUX méthodes :

```csharp
    public void UseSkill(SkillData skill, Entity target = null)
    {
        if (skill == null) return;

        // BeginSkillUse() toujours appelée ici, inconditionnellement — idempotente (revérifie
        // juste un état déjà posé, sans effet si déjà fait). Nécessaire : SkillBar.StartChannel()
        // l'appelle déjà au lancement pour le chemin normal d'une canalisation, MAIS d'autres
        // chemins existants (steps de combo dans SkillBar.TryAdvanceCombo, skillToCast d'un
        // effet passif dans PassiveSkillSystem) appellent SkillSystem.Execute() directement
        // sans jamais passer par StartChannel — un skill castTime>0 lancé par l'un de ces
        // chemins perdrait silencieusement combat-entry/AFK-clear/Stealth-break sans cet appel
        // inconditionnel. PlayAttack reste conditionnelle : pour une canalisation, PlayChannel()
        // a déjà joué l'anim au lancement, la rejouer ici écraserait le clip en cours (ou son
        // retour à la locomotion) à la résolution.
        BeginSkillUse(skill);
        if (skill.castTime <= 0f)
            animatorController?.PlayAttack(skill.attackAnimation);

        ResolveSkillUse(skill, target);
    }

    /// <summary>Bookkeeping qui doit réagir à la RÉSOLUTION d'un skill (affinité élémentaire,
    /// recalcul stats, titre) — jamais BeginSkillUse ni PlayAttack ici, les deux sont déjà
    /// gérés au LANCEMENT par SkillBar (StartInstant/StartMultiHit/StartChannel/LaunchComboHit)
    /// pour tout ce qui passe par le nouveau flux événementiel (chantier B). Appeler PlayAttack
    /// ici referait démarrer l'anim "Attack" PAR-DESSUS elle-même juste après son propre
    /// impact — même famille de bug que celui trouvé et corrigé sur la Canalisation au
    /// chantier A.</summary>
    public void ResolveSkillUse(SkillData skill, Entity target)
    {
        if (skill == null) return;

        bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);

        // Buff/Debuff ne comptent PAS pour la fenêtre d'affinité — voir BeginSkillUse.
        bool countsForAffinity = skill.effectType != SkillEffectType.Buff
                               && skill.effectType != SkillEffectType.Debuff;

        if (countsForAffinity)
        {
            int? targetLevel = target is Mob targetMob ? targetMob.mobLevel : (int?)null;

            if (!skill.IsNeutral)
                foreach (var element in skill.elements)
                    elementalSystem.RegisterCast(element, isBasicAttack: isBasic, targetLevel: targetLevel);
            else
                elementalSystem.RegisterCast(ElementType.Neutral, isBasicAttack: isBasic, targetLevel: targetLevel);
        }

        RequestRecalculate();
        RefreshTitle();
    }
```

- [ ] **Step 2 : Vérifier la non-régression de `UseSkill()`**

Lire le fichier autour de la modification pour confirmer : `UseSkill()` fait EXACTEMENT les
mêmes appels dans le même ordre qu'avant (juste factorisé via `ResolveSkillUse`), aucun
appelant existant de `UseSkill()` n'a besoin de changer.

- [ ] **Step 3 : Compiler, vérifier 0 erreur**

- [ ] **Step 4 : Commit**

```bash
git add Entities/Player.cs
git commit -m "$(cat <<'EOF'
feat: extract Player.ResolveSkillUse() from UseSkill()

Splits the elemental-bookkeeping/recalc/title half of UseSkill() into
its own method, callable at resolution time without re-triggering
BeginSkillUse or PlayAttack. UseSkill() itself is unchanged in
behavior -- just now calls ResolveSkillUse() internally instead of
duplicating its body. Needed so SkillSystem.ResolveExecute() (chantier
B, next tasks) can resolve a skill without restarting its animation.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `Combat/SkillSystem.cs` — `ResolveExecute()` et `ResolveMultiHitStep()`

**Files:**
- Modify: `Combat/SkillSystem.cs`

**Interfaces:**
- Consumes: `player.ResolveSkillUse(SkillData, Entity)` (Tâche 2) ; `DispatchByTargetType`,
  `CalculateDamageForStep`, `ApplyOnHitDealtEffects`, `ApplyStatusEffectEntry`, `CheckKill`
  (méthodes privées déjà existantes dans ce fichier, réutilisées telles quelles).
- Produces: `public void ResolveExecute(SkillData skill, Entity caster, Entity target)` et
  `public void ResolveMultiHitStep(SkillData skill, Entity caster, Entity target, int
  hitIndex)` — consommées par la Tâche 4 (`SkillBar.ResolveInstant`/`ResolveMultiHitIndex`).

- [ ] **Step 1 : Ajouter `ResolveExecute()`**

Juste après la méthode `Execute()` existante (`Combat/SkillSystem.cs:94-144`, avant
`DispatchByTargetType`), ajouter :

```csharp

    /// <summary>Résout un skill DÉJÀ lancé par SkillBar (mana/anim/BeginSkillUse déjà faits au
    /// lancement) — dispatch des dégâts/effets, event, VFX/Son, bookkeeping élémentaire.
    /// JAMAIS d'appel à player.UseSkill()/BeginSkillUse()/PlayAttack ici, seulement
    /// player.ResolveSkillUse() — sinon l'anim redémarrerait par-dessus elle-même juste après
    /// son propre impact. DEUXIÈME écart volontaire avec Execute() : la branche MultiHit
    /// d'Execute() (dispatch + StartCoroutine(ExecuteMultiHit)) est ENTIÈREMENT absente ici —
    /// ResolveExecute() ne gère jamais un skill MultiHit, ce cas passe par
    /// ResolveMultiHitStep() ci-dessous à la place, appelée directement par SkillBar au bon
    /// index. Utilisée pour Normal/Combo-step côté joueur (chantier B) ; Mob/PNJ/passifs
    /// restent sur Execute() (inchangée, ci-dessus, MultiHit inclus).</summary>
    public void ResolveExecute(SkillData skill, Entity caster, Entity target)
    {
        if (skill == null || caster == null || caster.isDead) return;

        if (caster.entityType == EntityType.Player && caster is Player player)
        {
            player.ResolveSkillUse(skill, target);

            GameEventBus.Publish(new SkillUsedEvent
            {
                skill          = skill,
                target         = target,
                caster         = player,
                primaryElement = skill.PrimaryElement,
                isCombo        = skill.elements != null && skill.elements.Count >= 2,
                locationID     = player.currentZoneID,
                isInParty      = false,
            });
        }

        if (target is Mob mobTarget && caster is Player attackerPlayer)
            mobTarget.RegisterLastSkill(attackerPlayer, skill);

        DispatchByTargetType(skill, caster, target);

        if (skill.vfxPrefab != null)
        {
            Vector3 vfxPos = target != null
                ? target.transform.position
                : _groundTargetPoint ?? caster.transform.position;
            Instantiate(skill.vfxPrefab, vfxPos, Quaternion.identity);
        }
        if (skill.soundEffect != null)
            AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
    }
```

- [ ] **Step 2 : Lire `ExecuteMultiHit()` en entier avant d'écrire `ResolveMultiHitStep()`**

Lire `Combat/SkillSystem.cs`, méthode `ExecuteMultiHit` (commence ligne 227). Son corps
complet, vérifié dans le fichier réel :

```csharp
    private IEnumerator ExecuteMultiHit(SkillData skill, Entity caster, Entity target)
    {
        foreach (HitStep step in skill.hitSteps)
        {
            yield return new WaitForSeconds(step.delay);

            if (target == null || target.isDead) yield break;

            float dmg = CalculateDamageForStep(step, skill, caster, target, out bool stepCrit);

            if (target is Mob mobStep && caster is Player p)
                mobStep.RegisterLastSkill(p, skill);

            target.TakeDamage(dmg, step.element, caster);
            ApplyOnHitDealtEffects(caster, target, dmg);

            if (caster.entityType == EntityType.Player && caster is Player playerStep)
            {
                GameEventBus.Publish(new DamageDealtEvent
                {
                    amount   = dmg,
                    element  = step.element,
                    source   = playerStep,
                    target   = target,
                    isCrit   = stepCrit,
                    isOneHit = target.isDead && dmg >= target.MaxHP,
                });
            }

            Color textColor = caster.entityType == EntityType.Player ? Color.cyan : Color.red;
            FloatingText.Spawn(Mathf.RoundToInt(dmg).ToString(), target.transform.position, textColor);

            if (step.statusEffects != null)
                foreach (var entry in step.statusEffects)
                    ApplyStatusEffectEntry(entry, caster, target);

            GameObject vfx   = step.vfxPrefab  ?? skill.vfxPrefab;
            AudioClip  sound = step.soundEffect ?? skill.soundEffect;
            if (vfx   != null) Instantiate(vfx, target.transform.position, Quaternion.identity);
            if (sound != null) AudioSource.PlayClipAtPoint(sound, caster.transform.position);

            CheckKill(target);
        }
    }
```

`ExecuteMultiHit()` reste TELLE QUELLE (inchangée) — toujours utilisée par `Execute()` pour
Mob/PNJ/passifs. Ne pas la modifier, ne pas la supprimer.

- [ ] **Step 3 : Ajouter `ResolveMultiHitStep()`**

Juste après `ResolveExecute()`, transcrivant le corps de la boucle ci-dessus pour un SEUL step
(sans `yield`/`WaitForSeconds`, plus le cas `hitIndex == 0` pour le coup de base) :

```csharp

    /// <summary>Résout UN hit précis d'un skill MultiHit (joueur uniquement, chantier B) —
    /// hitIndex 0 = coup de base (dispatch standard, comme un skill à un seul coup), hitIndex
    /// 1..N = hitSteps[hitIndex - 1] (même calcul que le corps de boucle d'ExecuteMultiHit,
    /// un step résolu à la demande au lieu d'un foreach avec WaitForSeconds).</summary>
    public void ResolveMultiHitStep(SkillData skill, Entity caster, Entity target, int hitIndex)
    {
        if (skill == null || caster == null || caster.isDead) return;

        if (hitIndex == 0)
        {
            ResolveExecute(skill, caster, target);
            return;
        }

        if (target == null || target.isDead) return;

        int stepIndex = hitIndex - 1;
        if (skill.hitSteps == null || stepIndex < 0 || stepIndex >= skill.hitSteps.Count) return;
        HitStep step = skill.hitSteps[stepIndex];

        float dmg = CalculateDamageForStep(step, skill, caster, target, out bool stepCrit);

        if (target is Mob mobStep && caster is Player p)
            mobStep.RegisterLastSkill(p, skill);

        target.TakeDamage(dmg, step.element, caster);
        ApplyOnHitDealtEffects(caster, target, dmg);

        if (caster.entityType == EntityType.Player && caster is Player playerStep)
        {
            GameEventBus.Publish(new DamageDealtEvent
            {
                amount   = dmg,
                element  = step.element,
                source   = playerStep,
                target   = target,
                isCrit   = stepCrit,
                isOneHit = target.isDead && dmg >= target.MaxHP,
            });
        }

        Color textColor = caster.entityType == EntityType.Player ? Color.cyan : Color.red;
        FloatingText.Spawn(Mathf.RoundToInt(dmg).ToString(), target.transform.position, textColor);

        if (step.statusEffects != null)
            foreach (var entry in step.statusEffects)
                ApplyStatusEffectEntry(entry, caster, target);

        GameObject vfx   = step.vfxPrefab  ?? skill.vfxPrefab;
        AudioClip  sound = step.soundEffect ?? skill.soundEffect;
        if (vfx   != null) Instantiate(vfx, target.transform.position, Quaternion.identity);
        if (sound != null) AudioSource.PlayClipAtPoint(sound, caster.transform.position);

        CheckKill(target);
    }
```

Note : le cas `hitIndex == 0` appelle `ResolveExecute()` (pas `DispatchByTargetType` en
double — `ResolveExecute` fait déjà tout ce qu'il faut pour un "coup de base", y compris le
bookkeeping élémentaire/l'event/le VFX du skill parent). Les hits `>= 1` n'appellent PAS
`ResolveExecute` — ils suivent exactement le calcul par-step de l'ancienne coroutine (dégâts,
event `DamageDealtEvent` PAS `SkillUsedEvent`, VFX/Son DU STEP pas du skill parent), fidèle à
`ExecuteMultiHit`.

- [ ] **Step 4 : Compiler (cumulativement avec les tâches précédentes), vérifier 0 erreur**

- [ ] **Step 5 : Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "$(cat <<'EOF'
feat: add ResolveExecute/ResolveMultiHitStep for animation-driven resolution

ResolveExecute mirrors Execute()'s non-MultiHit path but calls
player.ResolveSkillUse() instead of the full UseSkill() -- avoids
re-triggering PlayAttack at resolution time. It never handles the
MultiHit branch Execute() has (dispatch + StartCoroutine) -- that case
goes through ResolveMultiHitStep instead. ResolveMultiHitStep resolves
one MultiHit hit on demand (0 = base hit via ResolveExecute, 1..N =
hitSteps[i-1], body transcribed from ExecuteMultiHit's loop),
replacing the WaitForSeconds-per-step coroutine for the player's
animation-event-driven path. Execute()/ExecuteMultiHit() untouched --
still used by Mob/PNJ and passive-triggered skills (out of scope for
this chantier).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `Data/Skills/SkillBar.cs` — Normal + MultiHit (état d'attente, lancement, résolution)

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `SkillSystem.ResolveExecute`/`ResolveMultiHitStep` (Tâche 3),
  `_player.AnimatorController.PlayAttack` (existant), `EngageAndFaceTarget` (existant),
  `GetEffectiveManaCost` (existant), `_cooldownTimers`/`_gcdTimer`/`GCD_DURATION` (existants),
  `TargetingSystem.Instance.DelayAutoAttack` (existant).
- Produces: `_pendingHitSlot/Skill/Target/Timeout`, `_pendingMultiSlot/Skill/Target/
  NextIndex/Timeout`, `public bool IsPendingHit`, `public bool IsPendingMultiHit`,
  `StartInstant(SkillData, int, Entity)`, `ResolveInstant()`, `StartMultiHit(SkillData, int,
  Entity)`, `ResolveMultiHitIndex(int)` — consommés par la Tâche 5 (Combo) et la Tâche 6
  (`OnAnimationHitEvent`, overlay CD).

Fusionne Normal et MultiHit en une seule tâche (au lieu de deux séparées) — `LaunchSkill()` a
besoin des DEUX méthodes de lancement pour être correct dès sa première version ; les écrire
dans deux tâches séparées laisserait `LaunchSkill()` incomplet/faux entre les deux.

- [ ] **Step 1 : Ajouter les nouveaux champs**

Dans `Data/Skills/SkillBar.cs`, juste après le bloc de champs Canalisation existant
(`_channelStartTime`, ligne ~104, juste avant `public bool IsChanneling`), ajouter :

```csharp

    // ── Attente de résolution (Normal / step de Combo castTime 0) — chantier B ────────────
    private int       _pendingHitSlot    = -1;
    private SkillData _pendingHitSkill   = null;
    private Entity    _pendingHitTarget  = null;
    private float     _pendingHitTimeout = 0f;

    public bool IsPendingHit => _pendingHitSlot >= 0;

    // ── Attente de résolution (MultiHit — séquence d'index) — chantier B ──────────────────
    private int       _pendingMultiSlot      = -1;
    private SkillData _pendingMultiSkill     = null;
    private Entity    _pendingMultiTarget    = null;
    private int       _pendingMultiNextIndex = 0;
    private float     _pendingMultiTimeout   = 0f;

    public bool IsPendingMultiHit => _pendingMultiSlot >= 0;
```

- [ ] **Step 2 : Étendre le verrou dans `TryUseSlot()`**

Trouver (`Data/Skills/SkillBar.cs:291-296`) :

```csharp
        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit ou canalisation en cours → tous les slots bloqués sans exception
        if (_multiHitLockTimer > 0f || _isChanneling)
        {
            return false;
        }
```

Remplacer par :

```csharp
        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit, canalisation, ou hit en attente de résolution (chantier B) → tous les
        // slots bloqués sans exception. Nécessaire : tous les skills partagent le MÊME state
        // Animator "Attack" — lancer un 2e skill pendant que le 1er attend encore son event
        // écraserait le clip en cours, et l'event du 1er ne tomberait jamais.
        if (_multiHitLockTimer > 0f || _isChanneling || IsPendingHit || IsPendingMultiHit)
        {
            return false;
        }
```

- [ ] **Step 3 : Remplacer `LaunchSkill()` par la version à 3 voies**

Trouver (`Data/Skills/SkillBar.cs:423-427`) :

```csharp
    private void LaunchSkill(SkillData skill, int slot, Entity target)
    {
        if (skill.castTime > 0f) StartChannel(skill, slot, target);
        else                     ExecuteSkill(skill, slot, target);
    }
```

Remplacer par :

```csharp
    private void LaunchSkill(SkillData skill, int slot, Entity target)
    {
        if (skill.castTime > 0f)
            StartChannel(skill, slot, target);
        else if (skill.executionType == SkillExecutionType.MultiHit
                 && skill.hitSteps != null && skill.hitSteps.Count > 0)
            StartMultiHit(skill, slot, target);
        else
            StartInstant(skill, slot, target);
    }
```

- [ ] **Step 4 : Ajouter `StartInstant()`/`ResolveInstant()`**

Juste après le nouveau `LaunchSkill()`, avant `StartChannel()` existante, ajouter :

```csharp

    // ── Normal / Combo-step (chantier B) ───────────────────────
    private void StartInstant(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        EngageAndFaceTarget(skill, slot, target);

        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingHitSlot    = slot;
        _pendingHitSkill   = skill;
        // GroundTarget n'a jamais de vraie cible Entity — même correction que StartChannel()
        // (chantier A) : sinon ResolveExecute()/le placement VFX utiliserait la position d'une
        // entité non-pertinente au lieu du point au sol.
        _pendingHitTarget  = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingHitTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingHitTimeout <= 0f)
            ResolveInstant();   // pas d'anim → rien à attendre, résout tout de suite
    }

    private void ResolveInstant()
    {
        if (_pendingHitSlot == -1) return;   // déjà résolu (event ET timeout se sont chevauchés)

        SkillData skill  = _pendingHitSkill;
        int       slot   = _pendingHitSlot;
        Entity    target = _pendingHitTarget;

        _pendingHitSlot    = -1;
        _pendingHitSkill   = null;
        _pendingHitTarget  = null;
        _pendingHitTimeout = 0f;

        SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }
```

⚠ Note pour la Tâche 5 (Combo) : cette version de `ResolveInstant()` ne gère pas encore le
cas combo — la Tâche 5 y ajoutera un branchement AVANT le bloc `_cooldownTimers[slot] = ...`.
Ne pas s'inquiéter si un combo se comporte comme un skill normal (CD posé à chaque step) tant
que la Tâche 5 n'est pas faite — comportement transitoire attendu de ce plan séquentiel.

- [ ] **Step 5 : Ajouter `StartMultiHit()`/`ResolveMultiHitIndex()`**

Juste après `ResolveInstant()`, ajouter :

```csharp

    // ── MultiHit (chantier B) ──────────────────────────────────
    private void StartMultiHit(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        EngageAndFaceTarget(skill, slot, target);

        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingMultiSlot      = slot;
        _pendingMultiSkill     = skill;
        // GroundTarget n'a jamais de vraie cible Entity — même raison que StartInstant() :
        // sinon ResolveMultiHitStep()/le placement VFX par step utiliserait la position d'une
        // entité non-pertinente au lieu du point au sol (raycasté juste au-dessus).
        _pendingMultiTarget    = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingMultiNextIndex = 0;
        _pendingMultiTimeout   = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingMultiTimeout <= 0f)
            ResolveMultiHitIndex(0);   // pas d'anim → résout tout enchaîné immédiatement
    }

    private void ResolveMultiHitIndex(int index)
    {
        if (_pendingMultiSlot == -1) return;
        if (index != _pendingMultiNextIndex) return;   // mauvais index (event hors séquence) — ignore

        SkillSystem.Instance?.ResolveMultiHitStep(_pendingMultiSkill, _player, _pendingMultiTarget, index);
        _pendingMultiNextIndex++;

        int totalHits = 1 + (_pendingMultiSkill.hitSteps?.Count ?? 0);   // 1 (base) + N hitSteps
        if (_pendingMultiNextIndex >= totalHits)
        {
            int slot = _pendingMultiSlot;
            SkillData skill = _pendingMultiSkill;

            _pendingMultiSlot    = -1;
            _pendingMultiSkill   = null;
            _pendingMultiTarget  = null;
            _pendingMultiTimeout = 0f;

            _cooldownTimers[slot] = skill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                float autoAttackDelay = skill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
        }
        else if (_pendingMultiTimeout <= 0f)
        {
            // Pas d'anim du tout — enchaîne immédiatement le hit suivant plutôt que d'attendre
            // un event qui ne viendra jamais.
            ResolveMultiHitIndex(_pendingMultiNextIndex);
        }
    }
```

- [ ] **Step 6 : Tick des deux timeouts dans `Update()`**

Dans `Update()`, juste après le bloc `// ── Poll canalisation ...` existant
(`Data/Skills/SkillBar.cs:152-172`) et avant le bloc `if (_comboStepCooldown > 0f)`, ajouter :

```csharp

        // ── Attente de résolution (chantier B) ─────────────────
        if (_pendingHitSlot != -1)
        {
            _pendingHitTimeout -= Time.deltaTime;
            if (_pendingHitTimeout <= 0f) ResolveInstant();
        }
        if (_pendingMultiSlot != -1)
        {
            _pendingMultiTimeout -= Time.deltaTime;
            if (_pendingMultiTimeout <= 0f) ResolveMultiHitIndex(_pendingMultiNextIndex);
        }
```

- [ ] **Step 7 : Compiler (cumulativement), vérifier 0 erreur**

`ExecuteSkill()` existe encore à ce stade (pas encore appelée nulle part après le Step 3, mais
pas encore supprimée non plus — normal, supprimée à la Tâche 6). Le code doit compiler malgré
ça (méthode simplement inutilisée, pas une erreur).

- [ ] **Step 8 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: add pending-hit state machine for Normal/MultiHit (chantier B)

StartInstant/ResolveInstant and StartMultiHit/ResolveMultiHitIndex
launch a skill's animation immediately (mana/CD-setup at click stays
the same) but defer damage dispatch until an Animation Event fires or
a clip-length-based timeout expires -- LaunchSkill() now routes to
one of three paths (Channel/MultiHit/Instant). Full skillbar lock
while a hit is pending, same reason as MultiHit's existing lock: every
skill shares one Animator "Attack" state. Combo integration (Task 5)
still pending -- ResolveInstant doesn't yet special-case combo steps.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `Data/Skills/SkillBar.cs` — Combo (`TryAdvanceCombo` refondu)

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `_pendingHitSlot/Skill/Target/Timeout`, `ResolveInstant()` (Tâche 4).
- Produces: `LaunchComboHit(SkillData, int, Entity)`, `AdvanceComboAfterHit(int, SkillData)` —
  `ResolveInstant()` (Tâche 4) modifiée pour appeler `AdvanceComboAfterHit` quand pertinent.

- [ ] **Step 1 : Remplacer `TryAdvanceCombo()`**

Trouver la méthode actuelle complète (`Data/Skills/SkillBar.cs`, section "Exécution combo
step") :

```csharp
    private bool TryAdvanceCombo(SkillData skill, int slot, Entity target)
    {
        if (skill.executionType != SkillExecutionType.ComboSequence) return false;
        if (skill.comboSteps == null || skill.comboSteps.Count == 0) return false;

        if (_comboSlot == -1)
        {
            // Premier appui — exécute le skill PARENT (step 0)
            _comboSkill = skill;
            _comboSlot  = slot;
            _comboStep  = 1; // prochain appui = comboSteps[0]

            _player.SpendMana(GetEffectiveManaCost(skill));
            SkillSystem.Instance?.Execute(skill, _player, target);
            EngageAndFaceTarget(skill, slot, target);

            // Ouvre la fenêtre combo — aucun lock sur les autres slots
            _comboTimer        = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;
            _comboStepCooldown = _comboSkill.comboStepInterval;

            // Icône → montre le prochain step
            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[0]);
            Debug.Log($"[SKILLBAR] Combo démarré — step 0 (parent), fenêtre {_comboTimer}s");
            return true;
        }

        if (_comboSlot != slot)
        {
            // Appui sur un autre slot pendant un combo — ignore
            return false;
        }

        // Délai minimum entre deux steps pas encore écoulé — ignore l'appui (input consommé,
        // la fenêtre _comboTimer continue de tourner normalement, rien d'autre ne se passe).
        if (_comboStepCooldown > 0f) return true;

        // Steps suivants — comboSteps[_comboStep - 1]
        int stepIndex = _comboStep - 1;
        SkillData stepSkill = _comboSkill.comboSteps[stepIndex];
        if (stepSkill == null) { ResetCombo(); return true; }

        _player.SpendMana(GetEffectiveManaCost(stepSkill));
        SkillSystem.Instance?.Execute(stepSkill, _player, target);
        EngageAndFaceTarget(stepSkill, slot, target);

        _comboStep++;

        if (_comboStep > _comboSkill.comboSteps.Count)
        {
            // Dernier step complété — CD sur le slot + reset
            Debug.Log($"[SKILLBAR] Combo terminé sur slot {slot}.");
            _cooldownTimers[slot] = _comboSkill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                // Durée basée sur l'anim du DERNIER step (celle qui vient de jouer), pas celle
                // du parent — même raison que dans ExecuteSkill.
                float autoAttackDelay = stepSkill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, stepSkill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
            ResetCombo();
            SkillBarUI.Instance?.RefreshSlot(slot);
        }
        else
        {
            // Ouvre la fenêtre pour le prochain step — aucun lock sur les autres slots
            _comboTimer        = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;
            _comboStepCooldown = _comboSkill.comboStepInterval;

            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[_comboStep - 1]);
            Debug.Log($"[SKILLBAR] Combo step {_comboStep}/{_comboSkill.comboSteps.Count} — fenêtre {_comboTimer}s");
        }

        return true;
    }
```

Remplacer intégralement par :

```csharp
    private bool TryAdvanceCombo(SkillData skill, int slot, Entity target)
    {
        if (skill.executionType != SkillExecutionType.ComboSequence) return false;
        if (skill.comboSteps == null || skill.comboSteps.Count == 0) return false;

        if (_comboSlot == -1)
        {
            // Premier appui — lance le skill PARENT (step 0) comme un hit en attente
            _comboSkill = skill;
            _comboSlot  = slot;
            _comboStep  = 1; // prochain step une fois CE hit résolu

            LaunchComboHit(skill, slot, target);
            Debug.Log($"[SKILLBAR] Combo démarré — step 0 (parent), en attente de résolution.");
            return true;
        }

        if (_comboSlot != slot)
        {
            // Appui sur un autre slot pendant un combo — ignore
            return false;
        }

        // Délai minimum entre deux steps pas encore écoulé — ignore l'appui (inchangé)
        if (_comboStepCooldown > 0f) return true;

        int stepIndex = _comboStep - 1;
        SkillData stepSkill = _comboSkill.comboSteps[stepIndex];
        if (stepSkill == null) { ResetCombo(); return true; }

        LaunchComboHit(stepSkill, slot, target);
        return true;
    }

    /// <summary>Lance un coup de combo (parent ou step) exactement comme StartInstant — mêmes
    /// champs _pendingHit*, même mécanisme d'attente/event/timeout, MÊME appel à
    /// BeginSkillUse() (sinon aucun combo ne déclencherait plus combat-entry/AFK-clear/
    /// Stealth-break ni ne mettrait à jour lastSkillUsed — régression sur une mécanique déjà
    /// en jeu, pas un détail : aujourd'hui chaque step passe par SkillSystem.Execute() →
    /// player.UseSkill() → BeginSkillUse(), et ResolveSkillUse (Tâche 2) ne l'appelle plus).
    /// ResolveInstant() détecte après coup qu'un combo est actif sur ce slot
    /// (IsComboActive && slot == _comboSlot) et route vers AdvanceComboAfterHit() au lieu de
    /// poser le CD directement.</summary>
    private void LaunchComboHit(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        _player.SpendMana(GetEffectiveManaCost(skill));
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);
        EngageAndFaceTarget(skill, slot, target);

        // GroundTarget par cohérence avec StartInstant()/StartMultiHit() (Tâche 4) — aucun
        // skill de combo existant n'utilise GroundTarget aujourd'hui, mais rien n'empêche
        // d'en configurer un plus tard : sans ce bloc, un tel step résoudrait ses effets sur
        // la position du caster au lieu du point visé.
        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingHitSlot    = slot;
        _pendingHitSkill   = skill;
        _pendingHitTarget  = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingHitTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;

        if (_pendingHitTimeout <= 0f) ResolveInstant();
    }

    /// <summary>Bookkeeping combo APRÈS résolution d'un coup — fenêtre suivante (icône +
    /// timer) ou fin de combo (CD + reset). `resolvedSkill` = le SkillData du coup qui vient
    /// de résoudre (parent au step 0, sinon le step lui-même) — utilisé pour la durée
    /// d'auto-attack-delay (anim du DERNIER coup, pas du parent).</summary>
    private void AdvanceComboAfterHit(int slot, SkillData resolvedSkill)
    {
        _comboStep++;
        _comboStepCooldown = _comboSkill.comboStepInterval;

        if (_comboStep > _comboSkill.comboSteps.Count)
        {
            Debug.Log($"[SKILLBAR] Combo terminé sur slot {slot}.");
            _cooldownTimers[slot] = _comboSkill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                float autoAttackDelay = resolvedSkill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, resolvedSkill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
            ResetCombo();
            SkillBarUI.Instance?.RefreshSlot(slot);
        }
        else
        {
            _comboTimer = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;
            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[_comboStep - 1]);
            Debug.Log($"[SKILLBAR] Combo step {_comboStep}/{_comboSkill.comboSteps.Count} — fenêtre {_comboTimer}s");
        }
    }
```

- [ ] **Step 2 : Brancher `ResolveInstant()` sur le combo**

Trouver (dans `ResolveInstant()`, ajoutée Tâche 4) :

```csharp
        SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }
```

Remplacer par :

```csharp
        SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        // Ce hit appartient-il à un Combo en cours sur ce slot ? Si oui, la suite (fenêtre
        // suivante, ou fin de combo + CD) est gérée par AdvanceComboAfterHit. Le CD n'est PAS
        // posé ici dans ce cas (seul le DERNIER step d'un combo pose le CD, inchangé).
        if (IsComboActive && slot == _comboSlot)
        {
            AdvanceComboAfterHit(slot, skill);
            return;
        }

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }
```

- [ ] **Step 3 : Compiler (cumulativement), vérifier 0 erreur**

- [ ] **Step 4 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: wire Combo into the pending-hit state machine (chantier B)

TryAdvanceCombo() no longer calls SkillSystem.Execute() directly --
each step (parent and subsequent) now launches via LaunchComboHit(),
the same _pendingHit* mechanism as StartInstant, including the
BeginSkillUse() call StartInstant/StartMultiHit also make (previously
provided by Execute() -> player.UseSkill() -- ResolveSkillUse no
longer calls it, so LaunchComboHit must, or combat-entry/AFK-clear/
Stealth-break silently stop firing for every combo). The combo window
(time to press the next step) now opens AFTER the current hit
resolves, in the new AdvanceComboAfterHit() called from
ResolveInstant() -- opening it at the click (old behavior) risked the
window expiring while the current hit's animation was still playing,
before the player even saw it land. Only the final step still posts
cooldown, same as before.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `Data/Skills/SkillBar.cs` — `OnAnimationHitEvent`, overlay CD, retrait d'`ExecuteSkill`

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `ResolveInstant()`, `ResolveMultiHitIndex(int)` (Tâches 4-5),
  `IsPendingHit`/`IsPendingMultiHit`/`_pendingHitTimeout`/`_pendingMultiTimeout` (Tâche 4).
- Produces: `public void OnAnimationHitEvent(int hitIndex)` — consommée par la Tâche 1
  (`PlayerAnimatorController.OnSkillHitFrame`, déjà écrite, referme la boucle).

- [ ] **Step 1 : Ajouter `OnAnimationHitEvent`**

Ajouter cette méthode publique dans la classe `SkillBar` (par exemple juste après
`ResolveMultiHitIndex`) :

```csharp

    /// <summary>Reçoit l'Animation Event relayé par PlayerAnimatorController.OnSkillHitFrame.
    /// hitIndex ignoré pour un hit simple (Normal/Combo-step) — un seul en attente possible à
    /// la fois. Pour un MultiHit, route vers l'index précis.</summary>
    public void OnAnimationHitEvent(int hitIndex)
    {
        if (IsPendingHit)      { ResolveInstant(); return; }
        if (IsPendingMultiHit) { ResolveMultiHitIndex(hitIndex); return; }
        // Aucun hit en attente — event reçu hors contexte (anim jouée sans skill en attente,
        // ou déjà résolu par le timeout juste avant). Ignoré silencieusement, pas une erreur.
    }
```

- [ ] **Step 2 : Étendre l'overlay CD**

Trouver `GetCooldownRemaining` (section "Utilitaires cooldown") :

```csharp
    public float GetCooldownRemaining(int slot)
    {
        if (slot < 0 || slot >= 10) return 0f;
        // Canalisation en cours sur ce slot — _cooldownTimers reste à 0 jusqu'à la résolution
        // (CD posé à la fin, pas au clic, voir StartChannel/ResolveChannel), donc sans ce
        // branchement l'overlay CD de la SkillBarUI resterait invisible pendant tout le cast
        // alors que le slot (et toute la barre) est verrouillé. Compte le temps ÉCOULÉ, pas
        // restant — SkillSlotUI.SetCooldown attend "temps restant avant utilisable", ici
        // c'est castTime - temps déjà passé dans la canalisation.
        if (_isChanneling && slot == _channelSlot && _channelSkill != null)
            return Mathf.Max(0f, _channelSkill.castTime - (Time.time - _channelStartTime));

        // Délai minimum entre deux steps d'un combo (comboStepInterval) — même trou que la
        // canalisation : _cooldownTimers reste à 0 tant que le combo n'est pas fini/expiré,
        // donc sans ça le slot a l'air "dispo" alors que le prochain step ne l'est pas encore.
        if (IsComboActive && slot == _comboSlot && _comboStepCooldown > 0f)
            return _comboStepCooldown;
```

Ajouter, juste après ce dernier bloc (avant le commentaire `// Slot 0 : CD individuel...`) :

```csharp

        // Hit en attente de résolution (Normal/MultiHit/Combo-step, chantier B) — même trou :
        // _cooldownTimers reste à 0 tant que l'event/timeout n'est pas tombé.
        if (_pendingHitSlot == slot)
            return Mathf.Max(0f, _pendingHitTimeout);
        if (_pendingMultiSlot == slot)
            return Mathf.Max(0f, _pendingMultiTimeout);
```

Trouver `GetCooldownTotal` :

```csharp
    public float GetCooldownTotal(int slot)
    {
        if (slot < 0 || slot >= 10 || _slots[slot] == null) return 0f;
        if (_isChanneling && slot == _channelSlot && _channelSkill != null)
            return _channelSkill.castTime;

        if (IsComboActive && slot == _comboSlot && _comboStepCooldown > 0f && _comboSkill != null)
            return _comboSkill.comboStepInterval;
```

Ajouter, juste après (avant le commentaire `// Slots 1-9 : si le GCD...`) :

```csharp

        if (_pendingHitSlot == slot && _pendingHitSkill != null && _pendingHitSkill.attackAnimation != null)
            return _pendingHitSkill.attackAnimation.length;
        if (_pendingMultiSlot == slot && _pendingMultiSkill != null && _pendingMultiSkill.attackAnimation != null)
            return _pendingMultiSkill.attackAnimation.length;
```

- [ ] **Step 3 : Supprimer `ExecuteSkill()` devenue obsolète**

Chercher tous les appels restants à `ExecuteSkill(` dans `Data/Skills/SkillBar.cs` (grep sur le
fichier). Il ne doit en rester AUCUN après les Tâches 4-5 (`LaunchSkill` route maintenant vers
`StartInstant`/`StartMultiHit`/`StartChannel`, `TryAdvanceCombo` vers `LaunchComboHit`,
`CheckApproach` vers `LaunchSkill`). Si un appel traîne encore, NE PAS supprimer la méthode —
signaler ça dans le rapport de tâche (BLOCKED ou DONE_WITH_CONCERNS) plutôt que de laisser un
appel cassé. Si confirmé aucun appelant restant, supprimer entièrement la méthode `ExecuteSkill`
(section "── Exécution ─────" du fichier).

- [ ] **Step 4 : Compiler (cumulativement, contre TOUT le fichier cette fois), vérifier 0 erreur**

C'est la première tâche où la compilation doit être verte de bout en bout pour ce fichier —
toutes les pièces (Tâches 1-6) sont maintenant en place.

- [ ] **Step 5 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: wire OnAnimationHitEvent, extend CD overlay, remove obsolete ExecuteSkill

OnAnimationHitEvent is the entry point PlayerAnimatorController's
relay calls into -- routes to ResolveInstant or ResolveMultiHitIndex
depending on what's pending. GetCooldownRemaining/GetCooldownTotal now
report the pending-hit's own countdown, same reasoning as the existing
channel/combo-window overlay branches -- otherwise a slot with a hit
in flight looked "ready" in the UI. ExecuteSkill() removed -- fully
superseded by StartInstant/StartMultiHit/StartChannel via LaunchSkill.

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

- [ ] **Step 1 : Poser au moins un Animation Event de test**

Geste Editor Unity (pas du code) — choisir un skill de test existant (ex: `skl_test_normal`,
déjà présent dans le projet) avec une `attackAnimation` assignée. Ouvrir ce clip dans la
fenêtre **Animation**, scrubber à une frame au milieu du clip (pas tout au début ni tout à la
fin — pour bien distinguer "résout au clic" de "résout à l'impact"), clic droit sur la
timeline → **Add Animation Event**, taper `OnSkillHitFrame` comme nom de fonction. Laisser le
paramètre `hitIndex` par défaut (0) pour ce skill à un seul coup.

- [ ] **Step 2 : Skill Normal avec event**

Lancer `skl_test_normal` (ou équivalent) sur une cible. Vérifier : les dégâts (rapport dans la
Console) apparaissent PILE quand l'anim atteint la frame de l'event, pas au moment du clic. CD
et GCD se posent seulement à ce moment-là (regarder l'overlay radial sur le slot : il doit se
vider progressivement de `attackAnimation.length` jusqu'à 0, PUIS le vrai CD prend le relais).

- [ ] **Step 3 : Skill sans anim — régression zéro**

Sur un skill castTime 0 SANS `attackAnimation` assignée (ou en la retirant temporairement d'un
skill de test) : vérifier résolution instantanée au clic, comme avant ce chantier.

- [ ] **Step 4 : Garde-fou timeout**

Sur un skill AVEC `attackAnimation` mais SANS event posé dessus (retirer temporairement
l'event ajouté au Step 1, ou tester sur un 2e skill de test non modifié) : vérifier que les
dégâts résolvent quand même, à la fin du clip (pas de blocage permanent de la skillbar).

- [ ] **Step 5 : MultiHit**

Sur `skl_test_multihit` (ou équivalent), poser 4 Animation Events sur son clip (indices 0, 1,
2, 3 — un par coup, base + 3 hitSteps). Vérifier : chaque coup résout à SA propre frame, dans
l'ordre. Tenter d'appuyer sur un autre slot PENDANT la séquence → bloqué (verrou actif).
Vérifier aussi que le personnage reste IMMOBILISÉ pendant toute la séquence (comme avant ce
chantier) — trouvaille de la review finale : ce verrou de déplacement dépendait de
`SkillBar.IsMultiHitLocked`, posé uniquement par l'ancien `ExecuteSkill()` ; corrigé pour
suivre aussi `IsPendingMultiHit`, à confirmer en jeu.

- [ ] **Step 6 : Combo**

Sur `skl_test_combo` (ou équivalent), poser un event sur CHACUN des clips (parent + chaque
step). Vérifier : chaque step résout à la frame d'event de SON PROPRE clip. La fenêtre pour
enchaîner le step suivant ne s'ouvre QU'APRÈS que le coup courant ait résolu — vérifier qu'on
ne peut pas "rater" la fenêtre pendant que l'anim du coup en cours joue encore. Le CD ne se
pose que sur le DERNIER step.

- [ ] **Step 7 : Canalisation — non-régression**

Tester `skl_test_channel` (ou équivalent) — doit se comporter EXACTEMENT comme validé au
chantier A (aucun changement de ce côté dans ce plan).

- [ ] **Step 8 : Verrou inter-skills**

Lancer un skill Normal, tenter d'en lancer un 2e (autre slot) AVANT que le 1er ait résolu →
bloqué par le verrou, pas de coupure d'anim visible.

- [ ] **Step 9 : Passif pendant un hit en attente**

Déclencher un passif (dégâts/soin/buff, SANS `attackAnimation`) pendant qu'un skill joueur
attend sa résolution → aucun conflit, les deux s'appliquent normalement.

- [ ] **Step 10 : Non-interruptibilité confirmée**

Se faire Stun/Freeze juste après avoir cliqué un skill Normal (pendant que son anim joue,
avant que l'event tombe) → le coup résout quand même normalement (comportement voulu,
contrairement à une Canalisation qui serait interrompue dans le même cas).

- [ ] **Step 11 : Skills existants sans event — non-régression globale**

Passer en revue 2-3 skills Normal (castTime 0, executionType Normal) déjà en jeu (pas les
skills de test) qui n'ont PAS encore d'Animation Event posé sur leur clip. Vérifier : ils
résolvent toujours après `attackAnimation.length` (le garde-fou), sans blocage ni comportement
visiblement différent d'avant ce chantier — puisqu'aucun event n'est encore posé dessus, ils
passent tous par le timeout à chaque utilisation, ce qui est normal tant que Florian n'a pas
encore posé les events sur le reste du contenu existant.

⚠ Cette absence de différence NE s'applique PAS aux skills **MultiHit** existants sans event
(trouvaille de la review finale) : `ResolveMultiHitIndex()`'s garde-fou timeout (`attackAnimation.
length`, posé UNE fois au lancement, pas par step) résout, une fois qu'il expire, TOUS les hits
restants d'un coup dans la même frame — au lieu de les étaler selon `hitSteps[i].delay` comme le
faisait l'ancienne coroutine. Un MultiHit existant sans events aura donc un comportement
visiblement différent (tous les FloatingText/effets qui apparaissent d'un coup à la fin du clip
au lieu d'être espacés) tant que Florian n'a pas posé un event par hit dessus — attendu et
documenté, pas un bug à corriger en urgence, mais à ne pas confondre avec une régression pendant
ce test.

Si tous les points ci-dessus passent, le chantier B est complet et fonctionnel pour le joueur.
