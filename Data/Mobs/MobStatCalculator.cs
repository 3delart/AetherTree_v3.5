using UnityEngine;

// =============================================================
// MOBSTATCALCULATOR — Calcul des stats finales d'un mob au spawn
// Path : Assets/Scripts/Data/Mobs/MobStatCalculator.cs
// AetherTree GDD v3.5 — §3.3.1
//
// Formule générale :
//   statFinale = (statBase + statParNiveau × (level - 1)) × multiplicateurMobType
//
// Les défenses mêlée/distance/magie sont définies indépendamment sur
// MobData SO (baseDefMelee, baseDefRanged, baseDefMagic + perLevel).
// Le multiplicateur MobType s'applique sur chaque défense séparément.
//
// Précision, esquive, critique : scalent par niveau via les champs perLevel.
// Critique : fixe sur le SO (baseCritChance, baseCritMultiplier).
//
// Appelé par Mob.ApplyData() après assignation de mob.data et mob.mobLevel.
// =============================================================

public static class MobStatCalculator
{
    // =========================================================
    // MULTIPLICATEURS PAR MOBTYPE — GDD §3.3.1
    // =========================================================

    private static float GetAtkMult(MobType type) => type switch
    {
        MobType.Elite       => 1.8f,
        MobType.BossZone    => 2.5f,
        MobType.BossDungeon => 3.0f,
        MobType.BossRaid    => 4.0f,
        _                   => 1.0f,
    };

    private static float GetHPMult(MobType type) => type switch
    {
        MobType.Elite       => 4.0f,
        MobType.BossZone    => 10.0f,
        MobType.BossDungeon => 12.0f,
        MobType.BossRaid    => 60.0f,
        _                   => 1.0f,
    };

    private static float GetManaMult(MobType type) => type switch
    {
        MobType.Elite       => 1.8f,
        MobType.BossZone    => 2.5f,
        MobType.BossDungeon => 3.0f,
        MobType.BossRaid    => 4.0f,
        _                   => 1.0f,
    };

    // =========================================================
    // CALCUL PRINCIPAL
    // =========================================================

    /// <summary>
    /// Calcule les stats finales d'un mob et les retourne dans un
    /// MobComputedStats. Appelé par Mob.ApplyData() au spawn.
    /// </summary>
    public static MobComputedStats Calculate(MobData data, int level)
    {
        if (data == null)
        {
            Debug.LogError("[MobStatCalculator] MobData null — stats par défaut.");
            return new MobComputedStats();
        }

        level = Mathf.Max(1, level);
        int lvlOffset = level - 1;

        float atkMult  = GetAtkMult(data.mobType);
        float hpMult   = GetHPMult(data.mobType);
        float manaMult = GetManaMult(data.mobType);

        // ── HP / Mana ────────────────────────────────────────
        float hp   = (data.baseHP   + data.hpPerLevel   * lvlOffset) * hpMult;
        float mana = (data.baseMana + data.manaPerLevel * lvlOffset) * manaMult;

        // ── Attaque ──────────────────────────────────────────
        float atkMin = (data.baseAtkMin + data.atkMinPerLevel * lvlOffset) * atkMult;
        float atkMax = (data.baseAtkMax + data.atkMaxPerLevel * lvlOffset) * atkMult;

        // ── Défense — chaque profil scale indépendamment ─────
        // Pas de multiplicateur MobType — les valeurs base + perLevel
        // suffisent à différencier les profils par type de mob.
        float meleeDef  = data.baseDefMelee  + data.defMeleePerLevel  * lvlOffset;
        float rangedDef = data.baseDefRanged + data.defRangedPerLevel * lvlOffset;
        float magicDef  = data.baseDefMagic  + data.defMagicPerLevel  * lvlOffset;

        // ── Précision / Esquive ───────────────────────────────
        float precision = data.basePresision + data.precisionPerLevel * lvlOffset;
        float dodge     = data.baseDodge     + data.dodgePerLevel     * lvlOffset;

        // ── Critique — fixe, ne scale pas avec le niveau ─────
        float critChance     = data.baseCritChance;
        float critMultiplier = data.baseCritMultiplier;

        // ── Points élémentaires — scalent avec le niveau ──────
        // Même principe que le joueur : valeur brute poussée sur Entity,
        // lue par CombatSystem via GetElementPoints(). GDD §6.2.
        float elemPoints = data.baseElementPointValue + data.elementPointValuePerLevel * lvlOffset;

        // ── Regen — uniquement si définie sur le SO (boss) ───
        float regenHP   = data.regenHP;
        float regenMana = data.regenMana;

        return new MobComputedStats
        {
            maxHP          = Mathf.Max(1f,  hp),
            maxMana        = Mathf.Max(0f,  mana),
            regenHP        = regenHP,
            regenMana      = regenMana,
            atkMin         = Mathf.Max(1f,  atkMin),
            atkMax         = Mathf.Max(1f,  atkMax),
            meleeDef       = Mathf.Max(0f,  meleeDef),
            rangedDef      = Mathf.Max(0f,  rangedDef),
            magicDef       = Mathf.Max(0f,  magicDef),
            precision      = Mathf.Max(0f,  precision),
            dodge          = Mathf.Max(0f,  dodge),
            critChance     = Mathf.Clamp01(critChance),
            critMultiplier = Mathf.Max(1f,  critMultiplier),
            elemPoints     = Mathf.Max(0f,  elemPoints),
        };
    }
}

// =============================================================
// STRUCT RÉSULTAT — passé à Mob.ApplyData()
// =============================================================

public struct MobComputedStats
{
    public float maxHP;
    public float maxMana;
    public float regenHP;
    public float regenMana;
    public float atkMin;
    public float atkMax;
    public float meleeDef;
    public float rangedDef;
    public float magicDef;
    public float precision;
    public float dodge;
    public float critChance;
    public float critMultiplier;
    public float elemPoints;
}
