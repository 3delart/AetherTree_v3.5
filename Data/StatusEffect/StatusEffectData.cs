using UnityEngine;
using System.Collections.Generic;

// =============================================================
// StatusEffectData — SO de base pour tous les buffs et debuffs
// Path : Assets/Scripts/Data/StatusEffect/StatusEffectData.cs
// AetherTree GDD v3.5 — §3.1.1
//
// Historique :
//   v3.0 — Silence réattribué à Ténèbres (Wind supprimé)
//           Glace supprimée → Freeze reste, réattribué à Eau
//           Shock renommé ArmorBreak (réduction défense physique)
//           Shocked ajouté séparément (interrupt + mini-stun Foudre)
//           ManaBreak renommé ManaDrain (§3.1.1.1)
//           HealOnTime renommé Regeneration (§3.1.1.2)
//           AttackSpeedUp / MoveSpeedUp fusionnés dans Haste (§3.1.1.2)
//           RangeDefense corrigé en RangedDefense (§3.1)
//           Doublons BuffStatType / DebuffStatType supprimés
//   v3.5 — BuffStatType + DebuffStatType fusionnés en StatModifierType
//           BuffType.RegenHP supprimé (doublon avec Regeneration)
//           BuffType.Taunt supprimé (c'est un DebuffType)
//           CritChanceUp / CritDamageUp séparés dans BuffType
// =============================================================

// ── Enums debuff ──────────────────────────────────────────────
public enum DebuffType
{
    // DoT — retirés du design, utiliser Dot (fin d'enum) avec damageElement à la place.
    // Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser ces 3 positions.
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Fire à la place.")]
    Burn,
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Nature à la place.")]
    Poison,
    [System.Obsolete("Retiré du design — utilise Dot + damageElement = Neutral à la place.")]
    Bleed,

    // Ralentissement & Immobilisation
    Freeze,     // Gel        — Eau        — immobilisation totale (réattribué Eau v3.0 — §3.1.1.1)
    Slow,       // Ralenti    — Eau        — réduit la vitesse de déplacement % (§3.1.1.1)
    Root,       // Enraciné   — Nature     — bloque le mouvement, peut toujours attaquer (§3.1.1.1)

    // Contrôle de foule dur
    Stun,       // Étourdi    — Terre      — bloque toutes les actions (CC dur §3.1.1.1)
    Fear,       // Peur       — Ténèbres   — fuite incontrôlée (CC dur §3.1.1.1)
    Sleep,      // Sommeil    — réveil au premier dégât reçu (§3.1.1.1)

    // Déplacement & Interrupt
    Knockback,  // Recul      — Eau        — déplace la cible à l'impact, ponctuel (§3.1.1.1)
    Shocked,    // Choc       — Foudre     — identique à Stun (bloque toutes les actions),
                // sa propre durée/valeurs via DebuffData (§3.1.1.1) — flag séparé (isShocked)
                // pour ne jamais couper prématurément un Stun actif en parallèle ou l'inverse.

    // Précision & Ressource
    [System.Obsolete("Retiré du design — utilise Stats + StatModifierType.Precision à la place. Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser cette position.")]
    Blind,      // Aveugle    — Lumière    — réduction précision drastique (§3.1.1.1)
    ManaDrain,  // Drain mana — Ténèbres   — drain progressif sur la durée, reversé au lanceur (§3.1.1.1)

    // Défense
    [System.Obsolete("Retiré du design — utilise Stats + StatModifierType.MeleeDefense/RangedDefense/MagicDefense/AllDefense à la place. Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser cette position.")]
    ArmorBreak, // Armure brisée — Terre  — réduction défense physique % temporaire (§3.1.1.1)

    // Utilitaire
    Silence,    // Silence    — Ténèbres   — bloque les skills (§3.1.1.1)
    Taunt,      // Taunt      — force les ennemis à cibler cette entité (§3.1.1.1)

    // Stats & Spéciaux
    Stats,      // Réduction de stat spécifique (utilise StatModifierType)
    Mark,       // Marque — bonus % dégâts subis par la cible (routé dans l'accumulateur
                // FinalDamageReduction, en négatif — compose avec les autres sources au lieu
                // d'être un multiplicateur séparé en plus, voir StatusEffectSystem)
    [System.Obsolete("Retiré — jamais utilisé, aucun hook custom implémenté. Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser cette position.")]
    Other,      // Effet spécial custom

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    Dot,        // DoT générique réutilisable pour tout élément (voir damageElement) —
                // même formule que Burn/Poison/Bleed, juste pas un nom/élément figé.
                // Futurs debuffs par élément (noyade=Eau, etc.) : soit ce type générique
                // avec damageElement + effectName/icon dédiés, soit un DebuffType propre —
                // au choix du designer au moment de les créer.
    Dispel,     // Retire les buffs actifs de la cible, jet indépendant par buff actif
                // (chancePerEffect sur DebuffData) — voir BuffType.Dispel, obsolète,
                // mal placé (Dispel est un effet négatif sur cible ennemie, pas un buff).
    HpDrain,    // Vol de vie progressif — dégâts/s sur la cible (TakeDamage, respecte
                // défense/résistances) reversés en soin au lanceur du debuff (§3.1.1.1)
}

// ── Enums buff ────────────────────────────────────────────────
public enum BuffType
{
    // Soins
    Heal,           // Soin instantané (§3.1.1.2)
    Regeneration,   // Soin sur la durée — HoT (§3.1.1.2)

    // Défense
    Shield,         // Bouclier — absorbe les dégâts en priorité avant les HP (§3.1.1.2)

    // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp retirés
    // (2026) — redondants avec Stats (StatModifierType couvre déjà chaque stat individuellement :
    // AllResistances, MeleeDefense/RangedDefense/MagicDefense, Dodge, Precision, AttackDamage,
    // MoveSpeed, CritChance, CritDamage). Gardés ici comme placeholders [Obsolete] pour ne pas
    // décaler les ordinaux sérialisés de Purified/Invincible/Stealth/Dispel/Stats/Other qui
    // suivent — Unity sérialise un enum par sa position int, pas son nom. Ne jamais réutiliser
    // ces slots pour une nouvelle valeur ; ajouter en fin d'enum à la place (voir Revive).
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.AllResistances.")]
    Removed_Barrier,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.MeleeDefense/RangedDefense/MagicDefense.")]
    Removed_DefenseUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.Dodge.")]
    Removed_DodgeUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.Precision.")]
    Removed_PrecisionUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.AttackDamage.")]
    Removed_AttackUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.MoveSpeed.")]
    Removed_Haste,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.CritChance.")]
    Removed_CritChanceUp,
    [System.Obsolete("Retiré 2026 — voir BuffType.Stats + StatModifierType.CritDamage.")]
    Removed_CritDamageUp,

    // Spéciaux
    Purified,       // Suppression des debuffs actifs, jet indépendant par debuff actif
                    // (chancePerEffect sur BuffData) — Lumière (§3.1.1.2)
    Invincible,     // Invincibilité temporaire — post-respawn 3s (§3.1.1.3)
    Stealth,        // Furtivité — interrompue par attaque (Player.UseSkill) ou dégât reçu
                    // (StatusEffectSystem.OnTakeDamage) — §3.1.1.3. Invisible aux Mobs (exclu de
                    // Mob.RefreshEnemyList tant qu'actif, sans exception liée au mouvement) —
                    // PNJ ne ciblent jamais les joueurs de toute façon (canFight ne détecte que
                    // les Mobs). Transparence visuelle locale sur Player (voir
                    // Player.UpdateStealthVisual/stealthAlpha). Le "clignotement toutes les 3s
                    // en bougeant" (repérage PvP) est HORS SCOPE tant qu'aucune infra réseau
                    // n'existe (2026-09-06) — rien à câbler contre des mobs/PNJ pour ça.
    [System.Obsolete("Retiré — Dispel cible un ennemi comme effet négatif, voir DebuffType.Dispel.")]
    Dispel,         // Ordinal gardé (assets déjà sauvegardés) — ne JAMAIS réutiliser cette position.
    Stats,          // Augmentation de stat spécifique (utilise StatModifierType)
    Other,          // Effet spécial custom
    Revive,         // Résurrection instantanée (Player uniquement) — ajouté en fin d'enum
                     // volontairement, pour ne jamais décaler les valeurs déjà sérialisées.
}

// ── Stat ciblée par un modificateur de buff ou debuff ─────────
// v3.5 — Fusion de BuffStatType + DebuffStatType en un seul enum.
// Utilisé par BuffData (buffType = Stats) et DebuffData (debuffType = Stats).
public enum StatModifierType
{
    MaxHP, MaxMana,
    // RegenHP/RegenMana ici = TEMPORAIRE (buff/debuff avec durée, expire). Pour un bonus
    // PERMANENT (équipement / PermanentSkillData.bonuses, jamais expire), voir
    // StatType.BonusRegenHP/BonusRegenMana dans StatBonus.cs — les deux s'additionnent sur
    // le même champ final Entity.RegenHP, pas de conflit, juste deux durées de vie différentes.
    [InspectorName("Regen HP (temporaire, buff/debuff)")]   RegenHP,
    [InspectorName("Regen Mana (temporaire, buff/debuff)")] RegenMana,
    AttackDamage, AttackSpeed, MoveSpeed,
    MeleeDefense, RangedDefense, MagicDefense,
    CritChance, CritDamage,
    [System.Obsolete("Retiré — jamais câblé (GetBaseStatValue/ModifyEntityStat n'avaient pas de " +
        "case, et aucun moyen de choisir l'élément visé). Remplacé par ElementalPointFire/Water/" +
        "Lightning/Earth/Nature/Darkness/Light/All — ordinal gardé, jamais réutilisé.")]
    ElementalPoint, Dodge, Precision,
    FireResistance, WaterResistance, EarthResistance,
    NatureResistance, LightningResistance,
    DarknessResistance, LightResistance,
    AllResistances,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Bonus de gain purs — Flat
    // ET Percent donnent le même résultat (base neutre 1f, voir GetBaseStatValue), 0.20 =
    // +20% dans les deux cas, choisis celui qui te semble le plus clair.
    XPBonus,    // +% XP gagnée sur kill de mob (joueur) — GDD Talisman XP_Bonus
    GoldBonus,  // +% Aeris gagné au ramassage — GDD Talisman Gold_Find

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    AllDefense, // Écrit simultanément sur MeleeDefense + RangedDefense + MagicDefense — jamais
                // stockée comme cible finale elle-même, toujours répartie au moment de
                // l'accumulation. Voir StatusEffectSystem.AccumulateStatLine.

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Accumulateurs BRUTS —
    // PAS la formule (Base+Flat)×(1+%) du reste de cet enum. Le mode (Flat/Percent) choisi
    // route la valeur vers Entity.FinalDamageBonusFlat OU FinalDamageBonusPercent (jamais les
    // deux combinés) — voir StatusEffectSystem.ReapplyActiveModifiers, cas spécial dédié, PAS
    // dans ExceptionStats (ni "stat normale" ni "stat exception", 3e catégorie). Appliqués sur
    // le NOMBRE de dégâts par CombatSystem.CalculateDamage/CalculateMobDamage, en tout dernier.
    FinalDamageBonus,     // Attaquant — bonus de dégâts infligés.
    FinalDamageReduction, // Défenseur — réduction des dégâts reçus.

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Remplacent l'ancien
    // ElementalPoint générique (obsolète, jamais câblé) — un vrai stat "normal" par élément,
    // formule (Base+Flat)×(1+%) comme le reste de cet enum (PAS une exception comme
    // StatType.PointsX côté équipement — divergence volontaire, demande explicite Florian).
    ElementalPointFire, ElementalPointWater, ElementalPointLightning, ElementalPointEarth,
    ElementalPointNature, ElementalPointDarkness, ElementalPointLight,
    ElementalPointAll, // Répartie sur les 7 ci-dessus au moment de l'accumulation, chacune
                       // calcule son propre résultat avec sa propre base — jamais stockée
                       // comme cible finale elle-même.

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety). Même schéma exact que
    // XPBonus/GoldBonus (exception toujours additive, base neutre — voir ExceptionStats) —
    // alimente le talisman Spirit_XP (2026-09-06).
    SpiritXpBonus, // +% XP gagnée par l'Esprit actif au kill — GDD Talisman Spirit_XP
}

// ── Ligne de stat additionnelle (BuffData.bonusStats / DebuffData.bonusStats) ─────
// S'applique EN PLUS de l'effet principal (Heal/Shield/Regeneration/...), quel que soit
// buffType/debuffType — permet de composer plusieurs stats sur un seul effet (ex: Talisman
// HP_Boost = +%MaxHP ET +%RegenHP ; Def_Boost = +%melee ET +%ranged ET +%magic ET AllDefense
// en Percent pour un bonus global par-dessus). Voir StatusEffectSystem.AccumulateStatLine pour
// l'ordre d'évaluation.
public enum StatLineMode
{
    Flat,     // Valeur directe (ex: +300 MaxHP)
    Percent,  // % — sommé GLOBALEMENT avec tous les autres % actifs ciblant la même stat
              // (tous buffs/debuffs actifs confondus, pas juste les lignes de CET effet), puis
              // appliqué en une seule fois : (Base + ΣFlat) × (1 + Σ%). Remplace
              // PercentOfBase (fusionné dans Percent, même ordinal 1) — plus de distinction
              // scopée par effet individuel. Voir StatusEffectSystem.AccumulateStatLine.
    [System.Obsolete("Retiré — PercentOfFinal fusionné dans Percent (voir AllDefense / somme " +
        "globale). Ordinal gardé (jamais réutilisé) pour qu'une valeur héritée=2 reste visible " +
        "et distincte dans l'Inspector plutôt que de silencieusement redevenir Flat.")]
    Obsolete_PercentOfFinal,
}

[System.Serializable]
public class StatLine
{
    public StatModifierType stat;

    [ShowIf(nameof(stat),
        StatModifierType.MaxHP, StatModifierType.MaxMana,
        StatModifierType.RegenHP, StatModifierType.RegenMana,
        StatModifierType.AttackDamage, StatModifierType.AttackSpeed,
        StatModifierType.MeleeDefense, StatModifierType.RangedDefense, StatModifierType.MagicDefense,
        StatModifierType.AllDefense, StatModifierType.Dodge, StatModifierType.Precision,
        StatModifierType.FinalDamageBonus, StatModifierType.FinalDamageReduction,
        StatModifierType.ElementalPointFire, StatModifierType.ElementalPointWater,
        StatModifierType.ElementalPointLightning, StatModifierType.ElementalPointEarth,
        StatModifierType.ElementalPointNature, StatModifierType.ElementalPointDarkness,
        StatModifierType.ElementalPointLight, StatModifierType.ElementalPointAll)]
    public StatLineMode mode = StatLineMode.Flat;

    [Tooltip("Flat : valeur directe\nPercent : % sommé globalement avec tous les autres % actifs ciblant la même stat, appliqué une fois : (Base+ΣFlat)×(1+Σ%). Masqué pour les stats toujours additives (Crit, Résistances, Points élémentaires, MoveSpeed, XPBonus, GoldBonus).")]
    public float value;
}

// =============================================================
// StatusEffectData — base abstraite
// =============================================================
public abstract class StatusEffectData : ScriptableObject
{
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"buff_\"\n" +
             "ou \"dbf_\" selon le type (ex: \"buff_shield\", \"dbf_poison\"). Ne JAMAIS afficher au\n" +
             "joueur — voir effectName pour l'affichage.")]
    public string effectID;
    [Tooltip("Nom affiché au joueur (fr/en).")]
    public LocalizedText effectName = new LocalizedText();
    [Tooltip("Description affichée au joueur (fr/en) — tooltip consommable, icône de statut actif.")]
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    [Header("Durée")]
    [Tooltip("Durée de base de l'effet en secondes.\n§3.1.1.4 : durée et chance d'application définitives sur SkillData.")]
    public float duration = 3f;

    /// <summary>Crée une instance runtime de cet effet.</summary>
    public abstract StatusEffectInstance CreateInstance(Entity source);

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(effectID))
            effectID = name;
    }
#endif
}

// =============================================================
// StatusEffectEntry — une ligne sur arme / rune / skill
// =============================================================
[System.Serializable]
public class StatusEffectEntry
{
    [Tooltip("SO de l'effet à appliquer (DebuffData ou BuffData).")]
    public StatusEffectData effect;

    [Tooltip("Probabilité d'application par attaque/utilisation [0..1].\nEx: 0.03 = 3% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.05f;

    /// <summary>Roll si l'effet se déclenche. True = appliquer l'effet.</summary>
    public bool Roll() => effect != null && Random.value < chance;
}
