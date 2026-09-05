# Plan C — ArmorType renommé + bonus automatiques

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renommer `ArmorType` (`Melee`/`Ranged`/`Magic` → `Lourde`/`Legere`/`Robe`) et lui donner
un vrai effet gameplay — bonus passif automatique par type, lu directement sur l'armure équipée,
sans configuration manuelle par le designer.

**Architecture:** Renommage d'enum ordinal-safe (3 identifiants C#, même ordre, un seul asset
existant affecté). Bonus codés en dur dans `CharacterStats.RecalculateStats` (PAS via
`config.bonuses` — décision explicite de Florian), réutilisant l'infrastructure Flat/%
existante (`AccumulateBonus`, `flatAcc`/`percentAcc`) pour les stats qui la supportent déjà
(Défenses, Dodge, Attack, Precision), et deux ajouts directs séparés pour les 2 stats qui n'ont
jamais eu de mode Percent dans ce système (Points élémentaires, réduction de cooldown).

**Tech Stack:** Unity C#, pas de framework de test automatisé (vérification manuelle Play Mode).

**Spec:** `docs/superpowers/specs/2026-09-05-unified-stat-modifiers-and-armor-type-design.md`
(section C — dépend des sections A et B, déjà implémentées : Plan A merged sur master
`358b7ab..b8c3b0e`, Plan B merged `2667b32..33546ec`).

## Global Constraints

- **Renommage ordinal-safe** : `ArmorType { Melee, Ranged, Magic }` → `{ Lourde, Legere, Robe }`,
  MÊME ORDRE (Melee↔Lourde=0, Ranged↔Legere=1, Magic↔Robe=2). Un seul `.asset` du projet a
  `armorType` sérialisé (`arm_base_test.asset`, valeur `0`) — Unity sérialise par entier, pas par
  nom, renommage sûr tant que l'ordre reste identique. `WeaponCategory` (enum séparé, `{ Unarmed,
  Melee, Ranged, Magic }`) N'EST PAS renommé — reste tel quel, seul `ArmorType` change.
- **Bonus automatiques** — codés en dur, lus directement sur `armor?.data.armorType`, PAS de
  `StatBonus`/`config.bonuses` configurable pour ces 3 bonus spécifiques :
  - **Lourde** → +10% Défense All (`StatType.AllDefense`, Percent) + 5% Esquive
    (`StatType.Dodge`, Percent)
  - **Légère** → +10% Attaque (`StatType.BonusAttack`, Percent) + 5% Précision
    (`StatType.Precision`, Percent)
  - **Robe** → +10% Points élémentaires tous éléments (multiplicatif direct sur
    `elementalPoints[e]`, PAS via le système `StatType`/`flatAcc`/`percentAcc` — les Points
    élémentaires n'ont jamais eu de mode Percent dans ce projet, voir note ci-dessous) + 5%
    Réduction cooldown = **`cooldownReduction += 0.05f`** (addition directe du ratio, PAS
    `×1.05` — décision confirmée par Florian : `cooldownReduction` est déjà un ratio, comme
    `CritChance`/`XPBonus`/`GoldBonus`, Flat et Percent y donnent le même résultat par
    convention établie dans ce projet).
- **Pourquoi Points élémentaires ne passe PAS par `AccumulateBonus`** : `AccumulateBonus`'s
  `case StatType.PointsAll:`/`PointsX:` fait toujours `elementalPoints[e] += b.value` — AUCUNE
  lecture du `mode`, une contribution "Percent 0.10" y serait AJOUTÉE telle quelle (+0.10 points,
  pas +10%). Retrofitter un vrai mode Percent pour tous les `PointsX` serait un changement de
  portée bien plus large que ce plan (`ExceptionStatTypes`/`ExceptionStats` classent déjà Points
  comme "toujours additif" dans les Plans A/B, décision explicite, inchangée ici). Pour CE bonus
  précis, la multiplication directe sur `elementalPoints[e]` APRÈS que toutes les autres sources
  (esprits, StatPoints) l'aient rempli est la solution la plus simple et la moins risquée.
- Ces 3 bonus s'ajoutent aux MÊMES `flatAcc`/`percentAcc` que le reste de l'équipement (pas un
  chemin séparé pour Lourde/Légère) — sauf les 2 exceptions ci-dessus (Points, cooldown) qui
  n'ont jamais eu de chemin `StatType` du tout.
- Pas de tests automatisés — vérification manuelle Play Mode (Task 3).

---

### Task 1: Renommer ArmorType (ordinal préservé)

**Files:**
- Modify: `Data/Equipment/WeaponType.cs`
- Modify: `Data/Equipment/ArmorData.cs`
- Modify: `Progression/CharacterData.cs`

**Interfaces:**
- Produces: `ArmorType.Lourde`/`Legere`/`Robe` (identifiants renommés, mêmes ordinaux 0/1/2) —
  consommés par Task 2 (`CharacterStats.cs`) et déjà consommés sans changement par
  `UI/Shared/TooltipSystem.cs:363` (`a.ArmorType.ToString()` — affiche automatiquement le nouveau
  nom, AUCUNE modification nécessaire dans ce fichier).

Recherche exhaustive faite cette session : SEULS les fichiers listés ci-dessus référencent
`ArmorType.Melee`/`ArmorType.Ranged`/`ArmorType.Magic` par NOM dans du code exécutable
(`grep -n "ArmorType" -r --include="*.cs"` sur tout le repo — les autres résultats sont des
commentaires génériques mentionnant "ArmorType" sans nommer un membre spécifique, aucun
changement requis pour eux).

- [ ] **Step 1 : Renommer l'enum**

Dans `Data/Equipment/WeaponType.cs`, remplacer :

```csharp
public enum ArmorType      { Melee, Ranged, Magic }
```

par :

```csharp
public enum ArmorType      { Lourde, Legere, Robe }
```

- [ ] **Step 2 : Mettre à jour `GetArmorType()` (même fichier)**

Toujours dans `Data/Equipment/WeaponType.cs`, remplacer :

```csharp
    /// <summary>ArmorType lié à cette catégorie d'arme.</summary>
    public static ArmorType GetArmorType(this WeaponType type)
    {
        switch (type.GetCategory())
        {
            case WeaponCategory.Ranged: return ArmorType.Ranged;
            case WeaponCategory.Magic:  return ArmorType.Magic;
            default:                    return ArmorType.Melee;
        }
    }
```

par (les `case WeaponCategory.Ranged`/`Magic` restent inchangés — `WeaponCategory` n'est PAS
renommé, seules les valeurs de RETOUR changent) :

```csharp
    /// <summary>ArmorType lié à cette catégorie d'arme.</summary>
    public static ArmorType GetArmorType(this WeaponType type)
    {
        switch (type.GetCategory())
        {
            case WeaponCategory.Ranged: return ArmorType.Legere;
            case WeaponCategory.Magic:  return ArmorType.Robe;
            default:                    return ArmorType.Lourde;
        }
    }
```

- [ ] **Step 3 : Mettre à jour `ArmorData.cs` (2 occurrences)**

Remplacer :

```csharp
    public ArmorType  armorType = ArmorType.Melee;
```

par :

```csharp
    public ArmorType  armorType = ArmorType.Lourde;
```

Et remplacer :

```csharp
    public ArmorType ArmorType    => data != null ? data.armorType    : global::ArmorType.Melee;
```

par :

```csharp
    public ArmorType ArmorType    => data != null ? data.armorType    : global::ArmorType.Lourde;
```

- [ ] **Step 4 : Mettre à jour `CharacterData.cs`**

Remplacer :

```csharp
    /// <summary>ArmorType équipable par ce personnage.</summary>
    public ArmorType ArmorType =>
        startingWeapon != null ? startingWeapon.LinkedArmorType : ArmorType.Melee;
```

par :

```csharp
    /// <summary>ArmorType équipable par ce personnage.</summary>
    public ArmorType ArmorType =>
        startingWeapon != null ? startingWeapon.LinkedArmorType : ArmorType.Lourde;
```

- [ ] **Step 5 : Vérifier la compilation**

Unity recompile, Console : 0 erreur. `UI/Shared/TooltipSystem.cs:363` (`a.ArmorType.ToString()`)
affichera désormais "Lourde"/"Legere"/"Robe" automatiquement — ne pas y toucher, c'est déjà
correct par construction (`.ToString()` sur un enum lit son nom C# actuel).

- [ ] **Step 6 : Commit**

```bash
git add Data/Equipment/WeaponType.cs Data/Equipment/ArmorData.cs Progression/CharacterData.cs
git commit -m "refactor: rename ArmorType Melee/Ranged/Magic to Lourde/Legere/Robe

Ordinal preserved (0/1/2 same positions) — only one project asset
(arm_base_test.asset) has armorType serialized, at ordinal 0, safe since
Unity serializes enums by int not name. GetArmorType()'s WeaponCategory
mapping updated to match (Melee->Lourde, Ranged->Legere, Magic->Robe),
WeaponCategory itself untouched. TooltipSystem's a.ArmorType.ToString()
now displays the new names with zero code change needed there."
```

---

### Task 2: Bonus automatiques dans CharacterStats.RecalculateStats

**Files:**
- Modify: `Entities/CharacterStats.cs`

**Interfaces:**
- Consumes: `ArmorType.Lourde`/`Legere`/`Robe` (Task 1). `AccumulateBonus(StatBonus, Dictionary<
  StatType,float> flatAcc, Dictionary<StatType,float> percentAcc, Dictionary<ElementType,float>
  resist)` (Plan A, déjà en place, inchangée — réutilisée telle quelle, y compris son fan-out
  `AllDefense` déjà correct).

- [ ] **Step 1 : Ajouter le bloc bonus automatique**

Dans `Entities/CharacterStats.cs`, trouver ce point d'insertion — juste après le bloc "Magic →
critChance forcé à 0" et juste AVANT le commentaire `// PUSH SUR ENTITY` :

```csharp
        // Magic → critChance forcé à 0 — pas de crit sur armes magiques. GDD §5.1.
        if (player.weaponCategory == WeaponCategory.Magic)
            accCritChance = 0f;

        // =========================================================
        // PUSH SUR ENTITY
        // =========================================================
```

Remplacer par (insertion du nouveau bloc entre les deux, sans toucher au reste) :

```csharp
        // Magic → critChance forcé à 0 — pas de crit sur armes magiques. GDD §5.1.
        if (player.weaponCategory == WeaponCategory.Magic)
            accCritChance = 0f;

        // =========================================================
        // BONUS AUTOMATIQUE ARMORTYPE — GDD §5.4
        // Codé en dur, PAS via config.bonuses (décision explicite Florian) — lu directement
        // sur l'ArmorType de l'armure équipée, indépendant de tout StatBonus configuré sur
        // l'ArmorData elle-même. Placé ici (après TOUTES les autres sources — esprits,
        // StatPoints, rang Neutre) pour que le bonus Robe sur elementalPoints capture bien
        // la totalité déjà accumulée, pas seulement une partie.
        // =========================================================
        if (armor?.data != null)
        {
            switch (armor.data.armorType)
            {
                case ArmorType.Lourde:
                    AccumulateBonus(
                        new StatBonus { statType = StatType.AllDefense, mode = ModifierType.Percent, value = 0.10f },
                        flatAcc, percentAcc, accResist);
                    AccumulateBonus(
                        new StatBonus { statType = StatType.Dodge, mode = ModifierType.Percent, value = 0.05f },
                        flatAcc, percentAcc, accResist);
                    break;

                case ArmorType.Legere:
                    AccumulateBonus(
                        new StatBonus { statType = StatType.BonusAttack, mode = ModifierType.Percent, value = 0.10f },
                        flatAcc, percentAcc, accResist);
                    AccumulateBonus(
                        new StatBonus { statType = StatType.Precision, mode = ModifierType.Percent, value = 0.05f },
                        flatAcc, percentAcc, accResist);
                    break;

                case ArmorType.Robe:
                    // Points élémentaires — pas de mode Percent dans ce système (voir spec §C),
                    // multiplication directe après que toutes les sources (esprits, StatPoints)
                    // aient déjà rempli elementalPoints[e] ci-dessus.
                    foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
                        elementalPoints[e] *= 1.10f;
                    // cooldownReduction est déjà un ratio (comme CritChance/XPBonus/GoldBonus) —
                    // addition directe, pas de multiplication (confirmé Florian).
                    cooldownReduction += 0.05f;
                    break;
            }
        }

        // =========================================================
        // PUSH SUR ENTITY
        // =========================================================
```

- [ ] **Step 2 : Vérifier la compilation**

Unity recompile, Console : 0 erreur. `StatBonus`/`ModifierType`/`ArmorType`/`ElementType` sont
déjà tous accessibles dans ce fichier (aucun `using` supplémentaire nécessaire — projet sans
namespaces, confirmé par les Plans A/B).

- [ ] **Step 3 : Vérifier en Play Mode (aperçu — détail complet Task 3)**

Équiper une armure `Lourde` avec un `MeleeDefense` de base connu (ex: 140 via `StatBonus` classique)
→ vérifier sur la fiche perso `140 × 1.10 = 154` (le bonus automatique s'additionne dans le MÊME
`percentAcc[MeleeDefense]` que tout autre `+%` équipement déjà présent, via le fan-out `AllDefense`
déjà existant). Changer l'armure pour `Légère`/`Robe` → vérifier que le bonus Lourde disparaît et
que le bonus correspondant au nouveau type s'applique à la place.

- [ ] **Step 4 : Commit**

```bash
git add Entities/CharacterStats.cs
git commit -m "feat: automatic ArmorType passive bonuses (Lourde/Legere/Robe)

Hardcoded in RecalculateStats, read directly off the equipped armor's
ArmorType, not configurable via config.bonuses (explicit design choice).
Lourde/Legere route through the existing AccumulateBonus/flatAcc/
percentAcc machinery (reuses the AllDefense fan-out as-is). Robe's
elemental-points and cooldown-reduction bonuses bypass that machinery
entirely — Points never had a Percent mode in this system (direct
multiply after all other sources have contributed), and
cooldownReduction is already a ratio (direct += 0.05, same convention as
CritChance/XPBonus/GoldBonus elsewhere in this codebase)."
```

---

### Task 3: Vérification manuelle finale (Play Mode)

**Files:** aucun — tâche de vérification uniquement.

- [ ] **Step 1 : Renommage visible**

Ouvrir l'Inspector d'une `ArmorData` existante (`arm_base_test.asset` ou toute autre) → vérifier
que le champ `armorType` affiche bien "Lourde"/"Legere"/"Robe" dans le dropdown (pas
"Melee"/"Ranged"/"Magic"), et que l'asset déjà sauvegardé garde la MÊME sélection qu'avant le
renommage (valeur 0 → maintenant affichée "Lourde", pas une valeur différente — confirme
l'ordinal safety).

- [ ] **Step 2 : Tooltip équipement**

Survoler une armure en jeu (tooltip) → vérifier que le nom du type affiché est bien "Lourde"/
"Legere"/"Robe" selon l'armure (déjà automatique via `.ToString()`, Task 1 Step 5).

- [ ] **Step 3 : Bonus Lourde**

Équiper une armure `Lourde` → vérifier sur la fiche perso : Défense Mêlée/Distance/Magique
chacune +10% de LEUR PROPRE valeur (pas une valeur combinée partagée), Esquive +5%. Comparer
avec/sans l'armure équipée pour confirmer le delta exact.

- [ ] **Step 4 : Bonus Légère**

Équiper une armure `Legere` → vérifier Attaque (min ET max) +10%, Précision +5%.

- [ ] **Step 5 : Bonus Robe**

Équiper une armure `Robe` avec des points élémentaires déjà investis (esprits et/ou StatPoints
élémentaires) → vérifier que CHAQUE élément affiche +10% de sa valeur totale déjà accumulée
(pas juste +10% d'une seule source). Vérifier aussi la réduction de cooldown effective en jeu
(+5 points de pourcentage, ex: 15%→20%, pas 15%→15.75%).

- [ ] **Step 6 : Changement d'armure à chaud**

Avec un personnage ayant un bonus Lourde actif, changer pour une armure Légère → vérifier que le
bonus Lourde disparaît complètement (pas de résidu sur Esquive/Défenses) et que le bonus Légère
s'applique correctement à la place. Refaire dans l'autre sens (Légère→Robe→Lourde) pour confirmer
qu'aucun bonus ne "reste collé" après un changement d'équipement.

- [ ] **Step 7 : Cumul avec un buff sur la même stat**

Appliquer un buff `+10% MeleeDefense` (Percent) en portant une armure Lourde (+10% AllDefense
déjà actif dessus) → vérifier `(base × 1.20)` [10% armure + 10% buff, MÊME pipeline équipement
vs... attention : le bonus Lourde est équipement, le buff est buff — donc ils NE s'additionnent
PAS, ils COMPOSENT (`base × 1.10 × 1.10 = base × 1.21`), comme documenté dans la clarification
inter-pipelines de la spec. Vérifier que le résultat observé est bien `×1.21`, pas `×1.20` ni
`×1.10` seul — confirme que Plan C compose correctement avec les Plans A/B déjà en place.
