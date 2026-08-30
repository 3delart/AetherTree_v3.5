# Refonte — Branchement UI langue & options (Phase 3, réduite)

> **Statut** : document de cadrage. **Révisé** — le cœur de cette phase (`PlayerSettings`, branchement de `LocalizationManager` dessus) a été avancé en Phase 1 (voir `note-refonte-itemdata-classe-de-base.md` §2.2-2.3), pour éviter de retoucher `LocalizationManager` deux fois. Ce document ne couvre plus que ce qui dépend d'un menu Options qui n'existe pas encore dans le projet.

**Contexte** : `PlayerSettings.Current.language` et `LocalizationManager` (API `CurrentLanguage`/`OnLanguageChanged`/`SetLanguage`/`LoadSavedLanguage`) sont opérationnels dès la Phase 1. Ce qui reste : les rendre visibles et actionnables pour le joueur, et faire en sorte que l'UI déjà affichée se rafraîchisse en direct quand la langue change.

## Ce qui rentre dans cette phase (réduite)

1. **Abonnements `OnLanguageChanged`** dans les panels UI affichant du texte localisé (`TooltipSystem`, `ShopUI`, `SocialUI`, et tout autre panel d'inventaire/équipement découvert au moment de l'implémentation) — pattern `OnEnable()`/`OnDisable()` déjà documenté dans `note-systeme-multilangue.md` §2.3. **Attention à la fuite mémoire par abonnement oublié** (audit `note-refonte-itemdata-classe-de-base.md` §2.1 point 6) — vérifier systématiquement la paire `OnEnable`/`OnDisable` à chaque panel touché.
2. **Sélecteur de langue** dans le menu Options — UI à identifier/créer au moment de l'implémentation (pas encore localisée dans le projet à ce jour). Appelle `LocalizationManager.SetLanguage(...)`.
3. **Migration `ConditionData`/`ConditionReward`/`MailboxSystem`/`SocialUI` vers `LocalizedText`** — inchangé par rapport à la version précédente de ce document :
   - `ConditionReward.rewardTitle` → `titleID` (stable) + `rewardTitleName` (`LocalizedText`).
   - `MailReward` (miroir de `ConditionReward`) → même split.
   - `MailMessage.subject`/`body` → `subjectText`/`bodyText` (`LocalizedText`) + propriétés calculées `Subject`/`Body`.
   - `SocialUI.cs` — 4 sites `mail.subject`/`mail.body` → `mail.Subject`/`mail.Body`.
   - Détail complet déjà écrit dans `note-systeme-multilangue.md` §6-8 (contient les numéros de ligne à revérifier au moment de l'implémentation, le code ayant pu changer depuis). Ces structures sont indépendantes de la hiérarchie `ItemData` donc pas déjà couvertes par la Phase 1, mais dépendent de la même infra `LocalizedText`/`LocalizationManager` déjà en place.
   - Utiliser `LocalizedText.IsEmpty` (ajouté lors de l'audit) pour remplacer les `string.IsNullOrEmpty(condition.description)` actuels dans `MailboxSystem.SendRewardMail()`.

## Ce qui reste explicitement hors scope

- Les options d'affichage/son elles-mêmes (pas encore discutées/spécifiées — `PlayerSettings` est prêt à les recevoir, voir Phase 1, mais aucun champ n'est ajouté en avance).
- Les labels UI codés en dur en français dans `SocialUI.cs` (ex: `"Récompense récupérée"`, `">> Recuperer"`, `"[Système]"`) — signalés dans `note-systeme-multilangue.md` §8.3, nécessitent une table de clés UI séparée (`LocalizationManager.Get("ui_xxx")`), système qui n'existe pas encore et n'est pas cadré ici.
