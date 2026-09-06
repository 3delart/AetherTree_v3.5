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
//   ⑧ Esprits (SpiritData) → points élémentaires + config.bonuses + paliers (Neutre :
//      milestones.bonuses StatBonus ; élémentaires : sharedElementalMilestones partagée)
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
        // Bonus dégâts élémentaires / pénétration / réduction coût mana PAR élément —
        // même famille additive que accResist, source principale : paliers SpiritData.
        var accDamageBonus       = new Dictionary<ElementType, float>();
        var accPenetration       = new Dictionary<ElementType, float>();
        var accManaCostReduction = new Dictionary<ElementType, float>();
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
        {
            accResist[e]             = 0f;
            accDamageBonus[e]        = 0f;
            accPenetration[e]        = 0f;
            accManaCostReduction[e]  = 0f;
        }

        var flatAcc = new Dictionary<StatType, float>();
        var percentAcc = new Dictionary<StatType, float>();

        // Reset points élémentaires
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            elementalPoints[e] = 0f;
        cooldownReduction = 0f;

        // XPBonus/GoldBonus — aucune source d'équipement, uniquement des talismans via
        // ReapplyActiveModifiers (appelé juste après par Player.RequestRecalculate). Sans ce
        // reset, la valeur laissée par le buff actif serait capturée comme "base" par
        // SnapshotBaseStats() plus bas, puis le buff se réappliquerait PAR-DESSUS à chaque
        // recalcul (équipement, level up...) — empilement infini.
        player.SetXPBonusPercent(0f);
        player.SetGoldBonusPercent(0f);
        player.SetSpiritXpBonusPercent(0f);

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
            AccumulateStatBonuses(weapon.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
            AccumulateStatBonuses(armor.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
            AccumulateStatBonuses(helmet.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
            AccumulateStatBonuses(gloves.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
            AccumulateStatBonuses(boots.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
                AccumulateStatBonuses(jewelry.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
            }
        }

        // =========================================================
        // ⑦ RUNES — config.bonuses via RuneInstance (GDD §5.7)
        // =========================================================
        if (weapon?.equippedRune != null)
            AccumulateStatBonuses(weapon.equippedRune.bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
        if (armor?.equippedRune != null)
            AccumulateStatBonuses(armor.equippedRune.bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);

        // =========================================================
        // ⑧ ESPRITS — points élémentaires + config.bonuses + paliers (GDD §5.6)
        // Neutre : milestones.bonuses (StatBonus, par-asset). Élémentaires : sharedElementalMilestones
        // (table PARTAGÉE, ciblée sur spirit.Element — voir SpiritData.cs, 2026-09-06).
        // =========================================================
        if (player.equippedSpiritInstances != null)
        {
            foreach (var spirit in player.equippedSpiritInstances)
            {
                if (spirit?.data == null) continue;

                // Points élémentaires cumulés au niveau actuel (0 si Esprit Neutre — GDD §5.6)
                elementalPoints[spirit.Element] += spirit.TotalElementalPoints;

                // Bonus passifs de base de l esprit (actifs dès l équipement)
                AccumulateStatBonuses(spirit.Bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);

                // Bonus de paliers Esprit Neutre débloqués jusqu au niveau actuel
                for (int mileLvl = 1; mileLvl <= spirit.level; mileLvl++)
                {
                    var milestone = spirit.data.GetMilestone(mileLvl);
                    if (milestone != null)
                        AccumulateStatBonuses(milestone.bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
                }

                // Bonus de paliers élémentaires — table PARTAGÉE entre les 7 esprits élémentaires
                // (SpiritMilestoneTable), toujours ciblés sur l'élément propre de CET esprit.
                var table = spirit.data.sharedElementalMilestones;
                if (table?.milestones != null)
                    foreach (var m in table.milestones)
                    {
                        if (m.level > spirit.level) continue;
                        accResist[spirit.Element]            += m.resistBonusPercent;
                        accDamageBonus[spirit.Element]        += m.elementalDamageBonusPercent;
                        accPenetration[spirit.Element]        += m.resistPenetrationPercent;
                        accManaCostReduction[spirit.Element]  += m.manaCostReductionPercent;
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
                AccumulateStatBonuses(p.bonuses, flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
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
        // BONUS AUTOMATIQUE ARMORTYPE — GDD §5.4
        // Codé en dur, PAS via config.bonuses (décision explicite Florian) — lu directement
        // sur l'ArmorType de l'armure équipée, indépendant de tout StatBonus configuré sur
        // l'ArmorData elle-même. Placé ici (après TOUTES les autres sources — esprits,
        // StatPoints, rang Neutre) pour que le bonus Robe sur elementalPoints capture bien
        // la totalité déjà accumulée, pas seulement une partie.
        // =========================================================
        if (armor?.data != null)
        {
            switch (armor.data.armorType)
            {
                // Lourde/Legere partagent GetArmorTypePassives (source unique, aussi lue par
                // CharacterPanelUI) — voir plus bas dans ce fichier, section ACCUMULATEUR StatBonus.
                case ArmorType.Lourde:
                case ArmorType.Legere:
                    AccumulateStatBonuses(GetArmorTypePassives(armor.data.armorType), flatAcc, percentAcc, accResist, accDamageBonus, accPenetration, accManaCostReduction);
                    break;

                case ArmorType.Robe:
                    // Points élémentaires — pas de mode Percent dans ce système (voir spec §C),
                    // multiplication directe après que toutes les sources (esprits, StatPoints)
                    // aient déjà rempli elementalPoints[e] ci-dessus.
                    foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                        elementalPoints[e] *= 1.10f;
                    // cooldownReduction est déjà un ratio (comme CritChance/XPBonus/GoldBonus) —
                    // addition directe, pas de multiplication (confirmé Florian).
                    //
                    // Note asymétrie (Fix 2, revue finale 2026-09-05) : le +5% de Dodge (Lourde) et
                    // de Precision (Legere) ci-dessus sont RELATIFS (×1.05 appliqué via percentAcc
                    // sur la valeur courante du stat — faible impact si le total est bas, plus fort
                    // s'il est déjà élevé), alors que ce +0.05f sur cooldownReduction est ABSOLU
                    // (+5 points de pourcentage ajoutés directement au ratio, fort dès le niveau 1,
                    // ne scale avec rien). C'est intentionnel (confirmé Florian) — ne PAS "harmoniser"
                    // l'un sur l'autre sans revalider avec lui.
                    cooldownReduction += 0.05f;
                    break;
            }
        }

        // =========================================================
        // PUSH SUR ENTITY
        // =========================================================
        float FinalOf(StatType stat, float baseAndDirectFlat)
        {
            float flat = flatAcc.TryGetValue(stat, out var f) ? f : 0f;
            float pct  = percentAcc.TryGetValue(stat, out var p) ? p : 0f;
            return (baseAndDirectFlat + flat) * (1f + pct);
        }

        player.SetMaxHP       (FinalOf(StatType.BonusHP, baseHP + accBonusHP));
        player.SetMaxMana     (FinalOf(StatType.BonusMana, baseMana + accBonusMana));
        player.SetRegenHP     (FinalOf(StatType.BonusRegenHP, baseRegenHP + accBonusRegenHP));
        player.SetRegenMana   (FinalOf(StatType.BonusRegenMana, baseRegenMana + accBonusRegenMana));
        player.SetMoveSpeed   (FinalOf(StatType.MoveSpeed, baseMoveSpeed + accBonusMoveSpeed));

        player.SetAttackDamageMin(FinalOf(StatType.BonusAttack, accAttackMin));
        player.SetAttackDamageMax(FinalOf(StatType.BonusAttack, accAttackMax));
        player.SetPrecision      (FinalOf(StatType.Precision, accPrecision));
        player.SetCritChance     (FinalOf(StatType.CritChance, accCritChance));
        player.SetCritMultiplier (FinalOf(StatType.CritMultiplier, accCritMultiplier));

        player.SetMeleeDefense (FinalOf(StatType.MeleeDefense, accMeleeDefense));
        player.SetRangedDefense(FinalOf(StatType.RangedDefense, accRangedDefense));
        player.SetMagicDefense (FinalOf(StatType.MagicDefense, accMagicDefense));
        player.SetDodge        (FinalOf(StatType.Dodge, accDodge));
        player.SetCritDamageReduction(FinalOf(StatType.CritDmgReduction, accCritDmgReduct));

        player.SetFinalDamageBonusFlat(
            flatAcc.TryGetValue(StatType.FinalDamageBonus, out var fdbF) ? fdbF : 0f);
        player.SetFinalDamageBonusPercent(
            percentAcc.TryGetValue(StatType.FinalDamageBonus, out var fdbP) ? fdbP : 0f);
        player.SetFinalDamageReductionFlat(
            flatAcc.TryGetValue(StatType.FinalDamageReduction, out var fdrF) ? fdrF : 0f);
        player.SetFinalDamageReductionPercent(
            percentAcc.TryGetValue(StatType.FinalDamageReduction, out var fdrP) ? fdrP : 0f);

        // Résistances sans plafond — fusion gants/bottes peut dépasser 100%.
        // Valeurs négatives possibles uniquement via debuffs (vulnérabilité).
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            player.SetElementalResistance(e, accResist[e]);

        // Bonus dégâts élémentaires / pénétration / réduction coût mana PAR élément —
        // source principale : paliers SpiritData (2026-09-06).
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
        {
            player.SetElementalDamageBonus(e, accDamageBonus[e]);
            player.SetElementalResistPenetration(e, accPenetration[e]);
            player.SetManaCostReduction(e, accManaCostReduction[e]);
        }

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

    /// <summary>Stats sans sélecteur Flat/% côté Inspector (voir ShowIf sur StatBonus.mode) —
    /// toujours additives. AccumulateBonus y route toute contribution en Flat quel que soit
    /// b.mode (filet de sécurité, en plus du masquage Inspector).</summary>
    private static readonly HashSet<StatType> ExceptionStatTypes = new HashSet<StatType>
    {
        StatType.CritChance, StatType.CritMultiplier, StatType.CritDmgReduction,
        StatType.ResistFire, StatType.ResistWater, StatType.ResistEarth, StatType.ResistNature,
        StatType.ResistLightning, StatType.ResistDarkness, StatType.ResistLight, StatType.ResistAll,
        StatType.MoveSpeed,
        StatType.PointsFire, StatType.PointsWater, StatType.PointsEarth, StatType.PointsNature,
        StatType.PointsLightning, StatType.PointsDarkness, StatType.PointsLight, StatType.PointsAll,
        StatType.ElementalDamageBonusFire, StatType.ElementalDamageBonusWater, StatType.ElementalDamageBonusEarth,
        StatType.ElementalDamageBonusNature, StatType.ElementalDamageBonusLightning,
        StatType.ElementalDamageBonusDarkness, StatType.ElementalDamageBonusLight, StatType.ElementalDamageBonusAll,
        StatType.ResistPenetrationFire, StatType.ResistPenetrationWater, StatType.ResistPenetrationEarth,
        StatType.ResistPenetrationNature, StatType.ResistPenetrationLightning,
        StatType.ResistPenetrationDarkness, StatType.ResistPenetrationLight, StatType.ResistPenetrationAll,
        StatType.ManaCostReductionFire, StatType.ManaCostReductionWater, StatType.ManaCostReductionEarth,
        StatType.ManaCostReductionNature, StatType.ManaCostReductionLightning,
        StatType.ManaCostReductionDarkness, StatType.ManaCostReductionLight, StatType.ManaCostReductionAll,
    };

    private void AccumulateStatBonuses(
        System.Collections.Generic.List<StatBonus> bonuses,
        Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc,
        Dictionary<ElementType, float> resist,
        Dictionary<ElementType, float> damageBonus,
        Dictionary<ElementType, float> penetration,
        Dictionary<ElementType, float> manaCostReduction)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            AccumulateBonus(b, flatAcc, percentAcc, resist, damageBonus, penetration, manaCostReduction);
    }

    /// <summary>Bonus passifs automatiques par ArmorType (GDD §5.4) — source unique partagée entre
    /// le moteur de calcul (RecalculateStats) et l'UI (CharacterPanelUI), pour que les deux
    /// n'aient jamais à être mises à jour séparément. Robe retourne une liste vide — ses 2 effets
    /// (Points élémentaires, cooldown) n'ont pas de StatType/chemin Flat-Percent, gérés à part.</summary>
    public static List<StatBonus> GetArmorTypePassives(ArmorType armorType)
    {
        switch (armorType)
        {
            case ArmorType.Lourde:
                return new List<StatBonus>
                {
                    new StatBonus { statType = StatType.AllDefense, mode = ModifierType.Percent, value = 0.10f },
                    new StatBonus { statType = StatType.Dodge,      mode = ModifierType.Percent, value = 0.05f },
                };
            case ArmorType.Legere:
                return new List<StatBonus>
                {
                    new StatBonus { statType = StatType.BonusAttack, mode = ModifierType.Percent, value = 0.10f },
                    new StatBonus { statType = StatType.Precision,   mode = ModifierType.Percent, value = 0.05f },
                };
            default:
                return new List<StatBonus>();
        }
    }

    private void AccumulateBonus(
        StatBonus b,
        Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc,
        Dictionary<ElementType, float> resist,
        Dictionary<ElementType, float> damageBonus,
        Dictionary<ElementType, float> penetration,
        Dictionary<ElementType, float> manaCostReduction)
    {
        switch (b.statType)
        {
            // Résistances élémentaires — sans plafond (GDD v3.5 §3.1), toujours additives
            case StatType.ResistFire:      resist[ElementType.Fire]      += b.value; return;
            case StatType.ResistWater:     resist[ElementType.Water]     += b.value; return;
            case StatType.ResistEarth:     resist[ElementType.Earth]     += b.value; return;
            case StatType.ResistNature:    resist[ElementType.Nature]    += b.value; return;
            case StatType.ResistLightning: resist[ElementType.Lightning] += b.value; return;
            case StatType.ResistDarkness:  resist[ElementType.Darkness]  += b.value; return;
            case StatType.ResistLight:     resist[ElementType.Light]     += b.value; return;
            case StatType.ResistAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    resist[e] += b.value;
                return;

            // Bonus dégâts élémentaires PAR élément — toujours additifs (source principale :
            // paliers SpiritData, voir StatBonus.cs).
            case StatType.ElementalDamageBonusFire:      damageBonus[ElementType.Fire]      += b.value; return;
            case StatType.ElementalDamageBonusWater:     damageBonus[ElementType.Water]     += b.value; return;
            case StatType.ElementalDamageBonusEarth:     damageBonus[ElementType.Earth]     += b.value; return;
            case StatType.ElementalDamageBonusNature:    damageBonus[ElementType.Nature]    += b.value; return;
            case StatType.ElementalDamageBonusLightning: damageBonus[ElementType.Lightning] += b.value; return;
            case StatType.ElementalDamageBonusDarkness:  damageBonus[ElementType.Darkness]  += b.value; return;
            case StatType.ElementalDamageBonusLight:     damageBonus[ElementType.Light]     += b.value; return;
            case StatType.ElementalDamageBonusAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    damageBonus[e] += b.value;
                return;

            // Pénétration de résistance ennemie PAR élément — toujours additive, s'ajoute à
            // ElementalSystem.GetRank5ResistPenetration (source séparée).
            case StatType.ResistPenetrationFire:      penetration[ElementType.Fire]      += b.value; return;
            case StatType.ResistPenetrationWater:     penetration[ElementType.Water]     += b.value; return;
            case StatType.ResistPenetrationEarth:     penetration[ElementType.Earth]     += b.value; return;
            case StatType.ResistPenetrationNature:    penetration[ElementType.Nature]    += b.value; return;
            case StatType.ResistPenetrationLightning: penetration[ElementType.Lightning] += b.value; return;
            case StatType.ResistPenetrationDarkness:  penetration[ElementType.Darkness]  += b.value; return;
            case StatType.ResistPenetrationLight:     penetration[ElementType.Light]     += b.value; return;
            case StatType.ResistPenetrationAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    penetration[e] += b.value;
                return;

            // Réduction de coût en mana PAR élément du skill lancé — toujours additive.
            case StatType.ManaCostReductionFire:      manaCostReduction[ElementType.Fire]      += b.value; return;
            case StatType.ManaCostReductionWater:     manaCostReduction[ElementType.Water]     += b.value; return;
            case StatType.ManaCostReductionEarth:     manaCostReduction[ElementType.Earth]     += b.value; return;
            case StatType.ManaCostReductionNature:    manaCostReduction[ElementType.Nature]    += b.value; return;
            case StatType.ManaCostReductionLightning: manaCostReduction[ElementType.Lightning] += b.value; return;
            case StatType.ManaCostReductionDarkness:  manaCostReduction[ElementType.Darkness]  += b.value; return;
            case StatType.ManaCostReductionLight:     manaCostReduction[ElementType.Light]     += b.value; return;
            case StatType.ManaCostReductionAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    manaCostReduction[e] += b.value;
                return;

            // Points élémentaires — inchangé, toujours additifs (spec §A)
            case StatType.PointsFire:      elementalPoints[ElementType.Fire]      += b.value; return;
            case StatType.PointsWater:     elementalPoints[ElementType.Water]     += b.value; return;
            case StatType.PointsEarth:     elementalPoints[ElementType.Earth]     += b.value; return;
            case StatType.PointsNature:    elementalPoints[ElementType.Nature]    += b.value; return;
            case StatType.PointsLightning: elementalPoints[ElementType.Lightning] += b.value; return;
            case StatType.PointsDarkness:  elementalPoints[ElementType.Darkness]  += b.value; return;
            case StatType.PointsLight:     elementalPoints[ElementType.Light]     += b.value; return;
            case StatType.PointsAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    elementalPoints[e] += b.value;
                return;

            // AllDefense — jamais stockée elle-même, répartie sur les 3 défenses réelles,
            // chacune calculera son propre résultat avec sa propre base (spec §A).
            case StatType.AllDefense:
                AddToAcc(flatAcc, percentAcc, StatType.MeleeDefense, b.mode, b.value);
                AddToAcc(flatAcc, percentAcc, StatType.RangedDefense, b.mode, b.value);
                AddToAcc(flatAcc, percentAcc, StatType.MagicDefense, b.mode, b.value);
                return;
        }

        AddToAcc(flatAcc, percentAcc, b.statType, b.mode, b.value);
    }

    /// <summary>Route une contribution dans la somme Flat ou % — force Flat pour les stats de
    /// `ExceptionStatTypes` quel que soit `mode` (filet de sécurité, le champ mode est de toute
    /// façon masqué côté Inspector pour elles).</summary>
    private static void AddToAcc(Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc, StatType stat, ModifierType mode, float value)
    {
        if (mode == ModifierType.Percent && !ExceptionStatTypes.Contains(stat))
            percentAcc[stat] = percentAcc.TryGetValue(stat, out var p) ? p + value : value;
        else
            flatAcc[stat] = flatAcc.TryGetValue(stat, out var f) ? f + value : value;
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