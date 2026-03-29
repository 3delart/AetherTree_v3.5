using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PNJDATA — ScriptableObject de configuration d'un PNJ
// Path : Assets/Scripts/Data/PNJData.cs
// AetherTree GDD v3.5 — §3.4 (PNJ)
//
// Contient toutes les données statiques d'un PNJ :
// type, stats, dialogues, shop, quêtes, respawn.
//
// Combat :
//   canFight = true → PNJ.cs active HandleCombatAI() via SkillSystem.
//   Fonctionne pour tout pnjType — Guard, Merchant itinérant, FactionNPC...
//   basicAttackSkill est obligatoire si canFight.
//   skills (optionnel) = liste de skills secondaires, même pattern que MobData.
//
// Mort / Respawn :
//   canDie = false → invulnérable (civils, décoratifs).
//   canDie = true  → meurt et respawne après respawnDelay secondes.
//   Remplace l'ancienne condition hardcodée if (pnjType == Guard).
//
// Créer via : Assets > Create > AetherTree > PNJ > PNJData
// =============================================================

[CreateAssetMenu(fileName = "PNJ_New", menuName = "AetherTree/PNJ/PNJData")]
public class PNJData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    public string  pnjName = "PNJ";
    public PNJType pnjType = PNJType.Decorative;
    public Sprite  portrait;

    // ── Dialogue ──────────────────────────────────────────────
    [Header("Dialogue")]
    public DialogueData defaultDialogue;
    public DialogueData knownPlayerDialogue;
    public DialogueData highReputationDialogue;
    [Tooltip("Rang de Réputation Monde minimum pour le dialogue premium (0 = désactivé)")]
    public int reputationDialogueThreshold = 0;

    // ── Marchand ──────────────────────────────────────────────
    [Header("Marchand (PNJType.Merchant)")]
    public List<ShopEntry> shopItems  = new List<ShopEntry>();
    public List<ShopEntry> shopSkills = new List<ShopEntry>();

    // ── Forgeron ──────────────────────────────────────────────
    [Header("Forgeron (PNJType.Blacksmith)")]
    [Tooltip("Niveau d'upgrade maximum autorisé par ce forgeron (3 = Braven, 10 = Erenthal)")]
    public int maxUpgradeLevel = 3;

    // ── Antiquaire ────────────────────────────────────────────
    [Header("Antiquaire (PNJType.Antiquarian)")]
    public bool canIdentifyRunes = false;
    public bool canInsertRunes   = false;

    // ── Quête ─────────────────────────────────────────────────
    [Header("Quête (PNJType.Quest)")]
    public List<QuestData> availableQuests = new List<QuestData>();

    // ── Maire ─────────────────────────────────────────────────
    [Header("Maire (PNJType.Mayor)")]
    public DialogueData guildUnlockDialogue;
    public DialogueData guildNotReadyDialogue;
    [Tooltip("Coût en Aeris pour créer une guilde — GDD v3.5 §3.4")]
    public int guildCreationCost = 0;

    // ── PNJ Faction ───────────────────────────────────────────
    [Header("Faction (PNJType.FactionNPC)")]
    public FactionType  faction = FactionType.None;
    public DialogueData hostileDialogue;

    // ── Capitaine de Port ─────────────────────────────────────
    [Header("Capitaine de Port (PNJType.HarborMaster)")]
    public List<string> availableDestinations = new List<string>();
    public float departureIntervalMin = 300f;
    public float departureIntervalMax = 900f;

    // ── Mort & Respawn ────────────────────────────────────────
    [Header("Mort & Respawn")]
    [Tooltip("false = invulnérable (civils, décoratifs).\ntrue = peut mourir et respawner.")]
    public bool  canDie       = false;
    [Tooltip("Délai de respawn en secondes après mort. 0 = pas de respawn.")]
    public float respawnDelay = 60f;

    // ── Déplacement ───────────────────────────────────────────
    [Header("Déplacement")]
    [Tooltip("Vitesse de déplacement de base (patrouille, déambulation).")]
    public float baseMoveSpeed = 2f;

    // ── Stats défensives — tous les PNJ ──────────────────────
    // Même pipeline que MobData — CombatSystem lit sur Entity.
    // Les PNJ non combattants gardent ces valeurs pour encaisser
    // quelques coups si un mob attaque un village.
    [Header("Stats défensives")]
    public float meleeDefense  = 10f;
    public float rangedDefense = 8f;
    public float magicDefense  = 5f;
    public float precision     = 10f;
    public float dodge         = 5f;

    // ── Combat — PNJ canFight ─────────────────────────────────
    // Activé dès que canFight = true, quel que soit le pnjType.
    // Guard, marchand itinérant, PNJ faction... tous passent par
    // le même HandleCombatAI() via SkillSystem.Execute().
    [Header("Combat (canFight)")]
    [Tooltip("Active l'IA de combat et les stats HP/Mana/Attaque.\nObligatoire : basicAttackSkill doit être assigné.")]
    public bool canFight = false;

    [Tooltip("HP maximum du PNJ combattant.")]
    public float baseMaxHP = 200f;

    [Tooltip("Mana maximum. 0 si le PNJ n'utilise que des skills sans coût.")]
    public float baseMaxMana = 50f;

    [Tooltip("Regen HP/s hors combat. 0 = pas de regen.")]
    public float baseRegenHP = 2f;

    [Tooltip("Dégâts de base de l'attaque — utilisés par CombatSystem.CalculateMobDamage() " +
             "via Entity.AttackDamageMin/Max.\nIgnoré si basicAttackSkill calcule ses propres dégâts via ratios.")]
    public float attackDamage = 15f;

    [Tooltip("Catégorie d'arme — détermine quelle défense de la cible s'applique. GDD §3.1.")]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;

    [Tooltip("Skill d'attaque de base — utilisé à chaque attackCooldown.\n" +
             "Doit avoir targetType = Target et effectType = Damage.\nObligatoire si canFight.")]
    public SkillData basicAttackSkill;

    [Tooltip("Skills secondaires (prioritaires sur l'attaque de base).\nMême logique que MobData.skills.")]
    public List<SkillData> skills = new List<SkillData>();

    [Tooltip("Rayon de détection des ennemis (Mobs). Équivalent de detectionRange sur MobData.")]
    public float aggroRadius = 15f;

    [Tooltip("Distance max depuis le spawn avant de lâcher la cible et rentrer. 0 = illimité.")]
    public float leashRadius = 10f;

    [Tooltip("Portée d'attaque. 0 = utilise basicAttackSkill.range.")]
    public float attackRange = 0f;

    [Tooltip("Cooldown de l'attaque de base en secondes.")]
    public float attackCooldown = 2f;

    [Tooltip("Vitesse de déplacement en mode combat. 0 = utilise baseMoveSpeed.")]
    public float combatMoveSpeed = 0f;

    // ── Critique ──────────────────────────────────────────────
    [Tooltip("Chance de critique [0..1]. Poussé sur Entity via SetCritChance().")]
    public float critChance     = 0.03f;
    [Tooltip("Multiplicateur dégâts critique. Base 1.5f.")]
    public float critMultiplier = 1.5f;

    // =========================================================
    // VALIDATION EDITOR
    // =========================================================

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (canFight && basicAttackSkill == null)
            Debug.LogWarning($"[PNJData] {pnjName} : canFight = true mais basicAttackSkill non assigné !");

        if (canFight && !canDie)
            Debug.LogWarning($"[PNJData] {pnjName} : canFight = true mais canDie = false — " +
                             "ce PNJ peut attaquer mais est invulnérable. Intentionnel ?");

        if (attackRange == 0f && basicAttackSkill != null && basicAttackSkill.range > 0f)
        {
            // Pas une erreur — juste informatif : PNJ.cs utilisera basicAttackSkill.range
        }
    }
#endif
}

// ── Types de PNJ — GDD v3.5 §3.4 ─────────────────────────────
public enum PNJType
{
    Merchant,       // Achat/vente items consommables et ressources
    Blacksmith,     // Amélioration arme/armure (+0→+3 Braven, +0→+10 Erenthal)
    Antiquarian,    // Identification et insertion de runes
    FusionNPC,      // Fusion de Gants & Bottes (S0→S6)
    CraftMaster,    // Déblocage activités (Bûcheron, Pêcheur...)
    Quest,          // Donneur de quêtes — conditions + récompenses
    Mayor,          // Création de guilde — dialogue conditionnel
    FactionNPC,     // Services et quêtes Solthars / Umbrans
    HarborMaster,   // Navigation bateau — choix de destination
    Guard,          // Dialogue neutre + IA combat mobs proches
    Decorative,     // Ambiance, lore, rumeurs — pas de service
}

// ── Entrée de shop ────────────────────────────────────────────
[System.Serializable]
public class ShopEntry
{
    [Tooltip("Glisse le SO ici (WeaponData, ArmorData, ConsumableData, ResourceData, SkillData...)")]
    public ScriptableObject item;
    public int  aerisCost;
    [Tooltip("Rang de Réputation Monde minimum (0 = toujours visible)")]
    public int  requiredWorldReputationRank = 0;
    public bool isUnlimitedStock = true;
    public int  stockCount = 1;
}

// ── Faction — GDD v3.5 §3.4 ───────────────────────────────────
public enum FactionType
{
    None,       // PNJ neutre — accessible à tous
    Solthars,   // PNJ Solthars — hostile aux Umbrans
    Umbrans,    // PNJ Umbrans — hostile aux Solthars
}
