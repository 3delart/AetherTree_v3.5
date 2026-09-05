# Plan B — Dégâts finaux au combat

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un mécanisme de dégâts finaux (bonus attaquant / réduction défenseur,
flat-puis-%, appliqué sur le NOMBRE de dégâts déjà calculé, pas sur une stat persistante) dans
`CombatSystem.CalculateDamage`/`CalculateMobDamage`, alimenté par le même système de buffs et de
bonus d'équipement que le Plan A.

**Architecture:** 4 nouveaux accumulateurs simples sur `Entity` (`FinalDamageBonusFlat/Percent`,
`FinalDamageReductionFlat/Percent`) — PAS la formule `(Base+Flat)×(1+%)` du Plan A (pas de "stat
de base" à multiplier ici, ce sont des accumulateurs bruts comme `XPBonus`/`GoldBonus`). Une
seule entrée d'enum par côté (`FinalDamageBonus`, `FinalDamageReduction`) dont le MODE choisi
(Flat/Percent) route la contribution vers l'UN OU L'AUTRE des deux accumulateurs Entity — un
3ème type de traitement, ni "stat normale" ni "stat exception" du Plan A, câblé en cas spécial
dans les deux pipelines. `CombatSystem` applique la formule sur le nombre de dégâts, en tout
dernier, après tout le reste du pipeline existant.

**Tech Stack:** Unity C#, pas de framework de test automatisé (vérification manuelle Play Mode).

**Spec:** `docs/superpowers/specs/2026-09-05-unified-stat-modifiers-and-armor-type-design.md`
(section B — dépend de la section A, déjà implémentée : Plan A, commits `358b7ab..b8c3b0e`,
déjà mergé sur master).

## Global Constraints

- **Formule combat** (attaquant puis défenseur, appliquée à la toute fin du pipeline de
  dégâts existant, après Mark/AttackUp, avant le `Mathf.Max(1f, ...)` final) :
  ```
  totalDamage = (totalDamage + attacker.FinalDamageBonusFlat) × (1 + attacker.FinalDamageBonusPercent)
  totalDamage = (totalDamage - target.FinalDamageReductionFlat) × (1 - target.FinalDamageReductionPercent)
  ```
- **PAS la formule du Plan A** : `FinalDamageBonus`/`FinalDamageReduction` sont des
  accumulateurs BRUTS — le mode Flat/Percent choisi sur une ligne route sa valeur vers l'un des
  deux champs Entity séparés (`...Flat` ou `...Percent`), jamais combinés via
  `(Base+Flat)×(1+Percent)`. Ne PAS les ajouter à `ExceptionStats`/`ExceptionStatTypes` (ni
  Plan A, ni ce plan) — traitement dédié en code, pas une liste.
- **Ordinal safety** : `FinalDamageBonus`/`FinalDamageReduction` s'ajoutent en fin des deux
  enums (`StatModifierType` et `StatType`), APRÈS `AllDefense` (dernière entrée actuelle des
  deux, confirmé par lecture directe des fichiers).
- **Pattern accumulateur Entity** — répliquer EXACTEMENT le pattern `XPBonusPercent`/
  `GoldBonusPercent` déjà en place (`Entities/Entity.cs`) : champ protégé initialisé à `0f`,
  propriété publique en lecture seule, setter clampé `Mathf.Max(0f, value)`, entrée dans le
  struct `BaseStats` toujours à `0f` (pas de source "base", uniquement additif), restauration
  dans `RequestRecalculate()` avant `ReapplyActiveModifiers()`.
- Pas de tests automatisés — vérification manuelle Play Mode (Task 4).

---

### Task 1: 4 accumulateurs sur Entity

**Files:**
- Modify: `Entities/Entity.cs`

**Interfaces:**
- Produces: `Entity.FinalDamageBonusFlat`/`FinalDamageBonusPercent`/
  `FinalDamageReductionFlat`/`FinalDamageReductionPercent` (propriétés publiques `float`),
  `SetFinalDamageBonusFlat(float)`/`SetFinalDamageBonusPercent(float)`/
  `SetFinalDamageReductionFlat(float)`/`SetFinalDamageReductionPercent(float)` (setters publics,
  clampés `Mathf.Max(0f, value)`) — consommées par Task 2 (pipeline buff) et Task 3 (pipeline
  équipement). `CombatSystem` (Task 4) lit les 4 propriétés directement.

- [ ] **Step 1 : Ajouter les 4 champs protégés**

Dans `Entities/Entity.cs`, juste après la déclaration existante (trouvée par recherche du texte
`protected float goldBonusPercent = 0f;`) :

```csharp
    protected float xpBonusPercent   = 0f;
    protected float goldBonusPercent = 0f;
```

ajouter juste en dessous :

```csharp
    /// <summary>Dégâts finaux — accumulateurs bruts (pas de "stat de base" à multiplier),
    /// alimentés par StatModifierType.FinalDamageBonus/FinalDamageReduction (buffs) et
    /// StatType.FinalDamageBonus/FinalDamageReduction (équipement). Appliqués par
    /// CombatSystem.CalculateDamage/CalculateMobDamage, tout à la fin du pipeline de dégâts,
    /// PAS via la formule (Base+Flat)×(1+%) du Plan A. Toujours 0f par défaut — pas de source
    /// permanente aujourd'hui, uniquement buffs/équipement additifs.</summary>
    protected float finalDamageBonusFlat        = 0f;
    protected float finalDamageBonusPercent     = 0f;
    protected float finalDamageReductionFlat    = 0f;
    protected float finalDamageReductionPercent = 0f;
```

- [ ] **Step 2 : Ajouter les 4 propriétés publiques**

Juste après la déclaration existante trouvée par recherche du texte
`public float GoldBonusPercent => goldBonusPercent;` :

```csharp
    public float XPBonusPercent   => xpBonusPercent;
    public float GoldBonusPercent => goldBonusPercent;
```

ajouter juste en dessous :

```csharp
    public float FinalDamageBonusFlat        => finalDamageBonusFlat;
    public float FinalDamageBonusPercent     => finalDamageBonusPercent;
    public float FinalDamageReductionFlat    => finalDamageReductionFlat;
    public float FinalDamageReductionPercent => finalDamageReductionPercent;
```

- [ ] **Step 3 : Ajouter les 4 champs au struct `BaseStats` + `SnapshotBaseStats()`**

`BaseStats` est un `private struct` nommé déclaré dans `Entity.cs` (juste avant le champ
`private BaseStats _base;`) :

```csharp
    private struct BaseStats
    {
        public float attackDamageMin, attackDamageMax;
        public float meleeDefense, rangedDefense, magicDefense;
        public float dodge, precision;
        public float critChance, critDamage, critDamageReduction;
        public float maxHP, maxMana;
        public float regenHP, regenMana;
        public float moveSpeed;
        public float xpBonusPercent, goldBonusPercent;
    }
```

Remplacer sa dernière ligne de champs :

```csharp
        public float xpBonusPercent, goldBonusPercent;
    }
```

par :

```csharp
        public float xpBonusPercent, goldBonusPercent;
        public float finalDamageBonusFlat, finalDamageBonusPercent;
        public float finalDamageReductionFlat, finalDamageReductionPercent;
    }
```

Puis dans `SnapshotBaseStats()`, remplacer :

```csharp
            xpBonusPercent  = 0f, // pas de base — uniquement additif via talismans
            goldBonusPercent = 0f,
        };
```

par :

```csharp
            xpBonusPercent  = 0f, // pas de base — uniquement additif via talismans
            goldBonusPercent = 0f,
            finalDamageBonusFlat        = 0f, // pas de base — uniquement additif via buffs/équipement
            finalDamageBonusPercent     = 0f,
            finalDamageReductionFlat    = 0f,
            finalDamageReductionPercent = 0f,
        };
```

- [ ] **Step 4 : Restaurer les 4 valeurs dans `RequestRecalculate()`**

Remplacer :

```csharp
        SetXPBonusPercent  (_base.xpBonusPercent);
        SetGoldBonusPercent(_base.goldBonusPercent);
```

par :

```csharp
        SetXPBonusPercent  (_base.xpBonusPercent);
        SetGoldBonusPercent(_base.goldBonusPercent);
        SetFinalDamageBonusFlat       (_base.finalDamageBonusFlat);
        SetFinalDamageBonusPercent    (_base.finalDamageBonusPercent);
        SetFinalDamageReductionFlat   (_base.finalDamageReductionFlat);
        SetFinalDamageReductionPercent(_base.finalDamageReductionPercent);
```

- [ ] **Step 5 : Ajouter les 4 setters**

Juste après la déclaration existante trouvée par recherche du texte
`public void SetGoldBonusPercent(float value) => goldBonusPercent = Mathf.Max(0f, value);` :

```csharp
    public void SetXPBonusPercent(float value)   => xpBonusPercent   = Mathf.Max(0f, value);
    public void SetGoldBonusPercent(float value) => goldBonusPercent = Mathf.Max(0f, value);
```

ajouter juste en dessous :

```csharp
    public void SetFinalDamageBonusFlat(float value)        => finalDamageBonusFlat        = Mathf.Max(0f, value);
    public void SetFinalDamageBonusPercent(float value)     => finalDamageBonusPercent     = Mathf.Max(0f, value);
    public void SetFinalDamageReductionFlat(float value)    => finalDamageReductionFlat    = Mathf.Max(0f, value);
    public void SetFinalDamageReductionPercent(float value) => finalDamageReductionPercent = Mathf.Max(0f, value);
```

- [ ] **Step 6 : Vérifier la compilation**

Unity recompile, Console : 0 erreur. Ces 4 accumulateurs ne sont encore alimentés par rien
(Task 2/3) ni lus par rien (Task 4) — code mort mais inerte, sans effet sur le jeu existant.

- [ ] **Step 7 : Commit**

```bash
git add Entities/Entity.cs
git commit -m "feat: add FinalDamageBonus/Reduction Flat+Percent accumulators to Entity

Same pattern as XPBonusPercent/GoldBonusPercent (protected field, public
readonly property, clamped setter, reset-to-zero in BaseStats/
RequestRecalculate). Inert until Tasks 2-4 wire them up — no equipment or
buff source feeds them yet, and CombatSystem doesn't read them yet."
```

---

### Task 2: Pipeline buff — StatModifierType + case spécial dans ReapplyActiveModifiers

**Files:**
- Modify: `Data/StatusEffect/StatusEffectData.cs`
- Modify: `Entities/StatusEffectSystem.cs`

**Interfaces:**
- Consumes: `Entity.SetFinalDamageBonusFlat/Percent`, `SetFinalDamageReductionFlat/Percent`
  (Task 1).
- Produces: `StatModifierType.FinalDamageBonus`/`FinalDamageReduction` (nouvelles valeurs
  d'enum) — consommées par les designers via `StatLine`/`bonusStats` sur `BuffData`/`DebuffData`,
  et par Task 4 (vérification).

- [ ] **Step 1 : Ajouter les 2 valeurs d'enum**

Dans `Data/StatusEffect/StatusEffectData.cs`, remplacer :

```csharp
    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    AllDefense, // Écrit simultanément sur MeleeDefense + RangedDefense + MagicDefense — jamais
                // stockée comme cible finale elle-même, toujours répartie au moment de
                // l'accumulation. Voir StatusEffectSystem.AccumulateStatLine.
}
```

par :

```csharp
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
}
```

- [ ] **Step 2 : Ajouter les 2 nouvelles valeurs au `[ShowIf]` de `StatLine.mode`**

Remplacer :

```csharp
    [ShowIf(nameof(stat),
        StatModifierType.MaxHP, StatModifierType.MaxMana,
        StatModifierType.RegenHP, StatModifierType.RegenMana,
        StatModifierType.AttackDamage, StatModifierType.AttackSpeed,
        StatModifierType.MeleeDefense, StatModifierType.RangedDefense, StatModifierType.MagicDefense,
        StatModifierType.AllDefense, StatModifierType.Dodge, StatModifierType.Precision)]
    public StatLineMode mode = StatLineMode.Flat;
```

par :

```csharp
    [ShowIf(nameof(stat),
        StatModifierType.MaxHP, StatModifierType.MaxMana,
        StatModifierType.RegenHP, StatModifierType.RegenMana,
        StatModifierType.AttackDamage, StatModifierType.AttackSpeed,
        StatModifierType.MeleeDefense, StatModifierType.RangedDefense, StatModifierType.MagicDefense,
        StatModifierType.AllDefense, StatModifierType.Dodge, StatModifierType.Precision,
        StatModifierType.FinalDamageBonus, StatModifierType.FinalDamageReduction)]
    public StatLineMode mode = StatLineMode.Flat;
```

- [ ] **Step 3 : Cas spécial dans la boucle d'application finale de `ReapplyActiveModifiers`**

Dans `Entities/StatusEffectSystem.cs`, trouver ce bloc (section `// ── Application finale — une
fois par stat ─────────────`) :

```csharp
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
```

Remplacer par :

```csharp
        foreach (StatModifierType stat in System.Enum.GetValues(typeof(StatModifierType)))
        {
            float flat = flatSum.TryGetValue(stat, out var f) ? f : 0f;
            float pct  = percentSum.TryGetValue(stat, out var p) ? p : 0f;
            if (flat == 0f && pct == 0f) continue;

            // FinalDamageBonus/Reduction — accumulateurs BRUTS, pas la formule (Base+Flat)×
            // (1+%) : le mode choisi route déjà flat/pct vers le bon accumulateur Entity, rien
            // à multiplier, rien à passer par ModifyEntityStat (qui n'a pas de case pour eux).
            if (stat == StatModifierType.FinalDamageBonus)
            {
                target.SetFinalDamageBonusFlat(flat);
                target.SetFinalDamageBonusPercent(pct);
                continue;
            }
            if (stat == StatModifierType.FinalDamageReduction)
            {
                target.SetFinalDamageReductionFlat(flat);
                target.SetFinalDamageReductionPercent(pct);
                continue;
            }

            float baseVal = pureBase[stat];
            float final   = (baseVal + flat) * (1f + pct);
            ModifyEntityStat(target, stat, final - baseVal);
        }
    }
```

(`SetFinalDamageBonusFlat(flat)` écrase plutôt qu'additionne — correct ici car `target` a déjà
été remis à `0f` par `RequestRecalculate()` juste avant l'appel à cette méthode, et `flat`/`pct`
sont déjà les sommes COMPLÈTES de tous les buffs/debuffs actifs ce cycle-ci, pas des deltas
incrémentaux.)

Ne PAS ajouter `FinalDamageBonus`/`FinalDamageReduction` à `ExceptionStats` — ce ne sont ni des
stats "normales" ni des stats "exception" du Plan A, elles ont leur propre branchement dédié
ci-dessus. Ne pas toucher `GetBaseStatValue`/`ModifyEntityStat` — aucun nouveau `case`
nécessaire (jamais transmises à ces méthodes).

- [ ] **Step 4 : Vérifier la compilation**

Unity recompile, Console : 0 erreur.

- [ ] **Step 5 : Commit**

```bash
git add Data/StatusEffect/StatusEffectData.cs Entities/StatusEffectSystem.cs
git commit -m "feat: wire FinalDamageBonus/Reduction into buff pipeline

New StatModifierType.FinalDamageBonus/FinalDamageReduction, ShowIf-visible
on StatLine.mode. Special-cased in ReapplyActiveModifiers's final
application loop — routes flat/percent sums directly to Entity's raw
accumulators instead of the Plan A (Base+Flat)*(1+Percent) formula, since
there's no base stat to multiply here."
```

---

### Task 3: Pipeline équipement — StatType + push direct dans CharacterStats

**Files:**
- Modify: `Data/Equipment/StatBonus.cs`
- Modify: `Entities/CharacterStats.cs`

**Interfaces:**
- Consumes: `Entity.SetFinalDamageBonusFlat/Percent`, `SetFinalDamageReductionFlat/Percent`
  (Task 1). `AccumulateBonus`/`AddToAcc` (Plan A, déjà en place, AUCUN changement nécessaire —
  routent déjà correctement `StatType.FinalDamageBonus`/`FinalDamageReduction` en Flat/%
  pourvu qu'ils ne soient PAS dans `ExceptionStatTypes`, ce que cette tâche respecte).
- Produces: `StatType.FinalDamageBonus`/`FinalDamageReduction` — consommables via
  `StatBonus`/`config.bonuses` sur n'importe quel équipement.

- [ ] **Step 1 : Ajouter les 2 valeurs d'enum**

Dans `Data/Equipment/StatBonus.cs`, remplacer :

```csharp
    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    [InspectorName("All Defense (flat ou %, voir mode)")] AllDefense,
}
```

par :

```csharp
    // Ajouté après coup — TOUJOURS en fin d'enum (ordinal safety).
    [InspectorName("All Defense (flat ou %, voir mode)")] AllDefense,

    // Ajoutés après coup — TOUJOURS en fin d'enum (ordinal safety). Accumulateurs BRUTS — PAS
    // la formule (Base+Flat)×(1+%) du reste de cet enum. Voir CharacterStats.RecalculateStats,
    // section PUSH — poussés directement depuis flatAcc/percentAcc, pas via FinalOf(). Appliqués
    // sur le NOMBRE de dégâts par CombatSystem, pas une stat persistante.
    [InspectorName("Final Damage Bonus (attaquant, flat ou %, voir mode)")]     FinalDamageBonus,
    [InspectorName("Final Damage Reduction (défenseur, flat ou %, voir mode)")] FinalDamageReduction,
}
```

- [ ] **Step 2 : Ajouter les 2 nouvelles valeurs au `[ShowIf]` de `StatBonus.mode`**

Remplacer :

```csharp
    [ShowIf(nameof(statType),
        StatType.MeleeDefense, StatType.RangedDefense, StatType.MagicDefense, StatType.AllDefense,
        StatType.BonusAttack, StatType.Dodge, StatType.Precision,
        StatType.BonusHP, StatType.BonusMana, StatType.BonusRegenHP, StatType.BonusRegenMana)]
    public ModifierType mode = ModifierType.Flat;
```

par :

```csharp
    [ShowIf(nameof(statType),
        StatType.MeleeDefense, StatType.RangedDefense, StatType.MagicDefense, StatType.AllDefense,
        StatType.BonusAttack, StatType.Dodge, StatType.Precision,
        StatType.BonusHP, StatType.BonusMana, StatType.BonusRegenHP, StatType.BonusRegenMana,
        StatType.FinalDamageBonus, StatType.FinalDamageReduction)]
    public ModifierType mode = ModifierType.Flat;
```

- [ ] **Step 3 : Push direct dans `RecalculateStats` — PAS via `FinalOf`**

Dans `Entities/CharacterStats.cs`, trouver le bloc PUSH (juste après la déclaration du local
function `FinalOf`) :

```csharp
        player.SetMeleeDefense (FinalOf(StatType.MeleeDefense, accMeleeDefense));
        player.SetRangedDefense(FinalOf(StatType.RangedDefense, accRangedDefense));
        player.SetMagicDefense (FinalOf(StatType.MagicDefense, accMagicDefense));
        player.SetDodge        (FinalOf(StatType.Dodge, accDodge));
        player.SetCritDamageReduction(FinalOf(StatType.CritDmgReduction, accCritDmgReduct));
```

Juste en dessous, ajouter (PAS via `FinalOf` — lecture directe de `flatAcc`/`percentAcc`, ce
sont des accumulateurs bruts sans "base" à multiplier) :

```csharp
        player.SetFinalDamageBonusFlat(
            flatAcc.TryGetValue(StatType.FinalDamageBonus, out var fdbF) ? fdbF : 0f);
        player.SetFinalDamageBonusPercent(
            percentAcc.TryGetValue(StatType.FinalDamageBonus, out var fdbP) ? fdbP : 0f);
        player.SetFinalDamageReductionFlat(
            flatAcc.TryGetValue(StatType.FinalDamageReduction, out var fdrF) ? fdrF : 0f);
        player.SetFinalDamageReductionPercent(
            percentAcc.TryGetValue(StatType.FinalDamageReduction, out var fdrP) ? fdrP : 0f);
```

Ne PAS ajouter `FinalDamageBonus`/`FinalDamageReduction` à `ExceptionStatTypes` — même raison
que Task 2 côté buff, ce sont des accumulateurs bruts, pas des stats "exception" flat-only.

- [ ] **Step 4 : Vérifier la compilation**

Unity recompile, Console : 0 erreur.

- [ ] **Step 5 : Commit**

```bash
git add Data/Equipment/StatBonus.cs Entities/CharacterStats.cs
git commit -m "feat: wire FinalDamageBonus/Reduction into equipment pipeline

New StatType.FinalDamageBonus/FinalDamageReduction, ShowIf-visible on
StatBonus.mode. Pushed directly from flatAcc/percentAcc in
RecalculateStats's PUSH section — bypasses FinalOf's (Base+Flat)*(1+%)
formula since there's no base stat to multiply, same treatment as the
buff pipeline's ReapplyActiveModifiers special case."
```

---

### Task 4: CombatSystem — appliquer la formule + vérification manuelle

**Files:**
- Modify: `Combat/CombatSystem.cs`

**Interfaces:**
- Consumes: `Entity.FinalDamageBonusFlat/Percent`, `FinalDamageReductionFlat/Percent`
  (Task 1, alimentées par Tasks 2-3).

- [ ] **Step 1 : Ajouter la formule dans `CalculateDamage`**

Dans `Combat/CombatSystem.cs`, `CalculateDamage`, remplacer :

```csharp
        // ── 8. AttackUp buff ──────────────────────────────────
        if (attacker?.statusEffects != null)
            totalDamage += attacker.statusEffects.GetBuffAttackBonus();

        // ── Résistance pour le log ────────────────────────────
```

par :

```csharp
        // ── 8. AttackUp buff ──────────────────────────────────
        if (attacker?.statusEffects != null)
            totalDamage += attacker.statusEffects.GetBuffAttackBonus();

        // ── 9. Dégâts finaux — bonus attaquant puis réduction défenseur ──
        // Mécanisme séparé de la partie stat (Plan A) — appliqué sur le NOMBRE de dégâts déjà
        // calculé, en tout dernier, avant le clamp minimum. Symétrique : flat puis % pour
        // chaque côté. GDD — spec unified-stat-modifiers §B.
        if (attacker != null)
            totalDamage = (totalDamage + attacker.FinalDamageBonusFlat) * (1f + attacker.FinalDamageBonusPercent);
        if (target != null)
            totalDamage = (totalDamage - target.FinalDamageReductionFlat) * (1f - target.FinalDamageReductionPercent);

        // ── Résistance pour le log ────────────────────────────
```

- [ ] **Step 2 : Ajouter la formule dans `CalculateMobDamage`**

Dans la même classe, `CalculateMobDamage(SkillData skill, Entity caster, Entity target)`,
remplacer :

```csharp
        float total = physDamage + elemDamage;

        if (target?.statusEffects != null && target.statusEffects.isMarked)
            total *= (1f + target.statusEffects.GetMarkDamageBonus());

        //LogDamageReport("MOB/PNJ", caster, target, null, skill, baseDamage, physDamage, elemRaw, elemDamage, 0f,total, false);

        return Mathf.Max(1f, total);
```

par :

```csharp
        float total = physDamage + elemDamage;

        if (target?.statusEffects != null && target.statusEffects.isMarked)
            total *= (1f + target.statusEffects.GetMarkDamageBonus());

        // ── Dégâts finaux — bonus attaquant puis réduction défenseur ──
        // Même mécanisme que CalculateDamage (joueur) — voir ce commentaire là-bas.
        if (caster != null)
            total = (total + caster.FinalDamageBonusFlat) * (1f + caster.FinalDamageBonusPercent);
        if (target != null)
            total = (total - target.FinalDamageReductionFlat) * (1f - target.FinalDamageReductionPercent);

        //LogDamageReport("MOB/PNJ", caster, target, null, skill, baseDamage, physDamage, elemRaw, elemDamage, 0f,total, false);

        return Mathf.Max(1f, total);
```

(`caster` ici est déjà de type `Entity` dans cette surcharge — le wrapper
`CalculateMobDamage(SkillData skill, Mob caster, Entity target)` délègue à celle-ci via un cast,
donc cette seule modification couvre les deux points d'entrée.)

- [ ] **Step 3 : Vérifier la compilation**

Unity recompile, Console : 0 erreur.

- [ ] **Step 4 : Commit**

```bash
git add Combat/CombatSystem.cs
git commit -m "feat: apply final damage bonus/reduction in CombatSystem

Last step of both damage pipelines (player and mob/PNJ) — attacker's
flat-then-percent bonus, then target's flat-then-percent reduction,
applied to the already-computed damage number, before the final
Mathf.Max(1f, ...) clamp. Symmetric with the equipment/buff accumulators
wired in Tasks 1-3."
```

- [ ] **Step 5 : Vérification manuelle Play Mode**

1. Configurer un `BuffData` avec `bonusStats = [{stat: FinalDamageBonus, mode: Flat, value:
   100}, {stat: FinalDamageBonus, mode: Percent, value: 0.05}]`, l'appliquer sur l'attaquant.
   Configurer un second `BuffData` (ou `DebuffData`) avec `bonusStats = [{stat:
   FinalDamageReduction, mode: Flat, value: 50}, {stat: FinalDamageReduction, mode: Percent,
   value: 0.05}]`, l'appliquer sur la cible. Lancer une attaque dont on connaît le total AVANT
   ce mécanisme (ex: noter les dégâts affichés sans les 2 buffs actifs), puis vérifier avec les
   2 buffs actifs : `dégâts_avec_buffs = (dégâts_sans_buffs + 100) × 1.05`, puis
   `résultat_final = (dégâts_avec_buffs - 50) × 0.95`. Comparer au nombre réellement infligé
   (Debug log `debugDamage = true` sur `CombatSystem` si besoin d'un chiffre exact).
2. Retirer les 2 buffs → vérifier que les dégâts reviennent exactement à la valeur de référence
   notée à l'étape 1 (les 4 accumulateurs retombent bien à `0f`, pas de résidu).
3. Équiper un item avec `StatBonus{FinalDamageReduction, Flat, 30}` (équipement, pas buff) sur
   la cible → vérifier que la réduction s'applique aussi (confirme le pipeline équipement,
   Task 3, indépendamment du pipeline buff).
4. Vérifier qu'un `StatBonus`/`StatLine` visant `FinalDamageBonus`/`FinalDamageReduction`
   affiche bien le sélecteur `mode` (Flat/Percent) dans l'Inspector, ni masqué ni manquant.
5. Cas limite : réduction cumulée supérieure aux dégâts bruts (ex: `FinalDamageReductionFlat`
   très élevé) → vérifier que `Mathf.Max(1f, totalDamage)` empêche bien un résultat négatif ou
   nul (dégâts minimum garantis à 1, comportement déjà existant, doit rester vrai après ce
   changement).
