# Zone différée & Trajectoire mobile pour Mob/PNJ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Câbler `hasDelayedImpact`/`isTrajectory` dans les boucles de décision Mob/PNJ, pour
qu'un Mob/PNJ puisse planter une zone à impact différé ou envoyer une trajectoire mobile, comme
le Player le fait déjà.

**Architecture:** `PlantDelayedZone()`/`StartTrajectory()` (déjà caster-agnostiques dans leur
corps) gagnent un host temporaire pour les casters non-Player, afin que leur coroutine survive
même si le caster (Mob) est détruit avant la fin de la zone/trajectoire. `Mob.cs`/`PNJ.cs`
gagnent un branchement mécanique (`if hasDelayedImpact / else if isTrajectory / else Execute`)
partout où ils appellent aujourd'hui `Execute()` nu.

**Tech Stack:** Unity C#, coroutines (`IEnumerator`), pas de framework de test automatisé —
vérification manuelle Play Mode uniquement.

**Spec:** `docs/superpowers/specs/2026-09-12-mob-pnj-delayed-trajectory-design.md`

## Global Constraints

- Scope réduit à `targetType` Target/AoE_Target (`hasDelayedImpact` — vérifié dans
  `SkillData.cs:226-229` : `Self`/`AoE_Self` ne font PAS partie du `[ShowIf]` de ce champ,
  seuls `Target`/`GroundTarget`/`AoE_Target` en font partie) et Direction (`isTrajectory`).
  `GroundTarget` reste techniquement sélectionnable pour `hasDelayedImpact` mais est HORS SCOPE
  volontaire pour Mob/PNJ — aucune plomberie de ciblage nouvelle nécessaire, mais aucun
  garde-fou non plus si un designer le configure quand même (retombe silencieusement sur la
  position du Mob lui-même, pas un crash — voir spec, section Décision 1).
- Host temporaire : quand `caster.entityType != EntityType.Player`, la coroutine tourne sur un
  `new GameObject(...).AddComponent<SkillSystem>()` dédié qui se détruit lui-même
  (`Destroy(gameObject)`) à la toute fin de sa PROPRE routine. Le chemin Player (`host == this`)
  reste inchangé. Le risque réel concerne spécifiquement le premier `WaitForSeconds(impactDelay)`
  de `DelayedZoneRoutine()` (aucune vérification `isDead` pendant cette attente) — pour
  `TrajectoryRoutine()` (vérification `isDead` à CHAQUE FRAME), le host est ajouté par symétrie/
  défense-en-profondeur, pas parce qu'un scénario de fuite concret existe pour cette méthode.
- `DelayedZoneRoutine()`/`TrajectoryRoutine()` gagnent un nouveau paramètre `bool
  destroySelfOnFinish`, ajouté APRÈS tous les paramètres existants.
- `TrajectoryRoutine()` a DEUX points de sortie (sortie précoce `totalDistance <= 0.01f`, fin de
  méthode naturelle/`casterDied`) — les DEUX ajoutent le nettoyage `destroySelfOnFinish`, au même
  endroit que le nettoyage `trajectoryVfx` déjà en place sur chacun. `DelayedZoneRoutine()` n'a
  qu'UN seul point de sortie (fin de méthode, après le nettoyage du marker).
- **Les dégâts s'arrêtent TOUJOURS si le caster meurt en cours de route** (`if (caster == null
  || caster.isDead) break;` dans les deux coroutines) — le host temporaire ne change PAS ce
  comportement, il protège uniquement le NETTOYAGE (marker/host lui-même) de la destruction du
  GameObject caster, pas la résolution des dégâts. Ne pas confondre les deux en test (voir
  Task 4, Step 3, corrigé en conséquence).
- **Fix du singleton `SkillSystem.Instance`** (trouvé en vérification, validé par Florian) :
  `Awake()` ne réclame `Instance` que si le GameObject porte un composant `Player` — empêche un
  Mob/PNJ/host temporaire de voler la place si son `Awake()` s'exécute avant celui du Player
  (Unity ne garantit aucun ordre entre `Awake()` de GameObjects différents). Inclus dans Task 1.
- **Retrait de `[RequireComponent(typeof(MonoBehaviour))]`** sur la classe `SkillSystem` (trouvé
  en vérification) : `MonoBehaviour` est abstrait, donc jamais réellement ajoutable par Unity —
  cet attribut est un no-op aujourd'hui (tous les `SkillSystem` existants vivent déjà sur un
  GameObject qui porte un VRAI MonoBehaviour concret comme `Player`/`Mob`/`PNJ`). Mais un host
  temporaire créé via `new GameObject(...).AddComponent<SkillSystem>()` n'a AUCUN autre
  composant — risque réel que `RequireComponent` échoue à satisfaire sa dépendance sur un type
  abstrait et fasse planter l'`AddComponent`. Retiré par précaution, aucun changement de
  comportement pour les cas existants (l'attribut n'y faisait déjà rien).
- Aucun nouveau champ `SkillData`, aucun changement de save format.
- Préserver l'idiome existant de CHAQUE fichier tel quel — Mob.cs utilise `?.` sur
  `_skillSystem`, PNJ.cs ne l'utilise pas (déjà garanti non-null par l'appelant) — ne pas
  uniformiser entre les deux.

---

### Task 1: Host temporaire dans `Combat/SkillSystem.cs`

**Files:**
- Modify: `Combat/SkillSystem.cs` (classe `SkillSystem` — attribut de classe, `Awake()`,
  méthodes `PlantDelayedZone()`, `DelayedZoneRoutine()`, `StartTrajectory()`,
  `TrajectoryRoutine()`)

**Interfaces:**
- Consumes: rien (modification interne à ce fichier)
- Produces: `PlantDelayedZone(SkillData, Entity, Entity)` et `StartTrajectory(SkillData,
  Entity)` gardent leur signature PUBLIQUE inchangée (aucun appelant externe n'a besoin de
  changer) — seul leur comportement interne change. Utilisées par Task 2 (Mob.cs) et Task 3
  (PNJ.cs) sans aucune modification de signature à connaître.

- [ ] **Step 1: Retirer `[RequireComponent(typeof(MonoBehaviour))]`**

Dans `Combat/SkillSystem.cs`, la classe est déclarée ainsi :

```csharp
[RequireComponent(typeof(MonoBehaviour))]
public class SkillSystem : MonoBehaviour
{
```

Remplacer par :

```csharp
public class SkillSystem : MonoBehaviour
{
```

`MonoBehaviour` est abstrait — cet attribut ne fait rien aujourd'hui (tous les `SkillSystem`
existants vivent déjà sur un GameObject avec un vrai MonoBehaviour concret) mais pourrait faire
échouer `AddComponent<SkillSystem>()` sur le GameObject nu du host temporaire ajouté au Step 4
plus bas. Aucun changement de comportement pour les cas existants.

- [ ] **Step 2: Fixer le singleton dans `Awake()`**

Remplacer :

```csharp
    private void Awake()
    {
        // Singleton souple — le Player porte l'instance principale.
        // Les Mob/PNJ portent leur propre composant sans écraser l'instance.
        if (Instance == null)
            Instance = this;
    }
```

par :

```csharp
    private void Awake()
    {
        // Singleton souple — le Player porte l'instance principale. Seul un SkillSystem sur un
        // GameObject avec un composant Player peut réclamer Instance — un Mob/PNJ/host
        // temporaire ne l'écrase donc jamais, peu importe l'ordre réel des Awake() (non garanti
        // par Unity entre GameObjects différents). Corrige un bug réel trouvé en vérification :
        // avant ce fix, `if (Instance == null)` seul pouvait laisser un Mob gagner la course au
        // démarrage et voler la place — cassant silencieusement tous les skills du Player
        // ensuite (SkillBar/TargetingSystem/PassiveSkillSystem appellent tous
        // SkillSystem.Instance?.Execute(...), le `?.` ne faisant plus rien sur un Instance
        // devenu un Mob mort).
        if (Instance == null && GetComponent<Player>() != null)
            Instance = this;
    }
```

- [ ] **Step 3: Lire les 4 méthodes en entier avant de modifier**

Dans `Combat/SkillSystem.cs`, lire en entier : `PlantDelayedZone()`, `DelayedZoneRoutine()`,
`StartTrajectory()`, `TrajectoryRoutine()`. Le code ACTUEL de `PlantDelayedZone()` (pour
référence — confirme qu'il correspond au fichier réel avant de continuer) :

```csharp
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
```

Le code ACTUEL de `DelayedZoneRoutine()` :

```csharp
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker)
    {
        yield return new WaitForSeconds(skill.impactDelay);

        // Clamp défensif — un zoneTickInterval <= 0 (mauvaise saisie, ou valeur posée par script/
        // API en contournant le [Min] de l'Inspector) ferait tourner cette boucle indéfiniment,
        // un OverlapSphere + dégâts + VFX à CHAQUE FRAME jusqu'à la mort du caster. 0.05s = 20
        // ticks/seconde max, largement suffisant pour tout usage gameplay réel.
        float tickInterval = Mathf.Max(0.05f, skill.zoneTickInterval);

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

            yield return new WaitForSeconds(tickInterval);
            remaining -= tickInterval;
        }

        if (marker != null) Destroy(marker);
    }
```

Le code ACTUEL de `StartTrajectory()` :

```csharp
    public void StartTrajectory(SkillData skill, Entity caster)
    {
        if (skill == null || caster == null || caster.isDead) return;

        if (caster.entityType == EntityType.Player && caster is Player player)
        {
            player.ResolveSkillUse(skill, null);

            GameEventBus.Publish(new SkillUsedEvent
            {
                skill          = skill,
                target         = null,
                caster         = player,
                primaryElement = skill.PrimaryElement,
                isCombo        = skill.elements != null && skill.elements.Count >= 2,
                locationID     = player.currentZoneID,
                isInParty      = false,
            });
        }

        Vector3 origin = caster.transform.position;
        Vector3 destination;

        if (skill.targetType == TargetType.GroundTarget)
        {
            // Même consommation que ExecuteGroundTarget()/PlantDelayedZone() — sans ce reset,
            // un skill sans rapport lancé plus tard hériterait d'une position périmée.
            destination = _groundTargetPoint ?? origin;
            _groundTargetPoint = null;
        }
        else // TargetType.Direction (ou targetType incompatible — voir OnValidate, traité
             // comme Direction par défaut plutôt que planter)
        {
            // _skillDirection n'est en réalité JAMAIS posé par le flow joueur actuel —
            // SetSkillDirection() n'a qu'un seul appelant dans tout le projet
            // (TargetingSystem.TryExecuteSkill(), lui-même sans appelant, code mort). Le
            // fallback caster.transform.forward est donc TOUJOURS celui utilisé en pratique
            // aujourd'hui — comportement déjà identique pour ExecuteDirection()/
            // ExecuteSkillshot()/ExecuteCone(), pas une régression introduite ici. La direction
            // résolue est celle où le PERSONNAGE fait face, pas la souris/le regard caméra.
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            float range = skill.range > 0f ? skill.range : 10f;
            destination = origin + dir * range;
        }

        // Quaternion.LookRotation logue un warning Console sur un vecteur nul — cas dégénéré
        // origin == destination (raycast manqué), déjà géré par le yield break précoce de
        // TrajectoryRoutine juste après. Le VFX sera de toute façon détruit à la frame suivante
        // par ce même chemin de sortie — Quaternion.identity en attendant évite juste le warning.
        Vector3 initialDir = destination - origin;
        GameObject trajectoryVfx = skill.vfxTrajectory != null
            ? Instantiate(skill.vfxTrajectory, origin,
                initialDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(initialDir) : Quaternion.identity)
            : null;

        StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx));
    }
```

Le code ACTUEL de `TrajectoryRoutine()` (début et fin — le corps de la boucle principale ne
change pas dans cette tâche, non reproduit ici) :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx)
    {
        float totalDistance = Vector3.Distance(origin, destination);
        if (totalDistance <= 0.01f)
        {
            // Origine == destination (ex: GroundTarget avec raycast manqué, joueur a cliqué le
            // ciel) — sans ce fallback, le skill consomme mana/HP/or + cooldown au lancement puis
            // ne produit RIEN de perceptible, ce qui se lit comme un bouton mort/cassé.
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
            if (trajectoryVfx != null)
                Destroy(trajectoryVfx);
            yield break; // origine == destination, rien à parcourir
        }

        // ... corps de la boucle principale inchangé ...

        // Trajectoire terminée sans toucher personne (et sans que le caster soit mort en cours
        // de route — dans ce cas-là on ne veut PAS de VFX, "le caster est mort avant qu'on puisse
        // savoir" n'est pas un vrai "whiff") — même fallback que le cas origine==destination
        // ci-dessus : le skill a coûté mana/HP/or + cooldown, il doit produire un feedback même
        // sur un whiff total.
        if (!casterDied && alreadyHit.Count == 0)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
        }

        // Nettoyage INCONDITIONNEL — contrairement au fallback vfxImpact/soundEffect ci-dessus
        // (qui ne joue pas si casterDied), le VFX de trajet doit TOUJOURS disparaître, mort du
        // caster comprise, sinon il resterait affiché indéfiniment sur une trajectoire abandonnée.
        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);
    }
```

Si le contenu réel diverge de ce qui précède (au-delà de détails cosmétiques comme des
commentaires), ARRÊTE-TOI et signale-le (BLOCKED) plutôt que de deviner comment adapter les
étapes suivantes.

- [ ] **Step 4: Ajouter le host temporaire dans `PlantDelayedZone()`**

Remplacer :

```csharp
        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        StartCoroutine(DelayedZoneRoutine(skill, caster, position, marker));
    }
```

par :

```csharp
        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        // Mob/PNJ : Destroy(gameObject, délai de corpse) sur le caster tuerait cette coroutine
        // avant qu'elle atteigne son propre nettoyage si impactDelay+zoneDuration dépasse ce
        // délai — délègue à un host temporaire qui survit indépendamment du caster, détruit
        // lui-même en fin de routine. Le chemin Player (jamais détruit) reste inchangé, tourne
        // sur `this` comme aujourd'hui.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"DelayedZoneHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.DelayedZoneRoutine(skill, caster, position, marker, destroySelfOnFinish));
    }
```

- [ ] **Step 5: Ajouter le paramètre `destroySelfOnFinish` à `DelayedZoneRoutine()`**

Remplacer :

```csharp
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker)
    {
```

par :

```csharp
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, GameObject marker, bool destroySelfOnFinish)
    {
```

Puis remplacer la toute dernière ligne de la méthode :

```csharp
        if (marker != null) Destroy(marker);
    }
```

par :

```csharp
        if (marker != null) Destroy(marker);
        if (destroySelfOnFinish) Destroy(gameObject);
    }
```

- [ ] **Step 6: Ajouter le host temporaire dans `StartTrajectory()`**

Remplacer :

```csharp
        StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx));
    }
```

par :

```csharp
        // Mob/PNJ : même raison que PlantDelayedZone() ci-dessus — host temporaire qui survit
        // indépendamment du caster, détruit lui-même en fin de routine.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"TrajectoryHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx, destroySelfOnFinish));
    }
```

- [ ] **Step 7: Ajouter le paramètre `destroySelfOnFinish` à `TrajectoryRoutine()` — DEUX
      points de sortie à traiter**

Remplacer la signature :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx)
    {
```

par :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx, bool destroySelfOnFinish)
    {
```

Remplacer le premier point de sortie (sortie précoce `totalDistance <= 0.01f`) :

```csharp
            if (trajectoryVfx != null)
                Destroy(trajectoryVfx);
            yield break; // origine == destination, rien à parcourir
        }
```

par :

```csharp
            if (trajectoryVfx != null)
                Destroy(trajectoryVfx);
            if (destroySelfOnFinish)
                Destroy(gameObject);
            yield break; // origine == destination, rien à parcourir
        }
```

Remplacer le second point de sortie (fin de méthode, nettoyage inconditionnel) :

```csharp
        // Nettoyage INCONDITIONNEL — contrairement au fallback vfxImpact/soundEffect ci-dessus
        // (qui ne joue pas si casterDied), le VFX de trajet doit TOUJOURS disparaître, mort du
        // caster comprise, sinon il resterait affiché indéfiniment sur une trajectoire abandonnée.
        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);
    }
```

par :

```csharp
        // Nettoyage INCONDITIONNEL — contrairement au fallback vfxImpact/soundEffect ci-dessus
        // (qui ne joue pas si casterDied), le VFX de trajet doit TOUJOURS disparaître, mort du
        // caster comprise, sinon il resterait affiché indéfiniment sur une trajectoire abandonnée.
        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);
        if (destroySelfOnFinish)
            Destroy(gameObject);
    }
```

- [ ] **Step 8: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console. Vérifier en particulier qu'aucune
erreur ne mentionne `RequireComponent`/`AddComponent` (confirme que le retrait du Step 1 était
bien nécessaire ou au moins sans risque).

- [ ] **Step 9: Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "feat: temporary host so Mob/PNJ delayed-zone/trajectory coroutines outlive the caster

Also fixes a real singleton race (SkillSystem.Instance could be claimed
by a Mob if its Awake() ran before the Player's) and drops a no-op
RequireComponent that could have failed on the new host's bare GameObject."
```

---

### Task 2: Branchement dans `Entities/Mob.cs`

**Files:**
- Modify: `Entities/Mob.cs` (méthodes `TryUseSkill()` et `HandleAttack()`)

**Interfaces:**
- Consumes: `SkillSystem.PlantDelayedZone(SkillData, Entity, Entity)` et
  `SkillSystem.StartTrajectory(SkillData, Entity)` (Task 1, signatures publiques inchangées)
- Produces: rien (comportement interne à `Mob.cs`, aucune autre tâche n'en dépend)

- [ ] **Step 1: Lire les 2 méthodes en entier avant de modifier**

Dans `Entities/Mob.cs`, lire `TryUseSkill()` et `HandleAttack()` en entier. Le code ACTUEL de
`HandleAttack()` (pour référence) :

```csharp
    private void HandleAttack()
    {
        if (isDead) return;

        if (IsBeyondLeash()) { GoReturn(); return; }

        if (statusEffects != null && (statusEffects.isStunned || statusEffects.isShocked || statusEffects.isFreezed)) return;

        Entity target = GetClosestEnemy();
        if (target == null || target.isDead) { GoReturn(); return; }

        if (!IsInRange(target, data.attackRange * 1.2f))
        {
            currentState = MobState.Chase;
            return;
        }

        LookAt(target.transform);

        // Skill secondaire prioritaire sur l'attaque de base — bloqué si Taunt actif, force
        // l'attaque de base uniquement sur la source du taunt (§3.1.1.1).
        bool tauntedInAttack = statusEffects != null && statusEffects.isTaunted;
        if (!tauntedInAttack && TryUseSkill(target)) return;

        // Attaque de base
        if (attackTimer <= 0f)
        {
            attackTimer = data.attackCooldown;

            // Double vérification avant de lancer l'attaque
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                _skillSystem?.Execute(data.basicAttackSkill, this, target);
            }
            else if (data.basicAttackSkill == null)
                Debug.LogWarning($"[MOB] {data.mobName} n'a pas de basicAttackSkill — assigne un SkillData dans MobData.");
        }
    }
```

Le code ACTUEL de `TryUseSkill()` :

```csharp
    private bool TryUseSkill(Entity target)
    {
        if (data.skills == null || data.skills.Count == 0) return false;

        // Tick des cooldowns
        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            if (_skillCooldowns.ContainsKey(skill))
                _skillCooldowns[skill] -= Time.deltaTime;
        }

        // Premier skill prêt et à portée
        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
            if (cd > 0f) continue;
            if (!IsInRange(target, skill.range)) continue;
            if (skill.manaCost > 0f && !HasMana(skill.manaCost)) continue;

            if (skill.manaCost > 0f) SpendMana(skill.manaCost);

            LookAt(target.transform);
            _skillSystem?.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            attackTimer = data.attackCooldown;
            return true;
        }

        return false;
    }
```

Si le contenu réel diverge de ce qui précède, ARRÊTE-TOI et signale-le (BLOCKED).

- [ ] **Step 2: Brancher `TryUseSkill()`**

Remplacer :

```csharp
            LookAt(target.transform);
            _skillSystem?.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
```

par :

```csharp
            LookAt(target.transform);
            if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, this);
            else
                _skillSystem?.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
```

- [ ] **Step 3: Brancher le `basicAttackSkill` dans `HandleAttack()`**

Remplacer :

```csharp
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                _skillSystem?.Execute(data.basicAttackSkill, this, target);
            }
```

par :

```csharp
            if (!isDead && !target.isDead && data.basicAttackSkill != null)
            {
                if (data.basicAttackSkill.hasDelayedImpact)
                    _skillSystem?.PlantDelayedZone(data.basicAttackSkill, this, target);
                else if (data.basicAttackSkill.isTrajectory)
                    _skillSystem?.StartTrajectory(data.basicAttackSkill, this);
                else
                    _skillSystem?.Execute(data.basicAttackSkill, this, target);
            }
```

- [ ] **Step 4: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 5: Commit**

```bash
git add Entities/Mob.cs
git commit -m "feat: wire hasDelayedImpact/isTrajectory into Mob skill decision loop"
```

---

### Task 3: Branchement dans `Entities/PNJ.cs`

**Files:**
- Modify: `Entities/PNJ.cs` (méthodes `TryUseSecondarySkill()` et `HandleCombatAI()`)

**Interfaces:**
- Consumes: `SkillSystem.PlantDelayedZone(SkillData, Entity, Entity)` et
  `SkillSystem.StartTrajectory(SkillData, Entity)` (Task 1)
- Produces: rien

- [ ] **Step 1: Lire les 2 méthodes en entier avant de modifier**

Dans `Entities/PNJ.cs`, lire `TryUseSecondarySkill()` et `HandleCombatAI()` en entier. Le code
ACTUEL de `HandleCombatAI()` (pour référence, section pertinente uniquement) :

```csharp
        if (dist <= attackRange)
        {
            _agent?.ResetPath();
            LookAt(_combatTarget.transform);
            // Skill secondaire bloqué si Taunt actif — force l'attaque de base uniquement sur
            // la source du taunt (§3.1.1.1), même schéma que Mob.HandleAttack.
            bool tauntedNow = statusEffects != null && statusEffects.isTaunted;
            if (!tauntedNow && TryUseSecondarySkill(_combatTarget)) return;
            if (_attackTimer <= 0f && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                    _skillSystem.Execute(data.basicAttackSkill, this, _combatTarget);
                _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            }
        }
```

Le code ACTUEL de `TryUseSecondarySkill()` :

```csharp
    private bool TryUseSecondarySkill(Entity target)
    {
        if (data.skills == null || data.skills.Count == 0) return false;

        // Tick des cooldowns
        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            if (_skillCooldowns.ContainsKey(skill))
                _skillCooldowns[skill] -= Time.deltaTime;
        }

        foreach (var skill in data.skills)
        {
            if (skill == null) continue;
            float cd = _skillCooldowns.ContainsKey(skill) ? _skillCooldowns[skill] : 0f;
            if (cd > 0f) continue;

            float range = skill.range > 0f ? skill.range : data.attackRange;
            if (Vector3.Distance(transform.position, target.transform.position) > range) continue;
            if (skill.manaCost > 0f && !HasMana(skill.manaCost)) continue;

            if (skill.manaCost > 0f) SpendMana(skill.manaCost);

            LookAt(target.transform);
            _skillSystem.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
            _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            return true;
        }

        return false;
    }
```

Si le contenu réel diverge de ce qui précède, ARRÊTE-TOI et signale-le (BLOCKED).

- [ ] **Step 2: Brancher `TryUseSecondarySkill()`**

Remplacer :

```csharp
            LookAt(target.transform);
            _skillSystem.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
```

par :

```csharp
            LookAt(target.transform);
            if (skill.hasDelayedImpact)
                _skillSystem.PlantDelayedZone(skill, this, target);
            else if (skill.isTrajectory)
                _skillSystem.StartTrajectory(skill, this);
            else
                _skillSystem.Execute(skill, this, target);
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
```

Note : PNJ.cs n'utilise PAS `?.` sur `_skillSystem` (déjà garanti non-null par le `if
(_skillSystem == null) return;` de l'appelant `HandleCombatAI()`) — préserve cet idiome tel
quel, ne mets pas de `?.` ici même si Mob.cs (Task 2) en a.

- [ ] **Step 3: Brancher le `basicAttackSkill` dans `HandleCombatAI()`**

Remplacer :

```csharp
            if (_attackTimer <= 0f && data.basicAttackSkill != null)
            {
                if (!isDead && !_combatTarget.isDead)
                    _skillSystem.Execute(data.basicAttackSkill, this, _combatTarget);
                _attackTimer = data.attackCooldown > 0f ? data.attackCooldown : 2f;
            }
```

par :

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

- [ ] **Step 4: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 5: Commit**

```bash
git add Entities/PNJ.cs
git commit -m "feat: wire hasDelayedImpact/isTrajectory into PNJ skill decision loop"
```

---

### Task 4: Vérification manuelle Play Mode (geste Florian)

**Files:** aucun fichier de code — configuration temporaire de `hasDelayedImpact`/`isTrajectory`
sur un skill ou `basicAttackSkill` d'un Mob/PNJ de test existant, pas automatisable par un agent.

**Interfaces:**
- Consumes: comportement de Task 1/2/3
- Produces: rien — tâche de validation finale

- [ ] **Step 1: Tester Mob avec `hasDelayedImpact` + `Target`**

Configurer temporairement `hasDelayedImpact = true` sur un skill secondaire (`data.skills`)
d'un Mob de test, `targetType = Target`. Le Mob doit planter une zone à la position de sa cible
(au moment du cast), infliger les dégâts après `impactDelay`. Vérifier que la cible peut esquiver
en sortant de la zone avant la détonation.

- [ ] **Step 2: Tester Mob avec `isTrajectory` + `Direction`**

Configurer temporairement `isTrajectory = true` sur un skill secondaire du même Mob,
`targetType = Direction`. Le Mob doit envoyer une hitbox qui voyage tout droit dans la direction
où il fait face (vers sa cible, puisqu'il vient de faire `LookAt`).

- [ ] **Step 3: Tester la mort du Mob PENDANT le `impactDelay` d'une zone (pas après)**

**Important** : les dégâts s'arrêtent TOUJOURS si le caster meurt en cours de route (`if (caster
== null || caster.isDead) break;` dans les deux coroutines) — le host temporaire ne change PAS
ça, il protège uniquement le NETTOYAGE contre la destruction du GameObject caster. Ne teste donc
PAS "les dégâts continuent après la mort du Mob" (ça ne doit PAS arriver, par design).

Ce qu'il faut réellement vérifier : configurer un `impactDelay` supérieur à 3 secondes (le délai
de corpse du Mob) sur un skill `hasDelayedImpact`, faire tuer le Mob JUSTE APRÈS le lancement
(donc PENDANT l'attente `impactDelay`, avant même que la boucle de tick ne démarre) — vérifier
dans la Hierarchy qu'un GameObject `DelayedZoneHost_<nomDuSkill>` apparaît au lancement,
**survit** à la disparition du Mob (le corps disparaît après 3s, le host doit encore exister à ce
moment), puis se nettoie proprement lui-même une fois que sa propre boucle détecte
`caster.isDead` (aucune fuite visible dans la Hierarchy après ce point — ni le marker, ni le
host). Sans le host, ce même test ferait disparaître le GameObject AVANT ce nettoyage (le Mob
lui-même aurait été détruit avec sa coroutine dessus), laissant le marker affiché pour toujours.

- [ ] **Step 4: Vérifier le CD posé au bon moment**

Le Mob ne doit pas pouvoir relancer le même skill avant son `cooldown`, peu importe la durée de
la zone/trajectoire déclenchée (CD déjà posé au lancement, avant même que
`PlantDelayedZone()`/`StartTrajectory()` ne soit appelée).

- [ ] **Step 5: Tester la parité PNJ**

Répéter les Steps 1 à 4 sur un PNJ combattant/hostile de test plutôt qu'un Mob, pour confirmer
que le comportement est identique.

- [ ] **Step 6: Tester le `basicAttackSkill` avec les flags**

Configurer temporairement `hasDelayedImpact` ou `isTrajectory` sur le `basicAttackSkill` d'un
Mob de test (cas de test, pas forcément un besoin de design réel) — confirmer que ça fonctionne
aussi sur ce chemin, pas seulement sur les skills secondaires.

- [ ] **Step 7: Rapporter les résultats**

Florian confirme si tous les points ci-dessus passent, ou signale les écarts observés pour
investigation.
