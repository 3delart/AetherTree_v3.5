# Calage des dégâts sur la frame d'impact (chantier B) — Design

## Contexte

Chantier A (Cast/Canalisation, `docs/superpowers/specs/2026-09-09-cast-canalisation-design.md`)
a posé un point d'extension explicite : `SkillBar.StartChannel()` appelle
`ProgressBarUI.StartProgress(..., onComplete: ResolveChannel, ...)`, et le commentaire sur place
dit noir sur blanc que ce `onComplete` est LE point unique où brancher un Animation Event à la
place du timer, sans toucher au reste de `ResolveChannel()`/`InterruptChannel()`/
`EndChannelState()`. Ce chantier B réalise cette extension — ET l'étend aux 3 autres types de
skill (Normal, MultiHit, Combo), qui aujourd'hui résolvent instantanément au clic (Normal/Combo)
ou sur un timer `WaitForSeconds` (MultiHit), sans aucun lien avec l'animation réellement jouée.

**Principe** (confirmé par Florian) : inversion de dépendance. Aujourd'hui : clic → calcul →
l'anim joue en parallèle, sans lien. Avec B : clic → l'anim joue → C'EST L'ANIM qui déclenche le
calcul, via un Animation Event Unity posé sur le clip à la vraie frame d'impact visuelle.

**Scope** : un seul système pour tous les types de skill, en une fois (pas de découpage en
sous-chantiers comme A). Aucune donnée de skill ne change — seul LE MOMENT où
`SkillSystem`/`Player` appliquent l'effet change. **Joueur uniquement** — Mob/PNJ castent
différemment (pas de `SkillBar`/`PlayerAnimatorController` identique), traités dans un chantier
futur une fois B validé en jeu sur le joueur. Les skills déclenchés par un **passif**
(`PassiveSkillSystem.skillToCast` → `SkillSystem.Execute()` directement, sans passer par
`SkillBar`) restent explicitement HORS SCOPE — voir Décisions.

## Décisions de design (issues du brainstorm)

| Question | Décision |
|---|---|
| Un système par type ou tout d'un coup ? | **Un seul système, tous les types en même temps.** Pas de découpage progressif comme A — Florian : "ça sera le même système pour tous". |
| Qu'est-ce qui change exactement ? | **Seulement le timing de résolution.** Aucune donnée `SkillData` modifiée — `damageMultiplier`, `hitSteps`, `comboSteps`, tout reste identique. Seul le moment où `SkillSystem`/`Player` appliquent l'effet bouge (du clic vers l'Animation Event). |
| Garde-fou anti-blocage (event jamais posé/reçu) | **`timeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f`** — une seule formule pour les deux cas : pas d'anim → résolution immédiate (0s, comme aujourd'hui, rien à attendre) ; anim présente mais event oublié → résout quand même à la fin du clip (filet de sécurité, ne devrait jamais se déclencher en pratique si l'event est bien posé au milieu du clip). Rejetée : timeout fixe (2-3s) — aurait demandé un cas spécial pour "pas d'anim", donc pas vraiment une formule unique. |
| Où vit l'Animation Event ? | **Sur le clip d'animation lui-même** (fenêtre Animation de Unity, marqueur sur la timeline), PAS un champ `SkillData`. Geste d'authoring par clip, pas une valeur à configurer dans l'Inspector du skill. |
| Comment gérer plusieurs hits sur un même clip (MultiHit) ? | Chaque event porte un **paramètre `int`** (l'index du hit) plutôt que de compter l'ordre d'arrivée — plus robuste. |
| MultiHit : un clip par coup, ou un seul clip avec plusieurs events ? | **Un seul clip, plusieurs events.** Cohérent avec l'architecture déjà en place (un seul state Animator "Attack" réutilisable, clip échangé à la volée — décision déjà prise pour éviter un state par skill). Un clip par coup casserait cette architecture et rendrait moins bien visuellement qu'une vraie anim de combo authored d'un bloc. |
| Le "coup de base" du MultiHit (aujourd'hui instantané au clic, indépendant des `hitSteps`) | **Unifié dans la même séquence d'events** — devient le hit d'index 0, les `hitSteps` sont les index 1..N. Plus de dispatch instantané séparé — cohérent avec le principe "l'anim décide", pas juste les hitSteps. |
| CD/GCD — au clic ou à la résolution, pour TOUS les types maintenant ? | **À la résolution, pour tous.** Déjà le principe pour la Canalisation (chantier A) ; sans ça, un skill CD court + anim longue verrait son CD quasi épuisé avant même que les dégâts partent — même piège déjà évité pour la Canalisation. |
| Faut-il verrouiller la skillbar pendant l'attente (Normal/Combo-step/MultiHit) ? | **Oui, verrou total**, même mécanisme que Canalisation/MultiHit existant. Pas optionnel : TOUS les skills partagent le MÊME state Animator "Attack" — lancer un 2e skill pendant que le 1er attend encore son event écraserait le clip en cours, et l'event du 1er ne tomberait jamais (mauvais clip en lecture). |
| Skills de passifs (`PassiveSkillSystem.skillToCast`) | **Hors scope.** Ils appellent `SkillSystem.Execute()` directement, jamais via `SkillBar` — donc jamais concernés par le nouveau verrou/timing. Règle de contenu (pas de code) : ne jamais assigner d'`attackAnimation` sur un skill de passif, sinon `PlayAttack()` s'exécuterait hors du verrou et écraserait l'anim en cours du joueur. Vérifié avec Florian : un passif dégâts/soin/buff SANS anim ne touche jamais l'Animator (`CalculateDamage`/`TakeDamage`/`Heal`/`ApplyBuff` sont indépendants), donc aucun conflit tant que cette règle est respectée. |

## Architecture

### 1. `World/PlayerAnimatorController.cs` — point d'entrée de l'event

```csharp
/// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
/// (Attack state). hitIndex : 0 par défaut (skills à un seul coup — Normal, chaque step de
/// Combo, Canalisation), ou l'index du hit pour un MultiHit (0 = coup de base, 1..N = hitSteps).
/// Relais pur — toute la logique de résolution reste dans SkillBar, qui possède déjà tout
/// l'état de lancement (slot, skill, target, verrous).</summary>
public void OnSkillHitFrame(int hitIndex = 0)
{
    SkillBar.Instance?.OnAnimationHitEvent(hitIndex);
}
```

Aucun autre changement dans ce fichier — `PlayOverrideClip`/`PlayAttack`/`PlayChannel`/
`CancelChannel` restent identiques (le `ResetTrigger(CancelActionTrigger)` déjà en place au
début de `PlayOverrideClip` continue de protéger contre un trigger `CancelAction` orphelin).

### 2. `Entities/Player.cs` — split `UseSkill()` en 3

`BeginSkillUse()` reste identique (déjà correct depuis A — combat/AFK/Stealth au lancement).
`UseSkill()` reste identique aussi — c'est le raccourci "tout en un" gardé pour tout ce qui ne
passe PAS par le nouveau flux (skills de passifs, voir Décisions). Nouvelle méthode extraite de
la SECONDE MOITIÉ de `UseSkill()` (`Entities/Player.cs:1107-1132`, tout ce qui suit le bloc
`BeginSkillUse`+`PlayAttack` conditionnel) :

```csharp
/// <summary>Bookkeeping qui doit réagir à la RÉSOLUTION d'un skill (affinité élémentaire,
/// recalcul stats, titre) — jamais BeginSkillUse ni PlayAttack ici, les deux sont déjà gérés
/// au LANCEMENT par SkillBar (StartInstant/StartMultiHit/StartChannel) pour tout ce qui passe
/// par le nouveau flux événementiel. Appeler PlayAttack ici referait démarrer l'anim "Attack"
/// PAR-DESSUS elle-même juste après son propre impact — bug de la même famille que celui
/// trouvé et corrigé sur la Canalisation au chantier A.</summary>
public void ResolveSkillUse(SkillData skill, Entity target)
{
    if (skill == null) return;

    bool isBasic = skill.skillType == SkillType.BasicAttack || skill.HasTag(SkillTag.BasicAttack);

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

`UseSkill()` peut être réécrite pour appeler ce nouveau `ResolveSkillUse()` en interne (DRY,
évite de dupliquer le bloc élémentaire/recalcul) :

```csharp
public void UseSkill(SkillData skill, Entity target = null)
{
    if (skill == null) return;

    BeginSkillUse(skill);
    if (skill.castTime <= 0f)
        animatorController?.PlayAttack(skill.attackAnimation);

    ResolveSkillUse(skill, target);
}
```

Comportement inchangé pour tout appelant existant de `UseSkill()` — juste factorisé.

### 3. `Combat/SkillSystem.cs` — nouveau point de résolution, sans re-déclencher l'anim

`Execute()` reste identique pour l'instant (utilisé par les chemins hors-scope : passifs — voir
Décisions). Nouvelle méthode, appelée par `SkillBar` à la résolution (event ou timeout), qui
fait tout ce qu'`Execute()` fait SAUF `player.UseSkill()` complet :

```csharp
/// <summary>Résout un skill DÉJÀ lancé (mana/anim/BeginSkillUse déjà faits par SkillBar au
/// lancement) — dispatch des dégâts/effets, event, VFX/Son, bookkeeping élémentaire. Jamais
/// d'appel à player.UseSkill()/BeginSkillUse()/PlayAttack ici, seulement
/// player.ResolveSkillUse() (voir Player.cs) — sinon l'anim redémarrerait par-dessus
/// elle-même juste après son propre impact.</summary>
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

Note : ce `ResolveExecute` ne gère PAS le branchement MultiHit (`DispatchByTargetType` seul,
pas de coroutine) — pour le joueur, le MultiHit passe maintenant par un chemin dédié dans
`SkillBar` (section 4 ci-dessous) qui résout chaque hit individuellement. `Execute()` (existant,
inchangé) garde sa logique MultiHit-par-coroutine telle quelle, pour les Mob/PNJ (hors scope
de ce chantier, toujours sur l'ancien chemin) et pour tout appelant qui n'est pas le joueur.

Nouvelle méthode pour résoudre UN SEUL hit à la demande (coup de base OU un hitStep précis),
réutilisée par `SkillBar.OnAnimationHitEvent` pour le MultiHit joueur — extrait du corps de la
boucle `ExecuteMultiHit` existante (`Combat/SkillSystem.cs:227-270`, à vérifier ligne exacte à
l'implémentation) pour le cas `hitIndex >= 1`, et de `DispatchByTargetType` pour `hitIndex == 0`
(coup de base = comportement normal du skill parent, comme un skill à un seul coup) :

```csharp
/// <summary>Résout UN hit précis d'un skill MultiHit — hitIndex 0 = coup de base (comme un
/// skill normal, dispatch standard), hitIndex 1..N = hitSteps[hitIndex - 1] (même calcul que
/// l'ancienne coroutine ExecuteMultiHit, un step à la fois plutôt qu'en boucle).</summary>
public void ResolveMultiHitStep(SkillData skill, Entity caster, Entity target, int hitIndex)
{
    if (skill == null || caster == null || caster.isDead) return;
    if (target == null || target.isDead) return;

    if (hitIndex == 0)
    {
        DispatchByTargetType(skill, caster, target);
        return;
    }

    int stepIndex = hitIndex - 1;
    if (skill.hitSteps == null || stepIndex < 0 || stepIndex >= skill.hitSteps.Count) return;
    HitStep step = skill.hitSteps[stepIndex];

    // Même calcul que le corps de la boucle ExecuteMultiHit existante — voir cette méthode à
    // l'implémentation pour le détail exact (CalculateDamageForStep, TakeDamage,
    // ApplyOnHitDealtEffects, GameEventBus, FloatingText, statusEffects, VFX/Son du step,
    // CheckKill) — transcrit tel quel, un seul step au lieu d'un foreach avec WaitForSeconds.
}
```

⚠ **Point à vérifier à l'implémentation** : le corps exact de la boucle `ExecuteMultiHit`
(`Combat/SkillSystem.cs`, au-delà de la ligne 270 lue dans ce document) n'a pas été entièrement
recopié ici — l'implémenteur doit lire la méthode complète et transcrire fidèlement son corps
de boucle dans `ResolveMultiHitStep`, sans réinventer la logique de calcul.

### 4. `Data/Skills/SkillBar.cs` — le gros du changement

#### Nouveaux champs (Normal / Combo-step — un seul hit en attente à la fois)

```csharp
// ── Attente de résolution (Normal / step de Combo castTime 0) ─────────────
private int       _pendingHitSlot    = -1;
private SkillData _pendingHitSkill   = null;
private Entity    _pendingHitTarget  = null;
private float     _pendingHitTimeout = 0f;

public bool IsPendingHit => _pendingHitSlot >= 0;
```

#### Nouveaux champs (MultiHit — séquence d'index)

```csharp
// ── Attente de résolution (MultiHit — séquence d'index) ───────────────────
private int       _pendingMultiSlot      = -1;
private SkillData _pendingMultiSkill     = null;
private Entity    _pendingMultiTarget    = null;
private int       _pendingMultiNextIndex = 0;   // prochain index attendu (0 = coup de base)
private float     _pendingMultiTimeout   = 0f;

public bool IsPendingMultiHit => _pendingMultiSlot >= 0;
```

#### `TryUseSlot()` — verrou étendu

Trouver le check existant (`Data/Skills/SkillBar.cs:293`, section "Vérification GCD & locks") :
```csharp
if (_multiHitLockTimer > 0f || _isChanneling)
{
    return false;
}
```
Remplacer par :
```csharp
if (_multiHitLockTimer > 0f || _isChanneling || IsPendingHit || IsPendingMultiHit)
{
    return false;
}
```
(`_multiHitLockTimer` reste tel quel comme ancien mécanisme pour Mob/PNJ — hors scope B, mais
le champ continue d'exister et de bloquer côté joueur si jamais réutilisé ailleurs ; en
pratique, côté joueur, c'est `IsPendingMultiHit` qui prend le relais désormais.)

#### `LaunchSkill()` — nouveau branchement à 3 voies

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

#### `StartInstant()` — remplace le corps actuel d'`ExecuteSkill()` pour Normal/Combo-step

```csharp
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
        TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
    }
}
```

Remarque : le raycast GroundTarget est repris tel quel du modèle déjà établi dans
`StartChannel()` (chantier A) — capturé au LANCEMENT, pas à la résolution, même raisonnement
(aim-then-resolve, cohérent avec mana/HP/gold dépensés au clic).

#### `StartMultiHit()` / résolution par index

```csharp
private void StartMultiHit(SkillData skill, int slot, Entity target)
{
    _player.BeginSkillUse(skill);
    EngageAndFaceTarget(skill, slot, target);

    _player.SpendMana(GetEffectiveManaCost(skill));
    if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
    if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

    _player.AnimatorController?.PlayAttack(skill.attackAnimation);

    _pendingMultiSlot      = slot;
    _pendingMultiSkill     = skill;
    // Même correction GroundTarget que StartInstant() — voir commentaire là-bas.
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
        // Séquence terminée — CD/GCD posés, verrou levé
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
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
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

#### `OnAnimationHitEvent(int hitIndex)` — dispatcher appelé par `PlayerAnimatorController`

```csharp
public void OnAnimationHitEvent(int hitIndex)
{
    if (IsPendingHit)      { ResolveInstant(); return; }
    if (IsPendingMultiHit) { ResolveMultiHitIndex(hitIndex); return; }
    // Aucun hit en attente — event reçu hors contexte (ex: anim jouée sans skill en cours,
    // ou déjà résolu par le timeout juste avant). Ignoré silencieusement, pas une erreur.
}
```

#### `Update()` — tick des deux timeouts

Ajouter, dans le même bloc que les autres ticks de timers (`Data/Skills/SkillBar.cs:127-210`) :
```csharp
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

#### `CheckApproach()` et `ExecuteSkill()` — devenus obsolètes

`ExecuteSkill()` (`Data/Skills/SkillBar.cs:735-790`) est intégralement remplacée par
`StartInstant()`/`StartMultiHit()` — à supprimer une fois le nouveau code en place et vérifié
(pas de suppression prématurée avant confirmation en jeu). `CheckApproach()` continue d'appeler
`LaunchSkill()` (inchangé, `Data/Skills/SkillBar.cs:671-672`) — fonctionne tel quel puisque
`LaunchSkill()` route déjà vers les 3 nouvelles méthodes.

#### `GetCooldownRemaining`/`GetCooldownTotal` — overlay pendant l'attente

Même besoin que pour la Canalisation/le délai de combo (chantier A) — sans ça, le slot en
attente de résolution aurait l'air "disponible" alors qu'il est verrouillé. Ajouter, dans
`GetCooldownRemaining` (`Data/Skills/SkillBar.cs:807-831`) et `GetCooldownTotal`
(`Data/Skills/SkillBar.cs:833-847`), les mêmes branchements que ceux déjà en place pour
`_isChanneling`/`_comboStepCooldown` :
```csharp
if (_pendingHitSlot == slot)
    return Mathf.Max(0f, _pendingHitTimeout);   // (remaining) / skill.attackAnimation.length ou son équivalent pour total
if (_pendingMultiSlot == slot)
    return Mathf.Max(0f, _pendingMultiTimeout);
```
Détail exact (quelle valeur pour "total" — probablement `_pendingHitSkill.attackAnimation.length`
capturée au lancement) à affiner à l'implémentation, même principe que les branchements déjà
écrits pour Canalisation/Combo.

## Fichiers touchés

- `World/PlayerAnimatorController.cs` — nouvelle méthode `OnSkillHitFrame(int hitIndex = 0)`.
- `Entities/Player.cs` — nouvelle méthode `ResolveSkillUse()`, `UseSkill()` refactorisée pour
  l'appeler en interne (comportement inchangé pour les appelants existants).
- `Combat/SkillSystem.cs` — nouvelles méthodes `ResolveExecute()` et `ResolveMultiHitStep()`.
  `Execute()`/`ExecuteMultiHit()` (coroutine) restent inchangées, toujours utilisées par les
  chemins hors-scope (passifs, Mob/PNJ futur).
- `Data/Skills/SkillBar.cs` — nouveaux champs `_pendingHit*`/`_pendingMulti*`, nouvelles
  méthodes `StartInstant`/`ResolveInstant`/`StartMultiHit`/`ResolveMultiHitIndex`/
  `OnAnimationHitEvent`, `LaunchSkill()` à 3 voies, verrou étendu dans `TryUseSlot()`, tick des
  2 timeouts dans `Update()`, overlay CD étendu. `ExecuteSkill()` devient obsolète (à retirer
  après vérification).

Aucun changement de `SkillData.cs` (aucun nouveau champ — les Animation Events vivent sur les
clips, pas dans les assets de skill), aucun changement d'enum, aucun risque de casse de
save/asset.

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian, à faire une
fois les Animation Events effectivement posés sur au moins un clip de test par catégorie :

1. Compiler, 0 erreur.
2. Skill Normal avec anim + event posé au milieu du clip → les dégâts partent PILE à la frame de
   l'event, pas au clic. CD/GCD posés seulement à ce moment-là (pas avant).
3. Même skill mais SANS `attackAnimation` assignée → résolution instantanée, comme avant B
   (régression zéro sur les skills sans anim).
4. Skill Normal AVEC anim mais SANS event posé dessus (oubli volontaire pour le test) → résout
   quand même à la fin du clip (garde-fou), pas de blocage permanent de la skillbar.
5. MultiHit : chaque coup (base + hitSteps) résout à SA propre frame d'event, dans l'ordre.
   Tenter d'appuyer sur un autre slot pendant la séquence → bloqué (verrou actif).
6. Combo : chaque step résout à la frame d'event de SON PROPRE clip (pas celui du parent).
7. Canalisation : toujours fonctionnelle exactement comme testé au chantier A (aucun changement
   de ce côté, le point d'extension n'a pas encore été branché sur un vrai event — reste sur le
   timer `ProgressBarUI` pour l'instant, sauf si Florian veut aussi le basculer dans ce chantier).
8. Lancer un skill Normal, tenter d'en lancer un 2e (autre slot) AVANT que le 1er ait résolu →
   bloqué par le verrou, pas de coupure d'anim.
9. Skill de passif (dégâts/soin/buff, sans `attackAnimation`) déclenché PENDANT qu'un skill
   joueur attend sa résolution → aucun conflit, les deux s'appliquent normalement.
10. Vérifier qu'aucun skill castTime 0 existant sans Animation Event posé ne se comporte
    différemment d'avant B (résolution après `attackAnimation.length`, invisible si l'event est
    correctement placé bien avant la fin du clip).
