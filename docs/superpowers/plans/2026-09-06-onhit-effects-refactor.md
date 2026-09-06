# Refonte OnHitDealt / OnHitReceived — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer les 3 systèmes de proc "à chaque coup" qui se chevauchent (OnHitEffectData
côté reçu, EquipmentConfig.statusEffects côté donné — bugué — et l'exemple critique/damage-amp
non couvert) par deux systèmes symétriques et propres : `OnHitDealtEffectData` (coup donné) et
`OnHitReceivedEffectData` (coup reçu), placables sur tout équipement, `PermanentSkillData`,
`MobData` et `PNJData`.

**Architecture:** Deux nouveaux/renommés ScriptableObject (5 types DEALT, 6 types RECEIVED).
Deux méthodes virtuelles sur `Entity` (`GetOnHitDealtEffects()`/`GetOnHitReceivedEffects()`)
donnent un point d'accès polymorphique unique à `CombatSystem`/`SkillSystem`, quelle que soit
la source de la liste (équipement+permanents pour Player, `MobData`/`PNJData` pour Mob/PNJ).
Les effets "réaction" (Lifesteal, Thorns, debuff/buff-proc...) s'exécutent après un coup déjà
résolu ; les effets "modificateur" (DamageAmpOnHit/DamageReductionOnHit) s'exécutent DANS
`CombatSystem.CalculateDamage`/`CalculateMobDamage`, au moment où le nombre de dégâts se
construit.

**Tech Stack:** Unity C#, ScriptableObject + `[ShowIf]` (Utils/ShowIfAttribute.cs) pour
l'Inspector conditionnel.

**Spec:** `docs/superpowers/specs/2026-09-06-onhit-effects-refactor-design.md`

## Global Constraints

- Rupture propre acceptée par Florian — AUCUNE migration de données à écrire. Il reconfigure
  les équipements déjà créés à la main après ce refactor.
- RuneData.statusEffects et SkillData.statusEffects (deux mécanismes différents, non liés à ce
  refactor) restent INTOUCHÉS — ne jamais les renommer ni les supprimer.
- Ordinal safety : les deux enums `OnHitDealtEffectType`/`OnHitReceivedEffectType` sont neufs,
  garder l'ordre de déclaration donné dans chaque tâche ci-dessous pour toute la suite du
  projet (pas de contrainte héritée, mais convention établie dès la création).
- Pas de framework de test automatisé dans ce projet — toute vérification se fait
  manuellement en Play Mode par Florian (voir Tâche 7).
- Chaque tâche doit laisser le projet dans un état qui COMPILE (0 erreur C#) — l'ordre des
  tâches ci-dessous a été choisi spécifiquement pour ça (voir note dans la Tâche 2 sur
  pourquoi la suppression de l'ancien système y est incluse plutôt que reportée).

---

### Task 1: Nouveau ScriptableObject `OnHitDealtEffectData`

**Files:**
- Create: `Data/StatusEffect/OnHitDealtEffectData.cs`

**Interfaces:**
- Produces: `OnHitDealtEffectType` (enum), `OnHitDealtEffectData` (classe), `OnHitDealtEffectEntry`
  (classe) — consommés par les Tâches 2, 3, 5, 6.

- [ ] **Step 1: Créer le fichier**

```csharp
using UnityEngine;

// =============================================================
// OnHitDealtEffectData — ScriptableObject template d'effet On-Hit DONNÉ
// Path : Assets/Scripts/Data/StatusEffect/OnHitDealtEffectData.cs
// AetherTree GDD v3.5 — refonte OnHitDealt/OnHitReceived (2026-09-06)
//
// Déclenché quand l'entité PORTEUSE (celle qui a cet effet dans sa liste
// onHitDealtEffects — équipement/permanent/MobData/PNJData) TOUCHE une cible.
// Chaque effet a une chance d'activation [0..1].
//
// Types disponibles :
//   DamageAmpOnHit   — MODIFICATEUR, amplifie CE coup en cours de calcul
//                       (roulé/appliqué dans CombatSystem.CalculateDamage/
//                       CalculateMobDamage, PAS ici comme réaction)
//   LifestealOnHit   — réaction, soigne l'attaquant (Flat ou % des dégâts infligés)
//   ManaOnHit        — réaction, restaure du mana à l'attaquant (Flat ou % MaxMana)
//   ApplyDebuffOnHit — réaction, applique un DebuffData sur LA CIBLE touchée
//   ApplyBuffOnHit   — réaction, applique un BuffData sur L'ATTAQUANT (soi)
//
// Voir OnHitReceivedEffectData.cs pour le pendant côté "coup reçu".
//
// Assets > Create > AetherTree > StatusEffects > OnHitDealtEffectData
// =============================================================

public enum OnHitDealtEffectType
{
    DamageAmpOnHit,
    LifestealOnHit,
    ManaOnHit,
    ApplyDebuffOnHit,
    ApplyBuffOnHit,
}

[CreateAssetMenu(fileName = "NewOnHitDealt", menuName = "AetherTree/StatusEffects/OnHitDealtEffectData")]
public class OnHitDealtEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "OnHitDealtEffect";
    public Sprite icon;

    [Header("Type")]
    public OnHitDealtEffectType effectType = OnHitDealtEffectType.LifestealOnHit;

    [Header("Déclenchement")]
    [Tooltip("Probabilité de déclenchement par coup infligé [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    // ── DamageAmpOnHit ────────────────────────────────────────
    [Tooltip("% de dégâts en plus sur CE coup. Ex: 0.10 = +10%. Roulé dans CombatSystem, " +
             "au même stage que le critique — voir CalculateDamage/CalculateMobDamage.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.DamageAmpOnHit, Header = "Amplification (DamageAmpOnHit)")]
    public float ampPercent = 0.10f;

    // ── LifestealOnHit ────────────────────────────────────────
    [Tooltip("Flat : montant fixe soigné.\nPercent : % des dégâts infligés CE coup.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit, Header = "Vol de vie (LifestealOnHit)")]
    public ModifierType lifestealModifier = ModifierType.Percent;
    [Tooltip("Montant du vol de vie.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit)]
    public float lifestealAmount = 0.10f;

    // ── ManaOnHit ─────────────────────────────────────────────
    [Tooltip("Flat : montant fixe.\nPercent : % du MaxMana de l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit, Header = "Restauration de mana (ManaOnHit)")]
    public ModifierType manaModifier = ModifierType.Flat;
    [Tooltip("Montant de mana restauré.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit)]
    public float manaAmount = 5f;

    // ── ApplyDebuffOnHit ──────────────────────────────────────
    [Tooltip("Debuff appliqué sur LA CIBLE touchée au déclenchement.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyDebuffOnHit, Header = "Debuff sur la cible (ApplyDebuffOnHit)")]
    public DebuffData debuffToApply;

    // ── ApplyBuffOnHit ────────────────────────────────────────
    [Tooltip("Buff appliqué sur L'ATTAQUANT (soi-même) au déclenchement.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyBuffOnHit, Header = "Buff sur soi (ApplyBuffOnHit)")]
    public BuffData buffToApply;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Retourne true si l'effet se déclenche ce coup.</summary>
    public bool Roll() => Random.value < chance;

    /// <summary>Calcule le montant de vol de vie selon les dégâts infligés ce coup.</summary>
    public float GetLifestealAmount(float damageDealt)
        => lifestealModifier == ModifierType.Percent ? damageDealt * lifestealAmount : lifestealAmount;

    /// <summary>Calcule le montant de mana restauré selon le MaxMana de l'attaquant.</summary>
    public float GetManaAmount(float attackerMaxMana)
        => manaModifier == ModifierType.Percent ? attackerMaxMana * manaAmount : manaAmount;
}

// =============================================================
// OnHitDealtEffectEntry — une ligne dans la liste d'un équipement/permanent/mob/pnj
// =============================================================
[System.Serializable]
public class OnHitDealtEffectEntry
{
    [Tooltip("SO de l'effet On-Hit infligé à appliquer.")]
    public OnHitDealtEffectData effect;

    [Tooltip("Override de la chance du SO [0..1].\nSi 0, utilise la chance définie dans le SO.")]
    [Range(0f, 1f)]
    public float chanceOverride = 0f;

    /// <summary>Chance effective : override si > 0, sinon valeur du SO.</summary>
    public float EffectiveChance => (chanceOverride > 0f && effect != null)
        ? chanceOverride
        : (effect != null ? effect.chance : 0f);

    /// <summary>Roll si l'effet se déclenche ce coup.</summary>
    public bool Roll() => effect != null && Random.value < EffectiveChance;
}
```

- [ ] **Step 2: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation. Aucune erreur attendue (fichier autonome, ne dépend
que de `ModifierType`/`DebuffData`/`BuffData`/`ShowIfAttribute`, déjà existants).

- [ ] **Step 3: Test manuel rapide**

`Assets > Create > AetherTree > StatusEffects > OnHitDealtEffectData` — vérifier que l'asset
se crée, que le dropdown `effectType` propose les 5 types, et que changer de type change bien
les champs visibles dans l'Inspector (ShowIf fonctionne).

- [ ] **Step 4: Commit**

```bash
git add "Data/StatusEffect/OnHitDealtEffectData.cs"
git commit -m "feat: add OnHitDealtEffectData for on-hit-dealt procs"
```

---

### Task 2: Renommage OnHitReceivedEffectData + câblage des listes partout + retrait de l'ancien système donné

**Contexte pour l'implémenteur** : cette tâche est volontairement grosse — elle regroupe TOUT
le renommage/câblage de données (aucune logique de jeu nouvelle) parce que ces changements sont
mutuellement dépendants : renommer `OnHitEffectEntry`→`OnHitReceivedEffectEntry` casse la
compilation de `EquipmentConfig`, qui casse celle des 8 classes d'équipement, qui casse celle
de `TooltipSystem`, etc. Les séparer en tâches individuelles laisserait le projet dans un état
qui ne compile pas entre deux tâches. Cette tâche inclut AUSSI la suppression de l'ancien
système de proc "donné" (`EquipmentConfig.statusEffects` + `SkillSystem.ApplyWeaponStatusEffects`/
`ApplyStatusEffectEntry`) — parce que son champ de données disparaît ici ; le retarder à la
Tâche 5 laisserait `SkillSystem.cs` référencer un champ qui n'existe plus entre les deux tâches.

**Files:**
- Modify: `Data/StatusEffect/OnHitEffectData.cs` → renommer en `Data/StatusEffect/OnHitReceivedEffectData.cs`
- Modify: `Data/Equipment/EquipmentConfig.cs`
- Modify: `Data/Equipment/ArmorData.cs`, `BootsData.cs`, `CosmeticDataBody.cs`, `CosmeticDataHead.cs`, `HelmetData.cs`, `GlovesData.cs`, `JewelryData.cs`, `WeaponData.cs`
- Modify: `Data/Skills/PermanentSkillData.cs`
- Modify: `Data/Mobs/MobData.cs`
- Modify: `Data/PNJ/PNJData.cs`
- Modify: `UI/Shared/TooltipSystem.cs`
- Modify: `Combat/SkillSystem.cs` (suppression uniquement — pas de nouvelle logique)

**Interfaces:**
- Consumes: rien de nouveau (renomme des types existants)
- Produces: `OnHitReceivedEffectType` (enum, renommé), `OnHitReceivedEffectData` (classe,
  renommée), `OnHitReceivedEffectEntry` (classe, renommée) ; `EquipmentConfig.onHitReceivedEffects`
  / `EquipmentConfig.onHitDealtEffects` ; propriétés `OnHitReceivedEffects`/`OnHitDealtEffects`
  sur les 8 classes d'équipement ; `PermanentSkillData.onHitReceivedEffects`/`onHitDealtEffects` ;
  `MobData.onHitReceivedEffects`/`onHitDealtEffects` ; `PNJData.onHitReceivedEffects`/`onHitDealtEffects`
  — tous consommés par les Tâches 3, 4, 5, 6.

- [ ] **Step 1: Renommer et réécrire `Data/StatusEffect/OnHitEffectData.cs` → `OnHitReceivedEffectData.cs`**

Supprimer l'ancien fichier `Data/StatusEffect/OnHitEffectData.cs`, créer
`Data/StatusEffect/OnHitReceivedEffectData.cs` avec ce contenu (identique à l'existant, classes
renommées + nouveau type `DamageReductionOnHit` en premier dans l'enum + son champ) :

```csharp
using UnityEngine;

// =============================================================
// OnHitReceivedEffectData — ScriptableObject template d'effet On-Hit REÇU
// Path : Assets/Scripts/Data/StatusEffect/OnHitReceivedEffectData.cs
// AetherTree GDD v3.5 §5.1 à §5.6 — refonte OnHitDealt/OnHitReceived (2026-09-06)
//
// Déclenché quand l'entité PORTEUSE (celle qui a cet effet dans sa liste
// onHitReceivedEffects) REÇOIT un coup. Chaque effet a une chance [0..1].
//
// Types disponibles :
//   DamageReductionOnHit — MODIFICATEUR, réduit CE coup en cours de calcul
//                          (roulé/appliqué dans CombatSystem.CalculateDamage/
//                          CalculateMobDamage, PAS ici comme réaction)
//   ReflectPercent   — renvoie X% des dégâts reçus à l'attaquant
//   Thorns           — renvoie une valeur fixe de dégâts à l'attaquant
//   CounterDebuff    — applique un debuff sur l'attaquant
//   HealOnHit        — soigne la cible (self)
//   CounterBuff      — applique un buff sur soi-même
//
// ReflectPercent & Thorns : pierceDefense contrôle si les dégâts renvoyés
//   ignorent la défense/résistance de l'attaquant (true = brut).
//
// Voir OnHitDealtEffectData.cs pour le pendant côté "coup infligé".
//
// Assets > Create > AetherTree > StatusEffects > OnHitReceivedEffectData
// =============================================================

public enum OnHitReceivedEffectType
{
    DamageReductionOnHit,
    ReflectPercent,
    Thorns,
    CounterDebuff,
    HealOnHit,
    CounterBuff,
}

[CreateAssetMenu(fileName = "NewOnHitReceived", menuName = "AetherTree/StatusEffects/OnHitReceivedEffectData")]
public class OnHitReceivedEffectData : ScriptableObject
{
    [Header("Identité")]
    public string effectName = "OnHitReceivedEffect";
    public Sprite icon;

    [Header("Type")]
    public OnHitReceivedEffectType effectType = OnHitReceivedEffectType.Thorns;

    [Header("Déclenchement")]
    [Tooltip("Probabilité de déclenchement par coup reçu [0..1].\nEx: 0.15 = 15% de chance.")]
    [Range(0f, 1f)]
    public float chance = 0.15f;

    // ── DamageReductionOnHit ──────────────────────────────────
    [Tooltip("% de dégâts en moins sur CE coup reçu. Ex: 0.50 = -50%. Roulé dans CombatSystem, " +
             "au même stage que le critique — voir CalculateDamage/CalculateMobDamage.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.DamageReductionOnHit, Header = "Réduction (DamageReductionOnHit)")]
    public float reductionPercent = 0.30f;

    // ── ReflectPercent ────────────────────────────────────────
    [Tooltip("Pourcentage des dégâts reçus renvoyés à l'attaquant.\nEx: 0.20 = 20% réfléchis.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, Header = "Reflect % (ReflectPercent)")]
    public float reflectPercent = 0.20f;

    // ── Thorns ────────────────────────────────────────────────
    [Tooltip("Dégâts fixes renvoyés à l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.Thorns, Header = "Épines fixes (Thorns)")]
    public float thornsDamage = 10f;

    // ── Commun Reflect + Thorns ───────────────────────────────
    [Tooltip("True : l'attaquant reçoit le montant fixé, tel quel (ignore défense/résistance).\n" +
             "False : réduit comme un dégât normal — défense (Neutral) ou résistance élémentaire\n" +
             "(autre élément) de l'attaquant, selon reflectElement ci-dessous.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, OnHitReceivedEffectType.Thorns, Header = "Options Reflect & Thorns", DisplayName = "Dégâts renvoyés bruts")]
    public bool pierceDefense = false;

    [Tooltip("Élément des dégâts renvoyés.\nNeutral = pas d'élément.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.ReflectPercent, OnHitReceivedEffectType.Thorns)]
    public ElementType reflectElement = ElementType.Neutral;

    // ── CounterDebuff ─────────────────────────────────────────
    [Tooltip("Debuff appliqué sur l'attaquant au déclenchement.\nGlisser un DebuffData ici.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.CounterDebuff, Header = "Counter-Debuff (CounterDebuff)")]
    public DebuffData counterDebuff;

    // ── HealOnHit ─────────────────────────────────────────────
    [Tooltip("Flat : valeur fixe soignée.\nPercent : % du MaxHP de la cible.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.HealOnHit, Header = "Soin au coup reçu (HealOnHit)")]
    public ModifierType healModifier = ModifierType.Flat;
    [Tooltip("Montant de soin.\nEx: 50 (Flat) ou 0.05 (Percent = 5% MaxHP).")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.HealOnHit)]
    public float healAmount = 50f;

    // ── CounterBuff ───────────────────────────────────────────
    [Tooltip("Buff appliqué sur soi-même au déclenchement.\nGlisser un BuffData ici.")]
    [ShowIf(nameof(effectType), OnHitReceivedEffectType.CounterBuff, Header = "Counter-Buff sur soi (CounterBuff)")]
    public BuffData counterBuff;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>Retourne true si l'effet se déclenche ce coup.</summary>
    public bool Roll() => Random.value < chance;

    /// <summary>Calcule le soin à appliquer selon le MaxHP de la cible.</summary>
    public float GetHealAmount(float targetMaxHP)
        => healModifier == ModifierType.Percent ? targetMaxHP * healAmount : healAmount;
}

// =============================================================
// OnHitReceivedEffectEntry — une ligne dans la liste d'un équipement/permanent/mob/pnj
// =============================================================
[System.Serializable]
public class OnHitReceivedEffectEntry
{
    [Tooltip("SO de l'effet On-Hit reçu à appliquer.")]
    public OnHitReceivedEffectData effect;

    [Tooltip("Override de la chance du SO [0..1].\nSi 0, utilise la chance définie dans le SO.")]
    [Range(0f, 1f)]
    public float chanceOverride = 0f;

    /// <summary>Chance effective : override si > 0, sinon valeur du SO.</summary>
    public float EffectiveChance => (chanceOverride > 0f && effect != null)
        ? chanceOverride
        : (effect != null ? effect.chance : 0f);

    /// <summary>Roll si l'effet se déclenche ce coup.</summary>
    public bool Roll() => effect != null && Random.value < EffectiveChance;
}
```

- [ ] **Step 2: `Data/Equipment/EquipmentConfig.cs` — retirer `statusEffects`/`onHitEffects`, ajouter les 2 nouvelles listes**

Trouver ce bloc (fin du fichier) :
```csharp
    [Header("Effets de statut (appliqués à chaque attaque)")]
    [Tooltip("Effets appliqués lors d'une attaque selon leur probabilité.\n" +
             "Glisser un DebuffData ou BuffData, puis régler la chance.\n" +
             "Ex: Burn 4s à 5% | Slow 2s à 10%")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("Résistances aux debuffs (porteur)")]
    [Tooltip("Chances de résister à un debuff spécifique quand on est attaqué.\n" +
             "Chargées dans StatusEffectSystem via CharacterStats.RecalculateStats().\n" +
             "Ex: Freeze 0.05 = 5% de résistance au gel")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

    [Header("Effets On-Hit (déclenchés quand le porteur reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le porteur reçoit un coup.\n" +
             "Lus par Player.ApplyOnHitEffects() à chaque TakeDamage().\n" +
             "Ex: Thorns 10 dmg à 100% | Reflect 20% à 15% | HealOnHit 2% MaxHP")]
    public List<OnHitEffectEntry> onHitEffects = new List<OnHitEffectEntry>();
}
```

Remplacer par :
```csharp
    [Header("Résistances aux debuffs (porteur)")]
    [Tooltip("Chances de résister à un debuff spécifique quand on est attaqué.\n" +
             "Chargées dans StatusEffectSystem via CharacterStats.RecalculateStats().\n" +
             "Ex: Freeze 0.05 = 5% de résistance au gel")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

    [Header("Effets On-Hit reçus (déclenchés quand le porteur reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le porteur reçoit un coup.\n" +
             "Lus par Entity.ApplyOnHitReceivedEffects() à chaque TakeDamage().\n" +
             "Ex: Thorns 10 dmg à 100% | Reflect 20% à 15% | HealOnHit 2% MaxHP")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();

    [Header("Effets On-Hit infligés (déclenchés quand le porteur touche une cible)")]
    [Tooltip("Effets déclenchés quand le porteur touche une cible.\n" +
             "Lus par SkillSystem.ApplyOnHitDealtEffects() à chaque coup infligé.\n" +
             "Ex: LifestealOnHit 10% à 15% | ManaOnHit 5 à 10% | ApplyBuffOnHit à 5%")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
}
```

Note : `statusEffects` (côté "Effets de statut, appliqués à chaque attaque") disparaît
entièrement de `EquipmentConfig` — c'était l'ancien système bugué. Ne PAS confondre avec
`SkillData.statusEffects` ou `RuneData.statusEffects`, qui sont des champs DIFFÉRENTS, dans
des classes différentes, non touchés par cette tâche.

- [ ] **Step 3: Les 8 classes d'équipement — renommer/ajouter les propriétés**

Dans chacun de ces 8 fichiers, trouver les 2 lignes :
```csharp
public List<OnHitEffectEntry> OnHitEffects => data?.config?.onHitEffects;
public List<StatusEffectEntry> StatusEffects => data?.config?.statusEffects;
```
(l'ordre exact des 2 lignes et l'alignement des espaces varient légèrement selon le fichier —
chercher `OnHitEffects` et `StatusEffects` dans chaque fichier pour les localiser) et les
remplacer par :
```csharp
public List<OnHitReceivedEffectEntry> OnHitReceivedEffects => data?.config?.onHitReceivedEffects;
public List<OnHitDealtEffectEntry>    OnHitDealtEffects    => data?.config?.onHitDealtEffects;
```

Fichiers à traiter (emplacement de la propriété au moment de l'écriture de ce plan, à
re-localiser par `grep OnHitEffects` si les lignes ont bougé) :
- `Data/Equipment/ArmorData.cs:251-253`
- `Data/Equipment/BootsData.cs:138-140`
- `Data/Equipment/CosmeticDataBody.cs:51-53`
- `Data/Equipment/CosmeticDataHead.cs:51-53`
- `Data/Equipment/HelmetData.cs:102-104`
- `Data/Equipment/GlovesData.cs:135-137`
- `Data/Equipment/JewelryData.cs:155-157`
- `Data/Equipment/WeaponData.cs:295-297`

- [ ] **Step 4: `Data/Skills/PermanentSkillData.cs` — renommer + ajouter**

Trouver :
```csharp
    [Header("③ Effets On-Hit (déclenchés quand on reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le joueur reçoit un coup.\n" +
             "Ex: Thorns 5 dmg | CounterPoison 8% | HealOnHit 1% MaxHP")]
    public List<OnHitEffectEntry> onHitEffects = new List<OnHitEffectEntry>();
```
Remplacer par :
```csharp
    [Header("③ Effets On-Hit reçus (déclenchés quand on reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le joueur reçoit un coup.\n" +
             "Ex: Thorns 5 dmg | CounterPoison 8% | HealOnHit 1% MaxHP")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();

    [Header("④ Effets On-Hit infligés (déclenchés quand on touche une cible)")]
    [Tooltip("Effets déclenchés quand le joueur touche une cible.\n" +
             "Ex: LifestealOnHit 10% | ManaOnHit 5")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
```

- [ ] **Step 5: `Data/Mobs/MobData.cs` — ajouter les 2 nouveaux champs**

Juste avant le bloc `// ── Visuel ─────` (ligne ~132), insérer :
```csharp
    // ── Effets On-Hit ─────────────────────────────────────────
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

```

- [ ] **Step 6: `Data/PNJ/PNJData.cs` — ajouter les 2 nouveaux champs**

Juste après le bloc `critMultiplier` (fin du groupe `canFight`, avant `// ===== VALIDATION
EDITOR =====`, ligne ~180), insérer :
```csharp
    // ── Effets On-Hit — actifs même hors canFight (un garde peut avoir Thorns) ──
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

```
(`onHitReceivedEffects` n'est PAS gated par `canFight` — un PNJ non-combattant peut quand même
subir des dégâts si `canDie = true` et un mob l'attaque, donc peut avoir un Thorns/Reflect
défensif. `onHitDealtEffects` lui n'a de sens que si le PNJ peut effectivement toucher
quelqu'un, gated par `canFight`.)

- [ ] **Step 7: `UI/Shared/TooltipSystem.cs` — renommer les références**

Trouver la signature :
```csharp
    private string BuildOnHitEffects(List<OnHitEffectEntry> effects)
```
Remplacer par :
```csharp
    private string BuildOnHitEffects(List<OnHitReceivedEffectEntry> effects)
```

Puis chercher tous les appels `BuildOnHitEffects(w.OnHitEffects)` /
`BuildOnHitEffects(a.OnHitEffects)` / etc. (6 sites : weapon, armor, helmet, gloves, boots,
jewelry — grep `BuildOnHitEffects(` dans ce fichier) et remplacer `.OnHitEffects` par
`.OnHitReceivedEffects` dans chacun (ex: `BuildOnHitEffects(w.OnHitEffects)` →
`BuildOnHitEffects(w.OnHitReceivedEffects)`).

- [ ] **Step 8: `Combat/SkillSystem.cs` — supprimer l'ancien système de proc donné**

Ce système lisait `EquipmentConfig.statusEffects`, retiré au Step 2 — sans cette suppression,
le fichier ne compile plus après ce Step 2.

D'abord, supprimer les méthodes `ApplyStatusEffectEntry` et `ApplyWeaponStatusEffects` en
entier (chercher `private void ApplyStatusEffectEntry` et `private void ApplyWeaponStatusEffects`
— la seconde englobe le commentaire d'en-tête `// EFFETS SECONDAIRES — StatusEffects de
l'équipement du Player` juste au-dessus, à supprimer aussi) :

```csharp
    private void ApplyStatusEffectEntry(StatusEffectEntry entry, Entity caster, Entity target)
    {
        if (entry == null || entry.effect == null) return;
        if (target == null || target.isDead) return;
        if (!entry.Roll()) return;

        var statusSystem = target.statusEffects;
        if (statusSystem == null) return;

        switch (entry.effect)
        {
            case BuffData buff:   statusSystem.ApplyBuff(buff, caster);           break;
            case DebuffData deb:  statusSystem.TryApplyDebuff(deb, caster);       break;
        }
    }

    // =========================================================
    // EFFETS SECONDAIRES — StatusEffects de l'équipement du Player
    // =========================================================

    private void ApplyWeaponStatusEffects(Player player, Entity target)
    {
        if (player == null || target == null || target.isDead) return;

        var statusSystem = target.statusEffects;
        if (statusSystem == null) return;

        var allEffects = new List<(List<StatusEffectEntry> effects, string source)>();

        if (player.equippedWeaponInstance?.data  != null) allEffects.Add((player.equippedWeaponInstance.StatusEffects, player.equippedWeaponInstance.WeaponName));
        if (player.equippedArmorInstance?.data   != null) allEffects.Add((player.equippedArmorInstance.StatusEffects,  player.equippedArmorInstance.ArmorName));
        if (player.equippedHelmetInstance?.data  != null) allEffects.Add((player.equippedHelmetInstance.StatusEffects, player.equippedHelmetInstance.HelmetName));
        if (player.equippedGlovesInstance?.data  != null) allEffects.Add((player.equippedGlovesInstance.StatusEffects, player.equippedGlovesInstance.GlovesName));
        if (player.equippedBootsInstance?.data   != null) allEffects.Add((player.equippedBootsInstance.StatusEffects,  player.equippedBootsInstance.BootsName));
        if (player.equippedJewelryInstances != null)
            foreach (var jewelry in player.equippedJewelryInstances)
                if (jewelry != null) allEffects.Add((jewelry.StatusEffects, jewelry.JewelryName));

        foreach (var (effects, sourceName) in allEffects)
        {
            if (effects == null) continue;
            foreach (var entry in effects)
            {
                if (entry == null || entry.effect == null || !entry.Roll()) continue;
                switch (entry.effect)
                {
                    case BuffData buff:
                        statusSystem.ApplyBuff(buff, player);
                        break;
                    case DebuffData debuff:
                        if (statusSystem.TryApplyDebuff(debuff, player))
                            Debug.Log($"[EQUIP] {sourceName} → {debuff.effectName.Get(LocalizationManager.CurrentLanguage)} sur {target.entityName}.");
                        break;
                }
            }
        }
    }
```

Ensuite, supprimer les 10 sites d'appel (chercher `ApplyWeaponStatusEffects(caster as Player` —
il y a exactement 10 occurrences, chacune précédée de `ApplyStatusEffects(skill, caster,
<entity>);` et suivie de `CheckKill(<entity>);`). Chaque occurrence ressemble à :
```csharp
            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            if (skill.effectType == SkillEffectType.Damage && caster.entityType == EntityType.Player)
                ApplyWeaponStatusEffects(caster as Player, entity);
            CheckKill(entity);
```
(le nom de variable cible varie : `target`, `entity`, `closest` selon la méthode — garder le
nom exact déjà présent). Supprimer uniquement les 2 lignes du `if`/appel, garder
`ApplyEffectType(...)`, `ApplyStatusEffects(...)` et `CheckKill(...)` intactes :
```csharp
            ApplyEffectType(skill, caster, entity);
            ApplyStatusEffects(skill, caster, entity);
            CheckKill(entity);
```

Aussi supprimer, dans `ExecuteMultiHit` (le seul appel avec `step.statusEffects`, PAS
`ApplyWeaponStatusEffects` — NE PAS toucher ce bloc, il consomme `HitStep.statusEffects`, un
champ SÉPARÉ et hors scope, pas `EquipmentConfig.statusEffects`) : rien à faire ici, ce bloc
reste identique.

- [ ] **Step 9: Vérifier la compilation**

Ouvrir Unity, attendre la recompilation complète. 0 erreur attendue. Si une erreur mentionne
`StatusEffects`/`OnHitEffects` introuvable quelque part, c'est un site manqué au Step 3 ou 7 —
grep `\.StatusEffects\b` et `\.OnHitEffects\b` dans tout `Assets/Scripts` pour confirmer 0
résultat restant (en dehors de `RuneData.cs`/`SkillData.cs`, qui ont leur PROPRE champ
`statusEffects`, sans rapport).

- [ ] **Step 10: Test manuel**

Ouvrir un `WeaponData` existant dans l'Inspector — vérifier que les sections "Effets On-Hit
reçus"/"Effets On-Hit infligés" apparaissent (vides, puisque rupture propre — Florian les
reconfigure lui-même plus tard). Ouvrir un `MobData`/`PNJData` — vérifier que les mêmes
sections apparaissent aussi (nouveau pour eux).

- [ ] **Step 11: Commit**

```bash
git add "Data/StatusEffect/OnHitEffectData.cs" "Data/StatusEffect/OnHitReceivedEffectData.cs" \
        "Data/Equipment/EquipmentConfig.cs" "Data/Equipment/ArmorData.cs" "Data/Equipment/BootsData.cs" \
        "Data/Equipment/CosmeticDataBody.cs" "Data/Equipment/CosmeticDataHead.cs" "Data/Equipment/HelmetData.cs" \
        "Data/Equipment/GlovesData.cs" "Data/Equipment/JewelryData.cs" "Data/Equipment/WeaponData.cs" \
        "Data/Skills/PermanentSkillData.cs" "Data/Mobs/MobData.cs" "Data/PNJ/PNJData.cs" \
        "UI/Shared/TooltipSystem.cs" "Combat/SkillSystem.cs"
git commit -m "refactor: rename OnHitEffectData to OnHitReceivedEffectData, wire onHitDealtEffects everywhere, remove legacy statusEffects proc system"
```

---

### Task 3: `Entity.cs` — polymorphisme + centralisation des réactions RECEIVED, `Player.cs` — overrides

**Files:**
- Modify: `Entities/Entity.cs`
- Modify: `Entities/Player.cs`

**Interfaces:**
- Consumes: `OnHitReceivedEffectType`/`OnHitReceivedEffectData`/`OnHitReceivedEffectEntry`,
  `OnHitDealtEffectEntry` (Tâches 1-2)
- Produces: `Entity.GetOnHitDealtEffects()` (virtual, `List<OnHitDealtEffectEntry>`),
  `Entity.GetOnHitReceivedEffects()` (virtual, `List<OnHitReceivedEffectEntry>`),
  `Entity.ApplyOnHitReceivedEffects(float, Entity)`, `Entity.DealOnHitCounterDamage(float,
  OnHitReceivedEffectData, Entity)` — consommés par les Tâches 4, 5, 6.

- [ ] **Step 1: `Entities/Entity.cs` — ajouter les 2 méthodes virtuelles + la logique de réaction + le hook dans `TakeDamage`**

Trouver la méthode `TakeDamage` :
```csharp
    public virtual void TakeDamage(float amount,
                                   ElementType sourceElement = ElementType.Neutral,
                                   Entity source = null)
    {
        if (isDead || amount <= 0f) return;

        // Invincible — annule les dégâts | Sleep — réveil au premier dégât
        if (statusEffects != null && statusEffects.OnTakeDamage()) return;

        // Shield — absorbe les dégâts en priorité
        if (statusEffects != null)
            amount = statusEffects.AbsorbWithShield(amount);

        if (amount <= 0f) return;

        currentHP = Mathf.Max(0f, currentHP - amount);

        if (currentHP <= 0f)
        {
            // ── OnFatalHit — intercept AVANT Die() ───────────────
            // PassiveSkillSystem.CanSurviveFatalHit() évalue les passives OnFatalHit,
            // applique leurs effets, et retourne true si le joueur survit.
            Player playerSelf = this as Player;
            if (playerSelf != null &&
                PassiveSkillSystem.Instance != null &&
                PassiveSkillSystem.Instance.CanSurviveFatalHit())
            {
                currentHP = 1f;
            }
            else
            {
                Die();
            }
        }
    }
```

Remplacer par (ajout du hook `ApplyOnHitReceivedEffects` en toute fin, APRÈS la résolution
mort/survie — même timing que l'ancien `Player.ApplyOnHitEffects()`, qui s'exécutait après
`base.TakeDamage()` complet) :
```csharp
    public virtual void TakeDamage(float amount,
                                   ElementType sourceElement = ElementType.Neutral,
                                   Entity source = null)
    {
        if (isDead || amount <= 0f) return;

        // Invincible — annule les dégâts | Sleep — réveil au premier dégât
        if (statusEffects != null && statusEffects.OnTakeDamage()) return;

        // Shield — absorbe les dégâts en priorité
        if (statusEffects != null)
            amount = statusEffects.AbsorbWithShield(amount);

        if (amount <= 0f) return;

        currentHP = Mathf.Max(0f, currentHP - amount);

        if (currentHP <= 0f)
        {
            // ── OnFatalHit — intercept AVANT Die() ───────────────
            // PassiveSkillSystem.CanSurviveFatalHit() évalue les passives OnFatalHit,
            // applique leurs effets, et retourne true si le joueur survit.
            Player playerSelf = this as Player;
            if (playerSelf != null &&
                PassiveSkillSystem.Instance != null &&
                PassiveSkillSystem.Instance.CanSurviveFatalHit())
            {
                currentHP = 1f;
            }
            else
            {
                Die();
            }
        }

        // Réactions On-Hit reçues (Thorns/Reflect/HealOnHit/CounterDebuff/CounterBuff) —
        // centralisé ici (au lieu de Player uniquement) pour que Mob/PNJ en bénéficient aussi
        // via leur propre override de GetOnHitReceivedEffects(). `amount` ici reflète déjà le
        // Shield (contrairement à l'ancien Player.ApplyOnHitEffects, qui recevait la valeur
        // AVANT absorption de bouclier — précision, pas un changement de comportement visible
        // puisque le montant absorbé n'était de toute façon jamais la cible d'un proc réel).
        if (source != null)
            ApplyOnHitReceivedEffects(amount, source);
    }

    /// <summary>Effets On-Hit infligés actifs sur cette entité (équipement+permanents pour
    /// Player, MobData/PNJData pour Mob/PNJ). Null par défaut — override dans les sous-classes
    /// concernées. Consommé par SkillSystem (réactions) et CombatSystem (DamageAmpOnHit).</summary>
    public virtual List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => null;

    /// <summary>Effets On-Hit reçus actifs sur cette entité — voir GetOnHitDealtEffects().
    /// Consommé par ApplyOnHitReceivedEffects (réactions) et CombatSystem
    /// (DamageReductionOnHit).</summary>
    public virtual List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => null;

    /// <summary>Applique les effets On-Hit REÇUS (Thorns/ReflectPercent/HealOnHit/
    /// CounterDebuff/CounterBuff) de cette entité quand elle reçoit un coup. DamageReductionOnHit
    /// n'apparaît pas ici — c'est un modificateur, déjà consommé dans CombatSystem au moment
    /// du calcul, pas une réaction post-coup.</summary>
    private void ApplyOnHitReceivedEffects(float damageTaken, Entity attacker)
    {
        var effects = GetOnHitReceivedEffects();
        if (effects == null) return;

        foreach (var entry in effects)
        {
            if (entry?.effect == null || !entry.Roll()) continue;
            switch (entry.effect.effectType)
            {
                case OnHitReceivedEffectType.Thorns:
                    DealOnHitCounterDamage(entry.effect.thornsDamage, entry.effect, attacker);
                    break;
                case OnHitReceivedEffectType.ReflectPercent:
                    DealOnHitCounterDamage(damageTaken * entry.effect.reflectPercent, entry.effect, attacker);
                    break;
                case OnHitReceivedEffectType.HealOnHit:
                    Heal(entry.effect.GetHealAmount(MaxHP));
                    break;
                case OnHitReceivedEffectType.CounterDebuff:
                    if (entry.effect.counterDebuff != null && attacker.statusEffects != null)
                        attacker.statusEffects.TryApplyDebuff(entry.effect.counterDebuff, this);
                    break;
                case OnHitReceivedEffectType.CounterBuff:
                    if (entry.effect.counterBuff != null && statusEffects != null)
                        statusEffects.ApplyBuff(entry.effect.counterBuff, this);
                    break;
            }
        }
    }

    /// <summary>Applique les dégâts d'un Thorns/ReflectPercent à l'attaquant. `pierceDefense`
    /// (Inspector: "Dégâts renvoyés bruts") true → montant tel quel. False → réduit comme un
    /// dégât normal : formule physique quadratique (défense mêlée) si reflectElement = Neutral,
    /// résistance élémentaire linéaire sinon — même split physique/élémentaire indépendant que
    /// CombatSystem, pas de mélange des deux canaux. Déplacé depuis Player.cs (session
    /// précédente) pour être partagé par toutes les entités.</summary>
    private void DealOnHitCounterDamage(float rawAmount, OnHitReceivedEffectData effect, Entity attacker)
    {
        if (rawAmount <= 0f || attacker == null) return;

        float amount = rawAmount;
        if (!effect.pierceDefense)
        {
            if (effect.reflectElement == ElementType.Neutral)
            {
                float def = attacker.GetMeleeDefense();
                amount = (rawAmount * rawAmount) / (rawAmount + def * 1.5f);
            }
            else
            {
                float resist = attacker.GetElementalResistance(effect.reflectElement);
                amount = rawAmount * (1f - Mathf.Clamp01(resist));
            }
        }

        attacker.TakeDamage(amount, effect.reflectElement, this);
    }
```

Vérifier en tête de fichier que `using System.Collections.Generic;` est déjà présent (pour
`List<>`) — c'est le cas dans tous les fichiers de ce projet vus jusqu'ici, mais à confirmer.

- [ ] **Step 2: `Entities/Player.cs` — override les 2 méthodes virtuelles, supprimer l'ancien `ApplyOnHitEffects`/`DealOnHitCounterDamage`/leur call site**

Trouver et supprimer entièrement ce bloc (méthodes `ApplyOnHitEffects` et
`DealOnHitCounterDamage`, maintenant dupliquées avec la version centralisée dans `Entity.cs`) :
```csharp
    /// <summary>
    /// Applique les effets On-Hit de tout l'équipement quand le joueur reçoit un coup.
    /// Chaque effet a une chance d'activation définie sur OnHitEffectData.
    /// Les effets sont lus depuis instance.OnHitEffects → data.config.onHitEffects.
    /// GDD §5.1 à §5.6 — unifié via EquipmentConfig.
    /// </summary>
    private void ApplyOnHitEffects(float damageTaken, Entity attacker)
    {
        var allOnHit = new List<List<OnHitEffectEntry>>();

        // Équipements via EquipmentConfig.onHitEffects
        if (equippedWeaponInstance?.OnHitEffects != null) allOnHit.Add(equippedWeaponInstance.OnHitEffects);
        if (equippedArmorInstance?.OnHitEffects  != null) allOnHit.Add(equippedArmorInstance.OnHitEffects);
        if (equippedHelmetInstance?.OnHitEffects != null) allOnHit.Add(equippedHelmetInstance.OnHitEffects);
        if (equippedGlovesInstance?.OnHitEffects != null) allOnHit.Add(equippedGlovesInstance.OnHitEffects);
        if (equippedBootsInstance?.OnHitEffects  != null) allOnHit.Add(equippedBootsInstance.OnHitEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitEffects != null) allOnHit.Add(j.OnHitEffects);

        // Skills permanents — liste propre (pas via EquipmentConfig)
        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitEffects != null && p.onHitEffects.Count > 0)
                    allOnHit.Add(p.onHitEffects);

        foreach (var list in allOnHit)
        {
            if (list == null) continue;
            foreach (var entry in list)
            {
                if (entry?.effect == null || !entry.Roll()) continue;
                switch (entry.effect.effectType)
                {
                    case OnHitEffectType.Thorns:
                        DealOnHitCounterDamage(entry.effect.thornsDamage, entry.effect, attacker);
                        break;
                    case OnHitEffectType.ReflectPercent:
                        DealOnHitCounterDamage(damageTaken * entry.effect.reflectPercent, entry.effect, attacker);
                        break;
                    case OnHitEffectType.HealOnHit:
                        Heal(entry.effect.GetHealAmount(MaxHP));
                        break;
                    case OnHitEffectType.CounterDebuff:
                        if (entry.effect.counterDebuff != null && attacker.statusEffects != null)
                            attacker.statusEffects.TryApplyDebuff(entry.effect.counterDebuff, this);
                        break;
                    case OnHitEffectType.CounterBuff:
                        if (entry.effect.counterBuff != null && statusEffects != null)
                            statusEffects.ApplyBuff(entry.effect.counterBuff, this);
                        break;
                }
            }
        }
    }

    /// <summary>Applique les dégâts d'un Thorns/ReflectPercent à l'attaquant. `pierceDefense`
    /// (Inspector: "Dégâts renvoyés bruts") true → montant tel quel. False → réduit comme un
    /// dégât normal : formule physique quadratique (défense mêlée) si reflectElement = Neutral,
    /// résistance élémentaire linéaire sinon — même split physique/élémentaire indépendant que
    /// CombatSystem, pas de mélange des deux canaux.</summary>
    private void DealOnHitCounterDamage(float rawAmount, OnHitEffectData effect, Entity attacker)
    {
        if (rawAmount <= 0f || attacker == null) return;

        float amount = rawAmount;
        if (!effect.pierceDefense)
        {
            if (effect.reflectElement == ElementType.Neutral)
            {
                float def = attacker.GetMeleeDefense();
                amount = (rawAmount * rawAmount) / (rawAmount + def * 1.5f);
            }
            else
            {
                float resist = attacker.GetElementalResistance(effect.reflectElement);
                amount = rawAmount * (1f - Mathf.Clamp01(resist));
            }
        }

        attacker.TakeDamage(amount, effect.reflectElement, this);
    }
```

Puis, à l'endroit exact où ce bloc supprimé se trouvait, insérer les 2 overrides à la place :
```csharp
    /// <summary>Agrège les effets On-Hit infligés de tout l'équipement + permanents débloqués.</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects()
    {
        var all = new List<OnHitDealtEffectEntry>();

        if (equippedWeaponInstance?.OnHitDealtEffects != null) all.AddRange(equippedWeaponInstance.OnHitDealtEffects);
        if (equippedArmorInstance?.OnHitDealtEffects  != null) all.AddRange(equippedArmorInstance.OnHitDealtEffects);
        if (equippedHelmetInstance?.OnHitDealtEffects != null) all.AddRange(equippedHelmetInstance.OnHitDealtEffects);
        if (equippedGlovesInstance?.OnHitDealtEffects != null) all.AddRange(equippedGlovesInstance.OnHitDealtEffects);
        if (equippedBootsInstance?.OnHitDealtEffects  != null) all.AddRange(equippedBootsInstance.OnHitDealtEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitDealtEffects != null) all.AddRange(j.OnHitDealtEffects);

        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitDealtEffects != null) all.AddRange(p.onHitDealtEffects);

        return all;
    }

    /// <summary>Agrège les effets On-Hit reçus de tout l'équipement + permanents débloqués.</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects()
    {
        var all = new List<OnHitReceivedEffectEntry>();

        if (equippedWeaponInstance?.OnHitReceivedEffects != null) all.AddRange(equippedWeaponInstance.OnHitReceivedEffects);
        if (equippedArmorInstance?.OnHitReceivedEffects  != null) all.AddRange(equippedArmorInstance.OnHitReceivedEffects);
        if (equippedHelmetInstance?.OnHitReceivedEffects != null) all.AddRange(equippedHelmetInstance.OnHitReceivedEffects);
        if (equippedGlovesInstance?.OnHitReceivedEffects != null) all.AddRange(equippedGlovesInstance.OnHitReceivedEffects);
        if (equippedBootsInstance?.OnHitReceivedEffects  != null) all.AddRange(equippedBootsInstance.OnHitReceivedEffects);
        if (equippedJewelryInstances != null)
            foreach (var j in equippedJewelryInstances)
                if (j?.OnHitReceivedEffects != null) all.AddRange(j.OnHitReceivedEffects);

        if (unlockedPermanents != null)
            foreach (var p in unlockedPermanents)
                if (p?.onHitReceivedEffects != null) all.AddRange(p.onHitReceivedEffects);

        return all;
    }
```

Enfin, dans `Player.TakeDamage`, trouver et supprimer l'appel à l'ancienne méthode (juste après
le bloc `if (hpBefore > 1f && currentHP <= 1f) activityCounter.Increment(...)`) :
```csharp
        if (source != null)
            ApplyOnHitEffects(amount, source);
    }
```
Remplacer par (fermeture simple de la méthode, plus rien à faire ici — le hook vit maintenant
dans `Entity.TakeDamage`, appelé via `base.TakeDamage(...)` un peu plus haut dans cette même
méthode) :
```csharp
    }
```

- [ ] **Step 3: Vérifier la compilation**

0 erreur attendue. Points à vérifier particulièrement : `Player.cs` ne référence plus
`OnHitEffectType`/`OnHitEffectData`/`OnHitEffectEntry` nulle part (grep pour confirmer), et
`List<List<OnHitEffectEntry>>` (l'ancien type de `allOnHit`) a bien disparu.

- [ ] **Step 4: Test manuel (Play Mode)**

Créer un `OnHitReceivedEffectData` type `Thorns` (10 dégâts, 100% chance), l'assigner sur
`onHitReceivedEffects` d'une armure équipée. Se faire toucher par un mob mannequin → vérifier
que le mob prend 10 dégâts en retour (log `CombatSystem.debugDamage` si besoin). Créer un
`OnHitReceivedEffectData` type `CounterBuff` avec un `BuffData` (Stats, MoveSpeed +10%,
Percent) → se faire toucher → vérifier que LE JOUEUR (pas l'attaquant) reçoit le buff (déjà le
cas avant ce refactor pour CounterBuff côté reçu — juste confirmer que rien n'a régressé).

- [ ] **Step 5: Commit**

```bash
git add "Entities/Entity.cs" "Entities/Player.cs"
git commit -m "refactor: centralize on-hit-received reactions in Entity, add OnHitDealt/Received polymorphism"
```

---

### Task 4: `Mob.cs` + `PNJ.cs` — overrides des méthodes virtuelles

**Files:**
- Modify: `Entities/Mob.cs`
- Modify: `Entities/PNJ.cs`

**Interfaces:**
- Consumes: `Entity.GetOnHitDealtEffects()`/`GetOnHitReceivedEffects()` (virtual, Tâche 3),
  `MobData.onHitDealtEffects`/`onHitReceivedEffects`, `PNJData.onHitDealtEffects`/`onHitReceivedEffects`
  (Tâche 2)
- Produces: overrides concrets consommés par `Entity.TakeDamage` (déjà câblé, Tâche 3) et par
  `CombatSystem`/`SkillSystem` (Tâches 5, 6)

- [ ] **Step 1: `Entities/Mob.cs` — ajouter les 2 overrides**

Trouver un emplacement logique — par exemple juste avant `protected override void Die()`
(chercher `protected override void Die()` dans `Mob.cs`) — et insérer juste avant :
```csharp
    /// <summary>Effets On-Hit infligés de ce mob — source unique, MobData, pas d'agrégation
    /// (contrairement à Player qui combine plusieurs pièces d'équipement).</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => data?.onHitDealtEffects;

    /// <summary>Effets On-Hit reçus de ce mob — voir GetOnHitDealtEffects().</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => data?.onHitReceivedEffects;

```

- [ ] **Step 2: `Entities/PNJ.cs` — ajouter les 2 overrides**

Trouver `protected override void Die()` dans `PNJ.cs` et insérer juste avant :
```csharp
    /// <summary>Effets On-Hit infligés de ce PNJ — source unique, PNJData.</summary>
    public override List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => data?.onHitDealtEffects;

    /// <summary>Effets On-Hit reçus de ce PNJ — voir GetOnHitDealtEffects().</summary>
    public override List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => data?.onHitReceivedEffects;

```

(Vérifier le nom exact du champ `MobData`/`PNJData` sur `Mob`/`PNJ` — dans les deux cas c'est
`data`, déjà utilisé abondamment ailleurs dans ces fichiers, ex: `data.baseAtkMin`.)

- [ ] **Step 3: Vérifier la compilation**

0 erreur attendue.

- [ ] **Step 4: Test manuel (Play Mode)**

Sur le `MobData` d'un mob mannequin, ajouter un `OnHitReceivedEffectData` type `Thorns` (100%
chance) dans `onHitReceivedEffects`. Attaquer ce mob en mêlée → vérifier que LE JOUEUR reçoit
des dégâts en retour (preuve que le hook centralisé de la Tâche 3 fonctionne aussi pour Mob,
sans code dupliqué).

- [ ] **Step 5: Commit**

```bash
git add "Entities/Mob.cs" "Entities/PNJ.cs"
git commit -m "feat: wire OnHitDealt/OnHitReceived to Mob and PNJ via MobData/PNJData"
```

---

### Task 5: `Combat/SkillSystem.cs` — réactions DEALT (Lifesteal/Mana/ApplyDebuff/ApplyBuff)

**Files:**
- Modify: `Combat/SkillSystem.cs`

**Interfaces:**
- Consumes: `Entity.GetOnHitDealtEffects()` (Tâche 3), `OnHitDealtEffectType`/
  `OnHitDealtEffectEntry` (Tâche 1)
- Produces: `SkillSystem.ApplyOnHitDealtEffects(Entity, Entity, float)` — méthode privée, pas
  consommée hors de ce fichier

- [ ] **Step 1: Ajouter la nouvelle méthode**

Juste après la méthode `CalculateDamage` (chercher `private float CalculateDamage(SkillData
skill, Entity caster, Entity target, out bool isCrit)` et sa fermeture `}`), insérer :
```csharp
    // =========================================================
    // EFFETS SECONDAIRES — On-Hit infligés (réactions, PAS DamageAmpOnHit —
    // celui-ci est un modificateur consommé dans CombatSystem.CalculateDamage/
    // CalculateMobDamage, jamais ici)
    // =========================================================

    /// <summary>Applique les effets On-Hit INFLIGÉS (Lifesteal/Mana/ApplyDebuff/ApplyBuff) de
    /// l'attaquant après un coup qui a effectivement touché (jamais appelée sur un Miss — les
    /// call sites sont placés juste après un target.TakeDamage(dmg,...) réussi).</summary>
    private void ApplyOnHitDealtEffects(Entity attacker, Entity target, float damageDealt)
    {
        var effects = attacker?.GetOnHitDealtEffects();
        if (effects == null) return;

        foreach (var entry in effects)
        {
            if (entry?.effect == null || !entry.Roll()) continue;
            switch (entry.effect.effectType)
            {
                case OnHitDealtEffectType.LifestealOnHit:
                    attacker.Heal(entry.effect.GetLifestealAmount(damageDealt));
                    break;

                case OnHitDealtEffectType.ManaOnHit:
                    attacker.RecoverMana(entry.effect.GetManaAmount(attacker.MaxMana));
                    break;

                case OnHitDealtEffectType.ApplyDebuffOnHit:
                    if (entry.effect.debuffToApply != null && target?.statusEffects != null)
                        target.statusEffects.TryApplyDebuff(entry.effect.debuffToApply, attacker);
                    break;

                case OnHitDealtEffectType.ApplyBuffOnHit:
                    if (entry.effect.buffToApply != null && attacker.statusEffects != null)
                        attacker.statusEffects.ApplyBuff(entry.effect.buffToApply, attacker);
                    break;

                // DamageAmpOnHit : modificateur, jamais géré ici — voir CombatSystem.
            }
        }
    }

```

- [ ] **Step 2: Appeler cette méthode aux 3 sites qui infligent réellement des dégâts**

**Site 1 — `ExecuteMultiHit`** (chercher `target.TakeDamage(dmg, step.element, caster);` dans
`ExecuteMultiHit`) :
```csharp
            target.TakeDamage(dmg, step.element, caster);
```
Remplacer par :
```csharp
            target.TakeDamage(dmg, step.element, caster);
            ApplyOnHitDealtEffects(caster, target, dmg);
```

**Site 2 — `ApplyEffectType`, cas `SkillEffectType.Damage`** (chercher `target.TakeDamage(dmg,
skill.PrimaryElement, caster);` — celui qui suit immédiatement le check `RollDodge`) :
```csharp
                target.TakeDamage(dmg, skill.PrimaryElement, caster);
```
Remplacer par :
```csharp
                target.TakeDamage(dmg, skill.PrimaryElement, caster);
                ApplyOnHitDealtEffects(caster, target, dmg);
```
(Ce site est déjà APRÈS le `break;` du cas Miss — donc jamais atteint sur un coup raté, exactement
la garantie décrite dans la spec §Pipeline.)

**Site 3 — `ApplySpecialEffect`, cas `SkillSpecialEffect.DrainHP`** (chercher
`target.TakeDamage(dmg, skill.PrimaryElement, caster);` À L'INTÉRIEUR du bloc `case
SkillSpecialEffect.DrainHP:` — attention, c'est la MÊME ligne de code texte que le Site 2, bien
vérifier qu'on édite l'occurrence dans le bloc `DrainHP`, pas une seconde fois celle
d'`ApplyEffectType`) :
```csharp
                target.TakeDamage(dmg, skill.PrimaryElement, caster);
                float healed = dmg * skill.drainHealRatio;
                caster.Heal(healed);
```
Remplacer par :
```csharp
                target.TakeDamage(dmg, skill.PrimaryElement, caster);
                ApplyOnHitDealtEffects(caster, target, dmg);
                float healed = dmg * skill.drainHealRatio;
                caster.Heal(healed);
```

- [ ] **Step 3: Vérifier la compilation**

0 erreur attendue.

- [ ] **Step 4: Test manuel (Play Mode)**

Créer un `OnHitDealtEffectData` type `LifestealOnHit` (Percent, 20%, 100% chance), assigner sur
l'arme équipée dans `onHitDealtEffects`. Attaquer un mob → vérifier un heal de 20% des dégâts
infligés à chaque coup. Créer un `OnHitDealtEffectData` type `ApplyBuffOnHit` (100% chance,
`BuffData` Stats MoveSpeed +10%) → attaquer → vérifier que LE JOUEUR (attaquant) reçoit le
buff — PAS le mob (c'est le bug que ce refactor corrige). Vérifier aussi qu'un Miss (dodge
élevé sur la cible) n'applique aucune réaction Lifesteal/Mana/Debuff/Buff.

- [ ] **Step 5: Commit**

```bash
git add "Combat/SkillSystem.cs"
git commit -m "feat: apply OnHitDealt reactions (Lifesteal/Mana/ApplyDebuff/ApplyBuff) after successful hits"
```

---

### Task 6: `Combat/CombatSystem.cs` — modificateurs DamageAmpOnHit / DamageReductionOnHit

**Files:**
- Modify: `Combat/CombatSystem.cs`

**Interfaces:**
- Consumes: `Entity.GetOnHitDealtEffects()`/`GetOnHitReceivedEffects()` (Tâche 3),
  `OnHitDealtEffectType.DamageAmpOnHit`/`OnHitReceivedEffectType.DamageReductionOnHit` (Tâches 1-2)
- Produces: rien de nouveau exposé — modifie le résultat de `CalculateDamage`/`CalculateMobDamage`

- [ ] **Step 1: Variante PLAYER — `CalculateDamage`**

Trouver ce bloc (juste après le crit, juste avant `── 6. Total`) :
```csharp
            float elemCritMult = 1f + (effectiveCritMult - 1f) * 0.5f;
            elemFinal *= elemCritMult;

            isCrit = true;
        }

        // ── 6. Total ──────────────────────────────────────────
        float totalDamage = physDamage + elemFinal;
```
Remplacer par :
```csharp
            float elemCritMult = 1f + (effectiveCritMult - 1f) * 0.5f;
            elemFinal *= elemCritMult;

            isCrit = true;
        }

        // ── Amplification / Réduction On-Hit — même stage que le critique, roulé
        // indépendamment (peut proc en même temps que le crit) ──
        var dealtEffects = attacker?.GetOnHitDealtEffects();
        if (dealtEffects != null)
            foreach (var entry in dealtEffects)
                if (entry?.effect != null && entry.effect.effectType == OnHitDealtEffectType.DamageAmpOnHit && entry.Roll())
                {
                    physDamage *= (1f + entry.effect.ampPercent);
                    elemFinal  *= (1f + entry.effect.ampPercent);
                }

        var receivedEffects = target?.GetOnHitReceivedEffects();
        if (receivedEffects != null)
            foreach (var entry in receivedEffects)
                if (entry?.effect != null && entry.effect.effectType == OnHitReceivedEffectType.DamageReductionOnHit && entry.Roll())
                {
                    physDamage *= (1f - entry.effect.reductionPercent);
                    elemFinal  *= (1f - entry.effect.reductionPercent);
                }

        // ── 6. Total ──────────────────────────────────────────
        float totalDamage = physDamage + elemFinal;
```

- [ ] **Step 2: Variante MOB/PNJ — `CalculateMobDamage`**

**Attention** : cette variante N'A PAS de bloc critique (`CalculateMobDamage` ne calcule aucun
critique) — l'ancrage n'est donc PAS "après le crit" ici, mais directement avant le calcul du
total. Trouver ce bloc :
```csharp
                elemDamage *= (1f - Mathf.Clamp01(elemResist));
            }
        }

        float total = physDamage + elemDamage;
```
Remplacer par (variables différentes de la variante PLAYER : `caster`/`elemDamage` au lieu de
`attacker`/`elemFinal`) :
```csharp
                elemDamage *= (1f - Mathf.Clamp01(elemResist));
            }
        }

        // ── Amplification / Réduction On-Hit — mêmes types que côté joueur, roulés ici
        // faute de bloc critique dans cette variante (CalculateMobDamage n'a pas de crit) ──
        var dealtEffects = caster?.GetOnHitDealtEffects();
        if (dealtEffects != null)
            foreach (var entry in dealtEffects)
                if (entry?.effect != null && entry.effect.effectType == OnHitDealtEffectType.DamageAmpOnHit && entry.Roll())
                {
                    physDamage *= (1f + entry.effect.ampPercent);
                    elemDamage *= (1f + entry.effect.ampPercent);
                }

        var receivedEffects = target?.GetOnHitReceivedEffects();
        if (receivedEffects != null)
            foreach (var entry in receivedEffects)
                if (entry?.effect != null && entry.effect.effectType == OnHitReceivedEffectType.DamageReductionOnHit && entry.Roll())
                {
                    physDamage *= (1f - entry.effect.reductionPercent);
                    elemDamage *= (1f - entry.effect.reductionPercent);
                }

        float total = physDamage + elemDamage;
```

- [ ] **Step 3: Vérifier la compilation**

0 erreur attendue.

- [ ] **Step 4: Test manuel (Play Mode)**

Créer un `OnHitDealtEffectData` type `DamageAmpOnHit` (10%, 100% chance) sur l'arme du joueur,
et un `OnHitReceivedEffectData` type `DamageReductionOnHit` (30%, 100% chance) sur le mob
mannequin. Activer `CombatSystem.debugDamage`, attaquer le mob, lire le log de dégâts —
vérifier que le montant final reflète bien ×1.10 puis ×0.70 (ou l'ordre inverse — l'ordre entre
les deux blocs n'affecte pas le résultat, la multiplication étant commutative) par rapport au
montant sans les deux effets. Refaire le même test en attaquant depuis un mob équipé d'un
`OnHitDealtEffectData` `DamageAmpOnHit` sur son `MobData` (variante `CalculateMobDamage`).

- [ ] **Step 5: Commit**

```bash
git add "Combat/CombatSystem.cs"
git commit -m "feat: apply DamageAmpOnHit/DamageReductionOnHit modifiers in damage calculation"
```

---

### Task 7: Vérification manuelle finale (Play Mode)

**Files:** aucun — tâche de vérification pure, reprend et complète les tests déjà faits tâche
par tâche.

- [ ] **Step 1: Checklist complète (reprend §Vérification de la spec)**

1. Compiler le projet en entier, 0 erreur.
2. `LifestealOnHit` (Percent 10%) sur une arme → taper un mob → heal proportionnel aux dégâts,
   à la fréquence de `chance`.
3. `ApplyBuffOnHit` avec un `BuffData` (Stats AttackDamage) → LE JOUEUR reçoit le buff en
   tapant, jamais le mob.
4. `DamageAmpOnHit` (10%, joueur) + `DamageReductionOnHit` (30%, mob cible) simultanés →
   log `CombatSystem.debugDamage` reflète les deux multiplicateurs quand ils proc ensemble.
5. Un mob équipé d'un `OnHitReceivedEffectData` Thorns renvoie des dégâts au joueur qui
   l'attaque en mêlée.
6. Miss (dodge élevé sur la cible) → aucune réaction DEALT/RECEIVED ne se déclenche
   visiblement sur ce coup raté.
7. Un `OnHitReceivedEffectData` sur un `PermanentSkillData` débloqué fonctionne toujours
   (renommage de champ, pas de changement de comportement attendu).
8. Un PNJ combattant (`canFight = true`) équipé d'un `OnHitDealtEffectData` sur son `PNJData`
   applique bien la réaction en touchant le joueur.

- [ ] **Step 2: Rapport**

Florian confirme chaque point de la checklist en Play Mode. Si un point échoue, revenir à la
tâche correspondante (1-6) pour corriger — pas de nouvelle tâche à créer pour un bug de
régression sur ce périmètre, fixer dans le fichier concerné directement.
