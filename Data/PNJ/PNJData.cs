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

[CreateAssetMenu(fileName = "pnj_", menuName = "AetherTree/PNJ/PNJData")]
public class PNJData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"pnj_\"\n" +
             "(ex: \"pnj_forgeron_braven\"). Ne JAMAIS afficher au joueur — voir pnjName pour l'affichage.")]
    public string  pnjID;
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

    // ── Boutique — tout PNJType.HasShop() (modèle composable §13.2) ─────────
    // Une seule liste — ShopEntry.item est un ScriptableObject générique (WeaponData,
    // ResourceData, SkillData, PermanentSkillData, PassiveSkillData...), ShopUI dispatch
    // déjà par type. Un PNJ "coach" qui vend des skills utilise cette même liste, glisser
    // les SkillData dedans — pas de liste séparée.
    [ShowIf(nameof(pnjType), PNJType.Merchant, PNJType.Forge, PNJType.Antiquarian,
        PNJType.Cordonnier, PNJType.Cook, PNJType.Tinkerer, PNJType.Jeweler, PNJType.Hatter,
        PNJType.CraftStation, Header = "Boutique (PNJType.HasShop())")]
    public List<ShopEntry> shopItems = new List<ShopEntry>();

    // ── Antiquaire ────────────────────────────────────────────
    [ShowIf(nameof(pnjType), PNJType.Antiquarian, Header = "Antiquaire (PNJType.Antiquarian)")]
    public bool canIdentifyRunes = false;
    [ShowIf(nameof(pnjType), PNJType.Antiquarian)]
    public bool canInsertRunes   = false;

    // ── Quête ─────────────────────────────────────────────────
    [ShowIf(nameof(pnjType), PNJType.Quest, Header = "Quête (PNJType.Quest)")]
    public List<QuestData> availableQuests = new List<QuestData>();

    // ── Maire ─────────────────────────────────────────────────
    [ShowIf(nameof(pnjType), PNJType.Mayor, Header = "Maire (PNJType.Mayor)")]
    public DialogueData guildUnlockDialogue;
    [ShowIf(nameof(pnjType), PNJType.Mayor)]
    public DialogueData guildNotReadyDialogue;
    [Tooltip("Coût en Aeris pour créer une guilde — GDD v3.5 §3.4")]
    [ShowIf(nameof(pnjType), PNJType.Mayor)]
    public int guildCreationCost = 0;

    // ── PNJ Faction (obsolète, gardé pour compat assets existants) ──
#pragma warning disable CS0618
    [ShowIf(nameof(pnjType), PNJType.FactionNPC, Header = "Faction (PNJType.FactionNPC)")]
    public FactionType  faction = FactionType.None;
    [ShowIf(nameof(pnjType), PNJType.FactionNPC)]
    public DialogueData hostileDialogue;
#pragma warning restore CS0618

    // ── Capitaine de Port ─────────────────────────────────────
    [ShowIf(nameof(pnjType), PNJType.HarborMaster, Header = "Capitaine de Port (PNJType.HarborMaster)")]
    public List<string> availableDestinations = new List<string>();
    [ShowIf(nameof(pnjType), PNJType.HarborMaster)]
    public float departureIntervalMin = 300f;
    [ShowIf(nameof(pnjType), PNJType.HarborMaster)]
    public float departureIntervalMax = 900f;

    // ── Mort & Respawn ────────────────────────────────────────
    [Header("Mort & Respawn")]
    [Tooltip("false = invulnérable (civils, décoratifs).\ntrue = peut mourir et respawner.")]
    public bool  canDie       = false;
    [Tooltip("Délai de respawn en secondes après mort. 0 = pas de respawn.")]
    [ShowIf(nameof(canDie), true)]
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
    [ShowIf(nameof(canFight), true)]
    public float baseMaxHP = 200f;

    [Tooltip("Mana maximum. 0 si le PNJ n'utilise que des skills sans coût.")]
    [ShowIf(nameof(canFight), true)]
    public float baseMaxMana = 50f;

    [Tooltip("Regen HP/s hors combat. 0 = pas de regen.")]
    [ShowIf(nameof(canFight), true)]
    public float baseRegenHP = 2f;

    [Tooltip("Dégâts de base de l'attaque — utilisés par CombatSystem.CalculateMobDamage() " +
             "via Entity.AttackDamageMin/Max.\nIgnoré si basicAttackSkill calcule ses propres dégâts via ratios.")]
    [ShowIf(nameof(canFight), true)]
    public float attackDamage = 15f;

    [Tooltip("Catégorie d'arme — détermine quelle défense de la cible s'applique. GDD §3.1.")]
    [ShowIf(nameof(canFight), true)]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;

    [Tooltip("Skill d'attaque de base — utilisé à chaque attackCooldown.\n" +
             "Doit avoir targetType = Target et effectType = Damage.\nObligatoire si canFight.")]
    [ShowIf(nameof(canFight), true)]
    public SkillData basicAttackSkill;

    [Tooltip("Skills secondaires (prioritaires sur l'attaque de base).\nMême logique que MobData.skills.")]
    [ShowIf(nameof(canFight), true)]
    public List<SkillData> skills = new List<SkillData>();

    [Tooltip("Rayon de détection des ennemis (Mobs). Équivalent de detectionRange sur MobData.")]
    [ShowIf(nameof(canFight), true)]
    public float aggroRadius = 15f;

    [Tooltip("Distance max depuis le spawn avant de lâcher la cible et rentrer. 0 = illimité.")]
    [ShowIf(nameof(canFight), true)]
    public float leashRadius = 10f;

    [Tooltip("Portée d'attaque. 0 = utilise basicAttackSkill.range.")]
    [ShowIf(nameof(canFight), true)]
    public float attackRange = 0f;

    [Tooltip("Cooldown de l'attaque de base en secondes.")]
    [ShowIf(nameof(canFight), true)]
    public float attackCooldown = 2f;

    [Tooltip("Vitesse de déplacement en mode combat. 0 = utilise baseMoveSpeed.")]
    [ShowIf(nameof(canFight), true)]
    public float combatMoveSpeed = 0f;

    // ── Critique ──────────────────────────────────────────────
    [Tooltip("Chance de critique [0..1]. Poussé sur Entity via SetCritChance().")]
    [ShowIf(nameof(canFight), true)]
    public float critChance     = 0.03f;
    [Tooltip("Multiplicateur dégâts critique. Base 1.5f.")]
    [ShowIf(nameof(canFight), true)]
    public float critMultiplier = 1.5f;

    // ── Effets On-Hit — actifs même hors canFight (un garde peut avoir Thorns) ──
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // =========================================================
    // VALIDATION EDITOR
    // =========================================================

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(pnjID))
            pnjID = name;

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

// ── Types de PNJ — GDD v3.5 §3.4, modèle composable "Boutique + onglets" §13.2 ──
//
// Depuis §13.2 : la plupart des PNJ marchands suivent le même modèle — une Boutique
// (achat/vente, ShopUI) + un ou plusieurs onglets complémentaires propres à leur métier.
// Seuls Guard/Decorative/Mayor/Quest/HarborMaster restent dialogue-only, sans fenêtre.
//
// Ordinaux figés : Unity sérialise un enum par sa position int, pas son nom (voir
// [[project_aethertree_passive_system_unification]] pour le précédent qui a motivé cette
// règle). Blacksmith/FusionNPC ont juste été RENOMMÉS (Forge/Cordonnier) — même ordinal,
// aucune migration d'asset nécessaire. CraftMaster/FactionNPC sont retirés du design mais
// gardés ici comme placeholders [Obsolete] pour ne pas décaler Quest/Mayor/HarborMaster/
// Guard/Decorative qui suivent (des .asset existants sérialisent déjà ces ordinaux — voir
// PNJ_01.asset/PNJ_02.asset pour CraftMaster, TestPnjData.asset pour HarborMaster). Les 5
// nouveaux types (Cook..CraftStation) sont ajoutés en fin d'enum, jamais au milieu.
public enum PNJType
{
    Merchant      = 0,  // Boutique seule
    Forge         = 1,  // Boutique + Craft Équipement + Upgrade (+0→+10) + Pari (rareté) — ex-Blacksmith

    [System.Obsolete("Retiré du design (2026) — Pari de rareté est un onglet de PNJType.Forge, " +
                      "plus un PNJ séparé. Réassigner les PNJData existants (ex: PNJ_rareté.asset) " +
                      "vers Forge. Ordinal gardé pour ne pas décaler Antiquarian/Cordonnier/... qui suivent.")]
    Rarity        = 2,  // RETIRÉ, placeholder — voir PNJType.Forge

    Antiquarian   = 3,  // Boutique + Identification (runes)
    Cordonnier    = 4,  // Boutique + Fusion Gants/Bottes (S0→S6) — ex-FusionNPC

    // 5 retiré (2026-09-07) — CraftMaster, déblocage métiers jamais implémenté. Zéro case dans
    // PNJ.cs (contrairement à Rarity/FactionNPC, toujours wirés eux) — vérifié avant
    // suppression. Ordinal 5 jamais réutilisé.

    Quest         = 6,  // Donneur de quêtes — conditions + récompenses
    Mayor         = 7,  // Création de guilde — dialogue conditionnel

    [System.Obsolete("Retiré du design (2026) — quêtes de faction pas prioritaires actuellement. " +
                      "Ordinal gardé pour ne pas décaler HarborMaster/Guard/Decorative déjà sérialisés.")]
    FactionNPC    = 8,  // RETIRÉ, placeholder

    HarborMaster  = 9,  // Navigation bateau — choix de destination (dialogue seul)
    Guard         = 10, // Dialogue neutre + IA combat mobs proches (dialogue seul)
    Decorative    = 11, // Ambiance, lore, rumeurs — pas de service (dialogue seul)

    // ── Ajoutés §13.2 — modèle composable Boutique + onglets ──────────────────
    Cook          = 12, // Boutique + Cuisiner (nourriture + potions, partagé Alchimie)
    Tinkerer      = 13, // Boutique + Bricoler (fusion ressources + déco housing)
    Jeweler       = 14, // Boutique + Gemmes (pose sur bijoux)
    Hatter        = 15, // Boutique + Craft de casques
    CraftStation  = 16, // Boutique + Craft (ressources intermédiaires, tous domaines)
}

// ── Onglets de la fenêtre PNJ partagée — GDD §13.2 ────────────────────────────
// Boutique toujours en 1er (onglet par défaut à l'ouverture) pour tout PNJType
// composable. Le contenu réel de chaque onglet est résolu par PNJWindowUI —
// certains (CraftEquipement, Cuisiner...) n'ont pas encore d'écran, affichés
// avec un panneau placeholder en attendant (voir PNJWindowUI.cs).
public enum PNJTabID
{
    Boutique            = 0,
    CraftEquipement     = 1,   // Forgeron
    Upgrade             = 2,   // Forgeron
    Pari                = 3,   // Forgeron
    Cuisiner            = 4,   // Cuisinier
    Fusion              = 5,   // Cordonnier
    Bricoler            = 6,   // Bricoleur
    Gemmes              = 7,   // Bijoutier
    CraftCasque         = 8,   // Tailleur
    Identification      = 9,   // Antiquaire
    CraftIntermediaire  = 10,  // Station de Craft

    CraftGantsBottes    = 11,  // Cordonnier — craft de base, distinct de Fusion
    CraftBijoux         = 12,  // Bijoutier — craft de base, distinct de Gemmes
}

// ── PNJ ayant une Boutique (ShopUI) — modèle composable §13.2 ────────────────
public static class PNJTypeExtensions
{
    public static bool HasShop(this PNJType type) => type switch
    {
        PNJType.Merchant or PNJType.Forge or PNJType.Antiquarian or
        PNJType.Cordonnier or PNJType.Cook or PNJType.Tinkerer or PNJType.Jeweler or
        PNJType.Hatter or PNJType.CraftStation => true,
        _ => false,
    };

    /// <summary>Liste ordonnée des onglets de la fenêtre PNJ partagée pour ce type —
    /// Boutique toujours en premier. Liste vide pour les PNJ dialogue-only.</summary>
    public static List<PNJTabID> GetTabs(this PNJType type) => type switch
    {
        PNJType.Merchant     => new List<PNJTabID> { PNJTabID.Boutique },
        PNJType.Forge        => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftEquipement, PNJTabID.Upgrade, PNJTabID.Pari },
        PNJType.Cook         => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Cuisiner },
        PNJType.Cordonnier   => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftGantsBottes, PNJTabID.Fusion },
        PNJType.Tinkerer     => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Bricoler },
        PNJType.Jeweler      => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftBijoux, PNJTabID.Gemmes },
        PNJType.Hatter       => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftCasque },
        PNJType.Antiquarian  => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Identification },
        PNJType.CraftStation => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftIntermediaire },
        _                    => new List<PNJTabID>(),
    };
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
    None     = 0,   // PNJ neutre — accessible à tous
    Solthars = 1,   // PNJ Solthars — hostile aux Umbrans
    Umbrans  = 2,   // PNJ Umbrans — hostile aux Solthars
}
