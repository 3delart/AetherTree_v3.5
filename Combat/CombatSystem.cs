using UnityEngine;

// =============================================================
// COMBATSYSTEM.CS — Calcul des dégâts
// Path : Assets/Scripts/Systems/CombatSystem.cs
// AetherTree GDD v3.5 — Section 21
//
// Pipeline deux branches indépendantes (§21.1) :
//   Branche Physique   : base → ×ratio skill → réduction def (brut²/(brut+def×1.5))
//   Branche Élémentaire: pts fixes → ×affinité → ×ratio skill → -résistance → ×vulnérabilité
//   Total = physique + élémentaire
//
// Réduction physique (§21.2) : brut² / (brut + def × 1.5)
//   → def = brut/2 : ~40% réduit | def = brut : ~50% | def = brut×2 : ~67%
//
// Miss% (§6.10) : Esquive^6 / (Esquive^6 + Précision^6) × 100
//   Les deux valeurs sont effectives (après buffs/debuffs) — GDD §3.1.1.2.
//
// Sources d'attaque par entité :
//   Player → CalculateDamage()    (WeaponInstance — FinalDamageMin/Max)
//   Mob    → CalculateMobDamage() (MobData.attackDamage)
//   PNJ    → CalculateMobDamage() (même pipeline — source = PNJData.attackDamage via Entity)
// =============================================================

public class CombatSystem : MonoBehaviour
{
    public static CombatSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    /// <summary>
    /// Calcule les dégâts d'une attaque du joueur.
    /// </summary>
    public float CalculateDamage(
        WeaponInstance weapon,
        SkillData skill,
        ElementalSystem elemental,
        Player attacker,
        Entity target)
    {
        // 1. Dégâts bruts de l'arme — roll entre FinalDamageMin et FinalDamageMax
        float baseDamage = Random.Range(weapon.FinalDamageMin, weapon.FinalDamageMax);
        baseDamage *= skill.damageMultiplier;

        // 2. Partie physique — réduction §21.2 : brut² / (brut + def × 1.5)
        float physDamage = 0f;
        if (target != null)
        {
            float mDef = target.GetMeleeDefense();
            float rDef = target.GetRangedDefense();
            float gDef = target.GetMagicDefense();

            // ArmorBreak — réduit toutes les défenses de la cible (§21bis.1)
            StatusEffectSystem targetStatus = target.GetComponent<StatusEffectSystem>();
            if (targetStatus != null && targetStatus.armorBreakReduction > 0f)
            {
                mDef = Mathf.Max(0f, mDef - targetStatus.armorBreakReduction);
                rDef = Mathf.Max(0f, rDef - targetStatus.armorBreakReduction);
                gDef = Mathf.Max(0f, gDef - targetStatus.armorBreakReduction);
            }

            float mRed = (baseDamage * baseDamage) / (baseDamage + mDef * 1.5f);
            float rRed = (baseDamage * baseDamage) / (baseDamage + rDef * 1.5f);
            float gRed = (baseDamage * baseDamage) / (baseDamage + gDef * 1.5f);

            physDamage = mRed * skill.damageMeleeRatio
                       + rRed * skill.damageRangedRatio
                       + gRed * skill.damageMagicRatio;
        }
        else
        {
            physDamage = baseDamage;
        }

        // 3. Partie élémentaire — s'ajoute aux dégâts physiques (ratio indépendant)
        float elemDamage = baseDamage * skill.elementalRatio;

        if (skill.PrimaryElement != ElementType.Neutral && elemental != null && attacker != null)
        {
            float elemPoints = attacker.GetElementPoints(skill.PrimaryElement);
            float flatBonus  = 1f + (elemPoints * 0.001f); // 1000 pts = +100%

            float affinityBonus = elemental.GetElementalDamageBonus(skill.PrimaryElement);

            elemDamage *= flatBonus * affinityBonus * skill.elementalMultiplier;
        }
        else
        {
            if (elemental != null)
                elemDamage *= elemental.GetNeutralBonus();
        }

        // 4. Vulnérabilité de la cible (Player)
        if (target != null)
        {
            Player targetPlayer = target.GetComponent<Player>();
            if (targetPlayer != null)
            {
                ElementalSystem targetElemental = targetPlayer.GetComponent<ElementalSystem>();
                if (targetElemental != null)
                    elemDamage *= targetElemental.GetVulnerability(skill.PrimaryElement);
            }
        }

        // 5. Résistance élémentaire de la cible (Mob)
        if (target != null)
        {
            Mob mob = target.GetComponent<Mob>();
            if (mob?.data != null)
            {
                float resistance = mob.data.GetElementalResistance(skill.PrimaryElement);
                elemDamage *= (1f - resistance);
            }
        }

        float totalDamage = physDamage + elemDamage;

        // 6. Critique — CritChance [0..1] | CritMultiplier [1+]
        if (Random.value < weapon.CritChance)
            totalDamage *= weapon.CritMultiplier;

        // 7. Mark — bonus dégâts sur cible marquée
        if (target?.statusEffects != null && target.statusEffects.isMarked)
            totalDamage *= (1f + target.statusEffects.GetMarkDamageBonus());

        // 8. AttackUp buff de l'attaquant
        if (attacker?.statusEffects != null)
            totalDamage += attacker.statusEffects.GetBuffAttackBonus();

        return Mathf.Max(1f, totalDamage);
    }

    /// <summary>
    /// Overload Mob → Entity pour compatibilité avec les appels existants.
    /// </summary>
    public float CalculateMobDamage(SkillData skill, Mob caster, Entity target)
        => CalculateMobDamage(skill, (Entity)caster, target);

    /// <summary>
    /// Pipeline dégâts commun Mob / PNJ — GDD §21.2.
    /// Lit Entity.AttackDamageMin, applique la défense pondérée et la résistance élémentaire.
    /// </summary>
    public float CalculateMobDamage(SkillData skill, Entity caster, Entity target)
    {
        float baseDamage = caster.AttackDamageMin > 0f ? caster.AttackDamageMin : 10f;
        baseDamage *= skill.damageMultiplier * Random.Range(0.9f, 1.1f);

        // Défense pondérée — formule §21.2 : brut² / (brut + def × 1.5)
        float reduction = 1f;
        if (target != null)
        {
            float mDef = target.GetMeleeDefense();
            float rDef = target.GetRangedDefense();
            float gDef = target.GetMagicDefense();

            // ArmorBreak — réduit toutes les défenses de la cible (§21bis.1)
            StatusEffectSystem targetStatus = target.GetComponent<StatusEffectSystem>();
            if (targetStatus != null && targetStatus.armorBreakReduction > 0f)
            {
                mDef = Mathf.Max(0f, mDef - targetStatus.armorBreakReduction);
                rDef = Mathf.Max(0f, rDef - targetStatus.armorBreakReduction);
                gDef = Mathf.Max(0f, gDef - targetStatus.armorBreakReduction);
            }

            float mRed = (baseDamage * baseDamage) / (baseDamage + mDef * 1.5f);
            float rRed = (baseDamage * baseDamage) / (baseDamage + rDef * 1.5f);
            float gRed = (baseDamage * baseDamage) / (baseDamage + gDef * 1.5f);

            reduction = mRed * skill.damageMeleeRatio
                      + rRed * skill.damageRangedRatio
                      + gRed * skill.damageMagicRatio;

            if (baseDamage > 0f)
                reduction /= baseDamage;
        }

        // Résistance élémentaire de la cible
        float elemResist = 0f;
        if (target != null)
        {
            Mob targetMob = target.GetComponent<Mob>();
            if (targetMob?.data != null)
                elemResist = targetMob.data.GetElementalResistance(skill.PrimaryElement);
        }

        float total = baseDamage * reduction * (1f - elemResist);

        // Mark — bonus dégâts sur cible marquée
        if (target?.statusEffects != null && target.statusEffects.isMarked)
            total *= (1f + target.statusEffects.GetMarkDamageBonus());

        return Mathf.Max(1f, total);
    }

    /// <summary>
    /// Roll de dodge selon la formule GDD §6.10 :
    /// Miss% = Esquive^6 / (Esquive^6 + Précision^6) × 100
    /// Les deux valeurs sont effectives (après buffs/debuffs) — GDD §3.1.1.2.
    /// </summary>
    public bool RollDodge(float effectiveDodge, float effectivePrecision)
    {
        if (effectiveDodge <= 0f) return false;
        float d6 = Mathf.Pow(effectiveDodge,     6f);
        float p6 = Mathf.Pow(effectivePrecision, 6f);
        float missChance = d6 / (d6 + p6);
        return Random.value < missChance;
    }

    /// <summary>
    /// Roll de toucher — tient compte de Blind sur l'attaquant (GDD §3.1.1.2).
    /// Utilise la précision effective de l'attaquant (après buffs/debuffs).
    /// </summary>
    public static bool RollHit(Entity attacker)
    {
        if (attacker == null) return true;

        // Précision effective : inclut les bonus de buff (PrecisionUp) et malus de debuff (Blind)
        float effectivePrecision = attacker.GetEffectivePrecision();

        // Blind — réduit la précision de l'attaquant
        if (attacker.statusEffects != null && attacker.statusEffects.isBlinded)
            effectivePrecision *= (1f - attacker.statusEffects.GetBlindMalus());

        return Random.value <= effectivePrecision / 100f;
    }
}
