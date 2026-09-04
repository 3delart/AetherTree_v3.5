# Nomenclature ID — Plan d'implémentation

> Exécuté en inline dans la même session (pas de subagents) — projet Unity solo, pas de
> framework de test automatisé en place. Vérification = compilation propre + contrôle visuel
> dans l'Inspector Unity, pas de suite pytest/NUnit (aucune n'existe dans ce projet).

**Goal :** ajouter un champ `xxxID` (string, stable) + son auto-remplissage `OnValidate()` sur
les 9 classes de SO qui n'en ont pas encore, conformément à `note-nomenclature-id.md`, et
corriger les 2 références de loot cassées trouvées pendant l'audit.

**Architecture :** un seul pattern répété 9 fois — `if (string.IsNullOrEmpty(xxxID)) xxxID = name;`
dans `OnValidate()` (déjà utilisé sur `PNJData`/`PassiveSkillData` ce jour). Étendre les
`OnValidate()` existants (`MobData`, `SkillData`, `PNJData`, `PassiveSkillData`) plutôt que
d'en créer un second (interdit en C#, une seule méthode `OnValidate` par classe).

**Spec :** `Assets/Scripts/a implémenter/note-nomenclature-id.md`

## Global Constraints

- Casse : `snake_case`, un seul champ `xxxID` par classe, jamais deux fois `OnValidate()`.
- Le champ ID va toujours dans le bloc `[Header("Identité")]` existant, juste avant le champ de
  nom d'affichage.
- `OnValidate()` reste toujours dans `#if UNITY_EDITOR` / `#endif`.
- Ne JAMAIS committer sans validation explicite de l'utilisateur (règle du projet, pas de
  commit automatique même si le pattern général de la plupart des plans en propose un).
- Renommage effectif des ~35 assets existants : **hors scope de ce plan**, travail manuel de
  Florian en éditeur (voir note §7).

---

### Task 1 : `MobData.mobID`

**Files :**
- Modify : `Assets/Scripts/Data/Mobs/MobData.cs:24-28` (champ), `:164-175` (OnValidate existant)

**Interfaces :** aucune — champ purement additif, rien d'autre n'en dépend dans ce plan.

- [ ] **Étape 1 — ajouter le champ**, juste avant `mobName` ligne 26 :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"mob_\"\n" +
             "(ex: \"mob_loup_gris\"). Ne JAMAIS afficher au joueur — voir mobName pour l'affichage.")]
    public string mobID;
```

- [ ] **Étape 2 — étendre l'`OnValidate()` existant** (commence ligne 165) en ajoutant au début
  du corps de la méthode :

```csharp
        if (string.IsNullOrEmpty(mobID))
            mobID = name;

```
  (les lignes déjà présentes — `baseAtkMax`, `atkMaxPerLevel`, `mobType == MobType.Capturable`
  — restent inchangées après cet ajout)

- [ ] **Étape 3 — vérifier la compilation** (retour Unity au focus, pas d'erreur Console).

---

### Task 2 : `RuneData.runeID`

**Files :**
- Modify : `Assets/Scripts/Data/Inventory/RuneData.cs:55-57`

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `runeName` :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"rune_\"\n" +
             "(ex: \"rune_weapon_t2\"). Ne JAMAIS afficher au joueur — voir runeName pour l'affichage.")]
    public string runeID;
```

- [ ] **Étape 2 — ajouter un nouvel `OnValidate()`** (n'existe pas encore dans cette classe),
  juste après la fermeture de la classe... non — À L'INTÉRIEUR de la classe `RuneData`, en tout
  dernier membre avant l'accolade fermante de la classe :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(runeID))
            runeID = name;
    }
#endif
```

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 3 : `GemData.gemID`

**Files :**
- Modify : `Assets/Scripts/Data/Inventory/GemData.cs:49-51`

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `gemName` :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"gem_\"\n" +
             "(ex: \"gem_ruby_t1\"). Ne JAMAIS afficher au joueur — voir gemName pour l'affichage.")]
    public string gemID;
```

- [ ] **Étape 2 — ajouter un nouvel `OnValidate()`** en dernier membre de la classe `GemData` :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(gemID))
            gemID = name;
    }
#endif
```

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 4 : `PNJData.pnjID`

**Files :**
- Modify : `Assets/Scripts/Data/PNJ/PNJData.cs:30-32` (champ), `:176-191` (OnValidate existant)

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `pnjName` (ligne 31) :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"pnj_\"\n" +
             "(ex: \"pnj_forgeron_braven\"). Ne JAMAIS afficher au joueur — voir pnjName pour l'affichage.")]
    public string pnjID;
```

- [ ] **Étape 2 — étendre l'`OnValidate()` existant** en ajoutant au tout début du corps :

```csharp
        if (string.IsNullOrEmpty(pnjID))
            pnjID = name;

```
  Le reste de la méthode (warnings `canFight`/`basicAttackSkill`, `canDie`, le bloc
  `attackRange`) reste identique après cet ajout.

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 5 : `DialogueData.dialogueID`

**Files :**
- Modify : `Assets/Scripts/Data/PNJ/DialogueData.cs:22-24`

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `dialogueName` (ligne 23) :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"dlg_\"\n" +
             "(ex: \"dlg_forgeron_braven\"). Ne JAMAIS afficher au joueur — voir dialogueName pour l'affichage.")]
    public string dialogueID;
```

- [ ] **Étape 2 — ajouter un nouvel `OnValidate()`** en dernier membre de la classe
  `DialogueData` (avant l'accolade fermante de la classe, après `GetStage`/`GetFirstStage`) :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(dialogueID))
            dialogueID = name;
    }
#endif
```

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 6 : `SkillData.skillID`

**Files :**
- Modify : `Assets/Scripts/Data/Skills/SkillData.cs:44-49` (champ), `:234-260` env. (OnValidate existant)

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `skillName` (ligne 46) :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"skl_\"\n" +
             "(ex: \"skl_shortsword_combo3\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string skillID;
```

- [ ] **Étape 2 — étendre l'`OnValidate()` existant** (commence ligne 235) en ajoutant au tout
  début du corps :

```csharp
        if (string.IsNullOrEmpty(skillID))
            skillID = name;

```
  Le reste (bloc `MultiHit`/`attackAnimation`) reste identique après cet ajout.

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 7 : `PassiveSkillData.passiveID`

**Files :**
- Modify : `Assets/Scripts/Data/Skills/PassiveSkillData.cs` (champ `skillName`/`description`
  section Identité, et `OnValidate()` ajouté ce jour même — voir tout premier bloc du fichier)

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, juste avant `skillName` dans le bloc `[Header("Identité")]` :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"pas_\"\n" +
             "(ex: \"pas_dernier_rempart\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string passiveID;
```

- [ ] **Étape 2 — étendre l'`OnValidate()` existant** (ajouté aujourd'hui, vérifie
  `OnInterval`/`cooldown`) en ajoutant au tout début du corps :

```csharp
        if (string.IsNullOrEmpty(passiveID))
            passiveID = name;

```
  Le bloc `OnInterval`/`cooldown<=0` déjà présent reste inchangé après cet ajout.

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 8 : `PermanentSkillData.permanentID`

**Files :**
- Modify : `Assets/Scripts/Data/Skills/PermanentSkillData.cs:27-32`

**Interfaces :** aucune.

- [ ] **Étape 1 — ajouter le champ**, avant `skillName` (ligne 30) :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"perm_\"\n" +
             "(ex: \"perm_vitalite_1\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string permanentID;
```

- [ ] **Étape 2 — ajouter un nouvel `OnValidate()`** en dernier membre de la classe
  `PermanentSkillData` :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(permanentID))
            permanentID = name;
    }
#endif
```

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 9 : `StatusEffectData.effectID` (base commune Buff/Debuff)

**Files :**
- Modify : `Assets/Scripts/Data/StatusEffect/StatusEffectData.cs:130-132`

**Interfaces :** hérité par `BuffData`/`DebuffData` — aucun changement requis sur ces deux
fichiers, le champ + `OnValidate()` posés une seule fois sur la base suffisent.

- [ ] **Étape 1 — ajouter le champ**, avant `effectName` (ligne 131) dans la classe abstraite
  `StatusEffectData` :

```csharp
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"buff_\"\n" +
             "ou \"dbf_\" selon le type (ex: \"buff_shield\", \"dbf_poison\"). Ne JAMAIS afficher au\n" +
             "joueur — voir effectName pour l'affichage.")]
    public string effectID;
```

- [ ] **Étape 2 — ajouter un nouvel `OnValidate()`** en dernier membre de la classe abstraite
  `StatusEffectData`, juste après `CreateInstance` (ligne 139) :

```csharp
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(effectID))
            effectID = name;
    }
#endif
```

- [ ] **Étape 3 — vérifier la compilation.**

---

### Task 10 : ~~Corriger les 2 loot tables~~ — annulée, faux positif

`LootEntry.itemSO` (référence directe) est ce que `LootTable.CreateInventoryItem()` utilise
réellement au roll — `itemID` est un champ legacy explicitement non lu quand `itemSO` est
assigné. Vérifié sur les deux `.asset` : `itemSO` est correctement rempli partout. Rien à
corriger, cette tâche ne s'exécute pas. Voir `note-nomenclature-id.md` §6.

---

## Hors scope de ce plan

- Renommage des ~35 assets existants vers la nouvelle convention (`wpn_`, `res_`, etc.) —
  manuel, Florian, en éditeur (voir `note-nomenclature-id.md` §5 pour la liste complète).
- `LootLoup.asset` : si les 4 items manquants n'existent pas encore comme ressources dans le
  projet, c'est un ajout de contenu à part, pas juste une correction de référence — à confirmer
  avec Florian avant d'exécuter la Task 10.
