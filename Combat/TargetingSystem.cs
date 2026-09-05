using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// =============================================================
// TARGETINGSYSTEM.CS — Ciblage, auto-attaque & approches
// Path : Assets/Scripts/Systems/TargetingSystem.cs
// AetherTree GDD v3.5 — §8 / §19 / §21
//
// ─── Cibles gérées ───────────────────────────────────────────
//   Entity  (Mob / PNJ / Pet) → Select → Engage (mobs/pets)
//   ResourceNode              → collecte
//   IInteractableBuilding     → interaction bâtiment
//   Ground / vide             → Deselect
//
// Le loot de kill de mob part directement dans l'inventaire (LootManager) —
// plus de pickup au sol à cliquer, voir docs/superpowers/specs/
// 2026-09-05-loot-direct-to-inventory-design.md.
//
// ─── Auto-approche unifiée ───────────────────────────────────
//   TOUTES les approches passent par une coroutine unique.
//   StopApproach() est PUBLIC — appelé par PlayerController
//   dès que le joueur prend le contrôle manuel.
//
// ─── Bâtiments ───────────────────────────────────────────────
//   Implémenter IInteractableBuilding sur forge, puits, etc.
// =============================================================

/// <summary>
/// Interface à implémenter sur tous les bâtiments interactifs
/// (Forge, Puits, Marché, Enclume, etc.).
/// </summary>
public interface IInteractableBuilding
{
    float  InteractionRadius { get; }
    void   Interact(Player player);
    string BuildingName { get; }
}

public class TargetingSystem : MonoBehaviour
{
    public static TargetingSystem Instance { get; private set; }

    // ── Couleurs ──────────────────────────────────────────────
    [Header("Couleurs de sélection")]
    public Color colorSelected = new Color(1f, 0.5f, 0f);
    public Color colorEngaged  = new Color(1f, 0f,   0f);

    // ── Références ────────────────────────────────────────────
    private Player       player;
    private NavMeshAgent _agent;

    // ── État sélection ────────────────────────────────────────
    private Entity                selectedTarget;
    private Entity                engagedTarget;
    private ResourceNode          selectedNode;
    private IInteractableBuilding selectedBuilding;
    private GameObject            selectedBuildingGO;

    // ── Outlines ──────────────────────────────────────────────
    private Outline selectedOutline;
    private Outline engagedOutline;
    private Outline _nodeOutline;
    private Outline _buildingOutline;

    // ── Auto-attaque ──────────────────────────────────────────
    private bool  autoAttacking   = false;
    private float autoAttackTimer = 0f;

    // ── Approche unifiée ──────────────────────────────────────
    private Coroutine _approachCoroutine;
    private const float ApproachTimeout = 15f;

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        player = GetComponent<Player>();
        _agent = GetComponent<NavMeshAgent>();

        if (player == null) Debug.LogError("[TARGETING] Player non trouvé !");
        if (_agent  == null) Debug.LogError("[TARGETING] NavMeshAgent non trouvé !");
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        HandleInput();
        TickAutoAttack();
        CheckApproachCancelled();
    }

    // =========================================================
    // AUTO-ATTAQUE
    // =========================================================

    private void TickAutoAttack()
    {
        if (!autoAttacking || engagedTarget == null)
        {
            return;
        }
        if (SkillBar.Instance != null && SkillBar.Instance.IsApproachingSkill)
        {
            return;
        }
        if (engagedTarget.isDead)
        {
            ClearForDeath(engagedTarget);
            return;
        }

        // Suivi + attaque fusionnés en une seule vérification par frame — avant,
        // une coroutine de chase s'arrêtait dès l'entrée en portée et rendait la
        // main à ce timer, qui ne revérifiait la distance qu'à son propre
        // intervalle (vitesse d'attaque). Une cible qui ressortait de portée
        // entre les deux n'était surveillée par personne (le perso restait
        // planté). Ici la distance est revérifiée chaque frame, donc le suivi
        // ne s'interrompt jamais tant que la cible reste engagée.
        var basicAttackSkill = SkillBar.Instance?.GetSkillAtSlot(0);
        if (basicAttackSkill != null && basicAttackSkill.range > 0f)
        {
            float dist = Vector3.Distance(player.transform.position, engagedTarget.transform.position);
            if (dist > basicAttackSkill.range * 1.1f)
            {
                if (_agent != null) _agent.SetDestination(engagedTarget.transform.position);
                return;
            }
            // À portée — l'auto-attaque ne touche jamais au TargetPanel/à la sélection,
            // même à l'arrivée : seul un vrai skill (slot ≥ 1, voir SkillBar.ExecuteSkill)
            // justifie de reprendre la main dessus.
            if (_agent != null && _agent.hasPath) _agent.ResetPath();
        }

        autoAttackTimer -= Time.deltaTime;
        if (autoAttackTimer <= 0f)
        {
            PerformAutoAttack();
            float speed = player?.equippedWeaponInstance != null
                ? player.equippedWeaponInstance.AttackSpeed : 1.2f;
            autoAttackTimer = speed > 0f ? 1f / speed : 1f;
        }
    }

    private void PerformAutoAttack()
    {
        if (player == null || SkillBar.Instance == null)
        {
            return;
        }
        if (engagedTarget == null || engagedTarget.isDead)
        {
            return;
        }
        if (player.statusEffects != null && player.statusEffects.isStunned)
        {
            return;
        }

        // Distance déjà validée dans TickAutoAttack() juste avant — plus besoin
        // de re-checker ici (l'ancienne approche via coroutine a été retirée).
        SkillBar.Instance.TryUseSlot(0, isAutoTick: true);
    }

    private void ToggleAutoAttack()
    {
        autoAttacking = !autoAttacking;
        Debug.Log($"[TARGETING] Auto-attaque : {autoAttacking}");
    }

    // =========================================================
    // INPUT
    // =========================================================

    private void HandleInput()
    {
        if (GameControls.Deselect)
        {
            if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
                DialogueUI.Instance.CloseDialogue();
            else
                Deselect();
            return;
        }

        if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (engagedTarget != null) ToggleAutoAttack();
            return;
        }

        if (!GameControls.TargetClick) return;
        if (UIManager.Instance != null && UIManager.Instance.BlocksWorldInput()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) { Deselect(); return; }

        if (hit.collider.CompareTag("Ground")) { Deselect(); return; }

        // ── Priorité 1 : ResourceNode ──────────────────────────
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node != null) { HandleNodeClick(node); return; }

        // ── Priorité 2 : Bâtiment interactif ───────────────────
        IInteractableBuilding building = hit.collider.GetComponentInParent<IInteractableBuilding>();
        if (building != null) { HandleBuildingClick(building, hit.collider.gameObject); return; }

        // ── Priorité 3 : Entity ────────────────────────────────
        Entity entity = hit.collider.GetComponentInParent<Entity>();
        if (entity == null || entity == player) { Deselect(); return; }

        switch (entity.entityType)
        {
            case EntityType.PNJ: HandlePNJClick(entity as PNJ); break;
            default:             HandleCombatEntityClick(entity); break;
        }
    }

    // =========================================================
    // HANDLERS PAR TYPE DE CIBLE
    // =========================================================

    // ── ResourceNode ──────────────────────────────────────────

    private void HandleNodeClick(ResourceNode node)
    {
        if (selectedNode == node)
        {
            float dist   = Vector3.Distance(player.transform.position, node.transform.position);
            float radius = node.data?.interactionRadius ?? 2.5f;
            if (dist <= radius)
                node.BeginCollect(player);
            else
                StartApproach(ApproachNodeRoutine(node));
        }
        else
        {
            ClearAllSelection();
            selectedNode    = node;
            _nodeOutline    = node.GetComponent<Outline>()
                        ?? node.gameObject.AddComponent<Outline>();
            _nodeOutline.OutlineColor = colorSelected;
            _nodeOutline.OutlineWidth = 4f;
            _nodeOutline.enabled      = true;
            TargetPanel.Instance?.ShowNode(node);
        }
    }

    // ── Bâtiment interactif ───────────────────────────────────

    private void HandleBuildingClick(IInteractableBuilding building, GameObject buildingGO)
    {
        if (selectedBuildingGO == buildingGO)
        {
            float dist = Vector3.Distance(player.transform.position, buildingGO.transform.position);
            if (dist <= building.InteractionRadius)
                building.Interact(player);
            else
                StartApproach(ApproachBuildingRoutine(building, buildingGO));
        }
        else
        {
            ClearAllSelection();
            selectedBuilding   = building;
            selectedBuildingGO = buildingGO;
            _buildingOutline   = buildingGO.GetComponent<Outline>()
                            ?? buildingGO.AddComponent<Outline>();
            _buildingOutline.OutlineColor = colorSelected;
            _buildingOutline.OutlineWidth = 4f;
            _buildingOutline.enabled      = true;
        }
    }

    // ── PNJ ───────────────────────────────────────────────────

    private void HandlePNJClick(PNJ pnj)
    {
        if (pnj == null) return;

        if (selectedTarget == pnj)
        {
            float dist = Vector3.Distance(player.transform.position, pnj.transform.position);
            if (dist <= pnj.interactionRadius)
                pnj.Interact(player);
            else
                StartApproach(ApproachPNJRoutine(pnj));
        }
        else
        {
            ClearAllSelection();
            Select(pnj);
        }
    }

    // ── Mob / Pet ─────────────────────────────────────────────

    private void HandleCombatEntityClick(Entity entity)
    {
        if (entity == selectedTarget && engagedTarget != entity)
            Engage(entity);
        else
            Select(entity);
    }

    // =========================================================
    // SELECT / ENGAGE
    // =========================================================

    public void Select(Entity entity)
    {
        if (entity == null) return;

        // Re-cliquer sa PROPRE cible déjà engagée (rouge) doit juste ramener le
        // TargetPanel dessus — ne pas repeindre son outline en orange. selectedOutline
        // et engagedOutline peuvent référencer le même composant Outline (un seul par
        // GameObject), donc sans ce garde-fou la cible attaquée redevenait visuellement
        // orange alors qu'elle restait réellement engagée (engagedTarget/autoAttacking
        // inchangés) — désync entre couleur affichée et état réel.
        if (entity == engagedTarget)
        {
            // Nettoie une éventuelle autre sélection orange en cours (ClearAllSelection
            // ne touche pas engagedTarget/engagedOutline, donc la cible rouge n'est pas
            // affectée) sans créer/repeindre d'outline pour cette entité — l'outline
            // rouge d'engagedOutline reste seule maîtresse de sa couleur.
            ClearAllSelection();
            selectedTarget = entity;
            TargetPanel.Instance?.Show(entity);
            return;
        }

        ClearAllSelection();

        selectedTarget  = entity;
        selectedOutline = entity.GetComponent<Outline>()
                       ?? entity.gameObject.AddComponent<Outline>();
        selectedOutline.OutlineColor = colorSelected;
        selectedOutline.OutlineWidth = 4f;
        selectedOutline.enabled      = true;

        TargetPanel.Instance?.Show(entity);
    }

    public void Engage(Entity entity)
    {
        if (entity == null) return;

        if (engagedOutline != null)
            engagedOutline.OutlineColor = colorSelected;

        engagedTarget  = entity;
        engagedOutline = entity.GetComponent<Outline>()
                      ?? entity.gameObject.AddComponent<Outline>();
        engagedOutline.OutlineColor = colorEngaged;
        engagedOutline.OutlineWidth = 4f;
        engagedOutline.enabled      = true;

        autoAttacking   = true;
        autoAttackTimer = 0f;
    }

    public void EngageFromSkill(Entity entity)
    {
        Select(entity);
        Engage(entity);
    }

    // =========================================================
    // EXÉCUTION DE SKILL
    // =========================================================

    public void TryExecuteSkill(SkillData skill)
    {
        if (skill == null || SkillSystem.Instance == null || player == null) return;

        switch (skill.targetType)
        {
            case TargetType.Self:
            case TargetType.AoE_Self:
                SkillSystem.Instance.Execute(skill, player, null);
                return;

            case TargetType.GroundTarget:
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    SkillSystem.Instance.SetGroundTargetPoint(hit.point);
                    SkillSystem.Instance.Execute(skill, player, null);
                }
                return;
            }

            case TargetType.Direction:
            case TargetType.Skillshot:
            case TargetType.Cone:
            case TargetType.Dash_Direction:
            {
                SkillSystem.Instance.SetSkillDirection(ResolveDirection());
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            case TargetType.Target:
            case TargetType.AoE_Target:
            case TargetType.Dash_Target:
            case TargetType.LineTarget:
            {
                Entity target = engagedTarget ?? selectedTarget;
                if (target == null || target.isDead)
                {
                    Debug.LogWarning($"[TARGETING] {skill.name} ({skill.targetType}) : aucune cible valide.");
                    return;
                }
                SkillSystem.Instance.Execute(skill, player, target);
                return;
            }

            default:
                SkillSystem.Instance.Execute(skill, player, engagedTarget ?? selectedTarget);
                return;
        }
    }

    // =========================================================
    // HELPERS DIRECTION
    // =========================================================

    private Vector3 ResolveDirection()
    {
        Entity aim = engagedTarget ?? selectedTarget;
        if (aim != null)
            return (aim.transform.position - player.transform.position).normalized;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            Vector3 toMouse = hit.point - player.transform.position;
            toMouse.y = 0f;
            if (toMouse.sqrMagnitude > 0.001f) return toMouse.normalized;
        }

        return player.transform.forward;
    }

    // =========================================================
    // AUTO-APPROCHE UNIFIÉE
    // =========================================================

    private void StartApproach(IEnumerator routine)
    {
        StopApproach();
        _approachCoroutine = StartCoroutine(routine);
    }

    /// <summary>
    /// Stoppe l'approche en cours.
    /// PUBLIC — appelé par PlayerController dès que MoveHeld est vrai.
    /// </summary>
    public void StopApproach()
    {
        if (_approachCoroutine == null) return;
        StopCoroutine(_approachCoroutine);
        _approachCoroutine = null;
    }

    private void CheckApproachCancelled()
    {
        if (GameControls.MoveHeld)
        {
            if (_approachCoroutine != null)
                StopApproach();

            // Désengage la cible — repasse en orange (sélectionnée) si elle existe
            if (engagedTarget != null)
            {
                // Transfère l'engagée en sélectionnée pour garder l'outline orange
                if (selectedOutline != null) { selectedOutline.enabled = false; }
                selectedTarget  = engagedTarget;
                selectedOutline = engagedOutline;
                if (selectedOutline != null) selectedOutline.OutlineColor = colorSelected;

                engagedTarget  = null;
                engagedOutline = null;
                autoAttacking  = false;
            }
        }
    }

    // ── Routine ResourceNode ──────────────────────────────────

    private IEnumerator ApproachNodeRoutine(ResourceNode node)
    {
        if (_agent == null) yield break;

        float radius  = node.data?.interactionRadius ?? 2.5f;
        float elapsed = 0f;
        _agent.SetDestination(node.transform.position);

        while (elapsed < ApproachTimeout)
        {
            if (node == null || !node.gameObject.activeSelf) yield break;
            _agent.SetDestination(node.transform.position);

            if (Vector3.Distance(player.transform.position, node.transform.position) <= radius)
            {
                _agent.ResetPath();
                node.BeginCollect(player);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.ResetPath();
        _approachCoroutine = null;
    }

    // ── Routine PNJ ───────────────────────────────────────────

    private IEnumerator ApproachPNJRoutine(PNJ pnj)
    {
        if (_agent == null) yield break;

        float elapsed = 0f;
        _agent.SetDestination(pnj.transform.position);

        while (elapsed < ApproachTimeout)
        {
            if (pnj == null || pnj.isDead) yield break;

            if (Vector3.Distance(player.transform.position, pnj.transform.position) <= pnj.interactionRadius)
            {
                _agent.ResetPath();
                pnj.Interact(player);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.ResetPath();
        _approachCoroutine = null;
    }

    // ── Routine Bâtiment ──────────────────────────────────────

    private IEnumerator ApproachBuildingRoutine(IInteractableBuilding building, GameObject buildingGO)
    {
        if (_agent == null) yield break;

        float elapsed = 0f;
        _agent.SetDestination(buildingGO.transform.position);

        while (elapsed < ApproachTimeout)
        {
            if (buildingGO == null || !buildingGO.activeSelf) yield break;

            if (Vector3.Distance(player.transform.position, buildingGO.transform.position) <= building.InteractionRadius)
            {
                _agent.ResetPath();
                building.Interact(player);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _agent.ResetPath();
        _approachCoroutine = null;
    }

    // =========================================================
    // DESELECT
    // =========================================================

    /// <summary>Clic gauche dans le vide / touche Deselect — n'enlève que le "regard"
    /// (sélection orange + TargetPanel). Le combat en cours (cible rouge/auto-attaque)
    /// ne s'arrête QUE via un vrai déplacement volontaire (PlayerController, clic droit)
    /// ou en engageant réellement un autre mob (Engage) — jamais en désélectionnant.
    /// Pour les cas où le combat DOIT vraiment s'arrêter (cible morte, mort du joueur),
    /// voir ClearEverything().</summary>
    public void Deselect()
    {
        ClearAllSelection();
        StopApproach();
        TargetPanel.Instance?.Hide();
    }

    /// <summary>Coupe tout — sélection ET engagement/combat. Réservé à la mort du
    /// joueur (RespawnSystem) — jamais pour un simple clic dans le vide (voir
    /// Deselect()) ni pour la mort d'un mob (voir ClearForDeath, scopé).</summary>
    public void ClearEverything()
    {
        ClearAllSelection();
        ClearEngage();
        StopApproach();
        TargetPanel.Instance?.Hide();
    }

    /// <summary>Nettoyage ciblé sur l'entité qui vient de mourir — ne touche que ce
    /// qui pointait sur ELLE. Si A meurt pendant que B est sélectionné, B et son
    /// TargetPanel restent intacts (seul l'engagement sur A est coupé).</summary>
    public void ClearForDeath(Entity entity)
    {
        if (entity == engagedTarget)
        {
            ClearEngage();
            StopApproach();
        }
        if (entity == selectedTarget)
        {
            ClearAllSelection();
            TargetPanel.Instance?.Hide();
        }
    }

    // =========================================================
    // HELPERS INTERNES
    // =========================================================

    private void ClearAllSelection()
    {
        if (selectedOutline != null) { selectedOutline.enabled = false; selectedOutline = null; }
        if (_nodeOutline     != null) { _nodeOutline.enabled     = false; _nodeOutline     = null; }
        if (_buildingOutline != null) { _buildingOutline.enabled = false; _buildingOutline = null; }
        selectedTarget     = null;
        selectedNode       = null;
        selectedBuilding   = null;
        selectedBuildingGO = null;
    }

    private void ClearEngage()
    {
        if (engagedOutline != null) { engagedOutline.enabled = false; engagedOutline = null; }
        engagedTarget = null;
        autoAttacking = false;
    }

    // =========================================================
    // ACCESSEURS PUBLICS
    // =========================================================

    public Entity                GetEngagedTarget()   => engagedTarget;
    public Entity                GetSelectedTarget()  => selectedTarget;
    public ResourceNode          GetSelectedNode()    => selectedNode;
    public IInteractableBuilding GetSelectedBuilding() => selectedBuilding;
    public bool                  IsAutoAttacking      => autoAttacking;
}
