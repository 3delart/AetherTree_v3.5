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

    /// <summary>Référence à l'instance VFX spawnée pour CET effet précis (une par instance,
    /// propre au stacking : chaque instance stackée a la sienne). Null si data.statusVfx est
    /// vide ou pas encore spawnée.</summary>
    public GameObject spawnedVfx;

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

    // UN SEUL accumulateur pour les 3 cas Dot/ManaDrain/HpDrain ci-dessous — PAS un bug de
    // partage d'état : DebuffType est fixé une fois à la création de l'instance (voir le
    // constructeur juste en dessous) et ne change jamais, donc UNE instance donnée ne tique
    // jamais plus d'un seul de ces 3 cas. Deux Poison stackés sur la même cible sont déjà DEUX
    // instances de DebuffInstance séparées (voir StatusEffectSystem, liste d'instances actives),
    // chacune avec son propre _tickAccum — aucun risque de mélange entre stacks.
    private FractionalAccumulator _tickAccum;

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
                // Accumulateur au lieu d'un TakeDamage(dmg) direct — dmg est calculé en
                // DPS × deltaTime, donc fractionnaire à CHAQUE frame (ex: 10 DPS à 60 FPS =
                // 0.16/frame) ; arrondir directement l'arrondirait à 0 pour toujours.
                float dmg = ComputeDotDps(target) * deltaTime;
                if (dmg > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(dmg);
                    if (whole > 0) target.TakeDamage(whole, DebuffData.damageElement, source);
                }
                break;

            case DebuffType.ManaDrain:
                // Drain de mana progressif sur la durée, reversé au lanceur (§3.1.1.1)
                // Percent : ratio du MaxMana de la CIBLE — corrigé 2026-09-29 (Florian), la
                // base LANCEUR (2026-09-07) cassait dans le sens boss→joueur (le boss a un pool
                // énorme, draine un joueur squishy de façon absurde) — cible garde le calcul
                // symétrique quel que soit qui lance sur qui, même convention que le DoT
                // (baseDamagePercent, déjà en % cible depuis le début).
                float drainRate = DebuffData.manaDrainModifier == ModifierType.Percent
                    ? target.MaxMana * DebuffData.manaDrainPerSecond
                    : DebuffData.manaDrainPerSecond;
                float drain = drainRate * deltaTime;
                if (drain > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(drain);
                    if (whole > 0)
                    {
                        target.SpendMana(whole);
                        source?.RecoverMana(whole);
                    }
                }
                break;

            case DebuffType.HpDrain:
                // Vol de vie progressif — dégâts VRAIS sur la cible (TakeDamage n'applique
                // aucune mitigation de défense/résistance elle-même — c'est CombatSystem qui
                // le fait en amont pour les dégâts de skill/DoT ; ici on l'appelle directement
                // avec le montant brut, donc ignore défense/résistances), peut tuer, reversés
                // en soin identique au lanceur.
                // Percent : ratio du MaxHP de la CIBLE — corrigé 2026-09-29 (Florian), même
                // raison que ManaDrain ci-dessus (la base LANCEUR du 2026-09-07 cassait dans le
                // sens boss→joueur), même convention que le DoT.
                float hpDrainRate = DebuffData.hpDrainModifier == ModifierType.Percent
                    ? target.MaxHP * DebuffData.hpDrainPerSecond
                    : DebuffData.hpDrainPerSecond;
                float hpDrain = hpDrainRate * deltaTime;
                if (hpDrain > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(hpDrain);
                    if (whole > 0)
                    {
                        target.TakeDamage(whole, ElementType.Neutral, source);
                        source?.Heal(whole);
                    }
                }
                break;

            // Freeze, Slow, Root, Stun, Fear, Sleep, Shocked, Silence, Taunt :
            // gérés via flags sur StatusEffectSystem (OnApply / OnExpire)
            // Knockback : effet ponctuel — géré via Entity.ApplyKnockBack()
        }
    }

    /// <summary>Dégâts/s d'un DoT (Dot, + Burn/Poison/Bleed obsolètes gardés pour compat) —
    /// 3 termes : un SOCLE (baseDamagePercent, % du MaxHP cible) TOUJOURS appliqué EN DÉGÂTS
    /// VRAIS — PAS réduit par la résistance élémentaire de la cible — peu importe
    /// l'investissement élémentaire de la source (un Dot octroyé par un équipement/proc ne doit
    /// jamais faire ~0 dégât juste parce que le porteur ne joue pas cet élément, ni être annulé
    /// par une cible très résistante), PLUS un bonus élémentaire (rang × rankDamagePercent +
    /// points élémentaires EFFECTIFS × elementalPointsMultiplier), qui LUI est réduit par la
    /// résistance élémentaire de la CIBLE — cohérent avec le reste du jeu (l'investissement
    /// élémentaire doit pouvoir être contré par de la résistance, le socle non). Les deux bonus
    /// comptent le rang — CE N'EST PAS un double-compte par erreur, décision explicite Florian :
    /// rankTerm et le bonus de rang inclus dans pointsTerm sont deux bonus différents qui
    /// doivent tous les deux s'appliquer, en plus du socle. Rang 0 ET points 0 (source hors
    /// élément) → seul le socle s'applique, jamais 0 dégât total. Formule et valeurs par défaut
    /// (0.01/0.2/0.1) calibrées par comparaison chiffrée sur plusieurs profils mob/joueur —
    /// décision explicite Florian (2026-09-06).</summary>
    private float ComputeDotDps(Entity target)
    {
        var element = DebuffData.damageElement;

        ElementalSystem sourceES = source?.GetComponent<ElementalSystem>();
        int   rank   = sourceES?.GetElementRank(element) ?? 0;
        float points = sourceES != null
            ? sourceES.GetEffectiveElementPoints(element)
            : source?.GetElementalPoints(element) ?? 0f;

        float baseTerm   = target.MaxHP * (DebuffData.baseDamagePercent / 100f);
        float rankTerm   = target.MaxHP * (rank * DebuffData.rankDamagePercent / 100f);
        float pointsTerm = points * DebuffData.elementalPointsMultiplier;

        // Entity.GetElementalResistance lit base MobData/CharacterData + tout modificateur
        // actif (buff/debuff Stats ciblant XResistance) — jamais mob.data directement, sinon
        // un debuff de résistance négative ("faiblesse") sur un mob resterait sans effet.
        // Plafond haut uniquement (résist > 100% ne doit jamais inverser le signe du DoT en
        // soin) — pas de plancher bas, une résistance négative (vulnérabilité) doit amplifier
        // les dégâts normalement. Décision explicite Florian (2026-09-06).
        float resist = Mathf.Min(target.GetElementalResistance(element), 1f);

        // Socle en dégâts vrais (jamais résisté) + bonus élémentaire résisté normalement.
        float elementalBonus = (rankTerm + pointsTerm) * (1f - resist);
        float finalDps       = baseTerm + elementalBonus;

        if (CombatSystem.Instance != null && CombatSystem.Instance.debugDamage)
        {
            Debug.Log($"━━━ DOT REPORT ({DebuffData.name}) ━━━\n" +
                      $"  Source : {source?.entityName ?? "?"}   Target : {target.entityName} (MaxHP {target.MaxHP:F0})\n" +
                      $"  Élément : {element}   Rang source : {rank}   Points effectifs source : {points:F1}\n" +
                      $"  BaseTerm ({DebuffData.baseDamagePercent}% MaxHP, socle NON résisté) : {baseTerm:F2}\n" +
                      $"  RankTerm ({DebuffData.rankDamagePercent}% MaxHP × rang) : {rankTerm:F2}\n" +
                      $"  PointsTerm (points × {DebuffData.elementalPointsMultiplier}) : {pointsTerm:F2}\n" +
                      $"  Bonus élémentaire brut : {(rankTerm + pointsTerm):F2}   Résist cible : {resist * 100f:F1}%   Bonus résisté : {elementalBonus:F2}\n" +
                      $"  DPS final (socle + bonus résisté) : {finalDps:F2}");
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

    // Même rôle que DebuffInstance._tickAccum, pour BuffType.Regeneration (seul cas qui applique
    // un montant continu via taux × deltaTime dans Tick() ci-dessous).
    private FractionalAccumulator _regenAccum;

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
                // Accumulateur au lieu d'un Heal(heal) direct — même raison que DebuffInstance._tickAccum :
                // heal est calculé en taux × deltaTime, fractionnaire à chaque frame.
                float heal = BuffData.GetHealPerSecond(target.MaxHP) * deltaTime;
                if (heal > 0f)
                {
                    int whole = _regenAccum.ExtractWhole(heal);
                    if (whole > 0) target.Heal(whole);
                }
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
