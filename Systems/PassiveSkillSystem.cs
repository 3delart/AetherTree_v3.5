using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PassiveSkillSystem — Évalue et déclenche les passives du joueur
// Path : Assets/Scripts/Systems/PassiveSkillSystem.cs
// AetherTree GDD v30
//
// Poser sur le même GameObject que Player ([RequireComponent]).
// Pas de références Inspector — tout auto-détecté.
//
// Flow :
//   GameEventBus events → handlers → TryTrigger() → ApplyAllEffects()
//
// Triggers couverts :
//   OnFatalHit          → intercepté dans Entity.TakeDamage() via CanSurviveFatalHit()
//   OnLowHP             → évalué dans OnDamageDealt (cible = joueur)
//   OnTakeDamagePercent → évalué dans OnDamageDealt (cible = joueur)
//   OnKill              → OnMobKilled
//   OnCritical          → OnDamageDealt (source = joueur, isCrit = true)
//   OnCast              → OnSkillUsed
//   OnCombo             → OnSkillUsed (isCombo = true)
//
// OnFatalHit — cas spécial :
//   Entity.TakeDamage() appelle PassiveSkillSystem.Instance?.CanSurviveFatalHit()
//   avant de tuer le joueur. Si une passive OnFatalHit se déclenche, le joueur
//   survit à 1 HP. Les effets de la passive (Invincible, Push...) s'appliquent
//   immédiatement après la survie.
//
// Cooldowns :
//   _cooldownTimers[passive] = secondes restantes (décrément dans Update)
//
// OncePerCombat :
//   _usedThisCombat hashset — vidé dans ResetCombat() (appelé par Player.Revive)
// =============================================================

[RequireComponent(typeof(Player))]
public class PassiveSkillSystem : MonoBehaviour
{
    public static PassiveSkillSystem Instance { get; private set; }

    private Player _player;

    // Cooldown individuel par passive
    private readonly Dictionary<PassiveSkillData, float> _cooldownTimers = new Dictionary<PassiveSkillData, float>();

    // Passives déjà utilisées ce combat (oncePerCombat)
    private readonly HashSet<PassiveSkillData> _usedThisCombat = new HashSet<PassiveSkillData>();

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _player  = GetComponent<Player>();
    }

    private void OnEnable()
    {
        GameEventBus.OnDamageDealt += OnDamageDealt;
        GameEventBus.OnMobKilled   += OnMobKilled;
        GameEventBus.OnSkillUsed   += OnSkillUsed;
    }

    private void OnDisable()
    {
        GameEventBus.OnDamageDealt -= OnDamageDealt;
        GameEventBus.OnMobKilled   -= OnMobKilled;
        GameEventBus.OnSkillUsed   -= OnSkillUsed;
    }

    private void Update()
    {
        if (_cooldownTimers.Count == 0) return;

        var keys = new List<PassiveSkillData>(_cooldownTimers.Keys);
        foreach (var passive in keys)
        {
            _cooldownTimers[passive] -= Time.deltaTime;
            if (_cooldownTimers[passive] <= 0f)
                _cooldownTimers.Remove(passive);
        }
    }

    // =========================================================
    // RESET COMBAT — appelé par Player.Revive()
    // =========================================================

    /// <summary>
    /// Vide le registre oncePerCombat.
    /// Appelé par Player.Revive() à chaque résurrection.
    /// </summary>
    public void ResetCombat() => _usedThisCombat.Clear();

    // =========================================================
    // ONFATALHIT — intercept AVANT Die()
    // =========================================================

    /// <summary>
    /// Appelé depuis Entity.TakeDamage() quand le coup serait fatal (HP ≤ 0).
    /// Retourne true si une passive OnFatalHit se déclenche — le joueur survit à 1 HP.
    /// Les effets sont appliqués immédiatement.
    /// </summary>
    public bool CanSurviveFatalHit()
    {
        if (_player == null || _player.unlockedPassives == null) return false;

        foreach (var passive in _player.unlockedPassives)
        {
            if (passive == null) continue;
            if (passive.triggerType != PassiveTriggerType.OnFatalHit) continue;

            if (!CanTrigger(passive)) continue;
            if (!passive.RollProc()) continue;

            RegisterTrigger(passive);
            ApplyAllEffects(passive);

            Debug.Log($"[PASSIVE] {passive.skillName} — coup fatal survécu !");
            return true;
        }
        return false;
    }

    // =========================================================
    // HANDLERS GAMEEVENTBUS
    // =========================================================

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_player == null || _player.isDead) return;
        if (_player.unlockedPassives == null || _player.unlockedPassives.Count == 0) return;

        foreach (var passive in _player.unlockedPassives)
        {
            if (passive == null) continue;

            switch (passive.triggerType)
            {
                // ── Reçoit un coup ≥ X% MaxHP ────────────────
                case PassiveTriggerType.OnTakeDamagePercent:
                    if (e.target == _player &&
                        e.amount >= _player.MaxHP * passive.triggerThreshold)
                        TryTrigger(passive);
                    break;

                // ── HP joueur ≤ seuil % après coup ───────────
                case PassiveTriggerType.OnLowHP:
                    if (e.target == _player &&
                        _player.HPPercent <= passive.triggerThreshold)
                        TryTrigger(passive);
                    break;

                // ── Inflige un critique ───────────────────────
                case PassiveTriggerType.OnCritical:
                    if (e.source == _player && e.isCrit)
                        TryTrigger(passive);
                    break;
            }
        }
    }

    private void OnMobKilled(MobKilledEvent e)
    {
        if (_player == null || _player.isDead) return;
        if (e.eligiblePlayers == null || !e.eligiblePlayers.Contains(_player)) return;
        if (_player.unlockedPassives == null) return;

        foreach (var passive in _player.unlockedPassives)
        {
            if (passive == null) continue;
            if (passive.triggerType == PassiveTriggerType.OnKill)
                TryTrigger(passive);
        }
    }

    private void OnSkillUsed(SkillUsedEvent e)
    {
        if (_player == null || _player.isDead) return;
        if (e.caster != _player) return;
        if (_player.unlockedPassives == null) return;

        foreach (var passive in _player.unlockedPassives)
        {
            if (passive == null) continue;

            switch (passive.triggerType)
            {
                // ── Utilise un skill (spécifique ou any) ─────
                case PassiveTriggerType.OnCast:
                    // triggerSkill == null → n'importe quel skill
                    if (passive.triggerSkill == null || passive.triggerSkill == e.skill)
                        TryTrigger(passive);
                    break;

                // ── Utilise un combo élémentaire ──────────────
                case PassiveTriggerType.OnCombo:
                    if (e.isCombo)
                        TryTrigger(passive);
                    break;
            }
        }
    }

    // =========================================================
    // DÉCLENCHEMENT
    // =========================================================

    /// <summary>Vérifie cooldown + oncePerCombat + chance, puis déclenche.</summary>
    private void TryTrigger(PassiveSkillData passive)
    {
        if (!CanTrigger(passive)) return;
        if (!passive.RollProc()) return;

        RegisterTrigger(passive);
        ApplyAllEffects(passive);

        Debug.Log($"[PASSIVE] {passive.skillName} déclenché !");
    }

    /// <summary>True si cooldown OK et oncePerCombat OK.</summary>
    private bool CanTrigger(PassiveSkillData passive)
    {
        if (_cooldownTimers.ContainsKey(passive)) return false;
        if (passive.oncePerCombat && _usedThisCombat.Contains(passive)) return false;
        return true;
    }

    /// <summary>Enregistre cooldown et oncePerCombat après déclenchement.</summary>
    private void RegisterTrigger(PassiveSkillData passive)
    {
        if (passive.cooldown > 0f)
            _cooldownTimers[passive] = passive.cooldown;
        if (passive.oncePerCombat)
            _usedThisCombat.Add(passive);
    }

    // =========================================================
    // APPLICATION DES EFFETS
    // =========================================================

    /// <summary>Applique tous les effets de la liste simultanément.</summary>
    private void ApplyAllEffects(PassiveSkillData passive)
    {
        if (passive.effects == null || passive.effects.Count == 0) return;

        foreach (var effect in passive.effects)
            ApplyEffect(effect);
    }

    private void ApplyEffect(PassiveEffect effect)
    {
        if (effect == null || _player == null) return;

        switch (effect.effectType)
        {
            // ── Buff sur soi ──────────────────────────────────
            case PassiveEffectType.BuffSelf:
                if (effect.buffToApply != null && _player.statusEffects != null)
                {
                    _player.statusEffects.ApplyBuff(effect.buffToApply, _player);
                    Debug.Log($"[PASSIVE EFFECT] BuffSelf → {effect.buffToApply.effectName}");
                }
                break;

            // ── Soin sur soi ──────────────────────────────────
            case PassiveEffectType.HealSelf:
            {
                float amount = effect.GetFinalValue(_player.MaxHP);
                _player.Heal(amount);
                FloatingText.Spawn($"+{Mathf.RoundToInt(amount)} HP",
                    _player.transform.position + Vector3.up * 2f, Color.green, 1.5f);
                Debug.Log($"[PASSIVE EFFECT] HealSelf → +{amount:F0} HP");
                break;
            }

            // ── Shield sur soi ────────────────────────────────
            case PassiveEffectType.ShieldSelf:
            {
                // Crée un BuffData Shield runtime
                // On utilise BuffData.shieldAmount = valeur calculée
                // Le shield est appliqué via StatusEffectSystem comme un buff normal
                float amount = effect.GetFinalValue(_player.MaxHP);
                if (_player.statusEffects != null)
                {
                    // Crée un BuffData temporaire runtime (pas de SO requis)
                    var shieldBuff = ScriptableObject.CreateInstance<BuffData>();
                    shieldBuff.buffType     = BuffType.Shield;
                    shieldBuff.shieldAmount = amount;
                    shieldBuff.duration     = 30f; // shield dure jusqu'à absorption ou 30s
                    shieldBuff.effectName   = "PassiveShield";
                    _player.statusEffects.ApplyBuff(shieldBuff, _player);
                    Destroy(shieldBuff, 1f); // nettoyage — le buff a déjà été enregistré
                    FloatingText.Spawn($"SHIELD +{Mathf.RoundToInt(amount)}",
                        _player.transform.position + Vector3.up * 2f,
                        new Color(0.4f, 0.7f, 1f), 1.5f);
                    Debug.Log($"[PASSIVE EFFECT] ShieldSelf → {amount:F0} pts");
                }
                break;
            }

            // ── Invincibilité sur soi ─────────────────────────
            case PassiveEffectType.InvincibleSelf:
            {
                if (_player.statusEffects != null)
                {
                    var invincBuff = ScriptableObject.CreateInstance<BuffData>();
                    invincBuff.buffType   = BuffType.Invincible;
                    invincBuff.duration   = effect.invincibleDuration;
                    invincBuff.effectName = "PassiveInvincible";
                    _player.statusEffects.ApplyBuff(invincBuff, _player);
                    Destroy(invincBuff, 1f);
                    FloatingText.Spawn($"INVINCIBLE {effect.invincibleDuration:F0}s",
                        _player.transform.position + Vector3.up * 2.5f,
                        new Color(1f, 0.85f, 0.2f), 2f);
                    Debug.Log($"[PASSIVE EFFECT] InvincibleSelf → {effect.invincibleDuration:F0}s");
                }
                break;
            }

            // ── Résurrection instantanée ──────────────────────
            case PassiveEffectType.ReviveSelf:
                _player.Revive(effect.reviveHPPercent, 0.30f);
                FloatingText.Spawn("RÉSURRECTION !",
                    _player.transform.position + Vector3.up * 2.5f,
                    new Color(1f, 0.85f, 0.2f), 2.5f);
                Debug.Log($"[PASSIVE EFFECT] ReviveSelf → {effect.reviveHPPercent:P0} HP");
                break;

            // ── Repousser les ennemis ─────────────────────────
            case PassiveEffectType.PushEnemiesAround:
            {
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    _player.transform.position,
                    effect.aoeRadius,
                    effect.pushForce,
                    towardsCenter: false,
                    caster: _player,
                    layerMask: ~UnityEngine.LayerMask.GetMask("Player"));

                FloatingText.Spawn($"ONDE ×{count}",
                    _player.transform.position + Vector3.up * 2f, Color.yellow, 1.5f);
                Debug.Log($"[PASSIVE EFFECT] PushEnemiesAround → ×{count} ennemis");
                break;
            }

            // ── Debuffer les ennemis proches ──────────────────
            case PassiveEffectType.DebuffEnemiesAround:
            {
                if (effect.debuffToApply == null) break;

                Collider[] hits = Physics.OverlapSphere(
                    _player.transform.position, effect.aoeRadius,
                    ~UnityEngine.LayerMask.GetMask("Player"));

                int count = 0;
                foreach (var col in hits)
                {
                    Entity entity = col.GetComponentInParent<Entity>();
                    if (entity == null || entity == _player || entity.isDead) continue;
                    if (entity.statusEffects == null) continue;

                    if (entity.statusEffects.TryApplyDebuff(effect.debuffToApply, _player))
                        count++;
                }

                FloatingText.Spawn($"{effect.debuffToApply.effectName} ×{count}",
                    _player.transform.position + Vector3.up * 2f, Color.magenta, 1.5f);
                Debug.Log($"[PASSIVE EFFECT] DebuffEnemiesAround → {effect.debuffToApply.effectName} ×{count}");
                break;
            }

            // ── Dégâts AoE autour du joueur ───────────────────
            case PassiveEffectType.DamageEnemiesAround:
            {
                if (_player.equippedWeaponInstance == null && effect.damageMultiplier <= 0f) break;

                float baseDmg = UnityEngine.Random.Range(_player.AttackDamageMin, _player.AttackDamageMax) * effect.damageMultiplier;

                Collider[] hits = Physics.OverlapSphere(
                    _player.transform.position, effect.aoeRadius,
                    ~UnityEngine.LayerMask.GetMask("Player"));

                int count = 0;
                foreach (var col in hits)
                {
                    Entity entity = col.GetComponentInParent<Entity>();
                    if (entity == null || entity == _player || entity.isDead) continue;

                    entity.TakeDamage(baseDmg, effect.damageElement, _player);
                    count++;
                }

                FloatingText.Spawn($"×{count} -{Mathf.RoundToInt(baseDmg)}",
                    _player.transform.position + Vector3.up * 2f, Color.red, 1.5f);
                Debug.Log($"[PASSIVE EFFECT] DamageEnemiesAround → {baseDmg:F0} dmg ×{count}");
                break;
            }
        }
    }

    // =========================================================
    // ACCESSEURS UI
    // =========================================================

    /// <summary>Cooldown restant pour une passive (0 = prête).</summary>
    public float GetCooldownRemaining(PassiveSkillData passive)
        => _cooldownTimers.TryGetValue(passive, out float t) ? Mathf.Max(0f, t) : 0f;

    /// <summary>True si la passive a déjà été utilisée ce combat.</summary>
    public bool IsUsedThisCombat(PassiveSkillData passive)
        => _usedThisCombat.Contains(passive);
}
