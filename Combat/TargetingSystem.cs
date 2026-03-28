using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// =============================================================
// TARGETINGSYSTEM.CS — Ciblage & Auto-attaque
// Path : Assets/Scripts/Systems/TargetingSystem.cs
// AetherTree GDD v3.5 — §8 / §19 / §21
//
// ─── Cibles gérées ───────────────────────────────────────────
//   Entity  (Mob / PNJ / Pet) → Select → Engage (mobs/pets)
//   ResourceNode              → SelectNode  → collecte
//   WorldLootItem             → SelectLoot  → pickup
//   WorldAerisItem            → SelectAeris → pickup
//   Ground / vide             → Deselect
//
// ─── Auto-approche unifiée ───────────────────────────────────
//   Toutes les approches passent par ApproachCoroutine() :
//     un seul pattern, une seule coroutine active à la fois.
//   Annulée automatiquement si le joueur bouge manuellement
//   (GameControls.MoveHeld).
//
// ─── Flow skill (GDD §7.1) ───────────────────────────────────
//   TryExecuteSkill(skill) résout la target selon skill.targetType :
//     Self / AoE_Self                          → caster = player, target = null
//     GroundTarget                             → point au sol, target = null
//     Direction / Skillshot / Cone             → direction forward/souris, target = null
//     Dash_Direction                           → direction forward/souris, target = null
//     Target / AoE_Target / Dash_Target /
//       LineTarget                             → engagedTarget ?? selectedTarget
//
// ─── Auto-attaque (GDD §8.1) ─────────────────────────────────
//   Timer cadence ici, exécution via SkillBar.TryUseSlot(0).
//   Dodge roll : esquive effective de la CIBLE vs précision effective du JOUEUR.
//
// ─── Sélection / Engagement ──────────────────────────────────
//   1er clic → Select (outline orange, TargetPanel)
//   2e clic  → Engage si Mob/Pet (outline rouge, auto-attaque)
//            → Interact si PNJ (dans rayon ou approche)
//            → Collect si ResourceNode (dans rayon ou approche)
//            → Pickup si LootItem (dans rayon ou approche)
// =============================================================

public class TargetingSystem : MonoBehaviour
{
    public static TargetingSystem Instance { get; private set; }

    // ── Couleurs ──────────────────────────────────────────────
    [Header("Couleurs de sélection")]
    public Color colorSelected = new Color(1f, 0.5f, 0f);  // Orange
    public Color colorEngaged  = new Color(1f, 0f,   0f);  // Rouge

    // ── Références ────────────────────────────────────────────
    private Player player;

    // ── État sélection ────────────────────────────────────────
    private Entity         selectedTarget;
    private Entity         engagedTarget;
    private ResourceNode   selectedNode;
    private WorldLootItem  selectedLoot;
    private WorldAerisItem selectedAeris;

    // ── Outlines ──────────────────────────────────────────────
    private Outline selectedOutline;
    private Outline engagedOutline;

    // ── Auto-attaque ──────────────────────────────────────────
    private bool  autoAttacking   = false;
    private float autoAttackTimer = 0f;

    // ── Approche unifiée ──────────────────────────────────────
    private Coroutine _approachCoroutine;

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
        if (player == null)
            Debug.LogError("[TARGETING] Player non trouvé sur ce GameObject !");
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
        if (!autoAttacking || engagedTarget == null) return;

        if (engagedTarget.isDead) { Deselect(); return; }

        autoAttackTimer -= Time.deltaTime;
        if (autoAttackTimer <= 0f)
        {
            PerformAutoAttack();
            float speed = player?.equippedWeaponInstance != null
                ? player.equippedWeaponInstance.AttackSpeed
                : 1.2f;
            autoAttackTimer = speed > 0f ? 1f / speed : 1f;
        }
    }

    private void PerformAutoAttack()
    {
        if (player == null || SkillBar.Instance == null) return;
        if (engagedTarget == null || engagedTarget.isDead) return;

        // Stun — bloque l'auto-attaque (GDD §21bis.1)
        if (player.statusEffects != null && player.statusEffects.isStunned) return;

        // Dodge roll — esquive effective de la cible vs précision effective du joueur (GDD §3.1.1.2)
        if (CombatSystem.Instance != null)
        {
            bool dodged = CombatSystem.Instance.RollDodge(
                engagedTarget.GetEffectiveDodge(),
                player.GetEffectivePrecision());
            if (dodged)
            {
                FloatingText.Spawn("ESQUIVE", engagedTarget.transform.position, Color.white);
                return;
            }
        }

        SkillBar.Instance.TryUseSlot(0);
    }

    private void ToggleAutoAttack()
    {
        autoAttacking = !autoAttacking;
        Debug.Log($"[TARGETING] Auto-attaque : {autoAttacking}");
    }

    // =========================================================
    // INPUT — dispatch principal
    // =========================================================

    private void HandleInput()
    {
        // Echap : ferme dialogue ou déselectionne
        if (GameControls.Deselect)
        {
            if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
                DialogueUI.Instance.CloseDialogue();
            else
                Deselect();
            return;
        }

        // Dialogue ouvert — bloque tout input monde
        if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen) return;

        // Tab — toggle auto-attaque sur la cible engagée
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (engagedTarget != null) ToggleAutoAttack();
            return;
        }

        if (!GameControls.TargetClick) return;
        if (UIManager.Instance != null && UIManager.Instance.BlocksWorldInput()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) { Deselect(); return; }

        // ── Sol / terrain ─────────────────────────────────────
        if (hit.collider.CompareTag("Ground")) { Deselect(); return; }

        // ── Priorité 1 : Aeris au sol ─────────────────────────
        WorldAerisItem aeris = hit.collider.GetComponentInParent<WorldAerisItem>();
        if (aeris != null) { HandleAerisClick(aeris); return; }

        // ── Priorité 2 : Loot au sol ──────────────────────────
        WorldLootItem loot = hit.collider.GetComponentInParent<WorldLootItem>();
        if (loot != null) { HandleLootClick(loot); return; }

        // ── Priorité 3 : ResourceNode ─────────────────────────
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node != null) { HandleNodeClick(node); return; }

        // ── Priorité 4 : Entity ───────────────────────────────
        Entity entity = hit.collider.GetComponentInParent<Entity>();
        if (entity == null || entity == player) { Deselect(); return; }

        switch (entity.entityType)
        {
            case EntityType.PNJ:  HandlePNJClick(entity as PNJ); break;
            default:              HandleCombatEntityClick(entity); break;  // Mob, Pet
        }
    }

    // =========================================================
    // HANDLERS PAR TYPE DE CIBLE
    // =========================================================

    // ── WorldAerisItem ────────────────────────────────────────

    private void HandleAerisClick(WorldAerisItem aeris)
    {
        if (selectedAeris == aeris)
        {
            float dist = Vector3.Distance(player.transform.position, aeris.transform.position);
            if (dist <= aeris.pickupRange)
                aeris.TryPickUp();
            else
                StartApproach(ApproachAerisRoutine(aeris));
        }
        else
        {
            ClearAllSelection();
            selectedAeris = aeris;
        }
    }

    // ── WorldLootItem ─────────────────────────────────────────

    private void HandleLootClick(WorldLootItem loot)
    {
        if (selectedLoot == loot)
        {
            float dist = Vector3.Distance(player.transform.position, loot.transform.position);
            if (dist <= loot.pickupRange)
                loot.TryPickUp();
            else
                StartApproach(ApproachLootRoutine(loot));
        }
        else
        {
            ClearAllSelection();
            selectedLoot = loot;
        }
    }

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
            selectedNode = node;
            TargetPanel.Instance?.ShowNode(node);
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

    // ── Mob / Pet — combat ────────────────────────────────────

    private void HandleCombatEntityClick(Entity entity)
    {
        if (entity == selectedTarget && engagedTarget != entity)
            Engage(entity);   // 2e clic → engage
        else
            Select(entity);   // 1er clic → sélection
    }

    // =========================================================
    // SELECT / ENGAGE
    // =========================================================

    /// <summary>Sélectionne une Entity — outline orange + TargetPanel.</summary>
    public void Select(Entity entity)
    {
        if (entity == null) return;
        ClearAllSelection();

        selectedTarget  = entity;
        selectedOutline = entity.GetComponent<Outline>()
                       ?? entity.gameObject.AddComponent<Outline>();
        selectedOutline.OutlineColor = colorSelected;
        selectedOutline.OutlineWidth = 4f;
        selectedOutline.enabled      = true;

        TargetPanel.Instance?.Show(entity);
    }

    /// <summary>Engage une Entity — outline rouge + démarre l'auto-attaque.</summary>
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

    /// <summary>Sélectionne ET engage immédiatement (depuis un skill qui initie le combat).</summary>
    public void EngageFromSkill(Entity entity)
    {
        Select(entity);
        Engage(entity);
    }

    // =========================================================
    // EXÉCUTION DE SKILL — point d'entrée depuis SkillBar
    // =========================================================

    /// <summary>
    /// Résout la target selon skill.targetType puis appelle SkillSystem.Execute().
    ///
    /// Self / AoE_Self                          → target null
    /// GroundTarget                             → point au sol, target null
    /// Direction / Skillshot / Cone /
    ///   Dash_Direction                         → direction forward/souris, target null
    /// Target / AoE_Target / Dash_Target /
    ///   LineTarget                             → engagedTarget ?? selectedTarget
    /// </summary>
    public void TryExecuteSkill(SkillData skill)
    {
        if (skill == null || SkillSystem.Instance == null || player == null) return;

        switch (skill.targetType)
        {
            // ── Sans target Entity ────────────────────────────
            case TargetType.Self:
            case TargetType.AoE_Self:
                SkillSystem.Instance.Execute(skill, player, null);
                return;

            // ── Point au sol ──────────────────────────────────
            case TargetType.GroundTarget:
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    SkillSystem.Instance.SetGroundTargetPoint(hit.point);
                    SkillSystem.Instance.Execute(skill, player, null);
                }
                else
                {
                    Debug.LogWarning($"[TARGETING] {skill.skillName} (GroundTarget) : aucune surface détectée.");
                }
                return;
            }

            // ── Direction libre (projectile large, zone directionnelle) ──
            case TargetType.Direction:
            {
                Vector3 dir = ResolveDirection();
                SkillSystem.Instance.SetSkillDirection(dir);
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            // ── Skillshot — projectile en ligne droite, frappe la première cible ──
            case TargetType.Skillshot:
            {
                Vector3 dir = ResolveDirection();
                SkillSystem.Instance.SetSkillDirection(dir);
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            // ── Cône — éventail devant le joueur ──────────────
            case TargetType.Cone:
            {
                Vector3 dir = ResolveDirection();
                SkillSystem.Instance.SetSkillDirection(dir);
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            // ── Dash directionnel — dash sans cible requise ───
            case TargetType.Dash_Direction:
            {
                Vector3 dir = ResolveDirection();
                SkillSystem.Instance.SetSkillDirection(dir);
                SkillSystem.Instance.Execute(skill, player, null);
                return;
            }

            // ── Target Entity requise ─────────────────────────
            case TargetType.Target:
            case TargetType.AoE_Target:
            case TargetType.Dash_Target:
            case TargetType.LineTarget:
            {
                Entity target = engagedTarget ?? selectedTarget;
                if (target == null || target.isDead)
                {
                    Debug.LogWarning($"[TARGETING] {skill.skillName} ({skill.targetType}) : aucune cible valide.");
                    return;
                }
                SkillSystem.Instance.Execute(skill, player, target);
                return;
            }

            default:
                // Fallback générique avec target si disponible
                SkillSystem.Instance.Execute(skill, player, engagedTarget ?? selectedTarget);
                return;
        }
    }

    // =========================================================
    // HELPERS DIRECTION
    // =========================================================

    /// <summary>
    /// Résout la direction du prochain skill directionnel.
    /// Si une cible est sélectionnée/engagée → vers elle.
    /// Sinon → rayon depuis la caméra vers la souris au sol.
    /// Fallback → regard du joueur.
    /// </summary>
    private Vector3 ResolveDirection()
    {
        // Priorité 1 : vers la cible engagée ou sélectionnée
        Entity aim = engagedTarget ?? selectedTarget;
        if (aim != null)
            return (aim.transform.position - player.transform.position).normalized;

        // Priorité 2 : direction vers le pointeur souris au sol
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            Vector3 toMouse = hit.point - player.transform.position;
            toMouse.y = 0f;
            if (toMouse.sqrMagnitude > 0.001f)
                return toMouse.normalized;
        }

        // Fallback : regard du joueur
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

    private void StopApproach()
    {
        if (_approachCoroutine != null)
        {
            StopCoroutine(_approachCoroutine);
            _approachCoroutine = null;
        }
    }

    /// <summary>Annule l'approche dès que le joueur prend le contrôle manuel.</summary>
    private void CheckApproachCancelled()
    {
        if (_approachCoroutine != null && GameControls.MoveHeld)
            StopApproach();
    }

    // ── Routine PNJ ───────────────────────────────────────────

    private IEnumerator ApproachPNJRoutine(PNJ pnj)
    {
        NavMeshAgent agent = player.GetComponent<NavMeshAgent>();
        if (agent == null) yield break;

        agent.SetDestination(pnj.transform.position);

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (pnj == null || pnj.isDead) yield break;
            if (Vector3.Distance(player.transform.position, pnj.transform.position) <= pnj.interactionRadius)
            {
                agent.ResetPath();
                pnj.Interact(player);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        agent.ResetPath();
        _approachCoroutine = null;
    }

    // ── Routine ResourceNode ──────────────────────────────────

    private IEnumerator ApproachNodeRoutine(ResourceNode node)
    {
        NavMeshAgent agent = player.GetComponent<NavMeshAgent>();
        if (agent == null) yield break;

        float radius = node.data?.interactionRadius ?? 2.5f;
        agent.SetDestination(node.transform.position);

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (node == null || !node.gameObject.activeSelf) yield break;
            agent.SetDestination(node.transform.position);

            if (Vector3.Distance(player.transform.position, node.transform.position) <= radius)
            {
                agent.ResetPath();
                node.BeginCollect(player);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        agent.ResetPath();
        _approachCoroutine = null;
    }

    // ── Routine WorldLootItem ─────────────────────────────────

    private IEnumerator ApproachLootRoutine(WorldLootItem loot)
    {
        NavMeshAgent agent = player.GetComponent<NavMeshAgent>();
        if (agent == null) yield break;

        agent.SetDestination(loot.transform.position);

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (loot == null || !loot.gameObject.activeSelf) yield break;
            if (Vector3.Distance(player.transform.position, loot.transform.position) <= loot.pickupRange)
            {
                agent.ResetPath();
                loot.TryPickUp();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        agent.ResetPath();
        _approachCoroutine = null;
    }

    // ── Routine WorldAerisItem ────────────────────────────────

    private IEnumerator ApproachAerisRoutine(WorldAerisItem aeris)
    {
        NavMeshAgent agent = player.GetComponent<NavMeshAgent>();
        if (agent == null) yield break;

        agent.SetDestination(aeris.transform.position);

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (aeris == null || !aeris.gameObject.activeSelf) yield break;
            if (Vector3.Distance(player.transform.position, aeris.transform.position) <= aeris.pickupRange)
            {
                agent.ResetPath();
                aeris.TryPickUp();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        agent.ResetPath();
        _approachCoroutine = null;
    }

    // =========================================================
    // DESELECT
    // =========================================================

    /// <summary>Réinitialise toute sélection et stoppe l'auto-attaque.</summary>
    public void Deselect()
    {
        ClearAllSelection();
        ClearEngage();
        StopApproach();
        TargetPanel.Instance?.Hide();
    }

    // =========================================================
    // HELPERS INTERNES
    // =========================================================

    private void ClearAllSelection()
    {
        if (selectedOutline != null) { selectedOutline.enabled = false; selectedOutline = null; }
        selectedTarget = null;
        selectedNode   = null;
        selectedLoot   = null;
        selectedAeris  = null;
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

    public Entity         GetEngagedTarget()  => engagedTarget;
    public Entity         GetSelectedTarget() => selectedTarget;
    public ResourceNode   GetSelectedNode()   => selectedNode;
    public WorldLootItem  GetSelectedLoot()   => selectedLoot;
    public WorldAerisItem GetSelectedAeris()  => selectedAeris;
    public bool           IsAutoAttacking     => autoAttacking;
}
