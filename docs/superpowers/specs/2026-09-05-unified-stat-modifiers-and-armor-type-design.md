# Système de stats unifié (Flat/%) + Dégâts finaux + Effets ArmorType — Design

## Contexte

Parti d'une question simple ("`armorType` sert à quoi ?") — vérifié que `ArmorType`
(Melee/Ranged/Magic) est aujourd'hui **100% inerte** : lu uniquement pour l'affichage tooltip,
jamais consulté par `CharacterStats.RecalculateStats` ni aucune logique de gating. Le GDD §5.4
prévoit pourtant des bonus passifs par type (Lourde=Tank, Légère=DPS, Robe=Mage).

En creusant comment donner un vrai effet à `armorType`, la discussion a révélé un problème plus
large : ce projet a **deux pipelines de modificateurs de stats séparés et incohérents entre eux**
— `StatType`/`StatBonus` (équipement/passifs permanents, AUCUN mode Flat/% : chaque valeur
d'enum est flat-ou-ratio par convention implicite, pas de champ mode) et
`StatModifierType`/`StatLine` (buffs/debuffs temporaires, construit plus tôt aujourd'hui avec
un système `Flat`/`PercentOfBase`/`PercentOfFinal` scopé par buff qui s'avère plus compliqué que
nécessaire). Aucun des deux ne gère les dégâts/réduction "finaux" au sens combat (post-calcul).

Ce spec couvre donc trois chantiers liés, dans l'ordre où ils doivent être construits :

**A. Modèle Stat/Mode unifié** — même règle de calcul, mêmes deux enums (cible + mode),
utilisés par les deux pipelines (buffs ET équipement).
**B. Dégâts finaux au combat** — nouveau mécanisme séparé, dans `CombatSystem.CalculateDamage`.
**C. `ArmorType` renommé + bonus automatiques** — consommateur concret du système A.

## A. Modèle Stat/Mode unifié

### Deux enums, pas un système générique à explosion combinatoire

- **Enum cible** (fusion conceptuelle — reste deux enums C# séparés pour ne pas casser les
  assets déjà sérialisés, voir §Ordinal safety, mais avec la MÊME liste de valeurs et la MÊME
  sémantique) : chaque stat modifiable a sa propre entrée, pas de généralisation combinatoire.
  - **Défense** : `MeleeDefense`, `RangedDefense`, `MagicDefense`, **`AllDefense`** (nouveau —
    écrit sur les 3 simultanément, même principe qu'`AllResistances` déjà existant).
  - **Résistance élémentaire** : déjà existant (Fire/Water/.../All), inchangé.
  - **Points élémentaires** : mécanisme par champ élément séparé déjà existant, inchangé.
  - **Autres stats "normales"** : `MaxHP`, `MaxMana`, `AttackDamage`, `Dodge`, `Precision`,
    Regen HP/Mana **naturel** (permanent, `StatType` uniquement) et Regen HP/Mana
    **temporaire** (buff, `StatModifierType` uniquement — déjà correctement séparés).
- **Enum mode** : `Flat` / `Percent` — 2 valeurs (remplace `StatLineMode` à 3 valeurs construit
  plus tôt aujourd'hui : `PercentOfBase`/`PercentOfFinal` disparaissent, remplacés par la règle
  de calcul globale ci-dessous).

### Règle de calcul — stats "normales"

```
FinalStat = (Base + Σ tous les Flat actifs, toutes sources) × (1 + Σ tous les % actifs, toutes sources)
```

Une seule somme de Flat, une seule somme de %, peu importe la source (équipement, buff,
statpoints, passif permanent). Exemple validé : Défense Mêlée base 140, buffs actifs
`+20 flat` et `+50 flat` et `+10% final` → `(140 + 20 + 50) × 1.10 = 231`.

### Exceptions — stats toujours 100% additives, jamais de mode Flat

`CritChance`, `CritMultiplier`, Résistances élémentaires (Fire/Water/Earth/Nature/Lightning/
Darkness/Light/All), `CritDamageReduction`, `MoveSpeed`, **`XPBonus`**, **`GoldBonus`**, et les
4 nouveaux accumulateurs du §B (**`FinalDamageBonusFlat`**, **`FinalDamageBonusPercent`**,
**`FinalDamageReductionFlat`**, **`FinalDamageReductionPercent`**) — pas de sélecteur Flat/%
du tout pour ces cibles (masqué via `ShowIf`), juste addition directe des valeurs (déjà leur
comportement actuel pour les stats existantes, rien à changer côté calcul). `XPBonus`/
`GoldBonus`/les 4 accumulateurs "dégâts finaux" rejoignent cette liste — pas de "stat de base"
à multiplier (0.20 = +20% direct), la formule `(Base+Flat)×(1+%)` leur donnerait un résultat
faux.

**Ordinal safety** : toute nouvelle entrée ajoutée à `StatModifierType` ou `StatType`
(`AllDefense`, `FinalDamageBonus`/`FinalDamageReduction` s'ils deviennent des entrées d'enum
plutôt que des champs Entity directs, etc.) va TOUJOURS en fin d'enum — jamais insérée au
milieu, jamais réordonnée (règle déjà appliquée partout ailleurs dans ce projet).

### Heal — mécanisme séparé, pas concerné par ce modèle

`BuffType.Heal` reste son propre mécanisme dédié (effet instantané ponctuel appliqué une seule
fois dans `OnApplyBuff`, PAS un buff à `Tick()` — envisagé un instant comme un "Regeneration à
0.9s" pour réutiliser le mécanisme de taux existant, mais rejeté : un heal étalé sur plusieurs
frames laisse une fenêtre où le joueur peut mourir/se faire dissiper avant de recevoir le
montant complet, contrairement à un vrai instantané qui engage 100% du montant dès le cast).

`healModifier` (actuellement `ModifierType` à 2 valeurs, réutilisé pour tous les effets à
mode Flat/Percent du fichier) passe à un enum dédié 3 valeurs propre au Heal :
`HealAmountMode { Flat, PercentMaxHP, PercentCurrentHP }` — ne peut plus réutiliser
`ModifierType` (2 valeurs seulement).

**UX inspector — 3 champs dédiés au lieu d'un seul champ réinterprété** (le problème actuel :
un seul `healAmount` sert aux 2 modes, sans slider Range pour le %, sans libellé propre — source
de confusion/erreur de saisie) :
```csharp
public enum HealAmountMode { Flat, PercentMaxHP, PercentCurrentHP }

[ShowIf(nameof(buffType), BuffType.Heal, Header = "Soin instantané (Heal)")]
public HealAmountMode healMode = HealAmountMode.Flat;

[Tooltip("Montant fixe de PV restaurés.")]
[ShowIf(nameof(healMode), HealAmountMode.Flat, AndField = nameof(buffType), AndValue = BuffType.Heal)]
public float healFlatAmount = 0f;

[Tooltip("% des PV MAX de la cible restaurés (0.10 = 10%).")]
[Range(0f, 1f)]
[ShowIf(nameof(healMode), HealAmountMode.PercentMaxHP, AndField = nameof(buffType), AndValue = BuffType.Heal)]
public float healPercentMaxHP = 0f;

[Tooltip("% des PV ACTUELS de la cible restaurés (0.10 = 10%).")]
[Range(0f, 1f)]
[ShowIf(nameof(healMode), HealAmountMode.PercentCurrentHP, AndField = nameof(buffType), AndValue = BuffType.Heal)]
public float healPercentCurrentHP = 0f;
```
Chaque mode a son propre champ, avec son propre libellé et son propre slider `[Range(0,1)]`
pour les %, plutôt qu'un montant générique réinterprété selon l'enum voisin.

**Extension nécessaire de `ShowIfAttribute`/`ShowIfPropertyDrawer`** (`Utils/ShowIfAttribute.cs`
+ `Editor/ShowIfPropertyDrawer.cs`) : aujourd'hui un seul champ condition par attribut
(`AllowMultiple = false`, une seule paire conditionField/values). Ces 3 nouveaux champs ont
besoin d'une condition en ET (`healMode == X` ET `buffType == Heal`) — sans le second volet,
les champs resteraient visibles par erreur dès que `healMode` a la bonne valeur, même si
`buffType` n'est plus `Heal` (valeur `healMode` orpheline mais toujours sérialisée). Ajout
rétrocompatible : deux propriétés optionnelles `AndField`/`AndValue` sur `ShowIfAttribute`
(non renseignées par défaut → aucun changement pour les ~10 fichiers qui utilisent déjà
`[ShowIf]` avec une seule condition), vérifiées en plus de la condition existante dans
`ShowIfPropertyDrawer.IsVisible`.

`GetHealAmount(float targetMaxHP, float currentHP)` — signature étendue (ajoute `currentHP`,
pas utilisé par les 2 modes existants) :
```csharp
public float GetHealAmount(float targetMaxHP, float currentHP) => healMode switch
{
    HealAmountMode.PercentMaxHP     => targetMaxHP * healPercentMaxHP,
    HealAmountMode.PercentCurrentHP => currentHP * healPercentCurrentHP,
    _                                => healFlatAmount,
};
```
Appelant (`StatusEffectSystem.OnApplyBuff`, `case BuffType.Heal:`) doit passer
`_entity.CurrentHP` en plus de `_entity.MaxHP` — seul point d'appel à mettre à jour.

### Deux pipelines à réviser

- **`StatModifierType`/`StatLine`/`ApplyBonusStats`** (buffs/debuffs, `Data/StatusEffect/
  StatusEffectData.cs` + `Entities/StatusEffectSystem.cs`) — construits plus tôt aujourd'hui
  avec `StatLineMode` à 3 valeurs et une logique `pureBase`/`ownDelta` scopée par buff (2
  passes). À réviser : `StatLineMode` passe à 2 valeurs (`Flat`/`Percent`), et le calcul
  devient une somme GLOBALE (tous buffs/debuffs actifs confondus) plutôt que scopée par buff
  individuel — se rapproche du calcul déjà fait pour les stats "normales" via
  `ModifyEntityStat`/`GetBaseStatValue`, mais doit maintenant produire UNE somme de Flat et UNE
  somme de % par stat, appliquées ensemble à la toute fin de `ReapplyActiveModifiers` (3 passes
  au lieu de 2 : collecter tous les Flat, collecter tous les %, appliquer
  `(pureBase + ΣFlat) × (1 + Σ%)` une fois par stat).
- **`StatType`/`StatBonus`** (équipement/passifs permanents, `Data/Equipment/StatBonus.cs` +
  `Entities/CharacterStats.cs`) — n'a AUCUN champ mode aujourd'hui (`StatBonus` = juste
  `statType` + `value`, mode implicite par convention/commentaire). À ajouter : un champ
  `ModifierType mode` (réutilise l'enum `ModifierType` déjà existant dans `Data/Skills/
  SkillData.cs` — Flat/Percent, pas besoin d'un 2e enum identique) sur `StatBonus`, avec
  `ShowIf` masquant le champ pour les cibles de la liste d'exceptions. `AccumulateBonus` dans
  `CharacterStats.cs` doit être réécrit pour séparer accumulation Flat et % par stat (au lieu
  d'un simple `+=` unique), puis appliquer la même formule `(Base+ΣFlat)×(1+Σ%)` au moment de
  pousser chaque stat finale sur `Entity`.

**Ordre d'application entre les deux pipelines** : équipement/permanent (`CharacterStats.
RecalculateStats`, tourne en premier) calcule sa propre valeur `(Base+ΣFlat_equip)×(1+Σ%_equip)`
et la pousse sur l'Entity — CETTE valeur devient alors le nouveau "Base" que
`StatusEffectSystem.ReapplyActiveModifiers` (tourne juste après) utilise pour SA propre passe
`(Base+ΣFlat_buff)×(1+Σ%_buff)`. Deux passes séquentielles, pas une fusion complète des deux
systèmes en un seul — les % de buff s'appliquent donc sur le total déjà boosté par l'équipement,
pas sur le stat brut du personnage seul. Approche choisie car elle respecte l'ordre d'appel déjà
existant (`RecalculateStats` avant `ReapplyActiveModifiers`, jamais changé) sans fusionner deux
systèmes entiers en un — plus petit changement, moins de risque de régression sur tout ce qui
marche déjà.

## B. Dégâts finaux au combat

Mécanisme séparé, symétrique, appliqué dans `Combat/CombatSystem.cs` (`CalculateDamage` et
`CalculateMobDamage`), APRÈS tout le reste (défense, crit, élémentaire, Mark) :

```
totalDamage = (totalDamage + attacker.FinalDamageBonusFlat) × (1 + attacker.FinalDamageBonusPercent)
totalDamage = (totalDamage - target.FinalDamageReductionFlat) × (1 - target.FinalDamageReductionPercent)
```

Deux nouvelles paires d'accumulateurs sur `Entity` (même pattern que `XPBonusPercent`/
`GoldBonusPercent` ajoutés ce matin — champs protégés + propriétés publiques + setters,
resetés à 0 dans `BaseStats`/`SnapshotBaseStats`/`RequestRecalculate`, alimentés par
`ModifyEntityStat` via deux nouvelles entrées `StatModifierType` : `FinalDamageBonus` (Flat +
Percent tous deux valides ici, PAS une exception — ce sont des accumulateurs simples, pas des
"stats" avec une base à multiplier, donc Flat et Percent restent deux accumulateurs SÉPARÉS —
`FinalDamageBonusFlat` et `FinalDamageBonusPercent` — pas la formule `(Base+Flat)×(1+%)`) et
`FinalDamageReduction` (même structure côté défense). Ces 4 valeurs (`FinalDamageBonusFlat/
Percent`, `FinalDamageReductionFlat/Percent`) sont alimentées par le MÊME `StatLine`/`StatBonus`
que le reste, juste avec un mode d'accumulation "somme simple" plutôt que "(Base+Flat)×(1+%)"
puisqu'il n'y a pas de "stat de base" pour les dégâts finaux — c'est un pur accumulateur, comme
XPBonus/GoldBonus.

## C. ArmorType renommé + bonus automatiques

`ArmorType` (`Data/Equipment/WeaponType.cs:131`, actuellement `{ Melee, Ranged, Magic }`) →
renommé `{ Lourde, Legere, Robe }` (identifiants C#, ordinal conservé — un seul `.asset`
existant dans le projet a `armorType` sérialisé, `arm_base_test.asset`, valeur `0` = premier
membre peu importe son nom — renommage sûr tant que l'ORDRE reste identique, confirmé via
lecture YAML : Unity sérialise par entier, pas par nom).

Bonus automatiques codés en dur dans `CharacterStats.RecalculateStats` (nouveau bloc, lu
directement sur `player.equippedArmorInstance?.data.armorType`, PAS via `config.bonuses` —
décision explicite de Florian, différent du reste de l'équipement) :
- **Lourde** → +10% Défense All (`AllDefense`, Percent) + 5% Esquive (Percent, mode caché car
  Dodge suit maintenant la formule normale — la valeur EST déjà un %, cohérent)
- **Légère** → +10% Attaque (Percent) + 5% Précision (Percent)
- **Robe** → +10% Points élémentaires, tous éléments (Percent) + 5% Réduction cooldown
  (Percent — `cooldownReduction` déjà un champ existant sur `CharacterStats`)

Ces 3 bonus s'accumulent dans les MÊMES sommes Flat/% que le reste (équipement classique +
statpoints + ce bonus automatique) — pas un chemin séparé, juste une source de plus qui
alimente les accumulateurs avant que la formule finale ne tourne.

## Fichiers touchés (aperçu — détail dans le plan)

- `Data/StatusEffect/StatusEffectData.cs` — `StatLineMode` réduit à 2 valeurs, nouvelles
  entrées `StatModifierType` (`AllDefense`, `FinalDamageBonus`, `FinalDamageReduction`).
- `Entities/StatusEffectSystem.cs` — `ApplyBonusStats`/`ReapplyActiveModifiers` réécrits pour
  la somme globale Flat/% (3 passes), `ModifyEntityStat`/`GetBaseStatValue` étendus.
- `Data/Equipment/StatBonus.cs` — nouveau champ `mode` sur `StatBonus`, nouvelles entrées
  `StatType` manquantes pour matcher (`AllDefense` si pas déjà présent sous un autre nom).
- `Entities/CharacterStats.cs` — `AccumulateBonus`/`RecalculateStats` réécrits pour Flat/%
  séparés + formule finale, nouveau bloc bonus automatique par `ArmorType`.
- `Entities/Entity.cs` — nouveaux accumulateurs `FinalDamageBonusFlat/Percent`,
  `FinalDamageReductionFlat/Percent` (pattern `XPBonusPercent` déjà en place).
- `Combat/CombatSystem.cs` — nouvelle étape finale dans `CalculateDamage`/`CalculateMobDamage`.
- `Data/Equipment/WeaponType.cs` — `ArmorType` renommé.
- `Data/StatusEffect/BuffData.cs` — `healModifier`(`ModifierType`) remplacé par `healMode`
  (nouvel enum `HealAmountMode` 3 valeurs) + 3 champs dédiés (`healFlatAmount`/
  `healPercentMaxHP`/`healPercentCurrentHP`), `GetHealAmount` étendu (+`currentHP`).
- `Utils/ShowIfAttribute.cs` + `Editor/ShowIfPropertyDrawer.cs` — ajout optionnel
  `AndField`/`AndValue` (condition ET secondaire), rétrocompatible.
- `Entities/StatusEffectSystem.cs` — `case BuffType.Heal:` passe `_entity.CurrentHP` à
  `GetHealAmount`.
- Tous les fichiers référençant `ArmorType.Melee/Ranged/Magic` par nom (peu nombreux, à
  recenser dans le plan).

## Décomposition recommandée pour l'implémentation

Trois sous-projets séquentiels, chacun testable indépendamment — **à découper en plans séparés**
plutôt qu'un seul plan géant (trop de surface pour une seule passe de revue) :
1. **A** (modèle unifié, les deux pipelines) — le plus gros morceau, tout le reste en dépend.
2. **B** (dégâts finaux combat) — peut se faire après A, dépend juste des 2 nouveaux
   accumulateurs sur `Entity`.
3. **C** (ArmorType) — dépend de A (a besoin que le pipeline équipement gère vraiment le mode
   Percent correctement).

## Vérification

Pas de framework de test automatisé — vérification manuelle Play Mode par Florian, par
sous-projet :
1. **A** : équiper une armure avec un bonus `+20 flat MeleeDefense` (StatBonus) + activer un
   buff `+50 flat` et `+10% Percent` sur la même stat → vérifier `(base+20+50)×1.10` affiché
   correctement sur la fiche perso. Vérifier qu'une stat "exception" (CritChance) additionne
   toujours simplement (pas de multiplication).
2. **B** : buff `+100 flat dégâts finaux` + `+5% dégâts finaux` sur l'attaquant, buff
   `-50 flat réduction` + `-5% réduction` sur la cible → vérifier le nombre de dégâts affiché
   correspond au calcul manuel.
3. **C** : équiper une armure Lourde/Légère/Robe → vérifier que le bonus correspondant
   s'applique automatiquement sans configurer `config.bonuses`, et que le nom affiché dans
   l'UI/tooltip montre bien "Lourde"/"Légère"/"Robe".
