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

    /// <summary>Knockback = repoussement physique (PAS un étourdissement en soi) + un
    /// mini-stun ponctuel type Shocked (0.5s, mouvement + skills bloqués) le temps du recul.
    /// Volontairement SÉPARÉ du pipeline DebuffData normal (_activeDebuffs) — sa propre
    /// minuterie indépendante, pour ne jamais risquer de couper prématurément un vrai Stun/
    /// Shocked actif en parallèle (ou l'inverse). Voir Entity.ApplyKnockBack().</summary>
    public bool isKnockedBack { get; private set; } = false;
    private Coroutine _knockbackCoroutine;

    /// <summary>Applique le mini-stun ponctuel du Knockback pour `duration` secondes —
    /// appelé UNIQUEMENT par Entity.ApplyKnockBack() pendant le recul physique. N'écrit
    /// jamais isStunned — indépendant du pipeline DebuffData.</summary>
    public void ApplyKnockbackStun(float duration)
    {
        if (_knockbackCoroutine != null) StopCoroutine(_knockbackCoroutine);
        isKnockedBack = true;
        _knockbackCoroutine = StartCoroutine(ClearKnockbackAfter(duration));
    }

    private System.Collections.IEnumerator ClearKnockbackAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        isKnockedBack = false;
        _knockbackCoroutine = null;
    }

    /// <summary>Source (caster) du debuff actif de ce type, ou null si absent/inconnu — utilisé
    /// par Fear pour fuir dans la direction opposée à qui a lancé le debuff (fallback aléatoire
    /// si null, voir PlayerController.HandleMovement).</summary>
    public Entity GetDebuffSource(DebuffType type)
        => _activeDebuffs.TryGetValue(type, out var instance) ? instance.source : null;

#pragma warning disable CS0618 // Blinded — obsolète (utiliser Stats+Precision), gardé pour compat assets existants
    /// <summary>Blinded — réduit la précision.</summary>
    public bool isBlinded    { get; private set; } = false;
#pragma warning restore CS0618

    /// <summary>Poisoned — DoT + réduction soins reçus.</summary>
    public bool isPoisoned   { get; private set; } = false;

#pragma warning disable CS0618 // ArmorBreak obsolète — gardé pour compat assets existants
    /// <summary>ArmorBreak — réduction de défense % temporaire.</summary>
    public bool isArmorBroken { get; private set; } = false;
#pragma warning restore CS0618

    /// <summary>Sleeping — immobilisé jusqu'au premier dégât reçu.</summary>
    public bool isSleeping   { get; private set; } = false;

    /// <summary>Freeze — immobilisation totale (hard CC). Réattribué Eau v3.0 — §3.1.1.1.</summary>
    public bool isFreezed    { get; private set; } = false;

    /// <summary>Shocked — identique à Stun (bloque toutes les actions), flag séparé pour ne
    /// jamais couper prématurément un Stun actif en parallèle ou l'inverse.</summary>
    public bool isShocked    { get; private set; } = false;

    /// <summary>Silence — bloque l'utilisation des skills.</summary>
    public bool isSilenced   { get; private set; } = false;

    /// <summary>Taunted — force les ennemis à cibler cette entité (attaque basique seulement en PvP). §3.1.1.1.</summary>
    public bool isTaunted    { get; private set; } = false;

    /// <summary>Marked — cible marquée, reçoit des dégâts supplémentaires (valeur numérique
    /// routée dans l'accumulateur FinalDamageReduction, pas ici — voir ReapplyActiveModifiers).</summary>
    public bool isMarked     { get; private set; } = false;

    // Valeurs numériques debuff
    public float slowMultiplier        { get; private set; } = 1f;
    public float poisonHealReduction   { get; private set; } = 0f;
#pragma warning disable CS0618 // ArmorBreak obsolète — gardé pour compat assets existants
    public float armorBreakReduction   { get; private set; } = 0f;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Blind obsolète — gardé pour compat assets existants
    public float blindPrecisionMalus   { get; private set; } = 0f;
#pragma warning restore CS0618

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

        // Tick debuffs (DoT, ManaDrain via DebuffInstance.Tick) — snapshot AVANT d'itérer :
        // Tick() peut infliger des dégâts (DoT) qui tuent l'entité, ce qui appelle Die() →
        // ClearAllEffects() → _activeDebuffs.Remove(...) SYNCHRONE pendant qu'on est encore en
        // train d'énumérer ce même dictionnaire ("Collection was modified" crash, vécu en test —
        // un DoT assez fort pour achever la cible pendant son propre tick). Itérer une copie
        // rend le dictionnaire live libre d'être muté pendant le Tick() ; les instances
        // restantes de la copie no-op proprement ensuite (DebuffInstance.Tick vérifie déjà
        // target.isDead en tête).
        var expiredDebuffs = new List<DebuffType>();
        foreach (var kvp in new List<KeyValuePair<DebuffType, DebuffInstance>>(_activeDebuffs))
        {
            kvp.Value.Tick(_entity, Time.deltaTime);
            if (kvp.Value.IsExpired) expiredDebuffs.Add(kvp.Key);
        }
        foreach (var t in expiredDebuffs) ExpireDebuff(t);

        // Tick buffs (Regeneration via BuffInstance.Tick) — même précaution, voir ci-dessus.
        var expiredBuffs = new List<BuffType>();
        foreach (var kvp in new List<KeyValuePair<BuffType, BuffInstance>>(_activeBuffs))
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

        // Refresh si déjà actif — GDD v3.5 §3.1.1.4 : le plus récent remplace l'ancien.
        // Source aussi mise à jour (pas juste la durée) — sinon un 2e caster qui relance le
        // même debuff (ex: Fear) laisse GetDebuffSource() pointer vers le PREMIER caster,
        // périmé (ex: fuite dans la mauvaise direction).
        if (_activeDebuffs.TryGetValue(debuff.debuffType, out var existing))
        {
            existing.Refresh();
            existing.source = source;
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

        // Refresh si déjà actif.
        if (_activeBuffs.TryGetValue(buff.buffType, out var existing))
        {
            existing.Refresh();
            existing.source = source; // Revive : re-cast met à jour QUI recevra le crédit/log au déclenchement
            return;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        _activeBuffs[buff.buffType] = instance;
        OnApplyBuff(instance);
    }

    /// <summary>Applique/rafraîchit un buff avec une durée EXPLICITE, ignorant BuffData.duration
    /// — utilisé par les talismans : le temps restant affiché (PlayerEffectPanel lit déjà
    /// remainingTime) doit refléter le vrai temps de vie restant du talisman
    /// (TalismanInstance.RemainingSeconds), pas un duration fixe sur l'asset. Le buff expire
    /// alors naturellement en même temps que le talisman, sans réglage manuel à synchroniser.</summary>
    public void ApplyBuffWithDuration(BuffData buff, Entity source, float remainingSeconds)
    {
        if (buff == null || _entity.isDead || remainingSeconds <= 0f) return;

        if (_activeBuffs.TryGetValue(buff.buffType, out var existing))
        {
            existing.remainingTime = remainingSeconds;
            existing.source = source;
            return;
        }

        BuffInstance instance = (BuffInstance)buff.CreateInstance(source);
        instance.remainingTime = remainingSeconds;
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
            case DebuffType.Shocked: isShocked  = true; break; // identique à Stun, flag séparé
            case DebuffType.Silence: isSilenced = true; break;
            case DebuffType.Taunt:   isTaunted  = true; break;

            case DebuffType.Freeze:
                isFreezed = true;
                slowMultiplier = 0f;
                break;

#pragma warning disable CS0618 // Blind obsolète (utiliser Stats+Precision) — gardé pour compat assets existants
            case DebuffType.Blind:
                isBlinded = true;
                blindPrecisionMalus += instance.DebuffData.debuffValue;
                break;
#pragma warning restore CS0618

#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:
                // §3.1.1.1 — DoT (tick) + réduction soins % (local)
                isPoisoned = true;
                poisonHealReduction += instance.DebuffData.healReduction;
                break;
#pragma warning restore CS0618

#pragma warning disable CS0618 // ArmorBreak obsolète (utiliser Stats+Defense) — gardé pour compat assets existants
            case DebuffType.ArmorBreak:
                // réduction défense % (lue dans Entity.GetMeleeDefense etc.)
                isArmorBroken = true;
                armorBreakReduction += instance.DebuffData.defenseReduction;
                break;
#pragma warning restore CS0618

            case DebuffType.Slow:
                slowMultiplier = Mathf.Min(slowMultiplier, instance.DebuffData.slowMultiplier);
                break;

            case DebuffType.Mark:
                // Flag pour l'UI/status — la valeur numérique (% dégâts subis) est routée dans
                // l'accumulateur FinalDamageReduction par ReapplyActiveModifiers ci-dessous.
                isMarked = true;
                RecalculateAndReapply();
                break;

            // ── Stats — modifie directement les champs Entity via Recalculate ──
            case DebuffType.Stats:
                // §3.1.1.1 — recalcul propre + ré-application de tous les modificateurs actifs
                RecalculateAndReapply();
                break;

            case DebuffType.Dispel:
                // Retire les buffs actifs DE CETTE ENTITÉ (celle sur qui ce debuff Dispel a
                // été appliqué — le ciblage ennemi est géré côté skill/appelant), jet
                // indépendant par buff actif.
                RemoveBuffsByChance(instance.DebuffData.chancePerEffect);
                break;

            // ManaDrain, HpDrain : tick dans DebuffInstance.Tick — pas de flag local
            // Knockback           : effet ponctuel — Entity.ApplyKnockBack()
            // Bleed               : DoT pur — tick dans DebuffInstance.Tick, pas de flag
        }

        // bonusStats s'applique quel que soit debuffType — sans recalcul ici, un debuff dont
        // le type principal ne déclenche pas déjà RecalculateAndReapply (Stun, Slow...) ne
        // verrait jamais ses bonusStats appliqués. Appel idempotent, sans effet si déjà fait
        // par le case Stats ci-dessus.
        if (instance.DebuffData.bonusStats != null && instance.DebuffData.bonusStats.Count > 0)
            RecalculateAndReapply();
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

    /// <summary>Stats sans sélecteur Flat/% (masqué côté Inspector via ShowIf) — toujours
    /// additives, jamais de multiplication par (1+%) DANS l'accumulation (AccumulateStatLine y
    /// route toute contribution en Flat, quel que soit le mode fourni — filet de sécurité en
    /// plus du masquage Inspector). CritChance/CritDamage/Résistances/XPBonus/GoldBonus sont
    /// nativement stockées en ratio (0.05 = 5%) — additionner leur valeur telle quelle EST déjà
    /// un %, rien de spécial à faire à l'application. MoveSpeed est un cas à part : stockée en
    /// unités brutes (ex: 5.0/s), donc sa valeur accumulée ici est réinterprétée comme un % DE
    /// LA BASE au moment de l'application finale (voir le cas spécial StatModifierType.MoveSpeed
    /// dans ReapplyActiveModifiers, même traitement que FinalDamageBonus/Reduction juste
    /// au-dessus) — décision explicite Florian (2026-09-06) : "-0.3" doit vouloir dire -30% de
    /// la vitesse de base, pas -0.3 unité brute.</summary>
    private static readonly HashSet<StatModifierType> ExceptionStats = new HashSet<StatModifierType>
    {
        StatModifierType.CritChance, StatModifierType.CritDamage,
        StatModifierType.FireResistance, StatModifierType.WaterResistance,
        StatModifierType.EarthResistance, StatModifierType.NatureResistance,
        StatModifierType.LightningResistance, StatModifierType.DarknessResistance,
        StatModifierType.LightResistance, StatModifierType.AllResistances,
        StatModifierType.MoveSpeed, StatModifierType.XPBonus, StatModifierType.GoldBonus,
        StatModifierType.SpiritXpBonus,
        // ElementalPoint (obsolète) retiré — remplacé par ElementalPointFire/.../All, traités
        // comme des stats NORMALES (Base+Flat)×(1+%), PAS des exceptions — demande explicite
        // Florian, divergence volontaire avec StatType.PointsX côté équipement (resté exception).
    };

    /// <summary>
    /// Ré-applique tous les modificateurs numériques des effets actifs sur l'entité.
    /// Appelé par Entity.RequestRecalculate() après restauration des stats de base.
    /// Ne touche PAS aux flags booléens (isStunned, etc.) — déjà corrects.
    /// </summary>
    public void ReapplyActiveModifiers(Entity target)
    {
        // Snapshot des stats "pures" — juste restaurées depuis _base par RequestRecalculate,
        // AVANT que cette méthode n'ajoute le moindre modificateur ce cycle-ci. Référence
        // stable pour tous les % actifs — sans ça, plusieurs % indépendants ciblant la même
        // stat composeraient entre eux au lieu de s'additionner, et le résultat dépendrait de
        // l'ordre d'itération de _activeBuffs/_activeDebuffs (non garanti par Dictionary).
        var pureBase = new Dictionary<StatModifierType, float>();
        foreach (StatModifierType s in System.Enum.GetValues(typeof(StatModifierType)))
            pureBase[s] = GetBaseStatValue(target, s);

        // ── Debuffs numériques ────────────────────────────────
        poisonHealReduction   = 0f;
        slowMultiplier        = 1f;
#pragma warning disable CS0618 // ArmorBreak/Blind obsolètes — gardés pour compat assets existants
        armorBreakReduction   = 0f;
        blindPrecisionMalus   = 0f;
#pragma warning restore CS0618

        var flatSum    = new Dictionary<StatModifierType, float>();
        var percentSum = new Dictionary<StatModifierType, float>();

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
#pragma warning disable CS0618 // Blind obsolète (utiliser Stats+Precision) — gardé pour compat assets existants
                case DebuffType.Blind:
                    blindPrecisionMalus += d.debuffValue;
                    break;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
                case DebuffType.Poison:
                    poisonHealReduction += d.healReduction;
                    break;
#pragma warning restore CS0618
#pragma warning disable CS0618 // ArmorBreak obsolète (utiliser Stats+Defense) — gardé pour compat assets existants
                case DebuffType.ArmorBreak:
                    armorBreakReduction += d.defenseReduction;
                    break;
#pragma warning restore CS0618
                case DebuffType.Mark:
                    // Routé dans l'accumulateur FinalDamageReduction (négatif = +dégâts subis) —
                    // compose avec les autres sources au lieu d'être un multiplicateur séparé.
                    AccumulateStatLine(flatSum, percentSum, StatModifierType.FinalDamageReduction,
                        true, -d.markDamageBonusPercent);
                    break;
                case DebuffType.Stats:
                    AccumulateStatLine(flatSum, percentSum, d.debuffStatType,
                        d.debuffModifier == ModifierType.Percent, -d.debuffValue);
                    break;
            }
            AccumulateBonusStatsLines(flatSum, percentSum, d.bonusStats, sign: -1f);
        }

        // ── Buffs numériques ──────────────────────────────────
        buffDefenseBonus    = 0f;
        buffDodgeBonus      = 0f;
        buffPrecisionBonus  = 0f;
        buffSpeedMultiplier = 1f;
        buffAttackBonus     = 0f;
        buffCritChanceBonus = 0f;
        buffCritDamageBonus = 0f;
        barrierElementResist = 0f;

        // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp
        // retirés (2026) — voir accumulateurs ci-dessus, jamais réécrits, sans effet.
        foreach (var kvp in _activeBuffs)
        {
            if (kvp.Key == BuffType.Stats)
            {
                var b = kvp.Value.BuffData;
                AccumulateStatLine(flatSum, percentSum, b.buffStatType,
                    b.buffModifier == ModifierType.Percent, b.buffStatValue);
            }
            AccumulateBonusStatsLines(flatSum, percentSum, kvp.Value.BuffData.bonusStats, sign: 1f);
        }

        // ── Application finale — une fois par stat ─────────────
        foreach (StatModifierType stat in System.Enum.GetValues(typeof(StatModifierType)))
        {
            float flat = flatSum.TryGetValue(stat, out var f) ? f : 0f;
            float pct  = percentSum.TryGetValue(stat, out var p) ? p : 0f;
            if (flat == 0f && pct == 0f) continue;

            // FinalDamageBonus/Reduction — accumulateurs BRUTS, pas la formule (Base+Flat)×
            // (1+%) : le mode choisi route déjà flat/pct vers le bon accumulateur Entity, rien
            // à multiplier, rien à passer par ModifyEntityStat (qui n'a pas de case pour eux).
            if (stat == StatModifierType.FinalDamageBonus)
            {
                target.SetFinalDamageBonusFlat   (target.FinalDamageBonusFlat    + flat);
                target.SetFinalDamageBonusPercent(target.FinalDamageBonusPercent + pct);
                continue;
            }
            if (stat == StatModifierType.FinalDamageReduction)
            {
                target.SetFinalDamageReductionFlat   (target.FinalDamageReductionFlat    + flat);
                target.SetFinalDamageReductionPercent(target.FinalDamageReductionPercent + pct);
                continue;
            }

            // MoveSpeed — exception au sens "toujours en %", PAS "toujours flat" comme
            // CritChance/Résistances : ces derniers sont nativement stockés en ratio (0.05 =
            // 5%), donc les additionner tel quel EST déjà un %. MoveSpeed est stockée en
            // unités brutes (ex: 5.0/s) — additionner -0.3 telle quelle retire 0.3 unité, pas
            // 30%. La valeur (routée dans flatSum par AccumulateStatLine, comme toute
            // ExceptionStats) est donc interprétée ici comme un pourcentage DE LA BASE, pas un
            // delta brut — décision explicite Florian (2026-09-06).
            if (stat == StatModifierType.MoveSpeed)
            {
                float baseSpeed = pureBase[stat];
                ModifyEntityStat(target, stat, baseSpeed * flat);
                continue;
            }

            float baseVal = pureBase[stat];
            float final   = (baseVal + flat) * (1f + pct);
            ModifyEntityStat(target, stat, final - baseVal);
        }
    }

    /// <summary>Accumule les lignes bonusStats d'UN effet actif (Buff ou Debuff) dans les
    /// sommes globales — plus de logique 2-passes par-effet (PercentOfBase/PercentOfFinal),
    /// juste un ajout à la somme Flat ou % de la stat ciblée. `sign` (-1 pour un debuff, +1
    /// pour un buff) inverse le signe — un debuff RETIRE, jamais besoin de valeurs négatives
    /// côté designer.</summary>
    private void AccumulateBonusStatsLines(Dictionary<StatModifierType, float> flatSum,
        Dictionary<StatModifierType, float> percentSum, List<StatLine> lines, float sign)
    {
        if (lines == null) return;
        foreach (var line in lines)
        {
            if (line == null) continue;
            AccumulateStatLine(flatSum, percentSum, line.stat,
                line.mode == StatLineMode.Percent, sign * line.value);
        }
    }

    /// <summary>Route une contribution (Flat ou %) dans les sommes globales par stat.
    /// `AllDefense` n'est jamais stockée comme cible : répartie sur les 3 défenses réelles,
    /// chacune calculera son propre résultat avec sa propre base. Les stats de `ExceptionStats`
    /// tombent toujours en Flat, quel que soit `isPercent` (filet de sécurité — le champ mode
    /// est de toute façon masqué côté Inspector pour elles).</summary>
    private void AccumulateStatLine(Dictionary<StatModifierType, float> flatSum,
        Dictionary<StatModifierType, float> percentSum, StatModifierType stat, bool isPercent,
        float value)
    {
        if (stat == StatModifierType.AllDefense)
        {
            AccumulateStatLine(flatSum, percentSum, StatModifierType.MeleeDefense, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.RangedDefense, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.MagicDefense, isPercent, value);
            return;
        }

        if (stat == StatModifierType.ElementalPointAll)
        {
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointFire, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointWater, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointLightning, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointEarth, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointNature, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointDarkness, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.ElementalPointLight, isPercent, value);
            return;
        }

        if (isPercent && !ExceptionStats.Contains(stat))
            percentSum[stat] = percentSum.TryGetValue(stat, out var p) ? p + value : value;
        else
            flatSum[stat] = flatSum.TryGetValue(stat, out var f) ? f + value : value;
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
            // XPBonus/GoldBonus : pas de "stat" existante à multiplier, la valeur EST déjà le
            // bonus (0.20 = +20%) — base neutre 1f pour que Flat ET Percent donnent le même
            // résultat correct (value × 1 = value), aucun piège de dropdown pour le designer.
            case StatModifierType.XPBonus:
            case StatModifierType.GoldBonus:
            case StatModifierType.SpiritXpBonus:   return 1f;
            case StatModifierType.ElementalPointFire:      return target.GetElementalPoints(ElementType.Fire);
            case StatModifierType.ElementalPointWater:     return target.GetElementalPoints(ElementType.Water);
            case StatModifierType.ElementalPointLightning: return target.GetElementalPoints(ElementType.Lightning);
            case StatModifierType.ElementalPointEarth:     return target.GetElementalPoints(ElementType.Earth);
            case StatModifierType.ElementalPointNature:    return target.GetElementalPoints(ElementType.Nature);
            case StatModifierType.ElementalPointDarkness:  return target.GetElementalPoints(ElementType.Darkness);
            case StatModifierType.ElementalPointLight:     return target.GetElementalPoints(ElementType.Light);
            // ElementalPointAll : jamais stockée elle-même, toujours répartie sur les 7 ci-dessus
            // au moment de l'accumulation (AccumulateStatLine) — jamais transmise ici.
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
            case StatModifierType.XPBonus:
                target.SetXPBonusPercent(target.XPBonusPercent + delta);
                break;
            case StatModifierType.GoldBonus:
                target.SetGoldBonusPercent(target.GoldBonusPercent + delta);
                break;
            case StatModifierType.SpiritXpBonus:
                target.SetSpiritXpBonusPercent(target.SpiritXpBonusPercent + delta);
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
            case StatModifierType.ElementalPointFire:
                target.SetElementalPoints(ElementType.Fire, target.GetElementalPoints(ElementType.Fire) + delta);
                break;
            case StatModifierType.ElementalPointWater:
                target.SetElementalPoints(ElementType.Water, target.GetElementalPoints(ElementType.Water) + delta);
                break;
            case StatModifierType.ElementalPointLightning:
                target.SetElementalPoints(ElementType.Lightning, target.GetElementalPoints(ElementType.Lightning) + delta);
                break;
            case StatModifierType.ElementalPointEarth:
                target.SetElementalPoints(ElementType.Earth, target.GetElementalPoints(ElementType.Earth) + delta);
                break;
            case StatModifierType.ElementalPointNature:
                target.SetElementalPoints(ElementType.Nature, target.GetElementalPoints(ElementType.Nature) + delta);
                break;
            case StatModifierType.ElementalPointDarkness:
                target.SetElementalPoints(ElementType.Darkness, target.GetElementalPoints(ElementType.Darkness) + delta);
                break;
            case StatModifierType.ElementalPointLight:
                target.SetElementalPoints(ElementType.Light, target.GetElementalPoints(ElementType.Light) + delta);
                break;
            // ElementalPointAll — jamais reçue ici, toujours répartie sur les 7 ci-dessus au
            // moment de l'accumulation (AccumulateStatLine), chacune avec sa propre base.
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
                RemoveDebuffsByChance(instance.BuffData.chancePerEffect);
                break;

            // Revive : ne fait RIEN au cast — buff dormant, consommé uniquement par
            // Player.Die() via TryConsumeRevive() si les HP tombent à 0 pendant sa durée.

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
            // MaxHP/MaxMana : un gain de max doit aussi combler le courant d'autant
            // (sinon le joueur doit regen pour en profiter) — mesuré avant/après le
            // recalcul plutôt que recalculé à la main (robuste Flat/Percent). Fait
            // UNIQUEMENT ici (application, une fois) — jamais dans ModifyEntityStat/
            // ReapplyActiveModifiers, qui tourne à CHAQUE recalcul (équipement, level up...)
            // tant que le buff reste actif ; y ajouter le heal soignerait en boucle.
            case BuffType.Stats:
                if (instance.BuffData.buffStatType == StatModifierType.MaxHP)
                {
                    float before = _entity.MaxHP;
                    RecalculateAndReapply();
                    float gained = _entity.MaxHP - before;
                    if (gained > 0f) _entity.Heal(gained);
                }
                else if (instance.BuffData.buffStatType == StatModifierType.MaxMana)
                {
                    float before = _entity.MaxMana;
                    RecalculateAndReapply();
                    float gained = _entity.MaxMana - before;
                    if (gained > 0f) _entity.RecoverMana(gained);
                }
                else
                {
                    RecalculateAndReapply();
                }
                break;

            // Regeneration : tick dans BuffInstance.Tick — pas d'action à l'apply
        }

        // bonusStats s'applique quel que soit buffType — sans recalcul ici, un buff dont le
        // type principal ne déclenche pas déjà RecalculateAndReapply (Heal, Shield...) ne
        // verrait jamais ses bonusStats appliqués. Même discipline avant/après que le case
        // Stats ci-dessus pour MaxHP/MaxMana (combler le nouveau plafond, pas juste l'augmenter
        // à vide) — idempotent, sans effet si déjà fait par le case Stats.
        if (instance.BuffData.bonusStats != null && instance.BuffData.bonusStats.Count > 0)
        {
            float beforeHP   = _entity.MaxHP;
            float beforeMana = _entity.MaxMana;
            RecalculateAndReapply();
            float gainedHP   = _entity.MaxHP   - beforeHP;
            float gainedMana = _entity.MaxMana - beforeMana;
            if (gainedHP   > 0f) _entity.Heal(gainedHP);
            if (gainedMana > 0f) _entity.RecoverMana(gainedMana);
        }
    }

    /// <summary>Purify — retire chaque debuff actif avec un jet INDÉPENDANT par effet (pas un
    /// seul jet global) — 4 debuffs à 50% ≠ "50% de tout enlever d'un coup" (§3.1.1.2).</summary>
    private void RemoveDebuffsByChance(float chance)
    {
        var types = new List<DebuffType>(_activeDebuffs.Keys);
        foreach (DebuffType t in types)
            if (Random.value < chance)
                ExpireDebuff(t);
    }

    /// <summary>Dispel — même principe que RemoveDebuffsByChance, mais sur les buffs actifs de
    /// LA CIBLE sur qui ce debuff Dispel a été appliqué (§3.1.1.3).</summary>
    private void RemoveBuffsByChance(float chance)
    {
        var types = new List<BuffType>(_activeBuffs.Keys);
        foreach (BuffType t in types)
            if (Random.value < chance)
                ExpireBuff(t);
    }

    // =========================================================
    // EXPIRATION DEBUFF
    // =========================================================

    private void ExpireDebuff(DebuffType type)
    {
        if (!_activeDebuffs.TryGetValue(type, out var expiring)) return;
        var expiringBonusStats = expiring.DebuffData.bonusStats;

        // ── Flags booléens — retirés manuellement ────────────
        switch (type)
        {
            case DebuffType.Stun:    isStunned  = false; break;
            case DebuffType.Fear:    isFeared   = false; break;
            case DebuffType.Root:    isRooted   = false; break;
            case DebuffType.Sleep:   isSleeping = false; break;
            case DebuffType.Shocked: isShocked  = false; break;
            case DebuffType.Silence: isSilenced = false; break;
            case DebuffType.Taunt:   isTaunted  = false; break;
            case DebuffType.Freeze:  isFreezed  = false; break;

            // Flags dérivés de valeurs numériques — recalculés dans ReapplyActiveModifiers
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:      isBlinded     = false; break;
            case DebuffType.ArmorBreak: isArmorBroken = false; break;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:     isPoisoned    = false; break;
#pragma warning restore CS0618
            case DebuffType.Mark:       isMarked      = false; break;
        }

        // ⚠ Remove AVANT RecalculateAndReapply — sinon l'effet expiré est encore
        // dans le dict et ses valeurs sont ré-appliquées à tort.
        _activeDebuffs.Remove(type);

        // ── Valeurs numériques — recalcul propre ──────────────
        // Tout effet qui touche des stats ou des multiplicateurs numériques
        // déclenche un recalcul. Les flags purs (Stun, Fear, Shocked...) n'en ont pas besoin.
        switch (type)
        {
            case DebuffType.Slow:
            case DebuffType.Freeze:
#pragma warning disable CS0618 // Blind/ArmorBreak obsolètes — gardés pour compat assets existants
            case DebuffType.Blind:
            case DebuffType.ArmorBreak:
#pragma warning restore CS0618
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
            case DebuffType.Poison:
#pragma warning restore CS0618
            case DebuffType.Mark:
            case DebuffType.Stats:
                RecalculateAndReapply();
                return; // déjà fait — évite le 2e appel juste en dessous (idempotent de toute
                        // façon, mais inutile de le refaire tout de suite après)
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL debuffType (Fear, Stun, Root...), pas
        // seulement les types déjà listés ci-dessus — sans ce filet, un debuff Fear avec un
        // bonusStats -MoveSpeed resterait appliqué POUR TOUJOURS après expiration (aucun des
        // cases ci-dessus ne le recalcule), jusqu'à ce qu'un événement sans rapport
        // (équipement, level up...) déclenche un recalcul complet par ailleurs.
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
    }

    // =========================================================
    // EXPIRATION BUFF
    // =========================================================

    /// <summary>Retire un buff actif spécifique immédiatement, sans jet — ex: talisman retiré
    /// avant sa propre expiration naturelle (voir Player.UnequipTalisman). Contrairement à
    /// RemoveBuffsByChance/RemoveDebuffsByChance (Purify/Dispel), cible un seul type, toujours.</summary>
    public void RemoveBuff(BuffType type)
    {
        if (_activeBuffs.ContainsKey(type))
            ExpireBuff(type);
    }

    /// <summary>Consomme le buff Revive actif (s'il y en a un) — appelé par Player.Die() AVANT
    /// le wipe généralisé des effets (ClearAllEffects), sinon ses valeurs seraient perdues.
    /// Retourne false si aucun Revive actif (mort normale, pas de résurrection).</summary>
    public bool TryConsumeRevive(out float delay, out float hpPercent, out float manaPercent)
    {
        if (_activeBuffs.TryGetValue(BuffType.Revive, out var instance))
        {
            var d = instance.BuffData;
            delay = d.reviveDelay;
            hpPercent = d.reviveHPPercent;
            manaPercent = d.reviveManaPercent;
            _activeBuffs.Remove(BuffType.Revive);
            return true;
        }
        delay = hpPercent = manaPercent = 0f;
        return false;
    }

    /// <summary>Retire TOUS les buffs et debuffs actifs immédiatement — appelé à la mort
    /// (Entity.Die()) : "si mort, alors perd tout les buffs/debuffs" (décision explicite
    /// Florian). Repasse par ExpireBuff/ExpireDebuff pour chaque effet (pas un Clear() direct
    /// des dictionnaires) pour que les flags booléens et le recalcul de stats restent
    /// cohérents, exactement comme une expiration naturelle.</summary>
    public void ClearAllEffects()
    {
        var debuffKeys = new List<DebuffType>(_activeDebuffs.Keys);
        foreach (var t in debuffKeys) ExpireDebuff(t);

        var buffKeys = new List<BuffType>(_activeBuffs.Keys);
        foreach (var t in buffKeys) ExpireBuff(t);
    }

    private void ExpireBuff(BuffType type)
    {
        if (!_activeBuffs.TryGetValue(type, out var expiring)) return;
        var expiringBonusStats = expiring.BuffData.bonusStats;

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
                return; // déjà fait — voir même remarque que ExpireDebuff
            // Purified, Heal : instantanés — pas d'expiration
            // Regeneration, Shield, Invincible, Stealth : pas de stat numérique à recalculer
        }

        // bonusStats peut être posé sur N'IMPORTE QUEL buffType (Heal, Shield, Talisman...),
        // pas seulement Stats — sans ce filet, ses bonus resteraient appliqués POUR TOUJOURS
        // après expiration (voir même remarque que ExpireDebuff ci-dessus).
        if (expiringBonusStats != null && expiringBonusStats.Count > 0)
            RecalculateAndReapply();
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
#pragma warning disable CS0618 // Blind obsolète — gardé pour compat assets existants
    public float GetBlindMalus()            => blindPrecisionMalus;
#pragma warning restore CS0618
#pragma warning disable CS0618 // ArmorBreak obsolète — gardé pour compat assets existants
    public float GetArmorBreakReduction()   => isArmorBroken ? armorBreakReduction : 0f;
#pragma warning restore CS0618
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

