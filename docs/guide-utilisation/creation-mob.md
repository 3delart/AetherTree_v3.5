# Guide — Création d'un Mob

> Référence pratique complète pour créer un nouveau mob : le `MobData` (ScriptableObject), le
> prefab (composants + Animator), le lien entre les deux, et l'enregistrement dans une zone de
> spawn. Un exemple concret complet ("Loup Gris") est donné en fin de guide.
>
> Contexte technique de l'IA de combat (Patrol/Engage/Return, aggro, leash) :
> `docs/superpowers/specs/2026-09-13-combat-ai-controller-design.md`. Ce guide en est le résumé
> pratique, côté création de contenu — pas besoin de lire la spec pour créer un mob standard.

## Sommaire

- [Vue d'ensemble](#vue-densemble)
- [Étape 1 — Créer le MobData](#étape-1--créer-le-mobdata)
- [Étape 2 — Préparer le prefab](#étape-2--préparer-le-prefab)
- [Étape 3 — Animator Controller du mob](#étape-3--animator-controller-du-mob)
- [Étape 4 — Lier MobData au prefab](#étape-4--lier-mobdata-au-prefab)
- [Étape 5 — Loot Table](#étape-5--loot-table)
- [Étape 6 — Enregistrer dans une zone de spawn](#étape-6--enregistrer-dans-une-zone-de-spawn)
- [Exemple complet — "Loup Gris"](#exemple-complet--loup-gris)
- [Vérification Play Mode](#vérification-play-mode)
- [Pièges fréquents / checklist rapide](#pièges-fréquents--checklist-rapide)

---

## Vue d'ensemble

Un mob = un `MobData` (les stats/données, réutilisable pour spawner N instances) + un prefab
portant le composant `Mob` (le comportement). Toute l'IA de combat (patrouille, poursuite,
attaque, retour au spawn) est déléguée au composant partagé `Combat/CombatAIController.cs` — le
même composant que celui utilisé par les PNJ combattants (voir `creation-pnj.md`). `Mob.cs`
implémente l'interface `ICombatAIProfile` et ne fait qu'exposer ses propres données
(`BasicAttackSkill`, `PatrolRadius`, `LeashDistance`...) au controller ; il n'y a rien à câbler
manuellement pour ça, c'est automatique dès que `Mob` est présent sur le prefab.

`aiType` (sur `MobData`) détermine le ciblage :
- **Passive** — n'attaque que s'il est agressé en premier (garde en mémoire qui l'a frappé).
- **Aggressive** — scanne en permanence `detectionRange` autour de lui et engage le premier
  joueur/PNJ venu.
- **Boss** — IA scriptée à phases (hors scope de ce guide, non implémentée par
  `CombatAIController`).

## Étape 1 — Créer le MobData

`Assets > Create > AetherTree > Mob > MobData`. Convention de nom : `mob_<nom>` (ex:
`mob_loup_gris`).

### Identité

| Champ | Rôle |
|---|---|
| `mobID` | Clé technique stable (auto-remplie avec le nom de l'asset si vide). Jamais affichée au joueur. |
| `mobName` | Nom affiché au joueur. |
| `mobType` | `Normal` / `Elite` (×1.8 atk, ×2.0 def, ×4.0 HP) / `BossZone` / `BossDungeon` / `BossRaid` / `Nocturnal` (force `isNocturnal`) / `Capturable` (force `isCapturable`). |
| `aiType` | Voir [Vue d'ensemble](#vue-densemble) — `Passive` / `Aggressive` / `Boss`. |

### Élémentaire

`elementType` (élément fixe, cohérent avec le biome) + `baseElementPointValue` /
`elementPointValuePerLevel` (points élémentaires que ce mob "vaut" pour les dégâts élémentaires
qu'il inflige, scalent par niveau).

### Stats de base (niveau 1) & Croissance par niveau

Les stats finales sont calculées au spawn par `MobStatCalculator` :

```
statFinale = (statBase + statParNiveau × (level-1)) × multMobType
```

| Groupe | Champs |
|---|---|
| Vie/Mana | `baseHP`, `baseMana` + `hpPerLevel`, `manaPerLevel` |
| Dégâts | `baseAtkMin`/`baseAtkMax` + `atkMinPerLevel`/`atkMaxPerLevel` (min auto-clampé ≤ max) |
| Défenses | `baseDefMelee`/`baseDefRanged`/`baseDefMagic` + leurs `...PerLevel` |
| Précision/Esquive/Crit | `basePresision`, `baseDodge`, `baseCritChance`, `baseCritMultiplier` + `precisionPerLevel`/`dodgePerLevel` (crit ne scale pas par niveau) |

Les résistances élémentaires (`fireResist`...`lightResist`, `[-1;1]`) sont un **profil fixe**,
ne scalent jamais avec le niveau — une valeur négative est une vulnérabilité assumée (ex: un
boss Feu qui craint l'Eau à -20%, amplifie au lieu de réduire).

`regenHP`/`regenMana` restent à 0 sauf cas spécial (boss) — les mobs normaux ne régénèrent pas
(GDD §3.1, régen réservée au joueur).

### Combat

`weaponCategory` détermine quelle défense du joueur (mêlée/distance/magie) s'applique quand ce
mob attaque.

### Skills

| Champ | Rôle |
|---|---|
| `basicAttackSkill` | Skill utilisé par défaut (cooldown le plus court en général). Si `null`, fallback sur dégâts directs sans passer par `SkillData`. |
| `skills` | Skills secondaires, **prioritaires** sur l'attaque de base — `CombatAIController` tente d'abord un skill secondaire disponible (hors cooldown, à portée) avant de retomber sur `basicAttackSkill`. |

Voir `creation-skills.md` pour créer les `SkillData` eux-mêmes.

### IA

| Champ | Rôle |
|---|---|
| `moveSpeed` | Vitesse de déplacement (Patrol et Engage — pas de vitesse combat séparée côté Mob, contrairement à PNJ). |
| `detectionRange` | Rayon de scan (`Aggressive` uniquement) — aussi utilisé comme base du leash (voir `leashMultiplier`). |
| `leashMultiplier` | Distance de leash = `detectionRange × leashMultiplier`. Au-delà, le mob abandonne la cible et rentre au point où il a engagé (`aggroPos`, pas forcément le spawn d'origine — l'ancre bouge à chaque nouvel engagement). |
| `patrolRadius` | Rayon de déambulation autour du spawn hors combat. `0` = reste immobile au spawn. |

### Animations locomotion

| Champ | Rôle |
|---|---|
| `idleClip` | Anim jouée à l'arrêt hors combat. |
| `walkClip` | Anim de déplacement en Patrol. |
| `chaseClip` | Anim de déplacement en Engage (poursuite/combat rapproché) — clip **distinct** de `walkClip`, même à vitesse identique (voir [Étape 3](#étape-3--animator-controller-du-mob)). |

Les 3 sont consommés par `CombatEntityAnimatorController` (composant unique partagé avec les
PNJ). `OnValidate` avertit en Console si l'un des 3 est vide — sans lui, le mob affiche l'anim
placeholder du Controller partagé (faite pour un autre rig, souvent T-pose/désarticulé).

### Cycle jour/nuit, Loot, Capture, Effets On-Hit, Visuel, Prefab

- `isNocturnal` : actif uniquement la nuit (forcé à `true` si `mobType = Nocturnal`).
- `lootTable` : voir [Étape 5](#étape-5--loot-table).
- `isCapturable` / `captureHPThreshold` / `petType` : capture en pet sous ce seuil de HP% (forcé
  actif si `mobType = Capturable`).
- `onHitReceivedEffects` / `onHitDealtEffects` : procs on-hit (buffs/debuffs à % de chance à
  chaque coup reçu/infligé), même système que l'équipement joueur.
- `portrait` : sprite UI. `prefab` : référence vers le prefab du mob (pratique pour retrouver le
  prefab depuis l'asset de données, notamment pour `SpawnManager`).

---

## Étape 2 — Préparer le prefab

Crée (ou duplique) un GameObject avec le modèle 3D + `Animator` en enfant, puis ajoute le
composant **`Mob`** sur la racine. Grâce aux `[RequireComponent]` de `Mob`, Unity ajoute
**automatiquement** en cascade :

- `NavMeshAgent`
- `StatusEffectSystem`
- `CapsuleCollider`
- `SkillSystem`
- `CombatAIController`
- `CombatEntityAnimatorController` (qui cascade lui-même `Animator` s'il n'est pas déjà présent)

Rien à faire manuellement pour ces composants — juste vérifier après coup que le
`NavMeshAgent` a un `Radius`/`Height` cohérents avec le modèle, que le `CapsuleCollider` est
bien dimensionné (les valeurs par défaut d'Unity sont rarement les bonnes), et que **Apply Root
Motion est décoché** sur l'`Animator` — le `NavMeshAgent` pilote déjà la position, cocher Apply
Root Motion fait bouger le mob deux fois (agent + anim), source de glissement/désync.

⚠ La scène doit avoir un **NavMesh baké** couvrant la zone où le mob doit se déplacer, sinon le
`NavMeshAgent` ne pourra ni patrouiller ni poursuivre.

---

## Étape 3 — Animator Controller du mob

Contrairement à l'ancien système (un Animator Controller par créature), **tout Mob/PNJ combattant
partage le MÊME Controller** — un seul asset pour tout le projet, ex `CombatEntityBase.controller`
— assigné sur `Animator.Controller` de chaque prefab. Seuls les clips diffèrent, jamais le
graphe. Ne crée PAS de nouveau Controller par mob — réutilise l'existant.

Le Controller partagé (à monter/vérifier une seule fois pour tout le projet) :

| État / Paramètre | Type | Rôle |
|---|---|---|
| `Idle` | — | Motion = clip **placeholder** dédié `PLACEHOLDER_Idle` (jamais réellement joué). |
| `Walk` | — | Motion = placeholder `PLACEHOLDER_Walk`. |
| `Chase` | — | Motion = placeholder `PLACEHOLDER_Chase`. |
| `Attack` | — | Motion = placeholder `PLACEHOLDER_Attack`. Réutilisé pour TOUS les skills (attaque de base + secondaires) — le clip est échangé à la volée via `AnimatorOverrideController`, jamais un state différent par skill. |
| `Speed` | Float | Vitesse actuelle du `NavMeshAgent` (`agent.velocity.magnitude`), mis à jour chaque frame. |
| `IsChasing` | Bool | `true` pendant tout l'état `Engage` (poursuite ET combat rapproché immobile), `false` en `Patrol`/`Return` — permet de distinguer Walk de Chase quand `Speed` est identique dans les deux cas. |
| `CancelAction` | Trigger | Coupe net une canalisation en cours (voir `creation-skills.md` § Canalisation). |

Transitions :

| De → Vers | Condition | Has Exit Time | Transition Duration |
|---|---|---|---|
| Idle → Walk | `Speed` > 0.1 | Non | ~0.1-0.15s |
| Walk → Idle | `Speed` < 0.1 | Non | ~0.1-0.15s |
| Idle → Chase | `IsChasing` = true | Non | ~0.1-0.15s |
| Chase → Idle | `IsChasing` = false | Non | ~0.1-0.15s |
| Walk → Chase | `IsChasing` = true | Non | ~0.1-0.15s |
| Chase → Walk | `IsChasing` = false | Non | ~0.1-0.15s |
| Any State → Idle | `CancelAction` | Non | proche de 0 |
| Attack → Idle | — (aucune condition) | **Oui** (~0.9-1) | proche de 0 |

`Attack` n'a **aucune transition entrante** dans le graphe — il est entré directement par code
(`Animator.Play("Attack", 0, 0f)`), jamais via un paramètre. Walk↔Chase directes sont
nécessaires : sans elles, un mob qui patrouille en marchant et engage sans jamais s'arrêter
resterait bloqué sur le clip Walk (Speed ne repasse jamais sous le seuil pour libérer un
passage par Idle).

Les 4 clips placeholder (`PLACEHOLDER_Idle`/`Walk`/`Chase`/`Attack`) sont des clips **bidons,
jamais réellement joués** — juste des clés d'échange retrouvées par nom au runtime
(`CombatEntityAnimatorController`). Un renommage de ces 4 clips dans le Controller casse le
lookup silencieusement (warning Console à l'Awake) — ne jamais les renommer sans mettre à jour
`CombatEntityAnimatorController.cs`.

**Créer les 4 placeholders (une seule fois pour le projet)** — un clip importé depuis un FBX
(Mixamo ou autre) est **en lecture seule**, impossible de le renommer directement :
1. Sélectionne n'importe quel clip existant (son contenu importe zéro, jamais rendu à l'écran).
2. **Ctrl+D** (Duplicate) — crée une copie standalone `.anim`, indépendante du FBX,
   **renommable** (contrairement au clip FBX source).
3. Renomme la copie **exactement** `PLACEHOLDER_Idle`. Répète 3× pour `Walk`/`Chase`/`Attack`.
4. Sélectionne chaque state dans le graphe → Inspector → champ **Motion** → assigne le
   placeholder correspondant. Piège vécu : glisser le clip FBX original dans le dossier ne crée
   PAS de copie (juste une référence au même asset read-only) — sans le vrai Ctrl+D, la
   `Motion` reste sur le nom d'origine (ex: `mixamo.com`) et le lookup par nom échoue.

Sur le prefab : assigne `idleClip`/`walkClip`/`chaseClip` sur le `MobData` (pas sur le
composant — voir [Animations locomotion](#animations-locomotion)), et pose les Animation Events
(`OnSkillHitFrame`) sur les clips de chaque `SkillData` du mob — exactement les mêmes règles que
côté joueur, voir `creation-skills.md`.

**Diagnostic Console** — deux warnings possibles à l'Awake :
- `Aucun Animator Controller assigné` → l'`Animator` du prefab n'a pas de Controller du tout —
  assigne `CombatEntityBase`.
- `Un ou plusieurs placeholders introuvables par nom` → le Controller est assigné mais un des 4
  states a un `Motion` vide ou pointant sur le mauvais clip (voir piège ci-dessus) — corrige le
  champ Motion du state concerné.

---

## Étape 4 — Lier MobData au prefab

Sur le composant `Mob` du prefab :

- `data` → glisser le `MobData` créé à l'Étape 1.
- `mobLevel` → niveau par défaut si le mob est placé directement en scène sans passer par
  `SpawnManager` (qui l'assigne lui-même avant `Awake()`). Vaut `1` par défaut.

## Étape 5 — Loot Table

`Assets > Create > AetherTree > Mob > LootTable`, puis glisse-la dans `MobData.lootTable`.

| Champ | Rôle |
|---|---|
| `entries` | Liste de `LootEntry` — glisser un SO d'item directement (`WeaponData`, `ArmorData`, `HelmetData`, `ResourceData`, `GemData`, `RuneData`...), `dropChance` [0..1], `minQuantity`/`maxQuantity`, `quantityBias` (0 = toujours min, 0.5 = uniforme, 1 = toujours max). Chaque entrée est tirée **indépendamment** des autres. |
| `aerisDropChance` / `minAeris` / `maxAeris` | Drop de monnaie. |
| `xpReward` | XP accordé aux joueurs éligibles (≥10% des dégâts totaux) à la mort du mob. |

## Étape 6 — Enregistrer dans une zone de spawn

Sur un `SpawnManager` de la scène, ajoute une entrée dans `zones` :

- `zoneName`, `spawnType` (`ClassicMob`/`MapBoss`/`DungeonMob`/`DungeonBoss`), `center`/`size`
  (zone de spawn en world space).
- `mobEntries` → liste de `{ mobData, weight }` (plusieurs `MobData` peuvent partager une zone,
  tirés au poids).
- `minLevel`/`maxLevel` → niveau tiré aléatoirement dans cette fourchette à chaque spawn.
- `mobCount`, `respawnDelay` (`bossRespawnDelay` pour un `spawnType` boss).

Un mob peut aussi être placé directement en scène sans `SpawnManager` (test rapide) — il gardera
alors `mobLevel = 1` sauf changement manuel dans l'Inspector.

---

## Exemple complet — "Loup Gris"

`MobData` — `mob_loup_gris.asset` :

| Champ | Valeur |
|---|---|
| `mobID` | `mob_loup_gris` |
| `mobName` | Loup Gris |
| `mobType` | Normal |
| `aiType` | Aggressive |
| `elementType` | Neutral |
| `baseHP` / `hpPerLevel` | 50 / 20 |
| `baseAtkMin`/`baseAtkMax` | 7 / 9 |
| `atkMinPerLevel`/`atkMaxPerLevel` | 3 / 5 |
| `baseDefMelee`/`baseDefRanged`/`baseDefMagic` | 7 / 9 / 6 |
| `weaponCategory` | Melee |
| `basicAttackSkill` | `skl_loup_morsure` (Damage, Normal, `range` 2.5, `cooldown` 1.5) |
| `skills` | `[skl_loup_bond]` (secondaire, cooldown plus long) |
| `moveSpeed` | 4 |
| `detectionRange` | 15 |
| `leashMultiplier` | 6 → leash = 90 unités |
| `patrolRadius` | 8 (déambule autour du spawn hors combat) |
| `idleClip`/`walkClip`/`chaseClip` | clips loup dédiés (rig quadrupède, propres au modèle) |
| `isNocturnal` | false |
| `lootTable` | `loot_loup` (peau de loup, un peu d'Aeris, xpReward 15) |
| `isCapturable` | false |

Prefab `Loup_Gris.prefab` :
- Modèle 3D (rig loup, quadrupède) + `Animator` en enfant, `Controller` = le
  `CombatEntityBase.controller` **partagé** (le même que tous les autres mobs/PNJ, y compris
  humanoïdes — voir [Étape 3](#étape-3--animator-controller-du-mob), le graphe ne dépend d'aucun
  rig).
- Composant `Mob` sur la racine → cascade `NavMeshAgent`/`StatusEffectSystem`/
  `CapsuleCollider`/`SkillSystem`/`CombatAIController`/`CombatEntityAnimatorController`
  (qui cascade lui-même `Animator`) automatique.
- `Mob.data` = `mob_loup_gris`.

Zone de spawn : `SpawnManager` → une `SpawnZone` "Forêt Nord", `mobEntries = [{mob_loup_gris,
weight 1}]`, `minLevel 1`/`maxLevel 5`, `mobCount 6`, `respawnDelay 30`.

---

## Vérification Play Mode

1. Compiler, 0 erreur.
2. Placer le prefab (ou attendre un spawn via `SpawnManager`) sur une zone couverte par le
   NavMesh baké.
3. Hors combat : le mob déambule dans `patrolRadius` autour de son spawn (ou reste immobile si
   `patrolRadius = 0`).
4. `Aggressive` : approcher à moins de `detectionRange` → le mob engage automatiquement.
   `Passive` : rester à portée sans l'attaquer → il ne doit PAS engager ; l'attaquer en premier →
   il riposte et le reste jusqu'à mort/leash.
5. Kiter le mob au-delà de `detectionRange × leashMultiplier` → il abandonne, rentre à
   `aggroPos` (point d'engagement, pas forcément le spawn d'origine), puis repasse en Patrol
   avec HP/Mana/cooldowns réinitialisés.
6. Mort → `MobKilledEvent` publié, loot/XP distribués aux joueurs ayant contribué ≥10% des
   dégâts totaux, mob détruit après 3s.

## Pièges fréquents / checklist rapide

- [ ] `basicAttackSkill` assigné — sinon fallback silencieux sur dégâts directs sans passer par
      le système de skill (pas d'anim, pas d'élément, pas de statusEffects).
- [ ] `baseAtkMax` ≥ `baseAtkMin` et `atkMaxPerLevel` ≥ `atkMinPerLevel` — auto-corrigé par
      `OnValidate` si besoin, mais vérifier que la valeur corrigée est bien celle voulue.
- [ ] NavMesh baké sur la zone de spawn — sinon le `NavMeshAgent` reste bloqué, aucune erreur
      Console explicite.
- [ ] `Animator.Controller` = le `CombatEntityBase.controller` **partagé** — jamais un Controller
      individuel par mob (ancien système, abandonné).
- [ ] Les 4 clips `PLACEHOLDER_Idle`/`Walk`/`Chase`/`Attack` du Controller partagé pas renommés
      — sinon `CombatEntityAnimatorController` ne les retrouve plus par nom, warning Console à
      l'Awake, aucune anim ne s'échange jamais (locomotion ET skills).
- [ ] `Apply Root Motion` décoché sur l'`Animator` — sinon le mob bouge deux fois (agent + anim),
      glisse/désync. **Cause confirmée du bug "rentre dans le sol entre deux attaques"** —
      corrigé en décochant sur tous les prefabs Mob/PNJ combattants.
- [ ] `idleClip`/`walkClip`/`chaseClip` remplis sur le `MobData` — sinon le mob affiche l'anim
      placeholder (faite pour un autre rig) au lieu de sa vraie locomotion.
- [ ] `leashMultiplier` trop petit (proche de 1) → le mob lâche sa cible dès qu'elle recule
      légèrement, comportement souvent perçu comme un bug de ciblage alors que c'est le réglage.
- [ ] `mobType = Elite/BossZone/BossDungeon/BossRaid` applique un multiplicateur de stats — ne
      pas aussi gonfler manuellement `baseHP`/`baseAtkMin`/`baseAtkMax` en plus, sauf si voulu.
