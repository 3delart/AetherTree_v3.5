# Système de déplacement des skills — Design

> Suite directe de la [simplification TargetType](2026-09-19-targettype-simplification-design.md)
> (2026-09-19), qui avait explicitement laissé le déplacement hors scope. Ce chantier couvre TOUT
> le déplacement induit par un skill : le caster qui se précipite ou se téléporte (Dash,
> Téléportation), et le caster qui déplace d'autres entités (Tirer/Regrouper, Repousser).

## Contexte

Aujourd'hui, `TargetType` a 8 valeurs actives dont 2 (`Dash_Target`/`Dash_Direction`) portent leur
propre logique de déplacement câblée en dur dans `SkillSystem.DashToTarget()`/`DashInDirection()`
— un caster qui fonce vers une cible ou une direction, inflige des dégâts au contact. C'est le
SEUL mécanisme de déplacement du jeu ; rien n'existe pour téléporter, tirer ou repousser une
entité.

Florian veut couvrir 4 verbes de déplacement : **Dash** (foncer), **Téléportation** (instantané),
**Tirer/Regrouper** (attirer une ou plusieurs cibles), **Repousser** (éloigner une ou plusieurs
cibles) — combinables avec les dégâts/buffs/debuffs normaux du skill (ex: "fonce dans le tas" =
Dash + dégâts AoE sur le trajet).

**Décision fondatrice** (validée avec Florian) : le déplacement ne peut PAS vivre dans
`effectType` (`Damage`/`Buff`/`Debuff`/`Other`) — c'est un champ à valeur unique, incompatible
avec "fonce dans le tas" qui a besoin de déplacement ET dégâts simultanément. Il vit dans un
**nouveau champ séparé et composable**, `displacementType`, exactement comme `isTrajectory`/
`hasDelayedImpact` se composent déjà avec `targetType` sans le remplacer.

**Deuxième décision** : une fois `displacementType` capable d'exprimer "foncer vers une cible" ou
"foncer dans une direction", `Dash_Target`/`Dash_Direction` deviennent redondants avec
`displacementType = DashSelf` + `targetType = Target`/`Cone`. Ils sont donc **absorbés et
supprimés** de `TargetType`, qui retombe à **6 valeurs pures** (`Target/Self/AoE_Self/
AoE_Target/GroundTarget/Cone`). Même mouvement de consolidation que le chantier précédent, cette
fois-ci complet.

## §1 — Nouveaux champs sur `SkillData`

```csharp
public enum DisplacementType
{
    None         = 0,   // défaut — aucun déplacement, comportement actuel inchangé
    DashSelf     = 1,   // le caster fonce (trajet animé, ~0.25-0.3s, dégâts aux entités croisées)
    TeleportSelf = 2,   // le caster se téléporte (instantané, pas de trajet)
    Pull         = 3,   // attire une/des cible(s) vers le caster ou un point
    Push         = 4,   // repousse une/des cible(s) loin d'une origine
}

public DisplacementType displacementType = DisplacementType.None;

// Distance de déplacement RÉELLE — distincte de `range` (qui gates la portée de CIBLAGE, jusqu'où
// tu peux cliquer/viser). displacementDistance plafonne jusqu'où tu voyages/pousses réellement
// dans cette direction, même si le point ciblé est plus loin (permet un dash "court" même en
// visant loin). Utilisée par DashSelf/TeleportSelf (Cone toujours, GroundTarget en plafond),
// Push (toujours).
public float displacementDistance = 5f;

// TeleportSelf + targetType=Target uniquement : true = atterrit derrière la cible, false =
// devant. Toujours à côté, jamais SUR la cible. Choix figé par skill (décision designer), pas
// calculé dynamiquement.
public bool teleportBehindTarget = true;

// TeleportSelf uniquement : les alliés dans aoeRadius de la position ORIGINE du caster sont
// téléportés au même décalage relatif près de la destination. Décoché = caster seul.
public bool bringsAllies = false;
```

`[ShowIf]` : `displacementDistance` masqué sauf pour les combos qui en ont réellement besoin —
`DashSelf`/`TeleportSelf` + `GroundTarget`/`Cone` (pas `Target`, qui utilise le `stopOffset`
existant, pas de plafond de distance), et `Push` sur TOUTES ses colonnes supportées (`Target`/
`GroundTarget`/`Cone`/`AoE_Self`, toujours). `Pull` ne l'utilise jamais (voir §3 — sa destination
est toujours un point déjà déterminé : la position du caster ou le point cliqué, jamais
plafonnée). `teleportBehindTarget`/`bringsAllies` masqués hors `DisplacementType.TeleportSelf`
(et `teleportBehindTarget` en plus hors `targetType = Target`).

`aoeFaction` (déjà existant, `Enemies`/`Allies`/`Everyone`) se réutilise tel quel pour la
sélection des cibles Pull/Push en zone — aucun nouveau champ de filtre nécessaire.

## §2 — Absorption de `Dash_Target`/`Dash_Direction`, `TargetType` → 6 valeurs

Ancienne représentation → nouvelle :
- `Dash_Target` (skill fonce vers une cible verrouillée) → `displacementType = DashSelf` +
  `targetType = Target`
- `Dash_Direction` (skill fonce dans une direction) → `displacementType = DashSelf` +
  `targetType = Cone` (déjà visé à la souris, réutilise `TargetingSystem.ResolveDirection()`)

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3, GroundTarget = 4, Cone = 5,
}
```

**Migration `.asset`** : vérifié — 1 seul asset utilise `Dash_Target` aujourd'hui
(`Content/Entity/Mobs/Wolf_test/skl_loup_special_test.asset`, ordinal 6), 0 utilisent
`Dash_Direction` (ordinal 7). Cet asset doit gagner `displacementType: 1` (DashSelf) en plus de
son `targetType` (qui reste `Target`, déjà au bon ordinal 0 — seul le retrait des 2 valeurs 6/7 de
l'enum compte comme suppression pure, sans renumérotation des survivants cette fois puisqu'ils
étaient déjà en tête de l'ancien enum).

## §2bis — Intégration au dispatch existant

Trouvé en re-relecture de la spec contre le code réel (`SkillSystem.cs`/`SkillBar.cs`/
`CombatAIController.cs`) — la spec ne disait nulle part OÙ `displacementType` se branche dans la
chaîne de dispatch déjà établie par le chantier TargetType. Trois décisions, dans l'ordre :

**1. Mutuellement exclusif avec `isTrajectory`/`hasDelayedImpact`.** `DashSelf` (le caster
physiquement se déplace) et `isTrajectory` (une hitbox virtuelle voyage) sont deux mécanismes de
"quelque chose se déplace" concurrents sur le même skill — aucun sens de les cumuler. Même
logique pour `hasDelayedImpact` (zone plantée) : rien dans la matrice §3 n'en a besoin. Un
`displacementType != None` avec `isTrajectory` OU `hasDelayedImpact` coché déclenche un warning
`OnValidate`, même idiome que les autres combos incompatibles déjà en place dans `SkillData.cs`
(`hasDelayedImpactSupportedTargetType`, etc.) — `[ShowIf]` masque `isTrajectory`/
`hasDelayedImpact` dès que `displacementType != None`.

**2. Priorité de dispatch — `displacementType` vérifié EN PREMIER.** Les 4 points d'entrée qui
implémentent déjà la chaîne `if (isTrajectory) ... else if (hasDelayedImpact) ... else Execute`
(`SkillBar.ResolveInstant()`, `SkillBar.ResolveChannel()`, `CombatAIController.
ResolvePendingHit()`, `CombatAIController.ResolveChannelCast()`) gagnent un nouveau premier
palier :

```csharp
if (skill.displacementType != DisplacementType.None)
    SkillSystem.Instance?.StartDisplacement(skill, caster, target);
else if (skill.isTrajectory)
    SkillSystem.Instance?.StartTrajectory(skill, caster, target);
else if (skill.hasDelayedImpact)
    SkillSystem.Instance?.PlantDelayedZone(skill, caster, target);
else
    SkillSystem.Instance?.Execute(skill, caster, target); // ou ResolveExecute selon le call site
```

**3. `DispatchByTargetType()` redevient pur — plus de special-case `Dash_Target`/
`Dash_Direction`.** Aujourd'hui son `switch` court-circuite `ExecuteOnTarget()` pour appeler
`DashToTarget()` sur `case TargetType.Dash_Target`. Ça casse structurellement une fois l'absorption
faite (§2) : `Target` sert maintenant À LA FOIS aux skills normaux ET aux skills `DashSelf` +
`Target`, le special-case ne peut plus se brancher sur `targetType` seul. Avec `displacementType`
vérifié EN AMONT (point 2 ci-dessus), `DispatchByTargetType()` n'est JAMAIS atteint pour un skill
de déplacement — les 2 cases `Dash_Target`/`Dash_Direction` sont supprimées du switch, qui
redevient un pur dispatcher "pas de déplacement", exactement son rôle avant que Dash n'y soit
greffé.

**Nouveau point d'entrée `StartDisplacement(SkillData, Entity caster, Entity target)`** (nom
proposé, le plan choisit le nom définitif) : joue le même rôle que `StartTrajectory()`/
`PlantDelayedZone()` — bypass complet de `DispatchByTargetType()`, sélection de cible/zone et
calcul de destination selon `targetType` (réutilise le filtre `PassesAoeFilter`/`OverlapSphere`
déjà en place dans `ExecuteAoESelf`/`ExecuteGroundTarget`, et le filtre angulaire déjà en place
dans `ExecuteCone` pour les colonnes `Cone` de Pull/Push), résistance équipement (§7) pour
Pull/Push, déplacement effectif (Lerp + `NavMesh.SamplePosition` + agent désactivé pour
DashSelf/Pull/Push, snap instantané pour TeleportSelf), puis `ApplyEffectType`/
`ApplyStatusEffects`/`CheckKill` au timing décrit en §4. Remplace `DashToTarget()`/
`DashInDirection()` (qui gèrent aujourd'hui exactement l'ancien couple `Dash_Target`/
`Dash_Direction`) plutôt que de les garder à côté — leur logique de Lerp/NavMesh/`agent.enabled`/
`Mob.IsDashing` est le point de départ, généralisée aux 4 verbes.

**Côté Mob/PNJ — même câblage, pas juste côté joueur.** Vérifié : les Mobs utilisent déjà
`Dash_Target` aujourd'hui (`DashToTarget()`/`DashInDirection()` ont des constantes de durée/offset
séparées `caster.entityType == EntityType.Player ? ... : ...`). Le point 2 ci-dessus modifie donc
`CombatAIController.ResolvePendingHit()`/`ResolveChannelCast()` EN PLUS de `SkillBar` — parité
totale avec comment `isTrajectory`/`hasDelayedImpact` ont déjà été câblés aux 4 points d'entrée
lors du chantier TargetType, pas une extension optionnelle.

## §3 — Matrice verbe × targetType

Chaque verbe ne supporte qu'un sous-ensemble de `targetType` (masqué via `[ShowIf]`, même idiome
que `stopAtFirstHit`) :

| Verbe | `Target` | `GroundTarget` | `Cone` | `AoE_Self` |
|---|---|---|---|---|
| **DashSelf** | Fonce jusqu'à la cible verrouillée, stoppe avant elle (comportement `DashToTarget` existant, inchangé). | Fonce vers le point cliqué, plafonné à `displacementDistance` si le point est plus loin. | Fonce dans la direction souris (`ResolveDirection()`), sur `displacementDistance`. | — |
| **TeleportSelf** | Téléportation instantanée devant/derrière la cible (`teleportBehindTarget`), jamais dessus — calculé par rapport au FACING de la CIBLE (`target.transform.forward`), pas du caster : "derrière" = du côté vers lequel la cible tourne le dos (le classique blink-backstab), "devant" = du côté qu'elle regarde. | Téléportation instantanée au point cliqué, plafonnée à `displacementDistance`. | Téléportation instantanée dans la direction souris, sur `displacementDistance` (blink). | — |
| **Pull** | Tire la cible verrouillée vers le caster (stoppe avant lui, même logique de `stopOffset` que Dash). | Sélectionne tout ce qui est dans `aoeRadius` du point cliqué (filtré par `aoeFaction`), tire TOUT vers ce point — c'est le "Regroupement". | Sélectionne tout dans l'éventail (angle `coneHalfAngle`, direction souris), tire tout vers le caster (même `stopOffset` que la colonne Target — jamais littéralement sur/dans le caster). | Sélectionne tout dans `aoeRadius` du caster, resserre tout vers le caster (même `stopOffset`, utile pour cluster une zone avant un gros AoE). | 
| **Push** | Repousse la cible verrouillée loin du caster, sur `displacementDistance`. | Sélectionne tout dans `aoeRadius` du point cliqué, repousse loin de CE POINT (effet "explosion sur place"), sur `displacementDistance`. | Repousse tout l'éventail loin du caster, sur `displacementDistance`. | Repousse tout ce qui est dans `aoeRadius` du caster, loin de lui, sur `displacementDistance`. |

Cases vides (`AoE_Target` pour tous les verbes, `AoE_Self` pour Dash/Teleport) : pas de sens
géométrique (on ne fonce/téléporte pas "sur soi-même" ; `AoE_Target` duplique `GroundTarget`
centré sur une entité plutôt qu'un point, jugé pas assez distinct pour être supporté dès cette
1ère passe) — non affichées côté Inspector, warning `OnValidate` si `displacementType` est actif
dessus quand même (même idiome que les combos non supportés déjà en place pour
`isTrajectory`/`hasDelayedImpact`).

## §4 — Timing des dégâts/effets

- **DashSelf** : au contact, comme aujourd'hui — sur la cible verrouillée à l'arrivée (`Target`),
  ou par entité traversée en route (`GroundTarget`/`Cone`, sweep par frame comme
  `DashInDirection` existant).
- **TeleportSelf** : à l'arrivée (instantané, donc "à l'arrivée" ≈ immédiatement après le snap) —
  le `targetType` est réévalué à la NOUVELLE position du caster (ex: `AoE_Self` téléporté au
  milieu d'un groupe frappe ce qui l'entoure après le saut, pas avant).
- **Pull** : au DÉPART (impact immédiat façon grappin — cohérent avec Push, même timing pour les
  deux verbes symétriques). La cible encaisse l'effet avant même d'avoir fini son trajet vers le
  caster.
- **Push** : au DÉPART (tu frappes, la cible s'envole ensuite).

## §5 — Interruption & résistance au CC

Deux questions distinctes, tranchées différemment :

**Le CASTER en plein déplacement** (`DashSelf`/`TeleportSelf`) : un CC dur (stun/shocked/freeze/
knockback/fear) qui atterrit PENDANT le trajet interrompt le déplacement à la position courante —
nouvelle garde, le Dash actuel ne le fait pas aujourd'hui (`DashToTarget`/`DashInDirection` ne
vérifient que `caster.isDead`). `TeleportSelf` étant instantané, cette garde ne s'applique
concrètement qu'à `DashSelf` (pas de fenêtre multi-frame à interrompre pour un snap).

**La CIBLE d'un Pull/Push — deux mécanismes distincts, pas à confondre** :
- Son ÉTAT de CC actuel (stun/fear/etc) ne bloque JAMAIS un Pull/Push — s'applique même sur une
  cible déjà stun. Cohérent narrativement (une cible stun est justement impuissante à résister)
  et évite une exception spéciale.
- Sa RÉSISTANCE ÉQUIPEMENT (chance %, voir §7) PEUT bloquer un Pull/Push — même mécanisme que la
  résistance aux debuffs classiques, roulé une fois à l'activation.

Seule une future immunité au déplacement forcé dédiée à un ARCHÉTYPE de mob (type boss raciné,
indépendant de tout équipement) reste hors scope — voir §8, à construire avec le système Donjons
plus tard si besoin.

## §6 — Validité de destination & mouvement des entités déplacées

- **NavMesh** : toute destination calculée (Dash/Teleport du caster, Push d'une cible) doit être
  clampée via `NavMesh.SamplePosition` avant d'être appliquée — même garde que
  `DashInDirection()` a déjà pour son point d'arrivée, étendue à tous les nouveaux cas. Empêche
  de téléporter/pousser quelqu'un dans un mur ou hors du mesh.
  - **Pull** n'a pas besoin de ce clamp : sa destination est soit la position du caster
    (forcément valide, il y est déjà), soit un point déjà validé par le raycast sol de
    `TryUseSlot()` au moment du clic `GroundTarget`.
- **Agent désactivé pendant le trajet** : toute entité en cours de déplacement forcé (caster en
  Dash, cible en Pull/Push) doit avoir son `NavMeshAgent` désactivé le temps du trajet et
  réactivé à l'arrivée — même pattern que `DashToTarget`/`DashInDirection` existants
  (`agent.enabled = false` / `true`).
- **IA suspendue pendant le déplacement forcé** : un Mob actuellement piloté par
  `CombatAIController` et en train d'être Pull/Push ne doit pas voir son IA se battre contre le
  déplacement forcé (retenter un pathing vers sa propre cible pendant qu'on le traîne) — réutilise
  ou étend le flag `Mob.IsDashing` déjà existant (posé par `DashToTarget`/`DashInDirection`) à un
  signal plus générique couvrant aussi Pull/Push. Détail d'implémentation, pas figé ici — la
  plan d'implémentation choisira le nom/la forme exacte.
- **Étalement Pull en zone** : quand un Pull en zone (`GroundTarget`/`Cone`/`AoE_Self`) ramène
  plusieurs entités sur le même point, chacune reçoit un léger offset (ex: position sur un petit
  cercle autour du point exact) pour éviter la superposition visuelle exacte de plusieurs
  modèles — coût de calcul négligeable.

## §7 — Résistance au déplacement forcé (Pull/Push) — équipement ET Mob/PNJ innée

Réutilise le système de résistance aux debuffs déjà fonctionnel (`DebuffResistanceEntry
{ debuffType, resistChance }` sur les équipements → agrégé par `CharacterStats.
ApplyDebuffResistances()` → stocké dans `StatusEffectSystem._debuffResistances` → roulé dans
`TryApplyDebuff()`), plutôt que de dupliquer toute cette plomberie.

**`DebuffType`** (`Data/StatusEffect/StatusEffectData.cs`) gagne UNE seule valeur en fin d'enum
(ordinal safety respecté, dernier ordinal actuel = `HpDrain = 21`) — Pull et Push partagent la
même résistance, pas de distinction par verbe (un équipement "anti-déplacement forcé" protège des
deux à la fois, pas la peine de gérer 2 entrées séparées côté designer) :

```csharp
Displacement = 22, // Résistance au déplacement forcé — Pull ET Push (SkillData.DisplacementType)
                    // partagent cette même clé, pas de distinction par verbe. Clé de résistance
                    // PURE, aucun DebuffData n'utilise jamais ce type, jamais appliqué comme un
                    // vrai debuff (pas de durée, pas de statusEffects.isXxx). Juste un point
                    // d'entrée dans le système de résistance équipement existant pour éviter de
                    // le dupliquer.
```

**Côté équipement** : aucun changement de code nécessaire — `DebuffResistanceEntry` accepte déjà
n'importe quel `DebuffType`, l'agrégation dans `CharacterStats` est déjà générique (boucle sur
toutes les entrées peu importe le type). Le designer ajoute juste une entrée
`{ debuffType: Displacement, resistChance: 0.1 }` sur un équipement comme pour n'importe quel
autre debuff.

**Côté `SkillSystem`** : au moment où un Pull/Push s'active sur une cible (voir §4, "au départ"),
rouler la résistance AVANT d'appliquer le déplacement — même pattern exact que
`StatusEffectSystem.TryApplyDebuff()` :

```csharp
float resistance = target.statusEffects.GetDebuffResistance(DebuffType.Displacement);
if (resistance > 0f && Random.value < resistance)
{
    // résisté — aucun déplacement, aucun dégât/effet associé (même traitement qu'un debuff résisté)
    return;
}
```

Un jet INDÉPENDANT par cible dans un Pull/Push en zone (pas un jet partagé pour tout le groupe) —
même principe que `RemoveDebuffsByChance`/`RemoveBuffsByChance` du système Dispel/Purify
(§CC/status-effect semantics, mémoire roadmap) : chaque cible résiste ou non indépendamment des
autres.

**Résistance INNÉE Mob/PNJ (demande Florian, trouvé en relisant §8 — comble un vrai trou de
symétrie, pas juste un nice-to-have)** : `CharacterStats.ApplyDebuffResistances()` est aujourd'hui
STRICTEMENT réservée au joueur (`private void ApplyDebuffResistances(Player player)`, lit
`player.equippedWeaponInstance`/etc) — un Mob n'a pas d'équipement, donc `Mob`/`PNJ` n'appellent
JAMAIS cette méthode. Concrètement : `StatusEffectSystem._debuffResistances` reste TOUJOURS vide
pour un Mob/PNJ aujourd'hui, `GetDebuffResistance()` y retourne toujours `0`, quel que soit le
`DebuffType`. Sans correction, un Mob/PNJ ne peut donc résister à AUCUN debuff (Stun/Fear/Silence/
etc — pas juste `Displacement`), et surtout ne peut jamais résister à un Pull/Push lancé par un
Player — la résistance §7 ne protégerait que le sens "Player poussé/tiré par un Mob", jamais
l'inverse.

**Fix** : `MobData`/`PNJData` gagnent un nouveau champ, même type que l'équipement, même endroit
que les champs `onHitReceivedEffects`/`onHitDealtEffects` déjà présents sur les deux (`Data/Mobs/
MobData.cs:150-152`, `Data/PNJ/PNJData.cs:202-205`) :

```csharp
[Tooltip("Résistances aux debuffs INNÉES de cette espèce/ce PNJ — indépendantes de tout " +
         "équipement (les Mobs/PNJ n'en portent pas). Même mécanisme que la résistance " +
         "équipement du joueur : resistChance = 1 sur Displacement/Stun/etc = immunité totale " +
         "(ex: boss raciné, immunisé au CC dur).")]
public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();
```

Câblé au même endroit que le reste des stats dérivées de `MobData` — `Mob.ApplyData()`
(`Entities/Mob.cs`, appelée depuis `Awake()`, juste après les autres `SetXxx()`) et l'équivalent
côté `PNJ.Awake()` — via une simple boucle `foreach (var entry in data.debuffResistances)
statusEffects.SetDebuffResistance(entry.debuffType, entry.resistChance);`. Aucun changement à
`TryApplyDebuff()`/`GetDebuffResistance()` : ils lisent déjà le même dictionnaire, peu importe qui
l'a rempli (équipement joueur ou données Mob/PNJ).

**Conséquence directe** : ceci RÉSOUT le point "Immunité boss" qui était en §8 (hors scope) —
`resistChance: 1.0` sur `Displacement` (et n'importe quel autre `DebuffType`, Stun/Fear/etc) dans
le `MobData` d'un boss = immunité GARANTIE (`Random.value < 1.0` est toujours vrai), sans
construire de système d'immunité séparé. Retiré du hors-scope, voir §8 mis à jour.

## §8 — Hors scope (explicitement)

- ~~Immunité CC/déplacement forcé pour les boss~~ — **résolu par §7** (résistance innée Mob/PNJ,
  `resistChance: 1.0` = immunité garantie). Reste un point d'attention pour le PLAN, pas un vrai
  "hors scope" : aucun système de ce type n'existe encore dans le code (les Donjons/boss, roadmap
  item 8, ne sont pas bâtis), donc aucun `MobData` de boss n'existe encore pour le configurer —
  mais le champ est prêt dès ce chantier-ci.
  Prévu pour plus tard, pas ce chantier.
- **Swap de position** (caster et cible échangent leurs places) — pas mentionné par Florian dans
  ce chantier, pas dans la matrice §3. Pourrait se brancher plus tard comme variante de
  `TeleportSelf` si besoin, sans casser l'architecture actuelle.
- **Direction de Push arbitraire** (pousser dans une direction fixe indépendante du caster/point)
  — la matrice §3 couvre "loin du caster" et "loin du point cliqué" uniquement, jugé suffisant
  pour la 1ère passe.

## Vérification

Pas de framework de test automatisé (comme tout le reste du projet) — vérification par
compilation + grep + Play Mode manuel par Florian, même méthode que le chantier TargetType. Points
clés à couvrir dans le plan d'implémentation :
1. Les 4 verbes × leurs colonnes supportées dans la matrice §3 (16 combinaisons à couvrir,
   4 cases vides par design).
2. Migration de `skl_loup_special_test.asset` (Dash_Target → `targetType=Target` +
   `displacementType=DashSelf`).
3. CC interrompt bien un DashSelf en cours (caster) ; ne bloque PAS un Pull/Push sur la cible.
4. NavMesh clamp sur les 3 destinations concernées (Dash/Teleport caster, Push cible).
5. `bringsAllies` embarque bien les alliés proches à la téléportation, avec le bon décalage
   relatif.
6. Étalement visuel d'un Pull en zone sur plusieurs cibles (pas de stack exact).
7. Résistance équipement (§7) : une cible avec une entrée `DebuffResistanceEntry` sur
   `Displacement` résiste bien un Pull ET un Push avec la bonne fréquence statistique ; un
   Pull/Push en zone roule indépendamment par cible (pas un seul jet partagé).
8. Côté Mob/PNJ (§2bis) : un Mob avec un skill `displacementType` configuré (Dash/Teleport/Pull/
   Push) l'exécute correctement via `CombatAIController`, pas juste côté joueur — au minimum
   confirmer que `skl_loup_special_test.asset` (le loup migré) fonctionne toujours en jeu après
   la migration.
9. Combo interdit `displacementType` + `isTrajectory`/`hasDelayedImpact` : configurer volontairement
   les deux sur un skill de test, vérifier que le warning `OnValidate` apparaît bien dans la
   Console.
10. Résistance innée Mob/PNJ (§7) : configurer `debuffResistances = [{ Displacement, 1.0 }]` sur
    un `MobData` de test, lui lancer un Pull/Push depuis le joueur → aucun déplacement ne doit
    avoir lieu. Vérifier aussi qu'un Mob SANS entrée `debuffResistances` se fait bien Pull/Push
    normalement (pas de régression — liste vide = aucune résistance, comportement identique à
    avant ce chantier).
