using UnityEngine;

// =============================================================
// StatusEffectInstance — base runtime d'un effet actif sur une entité
// Path : Assets/Scripts/Data/StatusEffect/StatusEffectInstance.cs
// AetherTree GDD v3.5 — §3.1.1
//
// v3.5 — BuffInstance.Tick() gère Regeneration directement (suppression du
//         chemin buffRegenBonus dans StatusEffectSystem.Update()).
//         critChanceBonus et critDamageBonus gérés séparément.
// =============================================================

public abstract class StatusEffectInstance
{
    public StatusEffectData data;
    public Entity           source;
    public float            remainingTime;

    public bool IsExpired => remainingTime <= 0f;

    protected StatusEffectInstance(StatusEffectData data, Entity source)
    {
        this.data     = data;
        this.source   = source;
        remainingTime = data.duration;
    }

    /// <summary>Remet la durée à zéro — même effet appliqué à nouveau.</summary>
    public void Refresh() => remainingTime = data.duration;

    /// <summary>Tick appelé par StatusEffectSystem chaque frame.</summary>
    public abstract void Tick(Entity target, float deltaTime);
}

// =============================================================
// DebuffInstance — runtime d'un debuff actif
// =============================================================
public class DebuffInstance : StatusEffectInstance
{
    public DebuffData DebuffData => (DebuffData)data;
    public DebuffType DebuffType => DebuffData.debuffType;

    public DebuffInstance(DebuffData data, Entity source) : base(data, source) { }

    public override void Tick(Entity target, float deltaTime)
    {
        if (target == null || target.isDead) return;

        remainingTime -= deltaTime;

        switch (DebuffType)
        {
#pragma warning disable CS0618 // Burn/Poison/Bleed obsolètes — gardés pour compat assets existants
            case DebuffType.Burn:
            case DebuffType.Bleed:
            case DebuffType.Poison:
#pragma warning restore CS0618
            case DebuffType.Dot:
                // Poison — le flag healReduction est géré à part via OnApply/OnExpire
                // dans StatusEffectSystem, indépendant du calcul de dégâts ici.
                float dmg = ComputeDotDps(target) * deltaTime;
                if (dmg > 0f)
                    target.TakeDamage(dmg, DebuffData.damageElement, source);
                break;

            case DebuffType.ManaDrain:
                // Drain de mana progressif sur la durée (§3.1.1.1)
                float drain = DebuffData.manaDrainPerSecond * deltaTime;
                if (drain > 0f)
                    target.SpendMana(drain);
                break;

            // Freeze, Slow, Root, Stun, Fear, Sleep, Shocked, Silence, Taunt :
            // gérés via flags sur StatusEffectSystem (OnApply / OnExpire)
            // Knockback : effet ponctuel — géré via Entity.ApplyKnockBack()
        }
    }

    /// <summary>Dégâts/s d'un DoT (Dot, + Burn/Poison/Bleed obsolètes gardés pour compat) —
    /// 2 termes, pas de socle séparé : rang élémentaire de la SOURCE × rankDamagePercent (%
    /// du MaxHP cible, plafonné naturellement par le rang max 5) + points élémentaires
    /// EFFECTIFS (brut + bonus de rang, même valeur que CombatSystem/l'UI — GetEffectiveElementPoints)
    /// de la source × elementalPointsMultiplier (flat, volontairement sans plafond —
    /// l'investissement doit toujours payer), réduit par la résistance élémentaire de la
    /// CIBLE. Les deux termes comptent le rang — CE N'EST PAS un double-compte par erreur,
    /// décision explicite Florian : rankTerm et le bonus de rang inclus dans pointsTerm sont
    /// deux bonus différents qui doivent tous les deux s'appliquer. Toujours en % du
    /// MaxHP cible (pas de mode Flat — un DoT flat ne scale pas avec le contenu). Rang 0 ET
    /// points 0 → 0 dégât (Dot = mécanisme élémentaire, pas universel).</summary>
    private float ComputeDotDps(Entity target)
    {
        var element = DebuffData.damageElement;

        ElementalSystem sourceES = source?.GetComponent<ElementalSystem>();
        int   rank   = sourceES?.GetElementRank(element) ?? 0;
        float points = sourceES != null
            ? sourceES.GetEffectiveElementPoints(element)
            : source?.GetElementalPoints(element) ?? 0f;

        float rankTerm   = target.MaxHP * (rank * DebuffData.rankDamagePercent / 100f);
        float pointsTerm = points * DebuffData.elementalPointsMultiplier;
        float dps        = rankTerm + pointsTerm;

        Mob   targetMob = target.GetComponent<Mob>();
        float resist    = targetMob?.data != null
            ? targetMob.data.GetElementalResistance(element)
            : target.GetElementalResistance(element);

        float finalDps = dps * (1f - resist);

        if (CombatSystem.Instance != null && CombatSystem.Instance.debugDamage)
        {
            Debug.Log($"━━━ DOT REPORT ({DebuffData.name}) ━━━\n" +
                      $"  Source : {source?.entityName ?? "?"}   Target : {target.entityName} (MaxHP {target.MaxHP:F0})\n" +
                      $"  Élément : {element}   Rang source : {rank}   Points effectifs source : {points:F1}\n" +
                      $"  RankTerm ({DebuffData.rankDamagePercent}% MaxHP × rang) : {rankTerm:F2}\n" +
                      $"  PointsTerm (points × {DebuffData.elementalPointsMultiplier}) : {pointsTerm:F2}\n" +
                      $"  DPS brut : {dps:F2}   Résist cible : {resist * 100f:F1}%   DPS final : {finalDps:F2}");
        }

        return finalDps;
    }
}

// =============================================================
// BuffInstance — runtime d'un buff actif
// =============================================================
public class BuffInstance : StatusEffectInstance
{
    public BuffData BuffData => (BuffData)data;
    public BuffType BuffType => BuffData.buffType;

    public float remainingShield;

    public BuffInstance(BuffData data, Entity source) : base(data, source)
    {
        remainingShield = data.shieldAmount;
    }

    public override void Tick(Entity target, float deltaTime)
    {
        if (target == null || target.isDead) return;

        remainingTime -= deltaTime;

        switch (BuffType)
        {
            case BuffType.Regeneration:
                // HoT — soin progressif sur la durée (§3.1.1.2)
                // GetHealPerSecond supporte Flat et Percent (% MaxHP)
                float heal = BuffData.GetHealPerSecond(target.MaxHP) * deltaTime;
                if (heal > 0f) target.Heal(heal);
                break;

            // Shield, DefenseUp, DodgeUp, Haste, AttackUp, CritChanceUp, CritDamageUp, Barrier :
            // gérés via flags/valeurs dans StatusEffectSystem (OnApply / OnExpire)
        }
    }

    /// <summary>Absorbe des dégâts avec le bouclier actif. Retourne les dégâts résiduels.</summary>
    public float AbsorbDamage(float incomingDamage)
    {
        if (remainingShield <= 0f) return incomingDamage;
        float absorbed  = Mathf.Min(remainingShield, incomingDamage);
        remainingShield -= absorbed;
        return incomingDamage - absorbed;
    }
}
