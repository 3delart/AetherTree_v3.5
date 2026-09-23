# Guide — Création d'un PNJ (dont Garde combattant)

> Référence pratique complète pour créer un `PNJData` + son prefab, dialogue-only ou combattant
> (`canFight`). Un exemple concret complet ("Garde du Village") est donné en fin de guide.
>
> Contexte technique de l'IA de combat (Patrol/Engage/Return, aggro, leash) :
> `docs/superpowers/specs/2026-09-13-combat-ai-controller-design.md`. Ce guide en est le résumé
> pratique. Pour créer un mob (ennemi), voir `creation-mob.md` — même IA de combat partagée
> (`CombatAIController`), données/contexte différents.

## Sommaire

- [Vue d'ensemble](#vue-densemble)
- [Étape 1 — Créer le PNJData](#étape-1--créer-le-pnjdata)
- [Étape 2 — Préparer le prefab](#étape-2--préparer-le-prefab)
- [Étape 3 — Animator Controller (PNJ combattant)](#étape-3--animator-controller-pnj-combattant)
- [Étape 4 — Dialogue](#étape-4--dialogue)
- [Exemple complet — "Garde du Village"](#exemple-complet--garde-du-village)
- [Vérification Play Mode](#vérification-play-mode)
- [Pièges fréquents / checklist rapide](#pièges-fréquents--checklist-rapide)

---

## Vue d'ensemble

Un PNJ = un `PNJData` (données) + un prefab portant le composant `PNJ`. Contrairement au Mob, un
PNJ peut être **purement dialogue** (marchand, forgeron, décoratif...) — dans ce cas, ni
`NavMeshAgent` ni `CombatAIController` ne sont créés. Dès que `PNJData.canFight = true`, le PNJ
devient aussi combattant : `PNJ.Awake()` ajoute alors **dynamiquement** un `CombatAIController`
(`AddComponent`, pas un `[RequireComponent]` sur la classe — pour ne jamais forcer un
`NavMeshAgent` sur un PNJ civil/décoratif) et délègue son IA de combat exactement comme un Mob,
via la même interface `ICombatAIProfile`.

`pnjType` détermine les onglets de fenêtre disponibles (Boutique, Craft, Fusion...) — voir
`PNJType` dans `Data/PNJ/PNJData.cs` pour la liste complète et le modèle composable
"Boutique + onglets" (§13.2 du GDD). `canFight` est **indépendant** de `pnjType` : un Garde, un
marchand itinérant, un PNJ de faction peuvent tous combattre de la même façon si `canFight =
true` — ce guide se concentre sur le cas `pnjType = Guard`, le plus courant pour un PNJ
combattant, mais s'applique identiquement à n'importe quel `pnjType`.

## Étape 1 — Créer le PNJData

`Assets > Create > AetherTree > PNJ > PNJData`. Convention de nom : `pnj_<nom>` (ex:
`pnj_garde_village`).

### Identité & Dialogue

| Champ | Rôle |
|---|---|
| `pnjID` | Clé technique stable (auto-remplie si vide). |
| `pnjName` | Nom affiché au joueur. |
| `pnjType` | Voir [Vue d'ensemble](#vue-densemble) — conditionne les champs "Boutique"/spécifiques qui apparaissent plus bas dans l'Inspector (`ShowIf`). |
| `defaultDialogue` | Dialogue par défaut, premier contact. |
| `knownPlayerDialogue` | Dialogue affiché si le joueur est déjà connu (voir `RegisterKnownPlayer`, persisté via `PlayerPrefs`). |
| `highReputationDialogue` + `reputationDialogueThreshold` | Dialogue premium si `player.worldReputationRank` ≥ seuil (0 = désactivé). |

Pour un PNJ purement dialogue (`Decorative`, `Merchant`...), c'est suffisant — passer directement
à la [création du prefab](#étape-2--préparer-le-prefab) sans toucher aux champs Combat.

### Mort & Respawn

| Champ | Rôle |
|---|---|
| `canDie` | `false` = invulnérable (civils, décoratifs). `true` = peut mourir et respawne. |
| `respawnDelay` | Secondes avant respawn (`0` = pas de respawn — reste mort). |

### Déplacement & Stats défensives

`baseMoveSpeed` (patrouille/déambulation) s'applique à **tous** les PNJ, combattants ou non.
`meleeDefense`/`rangedDefense`/`magicDefense`/`precision`/`dodge` aussi — même un PNJ non
combattant encaisse des coups avec ces valeurs si un mob attaque un village.

### Combat (canFight)

Cocher `canFight` fait apparaître :

| Champ | Rôle |
|---|---|
| `baseMaxHP` / `baseMaxMana` / `baseRegenHP` | Stats vitales du PNJ combattant. |
| `attackDamage` | Dégâts de base — utilisés seulement si `basicAttackSkill` ne calcule pas ses propres dégâts via ratios (cas rare, généralement ignoré au profit du skill). |
| `weaponCategory` | Détermine quelle défense de la cible s'applique. |
| `basicAttackSkill` | **Obligatoire** si `canFight`. `targetType = Target`, `effectType = Damage`. Son `cooldown` EST le délai entre deux attaques (pas de champ séparé). |
| `skills` | Skills secondaires, prioritaires sur l'attaque de base — même logique que `MobData.skills`. |
| `aggroRadius` | Rayon de détection des mobs (équivalent `MobData.detectionRange`). |
| `leashRadius` | Distance max depuis le spawn avant d'abandonner et rentrer. `0` = illimité. **Ancre fixe au spawn** (contrairement au Mob dont l'ancre bouge à chaque engagement) — un Garde reste fidèle à son poste. |
| `combatMoveSpeed` | Vitesse en mode combat. `0` = utilise `baseMoveSpeed`. |
| `patrolRadius` | Rayon de déambulation hors combat. `0` = reste immobile au spawn (comportement historique). |
| `aiType` | `Passive` (n'engage que si attaqué) / `Aggressive` (défaut — engage dès qu'un mob entre dans `aggroRadius`). **Ne jamais changer le défaut sans intention** — `Aggressive` préserve le comportement des `PNJData` existants. `Boss` non utilisé côté PNJ. |
| `idleClip`/`walkClip`/`chaseClip` | Anims locomotion (arrêt / Patrol / Engage) — mêmes clips que côté Mob, consommés par `CombatEntityAnimatorController`. Voir [Étape 3](#étape-3--animator-controller-pnj-combattant). |
| `critChance` / `critMultiplier` | Poussés sur `Entity` via les setters habituels. |

`OnValidate` avertit dans la Console si `canFight = true` sans `basicAttackSkill`, si
`canFight = true` avec `canDie = false` (un PNJ qui attaque mais est invulnérable — souvent
volontaire pour un Garde increvable, mais vérifie que c'est bien l'intention), ou si
`idleClip`/`walkClip`/`chaseClip` est incomplet (retombe sur l'anim placeholder du Controller
partagé, faite pour un autre rig).

### Effets On-Hit

`onHitReceivedEffects` s'applique à **tous** les PNJ (même non combattants — un civil peut avoir
Thorns). `onHitDealtEffects` n'a de sens que `canFight = true`.

---

## Étape 2 — Préparer le prefab

Ajoute le composant **`PNJ`** sur la racine du GameObject. `PNJ` a un seul `[RequireComponent]`
fixe : `SkillSystem` — présent sur **tout** PNJ, combattant ou non (nécessaire pour les effets
on-hit même hors combat). Assigne `PNJ.data` = le `PNJData` créé à l'Étape 1.

**Un PNJ non combattant (`canFight = false`) s'arrête là** — pas d'`Animator`, pas de
`NavMeshAgent`, rien d'autre à ajouter. Ne force JAMAIS ces composants "au cas où" sur un
marchand/décoratif — `CombatEntityAnimatorController` requiert un `Animator`
(`[RequireComponent]`), l'ajouter sans raison fait apparaître un `Animator` vide inutile
("Creating missing Animator component" en Console, vu en test manuel sur un PNJ Cuisinier).

**Si `data.canFight = true`** : `PNJ.Awake()` ajoute **dynamiquement** (`AddComponent`, jamais
via `[RequireComponent]` sur la classe) `CombatEntityAnimatorController` puis `CombatAIController`
— ce dernier requiert lui-même `NavMeshAgent`. Pose donc manuellement, sur le prefab, AVANT de
tester :
- Un `Animator` (avec le modèle/rig déjà en place) — `CombatEntityAnimatorController` en a
  besoin dès qu'il est ajouté, sinon Unity en crée un vide par défaut.
- Un `NavMeshAgent` réglé (Radius/Height/Speed cohérents avec le modèle) — sinon Unity en ajoute
  un par défaut à l'instant de l'`AddComponent<CombatAIController>()`, avec des réglages
  probablement pas adaptés.

⚠ La scène doit avoir un **NavMesh baké** couvrant la zone de patrouille/leash du PNJ combattant.

## Étape 3 — Animator Controller (PNJ combattant)

Identique au Mob — **même Controller partagé** (`CombatEntityBase.controller`, un seul asset
pour tout le projet), voir `creation-mob.md` § Animator Controller pour le détail complet du
graphe (états Idle/Walk/Chase/Attack, transitions, placeholders par nom). Le PNJ utilise
exactement le même mécanisme, `IsChasing` inclus (`PNJ.CurrentState`, calqué sur
`Mob.CurrentState`) — pas de différence de logique entre les deux côtés.

Assigne `idleClip`/`walkClip`/`chaseClip` sur le `PNJData` (pas sur le composant), puis pose les
Animation Events (`OnSkillHitFrame`) sur les clips des skills du PNJ — voir `creation-skills.md`.

Pour un PNJ purement dialogue sans combat, `CombatEntityAnimatorController` n'est **jamais**
ajouté (voir [Étape 2](#étape-2--préparer-le-prefab)) — aucun Animator/Controller nécessaire.

## Étape 4 — Dialogue

Crée un ou plusieurs `DialogueData` (stages + options), assigne-les dans les champs Dialogue de
l'Étape 1. Le contenu du dialogue et le système de stages/récompenses ne sont pas couverts par
ce guide (voir `DialogueData`/`DialogueStage` directement) — ce guide se concentre sur la
création du PNJ lui-même (données + prefab + combat).

Pour un PNJ combattant, `interactionRadius` (sur le composant `PNJ`) reste indépendant de
`aggroRadius`/`leashRadius` — c'est le rayon pour ouvrir le dialogue (touche E/clic), pas lié au
combat.

---

## Exemple complet — "Garde du Village"

`PNJData` — `pnj_garde_village.asset` :

| Champ | Valeur |
|---|---|
| `pnjID` | `pnj_garde_village` |
| `pnjName` | Garde du Village |
| `pnjType` | Guard |
| `defaultDialogue` | `dlg_garde_neutre` |
| `canDie` | true |
| `respawnDelay` | 90 |
| `baseMoveSpeed` | 2.5 |
| `meleeDefense`/`rangedDefense`/`magicDefense` | 12 / 8 / 6 |
| `canFight` | true |
| `baseMaxHP` | 300 |
| `baseMaxMana` | 0 (pas de skills à coût mana) |
| `attackDamage` | 15 (ignoré si `basicAttackSkill` calcule via ratios) |
| `weaponCategory` | Melee |
| `basicAttackSkill` | `skl_garde_coup_epee` (Damage, Normal, `range` 2.5, `cooldown` 1.2) |
| `skills` | `[skl_garde_bouclier]` (secondaire — ex: Buff Shield sur lui-même) |
| `aggroRadius` | 12 |
| `leashRadius` | 15 (ancre = position de spawn, fixe) |
| `combatMoveSpeed` | 4 |
| `patrolRadius` | 5 (fait sa ronde autour de son poste) |
| `aiType` | Aggressive (attaque tout mob qui entre dans `aggroRadius`) |
| `idleClip`/`walkClip`/`chaseClip` | clips Garde dédiés (rig humanoïde) |
| `critChance`/`critMultiplier` | 0.05 / 1.5 |

Prefab `Garde_Village.prefab` :
- Modèle 3D (rig humanoïde) + `Animator` en enfant, `Controller` = `CombatEntityBase.controller`
  **partagé** (le même que `Loup_Gris.prefab` malgré le rig différent — voir `creation-mob.md` §
  Animator Controller).
- Composant `PNJ` sur la racine → cascade `SkillSystem`/`CombatEntityAnimatorController`
  automatique. `NavMeshAgent` posé et réglé manuellement (sera repris par `CombatAIController`
  au démarrage puisque `canFight = true`).
- `PNJ.data` = `pnj_garde_village`, `interactionRadius` = 3.

---

## Vérification Play Mode

1. Compiler, 0 erreur — vérifier aucun `Debug.LogWarning` `[PNJData]` au chargement de la scène
   (canFight sans basicAttackSkill, ou canFight sans canDie si non voulu).
2. Interaction (E/clic) à moins de `interactionRadius` → dialogue s'ouvre.
3. Hors combat : le Garde fait sa ronde dans `patrolRadius` autour de son spawn (ou reste
   immobile si `patrolRadius = 0`).
4. `Aggressive` : un mob entre dans `aggroRadius` → le Garde engage automatiquement.
   `Passive` : le Garde n'engage que s'il est frappé en premier par le mob.
5. Kiter le combat au-delà de `leashRadius` (mesuré depuis le **spawn**, jamais depuis le point
   d'engagement contrairement au Mob) → le Garde abandonne et rentre à son poste. `leashRadius =
   0` = ne rentre jamais tant qu'une cible existe.
6. Si `canDie = true` : tuer le Garde → il respawne après `respawnDelay` secondes, à sa position
   de spawn, HP/Mana à 100%, cooldowns réinitialisés. **Pas** de reset HP/Mana instantané au
   retour en Patrol après un combat gagné (contrairement au Mob) — seule la mort/respawn
   restaure les HP, `baseRegenHP` fait le reste hors combat.

## Pièges fréquents / checklist rapide

- [ ] `basicAttackSkill` assigné dès que `canFight = true` — sinon avertissement Console au
      chargement et le Garde ne peut jamais attaquer.
- [ ] `aiType` laissé à `Aggressive` par défaut sur tout `PNJData` existant — le changer en
      `Passive` est une décision de gameplay explicite, pas un oubli à corriger.
- [ ] `leashRadius = 0` volontaire (illimité) vs oubli — un Garde avec `leashRadius = 0` peut
      suivre un mob indéfiniment loin de son poste.
- [ ] NavMesh baké sur la zone de patrouille/leash du Garde.
- [ ] `Animator.Controller` = le `CombatEntityBase.controller` **partagé**, ses 4 placeholders
      pas renommés, `idleClip`/`walkClip`/`chaseClip` remplis sur le `PNJData`, `Apply Root
      Motion` décoché — mêmes pièges que côté Mob (voir `creation-mob.md`).
- [ ] `canFight = true` + `canDie = false` : intentionnel (Garde increvable) ou oubli ? Averti
      en Console mais pas bloqué.
- [ ] Un PNJ non combattant n'a besoin d'aucun `NavMeshAgent` — ne pas en ajouter "au cas où",
      ça ne sert à rien tant que `canFight = false`.
