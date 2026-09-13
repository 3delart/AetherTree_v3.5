using UnityEngine;

// =============================================================
// SpiritCompanion.cs — visuel d'esprit flottant qui suit le joueur
// Path : Assets/Scripts/Entities/SpiritCompanion.cs
// AetherTree GDD v3.6 — §5.8 (2026-09-06)
//
// Ajouté automatiquement (AddComponent) sur l'instance spawnée par
// Player.SpawnSpiritCompanion() — le prefab (SpiritData.spiritPrefab) n'a besoin d'aucun
// script dessus, juste le mesh/rig/anim.
//
// Suivi minimal (2026-09-08) : la POSITION se rapproche simplement de celle du joueur
// (Lerp, zéro offset — un offset en espace local du joueur faisait décrire une courbe au
// spirit à chaque virage, et cassait le rendu de l'Idle_Hover). La ROTATION n'est PAS
// pilotée ici du tout — le spirit garde l'orientation que lui donne l'anim/le rig, jamais
// calquée sur celle du joueur. Tout en LateUpdate, après l'Animator, pour éviter qu'il
// écrase la position si les clips animent aussi la racine du rig.
// =============================================================
public class SpiritCompanion : MonoBehaviour
{
    [Header("Suivi")]
    [Tooltip("Vitesse de rattrapage de la position — plus haut = suit de plus près.")]
    public float followSmoothing = 5f;
    [Tooltip("Vitesse de rattrapage de l'orientation — le spirit fait face à SA direction de\nvol réelle (vers le joueur, puisqu'il le suit), pas la rotation du joueur elle-même —\nsinon un virage fait décrire une courbe (déjà vu, voir historique).")]
    public float rotationSmoothing = 5f;

    [Header("Animation")]
    [Tooltip("Nom du paramètre bool sur l'Animator Controller — true quand le joueur bouge\n(état Fly_Move), false à l'arrêt (état Idle_Hover). L'Animator est auto-détecté\n(GetComponentInChildren) ; absent = aucune anim pilotée.")]
    public string isMovingParam = "IsMoving";
    [Tooltip("Vitesse du joueur au-dessus de laquelle il est considéré en mouvement (unités/sec).")]
    public float idleSpeedThreshold = 0.15f;

    private Transform _target;
    private Animator  _animator;
    private Vector3   _lastTargetPos;
    private bool      _hasLastPos;

    /// <summary>Appelé une fois juste après Instantiate — ne touche jamais followSmoothing,
    /// seulement la cible à suivre.</summary>
    public void Init(Transform target)
    {
        _target = target;
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null)
            Debug.LogWarning($"[SPIRIT] Aucun Animator trouvé sur {name} (GetComponentInChildren) — " +
                              "l'état d'anim ne sera jamais piloté, reste bloqué sur l'état par défaut du Controller.");

        _hasLastPos = false;
    }

    private void Update()
    {
        if (_target == null || _animator == null) return;

        float targetSpeed = 0f;
        if (_hasLastPos)
            targetSpeed = (_target.position - _lastTargetPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastTargetPos = _target.position;
        _hasLastPos = true;

        bool isMoving = targetSpeed > idleSpeedThreshold;
        _animator.SetBool(isMovingParam, isMoving);
    }

    /// <summary>Position (se rapproche du joueur, zéro offset) + orientation vers la
    /// direction de vol réelle (le joueur, puisque c'est la cible suivie) — PAS la rotation
    /// du joueur elle-même. En LateUpdate, après l'Animator.</summary>
    private void LateUpdate()
    {
        if (_target == null) return;

        transform.position = Vector3.Lerp(transform.position, _target.position, Time.deltaTime * followSmoothing);

        Vector3 toTarget = _target.position - transform.position;
        if (toTarget.sqrMagnitude > 0.01f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(toTarget.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * rotationSmoothing);
        }
    }
}
