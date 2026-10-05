using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

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

    [Header("Feedback clic-déplacement")]
    [Tooltip("Sprite du marqueur au sol — rétrécit et s'efface au point cliqué. Null = pas de " +
             "feedback (comportement d'avant, no-op silencieux). AnyRPG research item #9.")]
    public Sprite clickMarkerSprite;
    public Color  clickMarkerColor     = Color.white;
    public float  clickMarkerWorldSize = 0.6f;
    public float  clickMarkerDuration  = 0.4f;

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
        // Agent désactivé — SkillSystem le fait pendant un DashSelf/Pull/Push/etc. en cours
        // (StartDisplacement, voir Combat/SkillSystem.cs) pour garder le contrôle exclusif du
        // transform le temps du trajet animé. SetDestination() sur un agent désactivé lève une
        // exception Unity ("Agent not on NavMesh") — sans ce garde, tenir une touche de
        // déplacement pendant que le joueur lance son propre DashSelf plantait ici.
        if (_agent == null || !_agent.enabled) return;

        var fx = _player?.statusEffects;

        // Stun, Freeze, Root ou Knockback (mini-stun) — bloque le mouvement
        if (fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed || fx.isRooted || fx.isKnockedBack)) return;

        // Skill en cours (Normal, Combo-step ou MultiHit) — immobile jusqu'à la fin RÉELLE de
        // l'anim (IsAnimLocked), pas juste jusqu'à la résolution des dégâts (qui peut tomber en
        // milieu de clip selon où l'Animation Event est placé) — IsMultiHitLocked reste utile à
        // part pour le cas passif→MultiHit qui passe encore par l'ancien SkillSystem.Execute().
        // Canalisation volontairement exclue : le mouvement l'annule, comportement voulu (CD
        // moitié).
        if (SkillBar.Instance != null && (SkillBar.Instance.IsMultiHitLocked || SkillBar.Instance.IsAnimLocked))
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

        // Clic droit pendant une visée GroundTarget (SkillBar) annule la visée au lieu de
        // déplacer le joueur — SkillBar.UpdateGroundTargetAiming() consomme ce même clic ce
        // frame pour couper l'aperçu. Seulement sur la frame d'appui (GetMouseButtonDown, pas
        // MoveHeld qui reste vrai tant que le bouton est maintenu) : si le joueur garde le clic
        // droit enfoncé après l'annulation, le mouvement normal reprend dès la frame suivante.
        if (SkillBar.Instance != null && SkillBar.Instance.IsAwaitingGroundTarget && Input.GetMouseButtonDown(1))
            return;

        if (!GameControls.MoveHeld) return;

        if (UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen()) return;

        // Le raycast 3D ci-dessous ignore l'UI (Physics.Raycast ne connaît que les colliders du
        // monde) — sans ce garde, un clic droit sur une barre d'action (hotbar, skillbar...) qui
        // couvre visuellement un point de sol AUSSI déplacerait le joueur, en plus de l'action UI
        // elle-même (ex: vider un slot au clic droit).
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

        // QueryTriggerInteraction.Ignore : ignore tous les colliders en mode Trigger
        // (ZoneTrigger, SphereCollider des arbres, etc.) — seul le sol solide est touché.
        // GroundRaycastUtil (pas Physics.Raycast direct) — un mob au collider large (Boss
        // Géant/Invasion) peut occuper tout l'espace écran entre la caméra et le sol ; un
        // simple Raycast prend le PREMIER collider touché (le mob, pas tagué "Ground") et
        // abandonne le déplacement — Florian, 2026-09-30 : "mon clic est sur son collider, ce
        // qui m'empêche de me déplacer".
        if (!GroundRaycastUtil.TryRaycastGround(ray, out RaycastHit hit)) return;

        // Déplacement manuel — annule toute approche automatique en cours.
        // SkillBar a son PROPRE suivi d'approche (_isApproaching/_pendingSkill, pour
        // le cast auto-approche d'un skill hors portée) — indépendant de celui de
        // TargetingSystem (loot/ressource/PNJ). Sans ce 2e appel, un clic manuel
        // pendant qu'un skill approche laissait CheckApproach() reprendre la main
        // sur l'agent chaque frame (SetDestination vers la cible périmée) et finir
        // par lancer ce skill périmé dès que la cible redevenait à portée.
        TargetingSystem.Instance?.StopApproach();
        SkillBar.Instance?.CancelApproach();
        _player?.RegisterMovement();

        _agent.SetDestination(hit.point);
        ClickMoveMarker.Spawn(hit.point, clickMarkerSprite, clickMarkerColor, clickMarkerWorldSize, clickMarkerDuration);

        Vector3 direction = (hit.point - transform.position);
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized),
                rotationSpeed * Time.deltaTime);
    }
}
