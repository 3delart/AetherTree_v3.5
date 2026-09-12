using UnityEngine;
using UnityEngine.AI;

// =============================================================
// PNJANIMATORCONTROLLER.CS — Pilotage de l'Animator d'un PNJ
// Path : Assets/Scripts/World/PNJAnimatorController.cs
//
// Fondations (sous-chantier 1, voir docs/superpowers/specs/
// 2026-09-12-mob-pnj-animator-foundations-design.md) — locomotion (Speed) +
// state "Attack" réutilisable, même pattern que MobAnimatorController.cs.
// Pas de RequireComponent(NavMeshAgent) : un PNJ non-canFight n'en a pas
// (voir PNJ.Awake() — _agent n'est assigné que si data.canFight est vrai).
// =============================================================

[RequireComponent(typeof(Animator))]
public class PNJAnimatorController : MonoBehaviour
{
    private const string SpeedParam  = "Speed";
    private const string AttackState = "Attack";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans l'Animator\n" +
             "Controller (un placeholder dédié, jamais réutilisé) — AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE, pas par nom de state — sans cette référence\n" +
             "exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private PNJ                         _pnj;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _pnj      = GetComponent<PNJ>();

        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[PNJAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;
        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);
    }

    private void PlayOverrideClip(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }

    /// <summary>Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par PNJ.StartPendingHit().</summary>
    public void PlayAttack(AnimationClip clip) => PlayOverrideClip(clip);

    /// <summary>Appelé par Unity depuis un Animation Event posé sur le clip en cours de lecture
    /// (state "Attack"). hitIndex : 0 par défaut (hit simple ou coup de base d'un MultiHit),
    /// 1..N pour les hitSteps. Relais pur vers l'instance locale.</summary>
    public void OnSkillHitFrame(int hitIndex = 0)
    {
        _pnj?.OnAnimationHitEvent(hitIndex);
    }
}
