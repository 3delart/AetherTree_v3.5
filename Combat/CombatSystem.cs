using UnityEngine;

// =============================================================
// COMBATSYSTEM.CS — Calcul des dégâts
// Path : Assets/Scripts/Systems/CombatSystem.cs
// AetherTree GDD v3.5 — §6.2
//
// Pipeline dégâts joueur :
//   ① Physique  — baseDamage (inclut ×NeutralDamageMult depuis RecalculateStats)
//                 Défenses réduites par GetNeutralArmorPenetration() si rang 5 Neutre
//                 Chaque part réduite par : dmg² / (dmg + def * 1.5)
//                 physDamage = mRed + rRed + gRed
//
//   ② Élémentaire — indépendant de l'arme, basé sur les elemPoints du joueur
//                   elemPoints = valeur FINALE depuis Entity (bonus de rang inclus via RecalculateStats)
//                   elemRaw = elemPoints * skill.EffectiveElementalMultiplier
//                   Vulnérabilité (si cible joueur) → * GetVulnerability()
//                   Résistance effective (- pénétration rang 5) → * (1 - resist)
//
//   ③ Critique → physDamage * CritMultiplier | elemDamage * (1 + (CritMult-1)*0.5)
//   ④ Total = physDamage + elemDamage
//   ⑤ Mark     → * (1 + markBonus)
//   ⑥ AttackUp → + buffBonus
// =============================================================

public class CombatSystem : MonoBehaviour
{
    public static CombatSystem Instance { get; private set; }

    [Header("Debug")]
    [Tooltip("Active les logs détaillés de chaque calcul de dégât.")]
    public bool debugDamage = false;

    private void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;
    }

    // =========================================================
    // CALCUL JOUEUR
    // =========================================================

    public float CalculateDamage(
        WeaponInstance weapon,
        SkillData      skill,
        ElementalSystem elemental,
        Player         attacker,
        Entity         target,
        out bool       isCrit)
    {
        // ── 1. Base physique ──────────────────────────────────
        float baseDamage = Random.Range(attacker.AttackDamageMin, attacker.AttackDamageMax);
        baseDamage *= skill.damageMultiplier;

        // ── 2. Défenses cible (armorBreak pris en compte) ────
        float mDef = 0f, rDef = 0f, gDef = 0f;
        if (target != null)
        {
            mDef = target.GetMeleeDefense();
            rDef = target.GetRangedDefense();
            gDef = target.GetMagicDefense();

            // ArmorBreak : déjà appliqué dans Entity.GetMeleeDefense/Ranged/Magic() — pas de double soustraction ici.

            // Pénétration d'armure Neutre rang 5
            if (elemental != null)
            {
                float armorPen = elemental.GetNeutralArmorPenetration();
                if (armorPen > 0f)
                {
                    mDef *= (1f - armorPen);
                    rDef *= (1f - armorPen);
                    gDef *= (1f - armorPen);
                }
            }
        }

        // ── 3. Partie physique — dmg² / (dmg + def * 1.5) ───
        // Chaque ratio appliqué sur sa part de baseDamage AVANT réduction défense.
        // Ex: 500 dmg * 0.7 melee = 350 → 350² / (350 + mDef*1.5)
        float physDamage = 0f;
        if (target != null)
        {
            float mDmg = baseDamage * skill.damageMeleeRatio;
            float rDmg = baseDamage * skill.damageRangedRatio;
            float gDmg = baseDamage * skill.damageMagicRatio;

            float mRed = mDmg > 0f ? (mDmg * mDmg) / (mDmg + mDef * 1.5f) : 0f;
            float rRed = rDmg > 0f ? (rDmg * rDmg) / (rDmg + rDef * 1.5f) : 0f;
            float gRed = gDmg > 0f ? (gDmg * gDmg) / (gDmg + gDef * 1.5f) : 0f;

            physDamage = mRed + rRed + gRed;
        }
        else
        {
            physDamage = baseDamage;
        }

        // ── 4. Partie élémentaire ─────────────────────────────
        // Indépendante de l'arme — basée sur les elemPoints EFFECTIFS du joueur.
        // elemPoints EFFECTIFS = brut (Entity) + bonus de rang flat+% (ElementalSystem).
        // Le rang est appliqué via GetEffectiveElementPoints() — source unique partagée
        // avec l'UI (RefreshCardElemental). CombatSystem ne recalcule pas le rang lui-même.
        float elemRaw    = 0f;
        float elemDamage = 0f;
        if (skill.EffectiveElementalMultiplier > 0f && attacker != null)
        {
            // Récupère les points effectifs (brut + rang) depuis ElementalSystem
            ElementalSystem attackerES = attacker.GetComponent<ElementalSystem>();
            float elemPoints = attackerES != null
                ? attackerES.GetEffectiveElementPoints(skill.PrimaryElement)
                : attacker.GetElementalPoints(skill.PrimaryElement); // fallback sans rang

            elemRaw    = elemPoints * skill.EffectiveElementalMultiplier;
            elemDamage = elemRaw;

            // ── 4a. Vulnérabilité — si la cible est un joueur ──────
            if (target != null)
            {
                Player targetPlayer = target.GetComponent<Player>();
                if (targetPlayer != null)
                {
                    ElementalSystem tES = targetPlayer.GetComponent<ElementalSystem>();
                    if (tES != null)
                        elemDamage *= tES.GetVulnerability(skill.PrimaryElement);
                }
            }

            // ── 4b. Résistance élémentaire + pénétration rang 5 ──
            float elemResist = 0f;
            
            if (target != null)
            {
                Mob targetMob = target.GetComponent<Mob>();

                // Mob → résistance sur MobData SO
                // Joueur / PNJ / Familier → résistance poussée sur Entity par RecalculateStats
                if (targetMob?.data != null)
                    elemResist = targetMob.data.GetElementalResistance(skill.PrimaryElement);
                else
                    elemResist = target.GetElementalResistance(skill.PrimaryElement);

                // Pénétration rang 5 — local uniquement, ne modifie pas le vrai stat. GDD §6.2.
                if (elemental != null)
                    elemResist = Mathf.Max(0f, elemResist
                        - elemental.GetRank5ResistPenetration(skill.PrimaryElement));

                // Barrier — résistance élémentaire du buff actif sur la cible. GDD §3.1.1.2.
                if (target.statusEffects != null)
                    elemResist += target.statusEffects.GetBarrierElementResist();

                // Clamp local au calcul — une résistance élémentaire peut dépasser 100%
                // (Fusion sans plafond), mais le facteur de réduction ne doit jamais
                // repasser négatif (inverserait le signe des dégâts élémentaires, pouvant
                // annuler la part physique d'un skill mixte phys+élém). Le stat brut
                // affiché/sauvegardé n'est PAS touché, seul ce calcul l'est.
                elemDamage *= (1f - Mathf.Clamp01(elemResist));
            }
        }
        // Neutre pur — les bonus offensifs sont appliqués en amont
        // (baseDamage ×, défenses ×, crit +) via GetNeutralRank(). GDD §6.3.

        float elemFinal = elemDamage;

        // ── 5. Critique — appliqué sur la partie physique uniquement ──
        isCrit = false;
        float effectiveCritChance = attacker.GetEffectiveCritChance();
        float effectiveCritMult   = attacker.CritMultiplier;
        // Note : bonus CritChance/CritMult Neutre (rangs 2+/3+) déjà inclus
        // dans les stats Entity via CharacterStats.RecalculateStats(). GDD §6.3.

        if (Random.value < effectiveCritChance)
        {
            physDamage *= effectiveCritMult;

            // Crit partiel sur l'élémentaire — 50% du bonus de crit. GDD §6.2.
            // Ex: critMult x1.90 → elem ×1.45  |  x2.00 → elem ×1.50
            float elemCritMult = 1f + (effectiveCritMult - 1f) * 0.5f;
            elemFinal *= elemCritMult;

            isCrit = true;
        }

        // ── 6. Total ──────────────────────────────────────────
        float totalDamage = physDamage + elemFinal;

        // ── 7. Mark ───────────────────────────────────────────
        if (target?.statusEffects != null && target.statusEffects.isMarked)
            totalDamage *= (1f + target.statusEffects.GetMarkDamageBonus());

        // ── 8. AttackUp buff ──────────────────────────────────
        if (attacker?.statusEffects != null)
            totalDamage += attacker.statusEffects.GetBuffAttackBonus();

        // ── 9. Dégâts finaux — bonus attaquant puis réduction défenseur ──
        // Mécanisme séparé de la partie stat (Plan A) — appliqué sur le NOMBRE de dégâts déjà
        // calculé, en tout dernier, avant le clamp minimum. Symétrique : flat puis % pour
        // chaque côté. GDD — spec unified-stat-modifiers §B.
        if (attacker != null)
            totalDamage = (totalDamage + attacker.FinalDamageBonusFlat) * (1f + attacker.FinalDamageBonusPercent);
        if (target != null)
            totalDamage = Mathf.Max(0f, totalDamage - target.FinalDamageReductionFlat)
                        * Mathf.Max(0f, 1f - target.FinalDamageReductionPercent);

        // ── Résistance pour le log ────────────────────────────
        float elemResistLog = 0f;
        if (target != null && skill.EffectiveElementalMultiplier > 0f)
        {
            Mob targetMobLog = target.GetComponent<Mob>();
            elemResistLog = targetMobLog?.data != null
                ? targetMobLog.data.GetElementalResistance(skill.PrimaryElement)
                : target.GetElementalResistance(skill.PrimaryElement);
            if (elemental != null)
                elemResistLog = Mathf.Max(0f, elemResistLog
                    - elemental.GetRank5ResistPenetration(skill.PrimaryElement));
        }

        LogDamageReport("PLAYER", attacker, target, weapon, skill, baseDamage, physDamage, elemRaw, elemFinal, elemResistLog, totalDamage, isCrit, effectiveCritMult);

        return Mathf.Max(1f, totalDamage);
    }

    // =========================================================
    // CALCUL MOB / PNJ
    // =========================================================

    public float CalculateMobDamage(SkillData skill, Mob caster, Entity target)
        => CalculateMobDamage(skill, (Entity)caster, target);

    public float CalculateMobDamage(SkillData skill, Entity caster, Entity target)
    {
        float baseDamage = caster.AttackDamageMin > 0f ? caster.AttackDamageMin : 10f;
        baseDamage *= skill.damageMultiplier * Random.Range(0.9f, 1.1f);

        // ── Défenses ──────────────────────────────────────────
        float mDef = 0f, rDef = 0f, gDef = 0f;
        if (target != null)
        {
            mDef = target.GetMeleeDefense();
            rDef = target.GetRangedDefense();
            gDef = target.GetMagicDefense();

            // ArmorBreak : déjà appliqué dans Entity.GetMeleeDefense/Ranged/Magic() — pas de double soustraction ici.
        }

        // ── Physique ──────────────────────────────────────────
        float physDamage = baseDamage;
        if (target != null)
        {
            float mDmg = baseDamage * skill.damageMeleeRatio;
            float rDmg = baseDamage * skill.damageRangedRatio;
            float gDmg = baseDamage * skill.damageMagicRatio;

            float mRed = mDmg > 0f ? (mDmg * mDmg) / (mDmg + mDef * 1.5f) : 0f;
            float rRed = rDmg > 0f ? (rDmg * rDmg) / (rDmg + rDef * 1.5f) : 0f;
            float gRed = gDmg > 0f ? (gDmg * gDmg) / (gDmg + gDef * 1.5f) : 0f;

            physDamage = mRed + rRed + gRed;
        }

        // ── Partie élémentaire — même pipeline que le joueur ─
        // elemPoints réels depuis Entity (pushés par ApplyData via SetElementalPoints).
        // elemRaw = elemPoints * skill.EffectiveElementalMultiplier
        // Résistance cible appliquée ensuite. GDD §6.2.
        float elemRaw    = 0f;
        float elemDamage = 0f;
        if (skill.EffectiveElementalMultiplier > 0f && skill.PrimaryElement != ElementType.Neutral)
        {
            float elemPoints = caster.GetElementalPoints(skill.PrimaryElement);
            elemRaw    = elemPoints * skill.EffectiveElementalMultiplier;
            elemDamage = elemRaw;

            if (target != null)
            {
                float elemResist = 0f;
                Mob targetMob = target.GetComponent<Mob>();
                elemResist = targetMob?.data != null
                    ? targetMob.data.GetElementalResistance(skill.PrimaryElement)
                    : target.GetElementalResistance(skill.PrimaryElement);

                // Barrier — résistance élémentaire du buff actif sur la cible. GDD §3.1.1.2.
                if (target.statusEffects != null)
                    elemResist += target.statusEffects.GetBarrierElementResist();

                // Clamp local au calcul — une résistance élémentaire peut dépasser 100%
                // (Fusion sans plafond), mais le facteur de réduction ne doit jamais
                // repasser négatif (inverserait le signe des dégâts élémentaires, pouvant
                // annuler la part physique d'un skill mixte phys+élém). Le stat brut
                // affiché/sauvegardé n'est PAS touché, seul ce calcul l'est.
                elemDamage *= (1f - Mathf.Clamp01(elemResist));
            }
        }

        float total = physDamage + elemDamage;

        if (target?.statusEffects != null && target.statusEffects.isMarked)
            total *= (1f + target.statusEffects.GetMarkDamageBonus());

        // ── Dégâts finaux — bonus attaquant puis réduction défenseur ──
        // Même mécanisme que CalculateDamage (joueur) — voir ce commentaire là-bas.
        if (caster != null)
            total = (total + caster.FinalDamageBonusFlat) * (1f + caster.FinalDamageBonusPercent);
        if (target != null)
            total = Mathf.Max(0f, total - target.FinalDamageReductionFlat)
                   * Mathf.Max(0f, 1f - target.FinalDamageReductionPercent);

        //LogDamageReport("MOB/PNJ", caster, target, null, skill, baseDamage, physDamage, elemRaw, elemDamage, 0f,total, false);

        return Mathf.Max(1f, total);
    }

    // =========================================================
    // DODGE / HIT
    // =========================================================

    public bool RollDodge(float effectiveDodge, float effectivePrecision)
    {
        if (effectiveDodge <= 0f) return false;
        float d6 = Mathf.Pow(effectiveDodge,     6f);
        float p6 = Mathf.Pow(effectivePrecision, 6f);
        return Random.value < d6 / (d6 + p6);
    }

    public static bool RollHit(Entity attacker)
    {
        if (attacker == null) return true;
        float effectivePrecision = attacker.GetEffectivePrecision();
        if (attacker.statusEffects != null && attacker.statusEffects.isBlinded)
            effectivePrecision *= (1f - attacker.statusEffects.GetBlindMalus());
        return Random.value <= effectivePrecision / 100f;
    }

    // =========================================================
    // DEBUG RAPPORT
    // =========================================================

    private void LogDamageReport(
        string         source,
        Entity         caster,
        Entity         target,
        WeaponInstance weapon,
        SkillData      skill,
        float          baseDamage,
        float          physFinal,
        float          elemRaw,
        float          elemFinal,
        float          elemResist,
        float          totalDamage,
        bool           isCrit,
        float          critMultUsed = 1f)
    {
        if (!debugDamage) return;

        Player      casterPlayer = caster as Player;
        ElementType elem         = skill?.PrimaryElement ?? ElementType.Neutral;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("━━━ DAMAGE REPORT ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        // Caster
        sb.AppendLine($"▶ CASTER [{source}] : {caster?.entityName ?? "?"}");
        sb.AppendLine($"  ATK base     : {caster?.AttackDamageMin:F1} – {caster?.AttackDamageMax:F1}");
        sb.AppendLine($"  Précision    : {caster?.GetEffectivePrecision():F1}");
        if (weapon != null)
        {
            sb.AppendLine($"  Arme         : {weapon.WeaponName}  {weapon.RarityLabel}");
            sb.AppendLine($"  CritChance   : {(casterPlayer?.GetEffectiveCritChance() ?? caster?.CritChance ?? 0f) * 100f:F1}%" +
                          $"   CritMult : x{(casterPlayer?.CritMultiplier ?? caster?.CritMultiplier ?? 1f):F2}");
        }
       if (casterPlayer != null && skill?.EffectiveElementalMultiplier > 0f)
        {
            ElementalSystem es = casterPlayer.GetElementalSystem();
            float ep = es != null
                ? es.GetEffectiveElementPoints(elem)
                : casterPlayer.GetElementalPoints(elem);
            int   elemRank = es != null ? es.GetElementRank(elem)     : 0;
            float elemAff  = es != null ? es.GetAffinity(elem) * 100f : 0f;
            sb.AppendLine($"  ElemPoints   : {ep:F0} ({elem})  rang {elemRank}  [{elemAff:F0}% affinité]");
        }

        // Skill
        if (skill != null)
        {
            sb.AppendLine($"▶ SKILL : {skill.name}   x{skill.damageMultiplier:F2}");
            sb.AppendLine($"  Ratios Phys  : Mêlée {skill.damageMeleeRatio:F2} | Dist {skill.damageRangedRatio:F2} | Mag {skill.damageMagicRatio:F2}");
            sb.AppendLine($"  Elem Mult    : x{skill.EffectiveElementalMultiplier:F2}   Élément : {elem}");
        }

        // Target
        sb.AppendLine($"▶ TARGET : {target?.entityName ?? "?"}   HP {target?.CurrentHP:F0} / {target?.MaxHP:F0}");
        sb.AppendLine($"  Def Mêlée    : {target?.GetMeleeDefense():F1}");
        sb.AppendLine($"  Def Distance : {target?.GetRangedDefense():F1}");
        sb.AppendLine($"  Def Magie    : {target?.GetMagicDefense():F1}");
        sb.AppendLine($"  Esquive      : {target?.GetEffectiveDodge():F1}");
        sb.AppendLine($"  Résist Elem  : {elemResist * 100f:F1}%  ({elem})");

        // Calcul
        sb.AppendLine($"▶ CALCUL");
        sb.AppendLine($"  Base brut    : {baseDamage:F1}");
        sb.AppendLine($"  Phys final   : {physFinal:F1}");
        if (elemRaw > 0f)
            sb.AppendLine($"  Elem brut    : {elemRaw:F1}  →  après résist : {elemFinal:F1}");
        else
            sb.AppendLine($"  Elem         : —");
        sb.AppendLine(isCrit
        ? $"  CRITIQUE ✦   : Phys x{critMultUsed:F2} · Elem x{1f+(critMultUsed-1f)*0.5f:F2}"
        : $"  Pas de crit");
        sb.AppendLine($"  ══ TOTAL     : {totalDamage:F1} ══");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        Debug.Log(sb.ToString());
    }
}
