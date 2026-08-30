# Refonte du système de stats rollées — WeaponData / WeaponInstance

> **Mise à jour** : section 9 ajoutée — extension du principe à la précision.

**Contexte** : besoin de pouvoir rééquilibrer les dégâts d'une arme (nerf/buff) après
son déploiement, sans que les items déjà droppés restent figés sur leurs anciennes
valeurs, tout en gardant de la variance dans le loot et un système facile à équilibrer
par arme.

> Cette note documente le design **final retenu**. Plusieurs itérations intermédiaires
> ont été explorées en cours de route (voir section 6) avant d'arriver à ce modèle.

---

## 1. Problème de départ

Avant la refonte, `CreateDropInstance()` rollait des valeurs **absolues** au drop :

```csharp
float dmgMin = Random.Range(baseDamageMinLow,  baseDamageMinHigh);
float dmgMax = Random.Range(baseDamageMaxLow,  baseDamageMaxHigh);
float prec   = Random.Range(basePrecisionMin,  basePrecisionMax);
```

Stockées telles quelles sur l'instance, ces valeurs ne changeaient plus jamais.
**Conséquence** : nerfer une arme sur le `WeaponData` ne touchait que les futurs drops
— toutes les armes déjà en possession des joueurs gardaient leurs anciennes valeurs,
nécessitant un script de migration manuel pour corriger les items existants.

---

## 2. Principe retenu : un seul `baseDamage`, tout le reste est dérivé

Plutôt que de saisir 4 bornes brutes à la main (`minLow`, `minHigh`, `maxLow`,
`maxHigh` — difficile à équilibrer, source de chevauchements), on pose **une seule
valeur de référence** (`baseDamage`), et deux pourcentages qui dérivent
automatiquement les 4 bornes réelles :

```csharp
public float baseDamage = 10f;              // dégât de référence central de l'arme
public float spreadBasePercent = 0.10f;      // écart entre profil "faible" et "fort"
public float rollGapPercent = 0.06f;         // variance fine à l'intérieur de chaque fourchette
```

### Formules de dérivation

```
MinLow  = baseDamage × (1 − spreadBasePercent)
MinHigh = MinLow × (1 + rollGapPercent)

MaxLow  = baseDamage × (1 + spreadBasePercent)
MaxHigh = MaxLow × (1 + rollGapPercent)
```

**Exemple avec `baseDamage = 100`, `spreadBasePercent = 10%`, `rollGapPercent = 6%`** :

| | Valeur |
|---|---|
| MinLow | 90 |
| MinHigh | 95.4 |
| MaxLow | 110 |
| MaxHigh | 116.6 |

### Ce que contrôle chaque paramètre

- **`spreadBasePercent`** : à quel point l'arme peut taper faible ET fort (son
  "punch" global). Plus il est grand, plus l'arme est "swingy" (ex: 20% → très
  chaotique, 3% → très régulière).
- **`rollGapPercent`** : la variance fine du roll *à l'intérieur* d'une même
  fourchette (qualité de l'item obtenu). Garantit un écart minimum visible entre
  `dmgMin` et `dmgMax` (évite l'affichage type "185-186").

### Pourquoi `MinHigh` et `MaxLow` ne se touchent jamais (avec des % raisonnables)

Comme les deux bornes basses (`MinLow`, `MaxLow`) dérivent du **même** `baseDamage`,
et que `spreadBasePercent` les écarte nettement avant que `rollGapPercent` ne les
élargisse chacune un peu plus, il existe une marge de sécurité tant que
`spreadBasePercent` reste raisonnablement plus grand que `rollGapPercent` (ex: 10%
vs 6% — marge confortable ; 5% vs 5% — marge nulle, les fourchettes se touchent
pile).

---

## 3. Garde-fou : avertissement de configuration, pas de clamp silencieux

```csharp
#if UNITY_EDITOR
private void OnValidate()
{
    if (MinHigh > MaxLow)
        Debug.LogWarning($"[WeaponData:{weaponName}] MinHigh ({MinHigh:F1}) > MaxLow ({MaxLow:F1}) — " +
                          "les fourchettes de dmgMin/dmgMax se chevauchent...", this);
}
#endif
```

Si un designer configure des `%` qui font se chevaucher les fourchettes (risque de
`dmgMin > dmgMax`), Unity affiche un avertissement dans la console **au moment de la
configuration** dans l'inspecteur — plutôt que de corriger silencieusement le
problème à l'exécution avec un clamp (voir section 6.3 pour pourquoi cette approche
a été écartée).

---

## 4. Réglage manuel par arme (pas d'automatisation par niveau)

`spreadBasePercent` et `rollGapPercent` sont des champs simples, **réglés à la main
sur chaque `WeaponData`** — pas de calcul automatique basé sur `weaponLevel`. Une
automatisation par niveau a été envisagée (interpolation entre un profil "niveau 1"
très swingy et un profil "niveau max" plus stable) mais écartée : le réglage manuel
par arme donne plus de contrôle direct, quitte à demander plus de travail de design.

**Recommandation d'usage** : baisser progressivement ces deux `%` sur les armes de
plus haut niveau / plus gros dégâts, pour éviter que l'écart en valeur absolue ne
devienne disproportionné (voir tableau ci-dessous).

| Échelle de dégâts | spreadBasePercent conseillé | rollGapPercent conseillé |
|---|---|---|
| ~100 (armes de départ) | 10% | 6% |
| ~2 000 – 5 000 (milieu de jeu) | 5-6% | 3-4% |
| ~12 000+ (fin de jeu) | 3% | 2% |

*(Valeurs indicatives — à ajuster selon le ressenti en jeu.)*

**Pas de `[Range]` dans l'inspecteur** sur ces deux champs (ni sur les précédents
champs similaires) : juste `[Min(0f)]`, pour éviter qu'un slider soit décalé par un
clic accidentel dans l'inspecteur Unity.

---

## 5. État final du code (`WeaponData.cs`)

**`WeaponData` — nouveaux champs** :
```csharp
public float baseDamage = 10f;
[Min(0f)] public float spreadBasePercent = 0.10f;
[Min(0f)] public float rollGapPercent = 0.06f;
public float basePrecisionMin = 85f;   // inchangé
public float basePrecisionMax = 95f;   // inchangé

public float MinLow  => baseDamage * (1f - spreadBasePercent);
public float MinHigh => MinLow * (1f + rollGapPercent);
public float MaxLow  => baseDamage * (1f + spreadBasePercent);
public float MaxHigh => MaxLow * (1f + rollGapPercent);
```

**`WeaponData.CreateDropInstance()`** :
```csharp
public WeaponInstance CreateDropInstance(int rarityRank = 0, int upgradeLevel = 0)
{
    float ratioMin  = Random.value;
    float ratioMax  = Random.value;
    float ratioPrec = Random.value;
    return new WeaponInstance(this, ratioMin, ratioMax, ratioPrec, rarityRank, upgradeLevel);
}
```

**`WeaponInstance` — champs** :
```csharp
public float rolledRatioMin;
public float rolledRatioMax;
public float rolledRatioPrecision;
```
*(remplace les anciens `rolledDamageMin`, `rolledDamageMax`, `rolledPrecision`)*

**`WeaponInstance` — stats finales** (signature publique inchangée) :
```csharp
public float FinalDamageMin =>
    Mathf.Lerp(data.MinLow, data.MinHigh, rolledRatioMin) * (1f + RarityBonus) * (1f + UpgradeBonus);

public float FinalDamageMax =>
    Mathf.Lerp(data.MaxLow, data.MaxHigh, rolledRatioMax) * (1f + RarityBonus) * (1f + UpgradeBonus);

public float FinalPrecision =>
    Mathf.Lerp(data.basePrecisionMin, data.basePrecisionMax, rolledRatioPrecision) * (1f + RarityBonus) * (1f + UpgradeBonus);
```

→ **Aucun changement côté UI/affichage** : `FinalDamageMin`/`FinalDamageMax`/
`FinalPrecision` gardent le même nom et type de retour qu'avant la refonte. Tout code
qui lisait déjà ces propriétés (tooltip, fiche d'objet, comparateur) continue de
fonctionner sans modification.

---

## 6. Itérations écartées en cours de route (pour référence)

Documenté ici pour éviter de retomber dans les mêmes impasses si le sujet revient.

### 6.1 — Un seul ratio partagé entre dmgMin/dmgMax/precision
Première version explorée. Simple et sans risque de collision, mais un item est
soit globalement "bon" soit "mauvais" sur toutes ses stats à la fois — pas assez
de variance de loot souhaitée.

### 6.2 — Deux fourchettes indépendantes saisies à la main (`minLow/minHigh` +
`maxLow/maxHigh` sans lien entre elles)
Permet la variance voulue, mais :
- 4 valeurs à équilibrer à la main par arme, source d'erreurs (chevauchement facile
  à introduire sans s'en rendre compte).
- Avec un clamp de sécurité (tri + écart minimum), deux rolls différents proches
  d'un seuil peuvent converger vers un résultat final identique (ex: roll 231/232
  et roll 231/245 finissaient tous deux à 231/245 avec un gap de 6%) — pas
  équitable, information de roll perdue.

### 6.3 — Un seul ratio de dégât, dmgMax dérivé de dmgMin via un gap fixe
Réglait le problème de collision (mapping continu, roll → résultat bijectif), mais
supprimait la variance indépendante entre dmgMin et dmgMax (retour de facto à un
seul ratio, juste maquillé différemment).

### 6.4 — `baseDamageMin` / `baseDamageMax` saisis séparément à la main + spread%
symétrique autour de chacun
Bonne idée de principe (variance manuelle possible : une arme peut taper très bas
ET très fort), mais avec un spread% identique aux deux étages, `MinHigh` et
`MaxLow` se retrouvaient à se **toucher exactement** dans le cas limite (jamais de
chevauchement, mais parfois écart nul) — l'écart minimum garanti (`rollGapPercent`)
n'était pas encore intégré à ce stade.

### 6.5 — Spread calculé automatiquement selon `weaponLevel`
Proposé (interpolation entre un profil niveau 1 et un profil niveau max), mais
explicitement écarté par choix : le réglage manuel par arme (design final retenu,
section 4) donne plus de contrôle direct sur le ressenti de chaque arme.

---

## 7. Ce qui va casser à la compilation

Cherche dans **tout le projet** (pas juste ce fichier) :

- `new WeaponInstance(...)` avec une ancienne signature — la signature actuelle est
  `(WeaponData source, float ratioMin, float ratioMax, float ratioPrecision, int rarity = 0, int upgrade = 0)`.
- `.rolledDamageMin`, `.rolledDamageMax`, `.rolledPrecision` — champs supprimés,
  remplacés par `.rolledRatioMin`, `.rolledRatioMax`, `.rolledRatioPrecision`.
- Toute référence à `baseDamageMinLow`, `baseDamageMinHigh`, `baseDamageMaxLow`,
  `baseDamageMaxHigh` (anciens champs bruts) — remplacés par `baseDamage` +
  `spreadBasePercent` + `rollGapPercent`.
- **Sauvegardes existantes** (JSON/binaire) : le format sérialisé de
  `WeaponInstance` et `WeaponData` a changé plusieurs fois au cours de cette refonte
  → les anciennes saves ne seront plus compatibles. Prévoir une migration de save
  si le jeu est déjà en prod, ou accepter la perte de rétrocompatibilité sinon.

Suggestion : `grep -r "rolledDamage\|rolledPrecision\|baseDamageMinLow\|baseDamageMinHigh\|baseDamageMaxLow\|baseDamageMaxHigh\|new WeaponInstance" .`
dans le projet complet pour lister tous les points d'impact avant de compiler.

---

## 8. Généraliser aux autres équipements (Armor, Accessoires, etc.)

Le pattern à reproduire pour chaque type d'équipement suivant la même logique
(stats avec fourchette + rareté + upgrade) :

1. Un seul `baseX` de référence (pas de bornes brutes saisies à la main).
2. Deux `%` réglables par item : un `spreadBasePercent` (écart entre profil
   faible/fort) et un `rollGapPercent` (variance fine dans chaque fourchette).
3. `MinLow`/`MinHigh`/`MaxLow`/`MaxHigh` en **propriétés calculées**, jamais
   stockées — recalculées à chaque lecture depuis les valeurs actuelles du SO.
4. Au drop/craft : rouler un ratio indépendant par stat variable (`Random.value`),
   stocké tel quel sur l'instance (jamais le résultat final).
5. Stats finales = `Lerp(Low, High, ratio) × (1 + rareté) × (1 + upgrade)`, calculées
   à la lecture, jamais figées.
6. `OnValidate()` en éditeur pour avertir si `spreadBasePercent` est trop faible par
   rapport à `rollGapPercent` (risque de chevauchement).

**Recommandation avant de dupliquer ce code** : si plusieurs types d'équipement
partagent cette logique, factoriser les parties communes (Lerp + rareté + upgrade +
validation) dans une classe de base ou des méthodes statiques partagées plutôt que
de copier-coller ce bloc dans chaque fichier. À faire quand les autres fichiers
d'équipement seront fournis.

---

## 9. Extension à la précision — une seule fourchette suffit

La précision suit le même principe de dérivation depuis une valeur de référence,
mais avec **une seule fourchette** au lieu de deux (`dmgMin` et `dmgMax`
nécessitaient chacun leur sous-fourchette pour ne jamais se chevaucher — voir
sections 2 et 6.4). La précision n'a qu'un `min → max` direct, donc un seul `%`
de spread suffit ; pas besoin d'un second paramètre type `rollGapPercent`.

**Anciens champs (bornes brutes saisies à la main)** :
```csharp
public float basePrecisionMin = 85f;
public float basePrecisionMax = 95f;
```

**Nouveaux champs (dérivés d'une seule référence)** :
```csharp
public float basePrecision = 90f;
[Min(0f)] public float precisionSpreadPercent = 0.10f; // 10% par défaut

public float PrecisionLow  => basePrecision * (1f - precisionSpreadPercent);
public float PrecisionHigh => basePrecision * (1f + precisionSpreadPercent);
```

Exemple avec `basePrecision = 90`, `precisionSpreadPercent = 10%` → fourchette
réelle `81` à `99`.

**`WeaponInstance.FinalPrecision`** — signature publique inchangée, seule la
source des bornes change :
```csharp
public float FinalPrecision =>
    Mathf.Lerp(data.PrecisionLow, data.PrecisionHigh, rolledRatioPrecision)
    * (1f + RarityBonus) * (1f + UpgradeBonus);
```

**Bénéfice identique aux dégâts** : `rolledRatioPrecision` (déjà stocké comme
ratio 0..1, voir section 2) fait qu'un rééquilibrage de `basePrecision` ou
`precisionSpreadPercent` sur le SO se répercute automatiquement sur toutes les
instances déjà droppées, sans script de migration.

### Ce qui va casser à la compilation (en plus de la section 7)

- `basePrecisionMin`, `basePrecisionMax` — champs supprimés, remplacés par
  `basePrecision` + `precisionSpreadPercent`. Chercher toute référence directe
  à ces deux anciens noms ailleurs dans le projet.
- Aucun changement sur `rolledRatioPrecision` ni sur la signature de
  `WeaponInstance` — ce champ existait déjà en ratio depuis la refonte
  principale (section 5), seule la source des bornes (`data.basePrecisionMin/Max`
  → `data.PrecisionLow/High`) change dans `FinalPrecision`.

Suggestion : ajouter `basePrecisionMin\|basePrecisionMax` au grep de la
section 7 pour une recherche complète en une seule passe.
