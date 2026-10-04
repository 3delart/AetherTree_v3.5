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
//   canFight = true → PNJ.cs active CombatAIController.Tick() via SkillSystem.
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
    // Réorganisation complète 2026-10-04 (demande Florian) — ordre d'affichage Identité →
    // Dialogue → Mort & Respawn (canDie) → Combat (canFight) → Animation, chaque bloc ShowIf
    // sur sa propre condition plutôt que dispersé par PNJType comme avant.
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"pnj_\"\n" +
             "(ex: \"pnj_forgeron_braven\"). Ne JAMAIS afficher au joueur — voir pnjName pour l'affichage.")]
    public string  pnjID;
    public string  pnjName = "PNJ";
    public PNJType pnjType = PNJType.Decorative;

    // shopSpecialty remplace l'ancien éclatement en PNJType séparés (Forge/Antiquarian/
    // Cordonnier/Cook/Tinkerer/Jeweler/Hatter/CraftStation) — ces 8 types ne faisaient QUE
    // sélectionner un bundle d'onglets (voir PNJTypeExtensions.GetTabs() plus bas), jamais un
    // rôle d'interaction différent. None = boutique seule, comportement de l'ancien
    // PNJType.Merchant. Demande Florian 2026-10-04.
    [ShowIf(nameof(pnjType), PNJType.Merchant)]
    public ShopSpecialty shopSpecialty = ShopSpecialty.None;

    public Sprite portrait;

    // ── Dialogue ──────────────────────────────────────────────
    [Header("Dialogue")]
    // Masqué si pnjType = Purification (voir purificationDialogueByAuraRank juste en dessous) —
    // demande Florian 2026-10-04, assumé : un PNJ Purification bien configuré remplit ses 6
    // rangs d'aura, le fallback defaultDialogue que purificationDialogueByAuraRank utilise en
    // interne pour une entrée vide/manquante ne sert alors jamais en pratique. Liste explicite
    // (pas d'opérateur "différent de" sur ShowIf) — à compléter si un futur PNJType est ajouté
    // et doit aussi afficher ce champ.
#pragma warning disable CS0618 // Rarity/FactionNPC obsolètes mais toujours valides pour ce champ
    [ShowIf(nameof(pnjType), PNJType.Merchant, PNJType.Quest, PNJType.Teleporter, PNJType.Guard,
        PNJType.Decorative, PNJType.Rarity, PNJType.FactionNPC)]
    public DialogueData defaultDialogue;
#pragma warning restore CS0618

    // ── Purification (PNJType.Purification) — spec §2.5 ────────
    // Coût de rachat par palier : pas sur ce PNJData, lu directement sur le PrestigeAuraData
    // PARTAGÉ du joueur (Player.prestigeAuraData.auraTiers[rank].purificationAerisCost/
    // purificationResource/purificationResourceQty) — voir PNJ.TryPurifyAura.
    [Tooltip("Texte d'accueil différent selon le palier Aura ACTUEL du joueur — index = auraRank " +
             "(0=Normal, 1=Terni, ... 5=Déchu). Une entrée vide/absente retombe sur defaultDialogue " +
             "(valeur gelée telle qu'elle était avant de masquer ce champ pour Purification, voir " +
             "ci-dessus). Chaque dialogue garde la même option \"Purifier mon Aura\" " +
             "(DialogueAction.PurifyAura), seul le texte de vœux change. Florian, 2026-09-29 : " +
             "\"il faudrait un dialogue différent en fonction du rang de l'aura\".")]
    [ShowIf(nameof(pnjType), PNJType.Purification, Header = "Purification (PNJType.Purification)")]
    public List<DialogueData> purificationDialogueByAuraRank = new List<DialogueData>();

    // ── Téléportation (PNJType.Teleporter) ──────────────────────
    // Rétabli 2026-10-04 — retiré puis remis dans la même session : "ne sert à rien" était faux,
    // c'est la liste de destinations proposées en dialogue pour s'y téléporter (Florian). Les
    // anciens départureIntervalMin/Max (délai de départ façon bateau) ne sont PAS rétablis — ne
    // collent plus au concept téléportation instantanée ; à rajouter si un futur besoin de
    // cooldown/délai apparaît.
    [Tooltip("Noms des destinations proposées — HarborUI (TODO Phase 8) les affichera en boutons\n" +
             "cliquables depuis le dialogue.")]
    [ShowIf(nameof(pnjType), PNJType.Teleporter, Header = "Téléportation (PNJType.Teleporter)")]
    public List<string> availableDestinations = new List<string>();

    // ── Quête ─────────────────────────────────────────────────
    [ShowIf(nameof(pnjType), PNJType.Quest, Header = "Quête (PNJType.Quest)")]
    public List<QuestData> availableQuests = new List<QuestData>();

    // ── Boutique — PNJType.Merchant (modèle composable §13.2) ─────────────
    // Une seule liste — ShopEntry.item est un ScriptableObject générique (WeaponData,
    // ResourceData, SkillData, PermanentSkillData, PassiveSkillData...), ShopUI dispatch
    // déjà par type. Un PNJ "coach" qui vend des skills utilise cette même liste, glisser
    // les SkillData dedans — pas de liste séparée.
    [ShowIf(nameof(pnjType), PNJType.Merchant, Header = "Boutique (PNJType.Merchant)")]
    public List<ShopEntry> shopItems = new List<ShopEntry>();

    // ── Antiquaire (shopSpecialty.Antiquarian) ─────────────────
    [ShowIf(nameof(shopSpecialty), ShopSpecialty.Antiquarian,
        AndField = nameof(pnjType), AndValue = PNJType.Merchant,
        Header = "Antiquaire (shopSpecialty.Antiquarian)")]
    public bool canIdentifyRunes = false;
    [ShowIf(nameof(shopSpecialty), ShopSpecialty.Antiquarian,
        AndField = nameof(pnjType), AndValue = PNJType.Merchant)]
    public bool canInsertRunes   = false;

    // ── PNJ Faction (obsolète, gardé pour compat assets existants) ──
#pragma warning disable CS0618
    [ShowIf(nameof(pnjType), PNJType.FactionNPC, Header = "Faction (PNJType.FactionNPC)")]
    public FactionType  faction = FactionType.None;
    [ShowIf(nameof(pnjType), PNJType.FactionNPC)]
    public DialogueData hostileDialogue;
#pragma warning restore CS0618

    // ── Mort & Respawn ────────────────────────────────────────
    [Header("Mort & Respawn")]
    [Tooltip("false = invulnérable (civils, décoratifs) ET jamais ciblé par un mob (voir\n" +
             "Mob.RefreshEnemyList()).\ntrue = peut être attaqué, mourir et respawner.")]
    public bool  canDie       = false;
    [Tooltip("Délai de respawn en secondes après mort. 0 = pas de respawn.")]
    [ShowIf(nameof(canDie), true)]
    public float respawnDelay = 60f;

    // Même pipeline que MobData — CombatSystem lit sur Entity. Masqué si canDie = false — un
    // PNJ invulnérable n'est plus jamais ciblé par un mob (voir Mob.RefreshEnemyList(), demande
    // Florian 2026-10-04), ces stats n'ont donc plus aucun effet pour lui.
    [Tooltip("Visible uniquement si canDie = true — un PNJ invulnérable n'est jamais ciblé par un mob, ces valeurs n'ont alors aucun effet.")]
    [ShowIf(nameof(canDie), true, Header = "Stats défensives (canDie)")]
    public float meleeDefense  = 10f;
    [ShowIf(nameof(canDie), true)]
    public float rangedDefense = 8f;
    [ShowIf(nameof(canDie), true)]
    public float magicDefense  = 5f;
    [ShowIf(nameof(canDie), true)]
    public float precision     = 10f;
    [ShowIf(nameof(canDie), true)]
    public float dodge         = 5f;

    // ── Combat — PNJ canFight ─────────────────────────────────
    // Activé dès que canFight = true, quel que soit le pnjType.
    // Guard, marchand itinérant, PNJ faction... tous passent par
    // le même CombatAIController.Tick() via SkillSystem.Execute().
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

    [Tooltip("Regen Mana/s hors combat. 0 = pas de regen. Ajouté 2026-10-04 (demande Florian) —\n" +
             "branché sur l'infra Entity.SetRegenMana déjà existante, jusqu'ici jamais utilisée\n" +
             "côté PNJ.")]
    [ShowIf(nameof(canFight), true)]
    public float baseRegenMana = 0f;

    [Tooltip("Dégâts de base de l'attaque — utilisés par CombatSystem.CalculateMobDamage() " +
             "via Entity.AttackDamageMin/Max.\nIgnoré si basicAttackSkill calcule ses propres dégâts via ratios.")]
    [ShowIf(nameof(canFight), true)]
    public float attackDamage = 15f;

    [Tooltip("Chance de critique [0..1]. Poussé sur Entity via SetCritChance().")]
    [ShowIf(nameof(canFight), true)]
    public float critChance     = 0.03f;
    [Tooltip("Multiplicateur dégâts critique. Base 1.5f.")]
    [ShowIf(nameof(canFight), true)]
    public float critMultiplier = 1.5f;

    [Tooltip("Catégorie d'arme — détermine quelle défense de la cible s'applique. GDD §3.1.")]
    [ShowIf(nameof(canFight), true)]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;

    [Tooltip("Skill d'attaque de base — son propre champ cooldown gère le délai entre deux\n" +
             "attaques.\nDoit avoir targetType = Target et effectType = Damage.\nObligatoire si canFight.")]
    [ShowIf(nameof(canFight), true)]
    public SkillData basicAttackSkill;

    [Tooltip("Skills secondaires (prioritaires sur l'attaque de base).\nMême logique que MobData.skills.")]
    [ShowIf(nameof(canFight), true)]
    public List<SkillData> skills = new List<SkillData>();

    // ── IA de combat — non listés explicitement par Florian dans la réorganisation 2026-10-04,
    // laissés ici sous canFight (position inchangée, juste déplacés après skills) ──
    [Tooltip("Rayon de détection des ennemis (Mobs). Équivalent de detectionRange sur MobData.")]
    [ShowIf(nameof(canFight), true)]
    public float aggroRadius = 15f;

    [Tooltip("Distance max depuis le spawn avant de lâcher la cible et rentrer. 0 = illimité.")]
    [ShowIf(nameof(canFight), true)]
    public float leashRadius = 10f;

    [Tooltip("Vitesse de déplacement en mode combat. 0 = utilise baseMoveSpeed.")]
    [ShowIf(nameof(canFight), true)]
    public float combatMoveSpeed = 0f;

    [Tooltip("Rayon de patrouille autour du spawn quand aucune cible n'est engagée. 0 = reste\n" +
             "immobile au spawn (comportement actuel). Équivalent de MobData.patrolRadius.")]
    [ShowIf(nameof(canFight), true)]
    public float patrolRadius = 0f;

    [Tooltip("Passive : n'engage que s'il est attaqué directement (TakeDamage). Aggressive :\n" +
             "engage dès qu'un ennemi entre dans aggroRadius — comportement actuel, DÉFAUT à ne\n" +
             "jamais changer pour ne pas casser les PNJData existants. Boss non utilisé côté\n" +
             "PNJ (réutilise MobAIType tel quel — même enum que MobData, aucun risque ordinal).")]
    [ShowIf(nameof(canFight), true)]
    public MobAIType aiType = MobAIType.Aggressive;

    // ── Effets On-Hit — PNJ canFight uniquement. Avant le 2026-10-04 ces effets reçus
    // restaient actifs même hors canFight (un garde passif pouvait avoir Thorns en encaissant
    // sans riposter) — changé sur demande explicite de Florian : gater comme le reste du bloc
    // combat, un PNJ non-combattant n'a plus de réaction/résistance possible.
    [Header("Effets On-Hit reçus")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // ── Résistances aux debuffs — PNJ canFight uniquement, même changement que
    // onHitReceivedEffects ci-dessus (demande Florian 2026-10-04) ──
    [Header("Résistances aux debuffs (innées, indépendantes de tout équipement)")]
    [Tooltip("Même mécanisme que la résistance équipement du joueur (DebuffResistanceEntry) — " +
             "un PNJ n'a pas d'équipement, ce champ le remplace. resistChance = 1 sur un " +
             "DebuffType = immunité totale.")]
    [ShowIf(nameof(canFight), true)]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

    // ── Animation ──────────────────────────────────────────────
    // Consommées par CombatEntityAnimatorController — même mécanisme que MobData (voir son
    // commentaire). baseMoveSpeed déplacé ici depuis son ancien bloc "Déplacement" séparé
    // (demande Florian 2026-10-04 — regroupé avec le reste de l'animation/déplacement).
    [Header("Animation")]
    [Tooltip("Catégorie de rig — pilote l'auto-fill des clips ci-dessous depuis " +
             "MobAnimationPresets (voir OnValidate) quand un champ est vide. N'écrase jamais " +
             "un clip déjà assigné à la main.")]
    public AnimationType animationType = AnimationType.Humanoid;
    [Tooltip("Vitesse de déplacement de base (patrouille, déambulation).")]
    public float baseMoveSpeed = 2f;
    [Tooltip("Anim jouée à l'arrêt hors combat. Toujours affiché (même hors canFight — un PNJ\n" +
             "statique respire quand même).")]
    public AnimationClip idleClip;
    [Tooltip("Variantes supplémentaires d'idleClip — une est tirée au hasard à chaque retour au\n" +
             "repos (évite de rejouer toujours la même pose). Optionnel : vide = toujours idleClip,\n" +
             "comportement inchangé.")]
    public List<AnimationClip> idleClipVariants = new List<AnimationClip>();
    [Tooltip("Anim de déplacement en Patrol/déambulation. Toujours affiché (PAS limité à canFight\n" +
             "depuis l'ajout de PNJ.isPatrolRoute — un PNJ ambulant non-combattant a aussi un\n" +
             "NavMeshAgent et en a besoin ; ShowIf ne sait pas exprimer canFight OU isPatrolRoute\n" +
             "sur deux champs différents, donc traité comme idleClip/deathClip ci-dessous).")]
    public AnimationClip walkClip;
    [Tooltip("Anim de déplacement en Engage (poursuite/combat rapproché) — distincte de Walk\n" +
             "même à vitesse égale, voir IsChasing sur CombatEntityAnimatorController. Nécessite\n" +
             "canFight (combat uniquement, pas de \"chase\" en patrol route).")]
    [ShowIf(nameof(canFight), true)]
    public AnimationClip chaseClip;
    [Tooltip("Anim de mort — jouée une fois via PlayDeath() avant le début de la séquence de\n" +
             "respawn (masquage renderers/collider). Optionnel : si null, aucune anim n'est jouée\n" +
             "et aucun délai n'est ajouté — voir PNJ.RespawnCoroutine(). Toujours affiché (même\n" +
             "hors canFight — un PNJ non-combattant peut quand même mourir, voir canDie).")]
    public AnimationClip deathClip;

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

        // AnimationType.None = PNJ fixe qui ne doit recevoir AUCUN clip, point — zéro auto-fill
        // (même règle que MobData, voir son OnValidate). Sinon (types normaux) : idle + walk
        // toujours auto-remplis si possible (walk élargi du canFight-only au toujours depuis le
        // 2026-10-04 — un PNJ ambulant non-combattant, PNJ.isPatrolRoute, a aussi besoin d'un
        // walkClip ; PNJData ne connaît pas ce champ scène-only, donc pas moyen de conditionner
        // l'auto-fill dessus précisément, autant le traiter comme idle) ; death réservé à
        // canDie (inutile sinon) ; chase réservé à canFight (un PNJ sans CombatAIController ne
        // joue jamais d'anim "chase", même en patrol route).
        if (animationType != AnimationType.None)
        {
            var preset = MobAnimationPresets.FindAsset()?.GetPreset(animationType);
            if (preset != null)
            {
                if (idleClip == null) idleClip = preset.idleClip;
                if ((idleClipVariants == null || idleClipVariants.Count == 0) && preset.idleClipVariants.Count > 0)
                    idleClipVariants = new List<AnimationClip>(preset.idleClipVariants);
                if (canDie && deathClip == null) deathClip = preset.deathClip;
                if (walkClip == null) walkClip = preset.walkClip;
                if (canFight)
                {
                    if (chaseClip == null) chaseClip = preset.chaseClip;
                }
            }
        }

        // Sans ces 3 clips, CombatEntityAnimatorController retombe sur les placeholders du
        // Controller partagé — une anim faite pour un AUTRE rig (souvent T-pose/désarticulé).
        if (canFight && (idleClip == null || walkClip == null || chaseClip == null))
            Debug.LogWarning($"[PNJData] {pnjName} : idleClip/walkClip/chaseClip incomplet(s) — " +
                              "ce PNJ affichera l'anim placeholder du Controller partagé (faite " +
                              "pour un autre rig) tant que les 3 clips ne sont pas assignés.");
    }
#endif
}

// ── Types de PNJ — GDD v3.5 §3.4, modèle composable "Boutique + onglets" §13.2 ──
//
// Depuis §13.2 : la plupart des PNJ marchands suivent le même modèle — une Boutique
// (achat/vente, ShopUI) + un ou plusieurs onglets complémentaires propres à leur métier.
// Seuls Guard/Decorative/Quest/Teleporter/Purification restent dialogue-only, sans fenêtre.
//
// PNJType.Merchant couvre TOUTE boutique, quelle que soit sa spécialité — voir
// ShopSpecialty/shopSpecialty plus bas. Avant le 2026-10-04, chaque spécialité (Forge/
// Antiquarian/Cordonnier/Cook/Tinkerer/Jeweler/Hatter/CraftStation) était son propre
// PNJType ; elles ne faisaient QUE sélectionner un bundle d'onglets (PNJTypeExtensions.
// GetTabs()), jamais un rôle d'interaction différent — fusionnées en un seul Merchant +
// un champ shopSpecialty (demande Florian). Les 4 .asset existants qui utilisaient ces
// ordinaux (Forgeron/Bijoutier/Cordonnier/Cook) ont été migrés manuellement vers
// pnjType: 0 (Merchant) + shopSpecialty correspondant.
//
// Ordinaux figés : Unity sérialise un enum par sa position int, pas son nom (voir
// [[project_aethertree_passive_system_unification]] pour le précédent qui a motivé cette
// règle). CraftMaster/Mayor/FactionNPC/Rarity/Forge/Antiquarian/Cordonnier/Cook/Tinkerer/
// Jeweler/Hatter/CraftStation sont tous retirés du design — ordinaux jamais réutilisés,
// jamais réordonnés, pour ne décaler aucun des types qui suivent dans un .asset existant.
public enum PNJType
{
    Merchant      = 0,  // Boutique — toute spécialité, voir shopSpecialty/PNJTypeExtensions.GetTabs()

    // 1 retiré (2026-10-04) — Forge (ex-Blacksmith), fusionné dans Merchant + shopSpecialty.Forge.
    // Ordinal 1 jamais réutilisé.

    [System.Obsolete("Retiré du design (2026) — Pari de rareté est un onglet de shopSpecialty.Forge, " +
                      "plus un PNJ séparé. Réassigner les PNJData existants (ex: PNJ_rareté.asset) " +
                      "vers Merchant+Forge. Ordinal gardé pour ne pas décaler les types qui suivent.")]
    Rarity        = 2,  // RETIRÉ, placeholder — voir shopSpecialty.Forge

    // 3 retiré (2026-10-04) — Antiquarian, fusionné dans Merchant + shopSpecialty.Antiquarian.
    // Ordinal 3 jamais réutilisé.

    // 4 retiré (2026-10-04) — Cordonnier (ex-FusionNPC), fusionné dans Merchant +
    // shopSpecialty.Cordonnier. Ordinal 4 jamais réutilisé.

    // 5 retiré (2026-09-07) — CraftMaster, déblocage métiers jamais implémenté. Zéro case dans
    // PNJ.cs (contrairement à Rarity/FactionNPC, toujours wirés eux) — vérifié avant
    // suppression. Ordinal 5 jamais réutilisé.

    Quest         = 6,  // Donneur de quêtes — conditions + récompenses

    // 7 retiré (2026-10-04) — Mayor, création de guilde jamais implémentée (TryCreateGuild
    // n'était qu'un TODO GuildSystem). Florian : "il ne sert à rien actuellement, on le refera
    // si jamais" — zéro .asset avec pnjType: 7, zéro champ guild* non-défaut — vérifié avant
    // suppression. Ordinal 7 jamais réutilisé.

    [System.Obsolete("Retiré du design (2026) — quêtes de faction pas prioritaires actuellement. " +
                      "Ordinal gardé pour ne pas décaler Teleporter/Guard/Decorative déjà sérialisés.")]
    FactionNPC    = 8,  // RETIRÉ, placeholder

    // Renommé HarborMaster → Teleporter le 2026-10-04 (même ordinal 9, aucune migration
    // d'asset) — Florian : "c'est pas forcément un bateau, c'est un PNJ qui propose des
    // destinations (map) à qui se téléporter".
    Teleporter    = 9,  // Téléportation — choix de destination en dialogue (dialogue seul)
    Guard         = 10, // Dialogue neutre + IA combat mobs proches (dialogue seul)
    Decorative    = 11, // Ambiance, lore, rumeurs — pas de service (dialogue seul)

    // 12-16 retirés (2026-10-04) — Cook/Tinkerer/Jeweler/Hatter/CraftStation, tous fusionnés
    // dans Merchant + shopSpecialty correspondant (même raison que Forge/Antiquarian/
    // Cordonnier ci-dessus). Ordinaux 12-16 jamais réutilisés.

    Purification  = 17, // Dialogue seul (pas de Boutique) — rachète les paliers d'Aura négatifs
                         // un par un, voir spec 2026-09-29-prestige-aura-design.md §2.5 et
                         // Player.prestigeAuraData.auraTiers/PNJ.TryPurifyAura.
}

// ── Spécialité boutique — PNJType.Merchant uniquement, pilote PNJTypeExtensions.GetTabs() ──
// Remplace l'ancien éclatement en PNJType séparés (voir commentaire de PNJType ci-dessus).
// None = boutique seule, comportement de l'ancien PNJType.Merchant pré-fusion.
public enum ShopSpecialty
{
    None          = 0,  // Boutique seule — ex-PNJType.Merchant
    Forge         = 1,  // Craft Équipement + Upgrade (+0→+10) + Pari (rareté) — ex-PNJType.Forge
    Antiquarian   = 2,  // Identification (runes) — ex-PNJType.Antiquarian
    Cordonnier    = 3,  // Fusion Gants/Bottes (S0→S6) — ex-PNJType.Cordonnier
    Cook          = 4,  // Cuisiner (nourriture + potions, partagé Alchimie) — ex-PNJType.Cook
    Tinkerer      = 5,  // Bricoler (fusion ressources + déco housing) — ex-PNJType.Tinkerer
    Jeweler       = 6,  // Gemmes (pose sur bijoux) — ex-PNJType.Jeweler
    Hatter        = 7,  // Craft de casques — ex-PNJType.Hatter
    CraftStation  = 8,  // Craft (ressources intermédiaires, tous domaines) — ex-PNJType.CraftStation
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

    // Ajouté 2026-10-04 (Florian) — un Merchant peut aussi donner des quêtes. PAS retourné par
    // GetTabs() ci-dessous (qui ne regarde que ShopSpecialty) — ajouté conditionnellement par
    // PNJWindowUI selon pnjData.availableQuests, voir PNJWindowUI.GetTabsFor().
    Quest               = 13,
}

// ── PNJ ayant une Boutique (ShopUI) — modèle composable §13.2 ────────────────
public static class PNJTypeExtensions
{
    public static bool HasShop(this PNJType type) => type == PNJType.Merchant;

    /// <summary>Liste ordonnée des onglets de la fenêtre PNJ partagée pour cette spécialité —
    /// Boutique toujours en premier. None = boutique seule (ex-PNJType.Merchant). Appelé
    /// uniquement pour un PNJData dont pnjType == Merchant (voir shopSpecialty).</summary>
    public static List<PNJTabID> GetTabs(this ShopSpecialty specialty) => specialty switch
    {
        ShopSpecialty.Forge        => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftEquipement, PNJTabID.Upgrade, PNJTabID.Pari },
        ShopSpecialty.Cook         => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Cuisiner },
        ShopSpecialty.Cordonnier   => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftGantsBottes, PNJTabID.Fusion },
        ShopSpecialty.Tinkerer     => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Bricoler },
        ShopSpecialty.Jeweler      => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftBijoux, PNJTabID.Gemmes },
        ShopSpecialty.Hatter       => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftCasque },
        ShopSpecialty.Antiquarian  => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.Identification },
        ShopSpecialty.CraftStation => new List<PNJTabID> { PNJTabID.Boutique, PNJTabID.CraftIntermediaire },
        _                          => new List<PNJTabID> { PNJTabID.Boutique }, // None
    };
}

// ── Entrée de shop ────────────────────────────────────────────
[System.Serializable]
public class ShopEntry
{
    [Tooltip("Glisse le SO ici (WeaponData, ArmorData, ConsumableData, ResourceData, SkillData...)")]
    public ScriptableObject item;
    public int  aerisCost;
    [Tooltip("Rang de Prestige minimum (0 = toujours visible)")]
    public int  requiredPrestigeRank = 0;
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
