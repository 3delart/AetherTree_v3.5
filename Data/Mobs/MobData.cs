using UnityEngine;
using System.Collections.Generic;

// =============================================================
// MOBDATA — ScriptableObject template de mob
// Path : Assets/Scripts/Data/Mobs/MobData.cs
// AetherTree GDD v3.5 — §3.3 / §3.3.1
//
// Principe de scaling (v3.5) :
//   Les stats de base (lv1) + la valeur de croissance par niveau
//   sont définies sur ce SO. MobStatCalculator applique les
//   multiplicateurs MobType au runtime lors du spawn.
//
//   statFinale = (statBase + statParNiveau × (level-1)) × multMobType
//
//   Les résistances élémentaires et les multiplicateurs de défense
//   (meleeDefMult, rangedDefMult, magicDefMult) sont des profils
//   fixes — ils ne scalent pas avec le niveau.
// =============================================================

[CreateAssetMenu(fileName = "mob_", menuName = "AetherTree/Mob/MobData")]
public class MobData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"mob_\"\n" +
             "(ex: \"mob_loup_gris\"). Ne JAMAIS afficher au joueur — voir mobName pour l'affichage.")]
    public string    mobID;
    public string    mobName = "Mob";
    public MobType   mobType = MobType.Normal;
    public MobAIType aiType  = MobAIType.Passive;

    // ── Élémentaire ───────────────────────────────────────────
    [Header("Élémentaire")]
    [Tooltip("Élément fixe du mob — cohérent avec le biome (§22.3)")]
    public ElementType elementType = ElementType.Neutral;
    public float baseElementPointValue = 1;
    public float elementPointValuePerLevel = 1;

    // ── Stats de base (niveau 1) ──────────────────────────────
    [Header("Stats de base — Niveau 1 (Type Normal)")]
    [Tooltip("HP au niveau 1 avant multiplicateur MobType.")]
    public float baseHP      = 50f;
    [Tooltip("Mana au niveau 1 avant multiplicateur MobType.")]
    public float baseMana    = 0f;
    [Tooltip("Dégâts min au niveau 1 avant multiplicateur MobType.")]
    public float baseAtkMin  = 7f;
    [Tooltip("Dégâts max au niveau 1 avant multiplicateur MobType.")]
    public float baseAtkMax  = 9f;
    [Tooltip("Défense de base au niveau 1 — déclinée via les mults mêlée/distance/magie.")]
    public float baseDefMelee     = 7;
    public float baseDefRanged    = 9f;
    public float baseDefMagic     = 6f;

    public float basePresision = 15f;
    public float baseDodge     = 10f;
    public float baseCritChance = 0.1f;
    public float baseCritMultiplier = 1.3f;

    // ── Croissance par niveau ─────────────────────────────────
    [Header("Croissance par niveau (ajout flat par niveau au-delà de 1)")]
    [Tooltip("HP gagnés par niveau (ex: 20 → lv10 = 50 + 20×9 = 230 avant mult).")]
    public float hpPerLevel      = 20f;
    [Tooltip("Mana gagné par niveau.")]
    public float manaPerLevel    = 2f;
    [Tooltip("Dégâts min gagnés par niveau.")]
    public float atkMinPerLevel  = 3f;
    [Tooltip("Dégâts max gagnés par niveau.")]
    public float atkMaxPerLevel  = 5f;
    [Tooltip("Défense gagnée par niveau (base — avant profil mêlée/distance/magie).")]
    public float defMeleePerLevel  = 1f;
    public float defRangedPerLevel = 1.2f;
    public float defMagicPerLevel  = 0.8f;
    public float precisionPerLevel = 0.5f;
    public float dodgePerLevel     = 0.3f;



    // ── Regen — uniquement pour les boss ─────────────────────
    [Header("Régénération (0 = désactivée — uniquement boss / cas spéciaux)")]
    public float regenHP   = 0f;
    public float regenMana = 0f;

    // ── Combat ────────────────────────────────────────────────
    [Header("Combat")]
    public float attackRange    = 1.5f;
    public float attackCooldown = 1.5f;
    [Tooltip("Catégorie d'arme — détermine quelle défense du joueur s'applique. GDD §3.1.")]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;

    // ── Résistances élémentaires — profil fixe ────────────────
    [Header("Résistances élémentaires [0;1] — fixes, ne scalent pas")]
    [Range(0f, 1f)] public float fireResist      = 0f;
    [Range(0f, 1f)] public float waterResist     = 0f;
    [Range(0f, 1f)] public float lightningResist = 0f;
    [Range(0f, 1f)] public float earthResist     = 0f;
    [Range(0f, 1f)] public float natureResist    = 0f;
    [Range(0f, 1f)] public float darknessResist  = 0f;
    [Range(0f, 1f)] public float lightResist     = 0f;

    // ── Skills ────────────────────────────────────────────────
    [Header("Skills")]
    [Tooltip("Skill d'attaque de base. Si null, fallback sur dégâts directs.")]
    public SkillData basicAttackSkill;
    [Tooltip("Skills spéciaux — prioritaires sur l'attaque de base.")]
    public List<SkillData> skills = new List<SkillData>();

    // ── IA ────────────────────────────────────────────────────
    [Header("IA")]
    public float moveSpeed       = 3f;
    public float detectionRange  = 15f;
    public float leashMultiplier = 6f;
    public float patrolRadius    = 10f;

    // ── Cycle jour/nuit ───────────────────────────────────────
    [Header("Cycle Jour/Nuit")]
    [Tooltip("Si true, ce mob n'apparaît que la nuit (§18.1 / §20)")]
    public bool isNocturnal = false;

    // ── Loot ──────────────────────────────────────────────────
    [Header("Loot")]
    public LootTable lootTable;

    // ── Capture / Pet ─────────────────────────────────────────
    [Header("Capture")]
    [Tooltip("Si true, peut être capturé comme pet (§3.5)")]
    public bool  isCapturable       = false;
    [Range(0f, 1f)]
    public float captureHPThreshold = 0.2f;
    public PetType petType          = PetType.Damage;

    // ── Visuel ────────────────────────────────────────────────
    [Header("Visuel")]
    public Sprite portrait;

    // ── Prefab ────────────────────────────────────────────────
    [Header("Prefab")]
    public GameObject prefab;

    // =========================================================
    // UTILITAIRES
    // =========================================================

    /// <summary>Résistance élémentaire [0;1] pour un élément donné.</summary>
    public float GetElementalResistance(ElementType element) => element switch
    {
        ElementType.Fire      => fireResist,
        ElementType.Water     => waterResist,
        ElementType.Lightning => lightningResist,
        ElementType.Earth     => earthResist,
        ElementType.Nature    => natureResist,
        ElementType.Darkness  => darknessResist,
        ElementType.Light     => lightResist,
        _                     => 0f,
    };

    /// <summary>True si ce mob est un boss (BossZone, BossDungeon ou BossRaid).</summary>
    public bool IsBoss()
        => mobType == MobType.BossZone
        || mobType == MobType.BossDungeon
        || mobType == MobType.BossRaid;

    /// <summary>True si ce mob est actif selon le cycle jour/nuit.</summary>
    public bool IsActiveAtTime(bool isNight)
        => isNocturnal ? isNight : !isNight;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(mobID))
            mobID = name;

        if (baseAtkMax < baseAtkMin)
            baseAtkMax = baseAtkMin;

        if (atkMaxPerLevel < atkMinPerLevel)
            atkMaxPerLevel = atkMinPerLevel;

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

// ── Type de mob ───────────────────────────────────────────────
public enum MobType
{
    Normal,       // Mob standard
    Elite,        // ×1.8 atk / ×2.0 def / ×4.0 HP
    BossZone,     // ×2.5 atk / ×3.0 def / ×10.0 HP — erre en zone ouverte
    BossDungeon,  // ×3.0 atk / ×3.5 def / ×12.0 HP — fixe en donjon
    BossRaid,     // ×4.0 atk / ×5.0 def / ×60.0 HP — fixe en raid
    Nocturnal,    // Actif uniquement la nuit (§18.1 / §20)
    Capturable,   // Peut devenir un pet (§3.5)
}

// ── IA du mob ─────────────────────────────────────────────────
public enum MobAIType
{
    Passive,      // N'attaque que si agressé
    Aggressive,   // Attaque les joueurs à portée
    Boss,         // IA scriptée — phases de combat (§3.3)
}

// ── Type de pet potentiel ─────────────────────────────────────
public enum PetType { Tank, Damage, Support, Utility, Hybrid }
