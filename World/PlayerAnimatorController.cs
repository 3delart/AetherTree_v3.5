using UnityEngine;
using UnityEngine.AI;

// =============================================================
// PLAYERANIMATORCONTROLLER.CS — Pilotage de l'Animator du joueur
// Path : Assets/Scripts/World/PlayerAnimatorController.cs
//
// Locomotion : paramètre "Speed" (float) piloté par NavMeshAgent.velocity,
// "InCombat" (bool) piloté par Player.CombatActive — l'Animator Controller
// doit définir ces deux paramètres + les états Idle/Walk/Run, avec une
// variante armée (InCombat=true) et désarmée (InCombat=false) — pas de
// logique ici pour lister ces états, c'est le graphe Animator qui décide
// des transitions selon ces deux paramètres.
//
// Attaque : un seul état "Attack" réutilisable dans l'Animator Controller
// (Motion placeholder au départ) — le clip réel est injecté à la volée via
// AnimatorOverrideController selon SkillData.attackAnimation, pour ne pas
// avoir à ajouter un state par skill à la main (des dizaines/centaines de
// skills à terme — voir a implémenter, décision prise avec l'utilisateur).
// =============================================================

[RequireComponent(typeof(Animator))]
public class PlayerAnimatorController : MonoBehaviour
{
    private const string SpeedParam    = "Speed";
    private const string InCombatParam = "InCombat";
    private const string AttackState   = "Attack";

    [Header("Attack (override)")]
    [Tooltip("Le MÊME clip que celui assigné comme Motion du state \"Attack\" dans le\n" +
             "Animator Controller (un placeholder, ex: un clip vide dédié — pas Happy Idle\n" +
             "réutilisé, pour éviter de confondre avec le vrai state Idle). AnimatorOverrideController\n" +
             "indexe par référence au clip D'ORIGINE du state, pas par nom de state — sans cette\n" +
             "référence exacte, l'override ne peut pas savoir quel slot remplacer.")]
    [SerializeField] private AnimationClip attackPlaceholderClip;

    private Animator                   _animator;
    private AnimatorOverrideController _overrideController;
    private NavMeshAgent                _agent;
    private Player                      _player;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _agent    = GetComponent<NavMeshAgent>();
        _player   = GetComponent<Player>();

        // Enveloppe le Controller de base dans un Override — permet d'échanger le clip
        // du state "Attack" à la volée sans toucher au graphe. Si aucun Controller n'est
        // encore assigné (mannequin pas encore équipé de son Animator Controller), on
        // n'enveloppe rien — PlayAttack() ne fera rien tant que ce sera le cas.
        if (_animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
            _animator.runtimeAnimatorController = _overrideController;
        }

        if (attackPlaceholderClip == null)
            Debug.LogWarning("[PlayerAnimatorController] attackPlaceholderClip non assigné — " +
                              "PlayAttack() ne pourra pas échanger le clip du state \"Attack\".", this);
    }

    private void Update()
    {
        if (_animator == null) return;

        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        _animator.SetFloat(SpeedParam, speed);
        _animator.SetBool(InCombatParam, _player != null && _player.CombatActive);
    }

    /// <summary>
    /// Joue l'animation d'un skill — échange le clip du state "Attack" réutilisable
    /// puis relance ce state depuis le début. Appelé par Player.UseSkill().
    /// Ne fait rien si le skill n'a pas d'attackAnimation assignée (ex: buff pur) ou si
    /// le Controller n'est pas encore prêt.
    /// </summary>
    public void PlayAttack(AnimationClip clip)
    {
        if (clip == null || _overrideController == null || attackPlaceholderClip == null) return;

        // Indexation par référence au clip D'ORIGINE (attackPlaceholderClip), pas par nom
        // de state — voir le commentaire sur le champ ci-dessus.
        _overrideController[attackPlaceholderClip] = clip;
        _animator.Play(AttackState, 0, 0f);
    }
}
