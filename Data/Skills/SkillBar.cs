using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
// SKILLBAR.CS — Barre de sorts runtime
// Path : Assets/Scripts/Core/SkillBar.cs
// AetherTree GDD v3.5 — Section 8.1 / §8.7
//
// Structure slots (§8.1) :
//   Slot 0     : BasicAttack — GCD = skill.cooldown (propre à l'arme)
//   Slots 1-8  : Actifs — soumis au GCD global de 1s + cooldown individuel
//   Slot 9     : Ultime — soumis au GCD global + cooldown ultime
//   P1, P2, P3 : Passifs utilitaires — proc automatique, pas de GCD (backlog §44)
//
// GCD (§8.7) :
//   Slot 0 → pas de GCD global, cooldown = skill.cooldown de la BasicAttack équipée.
//   Slots 1-9 → GCD_DURATION (1s) déclenché après chaque cast actif.
//   _basicAttackLockTimer SUPPRIMÉ — le slot 0 est naturellement protégé
//   par _cooldownTimers[0] qui est posé à skill.cooldown lors du cast.
//
// Calls SkillSystem.Execute(skill, caster, target) — target peut être null
//   pour Self, AoE_Self, GroundTarget, Direction, Skillshot, LineTarget, Cone.
//
// ⚠ TODO §8.1 (backlog §44) : slots passifs P1/P2/P3 manquants
//   Passifs utilitaires : proc via condition (HP < 30%, crit...) — §8.6
//   À implémenter : _passiveSlots[3], TryProcPassives(), SetPassiveAtSlot()
//
// ⚠ TODO §44 : Slot 0 protégé contre le drag & drop
//   Bloquer drag & drop sur slot 0 côté UI (SkillBarUI)
// =============================================================

public class SkillBar : MonoBehaviour
{
    public static SkillBar Instance { get; private set; }

    // Slots 0-8 = actifs, slot 9 = ultime
    private SkillData[] _slots          = new SkillData[10];
    private float[]     _cooldownTimers = new float[10];

    // ── GCD §8.7 ──────────────────────────────────────────────
    // GCD de 1s sur slots 1-9 (actifs + ultime), déclenché par tout skill actif.
    // Slot 0 (BasicAttack) utilise uniquement son propre _cooldownTimers[0].
    private const float GCD_DURATION = 1f;
    private float       _gcdTimer    = 0f;

    // Lock total tous les slots pendant un MultiHit (coroutine en cours).
    // Durée = somme des delays du hitSteps. Alimenté par LockForMultiHit().
    private float _multiHitLockTimer = 0f;

    private Player          _player;
    private ElementalSystem _elemental;
    private NavMeshAgent    _agent;

    // Auto-approche
    private SkillData _pendingSkill;
    private Entity    _pendingTarget;
    private int       _pendingSlot   = -1;
    private bool      _isApproaching = false;

    // ── Combo séquentiel (Méthode 2) ──────────────────────────
    // Un seul combo actif à la fois — le slot qui a initié le combo.
    // _comboStep    : index du prochain step à exécuter (0 = pas de combo actif)
    // _comboSlot    : slot SkillBar qui porte le combo en cours (-1 = aucun)
    // _comboTimer   : temps restant avant expiration de la fenêtre
    // _comboSkill   : le SkillData racine du combo (pour accéder aux comboSteps)
    private int       _comboStep    = 0;
    private int       _comboSlot    = -1;
    private float     _comboTimer   = 0f;
    private SkillData _comboSkill   = null;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player    = FindObjectOfType<Player>();
        _elemental = _player?.GetComponent<ElementalSystem>();
        _agent     = _player?.GetComponent<NavMeshAgent>();
    }

    private void Update()
    {
        // Cooldowns individuels
        for (int i = 0; i < 10; i++)
            if (_cooldownTimers[i] > 0f)
                _cooldownTimers[i] -= Time.deltaTime;

        // GCD — slots 1-9 uniquement
        if (_gcdTimer > 0f)
            _gcdTimer -= Time.deltaTime;

        // Lock total pendant un MultiHit
        if (_multiHitLockTimer > 0f)
            _multiHitLockTimer -= Time.deltaTime;

        // ── Timer combo séquentiel ────────────────────────────
        if (_comboSlot >= 0 && _comboTimer > 0f)
        {
            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f)
            {
                // Fenêtre expirée — CD déclenché + reset
                Debug.Log($"[SKILLBAR] Combo expiré sur slot {_comboSlot} — CD déclenché.");
                _cooldownTimers[_comboSlot] = _comboSkill != null ? _comboSkill.cooldown : 1f;
                ResetCombo();
            }
        }

        // Annulation approche
        if (_isApproaching)
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelApproach();
                return;
            }
            CheckApproach();
        }

        // Input slots
        int slot = GameControls.GetSkillSlotPressed();
        if (slot >= 0) TryUseSlot(slot);
    }

    // ── Gestion des slots ─────────────────────────────────────
    public void SetSkillAtSlot(int slot, SkillData skill)
    {
        if (slot < 0 || slot >= 10) return;
        _slots[slot] = skill;
        // Notifie immédiatement l'UI — évite la race condition avec
        // SetStartingSkillBar (coroutine frame+1) vs SkillBarUI.Start() (frame 1)
        SkillBarUI.Instance?.RefreshSlot(slot);
    }

    public SkillData GetSkillAtSlot(int slot)
    {
        if (slot < 0 || slot >= 10) return null;
        return _slots[slot];
    }

    public bool HasDualElementSkillEquipped()
    {
        for (int i = 0; i < 10; i++)
            if (_slots[i] != null && _slots[i].elements != null && _slots[i].elements.Count >= 2)
                return true;
        return false;
    }

    // ── Utilisation ───────────────────────────────────────────
    public bool TryUseSlot(int slot)
    {
        if (slot < 0 || slot >= 10) return false;
        var skill = _slots[slot];
        if (skill == null)   return false;
        if (_player == null) return false;

        // ── Vérification status effects bloquants ────────────
        var fx = _player.statusEffects;
        if (fx != null)
        {
            // Stun — bloque toutes les actions (GDD §21bis.1)
            if (fx.isStunned)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Stun actif");
                return false;
            }
            // Silence — bloque les skills mais pas l'attaque de base (slot 0)
            if (fx.isSilenced && slot != 0)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Silence actif");
                return false;
            }
        }

        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit en cours → tous les slots bloqués sans exception
        if (_multiHitLockTimer > 0f) return false;

        if (slot == 0)
        {
            // Basic attack : cooldown propre à l'arme équipée (skill.cooldown).
            // Pas de GCD global — _cooldownTimers[0] suffit.
            if (_cooldownTimers[0] > 0f) return false;
        }
        else
        {
            // Actifs / ultime : bloqués par leur CD individuel ET par le GCD global.
            if (_cooldownTimers[slot] > 0f) return false;
            if (_gcdTimer > 0f)             return false;
        }

        // Vérification mana
        if (_player.CurrentMana < skill.manaCost)
        {
            Debug.Log($"[SKILLBAR] Mana insuffisante pour {skill.skillName}");
            return false;
        }

        // Récupère la cible courante
        Entity target = TargetingSystem.Instance?.GetEngagedTarget()
                     ?? TargetingSystem.Instance?.GetSelectedTarget();

        // ── Vérification portée pour les skills qui nécessitent une cible ──
        // Inclut tous les TargetType nécessitant une Entity valide au cast.
        bool needsTarget = skill.targetType == TargetType.Target
                        || skill.targetType == TargetType.AoE_Target
                        || skill.targetType == TargetType.Dash_Target
                        || skill.targetType == TargetType.LineTarget;

        if (needsTarget)
        {
            if (target == null)
            {
                Debug.Log($"[SKILLBAR] Aucune cible pour {skill.skillName}");
                return false;
            }

            float dist  = Vector3.Distance(_player.transform.position, target.transform.position);
            float range = skill.range > 0f ? skill.range : GetDefaultRange();

            if (dist > range)
            {
                StartApproach(skill, slot, target);
                return false;
            }
        }

        // ── Combo séquentiel (Méthode 2) ─────────────────────
        if (TryAdvanceCombo(skill, slot, target)) return true;

        ExecuteSkill(skill, slot, target);
        return true;
    }

    // ── Reset combo ───────────────────────────────────────────
    private void ResetCombo()
    {
        // Refresh AVANT de remettre _comboSlot à -1
        if (_comboSlot >= 0) SkillBarUI.Instance?.RefreshSlot(_comboSlot);
        _comboStep  = 0;
        _comboSlot  = -1;
        _comboTimer = 0f;
        _comboSkill = null;
    }

    // ── Exécution combo step ──────────────────────────────────
    /// <summary>
    /// Gère l'avancement du combo séquentiel.
    /// Retourne true si le combo a pris en charge l'appui (pas d'exécution normale).
    /// Step 0 = skill parent lui-même, steps suivants = comboSteps[].
    /// </summary>
    private bool TryAdvanceCombo(SkillData skill, int slot, Entity target)
    {
        if (skill.executionType != SkillExecutionType.ComboSequence) return false;
        if (skill.comboSteps == null || skill.comboSteps.Count == 0) return false;

        if (_comboSlot == -1)
        {
            // Premier appui — exécute le skill PARENT (step 0)
            _comboSkill = skill;
            _comboSlot  = slot;
            _comboStep  = 1; // prochain appui = comboSteps[0]

            _player.SpendMana(skill.manaCost);
            SkillSystem.Instance?.Execute(skill, _player, target);
            if (target != null) TargetingSystem.Instance?.EngageFromSkill(target);

            // Ouvre la fenêtre combo — aucun lock sur les autres slots
            _comboTimer = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;

            // Icône → montre le prochain step
            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[0]);
            Debug.Log($"[SKILLBAR] Combo démarré — step 0 (parent), fenêtre {_comboTimer}s");
            return true;
        }

        if (_comboSlot != slot)
        {
            // Appui sur un autre slot pendant un combo — ignore
            return false;
        }

        // Steps suivants — comboSteps[_comboStep - 1]
        int stepIndex = _comboStep - 1;
        SkillData stepSkill = _comboSkill.comboSteps[stepIndex];
        if (stepSkill == null) { ResetCombo(); return true; }

        _player.SpendMana(stepSkill.manaCost);
        SkillSystem.Instance?.Execute(stepSkill, _player, target);
        if (target != null) TargetingSystem.Instance?.EngageFromSkill(target);

        _comboStep++;

        if (_comboStep > _comboSkill.comboSteps.Count)
        {
            // Dernier step complété — CD sur le slot + reset
            Debug.Log($"[SKILLBAR] Combo terminé sur slot {slot}.");
            _cooldownTimers[slot] = _comboSkill.cooldown;
            if (slot >= 1) _gcdTimer = GCD_DURATION;
            ResetCombo();
            SkillBarUI.Instance?.RefreshSlot(slot);
        }
        else
        {
            // Ouvre la fenêtre pour le prochain step — aucun lock sur les autres slots
            _comboTimer = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;

            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[_comboStep - 1]);
            Debug.Log($"[SKILLBAR] Combo step {_comboStep}/{_comboSkill.comboSteps.Count} — fenêtre {_comboTimer}s");
        }

        return true;
    }

    // ── Auto-approche ─────────────────────────────────────────
    private void StartApproach(SkillData skill, int slot, Entity target)
    {
        _pendingSkill  = skill;
        _pendingSlot   = slot;
        _pendingTarget = target;
        _isApproaching = true;

        if (_agent != null)
            _agent.SetDestination(target.transform.position);
    }

    private void CheckApproach()
    {
        if (_pendingTarget == null || _pendingTarget.isDead)
        {
            CancelApproach();
            return;
        }

        float dist  = Vector3.Distance(_player.transform.position, _pendingTarget.transform.position);
        float range = _pendingSkill.range > 0f ? _pendingSkill.range : GetDefaultRange();

        if (dist <= range)
        {
            if (_agent != null) _agent.ResetPath();
            ExecuteSkill(_pendingSkill, _pendingSlot, _pendingTarget);
            CancelApproach();
        }
        else
        {
            if (_agent != null) _agent.SetDestination(_pendingTarget.transform.position);
        }
    }

    // ── Exécution ─────────────────────────────────────────────
    private void ExecuteSkill(SkillData skill, int slot, Entity target)
    {
        _player.SpendMana(skill.manaCost);
        // NOTE : Ne PAS appeler player.UseSkill() ici.
        // SkillSystem.Execute() → player.UseSkill() s'en charge.
        // Double appel = RegisterCast() élémentaire × 2 → affinité doublée.
        _cooldownTimers[slot] = skill.cooldown;

        // ── GCD §8.7 ──────────────────────────────────────────
        // Un actif ou l'ultime (slots 1-9) déclenche le GCD global sur tous les slots 1-9.
        // Le slot 0 (BasicAttack) ne déclenche PAS de GCD — son CD vient de skill.cooldown.
        if (slot >= 1)
            _gcdTimer = GCD_DURATION;

        // Engage la cible dans TargetingSystem (outline rouge, auto-attaque).
        // Uniquement pour les skills qui ciblent une Entity.
        if (target != null
            && skill.targetType != TargetType.Self
            && skill.targetType != TargetType.AoE_Self
            && skill.targetType != TargetType.GroundTarget
            && skill.targetType != TargetType.Direction
            && skill.targetType != TargetType.Skillshot
            && skill.targetType != TargetType.Cone)
        {
            TargetingSystem.Instance?.EngageFromSkill(target);
        }

        // GroundTarget — passe par TargetingSystem.TryExecuteSkill pour
        // le raycast sol au moment du cast (lancer rapide, position curseur).
        // Tous les autres targetTypes passent directement par SkillSystem.Execute.
        if (skill.targetType == TargetType.GroundTarget)
            TargetingSystem.Instance?.TryExecuteSkill(skill);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);
    }

    // ── Portée par défaut selon arme ──────────────────────────
    private float GetDefaultRange()
    {
        if (_player == null) return 2.5f;
        return _player.weaponCategory == WeaponCategory.Ranged ? 10f : 2.5f;
    }

    // ── Utilitaires cooldown ──────────────────────────────────
    public float GetCooldownRatio(int slot)
    {
        if (slot < 0 || slot >= 10 || _slots[slot] == null) return 0f;
        float cd = _slots[slot].cooldown;
        return cd > 0f ? Mathf.Clamp01(_cooldownTimers[slot] / cd) : 0f;
    }

    public float GetCooldownRemaining(int slot)
    {
        if (slot < 0 || slot >= 10) return 0f;
        // Slot 0 : CD individuel seulement (pas de GCD global sur la basic).
        // Slots 1-9 : max entre le CD individuel et le GCD restant.
        float individual = Mathf.Max(0f, _cooldownTimers[slot]);
        if (slot >= 1)
            return Mathf.Max(individual, Mathf.Max(0f, _gcdTimer));
        return individual;
    }

    public float GetCooldownTotal(int slot)
    {
        if (slot < 0 || slot >= 10 || _slots[slot] == null) return 0f;
        // Slots 1-9 : si le GCD est plus long que le CD individuel, on base sur GCD_DURATION.
        // Slot 0 : toujours le cooldown de la BasicAttack équipée.
        if (slot >= 1 && _gcdTimer > _cooldownTimers[slot])
            return GCD_DURATION;
        return _slots[slot].cooldown;
    }

    public void CancelApproach()
    {
        _pendingSkill  = null;
        _pendingTarget = null;
        _pendingSlot   = -1;
        _isApproaching = false;
    }

    /// <summary>
    /// Appelé par SkillSystem avant ExecuteMultiHit.
    /// Bloque tous les slots pendant la durée totale du MultiHit (somme des delays).
    /// </summary>
    public void LockForMultiHit(SkillData skill)
    {
        if (skill?.hitSteps == null || skill.hitSteps.Count == 0) return;
        float total = 0f;
        foreach (var step in skill.hitSteps)
            total += step.delay;
        // Ajoute une petite marge pour couvrir le dernier hit
        total += 0.3f;
        _multiHitLockTimer = total;
        _gcdTimer          = Mathf.Max(_gcdTimer, total); // bloque aussi les actifs
        // Slot 0 bloqué via _multiHitLockTimer — pas de _gcdTimer sur slot 0
        Debug.Log($"[SKILLBAR] MultiHit lock {total:F2}s pour {skill.skillName}");
    }
}
