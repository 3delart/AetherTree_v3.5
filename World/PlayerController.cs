using UnityEngine;
using UnityEngine.AI;

// =============================================================
// PLAYERCONTROLLER.CS — Déplacement joueur
// Path : Assets/Scripts/Core/PlayerController.cs
// AetherTree GDD v3.5
//
// Gère uniquement le déplacement manuel (clic droit).
// Toutes les approches automatiques (loot, ressources, PNJ,
// bâtiments) sont gérées par TargetingSystem.
//
// Quand le joueur prend le contrôle manuel :
//   → TargetingSystem.StopApproach() annule toute approche active
//   → Plus de référence à LootApproach ou SkillBar.CancelApproach
// =============================================================

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed        = 5f;
    public float stoppingDistance = 0.1f;
    public float rotationSpeed    = 10f;

    private NavMeshAgent _agent;
    private Camera       _mainCamera;
    private Player       _player;

    private void Start()
    {
        _agent                  = GetComponent<NavMeshAgent>();
        _agent.speed            = moveSpeed;
        _agent.stoppingDistance = stoppingDistance;
        _agent.angularSpeed     = 999f;
        _agent.acceleration     = 999f;
        _mainCamera             = Camera.main;
        _player                 = GetComponent<Player>();
    }

    private void Update()
    {
        // Vitesse synchronisée avec RecalculateStats + buffs/debuffs
        if (_player != null && _agent != null)
        {
            float baseSpeed = _player.MoveSpeed;
            float slow      = _player.statusEffects != null ? _player.statusEffects.slowMultiplier      : 1f;
            float haste     = _player.statusEffects != null ? _player.statusEffects.buffSpeedMultiplier : 1f;
            _agent.speed = baseSpeed * slow * haste;
        }

        HandleMovement();
    }

    private void HandleMovement()
    {
        var fx = _player?.statusEffects;

        // Stun, Root ou Knockback (mini-stun) — bloque le mouvement
        if (fx != null && (fx.isStunned || fx.isShocked || fx.isRooted || fx.isKnockedBack)) return;

        // MultiHit en cours — immobile le temps du combo (ComboSequence exclu,
        // on peut se déplacer entre deux sorts d'un ComboSequence).
        if (SkillBar.Instance != null && SkillBar.Instance.IsMultiHitLocked)
        {
            if (_agent.hasPath) _agent.ResetPath();
            return;
        }

        // Fear — fuite vers direction opposée à la source RÉELLE du debuff (pas la cible
        // engagée/sélectionnée — celui qui a lancé le Fear n'est pas forcément qui on combat).
        // Fallback aléatoire si la source est morte/introuvable.
        if (fx != null && fx.isFeared)
        {
            Entity fearSource = fx.GetDebuffSource(DebuffType.Fear);
            Vector3 fleeDir;
            if (fearSource != null && !fearSource.isDead)
            {
                fleeDir = (_player.transform.position - fearSource.transform.position).normalized;
            }
            else
            {
                fleeDir = new Vector3(Random.value * 2f - 1f, 0f, Random.value * 2f - 1f);
                fleeDir = fleeDir.sqrMagnitude > 0.001f ? fleeDir.normalized : Vector3.forward;
            }
            _agent.SetDestination(_player.transform.position + fleeDir * 5f);
            return;
        }

        if (!GameControls.MoveHeld) return;

        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen()) return;

        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

        // QueryTriggerInteraction.Ignore : ignore tous les colliders en mode Trigger
        // (ZoneTrigger, SphereCollider des arbres, etc.) — seul le sol solide est touché
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;

        if (!hit.collider.CompareTag("Ground")) return;

        // Déplacement manuel — annule toute approche automatique en cours.
        // SkillBar a son PROPRE suivi d'approche (_isApproaching/_pendingSkill, pour
        // le cast auto-approche d'un skill hors portée) — indépendant de celui de
        // TargetingSystem (loot/ressource/PNJ). Sans ce 2e appel, un clic manuel
        // pendant qu'un skill approche laissait CheckApproach() reprendre la main
        // sur l'agent chaque frame (SetDestination vers la cible périmée) et finir
        // par lancer ce skill périmé dès que la cible redevenait à portée.
        TargetingSystem.Instance?.StopApproach();
        SkillBar.Instance?.CancelApproach();
        _player?.RegisterAction();

        _agent.SetDestination(hit.point);

        Vector3 direction = (hit.point - transform.position);
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized),
                rotationSpeed * Time.deltaTime);
    }
}
