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
//   P1, P2, P3 : Passifs (PassiveSkillData) — gérés par PassifBarUI/PassiveSkillSystem,
//                hors de cette classe (pas de slots ici, pas de GCD).
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
// ⚠ TODO §44 : Slot 0 protégé contre le drag & drop
//   Bloquer drag & drop sur slot 0 côté UI (SkillBarUI)
// =============================================================

public class SkillBar : MonoBehaviour
{
    public static SkillBar Instance { get; private set; }

    [Header("Chargement forcé")]
    [Tooltip("N'est jamais lu directement — juste glisser l'asset ici pour forcer Unity à le " +
             "charger en mémoire (déclenche son propre OnEnable() → WeaponTypeRegistry.Instance). " +
             "Sans ce champ, rien dans la scène ne référence l'asset, donc il n'est jamais chargé " +
             "et Player.SetStartingSkillBar() timeout après 5s (\"WeaponTypeRegistry introuvable\").")]
    public WeaponTypeRegistry weaponTypeRegistry;

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

    // Bloque le déplacement joueur pendant un MultiHit (immobile le temps du combo).
    // ComboSequence n'utilise PAS ce lock — on peut se déplacer entre deux sorts du combo.
    public bool IsMultiHitLocked => _multiHitLockTimer > 0f;

    // Vrai pendant qu'un skill (slot ≥1) se déplace vers sa cible avant exécution.
    // TargetingSystem s'en sert pour suspendre l'auto-attaque le temps du trajet
    // (on ne tape pas A en marchant pour livrer un sort sur B).
    public bool IsApproachingSkill => _isApproaching && _pendingSlot != 0;

    private Player          _player;
    private ElementalSystem _elemental;
    private NavMeshAgent    _agent;

    // Auto-approche
    private SkillData _pendingSkill;
    private Entity    _pendingTarget;
    // GroundTarget uniquement — point au sol visé, figé au clic (jamais re-raycasté à
    // l'arrivée, même philosophie que _pendingTarget : commis à sa valeur d'origine).
    // Mutuellement exclusif avec _pendingTarget (l'un ou l'autre est set, jamais les deux).
    private Vector3?  _pendingGroundPoint;
    private int       _pendingSlot   = -1;
    private bool      _isApproaching = false;

    // ── Combo séquentiel (Méthode 2) ──────────────────────────
    // Un seul combo actif à la fois — le slot qui a initié le combo.
    // _comboStep    : index du prochain step à exécuter (0 = parent en attente de résolution
    //                 OU pas de combo actif — voir _comboSlot pour distinguer les deux ; il
    //                 existe une brève fenêtre entre le lancement du parent et sa résolution où
    //                 un combo EST actif avec _comboStep == 0)
    // _comboSlot    : slot SkillBar qui porte le combo en cours (-1 = aucun)
    // _comboTimer   : temps restant avant expiration de la fenêtre
    // _comboSkill   : le SkillData racine du combo (pour accéder aux comboSteps)
    private int       _comboStep         = 0;
    private int       _comboSlot         = -1;
    private float     _comboTimer        = 0f;
    private SkillData _comboSkill        = null;
    // Délai minimum entre deux steps (SkillData.comboStepInterval) — empêche de spammer tout
    // le combo en < 1s. Tant que > 0, un appui sur le combo est ignoré (fenêtre _comboTimer
    // continue de tourner normalement, seul le step suivant attend).
    private float     _comboStepCooldown = 0f;

    // ── Canalisation (castTime > 0) ────────────────────────────
    private const float CHANNEL_CANCEL_MOVE_THRESHOLD = 0.3f;   // même seuil que ResourceNode

    private bool      _isChanneling      = false;
    private SkillData _channelSkill      = null;
    private int       _channelSlot       = -1;
    private Entity    _channelTarget     = null;
    private Vector3   _channelStartPos   = Vector3.zero;
    private float     _channelStartTime  = 0f;   // Time.time au lancement — pour l'overlay CD de la SkillBarUI
    // Référence gardée pour pouvoir le détruire si la canalisation est interrompue avant son
    // terme (trouvé lors de l'audit VFX du 2026-09-12 — sans ça, le VFX de cast reste affiché
    // pour toute sa durée configurée même si la canalisation est coupée bien avant).
    private GameObject _channelVfxCast    = null;

    // ── Attente de résolution (Normal / step de Combo castTime 0) — chantier B ────────────
    private int       _pendingHitSlot    = -1;
    private SkillData _pendingHitSkill   = null;
    private Entity    _pendingHitTarget  = null;
    private float     _pendingHitTimeout = 0f;

    public bool IsPendingHit => _pendingHitSlot >= 0;

    // ── Attente de résolution (MultiHit — séquence d'index) — chantier B ──────────────────
    private int       _pendingMultiSlot      = -1;
    private SkillData _pendingMultiSkill     = null;
    private Entity    _pendingMultiTarget    = null;
    private int       _pendingMultiNextIndex = 0;
    private float     _pendingMultiTimeout   = 0f;

    public bool IsPendingMultiHit => _pendingMultiSlot >= 0;

    // ── Verrou pleine durée d'anim (Normal/MultiHit/Combo-step) ────────────────────────────
    // Distinct de IsPendingHit/IsPendingMultiHit : ceux-ci retombent dès la RÉSOLUTION (event/
    // timeout, potentiellement avant la fin du clip si l'event est placé en milieu d'anim).
    // Florian veut qu'un skill reste bloquant (verrou SkillBar + mouvement) jusqu'à la fin
    // RÉELLE de l'anim, pas juste jusqu'à la résolution des dégâts — donc un timer séparé,
    // posé à la durée du clip au lancement, indépendant du moment où le hit résout.
    private float _animLockTimer = 0f;
    public bool IsAnimLocked => _animLockTimer > 0f;

    public bool IsChanneling => _isChanneling;

    /// <summary>True pendant la fenêtre d'attente d'un ComboSequence (entre deux appuis) —
    /// utilisé par TargetingSystem.TickAutoAttack pour ne pas laisser l'auto-attaque se
    /// déclencher pendant qu'un combo attend son prochain step, même pattern que
    /// IsApproachingSkill/IsChanneling.</summary>
    public bool IsComboActive => _comboSlot >= 0;

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
        {
            _multiHitLockTimer -= Time.deltaTime;
        }

        // ── Poll canalisation (CC / Silence / mouvement / cible morte) ──
        if (_isChanneling)
        {
            var fx = _player.statusEffects;
            bool hardCC = _player.isDead || (fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed
                                       || fx.isKnockedBack || fx.isFeared || fx.isSilenced));
            if (hardCC)
            {
                InterruptChannel(voluntary: false, reason: _player.isDead ? "mort" : "CC");
            }
            else if (_channelTarget != null && _channelTarget.isDead)
            {
                InterruptChannel(voluntary: true, reason: "cible morte");
            }
            else
            {
                float moved = Vector3.Distance(_player.transform.position, _channelStartPos);
                if (moved > CHANNEL_CANCEL_MOVE_THRESHOLD)
                    InterruptChannel(voluntary: true, reason: "mouvement");
            }
        }

        // ── Attente de résolution (chantier B) ─────────────────
        if (_pendingHitSlot != -1)
        {
            _pendingHitTimeout -= Time.deltaTime;
            if (_pendingHitTimeout <= 0f) ResolveInstant();
        }
        if (_pendingMultiSlot != -1)
        {
            _pendingMultiTimeout -= Time.deltaTime;
            if (_pendingMultiTimeout <= 0f) ResolveMultiHitIndex(_pendingMultiNextIndex);
        }

        if (_animLockTimer > 0f)
            _animLockTimer -= Time.deltaTime;

        if (_comboStepCooldown > 0f)
            _comboStepCooldown -= Time.deltaTime;

        // ── Timer combo séquentiel ────────────────────────────
        // Gelé tant que le coup courant de CE combo est en attente de résolution (chantier B)
        // — sinon un appui fait juste avant l'expiration voit son combo expirer/reset PENDANT
        // que son propre coup joue encore, et ResolveInstant() repost un CD non pertinent
        // par-dessus quand le coup résout (le combo n'est plus "actif" à ce moment-là). Un CC
        // dur qui atterrit pendant ce même coup ne doit pas non plus interrompre un combo dont
        // le coup en cours va de toute façon résoudre (même règle de non-interruptibilité que
        // Normal/MultiHit une fois lancés).
        bool comboHitPending = IsPendingHit && _pendingHitSlot == _comboSlot;
        if (_comboSlot >= 0 && _comboTimer > 0f && !comboHitPending)
        {
            var fx = _player.statusEffects;
            bool hardCC = fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed
                                       || fx.isKnockedBack || fx.isFeared);
            // Silence volontairement EXCLU ici — un combo castTime 0 n'est pas une
            // canalisation ; Silence bloque déjà les NOUVEAUX lancements via TryUseSlot,
            // mais n'a jamais interrompu une fenêtre d'attente ouverte avant ce plan.

            _comboTimer -= Time.deltaTime;
            if (_comboTimer <= 0f || hardCC)
            {
                Debug.Log($"[SKILLBAR] Combo {(hardCC ? "interrompu (CC)" : "expiré")} sur slot {_comboSlot} — CD déclenché.");
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
    public bool TryUseSlot(int slot, bool isAutoTick = false)
    {
        if (slot < 0 || slot >= 10) return false;
        var skill = _slots[slot];
        if (skill == null)   return false;
        if (_player == null) return false;

        // Appui manuel sur le slot 0 (pas le tick auto-attaque) alors qu'on est déjà
        // engagé sur A avec B sélectionné : bascule l'engagement vers B tout de suite,
        // comme un double-clic — INDÉPENDANT du cooldown/GCD ci-dessous, sinon la
        // bascule échoue silencieusement la majeure partie du temps (cooldown occupé
        // ~80% du cycle d'attaque). Le prochain coup réel reste cadencé normalement ;
        // TickAutoAttack se charge de la chase/attaque une fois engagedTarget=B.
        if (slot == 0 && !isAutoTick)
        {
            Entity liveSelected = TargetingSystem.Instance?.GetSelectedTarget();
            Entity liveEngaged  = TargetingSystem.Instance?.GetEngagedTarget();
            if (liveEngaged != null && liveSelected != null && liveSelected != liveEngaged && !liveSelected.isDead)
            {
                TargetingSystem.Instance.Engage(liveSelected);
            }
        }

        // ── Vérification status effects bloquants ────────────
        var fx = _player.statusEffects;
        if (fx != null)
        {
            // Stun, Shocked (identique à Stun), Freeze ou Knockback (mini-stun ponctuel) — bloque toutes les actions (GDD §21bis.1)
            if (fx.isStunned || fx.isShocked || fx.isFreezed || fx.isKnockedBack)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Stun/Shocked/Freeze/Knockback actif");
                return false;
            }
            // Fear — fuite incontrôlée, bloque toutes les actions (CC dur au même titre que Stun)
            if (fx.isFeared)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Fear actif");
                return false;
            }
            // Silence — bloque les skills mais pas l'attaque de base (slot 0)
            if (fx.isSilenced && slot != 0)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Silence actif");
                return false;
            }
            // Taunt — bloque les skills, force l'attaque de base (slot 0) uniquement sur la
            // source du taunt (§3.1.1.1) — voir override de cible plus bas.
            if (fx.isTaunted && slot != 0)
            {
                Debug.Log("[SKILLBAR] ❌ Bloqué — Taunt actif");
                return false;
            }
        }

        // ── Vérification GCD & locks ──────────────────────────
        // MultiHit, canalisation, ou anim d'un skill en cours (chantier B) → tous les slots
        // bloqués sans exception. IsAnimLocked (pas IsPendingHit/IsPendingMultiHit) : Florian
        // veut qu'aucun nouveau skill ne puisse se lancer avant que l'anim COMPLÈTE du
        // précédent soit terminée, pas juste sa résolution (qui peut tomber en milieu de
        // clip selon où l'Animation Event est placé) — sinon un 2e skill écraserait le clip
        // en cours en plein follow-through, et l'event du 1er (s'il n'était pas encore tombé)
        // ne se déclencherait jamais.
        if (_multiHitLockTimer > 0f || _isChanneling || IsAnimLocked)
        {
            return false;
        }

        if (slot == 0)
        {
            // Basic attack : cooldown propre à l'arme équipée (skill.cooldown).
            // Pas de GCD global — _cooldownTimers[0] suffit.
            if (_cooldownTimers[0] > 0f)
            {
                return false;
            }
        }
        else
        {
            // Actifs / ultime : bloqués par leur CD individuel ET par le GCD global.
            if (_cooldownTimers[slot] > 0f) return false;
            if (_gcdTimer > 0f)             return false;
        }

        // Vérification mana
        if (_player.CurrentMana < GetEffectiveManaCost(skill))
        {
            Debug.Log($"[SKILLBAR] Mana insuffisante pour {skill.name}");
            return false;
        }

        // Vérification HP — même convention que mana : bloqué si pas assez de marge,
        // jamais de mort déclenchée par le coût d'un skill (<=, pas <, pour ne jamais
        // autoriser de retomber exactement à 0).
        if (skill.hpCost > 0f && _player.CurrentHP <= skill.hpCost)
        {
            Debug.Log($"[SKILLBAR] HP insuffisants pour {skill.name}");
            return false;
        }

        // Vérification Aeris
        if (skill.goldCost > 0 && (AerisSystem.Instance == null || AerisSystem.Instance.Aeris < skill.goldCost))
        {
            Debug.Log($"[SKILLBAR] Aeris insuffisant pour {skill.name}");
            return false;
        }

        // Récupère la cible courante — le tick auto-attaque continu (isAutoTick,
        // appelé par TargetingSystem.PerformAutoAttack) reste strictement sur
        // l'engagée (rouge) tant qu'elle existe, sans dévier vers une simple
        // sélection. Tout appui EXPLICITE (clavier, clic bouton, y compris slot 0)
        // vise en priorité ce qui est sélectionné (orange) — comme le double-clic —
        // et ne retombe sur l'engagée que si rien n'est sélectionné. C'est ce cast
        // qui promeut ensuite la cible visée en nouvelle engagée via Engage()/
        // EngageFromSkill() plus bas.
        Entity target = isAutoTick
            ? TargetingSystem.Instance?.GetEngagedTarget()  ?? TargetingSystem.Instance?.GetSelectedTarget()
            : TargetingSystem.Instance?.GetSelectedTarget() ?? TargetingSystem.Instance?.GetEngagedTarget();

        // Taunt actif : à ce point slot == 0 forcément (skills déjà bloqués plus haut) — force
        // la cible de l'attaque de base sur la source du taunt, peu importe la sélection/
        // l'engagée du joueur (§3.1.1.1). Fallback sur la cible normale si la source est
        // morte/introuvable — même garde-fou que Fear (PlayerController.HandleMovement).
        if (fx != null && fx.isTaunted)
        {
            Entity tauntSource = fx.GetDebuffSource(DebuffType.Taunt);
            if (tauntSource != null && !tauntSource.isDead) target = tauntSource;
        }

        // Un nouvel appui valide (passé les checks CD/mana/stun ci-dessus) sur le
        // MÊME slot que l'approche en cours, pour un skill/cible différent, annule
        // cette dernière — sinon elle continue de tirer l'agent vers l'ancienne
        // cible en arrière-plan. Restreint à `_pendingSlot == slot` : l'auto-attaque
        // (slot 0) tourne en continu (~1x/s) pendant qu'un skill (slot ≥ 1) est en
        // train d'approcher sa cible — sans ce garde-fou, CHAQUE tic d'auto-attaque
        // annulait l'approche du skill avant qu'elle n'ait une chance d'arriver à
        // portée (le skill ne partait donc jamais, l'auto-attaque semblant "forcer"
        // à taper l'engagée). Un skill sur un autre slot ne doit annuler que SA
        // PROPRE approche précédente, jamais celle d'un slot différent.
        // GroundTarget n'a pas de cible Entity — un nouveau clic sur ce slot doit toujours
        // annuler une approche vers un ancien point au sol (le nouveau point n'est connu
        // qu'après ce check, il ne peut jamais être "égal" à l'ancien ici).
        if (_isApproaching && _pendingSlot == slot &&
            (_pendingSkill != skill || _pendingTarget != target || _pendingGroundPoint.HasValue))
        {
            CancelApproach();
        }

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
                Debug.Log($"[SKILLBAR] Aucune cible pour {skill.name}");
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
        else if (skill.targetType == TargetType.GroundTarget)
        {
            // Point figé ICI, au clic — jamais re-raycasté à la résolution ni à l'arrivée
            // d'une approche (StartInstant()/StartMultiHit()/StartChannel()/LaunchComboHit()
            // consomment désormais ce point déjà posé, voir plus bas — même philosophie que
            // les skills à cible Entity : commis à sa valeur d'origine, jamais réévalué en
            // route).
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                Debug.Log($"[SKILLBAR] Raycast sol manqué pour {skill.name}");
                return false;
            }

            float dist  = Vector3.Distance(_player.transform.position, hit.point);
            float range = skill.range > 0f ? skill.range : GetDefaultRange();

            if (dist > range)
            {
                StartGroundApproach(skill, slot, hit.point);
                return false;
            }

            SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

        // ── Combo séquentiel (Méthode 2) ─────────────────────
        if (TryAdvanceCombo(skill, slot, target)) return true;

        LaunchSkill(skill, slot, target);
        return true;
    }

    // ── Reset combo ───────────────────────────────────────────
    private void ResetCombo()
    {
        // Refresh AVANT de remettre _comboSlot à -1
        if (_comboSlot >= 0) SkillBarUI.Instance?.RefreshSlot(_comboSlot);
        _comboStep         = 0;
        _comboSlot         = -1;
        _comboTimer        = 0f;
        _comboSkill        = null;
        _comboStepCooldown = 0f;
    }

    // ── Canalisation ──────────────────────────────────────────

    // ── Dispatch castTime 0 vs canalisation — UNIQUE point d'entrée pour lancer un skill ──
    // Utilisé par TryUseSlot() ET CheckApproach() — ne jamais appeler ExecuteSkill()
    // directement depuis un autre endroit, sinon castTime > 0 serait contourné.
    private void LaunchSkill(SkillData skill, int slot, Entity target)
    {
        if (skill.castTime > 0f)
            StartChannel(skill, slot, target);
        else if (skill.executionType == SkillExecutionType.MultiHit
                 && skill.hitSteps != null && skill.hitSteps.Count > 0)
            StartMultiHit(skill, slot, target);
        else
            StartInstant(skill, slot, target);
    }

    // ── Normal / Combo-step (chantier B) ───────────────────────
    private void StartInstant(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        EngageAndFaceTarget(skill, slot, target);

        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        // GroundTarget : _groundTargetPoint est déjà posé par TryUseSlot()/CheckApproach()
        // avant cet appel (point figé au clic, jamais re-raycasté ici — voir StartApproach/
        // StartGroundApproach ci-dessous).

        _pendingHitSlot    = slot;
        _pendingHitSkill   = skill;
        // GroundTarget n'a jamais de vraie cible Entity — même correction que StartChannel()
        // (chantier A) : sinon ResolveExecute()/le placement VFX utiliserait la position d'une
        // entité non-pertinente au lieu du point au sol.
        _pendingHitTarget  = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingHitTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;
        _animLockTimer     = _pendingHitTimeout;

        if (_pendingHitTimeout <= 0f)
            ResolveInstant();   // pas d'anim → rien à attendre, résout tout de suite
    }

    private void ResolveInstant()
    {
        if (_pendingHitSlot == -1) return;   // déjà résolu (event ET timeout se sont chevauchés)

        SkillData skill  = _pendingHitSkill;
        int       slot   = _pendingHitSlot;
        Entity    target = _pendingHitTarget;

        _pendingHitSlot    = -1;
        _pendingHitSkill   = null;
        _pendingHitTarget  = null;
        _pendingHitTimeout = 0f;

        // Combo step : TOUJOURS résolution immédiate, JAMAIS de zone différée — évalué en
        // PREMIER, avant tout branchement hasDelayedImpact. C'est la vraie protection contre
        // un step de combo configuré avec hasDelayedImpact = true (le warning OnValidate seul
        // ne peut pas détecter ce cas — voir Tâche 1). La suite (fenêtre suivante, ou fin de
        // combo + CD) est gérée par AdvanceComboAfterHit ; le CD n'est PAS posé ici dans ce cas
        // (seul le DERNIER step d'un combo pose le CD, inchangé).
        if (IsComboActive && slot == _comboSlot)
        {
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);
            AdvanceComboAfterHit(slot, skill);
            return;
        }

        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }

    // ── MultiHit (chantier B) ──────────────────────────────────
    private void StartMultiHit(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        EngageAndFaceTarget(skill, slot, target);

        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        // GroundTarget : _groundTargetPoint déjà posé par TryUseSlot()/CheckApproach() avant
        // cet appel — point figé au clic, jamais re-raycasté ici.

        _pendingMultiSlot      = slot;
        _pendingMultiSkill     = skill;
        // GroundTarget n'a jamais de vraie cible Entity — même raison que StartInstant() :
        // sinon ResolveMultiHitStep()/le placement VFX par step utiliserait la position d'une
        // entité non-pertinente au lieu du point au sol.
        _pendingMultiTarget    = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingMultiNextIndex = 0;
        _pendingMultiTimeout   = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;
        _animLockTimer         = _pendingMultiTimeout;

        if (_pendingMultiTimeout <= 0f)
            ResolveMultiHitIndex(0);   // pas d'anim → résout tout enchaîné immédiatement
    }

    private void ResolveMultiHitIndex(int index)
    {
        if (_pendingMultiSlot == -1) return;
        if (index != _pendingMultiNextIndex)
        {
            // Event mal numéroté sur le clip (Animation window) — le plus probable étant un
            // hitIndex qui démarre à 1 au lieu de 0. Averti explicitement car sinon ce cas est
            // un no-op parfaitement silencieux : la séquence ne progresse plus jusqu'au
            // timeout, qui résout alors tous les coups restants d'un coup, sans que rien
            // n'explique pourquoi dans la Console.
            Debug.LogWarning($"[SKILLBAR] Animation Event MultiHit reçu avec hitIndex={index}, attendu={_pendingMultiNextIndex} — event mal numéroté sur le clip ?");
            return;
        }

        SkillSystem.Instance?.ResolveMultiHitStep(_pendingMultiSkill, _player, _pendingMultiTarget, index);
        _pendingMultiNextIndex++;

        int totalHits = 1 + (_pendingMultiSkill.hitSteps?.Count ?? 0);   // 1 (base) + N hitSteps
        if (_pendingMultiNextIndex >= totalHits)
        {
            int slot = _pendingMultiSlot;
            SkillData skill = _pendingMultiSkill;

            _pendingMultiSlot    = -1;
            _pendingMultiSkill   = null;
            _pendingMultiTarget  = null;
            _pendingMultiTimeout = 0f;

            _cooldownTimers[slot] = skill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                float autoAttackDelay = skill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
        }
        else if (_pendingMultiTimeout <= 0f)
        {
            // Pas d'anim du tout — enchaîne immédiatement le hit suivant plutôt que d'attendre
            // un event qui ne viendra jamais.
            ResolveMultiHitIndex(_pendingMultiNextIndex);
        }
    }

    /// <summary>Reçoit l'Animation Event relayé par PlayerAnimatorController.OnSkillHitFrame.
    /// hitIndex ignoré pour un hit simple (Normal/Combo-step) — un seul en attente possible à
    /// la fois. Pour un MultiHit, route vers l'index précis.</summary>
    public void OnAnimationHitEvent(int hitIndex)
    {
        if (IsPendingHit)      { ResolveInstant(); return; }
        if (IsPendingMultiHit) { ResolveMultiHitIndex(hitIndex); return; }
        // Aucun hit en attente — event reçu hors contexte (anim jouée sans skill en attente,
        // ou déjà résolu par le timeout juste avant). Ignoré silencieusement, pas une erreur.
    }

    private void StartChannel(SkillData skill, int slot, Entity target)
    {
        // Combat/AFK/Stealth doivent réagir au LANCEMENT, pas à la résolution — voir Tâche 2
        // (faille Stealth trouvée en relecture, confirmée par Florian).
        _player.BeginSkillUse(skill);

        // Engage/orientation vers la cible au LANCEMENT, même principe "lancement pas
        // résolution" que BeginSkillUse ci-dessus (relecture Tâche 4, fix round 1).
        EngageAndFaceTarget(skill, slot, target);

        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);

        _isChanneling    = true;
        _channelSkill    = skill;
        _channelSlot     = slot;
        // GroundTarget n'a jamais de vraie cible Entity (targetType.GroundTarget = zone au sol,
        // pas une entité) — même si TryUseSlot a assigné `target` depuis la sélection courante,
        // on le null ici : évite qu'un skill de zone résolve avec une cible non-pertinente
        // (mauvaise position de VFX dans SkillSystem.Execute) et évite un faux-positif du poll
        // d'interrupt "cible morte" si cette entité non-pertinente meurt pendant la canalisation.
        _channelTarget   = skill.targetType == TargetType.GroundTarget ? null : target;
        _channelStartPos = _player.transform.position;
        _channelStartTime = Time.time;

        // Résidu de vélocité NavMeshAgent (ex: hand-off depuis CheckApproach) qui pourrait
        // sinon déclencher immédiatement le poll d'annulation par mouvement dans Update().
        _agent?.ResetPath();

        _player.AnimatorController?.PlayChannel(skill.channelAnimation);

        _channelVfxCast = skill.vfxCast != null
            ? Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity)
            : null;

        // GroundTarget : le point est locké au clic (TryUseSlot()/CheckApproach(), avant cet
        // appel), pas re-raycasté ici — aim-then-channel, cohérent avec mana/HP/gold dépensés
        // au clic. ResolveChannel() n'a besoin d'aucun changement : _groundTargetPoint est déjà
        // posé quand SkillSystem.Execute() le consomme.

        // POINT D'EXTENSION CHANTIER B (calage sur frame d'impact, hors scope de ce plan) :
        // le déclencheur de ResolveChannel() est ICI, et seulement ici. Le jour où B est
        // spécifié, onComplete sera remplacé par un Animation Event posé sur channelAnimation
        // au lieu du timer de la bar — aucun autre code de cette méthode/classe n'aura besoin
        // de changer. Ne jamais coupler ResolveChannel() à autre chose que cet appelant.
        ProgressBarUI.Instance?.StartProgress(
            label:        skill.skillName.Get(LocalizationManager.CurrentLanguage),
            duration:     skill.castTime,
            onComplete:   ResolveChannel,
            onCancel:     () => InterruptChannel(voluntary: true, reason: "bar volée"),
            type:         ProgressBarUI.BarType.Cast,
            followTarget: _player.transform
        );
    }

    private void ResolveChannel()
    {
        if (!_isChanneling) return;   // garde-fou si déjà interrompu entre-temps

        // Capturer AVANT EndChannelState() — celle-ci met _channelTarget à null, et
        // Execute()/PlantDelayedZone() ont besoin de la vraie cible.
        SkillData skill  = _channelSkill;
        int       slot   = _channelSlot;
        Entity    target = _channelTarget;

        EndChannelState();

        if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }
    }

    /// <summary>voluntary = true (mouvement OU cible morte — ni un choix punitif du joueur ni
    /// un CC gagné par l'adversaire, CD moitié) | false (CC/Silence subi, CD complet).</summary>
    private void InterruptChannel(bool voluntary, string reason)
    {
        if (!_isChanneling) return;

        SkillData skill = _channelSkill;
        int       slot  = _channelSlot;

        EndChannelState();

        ProgressBarUI.Instance?.Cancel();
        _player.AnimatorController?.CancelChannel();

        _cooldownTimers[slot] = voluntary ? skill.cooldown * 0.5f : skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }

        Debug.Log($"[SKILLBAR] Canalisation interrompue ({reason}) — CD {_cooldownTimers[slot]:F2}s.");
    }

    private void EndChannelState()
    {
        _isChanneling  = false;
        _channelSkill  = null;
        _channelSlot   = -1;
        _channelTarget = null;

        // Appelée aussi bien par ResolveChannel() (fin normale) que InterruptChannel() (coupée
        // avant terme) — dans les deux cas, la canalisation est terminée, le VFX de cast n'a
        // plus de raison de rester affiché plus longtemps que la canalisation elle-même.
        if (_channelVfxCast != null)
        {
            Destroy(_channelVfxCast);
            _channelVfxCast = null;
        }
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
            // Premier appui — lance le skill PARENT (step 0) comme un hit en attente
            _comboSkill = skill;
            _comboSlot  = slot;
            _comboStep  = 0; // AdvanceComboAfterHit() le portera à 1 quand CE hit (parent) résout

            LaunchComboHit(skill, slot, target);
            Debug.Log($"[SKILLBAR] Combo démarré — step 0 (parent), en attente de résolution.");
            return true;
        }

        if (_comboSlot != slot)
        {
            // Appui sur un autre slot pendant un combo — ignore
            return false;
        }

        // Délai minimum entre deux steps pas encore écoulé — ignore l'appui (inchangé)
        if (_comboStepCooldown > 0f) return true;

        // Garde-fou : _comboStep peut valoir 0 entre le lancement du parent et sa résolution
        // (fenêtre active mais aucun comboSteps[] encore "next") — un appui qui arriverait ici
        // dans cette fenêtre ne doit pas indexer comboSteps[-1]. Ne devrait normalement jamais
        // arriver (TryUseSlot bloque tout nouveau lancement tant qu'un hit est en attente),
        // mais la méthode doit rester sûre sans dépendre d'un verrou posé ailleurs.
        if (_comboStep < 1) return true;

        int stepIndex = _comboStep - 1;
        SkillData stepSkill = _comboSkill.comboSteps[stepIndex];
        if (stepSkill == null) { ResetCombo(); return true; }

        LaunchComboHit(stepSkill, slot, target);
        return true;
    }

    /// <summary>Lance un coup de combo (parent ou step) exactement comme StartInstant — mêmes
    /// champs _pendingHit*, même mécanisme d'attente/event/timeout, MÊME appel à
    /// BeginSkillUse() (sinon aucun combo ne déclencherait plus combat-entry/AFK-clear/
    /// Stealth-break ni ne mettrait à jour lastSkillUsed — régression sur une mécanique déjà
    /// en jeu, pas un détail : aujourd'hui chaque step passe par SkillSystem.Execute() →
    /// player.UseSkill() → BeginSkillUse(), et ResolveSkillUse (Tâche 2) ne l'appelle plus).
    /// ResolveInstant() détecte après coup qu'un combo est actif sur ce slot
    /// (IsComboActive && slot == _comboSlot) et route vers AdvanceComboAfterHit() au lieu de
    /// poser le CD directement.</summary>
    private void LaunchComboHit(SkillData skill, int slot, Entity target)
    {
        _player.BeginSkillUse(skill);
        _player.SpendMana(GetEffectiveManaCost(skill));
        _player.AnimatorController?.PlayAttack(skill.attackAnimation);

        if (skill.vfxCast != null)
            Instantiate(skill.vfxCast, _player.transform.position, Quaternion.identity);

        EngageAndFaceTarget(skill, slot, target);

        // GroundTarget : aucun skill de combo existant n'utilise GroundTarget aujourd'hui, mais
        // rien n'empêche d'en configurer un plus tard — _groundTargetPoint est déjà posé par
        // TryUseSlot()/CheckApproach() avant cet appel, pas re-raycasté ici.

        _pendingHitSlot    = slot;
        _pendingHitSkill   = skill;
        _pendingHitTarget  = skill.targetType == TargetType.GroundTarget ? null : target;
        _pendingHitTimeout = skill.attackAnimation != null ? skill.attackAnimation.length : 0f;
        _animLockTimer     = _pendingHitTimeout;

        if (_pendingHitTimeout <= 0f) ResolveInstant();
    }

    /// <summary>Bookkeeping combo APRÈS résolution d'un coup — fenêtre suivante (icône +
    /// timer) ou fin de combo (CD + reset). `resolvedSkill` = le SkillData du coup qui vient
    /// de résoudre (parent au step 0, sinon le step lui-même) — utilisé pour la durée
    /// d'auto-attack-delay (anim du DERNIER coup, pas du parent).</summary>
    private void AdvanceComboAfterHit(int slot, SkillData resolvedSkill)
    {
        _comboStep++;
        _comboStepCooldown = _comboSkill.comboStepInterval;

        if (_comboStep > _comboSkill.comboSteps.Count)
        {
            Debug.Log($"[SKILLBAR] Combo terminé sur slot {slot}.");
            _cooldownTimers[slot] = _comboSkill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                float autoAttackDelay = resolvedSkill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, resolvedSkill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
            ResetCombo();
            SkillBarUI.Instance?.RefreshSlot(slot);
        }
        else
        {
            _comboTimer = _comboSkill.comboWindowDuration > 0f ? _comboSkill.comboWindowDuration : 2f;
            SkillBarUI.Instance?.RefreshSlotWithSkill(slot, _comboSkill.comboSteps[_comboStep - 1]);
            Debug.Log($"[SKILLBAR] Combo step {_comboStep}/{_comboSkill.comboSteps.Count} — fenêtre {_comboTimer}s");
        }
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

    // GroundTarget uniquement — même mécanisme que StartApproach() mais vers un Vector3 figé
    // au clic plutôt qu'une Entity (voir _pendingGroundPoint).
    private void StartGroundApproach(SkillData skill, int slot, Vector3 point)
    {
        _pendingSkill       = skill;
        _pendingSlot        = slot;
        _pendingGroundPoint = point;
        _isApproaching      = true;

        if (_agent != null)
            _agent.SetDestination(point);
    }

    private void CheckApproach()
    {
        // GroundTarget : le point est fixe (jamais de re-sélection dynamique comme pour une
        // Entity au slot 0 ci-dessous — un point au sol ne peut pas "mourir" ni être remplacé
        // par une nouvelle cible en cours de route).
        if (_pendingGroundPoint.HasValue)
        {
            float distG  = Vector3.Distance(_player.transform.position, _pendingGroundPoint.Value);
            float rangeG = _pendingSkill.range > 0f ? _pendingSkill.range : GetDefaultRange();

            if (distG <= rangeG)
            {
                // Même garde que le cas Entity ci-dessous — laisser l'anim en cours se terminer.
                if (IsAnimLocked) return;

                if (_agent != null) _agent.ResetPath();
                SkillSystem.Instance?.SetGroundTargetPoint(_pendingGroundPoint.Value);
                if (!TryAdvanceCombo(_pendingSkill, _pendingSlot, null))
                    LaunchSkill(_pendingSkill, _pendingSlot, null);
                CancelApproach();
            }
            else
            {
                if (_agent != null) _agent.SetDestination(_pendingGroundPoint.Value);
            }
            return;
        }

        if (_pendingTarget == null || _pendingTarget.isDead)
        {
            CancelApproach();
            return;
        }

        // Slot 0 (auto-attaque manuelle) reste "vivant" pendant le trajet : si la
        // sélection change en route, on rebascule vers la nouvelle cible plutôt que
        // de livrer le coup sur celle figée au moment de l'appui touche — même règle
        // de priorité que la résolution immédiate ci-dessus (TryUseSlot, isAutoTick).
        // Les vrais skills (slot ≥ 1) restent committés à leur cible d'origine, une
        // simple sélection en cours de route ne doit jamais les rediriger.
        if (_pendingSlot == 0)
        {
            Entity liveTarget = TargetingSystem.Instance?.GetSelectedTarget()
                              ?? TargetingSystem.Instance?.GetEngagedTarget();
            if (liveTarget != null && liveTarget != _pendingTarget && !liveTarget.isDead)
            {
                _pendingTarget = liveTarget;
            }
        }

        float dist  = Vector3.Distance(_player.transform.position, _pendingTarget.transform.position);
        float range = _pendingSkill.range > 0f ? _pendingSkill.range : GetDefaultRange();

        if (dist <= range)
        {
            // L'anim COMPLÈTE d'un skill en cours (Normal/MultiHit/Combo-step, chantier B) doit
            // se terminer avant qu'on livre l'approche — sinon LaunchSkill()/TryAdvanceCombo()
            // écraseraient silencieusement _pendingHit*/_pendingMulti* d'un autre skill encore
            // en vol. On NE cancel PAS l'approche : elle réessaiera à la frame suivante une
            // fois l'anim en cours terminée.
            if (IsAnimLocked) return;

            if (_agent != null) _agent.ResetPath();
            // TryAdvanceCombo AVANT LaunchSkill — même pattern que TryUseSlot(). Sans ce check,
            // un combo (ComboSequence) lancé hors de portée sautait toute la logique combo à
            // l'arrivée (jamais entré dans _comboSlot == -1) et s'exécutait comme un skill
            // normal, CD posé immédiatement au lieu d'ouvrir la fenêtre pour le step suivant —
            // bug pré-existant, pas lié à la canalisation.
            if (!TryAdvanceCombo(_pendingSkill, _pendingSlot, _pendingTarget))
                LaunchSkill(_pendingSkill, _pendingSlot, _pendingTarget);
            CancelApproach();
        }
        else
        {
            if (_agent != null) _agent.SetDestination(_pendingTarget.transform.position);
        }
    }

    /// <summary>Coût en mana réel d'un skill après réduction PAR élément (paliers Esprit, etc.
    /// — StatType.ManaCostReductionX, voir Entity.GetManaCostReduction). Utilisé pour le check
    /// de mana disponible ET la dépense réelle, jamais skill.manaCost brut directement.</summary>
    private float GetEffectiveManaCost(SkillData skill)
    {
        float reduction = Mathf.Clamp01(_player.GetManaCostReduction(skill.PrimaryElement));
        return skill.manaCost * (1f - reduction);
    }

    // ── Engage + orientation vers la cible ─────────────────────
    // Extrait d'ExecuteSkill() (fix round 1, Tâche 4) — partagé avec StartChannel() pour que
    // les skills canalisés engagent/s'orientent au LANCEMENT plutôt qu'à la résolution.
    /// <summary>
    /// Engage la cible dans TargetingSystem — uniquement pour les skills qui ciblent une
    /// Entity. Slot 0 (auto-attaque) et slots ≥ 1 (skills) n'ont PAS le même besoin ici :
    ///  - Slot 0 : appelle Engage() SEUL (jamais Select()). Engage() ne touche jamais
    ///    selectedTarget/TargetPanel, donc le rappeler à chaque tic est sans danger pour la
    ///    sélection orange en cours — et c'est OBLIGATOIRE : l'attaque de base peut aussi
    ///    partir d'un appui clavier sans être passée par le flow 2-clics de TargetingSystem,
    ///    auquel cas engagedTarget/autoAttacking ne seraient jamais posés.
    ///  - Slots ≥ 1 : EngageFromSkill() complet (Select + Engage) — un vrai skill doit aussi
    ///    ramener le TargetPanel sur sa cible.
    /// Buff/Debuff n'engagent JAMAIS le combat, même sur Target/AoE_Target/Dash_Target/
    /// LineTarget — un buff n'est jamais hostile, et on peut débuff une cible sans pour
    /// autant l'agresser (l'auto-attaque doit rester un choix explicite du joueur).
    /// Snap immédiat vers la cible — sinon l'anim (auto-attaque ou skill) peut jouer dans le
    /// mauvais sens si le perso n'était pas déjà orienté dessus (rotation instantanée, pas de
    /// lissage, l'action doit partir orientée dès la 1ère frame). Appelé au clic (ExecuteSkill)
    /// pour un skill instant, ou au LANCEMENT d'une canalisation (StartChannel) — jamais à la
    /// résolution, l'engagement/l'orientation doivent être immédiats dans les deux cas.
    /// </summary>
    private void EngageAndFaceTarget(SkillData skill, int slot, Entity target)
    {
        if (target != null
            && skill.targetType != TargetType.Self
            && skill.targetType != TargetType.AoE_Self
            && skill.targetType != TargetType.GroundTarget
            && skill.targetType != TargetType.Direction
            && skill.targetType != TargetType.Skillshot
            && skill.targetType != TargetType.Cone
            && skill.effectType != SkillEffectType.Buff
            && skill.effectType != SkillEffectType.Debuff)
        {
            if (slot == 0) TargetingSystem.Instance?.Engage(target);
            else           TargetingSystem.Instance?.EngageFromSkill(target);

            Vector3 faceDir = target.transform.position - _player.transform.position;
            faceDir.y = 0f;
            if (faceDir.sqrMagnitude > 0.001f)
                _player.transform.rotation = Quaternion.LookRotation(faceDir);
        }
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
        // Canalisation en cours sur ce slot — _cooldownTimers reste à 0 jusqu'à la résolution
        // (CD posé à la fin, pas au clic, voir StartChannel/ResolveChannel), donc sans ce
        // branchement l'overlay CD de la SkillBarUI resterait invisible pendant tout le cast
        // alors que le slot (et toute la barre) est verrouillé. Compte le temps ÉCOULÉ, pas
        // restant — SkillSlotUI.SetCooldown attend "temps restant avant utilisable", ici
        // c'est castTime - temps déjà passé dans la canalisation.
        if (_isChanneling && slot == _channelSlot && _channelSkill != null)
            return Mathf.Max(0f, _channelSkill.castTime - (Time.time - _channelStartTime));

        // Délai minimum entre deux steps d'un combo (comboStepInterval) — même trou que la
        // canalisation : _cooldownTimers reste à 0 tant que le combo n'est pas fini/expiré,
        // donc sans ça le slot a l'air "dispo" alors que le prochain step ne l'est pas encore.
        if (IsComboActive && slot == _comboSlot && _comboStepCooldown > 0f)
            return _comboStepCooldown;

        // Hit en attente de résolution (Normal/MultiHit/Combo-step, chantier B) — même trou :
        // _cooldownTimers reste à 0 tant que l'event/timeout n'est pas tombé.
        if (_pendingHitSlot == slot)
            return Mathf.Max(0f, _pendingHitTimeout);
        if (_pendingMultiSlot == slot)
            return Mathf.Max(0f, _pendingMultiTimeout);

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
        if (_isChanneling && slot == _channelSlot && _channelSkill != null)
            return _channelSkill.castTime;

        if (IsComboActive && slot == _comboSlot && _comboStepCooldown > 0f && _comboSkill != null)
            return _comboSkill.comboStepInterval;

        if (_pendingHitSlot == slot && _pendingHitSkill != null && _pendingHitSkill.attackAnimation != null)
            return _pendingHitSkill.attackAnimation.length;
        if (_pendingMultiSlot == slot && _pendingMultiSkill != null && _pendingMultiSkill.attackAnimation != null)
            return _pendingMultiSkill.attackAnimation.length;

        // Slots 1-9 : si le GCD est plus long que le CD individuel, on base sur GCD_DURATION.
        // Slot 0 : toujours le cooldown de la BasicAttack équipée.
        if (slot >= 1 && _gcdTimer > _cooldownTimers[slot])
            return GCD_DURATION;
        return _slots[slot].cooldown;
    }

    public void CancelApproach()
    {
        _pendingSkill       = null;
        _pendingTarget      = null;
        _pendingGroundPoint = null;
        _pendingSlot        = -1;
        _isApproaching      = false;
    }

    /// <summary>
    /// Appelé par SkillSystem avant ExecuteMultiHit.
    /// Bloque tous les slots pendant la durée totale du MultiHit (somme des delays).
    /// Cette durée doit correspondre à celle de skill.attackAnimation — sinon
    /// l'auto-attaque (slot 0) reprend la main dès l'expiration du lock gameplay,
    /// même si l'animation est encore en train de jouer, et écrase le state Attack
    /// partagé en plein milieu. Avertissement éditeur sur ce mismatch : voir
    /// SkillData.OnValidate() — à corriger côté données (delays ou anim), pas ici.
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
        Debug.Log($"[SKILLBAR] MultiHit lock {total:F2}s pour {skill.name}");
    }
}
