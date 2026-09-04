# AetherTree — Récap des points à régler avant le passage en réseau

Ce document liste tout ce qui a été identifié comme point d'attention pour transformer le projet Unity actuel (solo) en MMO multijoueur avec Mirror. Rien n'a encore été modifié dans le code — c'est une feuille de route.

---

## 🧭 Stratégie générale — continuer le solo ou tout refaire en multi d'abord ?

Ni l'un ni l'autre à 100%. Les deux extrêmes ont un coût caché :

- **Continuer tout en solo, corriger après** → risqué : le pattern répété dans presque tous les fichiers vus (`GameEventBus`, `SceneLoader`, `Portal`, `UnlockManager`, `MailboxSystem`...) — singleton global + `FindObjectOfType<Player>()` + état "un seul joueur, un seul monde" — n'est pas une feature isolée à corriger vite. C'est un mur porteur : chaque nouvelle feature solo a tendance à copier ce même pattern, épaississant la dette à chaque ajout.
- **Tout refaire en multi d'abord, puis les features** → risqué aussi : des mois de plomberie réseau sans aucune feature "jouable" pour valider le fun, avec le piège classique des projets MMO solo/indé qui s'essoufflent avant d'avoir quoi que ce soit à montrer.

### Approche recommandée (hybride)

1. **Corriger les patterns "un seul joueur" maintenant** — remplacer `FindObjectOfType<Player>()` par des références explicites, sortir les états "par joueur" des singletons globaux uniques. Reste 100% fonctionnel en solo aujourd'hui, mais élimine le mur porteur avant qu'il ne s'épaississe.
2. **Valider le prototype réseau minimal tout de suite** (pas dans 6 mois) — Mirror sur juste le mouvement + un sort basique, testé en local puis sur un VPS avec un ami. C'est le point le plus incertain du projet (latence, synchro, autorité serveur) : mieux vaut le découvrir tôt sur peu de contenu que tard sur 200 sorts.
3. **Continuer les features EN suivant ce pattern**, une fois le Cmd/Rpc validé — chaque nouvelle feature (quêtes, craft, social...) s'écrit directement selon le modèle serveur-autoritaire déjà établi, au lieu d'être écrite en solo puis reconvertie.
4. **Le contenu (sorts, mobs, zones, quêtes) peut ensuite avancer librement** — une fois le squelette réseau posé et le pattern répété avec succès, ajouter du contenu suit un template connu.

### Contexte favorable actuel

Le projet est aujourd'hui **uniquement sur des assets de test** — aucune map, aucune arme visuelle, aucun sort visuel, aucune animation de personnage. C'est la **fenêtre idéale** pour attaquer structure + prototype réseau :
- Tout le travail de structure (GameEventBus, SceneLoader, FindObjectOfType, prototype Mirror) est **entièrement indépendant des visuels** — rien de ce travail ne sera à refaire une fois les assets ajoutés.
- Le prototype réseau peut se valider avec des capsules grises et des `Debug.Log` de dégâts — pas besoin d'attendre le moindre asset.
- Évite le piège classique : ajouter animations/VFX **avant** que le réseau fonctionne mène souvent à devoir les retravailler (ex: une animation doit être déclenchée par une Rpc depuis le serveur, pas jouée directement en local comme dans un flux solo).
- **Point à garder en tête pour plus tard** (pas à faire maintenant) : quand les animations arriveront, utiliser `NetworkAnimator` (composant Mirror) plutôt qu'un `Animator` piloté localement, pour rester cohérent avec le modèle serveur-autoritaire déjà en place.

---

## 🔴 Priorité 0 — Fondations à trancher AVANT tout le reste

Ces deux systèmes sont actuellement pensés comme des **singletons globaux "un seul joueur, un seul monde"**. Si on ajoute le réseau par-dessus sans les revoir, tout le reste va se construire sur des fondations bancales.

### GameEventBus.cs
- C'est un singleton **statique local à chaque process** (client ou serveur). Aujourd'hui, si le serveur publie un event, seuls les abonnés **sur cette même machine** le reçoivent — pas les clients à distance.
- Il faut séparer deux catégories d'events :
  - **Internes serveur** (ex: `StatsChangedEvent`) → restent tels quels, consommés uniquement côté serveur (XP, loot, progression).
  - **Events à notifier aux clients** (ex: `DamageDealtEvent`, `PlayerLevelUpEvent`, `MobKilledEvent`) → doivent être doublés d'une **Rpc Mirror** envoyée après le `Publish()` local.
- Les structs d'événements (`MobKilledEvent`, `DamageDealtEvent`...) contiennent des références directes à `Player`, `Entity`, `MobData` — **non sérialisables tel quel** par Mirror. Il faudra des versions "réseau" allégées (IDs/valeurs primitives) pour les Rpc.
- `Reset()` avec ses `Resubscribe()` (UnlockManager, XPSystem, LootManager, QuestSystem, AerisSystem, CharacterPanelUI) → à trancher un par un : logique côté serveur uniquement, ou aussi côté client (UI) ?

### SceneLoader.cs
- Un seul `_currentMap` global pour tout le process. En multijoueur, plusieurs joueurs seront sur des maps différentes en même temps → ce modèle ne fonctionne plus côté serveur tel quel.
- `FindObjectOfType<Player>()` (dans `RepositionPlayer()` et pour la sauvegarde) → ne retrouve qu'**un seul** joueur au hasard, pas "le bon" joueur. À revoir complètement en multi.
- Architecture cible probable : **côté client**, garder une logique proche de l'actuelle (charge/affiche la zone où se trouve son propre joueur local). **Côté serveur**, remplacer le chargement de scène additif par une gestion de plusieurs zones logiques en parallèle pour tous les joueurs connectés.
- `SaveSystem.Instance?.Save(player)` déclenché au changement de map → doit rester côté serveur uniquement (écriture en base de données).
- `GameEventBus.Reset()` appelé à chaque changement de map → à adapter selon l'évolution de GameEventBus, et à faire par joueur, pas globalement.

### Portal.cs (dépend des deux ci-dessus)
- `FindObjectOfType<Player>()` dans `RepositionPlayer()` → même problème, à remplacer par une référence explicite au joueur qui a déclenché le trigger.
- `PortalTransferData` (classe statique) → **une seule valeur globale** (`TargetPortalID`) pour tout le process. Si deux joueurs traversent un portail en même temps, ils s'écrasent mutuellement. Doit devenir une donnée **par connexion/joueur**.
- La téléportation elle-même (`agent.Warp()`) doit être validée et déclenchée côté serveur, puis synchronisée aux clients comme un déplacement classique.

**→ Recommandation : régler ces trois fichiers (au moins l'architecture, pas forcément le code final) avant d'aller plus loin dans le réseau. C'est plus structurant que le split SpellData.**

---

## 🟠 Priorité 1 — Refactor de données (rapide à faire maintenant, coûteux plus tard)

### SpellData / EquipmentData
- Séparer données de gameplay (stats : damage, range, cooldown, manaCost) et données visuelles (prefabs, sons, icônes) — soit en 2 ScriptableObjects, soit via **Addressables** (un seul SO, mais chargement conditionnel des assets lourds).
- Le serveur ne doit référencer/charger que les données de gameplay — jamais les assets visuels (garde le build serveur léger).
- Recommandation actuelle : commence par 1 sort refactoré proprement plutôt que d'attendre d'en avoir 200.

---

## 🟡 Priorité 2 — Architecture réseau du mouvement et des actions (le prototype Mirror)

### PlayerController.cs
- Hériter de `NetworkBehaviour` au lieu de `MonoBehaviour`.
- Restreindre toute lecture d'input à `isLocalPlayer` dans `Update()`.
- Remplacer l'appel direct `_agent.SetDestination(hit.point)` par une `[Command]` (`CmdRequestMove`) — c'est le **serveur** qui pilote le NavMeshAgent.
- Ajouter un `NetworkTransform` sur le Player Prefab (server authority par défaut) pour synchroniser la position aux autres clients automatiquement.
- Appeler `SetTarget()` sur la caméra via `OnStartLocalPlayer()`, pas dans un flux générique.
- `statusEffects` (stun, root, fear, slow, haste) → actuellement lus localement dans `HandleMovement()`. À terme, doivent être des `[SyncVar]` mises à jour uniquement par le serveur.
- La logique de fuite (`Fear`) doit migrer côté serveur.

### CameraController.cs
- Aucun changement structurel — reste 100% côté client (visuel pur). Seul point : s'assurer que `SetTarget()` n'est appelé que pour le joueur local (voir `OnStartLocalPlayer()` ci-dessus).

---

## 🟢 Priorité 3 — Rendre Entity et ses sous-classes serveur-autoritaires

### Entity.cs
- `currentHP`, `currentMana`, `isDead`, et les stats (`maxHP`, `meleeDefense`, etc.) → à passer en `[SyncVar]` pour que les clients voient les barres de vie/mana des autres joueurs.
- `TakeDamage()`, `Heal()`, `Die()`, `SpendMana()`, `RecoverMana()` → doivent être appelées **uniquement côté serveur**, jamais directement par un client.
- `Update()` (régénération passive) → doit tourner uniquement côté serveur (`isServer` check).
- `elementalResistances` / `elementalPoints` (Dictionary) → pas nativement synchronisables en `[SyncVar]`, utiliser `SyncDictionary` de Mirror ou ne synchroniser que les valeurs affichées.

### Player.cs
- `level`, `xpCombat`, réputation, équipement, etc. → doivent être persistés en base de données, traités comme état serveur-autoritaire.
- `OnLevelUp()`, `AddCombatXP()`, `stats.RecalculateStats()` → exécution serveur uniquement.
- `Revive()` / `ReviveAtRespawnPoint()` → doivent passer par une validation serveur (Cmd), pas déclenchables librement côté client.
- Les événements sociaux/guilde/PvP impliquant d'autres joueurs → nécessiteront une communication serveur → plusieurs clients (pas juste le joueur local).

### Mob.cs
- Toute l'IA (`enemyList`, machine à états Patrol/Chase/Attack/Return, `Update()`) → simulation **exclusivement côté serveur**.
- `damageContributions` (loot ≥10%), `Die()`, publication de `MobKilledEvent` → logique serveur uniquement (anti-triche loot).
- Besoin d'un `NetworkTransform` comme le joueur, piloté par le serveur.
- Le mob ne reçoit jamais d'input client direct — le point d'entrée réseau pour lui infliger des dégâts passe par CombatSystem/SkillSystem (voir plus bas).

### PNJ.cs
- Simulation serveur uniquement, comme le Mob.
- Interactions boutique/forge/quête → nécessitent leurs propres Cmd (`CmdOpenShop`, `CmdBuyItem`...) validées serveur — jamais d'achat/objet géré localement côté client.
- "Mémoire joueur" via `SaveSystem` → à articuler avec la base de données comptes/personnages prévue.

### StatusEffectSystem.cs
- Tous les flags (`isStunned`, `isFeared`, `isRooted`, `isBlinded`...) et les dictionnaires de buffs/debuffs actifs → calcul et modification **côté serveur uniquement**.
- Flags synchronisés (`[SyncVar]`) a minima pour affichage des icônes côté client et blocage local de l'input en prédiction — mais le serveur reste toujours juge final.
- Point anti-triche sensible : sans synchro serveur stricte, un client patché pourrait ignorer un stun/root/fear localement.

### StatPointSystem.cs
- Pas encore détaillé — probable : `InvestPoint()`, `Respec()`, `GainPoints()` doivent devenir des Cmd validées serveur (anti-triche points de stats).

---

## 🔵 Priorité 4 — Combat et compétences (CombatSystem / SkillSystem)

### CombatSystem.cs
- Bonne nouvelle : `CalculateDamage()` / `CalculateMobDamage()` sont déjà des **fonctions pures** (paramètres → résultat, sans effet de bord) — forme idéale pour tourner côté serveur sans réécriture profonde.
- `Random.Range()` utilisé dans le calcul → doit s'exécuter **uniquement côté serveur** pour éviter des résultats incohérents entre machines ou un client qui truque son propre random.
- `CombatSystem.Instance` (singleton) → ses méthodes de calcul ne doivent faire autorité que côté serveur. Le client peut éventuellement garder une instance pour de l'affichage prévisionnel (tooltips), jamais pour les dégâts réels.

### SkillSystem.cs
- Point d'entrée principal à sécuriser : `Execute(skill, caster, target)` doit devenir — client envoie une **Cmd** ("je veux lancer ce skill") → serveur valide (cooldown, mana, portée, ligne de vue) → serveur appelle `Execute()`.
- `SkillSystem.Instance` (singleton "souple" porté par le Player) → ne fonctionne plus tel quel en réseau (plusieurs joueurs = plusieurs instances). À remplacer par une référence directe au component de l'entité concernée.
- `_groundTargetPoint` / `_skillDirection` (state stocké avant Execute) → à transformer en **paramètres explicites** de la Cmd envoyée au serveur, pour éviter les problèmes de timing/désync.
- `Instantiate(vfxPrefab)`, `AudioSource.PlayClipAtPoint()` → **jamais côté serveur** (headless). Doit devenir une **Rpc** envoyée aux clients après validation serveur.
- Effets spéciaux (`DrainHP`, `TeleportSelf`, `Pull`/`Push`/`Vortex`...) → modifient position/HP/mana réels, donc soumis aux mêmes règles d'autorité serveur que le reste.

---

## 🟣 Priorité 3bis — Système de déblocage par conditions (UnlockManager / ConditionData / MailboxSystem)

> **Mise à jour** : points A-D de l'"Architecture cible" ci-dessous **implémentés**. `ConditionCheckerBase.RelevantEventType` posé sur les 11 checkers, `UnlockManager` indexe par type d'event dans `Init()` (`_conditionsByEventType`), `validEntries` mis en cache (`_validEntriesCache`, remplace le recalcul de `FinalizeCondition`), `WinnerKey`/`localWinnerCounts` en clé `ValueTuple` (sans allocation). `AccountKey` reste en string — persisté via `StringIntPair`/`CharacterProgress.cs`, changer son type casserait le format de save. Bonus non prévu dans le doc d'origine : l'octroi de récompense (`Unlock()` → mail/save) est maintenant différé via une file (`_pendingUnlocks`, vidée dans `Update()`) plutôt qu'exécuté en synchrone dans le event-handler de combat — la détection/le comptage restent en temps réel (rien n'est perdu), seul l'octroi (I/O-like) sort du chemin chaud. Retrait d'une condition de l'index réservé au cas `maxWinners` épuisé pour toutes ses entries (jamais sur une simple complétion joueur — voir point E ci-dessous, toujours vrai).
>
> **Reste à faire** : point E (état runtime séparé par joueur) — inchangé, dépend du reste du refactor réseau comme documenté plus bas.

Identifié comme **un des blocs les plus lourds du jeu**, à la fois en performance runtime et en architecture multijoueur. Analyse complète du pipeline : `ActivityCounter` → `UnlockManager` → `ConditionData`/`ConditionEntry`/`ConditionCheckerBase` → `MailboxSystem`.

### Diagnostic — la boucle actuelle

Pour **chaque event publié** sur `GameEventBus` (potentiellement plusieurs fois par seconde en combat, ex: `DamageDealtEvent`) :

```
EvaluateAll(event)
 └─ pour CHACUNE des N conditions du jeu (même sans rapport avec l'event)
     └─ EvaluateCondition()
         └─ pour CHACUNE des entries de la condition
             ├─ entry.checker.Evaluate(event, player)   ← appelé même si aucun rapport avec l'event
             ├─ si hit : IncrementCounter() → alloue une string ($"{id}__{index}")
             └─ GetCounter() → lookup dictionnaire supplémentaire
     └─ FinalizeCondition()
         └─ reboucle sur TOUTES les entries pour recompter validEntries
            (valeur statique, ne change jamais après l'Init — recalculée pour rien à chaque event)
```

Coût réel : O(conditions × entries) × 2, pour un event donné, alors que la quasi-totalité des conditions n'ont aucun rapport avec le type d'event reçu.

### Les 4 gaspillages identifiés

1. **Pas de filtrage par type d'événement** — chaque checker doit faire son propre `if (gameEvent is X)` en interne, donc il est appelé et exécuté même quand il n'est pas concerné. Le gaspillage le plus lourd.
2. **`FinalizeCondition()` recalcule `validEntries` à chaque event** alors que cette valeur est figée dès la config du SO — devrait être calculée une fois à l'Init et mise en cache.
3. **Allocations string dans le hot path** (`AccountKey`, `WinnerKey` via `$"{id}__{index}"`) à chaque fois qu'un checker retourne `true` — pression GC inutile, surtout multipliée par de nombreux joueurs simultanés.
4. **État global partagé** (`privateCounters`, `completedEntries`, `accountCounters`, `records`, `localWinnerCounts`) — même problème que `GameEventBus`/`SceneLoader` : singleton unique (`Instance`, `DontDestroyOnLoad`) + `player = FindObjectOfType<Player>()`, pensé pour un seul joueur par process. Ne fonctionne pas tel quel en MMO.

### Architecture cible

**A. `ConditionCheckerBase` — exposer les types d'event concernés**

```csharp
[System.Serializable]
public abstract class ConditionCheckerBase
{
    /// Type(s) d'event que ce checker doit recevoir pour être évalué.
    public abstract System.Type[] RelevantEventTypes { get; }

    public abstract bool Evaluate(object gameEvent, Player player);
}
```
Changement minimal par checker existant — une propriété à ajouter, la logique d'`Evaluate()` ne change pas.

**B. `UnlockConditionIndex` — configuration statique, calculée une seule fois, partagée par tous les joueurs**
- `Dictionary<System.Type, List<(ConditionData, int entryIndex)>>` construit à l'Init à partir de `RelevantEventTypes`.
- `Dictionary<string conditionID, int>` pour `validEntryCounts`, calculé une fois (remplace le recalcul dans `FinalizeCondition`).

**C. `EvaluateAll` devient ciblé** — ne consulte que `_index.byEventType[gameEvent.GetType()]` au lieu de boucler sur toutes les conditions. Passage de "toutes les conditions à chaque event" à "seulement les entries réellement concernées".

**D. Clés composites au lieu de concaténation de string** — `Dictionary<(string, int), int>` plutôt que `Dictionary<string, int>` avec clé `$"{id}__{index}"`, pour éviter l'allocation à chaque hit.

**E. État runtime séparé par joueur** — `UnlockManager` (côté serveur) garde l'index statique (partagé) + soit un component par joueur soit une `Dictionary<playerID, PlayerUnlockState>` (à trancher selon ce qui s'intègre le mieux avec Mirror). Chaque event doit porter l'info "quel joueur concerné" pour router vers le bon state.

**Gain attendu** : d'un ou deux ordres de grandeur sur le coût de ce système, en particulier avec beaucoup de conditions et des events fréquents comme `DamageDealtEvent`.

### Modèle client/serveur pour ce système précis

Principe général : *le client ne dit jamais "j'ai réussi, donne-moi la récompense" sans preuve — le serveur reconfirme depuis son propre état.* Mais ce système bénéficie d'un cas particulier favorable :

- **Détection de condition** (kills, dégâts, skills — tout ce qui vient d'events déjà serveur-autoritaires via CombatSystem/SkillSystem/Mob) → **aucune requête client nécessaire**. Le serveur voit ces events se produire en temps réel puisque c'est lui qui simule le combat ; `UnlockManager` s'abonne et évalue de façon transparente, sans aller-retour réseau.
- **Events pas encore serveur-autoritaires** (`NpcInteractEvent`, `ZoneEvent`, aujourd'hui probablement détectés via trigger Unity côté client) → à faire remonter en `[Command]`, avec **revalidation serveur** depuis la donnée réelle (ex: position réelle du joueur côté serveur), jamais confiance aveugle sur ce que le client prétend :
  ```csharp
  [Command]
  private void CmdEnterZone(string zoneID)
  {
      if (!ZoneSystem.IsPositionInZone(transform.position, zoneID)) return;
      player.OnEnterZone(zoneID);
  }
  ```
- **Réclamation de récompense** (ouvrir le mail, cliquer "Récupérer") → bon candidat naturel pour un `[Command]` explicite côté client, avec vérification serveur stricte que le mail existe pour ce joueur et n'est pas déjà réclamé.

### Anti-dataminage — distinct de l'anti-triche ci-dessus

Point absent des sections précédentes : le serveur-autoritaire empêche un client de **tricher**
("j'ai rempli la condition" sans preuve), mais n'empêche pas un dataminer de **lire** le
contenu si l'asset est quand même embarqué dans le build client. Ce sont deux problèmes
différents, deux solutions différentes.

`ConditionData`/`ConditionEntry`/`ConditionCheckerBase` est le seul et unique pipeline de
déblocage passif du jeu (skills cachés, recettes, items secrets) — c'est LE contenu à protéger
en priorité, pas parce qu'il est précieux en soi, mais parce qu'il révèle le *déclencheur*
("reste 10 min sous cet arbre la nuit") qui casse la découverte organique (GDD §1.9). Le
résultat obtenu (le skill/item lui-même), une fois débloqué par n'importe quel joueur, finira
de toute façon par se savoir socialement — pas la peine de le cacher, seul le *comment* compte.

Nuance sur `QuestData` : `triggerCondition` et chaque `QuestObjective.checker` utilisent le
même `ConditionCheckerBase` que `ConditionData` — mais uniquement problématique pour une quête
`QuestType.Hidden` (déclencheur voulu secret comme un skill caché). Une quête normale, visible
dans le journal dès acceptée, n'a rien à cacher.

**Solution retenue** : une fois le split client/serveur en place, `ConditionData` (+ le
`triggerCondition` des quêtes `Hidden` uniquement) va dans un groupe Addressables — ou un
dossier — serveur-only, jamais inclus dans le build client. Le serveur évalue, le client ne
reçoit jamais que le résultat (mail de récompense — voir "bon réflexe déjà en place" ci-dessous,
`MailReward` est déjà une copie de `ConditionReward`, jamais une référence au `ConditionData`
déclencheur, donc le flux de données est déjà correct côté code — reste seulement la séparation
au niveau du build à faire quand Mirror arrivera).

Tout le reste (items, recettes une fois débloquées, mobs, zones) **reste embarqué client
normalement** — nécessaire au rendu (catalogue boutique, équipement des autres joueurs, VFX de
leurs skills) et le "streaming à la demande" coûterait un vrai système de livraison de contenu
(versioning, cache par joueur) pour un bénéfice limité — même les gros MMO (WoW, FFXIV)
n'empêchent pas le dataminage des items/quêtes, ils l'acceptent comme un coût connu.

### MailboxSystem.cs — notes

- Même pattern singleton global (`Instance`, `DontDestroyOnLoad`) que les autres systèmes → à faire passer en état par-joueur côté serveur, comme `UnlockManager`.
- **Bon réflexe déjà en place** : `MailReward` est une donnée miroir de `ConditionReward`, stockée dans le mail au moment de l'envoi plutôt que recalculée à la réclamation — évite qu'un changement ultérieur du `ConditionData` (rééquilibrage, etc.) modifie rétroactivement une récompense déjà promise.
- `mail.CanClaim` / `rewardClaimed` → la structure protège déjà contre une double réclamation **au niveau des données** ; il faut s'assurer que `ClaimReward()` (méthode tronquée dans l'extrait vu, entre `SendRewardMail` et la grosse fonction de distribution) fait bien cette vérification **côté serveur** avant de marquer `rewardClaimed = true`, et que c'est cette méthode précise qui doit devenir le point d'entrée du `[Command]` de réclamation.
- `InventorySystem.Instance?.AddItem(...)` appelé directement dans la distribution de récompense → à sécuriser comme le reste de l'inventaire (autorité serveur uniquement, voir section Entity/Player plus haut).
- Les nombreux `TODO` (TitleSystem, CraftSystem.UnlockRecipe, PetSystem.UnlockPet) → pas un point réseau en soi, mais à garder en tête : ces systèmes non encore implémentés devront suivre le même principe serveur-autoritaire quand ils arriveront.

---

## 🟤 Ménage de printemps (2026) — logique déjà écrite, jamais branchée

Trouvé pendant un grand nettoyage de code mort (3 agents Explore, tout `Assets/Scripts`). Pas
du code mort à proprement parler — de la logique complète mais dont le point d'entrée manque
encore. Gardé volontairement (pas supprimé) car ce sont des features MMO clairement prévues.
**À revoir de zéro** le jour où ces systèmes seront construits, pas juste "rebrancher tel quel"
(le contexte aura changé d'ici là, surtout après le passage réseau).

- **Pet / Capture** — `Entities/Mob.cs` : `AggroFrom(Entity)`, `IsCaptureable()`,
  `GetDamageContribution(Player)`. Système de capture/familier pas encore construit.
- **Résurrection par un autre joueur** — `Systems/RespawnSystem.cs` : `Revive(float hpPercent,
  float manaPercent)`. Nécessite du multijoueur pour avoir un sens.
- **Cooldown UI des passifs** — `Systems/PassiveSkillSystem.cs` : `GetCooldownRemaining(...)`,
  `IsUsedThisCombat(...)`. Logique de tracking déjà là, juste aucune UI ne l'affiche encore.
- **PvP / Duel / Arène / Champ de bataille / Groupe / Social / Pet / Hôtel des Ventes** —
  `Entities/Player.cs` : tout un cluster de méthodes (`OnOpenWorldDeath`, `OnPvPDeath`,
  `OnPvPKill`, `OnDuelWon`, `OnDuelLost`, `OnFreeArenaTop3`, `OnPvPReportValidated`,
  `OnWorldReportValidated`, `OnArenaResult`, `OnBattlefieldResult`, `RegisterGroupMemberLed`,
  `OnPlayerMet`, `OnPetCaptured`, `OnAnimalCaressed`, `OnServerConnect`,
  `GetHdVListingFeeRate`, `GetHdVSlotCount`, `OnHdVTransactionCompleted`,
  `OnHdVListingCancelled`) — publient déjà les bons events (`SocialEvent`, `PetEvent`,
  `ServerEvent`, `ItemEvent`...) mais rien ne les appelle encore (aucun système PvP/Groupe/HDV/
  Pet construit côté gameplay). `XPSystem.cs` confirme déjà "TODO Phase 8 bonus groupe §15.2".
- **4 events GameEventBus jamais publiés** — `PlayerLevelUpEvent`, `DebuffReceivedEvent`,
  `NpcInteractEvent`, `TimeEvent`. Le câblage complet existe (struct dans `GameEvents.cs` +
  event/`Publish`/`Reset` dans `GameEventBus.cs` + un `Checker` dédié dans
  `Progression/Conditions/Checkers/` + un abonnement dans `UnlockManager.cs`) mais **aucun
  code n'appelle jamais `GameEventBus.Publish(new XxxEvent{...})`** pour ces 4 — toute
  `ConditionData` qui en dépendrait est aujourd'hui impossible à déclencher. Il manque
  uniquement le point de déclenchement (ex: `Player.OnLevelUp()` devrait publier
  `PlayerLevelUpEvent`, `StatusEffectSystem` devrait publier `DebuffReceivedEvent`, etc.).

---

## Ordre de traitement recommandé

1. **Trancher l'architecture** GameEventBus + SceneLoader + Portal (au moins sur le papier — comment ça doit fonctionner à plusieurs joueurs/zones)
2. **Refactor SpellData/EquipmentData** (rapide maintenant, coûteux plus tard)
3. **Prototype réseau du mouvement** (PlayerController + Mirror, testé en local 127.0.0.1)
4. **Entity + sous-classes en SyncVar / logique serveur-autoritaire** (HP, mana, stats, mort)
5. **CombatSystem + SkillSystem en Cmd/Rpc** (le calcul pur migre tel quel côté serveur, l'exécution se scinde en Cmd → validation → Rpc pour les effets visuels)
6. **StatusEffectSystem synchronisé**
7. **PNJ et interactions commerce/quête**
8. **Système de déblocage par conditions** (index par type d'event + état par joueur + Cmd de réclamation de récompense) — peut se faire en parallèle des étapes 4-7, car son gros du travail (indexation, séparation du state) est indépendant du reste, mais dépend du fait que CombatSystem/SkillSystem publient déjà leurs events côté serveur (donc à ne finaliser qu'après l'étape 5)
9. Puis, comme vu précédemment : VPS → comptes → persistance BDD → sélection serveur/personnages → launcher → anti-triche avancé

Chaque étape doit fonctionner et être testée avant de passer à la suivante.
