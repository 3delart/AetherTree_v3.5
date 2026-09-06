# Refonte OnHitDealt / OnHitReceived — Design

## Contexte

Audit complet des buffs/debuffs (session du 2026-09-06) a mené à examiner les mécanismes de
proc "à chaque coup", révélant 3 systèmes qui se chevauchent et un bug :

1. **`OnHitEffectData`** (`Data/StatusEffect/OnHitEffectData.cs`) — côté REÇU. 5 types
   (Thorns, ReflectPercent, HealOnHit, CounterDebuff, CounterBuff). Placé sur
   `EquipmentConfig.onHitEffects` + `PermanentSkillData.onHitEffects`. Consommé par
   `Player.ApplyOnHitEffects()` à chaque `TakeDamage()`. **Player uniquement** — Mob/PNJ n'ont
   aucun équivalent.

2. **`EquipmentConfig.statusEffects`** (`List<StatusEffectEntry>`) — côté DONNÉ. Glisse un
   `BuffData` ou `DebuffData` générique, chance par attaque. Consommé par
   `SkillSystem.ApplyWeaponStatusEffects()` / `ApplyStatusEffectEntry()`.
   **Bug confirmé** : une entrée `BuffData` s'applique sur `target.statusEffects` (la CIBLE
   frappée), jamais sur le porteur de l'équipement — un buff-sur-soi-en-attaquant est
   impossible à configurer correctement aujourd'hui.

3. **`WeaponData.critChance`/`critMultiplier`** — fixes sur le SO, pas un système de proc,
   mais sert d'exemple pour un 3e besoin non couvert : un effet qui **modifie le montant du
   coup en cours** (ex: "5% chance +10% dégâts sur ce coup"), distinct des effets qui
   **réagissent** à un coup déjà résolu (Lifesteal, debuff-proc, Thorns...).

Décision (Florian, 2026-09-06) : remplacer les 3 par **deux systèmes propres et symétriques** —
`OnHitDealt` (coup donné) et `OnHitReceived` (coup reçu) — placables sur TOUT équipement
(Weapon/Armor/Helmet/Gloves/Boots/Jewelry) ET sur `PermanentSkillData`, ET étendus à
`MobData`/`PNJData` (actuellement sans aucun mécanisme de proc).

**Rupture propre acceptée** — Florian reconfigurera les équipements déjà créés à la main
(verbatim: *"je referais les équipements déjà créer"*). Pas de migration de données à écrire.
Rune/Gem restent hors scope (déjà acté dans le refactor équipement précédent).

## Deux catégories d'effets, deux points d'exécution

Question centrale tranchée avec Florian : est-ce qu'un effet **réagit** à un coup déjà
calculé, ou **modifie** le montant en cours de calcul ?

- **Réaction** (Thorns, ReflectPercent, HealOnHit, CounterDebuff, CounterBuff, LifestealOnHit,
  ManaOnHit, ApplyDebuffOnHit, ApplyBuffOnHit) — le montant du coup est déjà connu et figé.
  S'exécute APRÈS `CombatSystem.CalculateDamage` + après le check Miss/Dodge (donc jamais sur
  un coup raté — déjà garanti par l'ordre actuel du pipeline, voir plus bas).

- **Modificateur** (DamageAmpOnHit, DamageReductionOnHit) — change le nombre lui-même. Doit
  s'exécuter DANS `CombatSystem.CalculateDamage`, au même stage que le critique (après
  défense/résistance, sur `physDamage`/`elemFinal`, avant Mark/FinalDamageBonus/Reduction).
  Roulé et appliqué AVANT le check Miss/Dodge (qui tourne après `CalculateDamage` dans le
  pipeline actuel) — décision explicite Florian : *"on garde l'ordre actuel"*. Conséquence
  acceptée : sur un Miss, le montant (éventuellement amplifié/réduit) est simplement jeté,
  comme aujourd'hui pour tout `dmg` calculé avant un Miss — aucune différence observable.

### Pipeline actuel (vérifié, ne change pas)

`SkillSystem.ApplyEffectType` (`SkillEffectType.Damage`, seul point d'entrée pour TOUS les
dispatchs — AoE, target, attaque de base, tout skill de dégâts) :

```
dmg = CalculateDamage(...)          // ← modificateurs Amp/Reduction ICI, dans CalculateDamage
if (RollDodge(...)) { Miss; break } // ← si Miss, dmg est jeté, rien après ne s'exécute
target.TakeDamage(dmg, ...)         // ← réactions RECEIVED ICI (dans Entity.TakeDamage)
...                                  // ← réactions DEALT ICI (juste après, dans ApplyEffectType)
```

Les réactions RECEIVED et DEALT sont donc déjà, par construction, à l'abri d'un Miss — pas de
garde supplémentaire à écrire pour ça.

## Nouveaux types

### `OnHitDealtEffectData` (nouveau fichier, `Data/StatusEffect/OnHitDealtEffectData.cs`)

```csharp
public enum OnHitDealtEffectType
{
    DamageAmpOnHit,     // Modificateur — amplifie CE coup, % (roulé dans CombatSystem)
    LifestealOnHit,     // Réaction — soigne l'attaquant, Flat ou % des dégâts infligés
    ManaOnHit,          // Réaction — restaure du mana à l'attaquant, Flat ou % MaxMana
    ApplyDebuffOnHit,   // Réaction — applique un DebuffData sur LA CIBLE touchée
    ApplyBuffOnHit,     // Réaction — applique un BuffData sur L'ATTAQUANT (soi) — corrige le bug
}

[CreateAssetMenu(fileName = "NewOnHitDealt", menuName = "AetherTree/StatusEffects/OnHitDealtEffectData")]
public class OnHitDealtEffectData : ScriptableObject
{
    public string effectName = "OnHitDealtEffect";
    public Sprite icon;
    public OnHitDealtEffectType effectType = OnHitDealtEffectType.LifestealOnHit;

    [Range(0f, 1f)] public float chance = 0.15f;

    // ── DamageAmpOnHit ──
    [ShowIf(nameof(effectType), OnHitDealtEffectType.DamageAmpOnHit, Header = "Amplification (DamageAmpOnHit)")]
    [Tooltip("% de dégâts en plus sur CE coup. Ex: 0.10 = +10%.")]
    public float ampPercent = 0.10f;

    // ── LifestealOnHit ──
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit, Header = "Vol de vie (LifestealOnHit)")]
    public ModifierType lifestealModifier = ModifierType.Percent;
    [Tooltip("Flat : montant fixe soigné.\nPercent : % des dégâts infligés CE coup.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.LifestealOnHit)]
    public float lifestealAmount = 0.10f;

    // ── ManaOnHit ──
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit, Header = "Restauration de mana (ManaOnHit)")]
    public ModifierType manaModifier = ModifierType.Flat;
    [Tooltip("Flat : montant fixe.\nPercent : % du MaxMana de l'attaquant.")]
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ManaOnHit)]
    public float manaAmount = 5f;

    // ── ApplyDebuffOnHit ──
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyDebuffOnHit, Header = "Debuff sur la cible (ApplyDebuffOnHit)")]
    public DebuffData debuffToApply;

    // ── ApplyBuffOnHit ──
    [ShowIf(nameof(effectType), OnHitDealtEffectType.ApplyBuffOnHit, Header = "Buff sur soi (ApplyBuffOnHit)")]
    public BuffData buffToApply;

    public bool Roll() => Random.value < chance;
    public float GetLifestealAmount(float damageDealt) =>
        lifestealModifier == ModifierType.Percent ? damageDealt * lifestealAmount : lifestealAmount;
    public float GetManaAmount(float attackerMaxMana) =>
        manaModifier == ModifierType.Percent ? attackerMaxMana * manaAmount : manaAmount;
}

[System.Serializable]
public class OnHitDealtEffectEntry
{
    public OnHitDealtEffectData effect;
    [Range(0f, 1f)] public float chanceOverride = 0f;
    public float EffectiveChance => (chanceOverride > 0f && effect != null) ? chanceOverride : (effect != null ? effect.chance : 0f);
    public bool Roll() => effect != null && Random.value < EffectiveChance;
}
```

### `OnHitReceivedEffectData` (renommage de `OnHitEffectData.cs` — même fichier, nouveau nom)

Garde les 5 types actuels tels quels (y compris le fix déjà livré cette session sur
`pierceDefense` → "Dégâts renvoyés bruts"), ajoute **`DamageReductionOnHit`** :

```csharp
public enum OnHitReceivedEffectType
{
    DamageReductionOnHit, // Modificateur — réduit CE coup reçu, % (roulé dans CombatSystem)
    ReflectPercent,
    Thorns,
    CounterDebuff,
    HealOnHit,
    CounterBuff,
}
```

Nouveau champ pour `DamageReductionOnHit` :
```csharp
[ShowIf(nameof(effectType), OnHitReceivedEffectType.DamageReductionOnHit, Header = "Réduction (DamageReductionOnHit)")]
[Tooltip("% de dégâts en moins sur CE coup reçu. Ex: 0.50 = -50%.")]
public float reductionPercent = 0.30f;
```

Renommer aussi `OnHitEffectEntry` → `OnHitReceivedEffectEntry` (même structure, juste le nom).

**Ordinal safety** : ces deux enums sont NEUFS (pas encore d'assets créés dessus au sens
`DebuffType`/`BuffType` — ce sont des enums internes à un SO déjà recréé de zéro), donc pas de
contrainte d'ordre héritée. Garder l'ordre de déclaration ci-dessus pour la suite (les
"modificateurs" en premier dans chaque enum, par cohérence de lecture).

## Points d'attache — équipement, permanents, Mob, PNJ

**`Data/Equipment/EquipmentConfig.cs`** :
- Supprimer `statusEffects` (bugué, remplacé) et `onHitEffects` (renommé).
- Ajouter :
```csharp
[Header("Effets On-Hit reçus (déclenchés quand le porteur reçoit un coup)")]
public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();

[Header("Effets On-Hit infligés (déclenchés quand le porteur touche une cible)")]
public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
```

**8 classes d'équipement** (`ArmorData`, `BootsData`, `CosmeticDataBody`, `CosmeticDataHead`,
`HelmetData`, `GlovesData`, `JewelryData`, `WeaponData`) — chacune expose aujourd'hui :
```csharp
public List<OnHitEffectEntry> OnHitEffects => data?.config?.onHitEffects;
public List<StatusEffectEntry> StatusEffects => data?.config?.statusEffects; // les 8 classes ont les 2 propriétés
```
→ Remplacer par :
```csharp
public List<OnHitReceivedEffectEntry> OnHitReceivedEffects => data?.config?.onHitReceivedEffects;
public List<OnHitDealtEffectEntry>    OnHitDealtEffects    => data?.config?.onHitDealtEffects;
```
(`StatusEffects` disparaît de ces 8 classes — plus aucun consommateur après le retrait de
`ApplyWeaponStatusEffects`. Ne pas confondre avec `SkillData.statusEffects` — effet secondaire
propre à UN skill précis, appliqué à son propre cast, mécanisme totalement différent et non
concerné par ce refactor — ni avec `RuneData.statusEffects`, hors scope par décision antérieure du refactor équipement.)

**`Data/Skills/PermanentSkillData.cs`** — renomme `onHitEffects` → `onHitReceivedEffects`,
ajoute `onHitDealtEffects` (même type de liste).

**`Data/Mobs/MobData.cs`** et **`Data/PNJ/PNJData.cs`** — nouveaux champs directs (pas
d'`EquipmentConfig` chez eux) :
```csharp
[Header("Effets On-Hit reçus")]
public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
[Header("Effets On-Hit infligés")]
public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
```

## Polymorphisme — `Entity.cs`

```csharp
/// <summary>Effets On-Hit infligés actifs sur cette entité (équipement+permanents pour Player,
/// MobData/PNJData pour Mob/PNJ). Null par défaut — override dans les sous-classes concernées.</summary>
public virtual List<OnHitDealtEffectEntry> GetOnHitDealtEffects() => null;

/// <summary>Effets On-Hit reçus actifs sur cette entité — voir GetOnHitDealtEffects().</summary>
public virtual List<OnHitReceivedEffectEntry> GetOnHitReceivedEffects() => null;
```

**`Player.cs`** — override en agrégeant équipement (6 pièces + bijoux multiples) + permanents,
exactement la logique déjà présente dans l'actuel `ApplyOnHitEffects` (liste de listes), mais
retournée plutôt qu'exécutée directement — deux méthodes `GetOnHitDealtEffects()`/
`GetOnHitReceivedEffects()` qui construisent et retournent `List<List<T>>` aplati en une seule
`List<T>` (ou gardent `List<List<T>>` si le consommateur préfère itérer par source — détail
d'implémentation laissé au plan).

**`Mob.cs`** — `override ... => data?.onHitDealtEffects;` / `=> data?.onHitReceivedEffects;`
(une seule source, pas d'agrégation).

**`PNJ.cs`** — idem, `data?.onHitDealtEffects`/`data?.onHitReceivedEffects`.

## Câblage — réactions RECEIVED (centralisées dans `Entity.TakeDamage`)

Actuellement `Player.TakeDamage` appelle `ApplyOnHitEffects(amount, source)` (Player-only,
lit l'équipement directement). Nouveau : la logique de résolution des 6 types
`OnHitReceivedEffectType` déménage dans une méthode `Entity.ApplyOnHitReceivedEffects(float
damageTaken, Entity attacker)`, appelée depuis `Entity.TakeDamage` (base, donc Player ET Mob
ET PNJ en profitent d'un coup), qui itère `GetOnHitReceivedEffects()` — retourne `null` pour
une entité sans liste (early-return, zéro coût). `Thorns`/`ReflectPercent` réutilisent la
logique `pierceDefense` déjà livrée cette session (formule quadratique/résistance élémentaire
selon l'élément si non-brut). `DamageReductionOnHit` est géré à part (voir section
Modificateurs, dans `CombatSystem`, pas ici).

`Player.ApplyOnHitEffects` (private) est supprimé — sa logique migre dans `Entity`.

## Câblage — réactions DEALT (dans `SkillSystem.ApplyEffectType`)

Après `target.TakeDamage(dmg, ...)` (donc uniquement si le coup n'a pas Miss), nouvelle méthode
`SkillSystem.ApplyOnHitDealtEffects(Entity attacker, Entity target, float damageDealt)` :
itère `attacker.GetOnHitDealtEffects()`, switch sur les 4 types réaction
(`LifestealOnHit`/`ManaOnHit`/`ApplyDebuffOnHit`/`ApplyBuffOnHit` — `DamageAmpOnHit` est un
modificateur, déjà consommé dans `CalculateDamage`). `ApplyDebuffOnHit` cible
`target.statusEffects.TryApplyDebuff(...)`, `ApplyBuffOnHit` cible
`attacker.statusEffects.ApplyBuff(..., attacker)` — corrige le bug (buff sur soi, plus jamais
sur la cible).

Remplace et supprime : `ApplyWeaponStatusEffects`, `ApplyStatusEffectEntry` (les deux
consommaient l'ancien `EquipmentConfig.statusEffects`, retiré).

**Portée des call sites** : le nouveau `ApplyOnHitDealtEffects` doit être appelé à chaque
endroit où `target.TakeDamage(dmg, ...)` suit un `CalculateDamage` réussi côté attaque —
recensés cette session : `SkillSystem.ApplyEffectType` (cas `Damage`, le point central) et le
cas `SkillSpecialEffect.DrainHP`. Le plan d'implémentation doit re-grep
`target.TakeDamage\(dmg` dans `SkillSystem.cs` pour n'en oublier aucun (3 sites `DamageDealtEvent`
déjà recensés cette session comme repère, mais le call-site du nouvel appel est indépendant de
`DamageDealtEvent`).

## Câblage — modificateurs (`CombatSystem.CalculateDamage`, PLAYER + MOB/PNJ)

Au même stage que le critique (après le bloc crit, avant "── Mark" / FinalDamageBonus) dans
les DEUX variantes de `CalculateDamage` :

```csharp
// ── Amplification / Réduction On-Hit — même stage que le critique ──
var dealtEffects = attacker.GetOnHitDealtEffects();
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
```

Note : plusieurs entrées qui proc simultanément s'accumulent multiplicativement (chaque proc
multiplie le résultat du précédent) — cohérent avec le traitement multiplicatif déjà en place
pour le crit/résistances dans ce même fichier, pas besoin d'un accumulateur séparé comme pour
les stats (Flat+%) puisqu'il n'y a qu'un seul canal ici (pas de composition Base+Flat×%).

## Fichiers touchés — récapitulatif

**Créés** :
- `Data/StatusEffect/OnHitDealtEffectData.cs`

**Renommés** (contenu réécrit, même emplacement) :
- `Data/StatusEffect/OnHitEffectData.cs` → classes/enum renommés en `OnHitReceived*`

**Modifiés** :
- `Data/Equipment/EquipmentConfig.cs`
- `Data/Equipment/ArmorData.cs`, `BootsData.cs`, `CosmeticDataBody.cs`, `CosmeticDataHead.cs`,
  `HelmetData.cs`, `GlovesData.cs`, `JewelryData.cs`, `WeaponData.cs`
- `Data/Skills/PermanentSkillData.cs`
- `Data/Mobs/MobData.cs`
- `Data/PNJ/PNJData.cs`
- `Entities/Entity.cs` (2 méthodes virtuelles + `ApplyOnHitReceivedEffects` + hook dans
  `TakeDamage`)
- `Entities/Player.cs` (overrides des 2 méthodes ; suppression de `ApplyOnHitEffects`)
- `Entities/Mob.cs` (overrides des 2 méthodes)
- `Entities/PNJ.cs` (overrides des 2 méthodes)
- `Combat/SkillSystem.cs` (`ApplyOnHitDealtEffects` nouveau ; suppression
  `ApplyWeaponStatusEffects`/`ApplyStatusEffectEntry` ; call site après chaque
  `target.TakeDamage(dmg,...)` côté attaque)
- `Combat/CombatSystem.cs` (bloc Amp/Reduction dans les 2 variantes de `CalculateDamage`)
- `UI/Shared/TooltipSystem.cs` (`BuildOnHitEffects` lit `OnHitReceivedEffects` — renommage de
  référence ; nouveau bloc à ajouter pour afficher `OnHitDealtEffects` si souhaité — au choix
  du designer, pas bloquant)

**Hors scope confirmé** : RuneData (son propre `statusEffects` n'est pas touché), migration de
données d'équipements existants (Florian reconfigure à la main), Mob/PNJ combat AI (aucun
changement de comportement, juste une nouvelle capacité de configuration).

## Vérification (Play Mode, Florian)

1. Compiler, 0 erreur — vérifier particulièrement les 8 fichiers equipment (renommage de
   propriété) et `TooltipSystem.cs`.
2. Créer un `OnHitDealtEffectData` type `LifestealOnHit` (Percent 10%), l'assigner sur une
   arme via `onHitDealtEffects` → taper un mob → vérifier heal proportionnel aux dégâts, à la
   fréquence de `chance`.
3. Créer un `OnHitDealtEffectData` type `ApplyBuffOnHit` avec un `BuffData` (ex: Stats
   AttackDamage) → vérifier que LE JOUEUR (pas le mob) reçoit le buff en tapant.
4. Créer un `OnHitDealtEffectData` type `DamageAmpOnHit` (10%) et un `OnHitReceivedEffectData`
   type `DamageReductionOnHit` (30%) sur les deux côtés d'un combat → vérifier au log de dégât
   (`CombatSystem.debugDamage`) que le montant reflète bien les deux multiplicateurs quand ils
   proc ensemble.
5. Vérifier qu'un mob équipé d'un `onHitReceivedEffects` (Thorns) renvoie bien des dégâts au
   joueur qui l'attaque en mêlée.
6. Cas Miss : forcer un mob à esquiver (dodge élevé) → vérifier qu'aucune réaction DEALT/RECEIVED
   ne se déclenche sur ce coup raté (Amp/Reduction peuvent avoir été roulés en interne, sans
   conséquence visible — normal, voir section Pipeline).
7. Vérifier qu'un `OnHitReceivedEffectData` sur un `PermanentSkillData` débloqué fonctionne
   toujours (renommage de champ, pas de changement de comportement attendu).
