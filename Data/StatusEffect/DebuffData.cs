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
    // Toujours en % du MaxHP cible pour base/rang (pas de mode Flat) — un DoT flat ne scale pas
    // avec le contenu (mob lvl30 vs lvl85), voir discussion — le % est la seule forme qui a du
    // sens ici.
#pragma warning disable CS0618 // Burn/Poison/Bleed obsolètes — gardés pour compat assets existants
    [Tooltip("% du Max HP de la cible par seconde, TOUJOURS appliqué (socle) — indépendant de\n" +
             "l'investissement élémentaire de la source. Ex: 0.2 = 0.2% MaxHP/s minimum garanti.")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot,
        Header = "Dégâts sur la durée (Dot)")]
    public float baseDamagePercent = 0.2f;

    [Tooltip("% du Max HP de la cible ajouté PAR RANG élémentaire (0-5) de la source, par seconde,\n" +
             "EN PLUS du socle — 0 si la source n'a pas investi dans damageElement.\n" +
             "Ex: 0.15 = +0.15%MaxHP/rang → rang 5 = +0.75% Max HP/s en bonus.")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot)]
    public float rankDamagePercent = 0.15f;

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

    // ── Ralentissement (Freeze, Slow) ─────────────────────────
    [Tooltip("Multiplicateur de vitesse [0..1].\n0 = immobilisé | 0.5 = 50% vitesse | 1 = aucun effet\nFreeze : toujours 0 — immobilisation totale (§3.1.1.1).")]
    [Range(0f, 1f)]
    [ShowIf(nameof(debuffType), DebuffType.Freeze, DebuffType.Slow, Header = "Ralentissement (Freeze, Slow)")]
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
    [Tooltip("ManaDrain (§3.1.1.1) : mana drainé par seconde, reversé au lanceur du debuff.\nRéutilise damagePerSecond pour le tick — ce champ est un alias lisible.")]
    [ShowIf(nameof(debuffType), DebuffType.ManaDrain, Header = "Drain de mana (ManaDrain)")]
    public float manaDrainPerSecond = 0f;

    // ── Drain de vie (HpDrain) ─────────────────────────────────
    [Tooltip("HpDrain : dégâts par seconde infligés à la cible (respecte défense/résistances,\npeut tuer), reversés en soin identique au lanceur du debuff.")]
    [ShowIf(nameof(debuffType), DebuffType.HpDrain, Header = "Drain de vie (HpDrain)")]
    public float hpDrainPerSecond = 0f;

    // ── Marque (Mark) ──────────────────────────────────────────
    [Tooltip("% de dégâts supplémentaires subis par la cible marquée. Routé dans le même\naccumulateur que FinalDamageReduction (en négatif) — compose avec les autres sources\nau lieu d'être un multiplicateur séparé en plus.")]
    [Range(0f, 5f)]
    [ShowIf(nameof(debuffType), DebuffType.Mark, Header = "Marque (Mark)")]
    public float markDamageBonusPercent = 0f;

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

    public override StatusEffectInstance CreateInstance(Entity source)
        => new DebuffInstance(this, source);
}
