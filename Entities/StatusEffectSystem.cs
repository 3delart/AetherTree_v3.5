using UnityEngine;
using System.Collections.Generic;

// =============================================================
// STATUSEFFECTSYSTEM — Composant gérant tous les effets actifs
// Path : Assets/Scripts/Core/StatusEffectSystem.cs
// AetherTree GDD v3.5 — §3.1.1
//
// À attacher sur : Entity (Player, Mob, PNJ, Pet) via [RequireComponent] sur Entity.
//
// Debuffs (GDD v3.5 §3.1.1.1) :
//   Burn, Slow, Knockback*, Root, Poison, Stun, ArmorBreak,
//   Shocked, Fear, ManaDrain, Blind, Freeze, Silence, Taunt, Mark
//   (*) Knockback : effet ponctuel — déclenché via Entity.ApplyKnockBack().
//
// Buffs (GDD v3.5 §3.1.1.2) :
//   Shield, Regeneration, Haste, DefenseUp, Purified*, Heal*,
//   DodgeUp, AttackUp, CritChanceUp, CritDamageUp, Barrier
//   (*) Purified et Heal sont des effets instantanés — pas de flag runtime.
//
// Effets spéciaux (GDD v3.5 §3.1.1.3) :
//   Invincibility, Stealth
//
// Règles clés (GDD v3.5 §3.1.1.4) :
//   — Un debuff ne se cumule pas avec lui-même (refresh uniquement)
//   — CC dur (Stun / Fear) : pas d'immunité automatique post-CC
//   — Burn et Poison coexistent (types différents)
//   — Deux Burn ne se cumulent pas — le plus récent remplace l'ancien (refresh)
//   — Slow et Freeze coexistent — Slow = déplacement réduit, Freeze = hard CC
//
// v3.5 :
//   — ManaDrain tick déplacé dans DebuffInstance.Tick() (cohérence DoT)
//   — Regeneration tick déplacé dans BuffInstance.Tick() (suppression buffRegenBonus dans Update)
//   — buffCritBonus séparé en buffCritChanceBonus + buffCritDamageBonus
//   — BuffType.Taunt supprimé — Taunt est un DebuffType
//   — Câblage Stats (buff + debuff) : RecalculateStats() + ReapplyActiveModifiers()
//   — OnApplyBuff/ExpireBuff pour valeurs numériques passent tous par Recalculate + Reapply
// =============================================================

[RequireComponent(typeof(Entity))]
public class StatusEffectSystem : MonoBehaviour
{
    // ── Effets actifs ─────────────────────────────────────────
    private Dictionary<DebuffType, DebuffInstance> _activeDebuffs
        = new Dictionary<DebuffType, DebuffInstance>();

    private Dictionary<BuffType, BuffInstance> _activeBuffs
        = new Dictionary<BuffType, BuffInstance>();

    // ── Debug Inspector ───────────────────────────────────────
    [Header("Debug — Effets actifs (lecture seule)")]
    [SerializeField] private List<string> _debugDebuffs = new List<string>();
    [SerializeField] private List<string> _debugBuffs   = new List<string>();

    // ── Résistances aux debuffs [0..1] ────────────────────────
    private Dictionary<DebuffType, float> _debuffResistances
        = new Dictionary<DebuffType, float>();

    // =========================================================
    // FLAGS DEBUFF — GDD v3.5 §3.1.1.1
    // =========================================================

    /// <summary>Stunned — bloque toutes les actions. CC dur.</summary>
    public bool isStunned    { get; private set; } = false;

    /// <summary>Feared — fuite incontrôlée. CC dur.</summary>
    public bool isFeared     { get; private set; } = false;

    /// <summary>Rooted — immobilisé, peut toujours attaquer et caster.</summary>
    public bool isRooted     { get; private set; } = false;

    /// <summary>Blinded — réduit la précision.</summary>
    public bool isBlinded    { get; private set; } = false;

    /// <summary>Poisoned — DoT + réduction soins reçus.</summary>
    public bool isPoisoned   { get; private set; } = false;

    /// <summary>ArmorBreak — réduction de défense % temporaire.</summary>
    public bool isArmorBroken { get; private set; } = false;

    /// <summary>Sleeping — immobilisé jusqu'au premier dégât reçu.</summary>
    public bool isSleeping   { get; private set; } = false;

    /// <summary>Freeze — immobilisation totale (hard CC). Réattribué Eau v3.0 — §3.1.1.1.</summary>
    public bool isFreezed    { get; private set; } = false;

    /// <summary>Silence — bloque l'utilisation des skills.</summary>
    public bool isSilenced   { get; private set; } = false;

    /// <summary>Taunted — force les ennemis à cibler cette entité (attaque basique seulement en PvP). §3.1.1.1.</summary>
    public bool isTaunted    { get; private set; } = false;

    /// <summary>Marked — cible marquée, reçoit des dégâts supplémentaires. Design decision.</summary>
    public bool isMarked     { get; private set; } = false;

    // Valeurs numériques debuff
    public float slowMultiplier        { get; private set; } = 1f;
    public float armorBreakReduction   { get; private set; } = 0f;
    public float shockDefenseReduction { get; private set; } = 0f;
    public float poisonHealReduction   { get; private set; } = 0f;
    public float blindPrecisionMalus   { get; private set; } = 0f;
    public float markDamageBonus       { get; private set; } = 0f;

    // =========================================================
    // FLAGS & VALEURS BUFF — GDD v3.5 §3.1.1.2 & §3.1.1.3
    // =========================================================

    /// <summary>Invincible — annule tous les dégâts entrants. Post-respawn 3s. §3.1.1.3.</summary>
    public bool isInvincible { get; private set; } = false;

    /// <summary>Stealthed — invisible jusqu'à une attaque ou dégât reçu. §3.1.1.3.</summary>
    public bool isStealthed  { get; private set; } = false;

    // Valeurs numériques buff
    public float buffDefenseBonus      { get; private set; } = 0f;
    public float buffDodgeBonus        { get; private set; } = 0f;
    public float buffPrecisionBonus    { get; private set; } = 0f;
    public float buffSpeedMultiplier   { get; private set; } = 1f;
    public float buffAttackBonus       { get; private set; } = 0f;
    public float buffCritChanceBonus   { get; private set; } = 0f;
    public float buffCritDamageBonus   { get; private set; } = 0f;
    public float barrierElementResist  { get; private set; } = 0f;

    private Entity _entity;

    private void Awake() => _entity = GetComponent<Entity>();

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (_entity.isDead) return;

        // Tick debuffs (DoT, ManaDrain via DebuffInstance.Tick)
        var expiredDebuffs = new List<DebuffType>();
        foreach (var kvp in _activeDebuffs)
        {
            kvp.Value.Tick(_entity, Time.deltaTime);
            if (kvp.Value.IsExpired) expiredDebuffs.Add(kvp.Key);
        }
        foreach (var t in expiredDebuffs) ExpireDebuff(t);

        // Tick buffs (Regeneration via BuffInstance.Tick)
        var expiredBuffs = new List<BuffType>();
        foreach (var kvp in _activeBuffs)
        {
            kvp.Value.Tick(_entity, Time.deltaTime);
            if (kvp.Value.IsExpired) expiredBuffs.Add(kvp.Key);
        }
        foreach (var t in expiredBuffs) ExpireBuff(t);

        // Refresh debug lists
        _debugDebuffs.Clear();
        foreach (var kvp in _activeDebuffs)
            _debugDebuffs.Add($"{kvp.Key} — {kvp.Value.remainingTime:F1}s");

        _debugBuffs.Clear();
        foreach (var kvp in _activeBuffs)
            _debugBuffs.Add($"{kvp.Key} — {kvp.Value.remainingTime:F1}s");
    }

    // =========================================================
    // APPLICATION DEBUFF
    // =========================================================

    public bool TryApplyDebuff(DebuffData debuff, Entity source)
    {
        if (debuff == null || _entity.isDead) return false;

        // Vérification résistance
        float resistance = GetDebuffResistance(debuff.debuffType);
        if (resistance > 0f && Random.value < resistance)
            return false;

        // Refresh si déjà actif — GDD v3.5 §3.1.1.4 : le plus récent remplace l'ancien
        if (_activeDebuffs.TryGetValue(debuff.debuffType, out var existing))
        {
            existing.Refresh();
            return true;
        }

        // Nouvelle application
        DebuffInstance instance = (DebuffInstance)debuff.CreateInstance(source);
        _activeDebuffs[debuff.debuffType] = instance;
        OnApplyDebuff(instance);

        return true;
    }

    // =========================================================
    // APPLICATION BUFF
    // =========================================================

    public void ApplyBuff(BuffData buff, Entity source)
    {
        if (buff == null || _entity.isDead) return;

        // Refresh si déjà actif
        if (_activeBuffs.TryGetValue(buff.buffType, out var existing))
        {
            existing.Refresh();
            return;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        _activeBuffs[buff.buffType] = instance;
        OnApplyBuff(instance);
    }

    // =========================================================
    // ON APPLY DEBUFF
    // =========================================================

    private void OnApplyDebuff(DebuffInstance instance)
    {
        switch (instance.DebuffType)
        {
            // ── Flags purs — pas de valeur numérique sur Entity ──
            case DebuffType.Stun:    isStunned  = true; break;
            case DebuffType.Fear:    isFeared   = true; break;
            case DebuffType.Root:    isRooted   = true; break;
            case DebuffType.Sleep:   isSleeping = true; break;
            case DebuffType.Silence: isSilenced = true; break;
            case DebuffType.Taunt:   isTaunted  = true; break;

            case DebuffType.Freeze:
                isFreezed = true;
                slowMultiplier = 0f;
                break;

            // ── Flags + valeurs locales (lues par CombatSystem via accesseurs) ──
            case DebuffType.Blind:
                isBlinded = true;
                blindPrecisionMalus += instance.DebuffData.debuffValue;
                break;

            case DebuffType.Poison:
                // §3.1.1.1 — DoT (tick) + réduction soins % (local)
                isPoisoned = true;
                poisonHealReduction += instance.DebuffData.healReduction;
                break;

            case DebuffType.ArmorBreak:
                // §3.1.1.1 — réduction défense % (lue dans Entity.GetMeleeDefense etc.)
                isArmorBroken = true;
                armorBreakReduction += instance.DebuffData.defenseReduction;
                break;

            case DebuffType.Shocked:
                // §3.1.1.1 — interruption cast + mini-stun 0.5s (géré par CombatSystem)
                shockDefenseReduction += instance.DebuffData.defenseReduction;
                break;

            case DebuffType.Slow:
                slowMultiplier = Mathf.Min(slowMultiplier, instance.DebuffData.slowMultiplier);
                break;

            case DebuffType.Mark:
                isMarked = true;
                markDamageBonus += instance.DebuffData.debuffValue;
                break;

            // ── Stats — modifie directement les champs Entity via Recalculate ──
            case DebuffType.Stats:
                // §3.1.1.1 — recalcul propre + ré-application de tous les modificateurs actifs
                RecalculateAndReapply();
                break;

            // ManaDrain : tick dans DebuffInstance.Tick — pas de flag local
            // Knockback  : effet ponctuel — Entity.ApplyKnockBack()
            // Bleed      : DoT pur — tick dans DebuffInstance.Tick, pas de flag
        }
    }

    // =========================================================
    // RECALCUL + RÉ-APPLICATION
    // =========================================================

    /// <summary>
    /// Demande un recalcul complet (base propre) puis ré-applique les modificateurs actifs.
    /// Appelé par OnApplyDebuff/Buff et ExpireDebuff/Buff pour les effets qui touchent
    /// des valeurs numériques de stats sur Entity.
    /// </summary>
    private void RecalculateAndReapply()
    {
        _entity.RequestRecalculate();
        // RequestRecalculate() appelle ReapplyActiveModifiers() en fin de chaîne
        // (via Entity.RequestRecalculate → statusEffects.ReapplyActiveModifiers)
        // Pas besoin de le rappeler ici.
    }

    /// <summary>
    /// Ré-applique tous les modificateurs numériques des effets actifs sur l'entité.
    /// Appelé par Entity.RequestRecalculate() après restauration des stats de base.
    /// Ne touche PAS aux flags booléens (isStunned, etc.) — déjà corrects.
    /// </summary>
    public void ReapplyActiveModifiers(Entity target)
    {
        // ── Debuffs numériques ────────────────────────────────
        // Reset des valeurs locales avant recalcul
        armorBreakReduction   = 0f;
        shockDefenseReduction = 0f;
        poisonHealReduction   = 0f;
        blindPrecisionMalus   = 0f;
        markDamageBonus       = 0f;
        slowMultiplier        = 1f;

        foreach (var kvp in _activeDebuffs)
        {
            var d = kvp.Value.DebuffData;
            switch (kvp.Key)
            {
                case DebuffType.Slow:
                    slowMultiplier = Mathf.Min(slowMultiplier, d.slowMultiplier);
                    break;
                case DebuffType.Freeze:
                    slowMultiplier = 0f;
                    break;
                case DebuffType.Blind:
                    blindPrecisionMalus += d.debuffValue;
                    break;
                case DebuffType.Poison:
                    poisonHealReduction += d.healReduction;
                    break;
                case DebuffType.ArmorBreak:
                    armorBreakReduction += d.defenseReduction;
                    break;
                case DebuffType.Shocked:
                    shockDefenseReduction += d.defenseReduction;
                    break;
                case DebuffType.Mark:
                    markDamageBonus += d.debuffValue;
                    break;
                case DebuffType.Stats:
                    ApplyStatDebuff(target, d);
                    break;
            }
        }

        // ── Buffs numériques ──────────────────────────────────
        // Reset des valeurs locales avant recalcul
        buffDefenseBonus    = 0f;
        buffDodgeBonus      = 0f;
        buffPrecisionBonus  = 0f;
        buffSpeedMultiplier = 1f;
        buffAttackBonus     = 0f;
        buffCritChanceBonus = 0f;
        buffCritDamageBonus = 0f;
        barrierElementResist = 0f;

        // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp
        // retirés (2026) — redondants avec Stats. Les accumulateurs buffDefenseBonus/
        // buffDodgeBonus/buffPrecisionBonus/buffSpeedMultiplier/buffAttackBonus/
        // buffCritChanceBonus/buffCritDamageBonus/barrierElementResist restent déclarés
        // (lus par Entity.cs/CombatSystem.cs/PlayerController.cs/Mob.cs) mais ne sont
        // plus jamais réécrits ici — toujours à leur valeur par défaut (0 ou 1),
        // sans effet, sans rien à changer côté lecteurs.
        foreach (var kvp in _activeBuffs)
        {
            if (kvp.Key == BuffType.Stats)
                ApplyStatBuff(target, kvp.Value.BuffData);
        }
    }

    // ── Application directe sur Entity ────────────────────────

    private void ApplyStatDebuff(Entity target, DebuffData d)
    {
        float v = d.debuffModifier == ModifierType.Percent
            ? GetBaseStatValue(target, d.debuffStatType) * d.debuffValue
            : d.debuffValue;

        ModifyEntityStat(target, d.debuffStatType, -v);
    }

    private void ApplyStatBuff(Entity target, BuffData b)
    {
        float v = b.buffModifier == ModifierType.Percent
            ? GetBaseStatValue(target, b.buffStatType) * b.buffStatValue
            : b.buffStatValue;

        ModifyEntityStat(target, b.buffStatType, v);
    }

    /// <summary>Lit la valeur actuelle d'une stat sur l'entité — sert de base pour les Percent.</summary>
    private float GetBaseStatValue(Entity target, StatModifierType stat)
    {
        switch (stat)
        {
            case StatModifierType.MaxHP:           return target.MaxHP;
            case StatModifierType.MaxMana:         return target.MaxMana;
            case StatModifierType.RegenHP:         return target.RegenHP;
            case StatModifierType.RegenMana:       return target.RegenMana;
            case StatModifierType.AttackDamage:    return target.AttackDamageMin;
            case StatModifierType.MoveSpeed:       return target.MoveSpeed;
            case StatModifierType.MeleeDefense:    return target.MeleeDefense;
            case StatModifierType.RangedDefense:   return target.RangedDefense;
            case StatModifierType.MagicDefense:    return target.MagicDefense;
            case StatModifierType.CritChance:      return target.CritChance;
            case StatModifierType.CritDamage:      return target.CritMultiplier;
            case StatModifierType.Dodge:           return target.Dodge;
            // ElementalPoint : pas de valeur globale — retourne 0f (base neutre pour Percent).
            // La valeur réelle dépend de l'élément ciblé ; voir ModifyEntityStat.
            default:                               return 0f;
        }
    }

    /// <summary>Applique un delta (positif ou négatif) sur une stat de l'entité.</summary>
    private void ModifyEntityStat(Entity target, StatModifierType stat, float delta)
    {
        switch (stat)
        {
            case StatModifierType.MaxHP:
                target.SetMaxHP(target.MaxHP + delta);
                break;
            case StatModifierType.MaxMana:
                target.SetMaxMana(target.MaxMana + delta);
                break;
            case StatModifierType.RegenHP:
                target.SetRegenHP(target.RegenHP + delta);
                break;
            case StatModifierType.RegenMana:
                target.SetRegenMana(target.RegenMana + delta);
                break;
            case StatModifierType.AttackDamage:
                target.SetAttackDamageMin(target.AttackDamageMin + delta);
                target.SetAttackDamageMax(target.AttackDamageMax + delta);
                break;
            case StatModifierType.AttackSpeed:
                // AttackSpeed non géré sur Entity base — réservé CombatSystem
                break;
            case StatModifierType.MoveSpeed:
                target.SetMoveSpeed(target.MoveSpeed + delta);
                break;
            case StatModifierType.MeleeDefense:
                target.SetMeleeDefense(target.MeleeDefense + delta);
                break;
            case StatModifierType.RangedDefense:
                target.SetRangedDefense(target.RangedDefense + delta);
                break;
            case StatModifierType.MagicDefense:
                target.SetMagicDefense(target.MagicDefense + delta);
                break;
            case StatModifierType.CritChance:
                target.SetCritChance(target.CritChance + delta);
                break;
            case StatModifierType.CritDamage:
                target.SetCritMultiplier(target.CritMultiplier + delta);
                break;
            case StatModifierType.Dodge:
                target.SetDodge(target.Dodge + delta);
                break;
            case StatModifierType.FireResistance:
                target.AddElementalResistance(ElementType.Fire, delta);
                break;
            case StatModifierType.WaterResistance:
                target.AddElementalResistance(ElementType.Water, delta);
                break;
            case StatModifierType.EarthResistance:
                target.AddElementalResistance(ElementType.Earth, delta);
                break;
            case StatModifierType.NatureResistance:
                target.AddElementalResistance(ElementType.Nature, delta);
                break;
            case StatModifierType.LightningResistance:
                target.AddElementalResistance(ElementType.Lightning, delta);
                break;
            case StatModifierType.DarknessResistance:
                target.AddElementalResistance(ElementType.Darkness, delta);
                break;
            case StatModifierType.LightResistance:
                target.AddElementalResistance(ElementType.Light, delta);
                break;
            case StatModifierType.AllResistances:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    target.AddElementalResistance(e, delta);
                break;
            // ElementalPoint — SetElementalPoints() existe sur Entity (GDD §3.1) :
            // câblé en additif sur l'élément ciblé par le buff/debuff SO (debuffStatElement).
            // AttackSpeed : non géré sur Entity base — réservé CombatSystem.
        }
    }

    // =========================================================
    // ON APPLY BUFF
    // =========================================================

    private void OnApplyBuff(BuffInstance instance)
    {
        switch (instance.BuffType)
        {
            // ── Instantanés — pas de flag runtime ────────────────
            case BuffType.Heal:
                float healAmt = instance.BuffData.GetHealAmount(_entity.MaxHP);
                if (healAmt > 0f) _entity.Heal(healAmt);
                break;

            case BuffType.Purified:
                CleanseAllDebuffs();
                break;

            // ── Résurrection (Player uniquement) ─────────────────
            case BuffType.Revive:
                if (_entity is Player p)
                    p.Revive(instance.BuffData.reviveHPPercent, instance.BuffData.reviveManaPercent);
                break;

            // ── Bouclier — valeur stockée dans l'instance ────────
            case BuffType.Shield:
                instance.remainingShield = instance.BuffData.GetShieldAmount(_entity.MaxHP);
                break;

            // ── Flags spéciaux ────────────────────────────────────
            case BuffType.Invincible:
                isInvincible = true;
                break;

            case BuffType.Stealth:
                isStealthed = true;
                break;

            // ── Valeurs numériques (Stats) ────────────────────────
            case BuffType.Stats:
                RecalculateAndReapply();
                break;

            // Regeneration : tick dans BuffInstance.Tick — pas d'action à l'apply
        }
    }

    /// <summary>Supprime tous les debuffs actifs (Purified — §3.1.1.2).</summary>
    private void CleanseAllDebuffs()
    {
        var types = new List<DebuffType>(_activeDebuffs.Keys);
        foreach (DebuffType t in types)
            ExpireDebuff(t);
    }

    // =========================================================
    // EXPIRATION DEBUFF
    // =========================================================

    private void ExpireDebuff(DebuffType type)
    {
        if (!_activeDebuffs.TryGetValue(type, out _) ) return;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case DebuffType.Stun:    isStunned  = false; break;
            case DebuffType.Fear:    isFeared   = false; break;
            case DebuffType.Root:    isRooted   = false; break;
            case DebuffType.Sleep:   isSleeping = false; break;
            case DebuffType.Silence: isSilenced = false; break;
            case DebuffType.Taunt:   isTaunted  = false; break;
            case DebuffType.Freeze:  isFreezed  = false; break;

            // Flags dérivés de valeurs numériques — recalculés dans ReapplyActiveModifiers
            case DebuffType.Blind:      isBlinded     = false; break;
            case DebuffType.Poison:     isPoisoned    = false; break;
            case DebuffType.ArmorBreak: isArmorBroken = false; break;
            case DebuffType.Mark:       isMarked      = false; break;
        }

        // ⚠ Remove AVANT RecalculateAndReapply — sinon l'effet expiré est encore
        // dans le dict et ses valeurs sont ré-appliquées à tort.
        _activeDebuffs.Remove(type);

        // ── Valeurs numériques — recalcul propre ──────────────
        // Tout effet qui touche des stats ou des multiplicateurs numériques
        // déclenche un recalcul. Les flags purs (Stun, Fear, etc.) n'en ont pas besoin.
        switch (type)
        {
            case DebuffType.Slow:
            case DebuffType.Freeze:
            case DebuffType.Blind:
            case DebuffType.Poison:
            case DebuffType.ArmorBreak:
            case DebuffType.Shocked:
            case DebuffType.Mark:
            case DebuffType.Stats:
                RecalculateAndReapply();
                break;
        }
    }

    // =========================================================
    // EXPIRATION BUFF
    // =========================================================

    private void ExpireBuff(BuffType type)
    {
        if (!_activeBuffs.TryGetValue(type, out _)) return;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case BuffType.Invincible: isInvincible = false; break;
            case BuffType.Stealth:    isStealthed  = false; break;
        }

        // ⚠ Remove AVANT RecalculateAndReapply — même raison que pour les debuffs.
        _activeBuffs.Remove(type);

        // ── Valeurs numériques — recalcul propre ──────────────
        switch (type)
        {
            case BuffType.Stats:
                RecalculateAndReapply();
                break;
            // Purified, Heal : instantanés — pas d'expiration
            // Regeneration, Shield, Invincible, Stealth : pas de stat numérique à recalculer
        }
    }

    // =========================================================
    // BOUCLIER
    // =========================================================

    /// <summary>
    /// Absorbe les dégâts avec le bouclier actif (Shield). Retourne les dégâts résiduels.
    /// </summary>
    public float AbsorbWithShield(float incomingDamage)
    {
        if (_activeBuffs.TryGetValue(BuffType.Shield, out var shield))
        {
            incomingDamage = shield.AbsorbDamage(incomingDamage);
            if (shield.remainingShield <= 0f) ExpireBuff(BuffType.Shield);
        }

        return incomingDamage;
    }

    // =========================================================
    // SLEEP — réveil au premier dégât (GDD v3.5 §3.1.1.1)
    // =========================================================

    /// <summary>
    /// Appelé par Entity.TakeDamage — réveille l'entité si elle dort.
    /// Retourne true si les dégâts doivent être annulés (Invincible).
    /// </summary>
    public bool OnTakeDamage()
    {
        if (isInvincible) return true;

        if (isSleeping) ExpireDebuff(DebuffType.Sleep);

        // Stealth interrompue par dégât reçu (§3.1.1.3)
        if (isStealthed) ExpireBuff(BuffType.Stealth);

        return false;
    }

    // =========================================================
    // ACCESSEURS — lus par CombatSystem
    // =========================================================

    public bool HasDebuff(DebuffType type) => _activeDebuffs.ContainsKey(type);
    public bool HasBuff(BuffType type)     => _activeBuffs.ContainsKey(type);

    public float GetBuffDefenseBonus()      => buffDefenseBonus;
    public float GetBuffDodgeBonus()        => buffDodgeBonus;
    public float GetBuffPrecisionBonus()     => buffPrecisionBonus;
    public float GetBuffSpeedMultiplier()   => buffSpeedMultiplier;
    public float GetBuffAttackBonus()       => buffAttackBonus;
    public float GetBuffCritChanceBonus()   => buffCritChanceBonus;
    public float GetBuffCritDamageBonus()   => buffCritDamageBonus;
    public float GetMarkDamageBonus()       => isMarked ? markDamageBonus : 0f;
    public float GetBlindMalus()            => blindPrecisionMalus;
    public float GetShockDefenseReduction() => shockDefenseReduction;
    public float GetArmorBreakReduction()   => isArmorBroken ? armorBreakReduction : 0f;
    public float GetPoisonHealReduction()   => isPoisoned ? poisonHealReduction : 0f;
    public float GetBarrierElementResist()  => barrierElementResist;

    // =========================================================
    // RÉSISTANCES AUX DEBUFFS
    // =========================================================

    public void SetDebuffResistance(DebuffType type, float value)
        => _debuffResistances[type] = Mathf.Clamp01(value);

    public float GetDebuffResistance(DebuffType type)
        => _debuffResistances.TryGetValue(type, out float v) ? v : 0f;

    public void ResetDebuffResistances() => _debuffResistances.Clear();

    // =========================================================
    // UI — données pour StatusEffectUI
    // =========================================================

    /// <summary>
    /// Retourne la liste de tous les effets actifs pour l'affichage UI.
    /// Appelé par StatusEffectUI chaque frame.
    /// </summary>
    public List<StatusEffectUIEntry> GetActiveEffectsForUI()
    {
        var list = new List<StatusEffectUIEntry>();

        foreach (var kvp in _activeDebuffs)
            list.Add(new StatusEffectUIEntry
            {
                key           = kvp.Key.ToString(),
                icon          = kvp.Value.data.icon,
                remainingTime = kvp.Value.remainingTime,
                totalDuration = kvp.Value.data.duration,
                isDebuff      = true
            });

        foreach (var kvp in _activeBuffs)
            list.Add(new StatusEffectUIEntry
            {
                key           = kvp.Key.ToString(),
                icon          = kvp.Value.data.icon,
                remainingTime = kvp.Value.remainingTime,
                totalDuration = kvp.Value.data.duration,
                isDebuff      = false
            });

        return list;
    }

}

