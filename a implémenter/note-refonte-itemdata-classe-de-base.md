# Refonte — ItemData, classe de base commune (Phase 1)

> **Statut** : implémenté. `ItemData`/`EquipmentDataBase` créés, les 13 types listés en §3 migrés, `LocalizedText`/`LocalizationManager`/`PlayerSettings` atterris dans `Data/Localization/` (auto-init via `[RuntimeInitializeOnLoadMethod]`), tous les sites consommateurs mis à jour (y compris `ResourceNode.cs`/`SpawnManager.cs`/`TargetPanel.cs`, trouvés seulement au sweep final — absents de l'exploration initiale). Grep de sécurité final passé : plus aucune référence `.xxxName` résiduelle, plus aucun `requiredLevel` dupliqué. `itemID`/`vendorPrice` remplis sur les assets existants (fait par l'utilisateur, hors code). Reste à faire en dehors de ce document : retaper les noms/descriptions des ~5 `.asset` de test existants (perdus au passage string→LocalizedText, comme prévu).
>
> **Mise à jour ultérieure** : le doublon `CosmeticData` legacy (§3, note ci-dessous) n'a pas été "réconcilié" — vérifié qu'il n'était référencé nulle part (ni `Player.cs`, ni `InventorySystem.cs`, ni `SaveSystem.cs`, ni le système de récompense — seuls `CosmeticDataHead`/`CosmeticDataBody` sont réellement équipables), donc supprimé purement (`Data/Inventory/CosmeticData.cs` + `.meta`, aucun `.asset` n'en dépendait — vérifié par recherche du GUID). Le compte passe donc de 13 à **12 types migrés**.

**Contexte** : 15 types de données d'items (WeaponData, ArmorData, HelmetData, GlovesData, BootsData, JewelryData, SpiritData, CosmeticDataHead, CosmeticDataBody, CardData, ResourceData, ConsumableData, CosmeticData legacy, RuneData, GemData — 13 concernés par cette phase, RuneData/GemData exclus, voir plus bas) dupliquent chacun indépendamment le même socle : un champ `xxxName` (string), un `description` (string), un `icon` (Sprite), et pour 9 d'entre eux un `EquipmentConfig config`. Le GDD documente depuis longtemps (§5.1 "ItemData SO — Classe de Base") une base commune jamais implémentée — aucun fichier `ItemData.cs` n'existe dans le projet.

En parallèle, `LocalizedText`/`LocalizationManager` existent déjà comme brouillon fini (voir `note-systeme-multilangue.md`) mais ne sont branchés nulle part. Plutôt que de créer `ItemData` avec des `string` puis les reconvertir en `LocalizedText` dans une phase ultérieure (double travail, double risque de casse sur les mêmes fichiers), la base commune doit être conçue **directement avec le multilangue** — les deux briques sont de toute façon posées ensemble.

**Mise à jour** : par le même raisonnement, `PlayerSettings` (prévu initialement pour la Phase 3, voir `note-refonte-settings-langue-phase3.md`) est avancé dans cette phase. `LocalizationManager` doit de toute façon être touché pour être branché sur `ItemData` — le faire dépendre de `PlayerSettings` dès maintenant plutôt que de `PlayerPrefs` brut, puis re-toucher `LocalizationManager` une deuxième fois en Phase 3, serait le même genre de double travail qu'on vient d'éviter pour `ItemData`. Seul le **branchement UI** (sélecteur de langue dans un menu Options, abonnements `OnLanguageChanged` dans les panels) reste en Phase 3, puisqu'aucun menu Options n'existe encore dans le projet.

**RuneData/GemData sont exclus de cette phase** — voir `note-rune-gem-hors-scope.md`. Le GDD v3.6 §5.11 décrit un système de runes totalement différent du code actuel, refonte à part entière. RuneData/GemData n'héritent pas de `ItemData` tant que cette refonte dédiée n'a pas eu lieu.

---

## 1. Hiérarchie de classes retenue

**`ItemData`** (`abstract class : ScriptableObject`) — reprend le GDD §5.1, avec `displayName`/`description` en `LocalizedText` dès la conception :

```csharp
public abstract class ItemData : ScriptableObject
{
    [Header("Identité")]
    public string        itemID;        // snake_case stable — clé technique, logs/saves/comparaisons, JAMAIS localisé
    public LocalizedText  displayName = new LocalizedText();   // nom affiché — fr/en
    public LocalizedText  description = new LocalizedText();   // description affichée — fr/en
    public Sprite         icon;

    [Header("Inventaire / Économie")]
    public bool isBound       = false;
    public bool isStackable   = false;
    public int  stackSize     = 99;
    public int  requiredLevel = 1;
    public int  vendorPrice   = 0;
}
```

**`EquipmentDataBase : ItemData`** — pour les 9 types qui portent déjà un `EquipmentConfig` (Weapon, Armor, Helmet, Gloves, Boots, Jewelry, Spirit, CosmeticHead, CosmeticBody) :

```csharp
public abstract class EquipmentDataBase : ItemData
{
    [Header("Configuration (bonus, effets de statut, résistances, on-hit)")]
    public EquipmentConfig config;
}
```

`CardData`, `ResourceData`, `ConsumableData`, `CosmeticData` (legacy) héritent directement de `ItemData` (pas de `config`, comme aujourd'hui).

**Pourquoi cette coupure en deux niveaux plutôt qu'un seul `ItemData` avec `config` dessus** : le GDD dit explicitement "ItemData ne contient aucune stat de combat" — `config` (bonus, statuts, résistances, on-hit) est combat-adjacent, et forcer Resource/Consumable/Card/Cosmetic-legacy à hériter d'un champ `config` qu'ils n'utilisent jamais serait la même duplication inutile qu'on cherche à éliminer, juste déplacée d'un cran.

**Pourquoi `= new LocalizedText()` sur `displayName`/`description`** : voir l'audit ci-dessous (§2.1) — évite un `NullReferenceException` si un SO est créé sans jamais ouvrir l'Inspector (ex: `ScriptableObject.CreateInstance<T>()` à l'exécution, ou un ancien save désérialisé partiellement).

**`[CreateAssetMenu]` reste uniquement sur les 13 types concrets** (`WeaponData`, `ArmorData`, ...) — jamais sur `ItemData`/`EquipmentDataBase`, qui sont `abstract` et ne doivent pas apparaître dans le menu de création d'assets.

**Amélioration suggérée — `OnValidate()` sur `ItemData`** : avertir en éditeur si `itemID` est vide, dans le même esprit que l'avertissement déjà présent sur `WeaponData` (chevauchement de fourchette) ou `ConditionData` (reward invalide) — cohérent avec le style déjà utilisé dans le projet, coût faible :
```csharp
#if UNITY_EDITOR
private void OnValidate()
{
    if (string.IsNullOrEmpty(itemID))
        Debug.LogWarning($"[ItemData] {name} : itemID vide — à remplir avant utilisation en jeu.", this);
}
#endif
```

## 2. Infra multilangue + settings joueur (prérequis, révisé)

### 2.1 — Audit de `LocalizedText.cs`/`LocalizationManager.cs` (brouillons existants)

Avant de les faire atterrir dans le projet réel, relecture critique des deux fichiers brouillon — problèmes trouvés et corrigés directement dans `a implémenter/` (fichiers déjà mis à jour) :

1. **`LocalizedText.Get()` plantait si l'objet lui-même est `null`** — pas de garde côté appelant. Corrigé par deux moyens complémentaires : (a) `ItemData.displayName`/`description` initialisés à `new LocalizedText()` par défaut (§1 ci-dessus), donc jamais `null` sur un item normal ; (b) ajout de `LocalizedText.GetSafe(text, lang, fallback)` (méthode statique, tolère `text == null`) pour les points d'entrée externes où la garantie n'existe pas (ex : donnée restaurée d'un vieux save, mail désérialisé).
2. **Pas de moyen de tester "texte vide" sans lire un champ interne** — plusieurs sites actuels font `string.IsNullOrEmpty(condition.description)` sur un `string` brut ; une fois `description` en `LocalizedText`, il fallait un équivalent. Ajout de `LocalizedText.IsEmpty` (`true` si `fr` et `en` sont tous les deux vides).
3. **`LocalizedText.Join(separator, parts)` plantait si `parts` (le tableau lui-même, pas un élément) était `null`** — garde ajoutée, retourne un `LocalizedText` vide dans ce cas plutôt que de lever une exception.
4. **`LocalizationManager` gérait son propre `PlayerPrefs` en dur** (`PlayerPrefs.SetInt("aethertree_language", ...)`) — en prévision de `PlayerSettings` (voir §2.2), ce mécanisme de persistance dédié aurait été à refaire une deuxième fois en Phase 3. Réécrit pour déléguer à `PlayerSettings.Current.language` — API publique (`CurrentLanguage`/`OnLanguageChanged`/`SetLanguage`/`LoadSavedLanguage`) strictement inchangée, donc aucun impact sur `ItemData` ni les 13 types migrés dans cette phase, qui ne dépendent que de cette API.
5. **Point d'attention non-corrigible dans le code lui-même (contrainte d'ordre au runtime)** : `LoadSavedLanguage()` doit être appelé avant le premier `Awake()`/`OnEnable()` d'un panel UI lisant `CurrentLanguage` — sinon ce panel lit la langue par défaut (FR) et ne se corrige jamais tant qu'aucun `OnLanguageChanged` n'est levé, puisque `LoadSavedLanguage()` lui-même n'émet pas l'event (documenté comme volontaire : rien n'est encore abonné au premier frame). Recommandation ajoutée en commentaire dans le fichier : appeler `LoadSavedLanguage()` depuis une méthode `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` plutôt que depuis un `GameManager.Awake()` dont l'ordre d'exécution face aux autres scripts n'est pas garanti par défaut dans Unity (sauf Script Execution Order explicite). À trancher au moment de l'implémentation — pas bloquant pour le reste.
6. **Risque de fuite mémoire par abonnement oublié** — `OnLanguageChanged` est un event statique : un panel UI qui s'abonne dans `OnEnable()` sans se désabonner dans `OnDisable()` reste référencé indéfiniment par `LocalizationManager`, même après destruction du panel. Pas un bug du fichier (c'est le pattern standard Unity pour un event statique), mais une règle stricte à respecter partout où `LocalizationManager.OnLanguageChanged` est utilisé (déjà documentée dans `note-systeme-multilangue.md` §2.3, reconfirmée ici).

### 2.2 — `PlayerSettings` (nouveau, avancé depuis la Phase 3)

Nouveau fichier `Assets/Scripts/a implémenter/PlayerSettings.cs` (déjà écrit) — classe unique de persistance pour les options joueur non liées à la progression du personnage :
```csharp
[System.Serializable]
public class PlayerSettings
{
    public Language language = Language.FR;
    // Futurs champs (non spécifiés, pas ajoutés en avance) :
    // public float musicVolume = 1f; public float sfxVolume = 1f; public bool fullscreen = true;

    public static PlayerSettings Current { get; private set; }
    public static void Load() { /* PlayerPrefs JSON, valeurs par défaut si absent/corrompu */ }
    public void Save() { /* PlayerPrefs JSON */ }
}
```
Stockage : un seul blob JSON dans `PlayerPrefs` (clé `"aethertree_settings"`), via `JsonUtility` — cohérent avec l'usage `PlayerPrefs` déjà en place pour la langue, pas de nouveau mécanisme de fichier à gérer. `Load()` avale les erreurs de désérialisation (JSON corrompu/format changé) et retombe sur des valeurs par défaut plutôt que de faire planter le bootstrap.

### 2.3 — Landing dans le projet réel

Déplacer (pas copier) `Assets/Scripts/a implémenter/LocalizedText.cs`, `LocalizationManager.cs` **et** `PlayerSettings.cs` vers `Assets/Scripts/Data/Localization/` — les trois fichiers sont maintenant finalisés côté brouillon (audit §2.1 appliqué). Ajouter l'appel `LocalizationManager.LoadSavedLanguage()` au bootstrap (voir point d'attention §2.1.5 sur l'ordre d'exécution).

**Ce qui reste en Phase 3** (voir `note-refonte-settings-langue-phase3.md`, révisé) : uniquement le branchement UI — sélecteur de langue dans un menu Options (pas encore existant), abonnements `OnLanguageChanged` dans les panels d'affichage. Le cœur de la persistance (`PlayerSettings`) et le branchement `LocalizationManager` dessus sont désormais faits ici, en Phase 1.

## 3. Pattern de migration (identique pour les 13 fichiers concernés)

1. `XxxData : EquipmentDataBase` (9 types) ou `XxxData : ItemData` (4 types : Card, Resource, Consumable, CosmeticData legacy).
2. Supprimer le champ `xxxName` (string) — remplacé par `displayName` (`LocalizedText`) hérité. Supprimer `description` (string) — remplacé par `description` (`LocalizedText`) hérité, même nom mais type différent. Supprimer `icon` s'il duplique — hérité. Supprimer `EquipmentConfig config` inline si présent — hérité pour les 9 types concernés. **Supprimer aussi `requiredLevel`** — hérité de `ItemData`, voir avertissement ci-dessous, c'est le point le plus facile à oublier de tout ce pattern.
3. **Pas de `[FormerlySerializedAs]` possible sur `displayName`/`description`** — `string → LocalizedText` est un changement de type, pas juste de nom ; Unity ne migre pas la valeur automatiquement même avec cet attribut (il ne fonctionne que pour un renommage à type identique). Sur les ~5 `.asset` de test existants (armes/armures), le nom/description sera réinitialisé à vide après migration — à retaper manuellement dans l'Inspector (contenu de test/placeholder uniquement à ce stade du projet, coût négligeable).
4. Instance correspondante (`WeaponInstance`, `ArmorInstance`, etc.) : l'accesseur `WeaponName`/`ArmorName`/etc. garde sa signature `string` mais lit désormais `data.displayName.Get(LocalizationManager.CurrentLanguage)` au lieu de `data.weaponName`. Ajouter `ItemId => data.itemID` (nouveau champ, vide par défaut sur les assets existants — à remplir manuellement).
5. `ResourceData.maxStack` → `stackSize` (hérité), `ResourceData.sellPrice` → `vendorPrice` (hérité) ; `ConsumableData.maxStack` → `stackSize` (hérité). **`[FormerlySerializedAs("maxStack")]`/`[FormerlySerializedAs("sellPrice")]` doivent être posés sur les champs de `ItemData` lui-même** (`stackSize`/`vendorPrice`), pas sur chaque type dérivé — un champ hérité ne peut pas porter un attribut différent selon la sous-classe qui l'utilise. Sans danger pour les 11 autres types qui n'ont jamais eu de champ `maxStack`/`sellPrice` : l'attribut ne fait simplement rien pour eux (pas de clé correspondante dans leur YAML existant).
6. **`isStackable`** — reste à `false` par défaut sur la base (correct pour la majorité des 13 types). `ResourceData` et `ConsumableData` sont toujours stackables aujourd'hui (logique `Add()`/`Remove()`/`quantity` déjà en place) — passer leur `isStackable` à `true`, soit en assignant `isStackable = true` via `Reset()` (callback Unity appelé à la création de l'asset dans l'éditeur) sur ces deux types, soit en acceptant de le cocher manuellement sur chaque asset existant (2 types seulement, peu d'assets). Rien ne lit encore ce flag aujourd'hui donc ce n'est pas un bug actif, mais une incohérence à corriger maintenant plutôt que de la découvrir plus tard.
7. **`vendorPrice`** — nouveau champ sur les 9 types `EquipmentDataBase` + `CardData`. **Câblé** dans `ShopUI.GetSellPrice()` (post Phase 1, session suivante) : `EquipSellPrice(vendorPrice, powerStat)` = `vendorPrice × powerStat` si `powerStat > 0`, sinon `vendorPrice` seul. `powerStat` = `FinalDamageMax` pour Weapon, somme des 3 défenses (Final* pour Armor, brutes pour Helmet/Gloves/Boots/Jewelry qui n'ont pas encore de roll). Spirit/CosmeticHead/CosmeticBody/Card (jamais gérés avant dans `GetSellPrice`) utilisent `vendorPrice` seul, sans multiplicateur. Remplace l'ancien calcul ad-hoc (`FinalDamageMax×5`, `FinalMeleeDefense×4`, valeurs fixes 20/15/15/30). **Conséquence** : tous les `.asset` existants ont `vendorPrice = 0` par défaut → non vendables tant que le designer ne remplit pas ce champ (comportement voulu par le GDD : `0 = non vendable au PNJ`), mais ça change le comportement précédent où ces items se vendaient automatiquement à un prix calculé.
8. Ne rien toucher d'autre : prefabs, mécaniques Fusion (Gloves/Boots), GemSlots (Jewelry), XP/level (Spirit), stub TODO (Card), champs Collectible (Resource), champs Potion/DungeonStone/TeleportItem (Consumable).

**Fichiers déjà confirmés porteurs d'un `requiredLevel` local à supprimer** (vérifié par grep sur le projet réel) : `WeaponData`, `ArmorData`, `HelmetData`, `GlovesData`, `BootsData`, `JewelryData`, `SpiritData`, `CosmeticDataHead`, `CosmeticDataBody`, `CardData`, `ConsumableData` — soit 11 des 13 fichiers. Seuls `ResourceData` et `CosmeticData` (legacy) n'en avaient pas (ils hériteront simplement du champ sans rien à retirer).

**Pourquoi c'est important de ne pas l'oublier** : si un `requiredLevel` local reste déclaré sur un type dérivé qui hérite déjà d'un `requiredLevel` depuis `ItemData`, ça ne provoque **pas d'erreur de compilation** — juste un warning `CS0108` ("hides inherited member"). Le champ dérivé masque silencieusement le champ hérité : lire `.requiredLevel` via une variable typée `WeaponData` lit le bon champ (celui du designer, visible dans l'Inspector), mais lire la même donnée via une variable typée `ItemData`/`EquipmentDataBase` (ex: du code générique qui manipule des `ItemData` plutôt que des types concrets) lit l'autre champ, resté à sa valeur par défaut (`1`), jamais mis à jour par le designer. Bug silencieux, difficile à repérer en test puisque tout compile et que le code spécifique-au-type continue de fonctionner normalement.

### Fichiers concernés

| # | Fichier | Base |
|---|---|---|
| 1 | `Data/Equipment/WeaponData.cs` | `EquipmentDataBase` |
| 2 | `Data/Equipment/ArmorData.cs` | `EquipmentDataBase` |
| 3 | `Data/Equipment/HelmetData.cs` | `EquipmentDataBase` |
| 4 | `Data/Equipment/GlovesData.cs` | `EquipmentDataBase` |
| 5 | `Data/Equipment/BootsData.cs` | `EquipmentDataBase` |
| 6 | `Data/Equipment/JewelryData.cs` | `EquipmentDataBase` |
| 7 | `Data/Equipment/SpiritData.cs` | `EquipmentDataBase` |
| 8 | `Data/Equipment/CosmeticDataHead.cs` | `EquipmentDataBase` |
| 9 | `Data/Equipment/CosmeticDataBody.cs` | `EquipmentDataBase` |
| 10 | `Data/Equipment/CardData.cs` | `ItemData` |
| 11 | `Data/Inventory/ResourceData.cs` | `ItemData` (+ renommage stackSize/vendorPrice) |
| 12 | `Data/Inventory/ConsumableData.cs` | `ItemData` (+ renommage stackSize) |
| 13 | `Data/Inventory/CosmeticData.cs` (legacy) | `ItemData` |

**Note sur `Data/Inventory/CosmeticData.cs`** : duplique fonctionnellement `CosmeticDataHead`/`CosmeticDataBody` (enum de slot `HeadSkin/BodySkin` vs deux classes séparées sans enum). Migré vers `ItemData` comme les autres, **sans** tenter de réconcilier les deux systèmes cosmétiques parallèles — décision de design séparée à trancher explicitement, pas un renommage mécanique.

## 4. Sites consommateurs à mettre à jour

- **`Data/Quests/QuestData.cs`** — vérifié en direct : `QuestRewardItem.DisplayName` getter (lignes 37-54). 9 des 11 branches → `.displayName.Get(LocalizationManager.CurrentLanguage)` : `weapon.weaponName`, `armor.armorName`, `helmet.helmetName`, `gloves.glovesName`, `boots.bootsName`, `jewelry.jewelryName`, `spirit.spiritName`, `consumable.consumableName`, `resource.resourceName`. **Les 2 dernières branches, `gem.gemName` (ligne 50) et `rune.runeName` (ligne 51), ne changent PAS** — RuneData/GemData sont hors scope de cette phase, ces deux lignes restent des `string` bruts tels quels. `GetIcon()` (lignes 58-72, même fichier) ne change pas du tout — `.icon` reste `Sprite` pour tous, hérité ou non.
- **`UI/PanelSecondaire/ShopUI.cs`** — vérifié en direct : `GetEntryName()` (switch lignes 586-599, 8 branches équipement/resource/consumable) → `.displayName.Get(...)`. `GetEntryDesc()` (lignes 604-617) : `.description` → `.description.Get(...)` (ne pas toucher les branches `SkillData`/`PermanentSkillData`, lignes 616-617, hors scope). Second helper (lignes 669-675, `item.WeaponInstance?.data.description` etc.) : même changement.
- **`Systems/MailboxSystem.cs`** — vérifié en direct :
  - `MailReward.GetDisplayName()` (lignes 77-100) : les 9 branches `(rewardEquipment as WeaponData)?.weaponName` etc. → `.displayName.Get(LocalizationManager.CurrentLanguage)`.
  - `DistributeReward()` : tous les `Debug.Log($"...{weapon.weaponName}")` (lignes 269, 278, 287, 296, 305, 314, 323, 332, 341, 350, 359, 368) → remplacer par `weapon.itemID` (ID stable, jamais localisé) — un log ne doit jamais dépendre de la langue du joueur ayant déclenché l'action.
  - **Amélioration optionnelle** : `MailReward.rewardEquipment` (ligne 59) et `ConditionReward.rewardEquipment` (`Progression/Conditions/ConditionData.cs` ligne 104) sont aujourd'hui typés `ScriptableObject` — une fois les 9 types équipement migrés vers `ItemData`, ces deux champs pourraient être retypés `ItemData` pour empêcher un designer de glisser accidentellement un SO sans rapport dans l'Inspector. Non requis pour la compilation (le cast `as WeaponData` etc. continue de fonctionner identiquement), pure amélioration de robustesse — à faire ou non selon le temps disponible.
- **`UI/Shared/TooltipSystem.cs`** — sites `FormatDesc(x.data?.description)` concernant les 13 types migrés (weapon/armor/helmet/gloves/boots/jewelry/spirit/consumable/resource — pas rune/gem, hors scope) → `FormatDesc(x.data?.description?.Get(LocalizationManager.CurrentLanguage))`.
- **`Systems/InventorySystem.cs`** — `InventoryItem.Name` : aucun changement requis (l'accesseur Instance garde sa signature `string`, seule son implémentation interne change via l'étape 4 du pattern ci-dessus).

## 5. Ordre d'implémentation proposé

1. Déplacer `LocalizedText.cs`/`LocalizationManager.cs`/`PlayerSettings.cs` vers `Data/Localization/`, ajouter l'appel `LocalizationManager.LoadSavedLanguage()` au bootstrap — compile seul.
2. Créer `ItemData.cs` et `EquipmentDataBase.cs` — compile seul, rien ne les référence encore.
3. Migrer `WeaponData`/`WeaponInstance` (pilote) — valide le pattern une fois avant de le répéter 12 fois. Corriger immédiatement ses consommateurs directs (`QuestData`, `ShopUI`, `MailboxSystem`, `TooltipSystem` pour la branche Weapon) pour revenir à 0 erreur.
4. Migrer `ArmorData`/`ArmorInstance` — même traitement, corriger ses consommateurs directs.
5. Migrer les 11 fichiers restants (groupés par switch consommateur partagé si utile : Helmet/Gloves/Boots/Jewelry ensemble, puis Spirit/CosmeticHead/CosmeticBody/Card ensemble, puis Resource/Consumable/CosmeticData legacy ensemble).
6. Passe finale de vérification sur `ShopUI`/`MailboxSystem`/`QuestData`/`TooltipSystem` — confirmer 0 référence `.xxxName` ou `.description` non-`.Get(...)` restante.

## 6. Ce qui va casser à la compilation / points d'attention

- Tout code lisant directement `weaponData.weaponName` (ou `armorName`, `helmetName`...) comme un `string` — doit passer par `.displayName.Get(LocalizationManager.CurrentLanguage)`.
- Tout code lisant directement `xxxData.description` comme un `string` — même chose, `.description.Get(...)`.
- `ResourceData.maxStack`/`sellPrice` et `ConsumableData.maxStack` renommés — chercher les usages directs de ces noms de champs.
- Les ~5 `.asset` d'arme/armure existants perdent leur nom/description au premier reserialize (string→LocalizedText, pas de migration automatique) — à retaper manuellement.
- `itemID` est un nouveau champ vide sur tous les assets existants — les logs qui l'utiliseront afficheront une chaîne vide tant qu'il n'est pas rempli manuellement dans l'Inspector.
- **`requiredLevel` dupliqué sur un type dérivé après migration ne casse PAS la compilation** — juste un warning `CS0108` facile à rater dans la sortie Console. Vérifier explicitement son absence dans chacun des 11 fichiers concernés (liste en §3) après migration, ne pas se fier uniquement à l'absence d'erreur.

Suggestion de grep avant de considérer la phase terminée :
```
grep -rn "\.weaponName\b\|\.armorName\b\|\.helmetName\b\|\.glovesName\b\|\.bootsName\b\|\.jewelryName\b\|\.spiritName\b\|\.consumableName\b\|\.resourceName\b\|\.cosmeticName\b\|\.cardName\b" .
grep -rn "\.description\b" .   # puis exclure manuellement les `.Get(` déjà corrects
grep -rn "public int requiredLevel" Assets/Scripts/Data/Equipment Assets/Scripts/Data/Inventory   # doit ne renvoyer que ItemData.cs
```
