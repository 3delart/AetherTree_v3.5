using UnityEngine;
using System.Collections.Generic;

// =============================================================
// MOBDATA — ScriptableObject template de mob
// Path : Assets/Scripts/Data/Mobs/MobData.cs
// AetherTree GDD v3.5 — 
//
// Changements v30 :
// — mobType : MobType (Normal, Elite, BossZone, BossDungeon, BossRaid, Nocturnal, Capturable)
// — dodge (ex-evasion) — §3.3
// — isNocturnal — §3.3
// — Aeris : géré dans LootTable (minAeris/maxAeris)
// — MobAIType conservé pour Passive/Aggressive (logique IA Mob.cs)
// =============================================================

[CreateAssetMenu(fileName = "NewMob", menuName = "AetherTree/Mob/MobData")]
public class MobData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string    mobName = "Mob";
    public MobType   mobType = MobType.Normal;
    public MobAIType aiType  = MobAIType.Passive;

    // ── Niveau ────────────────────────────────────────────────
    [Header("Niveau")]
    [Tooltip("Niveau du mob — utilisé pour le scaling des stats (section 8.3 / 8.4)")]
    public int mobLevel = 1;

    // ── Élémentaire ───────────────────────────────────────────
    [Header("Élémentaire")]
    [Tooltip("Élément fixe du mob — cohérent avec le biome (section 22.3)")]
    public ElementType elementType = ElementType.Neutral;

    // ── Stats ─────────────────────────────────────────────────
    [Header("HP/MP")]
    public float maxHP         = 50f;
    public float maxMana       = 0f;
    public float regenHP       = 0f;
    public float regenMana     = 0f;

    [Header("Attack")]
    public float attackDamage    = 10f;
    public float attackRange     = 1.5f;
    public float attackCooldown  = 1.5f;
    [Tooltip("Chance de critique [0..1]. Poussé sur Entity via SetCritChance() dans Mob.ApplyData().")]
    public float critChance      = 0.05f;
    [Tooltip("Multiplicateur dégâts critique. Base 1.5f. Poussé via SetCritMultiplier() dans Mob.ApplyData().")]
    public float critMultiplier  = 1.5f;
    [Tooltip("Catégorie d'arme — détermine quelle défense du joueur/PNJ s'applique dans CombatSystem. GDD §3.1.")]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;




    // ── Effets sur attaque de base ────────────────────────────
    [Header("Effets sur attaque de base")]
    [Tooltip("Effets appliqués lors de l'attaque de base.\nEx: Poison 10% | Slow 5%")]
    public System.Collections.Generic.List<StatusEffectEntry> statusEffects = new System.Collections.Generic.List<StatusEffectEntry>();


    // ── Skills ───────────────────────────────────────────────
    [Header("Skills (utilisés en combat)")]
    [Tooltip("Skill d'attaque de base — utilisé à chaque attackCooldown.\nDoit avoir targetType = Target et effectType = Damage.\nSi null, fallback sur dégâts directs (attackDamage).")]
    public SkillData basicAttackSkill;
    [Tooltip("Skills spéciaux que ce mob peut lancer en combat (prioritaires sur l'attaque de base).\nSi vide, attaque de base uniquement.")]
    public System.Collections.Generic.List<SkillData> skills = new System.Collections.Generic.List<SkillData>();



    // ── Précision & Esquive — GDD v30 §6.10 ──────────────────
    [Header("Précision & Esquive")]
    [Tooltip("Précision du mob — utilisée dans la formule Miss% (§6.10)\nFormule : Esquive^6 / (Esquive^6 + Précision^6) × 100")]
    public float precision = 15f;
    [Tooltip("Esquive du mob — utilisée dans la formule Miss% (§6.10)")]
    public float dodge     = 10f;

    [Header("Defense")]
    [Tooltip("Défense contre les attaques Melee — pipeline §21.1")]
    public float meleeDefense  = 0f;
    [Tooltip("Défense contre les attaques Ranged — pipeline §21.1")]
    public float rangedDefense = 0f;
    [Tooltip("Défense contre les attaques Magic — pipeline §21.1")]
    public float magicDefense  = 0f;

    

    // ── Résistances élémentaires ──────────────────────────────
    [Header("Résistances élémentaires [0;1]")]
    [Range(0f, 1f)] public float fireResist      = 0f;
    [Range(0f, 1f)] public float waterResist     = 0f;
    [Range(0f, 1f)] public float lightningResist = 0f;
    [Range(0f, 1f)] public float earthResist     = 0f;
    [Range(0f, 1f)] public float natureResist    = 0f;
    [Range(0f, 1f)] public float darknessResist  = 0f;
    [Range(0f, 1f)] public float lightResist     = 0f;

    // ── Mouvement & Combat ────────────────────────────────────
    [Header("IA")]
    public float moveSpeed       = 3f;
    public float detectionRange  = 15f;
    public float leashMultiplier = 6f;
    public float patrolRadius    = 10f;

    

    // ── Cycle jour/nuit — GDD v30 §18.1 / §20 ───────────────
    [Header("Cycle Jour/Nuit")]
    [Tooltip("Si true, ce mob n'apparaît que la nuit (§18.1 / §20)")]
    public bool isNocturnal = false;

    // ── Loot ──────────────────────────────────────────────────
    // XP + Aeris + items : tout défini dans LootTable
    [Header("Loot")]
    public LootTable lootTable;

    // ── Capture / Pet — GDD v21 section 13 / 22.1 ────────────
    [Header("Capture")]
    [Tooltip("Si true, peut être capturé comme pet (section 13 / 22.7)")]
    public bool  isCapturable       = false;
    [Range(0f, 1f)]
    public float captureHPThreshold = 0.2f;
    public PetType petType          = PetType.Damage;

    // ── Visuel ────────────────────────────────────────────────
    [Header("Visuel")]
    [Tooltip("Portrait affiché dans le TargetPanel et les fenêtres détail")]
    public Sprite portrait;

    
    
    // ── Prefab ────────────────────────────────────────────────
    [Header("Prefab")]
    public GameObject prefab;

    // =========================================================
    // UTILITAIRES
    // =========================================================

    /// <summary>Résistance élémentaire [0;1] pour un élément donné.</summary>
    public float GetElementalResistance(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire:      return fireResist;
            case ElementType.Water:     return waterResist;
            case ElementType.Lightning: return lightningResist;
            case ElementType.Earth:     return earthResist;
            case ElementType.Nature:    return natureResist;
            case ElementType.Darkness:  return darknessResist;
            case ElementType.Light:     return lightResist;
            default:                    return 0f;
        }
    }

    /// <summary>True si ce mob est un boss (BossZone, BossDungeon ou BossRaid).</summary>
    public bool IsBoss()
        => mobType == MobType.BossZone
        || mobType == MobType.BossDungeon
        || mobType == MobType.BossRaid;

    /// <summary>
    /// True si ce mob est actif selon le cycle jour/nuit.
    /// Appelé par SpawnManager — GDD v30 §18.1 / §20.
    /// </summary>
    public bool IsActiveAtTime(bool isNight)
        => isNocturnal ? isNight : !isNight;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mobType == MobType.Capturable && !isCapturable)
        {
            isCapturable = true;
            Debug.LogWarning($"[MobData] {mobName} : mobType Capturable → isCapturable forcé à true.");
        }

        if (mobType == MobType.Nocturnal && !isNocturnal)
        {
            isNocturnal = true;
            Debug.LogWarning($"[MobData] {mobName} : mobType Nocturnal → isNocturnal forcé à true.");
        }


    }
#endif
}

// ── Type de mob — GDD v30 §18.1 ──────────────────────────────
public enum MobType
{
    Normal,       // Mob standard de la zone
    Elite,        // Version renforcée — ×3 HP, ×3 XP, meilleur loot (§18.1)
    BossZone,     // Erre librement dans une zone — ×10 HP, ×10 XP, loot rare (§18.1)
    BossDungeon,  // Fixe en donjon — reset à chaque instance
    BossRaid,     // Fixe en donjon raid — reset à chaque instance
    Nocturnal,    // Actif uniquement la nuit (§18.1 / §20)
    Capturable,   // Peut devenir un pet (§3.5 (famillier))
}

// ── IA du mob — logique d'aggro dans Mob.cs ───────────────────
public enum MobAIType
{
    Passive,      // N'attaque que si agressé
    Aggressive,   // Attaque les joueurs à portée
    Boss,         // IA scriptée — phases de combat (§3.3 phase boss)
}

// ── Type de pet potentiel ─────────────────────────────────────
public enum PetType { Tank, Damage, Support, Utility, Hybrid }
