# Prestige & Aura — Spec

**Statut** : brouillon, en attente de relecture Florian. Chiffres marqués ⚠️ CALIBRER sont des
propositions de départ, pas des valeurs finales — à ajuster en testant.

## Contexte

Remplace le système `worldReputation` du GDD §4.5.1 (une seule jauge, contradiction interne dans
le GDD : "elle ne se perd pas" vs un tableau de pertes juste en dessous). Inspiré du couple
Réputation/Dignité de NosTale (seuils et paliers de malus repris tels quels, juste renommés pour
ne pas copier — voir §5 pour les correspondances).

Deux jauges **indépendantes**, pas d'interaction entre elles :

- **Prestige** — statut social, monte principalement, ne descend que dans de rares cas (pas liée à
  la mort). Débloque des accès (PNJ exclusifs, quêtes avancées, recettes endgame) — PAS de
  réduction de prix marchand (rôle retiré, voir §3).
- **Aura** — jauge de pénalité, descend sur la mort (et actions "déshonorantes" similaires),
  applique des malus progressifs (prix PNJ en hausse, capture de familier bloquée, debuffs de
  stats directs). Remplace entièrement le rôle "pénalité de mort" que `worldReputation` avait dans
  le GDD original.

## §0 — Icône de statut affichée — règle unique (ajouté 2026-09-29)

Un seul emplacement d'icône de statut par joueur, mais SA SOURCE change selon l'état d'Aura :

- **Aura négative (-1000 à -1)** → affiche l'icône du palier AURA courant (Déchu/Maudit/.../Terni)
  — un "badge de honte" visible qui prend le dessus.
- **Aura non-négative (0 à +100)** → affiche l'icône du palier PRESTIGE à la place — le joueur
  "flex" son statut normalement tant que son Aura n'est pas entamée.

Donc jamais les deux icônes en même temps — Aura compromise masque/remplace l'affichage Prestige,
un signal visuel fort et immédiat sans avoir à lire deux jauges séparées.

## §1 — Prestige

### 1.1 Champ

`Player.prestige : int`, range `0` à illimité en théorie (dernier palier "5 000 001+"). Remplace
`Player.worldReputation` (même field renommé, ou nouveau field + migration — à trancher en
implémentation, aucun impact design).

### 1.2 Paliers (seuils NosTale conservés, noms renommés)

Sous-grades Vert/Bleu/Rouge à l'intérieur de chaque palier (sauf Légende) — purement visuel
(couleur de bordure d'icône), ne débloque rien de plus, juste une granularité affichée. Calculé en
divisant la plage du palier en 3 tiers égaux.

| Points requis | Grade |
|---|---|
| 0 – 500 | Novice |
| 501 – 2 000 | Apprenti |
| 2 001 – 10 000 | Éveillé |
| 10 001 – 50 000 | Aguerri |
| 50 001 – 170 000 | Chevronné |
| 170 001 – 450 000 | Illustre |
| 450 001 – 1 000 000 | Maître |
| 1 000 001 – 5 000 000 | Élite |
| 5 000 001+ | Légende (grade unique, pas de sous-grade) |

### 1.3 Déblocages par palier ⚠️ CALIBRER

Reprend l'esprit du tableau GDD §4.5.1 original, sans les % de réduction prix (retirés, voir §3).
À caler précisément avec Florian une fois le reste du contenu (quêtes, PNJ, recettes) plus avancé
— pour l'instant, structure indicative :

| Palier | Déblocage indicatif |
|---|---|
| Apprenti | Quêtes secondaires palier 1-2 |
| Éveillé | Marchands exclusifs en ville |
| Aguerri | Quêtes de réputation avancées |
| Chevronné | PNJ de faction exclusifs |
| Illustre | Quêtes de lore rares |
| Maître | Recettes de craft endgame (§13 GDD) |
| Élite | — |
| Légende | PNJ marchand Légendaire (catalogue exclusif) |

### 1.4 Gains — TRANCHÉ 2026-09-29 : la valeur vit sur l'asset, pas dans une formule centrale

Florian : le jeu est gameplay-first, pas quête/histoire-first — une formule globale sur un système
de quêtes qui existe à peine n'a pas de sens. Principe unique retenu, cohérent avec l'idée déjà
posée pour les mobs : **chaque asset qui donne du Prestige porte son propre champ
`prestigeReward: int`**, avec une valeur par défaut raisonnable si l'auteur du contenu n'y touche
pas. Pas de formule `palier × X` globale.

| Source | Mécanique |
|---|---|
| Quête (principale ou secondaire, plus de distinction) | `QuestData.prestigeReward` (défaut 50) |
| Donjon | `DungeonData.prestigeReward` (défaut 100) — **daily, pas "1ère fois à vie"** : une fois par donjon par jour réel (même pattern que `restockDelay` déjà utilisé pour le stock marchand). Florian a explicitement préféré le repeatable-daily au one-shot-lifetime, cohérent avec un jeu centré gameplay/farm. |
| Mob spécifique (boss de map ou tout mob dangereux) | `LootTable.prestigeReward` optionnel (asset séparé référencé par `MobData.lootTable`, PAS un champ sur `MobData` lui-même — voir `Data/Mobs/MobData.cs:148`), absent = 0 (la plupart des mobs n'en donnent pas). PAS de bonus automatique lié à `MobAIType.Aggressive` — Florian a explicitement préféré rester sur le mécanisme générique déjà décidé (le champ manuel, un mob agressif "dangereux pour le monde" reçoit juste 1-2 posé à la main dessus comme n'importe quel autre mob à reward). Pas de cas spécial dans le code. |
| Événement world (Boss Géant/Invasion/Combat à Vague) | Voir §1.4bis — reward plein par participation, pas de calcul de contribution pour l'instant (solo) |
| Donation PNJ / guilde | 20 (plafonné : max 1×/semaine) |
| **Kill PvP** | ⚠️ DIFFÉRÉ — bloqué sur les zones PvP non construites (bloc multijoueur parqué), pas à spécifier maintenant |

### 1.4bis — Contribution événement world : DIFFÉRÉ, problème multijoueur pas encore réel

Florian a soulevé un vrai souci de design : comment calculer une "contribution" équitable sur un
event à 500 joueurs (adds vs boss, seuil de %, joueurs pénalisés injustement) ? **Décision : ne
PAS designer cet algorithme maintenant** — le jeu est solo aujourd'hui, donc la contribution d'un
joueur est TOUJOURS 100% par définition (un seul joueur possible). Implémentation actuelle : "le
joueur a participé (a tapé au moins un mob de l'event en cours)" → reward plein depuis
`EventData.prestigeReward` (ou équivalent, une fois les events eux-mêmes construits — roadmap
#2-4, pas encore fait). Le vrai algorithme de partage équitable (contribution %, distinction
adds/boss, etc.) est un problème MULTIJOUEUR à concevoir quand le multijoueur existera réellement
— inutile de designer une formule pour 500 joueurs fictifs aujourd'hui. Même statut que le
PvP kill/death : différé, pas oublié.

### 1.5 Pertes — rares, PAS liées à la mort PvE

La mort en PvE (monde ouvert ou donjon) ne touche JAMAIS Prestige — c'est le rôle d'Aura (§2).

| Source | Valeur |
|---|---|
| Quête échouée / abandonnée après acceptation | -30 |
| **Mort en PvP** | ⚠️ DIFFÉRÉ — même statut que le kill PvP ci-dessus (§1.4), bloqué sur les zones PvP non construites |

Florian a explicitement laissé "autre ?" ouvert sur les deux tableaux (gains et pertes) — pas de
source supplémentaire à inventer ici, à compléter si un besoin apparaît en testant.

## §2 — Aura

### 2.1 Champ

`Player.aura : int`, `+100` = valeur de départ/max d'un nouveau personnage. **PAS de plancher dur
à -1000** (retiré 2026-09-29, voir discussion) — le nombre brut peut descendre indéfiniment sous
-1000 si le joueur meurt en boucle sans jamais se purifier. Seul le PLAFOND est clampé : jamais
au-dessus de +100 (une Purification qui ferait dépasser est plafonnée, pas de sur-stock).

**Pourquoi pas de plancher** : le coût de Purification (§2.5) doit continuer à avoir un sens même
très profond dans le négatif — avec un plancher dur, mourir une 15e ou une 50e fois une fois au
fond coûterait pareil à réparer, plus aucune pression à se purifier tôt plutôt que tard. Sans
plancher, le nombre continue de couler et sert de donnée pour le calcul de coût. **Les MALUS eux
restent plafonnés à la sévérité de Déchu** (§2.2) — aucun palier pire que Déchu n'existe, le badge
affiché reste "Déchu" quelle que soit la profondeur en dessous de -1000, seul le nombre brut
(invisible du joueur) continue de baisser pour nourrir le coût de Purification.

### 2.2 Paliers et malus

⚠️ % prix hérités tels quels de la table NosTale d'origine (§5), jamais re-confirmés
individuellement par Florian après le pivot "prix géré par Aura uniquement" (§3) — la STRUCTURE
est validée, ces chiffres précis restent à valider ou recalibrer.

| Points | Grade | Malus prix PNJ | Capture familier | Debuff (voir §2.3) |
|---|---|---|---|---|
| +100 à 0 | Normal | Aucun | Autorisée | Aucun |
| -1 à -99 | Terni | +10% | Autorisée | Aucun |
| -100 à -199 | Voilé | +20% | Autorisée | Niveau 1 (léger) |
| -200 à -399 | Éteint | +30% | Bloquée | Niveau 2 |
| -400 à -599 | Corrompu | +40% | Bloquée | Niveau 3 |
| -600 à -799 | Maudit | +50% | Bloquée | Niveau 4 |
| -800 à -1000 | Déchu | +50% | Bloquée | Niveau 5 (le plus fort) |

Debuff dès **Voilé** (-100), pas seulement les 2 derniers paliers comme la référence NosTale —
Florian : "on peut commencer un debuff léger plus rapidement genre à -100". Escalade sur les 5
paliers Voilé→Déchu (pas 4 — corrigé après relecture, §2.3 listait une répartition différente et
incohérente avec ce tableau avant correction). Capture familier bloquée reste à partir d'**Éteint**
(-200), comme la table NosTale d'origine — seul le DEBUFF a été avancé à Voilé, pas la capture.

### 2.3 Implémentation des malus — via DebuffData existant

Pas de nouveau système — réutilise `DebuffData`/`StatusEffectSystem` déjà en place partout dans le
projet (`StatusEffectSystem.TryApplyDebuff(DebuffData, Entity)`). Un `DebuffData` peut déjà
contenir plusieurs lignes de modificateurs de stats (n'importe quelle stat, pas juste ATK/DEF —
confirmé par Florian, "on peut toucher à toutes les stats qu'on souhaite, c'est déjà construit").

**5 `DebuffData` distincts** (un par palier concerné, Voilé→Déchu — corrigé après relecture,
la version précédente n'en listait que 4 et ne correspondait pas au tableau §2.2). Pas de cumul
entre eux — swap propre à chaque changement de palier, pas d'empilement de plusieurs instances
actives en même temps :

- `Debuff_AuraVoilee` (palier Voilé, -100 à -199, niveau 1) — léger, 1-2 stats mineures
- `Debuff_AuraEteinte` (palier Éteint, -200 à -399, niveau 2) — un cran plus fort
- `Debuff_AuraCorrompue` (palier Corrompu, -400 à -599, niveau 3) — plus fort encore
- `Debuff_AuraMaudite` (palier Maudit, -600 à -799, niveau 4) — plus fort encore
- `Debuff_AuraDechue` (palier Déchu, -800 à -1000, niveau 5) — le pire, chaque asset contient TOUT
  le contenu du palier précédent + plus (pas de stacking runtime entre 2 instances, tout est écrit
  à la main dans l'asset du palier le plus sévère actif)

⚠️ CALIBRER : quelles stats exactement et quelles magnitudes par palier — pas encore décidé, à
faire une fois les `DebuffData` créés dans l'Éditeur.

Application : un système centralisé (`Player.RefreshAuraState()` ou équivalent, appelé chaque fois
qu'`aura` change) compare le palier courant au précédent — si différent, retire l'ancien
`DebuffData` (si un était actif) et applique le nouveau via `StatusEffectSystem.TryApplyDebuff`.
Retour à Normal (aura ≥ 0) retire tout debuff actif, aucun n'est appliqué.

**Contrainte — non purifiable, 2026-09-29, ACTIONABLE MAINTENANT (pas différée)** : ce debuff ne
doit JAMAIS pouvoir être retiré autrement que par le franchissement de seuil Aura lui-même.
Correction après recherche plus poussée : un vrai système Purify existe DÉJÀ dans le projet
(`BuffType.Purified` → `StatusEffectSystem.RemoveDebuffsByChance()`, lignes 970-981 — retire
chaque debuff actif via un jet de chance indépendant par instance). Sans garde-fou, un joueur avec
un buff/potion Purify effacerait son malus Aura sans jamais passer par le PNJ Purification prévu
au §2.5 — contournerait tout le système.

Fix : ajouter `public bool immuneToPurify = false;` sur `DebuffData` (`Data/StatusEffect/
DebuffData.cs`), cocher `true` sur les 5 `DebuffData` Aura. `RemoveDebuffsByChance()` filtre les
instances dont `instance.Instance.DebuffData.immuneToPurify == true` avant de rouler leur jet de
suppression. Petit changement, 2 fichiers touchés (`DebuffData.cs` + `StatusEffectSystem.cs`),
fait EN MÊME TEMPS que la création des 5 assets, pas un chantier séparé pour plus tard.

### 2.4 Capture de familier bloquée — TRANCHÉ 2026-09-29, via référence directe sur DebuffData

Florian : la capture viendra d'un sort + un consommable (pas encore designé — Familier, #8 sur la
roadmap). Le blocage n'a donc pas besoin d'attendre le design complet du Familier — juste besoin
que le `SkillData` de capture EXISTE comme asset (Florian peut le créer dès maintenant, sa vraie
logique de capture viendra plus tard, seul `isCaptureSkill`-style n'est même pas nécessaire, voir
ci-dessous).

**Mécanisme final (Florian, plus simple que ma 1ère proposition avec enum+flag dédiés)** :
référence directe, même pattern que `Portal.requiredMobs` — pas de nouveau `DebuffType`, pas de
flag sur `SkillData` :

- Nouveau champ `DebuffData.blockedSkills : List<SkillData>` — glisser le(s) skill(s) à bloquer
  directement dans l'asset du debuff concerné (ici, le `SkillData` de capture dans les `DebuffData`
  Éteint→Déchu, voir §2.2).
- Nouvelle méthode `StatusEffectSystem.IsSkillBlocked(SkillData skill)` — vrai si un debuff actif
  référence ce skill dans son `blockedSkills`.
- `SkillBar.cs`, même bloc que le check Silence existant (`fx.isSilenced`, ligne ~323) :
  `if (fx.IsSkillBlocked(skillData)) { bloqué }`.

Avantage sur l'enum dédié : réutilisable pour bloquer N'IMPORTE quel skill par n'importe quel
futur debuff, sans nouveau champ/enum à chaque fois — juste glisser la référence.

### 2.4bis — Sources confirmées (récap Florian 2026-09-29)

**Pertes** : mort en monde ouvert, mort en donjon. Florian a laissé "autre ?" ouvert — pas de
source supplémentaire inventée ici.

**Gains** : Florian a posé la question "Points d'aura gagné : ?" sans réponse — c'est EXACTEMENT
la même question que le Recovery ci-dessous (§2.5), pas un point séparé. Répondre à §2.5 répond
aussi à "comment gagne-t-on de l'Aura".

### 2.5 Recovery — TRANCHÉ 2026-09-29 : PNJ "Purification", achat PAR PALIER

Florian : "je préfère clairement la deuxième idée" — AUCUNE régénération passive automatique.
L'Aura ne remonte QUE via un PNJ dédié (nom à définir, ex : prêtre/oracle).

**Modèle final** (après plusieurs itérations — slider de quantité et coût multi-ressource
s'excluaient mutuellement avec l'UI existante, résolu en changeant d'angle) : le joueur achète UNE
TRANSITION DE PALIER à la fois, jamais un montant brut de points.

- **6 transitions fixes**, chacune son propre coût (Aeris + ressources au choix, indépendant par
  transition, ⚠️ CALIBRER) : Déchu→Maudit, Maudit→Corrompu, Corrompu→Éteint, Éteint→Voilé,
  Voilé→Terni, Terni→Normal.
- Acheter une transition **SET** `player.aura` directement au plancher du palier suivant — pas un
  ajout de points. Peu importe où exactement dans le palier courant le joueur se trouve (-650 ou
  -850, tous les deux dans une portion basse), le coût et la destination sont les MÊMES : le prix
  est fixé PAR PALIER, jamais par distance de points exacte.
- Exemple (Florian) : un joueur à -1000 paie pour atteindre -600, puis re-paie pour atteindre
  -400, puis -200, puis -100, puis 0. Chaque achat = un palier, jamais un saut direct au sommet.
- Aura déjà ≥ 0 (Normal) → rien à acheter, aucune option de Purification proposée (pas de malus à
  réparer).

**Sélection dynamique du palier — PAS de nouveau système Condition/AuraChecker requis.** Le PNJ
calcule lui-même le palier courant depuis `player.aura` (petite table de seuils, même pattern que
`Player.WorldRepThresholds` déjà existant dans le code) et affiche UNE SEULE option pertinente
(coût + palier suivant), calculée au runtime — pas 6 `DialogueStage` statiques avec des conditions
de range. Précédent direct dans le projet : `PNJData.highReputationDialogue` +
`reputationDialogueThreshold` fait déjà exactement ce genre de comparaison de seuil directement
dans le script PNJ, sans passer par le système `IConditionChecker` générique. `AuraChecker` (§4)
reste un sujet séparé, pour de vraies récompenses conditionnelles plus tard — pas requis pour que
ce PNJ fonctionne.

Ce point est maintenant CLOS côté mécanique (PNJ-only, achat par palier, calcul dynamique côté
PNJ) — reste seulement à calibrer les 6 coûts et à décider du nom/emplacement du PNJ.

## §3 — Prix PNJ — Aura uniquement, Prestige n'y touche plus

`prixEffectif = prixBase × (1 + malusAura)`. Le `réductionRéputation` du GDD §4.5.1/§12.6
original (basé sur le rang de réputation) est retiré — Prestige garde son rôle de déblocage
(§1.3) mais n'influence plus jamais un prix.

**Impact code existant** : `Player.GetHdVListingFeeRate()`/`GetHdVSlotCount()` lisent
`worldReputationRank` (`Entities/Player.cs:1286-1296`) — HdV est un système parqué (bloc
multijoueur), pas de conflit immédiat, mais à revoir si HdV est un jour construit : ces méthodes
devraient probablement lire le palier Prestige (pas Aura, HdV n'a pas de rapport avec la
mort/comportement) une fois le renommage fait.

## §4 — Conditions & Rewards — intégration ConditionData — TRANCHÉ 2026-09-29 : on les construit

Florian : "il faudra aussi caler les conditions dessus. Si aura = -1000, alors déblocage de
récompense sur ConditionData etc ?? il faut tout prévoir." Confirmé : "oui, il faut les
construire, on peut s'en servir pendant la démo."

**Pas de nouvelles classes de checker** — `StatChecker` (`Progression/Conditions/Checkers/
StatChecker.cs`) couvre déjà ce besoin via son mode `FinalStat` : `minValue`/`maxValue` généliques
existent déjà (voir `FinalStatType.PlayerLevel`, ajouté au même enum plutôt que dans un checker
dédié — même précédent direct à suivre). Juste :

- Ajouter `Prestige` et `Aura` à `StatChecker.FinalStatType`.
- 2 lignes dans le switch qui résout la valeur : `FinalStatType.Prestige => p.prestige,` /
  `FinalStatType.Aura => p.aura,`.
- `minValue`/`maxValue` déjà supportés couvrent nativement "Aura ≤ X" (juste `maxValue = X`,
  `minValue = 0`) sans rien construire de spécifique.

Les deux sens (récompense pour BONNE aura ET récompense pour MAUVAISE aura, façon "chemin du
vilain") restent possibles avec ce même mécanisme générique — le contenu "chemin sombre" réel
(quêtes/PNJ réservés à une Aura basse) n'est PAS construit maintenant, seul le checker l'est,
prêt à être utilisé dès qu'un designer veut poser une condition dessus.

## §5 — Correspondance avec la référence NosTale (pour mémoire, pas dans le jeu)

| NosTale | AetherTree |
|---|---|
| Réputation | Prestige |
| Dignité | Aura |
| Normal / Soupçonné / Doigt pointé / Banni de la société / Inconduite émotionnelle / Moqueries de tous / Inutile à la société | Normal / Terni / Voilé / Éteint / Corrompu / Maudit / Déchu |
| Seuils de points (Réputation et Dignité) | Identiques, conservés tels quels |

## §6 — Hooks de code identifiés (où brancher les gains/pertes)

Réutilise directement les points d'accroche déjà câblés aujourd'hui pour `AnnoncePanel` :

- `InstanceSession.OnPlayerDeath(Player)` — Aura -X (mort en donjon)
- `InstanceSession.OnBossKilled(Mob)` — Prestige + `DungeonData.prestigeReward`, daily (pas
  "1ère fois", corrigé après relecture — voir §1.4) : nécessite un flag "dernière date de reward
  par donjon par joueur" pas encore existant (pas un simple bool "complété une fois", un
  timestamp/date à comparer au jour réel courant), à ajouter.
- Mort en monde ouvert — hook pas encore identifié (`Player.Die()`? `RespawnSystem`?) — à localiser
  en implémentation
- Quête complétée/échouée — `QuestSystem` (Prestige +/-)
- Événement collectif (Invasion/Boss Géant/Combat à Vague) — pas encore construits (roadmap #2-4),
  hook à ajouter en même temps que ces features

## Points ouverts — résumé (à trancher avant code)

1. ~~Formule de gain/perte Prestige (§1.4/1.5)~~ — **TRANCHÉ** : la valeur vit sur l'asset
   (`QuestData`/`DungeonData`/`LootTable`.`prestigeReward` — `LootTable`, pas `MobData` : c'est
   l'asset séparé référencé par `MobData.lootTable`), pas de formule globale.
   Valeurs par défaut données (50 quête / 100 donjon / 20 donation / -30 quête ratée) — ajustables
   asset par asset sans retoucher le code.
2. Contenu exact des 5 `DebuffData` Aura (§2.3) — quelles stats, quelles magnitudes.
3. ~~Mécanisme "bloque capture familier" (§2.4)~~ — **TRANCHÉ ET IMPLÉMENTÉ** : `DebuffData.
   blockedSkills`/`StatusEffectSystem.IsSkillBlocked`/check dans `SkillBar.cs`, fait 2026-09-29.
   Reste juste à créer le vrai `SkillData` de capture (Florian, dans l'Éditeur) et le glisser
   dans les `DebuffData` Éteint→Déchu — pas bloquant côté code.
4. ~~Recovery d'Aura (§2.5)~~ — **TRANCHÉ** : PNJ Purification, achat PAR PALIER (6 transitions
   fixes, pas de régén passive, pas de slider). Reste à calibrer les 6 coûts (Aeris + ressources
   par transition) et nommer/placer le PNJ.
5. ~~`PrestigeChecker`/`AuraChecker` (§4)~~ — **TRANCHÉ** : construits pour cette démo, mais pas
   comme 2 nouvelles classes — juste `Prestige`/`Aura` ajoutés à `StatChecker.FinalStatType`
   (déjà générique min/max). Pas requis pour le PNJ Purification (§2.5, résolu autrement). Le
   contenu "chemin sombre" (quêtes/PNJ réservés à Aura basse) n'est PAS construit maintenant,
   seul le checker l'est.
6. ~~Contribution événement world (§1.4bis)~~ — **TRANCHÉ, différé** : pas d'algorithme de
   partage équitable pour l'instant (solo = contribution toujours 100%), à concevoir quand le
   multijoueur existera réellement.
7. PvP kill/mort → Prestige (§1.4/1.5) — explicitement différé, bloqué sur les zones PvP non
   construites, pas à trancher maintenant.

**Points 2-3-5 restent à trancher avant d'écrire le code d'implémentation.** Le reste est soit
tranché, soit explicitement différé (pas bloquant).
