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

    // Cooldown du skill MultiHit — posé à la FIN du lock (skill.cooldown mesuré depuis
    // la fin de l'exécution, pas depuis le cast) plutôt qu'immédiatement dans ExecuteSkill().
    // Sinon un CD de 6s sur un skill qui dure 5s ne laisse qu'1s de vrai temps mort.
    private int       _multiHitCooldownSlot  = -1;
    private SkillData _multiHitCooldownSkill = null;

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

    // ── Canalisation (castTime > 0) ────────────────────────────
    private const float CHANNEL_CANCEL_MOVE_THRESHOLD = 0.3f;   // même seuil que ResourceNode

    private bool      _isChanneling      = false;
    private SkillData _channelSkill      = null;
    private int       _channelSlot       = -1;
    private Entity    _channelTarget     = null;
    private Vector3   _channelStartPos   = Vector3.zero;

    public bool IsChanneling => _isChanneling;

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
            if (_multiHitLockTimer <= 0f && _multiHitCooldownSlot >= 0)
            {
                // Le MultiHit vient de finir — le cooldown démarre maintenant, pas au cast.
                _cooldownTimers[_multiHitCooldownSlot] = _multiHitCooldownSkill != null ? _multiHitCooldownSkill.cooldown : 0f;
                Debug.Log($"[SKILLBAR] MultiHit terminé — cooldown {_cooldownTimers[_multiHitCooldownSlot]:F2}s démarré sur slot {_multiHitCooldownSlot}.");
                _multiHitCooldownSlot  = -1;
                _multiHitCooldownSkill = null;
            }
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

        // ── Timer combo séquentiel ────────────────────────────
        if (_comboSlot >= 0 && _comboTimer > 0f)
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
        // MultiHit ou canalisation en cours → tous les slots bloqués sans exception
        if (_multiHitLockTimer > 0f || _isChanneling)
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
        if (_isApproaching && _pendingSlot == slot && (_pendingSkill != skill || _pendingTarget != target))
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
        _comboStep  = 0;
        _comboSlot  = -1;
        _comboTimer = 0f;
        _comboSkill = null;
    }

    // ── Canalisation ──────────────────────────────────────────

    // ── Dispatch castTime 0 vs canalisation — UNIQUE point d'entrée pour lancer un skill ──
    // Utilisé par TryUseSlot() ET CheckApproach() — ne jamais appeler ExecuteSkill()
    // directement depuis un autre endroit, sinon castTime > 0 serait contourné.
    private void LaunchSkill(SkillData skill, int slot, Entity target)
    {
        if (skill.castTime > 0f) StartChannel(skill, slot, target);
        else                     ExecuteSkill(skill, slot, target);
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

        // Résidu de vélocité NavMeshAgent (ex: hand-off depuis CheckApproach) qui pourrait
        // sinon déclencher immédiatement le poll d'annulation par mouvement dans Update().
        _agent?.ResetPath();

        _player.AnimatorController?.PlayChannel(skill.channelAnimation);

        // GroundTarget : raycast au LANCEMENT (aim-then-channel), pas à la résolution — le point
        // est locké quand le joueur commet à la canalisation, cohérent avec mana/HP/gold dépensés
        // au clic. ResolveChannel() n'a besoin d'aucun changement : _groundTargetPoint sera déjà
        // posé quand SkillSystem.Execute() le consomme.
        if (skill.targetType == TargetType.GroundTarget)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                SkillSystem.Instance?.SetGroundTargetPoint(hit.point);
        }

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

        // Capturer AVANT EndChannelState() — celle-ci met _channelTarget à null, et Execute()
        // a besoin de la vraie cible.
        SkillData skill  = _channelSkill;
        int       slot   = _channelSlot;
        Entity    target = _channelTarget;

        EndChannelState();

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

            _player.SpendMana(GetEffectiveManaCost(skill));
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

        _player.SpendMana(GetEffectiveManaCost(stepSkill));
        SkillSystem.Instance?.Execute(stepSkill, _player, target);
        if (target != null) TargetingSystem.Instance?.EngageFromSkill(target);

        _comboStep++;

        if (_comboStep > _comboSkill.comboSteps.Count)
        {
            // Dernier step complété — CD sur le slot + reset
            Debug.Log($"[SKILLBAR] Combo terminé sur slot {slot}.");
            _cooldownTimers[slot] = _comboSkill.cooldown;
            if (slot >= 1)
            {
                _gcdTimer = GCD_DURATION;
                // Durée basée sur l'anim du DERNIER step (celle qui vient de jouer), pas celle
                // du parent — même raison que dans ExecuteSkill.
                float autoAttackDelay = stepSkill.attackAnimation != null
                    ? Mathf.Max(GCD_DURATION, stepSkill.attackAnimation.length)
                    : GCD_DURATION;
                TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
            }
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

    // ── Exécution ─────────────────────────────────────────────
    private void ExecuteSkill(SkillData skill, int slot, Entity target)
    {
        _player.SpendMana(GetEffectiveManaCost(skill));
        if (skill.hpCost > 0f) _player.SpendHP(skill.hpCost);
        if (skill.goldCost > 0) AerisSystem.Instance?.Spend(skill.goldCost);
        // NOTE : Ne PAS appeler player.UseSkill() ici.
        // SkillSystem.Execute() → player.UseSkill() s'en charge.
        // Double appel = RegisterCast() élémentaire × 2 → affinité doublée.

        // MultiHit avec hitSteps : le cooldown démarre à la FIN de l'exécution (posé dans
        // Update() quand _multiHitLockTimer expire, voir LockForMultiHit()), pas ici au cast —
        // sinon un CD de 6s sur un skill qui dure 5s ne laisse qu'1s de vrai temps mort.
        // Repli sur le comportement immédiat si le skill est mal configuré (pas de hitSteps),
        // pour ne jamais laisser un skill sans cooldown du tout.
        bool deferToMultiHitEnd = skill.executionType == SkillExecutionType.MultiHit
                                && skill.hitSteps != null && skill.hitSteps.Count > 0;
        if (deferToMultiHitEnd)
        {
            _multiHitCooldownSlot  = slot;
            _multiHitCooldownSkill = skill;
        }
        else
        {
            _cooldownTimers[slot] = skill.cooldown;
        }

        // ── GCD §8.7 ──────────────────────────────────────────
        // Un actif ou l'ultime (slots 1-9) déclenche le GCD global sur tous les slots 1-9.
        // Le slot 0 (BasicAttack) ne déclenche PAS de GCD — son CD vient de skill.cooldown.
        // Repousse aussi le prochain tick d'auto-attaque (TargetingSystem) — sinon elle peut
        // se déclencher dans la même frame/juste après le skill (Engage()/EngageFromSkill
        // remet son propre timer à un état qui ne suffit pas à l'empêcher, voir historique).
        // Durée = la plus longue entre le GCD et l'anim qui vient d'être lancée (PlayAttack,
        // via player.UseSkill plus haut) — sinon une anim de skill > 1s se ferait couper par
        // l'auto-attaque avant sa fin.
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }

        // Engage la cible + oriente le caster — voir EngageAndFaceTarget() pour le détail
        // slot 0 vs slots ≥ 1 / exclusion Buff-Debuff.
        EngageAndFaceTarget(skill, slot, target);

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
