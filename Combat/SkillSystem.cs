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
//   Self / AoE_Self / GroundTarget / Cone → target null autorisée
//   Target / AoE_Target / Dash_Target      → target requise
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
//   Target        → monocible sur Entity requise. + isTrajectory : perce jusqu'à la cible
//                   (stopAtFirstHit pour s'arrêter au 1er hit).
//   AoE_Target    → zone autour de la cible
//   Dash_Target   → dash vers la cible puis dégâts
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint), visé souris.
//                   + isTrajectory : voyage vers ce point (stopAtFirstHit possible).
//   Cone          → cône en éventail devant le caster, visé souris (SetSkillDirection via
//                   TargetingSystem.ResolveDirection())
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
// Cone / Dash_Direction :
//   SetSkillDirection(dir) appelé par SkillBar avant Execute()/StartTrajectory()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================

public class SkillSystem : MonoBehaviour
{
    public static SkillSystem Instance { get; private set; }

    private Player  _player;

    // Point au sol pour GroundTarget — alimenté par TargetingSystem
    private Vector3? _groundTargetPoint = null;

    // Direction pour skills directionnels — alimenté par TargetingSystem
    private Vector3? _skillDirection = null;

    // Demi-hauteur FIXE d'une hitbox de trajectoire Box (TrajectoryShape.Box) — pas de champ
    // Inspector dédié, aucune variation de hauteur nécessaire au combat (entités ~au même niveau
    // au sol) ; juste assez généreuse pour ne jamais rater une cible à cause de la hauteur.
    private const float TrajectoryBoxHalfHeight = 1.5f;

    // =========================================================
    // INITIALISATION
    // =========================================================

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

        // Position résolue AVANT le dispatch — ExecuteGroundTarget() consomme et annule
        // _groundTargetPoint, donc la lire APRÈS le dispatch retombe toujours sur la position
        // du caster pour un GroundTarget (bug trouvé lors de l'audit VFX du 2026-09-12,
        // présent depuis l'introduction de GroundTarget, pas lié au chantier VFX lui-même).
        Vector3 vfxPos = target != null
            ? target.transform.position
            : _groundTargetPoint ?? caster.transform.position;

        // Dash_Target/Dash_Direction résolvent leurs dégâts en coroutine, ~0.25-0.3s APRÈS ce
        // point (le temps du dash) — DashToTarget()/DashInDirection() jouent maintenant
        // vfxImpact/soundEffect elles-mêmes, au moment RÉEL du coup, pour ne pas les jouer trop
        // tôt ici (trouvé lors de l'audit VFX du 2026-09-12).
        bool isDash = skill.targetType == TargetType.Dash_Target
                   || skill.targetType == TargetType.Dash_Direction;

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
        if (!isDash)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, vfxPos, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
        }
    }

    /// <summary>Résout un skill DÉJÀ lancé par SkillBar (mana/anim/BeginSkillUse déjà faits au
    /// lancement) — dispatch des dégâts/effets, event, VFX/Son, bookkeeping élémentaire.
    /// JAMAIS d'appel à player.UseSkill()/BeginSkillUse()/PlayAttack ici, seulement
    /// player.ResolveSkillUse() — sinon l'anim redémarrerait par-dessus elle-même juste après
    /// son propre impact. DEUXIÈME écart volontaire avec Execute() : la branche MultiHit
    /// d'Execute() (dispatch + StartCoroutine(ExecuteMultiHit)) est ENTIÈREMENT absente ici —
    /// ResolveExecute() ne gère jamais un skill MultiHit, ce cas passe par
    /// ResolveMultiHitStep() ci-dessous à la place, appelée directement par SkillBar (joueur)
    /// ou Mob.cs/PNJ.cs (chantier fondations Animator Mob/PNJ) au bon index. Utilisée pour
    /// Normal/Combo-step côté joueur (chantier B) ET pour Normal côté Mob/PNJ (leur pending-hit
    /// interne, même principe). Execute() (inchangée, ci-dessus, MultiHit inclus) reste
    /// utilisée par : les skills de passif (jamais via SkillBar/pending-hit), la Canalisation
    /// joueur (SkillBar.ResolveChannel()), et le détour volontaire MultiHit-sans-attackAnimation
    /// de Mob.cs/PNJ.cs (StartPendingHit() — préserve HitStep.delay, voir ces fichiers).</summary>
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

        // Position résolue AVANT le dispatch — même raison que Execute() ci-dessus.
        Vector3 vfxPos = target != null
            ? target.transform.position
            : _groundTargetPoint ?? caster.transform.position;

        // Dash_Target/Dash_Direction jouent leur propre vfxImpact/soundEffect en coroutine —
        // même raison que Execute() ci-dessus.
        bool isDash = skill.targetType == TargetType.Dash_Target
                   || skill.targetType == TargetType.Dash_Direction;

        DispatchByTargetType(skill, caster, target);

        if (!isDash)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, vfxPos, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
        }
    }

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

        // Entité à suivre si skill.zoneFollowsAnchor est coché (§5 de la spec) — le CASTER pour
        // Self/AoE_Self (le champ n'est même pas visible dans l'Inspector pour les autres
        // targetType, voir SkillData.zoneFollowsAnchor), la CIBLE pour Target/AoE_Target, jamais
        // pour GroundTarget (pas d'entité à suivre, un point au sol reste toujours figé).
        Entity anchorEntity = null;
        if (skill.zoneFollowsAnchor)
        {
            if (skill.targetType == TargetType.Self || skill.targetType == TargetType.AoE_Self)
                anchorEntity = caster;
            else if (skill.targetType == TargetType.Target || skill.targetType == TargetType.AoE_Target)
                anchorEntity = target;
        }

        GameObject marker = skill.vfxZoneMarker != null
            ? Instantiate(skill.vfxZoneMarker, position, Quaternion.identity)
            : null;

        // Si la zone doit suivre une entité, parente le marker à son transform — Unity gère le
        // suivi (position ET rotation) automatiquement, cohérent pour un VFX de tourbillon qui
        // doit tourner avec le joueur. Sinon (défaut), le marker reste en world space, fixe,
        // comportement inchangé.
        if (marker != null && anchorEntity != null)
            marker.transform.SetParent(anchorEntity.transform);

        // Mob/PNJ : Destroy(gameObject, délai de corpse) sur le caster tuerait cette coroutine
        // avant qu'elle atteigne son propre nettoyage si impactDelay+zoneDuration dépasse ce
        // délai — délègue à un host temporaire qui survit indépendamment du caster, détruit
        // lui-même en fin de routine. Le chemin Player (jamais détruit) reste inchangé, tourne
        // sur `this` comme aujourd'hui.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"DelayedZoneHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.DelayedZoneRoutine(skill, caster, position, anchorEntity, marker, destroySelfOnFinish));
    }

    /// <summary>Tick(s) de détonation d'une zone plantée par PlantDelayedZone(). Un seul tick si
    /// `zoneDuration == 0` (impact différé simple), sinon retick toutes les
    /// `zoneTickInterval` secondes pendant `zoneDuration` (zone persistante type lave). Chaque
    /// tick réutilise EXACTEMENT la logique de ExecuteGroundTarget()/ExecuteAoETarget() — même
    /// OverlapSphere + PassesAoeFilter + ApplyEffectType/ApplyStatusEffects/CheckKill.</summary>
    private IEnumerator DelayedZoneRoutine(SkillData skill, Entity caster, Vector3 position, Entity anchorEntity, GameObject marker, bool destroySelfOnFinish)
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

            // zoneFollowsAnchor : recalcule la position sur l'entité suivie SI elle est encore
            // vivante — sinon retombe sur `position` (figée), évite un NullReferenceException/
            // téléportation en (0,0,0) si l'entité suivie meurt entre deux ticks. `position`
            // elle-même n'est JAMAIS réassignée (reste le point d'origine pour ce fallback).
            Vector3 tickPosition = (anchorEntity != null && !anchorEntity.isDead)
                ? anchorEntity.transform.position
                : position;

            Collider[] hits = Physics.OverlapSphere(tickPosition, skill.aoeRadius);
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
                Instantiate(skill.vfxImpact, tickPosition, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, tickPosition);

            if (remaining <= 0f) break;

            yield return new WaitForSeconds(tickInterval);
            remaining -= tickInterval;
        }

        if (marker != null) Destroy(marker);
        if (destroySelfOnFinish) Destroy(gameObject);
    }

    /// <summary>Résout un skill `isTrajectory` DÉJÀ lancé par SkillBar (mana/anim/BeginSkillUse
    /// déjà faits au lancement) — fait le bookkeeping de résolution immédiatement (comme
    /// ResolveExecute/PlantDelayedZone), mais lance une coroutine qui déplace une hitbox du
    /// caster vers une destination, infligeant des dégâts à tout ce qu'elle traverse. Distinct de
    /// PlantDelayedZone (zone FIXE une fois plantée) — mutuellement exclusif, voir
    /// SkillData.OnValidate(). GroundTarget/Direction/Skillshot ne prennent jamais de cible Entity
    /// (voir commentaire en tête de fichier) ; `target` n'est lu que pour Target/AoE_Target/
    /// LineTarget — position figée AU LANCEMENT, pas de homing (voir tooltip
    /// SkillData.isTrajectory). Cone est un chemin de résolution entièrement séparé
    /// (TrajectoryConeRoutine, pas de `destination` unique) — voir plus bas.</summary>
    public void StartTrajectory(SkillData skill, Entity caster, Entity target = null)
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

        // Cône : chemin séparé — pas un point qui voyage vers UNE destination, un ÉVENTAIL qui
        // s'élargit depuis origin (portée 0 → range). Résolu par TrajectoryConeRoutine, jamais le
        // sweep point-à-point de TrajectoryRoutine (géométrie incompatible) — voir tooltip
        // SkillData.isTrajectory. Sort tôt, saute tout le calcul de `destination` ci-dessous.
        if (skill.targetType == TargetType.Cone)
        {
            Vector3 coneDir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;

            GameObject coneVfx = skill.vfxTrajectory != null
                ? Instantiate(skill.vfxTrajectory, origin, Quaternion.LookRotation(coneDir))
                : null;

            SkillSystem coneHost = caster.entityType == EntityType.Player
                ? this
                : new GameObject($"TrajectoryHost_{skill.name}").AddComponent<SkillSystem>();
            bool coneDestroySelfOnFinish = coneHost != this;

            coneHost.StartCoroutine(coneHost.TrajectoryConeRoutine(skill, caster, origin, coneDir, coneVfx, coneDestroySelfOnFinish));
            return;
        }

        Vector3 destination;

        if (skill.targetType == TargetType.GroundTarget)
        {
            // Même consommation que ExecuteGroundTarget()/PlantDelayedZone() — sans ce reset,
            // un skill sans rapport lancé plus tard hériterait d'une position périmée.
            destination = _groundTargetPoint ?? origin;
            _groundTargetPoint = null;
        }
        else if (skill.targetType == TargetType.Target || skill.targetType == TargetType.AoE_Target)
        {
            // Position figée AU LANCEMENT, pas de homing — si target meurt/sort de portée avant
            // que StartTrajectory() soit appelée (délai d'anim), on vise quand même son dernier
            // point connu plutôt que d'annuler silencieusement le skill (même philosophie que le
            // fallback origin==destination plus bas, qui gère déjà le cas target == null).
            // AoE_Target : TrajectoryRoutine balaie déjà TOUT ce qui est sur le trajet (pas juste
            // la cible la plus proche) — même résultat qu'ExecuteAoETarget côté instantané, juste
            // étalé dans le temps.
            destination = target != null ? target.transform.position : origin;
        }
        else // Fallback générique — targetType théoriquement incompatible avec isTrajectory (voir
             // OnValidate) : GroundTarget/Target/AoE_Target/Cone ont chacun leur propre branche
             // ci-dessus, donc en usage normal AUCUN targetType valide n'atteint cette branche.
             // Gardée comme filet de sécurité (facing du caster + range) pour ne jamais laisser un
             // skill qui a déjà coûté mana/CD se solder par un no-op silencieux — voir spec §6b.
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

            // Demi-épaisseur AVANT (axe du trajet) de la hitbox — forme-dépendante : Sphere =
            // aoeRadius (rayon), Box = moitié de trajectoryDepth (jamais aoeRadius côté Box,
            // qui n'y sert plus à rien — voir TrajectoryRoutine).
            float forwardHalfExtent = skill.trajectoryShape == TrajectoryShape.Box
                ? (skill.trajectoryDepth > 0f ? skill.trajectoryDepth : 1f) / 2f
                : (skill.aoeRadius       > 0f ? skill.aoeRadius       : 0.5f);

            // Le CENTRE de la hitbox voyage jusqu'à `destination` (TrajectoryRoutine le plafonne
            // par Mathf.Min sur la distance totale) — mais la hitbox elle-même a du volume, donc
            // son bord AVANT dépasserait `destination` de `forwardHalfExtent` si on ne compensait
            // pas ici. Sans ça, le vfxTrajectory s'arrête visuellement à `range`, mais les dégâts
            // portent au-delà — décalage trouvé en discussion, corrigé en raccourcissant la
            // distance de voyage pour que le BORD de la hitbox (pas son centre) arrive pile à
            // `range`. Clampé à 0 — une hitbox plus épaisse que `range` (mur très large, portée
            // courte) donne une distance de voyage nulle, pas négative.
            float effectiveRange = Mathf.Max(0f, range - forwardHalfExtent);
            destination = origin + dir * effectiveRange;
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

        // Mob/PNJ : même raison que PlantDelayedZone() ci-dessus — host temporaire qui survit
        // indépendamment du caster, détruit lui-même en fin de routine.
        SkillSystem host = caster.entityType == EntityType.Player
            ? this
            : new GameObject($"TrajectoryHost_{skill.name}").AddComponent<SkillSystem>();
        bool destroySelfOnFinish = host != this;

        host.StartCoroutine(host.TrajectoryRoutine(skill, caster, origin, destination, trajectoryVfx, destroySelfOnFinish));
    }

    /// <summary>Déplace un point virtuel de `origin` à `destination` à la vitesse
    /// `skill.projectileSpeed` (fallback 10), balaie un SphereCastAll (rayon `skill.aoeRadius`,
    /// fallback 0.5) entre la position du tick précédent et la position du tick courant à CHAQUE
    /// FRAME — ne peut jamais sauter une cible même à vitesse élevée. Un Overlap initial à
    /// `origin` précède la boucle (un SphereCast/BoxCast ne détecte pas un chevauchement déjà
    /// présent à son point de départ — sinon une entité collée au caster au lancement ne serait
    /// jamais touchée). Forme de la hitbox = skill.trajectoryShape (Sphere par défaut ou Box,
    /// largeur/profondeur indépendantes via trajectoryWidth/trajectoryDepth, hauteur fixe). Une entité ne peut
    /// être touchée qu'une seule fois par cast (HashSet).
    /// vfxImpact/soundEffect joués par entité touchée (même précédent que ResolveMultiHitStep).
    /// Le VFX de TRAJET (trajectoryVfx) est spawné au lancement, suit position+rotation à chaque
    /// frame pendant tout le déplacement, et est détruit sur chaque chemin de sortie de la
    /// coroutine. Si `skill.stopAtFirstHit`, le sweep s'arrête au premier hit (initial pass OU
    /// boucle par frame) au lieu d'aller jusqu'à `destination`. Si `skill.hasDelayedImpact` ET
    /// `targetType = Target/GroundTarget` (seule combinaison autorisée avec isTrajectory pour ces
    /// deux types, voir SkillData.isTrajectory), plante EN PLUS une zone classique
    /// (DelayedZoneRoutine) au point d'arrêt réel (respecte stopAtFirstHit) une fois le sweep
    /// terminé — double dégât voulu, pas une alternative au sweep.</summary>
    private IEnumerator TrajectoryRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 destination, GameObject trajectoryVfx, bool destroySelfOnFinish)
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
            if (destroySelfOnFinish)
                Destroy(gameObject);
            yield break; // origine == destination, rien à parcourir
        }

        float speed  = skill.projectileSpeed > 0f ? skill.projectileSpeed : 10f;
        float radius = skill.aoeRadius       > 0f ? skill.aoeRadius       : 0.5f;
        Vector3 dir  = (destination - origin) / totalDistance;

        // Box uniquement — largeur (trajectoryWidth) et profondeur (trajectoryDepth) réglables
        // indépendamment ; `radius` (aoeRadius) reste propre à Sphere, pas réutilisé ici. Hauteur
        // FIXE (TrajectoryBoxHalfHeight×2) — pas de champ dédié, aucune variation de hauteur
        // nécessaire au combat (entités ~au même niveau au sol), juste assez généreuse pour ne
        // jamais rater une cible à cause de la hauteur. Vector3 par défaut (0,0,0) ignoré côté
        // Sphere — jamais lu dans ce cas.
        Vector3 halfExtents = skill.trajectoryShape == TrajectoryShape.Box
            ? new Vector3((skill.trajectoryWidth > 0f ? skill.trajectoryWidth : 2f) / 2f,
                          TrajectoryBoxHalfHeight,
                          (skill.trajectoryDepth  > 0f ? skill.trajectoryDepth  : 1f) / 2f)
            : Vector3.zero;

        HashSet<Entity> alreadyHit = new HashSet<Entity>();

        // Pass initiale à l'origine — un SphereCastAll/BoxCastAll ne détecte JAMAIS un collider
        // déjà en chevauchement à son point de départ (limitation connue de la physique Unity,
        // même raison pour laquelle DashInDirection utilise OverlapSphere et non un SphereCast).
        // Sans ce pass, une entité collée au caster au moment du lancement (ex: un ennemi au
        // corps-à-corps quand le joueur lance la trajectoire) pourrait n'être JAMAIS touchée.
        Collider[] initialHits = skill.trajectoryShape == TrajectoryShape.Box
            ? Physics.OverlapBox(origin, halfExtents, Quaternion.LookRotation(dir))
            : Physics.OverlapSphere(origin, radius);
        Vector3 stopPoint = origin; // point d'arrêt réel si stopAtFirstHit coupe court — mis à
                                     // jour à chaque hit, lu après la boucle pour la combo zone.
        bool stoppedEarly = false;
        foreach (Collider col in initialHits)
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

            // stopAtFirstHit : le trajet s'arrête ICI, dès la pass initiale — une entité déjà
            // collée au caster au lancement compte comme le "premier hit".
            if (skill.stopAtFirstHit) { stopPoint = origin; stoppedEarly = true; break; }
        }

        Vector3 previousPos  = origin;
        float   traveled     = 0f;
        bool    casterDied   = false;

        while (traveled < totalDistance && !stoppedEarly)
        {
            // Garde caster mort en cours de trajet — même effet que le `break` de
            // DelayedZoneRoutine (rien à nettoyer après, pas de marker/VFX créé par cette
            // coroutine). DashToTarget/DashInDirection utilisent `yield break` (pas `break`) car
            // ILS ont du nettoyage post-boucle à sauter — pas le cas ici, comparaison à ces
            // deux-là non pertinente.
            if (caster == null || caster.isDead) { casterDied = true; break; }

            traveled += speed * Time.deltaTime;
            Vector3 currentPos = origin + dir * Mathf.Min(traveled, totalDistance);
            float   segment    = Vector3.Distance(previousPos, currentPos);

            if (segment > 0.0001f)
            {
                Vector3 segmentDir = (currentPos - previousPos).normalized;
                RaycastHit[] hits = skill.trajectoryShape == TrajectoryShape.Box
                    ? Physics.BoxCastAll(previousPos, halfExtents, segmentDir, Quaternion.LookRotation(segmentDir), segment)
                    : Physics.SphereCastAll(previousPos, radius, segmentDir, segment);
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

                    // stopAtFirstHit : le trajet s'arrête AU POINT DE CE HIT, pas à `destination`
                    // — une flèche qui perce le premier ennemi ne continue pas au-delà.
                    if (skill.stopAtFirstHit) { stopPoint = h.point; stoppedEarly = true; break; }
                }

                if (trajectoryVfx != null)
                {
                    trajectoryVfx.transform.position = stoppedEarly ? stopPoint : currentPos;
                    trajectoryVfx.transform.rotation = Quaternion.LookRotation(segmentDir);
                }
            }

            previousPos = currentPos;
            if (stoppedEarly) break;
            yield return null;
        }

        // Trajectoire terminée sans toucher personne (et sans que le caster soit mort en cours
        // de route — dans ce cas-là on ne veut PAS de VFX, "le caster est mort avant qu'on puisse
        // savoir" n'est pas un vrai "whiff").

        // hasDelayedImpact + isTrajectory combinés — UNIQUEMENT Target/GroundTarget/Cone (voir
        // OnValidate, exception délibérée à leur exclusion mutuelle habituelle — Cone est géré
        // dans TrajectoryConeRoutine, pas ici). Le sweep a déjà infligé ses dégâts immédiats aux
        // entités traversées ; en plus, une zone classique se plante là où le trajet s'est
        // terminé (aoeRadius côté Sphere — jamais lu par le sweep lui-même, qui a son propre
        // radius local) et détone après impactDelay — double dégât voulu si une cible reste dans
        // le rayon final (demande explicite Florian — ex: mur de feu qui voyage vers le point
        // cliqué (GroundTarget) ET laisse une zone brûlante à l'arrivée ; lance qui transperce
        // jusqu'à la cible (Target) ET explose à l'arrivée).
        bool plantsZoneAtEnd = !casterDied && skill.hasDelayedImpact &&
            (skill.targetType == TargetType.GroundTarget || skill.targetType == TargetType.Target);

        // Point où la zone se plante — le point d'arrêt réel si stopAtFirstHit a coupé court,
        // sinon la destination d'origine (trajet allé jusqu'au bout).
        Vector3 zonePlantPoint = stoppedEarly ? stopPoint : destination;

        // Whiff total (rien touché EN CHEMIN) — pas de feedback immédiat si une zone va de toute
        // façon se planter au bout (son propre marker/détonation suffit, un vfxImpact immédiat en
        // plus serait un doublon confus). Même fallback que le cas origine==destination ci-dessus
        // sinon : le skill a coûté mana/HP/or + cooldown, il doit produire un feedback même sur un
        // whiff total.
        if (!casterDied && alreadyHit.Count == 0 && !plantsZoneAtEnd)
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

        if (plantsZoneAtEnd)
        {
            GameObject marker = skill.vfxZoneMarker != null
                ? Instantiate(skill.vfxZoneMarker, zonePlantPoint, Quaternion.identity)
                : null;
            // Nested sur CE host (pas un nouveau) — destroySelfOnFinish: false ici, le Destroy du
            // host reste géré une seule fois, juste en dessous, une fois cette attente terminée.
            // anchorEntity: null — le point d'arrivée/d'arrêt d'une trajectoire est déjà un point
            // fixe résolu, "suivre" une entité n'a pas de sens ici (voir zoneFollowsAnchor).
            yield return DelayedZoneRoutine(skill, caster, zonePlantPoint, null, marker, destroySelfOnFinish: false);
        }

        if (destroySelfOnFinish)
            Destroy(gameObject);
    }

    /// <summary>Élargit un cône depuis `origin` (portée 0 → skill.range, à la vitesse
    /// skill.projectileSpeed, fallback 10) — PAS un point qui voyage (contrairement à
    /// TrajectoryRoutine), une ONDE qui s'étend : à chaque frame, `OverlapSphere` au rayon
    /// courant depuis `origin` (FIXE, ne bouge jamais — contrairement au sweep point-à-point),
    /// filtré par angle (`skill.coneHalfAngle`, fallback 45°) autour de `dir` — même filtre
    /// exact que ExecuteCone(), juste étalé dans le temps au lieu d'un unique OverlapSphere au
    /// rayon final. Une entité ne peut être touchée qu'une seule fois par cast (HashSet), même
    /// convention vfxImpact/soundEffect par entité touchée que TrajectoryRoutine. `trajectoryVfx`
    /// est spawné une fois à `origin`, orienté vers `dir`, et NE BOUGE JAMAIS (pas de position à
    /// suivre pour un cône qui s'élargit sur place) — l'asset VFX doit porter sa propre animation
    /// de croissance timée sur skill.range/skill.projectileSpeed si un effet visuel progressif est
    /// voulu ; ce n'est pas ce composant qui le scale. Si `skill.hasDelayedImpact` (seule
    /// combinaison autorisée avec isTrajectory pour Cone, voir SkillData.isTrajectory), plante EN
    /// PLUS une zone classique (DelayedZoneRoutine) au bout du cône une fois l'expansion terminée
    /// — double dégât voulu, pas une alternative à l'expansion.</summary>
    private IEnumerator TrajectoryConeRoutine(SkillData skill, Entity caster, Vector3 origin, Vector3 dir, GameObject trajectoryVfx, bool destroySelfOnFinish)
    {
        float maxRange  = skill.range          > 0f ? skill.range          : 5f;
        float halfAngle = skill.coneHalfAngle  > 0f ? skill.coneHalfAngle  : 45f;
        float speed     = skill.projectileSpeed > 0f ? skill.projectileSpeed : 10f;
        float totalTime = maxRange / speed;

        HashSet<Entity> alreadyHit = new HashSet<Entity>();
        float   elapsed    = 0f;
        bool    casterDied = false;

        while (elapsed < totalTime)
        {
            // Même garde caster-mort-en-vol que TrajectoryRoutine — rien à nettoyer après (pas
            // de marker/VFX propre à cette coroutine au-delà de trajectoryVfx, déjà géré plus bas).
            if (caster == null || caster.isDead) { casterDied = true; break; }

            elapsed += Time.deltaTime;
            float currentReach = Mathf.Min(maxRange, speed * elapsed);

            foreach (Collider col in Physics.OverlapSphere(origin, currentReach))
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity.isDead) continue;
                if (alreadyHit.Contains(entity)) continue;
                if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                Vector3 toEntity = (entity.transform.position - origin).normalized;
                if (Vector3.Angle(dir, toEntity) > halfAngle) continue;

                alreadyHit.Add(entity);
                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);

                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
            }

            yield return null;
        }

        Vector3 tipPoint = origin + dir * maxRange;

        // hasDelayedImpact + isTrajectory combinés — UNIQUEMENT Cone (voir OnValidate, exception
        // délibérée à leur exclusion mutuelle habituelle). L'expansion a déjà infligé ses dégâts
        // immédiats à tout ce qui est entré dans l'éventail ; en plus, une zone classique se
        // plante au bout (aoeRadius, libre pour Cone depuis coneHalfAngle) et détone après
        // impactDelay — double dégât voulu si une cible reste dans le rayon final (demande
        // explicite Florian).
        bool plantsZoneAtEnd = !casterDied && skill.hasDelayedImpact;

        // Cône terminé sans toucher personne (whiff) — même feedback de secours que
        // TrajectoryRoutine, joué au bout du cône dans la direction visée plutôt qu'à `origin`
        // (un vfxImpact à origin serait littéralement sur le caster, pas lisible comme un whiff).
        // Sauté si une zone va de toute façon se planter au bout (son propre marker/détonation
        // suffit, un vfxImpact immédiat en plus serait un doublon confus).
        if (!casterDied && alreadyHit.Count == 0 && !plantsZoneAtEnd)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, tipPoint, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, tipPoint);
        }

        if (trajectoryVfx != null)
            Destroy(trajectoryVfx);

        if (plantsZoneAtEnd)
        {
            GameObject marker = skill.vfxZoneMarker != null
                ? Instantiate(skill.vfxZoneMarker, tipPoint, Quaternion.identity)
                : null;
            // Nested sur CE host (pas un nouveau) — destroySelfOnFinish: false ici, le Destroy du
            // host reste géré une seule fois, juste en dessous, une fois cette attente terminée.
            yield return DelayedZoneRoutine(skill, caster, tipPoint, null, marker, destroySelfOnFinish: false);
        }

        if (destroySelfOnFinish)
            Destroy(gameObject);
    }

    /// <summary>Résout UN hit précis d'un skill MultiHit (joueur — chantier B — et Mob/PNJ via
    /// leur propre pending-hit) — hitIndex 0 = coup de base (dispatch standard, comme un skill
    /// à un seul coup), hitIndex
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

        GameObject vfx   = step.vfxPrefab  ?? skill.vfxImpact;
        AudioClip  sound = step.soundEffect ?? skill.soundEffect;
        if (vfx   != null) Instantiate(vfx, target.transform.position, Quaternion.identity);
        if (sound != null) AudioSource.PlayClipAtPoint(sound, caster.transform.position);

        CheckKill(target);
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
                if (target != null && !target.isDead && PassesAoeFilter(skill.aoeFaction, caster, target))
                    StartCoroutine(DashToTarget(skill, caster, target));
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

            GameObject vfx   = step.vfxPrefab  ?? skill.vfxImpact;
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
        if (!PassesAoeFilter(skill.aoeFaction, caster, target)) return;
        ApplyEffectType(skill, caster, target);
        ApplyStatusEffects(skill, caster, target);
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
            CheckKill(entity);
        }
    }

    // =========================================================
    // CONE — éventail devant le caster
    // Utilise _skillDirection ou le regard du caster.
    // skill.range = portée du cône (mètres), skill.coneHalfAngle = demi-angle (degrés) — deux
    // champs dédiés depuis l'extension isTrajectory (avant : aoeRadius réutilisé comme angle,
    // confusion trouvée en test manuel — aoeRadius garde maintenant son sens habituel partout).
    // =========================================================

    private void ExecuteCone(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float range    = skill.range         > 0f ? skill.range         : 5f;
        float halfAngle = skill.coneHalfAngle > 0f ? skill.coneHalfAngle : 45f;

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
                CheckKill(target);

                // vfxImpact/soundEffect joués ICI, au moment RÉEL du coup — Execute()/
                // ResolveExecute() ne les jouent plus pour Dash_Target (voir leur propre
                // commentaire), sinon ils partaient immédiatement au clic, ~0.3s avant l'arrivée
                // du dash (trouvé lors de l'audit VFX du 2026-09-12).
                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, target.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, target.transform.position);
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
                CheckKill(entity);

                // vfxImpact/soundEffect joués ICI, par entité touchée, au moment RÉEL du coup —
                // même raison que DashToTarget ci-dessus.
                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
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
                ApplyOnHitDealtEffects(caster, target, dmg);

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
    // EFFETS SECONDAIRES — On-Hit infligés (réactions, PAS DamageAmpOnHit —
    // celui-ci est un modificateur consommé dans CombatSystem.CalculateDamage/
    // CalculateMobDamage, jamais ici)
    // =========================================================

    /// <summary>Applique les effets On-Hit INFLIGÉS (Lifesteal/Mana/ApplyDebuff/ApplyBuff) de
    /// l'attaquant après un coup qui a effectivement touché (jamais appelée sur un Miss — les
    /// call sites sont placés juste après un target.TakeDamage(dmg,...) réussi).</summary>
    private void ApplyOnHitDealtEffects(Entity attacker, Entity target, float damageDealt)
    {
        var effects = attacker?.GetOnHitDealtEffects();
        if (effects == null) return;

        foreach (var entry in effects)
        {
            if (entry?.effect == null || !entry.Roll()) continue;
            switch (entry.effect.effectType)
            {
                case OnHitDealtEffectType.LifestealOnHit:
                    attacker.Heal(entry.effect.GetLifestealAmount(damageDealt));
                    break;

                case OnHitDealtEffectType.ManaOnHit:
                    attacker.RecoverMana(entry.effect.GetManaAmount(attacker.MaxMana));
                    break;

                case OnHitDealtEffectType.ApplyDebuffOnHit:
                    if (entry.effect.debuffToApply != null && target?.statusEffects != null)
                        target.statusEffects.TryApplyDebuff(entry.effect.debuffToApply, attacker);
                    break;

                case OnHitDealtEffectType.ApplyBuffOnHit:
                    if (entry.effect.buffToApply != null && attacker.statusEffects != null)
                        attacker.statusEffects.ApplyBuff(entry.effect.buffToApply, attacker);
                    break;

                // DamageAmpOnHit : modificateur, jamais géré ici — voir CombatSystem.
            }
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
                ApplyOnHitDealtEffects(caster, target, dmg);
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
