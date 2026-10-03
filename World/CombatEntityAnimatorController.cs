using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

// =============================================================
// COMBATENTITYANIMATORCONTROLLER.CS — Pilotage Animator partagé Mob/PNJ
// Path : Assets/Scripts/World/CombatEntityAnimatorController.cs
//
// Remplace MobAnimatorController.cs + PNJAnimatorController.cs (quasi-identiques depuis la
// migration CombatAIController) par UN SEUL composant, utilisé sur tout Mob/PNJ combattant, quel
// que soit son rig (humanoïde, quadrupède, ce que le designer veut) — voir
// docs/guide-utilisation/creation-mob.md § Animator Controller.
//
// Le Animator Controller assigné (Animator.runtimeAnimatorController) doit être LE MÊME asset
// PARTAGÉ pour tous les Mob/PNJ (graphe Idle/Walk/Chase/Attack + params Speed/IsChasing/
// CancelAction) — seul le graphe/la logique est mutualisé, jamais les clips. Ses 4 states
// portent chacun un clip PLACEHOLDER dédié (jamais réellement joué — écrasé par
// AnimatorOverrideController avant le premier frame), retrouvés ici PAR NOM plutôt qu'assignés
// à la main par prefab (élimine la classe de bug "attackPlaceholderClip oublié/mauvais" vue en
// test manuel — un seul asset à configurer pour tout le projet, pas un champ Inspector par
// créature). Renommer un de ces 4 clips dans le Controller casse le lookup silencieusement —
// vérifier les logs d'Awake si un swap ne marche plus après une édition du Controller partagé.
//
// Walk/Chase sont swappés UNE SEULE FOIS à Awake (fixes pour toute la vie de l'instance, lus sur
// ICombatAnimatorProfile — implémenté par Mob et PNJ via leur MobData/PNJData). Idle est swappé à
// Awake PUIS re-tiré au hasard (IdleClip + IdleClipVariants) à chaque retour au repos, voir
// SwapIdleClip(). Attack est swappé à CHAQUE cast (clip différent par skill, voir PlayAttack/
// PlayChannel).
// =============================================================

/// <summary>Fourni par l'owner (Mob ou PNJ) — locomotion (clips + état courant) et relais de
/// l'Animation Event de hit. Mob et PNJ implémentent déjà CurrentState/OnAnimationHitEvent pour
/// d'autres besoins (MobAnimatorController historique / CombatAIController) — cette interface ne
/// fait que les déclarer formellement, comme ICombatAnimator côté CombatAIController.</summary>
public interface ICombatAnimatorProfile
{
    AnimationClip IdleClip  { get; }
    AnimationClip WalkClip  { get; }
    AnimationClip ChaseClip { get; }
    AnimationClip DeathClip { get; }
    // Variantes supplémentaires d'Idle (IdleClip reste la variante de base toujours incluse) —
    // une est tirée au hasard à chaque retour au repos (Speed passe à 0), voir
    // CombatEntityAnimatorController.Update(). Optionnel : null/vide = toujours IdleClip, comme
    // avant ce chantier.
    List<AnimationClip> IdleClipVariants { get; }
    CombatAIState CurrentState { get; }
    void OnAnimationHitEvent(int hitIndex);
}

[RequireComponent(typeof(Animator))]
public class CombatEntityAnimatorController : MonoBehaviour, ICombatAnimator
{
    // Noms EXACTS des 4 clips placeholder posés dans le Animator Controller partagé — un
    // renommage dans le Controller doit être répercuté ici, sinon le lookup échoue et le swap
    // ne fait plus rien (l'anim reste sur le placeholder d'origine, jamais celle voulue). Mis à
    // jour (2026-10) pour matcher les clips réels du preset Humanoid (Idle_PlaceholderHumanoid
    // etc., voir MobAnimationPresets) — si un Controller distinct pour Quadruped/Autre voit le
    // jour un jour avec ses propres clips placeholder, ces constantes devront redevenir
    // génériques ou ce lookup devra être revu par rig.
    private const string PlaceholderIdleName   = "Idle_PlaceholderHumanoid";
    private const string PlaceholderWalkName   = "Walk_PlaceholderHumanoid";
    private const string PlaceholderChaseName  = "Chase_PlaceholderHumanoid";
    private const string PlaceholderAttackName = "Attack_PlaceholderHumanoid";
    private const string PlaceholderDeathName  = "Death_PlaceholderHumanoid";

    private const string SpeedParam          = "Speed";
    private const string IsChasingParam      = "IsChasing";
    private const string AttackState         = "Attack";
    // Death est un state SÉPARÉ d'Attack — pas de transition automatique de sortie (contrairement
    // à Attack), il doit tenir la pose jusqu'à Destroy(gameObject) (Mob) ou jusqu'au respawn (PNJ).
    // Configuré manuellement dans le Controller partagé par Florian, même graphe que Attack.
    private const string DeathState          = "Death";
    private const string CancelActionTrigger = "CancelAction";

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private ICombatAnimatorProfile      _profile;

    private AnimationClip _idlePlaceholder;
    private AnimationClip _walkPlaceholder;
    private AnimationClip _chasePlaceholder;
    private AnimationClip _attackPlaceholder;
    private AnimationClip _deathPlaceholder;

    // true au départ — fait déclencher le tirage d'une variante Idle dès le premier Update() si
    // l'entité démarre déjà à l'arrêt (spawn immobile), sans dupliquer la logique de tirage à part.
    private bool _wasMoving = true;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _profile  = GetComponent<ICombatAnimatorProfile>();

        if (_animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[CombatEntityAnimatorController] Aucun Animator Controller assigné " +
                              "— PlayAttack()/PlayChannel() et la locomotion Idle/Walk/Chase ne " +
                              "pourront jamais s'échanger.", this);
            return;
        }

        _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        _animator.runtimeAnimatorController = _overrideController;

        ResolvePlaceholders();
        ApplyLocomotionClips();
    }

    /// <summary>Retrouve les 4 clips placeholder du Controller PARTAGÉ par nom — remplace les 4
    /// champs Inspector par-prefab de l'ancien système (un seul asset à configurer pour tout le
    /// projet). Un placeholder manquant reste null : le swap correspondant est silencieusement
    /// ignoré plus bas (log déjà émis ici pour ne pas laisser le silence total).</summary>
    private void ResolvePlaceholders()
    {
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        _overrideController.GetOverrides(overrides);

        foreach (var pair in overrides)
        {
            if (pair.Key == null) continue;
            switch (pair.Key.name)
            {
                case PlaceholderIdleName:   _idlePlaceholder   = pair.Key; break;
                case PlaceholderWalkName:   _walkPlaceholder   = pair.Key; break;
                case PlaceholderChaseName:  _chasePlaceholder  = pair.Key; break;
                case PlaceholderAttackName: _attackPlaceholder = pair.Key; break;
                case PlaceholderDeathName:  _deathPlaceholder  = pair.Key; break;
            }
        }

        // _deathPlaceholder volontairement EXCLU de ce garde-fou — Florian ajoute le state/clip
        // Death au Controller partagé après ce chantier ; tant que ce n'est pas fait, PlayDeath()
        // no-op silencieusement (voir son early-return), pas de warning bruyant à chaque Awake.
        if (_idlePlaceholder == null || _walkPlaceholder == null ||
            _chasePlaceholder == null || _attackPlaceholder == null)
            Debug.LogWarning("[CombatEntityAnimatorController] Un ou plusieurs placeholders " +
                              $"({PlaceholderIdleName}/{PlaceholderWalkName}/{PlaceholderChaseName}/" +
                              $"{PlaceholderAttackName}) introuvables par nom dans le Controller " +
                              "assigné — vérifier qu'il s'agit bien du Controller partagé et que " +
                              "ses 4 clips placeholder n'ont pas été renommés.", this);
    }

    /// <summary>Swap Idle/Walk/Chase une seule fois — fixes pour toute la vie de l'instance,
    /// contrairement à Attack (un clip différent par skill, swappé à chaque cast).</summary>
    private void ApplyLocomotionClips()
    {
        if (_profile == null) return;

        if (_idlePlaceholder  != null && _profile.IdleClip  != null) _overrideController[_idlePlaceholder]  = _profile.IdleClip;
        if (_walkPlaceholder  != null && _profile.WalkClip  != null) _overrideController[_walkPlaceholder]  = _profile.WalkClip;
        if (_chasePlaceholder != null && _profile.ChaseClip != null) _overrideController[_chasePlaceholder] = _profile.ChaseClip;
    }

    private void Update()
    {
        // _overrideController == null couvre aussi bien _animator == null (RequireComponent
        // garantit sa présence, jamais null en pratique) que le cas réel rencontré en test manuel
        // : Animator présent mais SANS Controller assigné (prefab pas encore branché sur
        // CombatEntityBase.controller) — Awake() n'a alors pas construit l'override et est
        // ressorti tôt ; sans ce garde-fou, SetFloat/SetBool spamment "Animator is not playing an
        // AnimatorController" à CHAQUE frame tant que le Controller manque.
        if (_overrideController == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);

        // Distingue Walk (Patrol) de Chase (Engage — poursuite ET combat rapproché immobile) —
        // impossible via Speed seul, Speed=0 pendant Engage (immobile en train d'attaquer)
        // ressemble à Speed=0 en Patrol (arrivé à un point d'attente).
        _animator.SetBool(IsChasingParam, _profile != null && _profile.CurrentState == CombatAIState.Engage);

        // Tirage d'une variante Idle au moment précis où on repasse à l'arrêt (front descendant
        // Speed>0 → Speed≈0) — pas à chaque frame immobile (resterait figé sur le même choix tout
        // du long, voir doc du champ _wasMoving), pas de Play() forcé non plus : juste remplacer le
        // contenu du slot AVANT que l'Animator ne transite lui-même vers Idle, pour ne pas casser
        // le blend Walk→Idle existant avec un Play(0,0f) qui coupe net.
        bool isMoving = speed > 0.05f;
        if (_wasMoving && !isMoving) SwapIdleClip();
        _wasMoving = isMoving;
    }

    /// <summary>Tire une variante Idle au hasard parmi IdleClip + IdleClipVariants et la place dans
    /// le slot placeholder — sans relancer le state (contrairement à PlayOverrideClip), pour
    /// laisser l'Animator transiter naturellement vers Idle avec son propre blend.</summary>
    private void SwapIdleClip()
    {
        if (_overrideController == null || _idlePlaceholder == null || _profile == null) return;

        var variants = _profile.IdleClipVariants;
        int variantCount = variants?.Count ?? 0;
        int roll = Random.Range(0, variantCount + 1); // 0 = IdleClip de base, 1..N = variantes

        AnimationClip chosen = roll == 0 ? _profile.IdleClip : variants[roll - 1];
        if (chosen != null) _overrideController[_idlePlaceholder] = chosen;
    }

    /// <summary>Échange le clip d'un slot placeholder puis relance le state associé depuis le
    /// début — généralisé pour servir Attack (placeholder/state réutilisés à chaque cast) ET
    /// Death (placeholder/state séparés, joués une seule fois à la mort). placeholderSlotKey
    /// DOIT être le clip D'ORIGINE du slot (clé de l'indexeur AnimatorOverrideController, pas
    /// le clip de destination) — voir ResolvePlaceholders().</summary>
    private void PlayOverrideClip(AnimationClip clip, AnimationClip placeholderSlotKey, string stateName)
    {
        if (clip == null || _overrideController == null || placeholderSlotKey == null) return;
        _overrideController[placeholderSlotKey] = clip;

        // Reset du trigger CancelAction avant de rejouer le state — sinon un trigger posé par
        // CancelChannel() qui n'a jamais trouvé de transition à consommer (ex: l'Animator était
        // déjà revenu en locomotion) resterait en attente et se déclencherait au prochain
        // re-entré dans Attack, coupant net un skill qui n'a rien à voir avec l'interrupt
        // précédent — même protection que PlayerAnimatorController.cs.
        _animator.ResetTrigger(CancelActionTrigger);
        _animator.Play(stateName, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.StartPendingHit()/PNJ équivalent.</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip, _attackPlaceholder, AttackState);

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (réutilise le state "Attack", pas de state séparé).</summary>
    public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip, _attackPlaceholder, AttackState);

    /// <summary>Joue l'animation de mort — échange le clip du state "Death" séparé (jamais
    /// réutilisé par autre chose, contrairement à Attack/Channel) puis le lance depuis le début.
    /// No-op si aucun deathClip n'est assigné sur le MobData/PNJData (skip silencieux, pas
    /// d'erreur) — retourne la longueur du clip pour laisser l'appelant (Mob.Die()/
    /// PNJ.RespawnCoroutine()) calculer son propre délai sans dupliquer cette logique.</summary>
    public float PlayDeath(AnimationClip clip)
    {
        if (clip == null) return 0f;
        PlayOverrideClip(clip, _deathPlaceholder, DeathState);
        return clip.length;
    }

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion.</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (coup simple ou coup de base d'un MultiHit),
    /// 1..N pour les hitSteps. Relais pur vers l'instance locale (Mob ou PNJ).</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _profile?.OnAnimationHitEvent(hitIndex);
    }

    // Hooks vides pour les Animation Events "FootStep"/"PlayFootStep" déjà posés aux bons frames
    // sur les clips de marche/course repris d'AnyRPG — sans ces méthodes, Unity logue juste un
    // warning "no receiver" (l'anim joue quand même, inoffensif). Prêts pour un futur système de
    // son de pas, pas de logique pour l'instant.
    public void FootStep()     { }
    public void PlayFootStep() { }
}
