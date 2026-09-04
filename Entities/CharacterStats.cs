using System.Collections.Generic;
using UnityEngine;

// =============================================================
// CHARACTERSTATS — Moteur de calcul des stats du joueur
// Path : Assets/Scripts/Core/CharacterStats.cs
// AetherTree GDD v3.5 — §3.2 / §5.1 à §5.6
//
// Moteur de calcul pur : lit toutes les sources, pousse les résultats
// sur les champs Entity via les setters publics.
// CombatSystem lit directement sur Entity — jamais sur CharacterStats.
//
// Sources agrégées dans RecalculateStats() :
//   ① Arme (WeaponData)    → stats rollées (dmg, prec, crit) + config.bonuses
//   ② Armure (ArmorData)   → défenses rollées + esquive rollée + config.bonuses
//   ③ Casque (HelmetData)  → défenses fixes + config.bonuses
//   ④ Gants (GlovesData)   → défenses + résist. élémentaires + config.bonuses
//   ⑤ Bottes (BootsData)   → défenses + résist. élémentaires + config.bonuses
//   ⑥ Bijoux (JewelryData) → défenses + config.bonuses
//   ⑦ Runes               → config.bonuses (weapon.equippedRune / armor.equippedRune)
//   ⑧ Esprits (SpiritData) → points élémentaires + milestones.bonuses + config.bonuses
//   ⑨ Skills permanents    → StatBonus + DebuffResistances
//   ⑩ StatPointSystem      → bonus paliers investis — GDD §3.2.1
//   ⑪ Rang d'affinité élém → appliqué à la volée dans CombatSystem (Mono/Dual) — GDD §6.2
//   ⑫ Rang Neutre          → CritChance + CritMult cumulatifs — GDD §6.3
//                            (dégâts base, armure, dégâts reçus → CombatSystem / TakeDamage)
//
// Chaque équipement expose ses bonus via EquipmentConfig (champ unique) :
//   config.bonuses / config.statusEffects /
//   config.debuffResistances / config.onHitEffects
//
// Flow complet :
//   EquipItem() / OnLevelUp() / InvestPoint()
//     → player.stats.RecalculateStats(player)
//     → Reset interne → Agrège toutes les sources
//     → Pousse sur Entity via SetMaxHP(), SetMeleeDefense(), etc.
//     → SnapshotBaseStats() — buffs/debuffs réappliqués après par ReapplyActiveModifiers()
//
// Résistances élémentaires (GDD §3.1) :
//   Pas de plafond — la fusion gants/bottes peut dépasser 100%.
//   Valeurs négatives possibles uniquement via debuffs (vulnérabilité).
//
// WeaponCategory (GDD §3.2) :
//   Magic → critChance forcé à 0 (pas de crit sur armes magiques — GDD §5.1).
//   Les courbes HP/Mana par catégorie sont encodées dans CharacterData SO.
// =============================================================

public class CharacterStats
{
    // =========================================================
    // POINTS ÉLÉMENTAIRES — stockés ici, poussés via ElementalSystem
    // =========================================================

    /// <summary>
    /// Points élémentaires d'équipement par élément.
    /// Agrège : esprits + casque/bijoux/runes/permanents + StatPoints élémentaire.
    /// S'ajoute aux points d'affinité de ElementalSystem.
    /// </summary>
    public Dictionary<ElementType, float> elementalPoints { get; private set; }
        = new Dictionary<ElementType, float>();

    // =========================================================
    // RÉDUCTION CD — pas sur Entity, géré par SkillSystem
    // =========================================================

    /// <summary>
    /// Réduction de cooldown globale [0..1].
    /// Source : StatPoints élémentaire paliers 40 et 100.
    /// Lue par SkillSystem au moment du calcul du cooldown effectif.
    /// </summary>
    public float cooldownReduction { get; private set; } = 0f;

    /// <summary>
    /// Résistances élémentaires cumulées depuis équipements + permanents + StatPoints.
    /// </summary>
    public Dictionary<ElementType, float> elementalResistances { get; private set; }
    = new Dictionary<ElementType, float>();

    // =========================================================
    // CONSTRUCTEUR
    // =========================================================

    public CharacterStats()
    {
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
        {
            elementalPoints[e]            = 0f;
            elementalResistances[e]       = 0f;
        }
    }

    // =========================================================
    // RECALCUL COMPLET
    // Appelé à chaque changement d'équipement, level up, invest StatPoint.
    // Repart de zéro — les buffs/debuffs sont indépendants et non écrasés.
    // =========================================================

    public void RecalculateStats(Player player)
    {
        if (player == null) return;

        // ── Accumulateurs locaux ──────────────────────────────
        // On accumule tout ici avant de pousser sur Entity en une seule passe.
        

        float accAttackMin      = 0f;
        float accAttackMax      = 0f;
        float accPrecision      = 0f;
        float accCritChance     = 0f;
        float accCritMultiplier = 1.0f;

        float accMeleeDefense   = 0f;
        float accRangedDefense  = 0f;
        float accMagicDefense   = 0f;
        float accDodge          = 0f;

        float accBonusHP        = 0f;
        float accBonusMana      = 0f;
        float accBonusRegenHP   = 0f;
        float accBonusRegenMana = 0f;
        float accBonusMoveSpeed = 0f;
        float accCritDmgReduct  = 0f;

        // Résistances élémentaires — accumulateur local avant push
        var accResist = new Dictionary<ElementType, float>();
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            accResist[e] = 0f;

        // Reset points élémentaires
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            elementalPoints[e] = 0f;
        cooldownReduction = 0f;

        // =========================================================
        // ① ARME — stats rollées + config.bonuses (GDD §5.1)
        // =========================================================
        var weapon = player.equippedWeaponInstance;
        if (weapon == null)
        {
            // Unarmed — scale avec le niveau. GDD §5.1 / §3.2.
            float lvl      = player.level;
            accAttackMin   = 5f  + lvl * 0.12f * 6f;
            accAttackMax   = 8f  + lvl * 0.12f * 9f;
            accPrecision   = 12f;
            accCritChance  = 0.03f;
        }
        else
        {
            accAttackMin      = weapon.FinalDamageMin;
            accAttackMax      = weapon.FinalDamageMax;
            accPrecision      = weapon.FinalPrecision;
            accCritChance     = weapon.CritChance;
            accCritMultiplier += weapon.CritMultiplier;
            AccumulateStatBonuses(weapon.Bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        }

        // =========================================================
        // ② ARMURE — défenses rollées + esquive rollée + config.bonuses (GDD §5.2)
        // =========================================================
        var armor = player.equippedArmorInstance;
        if (armor?.data != null)
        {
            accMeleeDefense  += armor.FinalMeleeDefense;
            accRangedDefense += armor.FinalRangedDefense;
            accMagicDefense  += armor.FinalMagicDefense;
            accDodge         += armor.FinalDodge;
            AccumulateStatBonuses(armor.Bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        }

        // =========================================================
        // ③ CASQUE — défenses fixes + config.bonuses (GDD §5.3)
        // =========================================================
        var helmet = player.equippedHelmetInstance;
        if (helmet != null)
        {
            // Défenses rollées (ratio, sans rareté/upgrade) définies sur HelmetData SO. GDD §5.5.
            accMeleeDefense  += helmet.MeleeDefense;
            accRangedDefense += helmet.RangedDefense;
            accMagicDefense  += helmet.MagicDefense;
            AccumulateStatBonuses(helmet.Bonuses,
                ref accAttackMin, ref accAttackMax, ref accPrecision,
                ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        }

        // =========================================================
        // ④ GANTS — défenses + résist. élémentaires + config.bonuses (GDD §5.4)
        // =========================================================
        var gloves = player.equippedGlovesInstance;
        if (gloves != null)
        {
            accMeleeDefense  += gloves.MeleeDefense;
            accRangedDefense += gloves.RangedDefense;
            accMagicDefense  += gloves.MagicDefense;
            foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                accResist[e] += gloves.GetResistance(e);
            AccumulateStatBonuses(gloves.Bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        }

        // =========================================================
        // ⑤ BOTTES — défenses + résist. élémentaires + config.bonuses (GDD §5.4)
        // =========================================================
        var boots = player.equippedBootsInstance;
        if (boots != null)
        {
            accMeleeDefense  += boots.MeleeDefense;
            accRangedDefense += boots.RangedDefense;
            accMagicDefense  += boots.MagicDefense;
            foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                accResist[e] += boots.GetResistance(e);
            AccumulateStatBonuses(boots.Bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        }

        // =========================================================
        // ⑥ BIJOUX — défenses + config.bonuses (GDD §5.5)
        // =========================================================
        if (player.equippedJewelryInstances != null)
        {
            foreach (var jewelry in player.equippedJewelryInstances)
            {
                if (jewelry == null) continue;
                accMeleeDefense  += jewelry.MeleeDefense;
                accRangedDefense += jewelry.RangedDefense;
                accMagicDefense  += jewelry.MagicDefense;
                AccumulateStatBonuses(jewelry.Bonuses, ref accAttackMin, ref accAttackMax,
                    ref accPrecision, ref accCritChance, ref accCritMultiplier,
                    ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                    ref accDodge, ref accBonusHP, ref accBonusMana,
                    ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                    ref accCritDmgReduct, accResist);
            }
        }

        // =========================================================
        // ⑦ RUNES — config.bonuses via RuneInstance (GDD §5.7)
        // =========================================================
        if (weapon?.equippedRune != null)
            AccumulateStatBonuses(weapon.equippedRune.bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
        if (armor?.equippedRune != null)
            AccumulateStatBonuses(armor.equippedRune.bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);

        // =========================================================
        // ⑧ ESPRITS — points élémentaires + milestones.bonuses + config.bonuses (GDD §5.6)
        // =========================================================
        if (player.equippedSpiritInstances != null)
        {
            foreach (var spirit in player.equippedSpiritInstances)
            {
                if (spirit?.data == null) continue;

                // Points élémentaires cumulés au niveau actuel (0 si Esprit Neutre — GDD §5.6)
                elementalPoints[spirit.Element] += spirit.TotalElementalPoints;

                // Bonus passifs de base de l esprit (actifs dès l équipement)
                AccumulateStatBonuses(spirit.Bonuses, ref accAttackMin, ref accAttackMax,
                    ref accPrecision, ref accCritChance, ref accCritMultiplier,
                    ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                    ref accDodge, ref accBonusHP, ref accBonusMana,
                    ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                    ref accCritDmgReduct, accResist);

                // Bonus de paliers débloqués jusqu au niveau actuel
                for (int mileLvl = 1; mileLvl <= spirit.level; mileLvl++)
                {
                    var milestone = spirit.data.GetMilestone(mileLvl);
                    if (milestone != null)
                        AccumulateStatBonuses(milestone.bonuses, ref accAttackMin, ref accAttackMax,
                            ref accPrecision, ref accCritChance, ref accCritMultiplier,
                            ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                            ref accDodge, ref accBonusHP, ref accBonusMana,
                            ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                            ref accCritDmgReduct, accResist);
                }
            }
        }

        // =========================================================
        // ⑨ SKILLS PERMANENTS — StatBonus + DebuffResistances
        // =========================================================
        if (player.unlockedPermanents != null)
        {
            foreach (var p in player.unlockedPermanents)
            {
                if (p == null) continue;
                AccumulateStatBonuses(p.bonuses, ref accAttackMin, ref accAttackMax,
                    ref accPrecision, ref accCritChance, ref accCritMultiplier,
                    ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                    ref accDodge, ref accBonusHP, ref accBonusMana,
                    ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                    ref accCritDmgReduct, accResist);
            }
        }

        // =========================================================
        // ⑩ STATPOINTS — GDD §3.2.1
        // Deux couches cumulées :
        //   a) Bonus linéaires : +1/rang investi (attaque, défense, élémentaire, HP/MP)
        //   b) Bonus de paliers : débloqués aux multiples de 10
        // =========================================================
        var sp = player.statPoints;
        if (sp != null)
        {
            // ── a) Bonus linéaires (1 rang = 1 de stat) ──────────
            accAttackMin     += sp.linearBonusAttack;
            accAttackMax     += sp.linearBonusAttack;
            accMeleeDefense  += sp.linearBonusDefense;
            accRangedDefense += sp.linearBonusDefense;
            accMagicDefense  += sp.linearBonusDefense;
            foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                elementalPoints[e] += sp.linearBonusElemental;
            accBonusHP   += sp.linearBonusHP;
            accBonusMana += sp.linearBonusMP;

            // ── b) Bonus de paliers ───────────────────────────────
            // Attaque
            accAttackMin      += sp.bonusAttackFlat;
            accAttackMax      += sp.bonusAttackFlat;
            accPrecision      += sp.bonusPrecision;
            accCritChance     += sp.bonusCritChance;
            accCritMultiplier += sp.bonusCritMultiplier;

            // Défense — s'applique aux 3 types
            accMeleeDefense   += sp.bonusDefenseFlat;
            accRangedDefense  += sp.bonusDefenseFlat;
            accMagicDefense   += sp.bonusDefenseFlat;
            accDodge          += sp.bonusDodge;
            accCritDmgReduct  += sp.bonusCritDmgReduction;

            // Résistances élémentaires all (Défense + Élémentaire)
            float resistAll = sp.TotalResistAllFromPoints;
            foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                accResist[e] += resistAll;

            // Élémentaire
            foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                elementalPoints[e] += sp.bonusElementalPoints;
            cooldownReduction += sp.bonusCooldownReduction;

            // HP/MP — bonusMaxMP poussé sur Entity.maxMana via SetMaxMana()
            accBonusHP        += sp.bonusMaxHP;
            accBonusMana      += sp.bonusMaxMP;
            accBonusRegenHP   += sp.bonusRegenHP;
            accBonusRegenMana += sp.bonusRegenMP;
        }

        // =========================================================
        // BONUS DE RANG D'AFFINITÉ — GDD §6.2
        // Les bonus flat + % de rang élémentaire (Mono/Dual) sont appliqués
        // à la volée dans CombatSystem, comme les bonus Neutre.
        // Les elementalPoints poussés ici sont donc toujours la valeur BRUTE
        // d'équipement — le rang booste au moment du calcul en combat.
        // =========================================================

        // =========================================================
        // BONUS DE RANG NEUTRE — GDD §6.3
        // CritChance et CritMult poussés sur les accumulateurs.
        // Dégâts base (×mult) poussés sur accAttackMin/Max → Entity stocke la valeur finale.
        // CombatSystem ne multiplie plus baseDamage. GDD §6.3.
        // =========================================================
        var elemSys = player.GetElementalSystem();
        if (elemSys != null)
        {
            accCritChance     += elemSys.GetNeutralCritChanceBonus();
            accCritMultiplier += elemSys.GetNeutralCritMultBonus();

            // Bonus dégâts Neutre — multiplié sur ATK avant push sur Entity
            float neutralMult = elemSys.GetNeutralDamageMultiplier();
            if (neutralMult > 1f)
            {
                accAttackMin *= neutralMult;
                accAttackMax *= neutralMult;
            }
        }

        // =========================================================
        // BASE HP/MANA/REGEN depuis CharacterData + niveau + WeaponCategory
        // GDD §3.2 — courbes encodées dans CharacterData SO par WeaponCategory
        // =========================================================
        var cd  = player.characterData;
        int lv          = Mathf.Max(1, player.level);
        int levelOffset = lv - 1;
        

        // HP / Mana : courbe propre par WeaponCategory — GDD §3.2
        // CharacterData expose GetBaseHP() / GetHPPerLevel() par catégorie.
        float baseHP        = cd != null ? cd.GetBaseHP  (player.weaponCategory) : 500f;
        float baseMana      = cd != null ? cd.GetBaseMana(player.weaponCategory) : 100f;
        float baseRegenHP   = cd != null ? cd.baseRegenHP                        : 1f;
        float baseRegenMana = cd != null ? cd.baseRegenMana                      : 0.5f;
        float baseMoveSpeed = cd != null ? cd.baseMoveSpeed                      : 4f;


        if (cd != null && levelOffset > 0)
        {
            baseHP   += cd.GetHPPerLevel  (player.weaponCategory) * levelOffset;
            baseMana += cd.GetManaPerLevel(player.weaponCategory) * levelOffset;
            // RegenHP / RegenMana : fixes — pas de progression par niveau. GDD §3.2.
        }

        // Magic → critChance forcé à 0 — pas de crit sur armes magiques. GDD §5.1.
        if (player.weaponCategory == WeaponCategory.Magic)
            accCritChance = 0f;

        // =========================================================
        // PUSH SUR ENTITY
        // =========================================================
        player.SetMaxHP       (baseHP        + accBonusHP);
        player.SetMaxMana     (baseMana      + accBonusMana);
        player.SetRegenHP     (baseRegenHP   + accBonusRegenHP);
        player.SetRegenMana   (baseRegenMana + accBonusRegenMana);
        player.SetMoveSpeed   (baseMoveSpeed + accBonusMoveSpeed);

        player.SetAttackDamageMin(accAttackMin);
        player.SetAttackDamageMax(accAttackMax);
        player.SetPrecision      (accPrecision);
        player.SetCritChance     (accCritChance);
        player.SetCritMultiplier (accCritMultiplier);

        player.SetMeleeDefense (accMeleeDefense);
        player.SetRangedDefense(accRangedDefense);
        player.SetMagicDefense (accMagicDefense);
        player.SetDodge        (accDodge);
        player.SetCritDamageReduction(accCritDmgReduct);

        // Résistances sans plafond — fusion gants/bottes peut dépasser 100%.
        // Valeurs négatives possibles uniquement via debuffs (vulnérabilité).
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            player.SetElementalResistance(e, accResist[e]);

        // Points élémentaires — poussés sur Entity pour lecture par CombatSystem / ElementalSystem
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            player.SetElementalPoints(e, elementalPoints[e]);

        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            elementalResistances[e] = accResist[e];

        // Résistances aux debuffs
        ApplyDebuffResistances(player);

        // Fige le snapshot — RequestRecalculate() sur le joueur repartira de ces valeurs.
        // Appelé APRÈS tous les SetXxx pour que le snapshot reflète la base propre.
        player.SnapshotBaseStats();
        GameEventBus.Publish(new StatsChangedEvent { player = player });
    }

    // =========================================================
    // RÉSISTANCES AUX DEBUFFS
    // =========================================================

    private void ApplyDebuffResistances(Player player)
    {
        if (player?.statusEffects == null) return;

        player.statusEffects.ResetDebuffResistances();

        // Agrège les résistances depuis config.debuffResistances de tous les équipements
        var sources = new System.Collections.Generic.List<System.Collections.Generic.List<DebuffResistanceEntry>>();

        if (player.equippedWeaponInstance?.DebuffResistances != null)
            sources.Add(player.equippedWeaponInstance.DebuffResistances);
        if (player.equippedArmorInstance?.DebuffResistances != null)
            sources.Add(player.equippedArmorInstance.DebuffResistances);
        if (player.equippedHelmetInstance?.DebuffResistances != null)
            sources.Add(player.equippedHelmetInstance.DebuffResistances);
        if (player.equippedGlovesInstance?.DebuffResistances != null)
            sources.Add(player.equippedGlovesInstance.DebuffResistances);
        if (player.equippedBootsInstance?.DebuffResistances != null)
            sources.Add(player.equippedBootsInstance.DebuffResistances);
        if (player.equippedJewelryInstances != null)
            foreach (var j in player.equippedJewelryInstances)
                if (j?.DebuffResistances != null) sources.Add(j.DebuffResistances);
        // Skills permanents — liste propre (pas via EquipmentConfig)
        if (player.unlockedPermanents != null)
            foreach (var p in player.unlockedPermanents)
                if (p?.debuffResistances != null && p.debuffResistances.Count > 0)
                    sources.Add(p.debuffResistances);

        // Accumule et plafonne à 1.0 par type
        var accumulated = new System.Collections.Generic.Dictionary<DebuffType, float>();
        foreach (var list in sources)
        {
            if (list == null) continue;
            foreach (var entry in list)
            {
                if (!accumulated.ContainsKey(entry.debuffType))
                    accumulated[entry.debuffType] = 0f;
                accumulated[entry.debuffType] = Mathf.Min(1f, accumulated[entry.debuffType] + entry.resistChance);
            }
        }

        foreach (var kvp in accumulated)
            player.statusEffects.SetDebuffResistance(kvp.Key, kvp.Value);
    }

    // =========================================================
    // ACCUMULATEUR StatBonus — méthode centrale
    // =========================================================

    private void AccumulateStatBonuses(
        System.Collections.Generic.List<StatBonus> bonuses,
        ref float atkMin, ref float atkMax, ref float precision,
        ref float critChance, ref float critMult,
        ref float meleeDef, ref float rangedDef, ref float magicDef,
        ref float dodge, ref float bonusHP, ref float bonusMana,
        ref float bonusRegenHP, ref float bonusRegenMana, ref float bonusMoveSpeed,
        ref float critDmgReduct,
        System.Collections.Generic.Dictionary<ElementType, float> resist)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            AccumulateBonus(b, ref atkMin, ref atkMax, ref precision,
                ref critChance, ref critMult,
                ref meleeDef, ref rangedDef, ref magicDef,
                ref dodge, ref bonusHP, ref bonusMana,
                ref bonusRegenHP, ref bonusRegenMana, ref bonusMoveSpeed,
                ref critDmgReduct, resist);
    }

    private void AccumulateBonus(
        StatBonus b,
        ref float atkMin, ref float atkMax, ref float precision,
        ref float critChance, ref float critMult,
        ref float meleeDef, ref float rangedDef, ref float magicDef,
        ref float dodge, ref float bonusHP, ref float bonusMana,
        ref float bonusRegenHP, ref float bonusRegenMana, ref float bonusMoveSpeed,
        ref float critDmgReduct,
        System.Collections.Generic.Dictionary<ElementType, float> resist)
    {
        switch (b.statType)
        {
            case StatType.BonusAttack:    atkMin += b.value; atkMax += b.value;      break;
            case StatType.CritChance:     critChance    += b.value;                  break;
            case StatType.CritMultiplier: critMult      += b.value;                  break;
            case StatType.CritDmgReduction:   critDmgReduct += b.value;              break;
            case StatType.Precision:      precision     += b.value;                  break;
            case StatType.MeleeDefense:   meleeDef      += b.value;                  break;
            case StatType.RangedDefense:  rangedDef     += b.value;                  break;
            case StatType.MagicDefense:   magicDef      += b.value;                  break;
            case StatType.Dodge:          dodge         += b.value;                  break;
            

            case StatType.BonusHP:        bonusHP       += b.value;                  break;
            case StatType.BonusMana:      bonusMana     += b.value;                  break;
            case StatType.BonusRegenHP:   bonusRegenHP  += b.value;                  break;
            case StatType.BonusRegenMana: bonusRegenMana+= b.value;                  break;
            case StatType.MoveSpeed:      bonusMoveSpeed+= b.value;                  break;

            // Résistances élémentaires — sans plafond (GDD v3.5 §3.1)
            case StatType.ResistFire:      resist[ElementType.Fire]      += b.value; break;
            case StatType.ResistWater:     resist[ElementType.Water]     += b.value; break;
            case StatType.ResistEarth:     resist[ElementType.Earth]     += b.value; break;
            case StatType.ResistNature:    resist[ElementType.Nature]    += b.value; break;
            case StatType.ResistLightning: resist[ElementType.Lightning] += b.value; break;
            case StatType.ResistDarkness:  resist[ElementType.Darkness]  += b.value; break;
            case StatType.ResistLight:     resist[ElementType.Light]     += b.value; break;
            case StatType.ResistAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    resist[e] += b.value;
                break;

            // Points élémentaires
            case StatType.PointsFire:      elementalPoints[ElementType.Fire]      += b.value; break;
            case StatType.PointsWater:     elementalPoints[ElementType.Water]     += b.value; break;
            case StatType.PointsEarth:     elementalPoints[ElementType.Earth]     += b.value; break;
            case StatType.PointsNature:    elementalPoints[ElementType.Nature]    += b.value; break;
            case StatType.PointsLightning: elementalPoints[ElementType.Lightning] += b.value; break;
            case StatType.PointsDarkness:  elementalPoints[ElementType.Darkness]  += b.value; break;
            case StatType.PointsLight:     elementalPoints[ElementType.Light]     += b.value; break;
            case StatType.PointsAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    elementalPoints[e] += b.value;
                break;
        }
    }

    // =========================================================
    // ACCESSEURS UTILITAIRES — pour CombatSystem
    // =========================================================

    /// <summary>Points élémentaires d'équipement pour un élément donné.</summary>
    public float GetElementalPoints(ElementType element)
        => elementalPoints.TryGetValue(element, out float v) ? v : 0f;

    /// <summary>Résistance élémentaire pour un élément donné [0..1].</summary>
    public float GetResistance(ElementType element)
    => elementalResistances.TryGetValue(element, out float v) ? v : 0f;
       
}