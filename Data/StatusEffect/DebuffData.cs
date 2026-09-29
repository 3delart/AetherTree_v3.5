using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DebuffData — ScriptableObject template de debuff
// Path : Assets/Scripts/Data/StatusEffect/DebuffData.cs
// AetherTree GDD v3.5 — §3.1.1.1
//
// v3.5 — DebuffStatType remplacé par StatModifierType (fusion avec BuffStatType)
//
// Assets > Create > AetherTree > StatusEffects > DebuffData
// =============================================================

[CreateAssetMenu(fileName = "dbf_", menuName = "AetherTree/StatusEffects/DebuffData")]
public class DebuffData : StatusEffectData
{
    [Header("Type de debuff")]
    public DebuffType debuffType;

    [Tooltip("Coché : ce debuff bloque une action fondamentale (mouvement ET/OU compétences) — " +
             "Stun/Fear/Sleep/Shocked/Freeze/Root/Displacement. Décoché : affaiblit sans bloquer " +
             "(Stats/Dot/Slow/HpDrain/ManaDrain/Silence/Prey/Dispel...). Sert de fallback de " +
             "résistance sur les mobs à grande échelle (World Boss/Invasion) quand aucun override " +
             "précis n'existe pour ce DebuffType — voir MobData.hardCCResistance/" +
             "softDebuffResistance et StatusEffectSystem.TryApplyDebuff. Classe le debuff À SA " +
             "CRÉATION, jamais besoin de toucher du code pour un futur ajout.")]
    public bool isHardCC = false;

    // ── Dégâts sur la durée (Dot — Burn/Poison/Bleed gardés ici en ShowIf UNIQUEMENT
    // pour que les assets déjà créés avec ces types obsolètes gardent leurs champs
    // visibles/éditables ; ne plus jamais choisir ces 3 types sur un nouvel asset) ──
    // Formule complète par tick (voir DebuffInstance.Tick) — 3 termes :
    //   baseTerme   = target.MaxHP × (baseDamagePercent / 100) — TOUJOURS appliqué, peu importe
    //                 l'investissement élémentaire de la source (socle — un Dot octroyé par un
    //                 équipement/proc ne doit jamais faire 0 dégât juste parce que le porteur ne
    //                 joue pas cet élément)
    //   rangTerme   = target.MaxHP × (rang élémentaire de la source × rankDamagePercent / 100)
    //   pointsTerme = points élémentaires EFFECTIFS (brut + bonus de rang, même valeur que
    //                 CombatSystem/l'UI) de la source × elementalPointsMultiplier (flat)
    //   dps = (baseTerme + rangTerme + pointsTerme) × (1 - résistance élémentaire de la cible)
    // rang/points lus sur la SOURCE (celui qui a appliqué le debuff), résistance sur la CIBLE —
    // même schéma que CombatSystem. rangTerme/pointsTerme sont un BONUS qui vient s'ajouter au
    // socle si la source investit dans damageElement — décision explicite Florian (2026-09-06).
    // IMPORTANT — le socle N'EST PAS réduit par la résistance élémentaire de la cible
    // (dégâts "vrais"), seul le bonus rang+points l'est — voir ComputeDotDps. Valeurs par
    // défaut (0.01/0.2/0.1) calibrées après comparaison chiffrée sur 3 profils de mob
    // (loup 50hp/0%, golem 2000hp/40%, boss 20000hp/80%) et 2 profils joueur (non-spécialisé
    // vs spécialisé 450 points/rang4) — décision explicite Florian (2026-09-06).
    // Toujours en % du MaxHP cible pour base/rang (pas de mode Flat) — un DoT flat ne scale pas
    // avec le contenu (mob lvl30 vs lvl85), voir discussion — le % est la seule forme qui a du
    // sens ici.
#pragma warning disable CS0618 // Burn/Poison/Bleed obsolètes — gardés pour compat assets existants
    [Tooltip("% du Max HP de la cible par seconde, TOUJOURS appliqué (socle), NON réduit par la\n" +
             "résistance élémentaire de la cible (dégâts vrais) — indépendant de l'investissement\n" +
             "élémentaire de la source. Ex: 0.01 = 0.01% MaxHP/s minimum garanti.")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot,
        Header = "Dégâts sur la durée (Dot)")]
    public float baseDamagePercent = 0.01f;

    [Tooltip("% du Max HP de la cible ajouté PAR RANG élémentaire (0-5) de la source, par seconde,\n" +
             "EN PLUS du socle, réduit par la résistance élémentaire de la cible — 0 si la source\n" +
             "n'a pas investi dans damageElement. Ex: 0.2 = +0.2%MaxHP/rang → rang 5 = +1% Max HP/s en bonus.")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot)]
    public float rankDamagePercent = 0.2f;

    [Tooltip("Dégâts flat ajoutés PAR POINT élémentaire EFFECTIF (brut + bonus de rang) de la\n" +
             "source dans damageElement, EN PLUS du socle — 0 si la source n'a pas investi.\n" +
             "Ex: 0.10 = +0.1 dégât/point. Toujours flat (pas de mode Percent) — volontairement\n" +
             "SANS plafond, l'investissement doit toujours payer (cœur du jeu).")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot)]
    public float elementalPointsMultiplier = 0.10f;

    [Tooltip("Élément des dégâts (sert aussi à lire la résistance élémentaire de la cible) — au choix.")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot)]
    public ElementType damageElement = ElementType.Neutral;
#pragma warning restore CS0618

    // ── Ralentissement (Slow uniquement) ──────────────────────
    // Freeze n'utilise PAS ce champ — son immobilisation est toujours totale, câblée en dur
    // (StatusEffectSystem.OnApplyDebuff : slowMultiplier = 0f) et Freeze bloque maintenant
    // aussi les actions (isFreezed, comme Stun — 2026-09-07). Afficher ce champ sur un asset
    // Freeze laissait croire qu'il était configurable alors qu'il ne l'a jamais été.
    [Tooltip("Multiplicateur de vitesse [0..1].\n0 = immobilisé | 0.5 = 50% vitesse | 1 = aucun effet")]
    [Range(0f, 1f)]
    [ShowIf(nameof(debuffType), DebuffType.Slow, Header = "Ralentissement (Slow)")]
    public float slowMultiplier = 0f;

    // ── Réduction de soins (Poison — obsolète, gardé pour compat assets existants) ──
#pragma warning disable CS0618
    [Tooltip("Poison (§3.1.1.1) : réduit les soins reçus par la cible.\n0 = aucune réduction | 0.3 = -30% soins reçus")]
    [Range(0f, 1f)]
    [ShowIf(nameof(debuffType), DebuffType.Poison, Header = "Réduction de soins reçus (Poison — obsolète)")]
    public float healReduction = 0f;
#pragma warning restore CS0618

#pragma warning disable CS0618 // ArmorBreak obsolète — gardé pour compat assets existants
    // ── Réduction de défense (ArmorBreak — obsolète, utiliser Stats) ──
    [Tooltip("ArmorBreak (obsolète) : réduction flat appliquée aux 3 types de défense.\nNouveaux debuffs : utiliser Stats + StatModifierType.MeleeDefense/RangedDefense/MagicDefense/AllDefense.")]
    [ShowIf(nameof(debuffType), DebuffType.ArmorBreak, Header = "Réduction de défense (ArmorBreak — obsolète)")]
    public float defenseReduction = 0f;
#pragma warning restore CS0618

    // ── Drain de mana (ManaDrain) ─────────────────────────────
    [Tooltip("Flat : mana drainé par seconde (valeur directe).\nPercent : % du MaxMana DE LA CIBLE drainé par seconde\n(ex: 0.01 = 1%/s) — symétrique quel que soit qui lance sur qui (corrigé\n2026-09-29, même convention que le DoT).")]
    [ShowIf(nameof(debuffType), DebuffType.ManaDrain, Header = "Drain de mana (ManaDrain)")]
    public ModifierType manaDrainModifier = ModifierType.Flat;
    [Tooltip("ManaDrain (§3.1.1.1) : mana drainé par seconde, reversé au lanceur du debuff.\nRéutilise damagePerSecond pour le tick — ce champ est un alias lisible.")]
    [ShowIf(nameof(debuffType), DebuffType.ManaDrain)]
    public float manaDrainPerSecond = 0f;

    // ── Drain de vie (HpDrain) ─────────────────────────────────
    [Tooltip("Flat : dégâts vrais par seconde (valeur directe).\nPercent : % du MaxHP DE LA CIBLE par seconde (ex: 0.01 = 1%/s) — symétrique\nquel que soit qui lance sur qui (corrigé 2026-09-29, même convention que le\nDoT, remplace l'ancienne base LANCEUR du 2026-09-07 qui cassait dans le sens\nboss→joueur).")]
    [ShowIf(nameof(debuffType), DebuffType.HpDrain, Header = "Drain de vie (HpDrain)")]
    public ModifierType hpDrainModifier = ModifierType.Flat;
    [Tooltip("HpDrain : dégâts vrais par seconde infligés à la cible (ignore défense/résistances,\npeut tuer), reversés en soin identique au lanceur du debuff.")]
    [ShowIf(nameof(debuffType), DebuffType.HpDrain)]
    public float hpDrainPerSecond = 0f;

    // ── Proie (Prey) ─────────────────────────────────────────
    [Tooltip("% de dégâts supplémentaires subis par la proie. Routé dans le même\naccumulateur que FinalDamageReduction (en négatif) — compose avec les autres sources\nau lieu d'être un multiplicateur séparé en plus.")]
    [Range(0f, 5f)]
    [ShowIf(nameof(debuffType), DebuffType.Prey, Header = "Proie (Prey)")]
    public float preyDamageBonusPercent = 0f;

    // ── Réduction de stat (Stats) ─────────────────────────────
    [Tooltip("Stat à réduire — utilisé uniquement si debuffType = Stats.\nv3.5 : utilise StatModifierType (fusion BuffStatType + DebuffStatType).")]
    [ShowIf(nameof(debuffType), DebuffType.Stats, Header = "Réduction de stat (Stats)")]
    public StatModifierType debuffStatType = StatModifierType.MoveSpeed;

    [Tooltip("Type de modificateur — Flat ou Percent.")]
    [ShowIf(nameof(debuffType), DebuffType.Stats)]
    public ModifierType debuffModifier = ModifierType.Percent;

    [Tooltip("Valeur de réduction.\nFlat : valeur directe | Percent : ratio (0.10 = -10%)")]
    [ShowIf(nameof(debuffType), DebuffType.Stats)]
    public float debuffValue = 0f;

    // ── Dispel ─────────────────────────────────────────────────
    [Tooltip("Chance de retirer CHAQUE buff actif de la cible — jet indépendant par buff, pas\n" +
             "un seul jet global (4 buffs à 0.5 ≠ 50% de tout retirer d'un coup).")]
    [Range(0f, 1f)]
    [ShowIf(nameof(debuffType), DebuffType.Dispel, Header = "Dispel")]
    public float chancePerEffect = 1f;

    // ── Bonus de stats additionnels ───────────────────────────
    [Header("Malus de stats additionnels (optionnel)")]
    [Tooltip("S'applique EN PLUS de l'effet principal ci-dessus, quel que soit debuffType —\n" +
             "permet de composer plusieurs stats sur un seul debuff. Négatif automatiquement\n" +
             "(un debuff RETIRE, jamais besoin d'entrer une valeur négative).")]
    public List<StatLine> bonusStats = new List<StatLine>();

    // ── Skills bloqués ─────────────────────────────────────────
    [Header("Skills bloqués (optionnel)")]
    [Tooltip("S'applique EN PLUS de l'effet principal, quel que soit debuffType — glisser ici " +
             "le(s) SkillData que ce debuff rend injouables tant qu'il est actif (voir " +
             "StatusEffectSystem.IsSkillBlocked / SkillBar.cs, même bloc que le check Silence). " +
             "Ex : malus Aura bloquant le skill de capture de familier.")]
    public List<SkillData> blockedSkills = new List<SkillData>();

    // ── Familier ───────────────────────────────────────────────
    [Header("Bloque l'équipement du familier (optionnel)")]
    [Tooltip("Coché : le joueur ne peut pas équiper de familier tant que ce debuff est actif " +
             "(\"le familier a honte de lui\" — Florian, 2026-09-29). PAS ENCORE CONSOMMÉ PAR " +
             "AUCUN CODE — le système d'équipement de familier n'existe pas encore (Familier, " +
             "roadmap #8). Champ préparé en avance, même logique que blockedSkills avant que " +
             "skl_capture existe : le check réel (ex : Player.EquipFamiliar()) devra interroger " +
             "\"le joueur a-t-il un debuff actif avec ce flag ?\" le jour où ce système est codé.")]
    public bool blocksFamiliarEquip = false;

    // ── Purify ───────────────────────────────────────────────
    [Header("Non purifiable (optionnel)")]
    [Tooltip("Coché : ce debuff ignore BuffType.Purified/Dispel (StatusEffectSystem." +
             "RemoveDebuffsByChance) — ne peut être retiré que par sa propre logique de source " +
             "(ex : malus Aura, retiré uniquement en franchissant un seuil, jamais par une potion " +
             "de purification). Voir spec Prestige/Aura §2.3.")]
    public bool immuneToPurify = false;

    public override StatusEffectInstance CreateInstance(Entity source)
        => new DebuffInstance(this, source);
}
