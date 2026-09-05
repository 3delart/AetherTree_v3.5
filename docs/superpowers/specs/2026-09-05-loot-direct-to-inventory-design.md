# Loot direct-en-inventaire — Design

## Contexte

Aujourd'hui, le loot d'un mob (`LootTable.RollAll()`) spawne physiquement au sol
(`LootManager` → `WorldPickupItem`), le joueur marche dessus et clique pour ramasser
(`TargetingSystem`'s pickup branch). Florian propose de sauter cette étape : le loot part
directement dans l'inventaire du/des joueur(s) éligible(s) au moment du kill.

**Bénéfice réel identifié pendant la discussion** (corrigé deux fois en cours de route) :
- Armure/Casque/Gants/Bottes ne sont **déjà pas** rendues visuellement sur le personnage
  aujourd'hui (l'apparence vient du système Cosmétique tête/corps) — leur `xxxPrefab` ne sert
  QUE au drop au sol. Les supprimer élimine cette FBX pour de bon.
- Ressources/Consommables : leur `prefab` sert aussi UNIQUEMENT au drop au sol (jamais porté).
  Même bénéfice.
- L'Arme reste visible sur le personnage, **avec un FBX unique PAR ASSET `WeaponData`**
  (décision finale Florian, voir §Arme visible) — le choix d'arme à la création du personnage
  est LE choix le plus important, donc l'identité visuelle par arme précise est voulue, pas
  un coût accidentel à éliminer. Le gain de ce lot reste réel : ce FBX ne sert plus QUE au
  visuel porté, plus jamais au drop au sol (qui disparaît entièrement, arme incluse).
- Bijoux/Esprit/Gemme/Rune n'avaient déjà aucune FBX dédiée (fallback sur un prefab générique)
  — pas de gain FBX supplémentaire pour ces types, mais gagnent quand même la suppression du
  clic-ramassage.

## Périmètre

Concerne UNIQUEMENT le loot de KILL de mob (`LootManager`/`WorldPickupItem`/`LootTable`). Les
nœuds de récolte (`ResourceNode` — arbres, minerai...) sont un système entièrement séparé
(son propre clic/coroutine dans `TargetingSystem`, jamais de `WorldPickupItem`) — **non touché**
par ce changement.

Vérifié : `WorldPickupItem` n'est créé QUE par `LootManager` (`AddComponent<WorldPickupItem>`
aux lignes `SpawnItem`/`SpawnAeris`) — aucun autre système (coffres, quêtes au sol, prefabs
placés à la main) ne le référence. Sa suppression et celle de la branche pickup de
`TargetingSystem` (`HandlePickupClick`/`ApproachPickupRoutine`/`selectedPickup`/
`GetSelectedPickup`) sont donc sûres, sans impact sur la récolte de ressources.

## Flux général

Remplace `LootManager.OnMobKilled(MobKilledEvent e)` :

1. `Mob.Die()` calcule déjà `eligiblePlayers` (≥10% des dégâts totaux, via
   `damageContributions`) — réutilisé tel quel, aucun changement de ce côté.
2. `LootTable.RollAll()` roll **une seule fois** (comportement de roll inchangé — seul ce qui
   arrive APRÈS le roll change).
3. Pour **chaque item** du roll : tirage aléatoire **uniforme** d'un gagnant parmi
   `eligiblePlayers` (pas pondéré par % de dégâts — `Mob.GetDamageContribution()` existe déjà
   si un tirage pondéré est voulu plus tard, sans rien à changer côté collecte des dégâts).
   Livré directement dans l'inventaire du gagnant.
4. Pour l'Aeris : même tirage indépendant, un seul gagnant reçoit le montant total (pas de
   partage/prorata — décision Florian, plus simple, cohérent avec le traitement des items).
5. Feedback : `Debug.Log` uniquement pour l'instant (`[LOOT] {joueur} a reçu {item} ({mob})`).
   Une section "drop" dans le chat viendra plus tard, hors périmètre de ce lot.

## Distribution multi-joueurs — prête maintenant, pas observable avant le réseau

`InventorySystem` est aujourd'hui un singleton global unique (même famille que
`MailboxSystem`/`AerisSystem`/`QuestSystem`) — un seul sac dans tout le jeu, celui du joueur
local. Aucun réseau/multi construit à ce jour (roadmap : bucket "Needs multiplayer").

Décision Florian : construire quand même la vraie logique de tirage (`PickRandomEligible`)
MAINTENANT plutôt que de coder juste "joueur local" — même principe que `SkillSystem.IsAlly`
plus tôt cette session : un point centralisé unique, correct dès aujourd'hui en solo (liste à
1 joueur = toujours lui), prêt à servir tel quel le jour où le réseau existera. Seule la
LIVRAISON (`InventorySystem.Instance`) reste le singleton global pour l'instant — commentaire
explicite dans le code marquant ce point comme LE seul endroit à mettre à jour quand un vrai
sac par-joueur-distant existera.

## Gestion inventaire plein — mail de secours

Si `InventorySystem.Instance.AddItem(item)` échoue (plein), l'item part en mail au lieu d'être
perdu. Réutilise le système de mail EXISTANT (`RewardType`/`MailReward`/`MailboxSystem.
DistributeReward`) **sans le modifier** :

- Le mail ne stocke PAS l'instance déjà rollée (rareté/ratios du kill) — juste une référence
  vers le SO d'origine, exactement comme un mail de récompense `ConditionData` aujourd'hui.
- Les stats sont rerollées à la RÉCUPÉRATION (`CreateDropInstance()`/`CreateInstance()`), pas
  préservées depuis le moment du kill. Le joueur ne voit pas de tooltip/stats dans le mail
  avant de cliquer "Récupérer" — déjà le comportement actuel des mails de récompense, aucune
  régression.
- Pourquoi pas préserver le roll exact : `MailReward` stocke des références SO directes, pas
  l'état rollé complet (rareté/ratios) — il faudrait dupliquer tous les champs que
  `SavedWeapon`/`SavedArmor` etc. gèrent déjà côté save, plus gérer leur propre persistance
  dans les mails sauvegardés. Trop de plomberie pour un cas limite (pas le chemin principal).
- Nouvelle petite méthode : `MailboxSystem.SendLootOverflowMail(InventoryItem item, string
  mobName)` — construit un `MailReward` à partir du type concret de `item` (Weapon/Armor/
  Helmet/Gloves/Boots/Jewelry/Spirit/Consumable/Resource/Gem/Rune — les 11 types que
  `LootTable.CreateInventoryItem()` sait produire aujourd'hui) et l'envoie via le pipeline mail
  existant.
- **Ajout nécessaire** : `RewardType`/`MailReward`/`DistributeReward` n'ont actuellement AUCUN
  cas pour Gem/Rune (contrairement à Weapon/Armor/etc., déjà couverts) — `RewardType.Gem` et
  `RewardType.Rune` doivent être ajoutés en fin d'enum (ordinal safety, même pattern que
  `RewardType.Talisman`/`RewardType.Quest` ajoutés plus tôt cette session), avec les champs
  `MailReward`/`ConditionReward` et cases `DistributeReward` correspondants — sinon un Gem/Rune
  dans un inventaire plein n'aurait nulle part où aller.
- Aeris n'a JAMAIS ce problème — `AerisSystem.Add()` n'a aucun plafond, toujours livré direct,
  pas de fallback nécessaire.

## Arme visible — FBX unique par asset, conservé

Décision finale Florian (révisée en cours de discussion) : le choix de l'arme à la création du
personnage est LE choix le plus important — avoir la même silhouette que son voisin en
choisissant une arme différente serait décevant. Donc **`WeaponData.weaponPrefab` reste un
champ par-asset**, un FBX unique par arme, PAS un mesh générique partagé par `WeaponType`
(la piste envisagée plus tôt dans la discussion est abandonnée).

- Aucun changement de code côté `World/WeaponVisual.cs` — continue de lire
  `data.weaponPrefab` directement, comme aujourd'hui.
- Ce qui change réellement : ce FBX ne sert plus JAMAIS au drop au sol (`LootManager.
  GetItemPrefab` disparaît avec le reste du nettoyage, voir plus bas) — son seul rôle devient
  le visuel porté. Le travail de création de ce FBX reste nécessaire (voulu, pas accidentel),
  contrairement à Armure/Casque/Gants/Bottes/Ressources/Consommables qui n'en ont plus besoin
  du tout.
- Ouvre la porte à du VFX distinct par arme (traînée/lueur à l'attaque propre à chaque
  `WeaponData`) — mentionné par Florian comme bénéfice de garder un asset unique par arme,
  pas dans le périmètre de ce lot mais compatible avec ce choix, à construire séparément.

## Nettoyage — code retiré ou rendu inutile

Vérifié via exploration (`WorldPickupItem` créé UNIQUEMENT par `LootManager`, zéro autre
référence dans le code ou les scènes/prefabs) :

- `Systems/LootManager.cs` — toute la logique de spawn physique (`SpawnLootDelayed`,
  `SpawnItem`, `SpawnAeris`, `GetItemPrefab`, `RandomSpawnPos`, `SpawnGO`, champs
  `pickupPrefab`/`aerisPrefab`) remplacée par le nouveau flux direct-en-inventaire.
- `World/WorldPickupItem.cs` — fichier entier supprimable (plus aucun créateur après le
  nettoyage de `LootManager`).
- `Combat/TargetingSystem.cs` — branche pickup entière supprimable : `HandlePickupClick`,
  `ApproachPickupRoutine`, champ `selectedPickup`, méthode `GetSelectedPickup()`. La branche
  `ResourceNode` (récolte) reste intacte, elle ne partage aucune logique avec la branche
  pickup au-delà du wrapper générique `StartApproach(...)`.
- `Data/Equipment/ArmorData.cs`/`HelmetData.cs`/`GlovesData.cs`/`BootsData.cs` (leurs
  `xxxPrefab` respectifs) — champs qui deviennent morts une fois le drop au sol supprimé,
  puisqu'ils ne servaient QUE à ça (jamais de visuel porté pour ces types). Idem
  `ResourceData.prefab`/`ConsumableData.prefab`. **`WeaponData.weaponPrefab` NE devient PAS
  mort** — conservé, sert désormais uniquement au visuel porté (voir §Arme visible).
- `LootManager.GetItemPrefab(item)` — simplifié/retiré : n'a plus besoin de résoudre un prefab
  de DROP pour l'arme (elle ne droppe plus visuellement) ; les branches Armor/Helmet/Gloves/
  Boots/Consumable/Resource disparaissent avec le reste de la logique de spawn physique.

## Vérification

Pas de framework de test automatisé — vérification manuelle en Play Mode par Florian :
1. Tuer un mob solo (1 seul joueur éligible) → items + Aeris arrivent directement dans
   l'inventaire, log confirme, aucun objet au sol.
2. Remplir l'inventaire au maximum, tuer un mob → vérifier qu'un mail arrive avec le loot au
   lieu d'une perte silencieuse ou d'un crash ; réclamer le mail, vérifier que l'item apparaît
   avec des stats fraîchement rollées.
3. Équiper différentes armes (même `WeaponType` ou non) → chaque asset `WeaponData` garde son
   FBX propre, aucune régression du visuel porté actuel (comportement déjà en place,
   non touché par ce lot).
4. Vérifier qu'un nœud de récolte (arbre/minerai) fonctionne toujours exactement comme avant
   (clic, approche, collecte) — aucune régression sur `ResourceNode`.
5. (Non testable avant le réseau) — confirmer par lecture de code que `PickRandomEligible`
   est bien appelé même en solo (liste à 1 élément), pour garantir qu'aucune régression
   silencieuse n'apparaîtra le jour où plusieurs joueurs seront réellement éligibles.
