using UnityEngine;
using UnityEngine.AI;

// =============================================================
// MOBANIMATORCONTROLLER.CS — Pilotage de l'Animator d'un Mob
// Path : Assets/Scripts/World/MobAnimatorController.cs
//
// Fondations (sous-chantier 1, voir docs/superpowers/specs/
// 2026-09-12-mob-pnj-animator-foundations-design.md) — locomotion (Speed) +
// state "Attack" réutilisable, même pattern que PlayerAnimatorController.cs.
// PAS de paramètre InCombat (aucune mécanique d'équipement visible sur Mob). IsChasing (bool)
// ajouté pour distinguer walk (Patrol) de chase (Chase) — Speed seul ne suffit pas, les deux
// utilisent la même vitesse aujourd'hui.
// CancelActionTrigger ajouté au sous-chantier 2 (canalisation) — pas de state Channel séparé,
// réutilise le state "Attack" existant.
// =============================================================

[RequireComponent(typeof(Animator))]
public class MobAnimatorController : MonoBehaviour
{
    private const string SpeedParam          = "Speed";
    private const string IsChasingParam      = "IsChasing";
    private const string AttackState         = "Attack";
    private const string CancelActionTrigger = "CancelAction";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans l'Animator\n" +
             "Controller (un placeholder dédié, jamais réutilisé) — AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE, pas par nom de state — sans cette référence\n" +
             "exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private Mob                         _mob;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _mob      = GetComponent<Mob>();

        // Enveloppe le Controller de base dans un Override — permet d'échanger le clip du state
        // "Attack" à la volée sans toucher au graphe. Si aucun Controller n'est encore assigné
        // (mannequin pas encore équipé de son Animator Controller), on n'enveloppe rien —
        // PlayAttack() ne fera rien tant que ce sera le cas.
        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[MobAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);

        // Distingue walk (Patrol) de chase (Chase) — impossible via Speed seul, les deux
        // utilisent la même vitesse aujourd'hui (Mob.agent.speed ne varie pas selon l'état).
        _animator.SetBool(IsChasingParam, _mob != null && _mob.CurrentState == MobState.Chase);
    }

    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;

        // Indexation par référence au clip D'ORIGINE (attackPlaceholderClip), pas par nom de
        // state — voir le commentaire sur le champ ci-dessus.
        _overrideController[attackPlaceholderClip] = clip;

        // Reset du trigger CancelAction avant de rejouer le state Attack — sinon un trigger posé
        // par CancelChannel() qui n'a jamais trouvé de transition à consommer (ex: l'Animator
        // était déjà revenu en locomotion) resterait en attente et se déclencherait au prochain
        // re-entré dans Attack, coupant net un skill qui n'a rien à voir avec l'interrupt
        // précédent — même protection que PlayerAnimatorController.cs.
        _animator.ResetTrigger(CancelActionTrigger);
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Mob.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Joue l'animation de canalisation d'un skill (castTime > 0) — même mécanisme
    /// d'échange que PlayAttack (réutilise le state "Attack", pas de state séparé). Appelé par
    /// Mob.StartChannelCast().</summary>
    public void PlayChannel(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Coupe net l'anim de canalisation en cours — déclenche le trigger qui force le
    /// retour à la locomotion. Appelé par Mob.InterruptChannelCast().</summary>
    public void CancelChannel()
    {
        if (_animator == null) return;
        _animator.SetTrigger(CancelActionTrigger);
    }

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (hit simple ou coup de base d'un MultiHit),
    /// 1..N pour les hitSteps. Relais pur vers l'instance locale — pas de singleton comme
    /// SkillBar côté Player, chaque Mob résout son propre pending-hit.</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _mob?.OnAnimationHitEvent(hitIndex);
    }
}
