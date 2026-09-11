# Zone à impact différé (chantier C) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un mécanisme de résolution différée par zone fixe au sol (skills Normal
instant ou canalisé, `targetType` Target/GroundTarget/AoE_Target) — la zone se plante au point
de résolution existant, mais les dégâts n'appliquent qu'après un délai configurable, à qui se
trouve réellement dans la zone à ce moment (permet une vraie fenêtre d'esquive), avec un mode
optionnel de zone persistante à ticks répétés (lave, pluie de glace). Pose aussi 3 champs VFX
(`vfxCast`/`vfxZoneMarker`/`vfxImpact`) en prévision du prochain chantier VFX.

**Architecture:** `SkillData` gagne les champs de configuration. `SkillBar` reste le seul point
qui décide QUAND une résolution se déclenche (inchangé, chantier A/B) — au point de résolution
existant, elle route soit vers la résolution immédiate (comme aujourd'hui) soit vers
`SkillSystem.PlantDelayedZone()` (nouveau). `SkillSystem` gagne une coroutine de détonation qui
réutilise EXACTEMENT la logique de filtrage/dégâts déjà écrite pour `ExecuteGroundTarget()`/
`ExecuteAoETarget()` (`Physics.OverlapSphere` + `PassesAoeFilter` + `ApplyEffectType`/
`ApplyStatusEffects`/`CheckKill`), pas de nouvelle logique de ciblage inventée.

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification manuelle Play
Mode par Florian (dernière tâche), nécessite de créer des assets de skill de test avec
`hasDelayedImpact` activé (geste Editor, hors de portée d'un agent).

**Spec:** `docs/superpowers/specs/2026-09-11-delayed-impact-zone-design.md`

## Global Constraints

- 3 fichiers touchés : `Data/Skills/SkillData.cs`, `Combat/SkillSystem.cs`,
  `Data/Skills/SkillBar.cs`. Aucun autre fichier.
- `hasDelayedImpact` uniquement pour `executionType = Normal` — MultiHit/ComboSequence non
  supportés. **La vraie protection contre un step de combo avec `hasDelayedImpact = true` est
  CÔTÉ CODE** (garde combo évaluée avant tout branchement zone différée dans
  `ResolveInstant()`), PAS le warning `OnValidate()` seul, qui ne peut pas détecter qu'un
  `SkillData` est référencé comme step d'un autre skill.
- `PlantDelayedZone()` doit reset `_groundTargetPoint = null` après l'avoir lu pour un
  `GroundTarget` — même consommation que `ExecuteGroundTarget()` fait déjà. Sans ce reset, un
  skill sans rapport lancé plus tard (ex: `TeleportSelf`, qui lit aussi ce champ) hérite
  silencieusement d'une position périmée.
- La coroutine de détonation doit garder `caster == null || caster.isDead` à chaque tick —
  utiliser `break` (pas `yield break`) pour sortir de la boucle sur cette garde, afin que le
  nettoyage du `vfxZoneMarker` en fin de coroutine s'exécute quand même.
- CD/GCD/`DelayAutoAttack` se postent **au point de résolution existant** (quand la zone se
  plante), jamais à la détonation — inchangé par rapport au comportement actuel, seuls les
  DÉGÂTS sont différés.
- `vfxCast` n'est câblé nulle part aujourd'hui — c'est un ajout neuf dans 4 méthodes
  (`StartInstant`, `StartChannel`, `StartMultiHit`, `LaunchComboHit`), pas une extension de
  code existant. Ne pas supposer qu'un mécanisme partiel existe déjà.
- Renommage `vfxPrefab` → `vfxImpact` sur `SkillData` via `[FormerlySerializedAs("vfxPrefab")]`
  (`using UnityEngine.Serialization;` à ajouter). `HitStep.vfxPrefab` reste un champ SÉPARÉ,
  PAS concerné par ce renommage. 6 usages réels de `.vfxPrefab` à renommer, tous dans
  `Combat/SkillSystem.cs` : lignes 135, 140, 181, 186, 240 (`skill.vfxPrefab`), 365 (à
  confirmer à l'implémentation — vérifier le fichier réel avant d'éditer, les numéros de ligne
  peuvent avoir légèrement bougé depuis l'écriture de ce plan).
- Pas de tests automatisés — chaque tâche se termine par "compiler, 0 erreur" (compilation
  cumulative avec les tâches précédentes). La vérification fonctionnelle réelle est la
  dernière tâche (Play Mode manuel par Florian).
- Toujours vérifier les noms/signatures exacts dans le code réel avant transcription — les
  extraits de code "existant" cités dans ce plan ont été lus dans le fichier réel au moment de
  l'écriture du plan, mais re-vérifier avant d'éditer reste la règle (le fichier a pu bouger).

---

### Task 1: `Data/Skills/SkillData.cs` — nouveaux champs + validation

**Files:**
- Modify: `Data/Skills/SkillData.cs`

**Interfaces:**
- Consumes: rien de nouveau (`ShowIf`/`AndField`/`AndValue` déjà existants, pattern déjà
  utilisé sur `executionType`).
- Produces: `SkillData.hasDelayedImpact` (bool), `SkillData.impactDelay` (float),
  `SkillData.zoneDuration` (float), `SkillData.zoneTickInterval` (float),
  `SkillData.vfxCast` (GameObject), `SkillData.vfxZoneMarker` (GameObject),
  `SkillData.vfxImpact` (GameObject, remplace `vfxPrefab`) — consommés par les Tâches 2 et 3.

- [ ] **Step 1 : Ajouter `using UnityEngine.Serialization;`**

En haut du fichier, ligne 1-2 actuelles :
```csharp
using UnityEngine;
using System.Collections.Generic;
```
Remplacer par :
```csharp
using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
```

- [ ] **Step 2 : Ajouter les 4 champs "Zone à impact différé"**

Trouver (fin de la section ⑨ Exécution avancée, juste avant la section ⑩) :
```csharp
    [Tooltip("ComboSequence uniquement — délai minimum en secondes entre deux steps.\n" +
             "Empêche de spammer tous les steps du combo en moins d'une seconde.\n" +
             "0 = pas de délai minimum. Réglable par combo (ex: 0.5s pour un combo rapide,\n" +
             "2s pour un combo lent/lourd).")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public float comboStepInterval = 0f;

    // ── ⑩ Visuel & Son ────────────────────────────────────────
```

Remplacer par :
```csharp
    [Tooltip("ComboSequence uniquement — délai minimum en secondes entre deux steps.\n" +
             "Empêche de spammer tous les steps du combo en moins d'une seconde.\n" +
             "0 = pas de délai minimum. Réglable par combo (ex: 0.5s pour un combo rapide,\n" +
             "2s pour un combo lent/lourd).")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public float comboStepInterval = 0f;

    // ── ⑨Bis Zone à impact différé ─────────────────────────────
    [Tooltip("Transforme la résolution de ce skill en zone au sol à impact différé — au lieu de\n" +
             "résoudre les dégâts immédiatement au point de résolution existant (fin d'anim / fin\n" +
             "de canalisation), une zone se plante à cet endroit et les dégâts n'appliquent qu'après\n" +
             "impactDelay secondes, à qui se trouve RÉELLEMENT dans la zone à ce moment (permet une\n" +
             "vraie fenêtre d'esquive). Non supporté avec MultiHit/ComboSequence — la vraie garde\n" +
             "contre un step de combo est côté code (SkillBar.ResolveInstant), pas ce warning seul.")]
    [ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget, TargetType.AoE_Target,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑨Bis Zone à impact différé")]
    public bool hasDelayedImpact = false;

    [Tooltip("Délai en secondes entre le plantage de la zone (résolution existante) et le premier\n" +
             "tick de dégâts.")]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float impactDelay = 1f;

    [Tooltip("Durée pendant laquelle la zone reste active APRÈS le premier tick (impactDelay).\n" +
             "0 = un seul tick, la zone disparaît ensuite (impact différé simple — ex: comète).\n" +
             "> 0 = zone persistante qui retick toutes les zoneTickInterval secondes pendant cette\n" +
             "durée (ex: zone de lave, pluie de glace).")]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float zoneDuration = 0f;

    [Tooltip("Fréquence des ticks de dégâts pendant zoneDuration. Ignoré si zoneDuration = 0.")]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float zoneTickInterval = 1f;

    // ── ⑩ Visuel & Son ────────────────────────────────────────
```

- [ ] **Step 3 : Renommer `vfxPrefab` en `vfxImpact` et ajouter `vfxCast`/`vfxZoneMarker`**

Trouver :
```csharp
    // ── ⑩ Visuel & Son ────────────────────────────────────────
    [Header("⑩ Visuel & Son")]
    public Sprite     icon;
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;
```

Remplacer par :
```csharp
    // ── ⑩ Visuel & Son ────────────────────────────────────────
    [Header("⑩ Visuel & Son")]
    public Sprite     icon;

    [Tooltip("VFX qui reste sur le caster, spawné au LANCEMENT (pentacle aux pieds, glow aux\n" +
             "mains...). Optionnel — vide = pas de VFX de cast.")]
    public GameObject vfxCast;

    [Tooltip("VFX de zone/avertissement — spawné quand la zone se plante au sol (point de\n" +
             "résolution existant), reste affiché jusqu'à la détonation. Actif uniquement si\n" +
             "hasDelayedImpact = true.")]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public GameObject vfxZoneMarker;

    [Tooltip("VFX joué exactement au moment où les dégâts s'appliquent réellement — immédiat pour\n" +
             "un skill sans délai (comportement historique de vfxPrefab), ou à chaque détonation\n" +
             "pour une zone à impact différé.")]
    [FormerlySerializedAs("vfxPrefab")]
    public GameObject vfxImpact;

    public AudioClip  soundEffect;
```

- [ ] **Step 4 : Ajouter l'avertissement `OnValidate()`**

Trouver (fin de `OnValidate()`, juste avant l'accolade fermante) :
```csharp
        if (castTime > 0f && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] castTime > 0 avec executionType = {executionType} — " +
                              "combinaison non recommandée. Avec ComboSequence, le combo prend la " +
                              "main et castTime est totalement ignoré (le skill ne canalise jamais). " +
                              "Avec MultiHit, le skill canalise normalement puis exécute sa séquence " +
                              "de hits à la résolution, MAIS la convention de cooldown différé du " +
                              "MultiHit n'est pas respectée dans ce cas (le CD de canalisation prend " +
                              "le dessus). Remets executionType à Normal si ce skill doit canaliser " +
                              "proprement.", this);
    }
#endif
}
```

Remplacer par :
```csharp
        if (castTime > 0f && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] castTime > 0 avec executionType = {executionType} — " +
                              "combinaison non recommandée. Avec ComboSequence, le combo prend la " +
                              "main et castTime est totalement ignoré (le skill ne canalise jamais). " +
                              "Avec MultiHit, le skill canalise normalement puis exécute sa séquence " +
                              "de hits à la résolution, MAIS la convention de cooldown différé du " +
                              "MultiHit n'est pas respectée dans ce cas (le CD de canalisation prend " +
                              "le dessus). Remets executionType à Normal si ce skill doit canaliser " +
                              "proprement.", this);

        // hasDelayedImpact + MultiHit/ComboSequence sur CE skill LUI-MÊME est détectable ici —
        // mais un step de combo (comboSteps[i] d'un AUTRE SkillData) a lui-même
        // executionType = Normal, donc invisible à ce check. La vraie garde contre ce cas
        // précis vit dans SkillBar.ResolveInstant() (garde combo évaluée avant tout
        // branchement zone différée) — ce warning reste utile pour la saisie directe sur un
        // skill MultiHit/ComboSequence, pas comme protection complète.
        if (hasDelayedImpact && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la zone différée ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);
    }
#endif
}
```

- [ ] **Step 5 : Compiler, vérifier 0 erreur**

- [ ] **Step 6 : Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "$(cat <<'EOF'
feat: add delayed-impact-zone fields + vfxCast/vfxZoneMarker to SkillData

hasDelayedImpact/impactDelay/zoneDuration/zoneTickInterval configure a
skill's resolution to plant a fixed ground zone (at the existing
resolution point) instead of dealing damage immediately -- damage
applies after impactDelay to whoever is actually standing in the zone
at that moment (real dodge window), optionally repeating every
zoneTickInterval for zoneDuration (persistent hazard zones like lava).
Restricted to executionType = Normal via ShowIf; the OnValidate
warning only catches a skill configured this way directly -- it can't
detect a SkillData used as another skill's comboSteps entry (its own
executionType is Normal too), so the real guard against that case
lives in SkillBar.ResolveInstant(), not here.

vfxPrefab renamed to vfxImpact (FormerlySerializedAs preserves
existing asset references) for a consistent 3-slot VFX model: vfxCast
(stays on the caster, spawns at launch), vfxZoneMarker (spawns when
the zone plants, delayed-impact only), vfxImpact (plays exactly when
damage lands -- immediately for a normal skill, per detonation tick
for a delayed zone). Groundwork for the next VFX chantier.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `Combat/SkillSystem.cs` — `PlantDelayedZone` + coroutine de détonation

**Files:**
- Modify: `Combat/SkillSystem.cs`

**Interfaces:**
- Consumes: `SkillData.hasDelayedImpact`/`impactDelay`/`zoneDuration`/`zoneTickInterval`/
  `vfxCast`/`vfxZoneMarker`/`vfxImpact` (Tâche 1) ; `_groundTargetPoint`, `PassesAoeFilter`,
  `ApplyEffectType`, `ApplyStatusEffects`, `CheckKill` (déjà existants dans ce fichier,
  inchangés).
- Produces: `public void PlantDelayedZone(SkillData skill, Entity caster, Entity target)` —
  consommée par la Tâche 3 (`SkillBar.ResolveInstant`/`ResolveChannel`).

- [ ] **Step 1 : Lire `ExecuteGroundTarget()` et `ExecuteAoETarget()` en entier avant d'écrire**

Lire `Combat/SkillSystem.cs`, méthodes `ExecuteGroundTarget()` et `ExecuteAoETarget()` — leur
corps exact (vérifié au moment de l'écriture de ce plan, à reconfirmer sur le fichier réel) :

```csharp
    private void ExecuteAoETarget(SkillData skill, Entity caster, Entity target)
    {
        Collider[] hits = Physics.OverlapSphere(target.transform.position, skill.aoeRadius);
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            CheckKill(entity);
        }
    }

    private void ExecuteGroundTarget(SkillData skill, Entity caster)
    {
        Vector3 center = _groundTargetPoint ?? caster.transform.position;
        _groundTargetPoint = null;

        Collider[] hits = Physics.OverlapSphere(center, skill.aoeRadius);
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            CheckKill(entity);
        }
    }
```

C'est CETTE boucle (`Physics.OverlapSphere` + filtre + `ApplyEffectType`/`ApplyStatusEffects`/
`CheckKill`) qu'il faut réutiliser à l'identique dans la coroutine de détonation ci-dessous —
ne pas réinventer une autre façon de trouver/filtrer les cibles.

Lire aussi `ResolveExecute()` en entier (déjà cité dans le plan écrit avant cette tâche, mais
reconfirmer sur le fichier réel) pour transcrire fidèlement son bloc de bookkeeping (tout SAUF
`DispatchByTargetType` et le spawn VFX/son, qui sont remplacés par la logique de zone) :

```csharp
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

- [ ] **Step 2 : Renommer les 6 usages de `.vfxPrefab` en `.vfxImpact`**

Chercher (`grep -n "\.vfxPrefab" Combat/SkillSystem.cs`) et remplacer CHAQUE occurrence de
`skill.vfxPrefab` par `skill.vfxImpact` — attendu aux alentours des lignes 135, 140 (dans
`Execute()`), 181, 186 (dans `ResolveExecute()`), et 240 (dans `ResolveMultiHitStep()`, forme
`step.vfxPrefab ?? skill.vfxPrefab` → `step.vfxPrefab ?? skill.vfxImpact` — **`step.vfxPrefab`
reste TEL QUEL, c'est le champ séparé de `HitStep`, ne pas le renommer**). Vérifier aussi
`ExecuteMultiHit()` (méthode coroutine plus bas dans le fichier, ancien chemin Mob/PNJ) qui a
probablement le même pattern `step.vfxPrefab ?? skill.vfxPrefab` à corriger de la même façon
(seul `skill.vfxPrefab` devient `skill.vfxImpact`, jamais `step.vfxPrefab`).

- [ ] **Step 3 : Ajouter `PlantDelayedZone()` et la coroutine de détonation**

Juste après `ResolveExecute()` (avant `ResolveMultiHitStep()`), ajouter :

```csharp

    /// <summary>Résout un skill `hasDelayedImpact` DÉJÀ lancé par SkillBar (mana/anim/
    /// BeginSkillUse déjà faits au lancement) — fait le bookkeeping de résolution immédiatement
    /// (comme ResolveExecute), mais reporte le DISPATCH DES DÉGÂTS à une coroutine différée qui
    /// détone après `skill.impactDelay` secondes, à qui se trouve réellement dans la zone à ce
    /// moment (pas la cible figée au clic). Position capturée en Vector3 pur — ni le caster ni
    /// la cible n'influencent la zone une fois plantée.</summary>
    public void PlantDelayedZone(SkillData skill, Entity caster, Entity target)
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

        Vector3 position;
        if (skill.targetType == TargetType.GroundTarget)
        {
            // Même consommation que ExecuteGroundTarget() — sans ce reset, un skill sans
            // rapport lancé plus tard (ex: TeleportSelf) hériterait de cette position périmée.
            position = _groundTargetPoint ?? caster.transform.position;
            _groundTargetPoint = null;
        }
        else
        {
            position = target != null ? target.transform.position : caster.transform.position;
        }

        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        StartCoroutine(DelayedZoneRoutine(skill, caster, position, marker));
    }

    /// <summary>Tick(s) de détonation d'une zone plantée par PlantDelayedZone(). Un seul tick si
    /// `zoneDuration == 0` (impact différé simple), sinon retick toutes les
    /// `zoneTickInterval` secondes pendant `zoneDuration` (zone persistante type lave). Chaque
    /// tick réutilise EXACTEMENT la logique de ExecuteGroundTarget()/ExecuteAoETarget() — même
    /// OverlapSphere + PassesAoeFilter + ApplyEffectType/ApplyStatusEffects/CheckKill.</summary>
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker)
    {
        yield return new WaitForSeconds(skill.impactDelay);

        float remaining = skill.zoneDuration;
        while (true)
        {
            // Même garde que les autres coroutines longues du fichier (DashToTarget/
            // DashInDirection) — le caster peut mourir entre le plantage et la détonation.
            // `break` (pas `yield break`) pour que le nettoyage du marker en fin de méthode
            // s'exécute quand même.
            if (caster == null || caster.isDead) break;

            Collider[] hits = Physics.OverlapSphere(position, skill.aoeRadius);
            foreach (Collider col in hits)
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity.isDead) continue;
                if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);
            }

            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, position, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, position);

            if (remaining <= 0f) break;

            yield return new WaitForSeconds(skill.zoneTickInterval);
            remaining -= skill.zoneTickInterval;
        }

        if (marker != null) Destroy(marker);
    }
```

- [ ] **Step 4 : Compiler (cumulativement avec la Tâche 1), vérifier 0 erreur**

- [ ] **Step 5 : Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "$(cat <<'EOF'
feat: add PlantDelayedZone + detonation coroutine (chantier C)

PlantDelayedZone mirrors ResolveExecute()'s bookkeeping (ResolveSkillUse,
SkillUsedEvent, RegisterLastSkill -- same tracking a normal resolution
already does) but replaces the immediate DispatchByTargetType with a
fixed Vector3 position capture (consuming _groundTargetPoint for
GroundTarget, same as ExecuteGroundTarget already does) and a
vfxZoneMarker spawn, then hands off to DelayedZoneRoutine.

The coroutine waits impactDelay, then re-derives targets via
Physics.OverlapSphere + PassesAoeFilter every tick -- the exact same
targeting logic ExecuteGroundTarget/ExecuteAoETarget already use, not
reinvented -- so movement in/out of the zone between ticks genuinely
matters. zoneDuration = 0 means a single tick (simple delayed impact,
e.g. a falling meteor); > 0 repeats every zoneTickInterval (persistent
hazard zones, e.g. lava). Guards caster null/dead each tick via break
(not yield break) so the marker cleanup at the end still runs.

Renamed vfxPrefab -> vfxImpact at all 6 real call sites (HitStep's own
vfxPrefab field is untouched, it's a separate field).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `Data/Skills/SkillBar.cs` — câblage `vfxCast` + branchement zone différée

**Files:**
- Modify: `Data/Skills/SkillBar.cs`

**Interfaces:**
- Consumes: `SkillSystem.PlantDelayedZone(SkillData, Entity, Entity)` (Tâche 2),
  `SkillData.vfxCast`/`hasDelayedImpact` (Tâche 1).
- Produces: rien de nouveau exposé — modifie le comportement interne de `StartInstant`,
  `StartChannel`, `StartMultiHit`, `LaunchComboHit`, `ResolveInstant`, `ResolveChannel`.

- [ ] **Step 1 : Ajouter le spawn `vfxCast` dans les 4 méthodes de lancement**

Dans CHACUNE des 4 méthodes ci-dessous, ajouter `if (skill.vfxCast != null)
Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);` juste après
l'appel à `_player.AnimatorController?.PlayAttack(...)` (ou `PlayChannel(...)` pour
`StartChannel`) — vérifier chaque emplacement exact sur le fichier réel avant d'éditer, les
blocs ci-dessous sont ceux lus au moment de l'écriture de ce plan.

**`StartInstant()`** — trouver :
```csharp
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingHitSlot    = slot;
```
Remplacer par :
```csharp
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingHitSlot    = slot;
```

**`StartMultiHit()`** — trouver :
```csharp
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingMultiSlot      = slot;
```
Remplacer par :
```csharp
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        _pendingMultiSlot      = slot;
```

**`LaunchComboHit()`** — trouver :
```csharp
        _player.BeginSkillUse(skill);
        _player.SpendMana(GetEffectiveManaCost(skill));
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);
        EngageAndFaceTarget(skill, slot, target);
```
Remplacer par :
```csharp
        _player.BeginSkillUse(skill);
        _player.SpendMana(GetEffectiveManaCost(skill));
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        EngageAndFaceTarget(skill, slot, target);
```

**`StartChannel()`** — trouver :
```csharp
        _player.AnimatorController?.PlayChannel(skill.channelAnimation);

        // GroundTarget : raycast au LANCEMENT (aim-then-channel), pas à la résolution — le point
```
Remplacer par :
```csharp
        _player.AnimatorController?.PlayChannel(skill.channelAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        // GroundTarget : raycast au LANCEMENT (aim-then-channel), pas à la résolution — le point
```

- [ ] **Step 2 : Restructurer `ResolveInstant()` — garde combo AVANT branchement zone différée**

Trouver le corps actuel complet :
```csharp
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

Remplacer intégralement par :
```csharp
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

        // Combo step : TOUJOURS résolution immédiate, JAMAIS de zone différée — évalué en
        // PREMIER, avant tout branchement hasDelayedImpact. C'est la vraie protection contre
        // un step de combo configuré avec hasDelayedImpact = true (le warning OnValidate seul
        // ne peut pas détecter ce cas — voir Tâche 1). La suite (fenêtre suivante, ou fin de
        // combo + CD) est gérée par AdvanceComboAfterHit ; le CD n'est PAS posé ici dans ce cas
        // (seul le DERNIER step d'un combo pose le CD, inchangé).
        if (IsComboActive && slot == _comboSlot)
        {
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
            AdvanceComboAfterHit(slot, skill);
            return;
        }

        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
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

- [ ] **Step 3 : Restructurer `ResolveChannel()` — branchement zone différée**

Trouver le corps actuel complet :
```csharp
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
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }
    }
```

Remplacer intégralement par :
```csharp
    private void ResolveChannel()
    {
        if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

        // Capturer AVANT EndChannelState() — celle-ci met _channelTarget à null, et
        // Execute()/PlantDelayedZone() ont besoin de la vraie cible.
        SkillData skill  = _channelSkill;
        int       slot   = _channelSlot;
        Entity    target = _channelTarget;

        EndChannelState();

        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }
    }
```

- [ ] **Step 4 : Compiler (cumulativement avec les Tâches 1-2), vérifier 0 erreur**

- [ ] **Step 5 : Commit**

```bash
git add Data/Skills/SkillBar.cs
git commit -m "$(cat <<'EOF'
feat: wire vfxCast spawn + delayed-impact-zone branching in SkillBar

vfxCast spawns at the caster's position in all 4 launch paths
(StartInstant, StartChannel, StartMultiHit, LaunchComboHit) -- none of
them spawned any VFX before this, it's new wiring in each.

ResolveInstant()'s existing combo guard (IsComboActive && slot ==
_comboSlot) now runs BEFORE any hasDelayedImpact branching and always
resolves immediately via ResolveExecute() -- this is the real
protection against a combo step configured with hasDelayedImpact=true
(SkillData's own OnValidate warning can't see that case, since a
combo step's own executionType is Normal same as any plain skill).
Only past that guard does a non-combo Normal skill get routed to
PlantDelayedZone() when hasDelayedImpact is set, else the existing
ResolveExecute() path, unchanged. ResolveChannel() gets the same
branching against Execute(). CD/GCD/DelayAutoAttack post right after
the branch in both methods, identical in both branches -- the zone
only delays damage, never cost/cooldown timing.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Vérification manuelle Play Mode

**Files:** aucun — tâche de vérification uniquement, pas de code.

**Interfaces:**
- Consumes : l'intégralité du système construit par les Tâches 1-3.
- Produces : rien — confirme que le système fonctionne, ou remonte les écarts à corriger.

Pas de framework de test automatisé sur ce projet. Cette checklist nécessite d'abord que
Florian crée au moins 2-3 assets `SkillData` de test avec `hasDelayedImpact = true` (geste
Editor Unity, pas automatisable par un agent) :
- Un skill `targetType = Target`, `hasDelayedImpact = true`, `zoneDuration = 0`,
  `impactDelay ≈ 2s`, **`aoeRadius > 0` (ex: 2-3)** (simule la comète). ⚠ `aoeRadius` vaut 0 par
  défaut et n'est jamais utilisé par un skill `Target` normal — pour une zone différée il
  redevient la taille réelle de la zone (`Physics.OverlapSphere`), l'oublier donne une zone de
  rayon 0 qui semble "rater" au moindre mouvement de la cible.
- Un skill `targetType = GroundTarget`, mêmes réglages (`aoeRadius > 0` aussi).
- Un skill `zoneDuration > 0` (ex: 5s), `zoneTickInterval = 1` (simule une zone de lave).

- [ ] **Step 1 : Esquive réelle sur `targetType = Target`**

Lancer le skill sur un mob/joueur cible. Vérifier : la zone se plante à la position ACTUELLE de
la cible au moment de la résolution (fin d'anim), PAS suivie ensuite. Faire bouger la cible
hors de la zone avant la fin de `impactDelay` → aucun dégât. Rester dans la zone → dégâts au
bon moment (`impactDelay` après le plantage, pas avant).

- [ ] **Step 2 : `targetType = GroundTarget`**

Vérifier que la zone se plante au point cliqué, pas sur une entité. Lancer un AUTRE skill
`GroundTarget` normal (sans `hasDelayedImpact`) juste après, avant que la 1ère zone ait détoné
→ vérifier qu'il vise bien le NOUVEAU point cliqué, pas l'ancien (confirme le reset de
`_groundTargetPoint`).

- [ ] **Step 3 : Zone persistante (`zoneDuration > 0`)**

Vérifier plusieurs ticks de dégâts espacés de `zoneTickInterval`. Entrer dans la zone en cours
de route (après le 1er tick) → touché au tick suivant. Sortir de la zone → plus touché au tick
d'après. `vfxZoneMarker` reste affiché pendant toute la durée, disparaît à la fin.

- [ ] **Step 4 : CD posé au lancement, pas à la détonation**

Lancer un skill `hasDelayedImpact`, vérifier que son CD (et le GCD si slot ≥ 1) démarrent
immédiatement au plantage de la zone — le joueur peut relancer un autre skill (slot différent)
normalement pendant que la zone attend sa détonation.

- [ ] **Step 5 : `[FormerlySerializedAs]` préserve les assets existants**

Ouvrir 2-3 `SkillData` existants qui avaient déjà un `vfxPrefab` assigné avant ce chantier —
vérifier que le champ `vfxImpact` affiche bien la même valeur après le renommage (pas de
référence perdue).

- [ ] **Step 6 : Warning Console sur MultiHit/ComboSequence + `hasDelayedImpact`**

Sur un skill de test, forcer `executionType = MultiHit` (ou `ComboSequence`) ET
`hasDelayedImpact = true` (temporairement, via script ou en trichant le ShowIf dans l'Inspector
debug) → vérifier le warning en Console. Puis vérifier le VRAI cas protégé : un step de combo
existant (`comboSteps[i]`) avec `hasDelayedImpact = true` activé sur SON asset → lancer le
combo → vérifier qu'il résout bien immédiatement (comportement combo normal), sans planter de
zone, malgré le champ activé sur l'asset (confirme la garde côté code dans
`ResolveInstant()`).

- [ ] **Step 7 : Garde-fou `zoneTickInterval` trop bas**

Sur le skill de test "zone persistante" (Step 3), régler temporairement `zoneTickInterval = 0`
(ou une valeur négative) → vérifier que le jeu NE part PAS en boucle infinie de dégâts/VFX à
chaque frame — le clamp défensif dans `DelayedZoneRoutine()` doit forcer un intervalle minimum
(0.05s) quoi qu'il arrive. Remettre `zoneTickInterval` à une valeur normale après ce test.

Si tous les points ci-dessus passent, le chantier C est complet et fonctionnel pour le joueur.
