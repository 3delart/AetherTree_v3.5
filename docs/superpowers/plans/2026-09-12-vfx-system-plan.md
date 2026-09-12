# Système VFX (trajet + statut) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un VFX qui suit une hitbox `isTrajectory` en mouvement (position + orientation),
et un VFX persistant tant qu'un buff/debuff est actif sur une entité (attaché en enfant de son
transform, suit tout automatiquement).

**Architecture:** Un nouveau champ `SkillData.vfxTrajectory`, mis à jour à chaque frame par
`SkillSystem.TrajectoryRoutine()` (position + `Quaternion.LookRotation`), détruit sur les 3
chemins de sortie de la coroutine. Un nouveau champ `StatusEffectData.statusVfx` (classe de base
partagée Buff/Debuff), spawné en enfant du transform de l'entité à la création d'une NOUVELLE
instance (jamais sur un simple refresh) via une nouvelle méthode privée
`StatusEffectSystem.SpawnStatusVfx()`, détruit à l'expiration réelle de cette instance précise
(référence stockée sur `StatusEffectInstance.spawnedVfx`, propre à chaque stack).

**Tech Stack:** Unity C#, ScriptableObject (`SkillData`, `StatusEffectData`), coroutines
(`IEnumerator`), pas de framework de test automatisé — vérification manuelle Play Mode
uniquement.

**Spec:** `docs/superpowers/specs/2026-09-12-vfx-system-design.md`

## Global Constraints

- `vfxTrajectory` : GameObject optionnel sur `SkillData`, `[ShowIf(nameof(isTrajectory), true)]`,
  ajouté juste après `vfxCast`. Portée Player uniquement (comme chantiers A-D).
- `statusVfx` : GameObject optionnel sur `StatusEffectData` (classe de base abstraite), ajouté
  juste après `icon`. Portée TOUTE Entity (Player/Mob/PNJ) — aucune restriction.
- Aucun changement de save format, aucune migration — tous les nouveaux champs sont des
  `GameObject` optionnels par défaut `null`, zéro régression sur les assets existants.
- Le VFX de statut ne spawn JAMAIS sur un simple `Refresh()` d'une instance déjà active — une
  seule instance = un seul VFX, jamais de duplication au recast.
- Le nettoyage du VFX de statut à l'expiration doit être placé AVANT tout `return` interne
  type-spécifique dans `ExpireBuffInstance()`/`ExpireDebuffInstance()` (ces méthodes ont des
  branches qui `return` tôt pour les types `Stats`).
- Le nettoyage du VFX de trajet dans `TrajectoryRoutine()` est INCONDITIONNEL sur les 3 chemins
  de sortie (contrairement au fallback vfxImpact/soundEffect existant, qui lui ne joue PAS si le
  caster est mort en cours de route — le VFX de trajet, lui, doit toujours disparaître, mort du
  caster comprise, sinon VFX fantôme abandonné).
- Piège identifié dans la spec (à ne pas reproduire) : la mise à jour position+rotation de la
  boucle principale DOIT être placée À L'INTÉRIEUR du bloc `if (segment > 0.0001f) { ... }`
  existant, AVANT la ligne `previousPos = currentPos;` qui suit ce bloc — si placée après,
  `currentPos - previousPos` vaut toujours zéro (les deux variables seraient déjà égales).
- `TryConsumeRevive()` (6ᵉ site de retrait d'instance, trouvé lors de la vérification
  indépendante de ce plan) retire l'instance `Revive` directement du dictionnaire SANS passer par
  `ExpireBuffInstance()` — nécessite son propre nettoyage `spawnedVfx` explicite (Task 5, Step 6),
  sinon un `statusVfx` sur un buff Revive fuit indéfiniment (le joueur ressuscite, son GameObject
  n'est jamais détruit).

---

### Task 1: Champ `vfxTrajectory` sur `SkillData`

**Files:**
- Modify: `Data/Skills/SkillData.cs:269` (ajout du champ juste après `vfxCast`)

**Interfaces:**
- Consumes: rien (nouveau champ indépendant)
- Produces: `public GameObject vfxTrajectory` — lu par `SkillSystem.StartTrajectory()`/
  `TrajectoryRoutine()` (Task 2)

- [ ] **Step 1: Ajouter le champ `vfxTrajectory`**

Dans `Data/Skills/SkillData.cs`, le champ `vfxCast` est déclaré ainsi (ligne 269) :

```csharp
    [Tooltip("VFX spawné à la position du caster AU LANCEMENT (pentacle aux pieds, glow aux\n" +
             "mains...) — ne suit PAS le caster ensuite s'il bouge (viendra avec le chantier VFX).\n" +
             "Optionnel — vide = pas de VFX de cast.")]
    public GameObject vfxCast;
```

Juste après cette ligne (`public GameObject vfxCast;`), insérer :

```csharp

    [Tooltip("VFX qui suit la hitbox pendant tout le déplacement d'un skill isTrajectory — suit " +
             "la position ET s'oriente selon la direction de déplacement. Actif uniquement si " +
             "isTrajectory = true. Distinct de vfxCast (spawné au lancement, ne suit pas) et de " +
             "vfxImpact (joué au moment du hit, ponctuel).")]
    [ShowIf(nameof(isTrajectory), true)]
    public GameObject vfxTrajectory;
```

Ce pattern `[ShowIf(nameof(isTrajectory), true)]` est déjà utilisé ailleurs dans ce même fichier
pour `vfxZoneMarker` (ligne 274-275, `[ShowIf(nameof(hasDelayedImpact), true)]`) — même idiome,
juste sur un booléen différent.

- [ ] **Step 2: Vérifier la compilation dans Unity**

Ouvrir Unity Editor (ou lancer une compilation batch si disponible), vérifier 0 erreur dans la
Console.

- [ ] **Step 3: Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "feat: add vfxTrajectory field to SkillData"
```

---

### Task 2: VFX de trajet dans `SkillSystem.StartTrajectory()`/`TrajectoryRoutine()`

**Files:**
- Modify: `Combat/SkillSystem.cs` (méthodes `StartTrajectory()` et `TrajectoryRoutine()`)

**Interfaces:**
- Consumes: `SkillData.vfxTrajectory` (Task 1)
- Produces: rien (comportement interne, aucune autre tâche n'en dépend)

- [ ] **Step 1: Lire les deux méthodes en entier avant de modifier**

Lire `Combat/SkillSystem.cs`, méthodes `StartTrajectory()` et `TrajectoryRoutine()` en entier
(recherche `public void StartTrajectory` et `private IEnumerator TrajectoryRoutine`). Ces
méthodes ont été modifiées par un fix wave après leur chantier initial (feedback sur un miss +
flag `casterDied`) — le code réel actuel est celui à lire, pas une version antérieure supposée.

Le code ACTUEL de `StartTrajectory()` (pour référence, vérifie qu'il correspond bien à ce que tu
lis dans le fichier réel) :

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
            destination = _groundTargetPoint ?? origin;
            _groundTargetPoint = null;
        }
        else // TargetType.Direction (ou targetType incompatible)
        {
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            float range = skill.range > 0f ? skill.range : 10f;
            destination = origin + dir * range;
        }

        StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination));
    }
```

Et le code ACTUEL de `TrajectoryRoutine()` :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination)
    {
        float totalDistance = Vector3.Distance(origin, destination);
        if (totalDistance <= 0.01f)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
            yield break;
        }

        float speed  = skill.projectileSpeed > 0f ? skill.projectileSpeed : 10f;
        float radius = skill.aoeRadius       > 0f ? skill.aoeRadius       : 0.5f;
        Vector3 dir  = (destination - origin) / totalDistance;

        HashSet<Entity> alreadyHit = new HashSet<Entity>();

        foreach (Collider col in Physics.OverlapSphere(origin, radius))
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (alreadyHit.Contains(entity)) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            alreadyHit.Add(entity);
            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            CheckKill(entity);

            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
        }

        Vector3 previousPos  = origin;
        float   traveled     = 0f;
        bool    casterDied   = false;

        while (traveled < totalDistance)
        {
            if (caster == null || caster.isDead) { casterDied = true; break; }

            traveled += speed * Time.deltaTime;
            Vector3 currentPos = origin + dir * Mathf.Min(traveled, totalDistance);
            float   segment    = Vector3.Distance(previousPos, currentPos);

            if (segment > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(previousPos, radius, (currentPos - previousPos).normalized, segment);
                foreach (RaycastHit h in hits)
                {
                    Entity entity = h.collider.GetComponentInParent<Entity>();
                    if (entity == null || entity.isDead) continue;
                    if (alreadyHit.Contains(entity)) continue;
                    if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                    alreadyHit.Add(entity);
                    ApplyEffectType(skill, caster, entity);
                    ApplyStatusEffects(skill, caster, entity);
                    CheckKill(entity);

                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
                }
            }

            previousPos = currentPos;
            yield return null;
        }

        if (!casterDied && alreadyHit.Count == 0)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
        }
    }
```

Si le contenu réel diverge de ce qui précède (au-delà de détails cosmétiques comme des
commentaires), ARRÊTE-TOI et signale-le (BLOCKED) plutôt que de deviner comment adapter les
étapes suivantes.

- [ ] **Step 2: Modifier `StartTrajectory()` — spawn initial + passage du paramètre**

Remplacer :

```csharp
        StartCoroutine(TrajectoryRoutine(skill, caster, origin, destination));
    }
```

par :

```csharp
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

- [ ] **Step 3: Modifier la signature de `TrajectoryRoutine()`**

Remplacer :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination)
    {
```

par :

```csharp
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx)
    {
```

- [ ] **Step 4: Nettoyage sur la sortie précoce (origin == destination)**

Remplacer (bloc EXACT du fichier réel, avec ses commentaires) :

```csharp
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
            yield break; // origine == destination, rien à parcourir
        }
```

par :

```csharp
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
```

- [ ] **Step 5: Mise à jour position+rotation dans la boucle principale**

**Attention, piège documenté dans la spec** : cette mise à jour doit se faire À L'INTÉRIEUR du
bloc `if (segment > 0.0001f) { ... }`, AVANT la ligne `previousPos = currentPos;` qui suit ce
bloc. Si placée après, `currentPos - previousPos` vaudrait toujours zéro (les deux variables
seraient déjà égales à ce point).

Remplacer (bloc EXACT du fichier réel, avec ses commentaires) :

```csharp
            if (segment > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(previousPos, radius, (currentPos - previousPos).normalized, segment);
                foreach (RaycastHit h in hits)
                {
                    Entity entity = h.collider.GetComponentInParent<Entity>();
                    if (entity == null || entity.isDead) continue;
                    if (alreadyHit.Contains(entity)) continue;
                    if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                    alreadyHit.Add(entity);
                    ApplyEffectType(skill, caster, entity);
                    ApplyStatusEffects(skill, caster, entity);
                    CheckKill(entity);

                    // Un vfxImpact/soundEffect PAR entité touchée — même précédent que
                    // ResolveMultiHitStep() (une trajectoire est une séquence de hits distincts,
                    // pas une zone unique comme DelayedZoneRoutine qui joue un seul vfx/son par
                    // tick peu importe combien d'entités touchées).
                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
                }
            }

            previousPos = currentPos;
            yield return null;
```

par :

```csharp
            if (segment > 0.0001f)
            {
                Vector3 segmentDir = (currentPos - previousPos).normalized;
                RaycastHit[] hits = Physics.SphereCastAll(previousPos, radius, segmentDir, segment);
                foreach (RaycastHit h in hits)
                {
                    Entity entity = h.collider.GetComponentInParent<Entity>();
                    if (entity == null || entity.isDead) continue;
                    if (alreadyHit.Contains(entity)) continue;
                    if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                    alreadyHit.Add(entity);
                    ApplyEffectType(skill, caster, entity);
                    ApplyStatusEffects(skill, caster, entity);
                    CheckKill(entity);

                    // Un vfxImpact/soundEffect PAR entité touchée — même précédent que
                    // ResolveMultiHitStep() (une trajectoire est une séquence de hits distincts,
                    // pas une zone unique comme DelayedZoneRoutine qui joue un seul vfx/son par
                    // tick peu importe combien d'entités touchées).
                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
                }

                if (trajectoryVfx != null)
                {
                    trajectoryVfx.transform.position = currentPos;
                    trajectoryVfx.transform.rotation = Quaternion.LookRotation(segmentDir);
                }
            }

            previousPos = currentPos;
            yield return null;
```

- [ ] **Step 6: Nettoyage inconditionnel en fin de méthode**

Remplacer :

```csharp
        if (!casterDied && alreadyHit.Count == 0)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, destination, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, destination);
        }
    }
```

par :

```csharp
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

- [ ] **Step 7: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 8: Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "feat: spawn and follow vfxTrajectory during TrajectoryRoutine"
```

---

### Task 3: Champ `statusVfx` sur `StatusEffectData`

**Files:**
- Modify: `Data/StatusEffect/StatusEffectData.cs:246` (ajout du champ juste après `icon`)

**Interfaces:**
- Consumes: rien (nouveau champ indépendant)
- Produces: `public GameObject statusVfx` — lu par `StatusEffectSystem.SpawnStatusVfx()` (Task 5)
  via `instance.data.statusVfx`

- [ ] **Step 1: Ajouter le champ `statusVfx`**

Dans `Data/StatusEffect/StatusEffectData.cs`, le champ `icon` est déclaré ainsi (ligne 246,
dans la classe abstraite `StatusEffectData`) :

```csharp
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    [Header("Durée")]
```

Remplacer par :

```csharp
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    [Tooltip("VFX persistant tant que ce buff/debuff est actif sur une entité — attaché en " +
             "enfant du transform de l'entité (suit position/rotation/animations " +
             "automatiquement). Optionnel — vide = pas de VFX de statut.")]
    public GameObject statusVfx;

    [Header("Durée")]
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 3: Commit**

```bash
git add Data/StatusEffect/StatusEffectData.cs
git commit -m "feat: add statusVfx field to StatusEffectData"
```

---

### Task 4: Champ `spawnedVfx` sur `StatusEffectInstance`

**Files:**
- Modify: `Data/StatusEffect/StatusEffectInstance.cs` (classe abstraite `StatusEffectInstance`)

**Interfaces:**
- Consumes: rien (nouveau champ indépendant)
- Produces: `public GameObject spawnedVfx` — écrit par `StatusEffectSystem.SpawnStatusVfx()`
  (Task 5), lu par `ExpireBuffInstance()`/`ExpireDebuffInstance()` (Task 5)

- [ ] **Step 1: Ajouter le champ `spawnedVfx`**

Dans `Data/StatusEffect/StatusEffectInstance.cs`, la classe abstraite de base est déclarée
ainsi :

```csharp
public abstract class StatusEffectInstance
{
    public StatusEffectData data;
    public Entity           source;
    public float            remainingTime;

    public bool IsExpired => remainingTime <= 0f;
```

Remplacer par :

```csharp
public abstract class StatusEffectInstance
{
    public StatusEffectData data;
    public Entity           source;
    public float            remainingTime;

    /// <summary>Référence à l'instance VFX spawnée pour CET effet précis (une par instance,
    /// propre au stacking : chaque instance stackée a la sienne). Null si data.statusVfx est
    /// vide ou pas encore spawnée.</summary>
    public GameObject spawnedVfx;

    public bool IsExpired => remainingTime <= 0f;
```

Ce champ est hérité par `BuffInstance` ET `DebuffInstance` (toutes deux définies plus bas dans ce
même fichier, héritent de `StatusEffectInstance`) — ne rien ajouter séparément sur ces deux
classes, le champ sur la base suffit.

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 3: Commit**

```bash
git add Data/StatusEffect/StatusEffectInstance.cs
git commit -m "feat: add spawnedVfx field to StatusEffectInstance base class"
```

---

### Task 5: Spawn/despawn du VFX de statut dans `StatusEffectSystem`

**Files:**
- Modify: `Entities/StatusEffectSystem.cs` (méthodes `ApplyBuff()`, `TryApplyDebuff()`,
  `ApplyBuffWithDuration()`, `ExpireBuffInstance()`, `ExpireDebuffInstance()`,
  `TryConsumeRevive()`)

**Interfaces:**
- Consumes: `StatusEffectData.statusVfx` (Task 3), `StatusEffectInstance.spawnedVfx` (Task 4)
- Produces: `private void SpawnStatusVfx(StatusEffectInstance instance)` — usage interne à ce
  fichier uniquement, aucune autre tâche n'en dépend

- [ ] **Step 1: Lire les 5 méthodes concernées en entier avant de modifier**

Dans `Entities/StatusEffectSystem.cs`, lire en entier : `ApplyBuff()`, `TryApplyDebuff()`,
`ApplyBuffWithDuration()`, `ExpireBuffInstance()`, `ExpireDebuffInstance()`, `TryConsumeRevive()`.
Le fichier est volumineux (1200+ lignes) — pas besoin de lire les autres méthodes, mais celles-ci
doivent être lues intégralement pour confirmer que le code ci-dessous correspond exactement à ce
qui existe. `TryConsumeRevive()` est un 6ᵉ site de retrait d'instance (Step 6 plus bas) — trouvé
lors de la vérification indépendante de ce plan : il retire l'instance `Revive` directement du
dictionnaire SANS passer par `ExpireBuffInstance()`, donc sans jamais nettoyer `spawnedVfx`.

Le code ACTUEL de `TryApplyDebuff()` (pour référence) :

```csharp
    public bool TryApplyDebuff(DebuffData debuff, Entity source)
    {
        if (debuff == null || _entity.isDead) return false;

        float resistance = GetDebuffResistance(debuff.debuffType);
        if (resistance > 0f && Random.value < resistance)
            return false;

        if (!StackableDebuffTypes.Contains(debuff.debuffType))
        {
            if (_activeDebuffs.TryGetValue(debuff.debuffType, out var existingList) && existingList.Count > 0)
            {
                var existing = existingList[0];
                existing.data = debuff;
                existing.Refresh();
                existing.source = source;
                return true;
            }

            DebuffInstance newInstance = (DebuffInstance)debuff.CreateInstance(source);
            _activeDebuffs[debuff.debuffType] = new List<DebuffInstance> { newInstance };
            OnApplyDebuff(newInstance);
            return true;
        }

        if (_activeDebuffs.TryGetValue(debuff.debuffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == debuff);
            if (sameAsset != null)
            {
                sameAsset.data = debuff;
                sameAsset.Refresh();
                sameAsset.source = source;
                return true;
            }
        }
        else
        {
            list = new List<DebuffInstance>();
            _activeDebuffs[debuff.debuffType] = list;
        }

        DebuffInstance stackedInstance = (DebuffInstance)debuff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyDebuff(stackedInstance);

        return true;
    }
```

Le code ACTUEL de `ApplyBuff()` (pour référence) :

```csharp
    public void ApplyBuff(BuffData buff, Entity source)
    {
        if (buff == null || _entity.isDead) return;

        if (!StackableBuffTypes.Contains(buff.buffType))
        {
            if (_activeBuffs.TryGetValue(buff.buffType, out var existingList) && existingList.Count > 0)
            {
                var existing = existingList[0];
                existing.data = buff;
                existing.Refresh();
                existing.source = source;
                return;
            }

            BuffInstance newInstance = (BuffInstance)buff.CreateInstance(source);
            _activeBuffs[buff.buffType] = new List<BuffInstance> { newInstance };
            OnApplyBuff(newInstance);
            return;
        }

        if (_activeBuffs.TryGetValue(buff.buffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == buff);
            if (sameAsset != null)
            {
                sameAsset.data = buff;
                sameAsset.Refresh();
                sameAsset.source = source;
                return;
            }
        }
        else
        {
            list = new List<BuffInstance>();
            _activeBuffs[buff.buffType] = list;
        }

        BuffInstance stackedInstance = (BuffInstance)buff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyBuff(stackedInstance);
    }
```

Le code ACTUEL de `ApplyBuffWithDuration()` (pour référence) :

```csharp
    public void ApplyBuffWithDuration(BuffData buff, Entity source, float remainingSeconds)
    {
        if (buff == null || _entity.isDead || remainingSeconds <= 0f) return;

        if (_activeBuffs.TryGetValue(buff.buffType, out var list))
        {
            var sameAsset = list.Find(i => i.data == buff);
            if (sameAsset != null)
            {
                sameAsset.remainingTime = remainingSeconds;
                sameAsset.source = source;
                return;
            }
        }
        else
        {
            list = new List<BuffInstance>();
            _activeBuffs[buff.buffType] = list;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
        list.Add(instance);
        OnApplyBuff(instance);
    }
```

Le code ACTUEL de `ExpireDebuffInstance()` (pour référence — début de méthode seulement, le
reste ne change pas) :

```csharp
    private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)
    {
        if (!_activeDebuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeDebuffs.Remove(type);

        var expiringBonusStats = instance.DebuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
```

Le code ACTUEL de `ExpireBuffInstance()` (pour référence — début de méthode seulement) :

```csharp
    private void ExpireBuffInstance(BuffType type, BuffInstance instance)
    {
        if (!_activeBuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeBuffs.Remove(type);

        var expiringBonusStats = instance.BuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
```

Si le contenu réel diverge de ce qui précède (au-delà de détails cosmétiques comme des
commentaires), ARRÊTE-TOI et signale-le (BLOCKED) plutôt que de deviner comment adapter les
étapes suivantes.

- [ ] **Step 2: Ajouter la méthode privée `SpawnStatusVfx()`**

Ajouter cette nouvelle méthode privée n'importe où dans la classe `StatusEffectSystem` (par
exemple juste avant `OnApplyDebuff()` ou juste après `ApplyBuffWithDuration()`) :

```csharp
    /// <summary>Spawn le VFX persistant d'un effet nouvellement créé (jamais sur un simple
    /// Refresh() — voir les 5 call sites dans ApplyBuff/TryApplyDebuff/ApplyBuffWithDuration).
    /// Enfant du transform de l'entité : suit position/rotation/animations automatiquement,
    /// détruit avec le GameObject parent si l'entité est détruite (comportement Unity par
    /// défaut, rien à coder).</summary>
    private void SpawnStatusVfx(StatusEffectInstance instance)
    {
        if (instance.data.statusVfx == null) return;
        instance.spawnedVfx = Instantiate(instance.data.statusVfx, _entity.transform.position,
            Quaternion.identity, _entity.transform);
    }
```

- [ ] **Step 3: Appeler `SpawnStatusVfx()` aux 5 sites de création d'instance**

Dans `TryApplyDebuff()`, remplacer :

```csharp
            DebuffInstance newInstance = (DebuffInstance)debuff.CreateInstance(source);
            _activeDebuffs[debuff.debuffType] = new List<DebuffInstance> { newInstance };
            OnApplyDebuff(newInstance);
            return true;
```

par :

```csharp
            DebuffInstance newInstance = (DebuffInstance)debuff.CreateInstance(source);
            _activeDebuffs[debuff.debuffType] = new List<DebuffInstance> { newInstance };
            OnApplyDebuff(newInstance);
            SpawnStatusVfx(newInstance);
            return true;
```

Et remplacer :

```csharp
        DebuffInstance stackedInstance = (DebuffInstance)debuff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyDebuff(stackedInstance);

        return true;
```

par :

```csharp
        DebuffInstance stackedInstance = (DebuffInstance)debuff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyDebuff(stackedInstance);
        SpawnStatusVfx(stackedInstance);

        return true;
```

Dans `ApplyBuff()`, remplacer :

```csharp
            BuffInstance newInstance = (BuffInstance)buff.CreateInstance(source);
            _activeBuffs[buff.buffType] = new List<BuffInstance> { newInstance };
            OnApplyBuff(newInstance);
            return;
```

par :

```csharp
            BuffInstance newInstance = (BuffInstance)buff.CreateInstance(source);
            _activeBuffs[buff.buffType] = new List<BuffInstance> { newInstance };
            OnApplyBuff(newInstance);
            SpawnStatusVfx(newInstance);
            return;
```

Et remplacer :

```csharp
        BuffInstance stackedInstance = (BuffInstance)buff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyBuff(stackedInstance);
    }
```

par :

```csharp
        BuffInstance stackedInstance = (BuffInstance)buff.CreateInstance(source);
        list.Add(stackedInstance);
        OnApplyBuff(stackedInstance);
        SpawnStatusVfx(stackedInstance);
    }
```

(Cette dernière substitution est à la fin de `ApplyBuff()` — vérifie bien qu'il s'agit de
`ApplyBuff()` et pas d'une autre méthode avant de remplacer, le texte `OnApplyBuff(stackedInstance);`
apparaît une seule fois dans le fichier d'après la lecture de la Task, mais confirme.)

Dans `ApplyBuffWithDuration()`, remplacer :

```csharp
        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
        list.Add(instance);
        OnApplyBuff(instance);
    }
```

par :

```csharp
        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
        list.Add(instance);
        OnApplyBuff(instance);
        SpawnStatusVfx(instance);
    }
```

- [ ] **Step 4: Nettoyage dans `ExpireBuffInstance()`**

Remplacer :

```csharp
    private void ExpireBuffInstance(BuffType type, BuffInstance instance)
    {
        if (!_activeBuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeBuffs.Remove(type);

        var expiringBonusStats = instance.BuffData.bonusStats;
```

par :

```csharp
    private void ExpireBuffInstance(BuffType type, BuffInstance instance)
    {
        if (!_activeBuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeBuffs.Remove(type);

        // Placé AVANT tout branchement type-spécifique ci-dessous — certains (BuffType.Stats)
        // font `return` tôt, un nettoyage placé plus bas dans la méthode serait sauté.
        if (instance.spawnedVfx != null) Destroy(instance.spawnedVfx);

        var expiringBonusStats = instance.BuffData.bonusStats;
```

- [ ] **Step 5: Nettoyage dans `ExpireDebuffInstance()`**

Remplacer :

```csharp
    private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)
    {
        if (!_activeDebuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeDebuffs.Remove(type);

        var expiringBonusStats = instance.DebuffData.bonusStats;
```

par :

```csharp
    private void ExpireDebuffInstance(DebuffType type, DebuffInstance instance)
    {
        if (!_activeDebuffs.TryGetValue(type, out var list) || !list.Remove(instance)) return;
        if (list.Count == 0) _activeDebuffs.Remove(type);

        // Placé AVANT tout branchement type-spécifique ci-dessous — 7 types (Slow, Freeze,
        // Blind, ArmorBreak, Poison, Prey, Stats) font `return` tôt via RecalculateAndReapply(),
        // un nettoyage placé plus bas dans la méthode serait sauté pour chacun d'eux.
        if (instance.spawnedVfx != null) Destroy(instance.spawnedVfx);

        var expiringBonusStats = instance.DebuffData.bonusStats;
```

- [ ] **Step 6: Nettoyage dans `TryConsumeRevive()` — 6ᵉ site de retrait d'instance, hors du
      chemin normal d'expiration**

Trouvé lors de la vérification indépendante de ce plan (avant dispatch) : `TryConsumeRevive()`
retire l'instance `Revive` directement du dictionnaire, SANS passer par `ExpireBuffInstance()` —
donc sans jamais nettoyer `spawnedVfx`. Un `BuffData` de type `Revive` avec un `statusVfx`
configuré (ex: une aura visuelle tant que la résurrection est armée) laisserait ce VFX attaché
au joueur INDÉFINIMENT après consommation (le joueur ressuscite, son GameObject n'est jamais
détruit, rien ne détruit jamais ce VFX enfant).

Le code ACTUEL de `TryConsumeRevive()` :

```csharp
    public bool TryConsumeRevive(out float delay, out float hpPercent, out float manaPercent)
    {
        if (_activeBuffs.TryGetValue(BuffType.Revive, out var list) && list.Count > 0)
        {
            var d = list[0].BuffData;
            delay = d.reviveDelay;
            hpPercent = d.reviveHPPercent;
            manaPercent = d.reviveManaPercent;
            _activeBuffs.Remove(BuffType.Revive);
            return true;
        }
        delay = hpPercent = manaPercent = 0f;
        return false;
    }
```

Remplacer par :

```csharp
    public bool TryConsumeRevive(out float delay, out float hpPercent, out float manaPercent)
    {
        if (_activeBuffs.TryGetValue(BuffType.Revive, out var list) && list.Count > 0)
        {
            var d = list[0].BuffData;
            delay = d.reviveDelay;
            hpPercent = d.reviveHPPercent;
            manaPercent = d.reviveManaPercent;
            // Retrait direct du dictionnaire (pas ExpireBuffInstance) — comportement pré-existant
            // inchangé. Le nettoyage VFX doit quand même se faire ici, sinon un statusVfx sur ce
            // Revive resterait attaché au joueur indéfiniment (il ressuscite, son GameObject
            // n'est jamais détruit, rien d'autre ne nettoierait jamais ce VFX enfant).
            if (list[0].spawnedVfx != null) Destroy(list[0].spawnedVfx);
            _activeBuffs.Remove(BuffType.Revive);
            return true;
        }
        delay = hpPercent = manaPercent = 0f;
        return false;
    }
```

- [ ] **Step 7: Vérifier la compilation**

Ouvrir Unity Editor, vérifier 0 erreur dans la Console.

- [ ] **Step 8: Commit**

```bash
git add Entities/StatusEffectSystem.cs
git commit -m "feat: spawn/despawn statusVfx on buff/debuff instance lifecycle"
```

---

### Task 6: Vérification manuelle Play Mode (geste Florian)

**Files:** aucun fichier de code — configuration de prefabs VFX de test sur des assets
`SkillData`/`BuffData`/`DebuffData` de test, pas automatisable par un agent.

**Interfaces:**
- Consumes: `SkillData.vfxTrajectory` (Task 1), comportement `TrajectoryRoutine` (Task 2),
  `StatusEffectData.statusVfx` (Task 3), comportement `StatusEffectSystem` (Task 5)
- Produces: rien — tâche de validation finale

- [ ] **Step 1: Configurer les prefabs de test**

Assigner un prefab VFX (placeholder si besoin, particle system simple) sur `vfxTrajectory` du
skill de test existant `skl_test_trajectory_storm` (chantier D). Assigner un prefab VFX sur
`statusVfx` d'un `BuffData` de test (ex: Regeneration) et d'un `DebuffData` de test (ex: un type
Dot/Burn existant).

- [ ] **Step 2: Tester le VFX de trajet — GroundTarget**

Lancer `skl_test_trajectory_storm`. Le VFX doit voyager du joueur vers le point cliqué, orienté
dans le sens du déplacement (pas statique), disparaître exactement à l'arrivée/résolution. Note
(trouvée en review finale) : pour une trajectoire en ligne droite, l'orientation reste CONSTANTE
pendant tout le trajet (elle ne "tourne" jamais en vol, c'est mathématiquement normal — la
direction ne change pas sur une ligne droite) — ne pas confondre ça avec un bug ; le calcul par
frame est une garantie pour de futures trajectoires courbes, pas un no-op à corriger.

- [ ] **Step 3: Tester la mort du caster en cours de route**

Déclencher la mort du caster (dégâts) pendant que la trajectoire voyage — le VFX doit disparaître
immédiatement, pas de VFX fantôme abandonné sur le terrain.

- [ ] **Step 4: Tester le cas origine == destination**

Si possible à déclencher (GroundTarget avec raycast manqué, ex: viser hors du terrain) — le VFX
ne doit pas rester affiché, doit être détruit immédiatement même sur ce chemin de sortie précoce.

- [ ] **Step 5: Tester le VFX de statut — buff**

Appliquer le buff de test configuré à l'étape 1 sur le joueur. Le VFX doit apparaître attaché au
joueur, suivre ses déplacements/animations, disparaître exactement à l'expiration de la durée du
buff.

- [ ] **Step 6: Tester le VFX de statut — debuff sur un Mob**

Appliquer le debuff de test configuré à l'étape 1 sur un Mob (pas le joueur) — confirme que ça
marche sur une Entity non-Player.

- [ ] **Step 7: Tester le refresh sans duplication**

Relancer le même buff/debuff (même asset) pendant qu'il est déjà actif sur la cible — un seul
VFX doit rester visible, pas un second qui se superpose.

- [ ] **Step 7bis: Tester le recast d'un asset DIFFÉRENT du même type non-stackable (trouvé en
      review finale)**

Configurer 2 `DebuffData` différents partageant le même `DebuffType` non-stackable (ex: deux
variantes de Stun), chacun avec son propre `statusVfx`. Appliquer le premier, puis pendant qu'il
est actif, appliquer le second (asset différent, même type) — le VFX du PREMIER doit disparaître
et celui du SECOND doit apparaître (pas les deux en même temps, pas l'ancien qui reste). Corrigé
lors de la review finale — vérifier que le fix tient en jeu.

- [ ] **Step 8: Tester le stacking**

Appliquer 2 buffs `Stats` différents partageant le même `BuffType`, chacun avec un `statusVfx`
configuré — les deux VFX doivent coexister, chacun suivant l'entité indépendamment.

- [ ] **Step 9: Tester la mort de l'entité avec VFX de statut actif**

Tuer une entité pendant qu'un `statusVfx` est affiché dessus — le VFX doit disparaître avec le
corps, pas de VFX orphelin flottant après la destruction.

- [ ] **Step 10: Rapporter les résultats**

Florian confirme si tous les points ci-dessus passent, ou signale les écarts observés pour
investigation.
