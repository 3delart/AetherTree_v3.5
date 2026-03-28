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
            case DebuffType.Burn:
            case DebuffType.Bleed:
                float dmg = DebuffData.damagePerSecond * deltaTime;
                if (dmg > 0f)
                    target.TakeDamage(dmg, DebuffData.damageElement, source);
                break;

            case DebuffType.Poison:
                // DoT — le flag healReduction est géré via OnApply/OnExpire dans StatusEffectSystem
                float poisonDmg = DebuffData.damagePerSecond * deltaTime;
                if (poisonDmg > 0f)
                    target.TakeDamage(poisonDmg, DebuffData.damageElement, source);
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
