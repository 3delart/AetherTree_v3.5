using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// SKILLSYSTEM.CS — Exécution des skills pour toutes les entités
// Path : Assets/Scripts/Systems/SkillSystem.cs
// AetherTree GDD v3.5 — §3.1 (Entity), §7.1 (Pipeline)
//
// Remplace SkillSystem.cs (v3.0) + MobSkillSystem.cs (v3.0).
//
// Principe :
//   Un seul système gère le lancer de skill quelle que soit l'entité.
//   Le type du caster est identifié via entity.entityType (GDD §3.1).
//
// Target nullable par design :
//   Self / AoE_Self / GroundTarget / Direction / Skillshot / Cone → target null autorisée
//   Target / AoE_Target / Dash_Target / LineTarget                 → target requise
//
// Points d'entrée :
//   Execute(skill, caster, target)
//     → Enregistrements Player (UseSkill, SkillUsedEvent)
//     → RegisterLastSkill sur Mob cible
//     → Dispatch selon executionType (MultiHit / Standard)
//     → Dispatch selon skill.targetType
//     → ApplyEffectType → CombatSystem (calcul pur)
//     → GameEventBus.Publish(DamageDealtEvent) [Player uniquement]
//
// TargetType gérés :
//   Target        → monocible sur Entity requise
//   AoE_Target    → zone autour de la cible
//   Dash_Target   → dash vers la cible puis dégâts
//   LineTarget    → ligne entre caster et cible, hits tout sur le trajet
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint)
//   Direction     → SphereCast dans une direction (SetSkillDirection)
//   Skillshot     → projectile en ligne droite (SphereCast, no target required)
//   Cone          → cône en éventail devant le caster (SetSkillDirection ou forward)
//   Dash_Direction → dash vers un point directionnel (SetSkillDirection ou forward)
//
// MultiHit (GDD §7.1) :
//   Hit initial = skill parent, puis chaque HitStep en coroutine.
//   Proxy SkillData temporaire — détruit après usage.
//
// GroundTarget :
//   SetGroundTargetPoint(point) appelé par TargetingSystem avant Execute().
//   Remis à null après utilisation.
//
// Direction / Skillshot / Cone / Dash_Direction :
//   SetSkillDirection(dir) appelé par TargetingSystem avant Execute()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================

[RequireComponent(typeof(MonoBehaviour))]
public class SkillSystem : MonoBehaviour
{
    public static SkillSystem Instance { get; private set; }

    private Player  _player;

    // Point au sol pour GroundTarget — alimenté par TargetingSystem
    private Vector3? _groundTargetPoint = null;

    // Direction pour skills directionnels — alimenté par TargetingSystem
    private Vector3? _skillDirection = null;

    // =========================================================
    // INITIALISATION
    // =========================================================

    private void Awake()
    {
        // Singleton souple — le Player porte l'instance principale.
        // Les Mob/PNJ portent leur propre composant sans écraser l'instance.
        if (Instance == null)
            Instance = this;
    }

    private void Start()
    {
        _player = GetComponent<Player>();
    }

    // =========================================================
    // POINT D'ENTRÉE UNIFIÉ
    // =========================================================

    /// <summary>
    /// Lance un skill depuis n'importe quelle entité (Player, Mob, PNJ, Pet).
    /// target peut être null pour Self / AoE_Self / GroundTarget / Direction /
    /// Skillshot / Cone / Dash_Direction.
    /// </summary>
    public void Execute(SkillData skill, Entity caster, Entity target)
    {
        if (skill == null || caster == null || caster.isDead) return;

        // ── Enregistrements spécifiques au Player ────────────
        if (caster.entityType == EntityType.Player && caster is Player player)
        {
            player.UseSkill(skill, target);

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

        // ── Enregistrement du killerSkill sur la cible Mob ───
        // Seulement si target est un Mob et caster un Player
        if (target is Mob mobTarget && caster is Player attackerPlayer)
            mobTarget.RegisterLastSkill(attackerPlayer, skill);

        // ── Dispatch selon executionType ─────────────────────
        if (skill.executionType == SkillExecutionType.MultiHit
            && skill.hitSteps != null && skill.hitSteps.Count > 0)
        {
            DispatchByTargetType(skill, caster, target);
            if (caster.entityType == EntityType.Player)
                SkillBar.Instance?.LockForMultiHit(skill);
            StartCoroutine(ExecuteMultiHit(skill, caster, target));
        }
        else
        {
            DispatchByTargetType(skill, caster, target);
        }

        // ── VFX & Son ────────────────────────────────────────
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

    // =========================================================
    // DISPATCH PAR TARGET TYPE
    // =========================================================

    private void DispatchByTargetType(SkillData skill, Entity caster, Entity target)
    {
        switch (skill.targetType)
        {
            // ── Requièrent une target Entity ──────────────────
            case TargetType.Target:
                if (target != null && !target.isDead)
                    ExecuteOnTarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.AoE_Target:
                if (target != null && !target.isDead)
                    ExecuteAoETarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.Dash_Target:
                if (target != null && !target.isDead)
                    StartCoroutine(DashToTarget(skill, caster, target));
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.LineTarget:
                if (target != null && !target.isDead)
                    ExecuteLineTarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            // ── Pas de target Entity ──────────────────────────
            case TargetType.Self:
                ExecuteOnSelf(skill, caster);
                break;

            case TargetType.AoE_Self:
                ExecuteAoESelf(skill, caster);
                break;

            case TargetType.GroundTarget:
                ExecuteGroundTarget(skill, caster);
                break;

            case TargetType.Direction:
                ExecuteDirection(skill, caster);
                break;

            case TargetType.Skillshot:
                ExecuteSkillshot(skill, caster);
                break;

            case TargetType.Cone:
                ExecuteCone(skill, caster);
                break;

            case TargetType.Dash_Direction:
                StartCoroutine(DashInDirection(skill, caster));
                break;

            default:
                Debug.LogWarning($"[SKILL] TargetType '{skill.targetType}' non géré — {caster.entityName} / {skill.name}.");
                break;
        }
    }

    private void LogMissingTarget(SkillData skill, Entity caster)
    {
        Debug.LogWarning($"[SKILL] {skill.name} ({skill.targetType}) depuis {caster.entityName} : cible null ou morte.");
    }

    // =========================================================
    // MULTIHIT (GDD §7.1)
    // =========================================================

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

    private float CalculateDamageForStep(HitStep step, SkillData parentSkill, Entity caster, Entity target, out bool isCrit)
    {
        var proxy = ScriptableObject.CreateInstance<SkillData>();
        proxy.damageMultiplier  = step.damageMultiplier;
        proxy.damageMeleeRatio  = step.damageMeleeRatio;
        proxy.damageRangedRatio = step.damageRangedRatio;
        proxy.damageMagicRatio  = step.damageMagicRatio;
        proxy.elementalMultiplier = step.elementalMultiplier;
        proxy.elements          = new List<ElementType> { step.element };

        float dmg = CalculateDamage(proxy, caster, target, out isCrit);
        Destroy(proxy);
        return dmg;
    }

    // =========================================================
    // TARGET — skill monocible
    // =========================================================

    private void ExecuteOnTarget(SkillData skill, Entity caster, Entity target)
    {
        if (target == null || target.isDead) return;
        ApplyEffectType(skill, caster, target);
        ApplyStatusEffects(skill, caster, target);
        if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
            ApplyWeaponStatusEffects(caster as Player, target);
        CheckKill(target);
    }

    // =========================================================
    // SELF — buff/heal sur le caster
    // =========================================================

    private void ExecuteOnSelf(SkillData skill, Entity caster)
    {
        ApplyEffectType(skill, caster, caster);
        ApplyStatusEffects(skill, caster, caster);
    }

    // =========================================================
    // AoE SELF — zone autour du caster
    // Filtre allié/ennemi via SkillAoeFaction — voir IsAlly/PassesAoeFilter.
    // =========================================================

    private void ExecuteAoESelf(SkillData skill, Entity caster)
    {
        Collider[] hits = Physics.OverlapSphere(caster.transform.position, skill.aoeRadius);

        int count = 0;
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
            count++;
        }
        Debug.Log($"[SKILL] AoE Self ({caster.entityName}) — {count} cibles touchées.");
    }

    // =========================================================
    // AoE TARGET — zone autour de la cible
    // =========================================================

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
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
        }
    }

    // =========================================================
    // GROUND TARGET — zone centrée sur un point au sol
    // =========================================================

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
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
        }
    }

    // =========================================================
    // DIRECTION — skill en ligne / projectile directionnel large
    // SphereCast dans la direction fournie par TargetingSystem.
    // Fallback : regard du caster.
    // =========================================================

    private void ExecuteDirection(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float range  = skill.range    > 0f ? skill.range    : 10f;
        float radius = skill.aoeRadius > 0f ? skill.aoeRadius : 0.5f;

        RaycastHit[] hits = Physics.SphereCastAll(
            caster.transform.position,
            radius,
            dir,
            range);

        foreach (RaycastHit h in hits)
        {
            Entity entity = h.collider.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
        }
    }

    // =========================================================
    // SKILLSHOT — projectile en ligne droite, sans target requise
    // Similaire à Direction mais rayon plus étroit (projectile précis).
    // Utilise _skillDirection ou le regard du caster.
    // =========================================================

    private void ExecuteSkillshot(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float range  = skill.range    > 0f ? skill.range    : 15f;
        float radius = skill.aoeRadius > 0f ? skill.aoeRadius : 0.25f; // projectile étroit

        RaycastHit[] hits = Physics.SphereCastAll(
            caster.transform.position,
            radius,
            dir,
            range);

        // Skillshot : hit la première cible valide uniquement
        float minDist  = float.MaxValue;
        Entity closest = null;
        foreach (RaycastHit h in hits)
        {
            Entity entity = h.collider.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;
            if (h.distance < minDist) { minDist = h.distance; closest = entity; }
        }

        if (closest != null)
        {
            ApplyEffectType(skill, caster, closest);
            ApplyStatusEffects(skill, caster, closest);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, closest);
            CheckKill(closest);
        }
    }

    // =========================================================
    // LINE TARGET — ligne entre le caster et la cible
    // Hits toutes les entités sur le trajet, cible incluse.
    // =========================================================

    private void ExecuteLineTarget(SkillData skill, Entity caster, Entity target)
    {
        Vector3 origin    = caster.transform.position;
        Vector3 targetPos = target.transform.position;
        Vector3 dir       = (targetPos - origin).normalized;
        float   distance  = Vector3.Distance(origin, targetPos);
        float   radius    = skill.aoeRadius > 0f ? skill.aoeRadius : 0.4f;

        RaycastHit[] hits = Physics.SphereCastAll(origin, radius, dir, distance);

        foreach (RaycastHit h in hits)
        {
            Entity entity = h.collider.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
        }
    }

    // =========================================================
    // CONE — éventail devant le caster
    // Utilise _skillDirection ou le regard du caster.
    // skill.aoeRadius = portée du cône, skill.range = angle demi-ouverture (degrés).
    // =========================================================

    private void ExecuteCone(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float range    = skill.range    > 0f ? skill.range    : 5f;
        float halfAngle = skill.aoeRadius > 0f ? skill.aoeRadius : 45f; // aoeRadius réutilisé comme angle

        Collider[] cols = Physics.OverlapSphere(caster.transform.position, range);

        int count = 0;
        foreach (Collider col in cols)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

            Vector3 toEntity = (entity.transform.position - caster.transform.position).normalized;
            float   angle    = Vector3.Angle(dir, toEntity);
            if (angle > halfAngle) continue;

            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
            count++;
        }
        Debug.Log($"[SKILL] Cone ({caster.entityName}) — {count} cibles touchées (angle {halfAngle}°, portée {range}m).");
    }

    // =========================================================
    // DASH TARGET — le caster fonce vers la cible
    // =========================================================

    private IEnumerator DashToTarget(SkillData skill, Entity caster, Entity target)
    {
        float dashDuration = caster.entityType == EntityType.Player ? 0.3f : 0.25f;
        float stopOffset   = caster.entityType == EntityType.Player ? 1.5f : 1.2f;

        float   elapsed  = 0f;
        Vector3 startPos = caster.transform.position;
        Vector3 endPos   = target.transform.position
                         - (target.transform.position - startPos).normalized * stopOffset;

        var agent = caster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        if (caster is Mob dashMob) dashMob.IsDashing = true;

        while (elapsed < dashDuration)
        {
            if (caster == null || caster.isDead) yield break;
            caster.transform.position = Vector3.Lerp(startPos, endPos, elapsed / dashDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (caster != null)
        {
            caster.transform.position = endPos;
            if (caster is Mob endMob) endMob.IsDashing = false;
            if (agent != null) agent.enabled = true;

            if (!caster.isDead && target != null && !target.isDead)
            {
                ApplyEffectType(skill, caster, target);
                ApplyStatusEffects(skill, caster, target);
                if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                    ApplyWeaponStatusEffects(caster as Player, target);
                CheckKill(target);
            }
        }
    }

    // =========================================================
    // DASH DIRECTION — dash en ligne droite dans une direction
    // Applique l'effet sur toutes les entités traversées.
    // =========================================================

    private IEnumerator DashInDirection(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float dashDuration = caster.entityType == EntityType.Player ? 0.3f : 0.25f;
        float dashDistance = skill.range > 0f ? skill.range : 6f;

        Vector3 startPos = caster.transform.position;
        Vector3 endPos   = startPos + dir * dashDistance;

        // Vérifie si la destination est sur le NavMesh
        if (UnityEngine.AI.NavMesh.SamplePosition(endPos, out UnityEngine.AI.NavMeshHit navHit, dashDistance, UnityEngine.AI.NavMesh.AllAreas))
            endPos = navHit.position;

        var agent = caster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        if (caster is Mob dashMob) dashMob.IsDashing = true;

        float   elapsed   = 0f;
        float   radius    = skill.aoeRadius > 0f ? skill.aoeRadius : 0.5f;
        var     alreadyHit = new HashSet<Entity>();

        while (elapsed < dashDuration)
        {
            if (caster == null || caster.isDead) yield break;

            Vector3 prev = caster.transform.position;
            caster.transform.position = Vector3.Lerp(startPos, endPos, elapsed / dashDuration);

            // Détecte les entités traversées frame par frame
            Collider[] cols = Physics.OverlapSphere(caster.transform.position, radius);
            foreach (Collider col in cols)
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity == caster || entity.isDead) continue;
                if (alreadyHit.Contains(entity)) continue;

                alreadyHit.Add(entity);
                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                    ApplyWeaponStatusEffects(caster as Player, entity);
                CheckKill(entity);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (caster != null)
        {
            caster.transform.position = endPos;
            if (caster is Mob endMob) endMob.IsDashing = false;
            if (agent != null) agent.enabled = true;
        }
    }

    // =========================================================
    // FILTRE ALLIÉ/ENNEMI — effets de zone (SkillAoeFaction)
    // =========================================================

    /// <summary>Alliés = même "camp" que le caster. Aujourd'hui : Mob ↔ Mob d'un côté,
    /// Player/Pet/PNJ ensemble de l'autre (PvE only — un PNJ hostile s'incarne en MobData, pas
    /// ici). Point d'extension UNIQUE et volontaire pour tout ce qui rendra un jour deux
    /// joueurs hostiles entre eux (Duel, Arène, Faction PvP en zone — aucun des trois construit
    /// à ce jour, et un Duel/Arène peut rendre deux joueurs ennemis même hors zone PvP dédiée) :
    /// quand l'un de ces systèmes existera, seul le corps de CETTE fonction changera — les
    /// appelants et SkillAoeFaction n'ont pas à être retouchés.</summary>
    private static bool IsAlly(Entity caster, Entity other)
        => (caster.entityType == EntityType.Mob) == (other.entityType == EntityType.Mob);

    /// <summary>Le caster passe naturellement ce filtre : IsAlly(caster, caster) vaut toujours
    /// true, donc Enemies l'exclut et Allies/Everyone l'incluent — pas besoin de check séparé
    /// "entity == caster" dans les boucles appelantes.</summary>
    private static bool PassesAoeFilter(SkillAoeFaction filter, Entity caster, Entity other)
    {
        if (filter == SkillAoeFaction.Everyone) return true;
        bool ally = IsAlly(caster, other);
        return filter == SkillAoeFaction.Allies ? ally : !ally;
    }

    // =========================================================
    // EFFET PRINCIPAL (GDD §7.1 Pipeline)
    // =========================================================

    private void ApplyEffectType(SkillData skill, Entity caster, Entity target)
    {
        switch (skill.effectType)
        {
            case SkillEffectType.Damage:
            {
                float dmg = CalculateDamage(skill, caster, target, out bool isCrit);

                if (target is Mob mobDmg && caster is Player pDmg)
                    mobDmg.RegisterLastSkill(pDmg, skill);
                
                // ── Hit / Dodge check ──────────────────────────────
                float effectivePrecision = caster.GetEffectivePrecision();
                if (caster.statusEffects != null && caster.statusEffects.isBlinded)
                    effectivePrecision *= (1f - caster.statusEffects.GetBlindMalus());

                float effectiveDodge = target.GetEffectiveDodge();

                if (CombatSystem.Instance.RollDodge(effectiveDodge, effectivePrecision))
                {
                    FloatingText.Spawn("Miss", target.transform.position, Color.gray, heightOffset: 1.8f);
                    break;
                }

                target.TakeDamage(dmg, skill.PrimaryElement, caster);

                if (caster.entityType == EntityType.Player && caster is Player playerDmg)
                {
                    GameEventBus.Publish(new DamageDealtEvent
                    {
                        amount   = dmg,
                        element  = skill.PrimaryElement,
                        source   = playerDmg,
                        target   = target,
                        isCrit   = isCrit,
                        isOneHit = target.isDead && dmg >= target.MaxHP,
                    });
                }

                Color textColor = caster.entityType == EntityType.Player ? Color.cyan : Color.red;
                FloatingText.Spawn(Mathf.RoundToInt(dmg).ToString(),
                    target.transform.position, textColor, heightOffset: 1.8f);
                break;
            }

            case SkillEffectType.Buff:
            case SkillEffectType.Debuff:
                // Géré via ApplyStatusEffects
                break;

            case SkillEffectType.Other:
                ApplySpecialEffect(skill, caster, target);
                break;
        }
    }

    // =========================================================
    // CALCUL DES DÉGÂTS
    // =========================================================

    private float CalculateDamage(SkillData skill, Entity caster, Entity target, out bool isCrit)
    {
        isCrit = false;

        if (CombatSystem.Instance == null)
        {
            float baseFallback = caster is Mob fallbackMob && fallbackMob.data != null
                ? fallbackMob.data.baseAtkMin
                : 10f;
            return baseFallback * skill.damageMultiplier * Random.Range(0.9f, 1.1f);
        }

        switch (caster.entityType)
        {
            case EntityType.Player:
                Player p = caster as Player;
                if (p == null) return 0f;
                // p.equippedWeaponInstance == null (unarmed) → weapon null passé tel quel,
                // jamais lu dans CalculateDamage (uniquement dans le log désactivé) — le
                // pipeline complet (ATK scalé au niveau, défense, crit, élémentaire) s'applique.
                return CombatSystem.Instance.CalculateDamage(
                    p.equippedWeaponInstance,
                    skill,
                    p.GetElementalSystem(),
                    p,
                    target,
                    out isCrit);

            case EntityType.Mob:
                return CombatSystem.Instance.CalculateMobDamage(skill, caster as Mob, target);

            case EntityType.PNJ:
                // Les PNJ combattants utilisent le même pipeline que les Mobs.
                // Entity.AttackDamageMin est alimenté par PNJData.attackDamage dans PNJ.Awake().
                return CombatSystem.Instance.CalculateMobDamage(skill, caster, target);

            default:
                return caster.AttackDamageMin * skill.damageMultiplier;
        }
    }

    // =========================================================
    // EFFETS SECONDAIRES — StatusEffects du SkillData
    // =========================================================

    private void ApplyStatusEffects(SkillData skill, Entity caster, Entity target)
    {
        if (skill.statusEffects == null || skill.statusEffects.Count == 0) return;
        if (target == null || target.isDead) return;

        var statusSystem = target.statusEffects;
        if (statusSystem == null)
        {
            Debug.LogWarning($"[SKILL] {target.entityName} n'a pas de StatusEffectSystem !");
            return;
        }

        foreach (var entry in skill.statusEffects)
        {
            if (entry == null || entry.effect == null) continue;
            if (!entry.Roll()) continue;

            switch (entry.effect)
            {
                case BuffData buff:
                    statusSystem.ApplyBuff(buff, caster);
                    break;
                case DebuffData debuff:
                    statusSystem.TryApplyDebuff(debuff, caster);
                    break;
            }
        }
    }

    private void ApplyStatusEffectEntry(StatusEffectEntry entry, Entity caster, Entity target)
    {
        if (entry == null || entry.effect == null) return;
        if (target == null || target.isDead) return;
        if (!entry.Roll()) return;

        var statusSystem = target.statusEffects;
        if (statusSystem == null) return;

        switch (entry.effect)
        {
            case BuffData buff:   statusSystem.ApplyBuff(buff, caster);           break;
            case DebuffData deb:  statusSystem.TryApplyDebuff(deb, caster);       break;
        }
    }

    // =========================================================
    // EFFETS SECONDAIRES — StatusEffects de l'équipement du Player
    // =========================================================

    private void ApplyWeaponStatusEffects(Player player, Entity target)
    {
        if (player == null || target == null || target.isDead) return;

        var statusSystem = target.statusEffects;
        if (statusSystem == null) return;

        var allEffects = new List<(List<StatusEffectEntry> effects, string source)>();

        if (player.equippedWeaponInstance?.data  != null) allEffects.Add((player.equippedWeaponInstance.StatusEffects, player.equippedWeaponInstance.WeaponName));
        if (player.equippedArmorInstance?.data   != null) allEffects.Add((player.equippedArmorInstance.StatusEffects,  player.equippedArmorInstance.ArmorName));
        if (player.equippedHelmetInstance?.data  != null) allEffects.Add((player.equippedHelmetInstance.StatusEffects, player.equippedHelmetInstance.HelmetName));
        if (player.equippedGlovesInstance?.data  != null) allEffects.Add((player.equippedGlovesInstance.StatusEffects, player.equippedGlovesInstance.GlovesName));
        if (player.equippedBootsInstance?.data   != null) allEffects.Add((player.equippedBootsInstance.StatusEffects,  player.equippedBootsInstance.BootsName));
        if (player.equippedJewelryInstances != null)
            foreach (var jewelry in player.equippedJewelryInstances)
                if (jewelry != null) allEffects.Add((jewelry.StatusEffects, jewelry.JewelryName));

        foreach (var (effects, sourceName) in allEffects)
        {
            if (effects == null) continue;
            foreach (var entry in effects)
            {
                if (entry == null || entry.effect == null || !entry.Roll()) continue;
                switch (entry.effect)
                {
                    case BuffData buff:
                        statusSystem.ApplyBuff(buff, player);
                        break;
                    case DebuffData debuff:
                        if (statusSystem.TryApplyDebuff(debuff, player))
                            Debug.Log($"[EQUIP] {sourceName} → {debuff.effectName.Get(LocalizationManager.CurrentLanguage)} sur {target.entityName}.");
                        break;
                }
            }
        }
    }

    // =========================================================
    // EFFETS SPECIAUX (SkillEffectType.Other)
    // =========================================================

    private void ApplySpecialEffect(SkillData skill, Entity caster, Entity target)
    {
        if (skill.specialEffect == SkillSpecialEffect.None)
        {
            Debug.LogWarning($"[SKILL] {skill.name} ({caster.entityName}) : effectType=Other mais specialEffect=None.");
            return;
        }

        Vector3 casterPos = caster.transform.position;

        switch (skill.specialEffect)
        {
            case SkillSpecialEffect.Pull:
                if (target == null) return;
                DisplacementUtils.WarpEntity(target, casterPos, skill.pullPushForce, towards: true);
                FloatingText.Spawn("PULL", target.transform.position, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.Push:
                if (target == null) return;
                DisplacementUtils.WarpEntity(target, casterPos, skill.pullPushForce, towards: false);
                FloatingText.Spawn("PUSH", target.transform.position, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.SwapPosition:
                if (target == null) return;
                Vector3 origCaster = casterPos;
                Vector3 origTarget = target.transform.position;
                DisplacementUtils.WarpToNavMesh(caster, origTarget);
                DisplacementUtils.WarpToNavMesh(target, origCaster);
                FloatingText.Spawn("SWAP", origTarget, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.PullAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: true, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"PULL ×{count}", center, Color.yellow, 1.8f);
                break;
            }

            case SkillSpecialEffect.PushAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: false, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"PUSH ×{count}", center, Color.yellow, 1.8f);
                break;
            }

            case SkillSpecialEffect.GatherAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.GatherAoE(
                    center, skill.aoeRadius, caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"GATHER ×{count}", center, Color.magenta, 1.8f);
                break;
            }

            case SkillSpecialEffect.Vortex:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: true, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"VORTEX ×{count}", center, Color.cyan, 1.8f);
                break;
            }

            case SkillSpecialEffect.TeleportSelf:
            {
                Vector3 dest = _groundTargetPoint ?? (target != null
                    ? target.transform.position
                    : casterPos);
                _groundTargetPoint = null;
                DisplacementUtils.WarpToNavMesh(caster, dest);
                FloatingText.Spawn("TELEPORT", dest, Color.cyan, 1.8f);
                break;
            }

            case SkillSpecialEffect.TeleportTarget:
                if (target == null) return;
                DisplacementUtils.WarpToNavMesh(target, casterPos);
                FloatingText.Spawn("TELEPORT", target.transform.position, Color.cyan, 1.8f);
                break;

            case SkillSpecialEffect.DrainHP:
            {
                if (target == null || target.isDead) return;
                float dmg = CalculateDamage(skill, caster, target, out bool isCrit);

                if (target is Mob mobDrn && caster is Player pDrn)
                    mobDrn.RegisterLastSkill(pDrn, skill);

                target.TakeDamage(dmg, skill.PrimaryElement, caster);
                float healed = dmg * skill.drainHealRatio;
                caster.Heal(healed);

                if (caster.entityType == EntityType.Player && caster is Player playerDrn)
                {
                    GameEventBus.Publish(new DamageDealtEvent
                    {
                        amount   = dmg,
                        element  = skill.PrimaryElement,
                        source   = playerDrn,
                        target   = target,
                        isCrit   = isCrit,
                        isOneHit = target.isDead && dmg >= target.MaxHP,
                    });
                }

                FloatingText.Spawn($"-{Mathf.RoundToInt(dmg)}",    target.transform.position, Color.red,   1.8f);
                FloatingText.Spawn($"+{Mathf.RoundToInt(healed)}", casterPos,                 Color.green, 1.8f);
                CheckKill(target);
                break;
            }

            case SkillSpecialEffect.DrainMana:
            {
                if (target == null || target.isDead) return;
                float stolen = target.CurrentMana * skill.drainHealRatio;
                target.SpendMana(stolen);
                caster.RecoverMana(stolen);
                FloatingText.Spawn($"MANA -{Mathf.RoundToInt(stolen)}", target.transform.position, Color.blue, 1.8f);
                break;
            }

            case SkillSpecialEffect.Summon:
                Debug.Log($"[SKILL] Summon ({caster.entityName}) — TODO (summonMobData={skill.summonMobData?.mobName ?? "null"}).");
                break;

            case SkillSpecialEffect.Interrupt:
                Debug.Log($"[SKILL] Interrupt ({caster.entityName}) — TODO.");
                break;
        }

        _groundTargetPoint = null;
        _skillDirection    = null;
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    private Vector3 ResolveAoECenter(SkillData skill, Entity caster)
    {
        if (skill.targetType == TargetType.GroundTarget && _groundTargetPoint.HasValue)
            return _groundTargetPoint.Value;
        return caster.transform.position;
    }

    /// <summary>
    /// Désélectionne la cible morte côté TargetingSystem.
    /// La mort (Die, XP, Loot) est gérée par Mob.Die() — pas besoin de la déclencher ici.
    /// </summary>
    private void CheckKill(Entity target)
    {
        if (target == null || !target.isDead) return;
        TargetingSystem.Instance?.ClearForDeath(target);
    }

    // =========================================================
    // API PUBLIQUE — appelée par TargetingSystem avant Execute()
    // =========================================================

    /// <summary>Fournit le point au sol pour le prochain GroundTarget.</summary>
    public void SetGroundTargetPoint(Vector3 point)
    {
        _groundTargetPoint = point;
    }

    /// <summary>
    /// Fournit la direction pour le prochain skill directionnel.
    /// Utilisé par : Direction, Skillshot, Cone, Dash_Direction.
    /// </summary>
    public void SetSkillDirection(Vector3 direction)
    {
        _skillDirection = direction.normalized;
    }
}
