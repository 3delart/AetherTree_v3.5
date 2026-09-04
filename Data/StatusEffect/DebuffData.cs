using UnityEngine;

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
    // Formule complète par tick (voir DebuffInstance.Tick) — 2 termes, pas de socle séparé :
    //   rangTerme   = target.MaxHP × (rang élémentaire de la source × rankDamagePercent / 100)
    //   pointsTerme = points élémentaires BRUTS de la source × elementalPointsMultiplier (flat)
    //   dps = (rangTerme + pointsTerme) × (1 - résistance élémentaire de la cible à damageElement)
    // rang/points lus sur la SOURCE (celui qui a appliqué le debuff), résistance sur la CIBLE —
    // même schéma que CombatSystem. Rang 0 ET points 0 (source sans investissement dans
    // damageElement) → 0 dégât, volontaire : un Dot est un mécanisme élémentaire, pas universel.
    // Toujours en % du MaxHP cible (pas de mode Flat) — un DoT flat ne scale pas avec le
    // contenu (mob lvl30 vs lvl85), voir discussion — le % est la seule forme qui a du sens ici.
#pragma warning disable CS0618 // Burn/Poison/Bleed obsolètes — gardés pour compat assets existants
    [Tooltip("% du Max HP de la cible ajouté PAR RANG élémentaire (0-5) de la source, par seconde.\n" +
             "Ex: 0.2 = +0.2%MaxHP/rang → rang 5 = +1% Max HP/s. Plafonné naturellement (rang max 5).")]
    [ShowIf(nameof(debuffType), DebuffType.Burn, DebuffType.Poison, DebuffType.Bleed, DebuffType.Dot,
        Header = "Dégâts sur la durée (Dot)")]
    public float rankDamagePercent = 0.2f;

    [Tooltip("Dégâts flat ajoutés PAR POINT élémentaire BRUT de la source (pas les points \"effectifs\"\n" +
             "avec bonus de rang — évite de compter le rang deux fois). Ex: 0.10 = +0.1 dégât/point.\n" +
             "Toujours flat (pas de mode Percent) — volontairement SANS plafond, l'investissement\n" +
             "doit toujours payer (cœur du jeu). 0 pour un élément sans points investissables (ex:\n" +
             "Neutral) — compenser en montant rankDamagePercent sur cet asset.")]
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

    // ── Réduction de défense (ArmorBreak, Shocked) ────────────
    [Tooltip("ArmorBreak : réduction flat appliquée aux 3 types de défense.\nShocked : même champ — utilisé pour le mini-stun via CombatSystem.")]
    [ShowIf(nameof(debuffType), DebuffType.ArmorBreak, DebuffType.Shocked, Header = "Réduction de défense (ArmorBreak, Shocked)")]
    public float defenseReduction = 0f;

    // ── Drain de mana (ManaDrain) ─────────────────────────────
    [Tooltip("ManaDrain (§3.1.1.1) : mana drainé par seconde.\nRéutilise damagePerSecond pour le tick — ce champ est un alias lisible.")]
    [ShowIf(nameof(debuffType), DebuffType.ManaDrain, Header = "Drain de mana (ManaDrain)")]
    public float manaDrainPerSecond = 0f;

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

    public override StatusEffectInstance CreateInstance(Entity source)
        => new DebuffInstance(this, source);
}
