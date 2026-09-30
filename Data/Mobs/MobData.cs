using UnityEngine;
using System.Collections.Generic;

// =============================================================
// MOBDATA — ScriptableObject template de mob
// Path : Assets/Scripts/Data/Mobs/MobData.cs
// AetherTree GDD v3.5 — §3.3 / §3.3.1
//
// Principe de scaling :
//   Les stats de base (lv1) + la valeur de croissance par niveau sont
//   définies directement sur ce SO — statFinale = statBase + statParNiveau ×
//   (level-1). Plus de multiplicateur par MobType (retiré 2026-09-24) : un
//   boss a ses propres stats de base tapées à la main, pour un équilibrage
//   direct sans facteur caché (voir MobStatCalculator.cs).
//
//   Les résistances élémentaires sont un profil fixe — elles ne scalent
//   pas avec le niveau.
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
    public MobType   mobType = MobType.WorldMob;
    public MobAIType aiType  = MobAIType.Passive;

    [Tooltip("Rôle en salle de donjon (Couloir) — Normal = respawn selon Mob.respawnEnabled de " +
             "l'instance, comme n'importe quel mob. Objective/Special/Boss ne respawnent JAMAIS, " +
             "même si Mob.respawnEnabled reste coché (garde en code, voir Mob.ShouldRespawn()) — " +
             "un Objective qui respawn reverrouillerait un portail déjà ouvert " +
             "(Portal.requiredMobs).")]
    [ShowIf(nameof(mobType), MobType.DungeonMob)]
    public DungeonRole dungeonRole = DungeonRole.Normal;

    [Tooltip("Rôle dans l'événement Invasion — Normal = mob de vague/renfort (trash), Boss = LE " +
             "boss de l'invasion (voir InvasionVariant.boss, Data/Content/InvasionData.cs). Même " +
             "patron que dungeonRole ci-dessus (un seul MobType, le rôle distingue trash/boss).")]
    [ShowIf(nameof(mobType), MobType.EventInvasionMob)]
    public InvasionRole invasionRole = InvasionRole.Normal;

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
    [Tooltip("Catégorie d'arme — détermine quelle défense du joueur s'applique. GDD §3.1.")]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;

    // ── Résistances élémentaires — profil fixe ────────────────
    // Range étendu au négatif (2026-09-06, demande Florian) — une valeur négative = faiblesse/
    // vulnérabilité DÉLIBÉRÉE (ex: un boss Feu qui craint l'Eau à -20%), amplifie les dégâts de
    // cet élément au lieu de les réduire. Le plafond haut (jamais >100% dans le calcul final)
    // reste géré côté CombatSystem/ComputeDotDps — pas de plafond bas ici.
    [Header("Résistances élémentaires [-1;1] — fixes, ne scalent pas (négatif = vulnérabilité)")]
    [Range(-1f, 1f)] public float fireResist      = 0f;
    [Range(-1f, 1f)] public float waterResist     = 0f;
    [Range(-1f, 1f)] public float lightningResist = 0f;
    [Range(-1f, 1f)] public float earthResist     = 0f;
    [Range(-1f, 1f)] public float natureResist    = 0f;
    [Range(-1f, 1f)] public float darknessResist  = 0f;
    [Range(-1f, 1f)] public float lightResist     = 0f;

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

    // ── Animations locomotion ──────────────────────────────────
    // Consommées par CombatEntityAnimatorController — swap par-dessus les 3 placeholders
    // (PLACEHOLDER_Idle/Walk/Chase) du Animator Controller PARTAGÉ entre tous les Mob/PNJ (voir
    // docs/guide-utilisation/creation-mob.md § Animator). Chaque mob garde ses propres clips,
    // liés à SON rig — seul le graphe/Controller est mutualisé, jamais les clips eux-mêmes.
    [Header("Animations locomotion")]
    [Tooltip("Anim jouée à l'arrêt hors combat.")]
    public AnimationClip idleClip;
    [Tooltip("Anim de déplacement en Patrol (déambulation).")]
    public AnimationClip walkClip;
    [Tooltip("Anim de déplacement en Engage (poursuite/combat rapproché) — distincte de Walk\n" +
             "même à vitesse égale, voir IsChasing sur CombatEntityAnimatorController.")]
    public AnimationClip chaseClip;
    [Tooltip("Anim de mort — jouée une fois via PlayDeath() avant Destroy(gameObject). Optionnel :\n" +
             "si null, aucune anim n'est jouée (comportement actuel inchangé), voir Mob.Die().")]
    public AnimationClip deathClip;

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

    // ── Effets On-Hit ─────────────────────────────────────────
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // ── Résistances aux debuffs ────────────────────────────────
    [Header("Résistances aux debuffs (innées, indépendantes de tout équipement)")]
    [Tooltip("Résistance appliquée à tout debuff dont l'asset a DebuffData.isHardCC coché " +
             "(Stun/Fear/Sleep/Shocked/Freeze/Root/Displacement) — pensé pour les boss à grande " +
             "échelle (World Boss/Invasion) : un CC dur ne doit JAMAIS dépendre d'un simple % " +
             "face à des dizaines/centaines d'attaquants simultanés (voir StatusEffectSystem." +
             "TryApplyDebuff). N'est utilisée QUE s'il n'existe aucun override précis pour ce " +
             "DebuffType dans debuffResistances ci-dessous — celui-ci reste prioritaire.")]
    [Range(0f, 1f)]
    public float hardCCResistance = 0f;

    [Tooltip("Même principe que hardCCResistance, mais pour les debuffs isHardCC DÉCOCHÉ " +
             "(Stats/Dot/Slow/HpDrain/ManaDrain/Silence/Prey/Dispel...) — affaiblissent sans " +
             "bloquer d'action, un % de résistance reste acceptable même à grande échelle.")]
    [Range(0f, 1f)]
    public float softDebuffResistance = 0f;

    [Tooltip("Même mécanisme que la résistance équipement du joueur (DebuffResistanceEntry) — " +
             "un Mob n'a pas d'équipement, ce champ le remplace. resistChance = 1 sur un " +
             "DebuffType = immunité totale (ex: boss raciné, immunisé au CC dur — voir " +
             "DebuffType.Displacement pour l'immunité au Pull/Push/SwapPosition). Écrase " +
             "hardCCResistance/softDebuffResistance ci-dessus pour le(s) type(s) listé(s) ici.")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

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

    /// <summary>True si ce mob est un boss — WorldBoss/EventGiantBoss (MobType), ou un boss de
    /// donjon/invasion exprimé via MobType.DungeonMob + DungeonRole.Boss / MobType.EventInvasionMob
    /// + InvasionRole.Boss (voir plus bas — BossDungeon et BossInvasion retirés de MobType pour ne
    /// plus dupliquer cette information à deux endroits, même patron pour les deux).</summary>
    public bool IsBoss()
        => mobType == MobType.WorldBoss
        || mobType == MobType.EventGiantBoss
        || (mobType == MobType.DungeonMob && dungeonRole == DungeonRole.Boss)
        || (mobType == MobType.EventInvasionMob && invasionRole == InvasionRole.Boss);

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

        // Sans ces 3 clips, CombatEntityAnimatorController retombe sur les placeholders du
        // Controller partagé — une anim faite pour un AUTRE rig (souvent T-pose/désarticulé).
        if (idleClip == null || walkClip == null || chaseClip == null)
            Debug.LogWarning($"[MobData] {mobName} : idleClip/walkClip/chaseClip incomplet(s) — " +
                              "ce mob affichera l'anim placeholder du Controller partagé (faite " +
                              "pour un autre rig) tant que les 3 clips ne sont pas assignés.");
    }
#endif
}

// ── Type de mob ───────────────────────────────────────────────
// Elite/BossRaid/Nocturnal/Capturable retirés (2026-09-24, demande Florian) — Elite jamais
// utilisé en contenu, BossRaid = raid multijoueur parqué hors scope démo, Nocturnal/Capturable
// dupliquaient les bools isNocturnal/isCapturable déjà présents sur MobData (indépendants de
// mobType, utilisables sur N'IMPORTE QUEL type). Valeurs ordinales des membres CONSERVÉS
// inchangées (2/3) — au moins un asset réel (mob_test, boss donjon test) a déjà mobType=3
// sérialisé, une renumérotation l'aurait silencieusement retypé.
// Plus de multiplicateur par valeur (retiré 2026-09-24, voir MobStatCalculator.cs) — chaque
// valeur est une pure catégorie, les stats de chaque MobData sont tapées directement dessus.
// DungeonMob ajouté (2026-09-28) sur l'ordinal 1, libéré par l'ancien Elite (jamais sérialisé
// sur aucun asset réel, confirmé par grep) — sépare TOUS les mobs de donjon (normaux, objectifs,
// spéciaux ET boss — voir DungeonRole) des mobs de monde ouvert (WorldMob). BossDungeon RETIRÉ le
// même jour : un boss de donjon s'exprime maintenant via MobType.DungeonMob + DungeonRole.Boss,
// pas par un MobType séparé — évite de dupliquer "c'est un boss de donjon" à deux endroits.
// MIGRATION : tout asset qui avait mobType = BossDungeon (ordinal 3) doit être repassé à la main
// sur MobType = DungeonMob + DungeonRole = Boss, sinon son mobType affiche une valeur vide dans
// l'Inspector (l'ordinal 3 existe toujours dans le fichier, juste sans nom d'enum dessus).
// EventInvasionMob (2026-09-30, demande Florian) couvre TOUT mob de l'événement Invasion (trash
// ET boss) — même patron que DungeonMob/DungeonRole : un seul MobType, le rôle (InvasionRole)
// distingue trash/boss. BossInvasion (ordinal 5, jamais sérialisé sur aucun asset réel — feature
// Invasion tout juste créée ce même jour) est retiré, PAS renuméroté pour combler le trou : ordinal
// 5 reste vacant, même discipline que l'ordinal 3 (ex-BossDungeon) plus bas.
//
// Renommage global (2026-09-30, demande Florian) — deux catégories nommées explicitement :
// "World" = mob PERMANENT (monde ouvert), "Event" = mob d'un WorldEventScheduler (temporaire,
// spawné puis résolu). Normal→WorldMob, BossMap→WorldBoss, MobDungeon→DungeonMob (PAS "World" —
// un mob de donjon vit en instance, pas dans le monde ouvert, catégorie à part), BossWorld→
// EventGiantBoss (PAS "EventWorldBoss" — collision de nom avec le nouveau WorldBoss (BossMap)
// alors que ce sont deux mobs différents ; "GiantBoss" colle au DisplayName déjà utilisé partout,
// "Boss Géant"), MobInvasion→EventInvasionMob. Renommage de noms d'enum uniquement, ordinaux
// (0/1/2/4/6) tous inchangés — sans risque pour les assets déjà sérialisés (Unity stocke
// l'ordinal, jamais le nom).
public enum MobType
{
    WorldMob         = 0,  // Mob standard, monde ouvert, permanent
    DungeonMob       = 1,  // Tout mob de donjon (normal/objectif/spécial/boss) — voir DungeonRole
    WorldBoss        = 2,  // ex-BossZone/BossMap — erre en zone ouverte en permanence (Palier 1, mini-boss)
    EventGiantBoss   = 4,  // Boss Géant (WorldEventScheduler, spawn sur un palier random)
    EventInvasionMob = 6,  // Tout mob de l'événement Invasion (vague/renfort/boss) — voir InvasionRole
}

// ── Rôle en salle de donjon (Couloir) ────────────────────────────
// Boss = LE boss de donjon (remplace l'ancien MobType.BossDungeon, retiré) — ne respawn jamais,
// comme Objective/Special. Ajouté en fin d'enum (ordinal 3) pour ne jamais renuméroter Normal/
// Objective/Special déjà potentiellement sérialisés. Voir MobData.dungeonRole.
public enum DungeonRole
{
    Normal    = 0, // Respawn selon Mob.respawnEnabled de l'instance placée, comme n'importe quel mob.
    Objective = 1, // Requis par un Portal.requiredMobs — ne respawn jamais.
    Special   = 2, // Mob unique/scénarisé — ne respawn jamais, sans être un objectif de portail.
    Boss      = 3, // LE boss de cette salle — ne respawn jamais. Voir Mob.isDungeonBoss (instance
                   // en scène) pour le déclenchement réel d'InstanceSession.OnBossKilled().
}

// ── Rôle dans l'événement Invasion ───────────────────────────────
// Même patron que DungeonRole ci-dessus — voir MobData.invasionRole.
public enum InvasionRole
{
    Normal = 0, // Mob de vague/renfort — le trash de l'invasion.
    Boss   = 1, // LE boss de cette invasion — voir InvasionVariant.boss.
}

// ── IA du mob ─────────────────────────────────────────────────
public enum MobAIType
{
    Passive    = 0,   // N'attaque que si agressé
    Aggressive = 1,   // Attaque les joueurs à portée
    Boss       = 2,   // IA scriptée — phases de combat (§3.3)
}

// ── Type de pet potentiel ─────────────────────────────────────
public enum PetType { Tank = 0, Damage = 1, Support = 2, Utility = 3, Hybrid = 4 }
