# Guide — Création d'un SkillData

> Référence pratique complète pour créer et configurer un `SkillData` — tous les champs, tous
> les types d'exécution (Normal/MultiHit/Combo/Canalisation), la synchronisation Animation
> Event (chantier B, 2026-09-10), et la compatibilité/variantes d'arme. À consulter à chaque
> nouveau skill créé.
>
> Contexte technique complet : `docs/superpowers/specs/2026-09-09-cast-canalisation-design.md`
> (Canalisation) et `docs/superpowers/specs/2026-09-10-hit-frame-sync-design.md` (Normal/
> MultiHit/Combo). Ce guide en est le résumé pratique, côté création de contenu.

## Sommaire

- [Champs de base](#champs-de-base)
- [Exemples concrets par type](#exemples-concrets-par-type)
- [Compatibilité arme & variantes](#compatibilité-arme--variantes)
- [Partage de clip entre skills](#partage-de-clip-entre-skills--seulement-si-le-timing-devent-est-identique)
- [Normal](#normal-executiontype--normal-casttime--0)
- [MultiHit](#multihit-executiontype--multihit-casttime--0)
- [Combo (ComboSequence)](#combo-comboSequence-executiontype--combosequence-casttime--0)
- [Canalisation](#canalisation-casttime--0)
- [Setup Animator Controller (une fois pour le projet)](#setup-animator-controller-une-fois-pour-le-projet)
- [Poser un Animation Event sur un clip FBX/Mixamo](#poser-un-animation-event-sur-un-clip-fbxmixamo)
- [Pièges fréquents / checklist rapide](#pièges-fréquents--checklist-rapide)

---

## Champs de base

> Numérotation alignée sur les headers Inspector réels de `SkillData.cs` (① à ⑩) — si tu vois un
> autre numéro dans l'Inspector qu'ici, le fichier a bougé, se fier à l'Inspector.

### ① Identité

| Champ | Rôle |
|---|---|
| `skillID` | Clé technique stable (auto-remplie avec le nom de l'asset si vide). Ne jamais afficher au joueur, ne jamais changer après coup. |
| `skillName` / `description` | Textes affichés au joueur (fr/en). |
| `tags` | Liste de `SkillTag` (Stun, DoT, Mobilite, Combo…) — informatif, utilisé pour le filtrage `SkillLibraryUI` et par `UnlockManager`. |
| `skillType` | `BasicAttack` (slot 0 uniquement) / `Active` (slots 1-8) / `Ultimate` (slot 9). |

### ③ Effet principal (+ Effet spécial, Dégâts physiques, Éléments)

| Champ | Rôle |
|---|---|
| `effectType` | `Damage` (dégâts phys+élem) / `Buff` (StatusEffects uniquement) / `Debuff` (StatusEffects uniquement) / `Other` (drain, téléport, invocation, dash… voir `specialEffect`). Conditionne quels champs suivants s'affichent (ShowIf). |
| `cooldown` | En secondes. Posé au moment de la **résolution**, pas du clic (voir sections par type plus bas — sauf exception Combo/steps intermédiaires). |
| `castTime` | 0 = instantané (Normal/MultiHit/Combo) ; > 0 = Canalisation. Voir section dédiée. |
| `specialEffect` | Actif seulement si `effectType = Other` — DrainHP/DrainMana/Summon/Interrupt uniquement (le déplacement — Pull/Push/SwapPosition/Téléportation/Dash — vit maintenant dans `displacementType`, ⑥bis ci-dessous, composable avec N'IMPORTE QUEL `effectType`). Champs additionnels (`drainHealRatio`, `summonMobData`…) apparaissent selon la valeur choisie. |

**Dégâts physiques** (actif si `effectType = Damage` ou `Other`) :

| Champ | Rôle |
|---|---|
| `damageMultiplier` | Multiplicateur global (1.0 = normal, 2.0 = double dégâts). |
| `damageMeleeRatio` / `damageRangedRatio` / `damageMagicRatio` | Part des dégâts réduite par chaque type de défense de la cible — **la somme doit faire 1.0**. Ex: skill mêlée pur → Melee 1.0/0/0 ; hybride → 0.7/0/0.3. |

**Éléments** (actif si `effectType = Damage` ou `Other`) :

`elements` (liste — vide = skill neutre, 1 = élémentaire simple, 2+ = combo élémentaire) + `elementalMultiplier` (0 à 5, ignoré si `elements` est vide).

### ④ Exécution avancée

`executionType` (Normal/MultiHit/ComboSequence — voir sections dédiées plus bas), `hitSteps` (MultiHit uniquement), `comboSteps`/`comboWindowDuration`/`comboStepInterval` (Combo uniquement). Disponible pour les **3 `skillType`** (`BasicAttack`/`Active`/`Ultimate` — ils ne diffèrent que par leur slot SkillBar, aucune restriction d'exécution entre eux) si `effectType = Damage`/`Other` — n'a pas de sens pour un Buff/Debuff pur.

### ⑤ Coût

`manaCost` / `hpCost` / `goldCost` — dépensés au **lancement** (pas à la résolution).

### ⑥ Ciblage & Portée

`aoeFaction` (Enemies/Allies/Everyone — qui peut être touché) s'applique à tous les types ci-dessous sauf `Self`. `projectileSpeed` ne sert qu'à la trajectoire (⑦, vitesse de déplacement du point/cône) — le visuel du projectile en mouvement, lui, c'est `vfxTrajectory` (⑨), pas un champ séparé (`projectilePrefab` a été supprimé, mort/jamais lu).

**`targetType` — les 6 possibilités, aucune n'est redondante malgré des géométries qui se
ressemblent. Le déplacement (Dash/Téléportation/Pull/Push/Swap, ex-`Dash_Target`/
`Dash_Direction`) est maintenant un modificateur composable (`displacementType`, ⑥bis) plutôt
qu'un `targetType` séparé :**

| `targetType` | Cible verrouillée ? | Portée / zone | Comportement | Cas d'usage typique |
|---|---|---|---|---|
| `Target` | **Oui** | `range` = distance max au clic | Touche la cible verrouillée. + `isTrajectory` : perce tout jusqu'à elle (position figée au lancement, pas de homing) — décoche `stopAtFirstHit` pour percer, coche-le pour s'arrêter au 1er ennemi touché (ex: lance qui transperce vs flèche qui s'arrête). | Sort mono-cible, ou lance qui transperce jusqu'à une cible verrouillée. |
| `Self` | Non | — | Touche uniquement le caster. | Buff/heal/shield sur soi. |
| `AoE_Self` | Non | `aoeRadius`, centré sur le **caster** | Sphère autour de toi, touche tout ce qui passe `aoeFaction`. | Explosion/aura centrée sur soi. |
| `AoE_Target` | **Oui** | `aoeRadius`, centré sur la **cible** | Sphère autour de la cible verrouillée (pas autour de toi). | Impact qui éclabousse autour d'un ennemi. |
| `GroundTarget` | Non (point au sol, visé à la souris) | `aoeRadius`, centré sur le point cliqué | Sphère à un point choisi au sol. + `isTrajectory` : voyage vers ce point (mur/vague qui avance vers où tu cliques) — `stopAtFirstHit` disponible pareil que `Target`. | Météore, zone posée au sol, ou mur de feu qui voyage vers la souris. |
| `Cone` | Non, **visé à la souris** | `range` (portée), `coneHalfAngle` (demi-angle en degrés) | Éventail devant le caster, orienté vers la souris (priorité à la cible engagée/sélectionnée si il y en a une) — touche tout ce qui est dans l'angle ET la portée. | Souffle, attaque en arc dirigée par le joueur. |

`Target`/`GroundTarget`/`AoE_Target`/`Cone` peuvent en plus
devenir des trajectoires mobiles (hitbox qui voyage/s'élargit) via `isTrajectory` — voir ⑦.

### ⑥bis Déplacement

`displacementType` — composable avec N'IMPORTE QUEL `effectType` (Damage/Buff/Debuff/Other),
contrairement à l'ancien `specialEffect` qui n'existait que si `effectType = Other` (impossible
d'avoir un skill "dash + dégâts"). **Mutuellement exclusif avec `isTrajectory`/`hasDelayedImpact`**
(warning Console si les deux sont cochés — deux mécanismes de "quelque chose se déplace"
concurrents).

| `displacementType` | `Target` | `GroundTarget` | `Cone` | `AoE_Self` |
|---|---|---|---|---|
| `DashSelf` | Fonce jusqu'à la cible verrouillée, stoppe avant elle. | Fonce vers le point cliqué, plafonné à `displacementDistance` si le point est plus loin. | Fonce dans la direction souris, sur `displacementDistance`. | — |
| `TeleportSelf` | Téléportation instantanée devant/derrière la cible (`teleportBehindTarget`, calculé par rapport au FACING de la cible), jamais dessus. | Téléportation instantanée au point cliqué, plafonnée à `displacementDistance`. | Téléportation instantanée dans la direction souris, sur `displacementDistance` (blink). | — |
| `Pull` | Tire la cible verrouillée vers le caster. | Sélectionne tout dans `aoeRadius` du point cliqué (filtré `aoeFaction`), tire TOUT vers ce point (Regroupement). | Sélectionne tout dans l'éventail (direction souris), tire tout vers le caster. | Sélectionne tout dans `aoeRadius` du caster, resserre tout vers le caster. |
| `Push` | Repousse la cible loin du caster, sur `displacementDistance`. | Sélectionne tout dans `aoeRadius` du point cliqué, repousse loin de CE POINT (explosion sur place), sur `displacementDistance`. | Repousse l'éventail loin du caster, sur `displacementDistance`. | Repousse tout autour de soi, loin du caster, sur `displacementDistance`. |
| `SwapPosition` | Caster et cible échangent leurs places, instantanément. | — | — | — |

Cases vides : pas de sens géométrique (on ne fonce/téléporte pas "sur soi-même" ; `SwapPosition`
a besoin d'exactement une entité, aucun sens en zone). Warning Console si configuré quand même,
pas de correction automatique.

**Timing des dégâts/effets** : `DashSelf` au contact (à l'arrivée pour `Target`, en route pour
`GroundTarget`/`Cone`) · `TeleportSelf`/`SwapPosition` à l'arrivée/l'échange (instantané,
`targetType` réévalué à la NOUVELLE position) · `Pull`/`Push` AU DÉPART, avant le trajet animé.

**Champs additionnels** : `displacementDistance` (`DashSelf`/`TeleportSelf` + `GroundTarget`/
`Cone`, `Push` toujours) · `displacementDuration` (`DashSelf`/`Pull`/`Push` — durée du trajet
animé en secondes, défaut 0.3 ; `TeleportSelf`/`SwapPosition` sont instantanés, pas concernés ;
plus petit = plus rapide/nerveux, plus grand = plus lent/lourd) · `teleportBehindTarget`
(`TeleportSelf` + `Target` uniquement, coché = derrière) · `bringsAllies` (`TeleportSelf`
uniquement, emmène les alliés proches).

**Interruption & résistance** : un CC dur (Stun/Shocked/Freeze/Knockback/Fear) qui atterrit
PENDANT un `DashSelf` en cours l'interrompt à la position courante — `TeleportSelf`/
`SwapPosition` sont instantanés, pas de fenêtre à interrompre. L'ÉTAT de CC actuel de la cible
d'un `Pull`/`Push`/`SwapPosition` ne bloque JAMAIS le déplacement (une cible stun ne peut pas
résister) — mais sa RÉSISTANCE ÉQUIPEMENT/INNÉE (`DebuffType.Displacement`, même mécanisme que
les autres debuffs, roulé indépendamment par cible en zone) le peut. Un `MobData`/`PNJData` avec
`debuffResistances = [{ Displacement, 1.0 }]` est immunisé (ex: boss raciné) — voir
`onHitReceivedEffects`/`onHitDealtEffects` pour le même genre de champ sur ces deux SO.

### ⑦ Zone à impact différé / Trajectoire mobile

Deux modes de résolution, tous deux optionnels :

| Champ | Rôle |
|---|---|
| `hasDelayedImpact` | Zone plantée au sol/sur une entité, dégâts différés de `impactDelay` secondes. `Self`/`AoE_Self`/`Target`/`GroundTarget`/`AoE_Target`/`Cone`. |
| `isTrajectory` | Hitbox mobile qui voyage/s'élargit et touche tout sur son passage (perce, sauf `stopAtFirstHit` coché). `GroundTarget`/`Target`/`AoE_Target`/`Cone`. |
| `stopAtFirstHit` | `isTrajectory` + `Target`/`GroundTarget` uniquement. Coché = le trajet s'arrête au 1er ennemi touché (comme une flèche qui se plante). Décoché (défaut) = perce tout (comme un rayon/une lance). |
| `zoneFollowsAnchor` | `hasDelayedImpact` + `Self`/`AoE_Self`/`Target`/`AoE_Target` uniquement (jamais `GroundTarget`, pas d'entité à suivre). Coché = la zone recalcule sa position à chaque tick sur le CASTER (Self/AoE_Self) ou la CIBLE (Target/AoE_Target) — ex: tourbillon qui te suit, corbeaux qui suivent une cible marquée. Décoché (défaut) = zone figée au point de lancement — ex: fiole de poison posée au sol. |

**Mutuellement exclusifs, SAUF `Cone`, `GroundTarget` et `Target`** — ces trois-là autorisent les
deux cochés en même temps : le dégât immédiat du sweep/de l'expansion s'applique normalement, ET
une zone classique (`aoeRadius` en Sphere) se plante en plus là où le trajet s'est terminé (le
point d'arrêt réel si `stopAtFirstHit` a coupé court, pas la destination d'origine), détonant
après `impactDelay` — **double dégât voulu** si une cible reste dans le rayon final. Pour tous
les autres `targetType`, toujours incompatibles (warning Console si les deux sont cochés
ensemble).

Exemple concret (GroundTarget) : un mur de feu qui voyage du caster jusqu'au point cliqué à la
souris (`isTrajectory`, `trajectoryShape = Box` pour un vrai mur large — voir plus bas), brûlant
tout sur son passage, PUIS laisse une zone de flammes au sol à l'endroit cliqué qui détone après
`impactDelay` (`hasDelayedImpact`).

**Forme de la hitbox** (`isTrajectory` uniquement, ignoré pour `Cone`) :

| Champ | Rôle |
|---|---|
| `trajectoryShape` | `Sphere` (défaut) — ronde, largeur = profondeur = `aoeRadius`. `Box` — rectangulaire, largeur/profondeur indépendantes, hauteur fixe (pas de champ — inutile au combat, entités ~au même niveau au sol). |
| `trajectoryWidth` / `trajectoryDepth` | Box uniquement. `trajectoryWidth` = largeur (perpendiculaire au trajet). `trajectoryDepth` = épaisseur avant/arrière — règle selon le VFX (mince type lame de vent vs épais type bloc de pierre). Ex : flèche fine → `Sphere` + `aoeRadius` petit (0.2-0.3). Mur de feu large et fin → `Box` + `trajectoryWidth` 6-8, `trajectoryDepth` 0.5-1. |

En `Box`, `aoeRadius` ne sert plus à rien (remplacé par `trajectoryWidth`/`trajectoryDepth`).

### ⑧ Effets secondaires

`statusEffects` — liste de `BuffData`/`DebuffData` + % de chance, déclenchés à l'usage. Un skill `Buff`/`Debuff` pur n'a QUE ça comme effet (pas de dégâts).

### ⑨ Visuel

`icon`, `vfxCast`, `vfxTrajectory` (`isTrajectory` uniquement), `vfxZoneMarker` (`hasDelayedImpact` uniquement), `vfxImpact`, et les animations :

`attackAnimation` vs `channelAnimation` — **mutuellement exclusifs, jamais les deux sur le même skill** :

- `attackAnimation` → tout skill `castTime = 0` (Normal, MultiHit, chaque step de Combo).
- `channelAnimation` → uniquement un skill `castTime > 0` (Canalisation).

Détails de synchronisation (Animation Event, timing, verrous) : voir les sections par type
ci-dessous.

### ⑩ Son

`soundEffect` (ou celui du coup de base pour un MultiHit — chaque `HitStep` peut avoir le sien, sinon hérite de celui-ci).

---

## Exemples concrets par type

Un exemple complet de champs pour chaque possibilité — à copier/adapter plutôt que de repartir
de zéro. Chaque exemple ne liste que les champs qui comptent pour ce type ; le reste garde ses
valeurs par défaut ou suit [Champs de base](#champs-de-base).

### Direct (`effectType = Damage`, `executionType = Normal`, `castTime = 0`)

Le cas le plus simple — un coup, une résolution. Exemple : `skl_epee_frappe1`.

| Champ | Valeur |
|---|---|
| `skillType` | Active |
| `effectType` | Damage |
| `executionType` | Normal |
| `castTime` | 0 |
| `cooldown` | 4 |
| `damageMultiplier` | 1.2 |
| `damageMeleeRatio`/`Ranged`/`Magic` | 1.0 / 0 / 0 |
| `manaCost` | 8 |
| `targetType` | Target |
| `range` | 2.5 |
| `elements` | vide (Neutre) |
| `statusEffects` | vide |
| `attackAnimation` | `epee_frappe1.anim` |

Setup : 1 Animation Event `OnSkillHitFrame` (`Int = 0`) à la frame d'impact — voir
[Normal](#normal-executiontype--normal-casttime--0).

### MultiHit (`executionType = MultiHit`)

Une activation, plusieurs coups. Exemple : `skl_dague_rafale` — 1 coup de base + 2 `hitSteps`.

| Champ | Valeur |
|---|---|
| `effectType` | Damage |
| `executionType` | MultiHit |
| `cooldown` | 6 |
| `damageMultiplier` (coup de base) | 0.6 |
| `hitSteps[0]` | `damageMultiplier` 0.6, `element` Neutral, `delay` 0.2 |
| `hitSteps[1]` | `damageMultiplier` 0.8, `element` Neutral, `delay` 0.2 |
| `attackAnimation` | `dague_rafale.anim` |

Setup : **3 Animation Events** sur `dague_rafale.anim` — `hitIndex 0` (coup de base), `hitIndex
1` (`hitSteps[0]`), `hitIndex 2` (`hitSteps[1]`), chacun à sa frame d'impact réelle. Voir
[MultiHit](#multihit-executiontype--multihit-casttime--0).

### Combo (`executionType = ComboSequence`)

N appuis successifs, N `SkillData` séparés. Exemple : combo épée 3 coups.

- **Parent** `skl_epee_combo` : `executionType = ComboSequence`, `comboSteps = [skl_epee_combo_2,
  skl_epee_combo_3]`, `comboWindowDuration = 1.5`, `comboStepInterval = 0.3`, `cooldown = 8`
  (posé une seule fois, à la fin du combo complet — les steps intermédiaires n'ont pas de
  cooldown effectif). `damageMultiplier = 1.0`, `attackAnimation = epee_combo1.anim` + event
  hitIndex 0.
- **Step 2** `skl_epee_combo_2` (asset séparé) : `damageMultiplier = 1.1`, sa propre
  `attackAnimation = epee_combo2.anim` + event hitIndex 0.
- **Step 3** `skl_epee_combo_3` (asset séparé, coup final plus fort) : `damageMultiplier = 1.5`,
  `attackAnimation = epee_combo3.anim` + event hitIndex 0, peut porter un `statusEffects` (ex:
  30% `dbf_stun_leger`) pour un finisher qui étourdit.

Voir [Combo](#combo-comboSequence-executiontype--combosequence-casttime--0).

### Canalisation (`castTime > 0`)

Sort à temps de cast. Exemple : `skl_boule_de_feu`.

| Champ | Valeur |
|---|---|
| `effectType` | Damage |
| `executionType` | Normal (obligatoire) |
| `castTime` | 1.5 |
| `cooldown` | 10 |
| `manaCost` | 25 |
| `targetType` | GroundTarget |
| `aoeRadius` | 3 |
| `elements` | `[Fire]` |
| `elementalMultiplier` | 2.0 |
| `channelAnimation` | `mage_cast_fire.anim` (**pas** `attackAnimation`) |
| `vfxCast` | glow aux mains |
| `vfxImpact` | explosion de feu |

Setup : aucun Animation Event — résolution automatique à la fin des `castTime` secondes. Voir
[Canalisation](#canalisation-casttime--0).

### Buff (`effectType = Buff`)

Aucun dégât — uniquement des `statusEffects`. Exemple : `skl_cri_de_guerre` (buff sur soi).

| Champ | Valeur |
|---|---|
| `effectType` | Buff |
| `executionType` | n'apparaît pas (masqué — pas de sens pour un Buff pur) |
| `castTime` | 0 |
| `cooldown` | 20 |
| `targetType` | Self |
| `manaCost` | 15 |
| `statusEffects` | `[{ effect: buff_haste, chance: 1.0 }]` |
| `attackAnimation` | `cri_de_guerre.anim` (optionnel — vide si effet purement passif sans anim dédiée) |

`buff_haste` (`BuffData`) : `buffType = Stats`, `buffStatType = MoveSpeed`, `buffModifier =
Percent`, `buffStatValue = 0.30`, `duration = 8`.

### Debuff (`effectType = Debuff`)

Aucun dégât direct — uniquement des `statusEffects` sur la cible. Exemple : `skl_cri_effroi`.

| Champ | Valeur |
|---|---|
| `effectType` | Debuff |
| `targetType` | AoE_Target |
| `aoeRadius` | 4 |
| `aoeFaction` | Enemies |
| `cooldown` | 15 |
| `statusEffects` | `[{ effect: dbf_fear, chance: 1.0 }]` |

`dbf_fear` (`DebuffData`) : `debuffType = Fear`, `duration = 3`.

Un debuff **DoT** (dégâts sur la durée) se construit pareil, avec `dbf_poison_lame`
(`DebuffData`) : `debuffType = Dot`, `damageElement = Nature`, `baseDamagePercent = 0.01`,
`rankDamagePercent = 0.2`, `elementalPointsMultiplier = 0.10`, `duration = 5` — attaché en
`statusEffects` sur n'importe quel skill `Damage` (le DoT s'ajoute en plus des dégâts directs du
coup), pas besoin d'un skill `effectType = Debuff` dédié pour ça.

### Déplacement (`displacementType`)

Composable avec N'IMPORTE QUEL `effectType` — voir ⑥bis pour la matrice complète.
Exemple : `skl_grappin` (tire la cible vers le caster, aucun dégât).

| Champ | Valeur |
|---|---|
| `effectType` | Other (ou Damage si le grappin doit aussi faire mal — les deux marchent) |
| `displacementType` | Pull |
| `targetType` | Target |
| `range` | 8 |
| `damageMultiplier` | 0 (pas de dégâts, juste le déplacement) |
| `cooldown` | 12 |

Exemple "fonce dans le tas" (Dash + dégâts AoE sur le trajet) :

| Champ | Valeur |
|---|---|
| `effectType` | Damage |
| `displacementType` | DashSelf |
| `targetType` | Cone (fonce dans la direction souris) |
| `displacementDistance` | 8 |
| `aoeRadius` | 1.5 (largeur du sweep pendant le trajet) |
| `damageMultiplier` | 1.2 |

### Effet spécial (`effectType = Other`)

Ni dégâts classiques ni statusEffect pur — `specialEffect` pilote un comportement dédié (hors
déplacement, voir ⑥bis).

Valeurs disponibles : `DrainHP` (+ `drainHealRatio`, ex: 0.5 = 50% des dégâts infligés rendus en
soin) / `DrainMana`, `Summon` (+ `summonMobData`/`summonDuration`), `Interrupt`.

---

## Compatibilité arme & variantes

| Champ | Rôle |
|---|---|
| `compatibleWeapons` | Types d'armes avec lesquels CE skill précis est utilisable. Vide = universel (skills Buff/Debuff/utilitaires sans lien à l'arme). Depuis chantier B, **1 seule entrée recommandée** pour un skill lié à une animation d'arme (voir "Un clip = un skill" plus bas) — une anim/timing ne convient proprement qu'à UNE arme à la fois. |
| `weaponVariantLinks` | Liste de liens vers les skills équivalents pour d'AUTRES variantes de la même famille d'arme (ex: `skl_massue_frappe1` → `{ Hammer, skl_marteau_frappe1 }`). **Système de déblocage pas encore implémenté (pas prévu pour le prototype)** — ce champ existe pour être rempli au fur et à mesure de la création de contenu, afin d'éviter d'avoir à reprendre tous les skills plus tard quand le système sera construit. |

**Comment remplir `weaponVariantLinks`** : uniquement sur le skill de la variante de BASE (celle
débloquée en premier, ex: Massue — la "famille de départ", voir `WeaponType.GetStartingFamily()`
en code). Chaque entrée = `{ weaponType: <variante>, skill: <SkillData de la variante> }`. Le
skill de la variante (`skl_marteau_frappe1`) n'a PAS besoin de lien retour vers la base — inutile
pour le mécanisme prévu (résolution toujours depuis ce que le joueur possède déjà).

Concrètement, pour une paire Massue/Marteau :
- `skl_massue_frappe1.compatibleWeapons = [Mace]`, `weaponVariantLinks = [{Hammer, skl_marteau_frappe1}]`
- `skl_marteau_frappe1.compatibleWeapons = [Hammer]`, `weaponVariantLinks = []` (vide)

Un skill sans variante prévue (exclusif à une arme, ou universel Buff/Debuff) laisse
`weaponVariantLinks` vide — c'est le cas par défaut, aucune action requise.

---

## Partage de clip entre skills — seulement si le timing d'event est identique

**Les Animation Events vivent sur le CLIP, pas sur le `SkillData`.** Plusieurs skills peuvent
partager le même clip (`attackAnimation` pointant vers le même fichier) **à condition qu'ils
aient tous besoin du/des MÊME(S) event(s), à la même frame** — ex: un combo à tirs rapides où
chaque coup est visuellement identique, un seul event à la même position pour tous les steps.
Rien à dupliquer dans ce cas, c'est un partage voulu, pas un problème.

**Dupliquer devient nécessaire dès qu'UN des skills partageant le clip a besoin d'un timing (ou
d'un nombre) d'event différent des autres** — animations différentes par step d'un combo, ou
même clip mais frame d'impact différente, ou un MultiHit qui a besoin de plusieurs events là où
un Normal sur le même clip n'en veut qu'un. Poser un event sur un clip partagé l'applique à
TOUS les skills qui le référencent — pas de timing indépendant possible sans copie séparée.

Pour dupliquer :

1. Dans le Project, déplie le FBX source (ou repère le clip déjà extrait) et sélectionne le
   clip à dupliquer.
2. **Ctrl+D** (Duplicate) — Unity crée une copie standalone `.anim`, indépendante du FBX
   d'origine et éditable directement (plus en read-only, contrairement au clip FBX source).
3. Renomme la copie pour éviter toute confusion (ex: `taunt_attack.anim`).
4. Réassigne `attackAnimation` (ou `channelAnimation`) du `SkillData` concerné vers cette copie.
5. Répète une fois par skill qui a besoin d'un timing d'event indépendant.
6. Pose les events sur chaque copie séparément, en suivant les règles par type ci-dessous.

⚠ Contrepartie : une copie dupliquée ne se met plus à jour automatiquement si le clip source
(FBX) est réimporté/modifié plus tard — à garder en tête si l'anim d'origine change.

**Cas vécu :** `skl_test_combo` (parent) + `skl_test_combo_step2` + `skl_test_combo_step3`
partagent aujourd'hui le même clip source — acceptable SI les 3 coups sont censés être
visuellement identiques (voulu), à séparer en 3 copies uniquement si tu veux une anim/timing
différent par step.

---

## Normal (`executionType = Normal`, `castTime = 0`)

Le cas le plus simple — un skill à un seul coup, résolution calée sur une frame précise de
l'animation.

1. Assigne `attackAnimation`.
2. Pose **1 Animation Event** sur ce clip, à la frame d'impact visuel :
   - `Function` = `OnSkillHitFrame`
   - `Int` (hitIndex) = laisser à **0** (valeur par défaut, coup unique).
3. Les dégâts/effets résolvent exactement à cette frame. Si tu oublies l'event (ou clip sans
   anim assignée), le skill résout quand même à la fin du clip — **garde-fou anti-softlock**,
   jamais de blocage permanent, juste un timing dégradé (comme avant ce système).
4. Le joueur reste bloqué (déplacement + relance de n'importe quel skill) jusqu'à la fin
   **réelle** du clip — pas juste jusqu'à la frame d'impact. Un skill au follow-through long
   bloquera donc plus longtemps que sa seule frame de hit.
5. **Place l'event à la frame d'impact réelle.** Le `cooldown` réel ne démarre qu'à la
   résolution (à la frame de l'event), indépendamment du blocage d'anim (qui dure toute la
   durée du clip, lui, depuis le clic). Si le `cooldown` du skill est confortablement plus long
   que l'anim (le cas normal — voir règle ci-dessous), l'event peut être placé à l'impact visuel
   sans souci, CD et fin d'anim se chevauchent naturellement. **Exception : l'attaque de base
   (`skillType = BasicAttack`, slot 0)** — voir encadré dédié plus bas, event à la 1ère frame
   obligatoire.

> **Règle de design à respecter systématiquement : `cooldown` doit toujours être ≥ durée de
> `attackAnimation`.** Si le CD est plus court que l'anim (ou que l'event est posé tard dans un
> clip long), le décompte du CD réel et le blocage d'anim ne se chevauchent plus complètement —
> visuellement ça ressemble à "deux cooldowns à la suite" (l'un pour l'anim, l'un pour le CD),
> et dans le pire cas l'icône peut sembler "prête" alors que le joueur reste bloqué. Respecter
> cette règle évite le problème à la source, quel que soit le type de skill.
>
> **Attaque de base (BasicAttack, slot 0) — cas particulier :** son `cooldown` est souvent très
> court (proche de la cadence d'attaque de l'arme, ex: 1s), pas assez de marge pour absorber un
> event placé en milieu/fin de clip. **Pose systématiquement l'event à la toute première frame
> (frame 0)** pour ce type de skill précisément — vérifié en jeu, ça élimine le symptôme.

## MultiHit (`executionType = MultiHit`, `castTime = 0`)

Une seule activation, plusieurs coups enchaînés sur la même animation.

1. Assigne `attackAnimation`.
2. Remplis `hitSteps` — une entrée par coup supplémentaire (au-delà du coup de base), chacune
   avec ses propres `damageMultiplier`, ratios, `element`, `statusEffects`, et
   `vfxImpact`/`soundEffect` optionnels (vide = hérite du skill parent).
3. Pose **1 Animation Event par coup, coup de base INCLUS** :
   - hitIndex **0** = coup de base (utilise les stats du `SkillData` parent, pas un `HitStep`).
   - hitIndex **1..N** = `hitSteps[0]`, `hitSteps[1]`, ... dans l'ordre.
   - Un MultiHit à 3 `hitSteps` = **4 events au total** (0, 1, 2, 3), chacun avec son `Int`
     réglé explicitement (ne pas laisser à 0 partout par erreur — c'est l'erreur la plus
     probable, voir [Pièges fréquents](#pièges-fréquents--checklist-rapide)).
4. `hitSteps[i].delay` reste utilisé pour le chemin Mob/PNJ (ancien système, inchangé) mais est
   **ignoré côté joueur** — c'est la position réelle de l'event sur le clip qui pilote le
   timing, pas cette valeur.
5. Un hitIndex mal numéroté (event qui ne matche pas la séquence attendue) déclenche un
   `Debug.LogWarning` explicite en Console — plus un échec totalement silencieux.
6. **Sans aucun event posé**, tous les coups restants tombent d'un coup à la fin du clip une
   fois le timeout de sécurité atteint (au lieu d'être étalés dans le temps) — comportement
   transitoire attendu tant que les events ne sont pas posés, pas un bug à signaler.

## Combo (ComboSequence) (`executionType = ComboSequence`, `castTime = 0`)

N appuis successifs sur le même slot, chaque appui joue un skill différent.

1. `comboSteps` : liste de `SkillData` **séparés** — chaque step (et le skill "parent" lui-même,
   qui compte comme le premier coup) est un asset à part entière, avec sa propre icône, ses
   propres stats, et sa propre `attackAnimation`.
2. Le skill **parent** compte comme le tout premier coup du combo — assigne-lui aussi une
   `attackAnimation` + son Animation Event (comme un skill Normal, hitIndex 0).
3. Chaque **step** (`comboSteps[i]`) a lui aussi sa propre `attackAnimation` + son propre event
   (hitIndex 0 — un step de combo est un coup unique, jamais un MultiHit imbriqué).
4. `comboWindowDuration` : fenêtre de temps pour enchaîner le step suivant, **ouverte APRÈS la
   résolution du coup courant** (pas au clic) — si le joueur ne réagit pas à temps, le combo
   expire et repart de zéro au prochain appui.
5. `comboStepInterval` : délai minimum entre deux appuis (anti-spam).
6. Le `cooldown` des steps intermédiaires est **ignoré** — seul celui du skill **parent**
   compte, posé une seule fois à la toute fin du combo complet.
7. Le joueur reste bloqué (déplacement + tout nouveau lancement, y compris le step suivant du
   même combo) jusqu'à la fin réelle de CHAQUE step avant de pouvoir enchaîner.

## Canalisation (`castTime > 0`)

Skill à temps de cast — le joueur reste immobile (sauf mouvement volontaire = annulation) le
temps de `castTime` secondes avant résolution.

1. Assigne `channelAnimation` (**pas** `attackAnimation`).
2. `executionType` doit rester **`Normal`** — combiner `castTime > 0` avec MultiHit ou
   ComboSequence n'est pas géré (`OnValidate` avertit dans la Console si c'est le cas :
   ComboSequence ignore totalement le castTime, MultiHit canalise mais casse la convention de
   CD différé).
3. **Aucun Animation Event à poser** — le timing est entièrement piloté par le timer
   `castTime` (barre de progression `ProgressBarUI`), résolution automatique à la fin.
4. Interruptible par CC dur (Stun/Freeze/Shock/Knockback/Fear), Silence, mouvement volontaire,
   ou mort de la cible → coupe l'anim net via le trigger Animator `CancelAction` (setup
   Animator Controller, une fois pour tout le projet, voir section suivante).
5. Le mouvement pendant une canalisation reste **toujours autorisé** — il l'annule
   volontairement (CD à moitié). C'est la seule exception au blocage de déplacement qui
   s'applique à tous les autres types de skill.

---

## Setup Animator Controller (une fois pour le projet)

Pas par skill — à vérifier une seule fois sur l'Animator Controller du joueur :

- Le champ `attackPlaceholderClip` (component `PlayerAnimatorController`, sur le GameObject du
  joueur) doit être assigné à un clip placeholder (celui d'origine du state "Attack").
- Un state nommé **exactement** `"Attack"` doit exister dans le graphe — c'est lui qui est
  réutilisé (clip échangé à la volée) pour absolument tous les skills (Normal, MultiHit, Combo,
  Canalisation — même mécanisme, `AnimatorOverrideController`).
- Paramètre **Trigger** nommé exactement `CancelAction`.
- Une transition **Any State → [state idle armé, ex: "sword idle"]** avec :
  - Condition = `CancelAction`
  - **Has Exit Time décoché** (sinon la transition attend la fin du clip avant de s'appliquer,
    ce qui annule tout l'intérêt du cut immédiat).

Sans ce setup : les animations jouent quand même (le state "Attack" se relance normalement),
mais `CancelChannel()` ne pourra jamais interrompre visuellement une canalisation — seule la
Canalisation est concernée, elle est la seule à pouvoir être coupée en plein milieu.

---

## Poser un Animation Event sur un clip FBX/Mixamo

Les clips importés depuis un FBX (Mixamo ou autre) sont **en lecture seule** dans la fenêtre
Animation classique (Window → Animation) — impossible d'y ajouter un event directement.

Passer par les **Import Settings du FBX** :

1. Sélectionne le fichier **FBX** dans le Project (pas le clip individuel).
2. Onglet **Animation** dans l'Inspector (à côté de Model / Rig / Materials).
3. Scroll jusqu'à la liste **Clips**, sélectionne le clip concerné.
4. Scroll encore dans les réglages du clip → section **Events** (mini-timeline horizontale, en
   dessous de Curves/Mask/Motion).
5. Clic droit sur cette mini-timeline → **Add Event** (ou bouton `+`).
6. Sélectionne le marker créé → renseigne :
   - `Function` = `OnSkillHitFrame`
   - `Int` = le hitIndex voulu (0 par défaut, ou l'index exact pour un MultiHit)
7. Pour positionner précisément le marker sur la bonne frame : utilise la fenêtre **Preview**
   (en haut de l'Inspector) ou la fenêtre Animation classique pour repérer visuellement la
   frame d'impact, puis clique-glisse le marker sur la mini-timeline Events au même endroit.
8. **Apply** en bas de l'Inspector pour sauvegarder.

---

## Pièges fréquents / checklist rapide

- [ ] `attackAnimation` OU `channelAnimation` rempli selon `castTime`, jamais les deux, jamais
      aucun des deux sur un skill censé avoir une anim.
- [ ] Nom de fonction de l'event tapé **exactement** `OnSkillHitFrame` (sensible à la casse,
      une faute de frappe = event silencieusement ignoré par Unity, aucune erreur Console).
- [ ] Pour un MultiHit : un event par hit, coup de base INCLUS (hitIndex 0), `Int` réglé
      explicitement à chaque event — l'erreur la plus commune est d'oublier l'event du coup de
      base ou de laisser tous les `Int` à 0.
- [ ] Pour un Combo : chaque step (parent inclus) a sa propre anim + son propre event —
      oublier l'event sur UN step ne bloque rien (garde-fou timeout) mais désynchronise le
      timing visuel de ce step précis.
- [ ] Canalisation : ne PAS poser d'event, ça ne sert à rien pour ce type (timing = castTime).
- [ ] `executionType` cohérent avec `castTime` (Normal uniquement si castTime > 0).
- [ ] Clip FBX/Mixamo : events posés via Import Settings → onglet Animation, jamais via la
      fenêtre Animation classique (read-only sur ce type de clip).
- [ ] `damageMeleeRatio` + `damageRangedRatio` + `damageMagicRatio` = 1.0 (Damage/Other).
- [ ] `compatibleWeapons` : 1 seule entrée si le skill est lié à une anim/timing d'arme précise
      (pas une liste de plusieurs armes — voir "Un clip = un skill").
- [ ] `weaponVariantLinks` rempli sur le skill de la variante de BASE si une/des variante(s)
      sont déjà créées pour la même compétence conceptuelle (système de déblocage pas encore
      actif, mais le lien évite une reprise plus tard).
