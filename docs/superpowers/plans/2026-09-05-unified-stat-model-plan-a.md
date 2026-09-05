# Plan A — Modèle Stat/Mode unifié (Flat/%)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unifier les deux pipelines de modificateurs de stats (buffs/debuffs et équipement)
sous une seule règle de calcul `(Base + ΣFlat) × (1 + Σ%)`, sommée GLOBALEMENT (toutes sources
actives confondues), en remplaçant le système `Flat`/`PercentOfBase`/`PercentOfFinal` scopé
par-effet côté buffs, et en ajoutant un vrai concept de mode Flat/% (aujourd'hui absent) côté
équipement.

**Architecture:** Deux enums de mode séparés mais de même forme (`StatLineMode` côté buff,
`ModifierType` déjà existant réutilisé côté équipement), tous deux réduits/alignés à 2 valeurs
(Flat/Percent). Chaque pipeline garde son propre calcul en 2 étapes — accumuler Flat et %
séparément par stat cible (toutes sources actives confondues), puis appliquer la formule une
fois par stat — plutôt qu'une fusion des deux systèmes en un seul. `AllDefense` (nouvelle entrée
dans les deux enums cibles) est un alias qui se répartit sur Melee/Ranged/MagicDefense au moment
de l'accumulation, jamais une cible stockée en tant que telle.

**Tech Stack:** Unity C#, ScriptableObject SO+Instance, pas de framework de test automatisé
(vérification manuelle Play Mode par Florian).

**Spec:** `docs/superpowers/specs/2026-09-05-unified-stat-modifiers-and-armor-type-design.md`
(section A — sections B et C sont des plans séparés, à faire après celui-ci).

## Global Constraints

- **Ordinal safety stricte** : toute nouvelle valeur d'enum va TOUJOURS en fin d'enum, jamais
  insérée au milieu ni réordonnée. Unity sérialise les enums par entier (ordinal), pas par nom.
- **Formule de calcul** (stats "normales", toutes sources confondues) :
  `FinalStat = (Base + Σ tous les Flat actifs) × (1 + Σ tous les % actifs)`.
- **Stats "exception"** (toujours additives, jamais de sélecteur Flat/% visible, jamais de
  multiplication) : `CritChance`, `CritMultiplier`/`CritDamage` (nom diffère par pipeline),
  Résistances élémentaires (Fire/Water/Earth/Nature/Lightning/Darkness/Light/All),
  `CritDmgReduction`, `MoveSpeed`, `XPBonus`, `GoldBonus` (ces 2 derniers n'existent que côté
  buff — `StatModifierType`, pas `StatType`). Points élémentaires (`PointsFire`/.../`PointsAll`,
  `ElementalPoint`) restent inchangés — jamais de mode, toujours additifs, non concernés par ce
  plan au-delà de leur statut d'exception implicite.
- **`AllDefense`** (nouvelle entrée, les deux pipelines) : jamais stockée comme cible finale —
  toujours répartie sur `MeleeDefense`+`RangedDefense`+`MagicDefense` au moment où sa valeur
  est accumulée (Flat ou %, chacune des 3 cibles reçoit la même contribution et calcule son
  propre résultat avec sa propre base).
- **Ordre d'application entre pipelines** (déjà dans le code, NE PAS changer l'ordre d'appel) :
  `CharacterStats.RecalculateStats` tourne EN PREMIER et pousse sa valeur finale sur l'Entity —
  cette valeur devient le "Base" que `StatusEffectSystem.ReapplyActiveModifiers` (tourne juste
  après, via `Entity.RequestRecalculate()`) utilise pour SA propre passe. Deux passes
  séquentielles indépendantes, pas de fusion des deux systèmes.
- Pas de tests automatisés — vérification manuelle Play Mode (Task 5).

---

### Task 1: Pipeline buff/debuff — modèle global unifié dans StatusEffectSystem

**Files:**
- Modify: `Data/StatusEffect/StatusEffectData.cs` (enum `StatLineMode`, enum `StatModifierType`)
- Modify: `Entities/StatusEffectSystem.cs` (`ReapplyActiveModifiers`, `ApplyBonusStats` supprimée
  et remplacée, `ApplyStatDebuff`/`ApplyStatBuff` supprimées)

**Interfaces:**
- Consumes : `StatLine.stat` (`StatModifierType`), `StatLine.mode` (`StatLineMode`, type
  inchangé, valeurs réduites), `StatLine.value` (`float`) — champs déjà existants sur `BuffData`/
  `DebuffData.bonusStats`, non modifiés par cette tâche. `DebuffData.debuffStatType`/
  `debuffModifier`(`ModifierType`)/`debuffValue`, `BuffData.buffStatType`/`buffModifier`
  (`ModifierType`)/`buffStatValue` — champs existants, non modifiés.
- Produces : `StatusEffectSystem.ReapplyActiveModifiers(Entity)` — signature publique inchangée,
  consommée par `Entity.RequestRecalculate()` (aucun changement d'appelant nécessaire).

Ce système gère aujourd'hui deux mécanismes séparés qui écrivent tous les deux sur les mêmes
stats via `StatModifierType` : (1) un unique "stat boost" par buff/debuff actif (`buffStatType`/
`buffModifier`/`buffStatValue` sur `BuffData`, pendant `BuffType.Stats`) et (2) une liste
`bonusStats` de `StatLine` (ajoutée plus tôt cette session, composable, plusieurs lignes par
effet). Les deux doivent maintenant alimenter la MÊME somme globale par stat, pour composer
correctement ensemble (aujourd'hui, (1) lit la valeur LIVE de la stat au moment de son propre
calcul — incohérent avec (2), qui lit déjà un snapshot figé `pureBase` — un vrai bug de
composition préexistant que cette tâche corrige au passage).

- [ ] **Step 1 : Réduire `StatLineMode` à 2 valeurs**

Dans `Data/StatusEffect/StatusEffectData.cs`, remplacer :

```csharp
public enum StatLineMode
{
    Flat,           // Valeur directe (ex: +300 MaxHP)
    PercentOfBase,  // % de la valeur AVANT les bonusStats de ce même effet (ex: +10% MaxHP)
    PercentOfFinal, // % du total APRÈS les lignes Flat/PercentOfBase de ce MÊME effet — pour
                    // un bonus "global" calculé sur le résultat déjà composé (ex: 3 lignes
                    // +10% melee/ranged/magic, puis 1 ligne PercentOfFinal +10% par-dessus
                    // les 3 déjà appliquées). Toujours évalué en 2e passe, jamais mélangé
                    // avec les 2 autres modes dans l'ordre d'apparition de la liste.
}
```

par :

```csharp
public enum StatLineMode
{
    Flat,     // Valeur directe (ex: +300 MaxHP)
    Percent,  // % — sommé GLOBALEMENT avec tous les autres % actifs ciblant la même stat
              // (tous buffs/debuffs actifs confondus, pas juste les lignes de CET effet), puis
              // appliqué en une seule fois : (Base + ΣFlat) × (1 + Σ%). Remplace
              // PercentOfBase/PercentOfFinal (retirés v3.5 unification) — plus de distinction
              // scopée par effet individuel. Voir StatusEffectSystem.AccumulateStatLine.
}
```

⚠ Migration assets existants : tout `StatLine` déjà sérialisé avec `mode = PercentOfBase`
(ordinal 1) devient `Percent` (ordinal 1, même position — comportement équivalent, juste
maintenant sommé globalement au lieu d'être scopé par effet). Tout `StatLine` avec
`mode = PercentOfFinal` (ordinal 2, valeur retirée) garde un entier 2 sérialisé sans nom
correspondant — se comporte comme `Flat` par défaut au runtime (voir Task 5, à vérifier
manuellement si un tel asset existe déjà).

- [ ] **Step 2 : Ajouter `StatModifierType.AllDefense` en fin d'enum**

Dans le même fichier, juste après `GoldBonus` :

```csharp
    XPBonus,    // +% XP gagnée sur kill de mob (joueur) — GDD Talisman XP_Bonus
    GoldBonus,  // +% Aeris gagné au ramassage — GDD Talisman Gold_Find

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    AllDefense, // Écrit simultanément sur MeleeDefense + RangedDefense + MagicDefense — jamais
                // stockée comme cible finale elle-même, toujours répartie au moment de
                // l'accumulation. Voir StatusEffectSystem.AccumulateStatLine.
}
```

- [ ] **Step 3 : Remplacer `ReapplyActiveModifiers`/`ApplyBonusStats` par le modèle global**

Dans `Entities/StatusEffectSystem.cs`, ajouter ce champ juste avant `ReapplyActiveModifiers`
(sert aussi au pipeline équipement — voir Task 3/4, mais défini ici car c'est le premier
pipeline à en avoir besoin) :

```csharp
    /// <summary>Stats sans sélecteur Flat/% (masqué côté Inspector via ShowIf) — toujours
    /// additives, jamais de multiplication par (1+%). AccumulateStatLine y route toute
    /// contribution en Flat, quel que soit le mode fourni (filet de sécurité en plus du
    /// masquage Inspector).</summary>
    private static readonly HashSet<StatModifierType> ExceptionStats = new HashSet<StatModifierType>
    {
        StatModifierType.CritChance, StatModifierType.CritDamage,
        StatModifierType.FireResistance, StatModifierType.WaterResistance,
        StatModifierType.EarthResistance, StatModifierType.NatureResistance,
        StatModifierType.LightningResistance, StatModifierType.DarknessResistance,
        StatModifierType.LightResistance, StatModifierType.AllResistances,
        StatModifierType.MoveSpeed, StatModifierType.XPBonus, StatModifierType.GoldBonus,
    };
```

Remplacer ENTIÈREMENT la méthode `ReapplyActiveModifiers` existante (de `public void
ReapplyActiveModifiers(Entity target)` jusqu'à son `}` fermant, juste avant `ApplyBonusStats`)
par :

```csharp
    public void ReapplyActiveModifiers(Entity target)
    {
        // Snapshot des stats "pures" — juste restaurées depuis _base par RequestRecalculate,
        // AVANT que cette méthode n'ajoute le moindre modificateur ce cycle-ci. Référence
        // stable pour tous les % actifs — sans ça, plusieurs % indépendants ciblant la même
        // stat composeraient entre eux au lieu de s'additionner, et le résultat dépendrait de
        // l'ordre d'itération de _activeBuffs/_activeDebuffs (non garanti par Dictionary).
        var pureBase = new Dictionary<StatModifierType, float>();
        foreach (StatModifierType s in System.Enum.GetValues(typeof(StatModifierType)))
            pureBase[s] = GetBaseStatValue(target, s);

        // ── Debuffs numériques ────────────────────────────────
        armorBreakReduction   = 0f;
        shockDefenseReduction = 0f;
        poisonHealReduction   = 0f;
        blindPrecisionMalus   = 0f;
        markDamageBonus       = 0f;
        slowMultiplier        = 1f;

        var flatSum    = new Dictionary<StatModifierType, float>();
        var percentSum = new Dictionary<StatModifierType, float>();

        foreach (var kvp in _activeDebuffs)
        {
            var d = kvp.Value.DebuffData;
            switch (kvp.Key)
            {
                case DebuffType.Slow:
                    slowMultiplier = Mathf.Min(slowMultiplier, d.slowMultiplier);
                    break;
                case DebuffType.Freeze:
                    slowMultiplier = 0f;
                    break;
                case DebuffType.Blind:
                    blindPrecisionMalus += d.debuffValue;
                    break;
#pragma warning disable CS0618 // Poison obsolète — gardé pour compat assets existants
                case DebuffType.Poison:
                    poisonHealReduction += d.healReduction;
                    break;
#pragma warning restore CS0618
                case DebuffType.ArmorBreak:
                    armorBreakReduction += d.defenseReduction;
                    break;
                case DebuffType.Shocked:
                    shockDefenseReduction += d.defenseReduction;
                    break;
                case DebuffType.Mark:
                    markDamageBonus += d.debuffValue;
                    break;
                case DebuffType.Stats:
                    AccumulateStatLine(flatSum, percentSum, d.debuffStatType,
                        d.debuffModifier == ModifierType.Percent, -d.debuffValue);
                    break;
            }
            AccumulateBonusStatsLines(flatSum, percentSum, d.bonusStats, sign: -1f);
        }

        // ── Buffs numériques ──────────────────────────────────
        buffDefenseBonus    = 0f;
        buffDodgeBonus      = 0f;
        buffPrecisionBonus  = 0f;
        buffSpeedMultiplier = 1f;
        buffAttackBonus     = 0f;
        buffCritChanceBonus = 0f;
        buffCritDamageBonus = 0f;
        barrierElementResist = 0f;

        // Barrier/DefenseUp/DodgeUp/PrecisionUp/AttackUp/Haste/CritChanceUp/CritDamageUp
        // retirés (2026) — voir accumulateurs ci-dessus, jamais réécrits, sans effet.
        foreach (var kvp in _activeBuffs)
        {
            if (kvp.Key == BuffType.Stats)
            {
                var b = kvp.Value.BuffData;
                AccumulateStatLine(flatSum, percentSum, b.buffStatType,
                    b.buffModifier == ModifierType.Percent, b.buffStatValue);
            }
            AccumulateBonusStatsLines(flatSum, percentSum, kvp.Value.BuffData.bonusStats, sign: 1f);
        }

        // ── Application finale — une fois par stat ─────────────
        foreach (StatModifierType stat in System.Enum.GetValues(typeof(StatModifierType)))
        {
            float flat = flatSum.TryGetValue(stat, out var f) ? f : 0f;
            float pct  = percentSum.TryGetValue(stat, out var p) ? p : 0f;
            if (flat == 0f && pct == 0f) continue;

            float baseVal = pureBase[stat];
            float final   = (baseVal + flat) * (1f + pct);
            ModifyEntityStat(target, stat, final - baseVal);
        }
    }

    /// <summary>Accumule les lignes bonusStats d'UN effet actif (Buff ou Debuff) dans les
    /// sommes globales — plus de logique 2-passes par-effet (PercentOfBase/PercentOfFinal),
    /// juste un ajout à la somme Flat ou % de la stat ciblée. isDebuff inverse le signe — un
    /// debuff RETIRE, jamais besoin de valeurs négatives côté designer.</summary>
    private void AccumulateBonusStatsLines(Dictionary<StatModifierType, float> flatSum,
        Dictionary<StatModifierType, float> percentSum, List<StatLine> lines, float sign)
    {
        if (lines == null) return;
        foreach (var line in lines)
        {
            if (line == null) continue;
            AccumulateStatLine(flatSum, percentSum, line.stat,
                line.mode == StatLineMode.Percent, sign * line.value);
        }
    }

    /// <summary>Route une contribution (Flat ou %) dans les sommes globales par stat.
    /// `AllDefense` n'est jamais stockée comme cible : répartie sur les 3 défenses réelles,
    /// chacune calculera son propre résultat avec sa propre base. Les stats de `ExceptionStats`
    /// tombent toujours en Flat, quel que soit `isPercent` (filet de sécurité — le champ mode
    /// est de toute façon masqué côté Inspector pour elles).</summary>
    private void AccumulateStatLine(Dictionary<StatModifierType, float> flatSum,
        Dictionary<StatModifierType, float> percentSum, StatModifierType stat, bool isPercent,
        float value)
    {
        if (stat == StatModifierType.AllDefense)
        {
            AccumulateStatLine(flatSum, percentSum, StatModifierType.MeleeDefense, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.RangedDefense, isPercent, value);
            AccumulateStatLine(flatSum, percentSum, StatModifierType.MagicDefense, isPercent, value);
            return;
        }

        if (isPercent && !ExceptionStats.Contains(stat))
            percentSum[stat] = percentSum.TryGetValue(stat, out var p) ? p + value : value;
        else
            flatSum[stat] = flatSum.TryGetValue(stat, out var f) ? f + value : value;
    }
```

- [ ] **Step 4 : Supprimer les méthodes devenues mortes**

Toujours dans `Entities/StatusEffectSystem.cs`, supprimer ENTIÈREMENT les deux méthodes
`ApplyStatDebuff` et `ApplyStatBuff` (juste après la nouvelle `AccumulateStatLine`, avant
`GetBaseStatValue`) :

```csharp
    private void ApplyStatDebuff(Entity target, DebuffData d)
    {
        float v = d.debuffModifier == ModifierType.Percent
            ? GetBaseStatValue(target, d.debuffStatType) * d.debuffValue
            : d.debuffValue;

        ModifyEntityStat(target, d.debuffStatType, -v);
    }

    private void ApplyStatBuff(Entity target, BuffData b)
    {
        float v = b.buffModifier == ModifierType.Percent
            ? GetBaseStatValue(target, b.buffStatType) * b.buffStatValue
            : b.buffStatValue;

        ModifyEntityStat(target, b.buffStatType, v);
    }
```

Leur logique est maintenant dans `ReapplyActiveModifiers` (appels directs à
`AccumulateStatLine` pour `DebuffType.Stats`/`BuffType.Stats`, voir Step 3). Ne PAS toucher
`GetBaseStatValue`/`ModifyEntityStat` — inchangées, aucun nouveau `case AllDefense` nécessaire
dedans (jamais transmise à ces deux méthodes, toujours résolue en Melee/Ranged/MagicDefense
avant).

- [ ] **Step 5 : Vérifier la compilation**

Ouvrir Unity, attendre la recompilation, vérifier la Console : 0 erreur. Les seuls usages
externes de `StatLineMode.PercentOfBase`/`PercentOfFinal` étaient dans les 2 méthodes
supprimées à l'instant — aucune autre référence dans le projet (déjà vérifié par lecture
complète de `StatusEffectSystem.cs` avant cette tâche).

- [ ] **Step 6 : Commit**

```bash
git add Data/StatusEffect/StatusEffectData.cs Entities/StatusEffectSystem.cs
git commit -m "feat: unify buff/debuff stat modifiers into global Flat/% model

StatLineMode drops PercentOfBase/PercentOfFinal (per-effect-scoped) for a
single Percent mode, summed globally across all active buffs/debuffs per
stat before one final (Base+Flat)*(1+Percent) application. Folds the
legacy single-stat buffStatType/debuffStatType mechanism into the same
global sums, fixing an inconsistency where it read the live mutating stat
instead of the frozen pureBase snapshot used everywhere else. Adds
StatModifierType.AllDefense (fans out to Melee/Ranged/MagicDefense at
accumulation time, never stored as its own target)."
```

---

### Task 2: Pipeline équipement — champ mode sur StatBonus + AllDefense

**Files:**
- Modify: `Data/Equipment/StatBonus.cs`

**Interfaces:**
- Produces : `StatBonus.mode` (`ModifierType`, nouveau champ public, défaut `Flat`) —
  consommé par Task 3/4 (`CharacterStats.AccumulateBonus`). `StatType.AllDefense` (nouvelle
  valeur d'enum) — consommée par Task 4.

Cette tâche est indépendante de Task 1 (fichiers différents) — peut être faite avant ou après,
ordre choisi ici pour que Task 3/4 aient tout ce qu'il leur faut déjà en place.

- [ ] **Step 1 : Ajouter `StatType.AllDefense` en fin d'enum**

Dans `Data/Equipment/StatBonus.cs`, juste après `BonusRegenMana` (dernière entrée) :

```csharp
    [InspectorName("Regen HP naturel (flat)")]   BonusRegenHP,
    [InspectorName("Regen Mana naturel (flat)")] BonusRegenMana,

    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    [InspectorName("All Defense (flat ou %, voir mode)")] AllDefense,
}
```

- [ ] **Step 2 : Ajouter le champ `mode` sur `StatBonus`**

Remplacer la classe `StatBonus` existante :

```csharp
[System.Serializable]
public class StatBonus
{
    [Tooltip("Type de stat à modifier — voir tableau des unités dans StatBonus.cs")]
    public StatType statType;

    [Tooltip(
        "FLAT  : Défenses, BonusAttack, Dodge, Précision, MoveSpeed,\n" +
        "        PointsFire/All/..., BonusHP, BonusMana, BonusRegen\n" +
        "        → entrer la valeur directe  ex: 200, 10, 0.5\n" +
        "\n" +
        "RATIO : CritChance, CritDamage, ResistFire/All/...\n" +
        "        → entrer en décimal  ex: 0.05 = 5% | 0.10 = 10%"
    )]
    public float value;
}
```

par :

```csharp
[System.Serializable]
public class StatBonus
{
    [Tooltip("Type de stat à modifier — voir tableau des unités dans StatBonus.cs")]
    public StatType statType;

    [Tooltip(
        "Flat : valeur directe ajoutée à la stat.\n" +
        "Percent : % appliqué sur (Base + tous les Flat actifs, équipement + buffs), sommé\n" +
        "avec tous les autres % actifs ciblant la même stat avant d'être appliqué UNE fois.\n" +
        "Masqué pour les stats toujours additives (Crit, Résistances, Points élémentaires,\n" +
        "MoveSpeed) — pas de multiplication pour elles, juste addition directe de `value`."
    )]
    [ShowIf(nameof(statType),
        StatType.MeleeDefense, StatType.RangedDefense, StatType.MagicDefense, StatType.AllDefense,
        StatType.BonusAttack, StatType.Dodge, StatType.Precision,
        StatType.BonusHP, StatType.BonusMana, StatType.BonusRegenHP, StatType.BonusRegenMana)]
    public ModifierType mode = ModifierType.Flat;

    [Tooltip(
        "Mode Flat : valeur directe, ex: 200, 10, 0.5\n" +
        "Mode Percent : décimal, ex: 0.05 = 5% | 0.10 = 10%\n" +
        "Stats sans sélecteur mode (Crit, Résistances, Points, MoveSpeed) : toujours en\n" +
        "décimal pour Crit/Résistances (0.10 = 10%), valeur directe pour Points/MoveSpeed."
    )]
    public float value;
}
```

- [ ] **Step 3 : Vérifier la compilation**

Unity recompile, Console : 0 erreur. `mode` n'est encore lu nulle part (Task 3/4) — champ
inerte pour l'instant, valeur par défaut `Flat` garantit qu'aucun `StatBonus` déjà sérialisé
ne change de comportement tant que Task 3/4 ne sont pas faites (tous les `StatBonus` existants
n'ont pas ce champ dans leur YAML → Unity le crée à `Flat` par défaut à la prochaine sauvegarde,
comportement identique à avant).

- [ ] **Step 4 : Commit**

```bash
git add Data/Equipment/StatBonus.cs
git commit -m "feat: add Flat/Percent mode field to StatBonus, AllDefense stat target

Reuses the existing ModifierType enum (Data/Skills/SkillData.cs) rather
than duplicating it. Mode selector hidden via ShowIf for stats that stay
purely additive (Crit, resistances, elemental points, MoveSpeed). Field
is inert until CharacterStats.AccumulateBonus reads it (next task)."
```

---

### Task 3: CharacterStats — accumulateurs en dictionnaire (refactor mécanique, comportement inchangé)

**Files:**
- Modify: `Entities/CharacterStats.cs`

**Interfaces:**
- Consumes : `StatBonus.statType`/`value` (`mode` pas encore lu ici — Task 4).
- Produces : `AccumulateStatBonuses(List<StatBonus>, Dictionary<StatType,float> flatAcc,
  Dictionary<ElementType,float> resist)` — nouvelle signature, remplace l'ancienne à 16
  paramètres `ref float`. Consommée par Task 4 (ajout du paramètre `percentAcc`) et par les 11
  sites d'appel de cette même tâche.

La méthode `AccumulateBonus`/`AccumulateStatBonuses` actuelle prend 14 paramètres `ref float` —
ajouter le support Percent en doublant chacun (14 de plus) serait illisible et source d'erreur
de transcription. Cette tâche remplace d'abord les `ref float` par UN SEUL dictionnaire
`Dictionary<StatType, float> flatAcc` (comportement strictement identique — tout reste Flat,
juste stocké différemment), sans toucher au calcul final. Task 4 ajoute ensuite `percentAcc` et
la vraie formule.

- [ ] **Step 1 : Remplacer `AccumulateStatBonuses`/`AccumulateBonus`**

Dans `Entities/CharacterStats.cs`, remplacer ENTIÈREMENT les deux méthodes (de
`private void AccumulateStatBonuses(` jusqu'au `}` fermant de `AccumulateBonus`, juste avant
`// ACCESSEURS UTILITAIRES`) par :

```csharp
    private void AccumulateStatBonuses(
        System.Collections.Generic.List<StatBonus> bonuses,
        Dictionary<StatType, float> flatAcc,
        Dictionary<ElementType, float> resist)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            AccumulateBonus(b, flatAcc, resist);
    }

    private void AccumulateBonus(
        StatBonus b,
        Dictionary<StatType, float> flatAcc,
        Dictionary<ElementType, float> resist)
    {
        switch (b.statType)
        {
            // Résistances élémentaires — sans plafond (GDD v3.5 §3.1)
            case StatType.ResistFire:      resist[ElementType.Fire]      += b.value; return;
            case StatType.ResistWater:     resist[ElementType.Water]     += b.value; return;
            case StatType.ResistEarth:     resist[ElementType.Earth]     += b.value; return;
            case StatType.ResistNature:    resist[ElementType.Nature]    += b.value; return;
            case StatType.ResistLightning: resist[ElementType.Lightning] += b.value; return;
            case StatType.ResistDarkness:  resist[ElementType.Darkness]  += b.value; return;
            case StatType.ResistLight:     resist[ElementType.Light]     += b.value; return;
            case StatType.ResistAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    resist[e] += b.value;
                return;

            // Points élémentaires
            case StatType.PointsFire:      elementalPoints[ElementType.Fire]      += b.value; return;
            case StatType.PointsWater:     elementalPoints[ElementType.Water]     += b.value; return;
            case StatType.PointsEarth:     elementalPoints[ElementType.Earth]     += b.value; return;
            case StatType.PointsNature:    elementalPoints[ElementType.Nature]    += b.value; return;
            case StatType.PointsLightning: elementalPoints[ElementType.Lightning] += b.value; return;
            case StatType.PointsDarkness:  elementalPoints[ElementType.Darkness]  += b.value; return;
            case StatType.PointsLight:     elementalPoints[ElementType.Light]     += b.value; return;
            case StatType.PointsAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    elementalPoints[e] += b.value;
                return;
        }

        // Reste des stats (Défenses, BonusAttack, Dodge, Precision, MoveSpeed, Crit*, BonusHP/
        // Mana/Regen, AllDefense) — stockées en Flat pur pour l'instant (Task 4 ajoute le
        // vrai routage Flat/Percent via b.mode).
        flatAcc[b.statType] = flatAcc.TryGetValue(b.statType, out var f) ? f + b.value : b.value;
    }
```

- [ ] **Step 2 : Adapter `RecalculateStats` — déclaration des accumulateurs**

Dans `RecalculateStats`, juste après la déclaration de `accResist` (bloc `// Résistances
élémentaires — accumulateur local avant push`), ajouter :

```csharp
        var flatAcc = new Dictionary<StatType, float>();
```

- [ ] **Step 3 : Mettre à jour les 11 sites d'appel de `AccumulateStatBonuses`**

Chacun des 11 appels existants (arme, armure, casque, gants, bottes, bijoux (dans la boucle
`foreach (var jewelry ...)`), rune arme, rune armure, esprit de base, esprit milestone (dans la
boucle `for (int mileLvl ...)`), skills permanents) a la forme actuelle :

```csharp
            AccumulateStatBonuses(X.Bonuses, ref accAttackMin, ref accAttackMax,
                ref accPrecision, ref accCritChance, ref accCritMultiplier,
                ref accMeleeDefense, ref accRangedDefense, ref accMagicDefense,
                ref accDodge, ref accBonusHP, ref accBonusMana,
                ref accBonusRegenHP, ref accBonusRegenMana, ref accBonusMoveSpeed,
                ref accCritDmgReduct, accResist);
```

(`X` variant : `weapon`, `armor`, `helmet`, `gloves`, `boots`, `jewelry`,
`weapon.equippedRune`, `armor.equippedRune`, `spirit`, `milestone`, `p` — et `.Bonuses` devient
`.bonuses` pour `weapon.equippedRune`/`armor.equippedRune`/`milestone`/`p`, déjà le cas
aujourd'hui, ne pas changer ce détail). Remplacer CHACUN des 11 appels par la forme courte :

```csharp
            AccumulateStatBonuses(X.Bonuses, flatAcc, accResist);
```

(en gardant `.bonuses`/`.Bonuses` exactement comme dans le code actuel à chaque site — seule la
liste de paramètres après le premier change, identique partout).

- [ ] **Step 4 : Adapter la section PUSH — lire `flatAcc` en plus des accumulateurs locaux**

Les accumulateurs locaux (`accMeleeDefense`, `accAttackMin`, etc.) ne reçoivent plus les
contributions `StatBonus` (déplacées dans `flatAcc`) — ils gardent uniquement leurs sources
directes (stats rollées d'armure/casque/gants/bottes, StatPoints, courbes CharacterData, bonus
de rang élémentaire/neutre). Remplacer le bloc `// PUSH SUR ENTITY` existant :

```csharp
        player.SetMaxHP       (baseHP        + accBonusHP);
        player.SetMaxMana     (baseMana      + accBonusMana);
        player.SetRegenHP     (baseRegenHP   + accBonusRegenHP);
        player.SetRegenMana   (baseRegenMana + accBonusRegenMana);
        player.SetMoveSpeed   (baseMoveSpeed + accBonusMoveSpeed);

        player.SetAttackDamageMin(accAttackMin);
        player.SetAttackDamageMax(accAttackMax);
        player.SetPrecision      (accPrecision);
        player.SetCritChance     (accCritChance);
        player.SetCritMultiplier (accCritMultiplier);

        player.SetMeleeDefense (accMeleeDefense);
        player.SetRangedDefense(accRangedDefense);
        player.SetMagicDefense (accMagicDefense);
        player.SetDodge        (accDodge);
        player.SetCritDamageReduction(accCritDmgReduct);
```

par (ajoute simplement le flat de `flatAcc` à chaque total, `TryGetValue` avec défaut `0f` —
strictement équivalent en valeur puisque `flatAcc` ne contient que ce que les `StatBonus`
contenaient déjà avant cette tâche) :

```csharp
        float FlatOf(StatType stat) => flatAcc.TryGetValue(stat, out var f) ? f : 0f;

        player.SetMaxHP       (baseHP        + accBonusHP        + FlatOf(StatType.BonusHP));
        player.SetMaxMana     (baseMana      + accBonusMana      + FlatOf(StatType.BonusMana));
        player.SetRegenHP     (baseRegenHP   + accBonusRegenHP   + FlatOf(StatType.BonusRegenHP));
        player.SetRegenMana   (baseRegenMana + accBonusRegenMana + FlatOf(StatType.BonusRegenMana));
        player.SetMoveSpeed   (baseMoveSpeed + accBonusMoveSpeed + FlatOf(StatType.MoveSpeed));

        player.SetAttackDamageMin(accAttackMin + FlatOf(StatType.BonusAttack));
        player.SetAttackDamageMax(accAttackMax + FlatOf(StatType.BonusAttack));
        player.SetPrecision      (accPrecision + FlatOf(StatType.Precision));
        player.SetCritChance     (accCritChance + FlatOf(StatType.CritChance));
        player.SetCritMultiplier (accCritMultiplier + FlatOf(StatType.CritMultiplier));

        player.SetMeleeDefense (accMeleeDefense + FlatOf(StatType.MeleeDefense) + FlatOf(StatType.AllDefense));
        player.SetRangedDefense(accRangedDefense + FlatOf(StatType.RangedDefense) + FlatOf(StatType.AllDefense));
        player.SetMagicDefense (accMagicDefense + FlatOf(StatType.MagicDefense) + FlatOf(StatType.AllDefense));
        player.SetDodge        (accDodge + FlatOf(StatType.Dodge));
        player.SetCritDamageReduction(accCritDmgReduct + FlatOf(StatType.CritDmgReduction));
```

(`AllDefense` traitée ici comme un simple alias Flat additionné aux 3 défenses — cette tâche
reste 100% Flat, le vrai fan-out Flat/% propre à `AllDefense` arrive Task 4, cette étape
intermédiaire est juste là pour que le comportement reste correct et testable après ce refactor
mécanique.)

- [ ] **Step 5 : Vérifier en Play Mode — comportement strictement inchangé**

Équiper un item ayant un `StatBonus` existant (n'importe lequel, ex: +30 Melee Defense sur une
arme) → vérifier sur la fiche perso que la valeur affichée est IDENTIQUE à avant cette tâche
(refactor mécanique, aucun changement de résultat attendu à ce stade).

- [ ] **Step 6 : Commit**

```bash
git add Entities/CharacterStats.cs
git commit -m "refactor: CharacterStats StatBonus accumulation via dictionary, not 14 ref floats

Mechanical refactor, no behavior change — replaces the 14-parameter ref
float signature (about to double to 28 for Percent support) with a single
Dictionary<StatType,float> flatAcc, matching the existing resist
dictionary pattern already used in the same method. Sets up Task 4 to add
Percent mode support without an unreadable parameter list."
```

---

### Task 4: CharacterStats — support Percent réel + AllDefense fan-out

**Files:**
- Modify: `Entities/CharacterStats.cs`

**Interfaces:**
- Consumes : `StatBonus.mode` (`ModifierType`, Task 2).
- Produces : `AccumulateBonus` route désormais réellement Flat vs Percent selon `b.mode`.

- [ ] **Step 1 : Ajouter la liste des stats "exception" côté équipement**

Dans `Entities/CharacterStats.cs`, juste avant `AccumulateStatBonuses`, ajouter :

```csharp
    /// <summary>Stats sans sélecteur Flat/% côté Inspector (voir ShowIf sur StatBonus.mode) —
    /// toujours additives. AccumulateBonus y route toute contribution en Flat quel que soit
    /// b.mode (filet de sécurité, en plus du masquage Inspector).</summary>
    private static readonly HashSet<StatType> ExceptionStatTypes = new HashSet<StatType>
    {
        StatType.CritChance, StatType.CritMultiplier, StatType.CritDmgReduction,
        StatType.ResistFire, StatType.ResistWater, StatType.ResistEarth, StatType.ResistNature,
        StatType.ResistLightning, StatType.ResistDarkness, StatType.ResistLight, StatType.ResistAll,
        StatType.MoveSpeed,
        StatType.PointsFire, StatType.PointsWater, StatType.PointsEarth, StatType.PointsNature,
        StatType.PointsLightning, StatType.PointsDarkness, StatType.PointsLight, StatType.PointsAll,
    };
```

(Les résistances/points ont déjà leur propre `case` avec `return` dans `AccumulateBonus` qui
ne passe jamais par `flatAcc`/`percentAcc` — cette liste ne sert donc en pratique qu'à
`CritChance`/`CritMultiplier`/`CritDmgReduction`/`MoveSpeed`, mais reste exhaustive pour rester
lisible et défensive si un futur refactor change l'ordre des `case`.)

`Dictionary`/`HashSet` viennent de `System.Collections.Generic` déjà importé en haut du fichier.

- [ ] **Step 2 : Étendre `AccumulateStatBonuses`/`AccumulateBonus` avec `percentAcc`**

Remplacer la signature (issue de Task 3) :

```csharp
    private void AccumulateStatBonuses(
        System.Collections.Generic.List<StatBonus> bonuses,
        Dictionary<StatType, float> flatAcc,
        Dictionary<ElementType, float> resist)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            AccumulateBonus(b, flatAcc, resist);
    }

    private void AccumulateBonus(
        StatBonus b,
        Dictionary<StatType, float> flatAcc,
        Dictionary<ElementType, float> resist)
    {
        switch (b.statType)
        {
            // Résistances élémentaires — sans plafond (GDD v3.5 §3.1)
            case StatType.ResistFire:      resist[ElementType.Fire]      += b.value; return;
            case StatType.ResistWater:     resist[ElementType.Water]     += b.value; return;
            case StatType.ResistEarth:     resist[ElementType.Earth]     += b.value; return;
            case StatType.ResistNature:    resist[ElementType.Nature]    += b.value; return;
            case StatType.ResistLightning: resist[ElementType.Lightning] += b.value; return;
            case StatType.ResistDarkness:  resist[ElementType.Darkness]  += b.value; return;
            case StatType.ResistLight:     resist[ElementType.Light]     += b.value; return;
            case StatType.ResistAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    resist[e] += b.value;
                return;

            // Points élémentaires
            case StatType.PointsFire:      elementalPoints[ElementType.Fire]      += b.value; return;
            case StatType.PointsWater:     elementalPoints[ElementType.Water]     += b.value; return;
            case StatType.PointsEarth:     elementalPoints[ElementType.Earth]     += b.value; return;
            case StatType.PointsNature:    elementalPoints[ElementType.Nature]    += b.value; return;
            case StatType.PointsLightning: elementalPoints[ElementType.Lightning] += b.value; return;
            case StatType.PointsDarkness:  elementalPoints[ElementType.Darkness]  += b.value; return;
            case StatType.PointsLight:     elementalPoints[ElementType.Light]     += b.value; return;
            case StatType.PointsAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    elementalPoints[e] += b.value;
                return;
        }

        // Reste des stats (Défenses, BonusAttack, Dodge, Precision, MoveSpeed, Crit*, BonusHP/
        // Mana/Regen, AllDefense) — stockées en Flat pur pour l'instant (Task 4 ajoute le
        // vrai routage Flat/Percent via b.mode).
        flatAcc[b.statType] = flatAcc.TryGetValue(b.statType, out var f) ? f + b.value : b.value;
    }
```

par :

```csharp
    private void AccumulateStatBonuses(
        System.Collections.Generic.List<StatBonus> bonuses,
        Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc,
        Dictionary<ElementType, float> resist)
    {
        if (bonuses == null) return;
        foreach (var b in bonuses)
            AccumulateBonus(b, flatAcc, percentAcc, resist);
    }

    private void AccumulateBonus(
        StatBonus b,
        Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc,
        Dictionary<ElementType, float> resist)
    {
        switch (b.statType)
        {
            // Résistances élémentaires — sans plafond (GDD v3.5 §3.1), toujours additives
            case StatType.ResistFire:      resist[ElementType.Fire]      += b.value; return;
            case StatType.ResistWater:     resist[ElementType.Water]     += b.value; return;
            case StatType.ResistEarth:     resist[ElementType.Earth]     += b.value; return;
            case StatType.ResistNature:    resist[ElementType.Nature]    += b.value; return;
            case StatType.ResistLightning: resist[ElementType.Lightning] += b.value; return;
            case StatType.ResistDarkness:  resist[ElementType.Darkness]  += b.value; return;
            case StatType.ResistLight:     resist[ElementType.Light]     += b.value; return;
            case StatType.ResistAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    resist[e] += b.value;
                return;

            // Points élémentaires — inchangé, toujours additifs (spec §A)
            case StatType.PointsFire:      elementalPoints[ElementType.Fire]      += b.value; return;
            case StatType.PointsWater:     elementalPoints[ElementType.Water]     += b.value; return;
            case StatType.PointsEarth:     elementalPoints[ElementType.Earth]     += b.value; return;
            case StatType.PointsNature:    elementalPoints[ElementType.Nature]    += b.value; return;
            case StatType.PointsLightning: elementalPoints[ElementType.Lightning] += b.value; return;
            case StatType.PointsDarkness:  elementalPoints[ElementType.Darkness]  += b.value; return;
            case StatType.PointsLight:     elementalPoints[ElementType.Light]     += b.value; return;
            case StatType.PointsAll:
                foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                    elementalPoints[e] += b.value;
                return;

            // AllDefense — jamais stockée elle-même, répartie sur les 3 défenses réelles,
            // chacune calculera son propre résultat avec sa propre base (spec §A).
            case StatType.AllDefense:
                AddToAcc(flatAcc, percentAcc, StatType.MeleeDefense, b.mode, b.value);
                AddToAcc(flatAcc, percentAcc, StatType.RangedDefense, b.mode, b.value);
                AddToAcc(flatAcc, percentAcc, StatType.MagicDefense, b.mode, b.value);
                return;
        }

        AddToAcc(flatAcc, percentAcc, b.statType, b.mode, b.value);
    }

    /// <summary>Route une contribution dans la somme Flat ou % — force Flat pour les stats de
    /// `ExceptionStatTypes` quel que soit `mode` (filet de sécurité, le champ mode est de toute
    /// façon masqué côté Inspector pour elles).</summary>
    private static void AddToAcc(Dictionary<StatType, float> flatAcc,
        Dictionary<StatType, float> percentAcc, StatType stat, ModifierType mode, float value)
    {
        if (mode == ModifierType.Percent && !ExceptionStatTypes.Contains(stat))
            percentAcc[stat] = percentAcc.TryGetValue(stat, out var p) ? p + value : value;
        else
            flatAcc[stat] = flatAcc.TryGetValue(stat, out var f) ? f + value : value;
    }
```

- [ ] **Step 3 : Déclarer `percentAcc` dans `RecalculateStats`**

Juste après la ligne `var flatAcc = new Dictionary<StatType, float>();` (ajoutée Task 3),
ajouter :

```csharp
        var percentAcc = new Dictionary<StatType, float>();
```

- [ ] **Step 4 : Mettre à jour les 11 sites d'appel**

Chaque appel `AccumulateStatBonuses(X.Bonuses, flatAcc, accResist);` (forme issue de Task 3)
devient :

```csharp
            AccumulateStatBonuses(X.Bonuses, flatAcc, percentAcc, accResist);
```

(même 11 sites que Task 3 Step 3, même variantes de `X`/`.Bonuses`/`.bonuses`.)

- [ ] **Step 5 : Remplacer la section PUSH par la vraie formule `(Base+Flat)×(1+%)`**

Remplacer le bloc PUSH issu de Task 3 :

```csharp
        float FlatOf(StatType stat) => flatAcc.TryGetValue(stat, out var f) ? f : 0f;

        player.SetMaxHP       (baseHP        + accBonusHP        + FlatOf(StatType.BonusHP));
        player.SetMaxMana     (baseMana      + accBonusMana      + FlatOf(StatType.BonusMana));
        player.SetRegenHP     (baseRegenHP   + accBonusRegenHP   + FlatOf(StatType.BonusRegenHP));
        player.SetRegenMana   (baseRegenMana + accBonusRegenMana + FlatOf(StatType.BonusRegenMana));
        player.SetMoveSpeed   (baseMoveSpeed + accBonusMoveSpeed + FlatOf(StatType.MoveSpeed));

        player.SetAttackDamageMin(accAttackMin + FlatOf(StatType.BonusAttack));
        player.SetAttackDamageMax(accAttackMax + FlatOf(StatType.BonusAttack));
        player.SetPrecision      (accPrecision + FlatOf(StatType.Precision));
        player.SetCritChance     (accCritChance + FlatOf(StatType.CritChance));
        player.SetCritMultiplier (accCritMultiplier + FlatOf(StatType.CritMultiplier));

        player.SetMeleeDefense (accMeleeDefense + FlatOf(StatType.MeleeDefense) + FlatOf(StatType.AllDefense));
        player.SetRangedDefense(accRangedDefense + FlatOf(StatType.RangedDefense) + FlatOf(StatType.AllDefense));
        player.SetMagicDefense (accMagicDefense + FlatOf(StatType.MagicDefense) + FlatOf(StatType.AllDefense));
        player.SetDodge        (accDodge + FlatOf(StatType.Dodge));
        player.SetCritDamageReduction(accCritDmgReduct + FlatOf(StatType.CritDmgReduction));
```

par :

```csharp
        float FinalOf(StatType stat, float baseAndDirectFlat)
        {
            float flat = flatAcc.TryGetValue(stat, out var f) ? f : 0f;
            float pct  = percentAcc.TryGetValue(stat, out var p) ? p : 0f;
            return (baseAndDirectFlat + flat) * (1f + pct);
        }

        player.SetMaxHP       (FinalOf(StatType.BonusHP, baseHP + accBonusHP));
        player.SetMaxMana     (FinalOf(StatType.BonusMana, baseMana + accBonusMana));
        player.SetRegenHP     (FinalOf(StatType.BonusRegenHP, baseRegenHP + accBonusRegenHP));
        player.SetRegenMana   (FinalOf(StatType.BonusRegenMana, baseRegenMana + accBonusRegenMana));
        player.SetMoveSpeed   (FinalOf(StatType.MoveSpeed, baseMoveSpeed + accBonusMoveSpeed));

        player.SetAttackDamageMin(FinalOf(StatType.BonusAttack, accAttackMin));
        player.SetAttackDamageMax(FinalOf(StatType.BonusAttack, accAttackMax));
        player.SetPrecision      (FinalOf(StatType.Precision, accPrecision));
        player.SetCritChance     (FinalOf(StatType.CritChance, accCritChance));
        player.SetCritMultiplier (FinalOf(StatType.CritMultiplier, accCritMultiplier));

        player.SetMeleeDefense (FinalOf(StatType.MeleeDefense, accMeleeDefense));
        player.SetRangedDefense(FinalOf(StatType.RangedDefense, accRangedDefense));
        player.SetMagicDefense (FinalOf(StatType.MagicDefense, accMagicDefense));
        player.SetDodge        (FinalOf(StatType.Dodge, accDodge));
        player.SetCritDamageReduction(FinalOf(StatType.CritDmgReduction, accCritDmgReduct));
```

(Pour les stats "exception" — `CritChance`/`CritMultiplier`/`CritDmgReduction`/`MoveSpeed` —
`percentAcc` n'est jamais peuplé les concernant, donc `FinalOf` réduit mathématiquement à
`baseAndDirectFlat + flat`, une simple addition — pas besoin de branche séparée.)

- [ ] **Step 6 : Vérifier en Play Mode**

1. Équiper une arme avec un `StatBonus` `+20 MeleeDefense, mode=Flat` — vérifier fiche perso
   affiche `base+20`.
2. Changer ce même `StatBonus` en `mode=Percent, value=0.10` — vérifier fiche perso affiche
   `base×1.10`.
3. Équiper DEUX pièces avec chacune un `StatBonus` `+10% MeleeDefense` (Percent) — vérifier le
   résultat est `base×1.20` (addition des %, pas composition `×1.10×1.10`).
4. Configurer un `StatBonus` `AllDefense, +10%, Percent` — vérifier que Melee/Ranged/Magic
   Defense reçoivent CHACUNE +10% de LEUR PROPRE valeur de base (pas une valeur combinée).
5. Vérifier qu'une stat exception (ex: CritChance avec un `StatBonus` classique, valeur
   `0.05`) s'additionne toujours simplement, champ `mode` bien masqué dans l'Inspector pour
   cette entrée.

- [ ] **Step 7 : Commit**

```bash
git add Entities/CharacterStats.cs
git commit -m "feat: real Flat/Percent split in CharacterStats equipment pipeline

AccumulateBonus now routes each StatBonus into a Flat or Percent sum per
stat (forced Flat for exception stats regardless of mode), applied once
per stat via (Base+Flat)*(1+Percent) at push time. AllDefense fans out to
Melee/Ranged/MagicDefense at accumulation time, each computing its own
result against its own base."
```

---

### Task 5: Vérification manuelle finale (Play Mode)

**Files:** aucun — tâche de vérification uniquement.

**Interfaces:** aucune — consomme le comportement produit par Tasks 1-4.

- [ ] **Step 1 : Pipeline buff/debuff (Task 1)**

Configurer un `BuffData` avec `bonusStats` = `[{stat: MeleeDefense, mode: Flat, value: 20},
{stat: MeleeDefense, mode: Flat, value: 50}, {stat: MeleeDefense, mode: Percent, value: 0.10}]`.
Équiper une armure donnant 140 de défense mêlée de base. Appliquer le buff → vérifier sur la
fiche perso `(140+20+50)×1.10 = 231`.

- [ ] **Step 2 : Composition de % multiples (non-compounding)**

Appliquer DEUX buffs différents, chacun `+10% MeleeDefense` (Percent) — vérifier le résultat
est base`×1.20` (addition), pas base`×1.10×1.10 = ×1.21` (composition — l'ancien bug que
`pureBase` corrigeait déjà, doit rester corrigé après ce refactor).

- [ ] **Step 3 : Stat exception (buff)**

Appliquer un buff `bonusStats = [{stat: CritChance, mode: Flat, value: 0.05}]` — vérifier
`CritChance` s'additionne simplement (`+0.05`), et que le champ `mode` est masqué dans
l'Inspector pour cette ligne (`CritChance` fait partie de `ExceptionStats`).

- [ ] **Step 4 : Mécanisme `BuffType.Stats`/`DebuffType.Stats` (ancien "single stat boost")**

Configurer un `BuffData` avec `buffType = Stats`, `buffStatType = AttackDamage`,
`buffModifier = Percent`, `buffStatValue = 0.10`, ET un `bonusStats` séparé `[{stat:
AttackDamage, mode: Flat, value: 15}]` sur le MÊME buff — vérifier que les deux mécanismes
composent correctement dans la même somme globale (`(base+15)×1.10`), pas de double calcul ni
de valeur ignorée.

- [ ] **Step 5 : Pipeline équipement (Tasks 3-4)**

Reprendre les 5 vérifications du Step 6 de Task 4 si pas déjà fait pendant l'implémentation.

- [ ] **Step 6 : Ordre équipement → buff**

Équiper une armure donnant `+50 MeleeDefense` (StatBonus, Flat) → vérifier fiche perso à 190
(140+50, en reprenant l'exemple Step 1 avec une armure de base à 140). Appliquer ENSUITE le
buff du Step 1 par-dessus → vérifier le résultat final est `(190+20+50)×1.10 = 286` (le "Base"
que le buff utilise est bien la valeur DÉJÀ boostée par l'équipement, pas la valeur brute du
personnage seul — confirme l'ordre séquentiel équipement-puis-buff documenté dans Global
Constraints).

- [ ] **Step 7 : Assets existants — migration StatLineMode**

Si un `BuffData`/`DebuffData` existant a déjà une ligne `bonusStats` configurée avec l'ancien
`PercentOfFinal` (ordinal 2, retiré Task 1) : ouvrir son Inspector, vérifier la valeur affichée
pour `mode` (probablement vide/invalide) — la reconfigurer manuellement en `Percent` si
l'intention était un %. Les lignes en `PercentOfBase` (ordinal 1) sont automatiquement
réinterprétées `Percent` (même ordinal, pas d'action nécessaire).

- [ ] **Step 8 : Confirmer prêt pour Plan B**

Si tout ce qui précède est correct, Plan A est terminé — Plan B (dégâts finaux combat) peut
commencer, il dépend uniquement des 2 nouveaux accumulateurs `FinalDamageBonus`/
`FinalDamageReduction` sur `Entity` (hors scope de ce plan).
