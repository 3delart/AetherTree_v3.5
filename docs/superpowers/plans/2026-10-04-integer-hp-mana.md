# HP/Mana entiers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Garantir que toute valeur FINALE de HP/Mana (`currentHP`, `currentMana`, `maxHP`,
`maxMana`) appliquée sur n'importe quelle entité (Player/PNJ/Mob) soit toujours un nombre entier,
sans jamais perdre silencieusement les petits montants fractionnaires continus (régénération
passive faible, DoT à faible DPS) qu'un arrondi naïf détruirait à chaque frame.

**Architecture:** Deux mécanismes complémentaires, tous deux dans des méthodes déjà existantes
(aucune signature publique ne change) :
1. Les 7 méthodes de mutation ponctuelle d'`Entity.cs` (`TakeDamage`/`Heal`/`SpendMana`/
   `SpendHP`/`RecoverMana`/`SetMaxHP`/`SetMaxMana`) arrondissent leur montant reçu avec
   `Mathf.Round` avant de l'appliquer.
2. Les 5 sources qui appliquent un flux CONTINU (régénération passive 1×/sec, DoT/ManaDrain/
   HpDrain/HoT appliqués chaque frame via `taux × deltaTime`) passent par un nouveau struct
   réutilisable `FractionalAccumulator`, qui garde le reliquat fractionnaire d'une frame à
   l'autre et ne déclenche une application réelle (toujours entière) qu'une fois qu'au moins un
   point entier s'est accumulé.

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification manuelle Play Mode
uniquement, chaque tâche ci-dessous détaille précisément quoi observer.

**Spec:** `docs/superpowers/specs/2026-10-04-integer-hp-mana-design.md`

## Global Constraints

- Aucune signature publique ne change — tous les paramètres `amount`/`value` restent `float`,
  seule leur application interne devient "arrondie avant d'être appliquée".
- Les CALCULS intermédiaires (multiplicateurs de crit/armure, pourcentages, formules de dégâts)
  restent en `float` partout — seul le moment où un résultat touche réellement `currentHP`/
  `currentMana`/`maxHP`/`maxMana` est concerné.
- `Mathf.Round` (pas `Ceil`/`Floor`) pour les montants ponctuels — un arrondi qui pourrait biaiser
  systématiquement vers le haut ou le bas sur des milliers de coups fausserait l'équilibrage.
- `Mathf.FloorToInt` (pas `Round`) à l'intérieur de `FractionalAccumulator.ExtractWhole` — un
  reliquat de 0.5 n'a pas encore atteint un point entier, il doit continuer à s'accumuler.
- Pas de framework de test automatisé dans ce projet — chaque tâche se termine par une procédure
  manuelle précise (valeurs exactes à régler, ce qu'observer), jamais "ajouter des tests".
- Commentaires en français, expliquent le POURQUOI (contrainte non-évidente), jamais le QUOI.
- Commits directs sur `master` — convention établie de ce repo, pas de branche de feature.

## Review Focus

- **Un `FractionalAccumulator` qui reçoit un montant déjà entier ne doit rien casser.** Un appel
  `Heal(whole)` depuis un accumulateur arrive déjà entier dans `Heal()`, qui l'arrondit à nouveau
  via `Mathf.Round` — doit être un no-op total (`Mathf.Round(5f) == 5f`), jamais un double-arrondi
  qui déplacerait la valeur. Tâche 2 doit le confirmer explicitement, pas juste le supposer.
- **`SetMaxHP`/`SetMaxMana` arrondis peuvent resserrer le clamp de `currentHP`/`currentMana`
  existant.** Si `maxHP` diminue (ex: debuff `-X% Max HP`) et que `value` arrondi tombe
  légèrement différemment de l'ancien comportement non-arrondi, `Mathf.Clamp(currentHP, 0f,
  maxHP)` doit rester cohérent — une entité avec 50 HP dont le max tombe à 45.4 (arrondi à 45)
  doit immédiatement se retrouver clampée à 45, pas y rester bloquée à une valeur fractionnaire
  intermédiaire. Tâche 2 doit tester explicitement ce cas de réduction de max.
- **Le reliquat de `_regenHPAccum`/`_regenManaAccum` (champs sur `Entity`, PAS sur un
  `StatusEffectInstance` détruit à la mort) survit à la mort/respawn de l'entité.** Contrairement
  aux accumulateurs de `DebuffInstance`/`BuffInstance` (détruits avec l'effet de statut au moment
  où `Die()` appelle `statusEffects.ClearAllEffects()`), les deux champs sur `Entity` ne sont
  jamais réinitialisés. Comportement assumé et documenté dans la spec : un reliquat qui survit ne
  fait que réduire légèrement le temps avant le PROCHAIN point entier de regen après respawn —
  aucune perte ni gain d'HP/Mana n'en découle, ce n'est pas un bug. Tâche 3 doit le vérifier
  explicitement (pas juste l'assumer) pour confirmer qu'aucun écart réel n'apparaît.
- **Une résurrection (`Revive()`) à un pourcentage qui, multiplié par un `maxHP` déjà entier,
  tombe pile sur un `.5`** (ex: 30% de 15 = 4.5) doit être tranchée par la même règle
  `Mathf.Round` que partout ailleurs (convention C# `MidpointRounding.ToEven` par défaut, donc 4.5
  → 4, 5.5 → 6) — pas une règle différente juste parce que c'est un cas ponctuel rare. Tâche 4
  doit le couvrir avec un `maxHP`/pourcentage qui tombe exactement sur ce cas.
- **Un DoT/ManaDrain/HpDrain à DPS très faible (< 1/sec) ne doit PAS silencieusement cesser de
  fonctionner.** C'est le bug que ce chantier corrige — mais la tâche qui implémente
  `DebuffInstance` doit elle-même observer, en jeu, qu'un DoT à 0.3/sec continue d'appliquer des
  dégâts avec un léger délai plutôt que de rester à 0 pour toujours, pas seulement faire confiance
  au design de `FractionalAccumulator` sans le voir tourner.

---

### Task 1: `Utils/FractionalAccumulator.cs`

**Files:**
- Create: `Utils/FractionalAccumulator.cs`

**Interfaces:**
- Consumes: rien — struct autonome, aucune dépendance sur le reste du projet.
- Produces: `public struct FractionalAccumulator { public int ExtractWhole(float delta); }` —
  consommé par les Tâches 3, 5 et 6.

- [ ] **Step 1: Créer le fichier**

```csharp
using UnityEngine;

// =============================================================
// FRACTIONALACCUMULATOR.CS — Convertit un flux continu de montants fractionnaires
// (régénération/DoT calculés en taux × deltaTime) en incréments ENTIERS.
// Path : Assets/Scripts/Utils/FractionalAccumulator.cs
//
// Sans ça, un DoT de 10 dégâts/sec à 60 FPS vaut 0.16 dégât/frame — arrondir ce montant
// directement à chaque frame donnerait 0 à CHAQUE frame, pour toujours (DoT silencieusement
// cassé, pas juste imprécis). Ce struct garde le reliquat fractionnaire d'une frame à l'autre
// au lieu de le perdre, et ne rend un entier que lorsqu'au moins un point complet s'est
// accumulé. Voir docs/superpowers/specs/2026-10-04-integer-hp-mana-design.md.
// =============================================================
public struct FractionalAccumulator
{
    private float _remainder;

    /// <summary>Ajoute `delta` (peut être fractionnaire) au reliquat et retourne la partie
    /// ENTIÈRE accumulée depuis la dernière extraction (0 si rien n'a encore atteint un point
    /// entier). Le reliquat restant est conservé pour le prochain appel, jamais perdu.
    /// FloorToInt (pas Round) : un reliquat de 0.5 n'a pas encore atteint un point entier, il
    /// doit continuer à s'accumuler plutôt que d'être arrondi prématurément vers le haut.</summary>
    public int ExtractWhole(float delta)
    {
        _remainder += delta;
        int whole = Mathf.FloorToInt(_remainder);
        if (whole > 0) _remainder -= whole;
        return whole;
    }
}
```

- [ ] **Step 2: Compile check**

Ouvrir Unity Editor, laisser recompiler, Console : 0 erreur. (Pas de framework de test
automatisé dans ce projet — c'est l'étape de vérification pour cette tâche.)

- [ ] **Step 3: Commit**

```bash
git add Utils/FractionalAccumulator.cs
git commit -m "feat: add FractionalAccumulator struct for continuous HP/mana trickle"
```

---

### Task 2: `Entity.cs` — arrondi des 7 méthodes de mutation ponctuelle

**Files:**
- Modify: `Entities/Entity.cs:486-529` (`TakeDamage`)
- Modify: `Entities/Entity.cs:659-674` (`Heal`)
- Modify: `Entities/Entity.cs:680-699` (`SpendMana`/`SpendHP`/`RecoverMana`)
- Modify: `Entities/Entity.cs:820-830` (`SetMaxHP`/`SetMaxMana`)

**Interfaces:**
- Consumes: rien de nouveau — modifie des méthodes déjà publiques existantes, mêmes signatures
  exactes.
- Produces: garantit qu'après cette tâche, TOUT appelant de `TakeDamage`/`Heal`/`SpendMana`/
  `SpendHP`/`RecoverMana`/`SetMaxHP`/`SetMaxMana` (y compris `SkillSystem.cs`, `SkillBar.cs`,
  `CombatAIController.cs`, aucun desquels n'est modifié par ce plan) obtient un `currentHP`/
  `currentMana`/`maxHP`/`maxMana` entier en sortie — consommé implicitement par les Tâches 3, 5,
  6 (qui appellent `Heal`/`RecoverMana`/`TakeDamage`/`SpendMana` avec des valeurs déjà entières
  extraites d'un `FractionalAccumulator`, et comptent sur le fait que `Mathf.Round` d'un entier
  est un no-op).

- [ ] **Step 1: Arrondir `TakeDamage` — APRÈS l'absorption de Shield, pas avant**

Trouver (dans `Entities/Entity.cs`) :

```csharp
        // Shield — absorbe les dégâts en priorité
        if (statusEffects != null)
            amount = statusEffects.AbsorbWithShield(amount);

        if (amount <= 0f) return;

        currentHP = Mathf.Max(0f, currentHP - amount);
```

Remplacer par :

```csharp
        // Shield — absorbe les dégâts en priorité
        if (statusEffects != null)
            amount = statusEffects.AbsorbWithShield(amount);

        if (amount <= 0f) return;

        // Arrondi APRÈS l'absorption de Shield (pas avant) — c'est le montant RÉELLEMENT
        // appliqué à currentHP qui doit être entier, pas le montant brut pré-absorption (qui
        // n'est jamais directement soustrait de currentHP, juste utilisé pour calculer combien
        // le Shield encaisse).
        amount = Mathf.Round(amount);
        if (amount <= 0f) return; // un montant qui arrondit à 0 ne doit déclencher aucun effet on-hit

        currentHP = Mathf.Max(0f, currentHP - amount);
```

- [ ] **Step 2: Arrondir `Heal`**

Trouver :

```csharp
        if (amount <= 0f) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }
```

(dans `Heal`, juste après le bloc de réduction Poison) Remplacer par :

```csharp
        if (amount <= 0f) return;
        amount    = Mathf.Round(amount);
        if (amount <= 0f) return; // un soin qui arrondit à 0 ne doit rien appliquer
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }
```

- [ ] **Step 3: Arrondir `SpendMana`, `SpendHP`, `RecoverMana`**

Trouver :

```csharp
    public virtual void SpendMana(float amount)
    {
        currentMana = Mathf.Max(0f, currentMana - amount);
    }
```

Remplacer par :

```csharp
    public virtual void SpendMana(float amount)
    {
        currentMana = Mathf.Max(0f, currentMana - Mathf.Round(amount));
    }
```

Trouver :

```csharp
    public virtual void SpendHP(float amount)
    {
        currentHP = Mathf.Max(0f, currentHP - amount);
    }
```

Remplacer par :

```csharp
    public virtual void SpendHP(float amount)
    {
        currentHP = Mathf.Max(0f, currentHP - Mathf.Round(amount));
    }
```

Trouver :

```csharp
    public virtual void RecoverMana(float amount)
    {
        if (isDead) return;
        currentMana = Mathf.Min(maxMana, currentMana + amount);
    }
```

Remplacer par :

```csharp
    public virtual void RecoverMana(float amount)
    {
        if (isDead) return;
        currentMana = Mathf.Min(maxMana, currentMana + Mathf.Round(amount));
    }
```

- [ ] **Step 4: Arrondir `SetMaxHP`/`SetMaxMana`**

Trouver :

```csharp
    public void SetMaxHP(float value)
    {
        maxHP     = Mathf.Max(1f, value);
        currentHP = Mathf.Clamp(currentHP, 0f, maxHP);
    }

    public void SetMaxMana(float value)
    {
        maxMana     = Mathf.Max(0f, value);
        currentMana = Mathf.Clamp(currentMana, 0f, maxMana);
    }
```

Remplacer par :

```csharp
    public void SetMaxHP(float value)
    {
        // Arrondi AVANT le Max(1f, ...) — sinon un maxHP calculé à 0.6 passerait le plancher à
        // 1f (comportement correct) mais un maxHP calculé à 1.6 donnerait maxHP=1.6 (pas entier)
        // au lieu de 2. L'ordre Round PUIS Max garantit un maxHP toujours entier ET jamais < 1.
        maxHP     = Mathf.Max(1f, Mathf.Round(value));
        currentHP = Mathf.Clamp(currentHP, 0f, maxHP);
    }

    public void SetMaxMana(float value)
    {
        maxMana     = Mathf.Max(0f, Mathf.Round(value));
        currentMana = Mathf.Clamp(currentMana, 0f, maxMana);
    }
```

- [ ] **Step 5: Compile check**

Unity Editor recompile, Console : 0 erreur.

- [ ] **Step 6: Vérification manuelle**

1. En combat, prendre un coup de compétence avec un multiplicateur de crit ET une réduction
   d'armure actifs (valeurs qui produisaient un `dmg` fractionnaire avant ce fix — n'importe
   quel skill avec `damageMultiplier` ≠ 1 sur une cible avec de la défense suffit). Observer
   `CurrentHP` après le coup (panneau joueur, déjà en `FloorToInt` depuis un chantier
   précédent) — doit être un entier exact, sans décimale résiduelle si on logue `CurrentHP`
   brut temporairement.
2. **Double-arrondi (Review Focus)** : ajouter temporairement `Debug.Log(_player.CurrentHP)`
   après un `Heal(5f)` direct (ex: bind temporaire sur une touche de debug, ou via une potion
   qui soigne exactement 5) — confirmer que le résultat est exactement `+5`, pas `+4` ni `+6`
   (un montant déjà entier qui traverse `Mathf.Round` ne doit jamais dévier). Retirer le log
   avant l'étape suivante.
3. **Clamp sur max réduit (Review Focus)** : avec `CurrentHP` à 50 et `MaxHP` à 50, appliquer un
   debuff qui réduit `MaxHP` à une valeur qui arrondit à 45 (ex: un debuff `-9% Max HP` sur un
   maxHP de 50 → 45.5 → arrondi 46, ou ajuster les valeurs de test pour tomber sur un cas net) —
   confirmer que `CurrentHP` se clampe immédiatement à la nouvelle valeur entière de `MaxHP`,
   sans rester bloqué entre les deux.

- [ ] **Step 7: Commit**

```bash
git add Entities/Entity.cs
git commit -m "feat: round HP/mana amounts at the 7 point-mutation methods in Entity.cs"
```

---

### Task 3: `Entity.cs` — accumulateurs dans `ApplyRegen()`

**Files:**
- Modify: `Entities/Entity.cs:90-91` (nouveaux champs privés, juste après `currentMana`)
- Modify: `Entities/Entity.cs:365-370` (`ApplyRegen()`)

**Interfaces:**
- Consumes: `FractionalAccumulator.ExtractWhole(float delta) : int` (Tâche 1). `Heal(float
  amount)`/`RecoverMana(float amount)` déjà arrondis (Tâche 2) — un appel `Heal(whole)` avec un
  `int` déjà extrait traverse l'arrondi de la Tâche 2 sans effet.
- Produces: rien consommé par une tâche ultérieure — `ApplyRegen()` reste `private`.

- [ ] **Step 1: Ajouter les 2 nouveaux champs**

Trouver (dans `Entities/Entity.cs`) :

```csharp
    protected float currentHP;
    protected float currentMana;
```

Remplacer par :

```csharp
    protected float currentHP;
    protected float currentMana;

    // Reliquat fractionnaire de la régénération passive — survit à la mort/respawn de l'entité
    // (contrairement aux accumulateurs de DebuffInstance/BuffInstance, détruits avec l'effet de
    // statut via ClearAllEffects() dans Die()). Assumé sans conséquence : un reliquat qui
    // survit ne fait que légèrement rapprocher le PROCHAIN point entier de regen après respawn,
    // aucune perte ni gain d'HP/Mana n'en découle. Voir Review Focus de ce plan.
    private FractionalAccumulator _regenHPAccum;
    private FractionalAccumulator _regenManaAccum;
```

- [ ] **Step 2: Réécrire `ApplyRegen()`**

Trouver :

```csharp
    // Sans effet si regenHP == 0f && regenMana == 0f (Mob/PNJ/Pet).
    private void ApplyRegen()
    {
        if (regenHP   > 0f) Heal(regenHP);
        if (regenMana > 0f) RecoverMana(regenMana);
```

Remplacer par :

```csharp
    // Sans effet si regenHP == 0f && regenMana == 0f (Mob/PNJ/Pet).
    private void ApplyRegen()
    {
        // FractionalAccumulator au lieu d'un Heal(regenHP) direct — regenHP/regenMana peuvent
        // être fractionnaires (bonus % de stats), et ce tick tourne 1×/sec : un regenHP de 0.3
        // arrondi directement vaudrait 0 à CHAQUE tick, pour toujours (regen silencieusement
        // cassée). L'accumulateur garde le reliquat et ne soigne que lorsqu'au moins 1 point
        // entier s'est accumulé — voir docs/superpowers/specs/2026-10-04-integer-hp-mana-design.md.
        if (regenHP > 0f)
        {
            int whole = _regenHPAccum.ExtractWhole(regenHP);
            if (whole > 0) Heal(whole);
        }
        if (regenMana > 0f)
        {
            int whole = _regenManaAccum.ExtractWhole(regenMana);
            if (whole > 0) RecoverMana(whole);
        }
```

(La ligne fermante `}` de la méthode existante reste inchangée après ce bloc.)

- [ ] **Step 3: Compile check**

Unity Editor recompile, Console : 0 erreur.

- [ ] **Step 4: Vérification manuelle**

1. Régler `regenHP` à une valeur fractionnaire faible (ex: 0.3 — via `CharacterData`/stats, ou
   temporairement `SetRegenHP(0.3f)` en debug). Rester hors combat 10+ secondes, observer
   `CurrentHP` : doit progresser par paliers de +1 avec un délai croissant entre chaque palier
   (le 1er palier arrive après ~3.3s, PAS après exactement 1s, PAS jamais) — pas de progression
   linéaire fractionnaire visible, pas de blocage à 0.
2. **Reliquat survit à la mort (Review Focus)** : avec `regenHP` fractionnaire actif, laisser le
   reliquat s'accumuler partiellement (ex: attendre 2 ticks sur un regenHP de 0.3 → reliquat
   ≈0.6, pas encore de point appliqué), puis mourir et respawn. Confirmer qu'aucune anomalie
   n'apparaît (pas de `CurrentHP` qui saute anormalement au respawn, pas d'exception) — le
   prochain point entier doit simplement arriver légèrement plus tôt que 3.3s après le respawn,
   ce qui est le comportement assumé, pas un bug à corriger.

- [ ] **Step 5: Commit**

```bash
git add Entities/Entity.cs
git commit -m "feat: accumulate fractional passive regen instead of rounding it away"
```

---

### Task 4: `Player.cs` — arrondi de `Revive()`

**Files:**
- Modify: `Entities/Player.cs:1539-1543` (`Revive()`)

**Interfaces:**
- Consumes: rien de nouveau.
- Produces: rien consommé par une tâche ultérieure — tâche indépendante, isolée.

- [ ] **Step 1: Arrondir les deux assignations directes**

Trouver (dans `Entities/Player.cs`) :

```csharp
    public void Revive(float hpPercent = 0.30f, float manaPercent = 0.30f)
    {
        isDead      = false;
        currentHP   = maxHP   * hpPercent;
        currentMana = maxMana * manaPercent;
```

Remplacer par :

```csharp
    public void Revive(float hpPercent = 0.30f, float manaPercent = 0.30f)
    {
        isDead      = false;
        // Assignation DIRECTE à currentHP/currentMana, pas via Heal()/RecoverMana() (le joueur
        // est encore isDead == true l'instant d'avant, Heal()/RecoverMana() refuseraient tout —
        // voir leurs gardes isDead) — donc l'arrondi doit être fait ici explicitement, pas
        // hérité automatiquement des méthodes arrondies d'Entity.cs. Trouvé en écrivant la spec
        // (inventaire exhaustif des sites de mutation) : ce site bypassait tout, pas rapporté
        // par Florian en test manuel.
        currentHP   = Mathf.Round(maxHP   * hpPercent);
        currentMana = Mathf.Round(maxMana * manaPercent);
```

- [ ] **Step 2: Compile check**

Unity Editor recompile, Console : 0 erreur.

- [ ] **Step 3: Vérification manuelle**

1. Mourir en tant que joueur, déclencher `Revive()` (valeurs par défaut 30%/30%, ou toute valeur
   configurée côté passive/item de résurrection). Observer `CurrentHP`/`CurrentMana` juste après
   : entiers exacts, pas de décimale.
2. **Cas `.5` pile (Review Focus)** : avec un `MaxHP` de 15 et `hpPercent` par défaut (0.30) →
   30% de 15 = 4.5 pile. Confirmer que `CurrentHP` après revive est soit 4 soit 5 (arrondi
   C# standard, `MidpointRounding.ToEven` → 4), jamais une valeur fractionnaire ni une exception.
   (Ajuster `MaxHP` temporairement si besoin pour obtenir ce cas exact de test.)

- [ ] **Step 4: Commit**

```bash
git add Entities/Player.cs
git commit -m "fix: round Player.Revive() HP/mana instead of assigning raw fractional values"
```

---

### Task 5: `StatusEffectInstance.cs` — accumulateur `DebuffInstance` (Dot/ManaDrain/HpDrain)

**Files:**
- Modify: `Data/StatusEffect/StatusEffectInstance.cs:43-48` (champ sur `DebuffInstance`)
- Modify: `Data/StatusEffect/StatusEffectInstance.cs:56-107` (`DebuffInstance.Tick()`)

**Interfaces:**
- Consumes: `FractionalAccumulator.ExtractWhole(float delta) : int` (Tâche 1). `TakeDamage`/
  `SpendMana`/`RecoverMana`/`Heal` déjà arrondis (Tâche 2).
- Produces: rien consommé par une tâche ultérieure — indépendante de la Tâche 6 (classe
  différente).

- [ ] **Step 1: Ajouter le champ `_tickAccum` sur `DebuffInstance`**

Trouver (dans `Data/StatusEffect/StatusEffectInstance.cs`) :

```csharp
public class DebuffInstance : StatusEffectInstance
{
    public DebuffData DebuffData => (DebuffData)data;
    public DebuffType DebuffType => DebuffData.debuffType;

    public DebuffInstance(DebuffData data, Entity source) : base(data, source) { }
```

Remplacer par :

```csharp
public class DebuffInstance : StatusEffectInstance
{
    public DebuffData DebuffData => (DebuffData)data;
    public DebuffType DebuffType => DebuffData.debuffType;

    // UN SEUL accumulateur pour les 3 cas Dot/ManaDrain/HpDrain ci-dessous — PAS un bug de
    // partage d'état : DebuffType est fixé une fois à la création de l'instance (voir le
    // constructeur juste en dessous) et ne change jamais, donc UNE instance donnée ne tique
    // jamais plus d'un seul de ces 3 cas. Deux Poison stackés sur la même cible sont déjà DEUX
    // instances de DebuffInstance séparées (voir StatusEffectSystem, liste d'instances actives),
    // chacune avec son propre _tickAccum — aucun risque de mélange entre stacks.
    private FractionalAccumulator _tickAccum;

    public DebuffInstance(DebuffData data, Entity source) : base(data, source) { }
```

- [ ] **Step 2: Accumulateur sur le cas `Dot`**

Trouver :

```csharp
            case DebuffType.Dot:
                // Poison — le flag healReduction est géré à part via OnApply/OnExpire
                // dans StatusEffectSystem, indépendant du calcul de dégâts ici.
                float dmg = ComputeDotDps(target) * deltaTime;
                if (dmg > 0f)
                    target.TakeDamage(dmg, DebuffData.damageElement, source);
                break;
```

Remplacer par :

```csharp
            case DebuffType.Dot:
                // Poison — le flag healReduction est géré à part via OnApply/OnExpire
                // dans StatusEffectSystem, indépendant du calcul de dégâts ici.
                // Accumulateur au lieu d'un TakeDamage(dmg) direct — dmg est calculé en
                // DPS × deltaTime, donc fractionnaire à CHAQUE frame (ex: 10 DPS à 60 FPS =
                // 0.16/frame) ; arrondir directement l'arrondirait à 0 pour toujours.
                float dmg = ComputeDotDps(target) * deltaTime;
                if (dmg > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(dmg);
                    if (whole > 0) target.TakeDamage(whole, DebuffData.damageElement, source);
                }
                break;
```

- [ ] **Step 3: Accumulateur sur le cas `ManaDrain`**

Trouver :

```csharp
                float drainRate = DebuffData.manaDrainModifier == ModifierType.Percent
                    ? target.MaxMana * DebuffData.manaDrainPerSecond
                    : DebuffData.manaDrainPerSecond;
                float drain = drainRate * deltaTime;
                if (drain > 0f)
                {
                    target.SpendMana(drain);
                    source?.RecoverMana(drain);
                }
                break;
```

Remplacer par :

```csharp
                float drainRate = DebuffData.manaDrainModifier == ModifierType.Percent
                    ? target.MaxMana * DebuffData.manaDrainPerSecond
                    : DebuffData.manaDrainPerSecond;
                float drain = drainRate * deltaTime;
                if (drain > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(drain);
                    if (whole > 0)
                    {
                        target.SpendMana(whole);
                        source?.RecoverMana(whole);
                    }
                }
                break;
```

- [ ] **Step 4: Accumulateur sur le cas `HpDrain`**

Trouver :

```csharp
                float hpDrainRate = DebuffData.hpDrainModifier == ModifierType.Percent
                    ? target.MaxHP * DebuffData.hpDrainPerSecond
                    : DebuffData.hpDrainPerSecond;
                float hpDrain = hpDrainRate * deltaTime;
                if (hpDrain > 0f)
                {
                    target.TakeDamage(hpDrain, ElementType.Neutral, source);
                    source?.Heal(hpDrain);
                }
                break;
```

Remplacer par :

```csharp
                float hpDrainRate = DebuffData.hpDrainModifier == ModifierType.Percent
                    ? target.MaxHP * DebuffData.hpDrainPerSecond
                    : DebuffData.hpDrainPerSecond;
                float hpDrain = hpDrainRate * deltaTime;
                if (hpDrain > 0f)
                {
                    int whole = _tickAccum.ExtractWhole(hpDrain);
                    if (whole > 0)
                    {
                        target.TakeDamage(whole, ElementType.Neutral, source);
                        source?.Heal(whole);
                    }
                }
                break;
```

- [ ] **Step 5: Compile check**

Unity Editor recompile, Console : 0 erreur.

- [ ] **Step 6: Vérification manuelle**

1. Appliquer un DoT (`DebuffType.Dot`, via `baseDamagePercent` réglé pour donner un DPS faible,
   ex: < 1/sec sur la cible de test) sur un Mob ou le joueur. Observer sur plusieurs secondes :
   les dégâts s'appliquent bien par paliers entiers avec un léger délai, **jamais silencieusement
   neutralisés** (Review Focus — c'est le bug central que ce chantier corrige, à voir tourner en
   jeu, pas juste faire confiance au code).
2. Appliquer un DoT à DPS plus élevé (ex: 10+/sec) — confirmer que les dégâts totaux sur la
   durée correspondent toujours globalement au DPS configuré × durée (pas de perte massive, juste
   la granularité en paliers entiers).
3. Tester `ManaDrain` et `HpDrain` de la même façon (debuff configuré avec ces types, DPS/sec
   faible) — mêmes observations : pas de blocage silencieux, transfert source/cible cohérent.

- [ ] **Step 7: Commit**

```bash
git add Data/StatusEffect/StatusEffectInstance.cs
git commit -m "fix: accumulate fractional DoT/ManaDrain/HpDrain instead of rounding them to zero"
```

---

### Task 6: `StatusEffectInstance.cs` — accumulateur `BuffInstance` (Regeneration/HoT)

**Files:**
- Modify: `Data/StatusEffect/StatusEffectInstance.cs:175-180` (champ sur `BuffInstance`)
- Modify: `Data/StatusEffect/StatusEffectInstance.cs:195-200` (cas `BuffType.Regeneration`)

**Interfaces:**
- Consumes: `FractionalAccumulator.ExtractWhole(float delta) : int` (Tâche 1). `Heal(float
  amount)` déjà arrondi (Tâche 2).
- Produces: rien consommé par une tâche ultérieure — indépendante de la Tâche 5 (classe
  différente).

- [ ] **Step 1: Ajouter le champ `_regenAccum` sur `BuffInstance`**

Trouver (dans `Data/StatusEffect/StatusEffectInstance.cs`) :

```csharp
public class BuffInstance : StatusEffectInstance
{
    public BuffData BuffData => (BuffData)data;
    public BuffType BuffType => BuffData.buffType;

    public float remainingShield;
```

Remplacer par :

```csharp
public class BuffInstance : StatusEffectInstance
{
    public BuffData BuffData => (BuffData)data;
    public BuffType BuffType => BuffData.buffType;

    public float remainingShield;

    // Même rôle que DebuffInstance._tickAccum, pour BuffType.Regeneration (seul cas qui applique
    // un montant continu via taux × deltaTime dans Tick() ci-dessous).
    private FractionalAccumulator _regenAccum;
```

- [ ] **Step 2: Accumulateur sur le cas `Regeneration`**

Trouver :

```csharp
            case BuffType.Regeneration:
                // HoT — soin progressif sur la durée (§3.1.1.2)
                // GetHealPerSecond supporte Flat et Percent (% MaxHP)
                float heal = BuffData.GetHealPerSecond(target.MaxHP) * deltaTime;
                if (heal > 0f) target.Heal(heal);
                break;
```

Remplacer par :

```csharp
            case BuffType.Regeneration:
                // HoT — soin progressif sur la durée (§3.1.1.2)
                // GetHealPerSecond supporte Flat et Percent (% MaxHP)
                // Accumulateur au lieu d'un Heal(heal) direct — même raison que DebuffInstance._tickAccum :
                // heal est calculé en taux × deltaTime, fractionnaire à chaque frame.
                float heal = BuffData.GetHealPerSecond(target.MaxHP) * deltaTime;
                if (heal > 0f)
                {
                    int whole = _regenAccum.ExtractWhole(heal);
                    if (whole > 0) target.Heal(whole);
                }
                break;
```

- [ ] **Step 3: Compile check**

Unity Editor recompile, Console : 0 erreur.

- [ ] **Step 4: Vérification manuelle**

1. Appliquer un buff `Regeneration` (HoT) à faible taux (< 1 HP/sec) sur le joueur ou un Mob.
   Observer sur plusieurs secondes : le soin s'applique par paliers entiers avec un léger délai,
   jamais silencieusement neutralisé.
2. Appliquer un HoT à taux plus élevé — confirmer que le total soigné sur la durée correspond
   globalement au taux configuré × durée.

- [ ] **Step 5: Commit**

```bash
git add Data/StatusEffect/StatusEffectInstance.cs
git commit -m "fix: accumulate fractional Regeneration HoT instead of rounding it to zero"
```

---

### Task 7: Vérification finale complète

Pas de changement de code — cette tâche est la checklist complète de la spec (section
"Vérification"), rejouée de bout en bout sur une configuration après l'autre pour rattraper tout
ce qu'une vérification individuelle de tâche aurait manqué.

**Files:** aucun.

**Interfaces:** aucune — cette tâche exerce seulement ce que les Tâches 1-6 ont déjà produit.

- [ ] **Step 1: Dégât fractionnaire → CurrentHP entier**

Coup de compétence avec crit/réduction d'armure actifs. Confirmer `CurrentHP` entier après.

- [ ] **Step 2: Régénération faible, Player ET un Mob/PNJ avec `regenHP` > 0**

`regenHP`/`regenMana` fractionnaire faible sur le joueur ET sur un Mob/PNJ ayant une régen
configurée (si applicable — sinon noter que Mob/PNJ par défaut n'en ont pas, voir
`Entities/Entity.cs` commentaire GDD §3.1). Confirmer progression par paliers entiers avec délai,
jamais bloquée à 0.

- [ ] **Step 3: DoT à faible DPS, sur le joueur ET sur un Mob**

Confirmer dans les deux sens (joueur empoisonné par un mob, mob empoisonné par le joueur) que les
dégâts s'appliquent toujours, par paliers entiers.

- [ ] **Step 4: `Revive()` → valeurs entières**

Mourir et ressusciter (30%/30% par défaut). `CurrentHP`/`CurrentMana` entiers exacts.

- [ ] **Step 5: Cas limite — DoT très faible sur combat court**

DoT à 0.05/sec sur un combat de moins de 20 secondes → aucun dégât appliqué pendant tout le
combat est un comportement ATTENDU (le reliquat n'a jamais atteint 1 point entier), à ne PAS
reporter comme un bug.

- [ ] **Step 6: Non-régression — un combat normal sans aucun regen/DoT fractionnaire**

Sur un personnage/Mob dont tous les taux de regen sont 0 et sans DoT actif, dérouler un combat
normal (dégâts de compétence, coûts de mana, heal on-hit) — confirmer qu'aucun comportement
visible n'a changé par rapport à avant ce chantier (les dégâts/soins/coûts de compétence
étaient déjà pratiquement toujours des valeurs rondes en pratique).

- [ ] **Step 7: Rapport**

Dire à Florian lesquelles des étapes ci-dessus sont passées/ont échoué. Tout échec aux Steps 1-4
est un vrai bug de ce plan (pas un gap pré-existant) et doit être corrigé avant de considérer ce
chantier terminé.
