# Nomenclature ID — convention unifiée pour tous les SO

**Contexte** : le projet a accumulé ~10 catégories de ScriptableObject (ressources,
consommables, équipement, skills, mobs, recettes, pnj, quêtes, statuseffect, zones,
conditions...) avec des clés stables (`xxxID`) posées au fil de l'eau, sans convention
commune — casse incohérente (`Flowerid01`, `wheat_id`, `ChickenFeather`), pas de préfixe de
catégorie, et certains types (Mob, PNJ, Rune, Gemme, Buff/Debuff, Dialogue) n'ont même pas de
champ ID du tout. Cette note fixe la convention définitive et sert de référence à chaque
nouvel asset créé, sans avoir à redemander.

Prolonge directement le principe déjà posé dans `note-systeme-multilangue.md` §2.1 : une clé
technique stable (`xxxID`), jamais traduite, jamais affichée au joueur, séparée du texte
affiché (`LocalizedText`).

---

## 1. Principe

**Le nom de l'asset Unity = l'ID.** Chaque SO a un champ `xxxID` (string, snake_case) dont la
valeur doit toujours être identique au nom du fichier `.asset`. Pas d'exception — même les
types qui s'appuyaient jusqu'ici sur `this.name` directement (Skill/Passif/Permanent) gagnent
leur propre champ explicite, pour ne plus avoir à se souvenir "pour ces 3 types-là, regarde le
nom du fichier à la place".

Format : `<prefixe>_<descriptif>[_<variante>]`, tout en snake_case, minuscules, sans accents.

- `wpn_shortsword_fire`
- `res_fleur_lune`
- `pnj_forgeron_braven`

## 2. Table des préfixes (3-4 lettres)

| Catégorie | Préfixe | Classe C# | Champ |
| --- | --- | --- | --- |
| Arme | `wpn` | `WeaponData` | `itemID` (hérité `ItemData`) |
| Armure | `arm` | `ArmorData` | `itemID` |
| Casque | `hlm` | `HelmetData` | `itemID` |
| Gants | `glv` | `GlovesData` | `itemID` |
| Bottes | `bts` | `BootsData` | `itemID` |
| Bijou | `jwl` | `JewelryData` | `itemID` |
| Esprit | `spr` | `SpiritData` | `itemID` |
| Cosmétique | `cos` | `CosmeticDataHead/Body` | `itemID` |
| Talisman | `talisman` | `TalismanData` | `itemID` |
| Ressource | `res` | `ResourceData` | `itemID` |
| Consommable | `cons` | `ConsumableData` | `itemID` |
| Rune | `rune` | `RuneData` | `runeID` *(nouveau champ)* |
| Gemme | `gem` | `GemData` | `gemID` *(nouveau champ)* |
| Skill actif | `skl` | `SkillData` | `skillID` *(nouveau champ)* |
| Passif | `pas` | `PassiveSkillData` | `passiveID` *(nouveau champ)* |
| Permanent | `perm` | `PermanentSkillData` | `permanentID` *(nouveau champ)* |
| Mob | `mob` | `MobData` | `mobID` *(nouveau champ)* |
| Recette | `rcp` | `RecipeData` | `recipeID` (déjà là) |
| PNJ | `pnj` | `PNJData` | `pnjID` *(nouveau champ)* |
| Dialogue | `dlg` | `DialogueData` | `dialogueID` *(nouveau champ)* |
| Quête | `quest` | `QuestData` | `questID` (déjà là) |
| Buff | `buff` | `BuffData` | `effectID` *(nouveau champ, sur base `StatusEffectData`)* |
| Debuff | `dbf` | `DebuffData` | `effectID` (même champ hérité) |
| Zone | `zone` | `ZoneData` | `zoneID` (déjà là) |
| Condition | `cond` | `ConditionData` | `conditionID` (déjà là) |
| Table de loot | `loot` | `LootTable` | `lootID` *(nouveau champ — jamais lu par string en code, juste pour l'uniformité du Project window, `LootTable` reste toujours référencé par lien direct)* |

## 3. Auto-remplissage sans risque de renommage cassant

```csharp
#if UNITY_EDITOR
private void OnValidate()
{
    if (string.IsNullOrEmpty(xxxID))
        xxxID = name; // pré-rempli une seule fois, jamais écrasé après
}
#endif
```

Ne remplit `xxxID` que s'il est **encore vide** — un renommage du fichier après coup ne touche
plus à l'ID une fois posé. Nommer l'asset correctement dès sa création (au moment où Unity met
le nom en édition juste après "Create") est le réflexe à prendre ; si l'ordre n'est pas
respecté, ça se corrige tout seul au prochain clic dans l'Inspector tant que le champ est
resté vide.

Même pattern déjà utilisé ce jour sur `PNJData.OnValidate()`/`PassiveSkillData.OnValidate()` —
rien de nouveau à inventer, juste à généraliser.

## 4. Champs à ajouter — fait (13 classes)

`MobData`, `RuneData`, `GemData`, `PNJData`, `DialogueData`, `SkillData`,
`PassiveSkillData`, `PermanentSkillData`, `StatusEffectData` (base commune
`BuffData`/`DebuffData`), `LootTable` — chacun un nouveau champ `xxxID` + son `OnValidate()`
auto-fill (voir §3).

`ItemData`, `ConditionData`, `QuestData`, `ZoneData` avaient déjà leur champ (`itemID`/
`conditionID`/`questID`/`zoneID`) mais sans auto-fill — `OnValidate()` étendu (ou ajouté pour
`QuestData`/`ZoneData`) pour appliquer le même pattern. `ItemData.OnValidate()` faisait juste un
`Debug.LogWarning` si vide — remplacé par le fill, plus de warning nécessaire.

## 5. Inventaire des 35 assets existants à corriger

Voir le message de conversation du 2026-09-01 pour le détail par asset (tableau complet par
catégorie) — pas dupliqué ici pour éviter une désynchronisation entre deux listes. Court résumé
par catégorie :

- **Équipement/Ressources** (8 assets, `itemID` déjà présent, juste à reformater) :
  `DataFaibleShortSword`, `DataMoyenneShortSword`, `DataForteShortSword`, `BowWeapon`,
  `ArmureMelee`, `WheatData`, `DataPlume_de_poule`, `FlowerData`.
- **Skills** (9 assets) : `SkillPoule`, `BasicAttackPoule`, `BasicAttack` (Wolf), `Skill_Loup`,
  `pnjskill`, `UnArmedSkill`, `ComboTroisCoup`, `WaterBasicShortSword`, `NeutralBasicShortSword`.
- **Permanent** (1) : `Permanent_01`.
- **Mobs** (4) : `ManequinMoyen`, `ManequinFaible`, `ManequinFort`, `TestLoup`.
- **PNJ** (5) : `PNJ_01`, `PNJ_02`, `TestPnjData`, `PNJ_Marchand`, `PNJ_Forgeron`.
- **Dialogues** (5) : `Dialogue_New` ×2, `TestPnjDialogue`, `Dialogue_marchand`,
  `Dialogue_forgeron`.
- **Conditions** (3, `conditionID` déjà présent) : `Condition_ComboTroisCoup`,
  `Condition_BasicAttackWater`, `Condition_ZoneTest`.
- **Quêtes** (2, `questID` déjà présent) : `TestQuest_01`, `TestQuest_02`.
- **Zone** (1, `zoneID` déjà présent) : `Zone_Sous_Arbre`.
- **StatusEffect** (3) : `BurnStatus`, `SlowStatus`, `KnockOutStatus`.

**Hors nomenclature** (infrastructure, pas du contenu) : `WeaponTypeRegistry`, `NewCharacter`.

## 6. Faux positif corrigé — pas de bug de loot

Diagnostic initial erroné : `LootLoup.asset` (4 entrées `itemID` vides) et `LootPoule.asset`
(`itemID: Plum` ≠ `itemID: ChickenFeather` de la ressource réelle) avaient été signalés comme
cassés. **Faux** — `LootTable.CreateInventoryItem()` (le vrai roll utilisé en jeu, `RollAll()`)
résout l'item via `LootEntry.itemSO` (référence directe à l'asset), jamais via `itemID` — ce
champ est explicitement legacy ("non utilisé si itemSO assigné", voir `LootTable.cs`). Les deux
`.asset` ont `itemSO` correctement assigné sur toutes leurs entrées. Rien à corriger ici.

## 7. Hors scope de cette note

- Renommage effectif des ~35 assets existants (Florian, manuel, en éditeur — pas quelque chose
  qu'un script peut faire à sa place vu que ça touche des noms de fichiers .asset + potentiels
  GUID/meta à ne pas casser).
- `RuneData`/`GemData` restent hors du système `ItemData` (voir
  `note-rune-gem-hors-scope.md`) — ce champ `runeID`/`gemID` est ajouté indépendamment de cette
  refonte séparée, pas un prérequis dessus.
- Table de clés UI génériques (labels d'interface fixes, pas liés à un SO) — hors scope, déjà
  noté comme décision à part dans `note-systeme-multilangue.md` §8.3.
