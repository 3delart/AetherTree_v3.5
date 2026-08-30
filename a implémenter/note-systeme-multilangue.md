# Système multilangue — WeaponData et au-delà

**Contexte** : besoin d'afficher le jeu en plusieurs langues (FR/EN pour commencer),
avec possibilité d'en ajouter d'autres plus tard, sans casser les logs, les
sauvegardes, ni devoir retoucher chaque script de données à chaque nouvelle langue.

---

## 1. Problème de départ

`weaponName` et `description` étaient de simples `string` sur `WeaponData` :

```csharp
public string weaponName = "Weapon";
[TextArea] public string description = "";
```

Deux soucis si on localise directement ces champs :

1. **Identité technique vs affichage mélangés** — `weaponName` sert parfois de
   clé implicite (logs, comparaisons, sauvegarde). Une fois traduit, sa valeur
   change selon la langue du joueur → logs illisibles/impossibles à grep,
   sauvegardes fragiles si le nom sert de référence quelque part.
2. **Ajout d'une langue = modifier chaque script de données** si on fait des
   champs plats (`weaponName_fr`, `weaponName_en`, `weaponName_es`...) sur
   `WeaponData`, `ArmorData`, `RuneData`, `ItemData`, etc. — un par un, pour
   chaque type d'équipement.

---

## 2. Principe retenu : `weaponId` (clé stable) + `LocalizedText` (affichage)

### 2.1 — Séparer clé technique et texte affiché

```csharp
public string       weaponId = "sword_iron_01"; // stable, jamais traduit, jamais affiché
public LocalizedText weaponName;                 // affichage joueur uniquement
```

- **`weaponId`** : identifiant technique, `snake_case`, unique par arme. Utilisé
  dans **tous** les `Debug.Log`, comparaisons internes, et clés de sauvegarde.
  Ne change jamais, quelle que soit la langue du joueur.
- **`weaponName`** : texte affiché à l'écran (tooltip, inventaire, fiche objet).
  Jamais utilisé comme clé de quoi que ce soit — uniquement lu au moment de
  l'affichage.

Règle simple à retenir pour la suite du projet : **si un texte peut apparaître
dans une sauvegarde, un log, ou une comparaison de code → ce n'est pas un
`LocalizedText`, c'est un `string` stable.** Si un texte n'est lu que par un
joueur humain → `LocalizedText`.

### 2.2 — Un seul type `LocalizedText`, réutilisable partout

```csharp
[System.Serializable]
public class LocalizedText
{
    [TextArea] public string fr;
    [TextArea] public string en;

    public string Get(Language lang) => lang switch
    {
        Language.EN => string.IsNullOrEmpty(en) ? fr : en, // fallback FR si traduction manquante
        _ => fr,
    };
}
```

Utilisable tel quel sur n'importe quel SO (`WeaponData.weaponName`,
`WeaponData.description`, et demain `ArmorData`, `RuneData`, `ItemData`,
`QuestData`...). Ajouter une langue = modifier **uniquement ce fichier**
(un champ + un cas dans `Get()`), jamais les scripts de données qui l'utilisent.

### 2.3 — État global de langue + notification de changement

```csharp
public enum Language { FR, EN }

public static class LocalizationManager
{
    public static Language CurrentLanguage { get; private set; } = Language.FR;
    public static event System.Action OnLanguageChanged;

    public static void SetLanguage(Language newLang)
    {
        if (newLang == CurrentLanguage) return;
        CurrentLanguage = newLang;
        PlayerPrefs.SetInt("aethertree_language", (int)newLang);
        OnLanguageChanged?.Invoke();
    }
}
```

- Le changement de langue dans les options **ne modifie aucun SO** — il change
  uniquement `CurrentLanguage` et prévient les abonnés.
- Chaque panel UI affichant du texte localisé s'abonne à `OnLanguageChanged` dans
  `OnEnable()` et se désabonne dans `OnDisable()`, et se relit (`Refresh()`) à
  chaque notification — voir exemple `WeaponTooltipUI` dans l'échange précédent.
- Persistance via `PlayerPrefs` : la langue choisie est retrouvée au lancement
  suivant (`LoadSavedLanguage()` à appeler une fois au bootstrap).

---

## 3. État final du code

**`WeaponData` — champs concernés** :
```csharp
public string        weaponId    = "new_weapon_id"; // clé stable
public LocalizedText  weaponName;                    // affichage
...
public LocalizedText  description;                   // affichage
```

**`WeaponInstance` — raccourcis mis à jour** :
```csharp
public string WeaponId   => data != null ? data.weaponId : "unknown_weapon";
public string WeaponName => data != null
    ? data.weaponName.Get(LocalizationManager.CurrentLanguage)
    : "Weapon";
```

**Tous les `Debug.Log` de `WeaponInstance`** utilisent désormais `WeaponId`
(clé stable) au lieu de `WeaponName` (texte localisé) — un log ne doit jamais
dépendre de la langue du joueur qui a déclenché l'action.

→ **Aucun changement de comportement côté UI** : le nom de l'arme s'affiche
toujours via `WeaponName`, simplement il se relit désormais dans la langue
courante à chaque frame de changement de langue plutôt que d'être figé.

---

## 4. Ce qui va casser à la compilation / points d'attention

- **`WeaponData.weaponName`** n'est plus un `string` — tout code qui faisait
  `weaponData.weaponName` directement (concaténation, comparaison, affichage
  brut) doit passer par `weaponData.weaponName.Get(LocalizationManager.CurrentLanguage)`.
- **`WeaponData.description`** : même chose.
- **Nouveau champ obligatoire à remplir** : `weaponId` sur chaque `WeaponData`
  existant (valeur par défaut `"new_weapon_id"` — à corriger sur chaque asset
  déjà créé, sinon collision de clé entre plusieurs armes).
- **`RuneInstance`** (référencé dans `TryInsertRune`/logs de `WeaponData.cs`) a
  le même problème s'il possède un `runeName` en `string` brut : prévoir le même
  traitement (`runeId` + `RuneData.runeName` en `LocalizedText`) avant de
  compiler tout le projet — sinon `rune.RuneName`/`rune.RuneId` référencés dans
  `WeaponInstance` ne compileront pas tant que `RuneData`/`RuneInstance` n'ont
  pas été mis à jour en miroir.
- **Sauvegardes existantes** : si un ancien format de save stockait un nom
  d'arme en `string` comme référence à l'objet (plutôt qu'un `weaponId` ou une
  référence au SO), prévoir une migration — le nom n'est plus une clé fiable
  une fois localisé.

Suggestion : `grep -rn "\.weaponName\b" .` (sans `.Get(`) dans tout le projet
pour lister les usages à corriger avant de compiler.

---

## 5. Généraliser aux autres types de données (Armor, Runes, Items, Quêtes...)

Le pattern à reproduire pour chaque SO ayant du texte affiché au joueur :

1. Un `xxxId` (`string`, `snake_case`) — clé stable, jamais traduite, utilisée
   dans tous les logs/comparaisons/sauvegardes.
2. Chaque champ de texte destiné au joueur (nom, description, lore...) devient
   un `LocalizedText` plutôt qu'un `string`.
3. Les raccourcis runtime (`XxxInstance.XxxName`) appellent
   `.Get(LocalizationManager.CurrentLanguage)` au lieu de lire le champ brut.
4. Les logs internes utilisent systématiquement `XxxId`, jamais `XxxName`.
5. L'UI qui affiche ces textes s'abonne à `LocalizationManager.OnLanguageChanged`
   pour se rafraîchir immédiatement au changement de langue dans les options.

**Prochain candidat naturel** : `RuneData`/`RuneInstance`, déjà référencé dans
`WeaponData.cs` (`rune.RuneName`, `rune.Label`) — à traiter en même temps ou
juste après pour éviter une incohérence entre armes déjà localisées et runes
qui ne le sont pas encore.

---

## 6. Extension à `ConditionData` / `UnlockManager` (système de déblocages)

### 6.1 — Ce qui était déjà correct

`ConditionData.conditionID` est déjà une clé stable (`string`), jamais traduite,
utilisée partout comme identifiant (compteurs, sauvegarde, logs) — exactement le
principe recherché. Rien à changer sur ce champ.

### 6.2 — Ce qui a été corrigé

**`ConditionData.displayName` / `ConditionData.description`** — `string` bruts
affichés au joueur (section "Affichage") → passés en `LocalizedText`.

**Bug concret trouvé dans `UnlockManager.Unlock()`** :
```csharp
// Avant — utilisait displayName (texte joueur) dans un log serveur
Debug.Log($"[UNLOCK] {condition.displayName} — scope:{condition.GetDominantScope()}");

// Après — conditionID (clé stable)
Debug.Log($"[UNLOCK] {condition.conditionID} — scope:{condition.GetDominantScope()}");
```
C'est exactement le piège que la clé stable est censée éviter : un log qui
changerait de contenu selon la langue du joueur ayant déclenché l'action,
devenant impossible à grep/comparer entre deux joueurs ou deux sessions.

**`ConditionReward.rewardTitle`** (`string` brut, reward de type `Title` /
`SkillAndTitle`) — séparé en deux champs, comme `weaponId`/`weaponName` sur
`WeaponData` :
```csharp
public string        titleID;          // clé stable — logs, save, futur TitleSystem
public LocalizedText  rewardTitleName;  // texte affiché au joueur (fr/en)
```
Bon moment pour ce choix : le `TitleSystem` n'est pas encore implémenté (voir
recap réseau, section MailboxSystem — juste un `TODO` actuellement), donc aucun
code existant à migrer côté lecture, seulement le champ sur le SO.

**`ConditionReward.rewardDescription`** et **`ConditionData.rewardDescription`**
(texte de mail affiché au joueur à la réclamation de la récompense) → passés en
`LocalizedText` tous les deux (ce sont deux champs distincts : un générique au
niveau de la condition, un par reward individuel).

### 6.3 — Ce qui va casser à la compilation

- `ConditionReward.IsValid()` — le `case RewardType.Title` doit désormais tester
  `!string.IsNullOrEmpty(titleID)` au lieu de `rewardTitle` (déjà corrigé dans le
  fichier fourni).
- **`MailboxSystem`** (non fourni dans cet échange, mais référencé par
  `UnlockManager.Unlock()` via `SendRewardMail(condition, reward)`) lit
  probablement `reward.rewardDescription` et/ou `reward.rewardTitle` comme des
  `string` bruts pour composer le texte du mail → à mettre à jour vers
  `.Get(LocalizationManager.CurrentLanguage)` au moment de l'affichage, pas au
  moment de l'envoi (le mail doit s'afficher dans la langue courante du joueur
  au moment où il le lit, pas au moment où il a été envoyé).
- Toute UI de fiche de condition/succès qui lisait `condition.displayName` ou
  `condition.description` directement doit passer par `.Get(...)`.
- `condition.rewardTitle` n'existe plus — remplacé par `titleID` +
  `rewardTitleName`. Chercher les références directes dans le projet.

Suggestion : `grep -rn "\.displayName\b\|\.rewardTitle\b\|\.rewardDescription\b" .`
(en excluant les appels `.Get(`) pour lister les points d'impact restants,
notamment dans `MailboxSystem` et l'UI de fiche de condition.

---

## 7. Extension à `MailboxSystem` (miroir de `ConditionReward`)

`MailReward` étant une donnée miroir de `ConditionReward` (stockée dans le mail
au moment de l'envoi — voir recap réseau), elle porte exactement les mêmes
champs et donc les mêmes corrections :

- **`MailReward.rewardTitle`** → séparé en `titleID` (clé stable) +
  `rewardTitleName` (`LocalizedText`), comme `ConditionReward`.
- **`MailReward.rewardDescription`** → `LocalizedText`.
- **`MailReward.GetDisplayName()`** — méthode d'affichage UI, corrigée pour lire
  les champs `LocalizedText` via `.Get(LocalizationManager.CurrentLanguage)`
  (cas `Title`, `Weapon`, et le fallback par défaut). Les autres types
  d'équipement (`Armor`, `Helmet`, `Gloves`...) lisent encore un `xxxName` en
  `string` brut — pas une erreur, ces SO n'ont pas encore été migrés vers
  `LocalizedText` (seul `WeaponData` l'a été jusqu'ici). Même pattern à
  reproduire dessus le jour où ils seront traités (section 5).
- **Logs de `DistributeReward()`** — `weapon.weaponName` → `weapon.weaponId`,
  `reward.rewardTitle` → `reward.titleID`, exactement le problème que tu as
  soulevé : un log ne doit jamais afficher un identifiant susceptible de
  changer selon la langue du joueur qui a déclenché l'action (ex: le nom d'une
  arme "Sword_iron_01" traduit doit rester traçable par son ID, pas par son nom
  affiché).
- **`ClaimReward()`** — le log utilisait `mail.subject` ; ce champ n'existe plus
  (voir point 7.1), remplacé par `mailID` (déjà une clé stable, générée par
  `SendRewardMail` sous la forme `reward_{conditionID}_{ticks}`).

### 7.1 — Problème plus profond trouvé : le mail était figé dans la langue de l'envoi

`SendRewardMail()` composait `subject`/`body` en `string` déjà traduits, stockés
tels quels sur `MailMessage`. Un mail envoyé pendant que le joueur est en FR
restait en FR même s'il changeait de langue avant d'ouvrir sa boîte mail — le
même problème que `WeaponData.weaponName` avant la refonte, mais sans le
mécanisme de relecture (`Get()`) pour le corriger.

**Correction** : `MailMessage.subjectText`/`bodyText` sont maintenant des
`LocalizedText`, et `Subject`/`Body` deviennent des **propriétés calculées** :
```csharp
public string Subject => subjectText?.Get(LocalizationManager.CurrentLanguage) ?? "";
public string Body    => bodyText?.Get(LocalizationManager.CurrentLanguage) ?? "";
```
Le sujet/corps sont donc recalculés à chaque lecture — un mail déjà en boîte de
réception s'affiche correctement même après un changement de langue en cours de
partie.

**Nouveau helper `LocalizedText.Join(separator, ...parts)`** ajouté pour
composer le corps du mail (description de condition + description de reward)
langue par langue, en reproduisant le comportement "si une partie est vide, ne
pas l'ajouter ni le séparateur" de l'ancien code.

**Nouveau constructeur `LocalizedText(fr, en)`** ajouté pour construire des
textes fixes bilingues directement en code (ex: le sujet générique d'un
déblocage secret, ou le préfixe "Récompense débloquée : " / "Reward unlocked: "),
sans passer par l'inspecteur.

### 7.2 — Ce qui va casser à la compilation

- `MailMessage.subject` / `MailMessage.body` n'existent plus — remplacés par
  `subjectText`/`bodyText` (à assigner) et `Subject`/`Body` (à lire, propriétés
  en lecture seule). Toute UI (`SocialUI`, panel de messagerie) qui lisait
  `mail.subject`/`mail.body` directement doit passer à `mail.Subject`/`mail.Body`.
- `MailReward.rewardTitle` n'existe plus — remplacé par `titleID` +
  `rewardTitleName`, comme côté `ConditionReward`.
- **Sauvegardes existantes** : si des mails étaient déjà persistés
  (`RestoreMail`) avec l'ancien format (`subject`/`body` en `string`), prévoir
  une migration ou accepter la perte de rétrocompatibilité — même point que
  soulevé pour les instances d'armes dans la note ratio de dégâts.

Suggestion : `grep -rn "\.subject\b\|\.body\b\|\.rewardTitle\b" .` (en excluant
`subjectText`/`bodyText`/`.Get(`) pour repérer les derniers points d'impact,
notamment dans `SocialUI`.

---

## 8. Extension à `SocialUI` (affichage du panel Mail)

### 8.1 — Problème : casse de compilation suite à la refonte de `MailMessage`

`SocialUI.cs` lisait directement `mail.subject` et `mail.body` — deux champs
`string` qui n'existent plus depuis la section 7.1 (remplacés par
`subjectText`/`bodyText` en `LocalizedText`, exposés via les propriétés
calculées `Subject`/`Body`). 4 occurrences trouvées :

- `OnNewMail()` — log de debug.
- `SpawnMailEntry()` — texte de la liste des mails.
- `ShowMailDetail()` — sujet et corps du mail détaillé.

### 8.2 — Solution appliquée

Chaque `mail.subject` → `mail.Subject`, chaque `mail.body` → `mail.Body` (les
propriétés calculées relisent `LocalizationManager.CurrentLanguage` à chaque
accès, donc l'affichage suit la langue courante sans autre changement).

**Cas particulier du log dans `OnNewMail()`** — même principe que partout
ailleurs dans ce document : un `Debug.Log` ne doit jamais afficher un texte
susceptible de changer selon la langue du joueur.
```csharp
// Avant
Debug.Log($"[SOCIALUI] Nouveau mail : {mail.subject}");
// Après
Debug.Log($"[SOCIALUI] Nouveau mail : {mail.mailID}");
```

### 8.3 — Problème distinct trouvé (non corrigé, à trancher) : labels UI codés en dur

En dehors du contenu par mail (`Subject`/`Body`, déjà couvert par
`LocalizedText`), `SocialUI.cs` contient plusieurs **labels d'interface
génériques** écrits uniquement en français, qui ne dépendent d'aucune donnée de
SO :

```csharp
rewardTitle.text = mail.rewardClaimed ? "Récompense récupérée" : "Pièces jointes";
txt.text          = mail.rewardClaimed ? ">> Recupere" : ">> Recuperer";
string sender     = mail.isFromServer  ? "[Système]" : $"[{mail.senderName}]";
detailFrom.text   = mail.isFromServer  ? "Serveur AetherTree" : mail.senderName;
```

**Pourquoi `LocalizedText` ne convient pas ici** : ces textes ne sont rattachés
à aucune instance de donnée (pas de SO, pas de `ConditionData` derrière) — ce
sont des libellés fixes de l'interface, répétés partout dans le jeu (boutons,
statuts, étiquettes). Le bon outil pour ce cas est le système à clés évoqué en
tout début de cette réflexion (`LocalizationManager.Get("ui_xxx")` avec une
table CSV/dictionnaire), pas un champ par instance.

**Non corrigé pour l'instant** — nécessite de définir la table de clés UI
(portée plus large que ce fichier, concerne toute l'interface du jeu). Décision
à prendre : traiter en même temps que le reste, ou en tâche séparée une fois
que la table de clés UI existe.

### 8.4 — Ce qui va casser à la compilation

Aucun autre changement de signature publique introduit par cette section —
seuls les 4 usages de `mail.subject`/`mail.body` listés en 8.1 nécessitaient
une correction, déjà appliquée. Si d'autres scripts UI (HUD, notifications)
lisent aussi `mail.subject`/`mail.body` directement, même correction à
appliquer : `grep -rn "\.subject\b\|\.body\b" .` en excluant `subjectText`/
`bodyText`/`.Get(`.
