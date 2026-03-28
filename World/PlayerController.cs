using UnityEngine;
using UnityEngine.AI;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed        = 5f;
    public float stoppingDistance = 0.1f;
    public float rotationSpeed    = 10f;

    private NavMeshAgent agent;
    private Camera       mainCamera;
    private Player       _player;

    void Start()
    {
        agent                  = GetComponent<NavMeshAgent>();
        agent.speed            = moveSpeed;
        agent.stoppingDistance = stoppingDistance;
        agent.angularSpeed     = 999f;
        agent.acceleration     = 999f;
        mainCamera             = Camera.main;
        _player                = GetComponent<Player>();
    }

    void Update()
    {
        // Vitesse — base depuis player.moveSpeed (RecalculateStats) × Slow × Haste
        if (_player != null)
        {
            float baseSpeed = _player.MoveSpeed; // toujours à jour via RecalculateStats
            float slow  = _player.statusEffects != null ? _player.statusEffects.slowMultiplier      : 1f;
            float haste = _player.statusEffects != null ? _player.statusEffects.buffSpeedMultiplier : 1f;
            agent.speed = baseSpeed * slow * haste;
        }

        HandleMovement();
    }

    private void HandleMovement()
    {
        var fx = _player?.statusEffects;

        // Stun ou Root — bloque le mouvement
        if (fx != null && (fx.isStunned || fx.isRooted))
        {
            Debug.Log("[MOVE] ❌ Bloqué — Stun ou Root actif");
            return;
        }

        // Fear
        if (fx != null && fx.isFeared)
        {
            Debug.Log("[MOVE] 😨 Fear actif — fuite");
            Entity threat = TargetingSystem.Instance?.GetEngagedTarget()
                        ?? TargetingSystem.Instance?.GetSelectedTarget();
            if (threat != null)
            {
                Vector3 fleeDir = (_player.transform.position - threat.transform.position).normalized;
                Vector3 fleePos = _player.transform.position + fleeDir * 5f;
                agent.SetDestination(fleePos);
            }
            return;
        }

        if (!GameControls.MoveHeld)
        {
            // Debug.Log("[MOVE] MoveHeld = false"); // décommenter si besoin
            return;
        }

        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen())
        {
            Debug.Log("[MOVE] ❌ Bloqué — UI Panel ouvert");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.CompareTag("Ground"))
            {
                agent.SetDestination(hit.point);
                SkillBar.Instance?.CancelApproach();
                LootApproach.Instance?.Cancel();

                Vector3 direction = (hit.point - transform.position).normalized;
                direction.y = 0f;
                if (direction != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction),
                        rotationSpeed * Time.deltaTime);
            }
            else
            {
                Debug.Log($"[MOVE] ⚠️ Tag incorrect — attendu 'Ground', reçu '{hit.collider.tag}'");
            }
        }
        else
        {
            Debug.Log("[MOVE] ❌ Raycast ne touche rien du tout");
        }
    }
}