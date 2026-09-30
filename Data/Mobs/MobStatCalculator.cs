using UnityEngine;

// =============================================================
// MOBSTATCALCULATOR — Calcul des stats finales d'un mob au spawn
// Path : Assets/Scripts/Data/Mobs/MobStatCalculator.cs
//
// Formule générale :
//   statFinale = statBase + statParNiveau × (level - 1)
//
// Plus de multiplicateur par MobType (retiré 2026-09-24, demande Florian) —
// un boss n'est plus un mob "Normal" automatiquement mis à l'échelle, ses
// stats de base sont tapées directement sur son propre MobData, pour un
// contrôle d'équilibrage total sans facteur caché à recalculer mentalement.
// MobType reste une catégorie (Normal/MobDungeon/BossMap/BossWorld/
// MobInvasion — un boss de donjon est MobDungeon + DungeonRole.Boss, un boss
// d'invasion est MobInvasion + InvasionRole.Boss) lue par IsBoss() et par
// d'autres systèmes (annonces, IA...), mais n'influence plus aucun calcul
// de stat ici.
//
// Précision, esquive, critique : scalent par niveau via les champs perLevel.
// Critique : fixe sur le SO (baseCritChance, baseCritMultiplier).
//
// Appelé par Mob.ApplyData() après assignation de mob.data et mob.mobLevel.
// =============================================================

public static class MobStatCalculator
{
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

        // ── HP / Mana ────────────────────────────────────────
        float hp   = data.baseHP   + data.hpPerLevel   * lvlOffset;
        float mana = data.baseMana + data.manaPerLevel * lvlOffset;

        // ── Attaque ──────────────────────────────────────────
        float atkMin = data.baseAtkMin + data.atkMinPerLevel * lvlOffset;
        float atkMax = data.baseAtkMax + data.atkMaxPerLevel * lvlOffset;

        // ── Défense — chaque profil scale indépendamment ─────
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
